/* Shared Retail ETP table parser. App-loaded, pure and no-write. */
(function (root, factory) {
  var api = factory(root && root.SaagarEtpImportFoundation, root && root.SaagarEtpXlsxParserPolicy, root && root.SaagarEtpRetailProfile);
  if (typeof module === 'object' && module.exports) module.exports = api;
  if (root) root.SaagarEtpRetailTableParser = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function (foundation, parserPolicy, profile) {
  'use strict';
  if (typeof require === 'function') {
    foundation = foundation || require('./etp-import-foundation.js');
    parserPolicy = parserPolicy || require('./etp-xlsx-parser-policy.js');
    profile = profile || require('./etp-retail-profile.js');
  }
  var PARSER_VERSION = 'retail-etp-parser-v1';
  function refusal(code, details) { return Object.freeze({ ok: false, code: code, details: details || null }); }
  function isoCompact(value) {
    if (value instanceof Date && Number.isFinite(value.getTime())) return value.toISOString().slice(0, 10).replace(/-/g, '');
    if (parserPolicy.isNumericToken(value)) {
      var integerText = parserPolicy.identifierText(value, { mode: 'EXACT_XLSX_INTEGER_TEXT', maxDigits: 8 });
      if (integerText === '0') return '';
      if (/^\d{8}$/.test(integerText)) {
        var y=Number(integerText.slice(0,4)),m=Number(integerText.slice(4,6)),d=Number(integerText.slice(6,8)),direct=new Date(Date.UTC(y,m-1,d));
        return direct.getUTCFullYear()===y&&direct.getUTCMonth()===m-1&&direct.getUTCDate()===d?integerText:null;
      }
      if (!/^[1-9]\d{0,6}$/.test(integerText || '')) return null;
      var serial = Number(integerText); if (!Number.isSafeInteger(serial) || serial === 60 || serial > 2958465) return null;
      var excelDate = new Date(Date.UTC(1899, 11, 30) + serial * 86400000);
      return Number.isFinite(excelDate.getTime()) ? excelDate.toISOString().slice(0, 10).replace(/-/g, '') : null;
    }
    var text = String(value == null ? '' : value).trim();
    return /^\d{4}-\d{2}-\d{2}$/.test(text) ? text.replace(/-/g, '') : text;
  }
  function scalar(value, isIdentifier, isDate, isApprovedNumeric, identifierPolicy) {
    if (isDate) return isoCompact(value);
    if (parserPolicy.isNumericToken(value)) return isIdentifier ? (parserPolicy.identifierText(value, identifierPolicy) || { error: 'XLSX_IDENTIFIER_NUMERIC_UNVERIFIED' }) :
      (isApprovedNumeric ? value.lexical : { error: 'XLSX_NUMERIC_FIELD_UNAPPROVED' });
    if (typeof value === 'number') return { error: 'XLSX_NUMERIC_TYPE_UNTRACKED' };
    if (value instanceof Date) return null;
    if (value == null) return '';
    return typeof value === 'boolean' ? String(value) : String(value).trim();
  }
  function parse(input) {
    if (!input || !Array.isArray(input.rows) || !input.rows.length) return refusal('RETAIL_TABLE_REQUIRED');
    var headers = input.rows[0], detected = profile.detect(headers, input.fileLabel, input.selectedReportId);
    if (!detected.ok) return detected;
    var report = profile.REPORTS[detected.reportId], normalizedHeaders = headers.map(foundation.normalizeHeader);
    var outputByHeader = {};
    Object.keys(report.fields).forEach(function (raw) { outputByHeader[foundation.normalizeHeader(raw)] = report.fields[raw]; });
    var identifierOutputs = Object.create(null);
    report.requiredIdentifiers.forEach(function (name) { identifierOutputs[name] = true; });
    report.numericTextOutputs.forEach(function (name) { identifierOutputs[name] = true; });
    var numericOutputs = Object.create(null);
    report.numericOutputs.forEach(function (name) { numericOutputs[name] = true; });
    var requiredSourceHeaders = Object.keys(outputByHeader).filter(function (header) { return identifierOutputs[outputByHeader[header]]; });
    var structural = parserPolicy.inspectTable(input.rows, requiredSourceHeaders, report.numericIdentifierPolicy);
    if (!structural.ok) return structural;
    var rows = [], adapters = profile.adapters(), datePolicy = input.datePolicy;
    for (var rowIndex = 1; rowIndex < input.rows.length; rowIndex += 1) {
      var source = {}, row = input.rows[rowIndex] || [];
      for (var column = 0; column < normalizedHeaders.length; column += 1) {
        var output = outputByHeader[normalizedHeaders[column]], value = row[column];
        var converted = scalar(value, !!identifierOutputs[output], /Date$/i.test(String(output||'')), !!numericOutputs[output], report.numericIdentifierPolicy);
        if (converted && converted.error) return refusal(converted.error, { row: rowIndex + 1, header: headers[column] });
        if (converted === null) return refusal('XLSX_CELL_TYPE_UNSUPPORTED', { row: rowIndex + 1, header: headers[column] });
        source[headers[column]] = converted;
      }
      var prepared = foundation.preparePersistableRow(detected.reportId, source, adapters, detected, datePolicy);
      if (!prepared.ok) return refusal('RETAIL_ROW_REFUSED', { row: rowIndex + 1, fatalErrors: prepared.fatalErrors, warnings: prepared.warnings });
      rows.push(prepared.persistableRow);
    }
    var stores = Array.from(new Set(rows.map(function (row) { return row.storeCode; })));
    if (stores.length !== 1) return refusal('RETAIL_STORE_SCOPE_MIXED');
    var expectedStore = String(input.expectedStoreCode || '').trim().toUpperCase();
    if (profile.STORES.indexOf(expectedStore) < 0) return refusal('RETAIL_EXPECTED_STORE_REQUIRED');
    if (stores[0] !== expectedStore) return refusal('RETAIL_STORE_SCOPE_MISMATCH');
    return Object.freeze({ ok: true, code: 'RETAIL_TABLE_ACCEPTED', profileVersion: profile.VERSION, parserVersion: PARSER_VERSION,
      reportId: detected.reportId, storeCode: stores[0], signatureKey: detected.signatureKey,
      rowCount: rows.length, rows: Object.freeze(rows) });
  }
  return Object.freeze({ VERSION: PARSER_VERSION, parse: parse });
});
