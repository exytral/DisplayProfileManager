"""Fail-closed, source-only validation of DPM SetupBootstrap PE CodeView PDB locator.

Bounded to known native Setup binaries. Does not execute the PE.
"""
from pathlib import Path
import argparse
import struct

EXPECTED = "DisplayProfileManager.SetupBootstrap.pdb"


def _u16(data, offset):
    if offset < 0 or offset + 2 > len(data):
        raise ValueError("Truncated PE 16-bit field")
    return struct.unpack_from("<H", data, offset)[0]


def _u32(data, offset):
    if offset < 0 or offset + 4 > len(data):
        raise ValueError("Truncated PE 32-bit field")
    return struct.unpack_from("<I", data, offset)[0]


def validate(data: bytes):
    if len(data) < 0x40 or data[:2] != b"MZ":
        raise ValueError("Missing DOS MZ header")
    pe = _u32(data, 0x3c)
    if pe < 0x40 or pe + 24 > len(data) or data[pe:pe+4] != b"PE\x00\x00":
        raise ValueError("Invalid PE signature/offset")
    sections = _u16(data, pe+6)
    opt_size = _u16(data, pe+20)
    opt = pe+24
    if not (1 <= sections <= 96) or opt+opt_size > len(data):
        raise ValueError("Invalid PE section or optional-header bounds")
    magic = _u16(data, opt)
    if magic == 0x10b:
        directory_offset, count_offset, minimum = 96, 92, 112
    elif magic == 0x20b:
        directory_offset, count_offset, minimum = 112, 108, 128
    else:
        raise ValueError("Not PE32/PE32+")
    if opt_size < minimum or _u32(data, opt+count_offset) <= 6:
        raise ValueError("Missing PE Debug Data Directory")
    debug_dir = opt + directory_offset + 6*8
    if debug_dir+8 > opt+opt_size:
        raise ValueError("Truncated debug data-directory slot")
    debug_rva = _u32(data, debug_dir)
    debug_size = _u32(data, debug_dir+4)
    if not debug_rva or not debug_size or debug_size % 28:
        raise ValueError("Invalid debug directory RVA/size")
    section_table = opt+opt_size
    if section_table+sections*40 > len(data):
        raise ValueError("Truncated section table")

    def rva_to_file(rva, size):
        matches = []
        for idx in range(sections):
            s = section_table+idx*40
            virtual_size = _u32(data, s+8)
            virtual_address = _u32(data, s+12)
            raw_size = _u32(data, s+16)
            raw_offset = _u32(data, s+20)
            if virtual_address <= rva and rva-virtual_address+size <= min(raw_size, max(raw_size, virtual_size)):
                off = raw_offset+(rva-virtual_address)
                if off+size <= len(data) and rva-virtual_address+size <= raw_size:
                    matches.append(off)
        if len(matches) != 1:
            raise ValueError("Debug directory RVA not uniquely mapped to file")
        return matches[0]

    debug_off = rva_to_file(debug_rva, debug_size)
    codeview = []
    for i in range(debug_size//28):
        item = debug_off+i*28
        kind = _u32(data, item+12)
        size = _u32(data, item+16)
        address = _u32(data, item+20)
        pointer = _u32(data, item+24)
        if kind != 2:
            continue
        if size < 26 or pointer <= 0 or pointer+size > len(data):
            raise ValueError("Invalid CodeView data pointer/size")
        if rva_to_file(address, size) != pointer:
            raise ValueError("CodeView address/pointer mismatch")
        record = data[pointer:pointer+size]
        if record[:4] != b"RSDS" or record[4:20] == bytes(16) or _u32(record,20) == 0:
            raise ValueError("Invalid RSDS signature/GUID/age")
        terminator = record.find(b"\x00",24)
        if terminator <= 24 or terminator != len(record)-1:
            raise ValueError("Missing/invalid bounded PDB terminator")
        try:
            locator = record[24:terminator].decode("utf-8", errors="strict")
        except UnicodeDecodeError as exc:
            raise ValueError("Invalid PDB locator UTF-8") from exc
        if locator != EXPECTED:
            raise ValueError(f"Unexpected/absolute PDB locator: {locator!r}")
        codeview.append((pointer, locator))
    if len(codeview) != 1:
        raise ValueError(f"Expected one Type-2 RSDS record, got {len(codeview)}")
    raw_markers = []
    pos = 0
    while True:
        pos = data.find(b"RSDS", pos)
        if pos < 0:
            break
        raw_markers.append(pos)
        pos += 4
    if raw_markers != [codeview[0][0]]:
        raise ValueError("Extra/unbound RSDS marker")
    return codeview[0][1]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("executables", nargs="+", type=Path)
    args = parser.parse_args()
    for exe in args.executables:
        locator = validate(exe.read_bytes())
        print(f"RSDS_PASS {exe.name}: {locator}")


if __name__ == "__main__":
    main()
