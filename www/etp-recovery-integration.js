/* Narrow ETP recovery hooks. Portable payloads never contain ETP facts: after
   a successful control-data restore, every restored ETP scope is fenced until
   its source reports are re-imported. */
(function (root, factory) {
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  if (root) root.SaagarEtpRecoveryIntegration = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';
  var ETP_RECOVERY_CONTRACT_VERSION = 1;
  function nativeStore(plugin) {
    if (!plugin || typeof plugin.fenceAfterRestore !== 'function' || typeof plugin.resetStore !== 'function') return null;
    return {
      fenceAfterRestore: async function (scope) {
        var key = String(scope && scope.scopeKey || '');
        if (!/^(WLMHW|HEMW)\|\d{4}-\d{2}\|\d{4}-\d{2}-\d{2}\.\.\d{4}-\d{2}-\d{2}$/.test(key)) return { ok: false, code: 'ETP_SCOPE_INVALID' };
        try { var result = await plugin.fenceAfterRestore({ contractVersion: ETP_RECOVERY_CONTRACT_VERSION, scopeKey: key }); return result && result.ok === true ? { ok: true } : { ok: false, code: String(result && result.code || 'ETP_RESTORE_FENCE_FAILED') }; }
        catch (error) { return { ok: false, code: String(error && error.code || 'ETP_RESTORE_FENCE_FAILED') }; }
      },
      resetStore: async function () {
        try { var result = await plugin.resetStore({ contractVersion: ETP_RECOVERY_CONTRACT_VERSION }); return result && result.ok === true ? { ok: true } : { ok: false, code: String(result && result.code || 'ETP_RESET_FAILED') }; }
        catch (error) { return { ok: false, code: String(error && error.code || 'ETP_RESET_FAILED') }; }
      }
    };
  }
  function create(store) {
    if (!store || typeof store.fenceAfterRestore !== 'function' || typeof store.resetStore !== 'function') return { ok: false, code: 'ETP_RECOVERY_DEPENDENCY_INVALID' };
    async function fenceRestoredScopes(scopes) {
      if (!Array.isArray(scopes)) return { ok: false, code: 'ETP_RESTORE_SCOPES_INVALID' };
      for (var i = 0; i < scopes.length; i++) {
        var result = await store.fenceAfterRestore(scopes[i]);
        if (!result || !result.ok) return { ok: false, code: result && result.code || 'ETP_RESTORE_FENCE_FAILED', fencedCount: i };
      }
      return { ok: true, fencedCount: scopes.length, state: 'REIMPORT_REQUIRED' };
    }
    async function resetForFactoryReset(confirmation) {
      if (confirmation !== 'RESET_ETP_STORE') return { ok: false, code: 'ETP_RESET_CONFIRMATION_REQUIRED' };
      var result = await store.resetStore(confirmation);
      if (!result || !result.ok) return { ok: false, code: result && result.code || 'ETP_RESET_FAILED' };
      return { ok: true, state: 'EMPTY' };
    }
    return { ok: true, integration: Object.freeze({ fenceRestoredScopes: fenceRestoredScopes, resetForFactoryReset: resetForFactoryReset }) };
  }
  function createFromCapacitor(plugin) {
    var store = nativeStore(plugin);
    return store ? create(store) : { ok: false, code: 'ETP_NATIVE_UNAVAILABLE' };
  }
  return Object.freeze({ VERSION: 1, create: create, createFromCapacitor: createFromCapacitor });
});
