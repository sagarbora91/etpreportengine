import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const gatewayApi = require('../www/etp-module-gateway.js');
const lifecycle = require('../www/etp-store-lifecycle-policy.js');
const core = require('../www/etp-core-contract.js');
const foundationStatus = require('../www/etp-foundation-status.js');
const queryContract = require('../www/etp-query-contract.js');
const profileAuthority = require('../www/etp-profile-authority.js');
const importHistoryApi = require('../www/etp-import-history.js');
const tenderDictionaryApi = require('../www/etp-tender-dictionary.js');

const generationA = `etp_${'a'.repeat(32)}`;
const generationB = `etp_${'b'.repeat(32)}`;
const scope = Object.freeze({
  storeCode: 'WLMHW', financialYear: '2026-27',
  periodStart: '2026-04-01', periodEnd: '2026-04-30'
});
const scopeKey = 'WLMHW|2026-27|2026-04-01..2026-04-30';

function receipt(generationId = generationA) {
  const created = lifecycle.create(scope, generationId).lifecycle;
  const authorityBinding=profileAuthority.authorize({storeCode:'WLMHW',purpose:'PRODUCTION',profileVersion:profileAuthority.PROFILE_VERSION,parserVersion:profileAuthority.PARSER_VERSION}).binding;
  return {
    contractVersion: core.ETP_CORE_VERSION,
    scopeKey,
    storeCode: scope.storeCode,
    activeGenerationId: generationId,
    profileVersion: core.ETP_CORE_VERSION,
    parserVersion: profileAuthority.PARSER_VERSION,
    profileAuthority: authorityBinding, tenderDictionary: tenderDictionaryApi.BUILD_IDENTITY,
    ruleVersion: core.RECON_RULE.ruleVersion,
    reconciliationStatus: 'PASS',
    enrichments: {
      R003: { status: 'PASS', differenceCount: 0 },
      R013: { status: 'PASS', differenceCount: 0 },
      paymentType25: { status: 'QUARANTINED', rowCount: 0, persisted: false }
    },
    coverage: Object.fromEntries(core.REPORTS.map(reportId => [reportId, {
      status: 'COMPLETE', periodStart: scope.periodStart, declaredPeriodEnd: scope.periodEnd,
      evidenceId: 'f'.repeat(64), zeroActivityConfirmed: false
    }])),
    publishedAt: '2026-05-01',
    lifecycle: { ...created, state: 'ACCEPTED', candidateGenerationId: null,
      activeGenerationId: generationId, activeManifestIdentity: 'manifest-safe',manifest:{authority:authorityBinding,tenderDictionary:tenderDictionaryApi.BUILD_IDENTITY} }
  };
}

function row(reportId, fields) {
  const values = {
    transaction_type_raw: 'INV', invoice_number: '1001', cro_number: '101',
    brand: 'Titan', cluster: 'A', gender: 'U'
  };
  return Object.fromEntries(fields.map(field => [field, values[field] ?? '1']));
}

function fixture(overrides = {}) {
  const calls = { get: 0, set: 0, remove: 0, run: 0, confirm: 0, read: 0, status: 0 };
  const registry = JSON.stringify({ scopes: { [scopeKey]: { current: receipt(), history: [] } } });
  const storage = overrides.storage || {
    getItem(key) { calls.get++; return key === gatewayApi.REGISTRY_KEY ? registry : null; },
    setItem() { calls.set++; }, removeItem() { calls.remove++; }
  };
  const runtime = overrides.runtime || {
    async run() { calls.run++; throw new Error('write path must not run'); },
    async confirm() { calls.confirm++; throw new Error('write path must not run'); },
    async readVerified(_selectedScope, request) {
      calls.read++;
      return { ok: true, page: { scopeKey, generationId: generationA,
        reportId: request.reportId, rows: [row(request.reportId, request.fields)],
        hasMore: false, nextCursor: null } };
    }
  };
  const made = gatewayApi.create({
    runtime, lifecyclePolicy: lifecycle, core, foundationStatus, queryContract, profileAuthority, importHistoryApi, tenderDictionaryApi, storage,
    statusReader: overrides.statusReader || (async () => {
      calls.status++;
      return { ok: true, status: { state: 'ACCEPTED', activeGenerationId: generationA, restoreFence: false } };
    }),
    authorize: overrides.authorize || (() => true),
    tokenFactory: () => 'c'.repeat(32)
  });
  assert.equal(made.ok, true);
  return { ...made, calls };
}

