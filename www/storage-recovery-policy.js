/* Stable, non-sensitive recovery contract for the authoritative native store.
   Native/crypto exception text is deliberately never used as user-facing copy
   or copied diagnostics: only allowlisted reason codes and numeric metadata
   cross this boundary. */
(function (root, factory) {
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  if (root) root.SaagarStorageRecoveryPolicy = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';

  var RECOVERY_CONTRACT_VERSION = 1;
  var DESCRIPTORS = {
    PLUGIN_MISSING: {
      title: 'Secure storage component is unavailable',
      body: 'SAAGAR cannot safely open its protected database in this installation. Restart the app after installing the approved APK. Do not clear app data.'
    },
    STORE_TIMEOUT: {
      title: 'Secure storage did not respond',
      body: 'The protected database did not respond in time. Retry after closing other apps. If this repeats, copy the diagnostics for support. Do not clear app data.'
    },
    NO_SPACE: {
      title: 'Device storage is full',
      body: 'There is not enough free device storage to open the protected database safely. Free some device space, then retry. Do not clear SAAGAR app data.'
    },
    DB_OPEN_FAILED: {
      title: 'Secure storage could not be opened',
      body: 'SAAGAR could not open its protected database. Retry once, then copy the diagnostics for support if the problem continues. Do not clear app data.'
    },
    DB_READ_ONLY: {
      title: 'Secure storage is read-only',
      body: 'The protected database is not writable. Restart the device and retry. If this repeats, copy the diagnostics for support. Do not clear app data.'
    },
    DB_IO_FAILED: {
      title: 'Device storage could not be read',
      body: 'Android reported a storage input/output problem. Restart the device and retry. If this repeats, copy the diagnostics for support. Do not clear app data.'
    },
    SCHEMA_UNSUPPORTED: {
      title: 'This database needs a compatible SAAGAR build',
      body: 'The protected database format is not supported by this APK. Install the approved current build, then retry. Do not clear app data.'
    },
    INTEGRITY_FAILED: {
      title: 'Secure storage needs recovery',
      body: 'The protected database did not pass its integrity check. SAAGAR blocked stale fallback data. Copy the diagnostics and use only an approved recovery procedure.'
    },
    MIGRATION_INCOMPLETE: {
      title: 'Secure storage setup is incomplete',
      body: 'The protected database was not fully published. Retry once, then copy the diagnostics for support if the problem continues. Do not clear app data.'
    },
    KEY_UNAVAILABLE: {
      title: 'Secure storage key is unavailable',
      body: 'Android could not unlock the key for this protected database. Copy the diagnostics and use an approved recovery procedure. Do not clear app data.'
    },
    ROW_AUTH_FAILED: {
      title: 'Protected data verification failed',
      body: 'At least one protected record could not be authenticated. SAAGAR stopped before exposing partial or stale data. Copy the diagnostics for support.'
    },
    ROW_FORMAT_INVALID: {
      title: 'Protected data format is invalid',
      body: 'At least one protected record has an invalid format. SAAGAR stopped before exposing partial or stale data. Copy the diagnostics for support.'
    },
    PAGE_CURSOR_INVALID: {
      title: 'Secure storage read could not continue',
      body: 'The protected database returned an invalid page sequence. Retry once, then copy the diagnostics for support if the problem continues.'
    },
    ROW_COUNT_MISMATCH: {
      title: 'Secure storage verification is incomplete',
      body: 'The protected record count did not match the verified database count. SAAGAR stopped before exposing partial data. Copy the diagnostics for support.'
    },
    DB_READ_FAILED: {
      title: 'Secure storage could not be read',
      body: 'SAAGAR could not read the protected database safely. Retry once, then copy the diagnostics for support if the problem continues.'
    },
    STORE_UNAVAILABLE: {
      title: 'Secure storage needs recovery',
      body: 'SAAGAR could not verify its protected database and has blocked stale fallback data. Retry once, then copy the diagnostics for support. Do not clear app data.'
    }
  };

  var NATIVE_CODE_MAP = {
    E_NATIVE_DB_OPEN: 'DB_OPEN_FAILED',
    E_NATIVE_FULL: 'NO_SPACE',
    E_NATIVE_CORRUPT: 'INTEGRITY_FAILED',
    E_NATIVE_IO: 'DB_IO_FAILED',
    E_NATIVE_READ_ONLY: 'DB_READ_ONLY',
    E_NATIVE_SCHEMA: 'SCHEMA_UNSUPPORTED',
    E_NATIVE_READ: 'DB_READ_FAILED',
    E_NATIVE_VERIFY: 'ROW_COUNT_MISMATCH',
    E_NATIVE_STATUS: 'STORE_UNAVAILABLE',
    E_NATIVE_WRITE: 'STORE_UNAVAILABLE',
    E_NATIVE_MIGRATION: 'MIGRATION_INCOMPLETE',
    E_NATIVE_RESET: 'STORE_UNAVAILABLE',
    E_INVALID_ARGUMENT: 'STORE_UNAVAILABLE',
    E_ARGS: 'STORE_UNAVAILABLE'
  };

  function knownCode(value) {
    value = String(value || '').toUpperCase();
    return Object.prototype.hasOwnProperty.call(DESCRIPTORS, value) ? value : '';
  }

  function reasonFromError(error, fallback) {
    var direct = knownCode(typeof error === 'string' ? error : (error && error.code));
    if (direct) return direct;
    var dataReason = knownCode(error && error.data && error.data.reason);
    if (dataReason) return dataReason;
    var nativeCode = String(error && error.code || '').toUpperCase();
    return NATIVE_CODE_MAP[nativeCode] || knownCode(fallback) || 'STORE_UNAVAILABLE';
  }

  function finiteInteger(value) {
    var number = Number(value);
    return Number.isFinite(number) && number >= 0 && Math.floor(number) === number ? number : null;
  }

  function inspectStatus(status, requireMigrated) {
    if (!status || status.available !== true) {
      return { ok: false, code: reasonFromError(status, 'STORE_UNAVAILABLE') };
    }
    if (String(status.integrity || '').toLowerCase() !== 'ok') {
      return { ok: false, code: 'INTEGRITY_FAILED' };
    }
    if (requireMigrated && status.migrated !== true) {
      return { ok: false, code: 'MIGRATION_INCOMPLETE' };
    }
    var rows = finiteInteger(status.rows);
    if (rows === null) return { ok: false, code: 'ROW_COUNT_MISMATCH' };
    return { ok: true, code: '', rows: rows };
  }

  function descriptor(code) {
    code = knownCode(code) || 'STORE_UNAVAILABLE';
    return {
      code: code,
      title: DESCRIPTORS[code].title,
      body: DESCRIPTORS[code].body
    };
  }

  function safeNumber(value) {
    var number = Number(value);
    return Number.isFinite(number) && number >= 0 ? Math.floor(number) : null;
  }

  function safeStorage(value) {
    value = value && typeof value === 'object' ? value : {};
    return {
      totalBytes: safeNumber(value.totalBytes),
      availableBytes: safeNumber(value.availableBytes),
      freeBytes: safeNumber(value.freeBytes),
      databaseBytes: safeNumber(value.databaseBytes),
      walBytes: safeNumber(value.walBytes),
      shmBytes: safeNumber(value.shmBytes),
      journalBytes: safeNumber(value.journalBytes),
      nativeStoreBytes: safeNumber(value.nativeStoreBytes)
    };
  }

  function diagnostics(input) {
    input = input && typeof input === 'object' ? input : {};
    var code = knownCode(input.code) || 'STORE_UNAVAILABLE';
    return {
      format: 'SAAGAR_STORAGE_RECOVERY',
      contractVersion: RECOVERY_CONTRACT_VERSION,
      createdAt: new Date().toISOString(),
      appVersion: String(input.appVersion || 'unknown').slice(0, 24),
      apkBuild: String(input.apkBuild || 'unknown').slice(0, 24),
      state: input.state === 'pending' ? 'pending' : 'blocked',
      code: code,
      stage: String(input.stage || 'native-status').replace(/[^a-z0-9_-]/gi, '').slice(0, 40) || 'native-status',
      attempt: Math.max(1, safeNumber(input.attempt) || 1),
      canRetry: input.canRetry !== false,
      canRestore: false,
      nativeMarker: input.nativeMarker === true,
      pluginPresent: input.pluginPresent === true,
      schemaVersion: safeNumber(input.schemaVersion),
      expectedRows: safeNumber(input.expectedRows),
      loadedRows: safeNumber(input.loadedRows),
      storage: safeStorage(input.storage)
    };
  }

  return {
    RECOVERY_CONTRACT_VERSION: RECOVERY_CONTRACT_VERSION,
    descriptor: descriptor,
    diagnostics: diagnostics,
    finiteInteger: finiteInteger,
    inspectStatus: inspectStatus,
    reasonFromError: reasonFromError,
    safeStorage: safeStorage
  };
});
