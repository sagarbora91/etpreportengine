import crypto from 'node:crypto';
import intake from '../../www/etp-e7-authority-intake.js';

export const scopeKey='SERVICE_ETP_V1|SC01|2026-27|2026-08-01..2026-08-31';
export const sha256=value=>crypto.createHash('sha256').update(value,'utf8').digest('hex');
export const api=intake.create({sha256});
const h=value=>sha256(value);
const payloads={
  SERVICE_REPORT_IDENTITY_SET:{authorityId:'SERVICE-REPORTS-2026-V1',identities:[
    {reportType:'S003_REVENUE',required:true,headerSignatureSha256:h('S003 exact headers'),sampleExportSha256:h('S003 sample'),sourceFileLabel:'S003 controlled sample.xlsx'},
    {reportType:'S004_TENDER_DETAILED',required:true,headerSignatureSha256:h('S004 exact headers'),sampleExportSha256:h('S004 sample'),sourceFileLabel:'S004 controlled sample.xlsx'},
    {reportType:'PENDING_SNAPSHOT',required:false,headerSignatureSha256:h('Pending exact headers'),sampleExportSha256:h('Pending sample'),sourceFileLabel:'Pending controlled sample.xlsx'}
  ]},
  SERVICE_JOB_STATUS_DICTIONARY:{authorityId:'STATUS-2026-V1',version:1,entries:[{sourceValue:'Delivered',canonicalValue:'DELIVERED'},{sourceValue:'Pending',canonicalValue:'PENDING'}]},
  SERVICE_TRANSACTION_DICTIONARY:{authorityId:'TXN-2026-V1',version:1,entries:[{sourceValue:'Service Revenue',canonicalValue:'SERVICE_REVENUE'},{sourceValue:'Part Revenue',canonicalValue:'PART_REVENUE'}]},
  SERVICE_PAYMENT_DICTIONARY:{authorityId:'PAY-2026-V1',version:1,entries:[{sourceValue:'Cash',canonicalValue:'CASH'},{sourceValue:'UPI',canonicalValue:'DIGITAL'}]},
  SERVICE_SKU_TOKEN_DICTIONARY:{authorityId:'SKU-2026-V1',version:1,entries:[{sourceValue:'PART-001',canonicalValue:'REAL_SKU'},{sourceValue:'LABOUR',canonicalValue:'NON_STOCK_SERVICE_TOKEN'}]},
  SERVICE_CUSTODY_CONSENT_RETENTION_AUTHORITY:{authorityId:'SERVICE-POLICY-2026-V1',effectiveFrom:'2026-04-01',effectiveTo:'2027-03-31',deliveredStageMeaning:'APPROVED_DELIVERED_STAGE_V1',sparsePeriodPolicy:'APPROVED_SPARSE_PERIOD_V1',purchaseScope:'APPROVED_PURCHASE_SCOPE_V1',custodyCompletenessScope:'APPROVED_CUSTODY_SCOPE_V1',custodyRule:'APPROVED_CUSTODY_RULE_V1',consentRule:'APPROVED_CONSENT_RULE_V1',retentionRule:'APPROVED_RETENTION_RULE_V1',privacyProjection:'EXACT_PII_FREE_FIELDS_ONLY'}
};
export const candidate=type=>({contractVersion:intake.VERSION,sourceType:type,scopeKey,payload:structuredClone(payloads[type])});
export function approval(type,c=candidate(type)){return{sourceType:type,status:'APPROVED',sourceSha256:api.prepareCandidate(c).sourceSha256,approvalId:'OWNER-'+type.slice(0,30)+'-01',approvedAt:'2026-08-25T09:00:00Z',approvedByRole:'Owner',approvedBy:'OWNER-1',serviceUnitId:'SC01',scopeKey};}
export function approvedPackage(){return Object.fromEntries(intake.SOURCE_TYPES.map(type=>{const c=candidate(type);return[type,api.validateApprovedCandidate(c,approval(type,c))];}));}
