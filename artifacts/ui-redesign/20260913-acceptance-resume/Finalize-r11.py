from pathlib import Path
import hashlib, json, zipfile, shutil, datetime

root = Path(__file__).resolve().parent
repo = root.parents[2]
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest().upper()
receipt = json.loads((root/'candidate-r11-receipt.json').read_text(encoding='utf-8-sig'))
for artifact in receipt['artifacts'].values():
    assert sha(repo/artifact['path']) == artifact['sha256']
manifest = json.loads((root/'candidate-1.8.8-r11/source-files.json').read_text(encoding='utf-8-sig'))
runtime = [entry for entry in manifest if entry['path'].split('/')[0] in {'src','tests-dotnet','scripts','installer','database','tools'} or '/' not in entry['path']]
for entry in runtime:
    assert sha(repo/entry['path']) == entry['sha256'], entry['path']
installed = json.loads((root/'vm-r11-install-result.json').read_text(encoding='utf-8-sig'))
assert installed['installedSha256'] == receipt['artifacts']['executable']['sha256']
assert installed['countsUnchanged'] and installed['settingsUnchanged'] and installed['integrityPassed']
assert installed['installerExitCode'] == 0
previous = repo/'artifacts/ui-redesign/20260912-sprint'
assert sha(previous/'installer-1.8.8-r9/EtpReportingEngine-Setup-1.8.8-x64.exe') == 'A16EB5E130455395ADCC5FB3A297F634B231440AD21F6F0E915795C553B6E7D1'
assert sha(previous/'candidate-1.8.8-r9/source-snapshot.zip') == 'B0554D8967D8772A8317E67F6DA499953FF3E173C083A8EE01EFA1B611B419BE'
assert sha(previous/'evidence-r9.zip') == 'E11187928349C03477836E64AA696683C98F0A2F51D4DCC12EBD465BCDDD8625'
result = dict(timestampUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),candidate='1.8.8-r11',runtimeSourceFilesMatched=len(runtime),artifactHashesMatched=True,installedHashMatched=True,originalCountsAndSettingsUnchanged=True,r9ArchivesUnchanged=True,uiInteraction='UNVERIFIED',note='Final documentation evolved after packaging; runtime source matches the frozen manifest.')
(root/'final-consistency-r11.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
records = root/'records-final-r11'
records.mkdir(exist_ok=False)
for relative in ['docs/audit/ETP-UI-REDESIGN-R10-2026-09-13.md','docs/audit/ETP-UI-REDESIGN-ACCEPTANCE.md','docs/audit/ETP-SESSION-HANDOFF-2026-09-13-UI-REDESIGN.md','docs/design/ETP-UI-REDESIGN-SPRINT-LEDGER.md','docs/design/ETP-UI-REDESIGN-DECISIONS.md','docs/design/ETP-UI-REDESIGN-ROUTE-COVERAGE.csv']:
    shutil.copy2(repo/relative, records/Path(relative).name)
for prefix in ['job-02','job-03','job-04','job-05']:
    for path in previous.glob(prefix+'*'):
        shutil.copy2(path,records/path.name)
archive=root/'evidence-r11.zip'
selected=[p for p in root.iterdir() if p.is_file() and p.suffix.lower() in {'.json','.txt','.log','.csv','.ps1','.pending','.py'}]
for directory in [records,root/'vm-r10-install-evidence',root/'vm-r11-install-evidence']:
    if directory.exists():
        selected.extend(p for p in directory.rglob('*') if p.is_file() and p.suffix.lower() in {'.json','.txt','.log','.ps1','.done','.error','.md','.csv'})
with zipfile.ZipFile(archive,'x',zipfile.ZIP_DEFLATED) as bundle:
    for path in sorted(set(selected)):
        bundle.write(path,path.relative_to(root).as_posix())
bundle_receipt=dict(sha256=sha(archive),bytes=archive.stat().st_size,files=len(set(selected)),exclusions='Original settings, backups and installer/executable payloads; no UI screenshots exist for this run.')
(root/'evidence-r11-receipt.json').write_text(json.dumps(bundle_receipt,indent=2),encoding='utf-8')
print(json.dumps(result))
print(json.dumps(bundle_receipt))
