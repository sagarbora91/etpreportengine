# Phase 5 audit remediation — 25 September 2026

Branch: `phase-5/continuous-sprint`, worktree `C:\Codex\Reporting Manger\phase5-sprint`.

This records the response to [the audit work list](PHASE-5-AUDIT-FINDINGS.md). The file contains **24 identifiers: S-01, S-02 and F-01 through F-22**. The original [Phase 5 report](PHASE-5-REPORT.md) remains historical evidence, not a current acceptance verdict.

**A5.1 and A5.2 remain UNPROVEN on the populated installation.** The automated fixture walk and duplicate-screen checks below do not establish either acceptance criterion under the three application roles and their intended Windows identities.

## Integration and execution boundaries

- Pulled with `git pull --ff-only` to `44b4934`, then read the complete work list before editing.
- Merged `origin/main` at `e2156d4` first, in `f62834c`. Preserved main's extracted `AdministrationTaskLayout` and index 14 in every arm; F-03 subsequently adds index 12. Kept main's property-watcher tests and the ImportAudit connection validation.
- Main contributed three additional Desktop tests. The clean post-merge baseline was **1,006 passed / 0 failed / 4 skipped**, rather than the pre-merge 1,003 / 0 / 4. Release build: 0 warnings, 0 errors.
- The first short-path attempt used a junction, `C:\p5`; the existing import confinement check correctly rejected that linked ancestor. Tests subsequently ran through the short substituted drive `P:\`, which is not a filesystem reparse point. One pre-fix baseline rerun encountered a SQL Shared Memory transport failure in `PhaseFiveStoreCatalogTests.Latest_combined_day_requires_every_active_store_and_ignores_newer_partial_or_inactive_sources(activeCount: 1)` during fixture bootstrap. The next complete run produced the clean baseline above; the transient failure is not omitted or attributed to a proven cause.
- Four older upgrade fixtures were renamed to the authorized `EtpPhase0Test_*` namespace in `7a305f6`, before baseline execution. All SQL work used disposable fixtures. The live `EtpReporting` database was not migrated or used for this work.
- No existing migration changed. The only schema addition is `database/migrations/0037_accounting_invariants.sql`. Both plain-language preflights run before either index, and upgrade tests check that refused upgrades preserve the complete batch/receipt history and migration journal.
- Changes followed audit section 9. In particular, the ungated, complete role walk ran red **before** the five screen fixes. Its initial walk visited Owner 90, Store Manager 70 and Viewer 55 destinations. After removal of the redundant trend destination, the recorded green walk visits **89 / 69 / 54** respectively, all 212 available role-destination visits (89 distinct Owner destinations).

## Finding-by-finding proof

“Red” means an observed failing run before the corresponding product fix. For an existing-correct behavior with a missing test (F-13 and parts of F-21), it means a stated temporary mutation that the new assertion caught. Those mutations were removed before the final green run and were not committed. Related test filters overlap; do not sum their counts.

| Finding | Change and regression proof | Observed result / commit |
| --- | --- | --- |
| S-01 | Persistence independently requires the exact approved, unconsumed restatement binding before replacing facts. Request/preflight is separated from persistence; desktop restatement/retry rechecks current import access. `CrossPhaseStoreManagerImportTests.Manager_restatement_without_approval_is_denied_before_canonical_facts_change` asserts `UnauthorizedAccessException`, unchanged canonical rows and no supersession. `Manager_restatement_requires_every_approved_binding_and_preserves_request_and_retry_workflow` covers binding/state mismatches and approved retry. | Red: 1 failure, previously `ImportSourceException`. Green: 16 SQL integration, 13 coordinator and 252 adapter tests. `4c7f1e7`. See the Owner-only guard correction below. |
| S-02 | Canonical attachment containment, file/ancestor reparse rejection and canonical transport path. `ReportEmailServiceTests` covers an outside file, sibling-prefix path, parent traversal, junction and canonical path; rejected attachments never call transport. | Red: 5 failed / 12 passed. Green: 17 focused and 257 total adapter tests. `8d84850`. |
| F-01 | Cash fields remain attached to the manual task body, outside the action strip. `PhaseFiveAuditLayoutTests.Cash_entry_task_keeps_the_cash_book_fields_in_its_focused_tree`; the role walk also checks populated cash tiles. | Failed before; passed after. Shared screen red: 11 Desktop failures and the complete role walk. Shared green: 22 Desktop tests and the role walk. `4ff12d5`. |
| F-02 | Moved `RefreshApprovalsButton` into `ApprovalActions`. `Investigation_and_approvals_keep_the_refresh_approvals_action_on_approvals_only` checks navigation in both directions. | Failed before; passed in the same screen green set. `4ff12d5`. |
| F-03 | Added index 12 in every extracted administration layout while retaining main's index 14. `Administration_tasks_keep_the_status_line_and_health_heading` checks the original heading object and status; main's layout test is extended. | Failed before; passed in the same screen green set. `4ff12d5`. |
| F-04 | Corrected the real maintenance title keys and included heading 27. `Maintenance_tasks_use_their_navigation_title_to_show_guidance_and_a_heading` covers all three actual navigation titles. | Failed before; passed in the same screen green set. `4ff12d5`. |
| F-05 | Automatic import includes refresh action 0. `Automatic_import_keeps_a_refresh_action_so_a_failed_load_can_be_retried` verifies the attached, enabled action. | Failed before; passed in the same screen green set. `4ff12d5`. |
| F-06 | Removed `trends`; retained `report-management-trend`, moved the daily sales bars into its Summary, preserved original detail/export rows. `Management_trend_report_keeps_the_daily_sales_bars_in_its_summary` verifies aggregation, negative sales and unchanged detail; the populated-grid fingerprint caught the original cross-workspace alias. | Red: chart assertion and actual duplicate-data fingerprint. Green: 21 Desktop and 7 integration tests; separate recorded role walk 1/0. `7a1dd01`. |
| F-07 | Removed the administration support button and handler; health has no support action. The audited Operations support destination remains. `Support_package_action_has_one_audited_destination`. | Failed before; passed in the alias green set. `7a1dd01`. |
| F-08 | Renamed the stock Help topic to `Stock Reports`. `Help_topics_render_distinct_headings_in_the_help_destination` and the real Owner destination fingerprint caught the duplicate. Structural allowances identify specific shared templates and their different query/filter purposes; populated-data aliases have no blanket allowance. | Failed before; passed in the alias green set. Fingerprint helper proofs cover property, indexed, dictionary and DataRowView values, SQL NULL and unresolved-binding refusal. `7a1dd01`. |
| F-09 | All three implicit cash defaults now apply only when exactly one active store exists: report loading, shell index selection and focused scope selection. `Cash_with_two_active_stores_requires_a_choice_before_querying` checks the prompt and zero query calls; `Cash_header_and_focused_scope_do_not_choose_between_two_stores` checks both selectors. | Red: 2 failures. Green: 26 cash/catalogue/focused-scope tests. `58bd92a`. |
| F-10 | App and invoice-reservation trigger distinguish exported final batches from rejectable unexported batches, keeping error 51452 and the actual earlier ID. `Concurrent_prepare_reserves_one_invoice_and_export_receipt_matches_bytes_and_settings` and `Invoice_reservation_refusal_names_the_earlier_batch_and_a_feasible_action` assert the exact exported sentence and retained approved guidance. | Both exported-message tests failed before. Final accounting/UI set: 17 passed / 0 failed. `7c05711`. |
| F-11 | New 0037 adds unique active store/day and one-receipt-per-batch indexes, with duplicate-history preflights 51560/51561. `Database_refuses_second_active_batch_for_an_adjustment_only_day`, `Database_refuses_a_second_export_receipt_for_one_batch`, and both `Upgrade_refuses_existing_duplicate_days_or_receipts_without_rewriting_history` cases. | All four cases failed before. Passed in the 17-test accounting/UI set; refused upgrades preserve data, journal and absence of both indexes. `7c05711`. |
| F-12 | Combined pack and DSR table titles use active catalogue display names; entry prompts and DSR Help are store-neutral. `Newly_configured_store_appears_in_dsr_and_combined_manual_totals_and_retired_masters_are_absent` asserts EAST/East branch titles and no absent seeded names. Four `Store_choice_prompt_is_valid_for_any_configured_catalogue` cases and `Dsr_help_describes_configured_stores_without_seed_store_names` cover the UI. | Red: 5 Desktop + 1 SQL failures. Green: 10 daily-workflow Desktop + 3 catalogue SQL tests. `c70e211`. |
| F-13 | `StoreLiteralGuardTests` scans runtime C#/XAML from the resolved repo root, excluding bin/obj, and reports file/line. | A temporary runtime comment with a forbidden code caused 1 failure / 1 pass and identified `PhaseFiveLiteralProbe.cs:1`. Probe removed: 2 passed / 0 failed. Source was already clean; this closes the missing automated guard. `c70e211`. |
| F-14 | Removed `TallyReference` from both batch records, SELECT/reader ordinals and mapping; the historical database column remains. `Accounting_batch_grid_contract_does_not_claim_a_Tally_voucher_identity`. | Red: 1 failure. Passed in the minor-fix green sets below. |
| F-15 | Store catalogue construction applies `LocalSqlConnectionPolicy.Validate`. `Catalogue_rejects_unsafe_connection_options_before_any_query` covers seven unsafe configurations, with an explicit local Integrated Security / Encrypt Optional positive control. | Red: 7 failures / 1 positive pass. Passed in the minor-fix green sets below. |
| F-16 | Fresh CanView checks on instance/static day reads, CanImport on save and CanAdminister on VERIFIED. Validate and canonicalize status before authorization/delegation, preventing SQL parameter truncation. `PhaseFiveDigitalRegisterBoundaryTests` covers inactive access, Viewer/Manager denial, case/space/truncation, and Viewer read → Manager draft → Owner verification → revoked-role denial. Adapter tests verify denied calls never delegate. | Red: 11 genuine behavioral failures after correcting fixture last-Owner setup. Passed in the minor-fix green sets below. |
| F-17 | Tally export checks the file and every ancestor before creating directories. New persisted and returned receipts use only the filename; old immutable receipts remain unchanged. `Tally_export_refuses_a_linked_ancestor_before_creating_directories_or_files` and `Export_receipt_persists_only_filename_and_matches_fresh_history`. | Red: both tests failed. Passed in the minor-fix green sets below. |
| F-18 | Replaced raw numeric-range forwarding with curated individual SqlError mappings. Duplicate messages retain only a positive batch ID and reviewed finality suffix; adversarial invoice text cannot supply a fake ID or exported state. `PhaseFiveFriendlyErrorAuditTests` includes an actual trigger error collection and unknown-code fallback. | Red: 11 failures / 1 unknown-code control pass. Passed in the minor-fix green sets below. |
| F-19 | The role walk is an ordinary Fact; PNG generation alone remains opt-in. `Every_role_reachable_destination_loads_with_disposable_data_and_optional_captures` ran with no capture environment variables and failed on the actual screen defects before their fixes. The same Fact runs its unchanged walk in an isolated test host, then proves another XAML view loads in the parent. | Red then green complete walk. `4ff12d5`; WPF lifecycle follow-up `a72160f`, described below. Default skips fall by one. |
| F-20 | The same walk enumerates every available, allowed `TaskNavigation.All` destination per role, awaits loads and checks real host/status/content. Screenshots use a separate bounded subset. | Initial observed 90/70/55 red then green; after alias removal 89/69/54 green. `4ff12d5`, `7a1dd01`. |
| F-21 | Exact 51452/earlier-ID assertions; exact five allowed statuses and direct invalid-status refusal; direct whitespace and NULL approval-reason rollback; approved → rejected → replacement with released reservations. | Six temporary fixture mutations each failed at the new assertion: unrelated SQL 208, wrong batch ID, widened status set, absent whitespace guard, absent NULL guard, and retained reservation. Mutations removed: accounting/UI 17 passed / 0 failed. `7c05711`. |
| F-22 | Serial SQL fixture collection scheduling, retaining protective `ClearAllPools()` calls. `SqlFixtureSchedulingTests.Fixture_collections_cannot_overlap_process_wide_pool_cleanup`. | Scheduling test: 1 failure before, 1 pass after. Six identical 19-test runs before and six after all passed. `98ebd44`; measurements below. |

F-14–F-18 were committed in `2b71f23`. Validation: **31 focused adapter tests, 37 SQL integration/accounting/register tests, 26 Desktop compatibility tests, and the complete 268-test adapter package passed**, all with zero failures. Independent fresh post-patch review found no concrete bypass or regression. S-01 and S-02 also received independent post-patch reviews without a confirmed issue.

## Documentation and navigation cleanup

Audit section 5 corrections are in the historical report and handoff: local-only evidence, dates/branch, retired request types, code-only source scan scope, optional WhatsApp recipient/history label, synthetic screenshots, manual resource hash check, plaintext/no-auth loopback coverage, and private-corpus-dependent Import counts. Historical counts were not rewritten as current evidence.

Section 6 was completed in `b02168f`. `PhaseFiveNavigationStateTests.Report_lists_reopen_the_workspace_after_help_closes_without_a_return_view` (both list routes) and `Favourites_replaces_the_help_workspace_kind_when_opened_from_help` produced **3 failures before the fix**. Both lists now restore the visible layer and task kind; Favourites cannot retain stale Help state. The related navigation, Help, administration, layout and alias filter passed **116 / 0 / 0** afterwards. Removed the unused `CanonicalId`, unreachable settings/profiles layout branches and unused Documents tab ordering; administration indices 12/14 remain present.

## Corrections and limits of the audit evidence

- **S-01 desktop wording:** an unconditional Owner-only restatement handler would break the accepted Manager request and Owner-approved retry workflow. Evidence: `PHASE-5-REPORT.md:18` records Q1; `database/migrations/0035_registers_approvals.sql:191` admits either import role, lines 200–204 bind the approved replacement, and line 215 grants the procedure to Managers and Owners. The fix restores a current import-permission check and enforces the full approved binding at persistence; it does not deny legitimate Manager requests. Existing exact pending/refused request assertions were retained at the separated preflight boundary.
- **F-21 blank reason:** whitespace reaches `database/migrations/0029_accounting_approval_reason.sql:2`'s `CK_accounting_batches_approval_reason` first (SQL 547). NULL reaches `database/migrations/0033_accounting_foundation.sql:63`'s trigger (51457). Both paths and unchanged NULL/DRAFT state are now tested. The initial expectation that whitespace always produced 51457 was refuted, rather than changing old SQL to satisfy it.
- **F-12 count:** four name-bearing strings occurred in three files, not three strings.
- **Fingerprint scope:** workspace/heading/control structure alone cannot catch the original trend pair because the implementations differ. The added populated-grid comparison caught it; value-reader tests prevent unresolved bindings from silently becoming equal nulls.
- **Cash walk setup:** selecting a store while still on DSR immediately re-applies DSR's fixed all-store scope. The walk now selects its intended store after navigation and asserts both header/source scope. The removed implicit cash default had masked that setup error. No additional cash product patch was needed.
- **Screenshot scope:** the fixture has one populated Demo store; migration 0011 also seeds two active catalogue stores. It is not proof of a populated shop installation or a catalogue containing only one store.
- **Independent reviews are static evidence.** No live email, WhatsApp, installed scheduler or production accounting action was exercised by them.

## F-22 measurements

All global pool clears remain intact. Only integration collection scheduling changed. The scheduling-policy regression failed before the assembly attribute was added and passed after it. The same filter ran six times before and six times after:

```powershell
dotnet test tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/Etp.Reporting.SqlServer.IntegrationTests.csproj -c Release --no-build -m:1 --filter 'FullyQualifiedName~AccountingFoundationTests|FullyQualifiedName~AccountingInvariantTests|FullyQualifiedName~PhaseFiveStoreCatalogTests|FullyQualifiedName~PhaseOneUpgradeSqlTests'
```

| Run | Before: passed / failed / skipped | Before wall seconds | After: passed / failed / skipped | After wall seconds |
| --- | --- | --- | --- | --- |
| 1 | 19 / 0 / 0 | 17.39 | 19 / 0 / 0 | 31.32 |
| 2 | 19 / 0 / 0 | 18.24 | 19 / 0 / 0 | 30.85 |
| 3 | 19 / 0 / 0 | 19.38 | 19 / 0 / 0 | 31.64 |
| 4 | 19 / 0 / 0 | 17.78 | 19 / 0 / 0 | 31.25 |
| 5 | 19 / 0 / 0 | 19.34 | 19 / 0 / 0 | 31.47 |
| 6 | 19 / 0 / 0 | 21.04 | 19 / 0 / 0 | 30.73 |

Mean process wall time: **18.86 → 31.21 seconds**. All twelve runs passed; this sample does **not** establish a failure-rate improvement or prove the cause of the earlier Shared Memory failure. Serialization prevents cross-collection overlap with process-wide pool cleanup, with the measured runtime cost above. Full output is retained locally as `tmp/phase5-pool-{before,after}-{1..6}.log`; timings are in the corresponding measurements JSON files. No output was reduced to summary lines.

## Final Release gate

The first final gate, at `98ebd44`, built with **0 warnings / 0 errors** but finished with **1,087 passed / 6 failed / 3 skipped**. All six failures were `InvalidOperationException: The Application object is being shut down.` after the now-default role walk shut down its WPF Application in the shared test host:

- All three role cases of `ImportRetryClosureTests.Problems_button_retries_only_failed_workbook_and_respects_role`.
- Both amount cases of `PhaseFiveAccountingTests.Owner_can_map_an_approved_adjustment_and_prepare_its_balanced_batch`.
- `ReportFilterClosureTests.All_roles_can_filter_real_sales_and_export_the_applied_scope_without_changing_facts`.

This refuted the assumption that joining the role walk's STA thread and shutting down its Application was sufficient isolation for later XAML tests. The full failed output is retained locally in `tmp/phase5-audit-full-gate-98ebd44-failed.log`.

Commit `a72160f` fixes that lifetime defect without changing production code or weakening the walk. A deterministic assertion was added to the same Fact: after all 89/69/54 visits finish and the walk shuts down, construct another real `ImportWorkspaceView` and require its `WorkbookPathInput` to load. It failed before isolation with the same Application-shutdown exception (**1 failed / 0 passed**). The ordinary Fact now runs the unchanged walk in a child VSTest process and performs this subsequent-XAML assertion in the parent. It requires a TRX record for exactly the expected test, one executed/passed result, zero failures/skips and exit code 0; an empty or skipped child run cannot pass. All original database/settings assertions, Application shutdown and dispatcher joining remain. Relative capture paths and diagnostics isolation are preserved, and process waiting/output draining are bounded.

The final focused compatibility run passed **13 / 0 / 0**, including all six previously failing cases, the full role walk and its subsequent-XAML assertion. Red and green logs: `tmp/phase5-rolewalk-isolation-{red,green}.log`. Independent read-only review found no weakened assertion or default-execution gap.

The replacement full gate passed on the code committed as **`a72160f`**, from `P:\`, using the exact requested commands and preserving unfiltered output. Only this documentation record changed afterwards.

```powershell
dotnet build Etp.Reporting.slnx -c Release -m:1 -nodeReuse:false
dotnet test Etp.Reporting.slnx -c Release --no-build -m:1
```

Release build: **0 warnings / 0 errors**, exit code 0, 6.08 seconds. Full test command: exit code 0.

| Project | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Desktop | 478 | 0 | 2 |
| Domain | 12 | 0 | 0 |
| Import | 110 | 0 | 0 |
| Reporting | 63 | 0 | 0 |
| SQL integration | 162 | 0 | 1 |
| SQL adapters | 268 | 0 | 0 |
| **Total** | **1,093** | **0** | **3** |

This is 90 more passes and one fewer skip than the auditor's 1,003 / 0 / 4, or 87 more passes than the clean post-main-merge baseline. The private Import corpus was present: all 110 Import tests ran. The three remaining skips are the existing optional capture/smoke tests, not the Phase 5 role walk:

- `PhaseThreeLiveCaptureTests.Merged_workspaces_render_against_the_disposable_fixture_database`.
- `ExtractedWorkspaceUiSmokeTests.Phase_four_changed_workspaces_render_at_supported_sizes_with_synthetic_data`.
- `PhaseFourFullWindowSmokeTests.Full_MainWindow_starts_displays_today_renders_and_closes_with_disposable_database`.

Full final output: `tmp/phase5-audit-final-build.log` and `tmp/phase5-audit-final-tests.log`. The SQL integration package completed in 5 minutes 11 seconds. No failing test was filtered out or accepted as a pass, and no new skip was added to obtain this result.

Raw red/green and gate logs are local files under this worktree's gitignored `tmp/`; they are not preserved in the repository. The committed assertions, commit messages and this result record are preserved. An independent checkout must rerun the commands. Temporary mutation proofs are explicitly identified above; normal final tests contain none of those mutations.

## Handoff: acceptance still unproven

No numbered code/test finding is silently omitted. **A5.1 and A5.2 remain UNPROVEN** until the installed application is exercised against a populated database as Owner, Store Manager and Viewer. In particular:

1. Walk every available destination for each installed role and read each status line, including the repaired layouts and report-list return paths.
2. Review the retained Management Trend report's source rows and moved chart on real populated dates. The original two-query alias was confirmed in source and by the fixture fingerprint; the duplicate destination is now removed.
3. Click every Archive Open/Export/Share action as a Viewer, including actual handoff behavior and denied actions.
4. Verify installed migrations 0033–0036 and the new 0037 after an authorized upgrade, plus accounting DENYs for `etp_store_manager` and `etp_viewer`. This work applied them only to disposable fixtures.
5. Exercise real SMTP TLS, certificates and authentication against the intended server. Plaintext, unauthenticated loopback tests do not prove those paths.

Existing deployment, shop-PC, native-DPI/touch, real WhatsApp, scheduled Windows identity and Tally read-back acceptance boundaries remain unchanged. There is no claim that a Tally import occurred or that Phase 5 is accepted/closed.
