"""Adversarial in-memory PE32 / PE32+ CodeView debug directory fixtures."""
import importlib.util
from pathlib import Path
import struct
import unittest

spec = importlib.util.spec_from_file_location("pdb_check", Path(__file__).with_name("validate-pdb-path.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def fixture(pe32plus=False, name=None, *, record=None):
    buf = bytearray(0x800)
    buf[:2] = b"MZ"
    struct.pack_into("<I", buf, 0x3c, 0x80)
    buf[0x80:0x84] = b"PE\x00\x00"
    struct.pack_into("<H",buf,0x84,0x8664 if pe32plus else 0x14c)
    struct.pack_into("<H",buf,0x86,1)
    size = 0xf0 if pe32plus else 0xe0
    struct.pack_into("<H",buf,0x94,size)
    opt=0x98
    struct.pack_into("<H",buf,opt,0x20b if pe32plus else 0x10b)
    directory=112 if pe32plus else 96
    struct.pack_into("<I",buf,opt+(108 if pe32plus else 92),16)
    debug = opt+directory+48
    struct.pack_into("<II",buf,debug,0x1020,28)
    section=opt+size
    buf[section:section+8]=b".rdata\x00\x00"
    struct.pack_into("<IIII",buf,section+8,0x600,0x1000,0x600,0x200)
    struct.pack_into("<I",buf,0x220+12,2)
    if record is None:
        locator = (module.EXPECTED if name is None else name).encode("utf-8")+b"\x00"
        record=b"RSDS"+bytes(range(1,17))+struct.pack("<I",1)+locator
    struct.pack_into("<III",buf,0x220+16,len(record),0x1100,0x300)
    buf[0x300:0x300+len(record)]=record
    return buf


class PdbLocatorTests(unittest.TestCase):
    def test_genuine_pe32_and_pe32plus_pass(self):
        for plus in (False,True):
            with self.subTest(pe32plus=plus):
                self.assertEqual(module.validate(bytes(fixture(plus))),module.EXPECTED)

    def test_absolute_path_and_wrong_names(self):
        for name in (r"C:\Users\Example\build\DisplayProfileManager.SetupBootstrap.pdb",
                     "/tmp/DisplayProfileManager.SetupBootstrap.pdb", "wrong.pdb",
                     "folder/DisplayProfileManager.SetupBootstrap.pdb"):
            with self.subTest(name=name),self.assertRaises(ValueError):
                module.validate(bytes(fixture(name=name)))

    def test_unbound_extra_rsds_even_malformed(self):
        for extra in (b"RSDS"+bytes(range(1,17))+struct.pack("<I",1)+b"bad.txt\x00",
                      b"RSDS"+bytes(range(1,17))+struct.pack("<I",1)+b"unterminated",
                      b"RSDS"+bytes(range(1,17))+struct.pack("<I",1)+module.EXPECTED.encode()+b"\x00"):
            with self.subTest(extra=extra[-20:]),self.assertRaises(ValueError):
                b=fixture()
                b[0x500:0x500+len(extra)]=extra
                module.validate(bytes(b))

    def test_malformed_rsds_record(self):
        good=b"RSDS"+bytes(range(1,17))+struct.pack("<I",1)
        for record in (good+b"abc",good+b"\xff.pdb\x00",
                       good+module.EXPECTED.encode(), b"RSDS"+bytes(16)+struct.pack("<I",1)+module.EXPECTED.encode()+b"\x00",
                       b"RSDS"+bytes(range(1,17))+bytes(4)+module.EXPECTED.encode()+b"\x00",
                       good+module.EXPECTED.encode()+b"\x00extra"):
            with self.subTest(record=record[-20:]),self.assertRaises(ValueError):
                module.validate(bytes(fixture(record=record)))

    def test_missing_truncated_or_fake_pe(self):
        for variant in ("standalone","dos","signature","optional","sections","no_debug","debug_rva",
                        "debug_size","pointer","size","address","type"):
            with self.subTest(variant=variant),self.assertRaises(ValueError):
                b=fixture()
                if variant=="standalone":b=b"RSDS"+bytes(20)+module.EXPECTED.encode()+b"\x00"
                if variant=="dos":b[0:2]=b"ZZ"
                if variant=="signature":b[0x80:0x84]=b"BAD!"
                if variant=="optional":struct.pack_into("<H",b,0x94,4)
                if variant=="sections":struct.pack_into("<H",b,0x86,0)
                if variant=="no_debug":struct.pack_into("<II",b,0x98+96+48,0,0)
                if variant=="debug_rva":struct.pack_into("<I",b,0x98+96+48,0xfffffff0)
                if variant=="debug_size":struct.pack_into("<I",b,0x98+96+52,31)
                if variant=="pointer":struct.pack_into("<I",b,0x220+24,len(b)+10)
                if variant=="size":struct.pack_into("<I",b,0x220+16,0x10000)
                if variant=="address":struct.pack_into("<I",b,0x220+20,0x1200)
                if variant=="type":struct.pack_into("<I",b,0x220+12,1)
                module.validate(bytes(b))

    def test_two_codeview_entries_are_rejected(self):
        b=fixture()
        struct.pack_into("<I",b,0x98+96+52,56)
        b[0x23c:0x258]=b[0x220:0x23c]
        with self.assertRaises(ValueError):
            module.validate(bytes(b))


if __name__ == "__main__":
    unittest.main()
