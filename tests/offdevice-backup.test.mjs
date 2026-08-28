import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import vm from 'node:vm';

const source = await readFile(new URL('../www/offdevice-backup.js', import.meta.url), 'utf8');
const destinationId = 'a'.repeat(64);
const recovery = {
  kdf: { name: 'PBKDF2', hash: 'SHA-256', iterations: 310000, salt: 'c2FsdHNhbHRzYWx0MQ==' },
  cipher: { name: 'AES-GCM', keyBits: 256, tagBits: 128, iv: 'aXYxMjM0NTY3ODkw' },
  wrappedKey: 'd3JhcHBlZC1wb3J0YWJsZS1rZXk='
};

function existingConfig(overrides = {}) {
  return {
    version: 1,
    destinationId,
    destinationLabel: 'Drive backup',
    provider: 'drive.documents',
    wrappedKey: 'U0tXMXdyYXBwZWQ=',
    recovery,
    createdAt: '2026-08-27T00:00:00.000Z',
    lastAttemptAt: null,
    lastSuccessAt: null,
    lastFailureAt: null,
    firstFailureAt: null,
    lastError: null,
    consecutiveFailures: 0,
    ...overrides
  };
}

function createRuntime(options = {}) {
  const storage = new Map();
  if (options.config !== undefined) storage.set('st_v2_offdevice_backup_config_v1', typeof options.config === 'string' ? options.config : JSON.stringify(options.config));
  const events = [];
  const timers = [];
  const files = new Map();
  const providerStatus = options.providerStatus || { configured: true, destinationId, label: 'Drive backup', provider: 'drive.documents' };
  const offDevice = {
    async chooseFolder() { events.push('choose'); return providerStatus; },
    async status() { events.push('provider-status'); return providerStatus; },
    async clearFolder() { events.push('clear-folder'); return { cleared: true }; },
    async copyFromCache(args) {
      events.push(`copy:${args.path}`);
      return options.copyResult || { verified: true, destinationId, sha256: 'b'.repeat(64), size: 1234, dailyFile: `backup-${args.date}.sccbak` };
    }
  };
  const keystore = {
    async available() { events.push('keystore-available'); return { available: options.keystoreAvailable !== false }; },
    async wrapKey({ data }) { events.push(`wrap:${data}`); return { wrapped: 'U0tXMXdyYXBwZWQ=' }; },
    async unwrapKey({ wrapped }) { events.push(`unwrap:${wrapped}`); return { data: 'c'.repeat(44) }; }
  };
  const fs = {
    async writeFile({ path, data, directory }) { events.push(`write:${directory}:${path}`); files.set(`${directory}:${path}`, data); },
    async deleteFile({ path, directory }) { events.push(`delete:${directory}:${path}`); files.delete(`${directory}:${path}`); }
  };
  const exportControl = {
    async approveScheduled(meta) { events.push(`approve:${meta.destinationId}`); return options.approve !== false; },
    authorizeScheduled(meta) { events.push(`authorize:${meta.destinationId}`); return options.authorize === false ? false : 'exp_test'; },
    beginDelivery(token) { events.push(`begin:${token}`); return options.begin !== false; },
    recordOutcome(token, outcome) { events.push(`outcome:${token}:${outcome}`); return options.record !== false; },
    async revokeScheduled() { events.push('revoke'); return options.revoke !== false; }
  };
  const portable = {
    async createRecoveryProfile(passphrase) { events.push(`profile:${passphrase}`); return { keyBase64: 'd'.repeat(44), profile: recovery }; },
    async sealWithKey(payload, key, profile) {
      events.push('seal');
      assert.equal(key, 'c'.repeat(44));
      assert.equal(JSON.stringify(profile), JSON.stringify(recovery));
      assert.equal(payload.scope, 'whitelisted-app-keys-only');
      return { format: 'saagar-portable-backup', version: 2, ciphertext: 'encrypted' };
    },
    isContainer(value) { return value?.format === 'saagar-portable-backup'; }
  };
  const window = {
    Capacitor: {
      isNativePlatform: () => options.native !== false,
      Plugins: { SaagarOffDevice: offDevice, SaagarKeystore: keystore, Filesystem: fs }
    },
    SaagarPortableBackup: portable,
    SaagarExportControl: exportControl,
    SaagarStore: { whenReady: callback => callback() },
    backupPayload: () => ({ app: 'Saagar Traders Business Control Centre', scope: 'whitelisted-app-keys-only', localStorage: { safe: 'data' } }),
    setOffDeviceBackup: () => { events.push('marker'); return options.marker !== false; },
    safeGet: key => storage.has(key) ? storage.get(key) : null,
    safeSet: (key, value) => { storage.set(key, String(value)); return true; },
    safeRemove: key => { storage.delete(key); return true; },
    TextEncoder,
    Uint8Array,
    btoa: value => Buffer.from(value, 'binary').toString('base64'),
    setTimeout: (callback, delay) => { timers.push({ type: 'timeout', callback, delay }); return timers.length; },
    setInterval: (callback, delay) => { timers.push({ type: 'interval', callback, delay }); return timers.length; },
    console: { warn() {} }
  };
  vm.runInNewContext(source, { window, globalThis: window }, { filename: 'offdevice-backup.js' });
  return { api: window.SaagarOffDeviceBackup, storage, events, timers, files };
}

