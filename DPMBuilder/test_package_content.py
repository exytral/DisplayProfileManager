import importlib.util,io,zipfile,unittest
from pathlib import Path
spec=importlib.util.spec_from_file_location("guard",Path(__file__).with_name("validate-package-content.py"))
guard=importlib.util.module_from_spec(spec);spec.loader.exec_module(guard)
def makezip(extra=b""):
    output=io.BytesIO()
    with zipfile.ZipFile(output,"w",zipfile.ZIP_DEFLATED) as z:
        for n in ("DisplayProfileManager.exe","DisplayProfileManager.dll","ShellExt.dll","LICENSE","THIRD-PARTY-LICENSES.md"):
            z.writestr(n,(b"clean"+extra) if n=="DisplayProfileManager.dll" else b"clean")
    return output.getvalue()
class GateTests(unittest.TestCase):
    def test_clean_bytes(self):guard.clean(b"plain")
    def test_ascii_user_home(self):
        with self.assertRaises(ValueError):guard.clean(b"x C:\\Users\\anyuser\\build\\source.pdb y")
    def test_utf16_user_home(self):
        with self.assertRaises(ValueError):guard.clean("C:\\Users\\anyuser\\build".encode("utf-16le"))
    def test_zip_crc_and_inner_bytes(self):
        with zipfile.ZipFile(io.BytesIO(makezip())) as z:self.assertIsNone(z.testzip())
        # Inspect through the decompression path with a fake PE validator.
        original=guard.inspect_bytes
        try:
            guard.inspect_bytes=lambda data,pdb_name=None:guard.clean(data)
            with self.assertRaises(ValueError):
                with zipfile.ZipFile(io.BytesIO(makezip(b" C:\\Users\\secret\\build.pdb"))) as z:
                    for name in z.namelist():guard.inspect_bytes(z.read(name))
        finally:guard.inspect_bytes=original
    def test_archive_guard_reads_clean_and_malicious_members(self):
        import tempfile
        with tempfile.TemporaryDirectory() as directory:
            archive=Path(directory)/"test.zip"
            original=guard.inspect_bytes
            try:
                guard.inspect_bytes=lambda data,pdb_name=None:guard.clean(data)
                archive.write_bytes(makezip())
                self.assertEqual(len(guard.check_portable(archive)),5)
                archive.write_bytes(makezip(b" C:\\Users\\someone\\private\\source.pdb"))
                with self.assertRaises(ValueError):
                    guard.check_portable(archive)
            finally:
                guard.inspect_bytes=original
    def test_lineage_negative(self):
        with self.assertRaises(Exception):guard.verify_lineage(Path("."),"Commit: stale\nTree: stale\nParent: stale")

