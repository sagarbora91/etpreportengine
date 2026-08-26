import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import fs from 'node:fs';

const require = createRequire(import.meta.url);
const gatewayApi = require('../www/etp-module-gateway.js');
const lifecycle = require('../www/etp-store-lifecycle-policy.js');
const core = require('../www/etp-core-contract.js');
const foundationStatus = require('../www/etp-foundation-status.js');
const queryContract = require('../www/etp-query-contract.js');
const profileAuthority = require('../www/etp-profile-authority.js');
const importHistoryApi = require('../www/etp-import-history.js');
const tenderDictionaryApi = require('../www/etp-tender-dictionary.js');
const gatewaySource = fs.readFileSync(new URL('../www/etp-module-gateway.js', import.meta.url), 'utf8');

const generationA = 'etp_' + 'a'.repeat(32);
const generationB = 'etp_' + 'b'.repeat(32);
const scope = { storeCode: 'WLMHW', financialYear: '2026-27', periodStart: '2026-04-01', periodEnd: '2026-04-30' };
const scopeKey = 'WLMHW|2026-27|2026-04-01..2026-04-30';

function receipt(generationId = generationA, publishedAt = '2026-05-01') {
  const life = lifecycle.create(scope, generationId).lifecycle;
  const authorityBinding=profileAuthority.authorize({storeCode:'WLMHW',purpose:'PRODUCTION',profileVersion:profileAuthority.PROFILE_VERSION,parserVersion:profileAuthority.PARSER_VERSION}).binding;
  const accepted = Object.freeze({ ...life, state: 'ACCEPTED', candidateGenerationId: null, activeGenerationId: generationId, activeManifestIdentity: 'manifest-safe',manifest:{authority:authorityBinding,tenderDictionary:tenderDictionaryApi.BUILD_IDENTITY} });
  return {
    contractVersion: core.ETP_CORE_VERSION,
    scopeKey,
    storeCode: 'WLMHW',
    activeGenerationId: generationId,
    profileVersion: core.ETP_CORE_VERSION,
    parserVersion: profileAuthority.PARSER_VERSION,
    profileAuthority: authorityBinding, tenderDictionary: tenderDictionaryApi.BUILD_IDENTITY,
    ruleVersion: core.RECON_RULE.ruleVersion,
    reconciliationStatus: 'PASS',
    enrichments: { R003: { status: 'FAIL', differenceCount: 2 }, R013: { status: 'PASS', differenceCount: 0 }, paymentType25: { status: 'QUARANTINED', rowCount: 9, persisted: false } },
    coverage: Object.fromEntries(core.REPORTS.map(id => [id, { status: 'COMPLETE', periodStart: scope.periodStart, declaredPeriodEnd: scope.periodEnd, evidenceId: 'f'.repeat(64), zeroActivityConfirmed: false }])),
    publishedAt,
    lifecycle: accepted
  };
}

function storageWith(current = receipt(), history = []) {
  const value = JSON.stringify({ scopes: { [scopeKey]: { current, history } } });
  return { getItem(key) { return key === gatewayApi.REGISTRY_KEY ? value : null; }, setItem() {} };
}

function fixture(overrides = {}) {
  let confirmedLife = null;
  const runtime = {
    async run() {
      const life = lifecycle.create(scope, generationB).lifecycle;
      return { ok: true, changed: false, awaitingConfirmation: true, lifecycle: { ...life, state: 'AWAITING_CONFIRMATION' } };
    },
    async confirm(life) {
      confirmedLife = life;
      return { ok: true, changed: true, lifecycle: { ...life, state: 'ACCEPTED', candidateGenerationId: null, activeGenerationId: life.candidateGenerationId } };
    },
    async readVerified(_scope, request) {
      return { ok: true, page: { scopeKey, generationId: generationA, reportId: request.reportId, rows: [{ invoice_number: '123', net_amount: 42 }], hasMore: false, nextCursor: null } };
    }
  };
  const made = gatewayApi.create({
    runtime: overrides.runtime || runtime,
    lifecyclePolicy: overrides.lifecyclePolicy || lifecycle,
    core: overrides.core || core,
    foundationStatus: overrides.foundationStatus || foundationStatus,
    queryContract: overrides.queryContract || queryContract,
    profileAuthority: overrides.profileAuthority || profileAuthority,
    importHistoryApi, tenderDictionaryApi,
    storage: overrides.storage || storageWith(),
    statusReader: overrides.statusReader || (async () => ({ ok: true, status: { state: 'ACCEPTED', activeGenerationId: generationA, restoreFence: false } })),
    authorize: overrides.authorize || (() => true),
    tokenFactory: () => 'c'.repeat(32)
  });
  return { ...made, runtime, confirmedLife: () => confirmedLife };
}

