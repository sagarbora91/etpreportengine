/* ETP native fact-store bridge.
   App-loaded bridge. This boundary accepts only policy-validated metadata and
   explicitly approved canonical fact fields; it never accepts workbook material. */
(function (root, factory) {
  var api = factory(root);
  if (typeof module === 'object' && module.exports) module.exports = api;
  if (root) root.SaagarEtpNativeStore = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function (root) {
  'use strict';

  var VERSION = 1;
  var MAX_CHUNK_ROWS = 500;
  var MAX_CHUNK_BYTES = 512 * 1024;
  var MAX_FIELDS = 128;
  var TOKEN = /^[A-Za-z0-9._:-]{1,96}$/;
  var HASH = /^[a-f0-9]{64}$/;
  var REPORTS = Object.freeze(['R003', 'R013', 'R022', 'R025']);
  var MAX_READ_ROWS = 200;
  var MAX_READ_FIELDS = 64;
  var PUBLIC_NATIVE_ERRORS = Object.freeze(['ETP_NATIVE_BUSY', 'ETP_STAGE_NOT_FOUND', 'ETP_CHUNK_ORDER_INVALID', 'ETP_SCOPE_CONFLICT', 'ETP_STORAGE_UNAVAILABLE', 'ETP_STORAGE_FULL', 'ETP_INTEGRITY_FAILED', 'ETP_KEY_UNAVAILABLE', 'ETP_REIMPORT_REQUIRED', 'ETP_STALE_GENERATION']);
  var NATIVE_ERROR_MAP = Object.freeze({ INTEGRITY_FAILED: 'ETP_INTEGRITY_FAILED', KEY_UNAVAILABLE: 'ETP_KEY_UNAVAILABLE', RESTORE_FENCED: 'ETP_REIMPORT_REQUIRED', STALE_GENERATION: 'ETP_STALE_GENERATION', NO_SPACE: 'ETP_STORAGE_FULL', DB_OPEN_FAILED: 'ETP_STORAGE_UNAVAILABLE', DB_READ_ONLY: 'ETP_STORAGE_UNAVAILABLE', DB_IO_FAILED: 'ETP_STORAGE_UNAVAILABLE', ETP_STORE_UNAVAILABLE: 'ETP_STORAGE_UNAVAILABLE', INVALID_GENERATION_STATE: 'ETP_STAGE_NOT_FOUND', CHUNK_ORDER_INVALID: 'ETP_CHUNK_ORDER_INVALID' });
  var FORBIDDEN = /(?:^|_)(?:workbook|worksheet|filename|file_label|file_path|source_name|source_bytes|blob|base64|customer|consumer|mobile|phone|email|address|name|aadhaar|pan|dob)(?:$|_)/i;

  function record(value) { return !!value && typeof value === 'object' && !Array.isArray(value); }
  function ownKeys(value) { return record(value) ? Object.keys(value) : []; }
  function exact(value, names) { var keys = ownKeys(value); return keys.length === names.length && keys.every(function (key) { return names.indexOf(key) >= 0; }); }
  function token(value) { var text = String(value == null ? '' : value); return TOKEN.test(text) ? text : ''; }
  function error(code) { return { ok: false, code: code }; }
  function freeze(value) { return Object.freeze(value); }
  function pluginError(reason) {
    var code = record(reason) ? String(reason.code || '') : '';
    code = NATIVE_ERROR_MAP[code] || code;
    return error(PUBLIC_NATIVE_ERRORS.indexOf(code) >= 0 ? code : 'ETP_NATIVE_CALL_FAILED');
  }
  function utf8Bytes(text) {
    var count = 0;
    for (var i = 0; i < text.length; i++) {
      var code = text.charCodeAt(i);
      if (code < 0x80) count++;
      else if (code < 0x800) count += 2;
      else if (code >= 0xD800 && code <= 0xDBFF && i + 1 < text.length && text.charCodeAt(i + 1) >= 0xDC00 && text.charCodeAt(i + 1) <= 0xDFFF) { count += 4; i++; }
      else count += 3;
    }
    return count;
  }
  function forbiddenKey(value) {
    if (!record(value)) return false;
    return Object.keys(value).some(function (key) { return FORBIDDEN.test(key) || (record(value[key]) && forbiddenKey(value[key])); });
  }
  function scalar(value) {
    return value === null || typeof value === 'string' || typeof value === 'boolean' || (typeof value === 'number' && Number.isFinite(value));
  }
  function resolvePlugin(explicit) {
    if (explicit) return explicit;
    try { return root.Capacitor && root.Capacitor.Plugins && root.Capacitor.Plugins.SaagarEtpStore || null; } catch (_) { return null; }
  }
  function validatePolicy(policy) {
    return !!(policy && typeof policy.validateLifecycle === 'function' && typeof policy.validateScope === 'function' && typeof policy.verifiedReadable === 'function');
  }
  function validateLifecycle(policy, lifecycle, states) {
    var checked = policy.validateLifecycle(lifecycle);
    if (!checked || !checked.ok) return error('ETP_LIFECYCLE_INVALID');
    if (states.indexOf(lifecycle.state) < 0) return error('ETP_LIFECYCLE_STATE_FORBIDDEN');
    return { ok: true };
  }
  function validateManifest(policy, lifecycle) {
    var value = lifecycle.manifest, seen = Object.create(null);
    if (!record(value) || !exact(value, ['scopeKey', 'generationId', 'authority', 'tenderDictionary', 'reports']) || value.scopeKey !== lifecycle.scopeKey || value.generationId !== lifecycle.candidateGenerationId || !Array.isArray(value.reports) || value.reports.length !== REPORTS.length) return error('ETP_MANIFEST_INVALID');
    var authority=value.authority;
    if(!record(authority)||!exact(authority,['contractVersion','authorityId','storeCode','status','purpose','profileVersion','parserVersion','evidenceIdentity'])||authority.contractVersion!=='ETP_PROFILE_AUTHORITY_V1'||authority.storeCode!==lifecycle.scope.storeCode||authority.storeCode!=='WLMHW'||authority.status!=='PRODUCTION_AUTHORIZED'||authority.purpose!=='PRODUCTION')return error('ETP_MANIFEST_INVALID');
    var dictionary=value.tenderDictionary;
    if(!record(dictionary)||!exact(dictionary,['contractVersion','versionId','effectiveAt'])||dictionary.contractVersion!=='ETP_TENDER_DICTIONARY_V1'||dictionary.versionId!=='retail-etp-tender-v1'||dictionary.effectiveAt!=='2026-08-24T00:00:00.000Z')return error('ETP_MANIFEST_INVALID');
    var reports = [];
    for (var i = 0; i < value.reports.length; i++) {
      var entry = value.reports[i], id = record(entry) ? String(entry.reportId || '').toUpperCase() : '';
      if (!exact(entry, ['reportId', 'sourceSha256', 'headerSignatureSha256', 'rowCount']) || REPORTS.indexOf(id) < 0 || seen[id] || !HASH.test(String(entry.sourceSha256 || '')) || !HASH.test(String(entry.headerSignatureSha256 || '')) || !Number.isSafeInteger(entry.rowCount) || entry.rowCount < 0 || entry.rowCount > 250000) return error('ETP_MANIFEST_INVALID');
      seen[id] = true; reports.push(freeze({ reportId: id, sourceSha256: entry.sourceSha256, headerSignatureSha256: entry.headerSignatureSha256, rowCount: entry.rowCount }));
    }
    reports.sort(function (a, b) { return a.reportId.localeCompare(b.reportId); });
    return { ok: true, manifest: freeze({ scopeKey: value.scopeKey, generationId: value.generationId, authority: freeze({contractVersion:authority.contractVersion,authorityId:authority.authorityId,storeCode:authority.storeCode,status:authority.status,purpose:authority.purpose,profileVersion:authority.profileVersion,parserVersion:authority.parserVersion,evidenceIdentity:authority.evidenceIdentity}), tenderDictionary:freeze({contractVersion:dictionary.contractVersion,versionId:dictionary.versionId,effectiveAt:dictionary.effectiveAt}), reports: freeze(reports) }) };
  }
  function validateFact(row, allowed) {
    if (!record(row) || forbiddenKey(row) || ownKeys(row).length > MAX_FIELDS) return error('ETP_FACT_SHAPE_INVALID');
    var keys = ownKeys(row);
    if (!keys.length || keys.some(function (key) { return !allowed[key] || FORBIDDEN.test(key) || !scalar(row[key]) || (typeof row[key] === 'string' && row[key].length > 4096); })) return error('ETP_FACT_FIELD_INVALID');
    var out = {};
    keys.sort().forEach(function (key) { out[key] = row[key]; });
    return { ok: true, fact: freeze(out) };
  }

  function create(options) {
    options = options || {};
    var policy = options.lifecyclePolicy;
    if (!validatePolicy(policy)) return error('ETP_POLICY_UNAVAILABLE');
    var plugin = resolvePlugin(options.plugin);
    if (!plugin) return error('ETP_NATIVE_UNAVAILABLE');
    var fields = Array.isArray(options.allowedFactFields) ? options.allowedFactFields : [];
    var allowed = Object.create(null);
    if (!fields.length || fields.length > MAX_FIELDS || fields.some(function (field) { var key = String(field); if (!/^[a-z][a-z0-9_]{0,63}$/.test(key) || FORBIDDEN.test(key) || allowed[key]) return true; allowed[key] = true; return false; })) return error('ETP_FACT_DICTIONARY_INVALID');

    async function call(name, payload) {
      if (typeof plugin[name] !== 'function') return error('ETP_NATIVE_METHOD_UNAVAILABLE');
      try {
        var result = await plugin[name](payload);
        if (!record(result) || result.ok !== true) return pluginError(result);
        return { ok: true, result: result };
      } catch (reason) { return pluginError(reason); }
    }
    async function beginStage(lifecycle) {
      var valid = validateLifecycle(policy, lifecycle, ['STAGING']); if (!valid.ok) return valid;
      return call('beginStage', { contractVersion: VERSION, scopeKey: lifecycle.scopeKey, generationId: lifecycle.candidateGenerationId });
    }
    async function appendChunk(lifecycle, chunk) {
      var valid = validateLifecycle(policy, lifecycle, ['STAGING']); if (!valid.ok) return valid;
      if (!exact(chunk, ['scopeKey', 'generationId', 'reportId', 'chunkIndex', 'rows']) || forbiddenKey(chunk)) return error('ETP_CHUNK_SHAPE_INVALID');
      if (chunk.scopeKey !== lifecycle.scopeKey || chunk.generationId !== lifecycle.candidateGenerationId) return error('ETP_CHUNK_SCOPE_INVALID');
      var reportId = String(chunk.reportId || '').toUpperCase();
      if (REPORTS.indexOf(reportId) < 0 || !Number.isSafeInteger(chunk.chunkIndex) || chunk.chunkIndex < 0 || chunk.chunkIndex > 999999) return error('ETP_CHUNK_METADATA_INVALID');
      if (!Array.isArray(chunk.rows) || !chunk.rows.length || chunk.rows.length > MAX_CHUNK_ROWS) return error('ETP_CHUNK_ROWS_INVALID');
      var rows = [], failed;
      chunk.rows.forEach(function (row) { if (failed) return; var checked = validateFact(row, allowed); if (!checked.ok) failed = checked; else rows.push(checked.fact); });
      if (failed) return failed;
      var payload = { contractVersion: VERSION, scopeKey: lifecycle.scopeKey, generationId: lifecycle.candidateGenerationId, reportId: reportId, chunkIndex: chunk.chunkIndex, rows: rows };
      if (utf8Bytes(JSON.stringify(payload)) > MAX_CHUNK_BYTES) return error('ETP_CHUNK_BYTES_EXCEEDED');
      return call('appendStageChunk', payload);
    }
    async function finishStage(lifecycle, manifestValue) {
      var valid = validateLifecycle(policy, lifecycle, ['STAGING']); if (!valid.ok) return valid;
      var candidate = Object.assign({}, lifecycle, { manifest: manifestValue });
      var manifest = validateManifest(policy, candidate); if (!manifest.ok) return manifest;
      return call('finishStage', { contractVersion: VERSION, scopeKey: lifecycle.scopeKey, generationId: lifecycle.candidateGenerationId, manifest: manifest.manifest });
    }
    async function publish(lifecycle) {
      var valid = validateLifecycle(policy, lifecycle, ['AWAITING_CONFIRMATION']); if (!valid.ok) return valid;
      var manifest = validateManifest(policy, lifecycle); if (!manifest.ok) return manifest;
      return call('publishStage', { contractVersion: VERSION, scopeKey: lifecycle.scopeKey, generationId: lifecycle.candidateGenerationId, manifest: manifest.manifest });
    }
    async function readStatus(scopeValue) {
      var scope = policy.validateScope(scopeValue); if (!scope || !scope.ok) return error('ETP_SCOPE_INVALID');
      var called = await call('readStatus', { contractVersion: VERSION, scopeKey: scope.key });
      if (!called.ok) return called;
      var result = called.result;
      if (!exact(result, ['ok', 'state', 'activeGenerationId', 'restoreFence']) || ['EMPTY', 'STAGING', 'ACCEPTED', 'REIMPORT_REQUIRED'].indexOf(result.state) < 0) return error('ETP_NATIVE_RESPONSE_INVALID');
      if (result.activeGenerationId !== null && !token(result.activeGenerationId)) return error('ETP_NATIVE_RESPONSE_INVALID');
      if (typeof result.restoreFence !== 'boolean' || (result.state === 'ACCEPTED' && (!result.activeGenerationId || result.restoreFence))) return error('ETP_NATIVE_RESPONSE_INVALID');
      return { ok: true, status: freeze({ state: result.state, activeGenerationId: result.activeGenerationId, restoreFence: result.restoreFence }) };
    }
    async function readFacts(scopeValue, request) {
      var scope = policy.validateScope(scopeValue); if (!scope || !scope.ok) return error('ETP_SCOPE_INVALID');
      if (!record(request) || !exact(request, ['generationId', 'reportId', 'fields', 'cursor', 'limit'])) return error('ETP_READ_SHAPE_INVALID');
      var generationId = token(request.generationId), reportId = String(request.reportId || '').toUpperCase();
      if (!generationId || REPORTS.indexOf(reportId) < 0 || !Array.isArray(request.fields) || !request.fields.length || request.fields.length > MAX_READ_FIELDS || !Number.isSafeInteger(request.limit) || request.limit < 1 || request.limit > MAX_READ_ROWS) return error('ETP_READ_METADATA_INVALID');
      var projected = [], seen = Object.create(null);
      for (var i = 0; i < request.fields.length; i++) { var field = String(request.fields[i]); if (!allowed[field] || seen[field] || FORBIDDEN.test(field)) return error('ETP_READ_FIELD_FORBIDDEN'); seen[field] = true; projected.push(field); }
      var cursor = request.cursor;
      if (cursor === null) cursor = { chunkIndex: 0, rowOffset: 0 };
      if (!record(cursor) || !exact(cursor, ['chunkIndex', 'rowOffset']) || !Number.isSafeInteger(cursor.chunkIndex) || cursor.chunkIndex < 0 || cursor.chunkIndex > 4095 || !Number.isSafeInteger(cursor.rowOffset) || cursor.rowOffset < 0 || cursor.rowOffset >= MAX_CHUNK_ROWS) return error('ETP_READ_CURSOR_INVALID');
      var called = await call('readFacts', { contractVersion: VERSION, scopeKey: scope.key, generationId: generationId, reportId: reportId, fields: projected, cursorChunkIndex: cursor.chunkIndex, cursorRowOffset: cursor.rowOffset, limit: request.limit });
      if (!called.ok) return called;
      var result = called.result;
      if (!exact(result, ['ok', 'scopeKey', 'generationId', 'reportId', 'rows', 'hasMore', 'nextChunkIndex', 'nextRowOffset']) || result.scopeKey !== scope.key || result.generationId !== generationId || result.reportId !== reportId || !Array.isArray(result.rows) || result.rows.length > request.limit || typeof result.hasMore !== 'boolean' || !Number.isSafeInteger(result.nextChunkIndex) || result.nextChunkIndex < 0 || result.nextChunkIndex > 4096 || !Number.isSafeInteger(result.nextRowOffset) || result.nextRowOffset < 0 || result.nextRowOffset >= MAX_CHUNK_ROWS) return error('ETP_NATIVE_RESPONSE_INVALID');
      var rows = [];
      for (var r = 0; r < result.rows.length; r++) { var checked = validateFact(result.rows[r], seen); if (!checked.ok) return error('ETP_NATIVE_RESPONSE_INVALID'); rows.push(checked.fact); }
      return { ok: true, page: freeze({ scopeKey: scope.key, generationId: generationId, reportId: reportId, rows: freeze(rows), hasMore: result.hasMore, nextCursor: result.hasMore ? freeze({ chunkIndex: result.nextChunkIndex, rowOffset: result.nextRowOffset }) : null }) };
    }
    async function fenceAfterRestore(scopeValue) {
      var scope = policy.validateScope(scopeValue); if (!scope || !scope.ok) return error('ETP_SCOPE_INVALID');
      var called = await call('fenceAfterRestore', { contractVersion: VERSION, scopeKey: scope.key });
      if (!called.ok) return called;
      if (!exact(called.result, ['ok', 'state']) || called.result.state !== 'REIMPORT_REQUIRED') return error('ETP_NATIVE_RESPONSE_INVALID');
      return { ok: true, status: freeze({ state: 'REIMPORT_REQUIRED', activeGenerationId: null, restoreFence: true }) };
    }
    async function resetScope(scopeValue, confirmation) {
      var scope = policy.validateScope(scopeValue); if (!scope || !scope.ok) return error('ETP_SCOPE_INVALID');
      if (confirmation !== 'RESET_ETP_SCOPE') return error('ETP_RESET_CONFIRMATION_REQUIRED');
      var called = await call('resetScope', { contractVersion: VERSION, scopeKey: scope.key });
      if (!called.ok) return called;
      if (!exact(called.result, ['ok', 'state']) || called.result.state !== 'EMPTY') return error('ETP_NATIVE_RESPONSE_INVALID');
      return { ok: true, status: freeze({ state: 'EMPTY', activeGenerationId: null, restoreFence: false }) };
    }
    async function resetStore(confirmation) {
      if (confirmation !== 'RESET_ETP_STORE') return error('ETP_RESET_CONFIRMATION_REQUIRED');
      var called = await call('resetStore', { contractVersion: VERSION });
      if (!called.ok) return called;
      if (!exact(called.result, ['ok', 'state']) || called.result.state !== 'EMPTY') return error('ETP_NATIVE_RESPONSE_INVALID');
      return { ok: true, status: freeze({ state: 'EMPTY', activeGenerationId: null, restoreFence: false }) };
    }
    return { ok: true, adapter: freeze({ beginStage: beginStage, appendChunk: appendChunk, finishStage: finishStage, publish: publish, readStatus: readStatus, readFacts: readFacts, fenceAfterRestore: fenceAfterRestore, resetScope: resetScope, resetStore: resetStore }) };
  }

  return freeze({ VERSION: VERSION, MAX_CHUNK_ROWS: MAX_CHUNK_ROWS, MAX_CHUNK_BYTES: MAX_CHUNK_BYTES, MAX_READ_ROWS: MAX_READ_ROWS, create: create });
});
