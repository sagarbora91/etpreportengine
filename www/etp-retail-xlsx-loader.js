/* API-23-compatible offline XLSX loading boundary. Pure/no-write and not app-loaded. */
(function (root, factory) {
  var api = factory(root && root.SaagarEtpXlsxPreflight, root && root.SaagarEtpXlsxParserPolicy,
    root && root.SaagarEtpRetailProfile, root && root.SaagarEtpRetailTableParser);
  if (typeof module === 'object' && module.exports) module.exports = api;
  if (root) root.SaagarEtpRetailXlsxLoader = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function (preflight, parserPolicy, profile, tableParser) {
  'use strict';
  if (typeof require === 'function') {
    preflight = preflight || require('./etp-xlsx-preflight.js');
    parserPolicy = parserPolicy || require('./etp-xlsx-parser-policy.js');
    profile = profile || require('./etp-retail-profile.js');
    tableParser = tableParser || require('./etp-retail-table-parser.js');
  }
  function refusal(code, details) { return Object.freeze({ ok: false, code: code, details: details || null }); }
  function bytesOf(value) {
    if (value instanceof Uint8Array) return value;
    if (typeof ArrayBuffer !== 'undefined' && value instanceof ArrayBuffer) return new Uint8Array(value);
    return null;
  }
  function create(dependencies) {
    var readWorkbook = dependencies && dependencies.readWorkbook;
    var unzipParts = dependencies && dependencies.unzipParts;
    if (typeof readWorkbook !== 'function' || typeof unzipParts !== 'function') throw new Error('ETP_XLSX_LOADER_DEPENDENCIES_REQUIRED');
    async function load(input) {
      var bytes = bytesOf(input && input.bytes);
      if (!bytes) return refusal('XLSX_BYTES_REQUIRED');
      var container = preflight.inspect(bytes);
      if (!container.ok) return container;
      var parts;
      try { parts = await unzipParts(bytes); } catch (_) { return refusal('XLSX_DECOMPRESSION_FAILED'); }
      var xml = preflight.inspectParts(parts);
      if (!xml.ok) return xml;
      var sheets;
      try {
        sheets = await readWorkbook(bytes, { parseNumber: parserPolicy.numericLexical });
      } catch (_) { return refusal('XLSX_PARSE_FAILED'); }
      if (!Array.isArray(sheets) || !sheets.length) return refusal('XLSX_SHEET_RESULT_INVALID');
      if (Array.isArray(sheets[0])) sheets = [{ sheet: '', data: sheets }];
      var candidates = [];
      sheets.forEach(function (sheet) {
        if (!sheet || !Array.isArray(sheet.data) || !sheet.data.length || !Array.isArray(sheet.data[0])) return;
        var detected = profile.detect(sheet.data[0], input.fileLabel, input.selectedReportId);
        if (detected.ok) candidates.push({ sheet: sheet, detected: detected });
      });
      if (!candidates.length) return refusal('XLSX_APPROVED_DATA_SHEET_NOT_FOUND');
      if (candidates.length !== 1) return refusal('XLSX_APPROVED_DATA_SHEET_AMBIGUOUS');
      var result = tableParser.parse({ rows: candidates[0].sheet.data, fileLabel: input.fileLabel,
        selectedReportId: input.selectedReportId, expectedStoreCode: input.expectedStoreCode,
        datePolicy: input.datePolicy });
      if (!result.ok) return result;
      return Object.freeze({ ok: true, code: 'RETAIL_XLSX_ACCEPTED', reportId: result.reportId,
        storeCode: result.storeCode, sheetName: String(candidates[0].sheet.sheet || ''),
        profileVersion: result.profileVersion, parserVersion: result.parserVersion, signatureKey: result.signatureKey,
        rowCount: result.rowCount, rows: result.rows, container: container, xml: xml });
    }
    return Object.freeze({ load: load });
  }
  return Object.freeze({ create: create });
});