test('fails closed when any gateway dependency is absent and exposes no raw native surface', () => {
  assert.equal(gatewayApi.create({}).code, 'ETP_GATEWAY_DEPENDENCY_INVALID');
  const { gateway } = fixture();
  assert.deepEqual(Object.keys(gateway).sort(), ['confirm', 'importFacade', 'inspectScope', 'listScopes', 'loadSummary', 'readFacade', 'readVerified', 'reports', 'run', 'version']);
  assert.deepEqual(Object.keys(gateway.readFacade).sort(), ['inspectScope', 'listScopes', 'loadSummary']);
  assert.equal(Object.isFrozen(gateway.readFacade), true);
  assert.deepEqual(Object.keys(gateway.importFacade).sort(), ['confirm', 'run']);
  assert.equal(Object.isFrozen(gateway.importFacade), true);
  assert.equal('plugin' in gateway, false);
  assert.equal('readFacts' in gateway, false);
  assert.equal(Object.isFrozen(gateway), true);
});

test('run validates exact four-file scope and returns only an opaque confirmation token', async () => {
  let received;
  const fx = fixture({ runtime: {
    async run(value) { received = value; const life = lifecycle.create(scope, generationB).lifecycle; return { ok: true, changed: false, awaitingConfirmation: true, lifecycle: { ...life, state: 'AWAITING_CONFIRMATION' }, reports: { rawWorkbook: 'forbidden' } }; },
    async confirm() { throw new Error('unused'); },
    async readVerified() { throw new Error('unused'); }
  } });
  const files = core.REPORTS.map(id => ({ selectedReportId: id, file: { name: id + '.xlsx', privateBytes: 'not-returned' } }));
  const result = await fx.gateway.run({ scope, files, coverageConfirmed: true });
  assert.equal(result.state, 'AWAITING_CONFIRMATION');
  assert.match(result.confirmationToken, /^confirm_[a-f0-9]{32}_1$/);
  assert.equal('lifecycle' in result, false);
  assert.equal('reports' in result, false);
  assert.equal('files' in result, false);
  assert.deepEqual(received.files, files);
  assert.deepEqual(received.coverageDeclaration, { confirmed: true, confirmedByRole: 'OWNER', reports: Object.fromEntries(core.REPORTS.map(id => [id, { status: 'COMPLETE' }])) });
  assert.equal((await fx.gateway.run({ scope, files: files.slice(0, 3), coverageConfirmed: true })).code, 'ETP_IMPORT_REQUEST_INVALID');
  assert.equal((await fx.gateway.run({ scope, files, coverageConfirmed: false })).code, 'ETP_IMPORT_REQUEST_INVALID');
  assert.equal((await fx.gateway.run({ scope, files, coverageDeclaration: { confirmedByRole: 'OWNER' } })).code, 'ETP_IMPORT_REQUEST_INVALID');
});

test('gateway accepts up to thirteen monthly exports per required report',async()=>{
  const fx=fixture(),files=core.REPORTS.flatMap(id=>[1,2].map(part=>({selectedReportId:id,file:{name:id+'-'+part+'.xlsx'}})));
  const result=await fx.gateway.run({scope,files,coverageConfirmed:true});
  assert.equal(result.ok,true,JSON.stringify(result));
  const tooMany=core.REPORTS.flatMap(id=>Array.from({length:id==='R003'?14:1},(_,part)=>({selectedReportId:id,file:{name:id+'-'+part+'.xlsx'}})));
  assert.equal((await fx.gateway.run({scope,files:tooMany,coverageConfirmed:true})).code,'ETP_REPORT_SELECTION_INVALID');
});

test('gateway denies HEMW production before files or runtime can be touched',async()=>{let runtimeCalls=0,fileReads=0;const fx=fixture({runtime:{async run(){runtimeCalls++;},async confirm(){runtimeCalls++;},async readVerified(){runtimeCalls++;}}}),files=core.REPORTS.map(id=>({selectedReportId:id,file:{name:id+'.xlsx',arrayBuffer:async()=>{fileReads++;}}}));const result=await fx.gateway.run({scope:{...scope,storeCode:'HEMW'},files,coverageConfirmed:true});assert.equal(result.code,'ETP_HEMW_PROFILE_AUTHORIZATION_REQUIRED');assert.equal(runtimeCalls,0);assert.equal(fileReads,0);});

