import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';

const source = fs.readFileSync(new URL('../scripts/verify-etp-core-real-files.mjs', import.meta.url), 'utf8');

test('controlled evidence authority is checked before any archive read', () => {
  const authority = source.indexOf("purpose:'AGGREGATE_EVIDENCE'");
  const archiveRead = source.indexOf('fs.readFileSync(archivePath)');
  assert.ok(authority >= 0);
  assert.ok(archiveRead > authority);
  assert.match(source, /if\(!decision\.ok\)return\{ok:false,storeCode,productionReady:false,code:decision\.code\}/);
});

test('real-file evidence is explicitly non-production and version-bound', () => {
  assert.match(source, /contractVersion:core\.ETP_CORE_VERSION/);
  assert.match(source, /productionReady:false/);
  assert.match(source, /profileVersion:profile\.ETP_PROFILE_VERSION/);
  assert.match(source, /parserVersion:tableParser\.PARSER_VERSION/);
  assert.doesNotMatch(source, /contractVersion:core\.VERSION/);
});

test('controlled verifier has no native staging publication or receipt surface', () => {
  assert.doesNotMatch(source, /beginGeneration|appendChunk|finishGeneration|publishGeneration|createReceipt|localStorage|sessionStorage/);
});
