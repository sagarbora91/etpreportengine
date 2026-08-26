/* ═══════════════════════════════════════════════════════════════════════════
   SAAGAR CONTROL CENTRE — STORAGE CORE  (Option C — SQLite-primary, PHASE 2-3)
   ───────────────────────────────────────────────────────────────────────────
   Goal (end state): make an in-memory, SQLite-backed key-value store the source
   of truth so the ~5 MB localStorage ceiling disappears — while keeping the
   SYNCHRONOUS localStorage API every shell function + embedded module uses.

   Build-time flag STORAGE_CORE_ENABLED is OFF by default → this script is a pure
   NO-OP and the app behaves byte-for-byte as it does today. When ON (test/staged):
     • MEM (a Map) is the source of truth once _ready; native localStorage is no
       longer written after _ready (the 5 MB cap is gone). sql.js in-memory DB is
       the persistence layer (atomic .tmp→rename→.bak file writes via Capacitor
       Filesystem). Before _ready (and in the no-FS/no-sql.js fallback) it is
       today's native-localStorage path with a MEM mirror.
     • §13 data-safety hardening: sequenced WAL (crash-durability, never cleared
       past an in-flight persist); atomic .bak rotation (promote-old-live FIRST);
       additive+verified migration with a one-way 'migrated' marker so deleted
       records can NEVER resurrect; DB-wins reconcile on subsequent boots;
       awaited+sequenced Factory Reset; hard boot timeout → fallback.
   FALLBACK: if window.Storage is unavailable or anything throws, we touch nothing.
   ═══════════════════════════════════════════════════════════════════════════ */
