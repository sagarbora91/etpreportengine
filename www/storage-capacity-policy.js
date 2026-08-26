/* Pure display policy for the Settings device-storage meter.
   The device bar deliberately uses availableBytes (space Android reports as
   available to this app), never freeBytes (which can include reserved blocks).
   The SAAGAR figure deliberately uses nativeStoreBytes only: it is the native
   SQLite database plus its journal sidecars, not total app storage. */
(function (root, factory) {
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  if (root) root.SaagarStorageCapacityPolicy = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';

  var CAPACITY_CONTRACT_VERSION = 1;
  var MAX_SAFE_BYTES = Number.MAX_SAFE_INTEGER || 9007199254740991;
  var UNIT_BYTES = 1024;
  var UNITS = ['B', 'KB', 'MB', 'GB', 'TB'];

  function finiteBytes(value) {
    var type = typeof value;
    if (value === null || value === undefined || (type !== 'number' && type !== 'string')) {
      return null;
    }
    if (type === 'string' && value.trim() === '') return null;
    var number = Number(value);
    if (!Number.isFinite(number)) return null;
    if (number <= 0) return 0;
    return Math.min(MAX_SAFE_BYTES, Math.floor(number));
  }

  function clamp(value, minimum, maximum) {
    return Math.min(maximum, Math.max(minimum, value));
  }

  function sanitizeStorage(value) {
    value = value && typeof value === 'object' ? value : {};
    return {
      totalBytes: finiteBytes(value.totalBytes),
      availableBytes: finiteBytes(value.availableBytes),
      nativeStoreBytes: finiteBytes(value.nativeStoreBytes)
    };
  }

  function deriveCapacity(value) {
    var storage = sanitizeStorage(value);
    var total = storage.totalBytes;
    var available = storage.availableBytes;
    var measured = total !== null && total > 0 && available !== null;

    if (!measured) {
      return {
        measured: false,
        totalBytes: total,
        availableBytes: available,
        usedBytes: null,
        usedPercent: null,
        databaseBytes: storage.nativeStoreBytes
      };
    }

    available = clamp(available, 0, total);
    var used = total - available;
    var usedPercent = Math.round((used / total) * 1000) / 10;
    return {
      measured: true,
      totalBytes: total,
      availableBytes: available,
      usedBytes: used,
      usedPercent: clamp(usedPercent, 0, 100),
      databaseBytes: storage.nativeStoreBytes
    };
  }

  function trimFixed(number, decimals) {
    return number.toFixed(decimals)
      .replace(/(\.\d*?[1-9])0+$/, '$1')
      .replace(/\.0+$/, '');
  }

  function formatBytes(value) {
    var bytes = finiteBytes(value);
    if (bytes === null) return '\u2014';
    if (bytes < UNIT_BYTES) return bytes + ' B';

    var unitIndex = 0;
    var amount = bytes;
    while (amount >= UNIT_BYTES && unitIndex < UNITS.length - 1) {
      amount /= UNIT_BYTES;
      unitIndex += 1;
    }
    var decimals = amount >= 100 ? 0 : (amount >= 10 ? 1 : 2);
    return trimFixed(amount, decimals) + ' ' + UNITS[unitIndex];
  }

  function displayModel(value) {
    var capacity = deriveCapacity(value);
    return {
      measured: capacity.measured,
      totalBytes: capacity.totalBytes,
      availableBytes: capacity.availableBytes,
      usedBytes: capacity.usedBytes,
      usedPercent: capacity.usedPercent,
      totalLabel: formatBytes(capacity.totalBytes),
      availableLabel: formatBytes(capacity.availableBytes),
      usedLabel: formatBytes(capacity.usedBytes),
      usedPercentLabel: capacity.usedPercent === null ? '\u2014' : trimFixed(capacity.usedPercent, 1) + '%',
      databaseBytes: capacity.databaseBytes,
      databaseLabel: formatBytes(capacity.databaseBytes)
    };
  }

  return {
    CONTRACT_VERSION: CAPACITY_CONTRACT_VERSION,
    deriveCapacity: deriveCapacity,
    displayModel: displayModel,
    finiteBytes: finiteBytes,
    formatBytes: formatBytes,
    sanitizeStorage: sanitizeStorage
  };
});
