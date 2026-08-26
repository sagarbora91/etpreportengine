import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const coordinatorApi = require('../www/etp-import-coordinator.js');
const lifecyclePolicy = require('../www/etp-store-lifecycle-policy.js');
const profileAuthority = require('../www/etp-profile-authority.js');

const H = 'a'.repeat(64), S = 'b'.repeat(64);
const scope = { storeCode: 'WLMHW', financialYear: '2026-27', periodStart: '2026-04-01', periodEnd: '2026-04-30' };
const authorityBinding=profileAuthority.authorize({storeCode:'WLMHW',purpose:'PRODUCTION',profileVersion:profileAuthority.PROFILE_VERSION,parserVersion:profileAuthority.PARSER_VERSION}).binding;
const tenderIdentity={contractVersion:'ETP_TENDER_DICTIONARY_V1',versionId:'retail-etp-tender-v1',effectiveAt:'2026-08-24T00:00:00.000Z'};
function fixture(generationId = 'gen1') {
  const scopeKey = 'WLMHW|2026-27|2026-04-01..2026-04-30';
  return {
    manifest: { scopeKey, generationId, authority:authorityBinding, tenderDictionary:tenderIdentity, reports: ['R003', 'R013', 'R022', 'R025'].map(reportId => ({ reportId, sourceSha256: S, headerSignatureSha256: H, rowCount: 1 })) },
    chunks: ['R003', 'R013', 'R022', 'R025'].map(reportId => ({ reportId, chunkIndex: 0, rows: [{ document_id: reportId + '-1' }] }))
  };
}
function harness(overrides = {}) {
  const calls = [];
  const data = fixture(overrides.generationId || 'gen1');
  const pipeline = {
    preflight: async value => (calls.push(['preflight', value]), overrides.preflight || { ok: true }),
    parse: async value => (calls.push(['parse', value]), overrides.parse || { ok: true, workbookSet: true }),
    validate: async value => (calls.push(['validate', value]), overrides.validate || { ok: true, ...data }),
    reconcile: async value => (calls.push(['reconcile', value]), overrides.reconcile || { ok: true, status: 'PASS' }),
    authorizePublication: async value => (calls.push(['authorize', value]), overrides.authorize || { ok: true })
  };
  const store = {
    beginStage: async value => (calls.push(['begin', value]), overrides.begin || { ok: true }),
    appendChunk: async (_life, value) => (calls.push(['append', value]), overrides.append || { ok: true }),
    finishStage: async (_life, value) => (calls.push(['finish', value]), overrides.finish || { ok: true }),
    publish: async value => (calls.push(['publish', value]), overrides.publish || { ok: true })
  };
  const made = coordinatorApi.create({ lifecyclePolicy, pipeline, store });
  assert.equal(made.ok, true);
  return { run: made.coordinator.run, confirm: made.coordinator.confirm, calls, data };
}
const request = { scope, files: [{ id: 1 }], generationId: 'gen1', confirmed: true };

test('runs the complete verified transaction and publishes only after reconciliation', async () => {
  const h = harness();
  const result = await h.run(request);
  assert.equal(result.ok, true);
  assert.equal(result.changed, true);
  assert.equal(result.lifecycle.state, 'ACCEPTED');
  assert.deepEqual(h.calls.map(x => x[0]), ['preflight', 'parse', 'validate', 'begin', 'append', 'append', 'append', 'append', 'finish', 'reconcile', 'authorize', 'publish']);
});

test('keeps publication untouched when staging fails', async () => {
  const h = harness({ append: { ok: false, code: 'ETP_STORAGE_FULL' } });
  const result = await h.run(request);
  assert.deepEqual({ ok: result.ok, code: result.code, stage: result.stage }, { ok: false, code: 'ETP_STORAGE_FULL', stage: 'STAGE_APPEND' });
  assert.equal(h.calls.some(x => x[0] === 'publish'), false);
});

test('keeps publication untouched when reconciliation blocks', async () => {
  const h = harness({ reconcile: { ok: true, status: 'BLOCKED' } });
  const result = await h.run(request);
  assert.equal(result.code, 'ETP_RECONCILIATION_REJECTED');
  assert.equal(h.calls.some(x => x[0] === 'publish'), false);
});

test('returns an identical import as a no-op before native staging', async () => {
  const acceptedBase = lifecyclePolicy.create(scope, 'old');
  let life = acceptedBase.lifecycle;
  for (const event of ['PREFLIGHT_PASS', 'PARSE_PASS', 'POLICY_PASS', 'BEGIN_STAGING', 'STAGE_COMPLETE']) life = lifecyclePolicy.transition(life, event).lifecycle;
  const oldManifest = fixture('old').manifest;
  life = lifecyclePolicy.attachManifest(life, oldManifest).lifecycle;
  life = lifecyclePolicy.transition(life, 'RECONCILE_PASS').lifecycle;
  life = lifecyclePolicy.transition(life, 'REQUEST_CONFIRMATION').lifecycle;
  life = lifecyclePolicy.publish(life).lifecycle;
  const h = harness();
  const result = await h.run({ ...request, currentLifecycle: life });
  assert.equal(result.ok, true);
  assert.equal(result.duplicate, true);
  assert.equal(result.lifecycle.activeGenerationId, 'old');
  assert.equal(h.calls.some(x => x[0] === 'begin'), false);
});

test('rejects a current generation from another store or period', async () => {
  const other = lifecyclePolicy.create({ ...scope, storeCode: 'HEMW' }, 'old').lifecycle;
  const h = harness();
  const result = await h.run({ ...request, currentLifecycle: other });
  assert.equal(result.code, 'ETP_ACTIVE_SCOPE_INVALID');
  assert.equal(h.calls.length, 0);
});

test('requires explicit confirmation after successful reconciliation', async () => {
  const h = harness();
  const result = await h.run({ ...request, confirmed: false });
  assert.equal(result.awaitingConfirmation, true);
  assert.equal(result.lifecycle.state, 'AWAITING_CONFIRMATION');
  assert.equal(h.calls.some(x => x[0] === 'publish'), false);
  const accepted = await h.confirm(result.lifecycle);
  assert.equal(accepted.ok, true);
  assert.equal(accepted.lifecycle.state, 'ACCEPTED');
});

test('manager authorization fails closed before native publication', async () => {
  const h = harness({ authorize: { ok: false } });
  const result = await h.run(request);
  assert.equal(result.code, 'ETP_PUBLICATION_AUTH_REQUIRED');
  assert.equal(h.calls.some(x => x[0] === 'publish'), false);
});

test('supports a zero-activity report without inventing a fact chunk', async () => {
  const data = fixture();
  data.manifest.reports.find(x => x.reportId === 'R003').rowCount = 0;
  data.chunks = data.chunks.filter(x => x.reportId !== 'R003');
  const h = harness({ validate: { ok: true, ...data } });
  const result = await h.run(request);
  assert.equal(result.ok, true);
  assert.equal(h.calls.filter(x => x[0] === 'append').length, 3);
});
