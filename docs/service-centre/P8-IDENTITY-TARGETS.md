# Service Centre: P8 identity targets (not enabled)

**Date:** 3 Oct 2026. **Lane:** L1 (catalogue) of the Service interim (S-2, decision 15).
**Sources:** `SERVICE-CENTRE-IMPORT-DESIGN-REVIEW.md` section 5 (1 Oct 2026) and `SERVICE-INTERIM-DESIGN.md` sections 3 and 4 (3 Oct 2026).

This page records where each Service family's identity should go when P8 builds the Service decision engine. **None of it is enabled.** In the interim every S entry of `EtpReportFamilies.json` has the same identity:

- `PrimaryDateHeader` is null, so planner 1 lands each file as one dated snapshot.
- Identity: Scope `Snapshot`, SnapshotDate `Block`, RowRule `Multiset` (`SnapshotItems` for S006), no `DocumentKey`, no `RowKey`, ChangePolicy `LatestReadingWins`, Route `Landing`, RulesetVersion 1.

The read views in 0048 section C apply the read rules (StateSnapshot, DateLog, JobList) on the read side. P8 replaces them with ledger views.

## Why not now

Row-date families would be refused by planner 1 whenever a rolling 4-day raw window overlaps an earlier window, or a consolidated rebuild restates a money row (the IF-015/016 class). Design section 4 explains this.

## Targets per group

Common rules, already applied to the interim roles:
- `store_code` is the only Key: the exporting centre, AW330.
- Customer columns are Descriptive and never a key.
- `*timestamp*` columns are Ignored (there are none today).
- Job status and job-progress dates are Attribute.
- Money is Fact.

| Group | Families | P8 scope and key | Row rule | Change policy | Evidence and caveats |
|---|---|---|---|---|---|
| Money logs (row date) | S003, S004, S019, S023-S026, S039, S040 | `Date` on the primary date: S003 `Trans Date`, S004 `BillingDate`, S019 `RepairDate`, S023-S026 `TransDate`, S039/S040 `Transaction Date` | Multiset | Review | (`DocumentNum`, `Line Num`) repeats on S003 rows and `BillingNumber` on S004 rows, so there is no natural key until a raw-corpus test proves one. |
| Snapshots, contract rule `current` | S006, S009, S010 | `Snapshot`, dated by block (contract, then Snapshot History rows, then the folder) | SnapshotItems (S006 RowKey candidate: `store_code`, `itemid`; not proven) | LatestReadingWins, with membership for S009/S010 | Shrinking is normal for the pending lists (review H3). |
| Job lists | S002, S020, S021, S022, S029, S030, S036, S037 | Candidate `Document` keyed by job order number, with no year; fallback `Period` | Multiset (rows are repair lines or test runs) | LatestReadingWins; status and progress columns are Attribute | The job number is never blank; rows of one job differ only in line columns (review H4). S029 `CaseNo` is blank on part of the rows: keep S029 on `Period`, or a month scope from `Month`/`Year`. |
| Status views (membership) | S011, S014-S018, S031-S035 | As job lists, plus membership | Multiset | LatestReadingWins, with membership | Jobs move between views. |
| Registers and stock movements | S007, S008, S012, S013 | `Period` (S012 candidate key `SRNNo`; S007/S008 to be tested) | Multiset | Review for quantities | S007/S008 carry exact duplicate rows: keep the multiplicity. |
| TAT and productivity | S027, S028 | `Period`, from the consolidation columns or contract blocks | Multiset | LatestReadingWins | Raw header only (review M2): `SourcePeriodFrom`, `SourcePeriodTo` and `SourceFile` are `ConsolidationColumns`, read as block metadata by `SourceColumnBlockReader` in P8. Deferred in the interim. |
| Summary | S005 | Not needed (Owner), or a contract-only `Period` | — | — | Review M4. |
| Derived | S001 | Not imported (`FAMILY_DERIVED`) | — | — | Review H2. |
| Retired | S038 | Not catalogued; Not needed by its code in the name | — | — | Its header equals S011's, so a raw "SRN REPORT" file is S011. |

