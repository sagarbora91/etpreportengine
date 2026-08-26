/* Shared Retail ETP import transaction coordinator.
   App-unloaded by design. Parsing and report adaptation are injected so this
   module owns ordering and atomic publication without owning XLSX semantics. */
(function (root, factory) {
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  if (root) root.SaagarEtpImportCoordinator = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';

  var REPORTS = Object.freeze(['R003', 'R013', 'R022', 'R025']);
  function record(value) { return !!value && typeof value === 'object' && !Array.isArray(value); }
  function fail(code, stage, detail) { return { ok: false, code: code, stage: stage, detail: detail || null }; }
  function requireMethod(value, name) { return !!value && typeof value[name] === 'function'; }
  function advance(policy, lifecycle, event) {
    var next = policy.transition(lifecycle, event);
    return next && next.ok ? next.lifecycle : null;
  }
  function validDependencies(options) {
    var pipeline = options && options.pipeline, store = options && options.store, policy = options && options.lifecyclePolicy;
    return policy && ['create', 'validateScope', 'transition', 'attachManifest', 'manifestIdentity', 'publish'].every(function (name) { return requireMethod(policy, name); }) &&
      pipeline && ['preflight', 'parse', 'validate', 'reconcile', 'authorizePublication'].every(function (name) { return requireMethod(pipeline, name); }) &&
      store && ['beginStage', 'appendChunk', 'finishStage', 'publish'].every(function (name) { return requireMethod(store, name); });
  }
  function reportSet(chunks, manifest) {
    if (!Array.isArray(chunks) || !record(manifest) || !Array.isArray(manifest.reports)) return false;
    var seen = Object.create(null), nextIndex = Object.create(null), rowCounts = Object.create(null), expected = Object.create(null);
    manifest.reports.forEach(function (entry) { expected[String(entry.reportId || '').toUpperCase()] = entry.rowCount; });
    for (var i = 0; i < chunks.length; i++) {
      var chunk = chunks[i], id = record(chunk) ? String(chunk.reportId || '').toUpperCase() : '';
      if (REPORTS.indexOf(id) < 0 || !Number.isSafeInteger(chunk.chunkIndex) || chunk.chunkIndex !== (nextIndex[id] || 0) || !Array.isArray(chunk.rows)) return false;
      seen[id] = true; nextIndex[id] = chunk.chunkIndex + 1; rowCounts[id] = (rowCounts[id] || 0) + chunk.rows.length;
    }
    return REPORTS.every(function (id) { return Number.isSafeInteger(expected[id]) && expected[id] >= 0 && (rowCounts[id] || 0) === expected[id] && (expected[id] === 0 || seen[id]); });
  }

  function create(options) {
    if (!validDependencies(options)) return fail('ETP_COORDINATOR_DEPENDENCY_INVALID', 'CREATE');
    var policy = options.lifecyclePolicy, pipeline = options.pipeline, store = options.store;

    async function run(request) {
      if (!record(request) || !record(request.scope) || !Array.isArray(request.files) || !request.files.length || typeof request.generationId !== 'string') return fail('ETP_IMPORT_REQUEST_INVALID', 'SELECT');
      var created = policy.create(request.scope, request.generationId);
      if (!created || !created.ok) return fail('ETP_SCOPE_INVALID', 'SELECT', created);
      var lifecycle = created.lifecycle;
      var previous = request.currentLifecycle || null;
      if (previous) {
        var priorCheck = policy.validateLifecycle(previous);
        if (!priorCheck || !priorCheck.ok || previous.scopeKey !== lifecycle.scopeKey || previous.state !== 'ACCEPTED') return fail('ETP_ACTIVE_SCOPE_INVALID', 'SELECT');
        lifecycle = Object.freeze(Object.assign({}, lifecycle, {
          activeGenerationId: previous.activeGenerationId,
          previousGenerationId: previous.previousGenerationId,
          activeManifestIdentity: previous.activeManifestIdentity
        }));
      }

      var checked = await pipeline.preflight({ scope: lifecycle.scope, files: request.files, coverageDeclaration: request.coverageDeclaration });
      if (!checked || !checked.ok) return fail('ETP_PREFLIGHT_REJECTED', 'PREFLIGHT', checked);
      lifecycle = advance(policy, lifecycle, 'PREFLIGHT_PASS');
      var parsed = await pipeline.parse({ scope: lifecycle.scope, files: request.files, preflight: checked, coverageDeclaration: request.coverageDeclaration });
      if (!parsed || !parsed.ok) return fail('ETP_PARSE_REJECTED', 'PARSE', parsed);
      lifecycle = advance(policy, lifecycle, 'PARSE_PASS');
      var validated = await pipeline.validate({ scope: lifecycle.scope, parsed: parsed, coverageDeclaration: request.coverageDeclaration });
      if (!validated || !validated.ok || !record(validated.manifest) || !reportSet(validated.chunks, validated.manifest)) return fail('ETP_POLICY_REJECTED', 'VALIDATE', validated);
      lifecycle = advance(policy, lifecycle, 'POLICY_PASS');

      /* Duplicate detection occurs before native staging, so an identical batch
         cannot disturb the accepted generation or create abandoned ciphertext. */
      var identity = policy.manifestIdentity(validated.manifest);
      if (previous && identity && identity === previous.activeManifestIdentity) {
        return { ok: true, changed: false, duplicate: true, lifecycle: previous };
      }

      lifecycle = advance(policy, lifecycle, 'BEGIN_STAGING');
      var nativeResult = await store.beginStage(lifecycle);
      if (!nativeResult || !nativeResult.ok) return fail(nativeResult && nativeResult.code || 'ETP_STAGE_FAILED', 'STAGE_BEGIN', nativeResult);
      for (var i = 0; i < validated.chunks.length; i++) {
        nativeResult = await store.appendChunk(lifecycle, Object.assign({ scopeKey: lifecycle.scopeKey, generationId: lifecycle.candidateGenerationId }, validated.chunks[i]));
        if (!nativeResult || !nativeResult.ok) return fail(nativeResult && nativeResult.code || 'ETP_STAGE_FAILED', 'STAGE_APPEND', nativeResult);
      }
      nativeResult = await store.finishStage(lifecycle, validated.manifest);
      if (!nativeResult || !nativeResult.ok) return fail(nativeResult && nativeResult.code || 'ETP_STAGE_FAILED', 'STAGE_FINISH', nativeResult);
      lifecycle = advance(policy, lifecycle, 'STAGE_COMPLETE');
      var attached = policy.attachManifest(lifecycle, validated.manifest);
      if (!attached || !attached.ok) return fail('ETP_MANIFEST_REJECTED', 'STAGE_FINISH', attached);
      lifecycle = attached.lifecycle;

      var reconciled = await pipeline.reconcile({ scope: lifecycle.scope, manifest: lifecycle.manifest, validated: validated, coverageDeclaration: request.coverageDeclaration });
      if (!reconciled || !reconciled.ok || reconciled.status !== 'PASS') return fail('ETP_RECONCILIATION_REJECTED', 'RECONCILE', reconciled);
      lifecycle = advance(policy, lifecycle, 'RECONCILE_PASS');
      lifecycle = advance(policy, lifecycle, 'REQUEST_CONFIRMATION');
      if (!lifecycle) return fail('ETP_LIFECYCLE_INVALID', 'CONFIRM');
      if (request.confirmed !== true) return { ok: true, changed: false, awaitingConfirmation: true, lifecycle: lifecycle, reconciliation: reconciled, coverage: reconciled.coverage || null };

      var authorization = await pipeline.authorizePublication({ scope: lifecycle.scope, lifecycle: lifecycle });
      if (!authorization || authorization.ok !== true) return fail('ETP_PUBLICATION_AUTH_REQUIRED', 'CONFIRM');

      nativeResult = await store.publish(lifecycle);
      if (!nativeResult || !nativeResult.ok) return fail(nativeResult && nativeResult.code || 'ETP_PUBLISH_FAILED', 'PUBLISH', nativeResult);
      var published = policy.publish(lifecycle);
      if (!published || !published.ok || !published.changed) return fail('ETP_LIFECYCLE_PUBLISH_FAILED', 'PUBLISH', published);
      return { ok: true, changed: true, duplicate: false, lifecycle: published.lifecycle, reconciliation: reconciled };
    }
    async function confirm(lifecycle) {
      var checked = policy.validateLifecycle(lifecycle);
      if (!checked || !checked.ok || lifecycle.state !== 'AWAITING_CONFIRMATION') return fail('ETP_CONFIRMATION_STATE_INVALID', 'CONFIRM');
      var authorization = await pipeline.authorizePublication({ scope: lifecycle.scope, lifecycle: lifecycle });
      if (!authorization || authorization.ok !== true) return fail('ETP_PUBLICATION_AUTH_REQUIRED', 'CONFIRM');
      var nativeResult = await store.publish(lifecycle);
      if (!nativeResult || !nativeResult.ok) return fail(nativeResult && nativeResult.code || 'ETP_PUBLISH_FAILED', 'PUBLISH', nativeResult);
      var published = policy.publish(lifecycle);
      if (!published || !published.ok || !published.changed) return fail('ETP_LIFECYCLE_PUBLISH_FAILED', 'PUBLISH', published);
      return { ok: true, changed: true, duplicate: false, lifecycle: published.lifecycle };
    }
    return { ok: true, coordinator: Object.freeze({ run: run, confirm: confirm }) };
  }

  return Object.freeze({ VERSION: 1, REPORTS: REPORTS, create: create });
});
