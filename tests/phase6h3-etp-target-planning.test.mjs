import assert from 'node:assert/strict';
import test from 'node:test';
import api from '../www/etp-target-planning.js';

const approval = suffix => ({ status: 'APPROVED', approvedBy: 'owner-1', approvedAt: `2026-08-${suffix}T10:00:00Z`, authorityRef: `OWNER-E4-${suffix}` });
const days = ['2026-09-01', '2026-09-02', '2026-09-03', '2026-09-04'];
function fixture(version = 1, previousVersionId) {
  return {
    storeCode: 'WLMHW', periodStart: days[0], periodEnd: days[3], version, previousVersionId,
    storeTargetPaise: 4000000, allocationLockDate: days[0],
    source: { documentRef: `TITAN-TARGET-${version}`, receivedDate: '2026-08-20', issuer: 'Titan Company Limited' },
    approval: approval(`2${version}`),
    allocations: [
      { croId: 'CRO-1', baseTargetPaise: 2000000, stretchTargetPaise: 2200000 },
      { croId: 'CRO-2', baseTargetPaise: 2000000, stretchTargetPaise: 2000000 }
    ],
    lyEvidence: { source: 'ETP_VERIFIED', coverageComplete: true, periodStart: '2025-09-01', periodEnd: '2025-09-04' },
    lyDailyActuals: days.map((planDate, i) => ({ planDate, lyDate: planDate.replace('2026-', '2025-'), actualPaise: (i + 1) * 100000 })),
    festiveOverrides: [{ date: days[2], version, multiplierBps: 20000, reason: 'Approved festive trading day', approval: approval(`1${version}`) }]
  };
}

test('publication fails closed without both target source and approval authority', () => {
  const missingSource = fixture(); delete missingSource.source;
  assert.deepEqual(api.publish(missingSource, []), { ok: false, code: 'TARGET_SOURCE_AUTHORITY_REQUIRED' });
  const missingApproval = fixture(); delete missingApproval.approval;
  assert.deepEqual(api.publish(missingApproval, []), { ok: false, code: 'TARGET_APPROVAL_REQUIRED' });
  assert.equal(Object.isFrozen(api), true);
});

test('version one is immutable, day-0 locked, reconciled and carries a normalized approved curve', () => {
  const published = api.publish(fixture(), []);
  assert.equal(published.ok, true);
  assert.equal(Object.isFrozen(published.version), true);
  assert.equal(Object.isFrozen(published.version.allocations[0]), true);
  assert.equal(published.version.allocationLockDate, days[0]);
  assert.deepEqual(published.version.allocationIdentity, {
    baseSumPaise: 4000000, storeTargetPaise: 4000000, reconciles: true, stretchSumPaise: 4200000, stretchBps: 500
  });
  assert.equal(published.version.curve.weights.reduce((sum, row) => sum + row.weightPpm, 0), api.CURVE_SCALE);
  assert.equal(published.version.curve.source, 'ETP_VERIFIED');
  assert.equal(published.version.curve.festiveOverrides[0].version, 1);
  assert.throws(() => { published.version.storeTargetPaise = 1; }, TypeError);
  const wrongLock = fixture(); wrongLock.allocationLockDate = days[1];
  assert.equal(api.publish(wrongLock, []).code, 'DAY_ZERO_ALLOCATION_LOCK_REQUIRED');
  const mismatch = fixture(); mismatch.allocations[0].baseTargetPaise--;
  assert.equal(api.publish(mismatch, []).code, 'TARGET_ALLOCATION_SUM_MISMATCH');
});

