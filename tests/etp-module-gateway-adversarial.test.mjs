import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const gatewayApi = require('../www/etp-module-gateway.js');
const lifecycle = require('../www/etp-store-lifecycle-policy.js');
const core = require('../www/etp-core-contract.js');
const foundationStatus = require('../www/etp-foundation-status.js');
const queryContract = require('../www/etp-query-contract.js');
const profileAuthority = require('../www/etp-profile-authority.js');
const importHistoryApi = require('../www/etp-import-history.js');
const tenderDictionaryApi = require('../www/etp-tender-dictionary.js');
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const generationA = `etp_${'a'.repeat(32)}`;
const generationB = `etp_${'b'.repeat(32)}`;
const scope = { storeCode: 'WLMHW', financialYear: '2026-27', periodStart: '2026-04-01', periodEnd: '2026-04-30' };
const scopeKey = 'WLMHW|2026-27|2026-04-01..2026-04-30';

function receipt(generationId = generationA, publishedAt = '2026-05-01', paymentRows = 9) {
  const life = lifecycle.create(scope, generationId).lifecycle;
  const authorityBinding=profileAuthority.authorize({storeCode:'WLMHW',purpose:'PRODUCTION',profileVersion:profileAuthority.PROFILE_VERSION,parserVersion:profileAuthority.PARSER_VERSION}).binding;
  return {
    contractVersion: core.ETP_CORE_VERSION, scopeKey, storeCode: scope.storeCode,
    activeGenerationId: generationId, profileVersion: core.ETP_CORE_VERSION,
    parserVersion: profileAuthority.PARSER_VERSION, profileAuthority: authorityBinding, tenderDictionary: tenderDictionaryApi.BUILD_IDENTITY,
    ruleVersion: core.RECON_RULE.ruleVersion, reconciliationStatus: 'PASS',
    enrichments: { R003: { status: 'PASS', differenceCount: 0 }, R013: { status: 'PASS', differenceCount: 0 },
      paymentType25: { status: 'QUARANTINED', rowCount: paymentRows, persisted: false } },
    coverage: Object.fromEntries(core.REPORTS.map((id) => [id, { status: 'COMPLETE', periodStart: scope.periodStart,
      declaredPeriodEnd: scope.periodEnd, evidenceId: 'f'.repeat(64), zeroActivityConfirmed: false }])),
    publishedAt,
    lifecycle: { ...life, state: 'ACCEPTED', candidateGenerationId: null, activeGenerationId: generationId,
      activeManifestIdentity: `manifest-${generationId}`, manifest:{authority:authorityBinding,tenderDictionary:tenderDictionaryApi.BUILD_IDENTITY} }
  };
}

function storageWith(current = receipt(), history = []) {
  const value = JSON.stringify({ scopes: { [scopeKey]: { current, history } } });
  return { getItem(key) { return key === gatewayApi.REGISTRY_KEY ? value : null; }, setItem() {} };
}

function make(overrides = {}) {
  let reads = 0;
  const runtime = overrides.runtime || {
    async run() { throw new Error('unused'); },
    async confirm() { throw new Error('unused'); },
    async readVerified(_scope, request) {
      reads += 1;
      return { ok: true, page: { scopeKey, generationId: generationA, reportId: request.reportId,
        rows: [{ [request.fields[0]]: 'safe' }], hasMore: false, nextCursor: null } };
    }
  };
  const result = gatewayApi.create({ runtime, lifecyclePolicy: lifecycle, core, foundationStatus, queryContract, profileAuthority, importHistoryApi, tenderDictionaryApi,
    storage: overrides.storage || storageWith(),
    authorize: overrides.authorize || (() => true),
    statusReader: overrides.statusReader || (async () => ({ ok: true, status: {
      state: 'ACCEPTED', activeGenerationId: generationA, restoreFence: false } })) });
  return { ...result, reads: () => reads };
}