(function () {
  'use strict';

  /* ── Build-time flag (Option C). ──
     ENGINE LIVE ON `main` since build 2.0 (passed the §14 gauntlet; see SQLITE_PRIMARY_PLAN.md §17 +
     README §5). The in-memory Map is the source of truth, persisted to bcc.sqlite. Rollback = set false
     (pure no-op → native localStorage). [Historical: this comment used to say "test branch only / main
     stays false" from before the gauntlet shipped — reconciled to the shipped value.] */
  var STORAGE_CORE_ENABLED = true;
  /* test/staging override hook — harnesses set window.__FORCE_STORAGE_CORE; never set in production */
  try { if (typeof window !== 'undefined' && window.__FORCE_STORAGE_CORE === true) STORAGE_CORE_ENABLED = true; } catch (e) {}
  if (!STORAGE_CORE_ENABLED) return;   /* ← DEFAULT PATH: do nothing, app == today */

  try {
    var SP = (typeof window !== 'undefined' && window.Storage && window.Storage.prototype) || null;
    if (!SP || !window.localStorage) { try { console.log('[storage-core] Storage unavailable — staying native'); } catch (e) {} return; }
    if (window.SaagarStore && window.SaagarStore.enabled) return;   /* already installed (defensive) */

    /* capture NATIVE methods up-front so the overrides never recurse */
    var nGet = SP.getItem, nSet = SP.setItem, nRemove = SP.removeItem, nClear = SP.clear, nKey = SP.key;
    var nLenDesc = Object.getOwnPropertyDescriptor(SP, 'length');
    var nLen = (nLenDesc && nLenDesc.get) ? nLenDesc.get : function () { return 0; };
    var ls = window.localStorage;

    /* ════════════════════════════════════════════════════════════════════════
       PHASE 2-3 — ASYNC SQLite ENGINE. Before _ready (and in fallback): today's
       native-localStorage passthrough + MEM mirror. After _ready: MEM is the
       source of truth; the sql.js in-memory DB is the persistence layer.
       ════════════════════════════════════════════════════════════════════════ */

    /* ── module-level state + constants ── */
    var SQL = null, db = null, _ready = false, _dirty = false, _bulk = false, _saveTimer = null, _resetting = false;
    var _whenReadyCbs = [], _bootTimer = null, _lastError = '', _lastSavedAt = null, _dbFromFile = false;
    var _persisting = false, _persistAgain = false, _persistP = null;
    var _nativeMode = false, _nativeBooting = false, _nativeRows = 0, _nativeClearSeq = 0, _nativeReplaceSeq = 0;
    var _storageBlocked = false, _authorityPending = false;
    var _bootAttempt = 0, _activeBootAttempt = 0, _nativeStatusSafe = null, _storageInfoAttempt = 0;
    var nativeDirty = new Map();
    var _persistPerf = [], _persistCounter = 0;   /* §13.2 persist mutex — serialize whole-file FS writes so concurrent flushes never race on the temp files */
    var dirtyKeys = new Set();          /* §13.4 retry set — failed kv writes stay here for retry */
    var _seq = 0;                       /* §13.1 monotonic WAL sequence — makes clear race-free */
    var DB_FILE = 'bcc.sqlite';
    var WAL_KEY = 'saagar_storage_wal';   /* §13.1 synchronous native-LS journal */
    var MIGRATED_KEY = 'saagar_storage_migrated';  /* §13.3 one-way marker: DB is authoritative */
    var NATIVE_MIGRATED_KEY = 'saagar_native_store_migrated_v1';  /* fail closed instead of opening a stale legacy snapshot */
    var STORAGE_LOG_KEY = 'saagar_sqlite_log';
    var DAT02_KEY = 'saagar_dat02_acceptance_v1';
    var INTERNAL = { 'saagar_storage_wal': 1, 'saagar_storage_migrated': 1, 'saagar_native_store_migrated_v1': 1, 'saagar_sqlite_log': 1, 'saagar_dat02_acceptance_v1': 1 };
    var RecoveryPolicy = window.SaagarStorageRecoveryPolicy || null;
    try { _authorityPending = nGet.call(ls, NATIVE_MIGRATED_KEY) === '1'; } catch (e) { _authorityPending = false; }
    var _recovery = {
      state: _authorityPending ? 'pending' : 'idle',
      code: '',
      stage: _authorityPending ? 'native-status' : 'startup',
      attempt: 0,
      canRetry: true,
      canRestore: false,
      nativeMarker: _authorityPending,
      pluginPresent: false,
      schemaVersion: null,
      expectedRows: null,
      loadedRows: 0,
      storage: null
    };
    var RECOVERY_CODES = {
      PLUGIN_MISSING:1, STORE_TIMEOUT:1, NO_SPACE:1, DB_OPEN_FAILED:1,
      DB_READ_ONLY:1, DB_IO_FAILED:1, SCHEMA_UNSUPPORTED:1,
      INTEGRITY_FAILED:1, MIGRATION_INCOMPLETE:1, KEY_UNAVAILABLE:1,
      ROW_AUTH_FAILED:1, ROW_FORMAT_INVALID:1, PAGE_CURSOR_INVALID:1,
      ROW_COUNT_MISMATCH:1, DB_READ_FAILED:1, STORE_UNAVAILABLE:1
    };
    function recoveryCode(error, fallback) {
      if (RecoveryPolicy && RecoveryPolicy.reasonFromError) return RecoveryPolicy.reasonFromError(error, fallback);
      var candidate = typeof error === 'string' ? error : (error && error.data && error.data.reason) || (error && error.code);
      candidate = String(candidate || '').toUpperCase();
      if (RECOVERY_CODES[candidate]) return candidate;
      fallback = String(fallback || '').toUpperCase();
      return RECOVERY_CODES[fallback] ? fallback : 'STORE_UNAVAILABLE';
    }
    function inspectNativeStatus(status, requireMigrated) {
      if (RecoveryPolicy && RecoveryPolicy.inspectStatus) return RecoveryPolicy.inspectStatus(status, requireMigrated);
      if (!status || status.available !== true) return { ok:false, code:recoveryCode(status, 'STORE_UNAVAILABLE') };
      if (String(status.integrity || '').toLowerCase() !== 'ok') return { ok:false, code:'INTEGRITY_FAILED' };
      if (requireMigrated && status.migrated !== true) return { ok:false, code:'MIGRATION_INCOMPLETE' };
      var rows = Number(status.rows);
      if (!Number.isFinite(rows) || rows < 0 || Math.floor(rows) !== rows) return { ok:false, code:'ROW_COUNT_MISMATCH' };
      return { ok:true, code:'', rows:rows };
    }
    function recoveryError(code, message) {
      var error = new Error(message || code || 'Secure storage unavailable');
      error.code = recoveryCode(code, 'STORE_UNAVAILABLE');
      return error;
    }
    function safeNativeStatus(status) {
      status = status && typeof status === 'object' ? status : {};
      function safeNumber(value) { var number = Number(value); return Number.isFinite(number) && number >= 0 ? Math.floor(number) : null; }
      var storage = RecoveryPolicy && RecoveryPolicy.safeStorage ? RecoveryPolicy.safeStorage(status.storage) : (function () {
        var input = status.storage && typeof status.storage === 'object' ? status.storage : {};
        return { totalBytes:safeNumber(input.totalBytes), availableBytes:safeNumber(input.availableBytes), freeBytes:safeNumber(input.freeBytes), databaseBytes:safeNumber(input.databaseBytes), walBytes:safeNumber(input.walBytes), shmBytes:safeNumber(input.shmBytes), journalBytes:safeNumber(input.journalBytes), nativeStoreBytes:safeNumber(input.nativeStoreBytes) };
      })();
      return { contractVersion:safeNumber(status.contractVersion), available:status.available === true, schemaVersion:safeNumber(status.schemaVersion), rows:safeNumber(status.rows), stagedRows:safeNumber(status.stagedRows), migrated:status.migrated === true, integrity:String(status.integrity || '').toLowerCase() === 'ok' ? 'ok' : 'failed', storage:storage };
    }
    function safeStorageInfo(value) {
      var policy = null;
      try { policy = window.SaagarStorageCapacityPolicy || null; } catch (e) {}
      if (policy && typeof policy.sanitizeStorage === 'function') {
        value = policy.sanitizeStorage(value);
      } else {
        value = value && typeof value === 'object' ? value : {};
      }
      function safeBytes(input) {
        var type = typeof input;
        if (input === null || input === undefined || (type !== 'number' && type !== 'string')) return null;
        if (type === 'string' && input.trim() === '') return null;
        var number = Number(input);
        if (!Number.isFinite(number)) return null;
        return Math.min(Number.MAX_SAFE_INTEGER || 9007199254740991, Math.max(0, Math.floor(number)));
      }
      return {
        totalBytes:safeBytes(value.totalBytes),
        availableBytes:safeBytes(value.availableBytes),
        nativeStoreBytes:safeBytes(value.nativeStoreBytes)
      };
    }
    function refreshStorageInfo() {
      var attempt = ++_storageInfoAttempt, plugin = nativeStorePlugin();
      if (!plugin || typeof plugin.storageInfo !== 'function') return Promise.resolve(null);
      return Promise.resolve().then(function () { return plugin.storageInfo({}); }).then(function (value) {
        if (attempt !== _storageInfoAttempt) return null;
        try { return safeStorageInfo(value); } catch (e) { return null; }
      }, function () { return null; });
    }
    var SAVE_DEBOUNCE = 6000;           /* whole-file export is heavy */
    var BOOT_TIMEOUT_MS = 6000;         /* fast status/probe deadline */
    var NATIVE_READ_MIN_TIMEOUT_MS = 120000;
    var NATIVE_READ_MAX_TIMEOUT_MS = 300000;
    var NATIVE_READ_ROW_BUDGET_MS = 25;
    var NATIVE_READ_STALL_TIMEOUT_MS = 15000;
    try {
      if (typeof window !== 'undefined' && window.__BOOT_TIMEOUT_MS) BOOT_TIMEOUT_MS = Number(window.__BOOT_TIMEOUT_MS);
      if (typeof window !== 'undefined' && window.__NATIVE_READ_TIMEOUT_MS) {
        var nativeReadOverride = Number(window.__NATIVE_READ_TIMEOUT_MS);
        if (Number.isFinite(nativeReadOverride) && nativeReadOverride > 0) {
          NATIVE_READ_MIN_TIMEOUT_MS = nativeReadOverride;
          NATIVE_READ_MAX_TIMEOUT_MS = nativeReadOverride;
        }
      }
      if (typeof window !== 'undefined' && window.__NATIVE_READ_STALL_TIMEOUT_MS) {
        var nativeStallOverride = Number(window.__NATIVE_READ_STALL_TIMEOUT_MS);
        if (Number.isFinite(nativeStallOverride) && nativeStallOverride > 0) NATIVE_READ_STALL_TIMEOUT_MS = nativeStallOverride;
      }
    } catch (e) {}
    function nativeReadTimeoutMs(rows) {
      var count = Number(rows);
      if (!Number.isFinite(count) || count < 0) count = 0;
      return Math.min(NATIVE_READ_MAX_TIMEOUT_MS, Math.max(NATIVE_READ_MIN_TIMEOUT_MS, Math.ceil(count) * NATIVE_READ_ROW_BUDGET_MS));
    }
    function armBootTimer(delay) {
      clearTimeout(_bootTimer);
      _bootTimer = setTimeout(bootTimeoutFallback, delay);
    }
    function nativeReadCall(factory) {
      return new Promise(function (resolve, reject) {
        var settled = false;
        var timer = setTimeout(function () {
          if (settled) return;
          settled = true;
          reject(recoveryError('STORE_TIMEOUT'));
        }, NATIVE_READ_STALL_TIMEOUT_MS);
        Promise.resolve().then(factory).then(function (value) {
          if (settled) return;
          settled = true;
          clearTimeout(timer);
          resolve(value);
        }, function (error) {
          if (settled) return;
          settled = true;
          clearTimeout(timer);
          reject(error);
        });
      });
    }
    var WAL_BIG = 50000;                /* §13.1 'set' values larger than this are journaled as a pointer (forces a prompt persist) */
    var WAL_MAX = 512000;               /* §13.1 byte cap on the WAL JSON so it can never approach the native-LS quota */

    /* ── R0-W2 at-rest encryption (whole-file envelope; MEM stays plaintext) ──
       STORAGE_ENCRYPT_ENABLED gates WRITES only — the reader below ALWAYS sniffs
       and decrypts SBCC1 envelopes, so flag-off = next persists write plaintext
       back in place (rollback recipe, no data movement). No marker key: readers
       trust file CONTENT only (SQLite magic vs SBCC1). W2-S1 ships the reader
       INERT (flag false, no writer exists yet — no ciphertext can exist). */
    var STORAGE_ENCRYPT_ENABLED = true;    /* W2-S3: FLIPPED true 2026-07-18 — DT2 device matrix passed on both devices; owner signed OD-1/OD-8/OD-K2..K5 (API-22 exception retired in build 2.9: production minSdk is 23 and all supported devices have Android Keystore). Rollback = set false (DT2-7 proved flag-off reads existing ciphertext then rewrites plaintext, no data loss). */
    try { if (typeof window !== 'undefined' && window.__FORCE_STORAGE_ENCRYPT === true) STORAGE_ENCRYPT_ENABLED = true; } catch (e) {}
    var DEK_FILE = 'bcc.dek';   /* R0-W2 W2-S2a: Keystore-WRAPPED DEK (SKW1 blob), NOT raw key material. (Legacy raw 'bcc.key' was never created in production — S1 shipped inert — so any stray test-APK copy is simply ignored.) */
    var ENV_MAGIC = [0x53, 0x42, 0x43, 0x43, 0x31];   /* "SBCC1" */
    var SQLITE_MAGIC = 'SQLite format 3';             /* first 15 bytes + \0 */

    /* ── ported helpers from sqlite-store.js (copy, do not re-import) ── */
    function FSplugin() { try { return (window.Capacitor && window.Capacitor.Plugins && window.Capacitor.Plugins.Filesystem) || null; } catch (e) { return null; } }
    function nativeStorePlugin() { try { return (window.Capacitor && window.Capacitor.Plugins && window.Capacitor.Plugins.SaagarNativeStore) || null; } catch (e) { return null; } }
    function dataDir() { return 'DATA'; }

    function log(m) {
      try { var a = JSON.parse(nGet.call(ls, STORAGE_LOG_KEY) || '[]'); if (!Array.isArray(a)) a = []; a.unshift({ at: new Date().toISOString(), m: '[core] ' + String(m) }); if (a.length > 60) a = a.slice(0, 60); nSet.call(ls, STORAGE_LOG_KEY, JSON.stringify(a)); } catch (e) {}
      try { console.log('[storage-core] ' + m); } catch (e) {}
    }

    function bytesToB64(u8) { var CHUNK = 0x8000, out = ''; for (var i = 0; i < u8.length; i += CHUNK) out += String.fromCharCode.apply(null, u8.subarray(i, i + CHUNK)); return btoa(out); }
    function b64ToBytes(b64) { var bin = atob(b64), u8 = new Uint8Array(bin.length); for (var i = 0; i < bin.length; i++) u8[i] = bin.charCodeAt(i); return u8; }

    /* ── R0-W2 crypto helpers (E2). Reader half is ALWAYS active; writer half (encryptForPersist)
       is wired in W2-S2 and inert until then. Every failure path is fail-open:
       encrypt failure → plaintext persist (durability > confidentiality);
       decrypt failure → null → the existing recovery chain treats it like corruption (distinct log). */
    function subtleOK() { try { return !!(window.crypto && window.crypto.subtle); } catch (e) { return false; } }
    function isEnvelope(u8) { if (!u8 || u8.length < 18) return false; for (var i = 0; i < 5; i++) if (u8[i] !== ENV_MAGIC[i]) return false; return true; }
    function isSqlite(u8) { if (!u8 || u8.length < 16) return false; for (var i = 0; i < 15; i++) if (u8[i] !== SQLITE_MAGIC.charCodeAt(i)) return false; return true; }
    /* ── R0-W2 W2-S2a: DEK custody = Android Keystore key-wrapping (replaces the raw bcc.key file).
       A random 32-byte DEK does the fast WebCrypto AES-GCM on the DB blob (proven ~98ms/15MB); the DEK
       itself is WRAPPED by a non-exportable hardware KEK in AndroidKeyStore and only the wrapped SKW1 blob
       lands in DATA/bcc.dek. FAIL-OPEN, LOUD (T1 distinct logs + T2 _encState in Diagnostics): a missing
       plugin / wrap fail / orphaned KEK degrades to plaintext-that-works, never a brick. */
    function keystorePlugin() { try { return (window.Capacitor && window.Capacitor.Plugins && window.Capacitor.Plugins.SaagarKeystore) || null; } catch (e) { return null; } }
    var _encState = STORAGE_ENCRYPT_ENABLED ? 'pending' : 'plaintext-flag-off';
    var _encBakDone = false;   /* R0-W2 E5: once-per-boot guard for the post-first-encrypted-persist .bak reseal */
    function importRawDEK(b64) { return window.crypto.subtle.importKey('raw', b64ToBytes(b64), { name: 'AES-GCM' }, false, ['encrypt', 'decrypt']); }
    /* PRECONDITION-1 fix + S2b intent-coalescing fix (adversarial P2): TWO memo cells keyed by intent.
       A writer getDEK(true) can NEVER be handed a reader getDEK(false) promise that resolves null (that
       would skip encryption → one plaintext persist). Each cell independently single-flights same-intent
       concurrent callers and independently clears on a null/reject resolution (sticky-null clear). The
       tails touch DIFFERENT variables, so a null-resolving reader can never clobber an in-flight writer
       promise (no cross-intent reassignment race). resetAll() clears both. */
    var _dekReadP = null;    /* in-flight/last getDEK(false) — decrypt readers */
    var _dekWriteP = null;   /* in-flight/last getDEK(true)  — encrypt writers (the ONLY minting path) */
    function getDEK(createIfMissing) {
     try {                                                          /* airtight never-throw: a partial FS/KS plugin (missing .readFile) could throw synchronously BEFORE the promise chain forms, escaping the tail rejection guard */
      if (createIfMissing) { if (_dekWriteP) return _dekWriteP; }   /* single-flight, per intent — a writer never returns a reader-built promise */
      else { if (_dekReadP) return _dekReadP; }
      var FS = FSplugin();
      if (!FS || !subtleOK()) return Promise.resolve(null);        /* not cached */
      var KS = keystorePlugin();
      var p = FS.readFile({ path: DEK_FILE, directory: dataDir() })
        .then(function (r) { return (r && r.data) ? r.data : null; })   /* wrapped-DEK b64 or null */
        .catch(function () { return null; })
        .then(function (wrappedB64) {
          if (wrappedB64) {                                          /* UNWRAP (reader AND writer) */
            if (!KS) { log('keystore plugin absent — cannot unwrap DEK; plaintext persist'); _encState = 'plaintext-no-keystore'; return null; }
            return KS.unwrapKey({ wrapped: wrappedB64 })
              .then(function (res) { _encState = 'encrypted-keystore'; return importRawDEK(res.data); })
              .catch(function (e) {
                var code = (e && (e.code || e.message)) || '';
                if (String(code).indexOf('E_ORPHAN') >= 0) log('KEK unwrap failed (orphaned) — DEK orphaned; recovery re-migration');
                else log('DEK unwrap failed: ' + (e && e.message));
                _encState = 'plaintext-no-keystore';
                return null;                                         /* → recovery chain */
              });
          }
          if (!createIfMissing) return null;                         /* reader, no file yet → null, NOT cached */
          if (!KS) { log('keystore plugin absent — DEK not minted; plaintext persist'); _encState = 'plaintext-no-keystore'; return null; }
          var dek = new Uint8Array(32); window.crypto.getRandomValues(dek);   /* MINT+WRAP+atomic write BEFORE first ciphertext (H3) */
          return KS.wrapKey({ data: bytesToB64(dek) })
            .then(function (res) {
              _encState = res.backing ? ('encrypted-keystore-' + res.backing) : 'encrypted-keystore';
              return FS.writeFile({ path: DEK_FILE + '.tmp', data: res.wrapped, directory: dataDir() })
                .then(function () { return FS.rename({ from: DEK_FILE + '.tmp', to: DEK_FILE, directory: dataDir() }); })
                .then(function () { log('DEK minted + keystore-wrapped (' + _encState + ')'); return importRawDEK(bytesToB64(dek)); });
            })
            .catch(function (e) { log('KEK wrap failed (' + (e && e.message) + ') — plaintext persist'); _encState = 'plaintext-no-keystore'; return null; });
        });
      /* sticky-null clear, PER-INTENT cell. Each tail touches ONLY its own cell → a null-resolving reader
         can never clobber an in-flight writer promise (and vice-versa). */
      if (createIfMissing) {
        _dekWriteP = p.then(function (key) { if (!key) _dekWriteP = null; return key; },
                            function () { _dekWriteP = null; return null; });
        return _dekWriteP;
      }
      _dekReadP = p.then(function (key) { if (!key) _dekReadP = null; return key; },
                         function () { _dekReadP = null; return null; });
      return _dekReadP;
     } catch (e) { if (createIfMissing) _dekWriteP = null; else _dekReadP = null; try { log('getDEK synchronous error — plaintext: ' + (e && e.message)); } catch (e2) {} return Promise.resolve(null); }
    }
    /* encrypt raw DB bytes → b64 envelope string; ANY failure falls back to PLAINTEXT write (fail-open, logged) */
    function encryptForPersist(raw) {
      if (!STORAGE_ENCRYPT_ENABLED || !subtleOK()) return Promise.resolve(bytesToB64(raw));
      return getDEK(true).then(function (key) {
        if (!key) { log('encrypt skipped — no key (plaintext persist)'); return bytesToB64(raw); }
        var iv = new Uint8Array(12); window.crypto.getRandomValues(iv);
        return window.crypto.subtle.encrypt({ name: 'AES-GCM', iv: iv }, key, raw).then(function (ct) {
          var c = new Uint8Array(ct), out = new Uint8Array(6 + 12 + c.length);
          out.set(ENV_MAGIC, 0); out[5] = 0x01; out.set(iv, 6); out.set(c, 18);
          return bytesToB64(out);
        });
      }).catch(function (e) { log('encrypt failed (' + (e && e.message) + ') — plaintext persist'); return bytesToB64(raw); });
    }
    /* reader: ALWAYS active. plaintext → pass through; envelope → decrypt; failure → null (recovery chain) */
    function decryptIfEnveloped(u8, label) {
      if (!isEnvelope(u8)) return Promise.resolve(u8);   /* plaintext (or junk — open() will reject junk) */
      if (u8[5] !== 0x01) { log('unknown envelope version ' + u8[5] + ' on ' + label + ' — treating as undecryptable'); return Promise.resolve(null); }   /* P2 fold: version-byte dispatch (only 0x01 defined) */
      if (!subtleOK()) { log('decrypt impossible (no subtle) on ' + label); return Promise.resolve(null); }
      return getDEK(false).then(function (key) {
        if (!key) { log('decrypt failed (key missing) on ' + label); return null; }
        var iv = u8.subarray(6, 18), ct = u8.subarray(18);
        return window.crypto.subtle.decrypt({ name: 'AES-GCM', iv: iv }, key, ct)
          .then(function (pt) { return new Uint8Array(pt); })
          .catch(function () { log('decrypt failed (tag/key) on ' + label); return null; });
      }).catch(function () { log('decrypt errored on ' + label); return null; });   /* adversarial S-2 fold: self-contained never-throw guarantee — a synchronous throw inside the .then (partial subtle impl / detached buffer) resolves to null → recovery chain, not an escaping rejection */
    }

    var NATIVE_RECORD_MAGIC = 'SBKV1:';
    var NATIVE_BATCH_OPS = 32;
    function utf8Encode(value) { return new TextEncoder().encode(String(value)); }
    function utf8Decode(value) { return new TextDecoder().decode(value); }
    function sha256Hex(value) {
      if (!subtleOK()) return Promise.reject(recoveryError('KEY_UNAVAILABLE'));
      return window.crypto.subtle.digest('SHA-256', utf8Encode(value)).then(function (hash) {
        var bytes = new Uint8Array(hash), out = '';
        for (var i = 0; i < bytes.length; i++) out += bytes[i].toString(16).padStart(2, '0');
        return out;
      });
    }
    function encodeNativeRecord(key, value, dek) {
      if (!dek) return Promise.reject(recoveryError('KEY_UNAVAILABLE'));
      var iv = new Uint8Array(12); window.crypto.getRandomValues(iv);
      var plain = utf8Encode(JSON.stringify([String(key), String(value == null ? '' : value)]));
      return Promise.all([
        sha256Hex(String(key)),
        window.crypto.subtle.encrypt({ name: 'AES-GCM', iv: iv }, dek, plain)
      ]).then(function (parts) {
        var ciphertext = new Uint8Array(parts[1]), envelope = new Uint8Array(iv.length + ciphertext.length);
        envelope.set(iv, 0); envelope.set(ciphertext, iv.length);
        return { keyId: parts[0], payload: NATIVE_RECORD_MAGIC + bytesToB64(envelope) };
      });
    }
    function decodeNativeRecord(row, dek) {
      if (!dek) return Promise.reject(recoveryError('KEY_UNAVAILABLE'));
      if (!row || typeof row.keyId !== 'string' || !/^[a-f0-9]{64}$/.test(row.keyId) || typeof row.payload !== 'string' || row.payload.indexOf(NATIVE_RECORD_MAGIC) !== 0) {
        return Promise.reject(recoveryError('ROW_FORMAT_INVALID'));
      }
      var envelope;
      try { envelope = b64ToBytes(row.payload.slice(NATIVE_RECORD_MAGIC.length)); }
      catch (e) { return Promise.reject(recoveryError('ROW_FORMAT_INVALID')); }
      if (envelope.length < 29) return Promise.reject(recoveryError('ROW_FORMAT_INVALID'));
      var iv = envelope.subarray(0, 12), ciphertext = envelope.subarray(12);
      return window.crypto.subtle.decrypt({ name: 'AES-GCM', iv: iv }, dek, ciphertext).then(function (plain) {
        var pair;
        try { pair = JSON.parse(utf8Decode(new Uint8Array(plain))); }
        catch (e) { throw recoveryError('ROW_FORMAT_INVALID'); }
        if (!Array.isArray(pair) || pair.length !== 2) throw recoveryError('ROW_FORMAT_INVALID');
        return sha256Hex(String(pair[0])).then(function (keyId) {
          if (keyId !== row.keyId) throw recoveryError('ROW_AUTH_FAILED');
          return { key: String(pair[0]), value: String(pair[1]) };
        });
      }, function () { throw recoveryError('ROW_AUTH_FAILED'); });
    }
    function markNativeDirty(key, seq) {
      if (!_nativeMode) return;
      key = String(key);
      nativeDirty.set(key, { seq: Number(seq) || _seq, type: MEM.has(key) ? 'set' : 'remove' });
      _dirty = true;
    }
    /* kv table ops — on a thrown DB error, queue the key for retry (§13.4, do NOT swallow) */
    function kvUpsert(k, v) {
      if (_nativeMode) { markNativeDirty(k, _seq); return; }
      if (!db) { dirtyKeys.add(String(k)); return; }
      try { db.run('INSERT INTO kv(k,v) VALUES(?,?) ON CONFLICT(k) DO UPDATE SET v=excluded.v', [String(k), String(v == null ? '' : v)]); _dirty = true; }
      catch (e) { _lastError = (e && e.message) || 'upsert'; dirtyKeys.add(String(k)); }
    }
    function kvDelete(k) {
      if (_nativeMode) { markNativeDirty(k, _seq); return; }
      if (!db) { dirtyKeys.add(String(k)); return; }
      try { db.run('DELETE FROM kv WHERE k=?', [String(k)]); _dirty = true; }
      catch (e) { _lastError = (e && e.message) || 'delete'; dirtyKeys.add(String(k)); }
    }
    function kvAll() { var o = {}; if (!db) return o; try { var r = db.exec('SELECT k,v FROM kv'); if (r && r[0]) r[0].values.forEach(function (row) { o[row[0]] = row[1]; }); } catch (e) {} return o; }

    /* MEM = the synchronous working store. Step 0 (sync, instant): hydrate from
       native localStorage so reads work immediately + upgrade-from-localStorage
       migration is lossless. Native stays a parallel copy; once the 'migrated'
       marker is set, reconcile() makes the DB authoritative (DB-wins) so a stale
       native-LS copy can never resurrect a deleted record (§13.3). */
    var MEM = new Map();
    if (!_authorityPending) (function hydrate() { try { var n = nLen.call(ls); for (var i = 0; i < n; i++) { var k = nKey.call(ls, i); if (k != null && !INTERNAL[k]) MEM.set(k, nGet.call(ls, k)); } } catch (e) {} })();

    /* ── §13.1 WAL — sequenced synchronous native-LS crash journal ──
       Every set/remove/clear is stamped with a monotonic seq. persist() captures
       the seq it snapshotted and clears ONLY entries <= that seq, so a write that
       lands DURING an in-flight async persist is never dropped. Large 'set' values
       are journaled as a pointer (no inline value) + a forced persist, so the WAL
       can never approach the native-LS quota. */
    function walRead() { var w; try { w = JSON.parse(nGet.call(ls, WAL_KEY) || '[]'); } catch (e) { w = []; } return Array.isArray(w) ? w : []; }
    function appendWAL(op, k, v) {
      var entry;
      if (op === 'clear') entry = { op: 'clear', ts: Date.now(), seq: ++_seq };
      else if (op === 'remove') entry = { op: 'remove', k: k, ts: Date.now(), seq: ++_seq };
      else { /* set */ var big = (v != null && String(v).length > WAL_BIG); entry = big ? { op: 'set', k: k, big: 1, ts: Date.now(), seq: ++_seq } : { op: 'set', k: k, v: v, ts: Date.now(), seq: ++_seq }; }
      try {
        var w = walRead(); w.push(entry);
        var s = JSON.stringify(w);
        if (s.length > WAL_MAX) {
          /* §13.1: do NOT blind-drop oldest entries (that would erase the only synchronously-durable
             record of a not-yet-persisted key). First degrade inline 'set' values to POINTERS (the
             value lives in the DB) so every key keeps a WAL record, and FORCE a prompt persist so the
             DB hits disk promptly. Only shift as an absolute last resort. */
          for (var di = 0; di < w.length && s.length > WAL_MAX; di++) { if (w[di] && w[di].op === 'set' && !w[di].big && w[di].v != null) { delete w[di].v; w[di].big = 1; s = JSON.stringify(w); } }
          while (s.length > WAL_MAX && w.length > 1) { w.shift(); s = JSON.stringify(w); }
          _dirty = true; try { flush(); } catch (_) {}
        }
        nSet.call(ls, WAL_KEY, s);
      } catch (e) {
        /* quota or other failure: do NOT silently lose durability — escalate to a prompt persist */
        _lastError = 'wal:' + ((e && e.message) || 'append'); try { _dirty = true; flush(); } catch (_) {}
      }
      if (entry.big) { _dirty = true; try { flush(); } catch (_) {} }   /* big value: persist promptly so the pointer is quickly backed by the DB */
      return entry.seq;
    }
    /* §13.1 race-free clear: keep only entries newer than the persisted snapshot. Returns # kept. */
    function clearWALThrough(through) {
      try {
        var w = walRead();
        var keep = []; for (var i = 0; i < w.length; i++) { if (w[i] && w[i].seq > through) keep.push(w[i]); }
        if (keep.length) nSet.call(ls, WAL_KEY, JSON.stringify(keep)); else nRemove.call(ls, WAL_KEY);
        return keep.length;
      } catch (e) { return -1; }
    }
    function clearWAL() { try { nRemove.call(ls, WAL_KEY); } catch (e) {} }
    function replayWAL() {                     /* applied in order into MEM + db before reconcile */
      var w = walRead(); if (!w.length) return;
      log('WAL non-empty (' + w.length + ') — replaying');
      for (var i = 0; i < w.length; i++) {
        var op = w[i]; if (!op || !op.op) continue;
        _seq = Math.max(_seq, Number(op.seq) || 0);
        try {
          if (op.op === 'clear') { MEM.clear(); try { db && db.run('DELETE FROM kv'); } catch (e) {} if (_nativeMode) { nativeDirty.clear(); _nativeClearSeq = Number(op.seq) || _seq; } }
          else if (op.op === 'remove') { if (op.k != null) { MEM.delete(op.k); if (_nativeMode) markNativeDirty(op.k, op.seq); else kvDelete(op.k); } }
          else if (op.op === 'set') {
            if (op.big) { /* value not journaled inline — it should already be in the DB from the forced persist; if absent it was lost in the crash. */ if (!MEM.has(op.k)) log('WAL big-set ' + op.k + ' not recoverable from journal (relying on DB)'); }
            else if (op.k != null) { MEM.set(op.k, op.v == null ? '' : op.v); if (_nativeMode) markNativeDirty(op.k, op.seq); else kvUpsert(op.k, op.v == null ? '' : op.v); }
          }
        } catch (e) { log('WAL replay err ' + i + ': ' + (e && e.message)); }
      }
      _dirty = true;
    }

    /* ── §13.2 atomic persist + ordered .bak rotation ──
       Order: promote CURRENT good live → .bak (atomic via .bak.tmp+rename) FIRST,
       THEN write new live (.tmp+rename). At any kill point either (live old, bak
       old/older) or (live new, bak = previous good) — never two bad files. */
    function perfNow() { try { return performance.now(); } catch (e) { return Date.now(); } }
    function recordPersistPerf(id, ok, exportMs, totalMs) {
      _persistPerf.push({ id: id, at: new Date().toISOString(), ok: ok === true, exportMs: Math.max(0, exportMs || 0), totalMs: Math.max(0, totalMs || 0) });
      if (_persistPerf.length > 40) _persistPerf = _persistPerf.slice(-40);
    }
    function scheduleSave() { if (_resetting || _bulk) return; clearTimeout(_saveTimer); _saveTimer = setTimeout(function () { flush(); }, SAVE_DEBOUNCE); }
    function encodeNativeOp(snapshot, dek) {
      if (snapshot.type === 'remove') return sha256Hex(snapshot.key).then(function (keyId) { return { type: 'remove', keyId: keyId, seq: snapshot.seq }; });
      return encodeNativeRecord(snapshot.key, snapshot.value, dek).then(function (record) {
        return { type: 'set', keyId: record.keyId, payload: record.payload, seq: snapshot.seq };
      });
    }
    function applyNativeSnapshots(plugin, snapshots, clearFirst, dek, stage) {
      var index = 0, pending = null, clearPending = !!clearFirst;
      function nextBatch() {
        var ops = [], bytes = 0;
        function fill() {
          if (pending) {
            var pendingBytes = (pending.keyId || '').length + (pending.payload || '').length;
            if (ops.length && bytes + pendingBytes > 12 * 1024 * 1024) return Promise.resolve();
            ops.push(pending); bytes += pendingBytes; pending = null;
          }
          if (index >= snapshots.length || ops.length >= NATIVE_BATCH_OPS) return Promise.resolve();
          return encodeNativeOp(snapshots[index++], dek).then(function (op) {
            var opBytes = (op.keyId || '').length + (op.payload || '').length;
            if (ops.length && bytes + opBytes > 12 * 1024 * 1024) { pending = op; return; }
            ops.push(op); bytes += opBytes;
            return fill();
          });
        }
        return fill().then(function () {
          if (!ops.length && !clearPending) return true;
          var doClear = clearPending; clearPending = false;
          return plugin.applyBatch({ ops: ops, clear: doClear, stage: stage === true }).then(function () {
            if (index < snapshots.length || pending) return nextBatch();
            return true;
          });
        });
      }
      return nextBatch();
    }
    function persistNative() {
      var plugin = nativeStorePlugin();
      if (_resetting || !_ready || !_nativeMode || !plugin) return Promise.resolve(false);
      if (_persisting) { if (_dirty || nativeDirty.size || _nativeClearSeq || _nativeReplaceSeq) _persistAgain = true; return _persistP; }
      if (!_dirty && !nativeDirty.size && !_nativeClearSeq && !_nativeReplaceSeq) return Promise.resolve(true);
      _persisting = true;
      var through = _seq, clearSeq = (_nativeClearSeq && _nativeClearSeq <= through) ? _nativeClearSeq : 0;
      var replaceSeq = (_nativeReplaceSeq && _nativeReplaceSeq <= through) ? _nativeReplaceSeq : 0;
      var snapshots = [];
      if (replaceSeq) {
        MEM.forEach(function (value, key) { if (!INTERNAL[key]) snapshots.push({ key: key, seq: through, type: 'set', value: value }); });
      } else {
        nativeDirty.forEach(function (entry, key) {
          if (entry.seq <= through) snapshots.push({ key: key, seq: entry.seq, type: MEM.has(key) ? 'set' : 'remove', value: MEM.has(key) ? MEM.get(key) : '' });
        });
      }
      var persistStarted = perfNow(), workStarted = perfNow(), perfId = ++_persistCounter;
      _persistP = getDEK(true).then(function (dek) {
        if (!dek) throw new Error('Native incremental persistence requires the Android Keystore encryption key.');
        if (!replaceSeq) return applyNativeSnapshots(plugin, snapshots, !!clearSeq, dek, false);
        return plugin.beginMigration({}).then(function () {
          return applyNativeSnapshots(plugin, snapshots, false, dek, true);
        }).then(function () { return plugin.finishMigration({ expectedRows: snapshots.length }); }).then(function (result) {
          if (!result || result.migrated !== true || Number(result.rows) !== snapshots.length) throw new Error('Native bulk publish verification failed');
          return true;
        });
      }).then(function () {
        for (var i = 0; i < snapshots.length; i++) {
          var current = nativeDirty.get(snapshots[i].key);
          if (current && current.seq === snapshots[i].seq) nativeDirty.delete(snapshots[i].key);
        }
        if (clearSeq && _nativeClearSeq === clearSeq) _nativeClearSeq = 0;
        if (replaceSeq && _nativeReplaceSeq === replaceSeq) _nativeReplaceSeq = 0;
        var remaining = clearWALThrough(through);
        _dirty = remaining > 0 || nativeDirty.size > 0 || _nativeClearSeq > 0 || _nativeReplaceSeq > 0;
        _nativeRows = MEM.size;
        _lastSavedAt = new Date().toISOString();
        return true;
      }, function (e) {
        _lastError = (e && e.message) || 'native-persist';
        log('native incremental persist failed: ' + _lastError + ' - WAL kept');
        return false;
      }).then(function (ok) {
        recordPersistPerf(perfId, ok === true, perfNow() - workStarted, perfNow() - persistStarted);
        _persisting = false;
        if (_persistAgain) { _persistAgain = false; return persistNative(); }
        if (_dirty || nativeDirty.size || _nativeClearSeq || _nativeReplaceSeq) scheduleSave();
        return ok;
      });
      return _persistP;
    }
    function persist() {
      if (_nativeMode) return persistNative();
      if (_resetting || !_ready || !db || !FSplugin()) return Promise.resolve(false);
      /* MUTEX (§13.2): only one whole-file write at a time. A flush requested mid-persist sets a
         re-run flag and shares the in-flight promise, so callers awaiting flush() see a completed
         persist that reflects the latest state, and the .tmp/.bak files are never raced. */
      if (_persisting) { if (_dirty || dirtyKeys.size) _persistAgain = true; return _persistP; }
      if (!_dirty && !dirtyKeys.size) return Promise.resolve(true);
      _persisting = true;
      var FS = FSplugin(), dir = dataDir(), through, raw, persistStarted = perfNow(), exportStarted = perfNow(), exportMs = 0, perfId = ++_persistCounter;
      try { through = _seq; raw = db.export(); exportMs = perfNow() - exportStarted; }                                          /* seq+snapshot stay SYNCHRONOUS before ANY await — clearWALThrough(through) invariant (§13.1/§13.2) depends on it; raw is a detached copy, so the deferred encrypt over it stays consistent with `through` */
      catch (e) { _persisting = false; _lastError = (e && e.message) || 'export'; recordPersistPerf(perfId, false, perfNow() - exportStarted, perfNow() - persistStarted); return Promise.resolve(false); }
      _persistP = FS.copy({ from: DB_FILE, to: DB_FILE + '.bak.tmp', directory: dir })   /* 1) promote current good live → .bak (atomic) */
        .then(function () { return FS.rename({ from: DB_FILE + '.bak.tmp', to: DB_FILE + '.bak', directory: dir }); })
        .catch(function () { return null; })                                              /* no live yet (first persist) → skip; .bak lags live by one */
        .then(function () { return encryptForPersist(raw); })                             /* R0-W2 E3: async encrypt INSIDE the mutex; flag-off ⇒ Promise.resolve(bytesToB64(raw)); no-key/encrypt-fail ⇒ plaintext b64 (self-catches → NEVER rejects) */
        .then(function (b64) { return FS.writeFile({ path: DB_FILE + '.tmp', data: b64, directory: dir }); })   /* 2) write new (maybe-encrypted) live atomically */
        .then(function () { return FS.rename({ from: DB_FILE + '.tmp', to: DB_FILE, directory: dir }); })
        .then(function () { var remaining = clearWALThrough(through); _dirty = (remaining > 0) || dirtyKeys.size > 0; _lastSavedAt = new Date().toISOString(); return true; },
              function (e) { _lastError = (e && e.message) || 'persist'; log('persist failed: ' + _lastError + ' — WAL kept'); return false; })
        .then(function (r) {                                                              /* release mutex; re-run once if a write landed during the in-flight persist */
          recordPersistPerf(perfId, r === true, exportMs, perfNow() - persistStarted);
          _persisting = false;
          if (_persistAgain) { _persistAgain = false; return persist(); }
          if (_dirty || dirtyKeys.size) scheduleSave();
          return r;
        });
      return _persistP;
    }
    function flush() {
      if (_bulk) return Promise.resolve(false);   /* §bulk: suspend all persistence until endBulk does the single durable write */
      if (db && dirtyKeys.size) { dirtyKeys.forEach(function (k) { try { var v = MEM.get(k); if (v !== undefined) { db.run('INSERT INTO kv(k,v) VALUES(?,?) ON CONFLICT(k) DO UPDATE SET v=excluded.v', [k, v]); } else { db.run('DELETE FROM kv WHERE k=?', [k]); } dirtyKeys.delete(k); } catch (e) { log('retry failed ' + k + ': ' + (e && e.message)); } }); }
      return persist();
    }

    function readPersistenceAcceptance() {
      try {
        var raw = nGet.call(ls, DAT02_KEY);
        if (!raw) return null;
        var value = JSON.parse(raw);
        return value && value.contract === 'DAT-02-v1' ? value : { damaged: true };
      } catch (e) { return { damaged: true }; }
    }
    function runPersistenceAcceptance() {
      var contract = window.SaagarPersistenceAcceptance;
      if (!_ready || (_nativeMode ? !nativeStorePlugin() : (!db || !FSplugin()))) return Promise.reject(new Error('SQLite persistence is not ready on this device.'));
      if (!contract || typeof contract.evaluate !== 'function' || typeof window.requestAnimationFrame !== 'function') return Promise.reject(new Error('DAT-02 measurement service is unavailable.'));
      var samples = [];
      function oneSample() {
        return new Promise(function (resolve) {
          window.requestAnimationFrame(function () {
            /* Some Android WebViews expose a requestAnimationFrame timestamp from
               a different clock domain while their window/surface is being
               replaced. DAT-02 must compare one monotonic clock throughout the
               sample or a fast save can be reported with a multi-second frame
               gap. */
            var frameStart = perfNow(), beforeId = _persistCounter, totalStart = frameStart;
            _dirty = true;
            var save = flush();
            window.requestAnimationFrame(function () {
              var frameEnd = perfNow();
              Promise.resolve(save).then(function (ok) {
                var perf = null;
                for (var i = 0; i < _persistPerf.length; i++) if (_persistPerf[i].id > beforeId) { perf = _persistPerf[i]; break; }
                resolve({ ok: ok === true && !!perf && perf.ok === true, exportMs: perf ? perf.exportMs : Infinity, frameGapMs: Math.max(0, frameEnd - frameStart), totalMs: Math.max(0, perfNow() - totalStart) });
              }, function () { resolve({ ok: false, exportMs: Infinity, frameGapMs: Math.max(0, frameEnd - frameStart), totalMs: Math.max(0, perfNow() - totalStart) }); });
            });
          });
        });
      }
      var chain = Promise.resolve(_persistP).catch(function () {});
      for (var n = 0; n < contract.THRESHOLDS.samples; n++) chain = chain.then(oneSample).then(function (sample) { samples.push(sample); });
      return chain.then(function () {
        var result = contract.evaluate(samples);
        try {
          nSet.call(ls, DAT02_KEY, JSON.stringify(result));
          if (nGet.call(ls, DAT02_KEY) !== JSON.stringify(result)) throw new Error('verification mismatch');
        } catch (e) { throw new Error('DAT-02 result could not be saved.'); }
        log('DAT-02 ' + (result.accepted ? 'accepted' : 'rejected') + ': export p95=' + result.metrics.exportP95Ms + 'ms, frame p95=' + result.metrics.frameGapP95Ms + 'ms, total p95=' + result.metrics.totalP95Ms + 'ms');
        return result;
      });
    }

    /* ── §13.8 _notify — synthetic StorageEvent for PARENT-window listeners only ──
       MEM-mode stops writing native localStorage, so the browser's 'storage' event
       no longer fires. We re-fire it on THIS window for the only real consumer:
       integration-bridge.js:532 (debounced safeCycle). NOTE: a manually-dispatched
       event does NOT cross frames — the in-iframe injectMobileMode 'storage' listener
       is NOT reached by this (and does not need to be: saagar_ui_mode coherence is
       driven by the ST_UI_MODE postMessage from applyUiModeToFrame, not storage). */
    function _notify(key, oldVal, newVal) {
      try {
        var evt;
        try { evt = new StorageEvent('storage', { key: String(key), oldValue: oldVal == null ? null : String(oldVal), newValue: newVal == null ? null : String(newVal), url: location.href, storageArea: ls }); }
        catch (e) { evt = new Event('storage'); evt.key = String(key); evt.newValue = newVal; evt.oldValue = oldVal; }
        window.dispatchEvent(evt);
      } catch (e) {}
    }

    /* ── §perf: lazily-cached ORDERED key list. SP.key(i)/keys() previously rebuilt Array.from(MEM.keys())
       on EVERY call, so a module loop `for(i=0;i<localStorage.length;i++) key(i)` was O(n²) (~11M ops at
       3,300 keys → multi-second stalls + scroll jank). The cache is NULLED on every MEM set(new)/delete/
       clear — over-invalidation is harmless because a read loop performs no writes mid-loop, so the cache
       builds once and serves the whole O(n) loop. It can never be stale (any key-set change drops it). ── */
    var _keysCache = null;
    function memKeys() { if (_keysCache === null) _keysCache = Array.from(MEM.keys()); return _keysCache; }

    function storageUnavailable() { return _storageBlocked || _authorityPending; }
    function blockedStorageError() {
      var error = new Error('Authoritative native storage is unavailable');
      error.code = 'STORAGE_BLOCKED';
      return error;
    }
    /* ── §3.2 overrides: MEM authoritative once _ready; native passthrough before ── */
    SP.getItem = function (k) { if (storageUnavailable()) return null; return _ready ? (MEM.has(String(k)) ? MEM.get(String(k)) : null) : nGet.call(ls, k); };
    SP.setItem = function (k, v) {
      if (storageUnavailable()) throw blockedStorageError();
      k = String(k); v = String(v);
      if (_bulk && _ready) { if (!MEM.has(k)) _keysCache = null; MEM.set(k, v); if (_nativeMode) { _seq++; markNativeDirty(k, _seq); } else kvUpsert(k, v); return; }   /* §bulk: MEM + in-memory DB only — no per-write WAL/flush; one durable persist at endBulk (seed/large restore) */
      var _isNew = !MEM.has(k);                         /* only a NEW key changes the key list */
      appendWAL('set', k, v);                          /* §13.1 synchronous journal FIRST */
      if (_ready) { MEM.set(k, v); if (_isNew) _keysCache = null; kvUpsert(k, v); scheduleSave(); _notify(k, undefined, v); }
      else { var r = nSet.call(ls, k, v); MEM.set(k, v); if (_isNew) _keysCache = null; return r; }   /* pre-ready: today's path + mirror */
    };
    SP.removeItem = function (k) {
      if (storageUnavailable()) throw blockedStorageError();
      k = String(k);
      if (_bulk && _ready) { MEM.delete(k); _keysCache = null; if (_nativeMode) { _seq++; markNativeDirty(k, _seq); } else kvDelete(k); return; }
      appendWAL('remove', k);
      /* §13.3: MIRROR the delete to native LS too. Native is a frozen migration snapshot used only as
         the last-ditch catastrophic safety copy; if we never propagated deletes, a key the user deleted
         in C-mode could resurrect when ALL DB files are lost. Deletes only SHRINK native (sets stay
         MEM-only), so the 5 MB cap stays gone. */
      if (_ready) { MEM.delete(k); _keysCache = null; kvDelete(k); try { nRemove.call(ls, k); } catch (e) {} scheduleSave(); _notify(k, undefined, null); }
      else { var r = nRemove.call(ls, k); MEM.delete(k); _keysCache = null; return r; }
    };
    SP.clear = function () {
      if (storageUnavailable()) throw blockedStorageError();
      if (_ready) {
        if (_bulk) { _seq++; MEM.clear(); _keysCache = null; dirtyKeys.clear(); nativeDirty.clear(); if (_nativeMode) _nativeClearSeq = _seq; else try { db && db.run('DELETE FROM kv'); } catch (e) {} _dirty = true; return; }
        var clearSeq = appendWAL('clear');                            /* §13.1 single O(1) sentinel — immune to the WAL byte-bound */
        /* §13.3: mirror the clear to native LS too (business keys only — keep WAL/marker/log/demo-guard)
           so the catastrophic re-migration finds no zombies. Synchronous + crash-safe (the WAL sentinel
           handles a crash before persist). */
        try { var ks = []; MEM.forEach(function (_v, k) { if (!INTERNAL[k]) ks.push(k); }); for (var ci = 0; ci < ks.length; ci++) { try { nRemove.call(ls, ks[ci]); } catch (e) {} } } catch (e) {}
        MEM.clear(); _keysCache = null; try { db && db.run('DELETE FROM kv'); } catch (e) {} dirtyKeys.clear();
        if (_nativeMode) { nativeDirty.clear(); _nativeClearSeq = clearSeq; }
        _dirty = true; flush();                        /* force a persist — clear durability must not ride the debounce */
      } else { var r = nClear.call(ls); MEM.clear(); _keysCache = null; return r; }
    };
    SP.key = function (i) { if (storageUnavailable()) return null; return _ready ? (memKeys()[i] || null) : nKey.call(ls, i); };  /* §13.7 ordered, O(1) via cache */
    try { Object.defineProperty(SP, 'length', { configurable: true, get: function () { if (storageUnavailable()) return 0; return _ready ? MEM.size : nLen.call(ls); } }); } catch (e) {}

    /* ── §13.3 reconcile() — marker-gated migration. Returns {firstBoot, verified}. ──
        • First C-boot (no marker): recovery (DB-only→MEM) + additive migrate-up
          (MEM→DB; native LS NEVER deleted) + PER-KEY verify. boot() sets the marker
          only after a verified pass persists successfully.
        • Subsequent boots (marker + a DB that actually loaded from a file): DB-WINS —
          MEM is replaced from the DB, so a deleted record (still present in the frozen
          native-LS copy that Step-0 hydrated) is dropped and can never resurrect, and
          a key updated in C-mode is never clobbered by the stale native value.
        • Marker present but the DB did NOT load from a file (all files corrupt/missing):
          do NOT wipe — re-migrate from the native-LS safety copy (preserve data). */
    function reconcile() {
      var dbRows = kvAll();
      var migrated = false; try { migrated = !!nGet.call(ls, MIGRATED_KEY); } catch (e) {}

      if (migrated && _dbFromFile) {
        /* DB-WINS: MEM := DB (the DB already reflects any un-persisted ops via replayWAL above). */
        MEM.clear();
        Object.keys(dbRows).forEach(function (k) { if (!INTERNAL[k]) MEM.set(k, dbRows[k]); });
        return { firstBoot: false, verified: true };
      }

      /* First boot, OR catastrophic recovery (marker set but file lost → fall back to native-LS copy). */
      if (migrated && !_dbFromFile) log('migrated marker set but no DB file loaded — re-migrating from native-LS safety copy');
      var recovered = 0;
      Object.keys(dbRows).forEach(function (k) { if (INTERNAL[k]) return; if (!MEM.has(k)) { MEM.set(k, dbRows[k]); recovered++; } });
      if (recovered) log('recovery: hydrated ' + recovered + ' key(s) from DB into MEM');
      var migCount = 0;
      MEM.forEach(function (v, k) { if (INTERNAL[k]) return; if (dbRows[k] !== v) { kvUpsert(k, v); migCount++; } });
      if (migCount) log('migrated ' + migCount + ' key(s) MEM → DB');
      /* §13.3 PER-KEY verification (not just a count). */
      var verified = true, mism = 0;
      try {
        var after = kvAll();
        MEM.forEach(function (v, k) { if (INTERNAL[k]) return; if (after[k] !== v) { verified = false; mism++; dirtyKeys.add(k); } });
        if (!verified) log('verify mismatch on ' + mism + ' key(s) — DB NOT promoted; data safe in MEM + native LS; retrying');
      } catch (e) { verified = false; }
      return { firstBoot: !migrated, verified: verified };
    }

    function loadNativeRows(plugin, status) {
      var inspected = inspectNativeStatus(status, true);
      if (!inspected.ok) return Promise.reject(recoveryError(inspected.code));
      var expected = inspected.rows, loaded = 0, after = '';
      var MAX_NATIVE_RECORD_CHARS = 16 * 1024 * 1024;
      var MAX_INLINE_NATIVE_RECORD_CHARS = 512 * 1024;
      var RECORD_CHUNK_CHARS = 256 * 1024;
      var DECRYPT_BATCH_SIZE = 8;
      _recovery.stage = 'native-read'; _recovery.expectedRows = expected; _recovery.loadedRows = 0;
      MEM.clear(); _keysCache = null; db = null; dirtyKeys.clear();
      function finiteInteger(value) {
        var number = Number(value);
        return Number.isFinite(number) && number >= 0 && number <= 9007199254740991 && Math.floor(number) === number ? number : null;
      }
      return getDEK(false).then(function (dek) {
        if (!dek) throw recoveryError('KEY_UNAVAILABLE');
        function commitRecord(record) {
          if (!INTERNAL[record.key]) MEM.set(record.key, record.value);
          loaded++; _recovery.loadedRows = loaded;
        }
        function decodePageRows(rows) {
          var at = 0;
          function nextBatch() {
            if (at >= rows.length) return Promise.resolve();
            var batch = rows.slice(at, at + DECRYPT_BATCH_SIZE);
            at += batch.length;
            return Promise.all(batch.map(function (row) { return decodeNativeRecord(row, dek); })).then(function (records) {
              records.forEach(commitRecord);
              return nextBatch();
            });
          }
          return nextBatch();
        }
        function readOversizedRecord(meta) {
          var keyId = String(meta && meta.keyId || '');
          var total = finiteInteger(meta && meta.chars);
          var sequence = finiteInteger(meta && meta.seq);
          if (!/^[a-f0-9]{64}$/.test(keyId) || keyId <= after || total === null || total <= MAX_INLINE_NATIVE_RECORD_CHARS || total > MAX_NATIVE_RECORD_CHARS || sequence === null || typeof plugin.readRecordChunk !== 'function') {
            throw recoveryError('ROW_FORMAT_INVALID');
          }
          var offset = 0, chunks = [];
          function nextChunk() {
            return nativeReadCall(function () {
              return plugin.readRecordChunk({ keyId: keyId, offset: offset, limit: RECORD_CHUNK_CHARS });
            }).catch(function (error) {
              throw recoveryError(recoveryCode(error, 'DB_READ_FAILED'));
            }).then(function (part) {
              if (!part || typeof part !== 'object' || String(part.keyId || '') !== keyId) throw recoveryError('PAGE_CURSOR_INVALID');
              var partOffset = finiteInteger(part.offset);
              var nextOffset = finiteInteger(part.nextOffset);
              var partTotal = finiteInteger(part.totalChars);
              var partSequence = finiteInteger(part.seq);
              if (partOffset !== offset || partTotal !== total || partSequence !== sequence || nextOffset === null || nextOffset <= offset || nextOffset > total) {
                throw recoveryError('PAGE_CURSOR_INVALID');
              }
              if (typeof part.chunk !== 'string' || part.chunk.length < 1 || part.chunk.length > RECORD_CHUNK_CHARS || nextOffset !== offset + part.chunk.length) {
                throw recoveryError('ROW_FORMAT_INVALID');
              }
              var finished = nextOffset === total;
              if (part.done !== finished) throw recoveryError('PAGE_CURSOR_INVALID');
              chunks.push(part.chunk);
              offset = nextOffset;
              if (!finished) return nextChunk();
              var payload = chunks.join('');
              chunks.length = 0;
              if (payload.length !== total) throw recoveryError('ROW_FORMAT_INVALID');
              return decodeNativeRecord({ keyId: keyId, payload: payload, seq: sequence }, dek);
            });
          }
          return nextChunk();
        }
        function page() {
          return nativeReadCall(function () {
            return plugin.readPage({ afterKeyId: after, limit: 32, maxBytes: 2 * 1024 * 1024 });
          }).catch(function (error) {
            throw recoveryError(recoveryCode(error, 'DB_READ_FAILED'));
          }).then(function (result) {
            if (!result || typeof result !== 'object' || !Array.isArray(result.rows)) throw recoveryError('ROW_FORMAT_INVALID');
            var rows = result.rows;
            var pageCursor = after;
            rows.forEach(function (row) {
              var keyId = String(row && row.keyId || '');
              if (!/^[a-f0-9]{64}$/.test(keyId) || keyId <= pageCursor) throw recoveryError('PAGE_CURSOR_INVALID');
              pageCursor = keyId;
            });
            if (result.oversized !== undefined && result.oversized !== null) {
              if (rows.length !== 0 || result.done !== false || String(result.afterKeyId || '') !== after) throw recoveryError('PAGE_CURSOR_INVALID');
              return readOversizedRecord(result.oversized).then(function (record) {
                commitRecord(record);
                after = String(result.oversized.keyId || '');
                return page();
              });
            }
            return decodePageRows(rows).then(function () {
              var returnedCursor = String(result.afterKeyId || '');
              if (result.done === false) {
                if (!rows.length || returnedCursor !== pageCursor || returnedCursor <= after) throw recoveryError('PAGE_CURSOR_INVALID');
                after = returnedCursor;
                return page();
              }
              if (result.done !== true || returnedCursor !== pageCursor) throw recoveryError('PAGE_CURSOR_INVALID');
            });
          });
        }
        return page();
      }).then(function () {
        if (_storageBlocked) return false;
        if (loaded !== expected) throw recoveryError('ROW_COUNT_MISMATCH');
        _nativeRows = loaded; _dbFromFile = loaded > 0; _nativeMode = true;
        replayWAL(); _keysCache = null; _nativeBooting = false; _authorityPending = false; _storageBlocked = false;
        _recovery.state = 'ready'; _recovery.code = ''; _recovery.stage = 'ready'; _recovery.loadedRows = loaded;
        setReady();
        if (_dirty || nativeDirty.size || _nativeClearSeq) flush();
        try { nSet.call(ls, NATIVE_MIGRATED_KEY, '1'); } catch (e) {}
        log('native incremental SQLite active (' + loaded + ' encrypted records)');
        return true;
      });
    }
    function migrateToNative(plugin) {
      var snapshots = [], migrateSeq = _seq;
      MEM.forEach(function (value, key) { if (!INTERNAL[key]) snapshots.push({ key: key, value: value, type: 'set', seq: migrateSeq }); });
      return getDEK(true).then(function (dek) {
        if (!dek) throw new Error('Android Keystore key unavailable for native migration');
        return plugin.beginMigration({}).then(function () {
          return applyNativeSnapshots(plugin, snapshots, false, dek, true);
        }).then(function () {
          return plugin.finishMigration({ expectedRows: snapshots.length });
        });
      }).then(function (result) {
        if (!result || result.migrated !== true || Number(result.rows) !== snapshots.length) throw new Error('Native migration verification did not complete');
        _nativeMode = true; _nativeRows = snapshots.length; try { if (db && db.close) db.close(); } catch (e) {} db = null; dirtyKeys.clear(); nativeDirty.clear(); _nativeClearSeq = 0; _nativeReplaceSeq = 0;
        clearWALThrough(migrateSeq); _dirty = false; _lastSavedAt = new Date().toISOString(); _nativeBooting = false;
        try { nSet.call(ls, MIGRATED_KEY, '1'); nSet.call(ls, NATIVE_MIGRATED_KEY, '1'); } catch (e) {}
        log('migrated and verified ' + snapshots.length + ' records in native incremental SQLite');
        return true;
      });
    }
    /* ── §13.5 / §13.6 boot (async) ── */
    function nativeMarkerSet() { try { return nGet.call(ls, NATIVE_MIGRATED_KEY) === '1'; } catch (e) { return false; } }
    function recoveryDescriptor(code) {
      if (RecoveryPolicy && RecoveryPolicy.descriptor) return RecoveryPolicy.descriptor(code);
      return { code:recoveryCode(code, 'STORE_UNAVAILABLE'), title:'Secure storage needs recovery', body:'SAAGAR could not verify its protected database and has blocked stale fallback data. Retry once, then copy the diagnostics for support. Do not clear app data.' };
    }
    function recoveryStatus() {
      var description = recoveryDescriptor(_recovery.code || 'STORE_UNAVAILABLE');
      return {
        state:_recovery.state,
        code:_recovery.code,
        title:description.title,
        message:description.body,
        stage:_recovery.stage,
        attempt:_recovery.attempt,
        canRetry:_recovery.canRetry,
        canRestore:false,
        nativeMarker:_recovery.nativeMarker,
        pluginPresent:_recovery.pluginPresent,
        schemaVersion:_recovery.schemaVersion,
        expectedRows:_recovery.expectedRows,
        loadedRows:_recovery.loadedRows,
        storage:_recovery.storage
      };
    }
    function buildRecoveryDiagnostics() {
      var appVersion = 'unknown', apkBuild = 'unknown';
      try { if (window.__SAAGAR_BUILD_ID) { appVersion = window.__SAAGAR_BUILD_ID.appVersion || appVersion; apkBuild = window.__SAAGAR_BUILD_ID.apkBuild || apkBuild; } } catch (e) {}
      try { if (typeof APP_VERSION !== 'undefined') appVersion = APP_VERSION; } catch (e) {}
      try { if (typeof APK_BUILD !== 'undefined') apkBuild = APK_BUILD; } catch (e) {}
      var input = Object.assign({}, _recovery, { appVersion:appVersion, apkBuild:apkBuild });
      if (RecoveryPolicy && RecoveryPolicy.diagnostics) return RecoveryPolicy.diagnostics(input);
      return { format:'SAAGAR_STORAGE_RECOVERY', contractVersion:1, createdAt:new Date().toISOString(), appVersion:String(appVersion), apkBuild:String(apkBuild), state:_recovery.state, code:recoveryCode(_recovery.code, 'STORE_UNAVAILABLE'), stage:_recovery.stage, attempt:_recovery.attempt, canRetry:true, canRestore:false, nativeMarker:_recovery.nativeMarker, pluginPresent:_recovery.pluginPresent };
    }
    function copyText(text) {
      try {
        if (window.navigator && window.navigator.clipboard && window.navigator.clipboard.writeText) {
          return Promise.resolve(window.navigator.clipboard.writeText(text)).then(function () { return true; }, function () { return fallback(); });
        }
      } catch (e) {}
      return fallback();
      function fallback() {
        try {
          var field = document.createElement('textarea'); field.value = text;
          field.setAttribute('readonly', ''); field.style.cssText = 'position:fixed;left:-9999px;top:0';
          document.body.appendChild(field); field.select();
          var copied = !!document.execCommand('copy'); document.body.removeChild(field);
          return Promise.resolve(copied);
        } catch (e) { return Promise.resolve(false); }
      }
    }
    function copyRecoveryDiagnostics() { return copyText(JSON.stringify(buildRecoveryDiagnostics(), null, 2)); }
    function retryRecovery() {
      if (!storageUnavailable()) return false;
      _recovery.stage = 'reload'; _recovery.canRetry = false;
      try { if (window.location && typeof window.location.reload === 'function') { window.location.reload(); return true; } } catch (e) {}
      try { if (typeof location !== 'undefined' && typeof location.reload === 'function') { location.reload(); return true; } } catch (e) {}
      _recovery.canRetry = true; return false;
    }
    function blockNativeStore(reason, stage) {
      var code = recoveryCode(reason, 'STORE_UNAVAILABLE');
      _storageBlocked = true; _authorityPending = true; _nativeBooting = false; _lastError = 'native-store-blocked:' + code;
      _recovery.state = 'blocked'; _recovery.code = code; _recovery.stage = stage || _recovery.stage || 'native-status'; _recovery.canRetry = true;
      clearTimeout(_bootTimer); log('BLOCKED: native authoritative store unavailable - refusing stale legacy fallback (' + code + ')');
      function renderBlocked() {
        try {
          if (!document.body || document.getElementById('saagar-storage-blocked')) return;
          var box = document.createElement('section'); box.id = 'saagar-storage-blocked';
          box.style.cssText = 'position:fixed;inset:0;z-index:2147483647;background:#081a33;color:#fff;padding:32px;font:16px/1.5 system-ui;display:flex;align-items:center;justify-content:center;text-align:center';
          var description = recoveryDescriptor(code);
          var message = document.createElement('div'); message.style.cssText = 'width:min(620px,100%);border:1px solid #d4af37;border-radius:14px;padding:24px;background:#10294d;box-shadow:0 18px 50px rgba(0,0,0,.28)';
          var title = document.createElement('h1'); title.style.cssText = 'color:#d4af37;margin:0 0 12px;font-size:28px'; title.textContent = description.title;
          var body = document.createElement('p'); body.style.cssText = 'margin:0 0 14px'; body.textContent = description.body;
          var ref = document.createElement('p'); ref.style.cssText = 'margin:0 0 20px;color:#c9d7ea;font:600 13px/1.4 ui-monospace,monospace'; ref.textContent = 'Recovery code: ' + code;
          var actions = document.createElement('div'); actions.style.cssText = 'display:flex;gap:10px;justify-content:center;flex-wrap:wrap';
          var retry = document.createElement('button'); retry.type = 'button'; retry.textContent = 'Retry storage'; retry.style.cssText = 'border:0;border-radius:9px;padding:11px 16px;background:#d4af37;color:#081a33;font-weight:800';
          var copy = document.createElement('button'); copy.type = 'button'; copy.textContent = 'Copy diagnostics'; copy.style.cssText = 'border:1px solid #91a6c2;border-radius:9px;padding:11px 16px;background:#18375f;color:#fff;font-weight:700';
          var feedback = document.createElement('p'); feedback.setAttribute('role', 'status'); feedback.style.cssText = 'min-height:24px;margin:14px 0 0;color:#c9d7ea;font-size:13px';
          retry.addEventListener('click', function () { retry.disabled = true; feedback.textContent = 'Restarting secure storage check…'; if (!retryRecovery()) { retry.disabled = false; feedback.textContent = 'Please close and reopen SAAGAR manually.'; } });
          copy.addEventListener('click', function () { copy.disabled = true; copyRecoveryDiagnostics().then(function (ok) { feedback.textContent = ok ? 'Diagnostics copied. No business data or PINs were included.' : 'Copy failed. Please take a screenshot showing the recovery code.'; copy.disabled = false; }); });
          actions.appendChild(retry); actions.appendChild(copy);
          message.appendChild(title); message.appendChild(body); message.appendChild(ref); message.appendChild(actions); message.appendChild(feedback); box.appendChild(message); document.body.appendChild(box);
        } catch (e) {}
      }
      if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', renderBlocked);
      else renderBlocked();
    }
    function setReady() { if (_ready) return; _ready = true; clearTimeout(_bootTimer); var cbs = _whenReadyCbs.slice(); _whenReadyCbs.length = 0; cbs.forEach(function (cb) { try { cb(); } catch (e) {} }); }
    function bootTimeoutFallback() {
      if (_ready) return;
      if (_nativeBooting && _authorityPending) { blockNativeStore('STORE_TIMEOUT', _recovery.stage || 'native-status'); return; }
      if (_nativeBooting) {
        _nativeBooting = false; _activeBootAttempt = ++_bootAttempt;
        log('native SQLite probe timed out - legacy persistence remains authoritative'); bootLegacy(null); return;
      }
      log('boot timeout - native-LS fallback (MEM already hydrated at Step 0)'); setReady();
    }
    function bootLegacy(migrationPlugin) {
      /* Android 6's factory WebView (Chrome 44) has no WebAssembly. Calling the
         wasm sql.js loader there both rejects and leaves an uncaught internal
         promise behind. A Capacitor build takes the native-first path in boot()
         below; browser-only previews retain native localStorage without ever
         invoking the unsupported loader. */
      if (typeof WebAssembly !== 'object') { _nativeBooting = false; log('WebAssembly absent - native-LS fallback'); setReady(); return Promise.resolve(false); }
      if (typeof initSqlJs !== 'function') { _nativeBooting = false; log('sql.js absent - native-LS fallback'); setReady(); return Promise.resolve(false); }
      return initSqlJs({ locateFile: function (f) { return f; } }).then(function (_SQL) {
        if (_ready) return;
        SQL = _SQL; var FS = FSplugin();
        function rd(path) { return FS ? FS.readFile({ path: path, directory: dataDir() }).then(function (r) { return r && r.data ? decryptIfEnveloped(b64ToBytes(r.data), path) : null; }).catch(function () { return null; }) : Promise.resolve(null); }   /* R0-W2 E4: sniff+decrypt per file; plaintext passes through untouched (zero async cost added — decryptIfEnveloped resolves synchronously-shaped for non-envelopes) */
        /* open + VALIDATE: sql.js does not throw on a corrupt blob at construction — only on first
           access. Probe with PRAGMA so corruption is detected at open time and recovery fires. */
        /* §13.2: PRAGMA quick_check is a FULL page-structure scan (not just page-1 schema_version),
           so a file torn within its first 4 KB is rejected here and the chain falls through to
           .tmp/.bak/fresh instead of being adopted and throwing later (outside this recovery chain). */
        function open(b) { var d = null; try { d = b ? new SQL.Database(b) : new SQL.Database(); var ic = d.exec('PRAGMA quick_check'); if (!(ic && ic[0] && ic[0].values && String(ic[0].values[0][0]).toLowerCase() === 'ok')) { try { d.close(); } catch (_) {} return false; } db = d; return true; } catch (e) { try { if (d) d.close(); } catch (_) {} return false; } }
        /* recovery chain: live → .tmp (an interrupted rename leaves a valid newer .tmp) → .bak → fresh */
        return rd(DB_FILE).then(function (bytes) {
          if (bytes && open(bytes)) { _dbFromFile = true; return; }
          return rd(DB_FILE + '.tmp').then(function (t) {
            if (t && open(t)) { _dbFromFile = true; log('recovered from .tmp (interrupted rename)'); return; }
            return rd(DB_FILE + '.bak').then(function (bak) {
              if (bak && open(bak)) { _dbFromFile = true; log('recovered from .bak'); return; }
              db = new SQL.Database(); _dbFromFile = false; log('no valid DB file — fresh DB');
            });
          });
        }).then(function () {
          /* §13.6 LATE-HEAL: if the boot-timeout fallback already fired (slow device), do NOT abandon the
             freshly-loaded DB. Replay the WAL + reconcile so MEM converges from the stale native-LS fallback
             to the REAL DB data (DB-WINS; any write made in the brief stale window is preserved by the WAL
             replay above-via-reconcile). setReady() is idempotent. This stops a slow boot from running the
             whole session on stale data + diverging from the loaded DB (the prior code did `if(_ready)return`,
             which abandoned the load and left MEM permanently stale). */
          var lateHeal = _ready;
          db.run('CREATE TABLE IF NOT EXISTS kv (k TEXT PRIMARY KEY, v TEXT)');
          replayWAL();
          var rc = reconcile();
          _keysCache = null;                 /* MEM rebuilt by replayWAL + reconcile — drop any cached key list */
          if (lateHeal) log('late DB load after boot-timeout fallback — MEM healed from DB (was on native-LS fallback)');
          if (migrationPlugin && !lateHeal) {
            return migrateToNative(migrationPlugin).then(function () { setReady(); return true; }, function (e) {
              _nativeBooting = false; _lastError = (e && e.message) || 'native-migration';
              log('native migration failed (' + _lastError + ') - legacy persistence retained');
              setReady(); _dirty = true; return flush();
            });
          }
          _nativeBooting = false;
          setReady();
          if (FS) {
            /* R0-W2 E5 — seal the .bak on the FIRST encrypted boot (the boot where the on-disk DB is still
               PLAINTEXT). The first ciphertext persist promotes the OLD plaintext live → .bak (a whole-DB
               plaintext copy) and writes ciphertext live; a SECOND persist promotes the now-ciphertext live →
               .bak so both files are ciphertext within seconds. Two things are required: (a) force THIS boot's
               flush to BE the first ciphertext persist — on a clean 2nd+ boot reconcile()/replayWAL() leave
               nothing dirty, so without _dirty the boot flush is a no-op and the single reseal below would
               itself become the first (plain→cipher) persist, leaving .bak plaintext; (b) fire ONE more persist
               after it succeeds. Gated on STORAGE_ENCRYPT_ENABLED (flag-off ⇒ zero extra persist, on-disk bytes
               identical to S2a) and on the DB NOT already being ciphertext at boot (_encState=='encrypted-keystore'
               is set by the reader when it decrypts an SBCC1 live) so 2nd+ encrypted boots skip the rewrite. */
            var _firstEncBoot = STORAGE_ENCRYPT_ENABLED && !_encBakDone && _encState !== 'encrypted-keystore';
            if (_firstEncBoot) _dirty = true;   /* make the boot flush the first ciphertext persist, not a no-op */
            flush().then(function (ok) {
              /* set the one-way marker ONLY after a verified first-boot migration is durably persisted */
              if (ok && rc && rc.firstBoot && rc.verified) { try { nSet.call(ls, MIGRATED_KEY, '1'); } catch (e) {} }
              /* E5 reseal: only after that first ciphertext persist SUCCEEDED (ok ⇒ ciphertext live + bcc.dek durable) */
              if (ok && _firstEncBoot && !_encBakDone) { _encBakDone = true; _dirty = true; flush(); }
            });
          }
        });
      }).catch(function (e) { _nativeBooting = false; _lastError = (e && e.message) || 'init'; log('sql.js init failed: ' + _lastError + ' - fallback'); setReady(); return false; });
    }
    function boot() {
      var attempt = ++_bootAttempt; _activeBootAttempt = attempt;
      armBootTimer(BOOT_TIMEOUT_MS);
      var plugin = nativeStorePlugin(), wasNative = nativeMarkerSet();
      _recovery.attempt = attempt; _recovery.nativeMarker = wasNative; _recovery.pluginPresent = !!plugin; _recovery.stage = 'native-status';
      if (!plugin) { if (wasNative) blockNativeStore('PLUGIN_MISSING', 'native-status'); else bootLegacy(null); return; }
      _nativeBooting = true;
      Promise.resolve().then(function () { return plugin.status({}); }).then(function (status) {
        if (attempt !== _activeBootAttempt || _storageBlocked) return false;
        _nativeStatusSafe = safeNativeStatus(status);
        _recovery.schemaVersion = _nativeStatusSafe.schemaVersion;
        _recovery.storage = _nativeStatusSafe.storage;
        var nativeAuthoritative = wasNative || (status && status.migrated === true);
        if (nativeAuthoritative) { _authorityPending = true; _recovery.state = 'pending'; }
        var inspected = inspectNativeStatus(status, nativeAuthoritative);
        if (inspected.ok && status.migrated === true) {
          _recovery.expectedRows = inspected.rows;
          armBootTimer(nativeReadTimeoutMs(inspected.rows));
          return loadNativeRows(plugin, status).catch(function (e) {
            if (attempt !== _activeBootAttempt || _storageBlocked) return false;
            _nativeMode = false;
            if (nativeAuthoritative) { blockNativeStore(e, _recovery.stage || 'native-read'); return false; }
            log('native load failed before first migration - legacy persistence retained');
            return bootLegacy(plugin);
          });
        }
        if (wasNative) { blockNativeStore(inspected.code, 'native-status'); return false; }
        /* Fresh API-23 installs must not depend on sql-wasm: migrate the Step-0
           localStorage snapshot directly into the encrypted native incremental
           store. This also makes the demo seed guard durable on its first write,
           so a restart cannot reseed the two-year dataset. */
        if (typeof WebAssembly !== 'object') {
          _recovery.stage = 'native-migration';
          return migrateToNative(plugin).then(function () {
            if (attempt !== _activeBootAttempt || _storageBlocked) return false;
            _authorityPending = false; _storageBlocked = false;
            _recovery.state = 'ready'; _recovery.code = ''; _recovery.stage = 'ready';
            setReady();
            log('native-first migration active without WebAssembly');
            return true;
          }, function (e) {
            if (attempt !== _activeBootAttempt) return false;
            blockNativeStore(recoveryCode(e, 'STORE_UNAVAILABLE'), 'native-migration');
            return false;
          });
        }
        return bootLegacy(plugin);
      }, function (e) {
        if (attempt !== _activeBootAttempt || _storageBlocked) return false;
        if (wasNative) { blockNativeStore(recoveryCode(e, 'STORE_UNAVAILABLE'), 'native-status'); return false; }
        _nativeBooting = false; log('native store unavailable before first migration - legacy persistence active'); return bootLegacy(null);
      });
    }
    if (document.readyState === 'complete' || document.readyState === 'interactive') boot();
    else document.addEventListener('DOMContentLoaded', boot);

    /* ── background flush (§6). pagehide just flushes (WAL is the synchronous durability layer;
        the old multi-MB 'saagar_emergency_export' dump was never read back — removed). ── */
    document.addEventListener('visibilitychange', function () { if (document.visibilityState === 'hidden' && (_dirty || dirtyKeys.size)) flush(); });
    window.addEventListener('pagehide', function () { if (_dirty || dirtyKeys.size) flush(); });
    try { if (window.Capacitor && window.Capacitor.Plugins && window.Capacitor.Plugins.App && window.Capacitor.Plugins.App.addListener) window.Capacitor.Plugins.App.addListener('pause', function () { if (_dirty || dirtyKeys.size) flush(); }); } catch (e) {}

    /* ── §13.5 full Factory-Reset wipe — atomic + awaited (called by index.html factoryReset) ── */
    function resetAll() {
      _storageInfoAttempt++;   /* invalidate any pre-reset device/database measurement */
      _resetting = true; clearTimeout(_saveTimer); clearTimeout(_bootTimer);
      try { MEM.clear(); _keysCache = null; } catch (e) {}
      try { if (db) db.run('DELETE FROM kv'); } catch (e) {}
      _dirty = false; dirtyKeys.clear(); nativeDirty.clear(); _nativeClearSeq = 0; _nativeReplaceSeq = 0;
      try { nClear.call(ls); } catch (e) {}                                  /* native LS (incl. WAL + marker) */
      try { nSet.call(ls, 'saagar_demo_seeded', 'cleared'); } catch (e) {}   /* survive the reload so the seeder does NOT repopulate */
      var FS = FSplugin(), ps = [];
      if (FS) ['', '.tmp', '.bak', '.bak.tmp'].forEach(function (s) { try { ps.push(FS.deleteFile({ path: DB_FILE + s, directory: dataDir() }).catch(function () {})); } catch (e) {} });
      if (FS) ['', '.tmp'].forEach(function (s) { try { ps.push(FS.deleteFile({ path: DEK_FILE + s, directory: dataDir() }).catch(function () {})); } catch (e) {} });   /* R0-W2 E6: wipe the wrapped DEK with the data (Keystore KEK alias is left; a fresh DEK re-wraps under it, or a new KEK mints — either way the old ciphertext is gone) */
      _dekReadP = null; _dekWriteP = null;
      try { var nativePlugin = nativeStorePlugin(); if (nativePlugin && nativePlugin.reset) ps.push(nativePlugin.reset({}).catch(function () {})); } catch (e) {}
      try { if (window.SaagarStore && window.SaagarStore.photo && window.SaagarStore.photo.clearAll) ps.push(Promise.resolve(window.SaagarStore.photo.clearAll()).catch(function () {})); } catch (e) {}
      return Promise.all(ps);
    }

    /* ── §3.8 window.SaagarStore — the surface the iframe shim + shell call ── */
    window.SaagarStore = {
      enabled: true,
      phase: 2,
      mode: 'mem-source',
      get: function (k) { if (storageUnavailable()) return null; return _ready ? (MEM.has(String(k)) ? MEM.get(String(k)) : null) : nGet.call(ls, k); },
      set: function (k, v) { return SP.setItem.call(ls, k, v); },
      remove: function (k) { return SP.removeItem.call(ls, k); },
      keys: function () { if (storageUnavailable()) return []; if (_ready) return memKeys().slice(); var a = [], n = nLen.call(ls); for (var i = 0; i < n; i++) a.push(nKey.call(ls, i)); return a; },
      length: function () { if (storageUnavailable()) return 0; return _ready ? MEM.size : nLen.call(ls); },
      ready: function () { return _ready; },
      whenReady: function (cb) { if (typeof cb !== 'function') return; if (_ready) { try { cb(); } catch (e) {} } else _whenReadyCbs.push(cb); },
      flush: function () { if (storageUnavailable()) return Promise.reject(blockedStorageError()); _dirty = true; return flush(); },
      recoveryStatus: recoveryStatus,
      recoveryDiagnostics: buildRecoveryDiagnostics,
      copyRecoveryDiagnostics: copyRecoveryDiagnostics,
      retryRecovery: retryRecovery,
      runPersistenceAcceptance: runPersistenceAcceptance,
      persistenceAcceptance: readPersistenceAcceptance,
      refreshStorageInfo: refreshStorageInfo,
      /* ── R0-W3-S3 primitive: whole-blob seal/unseal over the SAME DEK/SBCC1 envelope as the DB.
         INERT until R0-W3-S3 wires callers (auto-backup seal-on-write + restore sniff). Flag-gated via
         encryptForPersist: on a flag-off build seal returns PLAINTEXT bytes (correct — R0-W3-S3 seal-on-write
         is behind the same STORAGE_ENCRYPT_ENABLED gate). unseal's reader is ALWAYS active (content-sniff):
         SBCC1 → decrypt; plaintext/legacy → pass through; key-miss/tag-fail → null. */
      seal: function (u8) { return encryptForPersist(u8).then(function (b64) { return b64ToBytes(b64); }); },
      unseal: function (u8) { return decryptIfEnveloped(u8, 'seal'); },
      /* §bulk — run a burst of writes (first-boot seed / large restore) WITHOUT per-write WAL journaling
         + DB export (that per-write cost froze the app for ~60s+ on a phone). Suspends them, runs fn()
         (MEM + in-memory DB only), then does ONE durable persist. try/finally guarantees _bulk is cleared
         even if fn throws — so normal single writes can NEVER be silently left without durability. Seed +
         restore are re-runnable (re-seed / re-import), so skipping per-write WAL inside the burst is safe. */
      bulk: function (fn) {
        if (typeof fn !== 'function') return Promise.resolve(false);
        if (storageUnavailable()) return Promise.reject(blockedStorageError());
        if (!_ready) { try { fn(); } catch (e) {} return (db ? flush() : Promise.resolve(false)); }
        _bulk = true; var bulkError = null;
        try { fn(); } catch (e) { bulkError = e; } finally { _bulk = false; }
        if (bulkError) { if (_nativeMode) { nativeDirty.clear(); _dirty = walRead().length > 0; } throw bulkError; }
        if (_nativeMode) _nativeReplaceSeq = _seq || ++_seq;
        _dirty = true; return flush();
      },
      /* §bulkAsync — like bulk() but holds the no-flush window across an ASYNC fn (one that yields to the UI
         between chunks). Used by the 1-year demo seed: it generates ~3,300 keys progressively, and WITHOUT
         this each chunk's debounced save would re-export + rewrite the whole growing DB (~50+ multi-MB disk
         writes + a multi-second synchronous serialize → phone ANR/force-close). This suspends all of that and
         does exactly ONE durable persist at the end. _bulk is always cleared (success or throw). */
      bulkAsync: function (fn) {
        if (typeof fn !== 'function') return Promise.resolve(false);
        if (storageUnavailable()) return Promise.reject(blockedStorageError());
        if (!_ready) { return Promise.resolve().then(fn).then(function () { return db ? flush() : false; }); }
        _bulk = true;
        return Promise.resolve().then(fn).then(
          function () { _bulk = false; if (_nativeMode) _nativeReplaceSeq = _seq || ++_seq; _dirty = true; return flush(); },
          function (e) { _bulk = false; if (_nativeMode) { nativeDirty.clear(); _dirty = walRead().length > 0; return Promise.reject(e); } _dirty = true; return flush().then(function () { throw e; }); }
        );
      },
      _reset: function () { return storageUnavailable() ? Promise.reject(blockedStorageError()) : resetAll(); },                            /* §13.5 awaited full wipe */
      /* ── diagnostics ── */
      _phase: 2,
      _mem: function () { return MEM; },
      _status: function () { return { ready: _ready, dirty: _dirty, dirtyKeys: _nativeMode ? nativeDirty.size : dirtyKeys.size, lastSavedAt: _lastSavedAt, lastError: _lastError, hasFS: _nativeMode ? !!nativeStorePlugin() : !!FSplugin(), migrated: _nativeMode || !!(function () { try { return nGet.call(ls, MIGRATED_KEY); } catch (e) { return 0; } })(), dbFromFile: _dbFromFile, rows: storageUnavailable() ? 0 : (_nativeMode ? MEM.size : (db ? Object.keys(kvAll()).length : 0)), encState: _encState, storageBlocked: _storageBlocked, authorityPending: _authorityPending, persistenceMode: _storageBlocked ? 'blocked' : (_authorityPending ? 'authority-pending' : (_nativeMode ? 'native-incremental' : 'legacy-snapshot')), nativeRows: _nativeRows, recovery: recoveryStatus(), nativeStatus: _nativeStatusSafe }; },
      _walLen: function () { return walRead().length; },
      _performance: function () { return _persistPerf.slice(); },
      _coherent: function () { try { if (_ready) return true; var n = nLen.call(ls); var c = 0; for (var i = 0; i < n; i++) { var k = nKey.call(ls, i); if (INTERNAL[k]) continue; if (MEM.get(k) !== nGet.call(ls, k)) return false; c++; } return MEM.size === c; } catch (e) { return false; } }
    };

    /* §7.2 — adopt the photo facade stashed by photo-store.js (loaded after us). */
    try { if (window.__SaagarPhoto && !window.SaagarStore.photo) window.SaagarStore.photo = window.__SaagarPhoto; } catch (e) {}

    /* §1 — window.SaagarDB delegating facade (sqlite-store.js stands down when we are enabled). */
    window.SaagarDB = {
      ready: function () { return _ready; },
      status: function () { var s = window.SaagarStore._status(); return { ready: s.ready, rows: s.rows, dirty: s.dirty, lastSavedAt: s.lastSavedAt, lastError: s.lastError, hasFS: s.hasFS }; },
      save: function () { if (storageUnavailable()) return Promise.reject(blockedStorageError()); _dirty = true; return flush(); },
      allKeys: function () { return (!storageUnavailable() && _ready) ? memKeys().filter(function (k) { return !INTERNAL[k]; }) : []; },
      query: function (sql, params) { if (storageUnavailable() || !_ready || !db) return null; try { return db.exec(sql, params || []); } catch (e) { _lastError = e.message; return null; } },
      pruneKeys: function (keys) { if (!_ready || !keys || !keys.length) return 0; var n = 0; keys.forEach(function (k) { try { SP.removeItem.call(ls, String(k)); n++; } catch (e) {} }); return n; },
      raw: function () { return storageUnavailable() ? null : db; }
    };

    try { console.log('[storage-core] phase 2-3 active — MEM source-of-truth (async sql.js); SaagarStore + SaagarDB ready'); } catch (e) {}
  } catch (e) {
    try { console.log('[storage-core] init failed (' + (e && e.message) + ') — staying on native localStorage'); } catch (_) {}
  }
})();
