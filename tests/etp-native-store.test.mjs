import assert from 'node:assert/strict';
import test from 'node:test';
import { createRequire } from 'node:module';
import { readFileSync } from 'node:fs';
const require = createRequire(import.meta.url);
const bridge = require('../www/etp-native-store.js');
const policy = require('../www/etp-store-lifecycle-policy.js');
const profileAuthority = require('../www/etp-profile-authority.js');

const scope = { storeCode: 'WLMHW', financialYear: '2024-25', periodStart: '2024-09-16', periodEnd: '2025-03-31' };
const fields = ['transaction_id', 'amount', 'report_date'];
const authorityBinding=profileAuthority.authorize({storeCode:'WLMHW',purpose:'PRODUCTION',profileVersion:profileAuthority.PROFILE_VERSION,parserVersion:profileAuthority.PARSER_VERSION}).binding;
const tenderIdentity={contractVersion:'ETP_TENDER_DICTIONARY_V1',versionId:'retail-etp-tender-v1',effectiveAt:'2026-08-24T00:00:00.000Z'};
function advance(value, events) { return events.reduce((current, event) => policy.transition(current, event).lifecycle, value); }
function manifest(generationId = 'gen:001') { return { scopeKey: 'WLMHW|2024-25|2024-09-16..2025-03-31', generationId, authority:authorityBinding, tenderDictionary:tenderIdentity, reports: policy.REPORT_IDS.map((reportId, index) => ({ reportId, sourceSha256: String(index + 1).repeat(64), headerSignatureSha256: String(index + 5).repeat(64), rowCount: 1 })) }; }
function fake(overrides = {}) { const calls = []; return { calls, plugin: { beginStage: async p => (calls.push(['begin', p]), { ok: true }), appendStageChunk: async p => (calls.push(['chunk', p]), { ok: true }), finishStage: async p => (calls.push(['finish', p]), { ok: true }), publishStage: async p => (calls.push(['publish', p]), { ok: true }), readStatus: async p => (calls.push(['status', p]), { ok: true, state: 'ACCEPTED', activeGenerationId: 'gen:001', restoreFence: false }), readFacts: async p => (calls.push(['read', p]), { ok: true, scopeKey: p.scopeKey, generationId: p.generationId, reportId: p.reportId, rows: [{ transaction_id: '0001', amount: 42 }], hasMore: true, nextChunkIndex: 0, nextRowOffset: 1 }), fenceAfterRestore: async p => (calls.push(['fence', p]), { ok: true, state: 'REIMPORT_REQUIRED' }), resetScope: async p => (calls.push(['reset', p]), { ok: true, state: 'EMPTY' }), resetStore: async p => (calls.push(['reset-store', p]), { ok: true, state: 'EMPTY' }), ...overrides } }; }
function adapter(state = fake()) { const made = bridge.create({ lifecyclePolicy: policy, plugin: state.plugin, allowedFactFields: fields }); assert.equal(made.ok, true); return { adapter: made.adapter, state }; }

test('creation fails closed without native plugin, policy or approved dictionary', () => {
  assert.equal(bridge.create({ lifecyclePolicy: policy, allowedFactFields: fields }).code, 'ETP_NATIVE_UNAVAILABLE');
  assert.equal(bridge.create({ plugin: {}, allowedFactFields: fields }).code, 'ETP_POLICY_UNAVAILABLE');
  assert.equal(bridge.create({ lifecyclePolicy: policy, plugin: {}, allowedFactFields: ['customer_name'] }).code, 'ETP_FACT_DICTIONARY_INVALID');
  assert.equal(bridge.create({ lifecyclePolicy: policy, plugin: {}, allowedFactFields: ['amount', 'amount'] }).code, 'ETP_FACT_DICTIONARY_INVALID');
});