test('report projections are explicit frozen allowlists and fail before verified runtime access', async () => {
  assert.deepEqual(gatewayApi.PROJECTIONS, {
    R003: ['transaction_type_raw', 'net_amount', 'scheme_discount', 'user_discount'],
    R013: ['invoice_number', 'transaction_type_raw', 'quantity', 'net_amount', 'cro_number'],
    R022: ['transaction_type_raw', 'invoice_quantity', 'net_value', 'cash_amount', 'card_amount', 'bhim_upi_amount', 'phonepe_amount', 'paytm_amount', 'razorpay_amount', 'bharatpe_amount', 'cheque_amount', 'others_amount', 'payment_type24_amount'],
    R025: ['invoice_number', 'transaction_type_raw', 'quantity', 'net_amount', 'brand', 'cluster', 'gender', 'scheme_discount', 'user_discount', 'tax_amount']
  });
  assert.equal(Object.isFrozen(gatewayApi.PROJECTIONS), true);
  for (const fields of Object.values(gatewayApi.PROJECTIONS)) assert.equal(Object.isFrozen(fields), true);

  const fx = make();
  for (const [reportId, field] of [['R003', 'cash_amount'], ['R013', 'brand'], ['R022', 'tax_amount'],
    ['R025', 'cro_number'], ['R022', 'payment_type25_amount'], ['R025', 'raw_row']]) {
    const result = await fx.gateway.readVerified(scope, { reportId, fields: [field], cursor: null, limit: 1 });
    assert.equal(result.code, 'ETP_VERIFIED_PROJECTION_INVALID', `${reportId}:${field}`);
  }
  assert.equal(fx.reads(), 0);
});

test('paging limits match the native boundary and malformed pages fail closed', async () => {
  assert.equal(gatewayApi.MAX_READ_ROWS, 200);
  const fx = make();
  assert.equal((await fx.gateway.readVerified(scope, { reportId: 'R025', fields: ['net_amount'], cursor: null, limit: 201 })).code,
    'ETP_VERIFIED_PROJECTION_INVALID');
  for (const cursor of [{ chunkIndex: 4096, rowOffset: 0 }, { chunkIndex: 0, rowOffset: 500 },
    { chunkIndex: -1, rowOffset: 0 }, { chunkIndex: 0, rowOffset: -1 }]) {
    assert.equal((await fx.gateway.readVerified(scope, { reportId: 'R025', fields: ['net_amount'], cursor, limit: 1 })).code,
      'ETP_READ_CURSOR_INVALID');
  }

  for (const poisoned of [NaN, Infinity, 'x'.repeat(4097)]) {
    const bad = make({ runtime: { async run() {}, async confirm() {}, async readVerified(_scope, request) {
      return { ok: true, page: { scopeKey, generationId: generationA, reportId: request.reportId,
        rows: [{ net_amount: poisoned }], hasMore: false, nextCursor: null } };
    } } });
    assert.equal((await bad.gateway.readVerified(scope, { reportId: 'R025', fields: ['net_amount'], cursor: null, limit: 1 })).code,
      'ETP_GATEWAY_RESPONSE_INVALID');
  }
  const missingProjection = make({ runtime: { async run() {}, async confirm() {}, async readVerified(_scope, request) {
    return { ok: true, page: { scopeKey, generationId: generationA, reportId: request.reportId,
      rows: [{}], hasMore: false, nextCursor: null } };
  } } });
  assert.equal((await missingProjection.gateway.readVerified(scope,
    { reportId: 'R025', fields: ['net_amount'], cursor: null, limit: 1 })).code, 'ETP_GATEWAY_RESPONSE_INVALID');
});

test('PAYMENTTYPE25 remains quarantine metadata and cannot become a fact projection', async () => {
  const fx = make();
  const inspected = await fx.gateway.inspectScope(scope, { historyLimit: 0 });
  assert.deepEqual(inspected.currentReceipt.exceptions.paymentType25,
    { status: 'QUARANTINED', rowCount: 9, persisted: false });
  assert.equal((await fx.gateway.readVerified(scope, { reportId: 'R022', fields: ['payment_type25_amount'], cursor: null, limit: 1 })).code,
    'ETP_VERIFIED_PROJECTION_INVALID');

  const invalid = receipt();
  invalid.enrichments.paymentType25.persisted = true;
  assert.equal((await make({ storage: storageWith(invalid) }).gateway.inspectScope(scope)).code, 'ETP_RECEIPT_NOT_FOUND');
});

