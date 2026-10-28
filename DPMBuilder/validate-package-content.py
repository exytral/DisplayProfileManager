"""Fail-closed local release package content provenance (does not execute payloads)."""
from pathlib import Path, PurePosixPath, PureWindowsPath
import argparse, importlib.util, hashlib, re, struct, zipfile

SPEC = importlib.util.spec_from_file_location("pe_pdb", Path(__file__).resolve().parents[1] / "DisplayProfileManager.SetupBootstrap" / "validate-pdb-path.py")
PE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PE)
# Recognized user-home roots, ASCII and UTF-16LE. Not a universal secret scanner.
HOME = re.compile(rb"(?:[A-Za-z]:[\\\\/]+Users[\\\\/]+|/(?:home|Users)/)[^\\x00\\r\\n]{1,160}", re.I)
HOME_TEXT = re.compile(r"(?:[A-Za-z]:[/\\]Users[/\\]|/(?:home|Users)/)[^\x00\r\n]{1,160}", re.I)

def safe_zip_name(name):
    # ZIP entries are POSIX paths. Reject Windows/UNC/ADS and mixed-separator traversal.
    if not name or chr(92) in name or ":" in name or "\\x00" in name:
        raise ValueError(f"Unsafe ZIP name: {name!r}")
    posix = PurePosixPath(name)
    win = PureWindowsPath(name)
    if posix.is_absolute() or win.is_absolute() or win.drive or win.root:
        raise ValueError(f"Rooted ZIP name: {name!r}")
    if any(part in ("", ".", "..") for part in name.split("/")):
        raise ValueError(f"Unsafe ZIP path components: {name!r}")
    if any(part == ".." for part in name.replace("\\\\", "/").split("/")):
        raise ValueError(f"Traversing ZIP entry: {name!r}")


def clean(data):
    if HOME.search(data) or any(HOME_TEXT.search(data[offset:].decode("utf-16le", errors="ignore")) for offset in (0, 1)):
        raise ValueError("Private user-root path in decompressed member or Setup bytes")

def inspect_bytes(data, pdb_name=None):
    clean(data)
    if pdb_name:
        original=PE.EXPECTED
        try:
            PE.EXPECTED=pdb_name
            observed = PE.validate(data)
            if observed != pdb_name:
                raise ValueError(f"Unexpected PE PDB locator: {observed!r}, expected {pdb_name!r}")
        finally:
            PE.EXPECTED=original

def portable_architecture(path):
    match = re.fullmatch(r'DisplayProfileManager-[0-9]+\.[0-9]+\.[0-9]+-(x64|x86|arm64)-Portable\.zip', Path(path).name)
    if match is None: raise ValueError('Invalid portable archive identity or architecture')
    return {'x64':0x8664,'x86':0x014c,'arm64':0xaa64}[match.group(1)]

def check_pe_machine(data, expected):
    # A matching COFF Machine alone does not constitute a structurally valid PE.
    if len(data) < 0x40 or data[:2] != b"MZ":
        raise ValueError("Missing PE DOS header")
    offset = struct.unpack_from("<I", data, 0x3c)[0]
    if offset < 0x40 or offset > len(data) - 24 or data[offset:offset+4] != b"PE\x00\x00":
        raise ValueError("Invalid or truncated PE signature/COFF header")
    machine, sections = struct.unpack_from("<HH", data, offset + 4)
    optional_size = struct.unpack_from("<H", data, offset + 20)[0]
    if machine != expected:
        raise ValueError("Wrong portable PE architecture")
    if sections == 0 or sections > 96:
        raise ValueError("Invalid PE section count")
    optional_start = offset + 24
    if optional_size < 96 or optional_start + optional_size > len(data):
        raise ValueError("Invalid/truncated PE optional header")
    magic = struct.unpack_from("<H", data, optional_start)[0]
    if magic not in (0x10b, 0x20b) or optional_size < (96 if magic == 0x10b else 112):
        raise ValueError("Invalid PE optional header magic/size")
    section_start = optional_start + optional_size
    if sections > (len(data) - section_start) // 40:
        raise ValueError("Truncated PE section table")
    for i in range(sections):
        sh = section_start + i * 40
        # Reject section payload pointers that overflow or point past the file.
        raw_size, raw_pointer = struct.unpack_from("<II", data, sh + 16)
        if raw_size and (raw_pointer < section_start + sections*40 or
                         raw_pointer > len(data) or raw_size > len(data)-raw_pointer):
            raise ValueError("Invalid PE section raw bounds")

