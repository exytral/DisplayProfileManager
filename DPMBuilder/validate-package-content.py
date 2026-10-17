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

def check_portable(path):
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
    return names

def check_builder(path):
    name=path.name.split("-2.3.0.zip")[0]
    with zipfile.ZipFile(path) as archive:
        if archive.testzip() is not None: raise ValueError("ZIP CRC failure")
        expected={f"{name}.exe",f"{name}.pyw","LICENSE","THIRD-PARTY-LICENSES.md"}
        names=archive.namelist()
        if len(names) != 4 or len(set(names)) != 4 or set(names) != expected: raise ValueError("Unexpected or duplicated Builder ZIP structure")
        for name in names:safe_zip_name(name)
        for member in archive.namelist():clean(archive.read(member))

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
