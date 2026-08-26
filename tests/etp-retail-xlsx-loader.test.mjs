import assert from 'node:assert/strict';
import test from 'node:test';
import { createRequire } from 'node:module';
import { unzipSync, zipSync, strToU8 } from 'fflate';
const require = createRequire(import.meta.url);
const foundation = require('../www/etp-import-foundation.js');
const numeric = require('../www/etp-xlsx-parser-policy.js');
const profile = require('../www/etp-retail-profile.js');
const tableParser = require('../www/etp-retail-table-parser.js');
const loaderFactory = require('../www/etp-retail-xlsx-loader.js');

const datePolicy = { earliestDate: '2024-04-01', asOfDate: '2026-08-08', maxFutureDays: 2 };
function parts(formula = false) {
  return {
    'xl/workbook.xml': strToU8('<workbook><sheets><sheet name="Sheet0" r:id="rId1"/></sheets></workbook>'),
    'xl/_rels/workbook.xml.rels': strToU8('<Relationships><Relationship Id="rId1" Target="worksheets/sheet1.xml"/></Relationships>'),
    'xl/worksheets/sheet1.xml': strToU8(`<worksheet><dimension ref="A1:A2"/><sheetData><c r="A1">${formula ? '<f>1+1</f>' : ''}<v>1</v></c></sheetData></worksheet>`)
  };
}
function container() {
  return zipSync({ '[Content_Types].xml': strToU8('<Types/>'), '_rels/.rels': strToU8('<Relationships/>'),
    'xl/workbook.xml': strToU8('<workbook/>'), 'xl/worksheets/sheet1.xml': strToU8('<worksheet/>') });
}
function rows(reportId = 'R022', store = 'WLMHW') {
  const report = profile.REPORTS[reportId];
  const byHeader = Object.fromEntries(Object.entries(report.fields).map(([raw, output]) => [foundation.normalizeHeader(raw), output]));
  const dropped = new Set(report.dropHeaders.map(foundation.normalizeHeader));
  return [report.exactHeaders.slice(), report.exactHeaders.map((header) => {
    const output = byHeader[foundation.normalizeHeader(header)];
    if (dropped.has(foundation.normalizeHeader(header))) return 'PRIVATE';
    if (output === 'storeCode') return store;
    if (output === 'transactionTypeRaw') return 'INV';
    if (output === 'invoiceDate') return '20260701';
    if (output === 'invoiceNumber') return '0000123';
    if (output === 'itemNumber') return 'ITEM-1';
    if (output === 'croNumber') return 'CRO-1';
    if (report.numericOutputs.includes(output)) return numeric.numericLexical('10.25');
    return 'X';
  })];
}
function makeLoader(readWorkbook, xmlParts = parts()) {
  return loaderFactory.create({ readWorkbook, unzipParts: async () => xmlParts });
}

test('loader preflights XML and passes lexical number parser to the offline candidate', async () => {
  let suppliedParser;
  const loader = makeLoader(async (_bytes, options) => { suppliedParser = options.parseNumber; return [{ sheet: 'Sheet0', data: rows() }]; });
  const result = await loader.load({ bytes: container(), fileLabel: 'Revenue Report.xlsx', expectedStoreCode: 'WLMHW', datePolicy });
  assert.equal(result.ok, true);
  assert.equal(result.code, 'RETAIL_XLSX_ACCEPTED');
  assert.equal(result.sheetName, 'Sheet0');
  assert.equal(result.rows[0].fields.invoiceNumber, '0000123');
  assert.equal(suppliedParser('1.2300').lexical, '1.2300');
});

test('formula-bearing XML fails before workbook parsing', async () => {
  let called = false;
  const loader = makeLoader(async () => { called = true; return []; }, parts(true));
  const result = await loader.load({ bytes: container() });
  assert.equal(result.code, 'XLSX_FORMULA_FORBIDDEN');
  assert.equal(called, false);
});

test('loader finds exactly one approved data sheet and refuses ambiguity', async () => {
  const none = makeLoader(async () => [{ sheet: 'Info', data: [['NOT', 'A', 'REPORT']] }]);
  assert.equal((await none.load({ bytes: container(), fileLabel: 'Revenue Report.xlsx' })).code, 'XLSX_APPROVED_DATA_SHEET_NOT_FOUND');
  const duplicate = makeLoader(async () => [{ sheet: 'One', data: rows() }, { sheet: 'Two', data: rows() }]);
  assert.equal((await duplicate.load({ bytes: container(), fileLabel: 'Revenue Report.xlsx' })).code, 'XLSX_APPROVED_DATA_SHEET_AMBIGUOUS');
});

test('approved numeric measures preserve lexical decimal text', () => {
  const result = tableParser.parse({ rows: rows(), fileLabel: 'Revenue Report.xlsx', expectedStoreCode: 'WLMHW', datePolicy });
  assert.equal(result.ok, true);
  assert.equal(result.rows[0].fields.netValue, '10.25');
});

test('exact integer lexical identifiers canonicalize while fractions and numeric context remain rejected', () => {
  const identifierRows = rows();
  identifierRows[1][identifierRows[0].findIndex((header) => foundation.normalizeHeader(header) === 'INVNUMBER')] = numeric.numericLexical('123');
  assert.equal(tableParser.parse({ rows: identifierRows, fileLabel: 'Revenue Report.xlsx', expectedStoreCode: 'WLMHW', datePolicy }).rows[0].fields.invoiceNumber, '123');
  const decimalRows = rows();
  decimalRows[1][decimalRows[0].findIndex((header) => foundation.normalizeHeader(header) === 'INVNUMBER')] = numeric.numericLexical('123.0');
  assert.equal(tableParser.parse({ rows: decimalRows, fileLabel: 'Revenue Report.xlsx', expectedStoreCode: 'WLMHW', datePolicy }).rows[0].fields.invoiceNumber, '123');
  const ambiguousRows = rows();
  ambiguousRows[1][ambiguousRows[0].findIndex((header) => foundation.normalizeHeader(header) === 'INVNUMBER')] = numeric.numericLexical('123.5');
  assert.equal(tableParser.parse({ rows: ambiguousRows, fileLabel: 'Revenue Report.xlsx', expectedStoreCode: 'WLMHW', datePolicy }).code, 'XLSX_IDENTIFIER_NUMERIC_UNVERIFIED');
  const contextRows = rows();
  contextRows[1][contextRows[0].findIndex((header) => foundation.normalizeHeader(header) === 'STORE_NAME')] = numeric.numericLexical('123');
  assert.equal(tableParser.parse({ rows: contextRows, fileLabel: 'Revenue Report.xlsx', expectedStoreCode: 'WLMHW', datePolicy }).code, 'XLSX_NUMERIC_FIELD_UNAPPROVED');
});

test('unsafe container refuses before decompression or parsing', async () => {
  let called = false;
  const loader = loaderFactory.create({ readWorkbook: async () => { called = true; }, unzipParts: async () => { called = true; } });
  const result = await loader.load({ bytes: new Uint8Array([1, 2, 3]) });
  assert.equal(result.code, 'XLSX_SIGNATURE_INVALID');
  assert.equal(called, false);
});