test('confirm consumes its opaque token once and never accepts a caller lifecycle', async () => {
  const fx = fixture();
  const files = core.REPORTS.map(id => ({ selectedReportId: id, file: { name: id + '.xlsx' } }));
  const started = await fx.gateway.run({ scope, files, coverageConfirmed: true });
  const accepted = await fx.gateway.confirm({ confirmationToken: started.confirmationToken });
  assert.equal(accepted.state, 'ACCEPTED');
  assert.equal(accepted.activeGenerationId, generationB);
  assert.equal(fx.confirmedLife().state, 'AWAITING_CONFIRMATION');
  assert.equal((await fx.gateway.confirm({ confirmationToken: started.confirmationToken })).code, 'ETP_CONFIRMATION_TOKEN_INVALID');
  assert.equal((await fx.gateway.confirm({ confirmationToken: 'x', lifecycle: fx.confirmedLife() })).code, 'ETP_CONFIRMATION_TOKEN_INVALID');
});

test('trusted authorization fails closed per call and a denied confirm does not consume its token', async () => {
  let mode = 'IMPORT';
  const fx = fixture({ authorize(action) { return action === mode; } });
  const files = core.REPORTS.map(id => ({ selectedReportId: id, file: { name: id + '.xlsx' } }));
  assert.equal(fx.gateway.listScopes({ limit: 1 }).code, 'ETP_ACCESS_DENIED');
  assert.equal((await fx.gateway.inspectScope(scope, { historyLimit: 1 })).code, 'ETP_ACCESS_DENIED');
  assert.equal((await fx.gateway.readVerified(scope, { reportId: 'R025', fields: ['net_amount'], cursor: null, limit: 1 })).code, 'ETP_ACCESS_DENIED');
  const started = await fx.gateway.run({ scope, files, coverageConfirmed: true });
  assert.equal(started.state, 'AWAITING_CONFIRMATION');
  mode = 'READ';
  assert.equal((await fx.gateway.confirm({ confirmationToken: started.confirmationToken })).code, 'ETP_ACCESS_DENIED');
  mode = 'CONFIRM';
  assert.equal((await fx.gateway.confirm({ confirmationToken: started.confirmationToken })).state, 'ACCEPTED');
  const denied = fixture({ authorize() { throw new Error('authority unavailable'); } });
  assert.equal(denied.gateway.listScopes({ limit: 1 }).code, 'ETP_ACCESS_DENIED');
});

test('import and confirmation accept bounded asynchronous authorization', async () => {
  const approvals = [];
  const fx = fixture({ authorize(action) {
    approvals.push(action);
    return Promise.resolve(action === 'IMPORT' || action === 'CONFIRM');
  } });
  const files = core.REPORTS.map(id => ({ selectedReportId: id, file: { name: id + '.xlsx' } }));
  const started = await fx.gateway.run({ scope, files, coverageConfirmed: true });
  assert.equal(started.state, 'AWAITING_CONFIRMATION');
  assert.equal((await fx.gateway.confirm({ confirmationToken: started.confirmationToken })).state, 'ACCEPTED');
  assert.deepEqual(approvals, ['IMPORT', 'CONFIRM']);
});

test('browser authority admits only Owner or matrix-enabled Store Manager reads', () => {
  assert.match(gatewaySource, /snapshot\.isOwner === true/);
  assert.match(gatewaySource, /snapshot\.role === 'Store Manager'/);
  assert.match(gatewaySource, /rootValue\.roleCanOpen\('etp'\) === true/);
  assert.match(gatewaySource, /action === 'IMPORT' \|\| action === 'CONFIRM'/);
  assert.match(gatewaySource, /rootValue && rootValue\.SaagarReauth/);
  assert.match(gatewaySource, /publish verified Retail ETP reports/);
});

test('verified reads require an accepted unfenced generation matching the valid current receipt', async () => {
  const request = { reportId: 'R025', fields: ['invoice_number', 'net_amount'], cursor: null, limit: 50 };
  const passed = await fixture().gateway.readVerified(scope, request);
  assert.equal(passed.ok, true);
  assert.deepEqual(passed.page.rows, [{ invoice_number: '123', net_amount: 42 }]);

  const fenced = fixture({ statusReader: async () => ({ ok: true, status: { state: 'REIMPORT_REQUIRED', activeGenerationId: generationA, restoreFence: true } }) });
  assert.equal((await fenced.gateway.readVerified(scope, request)).code, 'ETP_REIMPORT_REQUIRED');
  const stale = fixture({ statusReader: async () => ({ ok: true, status: { state: 'ACCEPTED', activeGenerationId: generationB, restoreFence: false } }) });
  assert.equal((await stale.gateway.readVerified(scope, request)).code, 'ETP_VERIFIED_GENERATION_UNAVAILABLE');
  assert.equal((await fixture({ storage: storageWith(null) }).gateway.readVerified(scope, request)).code, 'ETP_RECEIPT_NOT_FOUND');
});

