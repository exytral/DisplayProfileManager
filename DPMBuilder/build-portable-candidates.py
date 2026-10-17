"""Create checked local portable candidates and verified source lineage. Never publish."""
from pathlib import Path
import argparse,hashlib,importlib.util,subprocess,zipfile,struct,json,shutil

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--repo",type=Path,required=True)
    parser.add_argument("--output",type=Path,required=True)
    args=parser.parse_args()
    root=args.repo.resolve();out=args.output.resolve()
    if out.is_relative_to(root):raise ValueError("Candidate output must be outside source checkout")
    def git(*parts):
        return subprocess.check_output(["git","-C",str(root),*parts],text=True).strip()
    if git("status","--porcelain"):raise ValueError("Source checkout not clean")
    if git("show","-s","--format=%G?")!="G":raise ValueError("Not owner Good-signed")
    info={"Commit":git("rev-parse","HEAD"),"Tree":git("rev-parse","HEAD^{tree}"),"Parent":git("rev-parse","HEAD^")}
    out.mkdir(parents=True,exist_ok=False)
    guard_path=root/"DPMBuilder/validate-package-content.py"
    spec=importlib.util.spec_from_file_location("package_guard",guard_path)
    guard=importlib.util.module_from_spec(spec);spec.loader.exec_module(guard)
    archive_rows=[]
    for arch,machine in (("x64",0x8664),("x86",0x14c),("ARM64",0xaa64)):
        src=root/"DisplayProfileManager/bin"/arch/"Release"
        for n in ("DisplayProfileManager.exe","DisplayProfileManager.dll","ShellExt.dll"):
            p=src/n
            b=p.read_bytes()
            offset=struct.unpack_from("<I",b,60)[0]
            if struct.unpack_from("<H",b,offset+4)[0]!=machine:raise ValueError("Unexpected PE machine: "+str(p))
        stage=out/("stage-"+arch)
        stage.mkdir()
        for p in src.iterdir():
            if p.is_file() and p.suffix.lower() not in (".pdb",".xml",".tmp",".log",".exp",".lib"):
                shutil.copy2(p,stage/p.name)
        if (src/"runtimes").exists():shutil.copytree(src/"runtimes",stage/"runtimes")
        for license in ("LICENSE","THIRD-PARTY-LICENSES.md"):
            shutil.copy2(root/license,stage/license)
        archive=out/f"DisplayProfileManager-2.3.0-{arch.lower()}-Portable.zip"
        with zipfile.ZipFile(archive,"w",zipfile.ZIP_DEFLATED) as z:
            for p in sorted(stage.rglob("*")):
                if p.is_file():z.write(p,p.relative_to(stage).as_posix())
        guard.check_portable(archive)
        archive_rows.append((archive.name,len(archive.read_bytes()),hashlib.sha256(archive.read_bytes()).hexdigest()))
        shutil.rmtree(stage)
    info_text="LOCAL REHEARSAL; NOT RELEASE OR PROMOTION\n"+"\n".join(f"{k}: {v}" for k,v in info.items())+"\n"
    (out/"CANDIDATE-INFO.txt").write_text(info_text,encoding="utf8")
    guard.verify_lineage(root,info_text)
    print(json.dumps({"lineage":info,"archives":archive_rows},indent=2))

if __name__=="__main__":main()