When P8 flips a family, it sets `PrimaryDateHeader` and `Identity`, re-runs `scripts/propose-etp-catalogue-roles.ps1 -Write`, and raises that family's `RulesetVersion`. `ServiceCatalogueTests` pins each family's fingerprint, so the change cannot be made silently.

## Interim raw name patterns

`RawNamePatterns` name a family from a raw export file name, which carries no family code. They are matched against the normalised file name, only among the families whose headers matched, after a code in the name and before the longest catalogue `Name`. The longest matching pattern wins; a tie names no family. Sagar's export spellings are kept beside the correct ones.

| Family | Patterns | Seen in the 3 Oct raw pack |
|---|---|---|
| S001 | none (derived; there is no all-status export) | — |
| S002 | JOB REPORT | yes |
| S003 | RENVENUE REPORT, REVENUE REPORT | yes |
| S004 | TENDER COLLECTION | yes |
| S005 | TENDER COLLECTIN SUMMARY, TENDER COLLECTION SUMMARY | yes |
| S006 | CLOSING STOCK | yes |
| S007 | PURCHSE REGISTER INVOICED CREATED DATE, PURCHASE REGISTER INVOICED CREATED DATE | yes |
| S008 | PURCHASE REGISTER INNVOICED RECCCIVED DATE, PURCHASE REGISTER INVOICED RECEIVED DATE | yes |
| S009 | PENDING REPORT | yes |
| S010 | PENDING DELIVERY | yes |
| S011 | SRN REPORT, SRN STATUS REPORT | no |
| S012 | SRN HISTORY | no (assumed) |
| S013 | GIT REPORT, GOODS IN TRANSIT | no (assumed) |
| S014 | R R DC | no (assumed) |
| S015 | R R INDENET RAISED, R R INDENT RAISED | yes |
| S016 | R R RA | no (assumed) |
| S017 | R R RWR | yes |
| S018 | R R DELIVERD, R R DELIVERED | yes |
| S019 | REPEAT RETURN | no (assumed) |
| S020 | REPLACEMENT REPORT | no (assumed) |
| S021 | DEPRECIATION REPORT | no (assumed) |
| S022 | EMPOWERMENT REPORT | yes |
| S023 | GPRC REPORT | no (assumed; the raw GPRC CLAIM is a new layout) |
| S024 | GPRC MB REPORT | no (assumed) |
| S025 | GPRC WDC | no (assumed) |
| S026 | GPRC WRA REPORT | no (assumed) |
| S027 | TATA REPORT, TAT REPORT | yes |
| S028 | TECHNICIAN PRODUCIVITY REPORT, TECHNICIAN PRODUCTIVITY REPORT | yes |
| S029 | DEFTRAN REPORT | yes |
| S030 | MIS REPORT | no (assumed) |
| S031 | R R PENDING DELIVERY | yes |
| S032 | R R PENDING REPAIR | yes |
| S033 | R R SRN | no (assumed) |
| S034 | R R REPAIRED | yes |
| S035 | R R SRN INV | no (assumed) |
| S036 | DELIVERY REPORT | yes |
| S037 | REPAIR REPORT | yes |
| S039 | WDC CLAIM | yes |
| S040 | WRA CLAIM | yes |

"Assumed" patterns follow the naming of the exports that were seen. They decide only between families that share a header layout, so a wrong guess leaves a file unnamed ("not guessed"); it never routes the file to a family with different headers. Correct them when a raw export of that family arrives.

## Recorded type deviations

`CanonicalField` and `DataType` are frozen to `scripts/service-centre/families.spec.json`, so these are recorded, not changed:

- **S004 and S005 `PhonePe` is `Identifier`**, while every other tender column is `Decimal`. It lands as text (`nvarchar`). The S004 money view (L4) must convert it with `TRY_CONVERT(decimal(19,4), ...)` if it adds PhonePe to the tender totals.
- Some date-like columns are `Text`: S001-view `DealerCAFDate`, S020/S021 `CancellationDate`, S022 `Cancelled date`, S029 `DEALER BOOKING DATE`, and S027 `EDD`. None is a read-rule date.
