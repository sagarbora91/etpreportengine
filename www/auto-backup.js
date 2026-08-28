/* Saagar BCC app-private automatic backup.
 *
 * Security invariants:
 * - backupPayload() is the single, whitelisted source of backup data;
 * - a backup is written/read only when SaagarStore produced an SBCC1 envelope;
 * - private retention deletes only this module's exact encrypted filenames;
 * - legacy cleanup deletes only backup-YYYY-MM-DD.json and latest.json from the
 *   old shared Documents/SaagarBCC-Backups directory.
 */
(function (root) {
  'use strict';

  var KEEP_DAYS = 7;
  var KEEP_WEEKS = 5;
  var KEEP_MONTHS = 12;
  var FAILURE_THRESHOLD_HOURS = 36;
  var AUTO_START_DELAY_MS = 6000;
  var AUTO_INTERVAL_MS = 6 * 60 * 60 * 1000;
  var MAX_BACKUP_BYTES = 64 * 1024 * 1024;
  var PRIVATE_FOLDER = 'SaagarBCC-Backups';
  var LATEST_FILE = 'latest.sbcc';
  var STATUS_KEY = 'bcc_autobackup_status_v2';
  var SBCC1 = [0x53, 0x42, 0x43, 0x43, 0x31];
  var DAILY_NAME = /^backup-\d{4}-\d{2}-\d{2}\.sbcc$/;
  var WEEKLY_NAME = /^week-\d{4}-W\d{2}\.sbcc$/;
  var MONTHLY_NAME = /^month-\d{4}-\d{2}\.sbcc$/;
  var LEGACY_DAILY_NAME = /^backup-\d{4}-\d{2}-\d{2}\.json$/;

  function codedError(code, message) {
    var error = new Error(message || code);
    error.code = code;
    return error;
  }

  function nativeContext() {
    var capacitor = root && root.Capacitor;
    var native = !!(capacitor && typeof capacitor.isNativePlatform === 'function' && capacitor.isNativePlatform());
    var fs = capacitor && capacitor.Plugins && capacitor.Plugins.Filesystem;
    return { native: native, fs: fs || null };
  }

  function requireContext() {
    var context = nativeContext();
    if (!context.native || !context.fs) throw codedError('BACKUP_READER_UNAVAILABLE', 'App-private backup storage is unavailable.');
    if (!root.SaagarStore || typeof root.SaagarStore.seal !== 'function' || typeof root.SaagarStore.unseal !== 'function') {
      throw codedError('BACKUP_READER_UNAVAILABLE', 'Secure backup encryption is unavailable.');
    }
    return context;
  }

  function utf8Encode(text) {
    if (typeof root.TextEncoder === 'function') return new root.TextEncoder().encode(text);
    var encoded = unescape(encodeURIComponent(text));
    var out = new Uint8Array(encoded.length);
    for (var i = 0; i < encoded.length; i++) out[i] = encoded.charCodeAt(i);
    return out;
  }

  function utf8Decode(bytes) {
    if (typeof root.TextDecoder === 'function') return new root.TextDecoder('utf-8', { fatal: true }).decode(bytes);
    var binary = '';
    for (var i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
    return decodeURIComponent(escape(binary));
  }

  function bytesToBase64(bytes) {
    var binary = '';
    var step = 0x8000;
    for (var i = 0; i < bytes.length; i += step) {
      binary += String.fromCharCode.apply(null, bytes.subarray(i, Math.min(i + step, bytes.length)));
    }
    return root.btoa(binary);
  }

  function base64ToBytes(value) {
    var binary;
    try { binary = root.atob(String(value || '')); }
    catch (_) { throw codedError('DEVICE_BOUND_BACKUP', 'The on-device backup is not valid base64.'); }
    var out = new Uint8Array(binary.length);
    for (var i = 0; i < binary.length; i++) out[i] = binary.charCodeAt(i);
    return out;
  }

  function isSealed(bytes) {
    if (!bytes || bytes.length <= SBCC1.length) return false;
    for (var i = 0; i < SBCC1.length; i++) if (bytes[i] !== SBCC1[i]) return false;
    return true;
  }

  function safeMessage(error) {
    if (!error) return 'Unknown backup failure';
    return String(error.code || error.message || error).slice(0, 160);
  }

  function emptyState() {
    return {
      lastBackup: null,
      lastAttemptAt: null,
      lastSuccessAt: null,
      lastFailureAt: null,
      firstFailureAt: null,
      lastError: null,
      consecutiveFailures: 0
    };
  }

  function loadState() {
    var state = emptyState();
    try {
      var parsed = JSON.parse(root.localStorage && root.localStorage.getItem(STATUS_KEY) || 'null');
      if (parsed && typeof parsed === 'object') {
        ['lastBackup', 'lastAttemptAt', 'lastSuccessAt', 'lastFailureAt', 'firstFailureAt', 'lastError'].forEach(function (key) {
          if (parsed[key] === null || typeof parsed[key] === 'string') state[key] = parsed[key];
        });
        var failures = Number(parsed.consecutiveFailures);
        if (isFinite(failures) && failures >= 0) state.consecutiveFailures = Math.floor(failures);
      }
    } catch (_) {}
    return state;
  }

  var state = loadState();
  var running = null;

  function saveState() {
    try { if (root.localStorage) root.localStorage.setItem(STATUS_KEY, JSON.stringify(state)); } catch (_) {}
  }

  function failureEscalated(now) {
    if (!state.consecutiveFailures) return false;
    var since = Date.parse(state.lastSuccessAt || state.firstFailureAt || '');
    return isFinite(since) && now.getTime() - since >= FAILURE_THRESHOLD_HOURS * 60 * 60 * 1000;
  }

  function status() {
    var context = nativeContext();
    return {
      lastBackup: state.lastBackup,
      lastAttemptAt: state.lastAttemptAt,
      lastSuccessAt: state.lastSuccessAt,
      lastFailureAt: state.lastFailureAt,
      lastError: state.lastError,
      consecutiveFailures: state.consecutiveFailures,
      failureEscalated: failureEscalated(new Date()),
      failureThresholdHours: 36,
      folder: 'App-private data/' + PRIVATE_FOLDER,
      native: context.native && !!context.fs,
      plaintextWarning: false,
      running: !!running
    };
  }

  function pad2(value) { return String(value).padStart(2, '0'); }

  function dateKey(date) {
    return date.getFullYear() + '-' + pad2(date.getMonth() + 1) + '-' + pad2(date.getDate());
  }

  function isoWeekKey(date) {
    var copy = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()));
    var weekday = copy.getUTCDay() || 7;
    copy.setUTCDate(copy.getUTCDate() + 4 - weekday);
    var yearStart = new Date(Date.UTC(copy.getUTCFullYear(), 0, 1));
    var week = Math.ceil((((copy - yearStart) / 86400000) + 1) / 7);
    return copy.getUTCFullYear() + '-W' + pad2(week);
  }

  function privatePath(name) { return PRIVATE_FOLDER + '/' + name; }

  function entryNames(result) {
    var files = result && Array.isArray(result.files) ? result.files : [];
    return files.map(function (entry) { return typeof entry === 'string' ? entry : String(entry && entry.name || ''); }).filter(Boolean);
  }

  function isNotFound(error) {
    var text = safeMessage(error);
    return /not.?found|does not exist|ENOENT/i.test(text);
  }

  async function readPrivateBytes(fs, name) {
    try {
      if (typeof fs.stat === 'function') {
        var info = await fs.stat({ path: privatePath(name), directory: 'DATA' });
        if (Number(info && info.size) > MAX_BACKUP_BYTES) throw codedError('BACKUP_TOO_LARGE', 'The on-device backup exceeds 64 MB.');
      }
      var result = await fs.readFile({ path: privatePath(name), directory: 'DATA' });
      var bytes = base64ToBytes(result && result.data);
      if (bytes.length > MAX_BACKUP_BYTES) throw codedError('BACKUP_TOO_LARGE', 'The on-device backup exceeds 64 MB.');
      return bytes;
    } catch (error) {
      if (error && (error.code === 'BACKUP_TOO_LARGE' || error.code === 'DEVICE_BOUND_BACKUP')) throw error;
      if (isNotFound(error)) return null;
      throw codedError('BACKUP_READER_UNAVAILABLE', 'The on-device backup could not be read.');
    }
  }

  async function writeVerified(fs, name, bytes) {
    var path = privatePath(name);
    var b64 = bytesToBase64(bytes);
    await fs.writeFile({ path: path, data: b64, directory: 'DATA', recursive: true });
    var check = await fs.readFile({ path: path, directory: 'DATA' });
    if (String(check && check.data || '') !== b64) {
      try { await fs.deleteFile({ path: path, directory: 'DATA' }); } catch (_) {}
      throw codedError('BACKUP_VERIFY_FAILED', 'The on-device backup failed verification.');
    }
  }

  async function fileExists(fs, name) {
    try { return !!(await readPrivateBytes(fs, name)); }
    catch (error) { if (isNotFound(error)) return false; throw error; }
  }

  async function prunePrivate(fs, expression, keep) {
    var result;
    try { result = await fs.readdir({ path: PRIVATE_FOLDER, directory: 'DATA' }); }
    catch (error) { if (isNotFound(error)) return; throw error; }
    var names = entryNames(result).filter(function (name) { return expression.test(name); }).sort().reverse();
    for (var i = keep; i < names.length; i++) {
      await fs.deleteFile({ path: privatePath(names[i]), directory: 'DATA' });
    }
  }

  function validatePayload(payload) {
    if (!payload || typeof payload !== 'object' || Array.isArray(payload) ||
        payload.app !== 'Saagar Traders Business Control Centre' ||
        payload.scope !== 'whitelisted-app-keys-only' ||
        !payload.localStorage || typeof payload.localStorage !== 'object' || Array.isArray(payload.localStorage)) {
      throw codedError('BACKUP_PAYLOAD_INVALID', 'The whitelisted backup payload is unavailable or invalid.');
    }
    return payload;
  }

  async function performBackup() {
    var now = new Date();
    state.lastAttemptAt = now.toISOString();
    saveState();
    try {
      var context = requireContext();
      if (typeof root.backupPayload !== 'function') throw codedError('BACKUP_PAYLOAD_INVALID', 'The whitelisted backup provider is unavailable.');
      var payload = validatePayload(root.backupPayload());
      var clearText = JSON.stringify(payload);
      var sealed = await root.SaagarStore.seal(utf8Encode(clearText));
      var bytes = sealed instanceof Uint8Array ? sealed : new Uint8Array(sealed || []);
      if (!isSealed(bytes)) throw codedError('BACKUP_ENCRYPTION_REQUIRED', 'Encrypted backup creation is unavailable.');

      var daily = 'backup-' + dateKey(now) + '.sbcc';
      var weekly = 'week-' + isoWeekKey(now) + '.sbcc';
      var monthly = 'month-' + dateKey(now).slice(0, 7) + '.sbcc';
      await writeVerified(context.fs, daily, bytes);
      if (!(await fileExists(context.fs, weekly))) await writeVerified(context.fs, weekly, bytes);
      if (!(await fileExists(context.fs, monthly))) await writeVerified(context.fs, monthly, bytes);
      await writeVerified(context.fs, LATEST_FILE, bytes);
      await prunePrivate(context.fs, DAILY_NAME, KEEP_DAYS);
      await prunePrivate(context.fs, WEEKLY_NAME, KEEP_WEEKS);
      await prunePrivate(context.fs, MONTHLY_NAME, KEEP_MONTHS);

      state.lastBackup = now.toISOString();
      state.lastSuccessAt = state.lastBackup;
      state.lastFailureAt = null;
      state.firstFailureAt = null;
      state.lastError = null;
      state.consecutiveFailures = 0;
      saveState();
      return status();
    } catch (error) {
      var failedAt = new Date().toISOString();
      state.lastFailureAt = failedAt;
      if (!state.consecutiveFailures) state.firstFailureAt = failedAt;
      state.consecutiveFailures += 1;
      state.lastError = safeMessage(error);
      saveState();
      throw error;
    }
  }

  function now() {
    if (!running) running = Promise.resolve().then(performBackup).finally(function () { running = null; });
    return running;
  }

  function automaticBackup() {
    var today = dateKey(new Date());
    var last = state.lastSuccessAt ? new Date(state.lastSuccessAt) : null;
    if (last && !isNaN(last.getTime()) && dateKey(last) === today) return Promise.resolve(status());
    return now();
  }

  async function readLatest() {
    var context = requireContext();
    var sealed = await readPrivateBytes(context.fs, LATEST_FILE);
    if (!sealed) return null;
    if (!isSealed(sealed)) throw codedError('DEVICE_BOUND_BACKUP', 'The on-device backup is not an encrypted device-bound backup.');
    var clear;
    try { clear = await root.SaagarStore.unseal(sealed); }
    catch (_) { throw codedError('DEVICE_BOUND_BACKUP', 'This backup cannot be decrypted on this device.'); }
    if (!clear) throw codedError('DEVICE_BOUND_BACKUP', 'This backup cannot be decrypted on this device.');
    var text;
    try { text = utf8Decode(clear instanceof Uint8Array ? clear : new Uint8Array(clear)); }
    catch (_) { throw codedError('DEVICE_BOUND_BACKUP', 'The decrypted backup is invalid.'); }
    try { validatePayload(JSON.parse(text)); }
    catch (_) { throw codedError('DEVICE_BOUND_BACKUP', 'The decrypted backup payload is invalid.'); }
    return text;
  }

  async function purgeLegacyDocs() {
    var context = nativeContext();
    if (!context.native || !context.fs) return { deleted: 0, scanned: 0, error: 'Legacy backup storage is unavailable.' };
    var result;
    try { result = await context.fs.readdir({ path: PRIVATE_FOLDER, directory: 'DOCUMENTS' }); }
    catch (error) {
      if (isNotFound(error)) return { deleted: 0, scanned: 0 };
      return { deleted: 0, scanned: 0, error: safeMessage(error) };
    }
    var names = entryNames(result).filter(function (name) { return name === 'latest.json' || LEGACY_DAILY_NAME.test(name); });
    var deleted = 0;
    var failures = [];
    for (var i = 0; i < names.length; i++) {
      try {
        await context.fs.deleteFile({ path: PRIVATE_FOLDER + '/' + names[i], directory: 'DOCUMENTS' });
        deleted++;
      } catch (error) { failures.push(safeMessage(error)); }
    }
    var outcome = { deleted: deleted, scanned: names.length };
    if (failures.length) outcome.error = failures[0];
    return outcome;
  }

  root.SaagarBackup = Object.freeze({
    now: now,
    readLatest: readLatest,
    purgeLegacyDocs: purgeLegacyDocs,
    status: status
  });

  function scheduleAutomatic() {
    function run() { automaticBackup().catch(function (error) { try { root.console.warn('[SaagarBackup]', safeMessage(error)); } catch (_) {} }); }
    var first = function () {
      if (root.SaagarStore && typeof root.SaagarStore.whenReady === 'function') root.SaagarStore.whenReady(run);
      else run();
    };
    if (typeof root.setTimeout === 'function') root.setTimeout(first, AUTO_START_DELAY_MS);
    if (typeof root.setInterval === 'function') root.setInterval(first, AUTO_INTERVAL_MS);
  }

  scheduleAutomatic();
})(typeof window !== 'undefined' ? window : globalThis);