test('recreated gateway exposes only validated bounded history and matching active generation', async () => {
  const history = [receipt(generationB, '2026-04-30', 1), receipt(generationA, '2026-04-29', 2)];
  const storage = storageWith(receipt(), history);
  const first = make({ storage });
  const before = await first.gateway.inspectScope(scope, { historyLimit: 2 });
  const relaunched = make({ storage });
  const after = await relaunched.gateway.inspectScope(scope, { historyLimit: 2 });
  assert.deepEqual(after, before);
  assert.equal(after.history.length, 2);
  assert.equal(JSON.stringify(after).includes('lifecycle'), false);

  const stale = make({ storage, statusReader: async () => ({ ok: true, status: {
    state: 'ACCEPTED', activeGenerationId: generationB, restoreFence: false } }) });
  assert.equal((await stale.gateway.inspectScope(scope, { historyLimit: 2 })).code,
    'ETP_VERIFIED_GENERATION_UNAVAILABLE');
});

test('gateway import history is scope-bound metadata only and ignores corrupt private entries',async()=>{const valid={contractVersion:importHistoryApi.VERSION,eventId:'evt-safe-1',scopeKey,storeCode:'WLMHW',financialYear:'2026-27',periodStart:'2026-04-01',periodEnd:'2026-04-30',outcome:'ACCEPTED',warningCodes:['PAYMENTTYPE25_QUARANTINED'],counts:{sourceCount:8,selectedCount:4,excludedCount:4},actorId:'BUILD_AUTHORIZED_OWNER',occurredAt:'2026-08-24T00:00:00Z',digestRefs:['sha256:'+'a'.repeat(64)]};const registry=JSON.stringify({scopes:{[scopeKey]:{current:receipt(),history:[]}}}),history=JSON.stringify({contractVersion:importHistoryApi.VERSION,events:[valid,{...valid,eventId:'evt-private',filename:'private.xlsx'}]});const storage={getItem(key){return key===gatewayApi.REGISTRY_KEY?registry:key===importHistoryApi.KEY?history:null;},setItem(){}};const made=make({storage});const inspected=await made.gateway.inspectScope(scope,{historyLimit:5});assert.equal(inspected.ok,true);assert.deepEqual(inspected.importHistory,[importHistoryApi.validateEvent(valid).event]);assert.doesNotMatch(JSON.stringify(inspected.importHistory),/filename|workbook|rows|customer|mobile/i);assert.equal((await made.gateway.inspectScope({...scope,periodStart:'2026-04-02'},{historyLimit:5})).ok,false);});

test('shell access denies ETP to every default staff role except Store Manager before module PIN entry', () => {
  const shell = fs.readFileSync(path.join(root, 'www/index.html'), 'utf8');
  assert.match(shell, /"Store Manager":ALL\(\)/);
  for (const role of ['Cashier', 'CRO', 'Greeter', 'Technician', 'Assistant Technician', 'Trainee', 'Others']) {
    const escaped = role.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const row = new RegExp(`"${escaped}":ONLY\\(([^)]*)\\)`).exec(shell);
    assert.ok(row, role);
    assert.doesNotMatch(row[1], /["']etp["']/);
  }
  const access = /function ensureModuleAccess\(id\)\{([\s\S]*?)\n\}/.exec(shell);
  assert.ok(access);
  assert.ok(access[1].indexOf('roleCanOpen(id)') < access[1].indexOf('modulePinRequired(id)'),
    'role denial must happen before any module PIN flow');
  assert.match(shell, /open\(id,\{[\s\S]*?ensureModuleAccess:ensureModuleAccess/);
});