test('begin stage sends only bounded scope and generation metadata in STAGING', async () => {
  const { adapter: value, state } = adapter();
  let lifecycle = advance(policy.create(scope, 'gen:001').lifecycle, ['PREFLIGHT_PASS', 'PARSE_PASS', 'POLICY_PASS']);
  assert.equal((await value.beginStage(lifecycle)).code, 'ETP_LIFECYCLE_STATE_FORBIDDEN');
  lifecycle = policy.transition(lifecycle, 'BEGIN_STAGING').lifecycle;
  assert.equal((await value.beginStage(lifecycle)).ok, true);
  assert.deepEqual(state.calls[0][1], { contractVersion: 1, scopeKey: lifecycle.scopeKey, generationId: 'gen:001' });
});

test('canonical chunks are bounded and contain only dictionary-approved scalar facts', async () => {
  const { adapter: value, state } = adapter();
  const lifecycle = advance(policy.create(scope, 'gen:001').lifecycle, ['PREFLIGHT_PASS', 'PARSE_PASS', 'POLICY_PASS', 'BEGIN_STAGING']);
  const chunk = { scopeKey: lifecycle.scopeKey, generationId: 'gen:001', reportId: 'R022', chunkIndex: 0, rows: [{ transaction_id: '0001', amount: 42, report_date: '2024-09-16' }] };
  assert.equal((await value.appendChunk(lifecycle, chunk)).ok, true);
  assert.equal(state.calls[0][0], 'chunk'); assert.equal(state.calls[0][1].rows[0].transaction_id, '0001');
  assert.equal((await value.appendChunk(lifecycle, { ...chunk, rows: Array(bridge.MAX_CHUNK_ROWS + 1).fill(chunk.rows[0]) })).code, 'ETP_CHUNK_ROWS_INVALID');
  assert.equal((await value.appendChunk(lifecycle, { ...chunk, unexpected: true })).code, 'ETP_CHUNK_SHAPE_INVALID');
});

test('raw workbook, file labels, PII-shaped fields and unapproved facts never cross bridge', async () => {
  const { adapter: value, state } = adapter();
  const lifecycle = advance(policy.create(scope, 'gen:001').lifecycle, ['PREFLIGHT_PASS', 'PARSE_PASS', 'POLICY_PASS', 'BEGIN_STAGING']);
  const base = { scopeKey: lifecycle.scopeKey, generationId: 'gen:001', reportId: 'R022', chunkIndex: 0 };
  for (const rows of [[{ transaction_id: '1', customer_name: 'secret' }], [{ transaction_id: '1', file_label: 'R022.xlsx' }], [{ transaction_id: '1', source_bytes: 'UEsDB' }], [{ transaction_id: '1', other: 'x' }]]) {
    assert.equal((await value.appendChunk(lifecycle, { ...base, rows })).ok, false);
  }
  assert.equal(state.calls.length, 0);
});

test('finish and publish require exact lifecycle states and validated four-report manifest', async () => {
  const { adapter: value, state } = adapter();
  let lifecycle = advance(policy.create(scope, 'gen:001').lifecycle, ['PREFLIGHT_PASS', 'PARSE_PASS', 'POLICY_PASS', 'BEGIN_STAGING']);
  assert.equal((await value.finishStage(lifecycle)).code, 'ETP_MANIFEST_INVALID');
  assert.equal((await value.finishStage(lifecycle, manifest())).ok, true);
  lifecycle = policy.transition(lifecycle, 'STAGE_COMPLETE').lifecycle;
  lifecycle = policy.attachManifest(lifecycle, manifest()).lifecycle;
  assert.equal((await value.publish(lifecycle)).code, 'ETP_LIFECYCLE_STATE_FORBIDDEN');
  lifecycle = advance(lifecycle, ['RECONCILE_PASS', 'REQUEST_CONFIRMATION']);
  assert.equal((await value.publish(lifecycle)).ok, true);
  assert.deepEqual(state.calls.map(x => x[0]), ['finish', 'publish']);
});

