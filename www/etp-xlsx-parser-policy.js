/* ETP parser-boundary policy. App-loaded, pure and no-write. */
(function (root, factory) {
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  if (root) root.SaagarEtpXlsxParserPolicy = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';
  var MAX_ROWS = 250000, MAX_COLUMNS = 128, MAX_CELLS = 2000000, MAX_CELL_TEXT = 4096;
  function refusal(code, stage) { return Object.freeze({ ok: false, code: code, stage: stage || 'table' }); }
  function numericLexical(value) {
    var lexical = String(value == null ? '' : value);
    if (!/^-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?$/.test(lexical)) throw new Error('XLSX_NUMERIC_LEXICAL_INVALID');
    return Object.freeze({ etpCellKind: 'numeric', lexical: lexical });
  }
  function isNumericToken(value) { return !!value && typeof value === 'object' && value.etpCellKind === 'numeric' && typeof value.lexical === 'string'; }
  function text(value) {
    if (value == null) return '';
    if (typeof value !== 'string') return null;
    var normalized = value.trim();
    return normalized.length <= MAX_CELL_TEXT ? normalized : null;
  }
  function normalizeHeader(value) {
    var normalized = text(value);
    return normalized == null ? '' : normalized.toUpperCase().replace(/[^A-Z0-9]+/g, '_').replace(/^_+|_+$/g, '');
  }
  function exactIntegerText(lexical, maxDigits) {
    var match = /^(0|[1-9]\d*)(?:\.(\d+))?(?:[eE]([+-]?\d+))?$/.exec(lexical);
    if (!match) return null;
    var fraction = match[2] || '', exponentText = match[3] || '0';
    if (exponentText.length > 5) return null;
    var exponent = Number(exponentText);
    if (!Number.isSafeInteger(exponent) || Math.abs(exponent) > MAX_CELL_TEXT) return null;
    var digits = match[1] + fraction, decimalAt = match[1].length + exponent;
    if (decimalAt < digits.length) {
      if (decimalAt < 0 || /[^0]/.test(digits.slice(Math.max(0, decimalAt)))) return null;
      digits = decimalAt === 0 ? '0' : digits.slice(0, decimalAt);
    } else if (decimalAt > digits.length) {
      if (decimalAt > maxDigits) return null;
      digits += '0'.repeat(decimalAt - digits.length);
    }
    digits = digits.replace(/^0+(?=\d)/, '');
    return digits.length <= maxDigits ? digits : null;
  }
  function identifierText(value, policy) {
    if (!isNumericToken(value)) return text(value);
    var rule = policy && policy.mode === 'EXACT_XLSX_INTEGER_TEXT' ? policy : null;
    if (!rule) return null;
    var lexical = value.lexical, maxDigits = Number(rule.maxDigits);
    if (!Number.isSafeInteger(maxDigits) || maxDigits < 1 || maxDigits > 64) return null;
    /* Canonicalize only the exact decimal value stored in XLSX. Scientific
       notation and insignificant decimal zeroes are expanded as strings, so
       no IEEE-754 conversion, rounding, padding, or leading-zero repair occurs. */
    return exactIntegerText(lexical, maxDigits);
  }
  function inspectTable(rows, requiredIdentifiers, identifierPolicy) {
    if (!Array.isArray(rows) || !rows.length || !Array.isArray(rows[0])) return refusal('XLSX_HEADER_INVALID', 'header');
    if (rows.length - 1 > MAX_ROWS) return refusal('XLSX_ROW_LIMIT_EXCEEDED');
    if (rows[0].length > MAX_COLUMNS) return refusal('XLSX_COLUMN_LIMIT_EXCEEDED');
    var cells = 0, headers = [], seen = Object.create(null);
    for (var column = 0; column < rows[0].length; column += 1) {
      var header = normalizeHeader(rows[0][column]);
      if (!header || Object.prototype.hasOwnProperty.call(seen, header)) return refusal('XLSX_HEADER_INVALID', 'header');
      seen[header] = column; headers.push(header);
    }
    var identifiers = Array.isArray(requiredIdentifiers) ? requiredIdentifiers.map(normalizeHeader) : [];
    for (var required = 0; required < identifiers.length; required += 1) if (!Object.prototype.hasOwnProperty.call(seen, identifiers[required])) return refusal('XLSX_HEADER_INVALID', 'header');
    for (var rowIndex = 1; rowIndex < rows.length; rowIndex += 1) {
      var row = rows[rowIndex];
      if (!Array.isArray(row) || row.length > MAX_COLUMNS) return refusal('XLSX_COLUMN_LIMIT_EXCEEDED');
      for (column = 0; column < row.length; column += 1) {
        var value = row[column];
        if (value != null && value !== '') cells += 1;
        if (cells > MAX_CELLS) return refusal('XLSX_CELL_LIMIT_EXCEEDED');
        if (typeof value === 'string' && value.length > MAX_CELL_TEXT) return refusal('XLSX_CELL_TEXT_TOO_LONG');
        if (typeof value === 'number') return refusal('XLSX_NUMERIC_TYPE_UNTRACKED');
        if (value != null && typeof value !== 'string' && typeof value !== 'boolean' && !isNumericToken(value) && !(value instanceof Date)) return refusal('XLSX_CELL_TYPE_UNSUPPORTED');
      }
      for (required = 0; required < identifiers.length; required += 1) {
        var identifier = row[seen[identifiers[required]]];
        var normalizedIdentifier = identifierText(identifier, identifierPolicy);
        if (isNumericToken(identifier) && !normalizedIdentifier) return refusal('XLSX_IDENTIFIER_NUMERIC_UNVERIFIED', 'identifier');
        if (!normalizedIdentifier) return refusal('XLSX_IDENTIFIER_MISSING', 'identifier');
      }
    }
    return Object.freeze({ ok: true, code: 'XLSX_TABLE_ACCEPTED', stage: 'table', rows: rows.length - 1, columns: headers.length, nonblankCells: cells });
  }
  return Object.freeze({ LIMITS: Object.freeze({ maxRows: MAX_ROWS, maxColumns: MAX_COLUMNS, maxCells: MAX_CELLS, maxCellText: MAX_CELL_TEXT }), numericLexical: numericLexical, isNumericToken: isNumericToken, identifierText: identifierText, inspectTable: inspectTable });
});