def check_portable(path, *, expected_machine=None):
    if expected_machine is None: expected_machine=portable_architecture(path)
    with zipfile.ZipFile(path) as archive:
        if archive.testzip() is not None: raise ValueError("Bad ZIP CRC")
        names=archive.namelist()
        if len(names)!=len(set(names)): raise ValueError("Duplicate ZIP member")
        expected={"DisplayProfileManager.exe","DisplayProfileManager.dll","ShellExt.dll","LICENSE","THIRD-PARTY-LICENSES.md"}
        if not expected <= set(names): raise ValueError("Missing required portable members")
        for item in archive.infolist():
            if item.is_dir(): raise ValueError("Directory ZIP member")
            safe_zip_name(item.filename)
            data=archive.read(item)
            inspect_bytes(data, {"DisplayProfileManager.dll":"DisplayProfileManager.pdb","ShellExt.dll":"ShellExt.pdb"}.get(item.filename))
            if item.filename in ('DisplayProfileManager.exe','DisplayProfileManager.dll','ShellExt.dll'):
                check_pe_machine(data,expected_machine)
    return names

def check_builder(path):
    match=re.fullmatch(r"(DPMShortcutBuilder|DPMThemeBuilder)-([0-9]+)\.([0-9]+)\.([0-9]+)\.zip",path.name)
    if match is None: raise ValueError("Invalid Builder archive basename or numeric version")
    name=match.group(1)
    with zipfile.ZipFile(path) as archive:
        if archive.testzip() is not None: raise ValueError("ZIP CRC failure")
        expected={f"{name}.exe",f"{name}.pyw","LICENSE","THIRD-PARTY-LICENSES.md"}
        names=archive.namelist()
        if len(names) != 4 or len(set(names)) != 4 or set(names) != expected: raise ValueError("Unexpected or duplicated Builder ZIP structure")
        for name in names:safe_zip_name(name)
        for member in archive.namelist():
            data=archive.read(member)
            clean(data)
            if member in ('LICENSE','THIRD-PARTY-LICENSES.md'):
                source=Path(__file__).resolve().parent/member
                if data != source.read_bytes(): raise ValueError(f'Wrong Builder legal member bytes: {member}')

def verify_lineage(repo, info):
    import subprocess
    def oid(*a):
        return subprocess.check_output(["git","-C",str(repo),"rev-parse",*a],text=True).strip()
    values={"Commit":oid("HEAD"),"Tree":oid("HEAD^{tree}"),"Parent":oid("HEAD^")}
    actual=dict(line.split(": ",1) for line in info.splitlines() if ": " in line)
    if any(actual.get(k)!=v for k,v in values.items()):
        raise ValueError("Incorrect source Git lineage")
    return values

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--repo",type=Path,required=True)
    parser.add_argument("--portable",type=Path,nargs=3,required=True)
    parser.add_argument("--builder",type=Path,nargs="*")
    parser.add_argument("--setup",type=Path,nargs="*")
    parser.add_argument("--candidate-info",type=Path)
    args=parser.parse_args()
    for p in args.portable:check_portable(p);print("PORTABLE_PASS",p.name)
    for p in args.builder:check_builder(p);print("BUILDER_PASS",p.name)
    for p in args.setup:clean(p.read_bytes());print("SETUP_RAW_PASS",p.name)
    if args.candidate_info:verify_lineage(args.repo,args.candidate_info.read_text(encoding="utf-8"));print("LINEAGE_PASS")

if __name__=="__main__":main()
