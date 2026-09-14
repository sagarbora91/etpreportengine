from pathlib import Path
import subprocess, hashlib, json, zipfile, datetime

root=Path(__file__).resolve().parent
repo=root.parents[2]
candidate=root/'candidate-1.8.8-r10'
installer=root/'installer-1.8.8-r10/EtpReportingEngine-Setup-1.8.8-x64.exe'
archive=candidate/'source-snapshot.zip'
if archive.exists():raise RuntimeError('Source archive already exists; preserve it')
tracked=subprocess.check_output(['git','ls-files','-c','-o','--exclude-standard','-z'],cwd=repo).decode().split('\0')
roots={'src','tests-dotnet','tools','scripts','database','installer','docs','knowledge'}
files=sorted({p for p in tracked if p and (p.split('/')[0] in roots or '/' not in p) and (repo/p).is_file()})
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest().upper()
manifest=[dict(path=p,sha256=sha(repo/p),bytes=(repo/p).stat().st_size) for p in files]
with zipfile.ZipFile(archive,'x',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
    for entry in manifest:z.write(repo/entry['path'],entry['path'])
with zipfile.ZipFile(archive) as z:
    for entry in manifest:
        assert hashlib.sha256(z.read(entry['path'])).hexdigest().upper()==entry['sha256']
        assert sha(repo/entry['path'])==entry['sha256']
(candidate/'source-files.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
release=json.loads((candidate/'release.json').read_text(encoding='utf-8-sig'))
assert release['version']=='1.8.8'
receipt=dict(candidate='1.8.8-r10',createdUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),head=release['commit'],branch=subprocess.check_output(['git','branch','--show-current'],cwd=repo,text=True).strip(),sourceTreeClean=release['sourceTreeClean'],sourceFileCount=len(files),note='HEAD is the starting d24a525 commit; source ZIP and per-file hashes identify the working-tree density and role-navigation changes. Historical graph outputs and ignored evidence are excluded from the source archive.',tests=dict(total=669,Desktop=345,SqlServer=195,Reporting=66,Import=51,Domain=12),artifacts={})
for kind,p in [('executable',candidate/'Etp.Reporting.Desktop.exe'),('installer',installer),('source',archive),('sourceManifest',candidate/'source-files.json')]:
    receipt['artifacts'][kind]=dict(path=p.relative_to(repo).as_posix(),bytes=p.stat().st_size,sha256=sha(p))
(root/'candidate-r10-receipt.json').write_text(json.dumps(receipt,indent=2),encoding='utf-8')
print(json.dumps(receipt,indent=2))



