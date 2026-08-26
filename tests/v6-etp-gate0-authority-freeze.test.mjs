import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { createV6EtpGate0Freeze, RECEIPT } from '../scripts/create-v6-etp-gate0-freeze.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const recorded = () => JSON.parse(fs.readFileSync(path.join(root, RECEIPT), 'utf8'));

test('Gate 0 freezes the dependency order and remains fail closed without business approval', () => {
  const value = createV6EtpGate0Freeze();
  assert.deepEqual(value.programmeOrder, ['E3','E4','E6','E5','E7']);
  assert.equal(value.freezeStatus, 'FROZEN_FAIL_CLOSED');
  assert.equal(value.formalBusinessApproval, false);
  assert.equal(value.productActivationAllowed, false);
  assert.equal(value.invariantAuthority.declarationsAsPaymentBasis, 'FORBIDDEN');
  assert.equal(value.invariantAuthority.moneySequence, 'E5_AFTER_E3_E4_E6_ACCEPTANCE');
  assert.equal(value.currentRetailAuthority.HEMW.profileStatus, 'EVIDENCE_PENDING');
  assert.equal(Object.isFrozen(value), true);
  assert.equal(Object.isFrozen(value.capabilities.E5.requiredSources), true);
});

test('all external authorities are hash-empty and approval-empty until real sources are supplied', () => {
  const value = createV6EtpGate0Freeze();
  const required = [
    ...Object.values(value.capabilities.E4.requiredSources),
    ...Object.values(value.capabilities.E5.requiredSources),
    ...Object.values(value.capabilities.E7.requiredSources)
  ];
  assert.ok(required.length >= 10);
  for (const item of required) {
    assert.equal(item.status, 'SOURCE_REQUIRED');
    assert.equal(item.sourceSha256, null);
    assert.equal(item.approval, null);
    assert.ok(Array.isArray(item.requiredFields) && item.requiredFields.length > 0);
  }
  assert.match(value.capabilities.E5.activationStatus, /^BLOCKED_/);
  assert.match(value.capabilities.E7.activationStatus, /^DEFERRED_/);
});

test('E3 owner authority is source-bound and has no remaining role or timing decision', () => {
  const value = createV6EtpGate0Freeze();
  assert.equal(value.capabilities.E3.activationStatus, 'OWNER_POLICY_APPROVED_ENGINEERING_ACTIVE');
  assert.deepEqual(value.capabilities.E3.pendingDecisions, []);
  assert.equal(value.capabilities.E3.authority.approvedByRole, 'Owner');
  assert.match(value.capabilities.E3.authority.source.sha256, /^[a-f0-9]{64}$/);
  assert.equal(value.capabilities.E3.rolePolicy.correctionWindowHours, 24);
  assert.equal(value.capabilities.E3.rolePolicy.ownerCorrectionBoundary, 'ANY_TIME_BEFORE_LOCK');
  assert.equal(value.capabilities.E3.rolePolicy.lockedChangePath, 'VERIFIED_SOURCE_RESTATEMENT_NEW_RECONCILIATION');
});

test('controlled reason and status catalogues are unique safe machine identities', () => {
  const value = createV6EtpGate0Freeze();
  const catalogues = [
    value.capabilities.E3.stateMachine,
    value.capabilities.E3.outcomes,
    value.capabilities.E3.correctionReasonCodes,
    value.capabilities.E3.dispositionReasonCodes,
    value.capabilities.E4.adjustmentReasonCodes,
    value.capabilities.E6.statuses,
    value.capabilities.E6.exceptionTypes,
    value.capabilities.E6.closureReasonCodes,
    value.capabilities.E5.schemeLifecycle,
    value.capabilities.E5.runLifecycle,
    value.capabilities.E5.clawbackLifecycle,
    value.capabilities.E7.mandatoryReports
  ];
  for (const items of catalogues) {
    assert.equal(new Set(items).size, items.length);
    for (const item of items) assert.match(item, /^[A-Z][A-Z0-9_]{1,63}$/);
  }
});

test('recorded Gate 0 receipt is deterministic and binds current pure contract bytes', () => {
  const expected = createV6EtpGate0Freeze();
  assert.deepEqual(recorded(), expected);
  for (const entry of Object.values(expected.contractBaselines)) {
    assert.match(entry.sha256, /^[a-f0-9]{64}$/);
    assert.equal(fs.statSync(path.join(root, entry.path)).size, entry.bytes);
  }
  const serialized = JSON.stringify(expected);
  assert.doesNotMatch(serialized, /customer_name|mobile_number|phone_number|raw_row|filename/i);
});
