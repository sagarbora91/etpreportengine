import assert from 'node:assert/strict';
import test from 'node:test';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const policy = require('../www/etp-store-lifecycle-policy.js');
const profileAuthority = require('../www/etp-profile-authority.js');

const scope = { storeCode: 'WLMHW', financialYear: '2024-25', periodStart: '2024-09-16', periodEnd: '2025-03-31' };
const authorityBinding=profileAuthority.authorize({storeCode:'WLMHW',purpose:'PRODUCTION',profileVersion:profileAuthority.PROFILE_VERSION,parserVersion:profileAuthority.PARSER_VERSION}).binding;
const tenderIdentity={contractVersion:'ETP_TENDER_DICTIONARY_V1',versionId:'retail-etp-tender-v1',effectiveAt:'2026-08-24T00:00:00.000Z'};
function manifest(generationId = 'gen:001', override = {}) {
  return { scopeKey: 'WLMHW|2024-25|2024-09-16..2025-03-31', generationId, authority:authorityBinding, tenderDictionary:tenderIdentity, reports: policy.REPORT_IDS.map((reportId, index) => ({ reportId, sourceSha256: String(index + 1).repeat(64), headerSignatureSha256: String(index + 5).repeat(64), rowCount: 100 + index })), ...override };
}
function advance(lifecycle, events) { return events.reduce((value, event) => { const result = policy.transition(value, event); assert.equal(result.ok, true); return result.lifecycle; }, lifecycle); }

test('scope is strictly isolated by store, financial year and exact period', () => {
  const one = policy.create(scope, 'gen:001');
  const two = policy.create({ ...scope, storeCode: 'HEMW' }, 'gen:001');
  assert.equal(one.ok, true); assert.equal(two.ok, true); assert.notEqual(one.lifecycle.scopeKey, two.lifecycle.scopeKey);
  assert.equal(policy.create({ ...scope, financialYear: '2025-26' }, 'gen:001').ok, false);
  assert.equal(policy.create({ ...scope, periodEnd: '2024-09-15' }, 'gen:001').ok, false);
});

test('lifecycle permits only the ordered no-write staging path', () => {
  let value = policy.create(scope, 'gen:001').lifecycle;
  assert.equal(policy.transition(value, 'BEGIN_STAGING').code, 'TRANSITION_FORBIDDEN');
  value = advance(value, ['PREFLIGHT_PASS', 'PARSE_PASS', 'POLICY_PASS', 'BEGIN_STAGING', 'STAGE_COMPLETE']);
  assert.equal(value.state, 'STAGED'); assert.equal(value.activeGenerationId, null); assert.equal(policy.verifiedReadable(value), false);
});

test('manifest requires exactly four bounded retail reports bound to scope and generation', () => {
  let value = advance(policy.create(scope, 'gen:001').lifecycle, ['PREFLIGHT_PASS', 'PARSE_PASS', 'POLICY_PASS', 'BEGIN_STAGING', 'STAGE_COMPLETE']);
  assert.equal(policy.attachManifest(value, manifest('other')).ok, false);
  assert.equal(policy.attachManifest(value, manifest('gen:001', { reports: manifest().reports.slice(0, 3) })).ok, false);
  const attached = policy.attachManifest(value, manifest()); assert.equal(attached.ok, true); assert.equal(attached.lifecycle.manifest.reports.length, 4);
  assert.equal(attached.lifecycle.manifest.authority.evidenceIdentity,authorityBinding.evidenceIdentity);
});

test('atomic publication changes neither active generation nor input before confirmation', () => {
  let value = advance(policy.create(scope, 'gen:001').lifecycle, ['PREFLIGHT_PASS', 'PARSE_PASS', 'POLICY_PASS', 'BEGIN_STAGING', 'STAGE_COMPLETE']);
  value = policy.attachManifest(value, manifest()).lifecycle;
  const before = structuredClone(value);
  assert.equal(policy.publish(value).code, 'PUBLISH_STATE_FORBIDDEN'); assert.deepEqual(value, before);
  value = advance(value, ['RECONCILE_PASS', 'REQUEST_CONFIRMATION']);
  const accepted = policy.publish(value);
  assert.equal(accepted.ok, true); assert.equal(accepted.changed, true);
  assert.equal(accepted.lifecycle.activeGenerationId, 'gen:001'); assert.equal(accepted.lifecycle.previousGenerationId, null);
  assert.equal(policy.verifiedReadable(accepted.lifecycle), true); assert.equal(value.activeGenerationId, null);
});

test('publish preserves the previous accepted generation as one coherent result', () => {
  let value = advance(policy.create(scope, 'gen:001').lifecycle, ['PREFLIGHT_PASS', 'PARSE_PASS', 'POLICY_PASS', 'BEGIN_STAGING', 'STAGE_COMPLETE']);
  value = policy.attachManifest(value, manifest('gen:001')).lifecycle;
  value = policy.publish(advance(value, ['RECONCILE_PASS', 'REQUEST_CONFIRMATION'])).lifecycle;
  value = policy.beginRestatement(value, 'gen:002').lifecycle;
  value = advance(value, ['PREFLIGHT_PASS', 'PARSE_PASS', 'POLICY_PASS', 'BEGIN_STAGING', 'STAGE_COMPLETE']);
  const changedReports = manifest('gen:002').reports.map((entry, index) => index ? entry : { ...entry, sourceSha256: 'a'.repeat(64) });
  value = policy.attachManifest(value, manifest('gen:002', { reports: changedReports })).lifecycle;
  value = advance(value, ['RECONCILE_PASS', 'REQUEST_CONFIRMATION']);
  const accepted = policy.publish(value).lifecycle;
  assert.equal(accepted.activeGenerationId, 'gen:002'); assert.equal(accepted.previousGenerationId, 'gen:001');
});

