/*
 * R0-W4 SEC-08 — central export control.
 *
 * All bulk/file exports must call authorize() immediately before data leaves the
 * app. The policy is disabled by default, requires an Admin PIN, re-authenticates
 * the owner on every manual export, supports a destination-bound standing owner
 * grant for scheduled encrypted backup, and records a sanitised register entry before the
 * caller is allowed to continue.
 *
 * The register deliberately stores metadata only: no customer names, mobile
 * numbers, salary values, message bodies, file contents, PINs or keys.
 */
(function (root) {
  'use strict';

  var POLICY_KEY = 'st_v2_export_policy_v1';
  var REGISTER_KEY = 'st_v2_export_register_v1';
  var SCHEDULE_KEY = 'st_v2_export_schedule_v1';
  var MAX_ENTRIES = 750;

  function nowIso() { return new Date().toISOString(); }
  function notify(message) {
    try {
      if (typeof root.toast === 'function') root.toast(message);
    } catch (_) {}
  }
  function audit(action, detail) {
    try {
      if (typeof root.auditLog === 'function') root.auditLog(action, detail || {});
    } catch (_) {}
  }
  function getRaw(key) {
    try {
      if (typeof root.safeGet === 'function') return root.safeGet(key);
      return root.localStorage ? root.localStorage.getItem(key) : null;
    } catch (_) { return null; }
  }
  function setRaw(key, value) {
    try {
      if (typeof root.safeSet === 'function') return root.safeSet(key, value) !== false;
      if (!root.localStorage) return false;
      root.localStorage.setItem(key, value);
      return root.localStorage.getItem(key) === String(value);
    } catch (_) { return false; }
  }
  function removeRaw(key) {
    try {
      if (typeof root.safeRemove === 'function') return root.safeRemove(key) !== false;
      if (!root.localStorage) return false;
      root.localStorage.removeItem(key);
      return root.localStorage.getItem(key) === null;
    } catch (_) { return false; }
  }
  function parseObject(raw) {
    if (!raw) return null;
    try {
      var value = JSON.parse(raw);
      return value && typeof value === 'object' && !Array.isArray(value) ? value : null;
    } catch (_) { return null; }
  }
  function readPolicy() {
    var raw = getRaw(POLICY_KEY);
    if (!raw) return { enabled: false, updatedAt: '', updatedBy: '' };
    var value = parseObject(raw);
    if (!value) return { enabled: false, damaged: true, updatedAt: '', updatedBy: '' };
    return {
      enabled: value.enabled === true,
      updatedAt: typeof value.updatedAt === 'string' ? value.updatedAt : '',
      updatedBy: cleanLabel(value.updatedBy, 80)
    };
  }
  function readRegister() {
    var raw = getRaw(REGISTER_KEY);
    if (!raw) return [];
    try {
      var value = JSON.parse(raw);
      return Array.isArray(value) ? value : null;
    } catch (_) { return null; }
  }
  function cleanLabel(value, max) {
    return String(value == null ? '' : value)
      .replace(/[\u0000-\u001f\u007f]/g, ' ')
      .replace(/\s+/g, ' ')
      .trim()
      .slice(0, max || 120);
  }
  function cleanCode(value, fallback, max) {
    var code = String(value == null ? '' : value)
      .toLowerCase()
      .replace(/[^a-z0-9._:-]+/g, '-')
      .replace(/^-+|-+$/g, '')
      .slice(0, max || 80);
    return code || fallback;
  }
  function fileType(value, fallback) {
    var match = String(value || '').toLowerCase().match(/\.([a-z0-9]{1,8})$/);
    return cleanCode(match ? match[1] : fallback, 'file', 12);
  }
  function clampCount(value) {
    var n = Number(value);
    if (!Number.isFinite(n) || n < 0) return 0;
    return Math.min(Math.floor(n), 1000000000);
  }
  function ownerActor() {
    try {
      if (typeof root.ownerName === 'function') {
        var name = cleanLabel(root.ownerName(), 80);
        if (name) return name;
      }
    } catch (_) {}
    return 'Owner';
  }
  function hasPin() {
    try { return typeof root.hasAdminPin === 'function' && !!root.hasAdminPin(); }
    catch (_) { return false; }
  }
  async function reauth(reason) {
    try {
      return typeof root.SaagarReauth === 'function' && (await root.SaagarReauth(reason)) === true;
    } catch (_) {
      return false; // export control is intentionally fail-closed
    }
  }
  function safeMeta(meta) {
    meta = meta || {};
    var kind = cleanCode(meta.kind, 'file', 24);
    var moduleId = cleanCode(meta.module, 'shell', 40);
    return {
      actionId: cleanCode(meta.exportId, moduleId + '-' + kind, 80),
      kind: kind,
      scopeId: cleanCode(meta.scopeId, moduleId + '-' + kind, 80),
      scopeLabel: cleanLabel(meta.scopeLabel || meta.scope || 'business records', 100) || 'business records',
      module: moduleId,
      rowCount: clampCount(meta.rowCount),
      fileType: fileType(meta.fileName, kind),
      purposeId: cleanCode(meta.purposeId, 'business-operation', 60)
    };
  }
  function registerAttempt(status, reason, detail, approvalMode) {
    var token = 'exp_' + Date.now().toString(36) + '_' + Math.random().toString(36).slice(2, 8);
    var entry = {
      id: token,
      actionId: detail.actionId,
      at: nowIso(),
      actor: ownerActor(),
      actorRole: 'Owner',
      approver: status === 'approved' ? ownerActor() : '',
      kind: detail.kind,
      fileType: detail.fileType,
      scopeId: detail.scopeId,
      module: detail.module,
      rowCount: detail.rowCount,
      purposeId: detail.purposeId,
      status: status
    };
    if (approvalMode) entry.approvalMode = cleanCode(approvalMode, 'fresh-owner-reauth', 40);
    if (reason) entry.reason = cleanCode(reason, 'denied', 40);
    return persistEntry(entry) ? { token: token, entry: entry } : null;
  }
  function persistEntry(entry) {
    var list = readRegister();
    if (list === null) return false;
    list.unshift(entry);
    return setRaw(REGISTER_KEY, JSON.stringify(list.slice(0, MAX_ENTRIES)));
  }
  function deny(reason, meta) {
    var detail = safeMeta(meta);
    var saved = registerAttempt('denied', reason, detail);
    audit('export.denied', {
      exportId: saved ? saved.token : '',
      actionId: detail.actionId,
      reason: cleanCode(reason, 'denied', 40),
      kind: detail.kind,
      scopeId: detail.scopeId,
      module: detail.module,
      rowCount: detail.rowCount
    });
    return false;
  }
  async function authorize(meta) {
    var detail = safeMeta(meta);
    try {
      if (root.SaagarDeviceSecurity && typeof root.SaagarDeviceSecurity.allowSensitive === 'function' &&
          !root.SaagarDeviceSecurity.allowSensitive('export')) {
        notify('Export blocked: this production device has an unsafe or unavailable security posture.');
        return deny('device-posture-unsafe', detail);
      }
    } catch (_) {
      notify('Export blocked: device security status could not be verified.');
      return deny('device-posture-error', detail);
    }
    var policy = readPolicy();
    if (policy.damaged) {
      notify('Export control is damaged. Owner must repair it in Security settings.');
      return deny('policy-damaged', detail);
    }
    if (!policy.enabled) {
      notify('Exports are disabled. Owner can enable them in Security settings.');
      return deny('policy-disabled', detail);
    }
    if (!hasPin()) {
      notify('Set an Admin PIN before enabling or using exports.');
      return deny('admin-pin-required', detail);
    }
    if (!await reauth('Approve export: ' + detail.scopeLabel + (detail.rowCount ? ' (' + detail.rowCount + ' rows/files)' : ''))) {
      notify('Export cancelled — owner approval was not completed.');
      return deny('owner-approval-denied', detail);
    }
    var saved = registerAttempt('approved', '', detail, 'fresh-owner-reauth');
    if (!saved) {
      notify('Export blocked because the export register could not be saved.');
      return deny('register-write-failed', detail);
    }
    audit('export.approved', {
      exportId: saved.token,
      actionId: saved.entry.actionId,
      kind: saved.entry.kind,
      scopeId: saved.entry.scopeId,
      module: saved.entry.module,
      rowCount: saved.entry.rowCount
    });
    return saved.token;
  }
  function readScheduledGrant() {
    var raw = getRaw(SCHEDULE_KEY);
    if (!raw) return null;
    var value = parseObject(raw);
    if (!value || value.enabled !== true || !value.destinationId || !value.scopeId) return { damaged: true };
    return {
      enabled: true,
      destinationId: cleanCode(value.destinationId, '', 80),
      scopeId: cleanCode(value.scopeId, '', 80),
      approvedAt: cleanLabel(value.approvedAt, 40),
      approvedBy: cleanLabel(value.approvedBy, 80)
    };
  }
  function postureAllowsScheduled() {
    try {
      if (root.SaagarDeviceSecurity && typeof root.SaagarDeviceSecurity.allowSensitive === 'function' &&
          !root.SaagarDeviceSecurity.allowSensitive('scheduled-export')) {
        notify('Automatic backup blocked: this production device has an unsafe or unavailable security posture.');
        return false;
      }
      return true;
    } catch (_) {
      notify('Automatic backup blocked: device security status could not be verified.');
      return false;
    }
  }
  async function approveScheduled(meta) {
    var detail = safeMeta(meta), destinationId = cleanCode(meta && meta.destinationId, '', 80);
    if (!destinationId) { notify('Choose a verified off-device backup folder first.'); return false; }
    if (!postureAllowsScheduled()) return false;
    var policy = readPolicy();
    if (policy.damaged || !policy.enabled) { notify('Enable file and bulk-data exports before setting up automatic backup.'); return false; }
    if (!hasPin()) { notify('Set an Admin PIN before setting up automatic backup.'); return false; }
    if (!await reauth('Approve automatic encrypted backup to the selected off-device folder')) return false;
    var grant = {
      enabled: true,
      destinationId: destinationId,
      scopeId: detail.scopeId,
      approvedAt: nowIso(),
      approvedBy: ownerActor()
    };
    if (!setRaw(SCHEDULE_KEY, JSON.stringify(grant))) { notify('Automatic-backup approval could not be saved.'); return false; }
    audit('export.schedule.approved', { scopeId: grant.scopeId, approvedBy: grant.approvedBy });
    return grant;
  }
  function authorizeScheduled(meta) {
    var detail = safeMeta(meta), destinationId = cleanCode(meta && meta.destinationId, '', 80);
    if (!postureAllowsScheduled()) return deny('device-posture-unsafe', detail);
    var policy = readPolicy();
    if (policy.damaged) return deny('policy-damaged', detail);
    if (!policy.enabled) return deny('policy-disabled', detail);
    if (!hasPin()) return deny('admin-pin-required', detail);
    var grant = readScheduledGrant();
    if (!grant || grant.damaged) return deny(grant && grant.damaged ? 'schedule-damaged' : 'schedule-missing', detail);
    if (!destinationId || grant.destinationId !== destinationId) return deny('schedule-destination-mismatch', detail);
    if (grant.scopeId !== detail.scopeId) return deny('schedule-scope-mismatch', detail);
    var saved = registerAttempt('approved', '', detail, 'standing-owner-grant');
    if (!saved) return deny('register-write-failed', detail);
    audit('export.schedule.authorized', { exportId: saved.token, scopeId: detail.scopeId, module: detail.module, rowCount: detail.rowCount });
    return saved.token;
  }
  async function revokeScheduled() {
    if (!hasPin()) { notify('Set an Admin PIN before changing automatic backup.'); return false; }
    if (!await reauth('Disable automatic encrypted off-device backup')) return false;
    if (!removeRaw(SCHEDULE_KEY)) { notify('Automatic-backup approval could not be removed.'); return false; }
    audit('export.schedule.revoked', { updatedBy: ownerActor() });
    return true;
  }
  function recordOutcome(token, outcome) {
    token = cleanLabel(token, 80);
    var status = cleanLabel(outcome, 24).toLowerCase();
    if (['completed', 'cancelled', 'failed', 'downloaded', 'shared', 'printed'].indexOf(status) < 0) status = 'completed';
    var list = readRegister();
    if (!token || list === null) return false;
    var row = list.find(function (item) { return item && item.id === token; });
    if (!row || (row.status !== 'approved' && row.status !== 'delivering')) return false;
    row.status = status;
    row.completedAt = nowIso();
    var ok = setRaw(REGISTER_KEY, JSON.stringify(list.slice(0, MAX_ENTRIES)));
    if (ok) audit('export.' + status, { exportId: token });
    return ok;
  }
  function beginDelivery(token) {
    token = cleanLabel(token, 80);
    var list = readRegister();
    if (!token || list === null) return false;
    var row = list.find(function (item) { return item && item.id === token; });
    if (!row || row.status !== 'approved') return false;
    row.status = 'delivering';
    row.startedAt = nowIso();
    var ok = setRaw(REGISTER_KEY, JSON.stringify(list.slice(0, MAX_ENTRIES)));
    if (ok) audit('export.delivery.started', { exportId: token });
    return ok;
  }
  async function setEnabled(enabled) {
    enabled = enabled === true;
    if (!hasPin()) {
      notify('Set an Admin PIN before changing export control.');
      return false;
    }
    if (!await reauth((enabled ? 'Enable' : 'Disable') + ' file and bulk-data exports')) return false;
    var policy = { enabled: enabled, updatedAt: nowIso(), updatedBy: ownerActor() };
    if (!setRaw(POLICY_KEY, JSON.stringify(policy))) {
      notify('Export policy could not be saved.');
      return false;
    }
    audit(enabled ? 'export.policy.enabled' : 'export.policy.disabled', { updatedBy: policy.updatedBy });
    notify(enabled ? 'Exports enabled — manual exports need fresh approval; automatic backup needs a destination-bound standing approval.' : 'Exports disabled.');
    return true;
  }
  root.SaagarExportControl = {
    POLICY_KEY: POLICY_KEY,
    REGISTER_KEY: REGISTER_KEY,
    SCHEDULE_KEY: SCHEDULE_KEY,
    authorize: authorize,
    approveScheduled: approveScheduled,
    authorizeScheduled: authorizeScheduled,
    revokeScheduled: revokeScheduled,
    scheduledGrant: readScheduledGrant,
    beginDelivery: beginDelivery,
    recordOutcome: recordOutcome,
    setEnabled: setEnabled,
    policy: readPolicy,
    entries: function () {
      var list = readRegister();
      return list === null ? null : list.slice();
    },
    safeMeta: safeMeta
  };
})(typeof window !== 'undefined' ? window : globalThis);
