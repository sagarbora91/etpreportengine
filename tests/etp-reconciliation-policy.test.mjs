import assert from 'node:assert/strict';
import test from 'node:test';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const policy = require('../www/etp-reconciliation-policy.js');

const rule = {
  ruleId: 'REC_002', ruleVersion: 'synthetic_v1', owner: 'Synthetic test only',
  label: 'Synthetic R022 to R025', severity: 'CRITICAL',
  sourceReports: { left: 'R022', right: 'R025' },
  keys: [
    { name: 'store', leftField: 'storeCode', rightField: 'storeCode' },
    { name: 'date', leftField: 'businessDate', rightField: 'businessDate' },
    { name: 'invoice', leftField: 'invoiceNumber', rightField: 'invoiceNumber' }
  ],
  transaction: { leftField: 'transactionType', rightField: 'transactionType', signs: { INV: 1, SR: -1, BC: -1 } },
  measures: [
    { name: 'quantity', leftField: 'quantity', rightField: 'quantity', scale: 0, toleranceUnits: 0 },
    { name: 'netValue', leftField: 'netValue', rightField: 'netValue', scale: 2, toleranceUnits: 0 }
  ],
  filters: []
};
const coverage = {
  left: { status: 'COMPLETE', periodStart: '2026-07-01', declaredPeriodEnd: '2026-07-31', evidenceId: 'left-1' },
  right: { status: 'COMPLETE', periodStart: '2026-07-01', declaredPeriodEnd: '2026-07-31', evidenceId: 'right-1' }
};

test('rule contract refuses omitted owner-controlled grain, measures, signs and tolerance', () => {
  const out = policy.validateRuleDefinition({ ruleId: 'REC_002' });
  assert.equal(out.ok, false);
  for (const code of ['RULE_VERSION_INVALID', 'RULE_OWNER_REQUIRED', 'SOURCE_REPORTS_INVALID', 'GRAIN_REQUIRED', 'SIGN_POLICY_INVALID', 'MEASURES_REQUIRED', 'FILTERS_REQUIRED']) assert.ok(out.errors.some((item) => item.code === code), code);
});

test('rule contract accepts an explicit synthetic definition without adding defaults', () => {
  const out = policy.validateRuleDefinition(rule);
  assert.equal(out.ok, true);
  assert.deepEqual(out.rule.measures.map(({ scale, toleranceUnits }) => ({ scale, toleranceUnits })), [{ scale: 0, toleranceUnits: 0 }, { scale: 2, toleranceUnits: 0 }]);
});

test('R025 lines aggregate to supplied common grain and reconcile to R022', () => {
  const left = [{ storeCode: 'WLMHW', businessDate: '2026-07-01', invoiceNumber: '0001', transactionType: 'INV', quantity: '2', netValue: '100.00' }];
  const right = [
    { storeCode: 'WLMHW', businessDate: '2026-07-01', invoiceNumber: '0001', transactionType: 'INV', quantity: '1', netValue: '40.00' },
    { storeCode: 'WLMHW', businessDate: '2026-07-01', invoiceNumber: '0001', transactionType: 'INV', quantity: '1', netValue: '60.00' }
  ];
  const out = policy.compareReports(left, right, rule, coverage);
  assert.equal(out.status, 'PASS');
  assert.deepEqual(out.differences, []);
});

test('INV/SR/BC signs are applied and a mismatch remains visible', () => {
  const left = [{ storeCode: 'WLMHW', businessDate: '2026-07-01', invoiceNumber: '0002', transactionType: 'SR', quantity: '1', netValue: '25.00' }];
  const right = [{ storeCode: 'WLMHW', businessDate: '2026-07-01', invoiceNumber: '0002', transactionType: 'SR', quantity: '1', netValue: '24.99' }];
  const out = policy.compareReports(left, right, rule, coverage);
  assert.equal(out.status, 'FAIL');
  assert.equal(out.differences[0].measure, 'netValue');
  assert.equal(out.differences[0].deltaUnits, -1);
});

test('numeric identifiers and unknown transaction types fail closed', () => {
  const rows = [{ storeCode: 'WLMHW', businessDate: '2026-07-01', invoiceNumber: 1, transactionType: 'OTHER', quantity: '1', netValue: '10.00' }];
  const out = policy.aggregateReportRows('R022', rows, rule);
  assert.equal(out.ok, false);
  assert.ok(out.errors.some((item) => item.code === 'GRAIN_VALUE_INVALID'));
  assert.ok(out.errors.some((item) => item.code === 'TRANSACTION_TYPE_UNKNOWN'));
});

test('coverage distinguishes confirmed zero activity from incomplete scope', () => {
  const zero = policy.evaluateCoverage({ status: 'COMPLETE_WITH_ZERO_ACTIVITY', periodStart: '2026-07-01', declaredPeriodEnd: '2026-07-31', evidenceId: 'signed-1', zeroActivityConfirmed: true });
  assert.equal(zero.complete, true);
  const unsigned = policy.evaluateCoverage({ status: 'COMPLETE_WITH_ZERO_ACTIVITY', periodStart: '2026-07-01', declaredPeriodEnd: '2026-07-31', evidenceId: 'unsigned' });
  assert.equal(unsigned.complete, false);
  assert.ok(unsigned.errors.some((item) => item.code === 'ZERO_ACTIVITY_CONFIRMATION_REQUIRED'));
  assert.equal(policy.evaluateCoverage({ status: 'INTERNAL_GAP', periodStart: '2026-07-01', declaredPeriodEnd: '2026-07-31', evidenceId: 'gap-1' }).complete, false);
});

test('incomplete coverage and mismatched cut-offs block rather than fail reconciliation', () => {
  const incomplete = policy.compareReports([], [], rule, { ...coverage, right: { ...coverage.right, status: 'INTERNAL_GAP' } });
  assert.equal(incomplete.status, 'BLOCKED');
  assert.equal(incomplete.code, 'RECON_INPUT_INCOMPLETE');
  const mismatch = policy.compareReports([], [], rule, { ...coverage, right: { ...coverage.right, declaredPeriodEnd: '2026-07-30' } });
  assert.equal(mismatch.status, 'BLOCKED');
  assert.equal(mismatch.code, 'CUTOFF_MISMATCH');
});

test('publication refuses missing facts, restored state, incomplete scope and critical failures', () => {
  const out = policy.publicationDecision({
    storeCode: 'WLMHW', factStoreAvailable: false, reimportRequired: true,
    coverages: [{ status: 'PARTIAL_END', periodStart: '2026-07-01', declaredPeriodEnd: '2026-07-31', evidenceId: 'partial' }],
    reconciliations: [{ status: 'FAIL', severity: 'CRITICAL' }]
  });
  assert.equal(out.status, 'NOT_READY');
  assert.equal(out.showValues, false);
  for (const code of ['FACT_STORE_UNAVAILABLE', 'REIMPORT_REQUIRED', 'REQUIRED_SCOPE_INCOMPLETE', 'CRITICAL_RECONCILIATION_NOT_PASSED']) assert.ok(out.reasons.includes(code), code);
});

test('publication permits complete scope only after critical reconciliation passes', () => {
  const out = policy.publicationDecision({
    storeCode: 'WLMHW', factStoreAvailable: true, reimportRequired: false, storeAmbiguous: false, piiPolicyViolation: false,
    coverages: [coverage.left, coverage.right], reconciliations: [{ status: 'PASS', severity: 'CRITICAL' }]
  });
  assert.deepEqual(out, { status: 'READY', showValues: true, reasons: [], warnings: [] });
});