class AdversarialRegressionTests(unittest.TestCase):
    @staticmethod
    def minimal_pe(plus=False, name=None):
        import importlib.util,struct
        origin=Path(__file__).resolve().parents[1]/"DisplayProfileManager.SetupBootstrap"/"test_pdb_path.py"
        spec=importlib.util.spec_from_file_location("fixture",origin)
        fixture_module=importlib.util.module_from_spec(spec);spec.loader.exec_module(fixture_module)
        return bytes(fixture_module.fixture(pe32plus=plus,name=name or guard.PE.EXPECTED))

    @staticmethod
    def portable(dll=b"not a PE",shell=b"not a PE",extras=()):
        output=io.BytesIO()
        with zipfile.ZipFile(output,"w",zipfile.ZIP_DEFLATED) as z:
            for n,b in (("DisplayProfileManager.exe",b"exe"),("DisplayProfileManager.dll",dll),
                        ("ShellExt.dll",shell),("LICENSE",b"legal"),("THIRD-PARTY-LICENSES.md",b"legal")):
                z.writestr(n,b)
            for n in extras:z.writestr(n,b"safe")
        return output.getvalue()

    def test_actual_pe_parser_negative_and_positive(self):
        import tempfile
        from unittest.mock import patch
        original=guard.PE.EXPECTED
        # Fixture contains an authentic PE32/PE32+ debug-directory bound RSDS.
        good_managed=self.minimal_pe(False,name="DisplayProfileManager.pdb")
        good_shell=self.minimal_pe(True,name="ShellExt.pdb")
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/"portable.zip"
            p.write_bytes(self.portable(good_managed,good_shell))
            self.assertEqual(len(guard.check_portable(p)),5)
            for bad in (self.portable(),self.portable(good_managed,b"not a PE")):
                p.write_bytes(bad)
                with self.assertRaises(ValueError):guard.check_portable(p)
        self.assertEqual(guard.PE.EXPECTED,original)

    def test_optimized_real_cli_rejects_invalid_pe(self):
        import subprocess,sys,tempfile
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/"bad.zip";p.write_bytes(self.portable())
            # This is a real separate Python interpreter, not patched inspect_bytes.
            for flag in ([],["-O"]):
                code=("import importlib.util,sys;"
                      "s=importlib.util.spec_from_file_location('guard',sys.argv[1]);"
                      "g=importlib.util.module_from_spec(s);s.loader.exec_module(g);"
                      "g.check_portable(sys.argv[2])")
                r=subprocess.run([sys.executable,*flag,"-B","-c",code,
                                  str(Path(__file__).with_name("validate-package-content.py")),str(p)],
                                 capture_output=True,text=True)
                self.assertNotEqual(r.returncode,0,(flag,r.stdout,r.stderr))

    def test_builder_duplicate_member_normal_and_optimized(self):
        import tempfile,subprocess,sys,warnings
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/"DPMShortcutBuilder-2.3.0.zip"
            with warnings.catch_warnings():
                warnings.simplefilter("ignore",UserWarning)
                with zipfile.ZipFile(p,"w",zipfile.ZIP_DEFLATED) as z:
                    for n in ("DPMShortcutBuilder.exe","DPMShortcutBuilder.pyw",
                              "LICENSE","THIRD-PARTY-LICENSES.md","LICENSE"):z.writestr(n,b"safe")
            with self.assertRaises(ValueError):guard.check_builder(p)
            code=("import importlib.util,sys;"
                  "s=importlib.util.spec_from_file_location('g',sys.argv[1]);"
                  "g=importlib.util.module_from_spec(s);s.loader.exec_module(g);"
                  "g.check_builder(sys.argv[2])")
            for flag in ([],["-O"]):
                r=subprocess.run([sys.executable,*flag,"-B","-c",code,
                                  str(Path(__file__).with_name("validate-package-content.py")),str(p)],
                                 capture_output=True,text=True)
                self.assertNotEqual(r.returncode,0,(flag,r.stderr))

    def test_platform_independent_unsafe_zip_names(self):
        for n in ("/absolute.txt","C:relative_secret.txt","C:/absolute.txt",
                  "\\\\server\\share\\secret.txt","../escape.txt","nested/../escape.txt",
                  "nested\\..\\escape.txt","nested\\evil.txt","folder/./file.txt",
                  "safe:name.txt","//server/share/file.txt"):
            with self.subTest(name=n),self.assertRaises(ValueError):
                guard.safe_zip_name(n)
        for n in ("runtimes/win-x64/native/foo.dll","DisplayProfileManager.exe",
                  "THIRD-PARTY-LICENSES.md"):
            guard.safe_zip_name(n)

    def test_utf16_forward_and_unix_home_variants(self):
        for name in ("C:/Users/Example/private.pdb","/Users/example/private",
                     "C:\\Users\\Example\\private.pdb","/home/example/private"):
            with self.subTest(name=name),self.assertRaises(ValueError):
                guard.clean(name.encode("utf-16le"))
            with self.subTest(ascii=name),self.assertRaises(ValueError):
                guard.clean(name.encode("utf-8"))
        guard.clean("runtimes/win-x64/native/readme".encode("utf-16le"))

if __name__=="__main__":unittest.main()