function assertMetadataOnly(value) {
  const blocked = /^(?:row|page|cursor|nextCursor|token|workbook|worksheet|filename|fileLabel|file|path|source|blob|bytes|base64|customer(?:_?name)?|consumer|mobile|phone|email|address|name|aadhaar|pan|dob|native|storage|runtime|plugin|readFacts)$/i;
  const seen = new Set();
  (function visit(node) {
    if (node === null || typeof node !== 'object' || seen.has(node)) return;
    seen.add(node);
    for (const key of Object.keys(node)) {
      assert.doesNotMatch(key, blocked, `forbidden facade output key: ${key}`);
      if (key === 'rows') assert.ok(Number.isSafeInteger(node[key]) && node[key] >= 0,
        'rows may occur only as an aggregate count, never as raw row material');
      visit(node[key]);
    }
  })(value);
}

test('read facade is frozen, exact and contains no publication, raw-read or persistence authority', () => {
  const { gateway } = fixture();
  assert.ok(gateway.readFacade);
  assert.equal(Object.isFrozen(gateway.readFacade), true);
  assert.deepEqual(Object.keys(gateway.readFacade).sort(), ['inspectScope', 'listScopes', 'loadSummary']);
  for (const key of ['run', 'confirm', 'readVerified', 'readFacts', 'storage', 'native', 'runtime', 'plugin']) {
    assert.equal(key in gateway.readFacade, false, key);
  }
});

test('facade list, inspection and summary outputs are frozen metadata only', async () => {
  const { gateway } = fixture();
  const listed = gateway.readFacade.listScopes({ limit: 1 });
  const inspected = await gateway.readFacade.inspectScope(scope, { historyLimit: 1 });
  const summary = await gateway.readFacade.loadSummary(scope);
  for (const result of [listed, inspected, summary]) {
    assert.equal(result.ok, true);
    assert.equal(Object.isFrozen(result), true);
    assertMetadataOnly(result);
  }
  assert.equal(summary.scope.scopeKey, scopeKey);
  assert.equal(summary.rowCount, 4);
  assert.equal(summary.pages, 4);
  assert.deepEqual(Object.keys(summary.summaries), core.REPORTS);
});

test('all facade reads are observational and never invoke a mutation path', async () => {
  const fx = fixture();
  fx.gateway.readFacade.listScopes({ limit: 1 });
  await fx.gateway.readFacade.inspectScope(scope, { historyLimit: 1 });
  await fx.gateway.readFacade.loadSummary(scope);
  assert.deepEqual({ set: fx.calls.set, remove: fx.calls.remove, run: fx.calls.run, confirm: fx.calls.confirm },
    { set: 0, remove: 0, run: 0, confirm: 0 });
  assert.equal(fx.calls.read, 4);
  assert.ok(fx.calls.get > 0);
  assert.ok(fx.calls.status > 0);
});

test('scope and generation mismatches fail closed before facts can cross the facade', async () => {
  const fx = fixture();
  const otherScope = { ...scope, storeCode: 'HEMW' };
  const missing = await fx.gateway.readFacade.loadSummary(otherScope);
  assert.equal(missing.ok, false);
  assert.equal(missing.code, 'ETP_RECEIPT_NOT_FOUND');
  assert.equal(fx.calls.read, 0);

  const stale = fixture({ statusReader: async () => ({ ok: true,
    status: { state: 'ACCEPTED', activeGenerationId: generationB, restoreFence: false } }) });
  const rejected = await stale.gateway.readFacade.loadSummary(scope);
  assert.equal(rejected.ok, false);
  assert.equal(rejected.code, 'ETP_VERIFIED_GENERATION_UNAVAILABLE');
  assert.equal(stale.calls.read, 0);
  assertMetadataOnly(rejected);
});

test('hostile coordinate cursors are rejected and never exposed by the read facade', async () => {
  const fx = fixture();
  const result = await fx.gateway.readVerified(scope, {
    reportId: 'R025', fields: ['net_amount'], limit: 10,
    cursor: { chunkIndex: 0, rowOffset: 0 }
  });
  assert.equal(result.ok, false);
  assert.equal(result.code, 'ETP_READ_CURSOR_INVALID');
  assert.equal(fx.calls.read, 0);
  assert.equal(JSON.stringify(Object.keys(fx.gateway.readFacade)).includes('cursor'), false);
});
