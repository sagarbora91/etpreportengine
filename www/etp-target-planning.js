/* Phase 6H.3 E4: pure target, planning and Leave computation policy. */
(function (root, factory) {
  'use strict';
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  if (root) root.SaagarEtpTargetPlanning = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';
  var VERSION = 'ETP_E4_TARGET_PLANNING_V1';
  var CURVE_SCALE = 1000000;
  var MAX_MONEY = 9000000000;
  var SAFE_ID = /^[A-Za-z0-9][A-Za-z0-9_.:-]{0,79}$/;

  function freeze(value) {
    if (value && typeof value === 'object' && !Object.isFrozen(value)) {
      Object.keys(value).forEach(function (key) { freeze(value[key]); });
      Object.freeze(value);
    }
    return value;
  }
  function record(value) { return !!value && typeof value === 'object' && !Array.isArray(value); }
  function fail(code) { return freeze({ ok: false, code: code }); }
  function text(value, max) { var out = typeof value === 'string' ? value.trim() : ''; return out && out.length <= max ? out : ''; }
  function id(value) { var out = text(value, 80); return SAFE_ID.test(out) ? out : ''; }
  function money(value) { return Number.isSafeInteger(value) && value >= 0 && value <= MAX_MONEY ? value : null; }
  function iso(value) {
    var raw = String(value || ''), match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(raw), date;
    if (!match) return '';
    date = new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3])));
    return date.getUTCFullYear() === Number(match[1]) && date.getUTCMonth() === Number(match[2]) - 1 && date.getUTCDate() === Number(match[3]) ? raw : '';
  }
  function instant(value) { var out = text(value, 40), parsed = Date.parse(out); return out && Number.isFinite(parsed) ? out : ''; }
  function addDays(value, amount) { return new Date(Date.parse(value + 'T00:00:00Z') + amount * 86400000).toISOString().slice(0, 10); }
  function previousYear(value) {
    var date = new Date(value + 'T00:00:00Z'), month = date.getUTCMonth();
    date.setUTCFullYear(date.getUTCFullYear() - 1);
    if (date.getUTCMonth() !== month) date = new Date(Date.UTC(date.getUTCFullYear(), month + 1, 0));
    return date.toISOString().slice(0, 10);
  }
  function dates(start, end) {
    var out = [], cursor = start;
    while (cursor <= end && out.length <= 366) { out.push(cursor); cursor = addDays(cursor, 1); }
    return cursor > end ? out : [];
  }
  function approval(value) {
    if (!record(value) || value.status !== 'APPROVED') return null;
    var by = id(value.approvedBy), at = instant(value.approvedAt), authorityRef = text(value.authorityRef, 160);
    return by && at && authorityRef ? freeze({ status: 'APPROVED', approvedBy: by, approvedAt: at, authorityRef: authorityRef }) : null;
  }
  function source(value) {
    if (!record(value)) return null;
    var documentRef = text(value.documentRef, 160), receivedDate = iso(value.receivedDate), issuer = text(value.issuer, 120);
    return documentRef && receivedDate && issuer ? freeze({ documentRef: documentRef, receivedDate: receivedDate, issuer: issuer }) : null;
  }
  function normalizeWeights(raw) {
    var total = raw.reduce(function (sum, item) { return sum + item.value; }, 0), used = 0;
    if (!Number.isSafeInteger(total) || total <= 0) return null;
    var out = raw.map(function (item) {
      var scaled = Math.floor(item.value / total * CURVE_SCALE);
      used += scaled; return { date: item.date, weightPpm: scaled };
    });
    for (var remainder = CURVE_SCALE - used, i = 0; remainder > 0; remainder--, i = (i + 1) % out.length) out[i].weightPpm++;
    return out;
  }
  function validVersion(value) {
    if (!record(value) || value.contractVersion !== VERSION || !id(value.storeCode) || value.versionId !== value.storeCode + '|' + value.periodStart + '..' + value.periodEnd + '|v' + value.version ||
        !iso(value.periodStart) || !iso(value.periodEnd) || value.periodStart > value.periodEnd || !Number.isSafeInteger(value.version) || value.version < 1 ||
        money(value.storeTargetPaise) === null || !source(value.source) || !approval(value.approval) || value.allocationLockDate !== value.periodStart ||
        !Array.isArray(value.allocations) || !value.allocations.length || !record(value.allocationIdentity) || !record(value.curve) ||
        value.curve.source !== 'ETP_VERIFIED' || value.curve.scale !== CURVE_SCALE || !Array.isArray(value.curve.weights)) return false;
    var seen = Object.create(null), base = 0, stretch = 0;
    for (var i = 0; i < value.allocations.length; i++) {
      var row = value.allocations[i];
      if (!record(row) || !id(row.croId) || seen[row.croId] || money(row.baseTargetPaise) === null || money(row.stretchTargetPaise) === null || row.stretchTargetPaise < row.baseTargetPaise) return false;
      seen[row.croId] = true; base += row.baseTargetPaise; stretch += row.stretchTargetPaise;
      if (!Number.isSafeInteger(base) || !Number.isSafeInteger(stretch)) return false;
    }
    var periodDates = dates(value.periodStart, value.periodEnd), weightDates = Object.create(null), weightSum = 0;
    if (value.curve.weights.length !== periodDates.length) return false;
    for (i = 0; i < value.curve.weights.length; i++) {
      row = value.curve.weights[i];
      if (!record(row) || periodDates.indexOf(row.date) < 0 || weightDates[row.date] || !Number.isSafeInteger(row.weightPpm) || row.weightPpm < 0) return false;
      weightDates[row.date] = true; weightSum += row.weightPpm;
    }
    return weightSum === CURVE_SCALE && base === value.storeTargetPaise && value.allocationIdentity.baseSumPaise === base &&
      value.allocationIdentity.stretchSumPaise === stretch && value.allocationIdentity.reconciles === true;
  }
  function buildCurve(input, periodDates, planVersion) {
    if (!record(input.lyEvidence) || input.lyEvidence.source !== 'ETP_VERIFIED' || input.lyEvidence.coverageComplete !== true ||
        input.lyEvidence.periodStart !== previousYear(periodDates[0]) || input.lyEvidence.periodEnd !== previousYear(periodDates[periodDates.length - 1])) return null;
    if (!Array.isArray(input.lyDailyActuals) || input.lyDailyActuals.length !== periodDates.length) return null;
    var ly = Object.create(null), overrides = Object.create(null), festive = [];
    for (var i = 0; i < input.lyDailyActuals.length; i++) {
      var row = input.lyDailyActuals[i], date = record(row) ? iso(row.planDate) : '', lyDate = record(row) ? iso(row.lyDate) : '', actual = record(row) ? money(row.actualPaise) : null;
      if (!date || lyDate !== previousYear(date) || periodDates.indexOf(date) < 0 || actual === null || ly[date] !== undefined) return null;
      ly[date] = actual;
    }
    if (!Array.isArray(input.festiveOverrides)) return null;
    for (i = 0; i < input.festiveOverrides.length; i++) {
      var item = input.festiveOverrides[i], itemDate = record(item) ? iso(item.date) : '', multiplier = record(item) ? item.multiplierBps : null;
      var checkedApproval = record(item) ? approval(item.approval) : null;
      if (!itemDate || periodDates.indexOf(itemDate) < 0 || overrides[itemDate] || !Number.isSafeInteger(multiplier) || multiplier < 0 || multiplier > 100000 || item.version !== planVersion || !text(item.reason, 160) || !checkedApproval) return null;
      overrides[itemDate] = multiplier;
      festive.push({ date: itemDate, version: item.version, multiplierBps: multiplier, reason: item.reason.trim(), approval: checkedApproval });
    }
    var raw = periodDates.map(function (date) {
      var base = ly[date], multiplierBps = overrides[date] === undefined ? 10000 : overrides[date];
      return { date: date, value: base * multiplierBps };
    });
    if (raw.some(function (item) { return !Number.isSafeInteger(item.value); })) return null;
    var weights = normalizeWeights(raw); if (!weights) return null;
    return freeze({ method: 'LY_DAILY_ACTUAL_WITH_VERSIONED_FESTIVE_OVERRIDE', source: 'ETP_VERIFIED', scale: CURVE_SCALE, weights: weights, festiveOverrides: festive });
  }
  function publish(input, history) {
    if (!record(input) || !Array.isArray(history)) return fail('TARGET_PUBLICATION_INVALID');
    var storeCode = id(input.storeCode), periodStart = iso(input.periodStart), periodEnd = iso(input.periodEnd), targetPaise = money(input.storeTargetPaise);
    var version = input.version, checkedSource = source(input.source), checkedApproval = approval(input.approval), periodDates = dates(periodStart, periodEnd);
    if (!storeCode || !periodStart || !periodEnd || periodStart > periodEnd || !periodDates.length || targetPaise === null || targetPaise === 0 || !Number.isSafeInteger(version) || version < 1) return fail('TARGET_DEFINITION_INVALID');
    if (!checkedSource) return fail('TARGET_SOURCE_AUTHORITY_REQUIRED');
    if (!checkedApproval) return fail('TARGET_APPROVAL_REQUIRED');
    if (input.allocationLockDate !== periodStart) return fail('DAY_ZERO_ALLOCATION_LOCK_REQUIRED');
    if (history.length !== version - 1) return fail('TARGET_VERSION_SEQUENCE_INVALID');
    for (var h = 0; h < history.length; h++) {
      if (!validVersion(history[h]) || history[h].storeCode !== storeCode || history[h].periodStart !== periodStart || history[h].periodEnd !== periodEnd || history[h].version !== h + 1) return fail('TARGET_HISTORY_INVALID');
    }
    if (version === 1 && history.length) return fail('TARGET_VERSION_ONE_IMMUTABLE');
    if (version > 1 && input.previousVersionId !== history[history.length - 1].versionId) return fail('TARGET_REVISION_PARENT_REQUIRED');
    if (!Array.isArray(input.allocations) || !input.allocations.length) return fail('TARGET_ALLOCATIONS_INVALID');
    var allocations = [], seen = Object.create(null), baseSum = 0, stretchSum = 0;
    for (var i = 0; i < input.allocations.length; i++) {
      var row = input.allocations[i], croId = record(row) ? id(row.croId) : '', base = record(row) ? money(row.baseTargetPaise) : null, stretch = record(row) ? money(row.stretchTargetPaise) : null;
      if (!croId || seen[croId] || base === null || stretch === null || stretch < base || !Number.isSafeInteger(baseSum + base) || !Number.isSafeInteger(stretchSum + stretch)) return fail('TARGET_ALLOCATIONS_INVALID');
      seen[croId] = true; baseSum += base; stretchSum += stretch; allocations.push({ croId: croId, baseTargetPaise: base, stretchTargetPaise: stretch });
    }
    if (baseSum !== targetPaise) return fail('TARGET_ALLOCATION_SUM_MISMATCH');
    var curve = buildCurve(input, periodDates, version); if (!curve) return fail('TARGET_CURVE_INVALID');
    var versionId = storeCode + '|' + periodStart + '..' + periodEnd + '|v' + version;
    return freeze({ ok: true, version: {
      contractVersion: VERSION, versionId: versionId, version: version, previousVersionId: version === 1 ? null : input.previousVersionId,
      storeCode: storeCode, periodStart: periodStart, periodEnd: periodEnd, storeTargetPaise: targetPaise,
      source: checkedSource, approval: checkedApproval, allocationLockDate: periodStart, allocations: allocations,
      allocationIdentity: { baseSumPaise: baseSum, storeTargetPaise: targetPaise, reconciles: true, stretchSumPaise: stretchSum, stretchBps: Math.round((stretchSum - targetPaise) * 10000 / targetPaise) },
      curve: curve
    } });
  }
  function checkedLeaves(value, version, croIds) {
    if (!Array.isArray(value)) return null;
    var out = [], seen = Object.create(null);
    for (var i = 0; i < value.length; i++) {
      var row = value[i], leaveId = record(row) ? id(row.leaveId) : '', croId = record(row) ? id(row.croId) : '', date = record(row) ? iso(row.date) : '', fraction = record(row) ? row.fractionBps : null;
      if (!leaveId || seen[leaveId] || !croIds[croId] || date < version.periodStart || date > version.periodEnd || [5000, 10000].indexOf(fraction) < 0 || !approval(record(row) ? row.approval : null)) return null;
      seen[leaveId] = true; out.push({ leaveId: leaveId, croId: croId, date: date, fractionBps: fraction, approval: approval(row.approval) });
    }
    return out;
  }
  function compute(version, input) {
    if (!validVersion(version) || !record(input) || !iso(input.asOfDate) || input.asOfDate < version.periodStart || input.asOfDate > version.periodEnd) return fail('TARGET_COMPUTE_INVALID');
    if (!record(input.actuals) || input.actuals.source !== 'ETP_VERIFIED' || input.actuals.coverageComplete !== true || iso(input.actuals.verifiedThrough) !== input.asOfDate || !id(input.actuals.receiptId) || !id(input.actuals.generationId) || !record(input.actuals.byCro)) return fail('TARGET_VERIFIED_ACTUAL_REQUIRED');
    if (input.declarations !== undefined && (!Array.isArray(input.declarations) || input.declarations.length)) return fail('TARGET_DECLARATIONS_NOT_ACHIEVEMENT');
    var croIds = Object.create(null), allocationByCro = Object.create(null);
    version.allocations.forEach(function (row) { croIds[row.croId] = true; allocationByCro[row.croId] = row; });
    var leaves = checkedLeaves(input.approvedLeave, version, croIds); if (!leaves) return fail('TARGET_LEAVE_INVALID');
    var actualKeys = Object.keys(input.actuals.byCro);
    if (actualKeys.length !== version.allocations.length || actualKeys.some(function (key) { return !croIds[key] || money(input.actuals.byCro[key]) === null; })) return fail('TARGET_VERIFIED_ACTUAL_INVALID');
    var weightByDate = Object.create(null); version.curve.weights.forEach(function (row) { weightByDate[row.date] = row.weightPpm; });
    var leaveByCroDate = Object.create(null);
    leaves.forEach(function (row) { leaveByCroDate[row.croId + '|' + row.date] = row.fractionBps; });
    var rows = [], shortfall = 0, periodDates = dates(version.periodStart, version.periodEnd);
    version.allocations.forEach(function (allocation) {
      var leaveWeight = 0, elapsedAvailableWeight = 0, totalAvailableWeight = 0, remainingCapacityBps = 0;
      periodDates.forEach(function (date) {
        var fraction = leaveByCroDate[allocation.croId + '|' + date] || 0, available = Math.round(weightByDate[date] * (10000 - fraction) / 10000);
        leaveWeight += weightByDate[date] - available; totalAvailableWeight += available;
        if (date <= input.asOfDate) elapsedAvailableWeight += available;
        else remainingCapacityBps += 10000 - fraction;
      });
      var target = Math.round(allocation.stretchTargetPaise * totalAvailableWeight / CURVE_SCALE);
      var pace = Math.round(allocation.stretchTargetPaise * elapsedAvailableWeight / CURVE_SCALE);
      var actual = money(input.actuals.byCro[allocation.croId]);
      var gap = target - actual, required = gap <= 0 ? 0 : (remainingCapacityBps ? Math.ceil(gap * 10000 / remainingCapacityBps) : null);
      var projected = elapsedAvailableWeight ? Math.round(actual * totalAvailableWeight / elapsedAvailableWeight) : null;
      var reduction = allocation.stretchTargetPaise - target; shortfall += reduction;
      rows.push({ croId: allocation.croId, monthTargetPaise: target, preLeaveTargetPaise: allocation.stretchTargetPaise, leaveProrationPaise: reduction,
        mtdPaceTargetPaise: pace, verifiedActualPaise: actual, rupeeGapPaise: gap, requiredRunRatePaisePerFullDay: required,
        projectedLandingPaise: projected, achievementBps: target ? Math.round(actual * 10000 / target) : null });
    });
    return freeze({ ok: true, plan: { contractVersion: VERSION, versionId: version.versionId, asOfDate: input.asOfDate,
      verifiedActualSource: 'ETP_VERIFIED', coverageShortfall: { label: 'Coverage Shortfall', amountPaise: shortfall }, cro: rows,
      identities: { baseAllocationsReconcile: version.allocationIdentity.baseSumPaise === version.storeTargetPaise, declarationsUsedAsAchievement: false },
      computedOnly: { achievementStored: false, projectedLandingStored: false } } });
  }
  return freeze({ VERSION: VERSION, CURVE_SCALE: CURVE_SCALE, MAX_MONEY: MAX_MONEY, publish: publish, compute: compute });
});
