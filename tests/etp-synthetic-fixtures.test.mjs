import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { createRequire } from 'node:module';
import readXlsxFile from 'read-excel-file/node';
import { unzipSync } from 'fflate';
import { buildFixtureSet, FIXTURE_CONTRACT, writeFixtureSet } from '../scripts/generate-etp-synthetic-fixtures.mjs';

const require = createRequire(import.meta.url);
const profile = require('../www/etp-retail-profile.js');
const loaderApi = require('../www/etp-retail-xlsx-loader.js');
const core = require('../www/etp-core-contract.js');
const reconciliation = require('../www/etp-reconciliation-policy.js');
const loader = loaderApi.create({
  readWorkbook: (bytes, options) => readXlsxFile(Buffer.from(bytes), options),
  unzipParts: (bytes) => unzipSync(bytes)
});
const datePolicy = { earliestDate: '2024-04-01', asOfDate: '2026-08-21', maxFutureDays: 2 };

function sha256(bytes) {
  return crypto.createHash('sha256').update(bytes).digest('hex');
}

async function load(entry, storeCode = 'WLMHW') {
  return loader.load({ bytes: entry.bytes, fileLabel: entry.filename, selectedReportId: entry.reportId,
    expectedStoreCode: storeCode, datePolicy });
}

test('synthetic fixture bytes and manifest are deterministic and stay outside tracked source', () => {
  const first = buildFixtureSet();
  const second = buildFixtureSet();
  assert.deepEqual(first.happy.map((entry) => [entry.filename, entry.sha256]),
    second.happy.map((entry) => [entry.filename, entry.sha256]));
  for (let index = 0; index < first.happy.length; index += 1) {
    assert.deepEqual(first.happy[index].bytes, second.happy[index].bytes);
    assert.equal(sha256(first.happy[index].bytes), first.happy[index].sha256);
  }
  assert.deepEqual(first.negative.bytes, second.negative.bytes);

  const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'saagar-etp-fixtures-'));
  try {
    const written = writeFixtureSet(temp);
    assert.equal(written.manifest.format, 'SAAGAR_ETP_SYNTHETIC_FIXTURES_V1');
    assert.equal(written.manifest.fixtureContract, 'synthetic-only; never production evidence');
    assert.equal(written.manifest.happyPath.length, 4);
    for (const entry of written.manifest.happyPath) {
      assert.equal(sha256(fs.readFileSync(path.join(temp, entry.relativePath))), entry.sha256);
    }
  } finally {
    fs.rmSync(temp, { recursive: true, force: true });
  }
});

test('four happy XLSX files use the shipping profiles and pass production parsing and reconciliation', async () => {
  const set = buildFixtureSet();
  assert.deepEqual(Object.fromEntries(Object.entries(profile.REPORTS).map(([id, report]) => [id, report.exactHeaders.length])),
    FIXTURE_CONTRACT.EXPECTED_COLUMNS);
  const reports = {};
  for (const entry of set.happy) {
    const parsed = await load(entry);
    assert.equal(parsed.ok, true, `${entry.reportId}: ${parsed.code}`);
    assert.equal(parsed.reportId, entry.reportId);
    assert.equal(parsed.storeCode, 'WLMHW');
    assert.equal(parsed.rowCount, 1);
    assert.equal(parsed.container.ok, true);
    assert.equal(parsed.xml.ok, true);
    assert.equal(JSON.stringify(parsed.rows).includes(FIXTURE_CONTRACT.PII_CANARY), false);
    reports[entry.reportId] = { ...parsed, sourceSha256: entry.sha256 };
  }
  assert.equal(reports.R022.rows[0].fields.paymentType25Amount, '0.00');

  const coverage = core.coverage({ storeCode: set.scope.storeCode, financialYear: set.scope.financialYear,
    periodStart: set.scope.businessDate, periodEnd: set.scope.businessDate }, reports, { confirmed: true, confirmedByRole: 'OWNER',
    reports: Object.fromEntries(FIXTURE_CONTRACT.REPORTS.map((id) => [id, { status: 'COMPLETE' }])) });
  assert.equal(coverage.ok, true);
  const facts = (id) => reports[id].rows.map((row) => row.fields);
  assert.equal(reconciliation.compareReports(facts('R022'), facts('R025'), core.RECON_RULE,
    { left: coverage.coverage.R022, right: coverage.coverage.R025 }).status, 'PASS');
  assert.equal(reconciliation.compareReports(facts('R013'), facts('R025'), core.ATTRIBUTION_RULE,
    { left: coverage.coverage.R013, right: coverage.coverage.R025 }).status, 'PASS');
  assert.equal(reconciliation.compareReports(facts('R003'), facts('R025'), core.DISCOUNT_RULE,
    { left: coverage.coverage.R003, right: coverage.coverage.R025 }).status, 'PASS');
});

test('blocking negative changes only R025 economics and REC-002 fails closed', async () => {
  const set = buildFixtureSet();
  const happyR022 = await load(set.happy.find((entry) => entry.reportId === 'R022'));
  const mismatchR025 = await load(set.negative);
  assert.equal(happyR022.ok, true);
  assert.equal(mismatchR025.ok, true);
  const coverage = {
    left: { status: 'COMPLETE', periodStart: set.scope.businessDate, declaredPeriodEnd: set.scope.businessDate, evidenceId: 'synthetic-r022' },
    right: { status: 'COMPLETE', periodStart: set.scope.businessDate, declaredPeriodEnd: set.scope.businessDate, evidenceId: 'synthetic-r025-negative' }
  };
  const result = reconciliation.compareReports(happyR022.rows.map((row) => row.fields),
    mismatchR025.rows.map((row) => row.fields), core.RECON_RULE, coverage);
  assert.equal(result.status, 'FAIL');
  assert.equal(result.code, 'RECON_MISMATCH');
  assert.deepEqual(result.differences.map((difference) => difference.measure), ['netAmount']);
});
