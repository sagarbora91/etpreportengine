/* ETP-A0 discovery, privacy, lineage and row-admission policy.
   Pure and dependency-free: no parser, persistence, UI, schema or app wiring. */
(function (root, factory) {
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  if (root) root.SaagarEtpImportFoundation = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';

  var REPORT_IDS = Object.freeze(['R022', 'R025', 'R013', 'R003']);
  var STORE_CODES = Object.freeze(['WLMHW', 'HEMW']);
  var TRANSACTION_TYPES = Object.freeze(['INV', 'SR', 'BC']);
  var BATCH_OUTCOMES = Object.freeze(['PREVIEW', 'VALIDATED', 'ACCEPTED', 'REJECTED', 'ABORTED']);
  // Keep batch metadata inside the same bounded capacity evaluated for API 23.
  var MAX_BATCH_ROWS = 250000;
  var MAX_FILE_LABEL = 120;

  function text(value) {
    if (value == null) return '';
    if (typeof value === 'string') return value.replace(/^\uFEFF/, '').trim();
    if (typeof value === 'number' && Number.isFinite(value)) return String(value);
    if (typeof value === 'boolean') return value ? 'TRUE' : 'FALSE';
    if (typeof value === 'bigint') return String(value);
    throw new TypeError('Cell value must be a text-compatible primitive.');
  }
  function record(value) { return !!value && typeof value === 'object' && !Array.isArray(value); }
  function issue(code, message, field) { return Object.assign({ code: code, message: message }, field ? { field: field } : {}); }
  function result(fatal, warnings) {
    return { ok: !fatal.length, outcome: fatal.length ? 'fatal' : (warnings.length ? 'warning' : 'accepted'), fatalErrors: fatal, warnings: warnings };
  }
  function normalizeHeader(value) {
    return text(value).toUpperCase().replace(/[^A-Z0-9]+/g, '_').replace(/^_+|_+$/g, '');
  }
  function compactHeader(value) { return normalizeHeader(value).replace(/_/g, ''); }
  function isForbiddenPiiHeader(value) {
    var token;
    try { token = compactHeader(value); } catch (error) { return true; }
    if (!token) return false;
    if (/^(?:NAME|MOBILE|MOBILENO|MOBILENUMBER|PHONE|PHONENO|PHONENUMBER|CONTACT|CONTACTNO|CONTACTNUMBER|EMAIL|EMAILID|ADDRESS)$/.test(token)) return true;
    if (/^(?:CUSTOMER|CUST).*(?:NAME|NUMBER|MOBILE|PHONE|CONTACT|EMAIL|ADDRESS|PAN)$/.test(token)) return true;
    if (/^(?:CRO|EMPLOYEE|STAFF|ADVISER|ADVISOR)NAME$/.test(token)) return true;
    if (/^(?:LOYALTYCONTACT|ENCIRCLEID|ENCIRCLENO|ENCIRCLENUMBER|ULP|ULPNO|ULPNUMBER)$/.test(token)) return true;
    if (/^(?:(?:CREDIT|GIFT)?CARD)(?:NO|NUMBER)$/.test(token)) return true;
    return /^(?:OTP|APPROVALOTP|APPROVALSECRET|AADHAAR|AADHAARNO|UID)$/.test(token);
  }
  function normalizeHeaderSignature(headers) {
    var fatal = [], normalized = [], seen = Object.create(null);
    if (!Array.isArray(headers) || !headers.length) return Object.assign(result([issue('HEADER_ROW_REQUIRED', 'A non-empty header row is required.', 'headers')], []), { headers: [], key: '' });
    headers.forEach(function (value, index) {
      var header = '';
      try { header = normalizeHeader(value); } catch (error) { fatal.push(issue('HEADER_INVALID', 'Header cells must be text-compatible.', 'headers[' + index + ']')); }
      if (!header) fatal.push(issue('HEADER_EMPTY', 'Blank headers are not allowed.', 'headers[' + index + ']'));
      else if (seen[header]) fatal.push(issue('HEADER_DUPLICATE', 'Duplicate normalized header.', header));
      else { seen[header] = true; normalized.push(header); }
    });
    normalized.sort();
    return Object.assign(result(fatal, []), { headers: normalized, key: fatal.length ? '' : normalized.join('\u001f') });
  }
  function signatureLists(value) {
    if (record(value)) value = value.signatures;
    if (!Array.isArray(value) || !value.length) return [];
    return value.every(function (entry) { return !Array.isArray(entry); }) ? [value] : value;
  }
  function detectReport(headers, definitions) {
    var source = normalizeHeaderSignature(headers), fatal = source.fatalErrors.slice(), matches = [];
    if (!record(definitions)) fatal.push(issue('DEFINITIONS_REQUIRED', 'Caller-supplied report signatures are required.', 'definitions'));
    else Object.keys(definitions).forEach(function (rawId) {
      var id = text(rawId).toUpperCase();
      if (REPORT_IDS.indexOf(id) < 0) { fatal.push(issue('REPORT_ID_UNKNOWN', 'Unknown report definition.', rawId)); return; }
      var lists = signatureLists(definitions[rawId]);
      if (!lists.length) { fatal.push(issue('SIGNATURE_REQUIRED', 'At least one signature is required.', id)); return; }
      lists.forEach(function (list) {
        var signature = normalizeHeaderSignature(list);
        if (!signature.ok) fatal.push(issue('SIGNATURE_INVALID', 'A report signature is invalid.', id));
        else if (signature.key === source.key) matches.push(id);
      });
    });
    matches = matches.filter(function (value, index, all) { return all.indexOf(value) === index; }).sort();
    if (fatal.length) return Object.assign(result(fatal, []), { code: 'HEADER_DETECTION_INVALID' });
    if (!matches.length) return Object.assign(result([issue('HEADER_UNKNOWN', 'The header signature is not approved.', 'headers')], []), { code: 'HEADER_UNKNOWN', signature: source.headers });
    if (matches.length > 1) return Object.assign(result([issue('HEADER_AMBIGUOUS', 'The header signature matches more than one report.', 'headers')], []), { code: 'HEADER_AMBIGUOUS', matches: matches });
    return Object.assign(result([], []), { code: 'REPORT_DETECTED', reportId: matches[0], signature: source.headers, signatureKey: source.key });
  }
  function yyyyMmDdToIso(value) {
    var match;
    try { match = /^(\d{4})(\d{2})(\d{2})$/.exec(text(value)); } catch (error) { return ''; }
    if (!match) return '';
    var date = new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3])));
    if (date.getUTCFullYear() !== Number(match[1]) || date.getUTCMonth() !== Number(match[2]) - 1 || date.getUTCDate() !== Number(match[3])) return '';
    return match[1] + '-' + match[2] + '-' + match[3];
  }
  function isoDate(value) { var match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(String(value || '').trim()); return match ? yyyyMmDdToIso(match[1] + match[2] + match[3]) : ''; }
  function financialYearFromInvoiceDate(value) {
    var date = isoDate(value) || yyyyMmDdToIso(value); if (!date) return '';
    var year = Number(date.slice(0, 4)), start = Number(date.slice(5, 7)) >= 4 ? year : year - 1;
    return String(start) + '-' + String((start + 1) % 100).padStart(2, '0');
  }
  function transactionEffect(value) {
    var raw = ''; try { raw = text(value).toUpperCase(); } catch (error) {}
    var sign = raw === 'INV' ? 1 : (raw === 'SR' || raw === 'BC' ? -1 : 0);
    return Object.freeze({ code: sign ? raw : 'UNKNOWN', raw: raw, known: !!sign, quantitySign: sign, valueSign: sign, salesEffect: sign, hasEffect: !!sign });
  }
  function safeOutput(value) { var out = String(value || '').trim(); return /^[A-Za-z][A-Za-z0-9_]{0,63}$/.test(out) && !/^(?:constructor|prototype|__proto__)$/.test(out) ? out : ''; }
  function compileAdapter(reportId, adapters) {
    var fatal = [], fields = Object.create(null), outputs = Object.create(null), drops = Object.create(null), required = [];
    var adapter = record(adapters) && record(adapters[reportId]) ? adapters[reportId] : null;
    if (!adapter || !record(adapter.fields)) return { fatalErrors: [issue('WHITELIST_REQUIRED', 'A caller-supplied report whitelist is required.', reportId)], fields: fields, drops: drops, required: required };
    Object.keys(adapter.fields).forEach(function (raw) {
      var source = normalizeHeader(raw), out = safeOutput(adapter.fields[raw]);
      if (!source || !out) fatal.push(issue('WHITELIST_FIELD_INVALID', 'Whitelist entries need safe fields.', raw));
      else if (isForbiddenPiiHeader(source) || isForbiddenPiiHeader(out)) fatal.push(issue('PII_FIELD_FORBIDDEN', 'PII fields cannot be persisted.', source));
      else if (fields[source] || outputs[out]) fatal.push(issue('WHITELIST_DUPLICATE', 'Whitelist fields must be unique.', source));
      else { fields[source] = out; outputs[out] = true; }
    });
    if (!Array.isArray(adapter.dropHeaders)) fatal.push(issue('DROP_HEADERS_REQUIRED', 'An explicit known-PII drop list is required.', 'dropHeaders'));
    else adapter.dropHeaders.forEach(function (raw) {
      var source = normalizeHeader(raw);
      if (!source || !isForbiddenPiiHeader(source)) fatal.push(issue('DROP_HEADER_INVALID', 'Only known PII headers may be dropped.', raw));
      else if (drops[source] || fields[source]) fatal.push(issue('DROP_HEADER_CONFLICT', 'Dropped headers must be unique and non-persisted.', source));
      else drops[source] = true;
    });
    ['requiredIdentifiers', 'requiredMeasures'].forEach(function (name) {
      if (!Array.isArray(adapter[name]) || !adapter[name].length) fatal.push(issue('REPORT_REQUIREMENTS_REQUIRED', 'Explicit identifiers and measures are required.', name));
      else adapter[name].forEach(function (raw) { var out = safeOutput(raw); if (!out || !outputs[out]) fatal.push(issue('REQUIRED_FIELD_NOT_WHITELISTED', 'Required fields must be whitelisted outputs.', raw)); else if (required.indexOf(out) < 0) required.push(out); });
    });
    var dateHeader = normalizeHeader(adapter.businessDateHeader || '');
    if (!/^(?:INVOICEDATE|INVDATE)$/.test(dateHeader.replace(/_/g, ''))) fatal.push(issue('BUSINESS_DATE_MAPPING_INVALID', 'An approved invoice-date header is required.', 'businessDateHeader'));
    if (!fields[dateHeader]) fatal.push(issue('BUSINESS_DATE_NOT_WHITELISTED', 'The invoice date must be whitelisted.', dateHeader));
    if (!outputs.storeCode) fatal.push(issue('STORE_CODE_MAPPING_REQUIRED', 'A storeCode mapping is required.', 'fields'));
    if (!outputs.transactionTypeRaw) fatal.push(issue('TRANS_TYPE_MAPPING_REQUIRED', 'A transactionTypeRaw mapping is required.', 'fields'));
    return { fatalErrors: fatal, fields: fields, drops: drops, required: required, dateHeader: dateHeader };
  }
  function preparePersistableRow(reportIdValue, sourceRow, adapters, detected, datePolicy) {
    var reportId = String(reportIdValue || '').trim().toUpperCase(), fatal = [], warnings = [], entries = [], seen = Object.create(null);
    if (REPORT_IDS.indexOf(reportId) < 0) fatal.push(issue('REPORT_ID_UNKNOWN', 'Unknown E1 report.', 'reportId'));
    if (!record(sourceRow)) fatal.push(issue('ROW_REQUIRED', 'A source row object is required.', 'row'));
    else Object.keys(sourceRow).forEach(function (raw) { var header = normalizeHeader(raw); if (!header || seen[header]) fatal.push(issue('ROW_HEADER_INVALID', 'Row headers must be nonblank and unique.', raw)); else { seen[header] = true; entries.push({ header: header, value: sourceRow[raw] }); } });
    var adapter = compileAdapter(reportId, adapters); fatal = fatal.concat(adapter.fatalErrors);
    var signature = normalizeHeaderSignature(entries.map(function (entry) { return entry.header; }));
    if (!detected || !detected.ok || detected.reportId !== reportId || !detected.signatureKey || detected.signatureKey !== signature.key) fatal.push(issue('HEADER_SIGNATURE_MISMATCH', 'The row must match its exact detected signature.', 'headers'));
    entries.forEach(function (entry) { if (adapter.drops[entry.header]) return; if (isForbiddenPiiHeader(entry.header)) fatal.push(issue('PII_FIELD_UNAPPROVED', 'Known PII needs explicit drop approval.', entry.header)); else if (!adapter.fields[entry.header]) fatal.push(issue('FIELD_NOT_WHITELISTED', 'Unapproved source field.', entry.header)); });
    function byOutput(output) { var source = Object.keys(adapter.fields).find(function (header) { return adapter.fields[header] === output; }); return entries.find(function (entry) { return entry.header === source; }) || null; }
    var storeEntry = byOutput('storeCode'), storeCode = storeEntry ? String(storeEntry.value || '').trim().toUpperCase() : '';
    if (!storeEntry) fatal.push(issue('STORE_CODE_REQUIRED', 'A store code is required.', 'storeCode')); else if (STORE_CODES.indexOf(storeCode) < 0) fatal.push(issue('STORE_CODE_UNKNOWN', 'Unknown store code.', 'storeCode'));
    var dateEntry = entries.find(function (entry) { return entry.header === adapter.dateHeader; }), businessDate = dateEntry ? yyyyMmDdToIso(dateEntry.value) : '';
    if (!dateEntry) fatal.push(issue('INVOICE_DATE_REQUIRED', 'The invoice date is required.', adapter.dateHeader)); else if (!businessDate) fatal.push(issue('INVOICE_DATE_INVALID', 'Invoice date must be a real YYYYMMDD date.', adapter.dateHeader));
    var earliest = record(datePolicy) ? isoDate(datePolicy.earliestDate) : '', asOf = record(datePolicy) ? isoDate(datePolicy.asOfDate) : '', skew = record(datePolicy) ? Number(datePolicy.maxFutureDays) : NaN;
    if (!earliest || !asOf || !Number.isSafeInteger(skew) || skew < 0 || skew > 366) fatal.push(issue('DATE_POLICY_INVALID', 'Deterministic date limits are required.', 'datePolicy'));
    else if (businessDate) { var latest = new Date(Date.parse(asOf + 'T00:00:00Z') + skew * 86400000).toISOString().slice(0, 10); if (businessDate < earliest) fatal.push(issue('INVOICE_DATE_TOO_OLD', 'Invoice date predates the historical limit.', adapter.dateHeader)); if (businessDate > latest) fatal.push(issue('INVOICE_DATE_TOO_FAR_FUTURE', 'Invoice date exceeds future skew.', adapter.dateHeader)); }
    adapter.required.forEach(function (output) { var entry = byOutput(output), value = ''; try { value = entry ? text(entry.value) : ''; } catch (error) {} if (!value) fatal.push(issue('REQUIRED_FIELD_MISSING', 'A report-specific required field is missing.', output)); });
    var transEntry = byOutput('transactionTypeRaw'), effect = transactionEffect(transEntry ? transEntry.value : '');
    if (!transEntry) fatal.push(issue('TRANS_TYPE_REQUIRED', 'A mapped transaction type is required.', 'transactionTypeRaw')); else if (!effect.known) warnings.push(issue('TRANS_TYPE_UNKNOWN_NO_EFFECT', 'Unknown transaction type has no effect.', 'transactionTypeRaw'));
    var fields = {};
    if (!fatal.length) entries.forEach(function (entry) { if (!adapter.drops[entry.header]) fields[adapter.fields[entry.header]] = text(entry.value); });
    var checked = result(fatal, warnings);
    if (checked.ok) checked.persistableRow = Object.freeze({ reportId: reportId, storeCode: storeCode, fields: Object.freeze(fields), businessDate: businessDate, financialYear: financialYearFromInvoiceDate(businessDate), businessDateHeader: adapter.dateHeader, headerSignatureKey: signature.key, transaction: effect });
    return checked;
  }
  function sanitizeFileLabel(value) {
    var original = String(value == null ? '' : value).trim().replace(/[\u0000-\u001f\u007f]/g, '');
    var leaf = original.split(/[\\/]/).pop() || '';
    return leaf.replace(/[^A-Za-z0-9._() -]+/g, '_').replace(/\s+/g, ' ')
      .replace(/_+/g, '_').replace(/^[ ._-]+|[ ._-]+$/g, '').slice(0, MAX_FILE_LABEL);
  }
  function safeToken(value, maxLength) {
    var valueText = String(value == null ? '' : value).trim();
    return valueText && valueText.length <= maxLength && /^[A-Za-z0-9._:-]+$/.test(valueText) ? valueText : '';
  }
  function timestamp(value) {
    var valueText = String(value == null ? '' : value).trim();
    if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,3})?Z$/.test(valueText)) return '';
    var time = Date.parse(valueText);
    return Number.isFinite(time) ? new Date(time).toISOString() : '';
  }
  function validFinancialYear(value) {
    var match = /^(\d{4})-(\d{2})$/.exec(String(value == null ? '' : value).trim());
    return !!match && Number(match[2]) === (Number(match[1]) + 1) % 100;
  }
  function validateBatchMetadata(value) {
    if (!record(value)) return Object.assign(result([issue('BATCH_METADATA_REQUIRED', 'Batch metadata is required.', 'metadata')], []), { code: 'BATCH_METADATA_INVALID' });
    var fatal = [], warnings = [], warningCodes = [];
    var reportId = String(value.reportId || '').trim().toUpperCase();
    var storeCode = String(value.storeCode || '').trim().toUpperCase();
    var financialYear = String(value.financialYear || '').trim();
    var periodStart = isoDate(value.periodStart), periodEnd = isoDate(value.declaredPeriodEnd);
    var rawLabel = String(value.fileLabel == null ? '' : value.fileLabel).trim().replace(/[\u0000-\u001f\u007f]/g, '');
    var fileLabel = sanitizeFileLabel(value.fileLabel), fileSha256 = String(value.fileSha256 || '').trim().toLowerCase();
    var rowCount = Number(value.rowCount), actorId = safeToken(value.actorId, 80);
    var importedAt = timestamp(value.importedAt), dictionaryVersion = safeToken(value.dictionaryVersion, 80);
    var outcome = String(value.outcome || '').trim().toUpperCase();
    if (REPORT_IDS.indexOf(reportId) < 0) fatal.push(issue('REPORT_ID_UNKNOWN', 'Unknown ETP report.', 'reportId'));
    if (STORE_CODES.indexOf(storeCode) < 0) fatal.push(issue('STORE_CODE_UNKNOWN', 'Unknown store code.', 'storeCode'));
    if (!validFinancialYear(financialYear)) fatal.push(issue('FINANCIAL_YEAR_INVALID', 'Financial year must use YYYY-YY.', 'financialYear'));
    if (!periodStart) fatal.push(issue('PERIOD_START_INVALID', 'Period start must be a real ISO date.', 'periodStart'));
    if (!periodEnd) fatal.push(issue('PERIOD_END_INVALID', 'Period end must be a real ISO date.', 'declaredPeriodEnd'));
    if (periodStart && periodEnd && periodStart > periodEnd) fatal.push(issue('PERIOD_RANGE_INVALID', 'Period start is after period end.', 'periodStart'));
    if (periodStart && validFinancialYear(financialYear) && financialYearFromInvoiceDate(periodStart) !== financialYear) fatal.push(issue('PERIOD_FY_MISMATCH', 'Period start is outside the financial year.', 'periodStart'));
    if (periodEnd && validFinancialYear(financialYear) && financialYearFromInvoiceDate(periodEnd) !== financialYear) fatal.push(issue('PERIOD_FY_MISMATCH', 'Period end is outside the financial year.', 'declaredPeriodEnd'));
    if (!fileLabel) fatal.push(issue('FILE_LABEL_INVALID', 'A safe source label is required.', 'fileLabel')); else if (fileLabel !== rawLabel) warnings.push(issue('FILE_LABEL_SANITIZED', 'The source label was reduced to a safe leaf.', 'fileLabel'));
    if (!/^[a-f0-9]{64}$/.test(fileSha256)) fatal.push(issue('FILE_HASH_INVALID', 'Source SHA-256 must be hexadecimal.', 'fileSha256'));
    if (!Number.isSafeInteger(rowCount) || rowCount < 0 || rowCount > MAX_BATCH_ROWS) fatal.push(issue('ROW_COUNT_INVALID', 'Row count is outside the accepted range.', 'rowCount'));
    if (!actorId) fatal.push(issue('ACTOR_ID_INVALID', 'A safe stable actor ID is required.', 'actorId'));
    if (!importedAt) fatal.push(issue('IMPORTED_AT_INVALID', 'Import timestamp must be ISO UTC.', 'importedAt'));
    if (!dictionaryVersion) fatal.push(issue('DICTIONARY_VERSION_INVALID', 'A safe dictionary version is required.', 'dictionaryVersion'));
    if (BATCH_OUTCOMES.indexOf(outcome) < 0) fatal.push(issue('BATCH_OUTCOME_INVALID', 'Unknown batch outcome.', 'outcome'));
    if (!Array.isArray(value.warningCodes) || value.warningCodes.length > 100) fatal.push(issue('WARNING_CODES_INVALID', 'Warning codes must be a bounded array.', 'warningCodes'));
    else value.warningCodes.forEach(function (raw, index) { var code = String(raw || '').trim().toUpperCase(); if (!/^[A-Z][A-Z0-9_:-]{0,63}$/.test(code)) fatal.push(issue('WARNING_CODE_INVALID', 'Warning code is unsafe.', 'warningCodes[' + index + ']')); else if (warningCodes.indexOf(code) < 0) warningCodes.push(code); });
    warningCodes.sort();
    var checked = result(fatal, warnings); checked.code = checked.ok ? 'BATCH_METADATA_VALID' : 'BATCH_METADATA_INVALID';
    if (checked.ok) checked.metadata = Object.freeze({ reportId: reportId, storeCode: storeCode, financialYear: financialYear, periodStart: periodStart, declaredPeriodEnd: periodEnd, fileLabel: fileLabel, fileSha256: fileSha256, rowCount: rowCount, actorId: actorId, importedAt: importedAt, dictionaryVersion: dictionaryVersion, warningCodes: Object.freeze(warningCodes), outcome: outcome });
    return checked;
  }
  function batchIdentity(value) {
    var checked = validateBatchMetadata(value); if (!checked.ok) return '';
    var metadata = checked.metadata;
    return [metadata.storeCode, metadata.financialYear, metadata.periodStart + '..' + metadata.declaredPeriodEnd, metadata.reportId, metadata.fileSha256].join('|');
  }
  return Object.freeze({ REPORT_IDS: REPORT_IDS, STORE_CODES: STORE_CODES, TRANSACTION_TYPES: TRANSACTION_TYPES, BATCH_OUTCOMES: BATCH_OUTCOMES, normalizeCellText: text, normalizeHeader: normalizeHeader, normalizeHeaderSignature: normalizeHeaderSignature, detectReport: detectReport, isForbiddenPiiHeader: isForbiddenPiiHeader, yyyyMmDdToIso: yyyyMmDdToIso, financialYearFromInvoiceDate: financialYearFromInvoiceDate, transactionEffect: transactionEffect, preparePersistableRow: preparePersistableRow, sanitizeFileLabel: sanitizeFileLabel, validateBatchMetadata: validateBatchMetadata, batchIdentity: batchIdentity });
});