test('revision and reallocation require a new sequential approved version with an exact parent', () => {
  const v1 = api.publish(fixture(), []).version;
  const directEdit = fixture(1); directEdit.allocations.reverse();
  assert.equal(api.publish(directEdit, [v1]).code, 'TARGET_VERSION_SEQUENCE_INVALID');
  const v2Input = fixture(2, v1.versionId);
  v2Input.allocations = [
    { croId: 'CRO-1', baseTargetPaise: 1500000, stretchTargetPaise: 1700000 },
    { croId: 'CRO-2', baseTargetPaise: 2500000, stretchTargetPaise: 2500000 }
  ];
  const v2 = api.publish(v2Input, [v1]);
  assert.equal(v2.ok, true);
  assert.equal(v2.version.previousVersionId, v1.versionId);
  const orphan = fixture(2, 'wrong-parent');
  assert.equal(api.publish(orphan, [v1]).code, 'TARGET_REVISION_PARENT_REQUIRED');
});

test('approved Leave pro-rates targets and exposes an explicit Coverage Shortfall identity', () => {
  const version = api.publish(fixture(), []).version;
  const result = api.compute(version, {
    asOfDate: days[1],
    approvedLeave: [{ leaveId: 'L-1', croId: 'CRO-1', date: days[2], fractionBps: 10000, approval: approval('25') }],
    actuals: { source: 'ETP_VERIFIED', coverageComplete: true, verifiedThrough: days[1], receiptId: 'receipt-1', generationId: 'generation-1', byCro: { 'CRO-1': 500000, 'CRO-2': 400000 } }
  });
  assert.equal(result.ok, true);
  assert.equal(result.plan.coverageShortfall.label, 'Coverage Shortfall');
  assert.ok(result.plan.coverageShortfall.amountPaise > 0);
  assert.equal(result.plan.cro[0].preLeaveTargetPaise - result.plan.cro[0].monthTargetPaise, result.plan.coverageShortfall.amountPaise);
  assert.equal(result.plan.identities.baseAllocationsReconcile, true);
  assert.equal(result.plan.computedOnly.achievementStored, false);
  assert.equal(Object.isFrozen(result.plan), true);
});

test('pace view uses verified ETP actuals and never declarations as achievement', () => {
  const version = api.publish(fixture(), []).version;
  const request = {
    asOfDate: days[1], approvedLeave: [],
    actuals: { source: 'ETP_VERIFIED', coverageComplete: true, verifiedThrough: days[1], receiptId: 'receipt-1', generationId: 'generation-1', byCro: { 'CRO-1': 500000, 'CRO-2': 400000 } }
  };
  const result = api.compute(version, request);
  assert.equal(result.ok, true);
  for (const row of result.plan.cro) {
    assert.ok(Number.isSafeInteger(row.monthTargetPaise));
    assert.ok(Number.isSafeInteger(row.mtdPaceTargetPaise));
    assert.ok(Number.isSafeInteger(row.verifiedActualPaise));
    assert.ok(Number.isSafeInteger(row.rupeeGapPaise));
    assert.ok(row.requiredRunRatePaisePerFullDay === null || Number.isSafeInteger(row.requiredRunRatePaisePerFullDay));
    assert.ok(row.projectedLandingPaise === null || Number.isSafeInteger(row.projectedLandingPaise));
    assert.ok(row.achievementBps === null || Number.isSafeInteger(row.achievementBps));
  }
  assert.equal(api.compute(version, { ...request, declarations: [{ croId: 'CRO-1', amountPaise: 99999999 }] }).code, 'TARGET_DECLARATIONS_NOT_ACHIEVEMENT');
  assert.equal(api.compute(version, { ...request, actuals: { ...request.actuals, source: 'DECLARED' } }).code, 'TARGET_VERIFIED_ACTUAL_REQUIRED');
  assert.equal(api.compute(version, { ...request, approvedLeave: [{ leaveId: 'L-X', croId: 'CRO-1', date: days[2], fractionBps: 10000 }] }).code, 'TARGET_LEAVE_INVALID');
  assert.equal(api.compute(Object.freeze({ contractVersion: api.VERSION }), request).code, 'TARGET_COMPUTE_INVALID');
  assert.equal(api.compute(version, { ...request, actuals: { ...request.actuals, byCro: { 'CRO-1': api.MAX_MONEY + 1, 'CRO-2': 0 } } }).code, 'TARGET_VERIFIED_ACTUAL_INVALID');
});
