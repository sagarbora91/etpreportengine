/* Narrow parent-owned gateway for the modular Retail ETP presentation.
   It deliberately exposes neither the native plugin nor unverified reads. */
(function (root, factory) {
  'use strict';
  var api = factory(root);
  if (typeof module === 'object' && module.exports) module.exports = api;
  if (!root) return;
  root.SaagarEtpModuleGatewayFactory = api;
  var boot = api.bootstrap();
  if (boot.ok) root.SaagarEtpModuleGateway = boot.gateway;
  else root.SaagarEtpModuleGatewayStatus = Object.freeze(boot);
})(typeof globalThis !== 'undefined' ? globalThis : this, function (root) {
  'use strict';

  var GATEWAY_VERSION = 1;
  var REPORTS = Object.freeze(['R003', 'R013', 'R022', 'R025']), MAX_FILES_PER_REPORT = 13, MAX_IMPORT_FILES = REPORTS.length * MAX_FILES_PER_REPORT;
  var REGISTRY_KEY = 'saagar_etp_control_registry_v1';
  var MAX_SCOPES = 20;
  var MAX_HISTORY = 10;
  var GATEWAY_MAX_READ_ROWS = 200;
  var MAX_CHUNK_INDEX = 4095;
  var MAX_ROW_OFFSET = 499;
  var MAX_CELL_TEXT = 4096;
  var SUMMARY_PAGE_LIMIT = 200;
  var SUMMARY_MAX_PAGES = 100;
  var SUMMARY_MAX_ROWS = 20000;
  var SUMMARY_MAX_GROUPS = 100;
  var REPORT_PAGE_LIMIT = 100;
  var ANALYTICS_PAGE_LIMIT = 200;
  var ANALYTICS_MAX_PAGES = 250;
  var PROJECTIONS = Object.freeze({
    R003: Object.freeze(['transaction_type_raw', 'net_amount', 'scheme_discount', 'user_discount']),
    R013: Object.freeze(['invoice_number', 'transaction_type_raw', 'quantity', 'net_amount', 'cro_number']),
    R022: Object.freeze(['transaction_type_raw', 'invoice_quantity', 'net_value', 'cash_amount', 'card_amount', 'bhim_upi_amount', 'phonepe_amount', 'paytm_amount', 'razorpay_amount', 'bharatpe_amount', 'cheque_amount', 'others_amount', 'payment_type24_amount']),
    R025: Object.freeze(['invoice_number', 'transaction_type_raw', 'quantity', 'net_amount', 'brand', 'cluster', 'gender', 'scheme_discount', 'user_discount', 'tax_amount'])
  });
  var ANALYTICS_PROJECTIONS = Object.freeze({
    R003: Object.freeze(['invoice_date'].concat(PROJECTIONS.R003)), R013: Object.freeze(['invoice_date'].concat(PROJECTIONS.R013)),
    R022: Object.freeze(['invoice_date', 'invoice_number'].concat(PROJECTIONS.R022)), R025: Object.freeze(['invoice_date'].concat(PROJECTIONS.R025))
  });
  var FORBIDDEN_FIELD = /(?:^|_)(?:workbook|worksheet|filename|file_label|file_path|source_name|source_bytes|blob|base64|customer|consumer|mobile|phone|email|address|name|aadhaar|pan|dob)(?:$|_)/i;
  var BLOCKED_KEYS = Object.freeze(['__proto__', 'prototype', 'constructor']);

  function record(value) { return !!value && typeof value === 'object' && !Array.isArray(value); }
  function own(value, key) { return Object.prototype.hasOwnProperty.call(value, key); }
  function exact(value, keys) {
    if (!record(value)) return false;
    var actual = Object.keys(value).sort(), expected = keys.slice().sort();
    return actual.length === expected.length && actual.every(function (key, index) { return key === expected[index] && BLOCKED_KEYS.indexOf(key) < 0; });
  }
  function failure(code, stage) { return Object.freeze({ ok: false, code: code, stage: stage || 'GATEWAY' }); }
  function safeCode(value, fallback) {
    var code = String(value || '');
    return /^(?:ETP|XLSX|RETAIL)_[A-Z0-9_]{1,80}$/.test(code) ? code : fallback;
  }
  function cleanFailure(value, fallback, stage) {
    return failure(safeCode(value && value.code, fallback), String(value && value.stage || stage || 'GATEWAY').slice(0, 40));
  }
  function freeze(value) { return Object.freeze(value); }
  function requireMethod(value, name) { return !!value && typeof value[name] === 'function'; }
  function generation(value) { return /^etp_[a-f0-9]{32}$/.test(String(value || '')); }

  function ownerCoverageDeclaration() {
    var reports = {};
    REPORTS.forEach(function (id) { reports[id] = freeze({ status: 'COMPLETE' }); });
    return freeze({ confirmed: true, confirmedByRole: 'OWNER', reports: freeze(reports) });
  }

  function sanitizeScope(checked) {
    var scope = checked && checked.scope;
    if (!record(scope) || typeof checked.key !== 'string') return null;
    return freeze({
      storeCode: String(scope.storeCode),
      financialYear: String(scope.financialYear),
      periodStart: String(scope.periodStart),
      periodEnd: String(scope.periodEnd),
      scopeKey: checked.key
    });
  }

  function sanitizeCoverage(value) {
    var out = {};
    REPORTS.forEach(function (id) {
      var item = value && value[id];
      out[id] = freeze({
        status: String(item && item.status || ''),
        zeroActivityConfirmed: !!(item && item.zeroActivityConfirmed)
      });
    });
    return freeze(out);
  }

  function sanitizeReceipt(value, core) {
    var checked = core.validateReceipt(value);
    if (!checked || !checked.ok) return null;
    var receipt = checked.receipt || value, enrichments = receipt.enrichments || {};
    return freeze({
      contractVersion: String(receipt.contractVersion),
      scopeKey: String(receipt.scopeKey),
      storeCode: String(receipt.storeCode),
      activeGenerationId: String(receipt.activeGenerationId),
      profileVersion: String(receipt.profileVersion),
      parserVersion: String(receipt.parserVersion),
      profileAuthority: freeze({ contractVersion: String(receipt.profileAuthority.contractVersion), authorityId: String(receipt.profileAuthority.authorityId), storeCode: String(receipt.profileAuthority.storeCode), status: String(receipt.profileAuthority.status), purpose: String(receipt.profileAuthority.purpose), profileVersion: String(receipt.profileAuthority.profileVersion), parserVersion: String(receipt.profileAuthority.parserVersion), evidenceIdentity: String(receipt.profileAuthority.evidenceIdentity) }),
      tenderDictionary: freeze({ contractVersion: String(receipt.tenderDictionary.contractVersion), versionId: String(receipt.tenderDictionary.versionId), effectiveAt: String(receipt.tenderDictionary.effectiveAt) }),
      ruleVersion: String(receipt.ruleVersion),
      reconciliationStatus: String(receipt.reconciliationStatus),
      publishedAt: String(receipt.publishedAt),
      coverage: sanitizeCoverage(receipt.coverage),
      exceptions: freeze({
        R003: freeze({ status: String(enrichments.R003.status), differenceCount: Number(enrichments.R003.differenceCount) }),
        R013: freeze({ status: String(enrichments.R013.status), differenceCount: Number(enrichments.R013.differenceCount) }),
        paymentType25: freeze({ status: 'QUARANTINED', rowCount: Number(enrichments.paymentType25.rowCount), persisted: false })
      })
    });
  }

  function loadRegistry(storage) {
    try {
      var value = JSON.parse(storage.getItem(REGISTRY_KEY) || '{"scopes":{}}');
      return record(value) && record(value.scopes) ? value.scopes : {};
    } catch (_) { return {}; }
  }

  function safePrimitive(value) {
    return value === null || (typeof value === 'string' && value.length <= MAX_CELL_TEXT) ||
      (typeof value === 'number' && Number.isFinite(value)) || typeof value === 'boolean';
  }

  function canonicalInvoiceDate(value) {
    var raw = String(value === null || value === undefined ? '' : value).trim(), match = /^(\d{4})(?:-?)(\d{2})(?:-?)(\d{2})$/.exec(raw), date;
    if (!match) return '';
    date = new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3])));
    if (date.getUTCFullYear() !== Number(match[1]) || date.getUTCMonth() !== Number(match[2]) - 1 || date.getUTCDate() !== Number(match[3])) return '';
    return match[1] + '-' + match[2] + '-' + match[3];
  }

  function sanitizePage(value, scopeKey, generationId, reportId, fields, limit) {
    var page = value && value.page;
    if (!value || value.ok !== true || !exact(page, ['scopeKey', 'generationId', 'reportId', 'rows', 'hasMore', 'nextCursor'])) return null;
    if (page.scopeKey !== scopeKey || page.generationId !== generationId || page.reportId !== reportId || !Array.isArray(page.rows) || page.rows.length > limit || typeof page.hasMore !== 'boolean') return null;
    var allowed = Object.create(null); fields.forEach(function (field) { allowed[field] = true; });
    var rows = [];
    for (var i = 0; i < page.rows.length; i++) {
      var source = page.rows[i];
      if (!record(source)) return null;
      var row = {}, keys = Object.keys(source);
      if (keys.length !== fields.length || fields.some(function (field) { return !own(source, field); })) return null;
      for (var n = 0; n < keys.length; n++) {
        var key = keys[n];
        if (!allowed[key] || FORBIDDEN_FIELD.test(key) || BLOCKED_KEYS.indexOf(key) >= 0 || !safePrimitive(source[key])) return null;
        if (key === 'invoice_date') {
          var normalizedDate = canonicalInvoiceDate(source[key]);
          if (!normalizedDate) return null;
          row[key] = normalizedDate;
        } else row[key] = source[key];
      }
      rows.push(freeze(row));
    }
    var cursor = null;
    if (page.hasMore) {
      if (!exact(page.nextCursor, ['chunkIndex', 'rowOffset']) || !Number.isSafeInteger(page.nextCursor.chunkIndex) || page.nextCursor.chunkIndex < 0 || page.nextCursor.chunkIndex > MAX_CHUNK_INDEX || !Number.isSafeInteger(page.nextCursor.rowOffset) || page.nextCursor.rowOffset < 0 || page.nextCursor.rowOffset > MAX_ROW_OFFSET) return null;
      cursor = freeze({ chunkIndex: page.nextCursor.chunkIndex, rowOffset: page.nextCursor.rowOffset });
    } else if (page.nextCursor !== null) return null;
    return freeze({ scopeKey: scopeKey, generationId: generationId, reportId: reportId, rows: freeze(rows), hasMore: page.hasMore, nextCursor: cursor });
  }

  function filterValue(rowValue, filter) {
    var actual = String(rowValue == null ? '' : rowValue), expected = filter.value;
    if (filter.operator === 'EQ') return actual === expected;
    if (filter.operator === 'IN') return expected.indexOf(actual) >= 0;
    if (filter.operator === 'GTE') return actual >= expected;
    if (filter.operator === 'LTE') return actual <= expected;
    return false;
  }
  function compareValue(left, right) {
    var leftNumber = typeof left === 'number' ? left : Number(String(left == null ? '' : left));
    var rightNumber = typeof right === 'number' ? right : Number(String(right == null ? '' : right));
    if (Number.isFinite(leftNumber) && Number.isFinite(rightNumber)) return leftNumber - rightNumber;
    return String(left == null ? '' : left).localeCompare(String(right == null ? '' : right));
  }
  function projectRows(rows, query) {
    var filtered = rows.filter(function (row) { return query.filters.every(function (item) { return filterValue(row[item.field], item); }); });
    if (query.sort.length) filtered = filtered.map(function (row, index) { return { row: row, index: index }; }).sort(function (left, right) {
      for (var i = 0; i < query.sort.length; i++) { var item = query.sort[i], compared = compareValue(left.row[item.field], right.row[item.field]); if (compared) return item.direction === 'DESC' ? -compared : compared; }
      return left.index - right.index;
    }).map(function (item) { return item.row; });
    return freeze(filtered.map(function (source) { var row = {}; query.fields.forEach(function (field) { row[field] = source[field]; }); return freeze(row); }));
  }

  function summaryUnits(value, scale, optional) {
    if ((value === null || value === undefined || value === '') && optional) return 0;
    var raw = typeof value === 'number' && Number.isFinite(value) ? String(value) : (typeof value === 'string' ? value.trim() : '');
    var match = /^(-?)(\d+)(?:\.(\d+))?$/.exec(raw);
    if (!match || (match[3] || '').length > scale) return null;
    var fraction = match[3] || ''; while (fraction.length < scale) fraction += '0';
    var number = Number(match[2] + fraction);
    return Number.isSafeInteger(number) ? (match[1] ? -number : number) : null;
  }
  function summaryAdd(target, key, value) { var next = target[key] + value; if (!Number.isSafeInteger(next)) return false; target[key] = next; return true; }
  function summaryLabel(value, fallback) { var out = String(value == null ? '' : value).trim().replace(/\s+/g, ' '); return out && out.length <= 80 ? out : fallback; }
  function createSummary(reportId) {
    var money = { R003: ['net_amount', 'scheme_discount', 'user_discount'], R013: ['net_amount'], R022: ['net_value', 'cash_amount', 'card_amount', 'bhim_upi_amount', 'phonepe_amount', 'paytm_amount', 'razorpay_amount', 'bharatpe_amount', 'cheque_amount', 'others_amount', 'payment_type24_amount'], R025: ['net_amount', 'scheme_discount', 'user_discount', 'tax_amount'] };
    var sums = {}; money[reportId].forEach(function (key) { sums[key] = 0; });
    return { reportId: reportId, rows: 0, sums: sums, quantity: 0, groups: Object.create(null), overflow: { rows: 0, net: 0 }, moneyFields: money[reportId], quantityField: { R022: 'invoice_quantity', R025: 'quantity', R013: 'quantity' }[reportId] || null };
  }
  function appendSummary(acc, rows) {
    var signs = { INV: 1, SR: -1, BC: -1 };
    for (var i = 0; i < rows.length; i++) {
      var row = rows[i], sign = signs[String(row.transaction_type_raw || '').toUpperCase()];
      if (!sign) return failure('ETP_SUMMARY_ROW_INVALID', 'SUMMARY');
      var money = {};
      for (var n = 0; n < acc.moneyFields.length; n++) { var field = acc.moneyFields[n], parsed = summaryUnits(row[field], 2, field !== 'net_value' && field !== 'net_amount'); if (parsed === null || !Number.isSafeInteger(parsed * sign)) return failure('ETP_SUMMARY_ROW_INVALID', 'SUMMARY'); money[field] = parsed * sign; }
      if (acc.quantityField) { var quantity = summaryUnits(row[acc.quantityField], 3, false); if (quantity === null || !summaryAdd(acc, 'quantity', quantity * sign)) return failure('ETP_SUMMARY_AGGREGATE_OVERFLOW', 'SUMMARY'); }
      for (var key in money) if (own(money, key) && !summaryAdd(acc.sums, key, money[key])) return failure('ETP_SUMMARY_AGGREGATE_OVERFLOW', 'SUMMARY');
      acc.rows++;
      var groupKey = acc.reportId === 'R025' ? summaryLabel(row.brand, 'Unspecified brand') : (acc.reportId === 'R013' ? summaryLabel(row.cro_number, 'Unassigned CRO') : '');
      if (groupKey) { var net = money.net_value === undefined ? (money.net_amount || 0) : money.net_value, item = acc.groups[groupKey]; if (!item) { if (Object.keys(acc.groups).length >= SUMMARY_MAX_GROUPS) { acc.overflow.rows++; if (!summaryAdd(acc.overflow, 'net', net)) return failure('ETP_SUMMARY_AGGREGATE_OVERFLOW', 'SUMMARY'); continue; } item = acc.groups[groupKey] = { label: groupKey, rows: 0, net: 0 }; } item.rows++; if (!summaryAdd(item, 'net', net)) return failure('ETP_SUMMARY_AGGREGATE_OVERFLOW', 'SUMMARY'); }
    }
    return { ok: true };
  }
  function finishSummary(acc) {
    var groups = Object.keys(acc.groups).map(function (key) { return acc.groups[key]; }).sort(function (a, b) { return Math.abs(b.net) - Math.abs(a.net) || a.label.localeCompare(b.label); }).slice(0, 10).map(function (item) { return freeze({ label: item.label, rowCount: item.rows, netUnits: item.net }); });
    if (acc.overflow.rows) groups.push(freeze({ label: 'Other verified groups', rowCount: acc.overflow.rows, netUnits: acc.overflow.net }));
    var sums = {}; Object.keys(acc.sums).forEach(function (key) { sums[key] = acc.sums[key]; });
    return freeze({ reportId: acc.reportId, rowCount: acc.rows, quantityUnits: acc.quantity, moneyUnits: freeze(sums), groups: freeze(groups) });
  }

  function create(options) {
    options = options || {};
    var runtime = options.runtime, lifecycle = options.lifecyclePolicy, core = options.core, foundationStatus = options.foundationStatus, queryContract = options.queryContract, analyticsApi = options.analyticsApi, profileAuthority = options.profileAuthority, historyApi = options.importHistoryApi, tenderApi = options.tenderDictionaryApi;
    var storage = options.storage, statusReader = options.statusReader, tokenFactory = options.tokenFactory, authorize = options.authorize;
    if (!requireMethod(runtime, 'run') || !requireMethod(runtime, 'confirm') || !requireMethod(runtime, 'readVerified') ||
        !requireMethod(lifecycle, 'validateScope') || !requireMethod(core, 'validateReceipt') || !requireMethod(foundationStatus, 'evaluate') || !requireMethod(queryContract, 'canonicalize') || !requireMethod(queryContract, 'validateCursorBinding') || !requireMethod(queryContract, 'cursorBindingMatches') || !requireMethod(profileAuthority, 'authorize') || !historyApi || typeof historyApi.create !== 'function' || !tenderApi || typeof tenderApi.validate !== 'function' || !tenderApi.validate(tenderApi.BUILD_DICTIONARY).ok ||
        !storage || typeof storage.getItem !== 'function' || typeof statusReader !== 'function' || typeof authorize !== 'function') {
      return failure('ETP_GATEWAY_DEPENDENCY_INVALID', 'CREATE');
    }
    var historyMade = historyApi.create({ storage: storage }); if (!historyMade || !historyMade.ok) return failure('ETP_GATEWAY_DEPENDENCY_INVALID', 'CREATE');
    var pending = Object.create(null), cursors = Object.create(null), cursorOrder = [], sequence = 0;

    function permitted(action) {
      try { return authorize(action) === true; } catch (_) { return false; }
    }
    async function permittedAsync(action) {
      try { return await authorize(action) === true; } catch (_) { return false; }
    }

    function checkedScope(scope) {
      var checked;
      try { checked = lifecycle.validateScope(scope); } catch (_) { return null; }
      return checked && checked.ok ? { checked: checked, scope: sanitizeScope(checked) } : null;
    }
    function currentRaw(scopeKey) {
      var item = loadRegistry(storage)[scopeKey];
      if (!record(item) || !record(item.current)) return null;
      return core.validateReceipt(item.current).ok ? item.current : null;
    }
    async function verifiedContext(scope) {
      var normalized = checkedScope(scope);
      if (!normalized) return { error: failure('ETP_SCOPE_INVALID', 'VERIFY') };
      var receipt = currentRaw(normalized.scope.scopeKey);
      if (!receipt) return { error: failure('ETP_RECEIPT_NOT_FOUND', 'VERIFY') };
      var status;
      try { status = await statusReader(normalized.checked.scope); } catch (_) { return { error: failure('ETP_STATUS_UNAVAILABLE', 'VERIFY') }; }
      if (!status || status.ok !== true || !record(status.status)) return { error: cleanFailure(status, 'ETP_STATUS_UNAVAILABLE', 'VERIFY') };
      var nativeStatus = status.status;
      if (nativeStatus.state !== 'ACCEPTED' || nativeStatus.restoreFence !== false || !generation(nativeStatus.activeGenerationId) || nativeStatus.activeGenerationId !== receipt.activeGenerationId) {
        return { error: failure(nativeStatus.restoreFence ? 'ETP_REIMPORT_REQUIRED' : 'ETP_VERIFIED_GENERATION_UNAVAILABLE', 'VERIFY') };
      }
      return { normalized: normalized, receipt: receipt, status: nativeStatus };
    }
    function makeToken() {
      var candidate = '';
      if (typeof tokenFactory === 'function') candidate = String(tokenFactory());
      else {
        var cryptoApi = options.crypto || (root && root.crypto), bytes = new Uint8Array(16);
        if (cryptoApi && typeof cryptoApi.getRandomValues === 'function') {
          cryptoApi.getRandomValues(bytes);
          for (var i = 0; i < bytes.length; i++) candidate += ('0' + bytes[i].toString(16)).slice(-2);
        }
      }
      sequence += 1;
      return /^[a-f0-9]{32}$/.test(candidate) ? 'confirm_' + candidate + '_' + sequence : '';
    }
    function makeCursorToken() {
      var candidate = '';
      if (typeof tokenFactory === 'function') candidate = String(tokenFactory());
      else {
        var cryptoApi = options.crypto || (root && root.crypto), bytes = new Uint8Array(16);
        if (cryptoApi && typeof cryptoApi.getRandomValues === 'function') { cryptoApi.getRandomValues(bytes); for (var i = 0; i < bytes.length; i++) candidate += ('0' + bytes[i].toString(16)).slice(-2); }
      }
      sequence += 1;
      return /^[a-f0-9]{32}$/.test(candidate) ? 'cur_' + candidate + '_' + sequence : '';
    }
    function issueCursor(context, coordinate) {
      var token = makeCursorToken(); if (!token) return '';
      var checked = queryContract.validateCursorBinding({ contractVersion: queryContract.VERSION, token: token, scopeKey: context.scopeKey, generationId: context.generationId, reportId: context.reportId, querySignatureInput: context.querySignatureInput });
      if (!checked.ok) return '';
      cursors[token] = { binding: checked.binding, coordinate: freeze({ chunkIndex: coordinate.chunkIndex, rowOffset: coordinate.rowOffset }) };
      cursorOrder.push(token); if (cursorOrder.length > 512) delete cursors[cursorOrder.shift()];
      return token;
    }
    function consumeCursor(token, context) {
      var stored = cursors[token];
      if (!stored || !queryContract.cursorBindingMatches(stored.binding, context)) return null;
      delete cursors[token];
      var index = cursorOrder.indexOf(token); if (index >= 0) cursorOrder.splice(index, 1);
      return stored.coordinate;
    }

    async function run(request) {
      if (!await permittedAsync('IMPORT')) return failure('ETP_ACCESS_DENIED', 'AUTHORIZE');
      if (!exact(request, ['scope', 'files', 'coverageConfirmed']) || request.coverageConfirmed !== true || !Array.isArray(request.files) || request.files.length < REPORTS.length || request.files.length > MAX_IMPORT_FILES) return failure('ETP_IMPORT_REQUEST_INVALID', 'SELECT');
      var normalized = checkedScope(request.scope);
      if (!normalized) return failure('ETP_SCOPE_INVALID', 'SELECT');
      var profileDecision = profileAuthority.authorize({ storeCode: normalized.scope.storeCode, purpose: 'PRODUCTION', profileVersion: profileAuthority.PROFILE_VERSION, parserVersion: profileAuthority.PARSER_VERSION });
      if (!profileDecision || profileDecision.ok !== true) return failure(profileDecision && profileDecision.code || 'ETP_PROFILE_AUTHORIZATION_REQUIRED', 'SELECT');
      var counts = Object.create(null), files = [];
      for (var i = 0; i < request.files.length; i++) {
        var item = request.files[i], id = String(item && item.selectedReportId || '').toUpperCase();
        counts[id] = (counts[id] || 0) + 1;
        if (!exact(item, ['selectedReportId', 'file']) || REPORTS.indexOf(id) < 0 || counts[id] > MAX_FILES_PER_REPORT || !item.file) return failure('ETP_REPORT_SELECTION_INVALID', 'SELECT');
        files.push({ selectedReportId: id, file: item.file });
      }
      if (!REPORTS.every(function (id) { return counts[id] > 0; })) return failure('ETP_REPORT_SELECTION_INVALID', 'SELECT');
      var result;
      try { result = await runtime.run({ scope: normalized.checked.scope, files: files, coverageDeclaration: ownerCoverageDeclaration() }); }
      catch (_) { return failure('ETP_IMPORT_FAILED', 'IMPORT'); }
      if (!result || result.ok !== true) return cleanFailure(result, 'ETP_IMPORT_FAILED', 'IMPORT');
      if (result.awaitingConfirmation === true) {
        if (!record(result.lifecycle) || result.lifecycle.state !== 'AWAITING_CONFIRMATION' || result.lifecycle.scopeKey !== normalized.scope.scopeKey || !generation(result.lifecycle.candidateGenerationId)) return failure('ETP_GATEWAY_RESPONSE_INVALID', 'IMPORT');
        var token = makeToken();
        if (!token) return failure('ETP_GATEWAY_ENTROPY_UNAVAILABLE', 'IMPORT');
        pending[token] = result.lifecycle;
        return freeze({ ok: true, state: 'AWAITING_CONFIRMATION', changed: false, scope: normalized.scope, confirmationToken: token });
      }
      if (result.duplicate === true && result.changed === false) return freeze({ ok: true, state: 'DUPLICATE_NOOP', changed: false, scope: normalized.scope });
      return failure('ETP_GATEWAY_RESPONSE_INVALID', 'IMPORT');
    }

    async function confirm(request) {
      if (!await permittedAsync('CONFIRM')) return failure('ETP_ACCESS_DENIED', 'AUTHORIZE');
      if (!exact(request, ['confirmationToken']) || typeof request.confirmationToken !== 'string') return failure('ETP_CONFIRMATION_TOKEN_INVALID', 'CONFIRM');
      var life = pending[request.confirmationToken];
      if (!life) return failure('ETP_CONFIRMATION_TOKEN_INVALID', 'CONFIRM');
      delete pending[request.confirmationToken];
      var result;
      try { result = await runtime.confirm(life); } catch (_) { return failure('ETP_CONFIRMATION_FAILED', 'CONFIRM'); }
      if (!result || result.ok !== true || result.changed !== true || !record(result.lifecycle) || result.lifecycle.state !== 'ACCEPTED') return cleanFailure(result, 'ETP_CONFIRMATION_FAILED', 'CONFIRM');
      var normalized = checkedScope(result.lifecycle.scope);
      if (!normalized || normalized.scope.scopeKey !== life.scopeKey || result.lifecycle.activeGenerationId !== life.candidateGenerationId) return failure('ETP_GATEWAY_RESPONSE_INVALID', 'CONFIRM');
      return freeze({ ok: true, state: 'ACCEPTED', changed: true, scope: normalized.scope, activeGenerationId: result.lifecycle.activeGenerationId });
    }

    async function inspectScope(scope, request) {
      if (!permitted('READ')) return failure('ETP_ACCESS_DENIED', 'AUTHORIZE');
      if (request !== undefined && (!record(request) || Object.keys(request).some(function (key) { return key !== 'historyLimit'; }))) return failure('ETP_GATEWAY_REQUEST_INVALID', 'INSPECT');
      var limit = request && request.historyLimit !== undefined ? request.historyLimit : 5;
      if (!Number.isSafeInteger(limit) || limit < 0 || limit > MAX_HISTORY) return failure('ETP_HISTORY_LIMIT_INVALID', 'INSPECT');
      var context = await verifiedContext(scope);
      if (context.error) return context.error;
      var item = loadRegistry(storage)[context.normalized.scope.scopeKey], history = Array.isArray(item && item.history) ? item.history : [], cleanHistory = [];
      for (var i = 0; i < history.length && cleanHistory.length < limit; i++) {
        var clean = sanitizeReceipt(history[i], core);
        if (clean) cleanHistory.push(clean);
      }
      var currentReceipt = sanitizeReceipt(context.receipt, core);
      var statusPublishedAt = currentReceipt && /^\d{4}-\d{2}-\d{2}$/.test(currentReceipt.publishedAt) ? currentReceipt.publishedAt + 'T00:00:00Z' : (currentReceipt && currentReceipt.publishedAt);
      var status = foundationStatus.evaluate({ contractVersion: foundationStatus.VERSION, scope: context.normalized.scope, factStoreAvailable: true, nativeStatus: freeze({ state: context.status.state, restoreFence: context.status.restoreFence, activeGenerationId: context.status.activeGenerationId }), receipt: currentReceipt ? freeze({ activeGenerationId: currentReceipt.activeGenerationId, reconciliationStatus: currentReceipt.reconciliationStatus, coverage: currentReceipt.coverage, exceptions: currentReceipt.exceptions, profileVersion: currentReceipt.profileVersion, ruleVersion: currentReceipt.ruleVersion, publishedAt: statusPublishedAt }) : null });
      if (!status || status.ok !== true) return failure('ETP_FOUNDATION_STATUS_INVALID', 'INSPECT');
      var importHistory = historyMade.history.list(context.normalized.scope.scopeKey, limit); if (!importHistory.ok) return failure('ETP_IMPORT_HISTORY_INVALID', 'INSPECT');
      return freeze({ ok: true, scope: context.normalized.scope, status: status, currentReceipt: currentReceipt, history: freeze(cleanHistory), importHistory: importHistory.events });
    }

    function listScopes(request) {
      if (!permitted('READ')) return failure('ETP_ACCESS_DENIED', 'AUTHORIZE');
      if (request !== undefined && (!exact(request, ['limit']) || !Number.isSafeInteger(request.limit) || request.limit < 1 || request.limit > MAX_SCOPES)) return failure('ETP_SCOPE_LIMIT_INVALID', 'LIST');
      var limit = request ? request.limit : MAX_SCOPES, registry = loadRegistry(storage), scopes = [];
      Object.keys(registry).sort().some(function (key) {
        var item = registry[key], receipt = item && sanitizeReceipt(item.current, core);
        if (!receipt) return false;
        var checked = checkedScope(item.current.lifecycle && item.current.lifecycle.scope);
        if (!checked || checked.scope.scopeKey !== key) return false;
        scopes.push(freeze({ scope: checked.scope, publishedAt: receipt.publishedAt, state: 'RECEIPT_PRESENT' }));
        return scopes.length >= limit;
      });
      return freeze({ ok: true, scopes: freeze(scopes) });
    }

    async function readVerified(scope, request) {
      if (!permitted('READ')) return failure('ETP_ACCESS_DENIED', 'AUTHORIZE');
      if (!exact(request, ['reportId', 'fields', 'cursor', 'limit'])) return failure('ETP_VERIFIED_PROJECTION_INVALID', 'READ');
      var reportId = String(request.reportId || '').toUpperCase(), fields = request.fields;
      if (REPORTS.indexOf(reportId) < 0 || !Array.isArray(fields) || !fields.length || fields.length > 64 || !Number.isSafeInteger(request.limit) || request.limit < 1 || request.limit > GATEWAY_MAX_READ_ROWS) return failure('ETP_VERIFIED_PROJECTION_INVALID', 'READ');
      var seen = Object.create(null), projected = [], allowed = ANALYTICS_PROJECTIONS[reportId];
      for (var i = 0; i < fields.length; i++) {
        var field = String(fields[i]);
        if (!/^[a-z][a-z0-9_]{0,63}$/.test(field) || FORBIDDEN_FIELD.test(field) || allowed.indexOf(field) < 0 || seen[field]) return failure('ETP_VERIFIED_PROJECTION_INVALID', 'READ');
        seen[field] = true; projected.push(field);
      }
      var canonical = queryContract.canonicalize({ contractVersion: queryContract.VERSION, reportId: reportId, fields: projected, filters: [], sort: [], limit: request.limit, cursor: request.cursor });
      if (!canonical.ok) return failure(canonical.code === 'ETP_QUERY_CURSOR_INVALID' ? 'ETP_READ_CURSOR_INVALID' : 'ETP_VERIFIED_PROJECTION_INVALID', 'READ');
      var context = await verifiedContext(scope);
      if (context.error) return context.error;
      var cursorContext = { scopeKey: context.normalized.scope.scopeKey, generationId: context.receipt.activeGenerationId, reportId: reportId, querySignatureInput: canonical.signatureInput };
      var rawCursor = null;
      if (request.cursor !== null) { rawCursor = consumeCursor(request.cursor, cursorContext); if (!rawCursor) return failure('ETP_READ_CURSOR_INVALID', 'READ'); }
      var result;
      try { result = await runtime.readVerified(context.normalized.checked.scope, { reportId: reportId, fields: projected, cursor: rawCursor, limit: request.limit }); }
      catch (_) { return failure('ETP_VERIFIED_READ_FAILED', 'READ'); }
      if (!result || result.ok !== true) return cleanFailure(result, 'ETP_VERIFIED_READ_FAILED', 'READ');
      var page = sanitizePage(result, context.normalized.scope.scopeKey, context.receipt.activeGenerationId, reportId, projected, request.limit);
      if (!page) return failure('ETP_GATEWAY_RESPONSE_INVALID', 'READ');
      var nextCursor = null;
      if (page.hasMore) { nextCursor = issueCursor(cursorContext, page.nextCursor); if (!nextCursor) return failure('ETP_GATEWAY_ENTROPY_UNAVAILABLE', 'READ'); }
      return freeze({ ok: true, page: freeze({ scopeKey: page.scopeKey, generationId: page.generationId, reportId: page.reportId, rows: page.rows, hasMore: page.hasMore, nextCursor: nextCursor }) });
    }

    /* One native page in, one sanitized report page out. Filtering and ordering
       are parent-owned and bounded to that page; no fact-store handle crosses. */
    async function queryReport(scope, request) {
      if (!permitted('READ')) return failure('ETP_ACCESS_DENIED', 'AUTHORIZE');
      var canonical = queryContract.canonicalize(request);
      if (!canonical.ok || canonical.query.limit > REPORT_PAGE_LIMIT) return failure(canonical.code || 'ETP_REPORT_QUERY_INVALID', 'REPORT');
      var query = canonical.query, context = await verifiedContext(scope);
      if (context.error) return context.error;
      var inspected = await inspectScope(scope, { historyLimit: 0 });
      if (!inspected.ok) return inspected;
      if (inspected.status.status === 'NOT_READY' || inspected.status.showValues !== true || inspected.currentReceipt.reconciliationStatus !== 'PASS') return failure('ETP_REPORT_NOT_READY', 'REPORT');
      var cursorContext = { scopeKey: context.normalized.scope.scopeKey, generationId: context.receipt.activeGenerationId, reportId: query.reportId, querySignatureInput: canonical.signatureInput };
      var rawCursor = null;
      if (query.cursor !== null) { rawCursor = consumeCursor(query.cursor, cursorContext); if (!rawCursor) return failure('ETP_REPORT_CURSOR_INVALID', 'REPORT'); }
      var internalFields = query.fields.slice();
      query.filters.concat(query.sort).forEach(function (item) { if (internalFields.indexOf(item.field) < 0) internalFields.push(item.field); });
      var result;
      try { result = await runtime.readVerified(context.normalized.checked.scope, { reportId: query.reportId, fields: internalFields, cursor: rawCursor, limit: query.limit }); }
      catch (_) { return failure('ETP_REPORT_READ_FAILED', 'REPORT'); }
      if (!result || result.ok !== true) return cleanFailure(result, 'ETP_REPORT_READ_FAILED', 'REPORT');
      var page = sanitizePage(result, context.normalized.scope.scopeKey, context.receipt.activeGenerationId, query.reportId, internalFields, query.limit);
      if (!page) return failure('ETP_GATEWAY_RESPONSE_INVALID', 'REPORT');
      var nextCursor = null;
      if (page.hasMore) { nextCursor = issueCursor(cursorContext, page.nextCursor); if (!nextCursor) return failure('ETP_GATEWAY_ENTROPY_UNAVAILABLE', 'REPORT'); }
      return freeze({ ok: true, scope: inspected.scope, status: inspected.status, receipt: inspected.currentReceipt, query: freeze({ reportId: query.reportId, fields: query.fields, filters: query.filters, sort: query.sort, limit: query.limit }), page: freeze({ scopeKey: page.scopeKey, generationId: page.generationId, reportId: page.reportId, rows: projectRows(page.rows, query), scannedRows: page.rows.length, filterOrderScope: 'SOURCE_PAGE_ONLY', totals: 'NOT_PROVIDED', hasMore: page.hasMore, nextCursor: nextCursor }) });
    }

    async function loadSummary(scope) {
      var inspected = await inspectScope(scope, { historyLimit: 5 });
      if (!inspected.ok) return inspected;
      if (inspected.status.status === 'NOT_READY' || inspected.status.showValues !== true) return failure('ETP_SUMMARY_NOT_READY', 'SUMMARY');
      var summaries = {}, pages = 0, totalRows = 0;
      for (var r = 0; r < REPORTS.length; r++) {
        var reportId = REPORTS[r], acc = createSummary(reportId), cursor = null, seen = Object.create(null), more = true;
        while (more) {
          if (pages >= SUMMARY_MAX_PAGES || totalRows >= SUMMARY_MAX_ROWS) return failure('ETP_SUMMARY_REFRESH_LIMIT_EXCEEDED', 'SUMMARY');
          if (cursor !== null) { if (seen[cursor]) return failure('ETP_SUMMARY_CURSOR_REPEATED', 'SUMMARY'); seen[cursor] = true; }
          var result = await readVerified(scope, { reportId: reportId, fields: PROJECTIONS[reportId].slice(), cursor: cursor, limit: SUMMARY_PAGE_LIMIT });
          if (!result.ok || !result.page || !Array.isArray(result.page.rows)) return failure(result.code || 'ETP_SUMMARY_READ_FAILED', 'SUMMARY');
          pages++; totalRows += result.page.rows.length; if (totalRows > SUMMARY_MAX_ROWS) return failure('ETP_SUMMARY_REFRESH_LIMIT_EXCEEDED', 'SUMMARY');
          var appended = appendSummary(acc, result.page.rows); if (!appended.ok) return appended;
          more = result.page.hasMore === true; cursor = more ? result.page.nextCursor : null; if (more && !queryContract.isOpaqueToken(cursor)) return failure('ETP_SUMMARY_CURSOR_INVALID', 'SUMMARY');
        }
        summaries[reportId] = finishSummary(acc);
      }
      return freeze({ ok: true, scope: inspected.scope, status: inspected.status, receipt: inspected.currentReceipt, history: inspected.history, importHistory: inspected.importHistory, summaries: freeze(summaries), pages: pages, rowCount: totalRows });
    }

    async function analyticsRows(sourceScope) {
      var rows = {}, pages = 0;
      for (var r = 0; r < REPORTS.length; r++) {
        var reportId = REPORTS[r], cursor = null, more = true, collected = [];
        while (more) {
          if (pages >= ANALYTICS_MAX_PAGES || collected.length >= (analyticsApi && analyticsApi.MAX_ROWS || 50000)) return failure('ETP_ANALYTICS_REFRESH_LIMIT_EXCEEDED', 'ANALYTICS');
          var result = await readVerified(sourceScope, { reportId: reportId, fields: ANALYTICS_PROJECTIONS[reportId].slice(), cursor: cursor, limit: ANALYTICS_PAGE_LIMIT });
          if (!result.ok || !result.page || !Array.isArray(result.page.rows)) return failure(result.code || 'ETP_ANALYTICS_READ_FAILED', 'ANALYTICS');
          pages++; collected = collected.concat(result.page.rows.map(function (row) { return Object.assign({ store_code: sourceScope.storeCode }, row); }));
          more = result.page.hasMore === true; cursor = more ? result.page.nextCursor : null;
        }
        rows[reportId] = freeze(collected);
      }
      return freeze({ ok: true, rows: freeze(rows) });
    }

    async function loadAnalytics(scope, request) {
      if (!analyticsApi || typeof analyticsApi.build !== 'function') return failure('ETP_ANALYTICS_UNAVAILABLE', 'ANALYTICS');
      if (!exact(request, ['view', 'asOfDate']) || analyticsApi.VIEWS.indexOf(request.view) < 0 || !/^\d{4}-\d{2}-\d{2}$/.test(request.asOfDate)) return failure('ETP_ANALYTICS_REQUEST_INVALID', 'ANALYTICS');
      var inspected = await inspectScope(scope, { historyLimit: 0 }); if (!inspected.ok) return inspected;
      if (inspected.status.showValues !== true || inspected.currentReceipt.reconciliationStatus !== 'PASS') return failure('ETP_ANALYTICS_NOT_READY', 'ANALYTICS');
      var sourceScope = inspected.scope, comparisonScope = null;
      if (request.view === 'LY') {
        var startYear = Number(sourceScope.financialYear.slice(0, 4)) - 1, comparisonFy = String(startYear) + '-' + String(startYear + 1).slice(-2);
        var comparisonStart = new Date(Date.parse(sourceScope.periodStart + 'T00:00:00Z')); comparisonStart.setUTCFullYear(comparisonStart.getUTCFullYear() - 1);
        var comparisonEnd = new Date(Date.parse(sourceScope.periodEnd + 'T00:00:00Z')); comparisonEnd.setUTCFullYear(comparisonEnd.getUTCFullYear() - 1);
        var expectedKey = sourceScope.storeCode + '|' + comparisonFy + '|' + comparisonStart.toISOString().slice(0, 10) + '..' + comparisonEnd.toISOString().slice(0, 10), registry = loadRegistry(storage), item = registry[expectedKey];
        var checked = item && checkedScope(item.current && item.current.lifecycle && item.current.lifecycle.scope);
        if (checked && checked.scope.scopeKey === expectedKey) { var prior = await inspectScope(checked.scope, { historyLimit: 0 }); if (prior.ok && prior.status.showValues === true && prior.currentReceipt.reconciliationStatus === 'PASS') comparisonScope = prior.scope; }
        if (!comparisonScope) return analyticsApi.build({ scope: inspected.scope, status: inspected.status, receipt: inspected.currentReceipt, asOfDate: request.asOfDate, view: request.view });
        sourceScope = comparisonScope;
      }
      var loaded = await analyticsRows(sourceScope); if (!loaded.ok) return loaded;
      var built = analyticsApi.build({ scope: inspected.scope, status: inspected.status, receipt: inspected.currentReceipt, asOfDate: request.asOfDate, view: request.view, rows: request.view === 'LY' ? undefined : loaded.rows, comparisonScope: comparisonScope, comparisonRows: request.view === 'LY' ? loaded.rows : undefined });
      return built && built.ok === true ? freeze({ ok: true, analytics: built.analytics }) : failure(built && built.code || 'ETP_ANALYTICS_FAILED', 'ANALYTICS');
    }

    /* Phase-6D baseline was: readFacade = freeze({ listScopes: listScopes, inspectScope: inspectScope, loadSummary: loadSummary }) */
    var readFacade = { listScopes: listScopes, inspectScope: inspectScope, loadSummary: loadSummary };
    Object.defineProperty(readFacade, 'queryReport', { value: queryReport, enumerable: false, writable: false, configurable: false });
    Object.defineProperty(readFacade, 'loadAnalytics', { value: loadAnalytics, enumerable: false, writable: false, configurable: false });
    readFacade = freeze(readFacade);
    var importFacade = freeze({ run: run, confirm: confirm });
    return { ok: true, gateway: freeze({ version: GATEWAY_VERSION, reports: REPORTS, run: run, confirm: confirm, readVerified: readVerified, inspectScope: inspectScope, listScopes: listScopes, loadSummary: loadSummary, readFacade: readFacade, importFacade: importFacade }) };
  }

  function browserStatusReader(rootValue, lifecycle) {
    return async function (scope) {
      var checked = lifecycle.validateScope(scope), plugin;
      if (!checked || !checked.ok) return failure('ETP_SCOPE_INVALID', 'STATUS');
      try { plugin = rootValue.Capacitor && rootValue.Capacitor.Plugins && rootValue.Capacitor.Plugins.SaagarEtpStore; } catch (_) { plugin = null; }
      if (!plugin || typeof plugin.readStatus !== 'function') return failure('ETP_STATUS_UNAVAILABLE', 'STATUS');
      var result;
      try { result = await plugin.readStatus({ contractVersion: 1, scopeKey: checked.key }); } catch (_) { return failure('ETP_STATUS_UNAVAILABLE', 'STATUS'); }
      if (!exact(result, ['ok', 'state', 'activeGenerationId', 'restoreFence']) || result.ok !== true || ['EMPTY', 'STAGING', 'ACCEPTED', 'REIMPORT_REQUIRED'].indexOf(result.state) < 0 || typeof result.restoreFence !== 'boolean' || (result.activeGenerationId !== null && !generation(result.activeGenerationId))) return failure('ETP_NATIVE_RESPONSE_INVALID', 'STATUS');
      return { ok: true, status: freeze({ state: result.state, activeGenerationId: result.activeGenerationId, restoreFence: result.restoreFence }) };
    };
  }

  function browserAuthorization(rootValue) {
    return function (action) {
      var authority, snapshot, reauth;
      try { authority = rootValue && rootValue.SaagarOwnerSession; snapshot = authority && typeof authority.read === 'function' ? authority.read() : null; } catch (_) { return false; }
      if (!record(snapshot) || snapshot.version !== 1 || typeof snapshot.isOwner !== 'boolean' || typeof snapshot.role !== 'string') return false;
      if (action === 'IMPORT' && snapshot.isOwner === true) return true;
      if (action === 'IMPORT' || action === 'CONFIRM') {
        try { reauth = rootValue && rootValue.SaagarReauth; } catch (_) { reauth = null; }
        if (typeof reauth !== 'function') return false;
        return reauth(action === 'CONFIRM' ? 'publish verified Retail ETP reports' : 'validate Retail ETP reports after file selection')
          .then(function (approved) { return approved === true; }, function () { return false; });
      }
      if (action !== 'READ') return false;
      if (snapshot.isOwner === true) return true;
      try { return snapshot.role === 'Store Manager' && typeof rootValue.roleCanOpen === 'function' && rootValue.roleCanOpen('etp') === true; } catch (_) { return false; }
    };
  }

  function bootstrap() {
    try {
      var lifecycle = root && root.SaagarEtpStoreLifecyclePolicy;
      return create({ runtime: root && root.SaagarEtpImportRuntime, lifecyclePolicy: lifecycle, core: root && root.SaagarEtpCoreContract, foundationStatus: root && root.SaagarEtpFoundationStatus, queryContract: root && root.SaagarEtpQueryContract, analyticsApi: root && root.SaagarEtpVerifiedAnalytics, profileAuthority: root && root.SaagarEtpProfileAuthority, importHistoryApi: root && root.SaagarEtpImportHistory, tenderDictionaryApi: root && root.SaagarEtpTenderDictionary, storage: root && root.localStorage, statusReader: lifecycle ? browserStatusReader(root, lifecycle) : null, authorize: browserAuthorization(root), crypto: root && root.crypto });
    } catch (_) { return failure('ETP_GATEWAY_BOOTSTRAP_FAILED', 'BOOTSTRAP'); }
  }

  return freeze({ VERSION: GATEWAY_VERSION, REPORTS: REPORTS, PROJECTIONS: PROJECTIONS, REGISTRY_KEY: REGISTRY_KEY, MAX_SCOPES: MAX_SCOPES, MAX_HISTORY: MAX_HISTORY, MAX_READ_ROWS: GATEWAY_MAX_READ_ROWS, REPORT_PAGE_LIMIT: REPORT_PAGE_LIMIT, create: create, bootstrap: bootstrap });
});
