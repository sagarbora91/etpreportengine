/* Small transaction coordinator used by the shell restore path.
   A restore is only reported as successful after apply + read-back verification.
   Any error triggers rollback + rollback verification before the error escapes. */
(function (root, factory) {
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  if (root) root.SaagarRestoreEngine = api;
})(typeof window !== 'undefined' ? window : globalThis, function () {
  'use strict';

  function required(plan, name) {
    if (!plan || typeof plan[name] !== 'function') throw new Error('Restore plan is missing ' + name + '().');
  }

  async function run(plan) {
    ['capture', 'validate', 'apply', 'verify', 'rollback', 'verifyRollback'].forEach(function (name) {
      required(plan, name);
    });
    await plan.validate();
    var before = await plan.capture();
    var applied = false;
    try {
      applied = true;
      var result = await plan.apply(before);
      await plan.verify(result, before);
      return { ok: true, result: result, before: before };
    } catch (cause) {
      if (!applied) throw cause;
      var rollbackError = null;
      try {
        await plan.rollback(before, cause);
        await plan.verifyRollback(before);
      } catch (err) {
        rollbackError = err;
      }
      var out = new Error(rollbackError
        ? 'Restore failed and automatic rollback could not be verified: ' + String(rollbackError.message || rollbackError)
        : 'Restore failed; the original device state was restored: ' + String(cause && cause.message || cause));
      out.code = rollbackError ? 'RESTORE_ROLLBACK_FAILED' : 'RESTORE_ROLLED_BACK';
      out.cause = cause;
      out.rollbackError = rollbackError;
      throw out;
    }
  }

  return { run: run };
});
