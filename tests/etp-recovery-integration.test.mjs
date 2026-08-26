import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const api = require('../www/etp-recovery-integration.js');

test('fences every restored ETP scope and reports re-import required', async () => {
  const calls = [];
  const made = api.create({
    fenceAfterRestore: async scope => (calls.push(scope), { ok: true }),
    resetStore: async () => ({ ok: true })
  });
  const result = await made.integration.fenceRestoredScopes([{ storeCode: 'WLMHW' }, { storeCode: 'HEMW' }]);
  assert.deepEqual(result, { ok: true, fencedCount: 2, state: 'REIMPORT_REQUIRED' });
  assert.equal(calls.length, 2);
});

test('restore fencing fails closed and reports how many scopes were fenced', async () => {
  let count = 0;
  const made = api.create({
    fenceAfterRestore: async () => (++count === 2 ? { ok: false, code: 'ETP_STORAGE_UNAVAILABLE' } : { ok: true }),
    resetStore: async () => ({ ok: true })
  });
  const result = await made.integration.fenceRestoredScopes([{}, {}, {}]);
  assert.deepEqual(result, { ok: false, code: 'ETP_STORAGE_UNAVAILABLE', fencedCount: 1 });
});

test('factory-reset hook requires the exact destructive confirmation and propagates failure', async () => {
  let calls = 0;
  const made = api.create({ fenceAfterRestore: async () => ({ ok: true }), resetStore: async () => (++calls, { ok: false, code: 'ETP_KEY_UNAVAILABLE' }) });
  assert.equal((await made.integration.resetForFactoryReset('wrong')).code, 'ETP_RESET_CONFIRMATION_REQUIRED');
  assert.equal(calls, 0);
  assert.equal((await made.integration.resetForFactoryReset('RESET_ETP_STORE')).code, 'ETP_KEY_UNAVAILABLE');
  assert.equal(calls, 1);
});

test('Capacitor binding sends only contract version and an exact scope key', async () => {
  const calls = [];
  const made = api.createFromCapacitor({
    fenceAfterRestore: async value => (calls.push(['fence', value]), { ok: true, state: 'REIMPORT_REQUIRED' }),
    resetStore: async value => (calls.push(['reset', value]), { ok: true, state: 'EMPTY' })
  });
  assert.equal(made.ok, true);
  assert.equal((await made.integration.fenceRestoredScopes([{ scopeKey: 'WLMHW|2026-27|2026-04-01..2026-04-30' }])).ok, true);
  assert.equal((await made.integration.resetForFactoryReset('RESET_ETP_STORE')).ok, true);
  assert.deepEqual(calls, [['fence', { contractVersion: 1, scopeKey: 'WLMHW|2026-27|2026-04-01..2026-04-30' }], ['reset', { contractVersion: 1 }]]);
});
