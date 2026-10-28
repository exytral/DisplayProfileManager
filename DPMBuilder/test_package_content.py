import importlib.util,io,re,zipfile,unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
assembly_info=(ROOT/"DisplayProfileManager"/"Properties"/"AssemblyInfo.cs").read_text(encoding="utf-8")
version_match=re.search(r'\[assembly:\s*AssemblyInformationalVersion\("([^"]+)"\)\]',assembly_info)
if version_match is None: raise RuntimeError("AssemblyInformationalVersion not found")
CURRENT_VERSION=version_match.group(1)
SYNTHETIC_VERSION="7.8.9"
SYNTHETIC_VERSION_ALT="12.34.56"

spec=importlib.util.spec_from_file_location("guard",Path(__file__).with_name("validate-package-content.py"))
guard=importlib.util.module_from_spec(spec);spec.loader.exec_module(guard)
def sample_pe(machine=0x14c, name='DisplayProfileManager.pdb'):
    import struct
    origin=Path(__file__).resolve().parents[1]/'DisplayProfileManager.SetupBootstrap'/'test_pdb_path.py'
    spec=importlib.util.spec_from_file_location('pe_fixture',origin)
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    data=bytearray(module.fixture(name=name))
    struct.pack_into('<H',data,0x84,machine)
    return bytes(data)

def makezip(extra=b''):
    output=io.BytesIO()
    with zipfile.ZipFile(output,"w",zipfile.ZIP_DEFLATED) as z:
        for n in ("DisplayProfileManager.exe","DisplayProfileManager.dll","ShellExt.dll","LICENSE","THIRD-PARTY-LICENSES.md"):
            z.writestr(n,(sample_pe(0x8664,"ShellExt.pdb" if n=="ShellExt.dll" else "DisplayProfileManager.pdb") + (extra if n=="DisplayProfileManager.dll" else b"")) if n in ("DisplayProfileManager.exe","DisplayProfileManager.dll","ShellExt.dll") else b"clean")
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
            archive=Path(directory)/f"DisplayProfileManager-{CURRENT_VERSION}-x64-Portable.zip"
            original=guard.inspect_bytes
            try:
                guard.inspect_bytes=lambda data,pdb_name=None:guard.clean(data)
                archive.write_bytes(makezip())
                self.assertEqual(len(guard.check_portable(archive,expected_machine=0x8664)),5)
                archive.write_bytes(makezip(b" C:\\Users\\someone\\private\\source.pdb"))
                with self.assertRaises(ValueError):
                    guard.check_portable(archive,expected_machine=0x8664)
            finally:
                guard.inspect_bytes=original
    def test_lineage_negative(self):
        with self.assertRaises(Exception):guard.verify_lineage(Path("."),"Commit: stale\nTree: stale\nParent: stale")