test('status validates scope and refuses malformed or sensitive native responses', async () => {
  let setup = fake(); let value = adapter(setup).adapter;
  const result = await value.readStatus(scope); assert.equal(result.ok, true); assert.equal(result.status.state, 'ACCEPTED');
  setup = fake({ readStatus: async () => ({ ok: true, state: 'ACCEPTED', activeGenerationId: null, restoreFence: false }) });
  assert.equal((await adapter(setup).adapter.readStatus(scope)).code, 'ETP_NATIVE_RESPONSE_INVALID');
  setup = fake({ readStatus: async () => ({ ok: true, state: 'EMPTY', activeGenerationId: null, restoreFence: false, filename: 'secret.xlsx' }) });
  assert.equal((await adapter(setup).adapter.readStatus(scope)).code, 'ETP_NATIVE_RESPONSE_INVALID');
  assert.equal((await value.readStatus({ ...scope, storeCode: 'OTHER' })).code, 'ETP_SCOPE_INVALID');
});

test('verified reads are bounded, scope-bound and dictionary-projected', async () => {
  const { adapter: value, state } = adapter();
  const result = await value.readFacts(scope, { generationId: 'gen:001', reportId: 'R022', fields: ['transaction_id', 'amount'], cursor: null, limit: 25 });
  assert.equal(result.ok, true); assert.deepEqual(result.page.rows, [{ amount: 42, transaction_id: '0001' }]);
  assert.deepEqual(result.page.nextCursor, { chunkIndex: 0, rowOffset: 1 });
  assert.deepEqual(state.calls[0][1], { contractVersion: 1, scopeKey: 'WLMHW|2024-25|2024-09-16..2025-03-31', generationId: 'gen:001', reportId: 'R022', fields: ['transaction_id', 'amount'], cursorChunkIndex: 0, cursorRowOffset: 0, limit: 25 });
  assert.equal((await value.readFacts(scope, { generationId: 'gen:001', reportId: 'R022', fields: ['report_date'], cursor: { chunkIndex: 0, rowOffset: 0 }, limit: bridge.MAX_READ_ROWS + 1 })).code, 'ETP_READ_METADATA_INVALID');
  assert.equal((await value.readFacts(scope, { generationId: 'gen:001', reportId: 'R022', fields: ['customer_name'], cursor: null, limit: 10 })).code, 'ETP_READ_FIELD_FORBIDDEN');
});

test('verified reads reject stale, fenced, malformed and over-broad native results', async () => {
  for (const code of ['RESTORE_FENCED', 'STALE_GENERATION', 'INTEGRITY_FAILED', 'KEY_UNAVAILABLE']) {
    const setup = fake({ readFacts: async () => ({ ok: false, code }) });
    assert.equal((await adapter(setup).adapter.readFacts(scope, { generationId: 'gen:001', reportId: 'R003', fields: ['amount'], cursor: null, limit: 1 })).ok, false);
  }
  const leaked = fake({ readFacts: async p => ({ ok: true, scopeKey: p.scopeKey, generationId: p.generationId, reportId: p.reportId, rows: [{ amount: 1, customer_name: 'secret' }], hasMore: false, nextChunkIndex: 1, nextRowOffset: 0 }) });
  assert.equal((await adapter(leaked).adapter.readFacts(scope, { generationId: 'gen:001', reportId: 'R003', fields: ['amount'], cursor: null, limit: 1 })).code, 'ETP_NATIVE_RESPONSE_INVALID');
});

