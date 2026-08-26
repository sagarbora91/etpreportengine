/* D1 Today hardening: presentation-only policy for owner reauthentication.
   Security decisions remain in index.html's PIN verifier; this module only
   supplies deterministic, testable copy and retry rules. */
(function (root, factory) {
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  if (root) root.SaagarReauthPolicy = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';
  var MAX_ATTEMPTS = 2;
  function cleanReason(reason) {
    var value = String(reason || 'complete this protected action').replace(/\s+/g, ' ').trim();
    return value.slice(0, 180) || 'complete this protected action';
  }
  function promptText(reason, attempt) {
    var suffix = attempt > 1 ? '\n\nThe previous PIN was incorrect. Try once more.' : '';
    return 'Owner approval required\n\n'
      + 'Purpose: ' + cleanReason(reason) + '\n'
      + 'Approval applies to this action only and expires immediately.\n'
      + 'Cancel keeps your data unchanged.' + suffix + '\n\nEnter Admin PIN:';
  }
  function outcomeText(status, lockSeconds) {
    if (status === 'cancelled') return 'Approval cancelled. No changes were made.';
    if (status === 'locked') {
      var seconds = Math.max(1, Math.ceil(Number(lockSeconds) || 0));
      return 'Owner approval is temporarily locked. Try again in ' + seconds + 's.';
    }
    if (status === 'incorrect-final') return 'Owner approval denied after two incorrect PIN attempts. No changes were made.';
    if (status === 'incorrect') return 'Incorrect Admin PIN.';
    return '';
  }
  function canRetry(status, attempt) { return status === 'incorrect' && Number(attempt) < MAX_ATTEMPTS; }
  return { MAX_ATTEMPTS:MAX_ATTEMPTS, cleanReason:cleanReason, promptText:promptText, outcomeText:outcomeText, canRetry:canRetry };
});