class BuilderArchiveNamingTests(unittest.TestCase):
    def _archive(self, folder, stem, version, members=None):
        p=Path(folder)/(stem+'-'+version+'.zip')
        entries=members if members is not None else [stem+'.exe',stem+'.pyw','LICENSE','THIRD-PARTY-LICENSES.md']
        with zipfile.ZipFile(p,'w',zipfile.ZIP_DEFLATED) as z:
            for n in entries:z.writestr(n,(Path(__file__).with_name(n).read_bytes() if n in ('LICENSE','THIRD-PARTY-LICENSES.md') else b'clean'))
        return p

    def test_exact_builder_archives_across_versions(self):
        import tempfile
        with tempfile.TemporaryDirectory() as d:
            for stem in ('DPMShortcutBuilder','DPMThemeBuilder'):
                for v in (CURRENT_VERSION,SYNTHETIC_VERSION,SYNTHETIC_VERSION_ALT):
                    guard.check_builder(self._archive(d,stem,v))


    def test_builder_wrong_legal_bytes_rejected(self):
        import tempfile
        with tempfile.TemporaryDirectory() as d:
            for stem in ('DPMShortcutBuilder','DPMThemeBuilder'):
                good=self._archive(d,stem,SYNTHETIC_VERSION)
                guard.check_builder(good)
                for altered in ('LICENSE','THIRD-PARTY-LICENSES.md'):
                    entries={n: (Path(__file__).with_name(n).read_bytes() if n in ('LICENSE','THIRD-PARTY-LICENSES.md') else b'clean') for n in (stem+'.exe',stem+'.pyw','LICENSE','THIRD-PARTY-LICENSES.md')}
                    entries[altered]=b'clean-but-wrong'
                    with zipfile.ZipFile(good,'w',zipfile.ZIP_DEFLATED) as z:
                        for name,data in entries.items():z.writestr(name,data)
                    with self.assertRaisesRegex(ValueError,'Wrong Builder legal'):guard.check_builder(good)
                    import subprocess,sys
                    code=('import importlib.util,sys;s=importlib.util.spec_from_file_location("v",sys.argv[1]);'
                          'v=importlib.util.module_from_spec(s);s.loader.exec_module(v);v.check_builder(__import__("pathlib").Path(sys.argv[2]))')
                    for flag in ([],['-O']):
                        actual=subprocess.run([sys.executable,*flag,'-B','-c',code,
                            str(Path(__file__).with_name('validate-package-content.py')),str(good)],
                            capture_output=True,text=True)
                        self.assertNotEqual(actual.returncode,0)
                        self.assertIn('Wrong Builder legal',actual.stderr)

    def test_bad_archive_basename_and_members_fail_closed(self):
        import tempfile
        with tempfile.TemporaryDirectory() as d:
            for stem,v in (('UnknownBuilder',SYNTHETIC_VERSION),('DPMThemeBuilder','7.8'),('DPMThemeBuilder',SYNTHETIC_VERSION+'-rc1'),('DPMThemeBuilder','v'+SYNTHETIC_VERSION),('DPMThemeBuilder','7.8.x'),('DPMThemeBuilder',SYNTHETIC_VERSION+'.zip')):
                with self.assertRaises(ValueError):guard.check_builder(self._archive(d,stem,v))
            stem='DPMShortcutBuilder'
            good=[stem+'.exe',stem+'.pyw','LICENSE','THIRD-PARTY-LICENSES.md']
            for bad in (good[:-1],good+['README'],good[:-1]+['../unsafe'],good[:-1]+[stem+'.exe']):
                with self.assertRaises(ValueError):guard.check_builder(self._archive(d,stem,SYNTHETIC_VERSION,bad))

    def test_bad_builder_archive_optimized_interpreter(self):
        import tempfile,subprocess,sys
        with tempfile.TemporaryDirectory() as d:
            p=self._archive(d,'UnknownBuilder',SYNTHETIC_VERSION)
            code=('import importlib.util,sys; s=importlib.util.spec_from_file_location("g",sys.argv[1]);'
                  'g=importlib.util.module_from_spec(s);s.loader.exec_module(g);g.check_builder(sys.argv[2])')
            for flag in ([],['-O']):
                result=subprocess.run([sys.executable,*flag,'-B','-c',code,str(Path(__file__).with_name('validate-package-content.py')),str(p)],capture_output=True,text=True)
                self.assertNotEqual(result.returncode,0)

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
            for n,b in (("DisplayProfileManager.exe",sample_pe()),("DisplayProfileManager.dll",dll),
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
        good_shell=self.minimal_pe(False,name="ShellExt.pdb")
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/f"DisplayProfileManager-{SYNTHETIC_VERSION}-x64-Portable.zip"
            p.write_bytes(self.portable(good_managed,good_shell))
            self.assertEqual(len(guard.check_portable(p,expected_machine=0x14c)),5)
            for bad in (self.portable(),self.portable(good_managed,b"not a PE")):
                p.write_bytes(bad)
                with self.assertRaises(ValueError):guard.check_portable(p,expected_machine=0x14c)
        self.assertEqual(guard.PE.EXPECTED,original)


    def test_final_portable_machine_mismatch_all_pe_members(self):
        import tempfile,struct,subprocess,sys
        with tempfile.TemporaryDirectory() as d:
            for arch,machine in (('x64',0x8664),('x86',0x14c),('arm64',0xaa64)):
                p=Path(d)/f'DisplayProfileManager-{SYNTHETIC_VERSION}-{arch}-Portable.zip'
                for bad_member in ('DisplayProfileManager.exe','DisplayProfileManager.dll','ShellExt.dll'):
                    members={n:sample_pe(machine,'ShellExt.pdb' if n=='ShellExt.dll' else 'DisplayProfileManager.pdb')
                        for n in ('DisplayProfileManager.exe','DisplayProfileManager.dll','ShellExt.dll')}
                    members['LICENSE']=b'legal';members['THIRD-PARTY-LICENSES.md']=b'legal'
                    with zipfile.ZipFile(p,'w',zipfile.ZIP_DEFLATED) as z:
                        for n,b in members.items():z.writestr(n,b)
                    self.assertEqual(5,len(guard.check_portable(p)))
                    mutant=bytearray(members[bad_member])
                    struct.pack_into('<H',mutant,0x84,0x14c if machine!=0x14c else 0x8664)
                    members[bad_member]=bytes(mutant)
                    with zipfile.ZipFile(p,'w',zipfile.ZIP_DEFLATED) as z:
                        for n,b in members.items():z.writestr(n,b)
                    for flag in ([],['-O']):
                        code=('import importlib.util,sys;spec=importlib.util.spec_from_file_location("v",sys.argv[1]);v=importlib.util.module_from_spec(spec);spec.loader.exec_module(v);v.check_portable(sys.argv[2])')
                        result=subprocess.run([sys.executable,*flag,'-B','-c',code,str(Path(__file__).with_name('validate-package-content.py')),str(p)],capture_output=True,text=True)
                        self.assertNotEqual(0,result.returncode,(arch,bad_member,flag))
                        self.assertIn('Wrong portable PE architecture',result.stderr)

    def test_t1_structurally_malformed_final_pe_members(self):
        import struct,tempfile,subprocess,sys
        with tempfile.TemporaryDirectory() as d:
            for arch,machine in (('x64',0x8664),('x86',0x14c),('arm64',0xaa64)):
                output=Path(d)/f'DisplayProfileManager-{SYNTHETIC_VERSION}-{arch}-Portable.zip'
                good={n:sample_pe(machine,'ShellExt.pdb' if n=='ShellExt.dll' else 'DisplayProfileManager.pdb')
                    for n in ('DisplayProfileManager.exe','DisplayProfileManager.dll','ShellExt.dll')}
                good.update({'LICENSE':b'legal','THIRD-PARTY-LICENSES.md':b'legal'})
                with zipfile.ZipFile(output,'w',zipfile.ZIP_DEFLATED) as z:
                    for n,data in good.items():z.writestr(n,data)
                self.assertEqual(5,len(guard.check_portable(output)))
                for name in ('DisplayProfileManager.exe','DisplayProfileManager.dll','ShellExt.dll'):
                    for defect in ('70-byte','partial-coff','e_lfanew','zero-sections','optional-small',
                                   'bad-magic','section-table'):
                        data=bytearray(good[name])
                        if defect=='70-byte':
                            data=bytearray(70);data[:2]=b'MZ'
                            struct.pack_into('<I',data,0x3c,0x40)
                            data[0x40:0x44]=b'PE\x00\x00'
                            struct.pack_into('<H',data,0x44,machine)
                        elif defect=='partial-coff':
                            data=data[:0x80+8]
                        elif defect=='e_lfanew':
                            struct.pack_into('<I',data,0x3c,0x20)
                        elif defect=='zero-sections':
                            struct.pack_into('<H',data,0x86,0)
                        elif defect=='optional-small':
                            struct.pack_into('<H',data,0x94,2)
                        elif defect=='bad-magic':
                            struct.pack_into('<H',data,0x98,0x999)
                        elif defect=='section-table':
                            struct.pack_into('<H',data,0x86,96)
                        entries=dict(good);entries[name]=bytes(data)
                        with zipfile.ZipFile(output,'w',zipfile.ZIP_DEFLATED) as z:
                            for n,blob in entries.items():z.writestr(n,blob)
                        with self.subTest(arch=arch,member=name,defect=defect),self.assertRaises(ValueError):
                            guard.check_portable(output)
                # Reconstitute a genuine seven-input CLI batch with one malicious portable.
                bad=bytearray(good['DisplayProfileManager.exe'])
                bad=bytearray(70);bad[:2]=b'MZ';struct.pack_into('<I',bad,0x3c,0x40)
                bad[0x40:0x44]=b'PE\x00\x00';struct.pack_into('<H',bad,0x44,machine)
                entries=dict(good);entries['DisplayProfileManager.exe']=bytes(bad)
                with zipfile.ZipFile(output,'w',zipfile.ZIP_DEFLATED) as z:
                    for n,blob in entries.items():z.writestr(n,blob)
                # Real production CLI rejects the malformed final member in normal and -O mode.
                valid=[Path(d)/f'DisplayProfileManager-{SYNTHETIC_VERSION}-{a}-Portable.zip' for a in ('x64','x86','arm64')]
                for a,q in zip(('x64','x86','arm64'),valid):
                    if q==output:continue
                    mm={'x64':0x8664,'x86':0x14c,'arm64':0xaa64}[a]
                    with zipfile.ZipFile(q,'w',zipfile.ZIP_DEFLATED) as z:
                        for n in ('DisplayProfileManager.exe','DisplayProfileManager.dll','ShellExt.dll'):
                            z.writestr(n,sample_pe(mm,'ShellExt.pdb' if n=='ShellExt.dll' else 'DisplayProfileManager.pdb'))
                        z.writestr('LICENSE',b'legal');z.writestr('THIRD-PARTY-LICENSES.md',b'legal')
                for flag in ([],['-O']):
                    cmd=[sys.executable,*flag,'-B',str(Path(__file__).with_name('validate-package-content.py')),
                         '--repo',str(Path(__file__).resolve().parents[1]),'--portable',*(str(q) for q in valid)]
                    result=subprocess.run(cmd,capture_output=True,text=True)
                    self.assertNotEqual(0,result.returncode,(arch,flag,result.stdout,result.stderr))

    def test_optimized_real_cli_rejects_invalid_pe(self):
        import subprocess,sys,tempfile
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/f"DisplayProfileManager-{SYNTHETIC_VERSION}-x64-Portable.zip";p.write_bytes(self.portable())
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
            p=Path(d)/f"DPMShortcutBuilder-{SYNTHETIC_VERSION}.zip"
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
