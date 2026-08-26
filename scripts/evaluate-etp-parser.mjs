#!/usr/bin/env node
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { createRequire } from 'node:module';
import readXlsxFile from 'read-excel-file/node';
import parserPackage from 'read-excel-file/package.json' with { type: 'json' };
const require = createRequire(import.meta.url);
const preflight = require('../www/etp-xlsx-preflight.js');

const sourceDir = path.resolve(process.argv[2] || '');
if (!sourceDir || !fs.statSync(sourceDir).isDirectory()) {
  throw new Error('Usage: node scripts/evaluate-etp-parser.mjs <private-fixture-directory>');
}

const REPORTS = Object.freeze([
  { id: 'R022', match: /Revenue_Report/i, expectedRows: 4398, expectedColumns: 46, date: 'INVOICEDATE', identifiers: ['INVNUMBER'] },
  { id: 'R025', match: /SDB-VariantwiseSales/i, expectedRows: 5065, expectedColumns: 41, date: 'INVDATE', identifiers: ['INVNUMBER', 'ITEMNUMBER'] },
  { id: 'R013', match: /CRO_Wise_Sales/i, expectedRows: 5065, expectedColumns: 28, date: 'INVDATE', identifiers: ['INVNUMBER', 'CRO_NUMBER'] },
  { id: 'R003', match: /All_Discount_Type/i, expectedRows: 5150, expectedColumns: 34, date: 'INVOICE_DATE', identifiers: ['INVOICE_NUMBER'] }
]);

function header(value) {
  return String(value == null ? '' : value).trim().toUpperCase().replace(/[^A-Z0-9]+/g, '_').replace(/^_+|_+$/g, '');
}
function dateText(value) {
  if (value instanceof Date && Number.isFinite(value.getTime())) return value.toISOString().slice(0, 10).replace(/-/g, '');
  return String(value == null ? '' : value).trim().replace(/-/g, '');
}

const files = fs.readdirSync(sourceDir).filter((name) => /\.xlsx$/i.test(name));
const results = [];
for (const report of REPORTS) {
  const name = files.find((candidate) => report.match.test(candidate));
  if (!name) throw new Error(`Private fixture missing for ${report.id}`);
  const file = path.join(sourceDir, name);
  const bytes = fs.readFileSync(file);
  const container = preflight.inspect(bytes);
  if (!container.ok) throw new Error(`Preflight refused ${report.id}: ${container.code}`);
  const before = process.memoryUsage();
  const started = performance.now();
  const sheets = await readXlsxFile(bytes);
  const elapsedMs = performance.now() - started;
  const after = process.memoryUsage();
  if (!Array.isArray(sheets) || sheets.length !== 1 || !Array.isArray(sheets[0].data)) {
    throw new Error(`Expected exactly one readable sheet for ${report.id}`);
  }
  const rows = sheets[0].data;
  const headers = (rows[0] || []).map(header);
  const index = Object.fromEntries(headers.map((value, position) => [value, position]));
  let numericIdentifierCells = 0;
  let unexpectedStoreCells = 0;
  let unknownTransactionCells = 0;
  let earliest = '';
  let latest = '';
  for (let rowIndex = 1; rowIndex < rows.length; rowIndex += 1) {
    const row = rows[rowIndex] || [];
    for (const identifier of report.identifiers) {
      if (Number.isInteger(index[identifier]) && typeof row[index[identifier]] === 'number') numericIdentifierCells += 1;
    }
    if (Number.isInteger(index.STORE_CODE) && String(row[index.STORE_CODE] || '').trim().toUpperCase() !== 'WLMHW') unexpectedStoreCells += 1;
    if (Number.isInteger(index.TRANS_TYPE) && !/^(?:INV|SR|BC)$/.test(String(row[index.TRANS_TYPE] || '').trim().toUpperCase())) unknownTransactionCells += 1;
    if (Number.isInteger(index[report.date])) {
      const value = dateText(row[index[report.date]]);
      if (/^\d{8}$/.test(value)) { if (!earliest || value < earliest) earliest = value; if (!latest || value > latest) latest = value; }
    }
  }
  const dataRows = Math.max(0, rows.length - 1);
  results.push({
    reportId: report.id,
    sourceBytes: bytes.length,
    sourceSha256: crypto.createHash('sha256').update(bytes).digest('hex'),
    preflightCode: container.code,
    zipEntryCount: container.entryCount,
    declaredUncompressedBytes: container.declaredUncompressedBytes,
    rows: dataRows,
    columns: headers.length,
    structuralMatch: dataRows === report.expectedRows && headers.length === report.expectedColumns,
    headerSignatureSha256: crypto.createHash('sha256').update([...headers].sort().join('\u001f')).digest('hex'),
    earliestBusinessDate: earliest,
    latestBusinessDate: latest,
    numericIdentifierCells,
    unexpectedStoreCells,
    unknownTransactionCells,
    elapsedMs: Math.round(elapsedMs),
    heapDeltaBytes: after.heapUsed - before.heapUsed,
    rssDeltaBytes: after.rss - before.rss
  });
}

const evidence = {
  evaluatedAt: new Date().toISOString(),
  candidate: { name: parserPackage.name, version: parserPackage.version, license: parserPackage.license },
  environment: { node: process.version, platform: process.platform, arch: process.arch },
  privacy: 'aggregate-metadata-only; no filenames, sheets, headers, cells or row values emitted',
  results,
  passed: results.length === REPORTS.length && results.every((item) => item.structuralMatch && item.numericIdentifierCells === 0 && item.unexpectedStoreCells === 0 && item.unknownTransactionCells === 0),
  disposition: results.some((item) => item.numericIdentifierCells > 0)
    ? 'BLOCKED_NUMERIC_IDENTIFIERS'
    : 'STRUCTURAL_EVALUATION_PASSED'
};
process.stdout.write(`${JSON.stringify(evidence, null, 2)}\n`);
if (!evidence.passed) process.exitCode = 1;
