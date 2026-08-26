#!/usr/bin/env node
/* Deterministic, synthetic-only Retail ETP XLSX fixtures.
   The exact headers come from the shipping profile; no production rows or PII
   are read. Generated binaries belong in the ignored output directory. */
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
import { strToU8, zipSync } from 'fflate';

const require = createRequire(import.meta.url);
const profile = require('../www/etp-retail-profile.js');
const foundation = require('../www/etp-import-foundation.js');

const REPORTS = Object.freeze(['R003', 'R013', 'R022', 'R025']);
const EXPECTED_COLUMNS = Object.freeze({ R003: 34, R013: 28, R022: 46, R025: 41 });
const ALIASES = Object.freeze({
  R003: 'All_Discount_Type',
  R013: 'CRO_Wise_Sales',
  R022: 'Revenue_Report',
  R025: 'SDB-VariantwiseSales'
});
const FIXED_ZIP_MTIME = new Date('2000-01-01T00:00:00.000Z');
const DEFAULT_DATE = '2026-08-21';
const PII_CANARY = 'SYNTHETIC_ONLY_NOT_A_REAL_PERSON';

function sha256(bytes) {
  return crypto.createHash('sha256').update(bytes).digest('hex');
}

function xml(value) {
  return String(value).replace(/&/g, '&amp;').replace(/</g, '&lt;')
    .replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&apos;');
}

function columnName(index) {
  let value = index + 1;
  let result = '';
  while (value) {
    value -= 1;
    result = String.fromCharCode(65 + value % 26) + result;
    value = Math.floor(value / 26);
  }
  return result;
}

function financialYear(isoDate) {
  const year = Number(isoDate.slice(0, 4));
  const start = Number(isoDate.slice(5, 7)) >= 4 ? year : year - 1;
  return `${start}-${String((start + 1) % 100).padStart(2, '0')}`;
}

function validateOptions(options = {}) {
  const storeCode = String(options.storeCode || 'WLMHW').toUpperCase();
  const businessDate = String(options.businessDate || DEFAULT_DATE);
  if (!profile.STORES.includes(storeCode)) throw new Error('storeCode must be WLMHW or HEMW');
  const parsedDate = /^\d{4}-\d{2}-\d{2}$/.test(businessDate)
    ? new Date(`${businessDate}T00:00:00.000Z`) : null;
  if (!parsedDate || Number.isNaN(parsedDate.getTime()) || parsedDate.toISOString().slice(0, 10) !== businessDate) {
    throw new Error('businessDate must be a real YYYY-MM-DD date');
  }
  for (const reportId of REPORTS) {
    if (!profile.REPORTS[reportId] || profile.REPORTS[reportId].exactHeaders.length !== EXPECTED_COLUMNS[reportId]) {
      throw new Error(`shipping profile changed for ${reportId}; review the fixture contract`);
    }
  }
  return { storeCode, businessDate, compactDate: businessDate.replace(/-/g, ''), financialYear: financialYear(businessDate) };
}

function valueFor(output, report, scope, mismatch) {
  if (output === 'storeCode') return { kind: 'text', value: scope.storeCode };
  if (output === 'transactionTypeRaw') return { kind: 'text', value: 'INV' };
  if (/invoiceDate$/i.test(output || '')) return { kind: 'text', value: scope.compactDate };
  if (/invoiceRefDate$/i.test(output || '')) return { kind: 'text', value: scope.compactDate };
  if (output === 'invoiceNumber') return { kind: 'text', value: '0000123' };
  if (output === 'itemNumber') return { kind: 'text', value: 'ITEM-0001' };
  if (output === 'croNumber') return { kind: 'text', value: 'CRO-0001' };
  if (output === 'invoiceYear' || output === 'referenceYear') return { kind: 'text', value: scope.businessDate.slice(0, 4) };
  if (/timestamp$/i.test(output || '')) return { kind: 'text', value: `${scope.compactDate}000000` };
  if (/referenceNumber$/i.test(output || '') || /invoiceRefNumber$/i.test(output || '')) return { kind: 'text', value: 'SYNTH-REF-0001' };
  if (report.numericOutputs.includes(output)) {
    if (output === 'invoiceQuantity' || output === 'quantity') return { kind: 'number', value: '1.000' };
    if (output === 'netAmount') return { kind: 'number', value: mismatch ? '102.01' : '100.00' };
    if (output === 'netValue') return { kind: 'number', value: '100.00' };
    return { kind: 'number', value: '0.00' };
  }
  if (/(?:number|year|code|timestamp)$/i.test(output || '')) return { kind: 'text', value: 'SYNTH-0001' };
  return { kind: 'text', value: 'SYNTHETIC_ONLY' };
}

function rowFor(reportId, scope, mismatch = false) {
  const report = profile.REPORTS[reportId];
  const outputByHeader = Object.fromEntries(Object.entries(report.fields)
    .map(([raw, output]) => [foundation.normalizeHeader(raw), output]));
  const dropped = new Set(report.dropHeaders.map(foundation.normalizeHeader));
  return report.exactHeaders.map((header) => dropped.has(foundation.normalizeHeader(header))
    ? { kind: 'text', value: PII_CANARY }
    : valueFor(outputByHeader[foundation.normalizeHeader(header)], report, scope, mismatch));
}

function cell(reference, value) {
  if (value.kind === 'number') return `<c r="${reference}"><v>${xml(value.value)}</v></c>`;
  return `<c r="${reference}" t="inlineStr"><is><t>${xml(value.value)}</t></is></c>`;
}

