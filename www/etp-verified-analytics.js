/* Phase 6H.1 E2: pure, read-only analytics over already verified ETP facts. */
(function (root, factory) {
  'use strict';
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  if (root) root.SaagarEtpVerifiedAnalytics = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';
  var VERSION = 'ETP_E2_ANALYTICS_V1';
  var VIEWS = Object.freeze(['DAY', 'MTD', 'YTD', 'LY']);
  var REPORTS = Object.freeze(['R003', 'R013', 'R022', 'R025']);
  var TENDERS = Object.freeze([
    ['Cash', 'cash_amount'], ['Card', 'card_amount'], ['BHIM UPI', 'bhim_upi_amount'],
    ['PhonePe', 'phonepe_amount'], ['Paytm', 'paytm_amount'], ['Razorpay', 'razorpay_amount'],
    ['BharatPe', 'bharatpe_amount'], ['Cheque', 'cheque_amount'], ['Others', 'others_amount'],
    ['Unmapped', 'payment_type24_amount']
  ]);
  var MAX_ROWS = 50000, MAX_MIX = 20;
  function freeze(value) {
    if (value && typeof value === 'object' && !Object.isFrozen(value)) {
      Object.keys(value).forEach(function (key) { freeze(value[key]); }); Object.freeze(value);
    }
    return value;
  }
  function record(value) { return !!value && typeof value === 'object' && !Array.isArray(value); }
  function fail(code) { return freeze({ ok: false, code: code }); }
  function iso(value) {
    var raw = String(value || ''), match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(raw), date;
    if (!match) return '';
    date = new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3])));
    return date.getUTCFullYear() === Number(match[1]) && date.getUTCMonth() === Number(match[2]) - 1 && date.getUTCDate() === Number(match[3]) ? raw : '';
  }
  function shiftYear(value, amount) {
    var date = new Date(value + 'T00:00:00Z'), month = date.getUTCMonth(), day = date.getUTCDate();
    date.setUTCFullYear(date.getUTCFullYear() + amount);
    if (date.getUTCMonth() !== month) date = new Date(Date.UTC(date.getUTCFullYear(), month + 1, 0));
    else if (date.getUTCDate() !== day) return '';
    return date.toISOString().slice(0, 10);
  }
  function addDays(value, amount) { return new Date(Date.parse(value + 'T00:00:00Z') + amount * 86400000).toISOString().slice(0, 10); }
  function days(left, right) { return Math.max(0, Math.round((Date.parse(right + 'T00:00:00Z') - Date.parse(left + 'T00:00:00Z')) / 86400000)); }
  function units(value, scale) {
    if (value === null || value === undefined || value === '') return 0;
    var raw = typeof value === 'number' && Number.isFinite(value) ? String(value) : typeof value === 'string' ? value.trim() : '';
    var match = /^(-?)(\d+)(?:\.(\d+))?$/.exec(raw);
    if (!match || (match[3] || '').length > scale) return null;
    var fraction = match[3] || ''; while (fraction.length < scale) fraction += '0';
    var result = Number(match[2] + fraction); if (!Number.isSafeInteger(result)) return null;
    return match[1] ? -result : result;
  }
  function scope(value) {
    if (!record(value)) return null;
    var store = String(value.storeCode || ''), fy = String(value.financialYear || ''), start = iso(value.periodStart), end = iso(value.periodEnd);
    if (!/^(?:WLMHW|HEMW)$/.test(store) || !/^\d{4}-\d{2}$/.test(fy) || !start || !end || start > end) return null;
    var key = store + '|' + fy + '|' + start + '..' + end;
    if (value.scopeKey !== undefined && value.scopeKey !== key) return null;
    return freeze({ storeCode: store, financialYear: fy, periodStart: start, periodEnd: end, scopeKey: key });
  }
  function range(view, asOf, current) {
    var start, end = asOf < current.periodEnd ? asOf : current.periodEnd, comparison = false;
    if (view === 'DAY') start = end;
    else if (view === 'MTD') start = end.slice(0, 8) + '01';
    else { start = (Number(end.slice(5, 7)) >= 4 ? end.slice(0, 4) : String(Number(end.slice(0, 4)) - 1)) + '-04-01'; }
    if (view === 'LY') { comparison = true; start = shiftYear(start, -1); end = shiftYear(end, -1); }
    return { start: start, end: end, desiredStart: start, desiredEnd: view === 'LY' ? shiftYear(asOf, -1) : asOf, comparison: comparison };
  }
  function checkedRows(value, storeCode) {
    if (!record(value)) return null; var output = {};
    for (var i = 0; i < REPORTS.length; i++) {
      var id = REPORTS[i], source = value[id]; if (!Array.isArray(source) || source.length > MAX_ROWS) return null;
      output[id] = [];
      for (var n = 0; n < source.length; n++) {
        var row = source[n]; if (!record(row) || row.store_code !== undefined && row.store_code !== storeCode || !iso(row.invoice_date)) return null;
        output[id].push(row);
      }
    }
    return output;
  }
  function selected(rows, start, end) { return rows.filter(function (row) { return row.invoice_date >= start && row.invoice_date <= end; }); }
  function sum(rows, field, scale) {
    var total = 0; for (var i = 0; i < rows.length; i++) { var value = units(rows[i][field], scale); if (value === null || !Number.isSafeInteger(total + value)) return null; total += value; } return total;
  }
  function ratio(numerator, denominator, scale) { return denominator ? Math.round(numerator * scale / denominator) : null; }
  function mix(rows, labelField, valueField) {
    var values = Object.create(null), total = 0;
    for (var i = 0; i < rows.length; i++) {
      var label = String(rows[i][labelField] || '').trim() || 'Unassigned', value = units(rows[i][valueField], 2);
      if (label.length > 80 || value === null || !Number.isSafeInteger(total + value)) return null;
      total += value; values[label] = (values[label] || 0) + value;
    }
    return freeze(Object.keys(values).map(function (label) { return { label: label, value: values[label], shareBps: total ? Math.round(values[label] * 10000 / total) : null }; }).sort(function (a, b) { return Math.abs(b.value) - Math.abs(a.value) || a.label.localeCompare(b.label); }).slice(0, MAX_MIX));
  }
  function tenderMix(rows) {
    var out = [], total = 0; TENDERS.forEach(function (item) { var value = sum(rows, item[1], 2); if (value) { total += value; out.push({ label: item[0], value: value }); } });
    out.forEach(function (item) { item.shareBps = total ? Math.round(item.value * 10000 / total) : null; });
    return freeze(out.sort(function (a, b) { return Math.abs(b.value) - Math.abs(a.value) || a.label.localeCompare(b.label); }));
  }
  function missingModel(input, current, view, period, verifiedThrough, missing) {
    return freeze({ ok: true, analytics: { contractVersion: VERSION, scope: current, view: view,
      period: { start: period.start, end: period.end, label: 'Missing verified coverage', partial: true, comparison: period.comparison },
      verified: { through: verifiedThrough, banner: verifiedThrough ? 'Verified through ' + verifiedThrough + ' · coverage missing' : 'Verified coverage unavailable', pendingDays: verifiedThrough ? days(verifiedThrough, input.asOfDate) : null },
      coverage: { complete: false, missingReports: missing, label: 'Partial period · unavailable values shown as —' },
      metrics: { netSale: null, bills: null, units: null, atv: null, upt: null, asp: null }, mixes: { brand: [], cro: [], tender: [] },
      exceptions: { returns: null, returnsBps: null, manualDiscount: null, paymentType25: Number(input.receipt && input.receipt.exceptions && input.receipt.exceptions.paymentType25 && input.receipt.exceptions.paymentType25.rowCount || 0) },
      identity: { storeNet: null, croAchievement: null, unassigned: null, reconciles: false } } });
  }
  function build(input) {
    if (!record(input) || VIEWS.indexOf(input.view) < 0 || !iso(input.asOfDate)) return fail('ETP_ANALYTICS_REQUEST_INVALID');
    var current = scope(input.scope), verifiedThrough = iso(input.status && input.status.verifiedThrough), receipt = input.receipt;
    if (!current || !verifiedThrough || !record(receipt) || receipt.reconciliationStatus !== 'PASS' || receipt.ruleVersion !== 'rec_002_v1') return fail('ETP_ANALYTICS_NOT_READY');
    var sourceScope = input.view === 'LY' ? scope(input.comparisonScope) : current, sourceRows = input.view === 'LY' ? input.comparisonRows : input.rows;
    var period = range(input.view, input.asOfDate, current), coverage = receipt.coverage || {}, missing = REPORTS.filter(function (id) { return !coverage[id] || ['COMPLETE', 'COMPLETE_WITH_ZERO_ACTIVITY'].indexOf(coverage[id].status) < 0; });
    if (!sourceScope || sourceScope.storeCode !== current.storeCode || period.start < sourceScope.periodStart || period.end > sourceScope.periodEnd || missing.length) return missingModel(input, current, input.view, period, verifiedThrough, missing.length ? missing : REPORTS.slice());
    var rows = checkedRows(sourceRows, current.storeCode); if (!rows) return fail('ETP_ANALYTICS_FACTS_INVALID');
    var r022 = selected(rows.R022, period.start, period.end), r025 = selected(rows.R025, period.start, period.end), r013 = selected(rows.R013, period.start, period.end), r003 = selected(rows.R003, period.start, period.end);
    var net = sum(r022, 'net_value', 2), qty = sum(r025, 'quantity', 3), assigned = sum(r013.filter(function (row) { return String(row.cro_number || '').trim(); }), 'net_amount', 2), returns = sum(r025.filter(function (row) { return String(row.transaction_type_raw || '').toUpperCase() === 'SR'; }), 'net_amount', 2), manual = sum(r003, 'user_discount', 2);
    if ([net, qty, assigned, returns, manual].some(function (value) { return value === null; })) return fail('ETP_ANALYTICS_FACTS_INVALID');
    var invoices = Object.create(null); r022.forEach(function (row) { var id = String(row.invoice_number || '').trim(); if (id) invoices[id] = true; }); var bills = Object.keys(invoices).length;
    var brand = mix(r025, 'brand', 'net_amount'), cro = mix(r013, 'cro_number', 'net_amount'); if (!brand || !cro) return fail('ETP_ANALYTICS_FACTS_INVALID');
    var partial = period.start > period.desiredStart || period.end < period.desiredEnd || verifiedThrough < period.end;
    var unassigned = net - assigned;
    return freeze({ ok: true, analytics: { contractVersion: VERSION, scope: current, view: input.view,
      period: { start: period.start, end: period.end, label: (partial ? 'Partial period · ' : '') + period.start + ' to ' + period.end, partial: partial, comparison: period.comparison },
      verified: { through: verifiedThrough, banner: 'Verified through ' + verifiedThrough + ' · ' + days(verifiedThrough, input.asOfDate) + ' days pending', pendingDays: days(verifiedThrough, input.asOfDate) },
      coverage: { complete: true, missingReports: [], label: partial ? 'Partial period' : 'Complete declared period' },
      metrics: { netSale: net, bills: bills, units: qty, atv: ratio(net, bills, 1), upt: ratio(qty, bills, 1), asp: ratio(net, qty, 1000) },
      mixes: { brand: brand, cro: cro, tender: tenderMix(r022) }, exceptions: { returns: returns, returnsBps: net + Math.abs(returns) ? Math.round(Math.abs(returns) * 10000 / (net + Math.abs(returns))) : null, manualDiscount: manual, paymentType25: Number(receipt.exceptions && receipt.exceptions.paymentType25 && receipt.exceptions.paymentType25.rowCount || 0) },
      identity: { storeNet: net, croAchievement: assigned, unassigned: unassigned, reconciles: net === assigned + unassigned } } });
  }
  return freeze({ VERSION: VERSION, VIEWS: VIEWS, REPORTS: REPORTS, MAX_ROWS: MAX_ROWS, build: build });
});
