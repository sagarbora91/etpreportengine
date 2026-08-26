(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  if (root) root.SaagarStoreContext = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';

  const ALL_STORE_CODE = 'ALL';
  const STORE_FIELDS = ['storeCode', 'branchCode', 'storeKey', 'store', 'branch', 'location'];

  function text(value) {
    return value == null ? '' : String(value).trim();
  }

  function token(value) {
    if (value && typeof value === 'object') {
      value = value.code || value.storeCode || value.branchCode || value.key ||
        value.storeKey || value.name || value.channel || '';
    }
    return text(value).toLowerCase().replace(/[^a-z0-9]/g, '');
  }

  function normalizeBranches(branches) {
    return (Array.isArray(branches) ? branches : [])
      .filter(branch => branch && branch.active !== false)
      .map(branch => {
        const code = text(branch.code || branch.storeCode || branch.branchCode).toUpperCase();
        const key = text(branch.storeKey || branch.key);
        const name = text(branch.channel || branch.name || code);
        const aliases = [
          code,
          key,
          name,
          branch.name,
          branch.channel,
          branch.fullName,
          ...(Array.isArray(branch.aliases) ? branch.aliases : [])
        ].map(token).filter(Boolean);
        return {
          code,
          key,
          name,
          aliases: Array.from(new Set(aliases))
        };
      })
      .filter(branch => branch.code);
  }

  function resolveSelection(savedCode, branches) {
    const stores = normalizeBranches(branches);
    const requested = text(savedCode).toUpperCase();
    if (!requested || requested === ALL_STORE_CODE) {
      return {
        code: ALL_STORE_CODE,
        name: 'All stores',
        isAll: true,
        valid: true,
        stores
      };
    }
    const selected = stores.find(store => store.code === requested);
    if (!selected) {
      return {
        code: ALL_STORE_CODE,
        name: 'All stores',
        isAll: true,
        valid: false,
        stores
      };
    }
    return Object.assign({ isAll: false, valid: true, stores }, selected);
  }

  function recordStoreValue(record) {
    if (!record || typeof record !== 'object') return '';
    for (const field of STORE_FIELDS) {
      if (record[field] != null && text(record[field])) return record[field];
    }
    return '';
  }

  function classifyRecord(record, selectedCode, branches) {
    const selection = resolveSelection(selectedCode, branches);
    const raw = recordStoreValue(record);
    const value = token(raw);
    if (selection.isAll) return value ? 'match' : 'unassigned';
    if (!value) return 'unassigned';
    if (selection.aliases.includes(value)) return 'match';
    const knownOther = selection.stores.some(store => store.aliases.includes(value));
    return knownOther ? 'other-store' : 'unknown-store';
  }

  /*
   * A dataset with no store tags remains visible as organisation-wide data.
   * As soon as any record carries a store tag, single-store views become strict:
   * matching records are included; unassigned, other-store, and unknown tags are
   * excluded and counted so the shell can explain the boundary.
   */
  function scopeRecords(records, selectedCode, branches) {
    const list = Array.isArray(records) ? records : [];
    const selection = resolveSelection(selectedCode, branches);
    const counts = {
      matched: 0,
      unassigned: 0,
      otherStore: 0,
      unknownStore: 0
    };
    if (selection.isAll) {
      list.forEach(record => {
        const state = classifyRecord(record, selection.code, branches);
        if (state === 'unassigned') counts.unassigned++;
        else counts.matched++;
      });
      return Object.assign({ items: list.slice(), mode: 'all' }, counts);
    }

    const classified = list.map(record => ({
      record,
      state: classifyRecord(record, selection.code, branches),
      tagged: !!token(recordStoreValue(record))
    }));
    const taggedCount = classified.filter(entry => entry.tagged).length;
    if (!taggedCount) {
      counts.unassigned = list.length;
      return Object.assign({ items: list.slice(), mode: 'organisation-wide' }, counts);
    }

    const items = [];
    classified.forEach(entry => {
      if (entry.state === 'match') {
        counts.matched++;
        items.push(entry.record);
      } else if (entry.state === 'unassigned') {
        counts.unassigned++;
      } else if (entry.state === 'other-store') {
        counts.otherStore++;
      } else {
        counts.unknownStore++;
      }
    });
    return Object.assign({ items, mode: 'scoped' }, counts);
  }

  return {
    ALL_STORE_CODE,
    normalizeBranches,
    resolveSelection,
    recordStoreValue,
    classifyRecord,
    scopeRecords
  };
});
