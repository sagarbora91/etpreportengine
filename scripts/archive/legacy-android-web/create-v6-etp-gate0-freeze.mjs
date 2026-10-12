#!/usr/bin/env node
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
export const RECEIPT = 'verification/audit/V6-ETP-GATE-0-AUTHORITY-FREEZE-2026-08-25.json';
const sha256 = value => crypto.createHash('sha256').update(value).digest('hex');
const freeze = value => {
  if (value && typeof value === 'object' && !Object.isFrozen(value)) {
    Object.values(value).forEach(freeze);
    Object.freeze(value);
  }
  return value;
};
const source = relativePath => {
  const bytes = fs.readFileSync(path.join(root, relativePath));
  return { path: relativePath.replaceAll('\\', '/'), bytes: bytes.length, sha256: sha256(bytes) };
};
const pendingSource = (sourceType, requiredFields) => ({
  sourceType,
  status: 'SOURCE_REQUIRED',
  requiredFields,
  sourceSha256: null,
  approval: null
});

export function createV6EtpGate0Freeze() {
  return freeze({
    schemaVersion: 1,
    gateId: 'v6-etp-gate0-authority-freeze-2026-08-25-v1',
    freezeStatus: 'FROZEN_FAIL_CLOSED',
    formalBusinessApproval: false,
    productActivationAllowed: false,
    programmeOrder: ['E3', 'E4', 'E6', 'E5', 'E7'],
    invariantAuthority: {
      sourceFacts: 'IMMUTABLE_VERIFIED_ETP_ONLY',
      declarationsAsPaymentBasis: 'FORBIDDEN',
      crossStoreBorrowing: 'FORBIDDEN',
      unknownDictionaryValue: 'QUARANTINE_OR_UNMAPPED_NEVER_GUESSED',
      operationalState: 'DURABLE_NON_FACT_STATE',
      portableBackup: 'OPERATIONAL_STATE_INCLUDED_SEALED_FACTS_EXCLUDED',
      restoreBehavior: 'PRESERVE_HUMAN_ACTIONS_FENCE_VERIFIED_READS_UNTIL_REIMPORT',
      privilegeBoundary: 'OWNER_SESSION_AND_FRESH_REAUTH_NOT_ROLE_SELECTOR',
      moneySequence: 'E5_AFTER_E3_E4_E6_ACCEPTANCE',
      serviceIsolation: 'E7_SEPARATE_SERVICE_ETP_V1_BOUNDARY'
    },
    currentRetailAuthority: {
      WLMHW: { profileStatus: 'PRODUCTION_AUTHORIZED', operationalActivation: 'PENDING_GATE0_OWNER_APPROVAL' },
      HEMW: { profileStatus: 'EVIDENCE_PENDING', operationalActivation: 'BLOCKED_PROFILE_EVIDENCE_REQUIRED' }
    },
    contractBaselines: {
      OPERATIONAL_FOUNDATION: source('www/etp-operational-foundation.js'),
      OPERATIONAL_STORE: source('www/etp-operational-store.js'),
      OPERATIONAL_ADAPTERS: source('www/etp-operational-adapters.js'),
      OPERATIONAL_RUNTIME: source('www/etp-operational-runtime.js'),
      E4_AUTHORITY_INTAKE: source('www/etp-e4-authority-intake.js'),
      E6_AUTHORITY_INTAKE: source('www/etp-e6-authority-intake.js'),
      E5_AUTHORITY_INTAKE: source('www/etp-e5-authority-intake.js'),
      E7_AUTHORITY_INTAKE: source('www/etp-e7-authority-intake.js'),
      E3_OWNER_AUTHORITY: source('docs/audit/V6-ETP-E3-OWNER-AUTHORITY-2026-08-25.md'),
      E3: source('www/etp-cro-reconciliation.js'),
      E3_ORCHESTRATOR: source('www/etp-e3-orchestrator.js'),
      E3_PRESENTATION: source('www/etp-e3-presentation.js'),
      E4: source('www/etp-target-planning.js'),
      E4_ORCHESTRATOR: source('www/etp-e4-orchestrator.js'),
      E4_PRESENTATION: source('www/etp-e4-presentation.js'),
      OPERATIONAL_GATEWAY: source('www/etp-operational-gateway.js'),
      OPERATIONAL_MOUNT: source('www/etp-operational-mount.js'),
      E3_VERIFIED_JOIN: source('www/etp-e3-verified-join.js'),
      OPERATIONAL_BOOTSTRAP: source('www/etp-operational-bootstrap.js'),
      OPERATIONAL_SHELL_COMPOSER: source('www/etp-operational-shell-composer.js'),
      OPERATIONAL_MODULE_HOST: source('www/etp-operational-module-host.js'),
      OPERATIONAL_FRAME_BRIDGE: source('www/etp-operational-frame-bridge.js'),
      E6: source('www/etp-exception-monitor.js'),
      E6_PRESENTATION: source('www/etp-e6-presentation.js'),
      E5: source('www/etp-incentive-control.js'),
      E5_PRESENTATION: source('www/etp-e5-presentation.js'),
      E5_PAYROLL_BRIDGE: source('www/etp-e5-payroll-bridge.js'),
      E7_OPERATIONAL_SERVICE_ONLY: source('www/service-workboard-policy.js')
      ,E7_SERVICE_VERIFIER: source('www/etp-e7-service-verifier.js')
      ,E7_SERVICE_OPERATIONAL: source('www/etp-e7-service-operational.js')
      ,E7_PRESENTATION: source('www/etp-e7-presentation.js')
      ,E7_MODULE_HOST: source('www/etp-e7-module-host.js')
    },
    capabilities: {
      E3: {
        activationStatus: 'OWNER_POLICY_APPROVED_ENGINEERING_ACTIVE',
        authority: {
          approvalId: 'E3-OWNER-2026-08-25-V1',
          approvedBy: 'Sagar',
          approvedByRole: 'Owner',
          approvedAt: '2026-08-25',
          policyVersion: 'ETP_E3_OWNER_POLICY_2026_08_25_V1',
          source: source('docs/audit/V6-ETP-E3-OWNER-AUTHORITY-2026-08-25.md')
        },
        rolePolicy: {
          declarationRoles: ['STAFF', 'STORE_MANAGER', 'OWNER'],
          checkerCloseImportRoles: ['STORE_MANAGER', 'OWNER'],
          reconciliationRoles: ['STORE_MANAGER', 'OWNER'],
          correctionWindowHours: 24,
          correctionWindowRoles: ['STORE_MANAGER', 'OWNER'],
          postWindowCorrectionRoles: ['OWNER'],
          varianceDispositionRoles: ['STORE_MANAGER', 'OWNER'],
          lockRoles: ['STORE_MANAGER', 'OWNER'],
          ownerCorrectionBoundary: 'ANY_TIME_BEFORE_LOCK',
          lockedChangePath: 'VERIFIED_SOURCE_RESTATEMENT_NEW_RECONCILIATION'
        },
        stateMachine: ['OPEN', 'CLOSED', 'IMPORTED', 'RECONCILED', 'VARIANCE', 'LOCKED'],
        outcomes: ['MATCHED', 'MISATTRIBUTED', 'UNCLAIMED', 'PHANTOM'],
        correctionReasonCodes: ['MISATTRIBUTED_CRO', 'UNCLAIMED_INVOICE', 'PHANTOM_DECLARATION', 'SOURCE_RESTATEMENT', 'OTHER_REVIEWED'],
        dispositionReasonCodes: ['CORRECTED_ATTRIBUTION', 'ACCEPTED_UNASSIGNED', 'DECLARATION_WITHDRAWN', 'SOURCE_REIMPORT_REQUIRED', 'ESCALATED_OWNER'],
        pendingDecisions: []
      },
      E4: {
        activationStatus: 'BLOCKED_TARGET_AUTHORITY_REQUIRED',
        versionRules: ['VERSION_1_IMMUTABLE', 'REVISION_REQUIRES_N_PLUS_1', 'DAY_0_ALLOCATION_LOCK', 'LEAVE_CREATES_NEW_VERSION', 'ACHIEVEMENT_ETP_ONLY'],
        adjustmentReasonCodes: ['TARGET_SOURCE_REVISION', 'FESTIVE_CALENDAR_OVERRIDE', 'APPROVED_LEAVE_PRORATION', 'COVERAGE_SHORTFALL', 'OTHER_OWNER_APPROVED'],
        requiredSources: {
          targetAuthority: pendingSource('TITAN_STORE_TARGET', ['storeCode', 'period', 'targetAmount', 'receivedAt', 'sourceReference']),
          festiveCalendar: pendingSource('FESTIVE_DAY_WEIGHT_OVERRIDE', ['storeCode', 'date', 'weight', 'reason']),
          croIdentityMap: pendingSource('CRO_IDENTITY_MAP', ['storeCode', 'croId', 'employeeId', 'effectiveFrom'])
        },
        pendingDecisions: ['STRETCH_POLICY', 'LY_WEIGHT_POLICY', 'LEAVE_PRORATION_FORMULA', 'COVERAGE_SHORTFALL_TREATMENT']
      },
      E6: {
        activationStatus: 'PENDING_OWNER_APPROVAL',
        statuses: ['OPEN', 'ACKNOWLEDGED', 'CLOSED'],
        exceptionTypes: ['LATE_ATTRIBUTION', 'UNASSIGNED_TREND', 'NEAR_TARGET_FINAL_WEEK', 'FINAL_48H_CONCENTRATION', 'EARLY_NEXT_MONTH_MOVEMENT', 'DECLARED_ACTUAL_VARIANCE', 'RESTATED_PERIOD'],
        closureReasonCodes: ['CORRECTED_AND_VERIFIED', 'ACCEPTED_WITH_EVIDENCE', 'SOURCE_REIMPORT_COMPLETED', 'FALSE_POSITIVE_CONFIRMED', 'SUPERSEDED_BY_RESTATEMENT', 'OTHER_OWNER_APPROVED'],
        thresholdAuthority: { status: 'CANDIDATE_REQUIRES_OWNER_APPROVAL', baselineVersion: 'e6-thresholds-2026-08-24-v1' },
        pendingDecisions: ['SLA_BY_TYPE', 'DEFAULT_OWNER_BY_TYPE', 'ACKNOWLEDGE_AUTHORITY', 'REASSIGN_AUTHORITY', 'CLOSE_AUTHORITY']
      },
      E5: {
        activationStatus: 'BLOCKED_SCHEME_AND_MAPPING_REQUIRED',
        schemeLifecycle: ['DRAFT', 'APPROVED_ACTIVE', 'SUPERSEDED'],
        runLifecycle: ['BLOCKED', 'ELIGIBLE', 'PROVISIONAL', 'FINALIZATION_READY', 'FINALIZED', 'PAYROLL_PENDING', 'PAYROLL_ATTACHED', 'PAYROLL_LOCKED'],
        clawbackLifecycle: ['PENDING_PAYROLL', 'ATTACHED', 'APPLIED', 'LOCKED'],
        requiredSources: {
          incentiveScheme: pendingSource('INCENTIVE_SCHEME', ['effectiveFrom', 'effectiveTo', 'basis', 'bands', 'rounding', 'eligibility', 'restatementPolicy']),
          croPayrollMap: pendingSource('CRO_PAYROLL_EMPLOYEE_MAP', ['storeCode', 'croId', 'employeeId', 'effectiveFrom'])
        },
        pendingDecisions: ['UNASSIGNED_TREATMENT', 'CLOSE_PLUS_15_POLICY', 'CLAWBACK_PERIOD_POLICY', 'PAYROLL_PRELOCK_POLICY']
      },
      E7: {
        activationStatus: 'DEFERRED_SOURCE_AND_PRIVACY_AUTHORITY_REQUIRED',
        boundary: 'SERVICE_ETP_V1_SEPARATE_FROM_RETAIL_ETP_V1',
        mandatoryReports: ['S003_REVENUE', 'S004_TENDER_DETAILED'],
        optionalReports: ['REPAIR_SNAPSHOT', 'TAT_SNAPSHOT', 'PENDING_SNAPSHOT', 'PURCHASE_CREATED_RECEIVED'],
        requiredSources: {
          representativeExports: pendingSource('SERVICE_ETP_EXPORT_SET', ['reportType', 'storeCode', 'period', 'headerSignature']),
          jobStatusDictionary: pendingSource('SERVICE_JOB_STATUS_DICTIONARY', ['sourceValue', 'canonicalStatus']),
          transactionDictionary: pendingSource('SERVICE_TRANSACTION_DICTIONARY', ['sourceValue', 'canonicalType']),
          paymentDictionary: pendingSource('SERVICE_PAYMENT_DICTIONARY', ['sourceValue', 'canonicalTender']),
          skuDictionary: pendingSource('SERVICE_SKU_TOKEN_DICTIONARY', ['sourceValue', 'classification']),
          privacyAuthority: pendingSource('SERVICE_CUSTODY_CONSENT_RETENTION_AUTHORITY', ['custodyRule', 'consentRule', 'retentionRule', 'approvedBy'])
        },
        matchingAuthority: 'EXACT_APPROVED_JOB_KEY_ONLY_NO_PII_FUZZY_MATCH',
        pendingDecisions: ['DELIVERED_STAGE_MEANING', 'SPARSE_PERIOD_POLICY', 'PURCHASE_SCOPE', 'CUSTODY_COMPLETENESS_SCOPE']
      }
    },
    catalogueStatus: 'FROZEN_CANDIDATE_NOT_ACTIVE',
    activationRule: 'EVERY_CAPABILITY_REQUIRES_EXPLICIT_OWNER_APPROVAL_AND_ALL_REQUIRED_SOURCE_HASHES'
  });
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const value = `${JSON.stringify(createV6EtpGate0Freeze(), null, 2)}\n`;
  if (process.argv.includes('--write')) {
    fs.writeFileSync(path.join(root, RECEIPT), value);
    process.stdout.write(`${RECEIPT}\n`);
  } else process.stdout.write(value);
}