test('native failures expose only stable ETP codes', async () => {
  const setup = fake({ beginStage: async () => { throw new Error('database path and customer leaked here'); } });
  const value = adapter(setup).adapter;
  const lifecycle = advance(policy.create(scope, 'gen:001').lifecycle, ['PREFLIGHT_PASS', 'PARSE_PASS', 'POLICY_PASS', 'BEGIN_STAGING']);
  assert.deepEqual(await value.beginStage(lifecycle), { ok: false, code: 'ETP_NATIVE_CALL_FAILED' });
  const allowed = fake({ beginStage: async () => ({ ok: false, code: 'ETP_STORAGE_FULL' }) });
  assert.deepEqual(await adapter(allowed).adapter.beginStage(lifecycle), { ok: false, code: 'ETP_STORAGE_FULL' });
  const invented = fake({ beginStage: async () => ({ ok: false, code: 'ETP_INTERNAL_PATH_LEAK' }) });
  assert.deepEqual(await adapter(invented).adapter.beginStage(lifecycle), { ok: false, code: 'ETP_NATIVE_CALL_FAILED' });
  const keyMissing = fake({ beginStage: async () => ({ ok: false, code: 'KEY_UNAVAILABLE', message: 'keystore alias and path must stay hidden' }) });
  assert.deepEqual(await adapter(keyMissing).adapter.beginStage(lifecycle), { ok: false, code: 'ETP_KEY_UNAVAILABLE' });
});

test('chunk byte ceiling measures actual UTF-8 bytes including non-ASCII text', async () => {
  const { adapter: value, state } = adapter();
  const lifecycle = advance(policy.create(scope, 'gen:001').lifecycle, ['PREFLIGHT_PASS', 'PARSE_PASS', 'POLICY_PASS', 'BEGIN_STAGING']);
  const large = '€'.repeat(180000);
  const chunk = { scopeKey: lifecycle.scopeKey, generationId: 'gen:001', reportId: 'R022', chunkIndex: 0, rows: Array(45).fill(null).map((_, index) => ({ transaction_id: String(index), amount: 1, report_date: large.slice(index * 4000, (index + 1) * 4000) })) };
  assert.equal((await value.appendChunk(lifecycle, chunk)).code, 'ETP_CHUNK_BYTES_EXCEEDED');
  assert.equal(state.calls.length, 0);
});

test('restore fencing is scope-bound and returns only verified reimport-required metadata', async () => {
  const { adapter: value, state } = adapter();
  const result = await value.fenceAfterRestore(scope);
  assert.deepEqual(result, { ok: true, status: { state: 'REIMPORT_REQUIRED', activeGenerationId: null, restoreFence: true } });
  assert.deepEqual(state.calls[0], ['fence', { contractVersion: 1, scopeKey: 'WLMHW|2024-25|2024-09-16..2025-03-31' }]);
  assert.equal((await value.fenceAfterRestore({ ...scope, storeCode: 'OTHER' })).code, 'ETP_SCOPE_INVALID');
});

test('scope reset requires an explicit literal confirmation and validates native response', async () => {
  const { adapter: value, state } = adapter();
  assert.equal((await value.resetScope(scope)).code, 'ETP_RESET_CONFIRMATION_REQUIRED');
  assert.equal(state.calls.length, 0);
  const result = await value.resetScope(scope, 'RESET_ETP_SCOPE');
  assert.deepEqual(result, { ok: true, status: { state: 'EMPTY', activeGenerationId: null, restoreFence: false } });
  assert.equal(state.calls[0][0], 'reset');
  const malformed = fake({ resetScope: async () => ({ ok: true, state: 'ACCEPTED' }) });
  assert.equal((await adapter(malformed).adapter.resetScope(scope, 'RESET_ETP_SCOPE')).code, 'ETP_NATIVE_RESPONSE_INVALID');
});

test('whole ETP store reset requires its distinct explicit confirmation and sends no scope or data', async () => {
  const { adapter: value, state } = adapter();
  assert.equal((await value.resetStore()).code, 'ETP_RESET_CONFIRMATION_REQUIRED');
  assert.equal((await value.resetStore('RESET_ETP_SCOPE')).code, 'ETP_RESET_CONFIRMATION_REQUIRED');
  assert.equal(state.calls.length, 0);
  const result = await value.resetStore('RESET_ETP_STORE');
  assert.deepEqual(result, { ok: true, status: { state: 'EMPTY', activeGenerationId: null, restoreFence: false } });
  assert.deepEqual(state.calls[0], ['reset-store', { contractVersion: 1 }]);
});

