/* Saagar Traders portable backup container.
   The payload is encrypted with a user-held recovery passphrase. The clear-text
   manifest contains only control totals and hashes, and is authenticated as
   AES-GCM additional data so it cannot be altered independently. */
(function (root, factory) {
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  if (root) root.SaagarPortableBackup = api;
})(typeof window !== 'undefined' ? window : globalThis, function () {
  'use strict';

  var FORMAT = 'saagar-portable-backup';
  var LEGACY_VERSION = 1;
  var VERSION = 2;
  var DEFAULT_ITERATIONS = 310000;
  var MIN_ITERATIONS = 100000;
  var MAX_ITERATIONS = 1000000;
  var encoder = new TextEncoder();
  var decoder = new TextDecoder('utf-8', { fatal: true });

  function fail(code, message) {
    var err = new Error(message);
    err.code = code;
    throw err;
  }

  function cryptoApi() {
    var c = (typeof globalThis !== 'undefined' && globalThis.crypto) || null;
    if (!c || !c.subtle || typeof c.getRandomValues !== 'function') {
      fail('CRYPTO_UNAVAILABLE', 'Secure backup encryption is unavailable on this device.');
    }
    return c;
  }

  function canonical(value) {
    if (value === null || typeof value !== 'object') return JSON.stringify(value);
    if (Array.isArray(value)) return '[' + value.map(canonical).join(',') + ']';
    return '{' + Object.keys(value).sort().map(function (key) {
      return JSON.stringify(key) + ':' + canonical(value[key]);
    }).join(',') + '}';
  }

  function bytesToBase64(bytes) {
    var binary = '';
    var chunk = 0x8000;
    for (var i = 0; i < bytes.length; i += chunk) {
      binary += String.fromCharCode.apply(null, bytes.subarray(i, Math.min(i + chunk, bytes.length)));
    }
    if (typeof btoa === 'function') return btoa(binary);
    if (typeof Buffer !== 'undefined') return Buffer.from(bytes).toString('base64');
    fail('BASE64_UNAVAILABLE', 'Backup encoding is unavailable.');
  }

  function base64ToBytes(text, label) {
    try {
      var clean = String(text || '');
      if (!clean || !/^[A-Za-z0-9+/]+={0,2}$/.test(clean) || clean.length % 4 !== 0) throw new Error('bad base64');
      var binary;
      if (typeof atob === 'function') binary = atob(clean);
      else if (typeof Buffer !== 'undefined') binary = Buffer.from(clean, 'base64').toString('binary');
      else fail('BASE64_UNAVAILABLE', 'Backup decoding is unavailable.');
      var out = new Uint8Array(binary.length);
      for (var i = 0; i < binary.length; i++) out[i] = binary.charCodeAt(i);
      return out;
    } catch (err) {
      fail('INVALID_CONTAINER', 'The backup ' + label + ' is invalid.');
    }
  }

  function toHex(bytes) {
    return Array.prototype.map.call(bytes, function (b) { return b.toString(16).padStart(2, '0'); }).join('');
  }

  function sha256Bytes(bytes) {
    return cryptoApi().subtle.digest('SHA-256', bytes).then(function (digest) {
      return toHex(new Uint8Array(digest));
    });
  }

  function sectionCount(name, value) {
    if (name === 'localStorage' || name === 'photos') {
      return value && typeof value === 'object' && !Array.isArray(value) ? Object.keys(value).length : 0;
    }
    return Array.isArray(value) ? value.length : 0;
  }

  function buildManifest(payload, plainBytes) {
    var names = ['localStorage', 'photos', 'qmsArchive', 'evidence'];
    return Promise.all(names.map(function (name) {
      var value = payload && Object.prototype.hasOwnProperty.call(payload, name)
        ? payload[name]
        : (name === 'localStorage' || name === 'photos' ? {} : []);
      return sha256Bytes(encoder.encode(canonical(value))).then(function (digest) {
        return [name, { count: sectionCount(name, value), sha256: digest }];
      });
    })).then(function (rows) {
      var sections = {};
      rows.forEach(function (row) { sections[row[0]] = row[1]; });
      return sha256Bytes(plainBytes).then(function (digest) {
        return {
          app: 'Saagar Traders Business Control Centre',
          appVersion: String(payload && payload.version || ''),
          apkBuild: String(payload && payload.apkBuild || ''),
          createdAt: String(payload && payload.createdAt || new Date().toISOString()),
          scope: String(payload && payload.scope || 'full-portable-backup'),
          sections: sections,
          plaintextBytes: plainBytes.length,
          plaintextSha256: digest
        };
      });
    });
  }

  function deriveKey(passphrase, salt, iterations, usage) {
    if (typeof passphrase !== 'string' || passphrase.length < 12) {
      fail('WEAK_PASSPHRASE', 'Use a recovery passphrase with at least 12 characters.');
    }
    return cryptoApi().subtle.importKey(
      'raw', encoder.encode(passphrase), 'PBKDF2', false, ['deriveKey']
    ).then(function (baseKey) {
      return cryptoApi().subtle.deriveKey(
        { name: 'PBKDF2', hash: 'SHA-256', salt: salt, iterations: iterations },
        baseKey,
        { name: 'AES-GCM', length: 256 },
        false,
        usage
      );
    });
  }

  function importAesKey(bytes, usage) {
    if (!(bytes instanceof Uint8Array) || bytes.length !== 32) {
      return Promise.reject(Object.assign(new Error('The portable backup key is invalid.'), { code: 'INVALID_BACKUP_KEY' }));
    }
    return cryptoApi().subtle.importKey('raw', bytes, { name: 'AES-GCM' }, false, usage);
  }

  function recoveryAad() {
    return encoder.encode(canonical({ format: FORMAT, version: VERSION, purpose: 'portable-backup-recovery-key' }));
  }

  function validateIterations(value) {
    var iterations = Number(value || DEFAULT_ITERATIONS);
    if (!Number.isInteger(iterations) || iterations < MIN_ITERATIONS || iterations > MAX_ITERATIONS) fail('INVALID_KDF', 'Backup key-strength setting is invalid.');
    return iterations;
  }

  function validateRecoveryProfile(value) {
    var recovery = value || {};
    var kdf = recovery.kdf || {}, cipher = recovery.cipher || {};
    if (kdf.name !== 'PBKDF2' || kdf.hash !== 'SHA-256' ||
        !Number.isInteger(kdf.iterations) || kdf.iterations < MIN_ITERATIONS || kdf.iterations > MAX_ITERATIONS ||
        cipher.name !== 'AES-GCM' || cipher.keyBits !== 256 || cipher.tagBits !== 128 || typeof recovery.wrappedKey !== 'string') {
      fail('INVALID_CONTAINER', 'The backup recovery settings are invalid.');
    }
    var salt = base64ToBytes(kdf.salt, 'recovery salt');
    var iv = base64ToBytes(cipher.iv, 'recovery IV');
    var wrappedKey = base64ToBytes(recovery.wrappedKey, 'recovery key');
    if (salt.length !== 16 || iv.length !== 12 || wrappedKey.length < 33) fail('INVALID_CONTAINER', 'The backup recovery data is invalid.');
    return { recovery: recovery, salt: salt, iv: iv, wrappedKey: wrappedKey };
  }

  function createRecoveryProfile(passphrase, options) {
    var iterations;
    try { iterations = validateIterations(options && options.iterations); } catch (err) { return Promise.reject(err); }
    var c;
    try { c = cryptoApi(); } catch (err) { return Promise.reject(err); }
    var portableKey = c.getRandomValues(new Uint8Array(32));
    var salt = c.getRandomValues(new Uint8Array(16));
    var iv = c.getRandomValues(new Uint8Array(12));
    return deriveKey(passphrase, salt, iterations, ['encrypt']).then(function (key) {
      return c.subtle.encrypt({ name: 'AES-GCM', iv: iv, additionalData: recoveryAad(), tagLength: 128 }, key, portableKey);
    }).then(function (wrapped) {
      return {
        keyBase64: bytesToBase64(portableKey),
        profile: {
          kdf: { name: 'PBKDF2', hash: 'SHA-256', iterations: iterations, salt: bytesToBase64(salt) },
          cipher: { name: 'AES-GCM', keyBits: 256, tagBits: 128, iv: bytesToBase64(iv) },
          wrappedKey: bytesToBase64(new Uint8Array(wrapped))
        }
      };
    });
  }

  function seal(payload, passphrase, options) {
    if (!payload || typeof payload !== 'object' || Array.isArray(payload)) {
      return Promise.reject(Object.assign(new Error('Backup payload must be an object.'), { code: 'INVALID_PAYLOAD' }));
    }
    var iterations;
    try { iterations = validateIterations(options && options.iterations); }
    catch (err) { return Promise.reject(err); }
    var c;
    try { c = cryptoApi(); } catch (err) { return Promise.reject(err); }
    var plainBytes = encoder.encode(JSON.stringify(payload));
    var salt = c.getRandomValues(new Uint8Array(16));
    var iv = c.getRandomValues(new Uint8Array(12));
    return buildManifest(payload, plainBytes).then(function (manifest) {
      var aad = encoder.encode(canonical(manifest));
      return deriveKey(passphrase, salt, iterations, ['encrypt']).then(function (key) {
        return c.subtle.encrypt({ name: 'AES-GCM', iv: iv, additionalData: aad, tagLength: 128 }, key, plainBytes);
      }).then(function (ciphertext) {
        return {
          format: FORMAT,
          version: LEGACY_VERSION,
          kdf: {
            name: 'PBKDF2',
            hash: 'SHA-256',
            iterations: iterations,
            salt: bytesToBase64(salt)
          },
          cipher: {
            name: 'AES-GCM',
            keyBits: 256,
            tagBits: 128,
            iv: bytesToBase64(iv)
          },
          manifest: manifest,
          ciphertext: bytesToBase64(new Uint8Array(ciphertext))
        };
      });
    });
  }

  function sealWithKey(payload, keyBase64, recoveryProfile) {
    if (!payload || typeof payload !== 'object' || Array.isArray(payload)) {
      return Promise.reject(Object.assign(new Error('Backup payload must be an object.'), { code: 'INVALID_PAYLOAD' }));
    }
    var c, keyBytes, recovery;
    try {
      c = cryptoApi();
      keyBytes = base64ToBytes(keyBase64, 'key');
      recovery = validateRecoveryProfile(recoveryProfile).recovery;
    } catch (err) { return Promise.reject(err); }
    var plainBytes = encoder.encode(JSON.stringify(payload));
    var iv = c.getRandomValues(new Uint8Array(12));
    return buildManifest(payload, plainBytes).then(function (manifest) {
      var aad = encoder.encode(canonical({ manifest: manifest, recovery: recovery }));
      return importAesKey(keyBytes, ['encrypt']).then(function (key) {
        return c.subtle.encrypt({ name: 'AES-GCM', iv: iv, additionalData: aad, tagLength: 128 }, key, plainBytes);
      }).then(function (ciphertext) {
        return {
          format: FORMAT,
          version: VERSION,
          keyMode: 'wrapped-random-key',
          recovery: recovery,
          cipher: { name: 'AES-GCM', keyBits: 256, tagBits: 128, iv: bytesToBase64(iv) },
          manifest: manifest,
          ciphertext: bytesToBase64(new Uint8Array(ciphertext))
        };
      });
    });
  }

  function parseContainer(input) {
    var value = input;
    if (typeof input === 'string') {
      try { value = JSON.parse(input); } catch (err) { fail('INVALID_CONTAINER', 'This is not a valid Saagar backup file.'); }
    }
    if (!value || typeof value !== 'object' || Array.isArray(value)) fail('INVALID_CONTAINER', 'This is not a valid Saagar backup file.');
    if (value.format !== FORMAT || (value.version !== LEGACY_VERSION && value.version !== VERSION)) fail('UNSUPPORTED_CONTAINER', 'This backup format is not supported by this app version.');
    var kdf = value.kdf || {}, cipher = value.cipher || {};
    if (cipher.name !== 'AES-GCM' || cipher.keyBits !== 256 || cipher.tagBits !== 128) fail('INVALID_CONTAINER', 'The backup encryption settings are invalid.');
    if (value.version === LEGACY_VERSION && (kdf.name !== 'PBKDF2' || kdf.hash !== 'SHA-256' ||
        !Number.isInteger(kdf.iterations) || kdf.iterations < MIN_ITERATIONS || kdf.iterations > MAX_ITERATIONS)) {
      fail('INVALID_CONTAINER', 'The backup encryption settings are invalid.');
    }
    if (value.version === VERSION) {
      if (value.keyMode !== 'wrapped-random-key') fail('INVALID_CONTAINER', 'The backup key mode is invalid.');
      validateRecoveryProfile(value.recovery);
    }
    if (!value.manifest || typeof value.manifest !== 'object' || Array.isArray(value.manifest)) {
      fail('INVALID_CONTAINER', 'The backup manifest is missing.');
    }
    return value;
  }

  function timingSafeEqualText(a, b) {
    a = String(a || ''); b = String(b || '');
    var diff = a.length ^ b.length;
    var len = Math.max(a.length, b.length);
    for (var i = 0; i < len; i++) diff |= (a.charCodeAt(i % (a.length || 1)) || 0) ^ (b.charCodeAt(i % (b.length || 1)) || 0);
    return diff === 0;
  }

  function verifyManifest(payload, plainBytes, manifest) {
    return buildManifest(payload, plainBytes).then(function (actual) {
      if (!timingSafeEqualText(canonical(actual), canonical(manifest))) {
        fail('MANIFEST_MISMATCH', 'Backup control totals do not match. The file may be damaged or incomplete.');
      }
      return true;
    });
  }

  function open(input, passphrase) {
    var container;
    try { container = parseContainer(input); } catch (err) { return Promise.reject(err); }
    var salt, iv, ciphertext, recoveryParts;
    try {
      iv = base64ToBytes(container.cipher.iv, 'IV');
      ciphertext = base64ToBytes(container.ciphertext, 'ciphertext');
      if (container.version === LEGACY_VERSION) salt = base64ToBytes(container.kdf.salt, 'salt');
      else recoveryParts = validateRecoveryProfile(container.recovery);
      if ((salt && salt.length !== 16) || iv.length !== 12 || ciphertext.length < 17) fail('INVALID_CONTAINER', 'The backup encryption data is invalid.');
    } catch (err) { return Promise.reject(err); }
    var aad = encoder.encode(canonical(container.version === LEGACY_VERSION ? container.manifest : { manifest: container.manifest, recovery: container.recovery }));
    var dataKeyPromise;
    if (container.version === LEGACY_VERSION) {
      dataKeyPromise = deriveKey(passphrase, salt, container.kdf.iterations, ['decrypt']);
    } else {
      dataKeyPromise = deriveKey(passphrase, recoveryParts.salt, container.recovery.kdf.iterations, ['decrypt'])
        .then(function (recoveryKey) {
          return cryptoApi().subtle.decrypt({ name: 'AES-GCM', iv: recoveryParts.iv, additionalData: recoveryAad(), tagLength: 128 }, recoveryKey, recoveryParts.wrappedKey);
        }).then(function (plainKey) { return importAesKey(new Uint8Array(plainKey), ['decrypt']); });
    }
    return dataKeyPromise.then(function (key) {
      return cryptoApi().subtle.decrypt({ name: 'AES-GCM', iv: iv, additionalData: aad, tagLength: 128 }, key, ciphertext);
    }).catch(function (err) {
      if (err && err.code) throw err;
      fail('DECRYPT_FAILED', 'The recovery passphrase is wrong, or the backup file was changed.');
    }).then(function (plain) {
      var bytes = new Uint8Array(plain);
      var payload;
      try { payload = JSON.parse(decoder.decode(bytes)); }
      catch (err) { fail('INVALID_PAYLOAD', 'The decrypted backup data is invalid.'); }
      if (!payload || typeof payload !== 'object' || Array.isArray(payload)) fail('INVALID_PAYLOAD', 'The decrypted backup data is invalid.');
      return verifyManifest(payload, bytes, container.manifest).then(function () { return { payload: payload, manifest: container.manifest }; });
    });
  }
  function isContainer(value) {
    try {
      var parsed = typeof value === 'string' ? JSON.parse(value) : value;
      return !!(parsed && parsed.format === FORMAT);
    } catch (err) { return false; }
  }

  return {
    FORMAT: FORMAT,
    VERSION: VERSION,
    LEGACY_VERSION: LEGACY_VERSION,
    DEFAULT_ITERATIONS: DEFAULT_ITERATIONS,
    canonical: canonical,
    isContainer: isContainer,
    createRecoveryProfile: createRecoveryProfile,
    seal: seal,
    sealWithKey: sealWithKey,
    open: open
  };
});
