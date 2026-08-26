import assert from 'node:assert/strict';
import test from 'node:test';
import { createRequire } from 'node:module';
import { zipSync, strToU8 } from 'fflate';
const require = createRequire(import.meta.url);
const policy = require('../www/etp-xlsx-preflight.js');

function workbook(extra = {}) {
  return zipSync({
    '[Content_Types].xml': strToU8('<Types/>'),
    '_rels/.rels': strToU8('<Relationships/>'),
    'xl/workbook.xml': strToU8('<workbook/>'),
    'xl/worksheets/sheet1.xml': strToU8('<worksheet/>'),
    ...extra
  }, { level: 6 });
}
test('bounded classic OOXML container is admitted without exposing entry names', () => {
  const out = policy.inspect(workbook());
  assert.equal(out.ok, true);
  assert.equal(out.entryCount, 4);
  assert.doesNotMatch(JSON.stringify(out), /workbook|sheet/i);
});
test('invalid signature and missing required parts fail closed', () => {
  assert.equal(policy.inspect(new Uint8Array([1, 2, 3])).code, 'XLSX_SIGNATURE_INVALID');
  assert.equal(policy.inspect(zipSync({ 'a.txt': strToU8('x') })).code, 'XLSX_REQUIRED_PART_MISSING');
});
test('path traversal and active content are rejected before parsing', () => {
  assert.equal(policy.inspect(workbook({ '../private.xml': strToU8('x') })).code, 'XLSX_PATH_UNSAFE');
  assert.equal(policy.inspect(workbook({ 'xl/vbaProject.bin': strToU8('x') })).code, 'XLSX_ACTIVE_CONTENT_FORBIDDEN');
  assert.equal(policy.inspect(workbook({ 'xl/externalLinks/externalLink1.xml': strToU8('x') })).code, 'XLSX_ACTIVE_CONTENT_FORBIDDEN');
});
test('compression bombs are rejected from declared metadata', () => {
  const bomb = workbook({ 'xl/sharedStrings.xml': new Uint8Array(2 * 1024 * 1024) });
  assert.equal(policy.inspect(bomb).code, 'XLSX_COMPRESSION_RATIO_EXCEEDED');
});
test('profile publishes fixed API-23 resource ceilings', () => {
  assert.deepEqual(policy.LIMITS, { profileVersion: 1, maxInputBytes: 33554432, maxEntries: 512, maxEntryBytes: 33554432, maxTotalBytes: 134217728, maxCompressionRatio: 100, maxSheets: 8, maxRowsPerSheet: 250000, maxColumns: 128, maxCells: 2000000, maxCellText: 4096, maxSharedStrings: 250000, maxSharedStringsBytes: 16777216 });
});

function xmlParts(overrides = {}) {
  return {
    'xl/workbook.xml': '<workbook><sheets><sheet name="Data" sheetId="1" r:id="rId1"/></sheets></workbook>',
    'xl/_rels/workbook.xml.rels': '<Relationships><Relationship Id="rId1" Target="worksheets/sheet1.xml"/></Relationships>',
    'xl/worksheets/sheet1.xml': '<worksheet><dimension ref="A1:B2"/><sheetData><row r="1"><c r="A1"><v>1</v></c></row></sheetData></worksheet>',
    ...overrides
  };
}
test('decompressed XML inspection admits a single bounded visible value-only sheet', () => {
  assert.deepEqual(policy.inspectParts(xmlParts()), { ok: true, code: 'XLSX_XML_ACCEPTED', stage: 'xml', profileVersion: 1, sheetCount: 1, cellCount: 1 });
});
test('namespaced OOXML and worksheets without optional dimensions are admitted', () => {
  const out = policy.inspectParts({
    'xl/workbook.xml': '<x:workbook><x:sheets><x:sheet name="Data" r:id="rId1"/></x:sheets></x:workbook>',
    'xl/_rels/workbook.xml.rels': '<Relationships><Relationship Id="rId1" Target="worksheets/sheet1.xml"/></Relationships>',
    'xl/worksheets/sheet1.xml': '<x:worksheet><x:sheetData><x:c r="A1"><x:v>1</x:v></x:c></x:sheetData></x:worksheet>'
  });
  assert.equal(out.code, 'XLSX_XML_ACCEPTED');
  assert.equal(out.cellCount, 1);
});
test('DTD, entities, external relationships and formulas fail closed', () => {
  assert.equal(policy.inspectParts(xmlParts({ 'xl/workbook.xml': '<!DOCTYPE x [<!ENTITY e SYSTEM "file:///private">]><workbook/>' })).code, 'XLSX_XML_DTD_FORBIDDEN');
  assert.equal(policy.inspectParts(xmlParts({ 'xl/_rels/workbook.xml.rels': '<Relationships><Relationship Id="rId1" Target="https://example.invalid/x" TargetMode="External"/></Relationships>' })).code, 'XLSX_EXTERNAL_RELATIONSHIP_FORBIDDEN');
  assert.equal(policy.inspectParts(xmlParts({ 'xl/worksheets/sheet1.xml': '<worksheet><dimension ref="A1"/><sheetData><c r="A1"><f>1+1</f><v>2</v></c></sheetData></worksheet>' })).code, 'XLSX_FORMULA_FORBIDDEN');
});
test('hidden, duplicate and ambiguously mapped sheets are rejected', () => {
  assert.equal(policy.inspectParts(xmlParts({ 'xl/workbook.xml': '<workbook><sheets><sheet name="Data" state="hidden" r:id="rId1"/></sheets></workbook>' })).code, 'XLSX_HIDDEN_SHEET_FORBIDDEN');
  assert.equal(policy.inspectParts(xmlParts({ 'xl/workbook.xml': '<workbook><sheets><sheet name="Data" r:id="rId1"/><sheet name="data" r:id="rId2"/></sheets></workbook>' })).code, 'XLSX_SHEET_IDENTITY_AMBIGUOUS');
  assert.equal(policy.inspectParts(xmlParts({ 'xl/_rels/workbook.xml.rels': '<Relationships><Relationship Id="rId1" Target="worksheets/sheet1.xml"/><Relationship Id="rId1" Target="worksheets/sheet2.xml"/></Relationships>' })).code, 'XLSX_SHEET_RELATIONSHIP_INVALID');
});
test('dimension, cell reference, cell-count and shared-string ceilings are enforced', () => {
  assert.equal(policy.inspectParts(xmlParts({ 'xl/worksheets/sheet1.xml': '<worksheet><dimension ref="A1:EA2"/></worksheet>' })).code, 'XLSX_DIMENSION_LIMIT_EXCEEDED');
  assert.equal(policy.inspectParts(xmlParts({ 'xl/worksheets/sheet1.xml': '<worksheet><dimension ref="A1:B2"/><c r="EA1"/></worksheet>' })).code, 'XLSX_CELL_REFERENCE_INVALID');
  assert.equal(policy.inspectParts(xmlParts({ 'xl/worksheets/sheet1.xml': '<worksheet><dimension ref="A1:I250000"/></worksheet>' })).code, 'XLSX_CELL_COUNT_EXCEEDED');
  assert.equal(policy.inspectParts(xmlParts({ 'xl/sharedStrings.xml': `<sst><si><t>${'x'.repeat(4097)}</t></si></sst>` })).code, 'XLSX_CELL_TEXT_TOO_LONG');
});