test('deterministic source-hash identity makes an unchanged reimport a no-op despite a new generation id', () => {
  let value = advance(policy.create(scope, 'gen:001').lifecycle, ['PREFLIGHT_PASS', 'PARSE_PASS', 'POLICY_PASS', 'BEGIN_STAGING', 'STAGE_COMPLETE']);
  value = policy.attachManifest(value, manifest('gen:001')).lifecycle;
  value = policy.publish(advance(value, ['RECONCILE_PASS', 'REQUEST_CONFIRMATION'])).lifecycle;
  const activeIdentity = value.activeManifestIdentity;
  value = policy.beginRestatement(value, 'gen:002').lifecycle;
  value = advance(value, ['PREFLIGHT_PASS', 'PARSE_PASS', 'POLICY_PASS', 'BEGIN_STAGING', 'STAGE_COMPLETE']);
  value = policy.attachManifest(value, manifest('gen:002')).lifecycle;
  const duplicate = policy.publish(advance(value, ['RECONCILE_PASS', 'REQUEST_CONFIRMATION']));
  assert.equal(duplicate.changed, false); assert.equal(duplicate.lifecycle.state, 'DUPLICATE_NOOP');
  assert.equal(duplicate.lifecycle.activeGenerationId, 'gen:001'); assert.equal(duplicate.lifecycle.activeManifestIdentity, activeIdentity);
});

test('authority and evidence identity participate in manifest identity',()=>{const one=manifest('gen:001'),changed=manifest('gen:002',{authority:{...authorityBinding,evidenceIdentity:'WLMHW_PROFILE_EVIDENCE_REBOUND_V2'}});assert.notEqual(policy.manifestIdentity(one),policy.manifestIdentity(changed));});

test('tender dictionary identity participates in replay identity and cannot cross versions',()=>{const one=manifest('gen:001'),changed=manifest('gen:002',{tenderDictionary:{...tenderIdentity,versionId:'retail-etp-tender-v2'}});assert.notEqual(policy.manifestIdentity(one),policy.manifestIdentity(changed));const life=advance(policy.create(scope,'gen:002').lifecycle,['PREFLIGHT_PASS','PARSE_PASS','POLICY_PASS','BEGIN_STAGING','STAGE_COMPLETE']);assert.equal(policy.attachManifest(life,changed).ok,false);});

test('HEMW cannot forge a production authority binding while evidence is pending',()=>{const heScope={...scope,storeCode:'HEMW'},life=advance(policy.create(heScope,'gen:001').lifecycle,['PREFLIGHT_PASS','PARSE_PASS','POLICY_PASS','BEGIN_STAGING','STAGE_COMPLETE']),forged=manifest('gen:001',{scopeKey:'HEMW|2024-25|2024-09-16..2025-03-31',authority:{...authorityBinding,storeCode:'HEMW'}});assert.equal(policy.attachManifest(life,forged).ok,false);});

test('every transition and publication rejects forged lifecycle invariants', () => {
  const valid = policy.create(scope, 'gen:001').lifecycle;
  for (const forged of [
    { ...valid, storeKind: 'OPERATIONAL_STORE' },
    { ...valid, factsPortable: true },
    { ...valid, scopeKey: 'HEMW|2024-25|2024-09-16..2025-03-31' },
    { ...valid, candidateGenerationId: '../unsafe' }
  ]) {
    assert.equal(policy.validateLifecycle(forged).ok, false);
    assert.equal(policy.transition(forged, 'PREFLIGHT_PASS').ok, false);
    assert.equal(policy.publish({ ...forged, state: 'AWAITING_CONFIRMATION' }).ok, false);
  }
});

test('restore is metadata-only, excludes facts and blocks verified reads until reimport publication', () => {
  const fenced = policy.fenceAfterRestore(scope, 'control:period:001');
  assert.equal(fenced.ok, true); assert.equal(fenced.lifecycle.factsPortable, false);
  assert.equal(fenced.lifecycle.state, 'REIMPORT_REQUIRED'); assert.equal(fenced.lifecycle.activeGenerationId, null);
  assert.equal(policy.verifiedReadable(fenced.lifecycle), false);
  assert.equal(policy.transition(fenced.lifecycle, 'BEGIN_REIMPORT').code, 'GENERATION_ID_INVALID');
  const restarted = policy.transition(fenced.lifecycle, { type: 'BEGIN_REIMPORT', generationId: 'gen:restored:001' });
  assert.equal(restarted.ok, true); assert.equal(restarted.lifecycle.state, 'SELECTED'); assert.equal(restarted.lifecycle.restoreFence, true);
  assert.equal(restarted.lifecycle.candidateGenerationId, 'gen:restored:001');
});

test('terminal failure and rejection states cannot accidentally publish', () => {
  const created = policy.create(scope, 'gen:001').lifecycle;
  for (const event of ['REJECT', 'CANCEL']) {
    const terminal = policy.transition(created, event).lifecycle;
    assert.equal(policy.publish(terminal).ok, false); assert.equal(policy.verifiedReadable(terminal), false);
  }
});

test('lifecycle snapshots contain only bounded metadata and no source rows or file labels', () => {
  const text = JSON.stringify(policy.fenceAfterRestore(scope, 'control:period:001').lifecycle);
  assert.doesNotMatch(text, /rowData|workbook|fileLabel|customer|mobile|phone/i);
});
