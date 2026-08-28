import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import vm from 'node:vm';

const source = await readFile(new URL('../www/auto-backup.js', import.meta.url), 'utf8');
const MAGIC = Uint8Array.from([0x53, 0x42, 0x43, 0x43, 0x31]);

function encoded(bytes) {
  return Buffer.from(bytes).toString('base64');
}

function createRuntime(options = {}) {
  const files = new Map(Object.entries(options.files || {}));
  const deleted = [];
  const writes = [];
  const timers = [];
  const storage = new Map(Object.entries(options.storage || {}));
  const fs = {
    async writeFile({ path, data, directory }) { writes.push(`${directory}:${path}`); files.set(`${directory}:${path}`, String(data)); },
    async readFile({ path, directory }) {
      const key = `${directory}:${path}`;
      if (!files.has(key)) throw new Error('File does not exist');
      return { data: files.get(key) };
    },
    async stat({ path, directory }) {
      const key = `${directory}:${path}`;
      if (!files.has(key)) throw new Error('File does not exist');
      return { size: Buffer.from(files.get(key), 'base64').length };
    },
    async readdir({ path, directory }) {
      const prefix = `${directory}:${path}/`;
      const names = [...files.keys()].filter(key => key.startsWith(prefix)).map(key => key.slice(prefix.length));
      if (!names.length && options.missingDirectories) throw new Error('File does not exist');
      return { files: names.map(name => ({ name })) };
    },
    async deleteFile({ path, directory }) {
      const key = `${directory}:${path}`;
      if (!files.has(key)) throw new Error('File does not exist');
      deleted.push(key);
      files.delete(key);
    }
  };
  const validPayload = {
    app: 'Saagar Traders Business Control Centre',
    scope: 'whitelisted-app-keys-only',
    schemaVersion: 1,
    localStorage: { firm: 'safe' }
  };
  const window = {
    Capacitor: {
      isNativePlatform: () => options.native !== false,
      Plugins: { Filesystem: fs }
    },
    SaagarStore: {
      seal: async clear => options.plaintextSeal
        ? clear
        : Uint8Array.from([...MAGIC, ...clear]),
      unseal: async sealed => options.unsealFails ? null : sealed.slice(MAGIC.length),
      whenReady: callback => callback()
    },
    backupPayload: () => options.payload || validPayload,
    localStorage: {
      getItem: key => storage.has(key) ? storage.get(key) : null,
      setItem: (key, value) => storage.set(key, String(value))
    },
    TextEncoder,
    TextDecoder,
    Uint8Array,
    btoa: value => Buffer.from(value, 'binary').toString('base64'),
    atob: value => Buffer.from(value, 'base64').toString('binary'),
    setTimeout: (callback, delay) => { timers.push({ type: 'timeout', callback, delay }); return timers.length; },
    setInterval: (callback, delay) => { timers.push({ type: 'interval', callback, delay }); return timers.length; },
    console: { warn() {} }
  };
  vm.runInNewContext(source, { window, globalThis: window }, { filename: 'auto-backup.js' });
  return { api: window.SaagarBackup, files, deleted, writes, timers, storage, validPayload };
}

test('exposes only the consumed backup API and schedules automatic backup', () => {
  const runtime = createRuntime();
  assert.deepEqual(Object.keys(runtime.api).sort(), ['now', 'purgeLegacyDocs', 'readLatest', 'status']);
  assert.equal(runtime.timers.find(timer => timer.type === 'timeout')?.delay, 6000);
  assert.equal(runtime.timers.find(timer => timer.type === 'interval')?.delay, 6 * 60 * 60 * 1000);
  assert.equal(runtime.api.status().failureThresholdHours, 36);
  assert.equal(runtime.api.status().plaintextWarning, false);
});

test('writes verified SBCC1 backups to app-private DATA and can read latest', async () => {
  const runtime = createRuntime();
  await runtime.api.now();
  const privateNames = [...runtime.files.keys()].filter(key => key.startsWith('DATA:SaagarBCC-Backups/'));
  assert.ok(privateNames.some(key => /backup-\d{4}-\d{2}-\d{2}\.sbcc$/.test(key)));
  assert.ok(privateNames.some(key => /week-\d{4}-W\d{2}\.sbcc$/.test(key)));
  assert.ok(privateNames.some(key => /month-\d{4}-\d{2}\.sbcc$/.test(key)));
  assert.ok(privateNames.includes('DATA:SaagarBCC-Backups/latest.sbcc'));
  assert.ok(privateNames.every(key => !key.endsWith('.json')));
  const text = await runtime.api.readLatest();
  assert.deepEqual(JSON.parse(text), runtime.validPayload);
  assert.equal(runtime.api.status().consecutiveFailures, 0);
  assert.ok(runtime.api.status().lastBackup);
});