test('R013 exposes bounded invoice identity without widening fields or leaking PII', async () => {
  const requested = [];
  const fx = fixture({ runtime: {
    async run() {}, async confirm() {},
    async readVerified(_scope, request) {
      requested.push(request);
      return { ok: true, page: { scopeKey, generationId: generationA, reportId: request.reportId,
        rows: [{ invoice_number: 'INV-013-1', cro_number: 'CRO-1' }], hasMore: true,
        nextCursor: { chunkIndex: 3, rowOffset: 7 } } };
    }
  } });
  const first = await fx.gateway.readVerified(scope, { reportId: 'R013', fields: ['invoice_number', 'cro_number'], cursor: null, limit: 1 });
  assert.equal(first.ok, true);
  assert.deepEqual(first.page.rows, [{ invoice_number: 'INV-013-1', cro_number: 'CRO-1' }]);
  assert.match(first.page.nextCursor, /^cur_[a-f0-9]{32}_1$/);
  assert.deepEqual(requested[0].fields, ['invoice_number', 'cro_number']);
  const second = await fx.gateway.readVerified(scope, { reportId: 'R013', fields: ['invoice_number', 'cro_number'], cursor: first.page.nextCursor, limit: 1 });
  assert.equal(second.ok, true);
  assert.deepEqual(requested[1].cursor, { chunkIndex: 3, rowOffset: 7 });
  assert.equal((await fx.gateway.readVerified(scope, { reportId: 'R013', fields: ['invoice_number', 'customer_name'], cursor: null, limit: 1 })).code, 'ETP_VERIFIED_PROJECTION_INVALID');
  assert.equal(requested.length, 2);
});

test('verified projections and returned rows fail closed on PII or out-of-contract fields', async () => {
  assert.equal((await fixture().gateway.readVerified(scope, { reportId: 'R025', fields: ['customer_name'], cursor: null, limit: 10 })).code, 'ETP_VERIFIED_PROJECTION_INVALID');
  const leaking = fixture({ runtime: {
    async run() {}, async confirm() {},
    async readVerified(_scope, request) { return { ok: true, page: { scopeKey, generationId: generationA, reportId: request.reportId, rows: [{ net_amount: 42, customer_name: 'Secret' }], hasMore: false, nextCursor: null } }; }
  } });
  assert.equal((await leaking.gateway.readVerified(scope, { reportId: 'R025', fields: ['net_amount'], cursor: null, limit: 10 })).code, 'ETP_GATEWAY_RESPONSE_INVALID');
});

test('scope inspection returns only sanitized current receipt and bounded validated history', async () => {
  const history = Array.from({ length: 14 }, (_, index) => receipt(index % 2 ? generationA : generationB, '2026-04-' + String(30 - index).padStart(2, '0')));
  history.splice(2, 0, { workbook: 'forbidden', customerName: 'Secret' });
  const fx = fixture({ storage: storageWith(receipt(), history) });
  const result = await fx.gateway.inspectScope(scope, { historyLimit: 10 });
  assert.equal(result.ok, true);
  assert.equal(result.history.length, 10);
  assert.equal(result.currentReceipt.exceptions.paymentType25.persisted, false);
  assert.equal(result.status.contractVersion, foundationStatus.VERSION);
  assert.equal(result.status.status, 'READY_WITH_WARNINGS');
  assert.equal('lifecycle' in result.currentReceipt, false);
  assert.equal(JSON.stringify(result).includes('workbook'), false);
  assert.equal(JSON.stringify(result).includes('Secret'), false);
  assert.equal((await fx.gateway.inspectScope(scope, { historyLimit: 11 })).code, 'ETP_HISTORY_LIMIT_INVALID');
});

test('scope listing is bounded, receipt-validated and contains no lifecycle or fact data', () => {
  const result = fixture().gateway.listScopes({ limit: 1 });
  assert.equal(result.ok, true);
  assert.deepEqual(result.scopes, [{ scope: { ...scope, scopeKey }, publishedAt: '2026-05-01', state: 'RECEIPT_PRESENT' }]);
  assert.equal(JSON.stringify(result).includes('lifecycle'), false);
  assert.equal(fixture().gateway.listScopes({ limit: 21 }).code, 'ETP_SCOPE_LIMIT_INVALID');
});
