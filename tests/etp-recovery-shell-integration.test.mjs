import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';

const index = fs.readFileSync(new URL('../www/index.html', import.meta.url), 'utf8');

test('shell loads the ETP recovery boundary and portable payload never embeds ETP facts', () => {
  assert.match(index, /<script src="etp-recovery-integration\.js"><\/script>/);
  const backup = index.slice(index.indexOf('function backupPayload()'), index.indexOf('function requestBackupPassphrase'));
  assert.doesNotMatch(backup, /etpFacts|saagar-etp\.db|payload_envelope|readFacts/);
  assert.match(backup, /localStorage:\s*store/);
  assert.match(index, /k==='saagar_etp_scope_registry' \|\| k==='saagar_etp_control_registry_v1'/);
});

test('verified restore fences restored ETP scopes before success is recorded', () => {
  const restore = index.slice(index.indexOf('async function restoreValidatedBackup()'), index.indexOf('function showRestoreAcceptance()'));
  const fenceAt = restore.indexOf('fenceRestoredScopes(restoredEtpScopes())');
  const successAt = restore.indexOf("auditLog('restore.verified'");
  assert.ok(fenceAt > 0);
  assert.ok(successAt > fenceAt);
  assert.match(restore, /if\(!fenced\|\|!fenced\.ok\)throw new Error/);
});

test('factory reset awaits ETP reset and cannot reload when it fails', () => {
  const wipe = index.slice(index.indexOf('async function factoryResetWipe(keys)'), index.indexOf('function archiveCutoffIso'));
  const resetAt = wipe.indexOf("await etpRecovery.resetForFactoryReset('RESET_ETP_STORE')");
  const operationalAt = wipe.indexOf('window.SaagarStore._reset()');
  assert.ok(resetAt > 0);
  assert.ok(operationalAt > resetAt);
  const failure = wipe.slice(wipe.indexOf("reason:'etp-reset-failed'"), wipe.indexOf('return false;') + 'return false;'.length);
  assert.doesNotMatch(failure, /location\.reload/);
});
