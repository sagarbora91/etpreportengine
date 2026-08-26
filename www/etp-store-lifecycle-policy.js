/* ETP sealed-store lifecycle policy.
   Pure and dependency-free: no SQLite, filesystem, localStorage, parser or UI wiring. */
(function (root, factory) {
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  if (root) root.SaagarEtpStoreLifecyclePolicy = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';

  var STORE_CODES = Object.freeze(['WLMHW', 'HEMW']);
  var REPORT_IDS = Object.freeze(['R022', 'R025', 'R013', 'R003']);
  var STATES = Object.freeze(['SELECTED', 'PREFLIGHT_PASSED', 'PARSED', 'POLICY_VALIDATED', 'STAGING', 'STAGED', 'RECONCILED', 'AWAITING_CONFIRMATION', 'ACCEPTED', 'DUPLICATE_NOOP', 'REJECTED', 'CANCELLED', 'FAILED_RETRYABLE', 'FAILED_FINAL', 'REIMPORT_REQUIRED']);
  var NEXT = Object.freeze({
    SELECTED: Object.freeze({ PREFLIGHT_PASS: 'PREFLIGHT_PASSED', REJECT: 'REJECTED', CANCEL: 'CANCELLED' }),
    PREFLIGHT_PASSED: Object.freeze({ PARSE_PASS: 'PARSED', REJECT: 'REJECTED', CANCEL: 'CANCELLED', FAIL_RETRYABLE: 'FAILED_RETRYABLE' }),
    PARSED: Object.freeze({ POLICY_PASS: 'POLICY_VALIDATED', REJECT: 'REJECTED', CANCEL: 'CANCELLED' }),
    POLICY_VALIDATED: Object.freeze({ BEGIN_STAGING: 'STAGING', REJECT: 'REJECTED', CANCEL: 'CANCELLED' }),
    STAGING: Object.freeze({ STAGE_COMPLETE: 'STAGED', FAIL_RETRYABLE: 'FAILED_RETRYABLE', FAIL_FINAL: 'FAILED_FINAL', CANCEL: 'CANCELLED' }),
    STAGED: Object.freeze({ RECONCILE_PASS: 'RECONCILED', REJECT: 'REJECTED', FAIL_RETRYABLE: 'FAILED_RETRYABLE' }),
    RECONCILED: Object.freeze({ REQUEST_CONFIRMATION: 'AWAITING_CONFIRMATION', REJECT: 'REJECTED', CANCEL: 'CANCELLED' }),
    AWAITING_CONFIRMATION: Object.freeze({ CANCEL: 'CANCELLED', REJECT: 'REJECTED' }),
    FAILED_RETRYABLE: Object.freeze({ RETRY_STAGING: 'STAGING', CANCEL: 'CANCELLED', FAIL_FINAL: 'FAILED_FINAL' }),
    REIMPORT_REQUIRED: Object.freeze({ BEGIN_REIMPORT: 'SELECTED' })
  });

  function record(value) { return !!value && typeof value === 'object' && !Array.isArray(value); }
  function copy(value) { return JSON.parse(JSON.stringify(value)); }
  function safeToken(value, max) { var s = String(value == null ? '' : value).trim(); return s.length && s.length <= max && /^[A-Za-z0-9._:-]+$/.test(s) ? s : ''; }
  function isoDate(value) {
    var match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(String(value || '').trim());
    if (!match) return '';
    var date = new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3])));
    return date.getUTCFullYear() === Number(match[1]) && date.getUTCMonth() === Number(match[2]) - 1 && date.getUTCDate() === Number(match[3]) ? match[0] : '';
  }
  function validFinancialYear(value) {
    var match = /^(\d{4})-(\d{2})$/.exec(String(value || '').trim());
    return !!match && Number(match[2]) === (Number(match[1]) + 1) % 100;
  }
  function financialYear(date) { var year = Number(date.slice(0, 4)); return String(date.slice(5, 7) >= '04' ? year : year - 1) + '-' + String((String(date.slice(5, 7)) >= '04' ? year + 1 : year) % 100).padStart(2, '0'); }
  function validateScope(value) {
    var errors = [], storeCode = record(value) ? String(value.storeCode || '').trim().toUpperCase() : '';
    var fy = record(value) ? String(value.financialYear || '').trim() : '';
    var start = record(value) ? isoDate(value.periodStart) : '', end = record(value) ? isoDate(value.periodEnd) : '';
    if (STORE_CODES.indexOf(storeCode) < 0) errors.push('STORE_CODE_INVALID');
    if (!validFinancialYear(fy)) errors.push('FINANCIAL_YEAR_INVALID');
    if (!start) errors.push('PERIOD_START_INVALID');
    if (!end) errors.push('PERIOD_END_INVALID');
    if (start && end && start > end) errors.push('PERIOD_RANGE_INVALID');
    if (start && validFinancialYear(fy) && financialYear(start) !== fy) errors.push('PERIOD_FY_MISMATCH');
    if (end && validFinancialYear(fy) && financialYear(end) !== fy) errors.push('PERIOD_FY_MISMATCH');
    return errors.length ? { ok: false, errors: Object.freeze(errors) } : { ok: true, scope: Object.freeze({ storeCode: storeCode, financialYear: fy, periodStart: start, periodEnd: end }), key: [storeCode, fy, start + '..' + end].join('|') };
  }
  function create(scopeValue, generationId) {
    var checked = validateScope(scopeValue), id = safeToken(generationId, 96);
    if (!checked.ok) return checked;
    if (!id) return { ok: false, errors: Object.freeze(['GENERATION_ID_INVALID']) };
    return { ok: true, lifecycle: Object.freeze({ version: 1, storeKind: 'SEPARATE_ETP_FACT_STORE', factsPortable: false, scope: checked.scope, scopeKey: checked.key, state: 'SELECTED', candidateGenerationId: id, activeGenerationId: null, previousGenerationId: null, activeManifestIdentity: null, restoreFence: false, manifest: null }) };
  }
  function validateLifecycle(value) {
    if (!record(value) || value.version !== 1 || value.storeKind !== 'SEPARATE_ETP_FACT_STORE' || value.factsPortable !== false || STATES.indexOf(value.state) < 0) return { ok: false, code: 'LIFECYCLE_INVALID' };
    var scope = validateScope(value.scope);
    if (!scope.ok || scope.key !== value.scopeKey) return { ok: false, code: 'LIFECYCLE_SCOPE_INVALID' };
    var candidateRequired = value.state !== 'ACCEPTED' && value.state !== 'DUPLICATE_NOOP' && value.state !== 'REIMPORT_REQUIRED';
    if (candidateRequired && !safeToken(value.candidateGenerationId, 96)) return { ok: false, code: 'LIFECYCLE_GENERATION_INVALID' };
    if (!candidateRequired && value.candidateGenerationId != null && !safeToken(value.candidateGenerationId, 96)) return { ok: false, code: 'LIFECYCLE_GENERATION_INVALID' };
    if (value.activeGenerationId != null && !safeToken(value.activeGenerationId, 96)) return { ok: false, code: 'LIFECYCLE_ACTIVE_INVALID' };
    if (value.previousGenerationId != null && !safeToken(value.previousGenerationId, 96)) return { ok: false, code: 'LIFECYCLE_PREVIOUS_INVALID' };
    if (value.activeManifestIdentity != null && !/^[A-Z0-9|.:_-]{1,600}$/i.test(String(value.activeManifestIdentity))) return { ok: false, code: 'LIFECYCLE_MANIFEST_IDENTITY_INVALID' };
    if (value.state === 'ACCEPTED' && (!value.activeGenerationId || !value.activeManifestIdentity || value.restoreFence)) return { ok: false, code: 'LIFECYCLE_ACCEPTED_INVALID' };
    if (value.state === 'REIMPORT_REQUIRED' && (value.activeGenerationId !== null || value.restoreFence !== true)) return { ok: false, code: 'LIFECYCLE_RESTORE_FENCE_INVALID' };
    return { ok: true };
  }
  function transition(lifecycle, event) {
    var lifecycleCheck = validateLifecycle(lifecycle); if (!lifecycleCheck.ok) return lifecycleCheck;
    var name = String(record(event) ? event.type : event || '').trim().toUpperCase(), next = NEXT[lifecycle.state] && NEXT[lifecycle.state][name];
    if (!next) return { ok: false, code: 'TRANSITION_FORBIDDEN', state: lifecycle.state, event: name };
    var out = copy(lifecycle); out.state = next;
    if (name === 'BEGIN_REIMPORT') {
      var generationId = safeToken(record(event) ? event.generationId : '', 96);
      if (!generationId) return { ok: false, code: 'GENERATION_ID_INVALID' };
      out.candidateGenerationId = generationId; out.restoreFence = true; out.manifest = null;
    }
    return { ok: true, lifecycle: Object.freeze(out) };
  }
  function validateManifest(value, lifecycle) {
    var errors = [], seen = Object.create(null), reports = [];
    if (!record(value) || !Array.isArray(value.reports)) return { ok: false, errors: Object.freeze(['MANIFEST_REQUIRED']) };
    if (String(value.scopeKey || '') !== lifecycle.scopeKey) errors.push('MANIFEST_SCOPE_MISMATCH');
    if (String(value.generationId || '') !== lifecycle.candidateGenerationId) errors.push('MANIFEST_GENERATION_MISMATCH');
    var authority = record(value.authority) ? value.authority : null;
    if (!authority || Object.keys(authority).sort().join('|') !== 'authorityId|contractVersion|evidenceIdentity|parserVersion|profileVersion|purpose|status|storeCode' || authority.contractVersion !== 'ETP_PROFILE_AUTHORITY_V1' || authority.storeCode !== lifecycle.scope.storeCode || authority.storeCode !== 'WLMHW' || authority.status !== 'PRODUCTION_AUTHORIZED' || authority.purpose !== 'PRODUCTION' || !safeToken(authority.authorityId, 96) || !safeToken(authority.evidenceIdentity, 96) || !safeToken(authority.profileVersion, 96) || !safeToken(authority.parserVersion, 96)) errors.push('MANIFEST_AUTHORITY_INVALID');
    var dictionary = record(value.tenderDictionary) ? value.tenderDictionary : null;
    if (!dictionary || Object.keys(dictionary).sort().join('|') !== 'contractVersion|effectiveAt|versionId' || dictionary.contractVersion !== 'ETP_TENDER_DICTIONARY_V1' || dictionary.versionId !== 'retail-etp-tender-v1' || dictionary.effectiveAt !== '2026-08-24T00:00:00.000Z') errors.push('MANIFEST_TENDER_DICTIONARY_INVALID');
    value.reports.forEach(function (entry) {
      var id = record(entry) ? String(entry.reportId || '').toUpperCase() : '';
      var hash = record(entry) ? String(entry.sourceSha256 || '').toLowerCase() : '';
      var signature = record(entry) ? String(entry.headerSignatureSha256 || '').toLowerCase() : '';
      var rows = record(entry) ? Number(entry.rowCount) : NaN;
      if (REPORT_IDS.indexOf(id) < 0 || seen[id]) errors.push('REPORT_SET_INVALID'); else seen[id] = true;
      if (!/^[a-f0-9]{64}$/.test(hash) || !/^[a-f0-9]{64}$/.test(signature)) errors.push('REPORT_HASH_INVALID');
      if (!Number.isSafeInteger(rows) || rows < 0 || rows > 250000) errors.push('REPORT_ROW_COUNT_INVALID');
      reports.push({ reportId: id, sourceSha256: hash, headerSignatureSha256: signature, rowCount: rows });
    });
    if (REPORT_IDS.some(function (id) { return !seen[id]; }) || reports.length !== REPORT_IDS.length) errors.push('REPORT_SET_INCOMPLETE');
    reports.sort(function (a, b) { return a.reportId.localeCompare(b.reportId); });
    return errors.length ? { ok: false, errors: Object.freeze(errors.filter(function (x, i, a) { return a.indexOf(x) === i; })) } : { ok: true, manifest: Object.freeze({ scopeKey: lifecycle.scopeKey, generationId: lifecycle.candidateGenerationId, authority: Object.freeze({ contractVersion: authority.contractVersion, authorityId: authority.authorityId, storeCode: authority.storeCode, status: authority.status, purpose: authority.purpose, profileVersion: authority.profileVersion, parserVersion: authority.parserVersion, evidenceIdentity: authority.evidenceIdentity }), tenderDictionary: Object.freeze({ contractVersion: dictionary.contractVersion, versionId: dictionary.versionId, effectiveAt: dictionary.effectiveAt }), reports: Object.freeze(reports) }) };
  }
  function manifestIdentity(value) {
    if (!record(value) || !Array.isArray(value.reports)) return '';
    var authority = record(value.authority) ? [value.authority.authorityId, value.authority.profileVersion, value.authority.parserVersion, value.authority.evidenceIdentity].join(':') : '';
    var dictionary = record(value.tenderDictionary) ? [value.tenderDictionary.contractVersion, value.tenderDictionary.versionId, value.tenderDictionary.effectiveAt].join(':') : '';
    return String(value.scopeKey || '') + '|AUTH:' + authority + '|TENDER:' + dictionary + '|' + value.reports.slice().sort(function (a, b) { return String(a.reportId).localeCompare(String(b.reportId)); }).map(function (entry) { return String(entry.reportId || '') + ':' + String(entry.sourceSha256 || '').toLowerCase(); }).join('|');
  }
  function attachManifest(lifecycle, manifest) {
    var lifecycleCheck = validateLifecycle(lifecycle); if (!lifecycleCheck.ok) return lifecycleCheck;
    if (lifecycle.state !== 'STAGED') return { ok: false, code: 'MANIFEST_STATE_FORBIDDEN' };
    var checked = validateManifest(manifest, lifecycle); if (!checked.ok) return checked;
    var out = copy(lifecycle); out.manifest = checked.manifest;
    return { ok: true, lifecycle: Object.freeze(out) };
  }
  function publish(lifecycle) {
    var lifecycleCheck = validateLifecycle(lifecycle); if (!lifecycleCheck.ok) return lifecycleCheck;
    if (lifecycle.state !== 'AWAITING_CONFIRMATION') return { ok: false, code: 'PUBLISH_STATE_FORBIDDEN' };
    var checked = validateManifest(lifecycle.manifest, lifecycle); if (!checked.ok) return checked;
    var out = copy(lifecycle), candidate = out.candidateGenerationId, identity = manifestIdentity(checked.manifest);
    if (identity === out.activeManifestIdentity) { out.state = 'DUPLICATE_NOOP'; out.candidateGenerationId = null; return { ok: true, lifecycle: Object.freeze(out), changed: false }; }
    out.previousGenerationId = out.activeGenerationId; out.activeGenerationId = candidate; out.candidateGenerationId = null;
    out.activeManifestIdentity = identity; out.state = 'ACCEPTED'; out.restoreFence = false;
    return { ok: true, lifecycle: Object.freeze(out), changed: true };
  }
  function beginRestatement(lifecycle, generationId) {
    var checked = validateLifecycle(lifecycle); if (!checked.ok) return checked;
    if (lifecycle.state !== 'ACCEPTED' && lifecycle.state !== 'DUPLICATE_NOOP') return { ok: false, code: 'RESTATEMENT_STATE_FORBIDDEN' };
    var id = safeToken(generationId, 96); if (!id || id === lifecycle.activeGenerationId) return { ok: false, code: 'GENERATION_ID_INVALID' };
    var out = copy(lifecycle); out.state = 'SELECTED'; out.candidateGenerationId = id; out.manifest = null;
    return { ok: true, lifecycle: Object.freeze(out) };
  }
  function fenceAfterRestore(scopeValue, controlLinkId) {
    var checked = validateScope(scopeValue), link = safeToken(controlLinkId, 96);
    if (!checked.ok) return checked;
    if (!link) return { ok: false, errors: Object.freeze(['CONTROL_LINK_ID_INVALID']) };
    return { ok: true, lifecycle: Object.freeze({ version: 1, storeKind: 'SEPARATE_ETP_FACT_STORE', factsPortable: false, scope: checked.scope, scopeKey: checked.key, state: 'REIMPORT_REQUIRED', candidateGenerationId: null, activeGenerationId: null, previousGenerationId: null, activeManifestIdentity: null, restoreFence: true, controlLinkId: link, manifest: null }) };
  }
  function verifiedReadable(lifecycle) { return !!(record(lifecycle) && lifecycle.state === 'ACCEPTED' && !lifecycle.restoreFence && lifecycle.activeGenerationId && lifecycle.manifest && lifecycle.manifest.generationId === lifecycle.activeGenerationId); }

  return Object.freeze({ STORE_CODES: STORE_CODES, REPORT_IDS: REPORT_IDS, STATES: STATES, validateScope: validateScope, validateLifecycle: validateLifecycle, create: create, transition: transition, attachManifest: attachManifest, manifestIdentity: manifestIdentity, publish: publish, beginRestatement: beginRestatement, fenceAfterRestore: fenceAfterRestore, verifiedReadable: verifiedReadable });
});