test('bridge method names and bounded fields match the native plugin source contract', () => {
  const native = readFileSync(new URL('../build-overrides/native/SaagarEtpStorePlugin.java', import.meta.url), 'utf8');
  for (const name of ['beginStage', 'appendStageChunk', 'finishStage', 'publishStage', 'readStatus', 'readFacts', 'fenceAfterRestore', 'resetScope', 'resetStore']) assert.match(native, new RegExp(`public (?:synchronized )?void ${name}\\(`));
  assert.match(native, /contractVersion/); assert.match(native, /scopeKey/); assert.match(native, /MAX_BYTES\s*=\s*512\s*\*\s*1024/);
});

test('native chunks use an authenticated encrypted envelope and never a plaintext payload column', () => {
  const native = readFileSync(new URL('../build-overrides/native/SaagarEtpStorePlugin.java', import.meta.url), 'utf8');
  const schema = native.match(/CREATE TABLE stage_chunk\(([^"]+)"\)/)?.[1] || '';
  assert.match(schema, /payload_envelope TEXT NOT NULL/);
  assert.doesNotMatch(schema, /(?:^|,)payload TEXT/);
  assert.doesNotMatch(native, /v\.put\("payload",\s*payload\)/);
  assert.match(native, /v\.put\("payload_envelope",\s*envelope\)/);
  assert.match(native, /AES\/GCM\/NoPadding/);
  assert.match(native, /String aad="1\|"\+scope\+"\|"\+gen\+"\|"\+report\+"\|"\+index\+"\|"\+rows\.length\(\)\+"\|"\+digest/);
  assert.match(native, /cipher\.updateAAD\(aad\.getBytes\(StandardCharsets\.UTF_8\)\)/);
  assert.match(native, /String envelope="ETP1\."/);
});

test('whole-store reset deletes both database sidecars and the dedicated ETP key alias', () => {
  const native = readFileSync(new URL('../build-overrides/native/SaagarEtpStorePlugin.java', import.meta.url), 'utf8');
  const body = native.slice(native.indexOf('void resetStore'), native.indexOf('private SQLiteDatabase db'));
  assert.match(body, /"-wal","-shm","-journal"/);
  assert.match(body, /deleteEntry\(ETP_KEY_ALIAS\)/);
  assert.match(body, /containsAlias\(ETP_KEY_ALIAS\).*fail\("RESET_FAILED"/s);
});

test('published status is authenticated by a generation seal including zero-row imports', () => {
  const native = readFileSync(new URL('../build-overrides/native/SaagarEtpStorePlugin.java', import.meta.url), 'utf8');
  assert.match(native, /manifest_digest TEXT/); assert.match(native, /chunk_count INTEGER/); assert.match(native, /seal_envelope TEXT/);
  assert.match(native, /String manifestDigest=sha256\(text\)/);
  assert.match(native, /String sealAad="1\|SEAL\|"\+scope\+"\|"\+gen\+"\|"\+manifestDigest\+"\|"\+expected\+"\|"\+chunks/);
  assert.match(native, /encryptEnvelope\("SEALED",sealAad\)/);
  const status = native.slice(native.indexOf('void readStatus'), native.indexOf('void fenceAfterRestore'));
  assert.match(status, /authenticateGenerationSeal\(db,scope,active\)/);
  const authStart = native.indexOf('void authenticateGenerationSeal');
  const auth = native.slice(authStart, native.indexOf('private String sha256', authStart));
  assert.match(auth, /SELECT manifest_digest,row_count,chunk_count,seal_envelope FROM generation/);
  assert.doesNotMatch(auth, /stage_chunk/);
  assert.match(auth, /MessageDigest\.isEqual\(opened,"SEALED"\.getBytes\(StandardCharsets\.UTF_8\)\)/);
});