test('exposes only the settings UI API and schedules provider backup checks', () => {
  const runtime = createRuntime();
  assert.deepEqual(Object.keys(runtime.api).sort(), ['configure', 'disable', 'run', 'status']);
  assert.equal(runtime.timers.find(timer => timer.type === 'timeout')?.delay, 10000);
  assert.equal(runtime.timers.find(timer => timer.type === 'interval')?.delay, 6 * 60 * 60 * 1000);
  assert.equal(runtime.api.status().failureThresholdHours, 36);
});

test('configuration wraps the random key, binds approval to destination and verifies first delivery', async () => {
  const runtime = createRuntime();
  const result = await runtime.api.configure('correct horse battery staple');
  assert.equal(result.verified, true);
  const stored = JSON.parse(runtime.storage.get('st_v2_offdevice_backup_config_v1'));
  assert.equal(stored.destinationId, destinationId);
  assert.equal(stored.wrappedKey, 'U0tXMXdyYXBwZWQ=');
  assert.equal(JSON.stringify(stored).includes('correct horse battery staple'), false);
  assert.equal(JSON.stringify(stored).includes('d'.repeat(44)), false);
  assert.ok(runtime.events.indexOf(`authorize:${destinationId}`) < runtime.events.findIndex(event => event.startsWith('copy:')));
  assert.ok(runtime.events.findIndex(event => event.startsWith('copy:')) < runtime.events.indexOf('marker'));
  assert.ok(runtime.events.indexOf('marker') < runtime.events.indexOf('outcome:exp_test:completed'));
  assert.equal([...runtime.files.keys()].length, 0, 'temporary CACHE handoff must be removed');
});

test('destination mismatch fails closed before key unwrap or cache delivery', async () => {
  const runtime = createRuntime({
    config: existingConfig(),
    providerStatus: { configured: true, destinationId: 'e'.repeat(64), label: 'Other folder' }
  });
  await assert.rejects(runtime.api.run(true), error => error.code === 'DESTINATION_MISMATCH');
  assert.equal(runtime.events.some(event => event.startsWith('unwrap:')), false);
  assert.equal(runtime.events.some(event => event.startsWith('write:')), false);
  assert.equal(runtime.events.includes('marker'), false);
});

test('unverified native readback never stamps success and records failed delivery', async () => {
  const runtime = createRuntime({
    config: existingConfig(),
    copyResult: { verified: false, destinationId }
  });
  await assert.rejects(runtime.api.run(true), error => error.code === 'COPY_NOT_VERIFIED');
  assert.equal(runtime.events.includes('marker'), false);
  assert.ok(runtime.events.includes('outcome:exp_test:failed'));
  assert.equal([...runtime.files.keys()].length, 0);
  const stored = JSON.parse(runtime.storage.get('st_v2_offdevice_backup_config_v1'));
  assert.equal(stored.consecutiveFailures, 1);
  assert.equal(stored.lastSuccessAt, null);
});

test('automatic invocation skips a second provider delivery on the same day', async () => {
  const runtime = createRuntime({ config: existingConfig({ lastSuccessAt: new Date().toISOString() }) });
  runtime.timers.find(timer => timer.type === 'timeout').callback();
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(runtime.events.some(event => event.startsWith('authorize:')), false);
  assert.equal(runtime.events.some(event => event.startsWith('copy:')), false);
});

test('disable revokes the standing grant before clearing provider permission and local config', async () => {
  const runtime = createRuntime({ config: existingConfig() });
  assert.equal((await runtime.api.disable()).disabled, true);
  assert.ok(runtime.events.indexOf('revoke') < runtime.events.indexOf('clear-folder'));
  assert.equal(runtime.storage.has('st_v2_offdevice_backup_config_v1'), false);
  assert.equal(runtime.api.status().configured, false);
});

test('damaged configuration is surfaced and never used for delivery', async () => {
  const runtime = createRuntime({ config: '{not-json' });
  assert.equal(runtime.api.status().damaged, true);
  await assert.rejects(runtime.api.run(true), error => error.code === 'CONFIG_DAMAGED');
  assert.equal(runtime.events.some(event => event.startsWith('authorize:')), false);
});
