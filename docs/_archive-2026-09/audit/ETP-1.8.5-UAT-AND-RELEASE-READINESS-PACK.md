# ETP Reporting Engine 1.8.5 — UAT and release-readiness pack

Status: **PRE-CANDIDATE; AUTOMATED BASELINE PASSED; EXTERNAL ACCEPTANCE NOT RUN**

This is the active external-acceptance procedure for 1.8.5. It replaces no historical evidence and does not authorize a build, signature, push, tag or release. The rejected 1.8.4 pack remains forensic history and must never be copied forward as a pass.

## 1. Entry criteria

Run the function-first audit, then prepare an isolated execution workspace:

```powershell
./scripts/Invoke-EtpFunctionAudit.ps1
./scripts/New-EtpExternalAcceptanceWorkspace.ps1
```

Preparation fails unless the automated audit is `PASS` with zero uncovered active functions. The generated workspace records the current source commit, automated result, synthetic control oracle, full function register and SHA-256 manifest. It deliberately records the candidate as `NOT_BUILT` until a clean artifact is produced and hash-bound.

## 2. Result vocabulary

Use only `PASS`, `FAIL`, `BLOCKED`, `NOT RUN`, `NOT APPLICABLE` or `DEFERRED`. A pass requires a privacy-safe evidence reference. Missing evidence is `NOT RUN`, never `PASS`. An expected denial is a pass only when the action is denied clearly, no state changes, and the denial is auditable where required.

## 3. Function-completeness rule

The generated `ETP-1.8.5-FUNCTION-EXECUTION-REGISTER.csv` contains every authoritative module, navigation function, report, route, import profile, startup mode and public application operation. Every active row must receive an external disposition or an approved rationale explaining why automated evidence is sufficient. Deferred/unavailable rows must retain their runtime reason and must not be presented as delivered functionality.

The curated role register adds realistic end-to-end journeys. Both registers are required: curated journeys do not replace exhaustive inventory, and exhaustive inventory does not replace human workflow observation.

## 4. Demo-data oracle

Use only the generated synthetic workbooks. Do not paste production rows, credentials, customer data or private paths into evidence.

| Control | Expected value |
|---|---:|
| Workbooks | 12 |
| Stores | WLMHW and HEMW |
| Canonical R025 rows | 6 |
| Invoice controls | 6 |
| Canonical net sales | 4,600.00 |
| Signed units | 4.00 |
| Eligible tender total | 4,600.00 |
| Quarantined tender rows | 2 |
| Stock movement rows | 4 |
| Closing-stock rows | 4 |
| Closing-stock quantity | 60.00 |
| R003 enrichment rows | 6 |
| R013 enrichment rows | 6 |

Returns remain negative. `R025.NETVALUE` remains canonical. `PAYMENTTYPE25` remains quarantined and excluded from the eligible tender total. Missing values must never be changed to zero to force reconciliation.

## 5. Role execution

Execute the role register using separate target-PC Windows identities.

- **Owner:** all viewer and operational functions, administration, user/master changes, approvals, controlled reopen/restatement, mappings, automation settings and recovery operations.
- **Store Manager:** permitted viewing, imports, daily inputs, stock counts, targets, registers and report generation; Owner-only administration and approval paths must be denied.
- **Viewer:** permitted navigation, reports, investigation, archive viewing and exports; imports, mutations, approvals, configuration and operational administration must be denied.

Test allowed and denied paths. A hidden button alone is not authorization evidence: attempt a direct or alternate entry path where the checklist requests it.

## 6. Installed-environment matrix

Record separate evidence for:

1. clean supported x64 Windows with SQL Server absent and internet available;
2. clean supported x64 Windows with supported SQL Server already installed;
3. offline/no-internet installation using the approved offline bundle;
4. controlled prerequisite failure and safe rollback;
5. same-version repair;
6. supported prior-version upgrade with preserved data;
7. uninstall with database and backups preserved;
8. reboot with SQL Express automatic startup, scheduled tasks and application reconnection.

The candidate executable, installer and offline bundle must be SHA-256-bound before the first result is recorded. A rebuild invalidates artifact-specific evidence.

## 7. Accessibility and hardware

Follow `docs/19_ACCESSIBILITY_AUDIT.md`. Observe keyboard-only operation, visible focus, Narrator, UI Automation names, 100/125/150/200% scaling, 200% text, high contrast, touch, actual Microsoft Excel, PDF viewing and a physical printer. Any issue preventing task completion or interpretation of a critical status blocks release.

## 8. Operations and integrations

Observe checksum backup, `RESTORE VERIFYONLY`, isolated restore, `DBCC CHECKDB`, lineage/control reconciliation, backup capacity warnings, daily backup scheduling, monthly drill scheduling and post-reboot operation. Test installed email/WhatsApp preparation without claiming delivery unless delivery evidence exists. Inspect the support package for privacy safety.

## 9. Defect policy

| Severity | Definition | Release treatment |
|---|---|---|
| S1 Critical | Data loss/corruption, privacy exposure, security bypass, incorrect financial control or unrecoverable installation | Immediate stop; release prohibited |
| S2 High | Core workflow unavailable, role bypass, material report/export error, inaccessible critical workflow | Release prohibited |
| S3 Medium | Workaround exists; no incorrect stored data/control result | Owner disposition required |
| S4 Low | Cosmetic, wording or minor usability issue | May defer with named owner and date |

Every fix invalidates affected evidence. Link the defect, fix commit, rerun result and new artifact hash when applicable.

## 10. Final gate

Release readiness requires all of the following:

- clean, frozen source commit and internally consistent version metadata;
- automated audit `PASS`, zero uncovered active functions and no unexplained skipped tests;
- built artifact, installer, offline package, checksums, SBOM and provenance bound to the same commit;
- clean-PC installer matrix passed;
- three-role UAT and full function-register disposition completed;
- accessibility, Excel, PDF, printer and integration checks completed;
- live operational recovery drill passed;
- signing identity/certificate supplied and Authenticode verified, or an explicit Owner decision that release remains blocked;
- all S1/S2 defects closed and S3/S4 defects formally dispositioned;
- business owner, operations owner and release approver sign-off;
- explicit authorization before push, tag and release promotion.

Until then the dashboard must remain `BLOCKED_PENDING_EXTERNAL_ACCEPTANCE`.
