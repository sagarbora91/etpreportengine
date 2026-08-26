/* ETP reconciliation, coverage and publication policy.
   Pure and dependency-free: no parser, persistence, UI or app wiring. */
(function (root, factory) {
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  if (root) root.SaagarEtpReconciliationPolicy = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';

  var COVERAGE_STATUSES = Object.freeze([
    'COMPLETE', 'COMPLETE_WITH_ZERO_ACTIVITY', 'PARTIAL_START', 'PARTIAL_END',
    'INTERNAL_GAP', 'SNAPSHOT_ONLY', 'DATE_UNCERTAIN', 'NOT_EXPECTED'
  ]);
  var COMPLETE_STATUSES = Object.freeze(['COMPLETE', 'COMPLETE_WITH_ZERO_ACTIVITY']);
  var SAFE_TOKEN = /^[A-Za-z][A-Za-z0-9_]{0,63}$/;

  function record(value) { return !!value && typeof value === 'object' && !Array.isArray(value); }
  function issue(code, message, field) { return Object.assign({ code: code, message: message }, field ? { field: field } : {}); }
  function result(errors) { return { ok: !errors.length, errors: errors }; }
  function token(value) { var out = String(value == null ? '' : value).trim(); return SAFE_TOKEN.test(out) ? out : ''; }
  function isoDate(value) {
    var raw = String(value == null ? '' : value).trim();
    var match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(raw);
    if (!match) return '';
    var date = new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3])));
    return date.getUTCFullYear() === Number(match[1]) && date.getUTCMonth() === Number(match[2]) - 1 && date.getUTCDate() === Number(match[3]) ? raw : '';
  }
  function safeText(value) {
    if (typeof value !== 'string') return '';
    var out = value.trim();
    return out && out.length <= 256 ? out : '';
  }
  function validateRuleDefinition(value) {
    var errors = [], keyNames = Object.create(null), measureNames = Object.create(null);
    if (!record(value)) return Object.assign(result([issue('RULE_REQUIRED', 'A reconciliation rule is required.', 'rule')]), { rule: null });
    var ruleId = token(value.ruleId), ruleVersion = token(value.ruleVersion), owner = safeText(value.owner), label = safeText(value.label);
    if (!ruleId) errors.push(issue('RULE_ID_INVALID', 'A safe rule ID is required.', 'ruleId'));
    if (!ruleVersion) errors.push(issue('RULE_VERSION_INVALID', 'A safe rule version is required.', 'ruleVersion'));
    if (!owner) errors.push(issue('RULE_OWNER_REQUIRED', 'An explicit rule owner is required.', 'owner'));
    if (!label) errors.push(issue('RULE_LABEL_REQUIRED', 'An explicit display label is required.', 'label'));
    if (value.severity !== 'CRITICAL' && value.severity !== 'HIGH' && value.severity !== 'MEDIUM') errors.push(issue('RULE_SEVERITY_INVALID', 'Severity must be explicit.', 'severity'));
    var reports = record(value.sourceReports) ? value.sourceReports : {};
    var leftReport = token(reports.left), rightReport = token(reports.right);
    if (!leftReport || !rightReport || leftReport === rightReport) errors.push(issue('SOURCE_REPORTS_INVALID', 'Two distinct source reports are required.', 'sourceReports'));
    if (!Array.isArray(value.keys) || !value.keys.length) errors.push(issue('GRAIN_REQUIRED', 'An explicit non-empty common grain is required.', 'keys'));
    var keys = [];
    (Array.isArray(value.keys) ? value.keys : []).forEach(function (entry, index) {
      var name = record(entry) ? token(entry.name) : '', leftField = record(entry) ? token(entry.leftField) : '', rightField = record(entry) ? token(entry.rightField) : '';
      if (!name || !leftField || !rightField || keyNames[name]) errors.push(issue('GRAIN_KEY_INVALID', 'Every grain key must be unique and explicitly mapped.', 'keys[' + index + ']'));
      else { keyNames[name] = true; keys.push({ name: name, leftField: leftField, rightField: rightField }); }
    });
    var transaction = record(value.transaction) ? value.transaction : {};
    var leftTransactionField = token(transaction.leftField), rightTransactionField = token(transaction.rightField);
    var signs = record(transaction.signs) ? transaction.signs : {};
    if (!leftTransactionField || !rightTransactionField) errors.push(issue('TRANSACTION_FIELDS_REQUIRED', 'Transaction fields must be explicit.', 'transaction'));
    if (Object.keys(signs).sort().join('|') !== 'BC|INV|SR' || signs.INV !== 1 || signs.SR !== -1 || signs.BC !== -1) errors.push(issue('SIGN_POLICY_INVALID', 'The explicit INV/SR/BC sign policy is required.', 'transaction.signs'));
    if (!Array.isArray(value.measures) || !value.measures.length) errors.push(issue('MEASURES_REQUIRED', 'At least one explicit measure is required.', 'measures'));
    var measures = [];
    (Array.isArray(value.measures) ? value.measures : []).forEach(function (entry, index) {
      var name = record(entry) ? token(entry.name) : '', leftField = record(entry) ? token(entry.leftField) : '', rightField = record(entry) ? token(entry.rightField) : '';
      var scale = record(entry) ? entry.scale : null, toleranceUnits = record(entry) ? entry.toleranceUnits : null;
      if (!name || !leftField || !rightField || measureNames[name] || !Number.isSafeInteger(scale) || scale < 0 || scale > 6 || !Number.isSafeInteger(toleranceUnits) || toleranceUnits < 0) errors.push(issue('MEASURE_INVALID', 'Each measure needs unique fields, scale and non-negative integer tolerance units.', 'measures[' + index + ']'));
      else { measureNames[name] = true; measures.push({ name: name, leftField: leftField, rightField: rightField, scale: scale, toleranceUnits: toleranceUnits }); }
    });
    if (!Array.isArray(value.filters)) errors.push(issue('FILTERS_REQUIRED', 'Filters must be explicitly supplied, including an empty array.', 'filters'));
    else if (value.filters.length) errors.push(issue('FILTER_UNSUPPORTED', 'This foundation accepts no implicit filtering; pre-filter rules need a separate approved contract.', 'filters'));
    var checked = result(errors);
    if (checked.ok) checked.rule = Object.freeze({ ruleId: ruleId, ruleVersion: ruleVersion, owner: owner, label: label, severity: value.severity, sourceReports: Object.freeze({ left: leftReport, right: rightReport }), keys: Object.freeze(keys), transaction: Object.freeze({ leftField: leftTransactionField, rightField: rightTransactionField, signs: Object.freeze({ INV: 1, SR: -1, BC: -1 }) }), measures: Object.freeze(measures), filters: Object.freeze([]) });
    return checked;
  }
  function decimalUnits(value, scale) {
    var raw = typeof value === 'number' && Number.isFinite(value) ? String(value) : (typeof value === 'string' ? value.trim() : '');
    var match = /^(-?)(\d+)(?:\.(\d+))?$/.exec(raw);
    if (!match || (match[3] || '').length > scale) return null;
    var digits = match[2] + (match[3] || '').padEnd(scale, '0');
    var units = Number(digits);
    if (!Number.isSafeInteger(units)) return null;
    return (match[1] ? -1 : 1) * units;
  }
  function aggregateReportRows(reportId, rows, definition) {
    var checked = validateRuleDefinition(definition), errors = checked.errors.slice(), groups = Object.create(null);
    if (!checked.ok) return Object.assign(result(errors), { groups: [] });
    var rule = checked.rule, side = reportId === rule.sourceReports.left ? 'left' : (reportId === rule.sourceReports.right ? 'right' : '');
    if (!side) errors.push(issue('REPORT_NOT_IN_RULE', 'The report is not a source for this rule.', 'reportId'));
    if (!Array.isArray(rows)) errors.push(issue('ROWS_REQUIRED', 'Rows must be an array.', 'rows'));
    if (errors.length) return Object.assign(result(errors), { groups: [] });
    rows.forEach(function (row, rowIndex) {
      if (!record(row)) { errors.push(issue('ROW_INVALID', 'Every row must be an object.', 'rows[' + rowIndex + ']')); return; }
      var keyValues = [], invalid = false;
      rule.keys.forEach(function (key) {
        var field = side === 'left' ? key.leftField : key.rightField;
        var value = safeText(row[field]);
        if (!value) { errors.push(issue('GRAIN_VALUE_INVALID', 'Grain values must be non-empty text.', 'rows[' + rowIndex + '].' + field)); invalid = true; }
        keyValues.push(value);
      });
      var transactionField = side === 'left' ? rule.transaction.leftField : rule.transaction.rightField;
      var transactionType = safeText(row[transactionField]).toUpperCase(), sign = rule.transaction.signs[transactionType];
      if (!sign) { errors.push(issue('TRANSACTION_TYPE_UNKNOWN', 'Unknown transactions have no reconciliation effect.', 'rows[' + rowIndex + '].' + transactionField)); invalid = true; }
      var amounts = {};
      rule.measures.forEach(function (measure) {
        var field = side === 'left' ? measure.leftField : measure.rightField;
        var units = decimalUnits(row[field], measure.scale);
        if (units === null) { errors.push(issue('MEASURE_VALUE_INVALID', 'Measure value is not an exact bounded decimal.', 'rows[' + rowIndex + '].' + field)); invalid = true; }
        else if (!Number.isSafeInteger(units * sign)) { errors.push(issue('MEASURE_VALUE_OVERFLOW', 'Signed measure exceeds safe integer range.', 'rows[' + rowIndex + '].' + field)); invalid = true; }
        else amounts[measure.name] = units * sign;
      });
      if (invalid) return;
      var groupKey = JSON.stringify(keyValues);
      if (!groups[groupKey]) { groups[groupKey] = { key: {}, units: {} }; rule.keys.forEach(function (key, index) { groups[groupKey].key[key.name] = keyValues[index]; }); rule.measures.forEach(function (measure) { groups[groupKey].units[measure.name] = 0; }); }
      rule.measures.forEach(function (measure) {
        var next = groups[groupKey].units[measure.name] + amounts[measure.name];
        if (!Number.isSafeInteger(next)) errors.push(issue('AGGREGATE_OVERFLOW', 'Aggregate exceeds safe integer range.', measure.name));
        else groups[groupKey].units[measure.name] = next;
      });
    });
    return Object.assign(result(errors), { reportId: reportId, groups: errors.length ? [] : Object.keys(groups).sort().map(function (key) { return groups[key]; }) });
  }
  function evaluateCoverage(value) {
    var errors = [];
    if (!record(value)) return Object.assign(result([issue('COVERAGE_REQUIRED', 'Coverage evidence is required.', 'coverage')]), { complete: false });
    var status = String(value.status || '').trim().toUpperCase(), start = isoDate(value.periodStart), end = isoDate(value.declaredPeriodEnd);
    if (COVERAGE_STATUSES.indexOf(status) < 0) errors.push(issue('COVERAGE_STATUS_INVALID', 'Coverage status is invalid.', 'status'));
    if (!start || !end || start > end) errors.push(issue('COVERAGE_PERIOD_INVALID', 'A valid selected period is required.', 'period'));
    if (!safeText(value.evidenceId)) errors.push(issue('COVERAGE_EVIDENCE_REQUIRED', 'Stable coverage evidence is required.', 'evidenceId'));
    if (status === 'COMPLETE_WITH_ZERO_ACTIVITY' && value.zeroActivityConfirmed !== true) errors.push(issue('ZERO_ACTIVITY_CONFIRMATION_REQUIRED', 'Zero activity needs signed confirmation.', 'zeroActivityConfirmed'));
    return Object.assign(result(errors), { status: status, periodStart: start, declaredPeriodEnd: end, complete: !errors.length && COMPLETE_STATUSES.indexOf(status) >= 0 });
  }
  function compareReports(leftRows, rightRows, definition, coverage) {
    var ruleCheck = validateRuleDefinition(definition);
    if (!ruleCheck.ok) return { ok: false, status: 'BLOCKED', code: 'RULE_INVALID', errors: ruleCheck.errors, differences: [] };
    var leftCoverage = evaluateCoverage(record(coverage) ? coverage.left : null), rightCoverage = evaluateCoverage(record(coverage) ? coverage.right : null);
    var coverageErrors = leftCoverage.errors.concat(rightCoverage.errors);
    if (coverageErrors.length || !leftCoverage.complete || !rightCoverage.complete) return { ok: true, status: 'BLOCKED', code: 'RECON_INPUT_INCOMPLETE', errors: coverageErrors, differences: [] };
    if (leftCoverage.periodStart !== rightCoverage.periodStart || leftCoverage.declaredPeriodEnd !== rightCoverage.declaredPeriodEnd) return { ok: true, status: 'BLOCKED', code: 'CUTOFF_MISMATCH', errors: [], differences: [] };
    var rule = ruleCheck.rule;
    var left = aggregateReportRows(rule.sourceReports.left, leftRows, rule), right = aggregateReportRows(rule.sourceReports.right, rightRows, rule);
    if (!left.ok || !right.ok) return { ok: false, status: 'BLOCKED', code: 'RECON_ROWS_INVALID', errors: left.errors.concat(right.errors), differences: [] };
    var all = Object.create(null);
    left.groups.forEach(function (group) { all[JSON.stringify(rule.keys.map(function (key) { return group.key[key.name]; }))] = { left: group, right: null }; });
    right.groups.forEach(function (group) { var key = JSON.stringify(rule.keys.map(function (item) { return group.key[item.name]; })); if (!all[key]) all[key] = { left: null, right: group }; else all[key].right = group; });
    var differences = [];
    Object.keys(all).sort().forEach(function (key) {
      rule.measures.forEach(function (measure) {
        var leftUnits = all[key].left ? all[key].left.units[measure.name] : 0, rightUnits = all[key].right ? all[key].right.units[measure.name] : 0;
        var deltaUnits = leftUnits - rightUnits;
        if (Math.abs(deltaUnits) > measure.toleranceUnits) differences.push({ key: JSON.parse(key), measure: measure.name, leftUnits: leftUnits, rightUnits: rightUnits, deltaUnits: deltaUnits, toleranceUnits: measure.toleranceUnits, scale: measure.scale });
      });
    });
    return { ok: true, status: differences.length ? 'FAIL' : 'PASS', code: differences.length ? 'RECON_MISMATCH' : 'RECON_MATCH', errors: [], differences: differences };
  }
  function publicationDecision(value) {
    var reasons = [], warnings = [];
    if (!record(value)) return { status: 'NOT_READY', showValues: false, reasons: ['PUBLICATION_INPUT_REQUIRED'], warnings: [] };
    if (!token(value.storeCode)) reasons.push('STORE_SCOPE_INVALID');
    if (value.factStoreAvailable !== true) reasons.push('FACT_STORE_UNAVAILABLE');
    if (value.reimportRequired === true) reasons.push('REIMPORT_REQUIRED');
    if (value.storeAmbiguous === true) reasons.push('STORE_SCOPE_AMBIGUOUS');
    if (value.piiPolicyViolation === true) reasons.push('PII_POLICY_VIOLATION');
    if (!Array.isArray(value.coverages) || !value.coverages.length) reasons.push('COVERAGE_REQUIRED');
    else value.coverages.forEach(function (coverage) { var checked = evaluateCoverage(coverage); if (!checked.ok || !checked.complete) reasons.push('REQUIRED_SCOPE_INCOMPLETE'); });
    if (!Array.isArray(value.reconciliations) || !value.reconciliations.length) reasons.push('RECONCILIATION_REQUIRED');
    else value.reconciliations.forEach(function (recon) {
      if (!record(recon) || recon.status === 'BLOCKED' || (recon.severity === 'CRITICAL' && recon.status !== 'PASS')) reasons.push('CRITICAL_RECONCILIATION_NOT_PASSED');
      else if (recon.status !== 'PASS') warnings.push('NONCRITICAL_RECONCILIATION_OPEN');
    });
    reasons = reasons.filter(function (value, index, all) { return all.indexOf(value) === index; }).sort();
    warnings = warnings.filter(function (value, index, all) { return all.indexOf(value) === index; }).sort();
    return { status: reasons.length ? 'NOT_READY' : (warnings.length ? 'READY_WITH_WARNINGS' : 'READY'), showValues: !reasons.length, reasons: reasons, warnings: warnings };
  }
  return Object.freeze({ COVERAGE_STATUSES: COVERAGE_STATUSES, validateRuleDefinition: validateRuleDefinition, aggregateReportRows: aggregateReportRows, evaluateCoverage: evaluateCoverage, compareReports: compareReports, publicationDecision: publicationDecision });
});