function workbookBytes(reportId, scope, mismatch = false) {
  const report = profile.REPORTS[reportId];
  const headers = report.exactHeaders.map((value) => ({ kind: 'text', value }));
  const rows = [headers, rowFor(reportId, scope, mismatch)];
  const lastColumn = columnName(headers.length - 1);
  const sheetRows = rows.map((row, rowIndex) => `<row r="${rowIndex + 1}">${row.map((value, columnIndex) =>
    cell(`${columnName(columnIndex)}${rowIndex + 1}`, value)).join('')}</row>`).join('');
  const worksheet = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><dimension ref="A1:${lastColumn}2"/><sheetData>${sheetRows}</sheetData></worksheet>`;
  const entries = {
    '[Content_Types].xml': strToU8('<?xml version="1.0"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/sharedStrings.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/></Types>'),
    '_rels/.rels': strToU8('<?xml version="1.0"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>'),
    'xl/workbook.xml': strToU8('<?xml version="1.0"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Import" sheetId="1" r:id="rId1"/></sheets></workbook>'),
    'xl/_rels/workbook.xml.rels': strToU8('<?xml version="1.0"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings" Target="sharedStrings.xml"/></Relationships>'),
    'xl/sharedStrings.xml': strToU8('<?xml version="1.0" encoding="UTF-8"?><sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="0" uniqueCount="0"></sst>'),
    'xl/worksheets/sheet1.xml': strToU8(worksheet)
  };
  return zipSync(entries, { level: 0, mtime: FIXED_ZIP_MTIME });
}

function filename(reportId, storeCode) {
  const prefix = storeCode === 'HEMW' ? 'H' : 'W';
  return `${prefix}${reportId.slice(1)}_${ALIASES[reportId]}.xlsx`;
}

export function buildFixtureSet(options = {}) {
  const scope = validateOptions(options);
  const happy = REPORTS.map((reportId) => {
    const bytes = workbookBytes(reportId, scope, false);
    return Object.freeze({ reportId, filename: filename(reportId, scope.storeCode), bytes,
      sha256: sha256(bytes), rows: 1, columns: EXPECTED_COLUMNS[reportId] });
  });
  const mismatchBytes = workbookBytes('R025', scope, true);
  const negative = Object.freeze({ reportId: 'R025', filename: filename('R025', scope.storeCode),
    bytes: mismatchBytes, sha256: sha256(mismatchBytes), rows: 1, columns: EXPECTED_COLUMNS.R025,
    expectedResult: 'REC-002 RECON_MISMATCH (netAmount exceeds Rs 1 tolerance)' });
  return Object.freeze({ scope: Object.freeze(scope), happy: Object.freeze(happy), negative, piiCanary: PII_CANARY });
}

function manifestFor(set) {
  const describe = (entry, relativePath) => ({ reportId: entry.reportId, relativePath,
    sha256: entry.sha256, bytes: entry.bytes.length, rows: entry.rows, columns: entry.columns });
  return {
    format: 'SAAGAR_ETP_SYNTHETIC_FIXTURES_V1',
    fixtureContract: 'synthetic-only; never production evidence',
    deterministicEpoch: FIXED_ZIP_MTIME.toISOString(),
    scope: { storeCode: set.scope.storeCode, financialYear: set.scope.financialYear,
      periodStart: set.scope.businessDate, periodEnd: set.scope.businessDate },
    happyPath: set.happy.map((entry) => describe(entry, `happy/${entry.filename}`)),
    blockingNegative: { ...describe(set.negative, `negative-rec002/${set.negative.filename}`),
      expectedResult: set.negative.expectedResult }
  };
}

export function writeFixtureSet(outputDirectory, options = {}) {
  const set = buildFixtureSet(options);
  const resolved = path.resolve(outputDirectory);
  fs.mkdirSync(path.join(resolved, 'happy'), { recursive: true });
  fs.mkdirSync(path.join(resolved, 'negative-rec002'), { recursive: true });
  for (const entry of set.happy) fs.writeFileSync(path.join(resolved, 'happy', entry.filename), entry.bytes);
  fs.writeFileSync(path.join(resolved, 'negative-rec002', set.negative.filename), set.negative.bytes);
  const manifest = manifestFor(set);
  fs.writeFileSync(path.join(resolved, 'manifest.json'), `${JSON.stringify(manifest, null, 2)}\n`);
  return { outputDirectory: resolved, manifest };
}

function parseArguments(argv) {
  const result = {};
  for (let index = 0; index < argv.length; index += 1) {
    const value = argv[index];
    if (value === '--output') result.output = argv[++index];
    else if (value === '--store') result.storeCode = argv[++index];
    else if (value === '--date') result.businessDate = argv[++index];
    else throw new Error(`unknown argument: ${value}`);
  }
  return result;
}

const invokedPath = process.argv[1] ? path.resolve(process.argv[1]) : '';
if (invokedPath === fileURLToPath(import.meta.url)) {
  const args = parseArguments(process.argv.slice(2));
  const defaultOutput = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '.tmp-etp-synthetic-fixtures');
  const written = writeFixtureSet(args.output || defaultOutput, args);
  process.stdout.write(`${JSON.stringify({ ok: true, outputDirectory: written.outputDirectory,
    manifest: path.join(written.outputDirectory, 'manifest.json') })}\n`);
}

export const FIXTURE_CONTRACT = Object.freeze({ REPORTS, EXPECTED_COLUMNS, DEFAULT_DATE, PII_CANARY });