test('automatic startup creates one daily backup and the interval does not duplicate that day', async () => {
  const runtime = createRuntime();
  runtime.timers.find(timer => timer.type === 'timeout').callback();
  while (runtime.api.status().running) await new Promise(resolve => setImmediate(resolve));
  const writesAfterStartup = runtime.writes.length;
  assert.ok(writesAfterStartup >= 4);
  runtime.timers.find(timer => timer.type === 'interval').callback();
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(runtime.writes.length, writesAfterStartup);
});

test('fails closed when the shared crypto helper falls back to plaintext', async () => {
  const runtime = createRuntime({ plaintextSeal: true });
  await assert.rejects(runtime.api.now(), error => error.code === 'BACKUP_ENCRYPTION_REQUIRED');
  assert.equal([...runtime.files.keys()].filter(key => key.startsWith('DATA:SaagarBCC-Backups/')).length, 0);
  assert.equal(runtime.api.status().consecutiveFailures, 1);
});

test('rejects a plaintext latest file instead of trusting storage-core pass-through', async () => {
  const runtime = createRuntime({
    files: { 'DATA:SaagarBCC-Backups/latest.sbcc': encoded(new TextEncoder().encode('{"unsafe":true}')) }
  });
  await assert.rejects(runtime.api.readLatest(), error => error.code === 'DEVICE_BOUND_BACKUP');
});

test('private GFS retention deletes only exact encrypted backup names', async () => {
  const files = {};
  for (let day = 1; day <= 10; day++) files[`DATA:SaagarBCC-Backups/backup-2026-07-${String(day).padStart(2, '0')}.sbcc`] = encoded(MAGIC);
  for (let week = 1; week <= 7; week++) files[`DATA:SaagarBCC-Backups/week-2026-W${String(week).padStart(2, '0')}.sbcc`] = encoded(MAGIC);
  for (let month = 1; month <= 14; month++) {
    const year = month > 12 ? 2026 : 2025;
    const value = month > 12 ? month - 12 : month;
    files[`DATA:SaagarBCC-Backups/month-${year}-${String(value).padStart(2, '0')}.sbcc`] = encoded(MAGIC);
  }
  files['DATA:SaagarBCC-Backups/customer-records.sbcc'] = encoded(MAGIC);
  files['DATA:SaagarBCC-Backups/backup-manual.sbcc'] = encoded(MAGIC);
  const runtime = createRuntime({ files });
  await runtime.api.now();
  const remaining = [...runtime.files.keys()];
  assert.equal(remaining.filter(key => /backup-\d{4}-\d{2}-\d{2}\.sbcc$/.test(key)).length, 7);
  assert.equal(remaining.filter(key => /week-\d{4}-W\d{2}\.sbcc$/.test(key)).length, 5);
  assert.equal(remaining.filter(key => /month-\d{4}-\d{2}\.sbcc$/.test(key)).length, 12);
  assert.ok(remaining.includes('DATA:SaagarBCC-Backups/customer-records.sbcc'));
  assert.ok(remaining.includes('DATA:SaagarBCC-Backups/backup-manual.sbcc'));
});

test('legacy purge is restricted to daily backup JSON and latest.json in Documents', async () => {
  const runtime = createRuntime({
    files: {
      'DOCUMENTS:SaagarBCC-Backups/backup-2026-08-28.json': 'e30=',
      'DOCUMENTS:SaagarBCC-Backups/latest.json': 'e30=',
      'DOCUMENTS:SaagarBCC-Backups/backup-manual.json': 'e30=',
      'DOCUMENTS:SaagarBCC-Backups/pre-reset-2026-08-28.json': 'e30=',
      'DOCUMENTS:SaagarBCC-Backups/customer-records.json': 'e30=',
      'DATA:SaagarBCC-Backups/backup-2026-08-28.sbcc': encoded(MAGIC)
    }
  });
  const result = await runtime.api.purgeLegacyDocs();
  assert.equal(result.deleted, 2);
  assert.equal(result.scanned, 2);
  assert.equal(result.error, undefined);
  assert.deepEqual(runtime.deleted.sort(), [
    'DOCUMENTS:SaagarBCC-Backups/backup-2026-08-28.json',
    'DOCUMENTS:SaagarBCC-Backups/latest.json'
  ]);
  assert.ok(runtime.files.has('DOCUMENTS:SaagarBCC-Backups/pre-reset-2026-08-28.json'));
  assert.ok(runtime.files.has('DOCUMENTS:SaagarBCC-Backups/customer-records.json'));
  assert.ok(runtime.files.has('DATA:SaagarBCC-Backups/backup-2026-08-28.sbcc'));
});

test('status escalates a continuing failure only after the 36-hour threshold', () => {
  const old = new Date(Date.now() - 37 * 60 * 60 * 1000).toISOString();
  const runtime = createRuntime({
    storage: {
      bcc_autobackup_status_v2: JSON.stringify({
        firstFailureAt: old,
        lastFailureAt: old,
        consecutiveFailures: 2
      })
    }
  });
  assert.equal(runtime.api.status().failureEscalated, true);
});
