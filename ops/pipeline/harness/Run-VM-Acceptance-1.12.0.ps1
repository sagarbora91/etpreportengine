<#
Run-VM-Acceptance-1.12.0.ps1

VM acceptance of ETP Reporting Engine 1.12.0 on the Hyper-V VM "ETP-Acceptance-186" (Workpc). 1.12.0 = 1.12.0 + 1.12 wave 1 (release/1.12.0: objects as files and DB-07, one connection
layer, shop results, Tally engine with port 9000 / test PC, collections first slice, update engine); 66 migrations in all: the 61 of 1.12.0 plus 0062 schema objects journal, 0063 collections contracts,
0064 cash deposit confirmations, 0065 update settings, 0066 tally test machine. From the 1.12.0 VM (61) an upgrade adds 5; from 1.9.6-1.9.9 (48) it adds 18 (0049-0066); from 'Rehearsal base' (1.9.2, 37) it adds 29.
(1.12.0 itself added 0049-0061 over 1.9.6-1.9.9: 0049-0051 1.10.0, 0052-0053 1.10.1, 0054-0055 1.11.0, 0056-0058 1.11.1, 0059-0060 1.11.2, 0061 1.12.0; 1.11.3 added none.)
COPY of Run-VM-Acceptance-1.12.0.ps1 (that file is unchanged), adapted 13 Oct 2026 for 1.12.0. What changed and the exact command lines: Reference\Work in progress 2026-10-13\VM-HARNESS-1.12.0-NOTES.md.
Changes: version/migration expectations; the Service view timing median bug fixed (the median read a property that did not exist, so every view printed 0.00 s and passed); the new read-only 1.12 checks
(Test-Release112Checks, also in -Stage Release112); a manual checklist for what needs Sagar (shop zip -> result, Store Manager bank import, update rehearsal, Tally send in Educational mode).
NOT RUN YET (parse-checked only).

Run it ELEVATED (Windows PowerShell 5.1), on Workpc, with nothing heavy running. The 1.12.0 uses:
  Install, clean VM:     -Scenario NewPc -Stage Install                      checkpoint 'Clean for 1.9.3*', 1.12.0 with a new database
  Install over 1.9.8/9:  -Scenario Upgrade -Stage Install -UpgradeFrom Checkpoint   newest checkpoint 'VM19[89]*' (or -CheckpointName)
  Upgrade 1.11.4 VM:     -Scenario Upgrade -Stage Install -UpgradeFrom Checkpoint -CheckpointName 'VM195 Upgrade after automated steps, 2026-10-13 13:25'   (VM at 1.11.4, 61 -> 66)
  Upgrade 1.9.6 VM:      -Scenario Upgrade -Stage Install -UpgradeFrom Checkpoint -CheckpointName 'VM196 delta*'   (1.9.6, 48 -> 66)
  Rehearsal:             -Scenario Rehearsal -UserChanges Script            'Rehearsal base*' (1.9.2) straight to 1.12.0, then the stages below
  Delta 1.9.9 -> 1.12.0: -Scenario Rehearsal -Stage Delta [-CheckpointName '<1.9.9 or 1.9.8 checkpoint*>']   upgrade + no row changes
                         + 18 migrations added (0049-0066) + raw pack again + the 0051 check; restores the checkpoint only when -CheckpointName is given
  Service UI checks:     -Scenario Rehearsal -Stage ServiceUi                read-only: the acceptance runbook's SQL checks + view timings
                         + the 0051 check, on the VM as it is (1.12.0, Service rows imported); restores nothing, installs nothing
  Night-one health run:  -Scenario Rehearsal -Stage HealthNightOne           (1.12.0) runs ETP's automation once in the guest (--automation-once; the
                         app has no other headless health switch), copies the newest HealthExports\health-*.json, reports overall + counts;
                         on the VM copy only, never live. Needs guest local time 02:30 or later (see the stage's comment).
  1.12 checks:           -Scenario Upgrade -UpgradeFrom Checkpoint -Stage Release112   read-only: objects journal, DB-07, ShopResults/evidence/Updates folders + ACLs,
                         Viewer denies, new tasks/scripts, columns; prints the manual checklist. Run it after -Stage HealthNightOne (DB-07 needs a health run).
  Shop PC rehearsal:     -Scenario NewPc -Stage Install -ShopRestore         1.12.0 without a database, then the staging restore, setup again
Every run of the installer is still followed by Test-BootstrapModuleLog (the 1.9.7 bootstrap check; 1.12.0 keeps that bootstrap).
1.11.2 adds light read-only checks after every setup (Test-Release1111Checks, also in -Stage ServiceUi): schema diag with its 6 tables and 14
procedures (0054, 0055); dbo.stores.is_service_money_shop set for WLMHW only (0052); the five retired manual inputs inactive (0053); the
C:\ProgramData\EtpReporting\HealthExports folder exists.
The VM195 evidence-folder, guest-folder and checkpoint prefixes are kept on purpose so -Stage Delta / ServiceUi / HealthNightOne and the later stages
find the existing session folder (E:\ETP\Reference\VM195-*-Rehearsal).

Rehearsal session, in order (one evidence folder E:\ETP\Reference\VM195-<yyyyMMdd-HHmm>-Rehearsal\, the later stages find it):
  -Stage Install   Restores 'Rehearsal base*' (1.9.2 + live data 1 Oct, 37 migrations), upgrades it STRAIGHT to 1.10.0 (as live
                   would be: 0038-0066 in one setup run, 37 -> 66 migrations), or through -IntermediateInstallerPath first
                   (for example 1.9.9, if live gets it before this session). Upgrade checks: no data loss,
                   pre-migration backup, journal = bundled (66, latest 0066_tally_test_machine), AW330 row inactive, Service tables empty.
                   -UserChanges Script: the 1.9.3 stage-2 proof at once: retire the two TFRROWJLTULT009 accounts, add
                   EtpAutomation (Store Manager), EtpTestManager (Store Manager) and EtpTestViewer (Viewer) through the product
                   procedure, setup again, backup + recovery drill + automated operations under EtpAutomation, then the
                   duplicate re-import check (HEMW R025 again under a new zip name). Takes checkpoint "VM195 VM-pre ...".
                   -UserChanges Ui: stops after the upgrade; Sagar makes the user changes in Settings > Users; then -Stage AfterUi.
  -Stage AfterUi   (Ui mode only) the same operations steps + duplicate check, then checkpoint "VM195 VM-pre ...".
  (Sagar)          VM-pre report checks in the VM console (plan section 4): the WLMHW gap days exist ONLY now.
  -Stage Titan     HEMW R010 re-import (7 Sep, R-HEMW-01), then the three held WLMHW Titan files one by one with their expected
                   counts (new/present/conflicts per file, from -ExpectedFiguresPath when given), then a backup. Checkpoint "VM195 VM-post ...".
  (Sagar)          VM-post report checks (plan section 6).
  -Stage Service   Stages the 29 Sep consolidated Service folder and the 3 Oct raw pack in the guest; Sagar imports each in the
                   app (consolidated, the same again, raw; optional GPRC CLAIM export); the script reads the outcomes, checks
                   S004 money per date against the known totals (match/no match only), AW330 isolation and Retail unchanged.
                   Checkpoint "VM195 Service ...".
  -Stage Final     Backup + recovery drill once more (with Service rows present), lists the report exports. Checkpoint.
  -Stage ServiceUi (1.10.0) Read-only SQL checks of SERVICE-UI-1.10.0-ACCEPTANCE.md against EtpReporting in the guest, and the
                   Service view timings (PASS < 1 s, REVIEW 1-3 s, FAIL > 3 s). Run it after -Stage Service, Final or Delta.
  -Stage HealthNightOne (1.12.0) The design's night-one dry run on the restored copy: one automation tick, the Nightly health export copied out.
  -Stage Release112 (1.12.0) Read-only 1.12 checks (see above) and the manual checklist for Sagar. Takes no checkpoint, installs nothing.

Other scenarios (Upgrade, NewPc) work as in the original script, with 1.12.0 as the new version.
Evidence: counts, codes, file names, hashes, account names, PASS/FAIL. No business values (money checks report match/no
match only). Diagnostics stay inside the VM. Writes to the VM database only through the product (setup, imports, tasks,
the user procedure). Workpc's live database is never touched.
Exit codes: 0 = automated part passed (maybe with REVIEW items), 1 = a check failed or the run stopped, 75 = heavy-job lock busy.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('NewPc', 'Upgrade', 'Rehearsal')][string]$Scenario,
    [ValidateSet('Install', 'AfterUi', 'Titan', 'Service', 'Final', 'Delta', 'ServiceUi', 'HealthNightOne', 'Release112')][string]$Stage = 'Install',
    [ValidateSet('RehearsalBase', 'Install192', 'Checkpoint')][string]$UpgradeFrom = 'RehearsalBase',
    [ValidateSet('Ui', 'Script')][string]$UserChanges = 'Script',
    # -Stage Titan: skip the HEMW R010 re-import (for example when it was already done by hand).
    [switch]$SkipHemwR010,
    # Optional JSON with the live-derived expected figures (kept OUT of git; see ops/pipeline/README.md): titan.<R030|R022|R010>.{new,present,conflicts}, s004[] {Date,Cash,Card,Upi}, serviceSrnOpen.
    [string]$ExpectedFiguresPath,
    # -Stage Install/AfterUi: skip the duplicate re-import check.
    [switch]$SkipDuplicateCheck,
    # -Stage Install: the checkpoint to restore instead of the scenario's default. -Stage Delta (1.10.0/1.12.0): when given, the 1.9.9 (or 1.9.8)
    # checkpoint to restore first (asks YES unless -Yes); when absent, Delta restores nothing and the VM must already be on 1.9.9/1.9.8.
    [string]$CheckpointName,
    # The 1.12.0 kit as the publish script writes it (EtpReportingEngine-Setup-1.12.0-x64.exe, SHA256SUMS.txt, release.json).
    # Test-InstallerFile reads the hash from SHA256SUMS.txt beside it; nothing is hard-coded here. Used by every stage that
    # runs setup: Install (clean VM, over 1.9.8/1.9.9, rehearsal from 1.9.2), setup-again, -Stage Delta and -ShopRestore.
    [string]$InstallerPath = 'E:\ETP\Installers\ETP Reporting Engine 1.12.0\EtpReportingEngine-Setup-1.12.0-x64.exe',
    # Not used since the 1.9.7 harness (1.9.6 was the 1.9.7 Delta's start version). Kept so the 7-9 Oct command lines still bind.
    [string]$Installer196Path = 'E:\ETP\Installers\ETP Reporting Engine 1.9.6\EtpReportingEngine-Setup-1.9.6-x64.exe',
    # -Scenario NewPc -Stage Install -ShopRestore (9 Oct): rehearse the SHOP PC: clean VM, 1.12.0 ($InstallerPath) without a database, then restore the
    # staging backup with restore-etp-database.ps1 -BackupPath -ReceiptPath. -StagingBackupPath defaults to the newest
    # E:\ETP\Installers\Shop PC database *\*.bak.
    [switch]$ShopRestore,
    [string]$StagingBackupPath,
    [string]$PreviousInstallerPath = 'E:\ETP\Installers\ETP Reporting Engine 1.9.2\EtpReportingEngine-Setup-1.9.2-x64.exe',
    # Optional middle hop (only if live is on it by the session): for example
    # 'E:\ETP\Installers\ETP Reporting Engine 1.9.3\EtpReportingEngine-Setup-1.9.3-x64.exe' with -IntermediateVersion 1.9.3.
    [string]$IntermediateInstallerPath,
    [string]$IntermediateVersion,
    [string]$DataPackage = 'E:\ETP\Installers\Consolidated data import package (29 Sep 2026)',
    [string]$ServiceRawFolder = 'E:\ETP\Reference\RAW DATA from ETP\Service Centre',
    # Optional (decision 16 Q8): Sagar's GPRC CLAIM export 1 Aug 2026 - today. TODO: not on disk on 4 Oct (E:\ETP-incoming missing).
    [string]$GprcExportFolder,
    [ValidateSet('Headless', 'Ui')][string]$ImportMode = 'Headless',
    [ValidateSet('Session', 'Console')][string]$SetupLauncher = 'Session',
    [switch]$SkipReimportCheck,
    [switch]$CheckpointCurrentStateFirst,
    [switch]$RerunSetup,
    [string]$OutputFolder,
    [switch]$Yes,
    [ValidateRange(10, 240)][int]$SetupTimeoutMinutes = 90,
    [ValidateRange(5, 240)][int]$ImportTimeoutMinutes = 60,
    [ValidateRange(0, 240)][int]$LockWaitMinutes = 30
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------- constants
$VmName = 'ETP-Acceptance-186'
$GuestComputer = 'DESKTOP-IPT0J6H'
$GuestUser = 'DESKTOP-IPT0J6H\ETPTest'   # VM-AND-SECOND-MACHINE-RUNBOOK-2026-09-22.md step A8
$ReferenceRoot = 'E:\ETP\Reference'
$EvidencePrefix = 'VM195'
$GuestStageDir = 'C:\EtpStaging'
$GuestEvidenceRoot = 'C:\EtpEvidence\VM195'
$NewVersion = '1.12.0'
# -Stage Delta upgrades from the 1.9.9 VM (or the 1.9.8 VM if 1.9.9 never reached it). Both are at 48 migrations (1.9.6-1.9.9 add none).
$DeltaFromVersions = @('1.9.9', '1.9.8', '1.9.7', '1.9.6')   # 1.9.6-1.9.9 all hold 48 migrations (0048); 1.12.0 adds 0049-0066 to any of them
$DeltaFromVersion = '1.9.9/1.9.8/1.9.7/1.9.6'   # text only; Invoke-DeltaStage sets $script:DeltaFrom to the version it found
$script:DeltaFrom = $DeltaFromVersion
# -ShopRestore installs $InstallerPath ($NewVersion) like every other path (on 9 Oct it was pinned to the 1.9.6 kit).
$PreviousVersion = '1.9.2'
# 1.9.2 = 0001-0037; 1.9.3 adds 0038-0045; 1.9.4 adds 0046 (amended in place, D-L9-3) and 0047; 1.9.5 adds 0048 (Service,
# incl. the GPRC CLAIM table if it needs one, decision 16). 1.9.6-1.9.9 add NO migration (still 48, 0048). What actually shipped after that
# (the 1.10.0 script wrongly assumed 0052 = job view performance and 52 migrations; the performance work is inside 0050):
#   1.10.0: 0049_tally_store_cost_centres, 0050_service_centre_ui, 0051_staff_targets_owner_only
#   1.10.1: 0052_service_ui_fixes (dbo.stores.is_service_money_shop), 0053_retire_unused_manual_inputs
#   1.11.0: 0054_error_register, 0055_health_check (schema diag)
#   1.11.1: 0056_service_job_customer_name, 0057_service_views_name_by_role, 0058_open_imports_signal
#   1.11.2: 0059_customer_data_permissions, 0060_report_logic_and_speed
#   1.11.4: 0061_service_view_speed (1.11.3 added no migration)
#   1.12.0: 0062_schema_objects_journal, 0063_collections_contracts, 0064_cash_deposit_confirmations, 0065_update_settings, 0066_tally_test_machine
# = 66 migrations. From the 1.11.4 VM (61) an upgrade adds 5; from a 1.9.6-1.9.9 database (48) it adds 18 (0049-0066); from 'Rehearsal base' (37) it adds 29 (0038-0066).
# The real file names (E:\ETP\Code\Worktrees\release-1.12.0\database\migrations) are used; they are still compared with -like.
$ExpectedMigrationCount = 66
$ExpectedLatestMigration = '0066_tally_test_machine'
$ExpectedNewMigrations = @('0049_tally_store_cost_centres', '0050_service_centre_ui', '0051_staff_targets_owner_only', '0052_service_ui_fixes', '0053_retire_unused_manual_inputs', '0054_error_register', '0055_health_check', '0056_service_job_customer_name', '0057_service_views_name_by_role', '0058_open_imports_signal', '0059_customer_data_permissions', '0060_report_logic_and_speed', '0061_service_view_speed', '0062_schema_objects_journal', '0063_collections_contracts', '0064_cash_deposit_confirmations', '0065_update_settings', '0066_tally_test_machine')   # added by 1.10.0-1.12.0 over any 1.9.6-1.9.9 database
$Expected112Migrations = @('0062_schema_objects_journal', '0063_collections_contracts', '0064_cash_deposit_confirmations', '0065_update_settings', '0066_tally_test_machine')   # added by 1.12.0 over the 1.11.4 VM (61)
$Release1114Migrations = 61
$CleanCheckpointPattern = 'Clean for 1.9.3*'
# -UpgradeFrom Checkpoint: the newest 1.9.9 or 1.9.8 VM checkpoint (VM198 ... / VM199 ...). For 1.12.0 pass -CheckpointName: the 1.11.4 VM is
# 'VM195 Upgrade after automated steps, 2026-10-13 13:25'; the 1.9.6 VM is 'VM196 delta*' (go-live is 1.11.4 first, 1.12.0 later).
$OldCheckpointPattern = 'VM19[89]*'
$RehearsalCheckpointPattern = 'Rehearsal base*'
# 1.11.2-1.11.4 (migrations 0052-0061): what Test-Release1111Checks expects. The 1.12.0 checks are Test-Release112Checks.
$DiagTables = @('error_register', 'error_register_versions', 'error_occurrences', 'health_check_runs', 'health_check_results', 'health_check_known')   # 0054, 0055
$DiagProcedures = @('ingest_error_occurrences', 'load_error_register', 'load_error_entry', 'set_error_status', 'merge_error_entries', 'apply_error_fix_manifest', 'record_health_check_run', 'load_health_check_runs', 'load_health_check_results', 'load_health_check_summary', 'load_health_check_known', 'set_health_check_known', 'clear_health_check_known', 'load_role_permissions')
$MoneyShopStore = 'WLMHW'   # 0052: the only store with is_service_money_shop = 1
$RetiredManualInputs = @('DISPLAY_STOCK', 'BACKSTOCK', 'DEFECTIVE_STOCK', 'Y_LOCATION_STOCK', 'PHYSICAL_STOCK')   # 0053: is_active = 0
$HealthExportsFolder = 'C:\ProgramData\EtpReporting\HealthExports'
$HealthNightlyMinutes = 150   # NightlyHealthSchedule.NightlyTime = 02:30 local: the nightly check is due from then, at any time of day after it
$RehearsalBaseCounts = [ordered]@{ 'dbo.sales_invoices' = 5822; 'dbo.sales_lines' = 6697; 'dbo.import_files' = 80; 'dbo.daily_reporting_days' = 11 }
$RehearsalBaseMigrations = 37   # 1.12.0 adds 29 to it (0038-0066)
$RetiredAccounts = @('TFRROWJLTULT009\Sagar', 'TFRROWJLTULT009\EtpAutomation')
$RetireReason = 'Retired PC TFRROWJLTULT009 (old laptop, replaced by Workpc on 2 Oct 2026)'
$AutomationReason = 'Automation account for the scheduled backup and recovery drill'
# Windows accounts that exist in the VM since 'Clean for 1.9.3' (Sagar knows their passwords). Added to ETP only on the VM,
# for R-WLMHW-17 (Viewer cannot generate packs) and R-WLMHW-10 (Open items as a Store Manager or Viewer).
$TestAccounts = @(
    [pscustomobject]@{ Identity = 'DESKTOP-IPT0J6H\EtpTestManager'; Role = 'STORE_MANAGER'; Name = 'EtpTestManager' },
    [pscustomobject]@{ Identity = 'DESKTOP-IPT0J6H\EtpTestViewer'; Role = 'VIEWER'; Name = 'EtpTestViewer' })
$TestAccountReason = 'VM acceptance 1.9.5 test account (VM only, never on live)'
# The held Titan files, in this order. Expected counts = the 1.9.3 rehearsal (Phase6-VM-20261003-2046-Rehearsal,
# titan-expected-counts.csv). 0047 adds a location column and changes no identity, so 1.9.5 must give the same counts.
$TitanFiles = @(
    [pscustomobject]@{ Order = 1; Key = 'R030'; Relative = 'Retail\WLMHW\R030_Variant_Stock_Ledger.xlsx'; Store = 'WLMHW'; ExpectedNew = $null; ExpectedPresent = $null; ExpectedConflicts = $null },
    [pscustomobject]@{ Order = 2; Key = 'R022'; Relative = 'Retail\WLMHW\R022_Revenue_Report.xlsx'; Store = 'WLMHW'; ExpectedNew = $null; ExpectedPresent = $null; ExpectedConflicts = $null },
    [pscustomobject]@{ Order = 3; Key = 'R010'; Relative = 'Retail\WLMHW\R010_BinWise_Stock.xlsx'; Store = 'WLMHW'; ExpectedNew = $null; ExpectedPresent = $null; ExpectedConflicts = $null })
# R-HEMW-01 data step (1.9.4 runbook E2): the package HEMW BinWise again, dated 7 Sep by 1.9.3+. Expected: Imported, new rows,
# 0 conflicts, not Duplicate (its period differs from import 26). Its counts become the live expectation for E2.
# NOT the Excel-edited HEMW BinWise in RAW DATA (IF-025).
$HemwR010 = [pscustomobject]@{ Order = 0; Key = 'HEMW-R010'; Relative = 'Retail\HEMW\R010_BinWise_Stock.xlsx'; Store = 'HEMW'; Sha256Prefix = 'b0f9d2a4' }
# R-HEMW-08 / R-WLMHW-16: a file already loaded, imported again under a new zip name. Expected: Duplicate, 0 new rows.
$DuplicateProbe = [pscustomobject]@{ Key = 'HEMW-R025-again'; Relative = 'Retail\HEMW\R025_SDB_VariantwiseSales.xlsx'; Store = 'HEMW' }
# Service (decisions 15-16, SERVICE-REHEARSAL-RUNBOOK.md steps 4-6). GPRC CLAIM is now its own family, so the raw pack
# expects 21 Imported (was 20), 1 Duplicate (second Empowerment file), 2 Not needed (S005, S028), 1 Unknown layout (TATA).
$ServiceConsolidatedRelative = 'Not imported\Service Centre (no importer profile)'
$ServiceConsolidatedGuestName = 'Service Centre till 29 sep 2026'   # the folder name dates the consolidated workbooks
$ServiceRawGuestName = 'Service raw pack'                          # raw files are dated by the window in their names
$ServiceExpect = [ordered]@{
    ConsolidatedFiles = 41; ConsolidatedImported = 35; ConsolidatedNotNeeded = 6
    RawFiles = 25; RawImported = 21; RawDuplicate = 1; RawNotNeeded = 2; RawUnknown = 1 }
# S004 (AW330) per business date and tender, from SERVICE-OPEN-QUESTIONS-ANSWERS.md (4 Oct): 25 Aug = Sagar's Titan cash
# book; 30 Sep - 2 Oct = the raw TENDER COLLECTION. Compared inside the guest; only match / no match leaves the VM.
$S004Expected = @()   # figures come from -ExpectedFiguresPath (not in git)
$TaskBackup = 'ETP Reporting Daily Backup'
$TaskDrill = 'ETP Reporting Monthly Recovery Drill'
$TaskAutomation = 'ETP Reporting Automated Operations'
$FactTables = @('dbo.sales_invoices', 'dbo.sales_lines', 'dbo.sales_invoice_controls', 'dbo.stock_movements', 'dbo.source_lineage', 'dbo.import_files')
$KeyTables = @('dbo.sales_invoices', 'dbo.sales_lines', 'dbo.import_files', 'dbo.daily_reporting_days')
# Retail tables the Service imports must not change.
$RetailFactTables = @('dbo.sales_invoices', 'dbo.sales_lines', 'dbo.sales_tenders', 'dbo.stock_snapshots', 'dbo.stock_movements', 'dbo.daily_reporting_days')
# 1.10.0 Service UI (migration 0050, SERVICE-UI-1.10.0-ACCEPTANCE.md section 1.4): the eight views 0050 creates or amends.
$ServiceUiViews = @('v_service_status_view_facts', 'v_service_claims', 'v_service_job', 'v_service_job_timeline', 'v_service_parts',
    'v_service_parts_transit', 'v_service_stock_summary', 'v_service_pending_current')
$ServiceUiGrantRows = 72   # 8 views x (3 GRANT SELECT + 6 DENY INSERT/UPDATE/DELETE for etp_viewer and etp_store_manager)
# Job stages v_service_job may hold (0050 section 3); anything else is a FAIL.
$ServiceJobStages = @('BOOKED', 'ON_BENCH', 'INDENT_RAISED', 'SRN_OUT', 'READY_FOR_DELIVERY', 'IN_TRANSIT_BACK', 'DC_ISSUED', 'RA_ISSUED', 'DELIVERED', 'RWR')
# Claim types and the report families each may come from (0050 section 2).
$ServiceClaimSources = [ordered]@{ GPRC = @('S023', 'S041'); MB = @('S024'); WDC = @('S025', 'S039'); WRA = @('S026', 'S040') }
# Open SRNs in v_service_pending_current (Q5) on the 29 Sep + 3 Oct Service data the VM holds (count from the expected-figures file).
$ServiceSrnOpenExpected = $null   # from -ExpectedFiguresPath (not in git); $null = the check is REVIEW
if ($ExpectedFiguresPath) {
    $ef = Get-Content -LiteralPath $ExpectedFiguresPath -Raw | ConvertFrom-Json
    foreach ($t in $TitanFiles) { $e = $ef.titan.($t.Key); if ($e) { $t.ExpectedNew = $e.new; $t.ExpectedPresent = $e.present; $t.ExpectedConflicts = $e.conflicts } }
    if ($ef.s004) { $S004Expected = @($ef.s004 | ForEach-Object { [pscustomobject]@{ Date = $_.Date; Cash = $_.Cash; Card = $_.Card; Upi = $_.Upi } }) }
    if ($null -ne $ef.serviceSrnOpen) { $ServiceSrnOpenExpected = [int]$ef.serviceSrnOpen }
}
# Timing thresholds (seconds) for SELECT COUNT(*) FROM dbo.v_service_job and one full read of each v_service_* view (design SD-11).
$TimingPassSeconds = 1.0
$TimingFailSeconds = 3.0
$TimingRuns = 3   # timed runs per query after one untimed warm-up run; the median is judged
# 0051: staff targets become Owner-only (a Store Manager may no longer INSERT/UPDATE them).
$StaffTargetsTable = 'dbo.staff_sales_targets'

$RunId = Get-Date -Format 'yyyyMMdd-HHmmss'
$script:Checks = New-Object System.Collections.Generic.List[object]
$script:CheckNo = 0
$script:UiSteps = New-Object System.Collections.Generic.List[string]
$script:Notes = New-Object System.Collections.Generic.List[string]
$script:RunInfo = [ordered]@{}
$script:Session = $null
$script:Credential = $null
$script:Out = $null
$script:GuestRunDir = "$GuestEvidenceRoot\$RunId"
$script:InstallerHash = $null
$script:PreviousHash = $null
$script:IntermediateHash = $null
$script:ZipPath = $null
$script:ZipHash = $null
$script:TempDir = $null
$script:LockBusy = $false
$script:GuestDocuments = $null
$script:TitanZips = @()
$script:HemwZip = $null
$script:DuplicateZip = $null
$script:TitanRows = New-Object System.Collections.Generic.List[object]
$script:CountsAfter = [ordered]@{}
$script:VmPreReady = $false
$script:From = $UpgradeFrom
if ($Scenario -eq 'Rehearsal') { $script:From = 'RehearsalBase' }
if ($Scenario -eq 'NewPc') { $script:From = '' }
if ($ShopRestore -and ($Scenario -ne 'NewPc' -or $Stage -ne 'Install')) { throw '-ShopRestore belongs to -Scenario NewPc -Stage Install.' }
if (($Stage -in @('Titan', 'Service', 'Final', 'Delta', 'ServiceUi', 'HealthNightOne')) -and $Scenario -ne 'Rehearsal') { throw "-Stage $Stage belongs to -Scenario Rehearsal." }
if (($Stage -eq 'Release112') -and ($Scenario -eq 'NewPc')) { throw '-Stage Release112 belongs to -Scenario Upgrade (the evidence folder of the 1.12.0 upgrade run) or Rehearsal.' }
if ([bool]$IntermediateInstallerPath -ne [bool]$IntermediateVersion) { throw 'Give -IntermediateInstallerPath and -IntermediateVersion together.' }

# ---------------------------------------------------------------- host helpers
function Write-Step([string]$Text) { Write-Host ''; Write-Host "== $Text" -ForegroundColor Cyan }

function Write-Utf8([string]$Path, [string]$Text) {
    $parent = Split-Path -Parent $Path
    if ($parent -and -not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    [IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding($false)))
}

# Untyped on purpose: a [bool] parameter refuses $null and strings at binding time.
function Get-Result($Ok, [string]$Otherwise = 'FAIL') { if ($Ok -eq $true) { return 'PASS' } return $Otherwise }

function Add-Check {
    param([string]$Area, [string]$Check, [string]$Expected, [string]$Actual, [ValidateSet('PASS', 'FAIL', 'REVIEW', 'INFO')][string]$Result)
    $script:CheckNo++
    $script:Checks.Add([pscustomobject]@{ No = $script:CheckNo; Area = $Area; Check = $Check; Expected = $Expected; Actual = $Actual; Result = $Result })
    $color = 'Gray'
    if ($Result -eq 'PASS') { $color = 'Green' } elseif ($Result -eq 'FAIL') { $color = 'Red' } elseif ($Result -eq 'REVIEW') { $color = 'Yellow' }
    Write-Host ("  [{0}] {1}: {2}" -f $Result, $Check, $Actual) -ForegroundColor $color
}

function Save-Json([string]$Name, $Object) { Write-Utf8 (Join-Path $script:Out $Name) ($Object | ConvertTo-Json -Depth 10) }

function Save-Csv([string]$Name, $Rows) {
    $path = Join-Path $script:Out $Name
    $parent = Split-Path -Parent $path
    if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    $list = @($Rows | Where-Object { $null -ne $_ })
    if ($list.Count -gt 0) { $list | Export-Csv -LiteralPath $path -NoTypeInformation -Encoding UTF8 } else { Write-Utf8 $path '' }
}

function Format-Cell($Value) { return ((([string]$Value) -replace '\|', '\|') -replace "`r?`n", ' ') }

function Get-LogMessage([string]$Line) { return ($Line -replace '^\d{4}-\d{2}-\d{2}T\S+\s+', '') }

function ConvertFrom-RoundTrip([string]$Text) {
    return [datetime]::Parse($Text, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
}

# ---------------------------------------------------------------- Hyper-V and session helpers
function Wait-Heartbeat([int]$Minutes = 10) {
    $deadline = (Get-Date).AddMinutes($Minutes)
    while ((Get-VMIntegrationService -VMName $VmName -Name 'Heartbeat').PrimaryStatusDescription -ne 'OK') {
        if ((Get-Date) -gt $deadline) { throw "The guest did not report a heartbeat in $Minutes minutes." }
        Start-Sleep -Seconds 10
    }
}

function Connect-Guest {
    if ($script:Session) { Remove-PSSession -Session $script:Session -ErrorAction SilentlyContinue; $script:Session = $null }
    $deadline = (Get-Date).AddMinutes(10)
    while (-not $script:Session) {
        try { $script:Session = New-PSSession -VMName $VmName -Credential $script:Credential -ErrorAction Stop }
        catch {
            # Never retry a wrong password: the account could lock.
            if ($_.Exception.Message -match '(?i)credential|password|user name|logon failure') {
                throw "PowerShell Direct refused the account or password ($($_.Exception.Message)). Run the script again and retype the password. If the name form is the problem, see the runbook (.\ETPTest)."
            }
            if ((Get-Date) -gt $deadline) { throw "Could not open a PowerShell Direct session in 10 minutes: $($_.Exception.Message)" }
            Start-Sleep -Seconds 15
        }
    }
    Invoke-Command -Session $script:Session -ScriptBlock $GuestHelpers -ErrorAction Stop
}

function Invoke-Guest([scriptblock]$Block, [object[]]$Arguments = @()) {
    return Invoke-Command -Session $script:Session -ScriptBlock $Block -ArgumentList $Arguments -ErrorAction Stop
}

function Invoke-GuestJson([scriptblock]$Block, [object[]]$Arguments = @()) {
    $output = @(Invoke-Guest $Block $Arguments)
    if ($output.Count -eq 0) { return $null }
    return ([string]$output[-1] | ConvertFrom-Json)
}

# A row set from the guest must be one element per row. The first VM run (3 Oct 17:00) got each SQL result set as ONE
# element (an array inside the array), so 37 journal rows counted as 1 and TCP/NP read "0,0". The guest blocks are
# fixed; this unrolls any nesting again (and drops nulls) so a check can never count a whole result set as one row.
function Expand-Rows($Value) {
    $out = New-Object System.Collections.Generic.List[object]
    foreach ($item in @($Value)) {
        if ($null -eq $item) { continue }
        if ($item -is [array]) { foreach ($inner in (Expand-Rows $item)) { $out.Add($inner) } }
        else { $out.Add($item) }
    }
    return , $out.ToArray()
}

function Repair-RowSets($Object, [string[]]$RowSets, [string[]]$SingleRows = @()) {
    if ($null -eq $Object) { return }
    $names = @($Object.PSObject.Properties | ForEach-Object { $_.Name })
    foreach ($p in $RowSets) { if ($names -contains $p) { $Object.$p = Expand-Rows $Object.$p } }
    foreach ($p in $SingleRows) { if ($names -contains $p) { $Object.$p = (Expand-Rows $Object.$p)[0] } }
}

# Migration ids of a journal row set (or a list of names): one per row, trimmed, no blanks or "(n rows affected)" lines.
# Writes the ids one by one to the pipeline; collect them with @(Get-MigrationIds ...).
function Get-MigrationIds($Journal) {
    # (Expand-Rows ...) in parentheses: its one array object is then enumerated row by row.
    return @((Expand-Rows $Journal) | ForEach-Object {
            $id = $_
            if ($_ -isnot [string]) { $id = $_.migration_id }
            ([string]$id).Trim()
        } | Where-Object { $_ -and $_ -notmatch '(?i)rows? affected' })
}

function Find-Checkpoint([string]$Pattern) {
    $all = @(Get-VMSnapshot -VMName $VmName | Where-Object { $_.Name -like $Pattern } | Sort-Object CreationTime)
    if ($all.Count -eq 0) {
        $names = (@(Get-VMSnapshot -VMName $VmName | ForEach-Object { $_.Name }) -join '; ')
        throw "No checkpoint of $VmName matches '$Pattern'. Checkpoints: $names"
    }
    if ($all.Count -gt 1) { Write-Host "  $($all.Count) checkpoints match '$Pattern'; using the newest." -ForegroundColor Yellow }
    return $all[-1]
}

function New-Checkpoint([string]$Name) {
    Checkpoint-VM -Name $VmName -SnapshotName $Name
    Write-Host "  checkpoint taken: $Name"
    $script:Notes.Add("Checkpoint taken: $Name")
}

function Restore-Checkpoint($Snapshot) {
    if (@('Running', 'Paused') -contains [string](Get-VM -Name $VmName).State) {
        Write-Host '  turning the VM off (its current state is discarded; no checkpoint is deleted)'
        Stop-VM -Name $VmName -TurnOff -Force
    }
    Restore-VMSnapshot -VMSnapshot $Snapshot -Confirm:$false
    if ((Get-VM -Name $VmName).State -ne 'Running') { Start-VM -Name $VmName }
    Wait-Heartbeat
}

function Copy-ToGuest([string]$HostFile, [string]$GuestFile, [string]$ExpectedHash) {
    if (-not $ExpectedHash) { $ExpectedHash = (Get-FileHash -LiteralPath $HostFile -Algorithm SHA256).Hash }
    $sizeMb = [math]::Round((Get-Item -LiteralPath $HostFile).Length / 1MB, 1)
    Write-Host "  copying $(Split-Path -Leaf $HostFile) ($sizeMb MB) to $GuestFile"
    $copied = $false
    try {
        Copy-VMFile -Name $VmName -SourcePath $HostFile -DestinationPath $GuestFile -FileSource Host -CreateFullPath -Force -ErrorAction Stop
        $copied = $true
    }
    catch { Write-Host "  Copy-VMFile failed ($($_.Exception.Message)); copying over the PowerShell Direct session instead." -ForegroundColor Yellow }
    if (-not $copied) { Copy-Item -ToSession $script:Session -LiteralPath $HostFile -Destination $GuestFile -Force }
    $actual = [string](Invoke-Guest { param($p) (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash } @($GuestFile))
    if ($actual -ne $ExpectedHash) { throw "Hash mismatch after copying $HostFile into the guest ($actual, expected $ExpectedHash)." }
}

function Copy-FromGuest([string]$Label) {
    $dest = Join-Path $script:Out $Label
    if (-not (Test-Path -LiteralPath $dest)) { New-Item -ItemType Directory -Path $dest -Force | Out-Null }
    try { Copy-Item -FromSession $script:Session -Path "$($script:GuestRunDir)\$Label\*" -Destination $dest -Recurse -Force -ErrorAction Stop }
    catch { Write-Host "  nothing copied from the guest for $Label ($($_.Exception.Message))" -ForegroundColor Yellow }
}

# ---------------------------------------------------------------- guest script blocks
# Defined once per session: SQL access through System.Data.SqlClient (part of .NET Framework, so nothing is
# installed), Windows authentication over shared memory, read-only queries only.
$GuestHelpers = {
    function global:Invoke-P6Sql {
        param([string]$Query, [string]$Database = 'master', [hashtable]$Parameters = @{}, [int]$Timeout = 900)
        $connection = $null
        $lastError = $null
        foreach ($source in @('lpc:.\SQLEXPRESS', '.\SQLEXPRESS')) {
            $candidate = New-Object System.Data.SqlClient.SqlConnection ('Data Source=' + $source + ';Initial Catalog=' + $Database + ';Integrated Security=SSPI;Connect Timeout=30;Application Name=ETP Phase 6 acceptance')
            try { $candidate.Open(); $connection = $candidate; break }
            catch { $lastError = $_.Exception; $candidate.Dispose() }
        }
        if (-not $connection) { throw $lastError }
        $rows = New-Object System.Collections.Generic.List[object]
        try {
            $command = $connection.CreateCommand()
            $command.CommandText = $Query
            $command.CommandTimeout = $Timeout
            foreach ($key in $Parameters.Keys) { [void]$command.Parameters.AddWithValue('@' + $key, [string]$Parameters[$key]) }
            $reader = $command.ExecuteReader()
            while ($reader.Read()) {
                $row = [ordered]@{}
                for ($i = 0; $i -lt $reader.FieldCount; $i++) {
                    $value = $reader.GetValue($i)
                    if ($value -is [DBNull]) { $value = $null }
                    elseif ($value -is [datetime]) { $value = $value.ToString('yyyy-MM-ddTHH:mm:ss.fff') }
                    elseif ($value -is [byte[]]) { $value = [BitConverter]::ToString($value) }
                    $row[$reader.GetName($i)] = $value
                }
                $rows.Add([pscustomobject]$row)
            }
            $reader.Close()
        }
        finally { $connection.Dispose() }
        # The rows leave as ONE array object (also when there are 0 or 1 rows). Callers assign it or wrap the call in
        # ( ), never @( ): @(Invoke-P6Sql ...) nests the whole result set as a single element, so 37 journal rows
        # counted as 1 and member access joined every value into one string (first VM run, 3 Oct 17:00).
        return , $rows.ToArray()
    }

    function global:Get-P6RowCounts {
        # Exact COUNT_BIG of every user table in EtpReporting.
        $sql = @'
SET NOCOUNT ON;
DECLARE @sql nvarchar(max);
SELECT @sql = STRING_AGG(CONVERT(nvarchar(max), N'SELECT N' + QUOTENAME(s.name + N'.' + t.name, N'''') + N' AS table_name, COUNT_BIG(*) AS row_count FROM ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name)), N' UNION ALL ')
FROM sys.tables AS t JOIN sys.schemas AS s ON s.schema_id = t.schema_id
WHERE t.is_ms_shipped = 0;
IF @sql IS NOT NULL BEGIN SET @sql = @sql + N' ORDER BY table_name;'; EXEC sys.sp_executesql @sql; END;
'@
        return (Invoke-P6Sql -Database 'EtpReporting' -Query $sql)
    }

    function global:Get-P6Fingerprint {
        # Per store: invoice and line counts, first and last date, and a SHA-256 of the quantity and value totals.
        # The totals themselves are never returned.
        $sql = @'
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.sales_invoices', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.sales_lines', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.sales_lines', N'source_gross_amount') IS NOT NULL AND COL_LENGTH(N'dbo.sales_lines', N'source_net_amount') IS NOT NULL
EXEC sys.sp_executesql N'
SELECT x.store_code, x.invoices, x.lines, CONVERT(varchar(10), x.first_date, 23) AS first_date, CONVERT(varchar(10), x.last_date, 23) AS last_date,
       CONVERT(varchar(64), HASHBYTES(''SHA2_256'', CONCAT(x.store_code, ''|'', x.invoices, ''|'', x.lines, ''|'', x.qty, ''|'', x.gross, ''|'', x.net)), 2) AS totals_sha256
FROM (SELECT i.store_code, COUNT_BIG(DISTINCT i.sales_invoice_id) AS invoices, COUNT_BIG(l.sales_line_id) AS lines,
             CONVERT(varchar(40), SUM(l.source_quantity)) AS qty, CONVERT(varchar(40), SUM(l.source_gross_amount)) AS gross,
             CONVERT(varchar(40), SUM(l.source_net_amount)) AS net, MIN(i.transaction_date) AS first_date, MAX(i.transaction_date) AS last_date
      FROM dbo.sales_invoices AS i LEFT JOIN dbo.sales_lines AS l ON l.sales_invoice_id = i.sales_invoice_id
      GROUP BY i.store_code) AS x
ORDER BY x.store_code;';
'@
        return (Invoke-P6Sql -Database 'EtpReporting' -Query $sql)
    }

    function global:Get-P6AppDir {
        $roots = @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*')
        $entry = @(Get-ItemProperty -Path $roots -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -match 'ETP Reporting' -and $_.InstallLocation })
        if ($entry.Count -gt 0) { return ([string]$entry[0].InstallLocation).TrimEnd('\') }
        return 'C:\Program Files\Saagar Traders\ETP Reporting Engine'
    }
}

$PrepGuestBlock = {
    param([string]$StageDir, [string]$RunDir)
    $evidenceRoot = Split-Path -Parent (Split-Path -Parent $RunDir)
    foreach ($d in @($StageDir, $evidenceRoot)) {
        if (-not (Test-Path -LiteralPath $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
        # Administrators and SYSTEM only, like runbook step A11.
        $null = & icacls.exe $d /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' 2>&1
    }
    New-Item -ItemType Directory -Path $RunDir -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $StageDir 'Data') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $StageDir 'Titan') -Force | Out-Null
    # Folders the unelevated app can read and write (the UI import source and the report exports).
    $documents = Join-Path $env:USERPROFILE 'Documents'
    foreach ($d in @((Join-Path $documents 'Phase6-Import'), (Join-Path $documents 'Phase6-Reports'))) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $elevated = ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    [ordered]@{ computer = $env:COMPUTERNAME; account = $identity.Name; elevated = $elevated; clockUtc = (Get-Date).ToUniversalTime().ToString('o'); documents = $documents } | ConvertTo-Json -Compress
}

$GuestFactsBlock = {
    param([string]$Phase)
    $ErrorActionPreference = 'Continue'
    $errors = New-Object System.Collections.Generic.List[string]
    $f = [ordered]@{}
    $f.phase = $Phase
    $f.collectedUtc = (Get-Date).ToUniversalTime().ToString('o')
    $f.computer = $env:COMPUTERNAME
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $f.account = $identity.Name
    $f.elevated = ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    try { $os = Get-CimInstance -ClassName Win32_OperatingSystem; $f.os = "$($os.Caption) $($os.Version)" } catch { $f.os = '' }
    $f.freeGbC = [math]::Round((Get-PSDrive -Name C).Free / 1GB, 1)
    $f.etpProcesses = @(Get-Process -Name 'Etp.Reporting.Desktop' -ErrorAction SilentlyContinue).Count
    $root = 'C:\ProgramData\EtpReporting'
    $f.programDataExists = Test-Path -LiteralPath $root

    # 1. Application
    $uninstallRoots = @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*')
    $apps = @(Get-ItemProperty -Path $uninstallRoots -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName })
    $etp = @($apps | Where-Object { $_.DisplayName -match 'ETP Reporting' })
    $f.etpUninstallEntries = @($etp | ForEach-Object { [ordered]@{ name = [string]$_.DisplayName; version = [string]$_.DisplayVersion; location = [string]$_.InstallLocation } })
    $f.sqlProducts = @($apps | Where-Object { $_.DisplayName -match 'SQL Server|ODBC Driver|sqlcmd|OLE DB Driver|Command Line Utilities' } | Sort-Object DisplayName | ForEach-Object { ("$($_.DisplayName) $($_.DisplayVersion)").Trim() })
    $appDir = Get-P6AppDir
    $f.appDir = $appDir
    $exe = Join-Path $appDir 'Etp.Reporting.Desktop.exe'
    $f.exeExists = Test-Path -LiteralPath $exe
    $f.exeProductVersion = ''
    $f.exeFileVersion = ''
    if ($f.exeExists) { $vi = (Get-Item -LiteralPath $exe).VersionInfo; $f.exeProductVersion = [string]$vi.ProductVersion; $f.exeFileVersion = [string]$vi.FileVersion }
    $incomplete = Join-Path $appDir 'SETUP-INCOMPLETE.txt'
    $f.markers = [ordered]@{ setupIncomplete = (Test-Path -LiteralPath $incomplete); restorePending = (Test-Path -LiteralPath (Join-Path $appDir 'DATABASE-RESTORE-PENDING.txt')); incompleteText = '' }
    if ($f.markers.setupIncomplete) { $f.markers.incompleteText = ((Get-Content -LiteralPath $incomplete -ErrorAction SilentlyContinue | Select-Object -First 2) -join ' ') }
    $f.sqlSetupMediaLeft = Test-Path -LiteralPath (Join-Path $appDir 'SqlSetupMedia')
    $f.bundledMigrations = @(Get-ChildItem -LiteralPath (Join-Path $appDir 'database\migrations') -Filter '*.sql' -File -ErrorAction SilentlyContinue | Sort-Object Name | ForEach-Object { $_.BaseName })

    # 2. Accounts and configuration
    $automationUser = Get-LocalUser -Name 'EtpAutomation' -ErrorAction SilentlyContinue
    $f.automationAccount = [ordered]@{ exists = [bool]$automationUser; enabled = $false; description = '' }
    if ($automationUser) { $f.automationAccount.enabled = [bool]$automationUser.Enabled; $f.automationAccount.description = [string]$automationUser.Description }
    $admins = @()
    try { $admins = @(Get-LocalGroupMember -SID 'S-1-5-32-544' -ErrorAction Stop | ForEach-Object { [string]$_.Name }) }
    catch {
        try {
            $group = [ADSI]("WinNT://$env:COMPUTERNAME/Administrators,group")
            $admins = @($group.psbase.Invoke('Members') | ForEach-Object { [string]$_.GetType().InvokeMember('Name', 'GetProperty', $null, $_, $null) })
        }
        catch { $errors.Add("administrators: $($_.Exception.Message)") }
    }
    $f.administrators = $admins
    $f.operations = $null
    $opsPath = Join-Path $root 'Operations\operations.json'
    if (Test-Path -LiteralPath $opsPath) {
        try {
            $o = Get-Content -Raw -LiteralPath $opsPath | ConvertFrom-Json
            $f.operations = [ordered]@{ serverInstance = [string]$o.serverInstance; database = [string]$o.database; automationPrincipal = [string]$o.automationPrincipal; allowAutomationFolderAccess = $o.allowAutomationFolderAccess }
        }
        catch { $errors.Add("operations.json: $($_.Exception.Message)") }
    }
    $f.custodyReceiptExists = Test-Path -LiteralPath (Join-Path $root 'Backups\certificate-custody.json')

    # 3. ProgramData folder ACLs (directories only, never file names inside them)
    $allowed = New-Object System.Collections.Generic.List[string]
    $allowed.Add('S-1-5-18'); $allowed.Add('S-1-5-32-544')
    try { $allowed.Add(([Security.Principal.NTAccount]'NT SERVICE\MSSQL$SQLEXPRESS').Translate([Security.Principal.SecurityIdentifier]).Value) } catch { }
    $automationSid = ''
    if ($automationUser) { $automationSid = $automationUser.SID.Value; $allowed.Add($automationSid) }
    $broadSids = @('S-1-1-0', 'S-1-5-32-545', 'S-1-5-11', 'S-1-5-4', 'S-1-5-32-546')
    $privateFolders = @('', 'Backups', 'Documents', 'Share', 'SetupLogs', 'Operations', 'Inbound', 'Processed', 'Failed', 'ReportPacks')
    $f.acls = @(foreach ($rel in @('', 'Backups', 'Backups\RecoveryDrill', 'Documents', 'Share', 'SetupLogs', 'Operations', 'Inbound', 'Processed', 'Processed\Duplicate', 'Failed', 'ReportPacks')) {
            $path = $root
            $label = '(root)'
            if ($rel) { $path = Join-Path $root $rel; $label = $rel }
            if (-not (Test-Path -LiteralPath $path -PathType Container)) { [ordered]@{ folder = $label; exists = $false }; continue }
            try {
                $acl = Get-Acl -LiteralPath $path
                $rules = @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))
                $entries = @(foreach ($rule in $rules) {
                        $sid = $rule.IdentityReference.Value
                        $name = $sid
                        try { $name = ([Security.Principal.SecurityIdentifier]$sid).Translate([Security.Principal.NTAccount]).Value } catch { }
                        [ordered]@{ sid = $sid; name = $name; rights = [string]$rule.FileSystemRights; allow = ([string]$rule.AccessControlType -eq 'Allow'); inherited = [bool]$rule.IsInherited }
                    })
                $ownerSid = ''
                try { $ownerSid = $acl.GetOwner([Security.Principal.SecurityIdentifier]).Value } catch { }
                [ordered]@{
                    folder           = $label
                    exists           = $true
                    protected        = [bool]$acl.AreAccessRulesProtected
                    expectProtected  = ($privateFolders -contains $rel)
                    ownerSid         = $ownerSid
                    entries          = @($entries | ForEach-Object { $suffix = ''; if ($_.inherited) { $suffix = ' (inherited)' }; if (-not $_.allow) { $suffix += ' DENY' }; "$($_.name): $($_.rights)$suffix" })
                    broad            = @($entries | Where-Object { $_.allow -and ($broadSids -contains $_.sid) } | ForEach-Object { $_.name })
                    unexpected       = @($entries | Where-Object { $_.allow -and ($allowed -notcontains $_.sid) -and ($broadSids -notcontains $_.sid) } | ForEach-Object { "$($_.name): $($_.rights)" })
                    automationRights = (@($entries | Where-Object { $automationSid -and $_.sid -eq $automationSid } | ForEach-Object { $_.rights }) -join ', ')
                }
            }
            catch { [ordered]@{ folder = $label; exists = $true; error = $_.Exception.Message } }
        })
    $f.installWritableByNonAdmins = @()
    if (Test-Path -LiteralPath $appDir) {
        try {
            $okSids = @('S-1-5-18', 'S-1-5-32-544', 'S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464', 'S-1-3-0')
            $acl = Get-Acl -LiteralPath $appDir
            $f.installWritableByNonAdmins = @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]) |
                Where-Object { [string]$_.AccessControlType -eq 'Allow' -and ($okSids -notcontains $_.IdentityReference.Value) -and ([string]$_.FileSystemRights -match 'Write|Modify|FullControl|ChangePermissions|TakeOwnership|Delete|CreateFiles|AppendData') } |
                ForEach-Object { "$($_.IdentityReference.Value): $($_.FileSystemRights)" })
        }
        catch { $errors.Add("install folder ACL: $($_.Exception.Message)") }
    }

    # 4. Scheduled tasks
    $f.tasks = @(foreach ($t in @(Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object { $_.TaskName -match '(?i)etp' })) {
            $info = $null
            try { $info = Get-ScheduledTaskInfo -InputObject $t -ErrorAction Stop } catch { }
            $lastRun = $null; $lastResult = $null
            if ($info) { $lastResult = [int64]$info.LastTaskResult; if ($info.LastRunTime) { $lastRun = $info.LastRunTime.ToUniversalTime().ToString('o') } }
            [ordered]@{
                name = [string]$t.TaskName; path = [string]$t.TaskPath; state = [string]$t.State
                user = [string]$t.Principal.UserId; logonType = [string]$t.Principal.LogonType; runLevel = [string]$t.Principal.RunLevel
                actions = (@($t.Actions | ForEach-Object { ([string]$_.Execute + ' ' + [string]$_.Arguments).Trim() }) -join ' || ')
                lastRunUtc = $lastRun; lastResult = $lastResult
            }
        })

    # 5. Backups folder: names and sizes, and the two receipts (operational metadata only)
    $f.backupFiles = @(Get-ChildItem -LiteralPath (Join-Path $root 'Backups') -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc | ForEach-Object { [ordered]@{ name = $_.Name; bytes = $_.Length; lastWriteUtc = $_.LastWriteTimeUtc.ToString('o') } })
    $receipts = [ordered]@{ latestVerified = 'EtpReporting-latest-verified.json'; latestDrill = 'EtpReporting-latest-drill.json' }
    foreach ($key in @($receipts.Keys)) {
        $f[$key] = $null
        $p = Join-Path $root ('Backups\' + $receipts[$key])
        if (Test-Path -LiteralPath $p) { try { $f[$key] = Get-Content -Raw -LiteralPath $p | ConvertFrom-Json } catch { $errors.Add("$($receipts[$key]): $($_.Exception.Message)") } }
    }

    # 6. SQL Server
    $svc = Get-CimInstance -ClassName Win32_Service -Filter 'Name="MSSQL$SQLEXPRESS"' -ErrorAction SilentlyContinue
    $f.sqlService = $null
    if ($svc) { $f.sqlService = [ordered]@{ state = [string]$svc.State; startMode = [string]$svc.StartMode; startName = [string]$svc.StartName } }
    $f.sqlServer = $null; $f.sqlProtocols = @(); $f.sqlSysadmins = @(); $f.databases = @(); $f.brokers = @()
    $f.journal = @(); $f.users = @(); $f.rowCounts = @(); $f.fingerprint = @(); $f.watchFolders = $null; $f.automationRoles = @(); $f.drillResults = @()
    if ($svc -and [string]$svc.State -eq 'Running') {
        $automationPrincipal = "$env:COMPUTERNAME\EtpAutomation"
        if ($f.operations -and $f.operations.automationPrincipal) { $automationPrincipal = [string]$f.operations.automationPrincipal }
        try { $f.sqlServer = (Invoke-P6Sql -Query "SELECT CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion')) AS product_version, CONVERT(int, SERVERPROPERTY('ProductMajorVersion')) AS major, CONVERT(nvarchar(128), SERVERPROPERTY('Edition')) AS edition, CONVERT(int, SERVERPROPERTY('EngineEdition')) AS engine_edition, CONVERT(nvarchar(128), SERVERPROPERTY('Collation')) AS collation, CONVERT(int, SERVERPROPERTY('IsIntegratedSecurityOnly')) AS windows_only, SUSER_SNAME() AS connected_as, IS_SRVROLEMEMBER('sysadmin') AS sysadmin_now")[0] }
        catch { $errors.Add("server: $($_.Exception.Message)") }
        try { $f.sqlProtocols = (Invoke-P6Sql -Query "SELECT CASE WHEN registry_key LIKE N'%\SuperSocketNetLib\Tcp' THEN 'TCP' ELSE 'NP' END AS protocol, CONVERT(nvarchar(10), value_data) AS enabled FROM sys.dm_server_registry WHERE value_name = N'Enabled' AND (registry_key LIKE N'%\SuperSocketNetLib\Tcp' OR registry_key LIKE N'%\SuperSocketNetLib\Np')") }
        catch { $errors.Add("protocols: $($_.Exception.Message)") }
        try { $f.sqlSysadmins = (Invoke-P6Sql -Query "SELECT m.name, m.type_desc, m.is_disabled FROM sys.server_role_members AS rm JOIN sys.server_principals AS r ON r.principal_id = rm.role_principal_id JOIN sys.server_principals AS m ON m.principal_id = rm.member_principal_id WHERE r.name = N'sysadmin' ORDER BY m.name") }
        catch { $errors.Add("sysadmins: $($_.Exception.Message)") }
        try { $f.databases = (Invoke-P6Sql -Query "SELECT name, state_desc, compatibility_level FROM sys.databases WHERE database_id > 4 ORDER BY name") }
        catch { $errors.Add("databases: $($_.Exception.Message)") }
        try { $f.brokers = (Invoke-P6Sql -Parameters @{ automation = $automationPrincipal } -Query "SELECT o.name AS broker, CASE WHEN EXISTS (SELECT 1 FROM sys.crypt_properties AS cp WHERE cp.class = 1 AND cp.major_id = o.object_id) THEN 1 ELSE 0 END AS is_signed, (SELECT COUNT(*) FROM sys.database_permissions AS dp WHERE dp.class = 1 AND dp.major_id = o.object_id AND dp.permission_name = 'EXECUTE' AND dp.state IN ('G','W') AND dp.grantee_principal_id = DATABASE_PRINCIPAL_ID(@automation)) AS automation_can_execute FROM sys.procedures AS o WHERE o.name LIKE N'etp[_]operations[_]%'") }
        catch { $errors.Add("brokers: $($_.Exception.Message)") }
        if (@($f.databases | Where-Object { $_.name -eq 'EtpReporting' }).Count -eq 1) {
            try { $f.journal = (Invoke-P6Sql -Database 'EtpReporting' -Query "SELECT migration_id, CONVERT(varchar(23), applied_utc, 126) AS applied_utc FROM dbo.schema_migrations ORDER BY migration_id") }
            catch { $errors.Add("journal: $($_.Exception.Message)") }
            try { $f.users = (Invoke-P6Sql -Database 'EtpReporting' -Query "SELECT u.windows_identity, u.role_code, u.is_active, CASE WHEN SUSER_ID(u.windows_identity) IS NULL THEN 0 ELSE 1 END AS has_own_login, CASE WHEN EXISTS (SELECT 1 FROM sys.server_permissions AS sp WHERE sp.class = 100 AND sp.grantee_principal_id = SUSER_ID(u.windows_identity) AND sp.permission_name = N'ALTER ANY LOGIN' AND sp.state = 'W') THEN 1 ELSE 0 END AS alter_any_login_with_grant FROM dbo.application_users AS u ORDER BY u.role_code, u.windows_identity") }
            catch { $errors.Add("users: $($_.Exception.Message)") }
            try { $f.rowCounts = @(Get-P6RowCounts) } catch { $errors.Add("row counts: $($_.Exception.Message)") }
            try { $f.fingerprint = @(Get-P6Fingerprint) } catch { $errors.Add("store totals: $($_.Exception.Message)") }
            try { $f.watchFolders = (Invoke-P6Sql -Database 'EtpReporting' -Query "SELECT CONVERT(int, is_enabled) AS is_enabled, inbound_path, processed_path, failed_path, report_output_path FROM dbo.watch_folder_settings")[0] }
            catch { $errors.Add("watch folders: $($_.Exception.Message)") }
            try {
                # Assign first: Invoke-P6Sql returns its rows as one array object, which a pipe or @( ) would not unroll.
                $roleRows = Invoke-P6Sql -Database 'EtpReporting' -Parameters @{ automation = $automationPrincipal } -Query "SELECT r.name AS role_name FROM sys.database_role_members AS drm JOIN sys.database_principals AS r ON r.principal_id = drm.role_principal_id JOIN sys.database_principals AS m ON m.principal_id = drm.member_principal_id WHERE m.sid = SUSER_SID(@automation) ORDER BY r.name"
                $f.automationRoles = @($roleRows | ForEach-Object { [string]$_.role_name })
            }
            catch { $errors.Add("automation roles: $($_.Exception.Message)") }
            try { $f.drillResults = (Invoke-P6Sql -Database 'EtpReporting' -Query "IF OBJECT_ID(N'dbo.recovery_drill_results', N'U') IS NOT NULL SELECT TOP (3) * FROM dbo.recovery_drill_results ORDER BY 1 DESC") }
            catch { $errors.Add("drill results: $($_.Exception.Message)") }
        }
    }
    $f.errors = $errors.ToArray()
    $f | ConvertTo-Json -Depth 8 -Compress
}

$NoEtpProcessBlock = { @(Get-Process -Name 'Etp.Reporting.Desktop' -ErrorAction SilentlyContinue).Count }

$StartProcBlock = {
    param([string]$FilePath, [string]$Arguments, [string]$StdOut, [string]$StdErr)
    $global:P6Proc = $null
    $global:P6ProcStartUtc = (Get-Date).ToUniversalTime()
    $start = @{ FilePath = $FilePath; ArgumentList = $Arguments; PassThru = $true }
    if ($StdOut) { $start.RedirectStandardOutput = $StdOut; $start.RedirectStandardError = $StdErr }
    $global:P6Proc = Start-Process @start
    $null = $global:P6Proc.Handle   # keeps ExitCode readable after the process ends
    [string]$global:P6Proc.Id
}

$PollBlock = {
    param([bool]$WatchLog)
    $r = [ordered]@{ status = 'NOPROCESS'; exitCode = $null; lastLine = '' }
    $p = $global:P6Proc
    if ($null -ne $p) { if ($p.HasExited) { $r.status = 'EXIT'; $r.exitCode = $p.ExitCode } else { $r.status = 'RUNNING' } }
    if ($WatchLog -and $global:P6ProcStartUtc) {
        $log = Get-ChildItem -LiteralPath 'C:\ProgramData\EtpReporting\SetupLogs' -Filter 'bootstrap-*.log' -File -ErrorAction SilentlyContinue |
            Where-Object { $_.LastWriteTimeUtc -ge $global:P6ProcStartUtc } | Sort-Object LastWriteTimeUtc | Select-Object -Last 1
        if ($log) { $r.lastLine = [string](Get-Content -LiteralPath $log.FullName -Tail 1 -ErrorAction SilentlyContinue) }
    }
    [pscustomobject]$r
}

# Used only after the session had to be reopened: the process object is gone, so look for it by name.
$PollByNameBlock = {
    $running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like 'EtpReportingEngine-Setup*' -or $_.ProcessName -eq 'Etp.Reporting.Desktop' })
    if ($running.Count -gt 0) { [pscustomobject]@{ status = 'RUNNING'; exitCode = $null; lastLine = '' } }
    else { [pscustomobject]@{ status = 'NOPROCESS'; exitCode = $null; lastLine = '' } }
}

$CollectSetupBlock = {
    param([string]$GuestDir, [string]$SinceUtc)
    $since = [datetime]::Parse($SinceUtc, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
    New-Item -ItemType Directory -Path $GuestDir -Force | Out-Null
    $r = [ordered]@{ bootstrapLogs = @(); lastLines = @(); keyLines = @(); completed = $false; dbccPassed = $false; freshMachine = $false
        preMigrationBackup = $false; custodyRefusal = $false; failedLine = ''; markers = [ordered]@{}; innoTail = @(); innoRefusedAtStart = $false
        # 1.9.7 (Import-EtpSecurityModule in scripts\bootstrap-etp-prerequisites.ps1): the security-module lines of the newest log, and
        # whether the INSTALLED bootstrap script carries the two new functions.
        lineCount = 0; moduleLines = @(); moduleFailedAttempts = 0; moduleLoadedOnRetry = ''; moduleGaveUp = ''
        installedBootstrapHasRetry = $false; installedBootstrapHasEarlyLog = $false }
    $logs = @(Get-ChildItem -LiteralPath 'C:\ProgramData\EtpReporting\SetupLogs' -Filter 'bootstrap-*.log' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTimeUtc -ge $since } | Sort-Object LastWriteTimeUtc)
    foreach ($l in $logs) { Copy-Item -LiteralPath $l.FullName -Destination $GuestDir -Force }
    $r.bootstrapLogs = @($logs | ForEach-Object { $_.Name })
    if ($logs.Count -gt 0) {
        $lines = @(Get-Content -LiteralPath $logs[-1].FullName -ErrorAction SilentlyContinue | Where-Object { $_ -and $_.Trim() })
        $text = $lines -join "`n"
        $r.lastLines = @($lines | Select-Object -Last 40)
        $r.keyLines = @($lines | Where-Object { $_ -match 'FAILED:|WARNING|NEXT STEP|NOTE:|completed successfully|DBCC|pre-migration|no ETP machine configuration|encrypts backups|operations module|Owner|tasks are installed' })
        $r.completed = ($lines.Count -gt 0) -and ($lines[-1] -match 'ETP prerequisite bootstrap completed successfully')
        $r.dbccPassed = $text -match 'DBCC integrity checks passed'
        $r.freshMachine = $text -match 'This PC had no ETP machine configuration'
        $r.preMigrationBackup = $text -match 'Verified pre-migration backup is retained at'
        $r.custodyRefusal = $text -match 'encrypts backups, so the pre-migration backup needs exported recovery keys'
        $failed = @($lines | Where-Object { $_ -match 'FAILED:' } | Select-Object -First 1)
        if ($failed.Count -gt 0) { $r.failedLine = [string]$failed[0] }
        $r.lineCount = $lines.Count
        # Exact texts from bootstrap-etp-prerequisites.ps1 (1.9.7): line 501 (each failed attempt), 504 (loaded after a retry), 509 via the
        # trap on 561 (gave up: "FAILED: ... could not be loaded in 13 attempts over 60 seconds"). A first-attempt success writes none of them.
        $r.moduleLines = @($lines | Where-Object { $_ -match 'PowerShell security module' } | ForEach-Object { [string]$_ })
        $r.moduleFailedAttempts = @($lines | Where-Object { $_ -match 'Attempt \d+ of \d+ to load the PowerShell security module \(Microsoft\.PowerShell\.Security, needed for Get-Acl\) failed:' }).Count
        $loadedLine = @($lines | Where-Object { $_ -match 'The PowerShell security module loaded on attempt \d+ of \d+\.' } | Select-Object -First 1)
        if ($loadedLine.Count -gt 0) { $r.moduleLoadedOnRetry = [string]$loadedLine[0] }
        $gaveUpLine = @($lines | Where-Object { $_ -match 'FAILED:.*The PowerShell security module \(Microsoft\.PowerShell\.Security, needed for Get-Acl\) could not be loaded in \d+ attempts' } | Select-Object -First 1)
        if ($gaveUpLine.Count -gt 0) { $r.moduleGaveUp = [string]$gaveUpLine[0] }
    }
    $appDir = Get-P6AppDir
    $bootstrapScript = Join-Path $appDir 'scripts\bootstrap-etp-prerequisites.ps1'
    if (Test-Path -LiteralPath $bootstrapScript) {
        $scriptText = Get-Content -Raw -LiteralPath $bootstrapScript -ErrorAction SilentlyContinue
        $r.installedBootstrapHasRetry = [bool]($scriptText -match 'function Import-EtpSecurityModule')
        $r.installedBootstrapHasEarlyLog = [bool]($scriptText -match 'function Test-EtpSetupLogDirectoryTrusted')
    }
    $incomplete = Join-Path $appDir 'SETUP-INCOMPLETE.txt'
    $r.markers = [ordered]@{ setupIncomplete = (Test-Path -LiteralPath $incomplete); restorePending = (Test-Path -LiteralPath (Join-Path $appDir 'DATABASE-RESTORE-PENDING.txt')); incompleteText = '' }
    if ($r.markers.setupIncomplete) { $r.markers.incompleteText = ((Get-Content -LiteralPath $incomplete -ErrorAction SilentlyContinue | Select-Object -First 2) -join ' ') }
    $inno = Join-Path $GuestDir 'setup-inno.log'
    if (Test-Path -LiteralPath $inno) {
        $innoLines = @(Get-Content -LiteralPath $inno -ErrorAction SilentlyContinue)
        $r.innoTail = @($innoLines | Select-Object -Last 25)
        $r.innoRefusedAtStart = [bool](@($innoLines | Where-Object { $_ -match 'InitializeSetup returned False' }).Count)
    }
    $r | ConvertTo-Json -Depth 6 -Compress
}

$ImportPrepBlock = {
    param([string]$ZipPath, [string]$GuestDir, [string]$ZipName = 'Retail.zip')
    $r = [ordered]@{ ok = $false; reason = ''; enabled = $false; inbound = ''; sinceUtc = ''; appExe = '' }
    New-Item -ItemType Directory -Path $GuestDir -Force | Out-Null
    $w = $null
    try { $w = (Invoke-P6Sql -Database 'EtpReporting' -Query 'SELECT CONVERT(int, is_enabled) AS is_enabled, inbound_path, processed_path, failed_path, report_output_path FROM dbo.watch_folder_settings')[0] }
    catch { $r.reason = "could not read the watch-folder settings: $($_.Exception.Message)"; return ($r | ConvertTo-Json -Compress) }
    $r.enabled = ([int]$w.is_enabled -eq 1)
    $r.inbound = [string]$w.inbound_path
    if (-not $r.enabled) { $r.reason = 'watch-folder automation is turned off in the database'; return ($r | ConvertTo-Json -Compress) }
    $global:P6Watch = $w
    # A restored database keeps the watch-folder paths of the PC it came from (the rehearsal base: the old laptop).
    try {
        foreach ($d in @($w.inbound_path, $w.processed_path, $w.failed_path, $w.report_output_path)) {
            if (-not (Test-Path -LiteralPath $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
        }
    }
    catch { $r.reason = "a watch folder in the database cannot be used on this PC ($($_.Exception.Message)); inbound=$($w.inbound_path)"; return ($r | ConvertTo-Json -Compress) }
    $global:P6Before = @{}
    foreach ($d in @($w.inbound_path, $w.processed_path, (Join-Path $w.processed_path 'Duplicate'), $w.failed_path, $w.report_output_path)) {
        $global:P6Before[$d] = @(Get-ChildItem -LiteralPath $d -File -ErrorAction SilentlyContinue | ForEach-Object { $_.Name })
    }
    $r.appExe = Join-Path (Get-P6AppDir) 'Etp.Reporting.Desktop.exe'
    $since = (Get-Date).ToUniversalTime()
    $global:P6ImportSinceUtc = $since
    $target = Join-Path $w.inbound_path $ZipName
    $global:P6ZipName = $ZipName
    $global:P6ZipTarget = $target
    Copy-Item -LiteralPath $ZipPath -Destination $target -Force
    # The service skips a file written in the last 10 seconds; mark it settled.
    (Get-Item -LiteralPath $target).LastWriteTimeUtc = $since.AddMinutes(-1)
    $r.sinceUtc = $since.ToString('o')
    $r.ok = $true
    $r | ConvertTo-Json -Compress
}

$ImportResultBlock = {
    param([string]$VmOnlyDir)
    $since = $global:P6ImportSinceUtc
    $w = $global:P6Watch
    $sinceText = $since.ToString('yyyy-MM-ddTHH:mm:ss.fff')
    $r = [ordered]@{ attempts = @(); runs = @(); zipNow = @(); newPackFiles = @(); diagnostics = @(); stderrChars = 0; rowCounts = @(); fingerprint = @(); errors = @() }
    try { $r.attempts = (Invoke-P6Sql -Database 'EtpReporting' -Parameters @{ since = $sinceText } -Query "SELECT file_name, report_code, store_code, CONVERT(varchar(10), period_start, 23) AS period_start, CONVERT(varchar(10), period_end, 23) AS period_end, outcome, rows_processed, new_rows, already_present_rows, conflict_rows FROM dbo.import_attempts WHERE recorded_utc >= CONVERT(datetime2(3), @since, 126) ORDER BY import_attempt_id") }
    catch { $r.errors += "attempts: $($_.Exception.Message)" }
    try { $r.runs = (Invoke-P6Sql -Database 'EtpReporting' -Parameters @{ since = $sinceText } -Query "SELECT run_type, source_file_name, store_code, CONVERT(varchar(10), business_date, 23) AS business_date, outcome, safe_message, run_by FROM dbo.automation_runs WHERE started_utc >= CONVERT(datetime2(3), @since, 126) ORDER BY automation_run_id") }
    catch { $r.errors += "automation runs: $($_.Exception.Message)" }
    foreach ($d in @($global:P6Before.Keys)) {
        foreach ($n in @(Get-ChildItem -LiteralPath $d -File -ErrorAction SilentlyContinue | ForEach-Object { $_.Name })) {
            if ($n -like "*$($global:P6ZipName)" -and ($global:P6Before[$d] -notcontains $n -or $d -eq $w.inbound_path)) { $r.zipNow += "$d\$n" }
        }
    }
    $r.newPackFiles = @(Get-ChildItem -LiteralPath $w.report_output_path -File -ErrorAction SilentlyContinue |
        Where-Object { $global:P6Before[$w.report_output_path] -notcontains $_.Name } | ForEach-Object { [ordered]@{ name = $_.Name; bytes = $_.Length } })
    # Diagnostics are listed, not copied: an exception message can quote an invoice number.
    $diagRoot = Join-Path $env:LOCALAPPDATA 'EtpReporting'
    if (Test-Path -LiteralPath $diagRoot) {
        $r.diagnostics = @(Get-ChildItem -LiteralPath $diagRoot -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { $_.LastWriteTimeUtc -ge $since -and $_.Name -ne 'settings.json' } | ForEach-Object { "$($_.FullName) ($($_.Length) bytes)" })
    }
    $stderr = Join-Path $VmOnlyDir 'stderr.txt'
    if (Test-Path -LiteralPath $stderr) { $r.stderrChars = (Get-Item -LiteralPath $stderr).Length }
    try { $r.rowCounts = @(Get-P6RowCounts) } catch { $r.errors += "row counts: $($_.Exception.Message)" }
    try { $r.fingerprint = @(Get-P6Fingerprint) } catch { $r.errors += "store totals: $($_.Exception.Message)" }
    $r | ConvertTo-Json -Depth 6 -Compress
}

$RunTaskBlock = {
    param([string]$TaskName, [int]$TimeoutMinutes)
    $r = [ordered]@{ task = $TaskName; found = $false; started = ''; lastRun = ''; lastResult = $null; timedOut = $false }
    $t = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if (-not $t) { return ($r | ConvertTo-Json -Compress) }
    $r.found = $true
    $before = Get-Date
    $r.started = $before.ToString('s')
    # The monthly drill runner exits 0 doing nothing unless today is its -DayOfMonth (1). For the test, point the task at today
    # for this one run (same account, same script), and put the registered argument back afterwards.
    $origArgs = $null
    $drillDay = $null
    $action = @($t.Actions)[0]
    if ($TaskName -match 'Recovery Drill' -and $action.Arguments -match '-DayOfMonth\s+(\d+)') {
        if ($before.Day -le 28) {
            $origArgs = [string]$action.Arguments
            $drillDay = $before.Day
            $action.Arguments = $origArgs -replace '-DayOfMonth\s+\d+', "-DayOfMonth $drillDay"
            Set-ScheduledTask -TaskName $TaskName -Action $action | Out-Null
            $t = Get-ScheduledTask -TaskName $TaskName
        }
        else { $r.drillDayNote = "today is day $($before.Day) (runner accepts 1-28): the drill task would do nothing" }
    }
    try {
    Start-ScheduledTask -InputObject $t
    $deadline = $before.AddMinutes($TimeoutMinutes)
    Start-Sleep -Seconds 10
    while ($true) {
        $info = Get-ScheduledTaskInfo -InputObject $t
        $state = [string](Get-ScheduledTask -TaskName $TaskName).State
        # 267009 = still running.
        if ($info.LastRunTime -ge $before.AddSeconds(-5) -and $state -ne 'Running' -and [int64]$info.LastTaskResult -ne 267009) { break }
        if ((Get-Date) -gt $deadline) { $r.timedOut = $true; break }
        Start-Sleep -Seconds 15
    }
    }
    finally {
        if ($origArgs) {
            $a2 = @((Get-ScheduledTask -TaskName $TaskName).Actions)[0]
            $a2.Arguments = $origArgs
            Set-ScheduledTask -TaskName $TaskName -Action $a2 | Out-Null
        }
    }
    $info = Get-ScheduledTaskInfo -InputObject $t
    $r.lastRun = $info.LastRunTime.ToString('s')
    $r.lastResult = [int64]$info.LastTaskResult
    $r.drillDayUsed = $drillDay
    $r | ConvertTo-Json -Compress
}

$ReceiptsBlock = {
    param([string]$SinceUtc)
    $since = [datetime]::Parse($SinceUtc, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
    $dir = 'C:\ProgramData\EtpReporting\Backups'
    $r = [ordered]@{ verified = $null; drill = $null }
    $vp = Join-Path $dir 'EtpReporting-latest-verified.json'
    if (Test-Path -LiteralPath $vp) {
        $item = Get-Item -LiteralPath $vp
        $v = Get-Content -Raw -LiteralPath $vp | ConvertFrom-Json
        $names = @($v.PSObject.Properties | ForEach-Object { $_.Name })
        $bak = [string]$v.backupPath
        $lengthOk = $false
        if ($bak -and (Test-Path -LiteralPath $bak)) { $lengthOk = ((Get-Item -LiteralPath $bak).Length -eq [int64]$v.lengthBytes) }
        # Row counts are the four A4.4 tables (counts only, no business values).
        $counts = ''
        if ($names -contains 'rowCounts' -and $v.rowCounts) { $counts = (@($v.rowCounts.PSObject.Properties | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join '; ') }
        $r.verified = [ordered]@{ fresh = ($item.LastWriteTimeUtc -ge $since); encryption = [string]$v.encryption; hasRowCounts = ($names -contains 'rowCounts' -and [bool]$v.rowCounts)
            rowCounts = $counts; rowCountsNotRecorded = [string]$v.rowCountsNotRecorded; backupFileMatchesLength = $lengthOk; verifiedAtUtc = [string]$v.verifiedAtUtc; purpose = [string]$v.purpose }
    }
    $dp = Join-Path $dir 'EtpReporting-latest-drill.json'
    if (Test-Path -LiteralPath $dp) {
        $item = Get-Item -LiteralPath $dp
        $d = Get-Content -Raw -LiteralPath $dp | ConvertFrom-Json
        $scalars = @($d.PSObject.Properties | Where-Object { $_.Value -isnot [System.Management.Automation.PSCustomObject] -and $_.Value -isnot [array] } | ForEach-Object { "$($_.Name)=$($_.Value)" })
        $pairs = @($d.rowCounts | Where-Object { $_ } | ForEach-Object { (@($_.PSObject.Properties | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ',') })
        $r.drill = [ordered]@{ fresh = ($item.LastWriteTimeUtc -ge $since); succeeded = [bool]$d.succeeded; rowCountPairs = ($pairs -join ' | '); fields = ($scalars -join '; ') }
    }
    # The drill's own record (migration 0045): Matched = all four pairs equal.
    $r.drillRow = $null
    try {
        $row = (Invoke-P6Sql -Database 'EtpReporting' -Query "IF OBJECT_ID(N'dbo.recovery_drill_results', N'U') IS NOT NULL SELECT TOP (1) CONVERT(varchar(33), completed_utc, 126) AS completed_utc, outcome, row_counts_status, not_recorded_reason, sales_invoices_receipt, sales_invoices_restored, sales_lines_receipt, sales_lines_restored, import_files_receipt, import_files_restored, daily_reporting_days_receipt, daily_reporting_days_restored FROM dbo.recovery_drill_results ORDER BY recovery_drill_result_id DESC")
        if ($row.Count -gt 0) { $r.drillRow = $row[0] }
    }
    catch { $r.drillRow = [ordered]@{ error = $_.Exception.Message } }
    $r | ConvertTo-Json -Depth 5 -Compress
}

$InboundHasZipBlock = { if ($global:P6ZipTarget) { Test-Path -LiteralPath $global:P6ZipTarget } else { $false } }

# Rehearsal -UserChanges Script only. The same two statements Settings > Users runs (Phase2OperationsRepository.UpsertUserAsync):
# EXEC dbo.configure_application_role, then the MERGE into dbo.application_users, in one transaction per account.
# The session is elevated, so this does NOT prove the unelevated save (decision 7); the Ui mode does.
$UserChangeBlock = {
    param([string]$Identity, [string]$Role, [bool]$Active, [string]$DisplayName, [string]$Reason)
    $r = [ordered]@{ identity = $Identity; role = $Role; active = $Active; before = ''; action = ''; messages = @(); error = '' }
    $connection = New-Object System.Data.SqlClient.SqlConnection 'Data Source=.\SQLEXPRESS;Initial Catalog=EtpReporting;Integrated Security=SSPI;Connect Timeout=30;Application Name=ETP Phase 6 acceptance (user change)'
    $messages = New-Object System.Collections.Generic.List[string]
    # PRINT lines from the procedure (0043 prints what an Owner is missing); no business data.
    $connection.add_InfoMessage({ param($s, $e) $messages.Add([string]$e.Message) }.GetNewClosure())
    try {
        $connection.Open()
        $read = $connection.CreateCommand()
        $read.CommandText = 'SELECT role_code, CONVERT(int, is_active) AS is_active, display_name FROM dbo.application_users WHERE windows_identity=@identity'
        [void]$read.Parameters.AddWithValue('@identity', $Identity)
        $reader = $read.ExecuteReader()
        $existingName = ''
        if ($reader.Read()) { $r.before = "$($reader.GetString(0)) active=$($reader.GetInt32(1))"; $existingName = $reader.GetString(2) }
        $reader.Close()
        if ($r.before -eq "$Role active=$([int]$Active)") { $r.action = 'already in this state; not saved again'; return ($r | ConvertTo-Json -Compress) }
        if (-not $r.before -and -not $Active) { $r.action = 'not listed; nothing to deactivate'; return ($r | ConvertTo-Json -Compress) }
        if ($existingName) { $DisplayName = $existingName }
        $tx = $connection.BeginTransaction()
        try {
            $provision = $connection.CreateCommand()
            $provision.Transaction = $tx
            $provision.CommandText = 'EXEC dbo.configure_application_role @identity,@role,@active'
            [void]$provision.Parameters.AddWithValue('@identity', $Identity)
            [void]$provision.Parameters.AddWithValue('@role', $Role)
            [void]$provision.Parameters.AddWithValue('@active', $Active)
            [void]$provision.ExecuteNonQuery()
            $upsert = $connection.CreateCommand()
            $upsert.Transaction = $tx
            $upsert.CommandText = @'
MERGE dbo.application_users WITH(HOLDLOCK) AS target
USING(SELECT @identity windows_identity) source ON target.windows_identity=source.windows_identity
WHEN MATCHED THEN UPDATE SET display_name=@name,role_code=@role,is_active=@active,modified_by=SUSER_SNAME(),modified_utc=SYSUTCDATETIME(),change_reason=@reason
WHEN NOT MATCHED THEN INSERT(windows_identity,display_name,role_code,is_active,modified_by,change_reason)
  VALUES(@identity,@name,@role,@active,SUSER_SNAME(),@reason);
'@
            [void]$upsert.Parameters.AddWithValue('@identity', $Identity)
            [void]$upsert.Parameters.AddWithValue('@name', $DisplayName)
            [void]$upsert.Parameters.AddWithValue('@role', $Role)
            [void]$upsert.Parameters.AddWithValue('@active', $Active)
            [void]$upsert.Parameters.AddWithValue('@reason', $Reason)
            [void]$upsert.ExecuteNonQuery()
            $tx.Commit()
            $r.action = 'saved'
        }
        catch { try { $tx.Rollback() } catch { }; throw }
    }
    catch { $r.error = $_.Exception.Message; $r.action = 'refused; nothing changed' }
    finally { $connection.Dispose() }
    $r.messages = $messages.ToArray()
    $r | ConvertTo-Json -Compress
}

$ReportsBlock = {
    param([string]$Folder)
    $files = @(Get-ChildItem -LiteralPath $Folder -File -Recurse -ErrorAction SilentlyContinue | ForEach-Object { [ordered]@{ name = $_.Name; ext = $_.Extension.ToLowerInvariant(); bytes = $_.Length; lastWrite = $_.LastWriteTime.ToString('s') } })
    ConvertTo-Json -InputObject $files -Depth 3 -Compress
}

# 1.9.5 facts, SELECT only: AW330, the Service tables and views (0048), the 0047 location column, the 0046 catalogue
# versions, AW330 day rows, database space. Names and counts only.
$Release195FactsBlock = {
    $f = [ordered]@{ stores = @(); aw330 = $null; serviceTables = @(); serviceTableCount = 0; serviceLandingRows = $null; serviceLandingNonEmpty = ''; serviceViewCount = 0
        aw330Days = $null; locationColumn = $false; movements = $null; movementsNoLocation = $null; defectiveBinRows = $null; kpiVersions = @(); dataUsedMb = $null; errors = @() }
    try { $f.stores = (Invoke-P6Sql -Database 'EtpReporting' -Query "SELECT store_code, CONVERT(int, is_active) AS is_active, CONVERT(varchar(20), business_unit_id) AS business_unit_id FROM dbo.stores ORDER BY store_code") }
    catch { $f.errors += "stores: $($_.Exception.Message)" }
    $f.aw330 = @($f.stores | Where-Object { [string]$_.store_code -eq 'AW330' }) | Select-Object -First 1
    try {
        $t = Invoke-P6Sql -Database 'EtpReporting' -Query "SELECT t.name AS table_name, SUM(p.rows) AS row_count FROM sys.tables AS t JOIN sys.partitions AS p ON p.object_id = t.object_id AND p.index_id IN (0, 1) WHERE t.name LIKE N'etp[_]landing[_]s[0-9]%' OR t.name LIKE N'%gprc%' GROUP BY t.name ORDER BY t.name"
        $f.serviceTables = @($t | ForEach-Object { [string]$_.table_name })
        $f.serviceTableCount = @($t).Count
        $f.serviceLandingRows = [int64](@($t) | Measure-Object -Property row_count -Sum).Sum
        $f.serviceLandingNonEmpty = (@($t | Where-Object { [int64]$_.row_count -gt 0 } | ForEach-Object { "$($_.table_name)=$($_.row_count)" }) -join '; ')
    }
    catch { $f.errors += "service tables: $($_.Exception.Message)" }
    try { $f.serviceViewCount = [int]((Invoke-P6Sql -Database 'EtpReporting' -Query "SELECT COUNT(*) AS n FROM sys.views WHERE name LIKE N'v[_]service[_]%'")[0].n) }
    catch { $f.errors += "service views: $($_.Exception.Message)" }
    try { $f.aw330Days = [int]((Invoke-P6Sql -Database 'EtpReporting' -Query "SELECT COUNT(*) AS n FROM dbo.daily_reporting_days WHERE store_code = N'AW330'")[0].n) }
    catch { $f.errors += "AW330 days: $($_.Exception.Message)" }
    try {
        $f.locationColumn = [bool]((Invoke-P6Sql -Database 'EtpReporting' -Query "SELECT CASE WHEN COL_LENGTH(N'dbo.stock_movements', N'location') IS NULL THEN 0 ELSE 1 END AS has_col")[0].has_col)
        if ($f.locationColumn) {
            $m = (Invoke-P6Sql -Database 'EtpReporting' -Query "EXEC sys.sp_executesql N'SELECT COUNT_BIG(*) AS total, SUM(CASE WHEN location IS NULL THEN 1 ELSE 0 END) AS no_location, SUM(CASE WHEN location = N''DEFECTIVEBIN'' THEN 1 ELSE 0 END) AS defective FROM dbo.stock_movements'")[0]
            $f.movements = [int64]$m.total; $f.movementsNoLocation = [int64]$m.no_location; $f.defectiveBinRows = [int64]$m.defective
        }
    }
    catch { $f.errors += "stock_movements.location: $($_.Exception.Message)" }
    try { $f.kpiVersions = (Invoke-P6Sql -Database 'EtpReporting' -Query "SELECT kpi_code, version FROM dbo.kpi_catalogue WHERE kpi_code IN (N'NET_SALES', N'INVOICE_COUNT') ORDER BY kpi_code") }
    catch { $f.errors += "kpi versions: $($_.Exception.Message)" }
    try { $f.dataUsedMb = [decimal]((Invoke-P6Sql -Database 'EtpReporting' -Query "SELECT CONVERT(decimal(12,1), SUM(CONVERT(bigint, FILEPROPERTY(name, 'SpaceUsed'))) * 8 / 1024.0) AS mb FROM sys.database_files WHERE type = 0")[0].mb) }
    catch { $f.errors += "space: $($_.Exception.Message)" }
    $f | ConvertTo-Json -Depth 6 -Compress
}

$GuestNowBlock = { (Get-Date).ToUniversalTime().ToString('o') }

# Import attempts and automation runs since a time (for imports Sagar makes in the app), plus row counts and store totals.
$AttemptsSinceBlock = {
    param([string]$SinceUtc)
    $since = [datetime]::Parse($SinceUtc, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
    $sinceText = $since.ToString('yyyy-MM-ddTHH:mm:ss.fff')
    $r = [ordered]@{ attempts = @(); runs = @(); rowCounts = @(); fingerprint = @(); errors = @() }
    try { $r.attempts = (Invoke-P6Sql -Database 'EtpReporting' -Parameters @{ since = $sinceText } -Query "SELECT file_name, report_code, store_code, CONVERT(varchar(10), period_start, 23) AS period_start, CONVERT(varchar(10), period_end, 23) AS period_end, outcome, rows_processed, new_rows, already_present_rows, conflict_rows FROM dbo.import_attempts WHERE recorded_utc >= CONVERT(datetime2(3), @since, 126) ORDER BY import_attempt_id") }
    catch { $r.errors += "attempts: $($_.Exception.Message)" }
    try { $r.runs = (Invoke-P6Sql -Database 'EtpReporting' -Parameters @{ since = $sinceText } -Query "SELECT run_type, store_code, CONVERT(varchar(10), business_date, 23) AS business_date, outcome FROM dbo.automation_runs WHERE started_utc >= CONVERT(datetime2(3), @since, 126) ORDER BY automation_run_id") }
    catch { $r.errors += "automation runs: $($_.Exception.Message)" }
    try { $r.rowCounts = @(Get-P6RowCounts) } catch { $r.errors += "row counts: $($_.Exception.Message)" }
    try { $r.fingerprint = @(Get-P6Fingerprint) } catch { $r.errors += "store totals: $($_.Exception.Message)" }
    $r | ConvertTo-Json -Depth 6 -Compress
}

# S004 money per date and tender (0048 view v_service_s004_daily) against expected totals. Compared here, in the guest:
# only match / no match and the tenders that differ leave the VM, never an amount.
$S004CheckBlock = {
    param([string]$ExpectedJson)
    # Windows PowerShell 5.1 ConvertFrom-Json returns a JSON array as ONE object (a plain @() keeps it as one element), so $e.Date became all
    # four dates joined and the date conversion failed (7 Oct). Flatten one level so each element is one date.
    $expected = @()
    foreach ($item in @(ConvertFrom-Json -InputObject $ExpectedJson)) { foreach ($e1 in $item) { $expected += $e1 } }
    $out = @()
    $viewFound = $false
    try { $viewFound = [bool]((Invoke-P6Sql -Database 'EtpReporting' -Query "SELECT CASE WHEN OBJECT_ID(N'dbo.v_service_s004_daily', N'V') IS NULL THEN 0 ELSE 1 END AS v")[0].v) } catch { }
    foreach ($e in $expected) {
        $row = [ordered]@{ date = [string]$e.Date; viewFound = $viewFound; match = $false; differing = ''; error = '' }
        if ($viewFound) {
            try {
                $rows = Invoke-P6Sql -Database 'EtpReporting' -Parameters @{ d = [string]$e.Date } -Query "SELECT tender, CONVERT(decimal(19,2), SUM(amount)) AS amount FROM dbo.v_service_s004_daily WHERE business_date = CONVERT(date, @d, 23) GROUP BY tender"
                $got = @{}; foreach ($x in @($rows)) { $got[[string]$x.tender] = [decimal]$(if ($null -eq $x.amount) { 0 } else { $x.amount }) }
                $diff = @()
                foreach ($pair in @(@('CASH', [decimal]$e.Cash), @('CARD', [decimal]$e.Card), @('UPI', [decimal]$e.Upi))) {
                    $have = [decimal]0; if ($got.ContainsKey($pair[0])) { $have = $got[$pair[0]] }
                    if ([math]::Abs($have - $pair[1]) -gt 0.5) { $diff += $pair[0] }
                }
                foreach ($k in @($got.Keys | Where-Object { @('CASH', 'CARD', 'UPI') -notcontains $_ })) { if ([math]::Abs($got[$k]) -gt 0.5) { $diff += "$k (non-zero)" } }
                $row.match = ($diff.Count -eq 0) -and (@($rows).Count -gt 0)
                $row.differing = ($diff -join ', ')
                if (@($rows).Count -eq 0) { $row.differing = 'no S004 rows for this date' }
            }
            catch { $row.error = $_.Exception.Message }
        }
        $out += [pscustomobject]$row
    }
    ConvertTo-Json -InputObject @($out) -Depth 3 -Compress
}

# ---------------------------------------------------------------- steps
function Get-Facts([string]$Phase) {
    Write-Host "  collecting the guest state ($Phase)"
    $facts = Invoke-GuestJson $GuestFactsBlock @($Phase)
    Repair-RowSets $facts @('etpUninstallEntries', 'sqlProtocols', 'sqlSysadmins', 'databases', 'brokers', 'journal', 'users', 'rowCounts', 'fingerprint', 'automationRoles', 'drillResults', 'backupFiles', 'tasks', 'acls', 'bundledMigrations') @('sqlServer', 'watchFolders')
    Save-Json "facts-$Phase.json" $facts
    foreach ($e in @($facts.errors)) { if ($e) { Write-Host "    guest note: $e" -ForegroundColor Yellow } }
    return $facts
}

function Wait-NoEtpProcess([int]$Minutes = 10, [switch]$AskSagar) {
    $deadline = (Get-Date).AddMinutes($Minutes)
    while ($true) {
        $n = [int](Invoke-Guest $NoEtpProcessBlock)
        if ($n -eq 0) { return }
        if ($AskSagar) {
            Read-Host "  ETP is running in the VM ($n process). Close it in the VM console (do not kill it), then press Enter" | Out-Null
            continue
        }
        # The five-minute automation task starts ETP briefly; setup refuses while any copy holds its mutex.
        if ((Get-Date) -gt $deadline) { throw "Etp.Reporting.Desktop is still running in the VM after $Minutes minutes. Setup refuses to run while it is open. Close it and run again." }
        Start-Sleep -Seconds 15
    }
}

function Wait-GuestProcess {
    param([string]$What, [int]$TimeoutMinutes, [switch]$WatchBootstrapLog)
    $start = Get-Date
    $deadline = $start.AddMinutes($TimeoutMinutes)
    $lastLine = ''
    $lastPrint = $start
    $byName = $false   # after a reconnect the new session has no process object, so watch by process name
    while ($true) {
        Start-Sleep -Seconds 20
        $state = $null
        try {
            if ($byName) { $state = Invoke-Guest $PollByNameBlock } else { $state = Invoke-Guest $PollBlock @([bool]$WatchBootstrapLog) }
        }
        catch {
            Write-Host "  the guest session dropped ($($_.Exception.Message)); reconnecting" -ForegroundColor Yellow
            Wait-Heartbeat
            Connect-Guest
            $byName = $true
            $state = Invoke-Guest $PollByNameBlock
        }
        $elapsed = [int]((Get-Date) - $start).TotalMinutes
        if ($state.lastLine -and $state.lastLine -ne $lastLine) { $lastLine = [string]$state.lastLine; $lastPrint = Get-Date; Write-Host "  [$elapsed min] $(Get-LogMessage $lastLine)" }
        elseif (((Get-Date) - $lastPrint).TotalMinutes -ge 3) { $lastPrint = Get-Date; Write-Host "  [$elapsed min] $What still running" }
        if ($state.status -eq 'EXIT') { Write-Host "  $What finished after $elapsed min, exit code $($state.exitCode)"; return $state.exitCode }
        if ($state.status -eq 'NOPROCESS') { Write-Host "  $What : no process left to watch (the session was reset); exit code unknown" -ForegroundColor Yellow; return $null }
        if ((Get-Date) -gt $deadline) { throw "$What was still running after $TimeoutMinutes minutes. It was left running. Look at the VM console before doing anything else." }
    }
}

function Invoke-Setup {
    param([string]$Label, [string]$GuestInstaller, [string]$Tasks, [switch]$AskToCloseApp, [switch]$NoRetry)
    Write-Step "Setup: $(Split-Path -Leaf $GuestInstaller), tasks '$Tasks' ($Label)"
    # 4 Oct: the five-minute automation task restarted ETP in the same second setup began (exit 5, "unable to close
    # applications"). Pause the ETP tasks for the setup run; they are enabled again right after it.
    $pausedTasks = @(Invoke-Guest { Get-ScheduledTask -TaskName 'ETP Reporting*' -ErrorAction SilentlyContinue | Where-Object State -ne 'Disabled' | ForEach-Object { Disable-ScheduledTask -TaskName $_.TaskName -TaskPath $_.TaskPath | Out-Null; $_.TaskPath + '|' + $_.TaskName } })
    Write-Host "  paused guest tasks: $($pausedTasks -join ', ')"
    Wait-NoEtpProcess -AskSagar:$AskToCloseApp
    $guestDir = "$($script:GuestRunDir)\$Label"
    $innoLog = "$guestDir\setup-inno.log"
    $argString = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /LOG="{0}" /TASKS="{1}"' -f $innoLog, $Tasks
    $sinceUtc = [string](Invoke-Guest { param($d) New-Item -ItemType Directory -Path $d -Force | Out-Null; (Get-Date).ToUniversalTime().ToString('o') } @($guestDir))
    Write-Host "  $GuestInstaller $argString"
    $exitCode = $null
    if ($SetupLauncher -eq 'Console') {
        $consoleCommand = "`$p = Start-Process -FilePath '$GuestInstaller' -ArgumentList '$argString' -Wait -PassThru; 'Setup exit code: ' + `$p.ExitCode"
        Write-Host ''
        Write-Host '  Start setup from the VM console now:' -ForegroundColor Yellow
        Write-Host "   1. vmconnect.exe localhost $VmName   (sign in as ETPTest; Sagar types the password)"
        Write-Host '   2. Start > Windows PowerShell > right-click > Run as administrator > Yes'
        Write-Host '   3. Paste this one line and press Enter:'
        Write-Host "      $consoleCommand"
        $typed = Read-Host '   4. When it prints "Setup exit code: N", type N here and press Enter'
        $n = 0
        if ([int]::TryParse(([string]$typed).Trim(), [ref]$n)) { $exitCode = $n }
    }
    else {
        [void](Invoke-Guest $StartProcBlock @($GuestInstaller, $argString, '', ''))
        $exitCode = Wait-GuestProcess -What "setup ($Label)" -TimeoutMinutes $SetupTimeoutMinutes -WatchBootstrapLog
    }
    $collected = Invoke-GuestJson $CollectSetupBlock @($guestDir, $sinceUtc)
    Copy-FromGuest $Label
    $run = [pscustomobject]@{ Label = $Label; Tasks = $Tasks; ExitCode = $exitCode; SinceUtc = $sinceUtc; Collected = $collected }
    Save-Json "$Label\setup-result.json" $run
    if ($pausedTasks.Count -gt 0) {
        Invoke-Guest { param($list) foreach ($t in $list) { $p, $n = $t -split '\|', 2; Enable-ScheduledTask -TaskName $n -TaskPath $p -ErrorAction SilentlyContinue | Out-Null } } @(, $pausedTasks) | Out-Null
        Write-Host "  re-enabled guest tasks: $($pausedTasks -join ', ')"
    }
    # Setup refuses at once while any ETP process holds the application mutex (the five-minute task can).
    # The exit code of a refusal in InitializeSetup is Inno's own (1) or the script's 1603; accept either, but only
    # when no bootstrap log was written and the Inno log shows the refusal or the mutex.
    $refusedAtStart = [bool]$collected.innoRefusedAtStart -or [bool](@($collected.innoTail | Where-Object { $_ -match '(?i)mutex|is running|unable to automatically close|could not be shut down' }).Count)
    if (-not $NoRetry -and $null -ne $exitCode -and $exitCode -ne 0 -and @($collected.bootstrapLogs).Count -eq 0 -and $refusedAtStart) {
        $script:Notes.Add("$Label`: setup refused at start (ETP was running, most likely the five-minute automation task). Retried once. The shop PC can hit the same race.")
        Write-Host '  setup refused to start because ETP was running; waiting and retrying once' -ForegroundColor Yellow
        Start-Sleep -Seconds 30
        return (Invoke-Setup -Label "$Label-retry" -GuestInstaller $GuestInstaller -Tasks $Tasks -AskToCloseApp:$AskToCloseApp -NoRetry)
    }
    return $run
}

function Invoke-HeadlessImport {
    param([string]$Label, [string]$GuestZip = "$GuestStageDir\Data\Retail.zip", [string]$ZipName = 'Retail.zip', [string]$What = 'the two store folders')
    Write-Step "Headless import of $What ($Label)"
    Wait-NoEtpProcess
    $guestDir = "$($script:GuestRunDir)\$Label"
    $vmOnly = "$($script:GuestRunDir)\vm-only\$Label"
    $prep = Invoke-GuestJson $ImportPrepBlock @($GuestZip, $guestDir, $ZipName)
    if (-not $prep.ok) {
        Add-Check 'Import' 'Watch-folder import could start' 'watch folders enabled and readable' $prep.reason 'FAIL'
        $script:UiSteps.Add('Import was not automated: ' + $prep.reason + '. In the app, check Operations > Watch folders, or import the file by hand (Imports > Intake).')
        return $null
    }
    Invoke-Guest { param($d) New-Item -ItemType Directory -Path $d -Force | Out-Null } @($vmOnly) | Out-Null
    Start-Sleep -Seconds 5
    # The five-minute task (EtpAutomation) runs the same command and may hold the unattended-run lease; our run then
    # returns at once and the zip waits in Inbound. Run again until the zip has left Inbound (at most 5 runs).
    $exit = $null
    $deadline = (Get-Date).AddMinutes($ImportTimeoutMinutes)
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        [void](Invoke-Guest $StartProcBlock @($prep.appExe, '--automation-once', "$vmOnly\stdout-$attempt.txt", "$vmOnly\stderr.txt"))
        $exit = Wait-GuestProcess -What "import ($Label)" -TimeoutMinutes ([math]::Max(5, [int]($deadline - (Get-Date)).TotalMinutes))
        Start-Sleep -Seconds 5
        if (-not [bool](Invoke-Guest $InboundHasZipBlock)) { break }
        if ((Get-Date) -gt $deadline) { break }
        Write-Host "  the zip is still in Inbound (another unattended run held the lease); running again in 30 s" -ForegroundColor Yellow
        Wait-NoEtpProcess
        Start-Sleep -Seconds 30
    }
    if ([bool](Invoke-Guest $InboundHasZipBlock)) { $script:Notes.Add("$Label`: the zip was still in Inbound after the import runs.") }
    $result = Invoke-GuestJson $ImportResultBlock @($vmOnly)
    Repair-RowSets $result @('attempts', 'runs', 'rowCounts', 'fingerprint', 'newPackFiles')
    Copy-FromGuest $Label
    Save-Csv "$Label\import-attempts.csv" $result.attempts
    Save-Csv "$Label\automation-runs.csv" $result.runs
    Save-Csv "$Label\row-counts.csv" $result.rowCounts
    Save-Csv "$Label\store-totals.csv" $result.fingerprint
    Save-Csv "$Label\report-pack-files.csv" $result.newPackFiles
    $diag = @($result.diagnostics)
    if ($diag.Count -gt 0 -or [int64]$result.stderrChars -gt 0) {
        $script:Notes.Add("$Label`: diagnostics left in the VM (not copied, they can quote invoice numbers): $($diag -join '; '); importer error output $($result.stderrChars) bytes at $vmOnly\stderr.txt")
    }
    foreach ($e in @($result.errors)) { if ($e) { Write-Host "    guest note: $e" -ForegroundColor Yellow } }
    return [pscustomobject]@{ Label = $Label; ExitCode = $exit; Result = $result }
}

# ---------------------------------------------------------------- checks
function Test-SetupRun {
    param($Run, [string]$Area, [switch]$ExpectFresh, [switch]$ExpectCustodyRefusal, [switch]$InfoOnly)
    $c = $Run.Collected
    $fail = 'FAIL'
    if ($InfoOnly) { $fail = 'REVIEW' }
    $exitText = 'unknown'
    if ($null -ne $Run.ExitCode) { $exitText = [string]$Run.ExitCode }
    $logText = 'none written by this run'
    if (@($c.bootstrapLogs).Count -gt 0) { $logText = (@($c.bootstrapLogs) -join ', ') }
    if ($ExpectCustodyRefusal) {
        Add-Check $Area 'Setup refuses the upgrade until the recovery keys are exported (edition that encrypts backups)' 'exit 1603 and the log names Settings > Database > Encrypted backup recovery keys' "exit $exitText; message found: $($c.custodyRefusal)" (Get-Result (($Run.ExitCode -eq 1603) -and [bool]$c.custodyRefusal))
        return
    }
    $exitResult = 'REVIEW'
    if ($null -ne $Run.ExitCode) { $exitResult = Get-Result ($Run.ExitCode -eq 0) $fail }
    Add-Check $Area 'Setup exit code' '0' $exitText $exitResult
    Add-Check $Area 'Bootstrap log of this run' 'last line "ETP prerequisite bootstrap completed successfully."' "$logText; completed=$($c.completed)" (Get-Result ([bool]$c.completed) $fail)
    Add-Check $Area 'Migration, journal count and DBCC CHECKDB passed (bootstrap)' 'line present' ([string]$c.dbccPassed) (Get-Result ([bool]$c.dbccPassed) $fail)
    if ($ExpectFresh) { Add-Check $Area 'New-PC path: SQL Server, folders, EtpAutomation and configuration created by setup' 'line present' ([string]$c.freshMachine) (Get-Result ([bool]$c.freshMachine) $fail) }
    if ($c.failedLine) { Add-Check $Area 'Failure line in the bootstrap log' 'none' (Get-LogMessage $c.failedLine) $fail }
    if ($c.markers.setupIncomplete) { Add-Check $Area 'SETUP-INCOMPLETE.txt' 'absent' $c.markers.incompleteText $fail }
    foreach ($line in @($c.keyLines | Where-Object { $_ -match 'WARNING|NEXT STEP|NOTE:' })) {
        $level = 'INFO'
        if ($line -match 'WARNING') { $level = 'REVIEW' }
        Add-Check $Area 'Bootstrap advice line' 'read it' (Get-LogMessage $line) $level
    }
}

# 1.9.7 bootstrap check (lane fix197/bootstrap, 197-BOOTSTRAP-DONE.md). scripts\bootstrap-etp-prerequisites.ps1 now (a) imports
# Microsoft.PowerShell.Security itself before the preflight - Import-EtpSecurityModule, up to 13 attempts 5 s apart (60 s) - and (b) trusts an
# existing protected SetupLogs folder at once - Test-EtpSetupLogDirectoryTrusted - so a preflight failure such as the 9 Oct Get-Acl one on
# Workpc writes %ProgramData%\EtpReporting\SetupLogs\bootstrap-*.log instead of vanishing on Inno's hidden console. The lines it writes:
#   "Attempt n of 13 to load the PowerShell security module (Microsoft.PowerShell.Security, needed for Get-Acl) failed: <reason>"   each failed try
#   "The PowerShell security module loaded on attempt n of 13."                                                                   success after a retry
#   "FAILED: ... The PowerShell security module (Microsoft.PowerShell.Security, needed for Get-Acl) could not be loaded in 13 attempts over 60 seconds. ..."
#                                                                                                                                 gave up; setup 1603
# A first-attempt success writes NONE of them (by design), so that case is proven by the installed script carrying both functions plus a log
# with no failure line. Run after every setup run of the 1.9.7 installer, in addition to Test-SetupRun.
function Test-BootstrapModuleLog {
    param($Run, [string]$Area)
    $c = $Run.Collected
    $asText = { param($x) @($x) | ForEach-Object { if ($_ -is [string]) { $_ } elseif ($null -ne $_ -and $_.PSObject.Properties['value']) { [string]$_.value } else { [string]$_ } } }
    $exitText = 'unknown'
    if ($null -ne $Run.ExitCode) { $exitText = [string]$Run.ExitCode }
    $exitOk = ($null -ne $Run.ExitCode) -and ([int]$Run.ExitCode -eq 0)
    $logs = @(& $asText $c.bootstrapLogs)
    $logText = 'MISSING: no bootstrap-*.log written by this run'
    if ($logs.Count -gt 0) { $logText = "$($logs -join ', ') ($($c.lineCount) lines)" }
    Add-Check $Area 'Bootstrap log of this setup run exists (1.9.7: SetupLogs trusted before the preflight)' 'setup exit 0 and %ProgramData%\EtpReporting\SetupLogs\bootstrap-*.log written by this run' "exit $exitText; $logText" (Get-Result ($exitOk -and ($logs.Count -gt 0)))
    Add-Check $Area 'Installed bootstrap carries the 1.9.7 change' 'Import-EtpSecurityModule and Test-EtpSetupLogDirectoryTrusted in scripts\bootstrap-etp-prerequisites.ps1' "retry function=$($c.installedBootstrapHasRetry); early-log function=$($c.installedBootstrapHasEarlyLog)" (Get-Result ([bool]$c.installedBootstrapHasRetry -and [bool]$c.installedBootstrapHasEarlyLog))
    $check = 'Security module import lines in the bootstrap log'
    $expected = 'no "Attempt n of 13 ... failed" line, or "The PowerShell security module loaded on attempt n of 13."; never the "could not be loaded in 13 attempts" FAILED line'
    if ($logs.Count -eq 0) { Add-Check $Area $check $expected 'no log to read (see the check above)' 'FAIL'; return }
    $failedAttempts = [int]$c.moduleFailedAttempts
    $loaded = [string]$c.moduleLoadedOnRetry
    $gaveUp = [string]$c.moduleGaveUp
    if ($gaveUp) {
        Add-Check $Area $check $expected "gave up after $failedAttempts failed attempt(s): $(Get-LogMessage $gaveUp)" 'FAIL'
        $script:Notes.Add("$Area`: the Workpc symptom (9 Oct) recurred and the 60 s retry did NOT recover; read the bootstrap log in the evidence folder.")
    }
    elseif ($loaded) {
        Add-Check $Area $check $expected "$failedAttempts failed attempt(s), then: $(Get-LogMessage $loaded)" 'PASS'
        $script:Notes.Add("$Area`: the Workpc symptom (module not loadable right after the file copy) appeared in the VM and the 1.9.7 retry recovered from it.")
    }
    elseif ($failedAttempts -gt 0) { Add-Check $Area $check $expected "$failedAttempts failed attempt(s) but neither the loaded line nor the FAILED line; read the log" 'FAIL' }
    else { Add-Check $Area $check $expected "no module line: loaded at the first attempt (the bootstrap writes nothing then); exit $exitText; $($c.lineCount) log lines" (Get-Result ($exitOk -and ([int]$c.lineCount -gt 0))) }
    $moduleLines = @(& $asText $c.moduleLines)
    if ($moduleLines.Count -gt 0) { Add-Check $Area 'Security module lines (verbatim, timestamps removed)' 'for the record' (@($moduleLines | ForEach-Object { Get-LogMessage $_ }) -join ' || ') 'INFO' }
}

function Test-InstalledState {
    param($F, [string]$Area, [string]$ExpectedVersion, [switch]$NewInstance, [switch]$Upgrade, [switch]$StrictOwnerGrant)
    $soft = 'FAIL'
    if ($Upgrade) { $soft = 'REVIEW' }
    # Rehearsal: setup's one-off SYSTEM grant (decision 7) is what the live install relies on, so a miss is a FAIL.
    $grantLevel = 'REVIEW'
    if ($StrictOwnerGrant) { $grantLevel = 'FAIL' }
    $ver = [string]$F.exeProductVersion
    Add-Check $Area 'Installed application version' "$ExpectedVersion (then +commit)" $ver (Get-Result ($ver -like "$ExpectedVersion*"))
    $entries = @($F.etpUninstallEntries)
    $entryText = (@($entries | ForEach-Object { "$($_.name) [$($_.version)]" }) -join '; ')
    Add-Check $Area 'Programs and Features entry' "exactly one, version $ExpectedVersion" $entryText (Get-Result (($entries.Count -eq 1) -and ([string]$entries[0].version -eq $ExpectedVersion)))
    Add-Check $Area 'Setup markers' 'no SETUP-INCOMPLETE.txt, no DATABASE-RESTORE-PENDING.txt' "incomplete=$($F.markers.setupIncomplete); restorePending=$($F.markers.restorePending)" (Get-Result ((-not $F.markers.setupIncomplete) -and (-not $F.markers.restorePending)))
    Add-Check $Area 'SQL media deleted after setup' 'no SqlSetupMedia folder' "left behind=$($F.sqlSetupMediaLeft)" (Get-Result (-not $F.sqlSetupMediaLeft) 'REVIEW')

    $svc = $F.sqlService
    if ($null -eq $svc) { Add-Check $Area 'SQL Server service (SQLEXPRESS)' 'installed, Running, Auto' 'not installed' 'FAIL' }
    else { Add-Check $Area 'SQL Server service (SQLEXPRESS)' 'Running, Auto' "$($svc.state), $($svc.startMode), $($svc.startName)" (Get-Result (($svc.state -eq 'Running') -and ($svc.startMode -eq 'Auto'))) }
    Add-Check $Area 'SQL Server, ODBC and Sqlcmd products' 'listed for the record' (@($F.sqlProducts) -join '; ') 'INFO'

    $s = $F.sqlServer
    if ($null -eq $s) { Add-Check $Area 'SQL Server answers' 'a query over shared memory' ('no answer: ' + (@($F.errors) -join ' / ')) 'FAIL' }
    else {
        $verText = "$($s.product_version) $($s.edition), collation $($s.collation)"
        if ($NewInstance) {
            Add-Check $Area 'SQL Server version and edition' 'SQL Server 2025 (17.x) Express' $verText (Get-Result (([int]$s.major -eq 17) -and ([string]$s.edition -like '*Express*')))
            Add-Check $Area 'Server collation' 'Latin1_General_CI_AS' ([string]$s.collation) (Get-Result ([string]$s.collation -eq 'Latin1_General_CI_AS'))
            Add-Check $Area 'Windows authentication only' '1' ([string]$s.windows_only) (Get-Result ([int]$s.windows_only -eq 1))
            # One row per protocol key (value_data '0' or '1'). Disabled = every row for that protocol reads 0, and at
            # least one row was read. Compared as numbers, so '0', 0 and ' 0' all count as disabled.
            $protocolRows = Expand-Rows $F.sqlProtocols
            $tcpValues = @($protocolRows | Where-Object { [string]$_.protocol -eq 'TCP' } | ForEach-Object { ([string]$_.enabled).Trim() })
            $npValues = @($protocolRows | Where-Object { [string]$_.protocol -eq 'NP' } | ForEach-Object { ([string]$_.enabled).Trim() })
            $isOff = { param([string[]]$Values) ($Values.Count -gt 0) -and (@($Values | Where-Object { $v = -1; -not ([int]::TryParse($_, [ref]$v) -and $v -eq 0) }).Count -eq 0) }
            $tcpOff = [bool](& $isOff $tcpValues)
            $npOff = [bool](& $isOff $npValues)
            $tcpText = 'not read'; if ($tcpValues.Count -gt 0) { $tcpText = $tcpValues -join ',' }
            $npText = 'not read'; if ($npValues.Count -gt 0) { $npText = $npValues -join ',' }
            Add-Check $Area 'Network protocols' 'TCP/IP 0, named pipes 0' "TCP/IP $tcpText, named pipes $npText" (Get-Result ($tcpOff -and $npOff))
            $others = @($F.sqlSysadmins | Where-Object { (@('WINDOWS_LOGIN', 'WINDOWS_GROUP') -contains [string]$_.type_desc) -and -not $_.is_disabled -and [string]$_.name -ne 'BUILTIN\Administrators' -and [string]$_.name -notlike 'NT SERVICE\*' } | ForEach-Object { [string]$_.name })
            $othersText = 'none'
            if ($others.Count -gt 0) { $othersText = $others -join ', ' }
            Add-Check $Area 'Other Windows SQL administrators' 'none besides BUILTIN\Administrators and NT SERVICE accounts' $othersText (Get-Result ($others.Count -eq 0))
        }
        else { Add-Check $Area 'SQL Server version and edition (setup does not change an existing engine)' 'recorded' $verText 'INFO' }
    }

    # Bundled = the .sql files in the installed app's database\migrations folder (read in the guest); journal = the rows
    # of dbo.schema_migrations. Both are one id per element, trimmed (Get-MigrationIds).
    $bundled = @(Get-MigrationIds $F.bundledMigrations | Sort-Object -Unique)
    $journal = @(Get-MigrationIds $F.journal | Sort-Object -Unique)
    $missing = @($bundled | Where-Object { $journal -notcontains $_ })
    $extra = @($journal | Where-Object { $bundled -notcontains $_ })
    $latestBundled = 'none'
    if ($bundled.Count -gt 0) { $latestBundled = $bundled[-1] }
    $latestApplied = 'none'
    if ($journal.Count -gt 0) { $latestApplied = $journal[-1] }
    $journalText = "$($journal.Count) applied, latest $latestApplied"
    if ($missing.Count -gt 0) { $journalText += "; missing: $($missing -join ', ')" }
    if ($extra.Count -gt 0) { $journalText += "; not bundled: $($extra -join ', ')" }
    Add-Check $Area 'Migration journal equals the bundled migrations' "$($bundled.Count) migrations, latest $latestBundled" $journalText (Get-Result (($bundled.Count -gt 0) -and ($journal.Count -eq $bundled.Count) -and ($latestApplied -eq $latestBundled) -and ($missing.Count -eq 0) -and ($extra.Count -eq 0)))

    $users = @($F.users)
    $owner = @($users | Where-Object { [string]$_.windows_identity -eq [string]$F.account -and [string]$_.role_code -eq 'OWNER' -and $_.is_active }) | Select-Object -First 1
    $ownerText = 'not an active Owner'
    if ($owner) { $ownerText = "active OWNER, own login=$($owner.has_own_login), ALTER ANY LOGIN WITH GRANT OPTION=$($owner.alter_any_login_with_grant)" }
    Add-Check $Area 'The account that ran setup is an active Owner with its own SQL login' "$($F.account)" $ownerText (Get-Result ([bool]$owner -and [int]$owner.has_own_login -eq 1) $soft)
    Add-Check $Area 'Owner may change users without Run as administrator (decision 7, migration 0043)' 'ALTER ANY LOGIN WITH GRANT OPTION granted' $ownerText (Get-Result ([bool]$owner -and [int]$owner.alter_any_login_with_grant -eq 1) $grantLevel)
    Add-Check $Area 'Application users' 'listed for the record' (@($users | ForEach-Object { "$($_.windows_identity) $($_.role_code) active=$($_.is_active)" }) -join '; ') 'INFO'

    $tasks = @($F.tasks)
    $ownerShort = ([string]$F.account -split '\\')[-1]
    $expect = @(
        @{ Name = $TaskAutomation; User = 'EtpAutomation'; Level = 'Limited' },
        @{ Name = $TaskBackup; User = 'EtpAutomation'; Level = 'Limited' },
        @{ Name = $TaskDrill; User = $ownerShort; Level = 'Highest' })
    $expectNames = @($expect | ForEach-Object { $_.Name })
    foreach ($e in $expect) {
        $t = @($tasks | Where-Object { $_.name -eq $e.Name }) | Select-Object -First 1
        if (-not $t) { Add-Check $Area "Task: $($e.Name)" "$($e.User), S4U, $($e.Level)" 'missing' 'FAIL'; continue }
        $user = ([string]$t.user -split '\\')[-1]
        Add-Check $Area "Task: $($e.Name)" "$($e.User), S4U, $($e.Level)" "$($t.user), $($t.logonType), $($t.runLevel), $($t.state)" (Get-Result (($user -eq $e.User) -and ([string]$t.logonType -eq 'S4U') -and ([string]$t.runLevel -eq $e.Level)))
    }
    $systemTasks = @($tasks | Where-Object { [string]$_.user -match '(?i)^(NT AUTHORITY\\)?SYSTEM$' } | ForEach-Object { $_.name })
    $systemText = 'none'
    if ($systemTasks.Count -gt 0) { $systemText = $systemTasks -join ', ' }
    Add-Check $Area 'No ETP task runs as SYSTEM' 'none' $systemText (Get-Result ($systemTasks.Count -eq 0))
    $otherTasks = @($tasks | Where-Object { $expectNames -notcontains $_.name } | ForEach-Object { "$($_.name) ($($_.user))" })
    if ($otherTasks.Count -gt 0) { Add-Check $Area 'Other tasks with ETP in the name' 'none expected from setup' ($otherTasks -join '; ') 'REVIEW' }

    $acls = @($F.acls)
    $missingFolders = @($acls | Where-Object { -not $_.exists } | ForEach-Object { $_.folder })
    $missingText = 'none'
    if ($missingFolders.Count -gt 0) { $missingText = $missingFolders -join ', ' }
    Add-Check $Area 'ETP folders exist (incl. Inbound, Processed\Duplicate, Failed, ReportPacks, Backups\RecoveryDrill)' 'all present' $missingText (Get-Result ($missingFolders.Count -eq 0))
    $broad = @($acls | Where-Object { @($_.broad).Count -gt 0 } | ForEach-Object { "$($_.folder): $(@($_.broad) -join ', ')" })
    $broadText = 'none'
    if ($broad.Count -gt 0) { $broadText = $broad -join '; ' }
    Add-Check $Area 'No broad access on ETP folders' 'no Everyone, Users, Authenticated Users, Interactive or Guests' $broadText (Get-Result ($broad.Count -eq 0))
    $unexpected = @($acls | Where-Object { @($_.unexpected).Count -gt 0 } | ForEach-Object { "$($_.folder): $(@($_.unexpected) -join ', ')" })
    $unexpectedText = 'none'
    if ($unexpected.Count -gt 0) { $unexpectedText = $unexpected -join '; ' }
    Add-Check $Area 'Only SYSTEM, Administrators, the SQL service and EtpAutomation on ETP folders' 'nobody else' $unexpectedText (Get-Result ($unexpected.Count -eq 0) 'REVIEW')
    $unprotected = @($acls | Where-Object { $_.exists -and $_.expectProtected -and -not $_.protected } | ForEach-Object { $_.folder })
    $unprotectedText = 'none'
    if ($unprotected.Count -gt 0) { $unprotectedText = $unprotected -join ', ' }
    Add-Check $Area 'ETP folders do not inherit from ProgramData' 'protected ACL on the root and its private folders' $unprotectedText (Get-Result ($unprotected.Count -eq 0))
    $readOnly = @($acls | Where-Object { ($_.folder -eq '(root)' -or $_.folder -eq 'Operations') -and [string]$_.automationRights -match 'Write|Modify|FullControl' } | ForEach-Object { "$($_.folder): $($_.automationRights)" })
    $readOnlyText = 'read-only'
    if ($readOnly.Count -gt 0) { $readOnlyText = $readOnly -join '; ' }
    Add-Check $Area 'EtpAutomation can only read the root and Operations' 'read and execute' $readOnlyText (Get-Result ($readOnly.Count -eq 0))
    $writable = @($F.installWritableByNonAdmins)
    $writableText = 'none'
    if ($writable.Count -gt 0) { $writableText = $writable -join '; ' }
    Add-Check $Area 'Installation folder not writable by non-administrators' 'none' $writableText (Get-Result ($writable.Count -eq 0))

    $au = $F.automationAccount
    $isAdmin = @($F.administrators | Where-Object { ([string]$_ -split '\\')[-1] -eq 'EtpAutomation' }).Count -gt 0
    Add-Check $Area 'EtpAutomation account' 'exists, enabled, not an administrator' "exists=$($au.exists) enabled=$($au.enabled) administrator=$isAdmin" (Get-Result ([bool]$au.exists -and [bool]$au.enabled -and -not $isAdmin))
    $o = $F.operations
    $expectedPrincipal = "$($F.computer)\EtpAutomation"
    $opsText = 'missing'
    if ($o) { $opsText = "serverInstance=$($o.serverInstance); database=$($o.database); automationPrincipal=$($o.automationPrincipal); allowAutomationFolderAccess=$($o.allowAutomationFolderAccess)" }
    Add-Check $Area 'operations.json' "serverInstance=.\SQLEXPRESS; database=EtpReporting; automationPrincipal=$expectedPrincipal; allowAutomationFolderAccess=True" $opsText (Get-Result ([bool]$o -and ([string]$o.serverInstance -eq '.\SQLEXPRESS') -and ([string]$o.database -eq 'EtpReporting') -and ([string]$o.automationPrincipal -eq $expectedPrincipal) -and ($o.allowAutomationFolderAccess -eq $true)) $soft)
    $w = $F.watchFolders
    if ($w) { Add-Check $Area 'Watch-folder automation' 'enabled, ProgramData\EtpReporting folders' "enabled=$($w.is_enabled); inbound=$($w.inbound_path)" (Get-Result ([int]$w.is_enabled -eq 1) 'REVIEW') }
    $brokerText = (@($F.brokers | ForEach-Object { "$($_.broker) signed=$($_.is_signed) automation can execute=$($_.automation_can_execute)" }) -join '; ')
    Add-Check $Area 'Operations module (master broker) and EtpAutomation roles' 'recorded; full rights only after EtpAutomation is a Store Manager' "$brokerText; roles: $(@($F.automationRoles) -join ', ')" 'INFO'
}

function Test-ImportRun {
    param($Imp, [string]$Area, [switch]$InfoOnly)
    if ($null -eq $Imp) { Add-Check $Area 'Headless import' 'ran' 'not run' 'FAIL'; return }
    $fail = 'FAIL'
    if ($InfoOnly) { $fail = 'REVIEW' }
    $r = $Imp.Result
    $attempts = @($r.attempts)
    $summary = (@($attempts | Group-Object outcome | Sort-Object Name | ForEach-Object { "$($_.Name)=$($_.Count)" }) -join ', ')
    Add-Check $Area 'Unattended import (--automation-once)' 'exit code 0' "exit $($Imp.ExitCode)" (Get-Result ($Imp.ExitCode -eq 0) $fail)
    Add-Check $Area 'Workbooks attempted' '64 (32 per store)' "$($attempts.Count): $summary" (Get-Result ($attempts.Count -eq 64) 'REVIEW')
    $bad = @($attempts | Where-Object { [string]$_.outcome -match '(?i)fail|unknown|cancel|block' -or [int]$_.conflict_rows -gt 0 })
    $badText = 'none'
    if ($bad.Count -gt 0) { $badText = (@($bad | ForEach-Object { "$($_.store_code) $($_.report_code) $($_.file_name): $($_.outcome), conflicts $($_.conflict_rows)" }) -join '; ') }
    Add-Check $Area 'No Failed, Unknown layout or conflicting workbook' 'none' $badText (Get-Result ($bad.Count -eq 0) $fail)
    foreach ($store in @('HEMW', 'WLMHW')) {
        $row = @(@($r.fingerprint) | Where-Object { $_.store_code -eq $store }) | Select-Object -First 1
        $text = 'no sales rows'
        if ($row) { $text = "$($row.invoices) invoices, $($row.lines) lines, $($row.first_date) to $($row.last_date)" }
        Add-Check $Area "Sales loaded for $store" 'invoices and lines above 0' $text (Get-Result ([bool]$row -and [int64]$row.invoices -gt 0 -and [int64]$row.lines -gt 0) $fail)
    }
    $zip = (@($r.zipNow) -join '; ')
    Add-Check $Area 'Source zip filed by the service' 'moved to Processed' $zip (Get-Result (($zip -match '\\Processed\\') -and ($zip -notmatch '\\Failed\\') -and ($zip -notmatch '\\Inbound\\')) 'REVIEW')
    $packs = @($r.runs | Where-Object { $_.run_type -eq 'AUTO_REPORT_PACK' })
    $packOk = @($packs | Where-Object { $_.outcome -eq 'Succeeded' }).Count
    Add-Check $Area 'Combined report packs made after the import' 'at least one Succeeded' "$packOk of $($packs.Count) succeeded; $(@($r.newPackFiles).Count) new files in ReportPacks" (Get-Result ($packOk -gt 0) 'REVIEW')
    if ($bad.Count -gt 0 -and -not $InfoOnly) {
        $script:UiSteps.Add("Triage each failed workbook with the etp-import-failure skill (register row per failure). Failed in $($Imp.Label): $badText. Read the diagnostics in the app (Imports > History) inside the VM.")
    }
}

function Test-ReimportRun($Second, $First) {
    $area = 'Re-import (same zip)'
    if ($null -eq $Second -or $null -eq $First) { Add-Check $area 'Second import of the same zip' 'ran' 'not run' 'FAIL'; return }
    $attempts = @($Second.Result.attempts)
    $newRows = [int64](($attempts | Measure-Object -Property new_rows -Sum).Sum)
    $summary = (@($attempts | Group-Object outcome | Sort-Object Name | ForEach-Object { "$($_.Name)=$($_.Count)" }) -join ', ')
    Add-Check $area 'Second import adds no rows (A1.3)' '0 new rows' "$newRows new rows; $summary" (Get-Result ($newRows -eq 0))
    $a = @{}; foreach ($row in @($First.Result.rowCounts)) { $a[[string]$row.table_name] = [int64]$row.row_count }
    $b = @{}; foreach ($row in @($Second.Result.rowCounts)) { $b[[string]$row.table_name] = [int64]$row.row_count }
    $changed = @($FactTables | Where-Object { $a.ContainsKey($_) -and $b[$_] -ne $a[$_] } | ForEach-Object { "$_ $($a[$_]) -> $($b[$_])" })
    $changedText = 'unchanged'
    if ($changed.Count -gt 0) { $changedText = $changed -join '; ' }
    Add-Check $area 'Fact tables unchanged by the second import' ($FactTables -join ', ') $changedText (Get-Result ($changed.Count -eq 0))
}

function Compare-Upgrade($Before, $After, $Run) {
    $area = 'No data loss'
    $b = [ordered]@{}; foreach ($row in @($Before.rowCounts)) { $b[[string]$row.table_name] = [int64]$row.row_count }
    $a = @{}; foreach ($row in @($After.rowCounts)) { $a[[string]$row.table_name] = [int64]$row.row_count }
    $lost = @(); $fewer = @(); $gone = @(); $rows = @()
    foreach ($t in @($b.Keys)) {
        # Not $after: PowerShell names are case-insensitive, and $After is the parameter.
        $countNow = $null
        if ($a.ContainsKey($t)) { $countNow = $a[$t] } else { $gone += $t }
        $rows += [pscustomobject]@{ table_name = $t; before = $b[$t]; after = $countNow }
        if ($null -ne $countNow -and $countNow -lt $b[$t]) {
            if ($KeyTables -contains $t) { $lost += "$t $($b[$t]) -> $countNow" } else { $fewer += "$t $($b[$t]) -> $countNow" }
        }
    }
    Save-Csv 'upgrade-row-counts.csv' $rows
    $lostText = 'no decrease'
    if ($lost.Count -gt 0) { $lostText = $lost -join '; ' }
    Add-Check $area 'Key tables keep every row' 'sales_invoices, sales_lines, import_files, daily_reporting_days: no decrease' $lostText (Get-Result ($lost.Count -eq 0))
    $fewerText = 'no decrease'
    if ($fewer.Count -gt 0) { $fewerText = $fewer -join '; ' }
    Add-Check $area 'Other tables' 'no decrease (read the bootstrap log if a migration restated rows)' $fewerText (Get-Result ($fewer.Count -eq 0) 'REVIEW')
    $goneText = 'none'
    if ($gone.Count -gt 0) { $goneText = $gone -join ', ' }
    Add-Check $area 'Tables that existed before still exist' 'all' $goneText (Get-Result ($gone.Count -eq 0) 'REVIEW')
    foreach ($store in @(@($Before.fingerprint) | ForEach-Object { [string]$_.store_code })) {
        $x = @(@($Before.fingerprint) | Where-Object { $_.store_code -eq $store })[0]
        $y = @(@($After.fingerprint) | Where-Object { $_.store_code -eq $store }) | Select-Object -First 1
        if (-not $y) { Add-Check $area "Store $store sales after the upgrade" 'present' 'missing' 'FAIL'; continue }
        $same = ([string]$x.totals_sha256 -eq [string]$y.totals_sha256)
        Add-Check $area "Store $store invoice/line counts and value totals (hashed)" "$($x.invoices) invoices, $($x.lines) lines, totals hash unchanged" "$($y.invoices) invoices, $($y.lines) lines, totals hash same=$same" (Get-Result ($same -and [int64]$y.invoices -eq [int64]$x.invoices -and [int64]$y.lines -eq [int64]$x.lines) 'REVIEW')
    }
    $activeBefore = @($Before.users | Where-Object { $_.is_active } | ForEach-Object { "$($_.windows_identity) ($($_.role_code))" })
    $activeAfter = @($After.users | Where-Object { $_.is_active } | ForEach-Object { "$($_.windows_identity) ($($_.role_code))" })
    $lostUsers = @($activeBefore | Where-Object { $activeAfter -notcontains $_ })
    $usersText = "all $($activeBefore.Count) kept"
    if ($lostUsers.Count -gt 0) { $usersText = 'no longer active: ' + ($lostUsers -join ', ') }
    # 0042 only lets Settings > Users deactivate an account Windows cannot resolve; no migration deactivates anyone by itself.
    Add-Check $area 'Active users and roles kept by setup' 'same (setup and migrations 0042/0043 deactivate nobody)' $usersText (Get-Result ($lostUsers.Count -eq 0))
    $jb = @(Get-MigrationIds $Before.journal)
    $ja = @(Get-MigrationIds $After.journal)
    $missingJ = @($jb | Where-Object { $ja -notcontains $_ })
    $addedJ = @($ja | Where-Object { $jb -notcontains $_ })
    $missingJText = "$($jb.Count - $missingJ.Count) of $($jb.Count) kept, $($addedJ.Count) added"
    if ($addedJ.Count -gt 0) { $missingJText += " ($($addedJ[0]) to $($addedJ[-1]))" }
    if ($missingJ.Count -gt 0) { $missingJText += "; missing: $($missingJ -join ', ')" }
    Add-Check $area 'Migrations applied before are still in the journal' "all $($jb.Count) kept" $missingJText (Get-Result (($jb.Count -gt 0) -and ($missingJ.Count -eq 0)))
    if (@(Get-MigrationIds $After.bundledMigrations | Where-Object { $jb -notcontains $_ }).Count -gt 0) {
        Add-Check $area 'Verified pre-migration backup taken before migrating' 'bootstrap line "Verified pre-migration backup is retained at ..."' ([string]$Run.Collected.preMigrationBackup) (Get-Result ([bool]$Run.Collected.preMigrationBackup))
    }
    $newBackups = @(@($After.backupFiles) | Where-Object { @($Before.backupFiles | ForEach-Object { $_.name }) -notcontains $_.name } | ForEach-Object { "$($_.name) ($($_.bytes) bytes)" })
    Add-Check $area 'New files in Backups' 'the pre-migration backup and its receipt' ($newBackups -join '; ') 'INFO'
}

# ---------------------------------------------------------------- host preparation
function Assert-Host {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run this script from an elevated Windows PowerShell window (Run as administrator). Hyper-V and PowerShell Direct need it.'
    }
    if ($PSVersionTable.PSVersion.Major -ne 5) { Write-Host "Note: written for Windows PowerShell 5.1; this is $($PSVersionTable.PSVersion)." -ForegroundColor Yellow }
    Import-Module Hyper-V -ErrorAction Stop
    $null = Get-VM -Name $VmName -ErrorAction Stop
}

function New-OutputFolder {
    $suffix = $Scenario
    if ($Scenario -eq 'Upgrade') { $suffix = "Upgrade-$UpgradeFrom" }
    $base = Join-Path $ReferenceRoot ('VM195-{0}-{1}' -f (Get-Date -Format 'yyyyMMdd-HHmm'), $suffix)
    $path = $base
    $n = 2
    while (Test-Path -LiteralPath $path) { $path = "$base-$n"; $n++ }
    New-Item -ItemType Directory -Path $path | Out-Null
    return $path
}

function Resolve-OutputFolder {
    if ($OutputFolder) {
        if (-not (Test-Path -LiteralPath $OutputFolder -PathType Container)) { throw "Output folder not found: $OutputFolder" }
        return (Resolve-Path -LiteralPath $OutputFolder).Path
    }
    $suffix = $Scenario
    if ($Scenario -eq 'Upgrade') { $suffix = "Upgrade-$UpgradeFrom" }
    $latest = @(Get-ChildItem -LiteralPath $ReferenceRoot -Directory -Filter "VM195-*-$suffix*" | Sort-Object LastWriteTime) | Select-Object -Last 1
    if (-not $latest) { throw "No VM195-*-$suffix folder under $ReferenceRoot. Run -Stage Install first (same -Scenario and -UpgradeFrom), or pass -OutputFolder." }
    return $latest.FullName
}

function Test-InstallerFile([string]$Path, [string]$Version, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Label not found: $Path. Build and copy it first (Migration 2026-10-03\INSTALLER-BUILD-RUNBOOK.md), or pass its path." }
    $dir = Split-Path -Parent $Path
    Write-Host "  hashing $Label ($([math]::Round((Get-Item -LiteralPath $Path).Length / 1MB)) MB)"
    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    # Publish-Installer-*.ps1 writes SHA256SUMS.txt; older kits used SHA256SUMS-installer.txt.
    $sums = @('SHA256SUMS-installer.txt', 'SHA256SUMS.txt' | ForEach-Object { Join-Path $dir $_ } | Where-Object { Test-Path -LiteralPath $_ }) | Select-Object -First 1
    if (-not $sums) { $sums = Join-Path $dir 'SHA256SUMS.txt' }
    if (Test-Path -LiteralPath $sums) {
        # The line naming this installer (Build-VM-Rehearsal-Base.ps1 does the same); else the first hash line.
        $hashLines = @(Get-Content -LiteralPath $sums | Where-Object { $_ -match '[0-9A-Fa-f]{64}' })
        $line = @($hashLines | Where-Object { $_ -like "*$(Split-Path -Leaf $Path)*" }) | Select-Object -First 1
        if (-not $line) { $line = $hashLines | Select-Object -First 1 }
        $expected = (([string]$line).Trim() -split '\s+')[0]
        Add-Check 'Host' "$Label hash matches $(Split-Path -Leaf $sums)" $expected $hash (Get-Result ($hash -ieq $expected))
        if ($hash -ine $expected) { throw "$Label does not match its SHA256SUMS-installer.txt. Stop: the file changed after it was built." }
    }
    else { Add-Check 'Host' "$Label hash file" 'SHA256SUMS.txt (or SHA256SUMS-installer.txt) beside the installer' 'missing' 'REVIEW' }
    $releaseText = 'no release.json'
    $release = Join-Path $dir 'release.json'
    if (Test-Path -LiteralPath $release) {
        try {
            $j = Get-Content -Raw -LiteralPath $release | ConvertFrom-Json
            $releaseText = "release.json version $($j.version), commit $($j.commit), clean tree $($j.sourceTreeClean)"
            Add-Check 'Host' "$Label release.json version" $Version ([string]$j.version) (Get-Result ([string]$j.version -eq $Version))
        }
        catch { $releaseText = "release.json unreadable: $($_.Exception.Message)" }
    }
    $script:RunInfo[$Label] = "$Path; SHA-256 $hash; $releaseText"
    return $hash
}

function New-DataZip {
    $retail = Join-Path $DataPackage 'Retail'
    $manifest = Join-Path $DataPackage 'SHA256SUMS.txt'
    if (-not (Test-Path -LiteralPath $manifest)) { throw "SHA256SUMS.txt is missing from $DataPackage" }
    Write-Host '  checking the Retail workbooks against SHA256SUMS.txt'
    $ok = 0
    $bad = @()
    foreach ($line in Get-Content -LiteralPath $manifest) {
        if ($line -notmatch '^([0-9a-f]{64}) \*?\./(.+)$') { continue }
        $expected = $Matches[1]
        $relative = $Matches[2]
        if ($relative -notlike 'Retail/*') { continue }
        $path = Join-Path $DataPackage ($relative -replace '/', '\')
        if (-not (Test-Path -LiteralPath $path)) { $bad += "missing $relative"; continue }
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { $bad += "changed $relative" } else { $ok++ }
    }
    $onDisk = @(Get-ChildItem -LiteralPath $retail -Recurse -File -Filter '*.xlsx' | Where-Object { -not $_.Name.StartsWith('~$') })
    $badText = 'none'
    if ($bad.Count -gt 0) { $badText = $bad -join '; ' }
    Add-Check 'Host' 'Consolidated data package (Retail)' '64 workbooks, all matching SHA256SUMS.txt, no extra files' "$ok match; problems: $badText; on disk $($onDisk.Count)" (Get-Result (($bad.Count -eq 0) -and ($ok -eq 64) -and ($onDisk.Count -eq $ok)))
    if ($bad.Count -gt 0 -or $ok -eq 0) { throw 'The data package does not match its SHA256SUMS.txt. Stop.' }
    # Entries named <store>/<file> with forward slashes, so the unpacked layout is Retail\<store>\<file>.
    $script:TempDir = Join-Path $env:TEMP "EtpVM195-$RunId"
    New-Item -ItemType Directory -Path $script:TempDir -Force | Out-Null
    $script:ZipPath = Join-Path $script:TempDir 'Retail.zip'
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::Open($script:ZipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($storeDir in @(Get-ChildItem -LiteralPath $retail -Directory | Sort-Object Name)) {
            foreach ($file in @(Get-ChildItem -LiteralPath $storeDir.FullName -File -Filter '*.xlsx' | Where-Object { -not $_.Name.StartsWith('~$') } | Sort-Object Name)) {
                [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, ($storeDir.Name + '/' + $file.Name), [System.IO.Compression.CompressionLevel]::Optimal)
            }
        }
    }
    finally { $zip.Dispose() }
    $script:ZipHash = (Get-FileHash -LiteralPath $script:ZipPath -Algorithm SHA256).Hash
    $script:RunInfo['Data'] = "$retail ($ok workbooks verified); zipped for the watch folder as Retail.zip (temporary, deleted at the end)"
}

function New-TitanZips {
    # One zip per held Titan file, entry <store>/<file>, the layout the package import uses (Retail\<store>\<file>).
    $manifest = Join-Path $DataPackage 'SHA256SUMS.txt'
    if (-not (Test-Path -LiteralPath $manifest)) { throw "SHA256SUMS.txt is missing from $DataPackage" }
    $expected = @{}
    foreach ($line in Get-Content -LiteralPath $manifest) {
        if ($line -match '^([0-9a-f]{64}) \*?\./(.+)$') { $expected[($Matches[2] -replace '/', '\')] = $Matches[1] }
    }
    if (-not $script:TempDir) { $script:TempDir = Join-Path $env:TEMP "EtpVM195-$RunId"; New-Item -ItemType Directory -Path $script:TempDir -Force | Out-Null }
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zips = @()
    foreach ($t in $TitanFiles) {
        $path = Join-Path $DataPackage $t.Relative
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Held Titan file not found: $path" }
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        $want = [string]$expected[$t.Relative]
        Add-Check 'Host' "Held Titan file $($t.Order) ($($t.Key)) matches SHA256SUMS.txt" $want.Substring(0, [math]::Min(8, $want.Length)) $hash.Substring(0, 8) (Get-Result ($want -and $hash -eq $want))
        if ($hash -ne $want) { throw "$($t.Relative) does not match the package's SHA256SUMS.txt. Stop." }
        $name = 'Titan{0}-{1}-{2}.zip' -f $t.Order, $t.Store, $t.Key
        $zipPath = Join-Path $script:TempDir $name
        if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
        $zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
        try { [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $path, ($t.Store + '/' + (Split-Path -Leaf $path)), [System.IO.Compression.CompressionLevel]::Optimal) }
        finally { $zip.Dispose() }
        $zips += [pscustomobject]@{ Spec = $t; Name = $name; HostZip = $zipPath; ZipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash; FileSha8 = $hash.Substring(0, 8); GuestZip = "$GuestStageDir\Titan\$name" }
    }
    $script:TitanZips = $zips
    $script:RunInfo['Titan files'] = (@($zips | ForEach-Object { "$($_.Spec.Order). $($_.Spec.Relative) (sha256 $($_.FileSha8))" }) -join '; ')
}

function Test-RehearsalNeedsTitan {
    # 1.9.5: the held Titan files load only in -Stage Titan, AFTER the VM-pre checks (the WLMHW gap days exist only before).
    return (($Scenario -eq 'Rehearsal') -and ($Stage -eq 'Titan'))
}

function Invoke-Preflight {
    Write-Step 'Host checks'
    $vm = Get-VM -Name $VmName
    $script:RunInfo['VM'] = "$VmName (generation $($vm.Generation), $([math]::Round($vm.MemoryStartup / 1GB, 1)) GB startup memory, state $($vm.State), files at $($vm.Path))"
    $gsi = Get-VMIntegrationService -VMName $VmName -Name 'Guest Service Interface'
    Add-Check 'Host' 'Guest Service Interface (for Copy-VMFile)' 'enabled' ([string]$gsi.Enabled) (Get-Result ([bool]$gsi.Enabled) 'REVIEW')
    $script:RunInfo['Checkpoints on the VM'] = (@(Get-VMSnapshot -VMName $VmName | Sort-Object CreationTime | ForEach-Object { $_.Name }) -join '; ')
    $freeGb = [math]::Round((Get-CimInstance -ClassName Win32_OperatingSystem).FreePhysicalMemory / 1MB, 1)
    $needGb = 1
    if ($vm.State -ne 'Running') { $needGb = [math]::Round($vm.MemoryStartup / 1GB + 1, 1) }
    Add-Check 'Host' 'Free memory on Workpc' "at least $needGb GB" "$freeGb GB" (Get-Result ($freeGb -ge $needGb))
    if ($freeGb -lt $needGb) { throw 'Not enough free memory for the VM. Close large programs and make sure no build or test is running.' }
    $qualifier = (Split-Path -Qualifier $vm.Path).TrimEnd(':')
    $diskGb = [math]::Round((Get-PSDrive -Name $qualifier).Free / 1GB, 1)
    Add-Check 'Host' "Free space on $qualifier`: (VM files and checkpoints)" 'at least 20 GB' "$diskGb GB" (Get-Result ($diskGb -ge 20))
    if ($diskGb -lt 20) { throw "Not enough free space on $qualifier`: for the VM to grow and keep checkpoints." }
    if (($Stage -eq 'Install') -or ($Stage -eq 'AfterUi') -or $RerunSetup) { $script:InstallerHash = Test-InstallerFile $InstallerPath $NewVersion "Installer $NewVersion" }
    if ($IntermediateInstallerPath -and $Stage -eq 'Install') { $script:IntermediateHash = Test-InstallerFile $IntermediateInstallerPath $IntermediateVersion "Installer $IntermediateVersion (middle hop)" }
    if ($Stage -eq 'Service') { Test-ServiceSources }
    if ($Stage -eq 'Delta') { $script:InstallerHash = Test-InstallerFile $InstallerPath $NewVersion "Installer $NewVersion"; Test-ServiceSources }
    $build192 = ($Stage -eq 'Install') -and ($Scenario -eq 'Upgrade') -and ($script:From -eq 'Install192') -and -not $CheckpointName
    if ($build192) { $script:PreviousHash = Test-InstallerFile $PreviousInstallerPath $PreviousVersion 'Installer 1.9.2' }
    $needsData = ($Stage -eq 'Install') -and ((($Scenario -eq 'NewPc') -and -not $ShopRestore) -or $build192)
    if ($needsData) { New-DataZip }
    if (Test-RehearsalNeedsTitan) { New-TitanZips }
}

function Sync-GuestClock {
    for ($i = 0; $i -lt 12; $i++) {
        $who = Invoke-GuestJson $PrepGuestBlock @($GuestStageDir, $script:GuestRunDir)
        # pwsh 7's ConvertFrom-Json turns the guest's ISO 'o' string into a local [datetime]; casting that to [string] drops the zone and
        # the parse then reads guest UTC as host-local time (the 19,800 s = 5.5 h "skew" on 4 and 7 Oct with the host on IST).
        $guestUtc = if ($who.clockUtc -is [datetime]) { $who.clockUtc.ToUniversalTime() } else { ConvertFrom-RoundTrip ([string]$who.clockUtc) }
        $skew = [math]::Abs(((Get-Date).ToUniversalTime() - $guestUtc).TotalSeconds)
        if ($skew -le 120) { break }
        # 4 Oct: Hyper-V time sync never corrected a 5.5 h (19,800 s = IST offset) skew from the restored checkpoint.
        # Set the guest clock from the host's UTC directly; the guest converts it to its own local time.
        # Setting the clock does not stick (Hyper-V time sync resets it): the skew is the guest's TIME ZONE. Match the host's.
        if ($i -ge 2) { Write-Host "  guest clock still $([int]$skew) s away after matching the time zone; continuing (REVIEW)"; break }
        $tz = (Get-TimeZone).Id
        Write-Host "  guest clock is $([int]$skew) s away from the host; setting the guest time zone to '$tz'"
        Invoke-Guest { param($id) Set-TimeZone -Id $id } @($tz) | Out-Null
        Start-Sleep -Seconds 5
    }
    Add-Check 'Guest' 'Guest identity' "$GuestComputer, $GuestUser, elevated" "$($who.computer), $($who.account), elevated=$($who.elevated)" (Get-Result ((([string]$who.computer) -eq $GuestComputer) -and (([string]$who.account) -eq $GuestUser) -and [bool]$who.elevated))
    Add-Check 'Guest' 'Guest clock' 'within 120 s of the host' "$([int]$skew) s" (Get-Result ($skew -le 120) 'REVIEW')
    $script:GuestDocuments = [string]$who.documents
    if (-not $who.elevated) { throw 'The PowerShell Direct session is not elevated. ETPTest must be a local administrator in the VM.' }
}

function Send-StagedFiles([switch]$Previous, [switch]$Data) {
    Write-Step 'Copying the installer and the data into the guest'
    $guestInstaller = "$GuestStageDir\" + (Split-Path -Leaf $InstallerPath)
    Copy-ToGuest $InstallerPath $guestInstaller $script:InstallerHash
    $guestPrevious = $null
    if ($Previous) {
        $guestPrevious = "$GuestStageDir\" + (Split-Path -Leaf $PreviousInstallerPath)
        Copy-ToGuest $PreviousInstallerPath $guestPrevious $script:PreviousHash
    }
    $guestIntermediate = $null
    if ($IntermediateInstallerPath) {
        $guestIntermediate = "$GuestStageDir\" + (Split-Path -Leaf $IntermediateInstallerPath)
        Copy-ToGuest $IntermediateInstallerPath $guestIntermediate $script:IntermediateHash
    }
    if ($Data) {
        Copy-ToGuest $script:ZipPath "$GuestStageDir\Data\Retail.zip" $script:ZipHash
        # A readable copy for the import through the app (the unelevated app cannot read C:\EtpStaging).
        $uiDest = "$($script:GuestDocuments)\Phase6-Import\Retail"
        Invoke-Guest { param($d) New-Item -ItemType Directory -Path $d -Force | Out-Null } @($uiDest) | Out-Null
        Copy-Item -ToSession $script:Session -Path (Join-Path (Join-Path $DataPackage 'Retail') '*') -Destination $uiDest -Recurse -Force
        $count = [int](Invoke-Guest { param($d) @(Get-ChildItem -LiteralPath $d -Recurse -File -Filter '*.xlsx').Count } @($uiDest))
        Add-Check 'Guest' 'Folder copy for the import through the app' "64 workbooks in $uiDest" "$count" (Get-Result ($count -eq 64) 'REVIEW')
    }
    return [pscustomobject]@{ Installer = $guestInstaller; Previous = $guestPrevious; Intermediate = $guestIntermediate }
}

function Add-CommonUiSteps([switch]$Upgrade, [switch]$UsersDone) {
    $script:UiSteps.Add("Open the VM console: vmconnect.exe localhost $VmName. Sign in as ETPTest (Sagar types the password).")
    $script:UiSteps.Add('Start ETP Reporting Engine from the Start menu normally, NOT Run as administrator. Expect: it opens as Owner, with no connection error.')
    if ($Scenario -eq 'Rehearsal' -and -not $UsersDone) {
        foreach ($acct in $RetiredAccounts) {
            $script:UiSteps.Add("Settings > Users: select $acct, untick Active, reason '$RetireReason', save. Expect: saved without Run as administrator (0042 lets an account of a PC that no longer exists be deactivated). If it fails, write down the exact message.")
        }
    }
    if (-not $UsersDone) {
        $script:UiSteps.Add("Settings > Users: add $GuestComputer\EtpAutomation as Store Manager, active, reason '$AutomationReason'. Expect: saved without Run as administrator (decision 7). If it is already listed as an active Store Manager, do not save it again.")
    }
    if ($Scenario -eq 'Rehearsal' -and -not $UsersDone) {
        $script:UiSteps.Add('Rehearsal: the VM now holds the same data as live (until the Titan files are imported in the next stage), so compare the 28 Sep DSR for BOTH stores with live ETP on Workpc. A difference is REVIEW: 0041 may restate legacy rows.')
    }
    elseif ($Scenario -eq 'Rehearsal') {
        $script:UiSteps.Add('Rehearsal (-UserChanges Script): the Titan files were already imported in this run, so compare only the Helios 28 Sep DSR with live.')
    }
    if ($Upgrade) {
        $script:UiSteps.Add('Imports > History: the batches from before the upgrade are still listed.')
        $script:UiSteps.Add('Reports: open one report for a date that had data before the upgrade. Expect the same numbers as before (compare with a screenshot or the old report export if you have one).')
    }
    else {
        $script:UiSteps.Add('Imports > History: the headless batch is listed. Its counts match import-1\import-attempts.csv in this folder.')
        $script:UiSteps.Add('Optional, proves the screen path: Imports > Intake > Import folder > Documents\Phase6-Import\Retail. Expect every file Duplicate or already present, none Failed.')
    }
    $script:UiSteps.Add('Reports, date 28 Sep 2026, both stores: DSR, Closing stock by brand, Cash book, Service sale, Customer-wise invoices, CRO-wise sales. For each: the screen shows "Amounts: <basis>"; export Excel and PDF into Documents\Phase6-Reports. Compare the Helios DSR VALUE and MTD with the same report in the live ETP on Workpc.')
    $again = '-Stage AfterUi -RerunSetup (same -Scenario'
    if ($Scenario -eq 'Upgrade') { $again += " and -UpgradeFrom $UpgradeFrom" }
    $again += ')'
    if ($UsersDone) { $script:UiSteps.Add('Close ETP. To list the report exports, run -Scenario Rehearsal -Stage AfterUi -SkipTitanImports (the Titan files are already in; importing them again would only add duplicates).') }
    else { $script:UiSteps.Add("Close ETP. Then on Workpc run this script again with $again.") }
    if ($Scenario -eq 'Rehearsal') {
        $script:UiSteps.Add("After the AfterUi run (Titan counts recorded): Phase 5 accounting steps O-D on this VM copy (decision 10), from PHASE5-ACCEPTANCE-PACK.md: finalise 25 Aug, Tally company 'TEST', then sheet O-D. Optional role sheets: add $GuestComputer\EtpTestManager (Store Manager) and $GuestComputer\EtpTestViewer (Viewer) in Settings > Users; both Windows accounts already exist in the VM.")
    }
}

function Invoke-NewPcFlow($Staged) {
    $f0 = Get-Facts 'before-setup'
    $clean = (@($f0.etpUninstallEntries).Count -eq 0) -and ($null -eq $f0.sqlService) -and (-not $f0.programDataExists)
    Add-Check 'Start state' 'Clean VM' 'no ETP, no SQLEXPRESS service, no ProgramData\EtpReporting' "ETP entries=$(@($f0.etpUninstallEntries).Count); SQL service=$([bool]$f0.sqlService); ProgramData=$($f0.programDataExists)" (Get-Result $clean)
    # Add-VM-TestAccounts.log (3 Oct 13:18) lists an EtpAutomation account in the VM just before the clean checkpoint was
    # retaken, although Clean-VM-Leftovers had removed it at 13:08. Setup reuses an existing account, so this is REVIEW.
    Add-Check 'Start state' 'No EtpAutomation account yet' 'absent (setup creates it)' "exists=$($f0.automationAccount.exists); description '$($f0.automationAccount.description)'" (Get-Result (-not $f0.automationAccount.exists) 'REVIEW')
    $run = Invoke-Setup -Label "setup-$NewVersion" -GuestInstaller $Staged.Installer -Tasks 'sqlprerequisites,newdatabase,desktopicon'
    Test-SetupRun $run 'Setup on a new PC' -ExpectFresh
    Test-BootstrapModuleLog $run 'Setup on a new PC'
    $f1 = Get-Facts 'after-setup'
    Test-InstalledState $f1 'Installed state' $NewVersion -NewInstance
    Test-Release1111Checks 'Installed state'
    if ($ImportMode -eq 'Headless') {
        $imp1 = Invoke-HeadlessImport 'import-1'
        Test-ImportRun $imp1 'Import of the two store folders'
        if (-not $SkipReimportCheck -and $imp1) {
            $imp2 = Invoke-HeadlessImport 'import-2-reimport'
            Test-ReimportRun $imp2 $imp1
        }
    }
    else { $script:UiSteps.Add('ImportMode Ui: import Documents\Phase6-Import\Retail in the app (Imports > Intake > Import folder). Expect Imported or "empty export" for every file; none Failed or Unknown layout. Then import it again: every file Duplicate.') }
    $null = Get-Facts 'end-of-automated-steps'
    Add-CommonUiSteps
}


# -Scenario NewPc -Stage Install -ShopRestore (9 Oct 2026, 1.9.7 on 10 Oct): the SHOP PC rehearsal. Clean VM -> install $InstallerPath with "Create a new empty
# database" unticked (setup tasks sqlprerequisites,desktopicon only) -> copy the staging backup + receipt in -> restore-etp-database.ps1
# -BackupPath -ReceiptPath as the account that will be the Owner -> checks -> setup again. Settings > Users steps stay Sagar's.
$ShopRestoreBlock = {
    param([string]$Backup, [string]$Receipt)
    $ps = Join-Path (Get-P6AppDir) 'scripts\restore-etp-database.ps1'
    $r = [ordered]@{ script = $ps; found = (Test-Path -LiteralPath $ps); exit = $null; lines = @() }
    if ($r.found) {
        $out = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $ps -BackupPath $Backup -ReceiptPath $Receipt 2>&1 | ForEach-Object { "$_" })
        $r.exit = $LASTEXITCODE
        $r.lines = @($out | Select-Object -Last 60)
    }
    $r | ConvertTo-Json -Compress -Depth 4
}

function Invoke-ShopRestoreFlow($Staged) {
    $area = 'Shop PC rehearsal'
    $f0 = Get-Facts 'before-setup'
    $clean = (@($f0.etpUninstallEntries).Count -eq 0) -and ($null -eq $f0.sqlService) -and (-not $f0.programDataExists)
    Add-Check 'Start state' 'Clean VM' 'no ETP, no SQLEXPRESS service, no ProgramData\EtpReporting' "ETP entries=$(@($f0.etpUninstallEntries).Count); SQL service=$([bool]$f0.sqlService); ProgramData=$($f0.programDataExists)" (Get-Result $clean)

    # The staging backup, its receipt and its hash line, checked on the host first.
    $bak = $StagingBackupPath
    if (-not $bak) { $bak = (Get-ChildItem -LiteralPath 'E:\ETP\Installers' -Directory -Filter 'Shop PC database *' | Sort-Object Name -Descending | ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Filter '*.bak' -File } | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName }
    if (-not $bak -or -not (Test-Path -LiteralPath $bak)) { throw 'No staging backup found under E:\ETP\Installers\Shop PC database *. Pass -StagingBackupPath.' }
    $rec = "$bak.receipt.json"
    if (-not (Test-Path -LiteralPath $rec)) { throw "The receipt $rec is missing." }
    $sumsFile = Join-Path (Split-Path -Parent $bak) 'SHA256SUMS.txt'
    $hash = (Get-FileHash -LiteralPath $bak -Algorithm SHA256).Hash
    $receipt = Get-Content -Raw -LiteralPath $rec | ConvertFrom-Json
    $sumsLine = @(Get-Content -LiteralPath $sumsFile | Where-Object { $_ -like "*$(Split-Path -Leaf $bak)*" }) | Select-Object -First 1
    $sumsOk = $sumsLine -and ((([string]$sumsLine).Trim() -split '\s+')[0] -ieq $hash)
    Add-Check 'Host' 'Staging backup: file hash = SHA256SUMS.txt = receipt' 'all three equal' "file $($hash.Substring(0, 8)); sums ok=$([bool]$sumsOk); receipt $(([string]$receipt.sha256).Substring(0, 8)); database $($receipt.database); $($receipt.migrations) migrations" (Get-Result ([bool]$sumsOk -and ([string]$receipt.sha256 -ieq $hash)))

    # 1. Setup like the shop PC: no new database.
    $run = Invoke-Setup -Label "setup-$NewVersion-shop-no-database" -GuestInstaller $Staged.Installer -Tasks 'sqlprerequisites,desktopicon'
    $exitText = 'unknown'; if ($null -ne $run.ExitCode) { $exitText = [string]$run.ExitCode }
    Add-Check $area 'Setup without a database: exit code 0' '0' $exitText (Get-Result ($null -ne $run.ExitCode -and [int]$run.ExitCode -eq 0))
    # With no database the bootstrap stops on purpose with its own message ("Setup stopped before creating a database, as asked..."); it never
    # prints the success line, so expect the stop message instead (9 Oct rehearsal).
    # Log lines come back either as strings or as {value=...} objects (JSON round trip): read both.
    $lineText = { param($x) @($x) | ForEach-Object { if ($_ -is [string]) { $_ } elseif ($null -ne $_ -and $_.PSObject.Properties['value']) { [string]$_.value } else { [string]$_ } } }
    $stopText = ((& $lineText $run.Collected.lastLines) + (& $lineText $run.Collected.keyLines)) -join ' '
    $stoppedAsAsked = $stopText -match 'stopped before creating a database'
    Add-Check $area 'Setup without a database: stopped before creating one, as asked' 'bootstrap log says Setup stopped before creating a database' "stopped as asked=$([bool]$stoppedAsAsked); success line present=$($run.Collected.completed)" (Get-Result ([bool]$stoppedAsAsked))
    Test-BootstrapModuleLog $run $area
    $f1 = Get-Facts 'after-setup-no-database'
    Add-Check $area 'Installed application version' "$NewVersion*" ([string]$f1.exeProductVersion) (Get-Result (([string]$f1.exeProductVersion) -like "$NewVersion*"))
    $svc = $f1.sqlService
    Add-Check $area 'SQL Server (SQLEXPRESS) running' 'Running, Auto' $(if ($svc) { "$($svc.state), $($svc.startMode)" } else { 'not installed' }) (Get-Result ($svc -and $svc.state -eq 'Running'))
    $dbNames = @($f1.databases | ForEach-Object { [string]$_.name })
    Add-Check $area 'No EtpReporting database yet (the restore helper needs it missing)' 'absent' ($dbNames -join ', ') (Get-Result ($dbNames -notcontains 'EtpReporting'))

    # 2. Copy the files in (outside ProgramData\EtpReporting) and restore as the account that will be the Owner.
    $guestDir = "$GuestStageDir\ShopDb"
    Invoke-Guest { param($d) New-Item -ItemType Directory -Path $d -Force | Out-Null } @($guestDir) | Out-Null
    $gBak = "$guestDir\" + (Split-Path -Leaf $bak)
    Copy-ToGuest $bak $gBak $hash
    Copy-ToGuest $rec "$gBak.receipt.json" ((Get-FileHash -LiteralPath $rec -Algorithm SHA256).Hash)
    Write-Step 'restore-etp-database.ps1 in the guest (the shop PC restore)'
    $res = Invoke-GuestJson $ShopRestoreBlock @($gBak, "$gBak.receipt.json")
    Save-Json 'shop-restore\restore-output.json' $res
    Add-Check $area 'Restore helper found in the installed app' 'scripts\restore-etp-database.ps1' ([string]$res.script) (Get-Result ([bool]$res.found))
    $tail = (@($res.lines | Select-Object -Last 6) -join ' / ')
    Add-Check $area 'Restore helper finished' 'exit code 0' "exit $($res.exit); last lines: $tail" (Get-Result ($null -ne $res.exit -and [int]$res.exit -eq 0))

    # 3. What the restore left behind.
    $f2 = Get-Facts 'after-restore'
    $dbNames2 = @($f2.databases | ForEach-Object { [string]$_.name })
    Add-Check $area 'EtpReporting exists after the restore (the staging name is gone)' 'EtpReporting present, no EtpStaging_*' ($dbNames2 -join ', ') (Get-Result (($dbNames2 -contains 'EtpReporting') -and (@($dbNames2 | Where-Object { $_ -like 'EtpStaging_*' }).Count -eq 0)))
    $journal = @($f2.journal)
    Add-Check $area 'Migrations after the restore' "$($receipt.migrations) applied, latest $($receipt.latestMigration)" "$($journal.Count) applied" (Get-Result ($journal.Count -eq [int]$receipt.migrations))
    $bad = @()
    foreach ($p in $receipt.rowCounts.PSObject.Properties) {
        $have = @($f2.rowCounts | Where-Object { [string]$_.table_name -eq $p.Name }) | Select-Object -First 1
        # The restore helper makes the account that runs it an Owner, so application_users grows by exactly that one row (9 Oct rehearsal: 7 -> 8).
        $want = [int64]$p.Value
        if ($p.Name -eq 'dbo.application_users') { $want = $want + 1 }
        if (-not $have -or [int64]$have.row_count -ne $want) { $bad += "$($p.Name) receipt $($p.Value) (expected $want) / restored $(if ($have) { $have.row_count } else { 'missing' })" }
    }
    Add-Check $area 'Row counts equal the receipt (10 tables; application_users +1 for the Owner the restore adds)' 'all equal' $(if ($bad.Count) { $bad -join '; ' } else { 'all equal' }) (Get-Result ($bad.Count -eq 0))
    $owner = @($f2.users | Where-Object { [string]$_.windows_identity -ieq $GuestUser -and [string]$_.role_code -eq 'OWNER' -and [bool]$_.is_active }) | Select-Object -First 1
    Add-Check $area "The account that ran the restore ($GuestUser) is an active Owner" 'OWNER, active' $(if ($owner) { 'OWNER, active' } else { (@($f2.users | ForEach-Object { "$($_.windows_identity)=$($_.role_code),active=$($_.is_active)" }) -join '; ') }) (Get-Result ([bool]$owner))
    $rf = Get-ReleaseFacts 'after-restore'
    Test-Release195Schema $rf 'Shop restore'
    Add-Check $area 'Service data came across' 'landing rows > 0' "$($rf.serviceLandingRows)" (Get-Result ([int64]$rf.serviceLandingRows -gt 0))

    # 4. Setup again: no pending migrations, safety backup, DBCC.
    $run2 = Invoke-Setup -Label 'setup-again-after-restore' -GuestInstaller $Staged.Installer -Tasks 'desktopicon'
    Test-SetupRun $run2 'Setup again after the restore'
    Test-BootstrapModuleLog $run2 'Setup again after the restore'
    $f3 = Get-Facts 'after-setup-again'
    $journal3 = @($f3.journal)
    Add-Check $area 'No migration was pending: still the same count' "$($journal.Count)" "$($journal3.Count)" (Get-Result ($journal3.Count -eq $journal.Count))

    $script:UiSteps.Add("Shop rehearsal, in the VM console (vmconnect.exe localhost $VmName, sign in as ETPTest): start ETP with Run as administrator. Expect: opens as Owner with the data (Today, Reports > Daily sales report for 28 Sep 2026 both stores).")
    $script:UiSteps.Add("Settings > Users: $($f2.computer)\EtpAutomation is listed as the restore left it. If it is NOT listed, add it as Store Manager (reason: shop PC setup). Open it and Save once, then run the installer again (/TASKS desktopicon) so setup gives it the backup and drill rights. Write PASS/FAIL for each step.")
    $script:UiSteps.Add('Settings > Users: deactivate the other PCs'' accounts (DESKTOP-6IBM1J5\..., WORKPC\..., TFRROWJLTULT009\...) with a reason. Expect: saved.')
    $script:UiSteps.Add('Run the Daily Backup task once, then the drill; check EtpReporting-latest-verified.json and the drill result (or rerun this script with -Scenario Rehearsal -Stage Final style checks).')
    $null = Get-Facts 'end-of-automated-steps'
}

function Invoke-UpgradeFlow($Staged, [switch]$NoUiSteps) {
    if ($script:From -eq 'Install192' -and -not $CheckpointName) {
        $b0 = Get-Facts 'clean-start'
        Add-Check 'Start state' 'Clean VM' 'no ETP, no SQLEXPRESS service' "ETP entries=$(@($b0.etpUninstallEntries).Count); SQL service=$([bool]$b0.sqlService)" (Get-Result ((@($b0.etpUninstallEntries).Count -eq 0) -and ($null -eq $b0.sqlService)))
        $run0 = Invoke-Setup -Label 'base-setup-1.9.2' -GuestInstaller $Staged.Previous -Tasks 'sqlprerequisites,newdatabase,desktopicon'
        Test-SetupRun $run0 'Base: 1.9.2 on a new PC' -ExpectFresh -InfoOnly
        if ($run0.ExitCode -ne 0) { throw '1.9.2 did not install cleanly, so the upgrade cannot be tested from it. See base-setup-1.9.2.' }
        $impBase = Invoke-HeadlessImport 'base-import-1.9.2'
        Test-ImportRun $impBase 'Base: data imported under 1.9.2' -InfoOnly
        Wait-NoEtpProcess
        New-Checkpoint ('Phase6 upgrade base (1.9.2 + data), ' + (Get-Date -Format 'yyyy-MM-dd HH:mm'))
    }
    else {
        # The 1.8.8 checkpoint was taken with SQL Server stopped (inventory 3 Oct 13:04). The rehearsal base was checkpointed
        # with the VM shut down, so its service starts with Windows; starting it here is harmless.
        $state = [string](Invoke-Guest { Start-Service -Name 'MSSQL$SQLEXPRESS' -ErrorAction SilentlyContinue; Start-Sleep -Seconds 20; $s = Get-Service -Name 'MSSQL$SQLEXPRESS' -ErrorAction SilentlyContinue; if ($s) { [string]$s.Status } else { 'not installed' } })
        Add-Check 'Start state' 'SQL Server service started before the comparison' 'Running' $state (Get-Result ($state -eq 'Running') 'REVIEW')
    }
    $before = Get-Facts 'before-upgrade'
    if ($script:From -eq 'RehearsalBase') {
        # Proves the checkpoint is the rehearsal base Build-VM-Rehearsal-Base.ps1 made (same counts as live on 3 Oct).
        $bc = @{}; foreach ($row in @($before.rowCounts)) { $bc[[string]$row.table_name] = [int64]$row.row_count }
        $diff = @($RehearsalBaseCounts.Keys | Where-Object { $bc[$_] -ne [int64]$RehearsalBaseCounts[$_] } | ForEach-Object { "$_ $($bc[$_]) (base $($RehearsalBaseCounts[$_]))" })
        $diffText = 'same as the rehearsal base'
        if ($diff.Count -gt 0) { $diffText = $diff -join '; ' }
        $versions = (@($before.etpUninstallEntries | ForEach-Object { $_.version }) -join ', ')
        Add-Check 'Start state' 'Rehearsal base as built on 3 Oct (1.9.2, 37 migrations, live counts)' "1.9.2; $RehearsalBaseMigrations migrations; $(@($RehearsalBaseCounts.Keys | ForEach-Object { "$_=$($RehearsalBaseCounts[$_])" }) -join ', ')" "$versions; $(@(Get-MigrationIds $before.journal).Count) migrations; $diffText" (Get-Result (($versions -eq $PreviousVersion) -and (@(Get-MigrationIds $before.journal).Count -eq [int]$RehearsalBaseMigrations) -and ($diff.Count -eq 0)) 'REVIEW')
        $active = @($before.users | Where-Object { $_.is_active } | ForEach-Object { "$($_.windows_identity) ($($_.role_code))" })
        Add-Check 'Start state' 'Active users in the rehearsal base' 'ETPTest Owner plus the old laptop accounts (retired in the live steps)' ($active -join '; ') 'INFO'
    }
    $dbBefore = @($before.databases | Where-Object { $_.name -eq 'EtpReporting' }).Count -eq 1
    $edition = ''
    if ($before.sqlServer) { $edition = [string]$before.sqlServer.edition }
    Add-Check 'Start state' 'Previous ETP with its database' 'ETP installed, EtpReporting present' "ETP: $(@($before.etpUninstallEntries | ForEach-Object { $_.version }) -join ', '); EtpReporting=$dbBefore; journal $(@(Get-MigrationIds $before.journal).Count); SQL $edition" (Get-Result ((@($before.etpUninstallEntries).Count -ge 1) -and $dbBefore))
    if (-not $dbBefore) { throw 'The start state has no EtpReporting database, so there is nothing to upgrade. Choose another checkpoint.' }
    $encrypts = $edition -and ($edition -notmatch 'Express|Web')
    $expectRefusal = $encrypts -and -not $before.custodyReceiptExists
    if ($expectRefusal) {
        Write-Host "  This SQL Server is '$edition', which encrypts backups, and no recovery keys were exported. $NewVersion setup is expected to refuse the upgrade and change nothing." -ForegroundColor Yellow
        $script:Notes.Add("The start state runs $edition with no certificate-custody.json, so setup was expected to refuse (the 24 Sep finding, now a clear message).")
    }
    # Optional middle hop (-IntermediateInstallerPath), only when live is on that release by the session.
    if ($Staged.Intermediate -and -not $expectRefusal) {
        $runMid = Invoke-Setup -Label "setup-$IntermediateVersion-upgrade" -GuestInstaller $Staged.Intermediate -Tasks 'sqlprerequisites,desktopicon'
        Test-SetupRun $runMid "Middle hop: upgrade to $IntermediateVersion"
        $mid = Get-Facts 'after-intermediate'
        $midVer = [string]$mid.exeProductVersion
        Add-Check "Middle hop: upgrade to $IntermediateVersion" 'Installed application version' "$IntermediateVersion (then +commit)" $midVer (Get-Result ($midVer -like "$IntermediateVersion*"))
        Add-Check "Middle hop: upgrade to $IntermediateVersion" 'Migration journal after the middle hop' 'recorded (1.9.3 = 45, 1.9.4 = 47)' "$(@(Get-MigrationIds $mid.journal).Count) migrations" 'INFO'
        if ($runMid.ExitCode -ne 0) { throw "The middle hop to $IntermediateVersion did not finish cleanly, so $NewVersion was not installed over it. See setup-$IntermediateVersion-upgrade." }
    }
    # No newdatabase: if the database were missing, setup must stop for a restore instead of creating an empty one.
    $run = Invoke-Setup -Label "setup-$NewVersion-upgrade" -GuestInstaller $Staged.Installer -Tasks 'sqlprerequisites,desktopicon'
    if ($expectRefusal) {
        Test-SetupRun $run 'Upgrade setup' -ExpectCustodyRefusal
        $after = Get-Facts 'after-refusal'
        $jBefore = @(Get-MigrationIds $before.journal)
        $jAfter = @(Get-MigrationIds $after.journal)
        $jNew = @($jAfter | Where-Object { $jBefore -notcontains $_ })
        Add-Check 'No data loss' 'No migration ran after the refusal' "$($jBefore.Count) migrations" "$($jAfter.Count) migrations; new: $(if ($jNew.Count -gt 0) { $jNew -join ', ' } else { 'none' })" (Get-Result (($jBefore.Count -gt 0) -and ($jAfter.Count -eq $jBefore.Count) -and ($jNew.Count -eq 0)))
        $script:UiSteps.Add('Upgrade not proven yet (expected refusal). To finish it: restore the same checkpoint, open the OLD ETP as Owner in the VM console, Settings > Database > Encrypted backup recovery keys > Create and export recovery keys (two folders, a password Sagar types; SAGAR ONLY).')
        $script:UiSteps.Add("Then on Workpc: Checkpoint-VM -Name $VmName -SnapshotName 'Upgrade base 1.8.8 keys exported'; and run this script with -Scenario Upgrade -CheckpointName 'Upgrade base 1.8.8 keys exported*'.")
        $script:UiSteps.Add('Or prove the upgrade from 1.9.2 instead (the shop path): -Scenario Upgrade -UpgradeFrom Install192.')
        return
    }
    Test-SetupRun $run 'Upgrade setup'
    Test-BootstrapModuleLog $run 'Upgrade setup'
    $after = Get-Facts 'after-upgrade'
    # A 1.9.2 base (fresh, the saved "Phase6 upgrade base" checkpoint, or the rehearsal base) has the SQL Server 2025
    # instance that 1.9.2 setup installed (with the 1 Oct sysadmin fix), so the strict SQL checks apply.
    $hardened = (@('Install192', 'RehearsalBase') -contains $script:From)
    $strictGrant = ($script:From -eq 'RehearsalBase')
    if ($hardened) { Test-InstalledState $after 'Installed state' $NewVersion -NewInstance -Upgrade -StrictOwnerGrant:$strictGrant } else { Test-InstalledState $after 'Installed state' $NewVersion -Upgrade }
    Compare-Upgrade $before $after $run
    Test-MigrationTarget $before $after 'Installed state'
    $rf = Get-ReleaseFacts 'after-upgrade'
    # -UpgradeFrom Checkpoint ('VM196 delta*') already holds Service rows, so "0 before any Service import" applies only to the 1.9.2 bases.
    Test-Release195Schema $rf 'Installed state' -BeforeService:($script:From -ne 'Checkpoint')
    if (-not $SkipReimportCheck -and $script:From -eq 'Install192') {
        $imp = Invoke-HeadlessImport 'import-after-upgrade'
        if ($imp) {
            $summary = (@($imp.Result.attempts | Group-Object outcome | Sort-Object Name | ForEach-Object { "$($_.Name)=$($_.Count)" }) -join ', ')
            $newRows = [int64]((@($imp.Result.attempts) | Measure-Object -Property new_rows -Sum).Sum)
            Add-Check 'Import after the upgrade' "Same zip imported again under $NewVersion" 'files 1.9.2 imported stay Duplicate; files 1.9.2 refused may now import' "$summary; $newRows new rows" 'INFO'
        }
    }
    if ($NoUiSteps) { return $after }
    $null = Get-Facts 'end-of-automated-steps'
    Add-CommonUiSteps -Upgrade
    Add-Release112UiSteps
    return $after
}

function Invoke-ScriptedUserChanges {
    Write-Step 'User changes through the product procedure (-UserChanges Script)'
    $changes = @()
    foreach ($acct in $RetiredAccounts) { $changes += [pscustomobject]@{ Identity = $acct; Role = $null; Active = $false; Name = ($acct -split '\\')[-1]; Reason = $RetireReason } }
    $changes += [pscustomobject]@{ Identity = "$GuestComputer\EtpAutomation"; Role = 'STORE_MANAGER'; Active = $true; Name = 'EtpAutomation'; Reason = $AutomationReason }
    # 1.9.5: the two VM test accounts (R-WLMHW-17 Viewer, R-WLMHW-10 Open items as Store Manager / Viewer). VM only.
    foreach ($ta in $TestAccounts) { $changes += [pscustomobject]@{ Identity = $ta.Identity; Role = $ta.Role; Active = $true; Name = $ta.Name; Reason = $TestAccountReason } }
    $facts = Get-Facts 'before-user-changes'
    $results = @()
    foreach ($c in $changes) {
        $role = $c.Role
        $reason = $c.Reason
        if (-not $c.Active) {
            # Deactivating keeps the account's role, as Settings > Users does when Active is unticked.
            $row = @($facts.users | Where-Object { [string]$_.windows_identity -eq $c.Identity }) | Select-Object -First 1
            $role = 'VIEWER'
            if ($row) { $role = [string]$row.role_code }
            $reason = $RetireReason
        }
        $res = Invoke-GuestJson $UserChangeBlock @($c.Identity, $role, [bool]$c.Active, $c.Name, $reason)
        $results += $res
        $msg = ''
        if (@($res.messages).Count -gt 0) { $msg = '; messages: ' + (@($res.messages) -join ' / ') }
        $want = "$role active=$([int][bool]$c.Active)"
        Add-Check 'Live steps (script)' "Settings > Users path: $($c.Identity) -> $want" 'saved, or already in this state' "before: $($res.before); $($res.action)$msg$(if ($res.error) { '; error: ' + $res.error })" (Get-Result (-not $res.error))
    }
    Save-Json 'user-changes.json' $results
    $script:Notes.Add('-UserChanges Script: the user changes ran in the elevated PowerShell Direct session, so they do not prove that an unelevated Owner can save users (decision 7). Run -UserChanges Ui once for that proof.')
}

function Test-LiveUserState($Facts, [string]$Area) {
    $users = @($Facts.users)
    foreach ($acct in $RetiredAccounts) {
        $row = @($users | Where-Object { [string]$_.windows_identity -eq $acct }) | Select-Object -First 1
        $text = 'not listed'
        if ($row) { $text = "$($row.role_code), active=$($row.is_active)" }
        Add-Check $Area "Old laptop account retired: $acct" 'inactive (or not listed)' $text (Get-Result ((-not $row) -or (-not $row.is_active)))
    }
    $owners = @($users | Where-Object { [string]$_.role_code -eq 'OWNER' -and $_.is_active -and [int]$_.has_own_login -eq 1 } | ForEach-Object { [string]$_.windows_identity })
    Add-Check $Area 'At least one active Owner who can sign in' 'ETPTest' ($owners -join ', ') (Get-Result ($owners.Count -ge 1))
}

function Invoke-InstallStage {
    $pattern = $CleanCheckpointPattern
    if ($script:From -eq 'Checkpoint') { $pattern = $OldCheckpointPattern }
    if ($script:From -eq 'RehearsalBase') { $pattern = $RehearsalCheckpointPattern }
    if ($CheckpointName) { $pattern = $CheckpointName }
    $snap = Find-Checkpoint $pattern
    $script:RunInfo['Checkpoint restored'] = "$($snap.Name) (created $($snap.CreationTime.ToString('yyyy-MM-dd HH:mm')))"
    if (-not $Yes) {
        $answer = Read-Host "Type YES to restore '$($snap.Name)' in $VmName. The VM's current state is discarded; no checkpoint is deleted"
        if ($answer -ne 'YES') { throw 'Stopped before restoring: YES was not typed.' }
    }
    if ($CheckpointCurrentStateFirst) { New-Checkpoint "Auto: state before VM195 run $RunId" }
    Write-Step "Restoring checkpoint: $($snap.Name)"
    Restore-Checkpoint $snap
    Write-Step 'Opening a PowerShell Direct session'
    Connect-Guest
    Sync-GuestClock
    $withPrevious = ($Scenario -eq 'Upgrade') -and ($script:From -eq 'Install192') -and -not $CheckpointName
    $withData = (($Scenario -eq 'NewPc') -and -not $ShopRestore) -or $withPrevious
    $staged = Send-StagedFiles -Previous:$withPrevious -Data:$withData
    if ($ShopRestore) { Invoke-ShopRestoreFlow $staged }
    elseif ($Scenario -eq 'NewPc') { Invoke-NewPcFlow $staged }
    elseif ($Scenario -eq 'Upgrade') { $null = Invoke-UpgradeFlow $staged }
    else { Invoke-RehearsalFlow $staged }
    Wait-NoEtpProcess
    if ($script:VmPreReady) { New-Checkpoint ("VM195 Rehearsal VM-pre ($NewVersion, users + drill done, before Titan), " + (Get-Date -Format 'yyyy-MM-dd HH:mm')) }
    else { New-Checkpoint ("VM195 $Scenario after automated steps, " + (Get-Date -Format 'yyyy-MM-dd HH:mm')) }
}

function Invoke-RehearsalFlow($Staged) {
    # Step 1 of the live runbook: the upgrade (with every check of -Scenario Upgrade -UpgradeFrom RehearsalBase).
    $after = Invoke-UpgradeFlow $Staged -NoUiSteps
    if ($null -eq $after) { throw 'The upgrade did not complete, so the live steps were not rehearsed. See the checks above.' }
    if (@($script:Checks | Where-Object { $_.Result -eq 'FAIL' }).Count -gt 0) {
        $script:Notes.Add('The upgrade had a FAIL, so the live steps after it were not started. Fix or explain it first.')
        $null = Get-Facts 'end-of-automated-steps'
        return
    }
    if ($UserChanges -eq 'Ui') {
        # Steps 2-3 are Sagar's, in Settings > Users (as on live); -Stage AfterUi does steps 4-6.
        $null = Get-Facts 'end-of-automated-steps'
        Add-CommonUiSteps -Upgrade
        return
    }
    # Steps 2-3 by script, then 4-6 at once (the postponed 1.9.3 stage-2 proof), then the VM-pre preparation.
    Invoke-ScriptedUserChanges
    Invoke-OperationsSteps -Area 'Live steps (script)' -RunSetupAgain
    Invoke-VmPreSteps
}

# Steps 4-6 of the live runbook: setup again, backup + drill with row counts, then (Rehearsal) the held Titan files.
function Invoke-OperationsSteps {
    param([string]$Area = 'UI steps', [switch]$RunSetupAgain)
    $f0 = Get-Facts 'operations-start'
    $auto = "$($f0.computer)\EtpAutomation"
    $row = @($f0.users | Where-Object { [string]$_.windows_identity -eq $auto }) | Select-Object -First 1
    $rowText = 'not in application_users'
    if ($row) { $rowText = "$($row.role_code), active=$($row.is_active)" }
    Add-Check $Area 'EtpAutomation is an active Store Manager (Settings > Users)' "$auto, STORE_MANAGER, active" $rowText (Get-Result ([bool]$row -and [string]$row.role_code -eq 'STORE_MANAGER' -and [bool]$row.is_active))
    if ($Scenario -eq 'Rehearsal') { Test-LiveUserState $f0 $Area }
    if ($RunSetupAgain) {
        $guestInstaller = "$GuestStageDir\" + (Split-Path -Leaf $InstallerPath)
        if (-not [bool](Invoke-Guest { param($p) Test-Path -LiteralPath $p } @($guestInstaller))) { Copy-ToGuest $InstallerPath $guestInstaller $script:InstallerHash }
        # /TASKS="desktopicon": no SQL media, no new database; the live rerun is the same plain setup run.
        $run = Invoke-Setup -Label 'setup-again' -GuestInstaller $guestInstaller -Tasks 'desktopicon' -AskToCloseApp:($Stage -eq 'AfterUi')
        Test-SetupRun $run 'Setup run again'
        Test-BootstrapModuleLog $run 'Setup run again'
        $ready = @($run.Collected.keyLines | Where-Object { $_ -match "operations module's rights" -and $_ -notmatch 'WARNING|lacks|does not have' }).Count -gt 0
        Add-Check 'Setup run again' 'EtpAutomation now has the operations module rights' 'bootstrap says it has them' ([string]$ready) (Get-Result $ready)
    }
    $a44 = 'REVIEW'
    if ($Scenario -eq 'Rehearsal') { $a44 = 'FAIL' }
    $null = Invoke-BackupAndDrill -Label 'operations' -WithDrill -RowCountLevel $a44
    $f1 = Get-Facts 'operations-end'
    $newInstance = ($Scenario -eq 'NewPc') -or (@('Install192', 'RehearsalBase') -contains $script:From)
    $strict = ($Scenario -eq 'Rehearsal')
    if ($newInstance) { Test-InstalledState $f1 'Installed state (after the user steps)' $NewVersion -NewInstance -StrictOwnerGrant:$strict } else { Test-InstalledState $f1 'Installed state (after the user steps)' $NewVersion -Upgrade }
    Add-Check 'Backup and drill' 'Operations module rights for EtpAutomation' 'signed broker, EXECUTE granted, etp_automation role' "$(@($f1.brokers | ForEach-Object { "signed=$($_.is_signed) execute=$($_.automation_can_execute)" }) -join '; '); roles: $(@($f1.automationRoles) -join ', ')" (Get-Result ((@($f1.brokers | Where-Object { [int]$_.is_signed -eq 1 -and [int]$_.automation_can_execute -ge 1 }).Count -ge 1)) 'REVIEW')
    # 1.9.5: the held Titan files are no longer imported here; -Stage Titan does it after the VM-pre checks.
}

function Invoke-BackupAndDrill {
    param([string]$Label, [switch]$WithDrill, [string]$RowCountLevel = 'REVIEW')
    $sinceUtc = [string](Invoke-Guest { (Get-Date).ToUniversalTime().ToString('o') })
    $tasks = @(@{ Name = $TaskBackup; Minutes = 30 })
    if ($WithDrill) { $tasks += @{ Name = $TaskDrill; Minutes = 45 }; $tasks += @{ Name = $TaskAutomation; Minutes = 20 } }
    foreach ($t in $tasks) {
        Write-Step "Running the scheduled task: $($t.Name) ($Label)"
        $res = Invoke-GuestJson $RunTaskBlock @($t.Name, $t.Minutes)
        Save-Json ("$Label\task-" + ($t.Name -replace '[^A-Za-z]', '') + '.json') $res
        Add-Check 'Scheduled tasks' "Task runs: $($t.Name) ($Label)" 'last result 0' "found=$($res.found); result=$($res.lastResult); timed out=$($res.timedOut)" (Get-Result ([bool]$res.found -and -not $res.timedOut -and [int64]$res.lastResult -eq 0))
    }
    $rc = Invoke-GuestJson $ReceiptsBlock @($sinceUtc)
    Repair-RowSets $rc @() @('drillRow')
    Save-Json "$Label\receipts.json" $rc
    $v = $rc.verified
    if ($v) {
        Add-Check 'Backup and drill' "Backup receipt written by this run ($Label)" 'fresh, file length matches' "fresh=$($v.fresh); length matches=$($v.backupFileMatchesLength); verified $($v.verifiedAtUtc)" (Get-Result ([bool]$v.fresh -and [bool]$v.backupFileMatchesLength))
        Add-Check 'Backup and drill' "Backup encryption on Express ($Label)" 'NONE' ([string]$v.encryption) (Get-Result ([string]$v.encryption -eq 'NONE') 'REVIEW')
        Add-Check 'Backup and drill' "Receipt records row counts, A4.4 ($Label)" 'rowCounts present' "rowCounts=$($v.hasRowCounts) $($v.rowCounts) $($v.rowCountsNotRecorded)" (Get-Result ([bool]$v.hasRowCounts) $RowCountLevel)
        $script:CountsAfter["Backup receipt ($Label)"] = "$($v.rowCounts) $($v.rowCountsNotRecorded)".Trim()
    }
    else { Add-Check 'Backup and drill' "Backup receipt ($Label)" 'EtpReporting-latest-verified.json' 'missing' 'FAIL' }
    if ($WithDrill) {
        if ($rc.drill) { Add-Check 'Backup and drill' 'Drill result written by this run' 'fresh, succeeded' "fresh=$($rc.drill.fresh); succeeded=$($rc.drill.succeeded); pairs: $($rc.drill.rowCountPairs)" (Get-Result ([bool]$rc.drill.fresh -and [bool]$rc.drill.succeeded)) }
        else { Add-Check 'Backup and drill' 'Drill result' 'EtpReporting-latest-drill.json' 'missing' 'FAIL' }
        $d = $rc.drillRow
        $dText = 'no row in dbo.recovery_drill_results'
        if ($d -and $d.error) { $dText = "unreadable: $($d.error)" }
        elseif ($d) { $dText = "$($d.outcome), $($d.row_counts_status) $($d.not_recorded_reason); sales_invoices $($d.sales_invoices_receipt)/$($d.sales_invoices_restored), sales_lines $($d.sales_lines_receipt)/$($d.sales_lines_restored), import_files $($d.import_files_receipt)/$($d.import_files_restored), daily_reporting_days $($d.daily_reporting_days_receipt)/$($d.daily_reporting_days_restored)" }
        Add-Check 'Backup and drill' 'Drill compared the four row counts (A4.4, migration 0045)' 'Succeeded, Matched, receipt = restored for each table' $dText (Get-Result ([bool]$d -and -not $d.error -and [string]$d.outcome -eq 'Succeeded' -and [string]$d.row_counts_status -eq 'Matched') $RowCountLevel)
    }
    return $rc
}

function Invoke-TitanImports {
    Write-Step 'Held Titan files: copy into the guest, then import one at a time in order'
    foreach ($z in $script:TitanZips) { Copy-ToGuest $z.HostZip $z.GuestZip $z.ZipHash }
    # A copy the unelevated app can read, for the fallback import through the screen (as the live import will be done).
    $uiTitan = "$($script:GuestDocuments)\Phase6-Import\Titan\WLMHW"
    Invoke-Guest { param($d) New-Item -ItemType Directory -Path $d -Force | Out-Null } @($uiTitan) | Out-Null
    foreach ($z in $script:TitanZips) { Copy-Item -ToSession $script:Session -LiteralPath (Join-Path $DataPackage $z.Spec.Relative) -Destination $uiTitan -Force }
    foreach ($z in $script:TitanZips) {
        $t = $z.Spec
        $label = 'titan-{0}-{1}' -f $t.Order, $t.Key
        $imp = Invoke-HeadlessImport -Label $label -GuestZip $z.GuestZip -ZipName $z.Name -What "Titan $($t.Key) ($($t.Relative))"
        $row = [ordered]@{ Order = $t.Order; File = $t.Relative; Sha256First8 = $z.FileSha8; Report = ''; Store = ''; Outcome = 'not run'; Rows = $null; New = $null; Present = $null; Conflicts = $null; ExitCode = $null; ExpectedNew = $t.ExpectedNew; ExpectedPresent = $t.ExpectedPresent }
        if ($null -eq $imp) {
            $script:TitanRows.Add([pscustomobject]$row)
            Add-Check 'Titan imports' "$($t.Order). $($t.Key) imported" 'ran' 'not run (watch-folder import could not start)' 'FAIL'
            $script:UiSteps.Add("Titan $($t.Order) ($($t.Key)) by hand, in this order: Imports > Intake > Choose workbook or ZIP > Documents\Phase6-Import\Titan\WLMHW\$(Split-Path -Leaf $t.Relative) > Import selected source (restatement unticked; never pick the folder). Write Rows/New/Present/Conflicts into the Titan table in SUMMARY.md.")
            continue
        }
        $attempts = @($imp.Result.attempts)
        $row.ExitCode = $imp.ExitCode
        $row.Report = (@($attempts | ForEach-Object { [string]$_.report_code } | Select-Object -Unique) -join ',')
        $row.Store = (@($attempts | ForEach-Object { [string]$_.store_code } | Select-Object -Unique) -join ',')
        $row.Outcome = (@($attempts | ForEach-Object { [string]$_.outcome }) -join ',')
        if ($attempts.Count -gt 0) {
            $row.Rows = [int64](($attempts | Measure-Object -Property rows_processed -Sum).Sum)
            $row.New = [int64](($attempts | Measure-Object -Property new_rows -Sum).Sum)
            $row.Present = [int64](($attempts | Measure-Object -Property already_present_rows -Sum).Sum)
            $row.Conflicts = [int64](($attempts | Measure-Object -Property conflict_rows -Sum).Sum)
        }
        $script:TitanRows.Add([pscustomobject]$row)
        $countsText = "rows $($row.Rows), new $($row.New), present $($row.Present), conflicts $($row.Conflicts); outcome $($row.Outcome); exit $($row.ExitCode)"
        $bad = ($attempts.Count -eq 0) -or ($row.Outcome -match '(?i)fail|unknown|cancel|block') -or ([int64]$row.Conflicts -gt 0)
        Add-Check 'Titan imports' "$($t.Order). $($t.Key) imports without a failure or conflict" "$($t.Store) $($t.Key), 0 conflicts, not Failed" $countsText (Get-Result (-not $bad))
        if ($null -ne $t.ExpectedNew) {
            $match = ([int64]$row.New -eq [int64]$t.ExpectedNew) -and ([int64]$row.Present -eq [int64]$t.ExpectedPresent) -and ([int64]$row.Conflicts -eq [int64]$t.ExpectedConflicts)
            Add-Check 'Titan imports' "$($t.Order). $($t.Key) counts as predicted (decision 10)" "new $($t.ExpectedNew), present $($t.ExpectedPresent), conflicts $($t.ExpectedConflicts)" "new $($row.New), present $($row.Present), conflicts $($row.Conflicts)" (Get-Result $match 'REVIEW')
        }
        else { Add-Check 'Titan imports' "$($t.Order). $($t.Key) counts (become the expected counts for live)" 'recorded' "new $($row.New), present $($row.Present), conflicts $($row.Conflicts)" 'INFO' }
        if ($bad) { $script:UiSteps.Add("Triage $($t.Key) with the etp-import-failure skill (one register row per failure); the diagnostics stay in the VM (Imports > History, and $($script:GuestRunDir)\vm-only\$label).") }
    }
    Save-Csv 'titan-expected-counts.csv' $script:TitanRows
    $ft = Get-Facts 'after-titan'
    foreach ($key in @($RehearsalBaseCounts.Keys)) {
        $r = @(@($ft.rowCounts) | Where-Object { [string]$_.table_name -eq $key }) | Select-Object -First 1
        if ($r) { $script:CountsAfter["$key after the Titan imports"] = [string]$r.row_count }
    }
    foreach ($s in @($ft.fingerprint)) { $script:CountsAfter["$($s.store_code) after the Titan imports"] = "$($s.invoices) invoices, $($s.lines) lines, $($s.first_date) to $($s.last_date), totals hash $(([string]$s.totals_sha256).Substring(0, [math]::Min(12, ([string]$s.totals_sha256).Length)))" }
}

function Invoke-AfterUiStage {
    if ((Get-VM -Name $VmName).State -ne 'Running') { Start-VM -Name $VmName }
    Wait-Heartbeat
    Write-Step 'Opening a PowerShell Direct session'
    Connect-Guest
    Sync-GuestClock
    # 4 Oct: -Stage AfterUi -UserChanges Script used to skip the user changes (11 FAILs on 3 Oct); make them here.
    if ($Scenario -eq 'Rehearsal' -and $UserChanges -eq 'Script') { Invoke-ScriptedUserChanges }
    # Rehearsal always runs setup again: that is the live step that gives EtpAutomation the operations rights.
    Invoke-OperationsSteps -Area 'UI steps' -RunSetupAgain:($RerunSetup -or $Scenario -eq 'Rehearsal')
    if ($Scenario -eq 'Rehearsal') {
        # 1.9.5: no report listing here (-Stage Final does it); prepare the VM-pre state instead.
        Invoke-VmPreSteps
        Wait-NoEtpProcess
        New-Checkpoint ("VM195 Rehearsal VM-pre ($NewVersion, users + drill done, before Titan), " + (Get-Date -Format 'yyyy-MM-dd HH:mm'))
        return
    }
    Test-ReportExports 'after-ui'
    Wait-NoEtpProcess
    New-Checkpoint ("VM195 $Scenario after UI steps, " + (Get-Date -Format 'yyyy-MM-dd HH:mm'))
}

function Test-ReportExports([string]$Label) {
    $reportFolder = "$($script:GuestDocuments)\Phase6-Reports"
    $reports = @(Invoke-GuestJson $ReportsBlock @($reportFolder))
    Save-Csv "$Label\report-exports.csv" $reports
    $pdf = @($reports | Where-Object { $_.ext -eq '.pdf' }).Count
    $xlsx = @($reports | Where-Object { $_.ext -eq '.xlsx' }).Count
    Add-Check 'Reports' 'Report exports saved in Documents\Phase6-Reports (VM-pre, VM-post and Service checks)' 'at least 6 PDF and 6 Excel files; Sagar ticks each check in the plan' "$pdf PDF, $xlsx Excel" (Get-Result (($pdf -ge 6) -and ($xlsx -ge 6)) 'REVIEW')
}

# ================================================================ 1.9.5 additions (4 Oct 2026)
function Open-GuestForStage {
    if ((Get-VM -Name $VmName).State -ne 'Running') { Start-VM -Name $VmName }
    Wait-Heartbeat
    Write-Step 'Opening a PowerShell Direct session'
    Connect-Guest
    Sync-GuestClock
}

function Get-ReleaseFacts([string]$Phase) {
    Write-Host "  collecting the 1.9.5 facts ($Phase)"
    $f = Invoke-GuestJson $Release195FactsBlock
    Repair-RowSets $f @('stores', 'serviceTables', 'kpiVersions') @('aw330')
    Save-Json "release-facts-$Phase.json" $f
    foreach ($e in @($f.errors)) { if ($e) { Write-Host "    guest note: $e" -ForegroundColor Yellow } }
    return $f
}

function Test-MigrationTarget($Before, $After, [string]$Area) {
    $jb = @(Get-MigrationIds $Before.journal)
    $ja = @(Get-MigrationIds $After.journal | Sort-Object -Unique)
    $latest = 'none'
    if ($ja.Count -gt 0) { $latest = $ja[-1] }
    $added = @($ja | Where-Object { $jb -notcontains $_ })
    $addedText = 'none'
    if ($added.Count -gt 0) { $addedText = "$($added[0]) to $($added[-1])" }
    Add-Check $Area "Migration level $NewVersion" "$ExpectedMigrationCount migrations, latest $ExpectedLatestMigration; added from $($jb.Count): $($ExpectedMigrationCount - $jb.Count)" "$($ja.Count) migrations, latest $latest; added $($added.Count) ($addedText)" (Get-Result (($ja.Count -eq $ExpectedMigrationCount) -and ($latest -like $ExpectedLatestMigration)))
    # 1.12.0: from the 1.11.4 VM (61) setup adds exactly 0062 .. 0066 (5).
    if ($jb.Count -eq $Release1114Migrations) {
        $unmatched112 = @($Expected112Migrations | Where-Object { $p = $_; @($added | Where-Object { $_ -like $p }).Count -ne 1 })
        Add-Check $Area "Migrations added by $NewVersion over 1.11.4 ($Release1114Migrations)" "$($Expected112Migrations.Count): $($Expected112Migrations -join ', ')" "$($added.Count): $(if ($added.Count) { $added -join ', ' } else { 'none' })" (Get-Result (($added.Count -eq $Expected112Migrations.Count) -and ($unmatched112.Count -eq 0)))
    }
    # 1.11.2: name each migration added by this setup run against 0049 .. 0066 (only when the start state was 1.9.6-1.9.9, i.e. 48).
    if ($jb.Count -eq ($ExpectedMigrationCount - $ExpectedNewMigrations.Count)) {
        $unmatched = @($ExpectedNewMigrations | Where-Object { $p = $_; @($added | Where-Object { $_ -like $p }).Count -ne 1 })
        Add-Check $Area "Migrations added by $NewVersion" "$($ExpectedNewMigrations.Count): $($ExpectedNewMigrations -join ', ')" "$($added.Count): $(if ($added.Count) { $added -join ', ' } else { 'none' })" (Get-Result (($added.Count -eq $ExpectedNewMigrations.Count) -and ($unmatched.Count -eq 0)))
    }
    # Whatever the start state, the journal must hold every one of the eighteen (0049-0066) by name.
    $missingNames = @($ExpectedNewMigrations | Where-Object { $p = $_; @($ja | Where-Object { $_ -like $p }).Count -ne 1 })
    Add-Check $Area 'Journal holds 0049-0066 by name' ($ExpectedNewMigrations -join ', ') $(if ($missingNames.Count) { 'missing: ' + ($missingNames -join ', ') } else { 'all present' }) (Get-Result ($missingNames.Count -eq 0))
    $numbers = @($ja | ForEach-Object { [int](([string]$_).Substring(0, 4)) })
    $gaps = @()
    for ($n = 1; $n -le $ExpectedMigrationCount; $n++) { if ($numbers -notcontains $n) { $gaps += ('{0:D4}' -f $n) } }
    $gapText = 'none'
    if ($gaps.Count -gt 0) { $gapText = $gaps -join ', ' }
    Add-Check $Area ('Migration numbers contiguous 0001-{0:D4} (0048 is Service; 0049-0051 are 1.10.0, 0052-0053 1.10.1, 0054-0055 1.11.0, 0056-0058 1.11.1, 0059-0060 1.11.2, 0061 1.11.4, 0062-0066 1.12.0)' -f $ExpectedMigrationCount) 'no gap' $gapText (Get-Result ($gaps.Count -eq 0))
}

function Test-Release195Schema($F, [string]$Area, [switch]$BeforeService) {
    $aw = $F.aw330
    $awText = 'no AW330 row'
    if ($aw) { $awText = "AW330 active=$($aw.is_active) business unit $($aw.business_unit_id)" }
    Add-Check $Area 'Service Centre store row (0048 section A)' 'one AW330 row, inactive (the SERVICE unit)' $awText (Get-Result ([bool]$aw -and [int]$aw.is_active -eq 0))
    $retail = @($F.stores | Where-Object { @('HEMW', 'WLMHW') -contains [string]$_.store_code -and [int]$_.is_active -eq 1 })
    Add-Check $Area 'Retail stores still active' 'HEMW and WLMHW active' (@($F.stores | ForEach-Object { "$($_.store_code)=$($_.is_active)" }) -join ', ') (Get-Result ($retail.Count -eq 2))
    # TODO (merged release): 35 landing tables (L2) plus the GPRC CLAIM table if L10 needed one (decision 16) -> 35 or 36.
    Add-Check $Area 'Service landing tables (0048 section B)' '35, or 36 with a GPRC CLAIM table (TODO: exact number from the merged 0048)' "$($F.serviceTableCount)" (Get-Result ($F.serviceTableCount -ge 35) 'REVIEW')
    Add-Check $Area 'Service read views (0048 section C)' 'at least 11 v_service_* views' "$($F.serviceViewCount)" (Get-Result ([int]$F.serviceViewCount -ge 11) 'REVIEW')
    if ($BeforeService) { Add-Check $Area 'Service landing rows before any Service import' '0' "$($F.serviceLandingRows) [$($F.serviceLandingNonEmpty)]" (Get-Result ([int64]$F.serviceLandingRows -eq 0) 'REVIEW') }
    Add-Check $Area 'No reporting day for AW330 (no Service day locking, no Retail pack)' '0' "$($F.aw330Days)" (Get-Result ($null -ne $F.aw330Days -and [int]$F.aw330Days -eq 0))
    Add-Check $Area 'stock_movements.location (0047)' 'column present; every movement has a bin after the backfill' "column=$($F.locationColumn); movements $($F.movements); without a bin $($F.movementsNoLocation)" (Get-Result ([bool]$F.locationColumn -and [int64]$F.movementsNoLocation -eq 0) 'REVIEW')
    Add-Check $Area 'Calculation catalogue versions (0046: each corrected row +1)' 'recorded; compare with the rehearsal base' (@($F.kpiVersions | ForEach-Object { "$($_.kpi_code) v$($_.version)" }) -join ', ') 'INFO'
    Add-Check $Area 'Database space used' 'recorded (growth baseline)' "$($F.dataUsedMb) MB" 'INFO'
    Test-Release1111Checks $Area
}

function New-PackageZip($Spec, [string]$ZipName, [string]$HashPrefix) {
    # One file of the 29 Sep package in a zip, entry <store>/<file> (the layout the watch-folder import uses).
    $manifest = Join-Path $DataPackage 'SHA256SUMS.txt'
    $want = ''
    foreach ($line in Get-Content -LiteralPath $manifest) {
        if ($line -match '^([0-9a-f]{64}) \*?\./(.+)$' -and (($Matches[2] -replace '/', '\') -eq $Spec.Relative)) { $want = $Matches[1] }
    }
    $path = Join-Path $DataPackage $Spec.Relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Package file not found: $path" }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    $ok = ($want -and $hash -eq $want) -and ((-not $HashPrefix) -or $hash.StartsWith($HashPrefix))
    Add-Check 'Host' "$($Spec.Key) matches SHA256SUMS.txt$(if ($HashPrefix) { " and starts $HashPrefix" })" $(if ($want) { $want.Substring(0, 8) } else { 'listed' }) $hash.Substring(0, 8) (Get-Result $ok)
    if (-not $ok) { throw "$($Spec.Relative) does not match the package's SHA256SUMS.txt. Stop." }
    if (-not $script:TempDir) { $script:TempDir = Join-Path $env:TEMP "EtpVM195-$RunId"; New-Item -ItemType Directory -Path $script:TempDir -Force | Out-Null }
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipPath = Join-Path $script:TempDir $ZipName
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    $zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try { [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $path, ($Spec.Store + '/' + (Split-Path -Leaf $path)), [System.IO.Compression.CompressionLevel]::Optimal) }
    finally { $zip.Dispose() }
    return [pscustomobject]@{ Spec = $Spec; Name = $ZipName; HostZip = $zipPath; ZipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash; FileSha8 = $hash.Substring(0, 8); GuestZip = "$GuestStageDir\Titan\$ZipName" }
}

function Get-OutcomeClass([string]$Outcome) {
    # TODO (merged release): confirm the outcome words import_attempts stores for Not needed / Unknown layout / Empty.
    if ($Outcome -match '(?i)fail|cancel|block|error') { return 'failed' }
    if ($Outcome -match '(?i)unknown|unrecogn') { return 'unknown' }
    if ($Outcome -match '(?i)duplicate|already') { return 'duplicate' }
    if ($Outcome -match '(?i)not.?needed|derived|deferred|skipped') { return 'notneeded' }
    if ($Outcome -match '(?i)import|empty|restat') { return 'imported' }
    return 'other'
}

function Get-OutcomeSummary($Attempts) {
    $classes = [ordered]@{ imported = 0; duplicate = 0; notneeded = 0; unknown = 0; failed = 0; other = 0 }
    foreach ($a in @($Attempts)) { $k = Get-OutcomeClass ([string]$a.outcome); $classes[$k] = [int]$classes[$k] + 1 }
    return $classes
}

# R-HEMW-08 / R-WLMHW-16: a file already loaded, through the watch folder under a new zip name. VM only (never on live).
function Invoke-DuplicateReimportCheck {
    $area = 'Duplicate re-import (R-HEMW-08)'
    $z = New-PackageZip $DuplicateProbe 'Again-HEMW-R025-duplicate-probe.zip' ''
    Copy-ToGuest $z.HostZip $z.GuestZip $z.ZipHash
    $before = Get-Facts 'before-duplicate-probe'
    $imp = Invoke-HeadlessImport -Label 'duplicate-reimport' -GuestZip $z.GuestZip -ZipName $z.Name -What 'HEMW R025 again (duplicate probe)'
    if ($null -eq $imp) { Add-Check $area 'Duplicate probe imported' 'ran' 'not run' 'FAIL'; return }
    $attempts = @($imp.Result.attempts)
    $sum = Get-OutcomeSummary $attempts
    $newRows = [int64](($attempts | Measure-Object -Property new_rows -Sum).Sum)
    Add-Check $area 'Same HEMW R025 content again is Duplicate' 'every attempt Duplicate / already imported; 0 new rows' "$($attempts.Count) attempt(s): $(@($attempts | ForEach-Object { $_.outcome }) -join ', '); new rows $newRows" (Get-Result (($attempts.Count -ge 1) -and ($sum.duplicate -eq $attempts.Count) -and ($newRows -eq 0)))
    $b = @{}; foreach ($row in @($before.rowCounts)) { $b[[string]$row.table_name] = [int64]$row.row_count }
    $a = @{}; foreach ($row in @($imp.Result.rowCounts)) { $a[[string]$row.table_name] = [int64]$row.row_count }
    $changed = @($FactTables | Where-Object { $_ -ne 'dbo.import_files' -and $b.ContainsKey($_) -and $a[$_] -ne $b[$_] } | ForEach-Object { "$_ $($b[$_]) -> $($a[$_])" })
    Add-Check $area 'Fact tables unchanged by the duplicate' 'unchanged' $(if ($changed.Count -gt 0) { $changed -join '; ' } else { 'unchanged' }) (Get-Result ($changed.Count -eq 0))
}

function Invoke-VmPreSteps {
    $rf = Get-ReleaseFacts 'vm-pre'
    Test-Release195Schema $rf 'VM-pre state' -BeforeService
    $f = Get-Facts 'vm-pre'
    foreach ($ta in $TestAccounts) {
        $row = @($f.users | Where-Object { [string]$_.windows_identity -eq $ta.Identity }) | Select-Object -First 1
        $text = 'not in application_users'
        if ($row) { $text = "$($row.role_code), active=$($row.is_active)" }
        $level = 'REVIEW'; if ($UserChanges -eq 'Script') { $level = 'FAIL' }
        Add-Check 'VM-pre state' "Test account $($ta.Identity)" "$($ta.Role), active" $text (Get-Result ([bool]$row -and [string]$row.role_code -eq $ta.Role -and [bool]$row.is_active) $level)
    }
    if (-not $SkipDuplicateCheck) { Invoke-DuplicateReimportCheck }
    $script:VmPreReady = $true
    Add-VmPreUiSteps
}

function Add-VmPreUiSteps {
    $script:UiSteps.Add("VM-PRE (before -Stage Titan; the WLMHW gap days exist only now). Open vmconnect.exe localhost $VmName, sign in as ETPTest, start ETP normally (NOT Run as administrator). Do plan section 4 (VM-ACCEPTANCE-1.9.5-PLAN.md), writing PASS/FAIL/DIFFERENT per line; export each checked report to Documents\Phase6-Reports.")
    $script:UiSteps.Add('VM-pre WLMHW (only here): Cash Book 16 Sep 2024 "R022 missing", modes blank (R-WLMHW-02); Management Trend FY 2024-25 tender variance blank, Tender Source "R022 missing", Tender Reconciliation outside 1 Jul-25 Aug Blocked (R-WLMHW-07); Stock Variance 1 Jul-29 Sep Blocked "Ledger covers to 25 Aug 2026" (R-WLMHW-13); Stock Movement 1 Jul-25 Aug row count and net as in plan section 4 (R-WLMHW-04); Tender Reconciliation 1 Jul-25 Aug the TC invoices Pass (R-WLMHW-03); Import > Problems WLMHW count same with WLMHW and All stores (R-WLMHW-09).')
    $script:UiSteps.Add('VM-pre HEMW: Closing Stock 7 Sep Blocked / no stock (before the R010 re-import of -Stage Titan); 29 Sep stock totals as in plan section 4; headers "Unit MRP" and "MRP value (GST incl.)"; Cash Book and DSR 25 Aug as in plan section 4; and WALK-INS "-" Data not available.')
    $script:UiSteps.Add('Imports > History: the duplicate probe row reads "Duplicate content" with "This source was already imported..." (R-HEMW-08 / R-WLMHW-16).')
    $script:UiSteps.Add('Viewer (R-WLMHW-17): sign out, sign in to Windows as EtpTestViewer (Sagar types the password), start ETP. Close day > Store pack and All stores pack: Generate disabled with a reason tooltip; Actions > Generate report pack disabled; Ctrl+Shift+P gives a message. Settings > Open items as Viewer: UNMATCHED equals the summary, "last synced" shows a time (R-WLMHW-10). Then EtpTestManager: Generate is enabled (do not generate).')
    $script:UiSteps.Add('Settings > Calculations (R-HEMW-05): NET_SALES = SUM(R025.NETAMOUNT), GST-inclusive wording as amended in 0046 (D-L9-3); version +1. Settings > Database > Dashboard overview opens for the Owner only (R-WLMHW-15).')
    $script:UiSteps.Add('Close ETP and sign out of the test accounts. Then on Workpc: -Scenario Rehearsal -Stage Titan.')
}

function Add-VmPostUiSteps {
    $script:UiSteps.Add('VM-POST (plan section 6), ETPTest, ETP started normally: WLMHW Stock Variance 1 Jul-29 Sep: 77196SL03 and 77205KM01 pass with opening 1 (R-WLMHW-05); Stock Movement 1 Jul-29 Sep shows a Location column with DEFECTIVEBIN rows, item totals unchanged (R-WLMHW-14); Cash Book / Tender Reconciliation whole-history TC (R-WLMHW-03), Stock Movement 1 Jul-25 Aug net (R-WLMHW-04) and Staff whole-period attribution/variance (R-WLMHW-01) as in plan section 6.')
    $script:UiSteps.Add('VM-post HEMW: Closing Stock 7 Sep after the R010 re-import: no GIFT CARD row when L9a is present (totals as in plan section 6). Export to Documents\Phase6-Reports.')
    $script:UiSteps.Add('Close ETP. Then on Workpc: -Scenario Rehearsal -Stage Service.')
}

function Invoke-HemwR010Reimport {
    $area = 'HEMW R010 re-import (R-HEMW-01, runbook E2)'
    $z = New-PackageZip $HemwR010 'Titan0-HEMW-R010-7Sep.zip' $HemwR010.Sha256Prefix
    Copy-ToGuest $z.HostZip $z.GuestZip $z.ZipHash
    $imp = Invoke-HeadlessImport -Label 'titan-0-HEMW-R010' -GuestZip $z.GuestZip -ZipName $z.Name -What 'HEMW R010 (7 Sep) again'
    $row = [ordered]@{ Order = 0; File = $HemwR010.Relative; Sha256First8 = $z.FileSha8; Report = ''; Store = ''; Outcome = 'not run'; Rows = $null; New = $null; Present = $null; Conflicts = $null; ExitCode = $null; ExpectedNew = $null; ExpectedPresent = $null }
    if ($null -eq $imp) { $script:TitanRows.Add([pscustomobject]$row); Add-Check $area 'Imported' 'ran' 'not run' 'FAIL'; return }
    $attempts = @($imp.Result.attempts)
    $row.ExitCode = $imp.ExitCode
    $row.Report = (@($attempts | ForEach-Object { [string]$_.report_code } | Select-Object -Unique) -join ',')
    $row.Store = (@($attempts | ForEach-Object { [string]$_.store_code } | Select-Object -Unique) -join ',')
    $row.Outcome = (@($attempts | ForEach-Object { [string]$_.outcome }) -join ',')
    $periods = (@($attempts | ForEach-Object { "$($_.period_start)..$($_.period_end)" } | Select-Object -Unique) -join ',')
    if ($attempts.Count -gt 0) {
        $row.Rows = [int64](($attempts | Measure-Object -Property rows_processed -Sum).Sum)
        $row.New = [int64](($attempts | Measure-Object -Property new_rows -Sum).Sum)
        $row.Present = [int64](($attempts | Measure-Object -Property already_present_rows -Sum).Sum)
        $row.Conflicts = [int64](($attempts | Measure-Object -Property conflict_rows -Sum).Sum)
    }
    $script:TitanRows.Add([pscustomobject]$row)
    $sum = Get-OutcomeSummary $attempts
    $ok = ($attempts.Count -ge 1) -and ($sum.imported -eq $attempts.Count) -and ([int64]$row.New -gt 0) -and ([int64]$row.Conflicts -eq 0)
    Add-Check $area 'Package HEMW BinWise imports as new rows for 7 Sep (not Duplicate, no restatement, 0 conflicts)' 'Imported, period 2026-09-07, new > 0, conflicts 0; counts = the live expectation for E2' "outcome $($row.Outcome); period $periods; rows $($row.Rows), new $($row.New), present $($row.Present), conflicts $($row.Conflicts)" (Get-Result $ok)
    Add-Check $area 'Dated 7 Sep 2026 from its Info block' '2026-09-07' $periods (Get-Result ($periods -match '2026-09-07') 'REVIEW')
    if (-not $ok) { $script:UiSteps.Add('HEMW R010 re-import did not import cleanly: triage with /etp-import-failure before E2 is done on live (do not tick restatement on live).') }
}

function Invoke-TitanStage {
    Open-GuestForStage
    $f0 = Get-Facts 'titan-start'
    $j = @(Get-MigrationIds $f0.journal | Sort-Object -Unique)
    $latest = 'none'; if ($j.Count -gt 0) { $latest = $j[-1] }
    Add-Check 'Titan stage' "VM is on $NewVersion (VM-pre state)" "$ExpectedMigrationCount migrations, latest $ExpectedLatestMigration" "$($j.Count), latest $latest" (Get-Result (($j.Count -eq $ExpectedMigrationCount) -and ($latest -like $ExpectedLatestMigration)))
    if ($latest -notlike $ExpectedLatestMigration) { throw "The VM is not at migration $ExpectedLatestMigration ($NewVersion). Restore the 'VM195 Rehearsal VM-pre' checkpoint first." }
    if (-not $SkipHemwR010) { Invoke-HemwR010Reimport }
    Invoke-TitanImports
    # The live runbook ends with a backup: its receipt's row counts are the post-import counts to expect on live.
    $null = Invoke-BackupAndDrill -Label 'after-titan' -RowCountLevel 'FAIL'
    $rf = Get-ReleaseFacts 'vm-post'
    # The audit copy had 17 DEFECTIVEBIN ledger rows for WLMHW (VERIFY matrix R-WLMHW-14); HEMW may add its own.
    Add-Check 'VM-post state' 'DEFECTIVEBIN movements after the full R030 (0047 + import)' 'at least 17 (WLMHW audit figure)' "$($rf.defectiveBinRows)" (Get-Result ([int64]$rf.defectiveBinRows -ge 17) 'REVIEW')
    Add-Check 'VM-post state' 'Every movement has a bin' '0 without a bin' "$($rf.movementsNoLocation) of $($rf.movements)" (Get-Result ([int64]$rf.movementsNoLocation -eq 0) 'REVIEW')
    Add-VmPostUiSteps
    Wait-NoEtpProcess
    New-Checkpoint ("VM195 Rehearsal VM-post (Titan + HEMW R010 loaded), " + (Get-Date -Format 'yyyy-MM-dd HH:mm'))
}

function Test-ServiceSources {
    $cons = Join-Path $DataPackage $ServiceConsolidatedRelative
    $nc = @(Get-ChildItem -LiteralPath $cons -File -ErrorAction SilentlyContinue | Where-Object { -not $_.Name.StartsWith('~$') }).Count
    Add-Check 'Host' 'Consolidated Service folder (29 Sep package)' "$($ServiceExpect.ConsolidatedFiles) files" "$nc" (Get-Result ($nc -eq $ServiceExpect.ConsolidatedFiles) 'REVIEW')
    $nr = @(Get-ChildItem -LiteralPath $ServiceRawFolder -File -ErrorAction SilentlyContinue | Where-Object { -not $_.Name.StartsWith('~$') }).Count
    Add-Check 'Host' 'Raw Service pack (3 Oct)' "$($ServiceExpect.RawFiles) files" "$nr" (Get-Result ($nr -eq $ServiceExpect.RawFiles) 'REVIEW')
    if ($nc -eq 0 -or $nr -eq 0) { throw 'A Service source folder is missing or empty.' }
    if ($GprcExportFolder -and -not (Test-Path -LiteralPath $GprcExportFolder -PathType Container)) { throw "GPRC export folder not found: $GprcExportFolder" }
}

function Send-ServiceFolder([string]$HostFolder, [string]$GuestName) {
    # Copies (never moves) every file into Documents\VM195-Import\Service\<GuestName>, file names unchanged
    # (trailing spaces before the extension included), where the unelevated app can read them.
    $dest = "$($script:GuestDocuments)\VM195-Import\Service\$GuestName"
    Invoke-Guest { param($d) New-Item -ItemType Directory -Path $d -Force | Out-Null } @($dest) | Out-Null
    $files = @(Get-ChildItem -LiteralPath $HostFolder -File | Where-Object { -not $_.Name.StartsWith('~$') })
    foreach ($file in $files) { Copy-Item -ToSession $script:Session -LiteralPath $file.FullName -Destination (Join-Path $dest $file.Name) -Force }
    $n = [int](Invoke-Guest { param($d) @(Get-ChildItem -LiteralPath $d -File).Count } @($dest))
    Add-Check 'Service' "Staged in the guest: $GuestName" "$($files.Count) files" "$n" (Get-Result ($n -eq $files.Count))
    return $dest
}

function Invoke-ServiceImportRound([string]$Label, [string]$Prompt, [string]$GuestFolder) {
    if ($ImportMode -eq 'Headless' -and $GuestFolder) {
        # Same route as the Titan files: zip the staged folder in the guest, drop it in the watch folder, run the app's unattended import.
        # (-ImportMode Ui keeps the by-hand Import folder screen path below.) A distinct zip name per round, so the repeat is a real duplicate.
        $zipName = "$Label.zip"
        $guestZip = "$GuestStageDir\$zipName"
        Invoke-Guest { param($src, $zip) Add-Type -AssemblyName System.IO.Compression.FileSystem; if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }; [IO.Compression.ZipFile]::CreateFromDirectory($src, $zip) } @($GuestFolder, $guestZip) | Out-Null
        $imp = Invoke-HeadlessImport -Label $Label -GuestZip $guestZip -ZipName $zipName -What "Service folder $GuestFolder"
        if ($null -eq $imp) { throw "The watch-folder import could not start for $Label (see the FAIL above). Re-run with -ImportMode Ui to import by hand." }
        $r = $imp.Result
        Save-Csv "service\$Label-attempts.csv" $r.attempts
        Save-Csv "service\$Label-automation-runs.csv" $r.runs
        return $r
    }
    $since = [string](Invoke-Guest $GuestNowBlock)
    while ($true) {
        Write-Host ''
        Write-Host "  $Prompt" -ForegroundColor Yellow
        # 7 Oct: Read-Host '...press Enter' returned at once in the elevated window (the whole stage ran in 30 s, nothing imported).
        # Ask for a typed word instead, and stop if the console is not waiting for input.
        $instant = 0
        while ($true) {
            $t0 = Get-Date
            $answer = Read-Host '  When the import has finished and ETP shows the result grid, type DONE and press Enter (SKIP to continue without it)'
            if ($answer -match '^\s*(done|skip)\s*$') { break }
            if (((Get-Date) - $t0).TotalSeconds -lt 1) { $instant++ } else { $instant = 0 }
            if ($instant -ge 5) { throw 'This console is not waiting for typed input (Read-Host returned empty five times at once). Run the script in a normal elevated PowerShell window.' }
        }
        Connect-Guest   # the session may have idled out while Sagar worked in the console
        $r = Invoke-GuestJson $AttemptsSinceBlock @($since)
        Repair-RowSets $r @('attempts', 'runs', 'rowCounts', 'fingerprint')
        if (@($r.attempts).Count -gt 0 -or $answer -match '^\s*skip\s*$') { break }
        Write-Host '  No import attempt has been recorded in the VM since this step began. Do the import in ETP in the VM, then type DONE again.' -ForegroundColor Red
    }
    Save-Csv "service\$Label-attempts.csv" $r.attempts
    Save-Csv "service\$Label-automation-runs.csv" $r.runs
    return $r
}

function Invoke-ServiceStage {
    Open-GuestForStage
    $area = 'Service'
    $rf0 = Get-ReleaseFacts 'service-before'
    Test-Release195Schema $rf0 'Service (before)' -BeforeService
    $base = Invoke-GuestJson $AttemptsSinceBlock @([string](Invoke-Guest $GuestNowBlock))
    Repair-RowSets $base @('attempts', 'runs', 'rowCounts', 'fingerprint')
    $consFolder = Send-ServiceFolder (Join-Path $DataPackage $ServiceConsolidatedRelative) $ServiceConsolidatedGuestName
    $rawFolder = Send-ServiceFolder $ServiceRawFolder $ServiceRawGuestName
    $serviceStartUtc = [string](Invoke-Guest $GuestNowBlock)

    # 1. Consolidated folder (runbook step 4). TODO (real Service behaviour): confirm whether Not-needed files write attempts.
    $r1 = Invoke-ServiceImportRound 'service-1-consolidated' "In the VM console, ETP as ETPTest (Owner, not elevated): Import > Import folder > $consFolder > import (restatement unticked)." $consFolder
    $s1 = Get-OutcomeSummary $r1.attempts
    $imp1 = @($r1.attempts | Where-Object { (Get-OutcomeClass ([string]$_.outcome)) -eq 'imported' })
    Add-Check $area 'Consolidated: Imported' "$($ServiceExpect.ConsolidatedImported) (an empty family may read Empty)" "$($s1.imported); all: $(@($s1.Keys | ForEach-Object { "$_=$($s1[$_])" }) -join ', ')" (Get-Result ($s1.imported -eq $ServiceExpect.ConsolidatedImported) 'REVIEW')
    Add-Check $area 'Consolidated: no Failed and no Unknown layout' '0' "failed $($s1.failed), unknown $($s1.unknown)" (Get-Result (($s1.failed -eq 0) -and ($s1.unknown -eq 0)))
    Add-Check $area 'Consolidated: Not needed (S001, S005, S038, S027, S028, the 00_ control file)' "$($ServiceExpect.ConsolidatedNotNeeded) (if listed as attempts)" "$($s1.notneeded)" 'INFO'
    $wrongStore = @($imp1 | Where-Object { [string]$_.store_code -ne 'AW330' })
    Add-Check $area 'Consolidated: every imported Service file is store AW330' 'AW330 only' "$(@($imp1 | ForEach-Object { $_.store_code } | Select-Object -Unique) -join ', ')" (Get-Result (($imp1.Count -gt 0) -and ($wrongStore.Count -eq 0)))
    $badDate = @($imp1 | Where-Object { [string]$_.period_end -ne '2026-09-29' })
    Add-Check $area 'Consolidated: snapshot date from the folder name' '2026-09-29' "$(@($imp1 | ForEach-Object { $_.period_end } | Select-Object -Unique) -join ', ')" (Get-Result ($badDate.Count -eq 0) 'REVIEW')

    # 2. Same folder again (runbook step 5).
    $r2 = Invoke-ServiceImportRound 'service-2-consolidated-again' "Import the SAME folder again: Import > Import folder > $consFolder." $consFolder
    $s2 = Get-OutcomeSummary $r2.attempts
    $new2 = [int64]((@($r2.attempts) | Measure-Object -Property new_rows -Sum).Sum)
    Add-Check $area 'Consolidated again: all Duplicate / already present' '0 Imported, 0 Failed, 0 new rows' "imported $($s2.imported), duplicate $($s2.duplicate), failed $($s2.failed), new rows $new2" (Get-Result (($s2.imported -eq 0) -and ($s2.failed -eq 0) -and ($new2 -eq 0)))
    $rfMid = Get-ReleaseFacts 'service-after-consolidated'

    # 3. Raw pack (runbook step 6), with GPRC CLAIM as its own family (decision 16 Q6).
    $r3 = Invoke-ServiceImportRound 'service-3-raw' "Import the raw pack: Import > Import folder > $rawFolder." $rawFolder
    $s3 = Get-OutcomeSummary $r3.attempts
    Add-Check $area 'Raw: Imported' "$($ServiceExpect.RawImported) (20 + GPRC CLAIM)" "$($s3.imported); all: $(@($s3.Keys | ForEach-Object { "$_=$($s3[$_])" }) -join ', ')" (Get-Result ($s3.imported -eq $ServiceExpect.RawImported) 'REVIEW')
    Add-Check $area 'Raw: Duplicate (the second Empowerment file)' "$($ServiceExpect.RawDuplicate)" "$($s3.duplicate)" (Get-Result ($s3.duplicate -eq $ServiceExpect.RawDuplicate) 'REVIEW')
    Add-Check $area 'Raw: no Failed (a Failed raw file is an IF-024 finding)' '0' "$($s3.failed)" (Get-Result ($s3.failed -eq 0))
    Add-Check $area 'Raw: Unknown layout only TATA REPORT (S027 deferred), not Failed' "$($ServiceExpect.RawUnknown)" "$($s3.unknown): $(@($r3.attempts | Where-Object { (Get-OutcomeClass ([string]$_.outcome)) -eq 'unknown' } | ForEach-Object { $_.file_name }) -join '; ')" (Get-Result ($s3.unknown -le $ServiceExpect.RawUnknown) 'REVIEW')
    $gprc = @($r3.attempts | Where-Object { ([string]$_.file_name) -like 'GPRC CLAIM*' })
    $gprcText = 'no attempt for GPRC CLAIM'
    if ($gprc.Count -gt 0) { $gprcText = (@($gprc | ForEach-Object { "$($_.report_code) $($_.outcome) store $($_.store_code) new $($_.new_rows)" }) -join '; ') }
    # TODO (merged release, lane L10): put the real GPRC CLAIM family code here instead of 'not S023 and not blank'.
    $gprcOk = ($gprc.Count -ge 1) -and (@($gprc | Where-Object { (Get-OutcomeClass ([string]$_.outcome)) -eq 'imported' -and [string]$_.report_code -and [string]$_.report_code -ne 'S023' -and [string]$_.store_code -eq 'AW330' }).Count -eq $gprc.Count)
    Add-Check $area 'GPRC CLAIM imports as its OWN family (decision 16 Q6), not as S023, not Unknown layout' 'Imported, own report code, AW330' $gprcText (Get-Result $gprcOk)
    $rawImp = @($r3.attempts | Where-Object { (Get-OutcomeClass ([string]$_.outcome)) -eq 'imported' })
    Add-Check $area 'Raw: dated by the end of the window in the file name' '2026-10-03' "$(@($rawImp | ForEach-Object { $_.period_end } | Select-Object -Unique) -join ', ')" (Get-Result (@($rawImp | Where-Object { [string]$_.period_end -ne '2026-10-03' }).Count -eq 0) 'REVIEW')

    # 4. Optional: Sagar's GPRC CLAIM export 1 Aug 2026 - today (decision 16 Q8).
    if ($GprcExportFolder) {
        $gFolder = Send-ServiceFolder $GprcExportFolder 'GPRC CLAIM export'
        $r4 = Invoke-ServiceImportRound 'service-4-gprc-export' "Import the GPRC CLAIM export: Import > Import folder > $gFolder." $gFolder
        $s4 = Get-OutcomeSummary $r4.attempts
        Add-Check $area 'GPRC CLAIM export (1 Aug - today) imports into the GPRC CLAIM family' 'Imported, 0 Failed, 0 Unknown' "$(@($r4.attempts | ForEach-Object { "$($_.report_code) $($_.outcome)" }) -join '; ')" (Get-Result (($s4.imported -ge 1) -and ($s4.failed -eq 0) -and ($s4.unknown -eq 0)))
    }
    else { $script:Notes.Add('GPRC CLAIM export 1 Aug - today (decision 16 Q8) was not given (-GprcExportFolder); the S023 + GPRC CLAIM union is checked on screen only.') }

    # 5. S004 money per date (decision 16 Q2), compared inside the guest.
    $s004 = @(Invoke-GuestJson $S004CheckBlock @((ConvertTo-Json -InputObject @($S004Expected) -Compress)))
    Save-Csv 'service\s004-check.csv' $s004
    foreach ($d in $s004) {
        $text = "match=$($d.match)"
        if ($d.differing) { $text += "; differs: $($d.differing)" }
        if ($d.error) { $text += "; error: $($d.error)" }
        if (-not $d.viewFound) { $text = 'v_service_s004_daily not found' }
        Add-Check $area "S004 money $($d.date) (Cash/Card/UPI per tender)" 'equals the known S004 totals (plan section 7.4; amounts not written here)' $text (Get-Result ([bool]$d.match))
    }

    # 6. Retail unaffected.
    $end = Invoke-GuestJson $AttemptsSinceBlock @($serviceStartUtc)
    Repair-RowSets $end @('attempts', 'runs', 'rowCounts', 'fingerprint')
    $b = @{}; foreach ($row in @($base.rowCounts)) { $b[[string]$row.table_name] = [int64]$row.row_count }
    $a = @{}; foreach ($row in @($end.rowCounts)) { $a[[string]$row.table_name] = [int64]$row.row_count }
    $changed = @($RetailFactTables | Where-Object { $b.ContainsKey($_) -and $a[$_] -ne $b[$_] } | ForEach-Object { "$_ $($b[$_]) -> $($a[$_])" })
    Add-Check $area 'Retail tables unchanged by the Service imports' ($RetailFactTables -join ', ') $(if ($changed.Count -gt 0) { $changed -join '; ' } else { 'unchanged' }) (Get-Result ($changed.Count -eq 0))
    foreach ($x in @($base.fingerprint)) {
        $y = @(@($end.fingerprint) | Where-Object { $_.store_code -eq $x.store_code }) | Select-Object -First 1
        Add-Check $area "Retail store $($x.store_code) sales totals (hashed) unchanged" 'same hash, same counts' "same=$([bool]$y -and ([string]$y.totals_sha256 -eq [string]$x.totals_sha256))" (Get-Result ([bool]$y -and ([string]$y.totals_sha256 -eq [string]$x.totals_sha256) -and ([int64]$y.invoices -eq [int64]$x.invoices)))
    }
    $awFp = @(@($end.fingerprint) | Where-Object { [string]$_.store_code -eq 'AW330' })
    Add-Check $area 'No Retail sales rows for AW330' 'none' "$($awFp.Count) store row(s)" (Get-Result ($awFp.Count -eq 0))
    $awPacks = @(@($end.runs) | Where-Object { [string]$_.store_code -eq 'AW330' -or ([string]$_.run_type -eq 'AUTO_REPORT_PACK' -and -not [string]$_.store_code) })
    Add-Check $area 'No report pack or automation run for AW330 during the Service imports' 'none' "$($awPacks.Count): $(@($awPacks | ForEach-Object { "$($_.run_type) $($_.store_code) $($_.outcome)" }) -join '; ')" (Get-Result ($awPacks.Count -eq 0) 'REVIEW')
    $rf1 = Get-ReleaseFacts 'service-after'
    Test-Release195Schema $rf1 'Service (after)'
    Add-Check $area 'Growth (SERVICE-REHEARSAL-RUNBOOK step 3)' 'recorded: landing rows and MB before / after consolidated / after raw' "rows $($rf0.serviceLandingRows) / $($rfMid.serviceLandingRows) / $($rf1.serviceLandingRows); MB $($rf0.dataUsedMb) / $($rfMid.dataUsedMb) / $($rf1.dataUsedMb)" 'INFO'
    Add-ServiceUiSteps
    Wait-NoEtpProcess -AskSagar
    New-Checkpoint ("VM195 Rehearsal Service imported, " + (Get-Date -Format 'yyyy-MM-dd HH:mm'))
}

function Add-ServiceUiSteps {
    $script:UiSteps.Add('1.10.0: the Service screens moved to the Service item of the left rail (tabs Today, Pending, Jobs, Claims, Parts, Money); Reports no longer lists "Service centre". Check them against SERVICE-UI-1.10.0-ACCEPTANCE.md sections 4-7 (screen = SQL), then run -Stage ServiceUi for the SQL side and the timings.')
    $script:UiSteps.Add('SERVICE screens (plan section 7.5; 1.9.x names), ETPTest Owner: Reports > Service centre: Jobs by status (header "Service data as at 03 Oct 2026"), Pending lists with ages, Job history for one job (do not record it), Service money check 30 Sep - 2 Oct. No phone, e-mail or address column anywhere; export one screen and check its columns, then delete the file.')
    $script:UiSteps.Add('Money check rules (decision 16): with no manual entries every S004 tender shows as a difference (nothing corrected). Then Cash and service entries, WLMHW, 1 Oct 2026: SERVICE_CASH and SERVICE_UPI equal to the S004 figures -> the 1 Oct difference is 0. HEMW, 1 Oct: a small SERVICE_CASH -> listed separately as "Service entry at Helios: not matched to AW330", never added. a SERVICE_WDC entry under WLMHW -> not in the tender comparison. VM only; never on live.')
    $script:UiSteps.Add('GPRC CLAIM: the GPRC screen/report reads S023 history (to 5 Aug) AND the GPRC CLAIM family (union, decision 16). TODO: screen name from lane L10.')
    $script:UiSteps.Add('Retail: Settings > Stores & masters lists AW330 as "Service Centre (AW330), Service, not a shop store" with Active disabled; Today shows the same combined date as before; generate one Retail daily pack for that date: WLMHW and HEMW only, same totals as before the Service imports.')
    $script:UiSteps.Add('Close ETP. Then on Workpc: -Scenario Rehearsal -Stage Final.')
}

function Invoke-FinalStage {
    Open-GuestForStage
    Wait-NoEtpProcess -AskSagar
    $null = Invoke-BackupAndDrill -Label 'final' -WithDrill -RowCountLevel 'FAIL'
    $rf = Get-ReleaseFacts 'final'
    Add-Check 'Final' 'Service rows kept after backup + drill' 'landing rows > 0' "$($rf.serviceLandingRows)" (Get-Result ([int64]$rf.serviceLandingRows -gt 0) 'REVIEW')
    Test-ReportExports 'final'
    $script:UiSteps.Add('Sagar: tick every line of the plan sections 4, 6 and 7; give the PASS/FAIL/DIFFERENT list to Claude. The checkpoints after the imports hold shop data; deleting them is Sagar''s decision.')
    Wait-NoEtpProcess
    New-Checkpoint ("VM195 Rehearsal final, " + (Get-Date -Format 'yyyy-MM-dd HH:mm'))
}


# -Stage Delta (1.10.0, 10 Oct 2026): the 1.10.0 installer OVER the 1.9.9 VM (or the 1.9.8 VM), then the Service raw pack again. With
# -CheckpointName the stage first restores that checkpoint (YES asked unless -Yes); without it the VM must already be on 1.9.9 or 1.9.8 and
# nothing is restored. Expect: setup 1.9.9 -> 1.12.0 applies exactly 0049-0066 (48 -> 66) after a verified pre-migration backup, loses no
# row and changes no row count of a table that existed before (Compare-Upgrade + Test-NoRowChanges), the bootstrap log of the run present
# (Test-BootstrapModuleLog); staff targets are Owner-only (0051, Test-StaffTargetsOwnerOnly); the raw pack re-imports with EVERY file
# Duplicate or Not needed and 0 new rows; Service rows unchanged; backup + drill pass; then checkpoint "VM1100 delta". Run -Stage ServiceUi
# afterwards for the Service UI SQL checks and timings. (The 1.9.7 version of this stage went 1.9.6 -> 1.9.7 with no migration.)
function Invoke-DeltaStage {
    if ($CheckpointName) {
        $snap = Find-Checkpoint $CheckpointName
        $script:RunInfo['Checkpoint restored'] = "$($snap.Name) (created $($snap.CreationTime.ToString('yyyy-MM-dd HH:mm')))"
        if (-not $Yes) {
            $answer = Read-Host "Type YES to restore '$($snap.Name)' in $VmName. The VM's current state is discarded; no checkpoint is deleted"
            if ($answer -ne 'YES') { throw 'Stopped before restoring: YES was not typed.' }
        }
        if ($CheckpointCurrentStateFirst) { New-Checkpoint "Auto: state before VM195 run $RunId" }
        Write-Step "Restoring checkpoint: $($snap.Name)"
        Restore-Checkpoint $snap
    }
    Open-GuestForStage
    $area = "Delta $NewVersion"
    Wait-NoEtpProcess -AskSagar
    $fBefore = Get-Facts 'delta-before'
    $verBefore = [string]$fBefore.exeProductVersion
    $from = @($DeltaFromVersions | Where-Object { $verBefore -like "$_*" }) | Select-Object -First 1
    Add-Check $area "Before: the VM is on $($DeltaFromVersions -join ' or ')" "$($DeltaFromVersions -join '* or ')*" $verBefore (Get-Result ([bool]$from))
    if (-not $from) { throw "The VM is not on $($DeltaFromVersions -join ' or ') (it reports '$verBefore'). Pass -CheckpointName '<the 1.9.9 or 1.9.8 checkpoint*>', or restore it first (Restore-VMSnapshot), then run -Stage Delta again." }
    $script:DeltaFrom = [string]$from
    $script:RunInfo['Delta from'] = "$from ($verBefore)"
    $jBefore = @(Get-MigrationIds $fBefore.journal | Sort-Object -Unique)
    $jBeforeLatest = 'none'
    if ($jBefore.Count -gt 0) { $jBeforeLatest = $jBefore[-1] }
    $startCount = $ExpectedMigrationCount - $ExpectedNewMigrations.Count
    Add-Check $area "Before: migration level of $from" "$startCount, latest 0048_service_centre_interim" "$($jBefore.Count), latest $jBeforeLatest" (Get-Result (($jBefore.Count -eq $startCount) -and ($jBeforeLatest -eq '0048_service_centre_interim')))
    $rf0 = Get-ReleaseFacts 'delta-before'
    $landing0 = [int64]$rf0.serviceLandingRows
    Add-Check $area 'Before: Service rows already loaded (Service stage / earlier deltas)' 'landing rows > 0' "$landing0 [$($rf0.serviceLandingNonEmpty)]" (Get-Result ($landing0 -gt 0) 'REVIEW')
    $s0 = [string]$rf0.serviceLandingNonEmpty
    $bothBefore = ($s0 -match 'etp_landing_s007=[1-9]') -and ($s0 -match 'etp_landing_s008=[1-9]')
    Add-Check $area 'Before: S007 and S008 hold rows (IF-026 proven by the 1.9.6 delta)' 'both non-empty' "S007/S008 non-empty=$bothBefore" (Get-Result $bothBefore 'REVIEW')

    $guestInstaller = "$GuestStageDir\" + (Split-Path -Leaf $InstallerPath)
    if (-not [bool](Invoke-Guest { param($p) Test-Path -LiteralPath $p } @($guestInstaller))) { Copy-ToGuest $InstallerPath $guestInstaller $script:InstallerHash }
    $setupArea = "Setup $NewVersion over $from"
    $run = Invoke-Setup -Label "setup-$NewVersion-upgrade" -GuestInstaller $guestInstaller -Tasks 'desktopicon' -AskToCloseApp
    Test-SetupRun $run $setupArea
    Test-BootstrapModuleLog $run $setupArea
    $f1 = Get-Facts 'delta-after-setup'
    Test-InstalledState $f1 "Installed state ($NewVersion)" $NewVersion -Upgrade
    # Compare-Upgrade: no decrease, users kept, journal kept, and (18 migrations pending) the verified pre-migration backup line.
    Compare-Upgrade $fBefore $f1 $run
    Test-NoRowChanges $fBefore $f1 "No row changes ($from -> $NewVersion)"
    # Test-MigrationTarget: 66, latest 0066_tally_test_machine, contiguous, and the 18 added ids match 0049 .. 0066 by name.
    Test-MigrationTarget $fBefore $f1 "Installed state ($NewVersion)"
    Test-StaffTargetsOwnerOnly "Installed state ($NewVersion)"
    $rf1 = Get-ReleaseFacts 'delta-after-setup'
    Add-Check $area 'Setup alone changed no Service rows' "$landing0" "$($rf1.serviceLandingRows)" (Get-Result ([int64]$rf1.serviceLandingRows -eq $landing0))

    $rawFolder = Send-ServiceFolder $ServiceRawFolder $ServiceRawGuestName
    $r = Invoke-ServiceImportRound 'delta-1100-raw-again' "Import the Service raw pack again: Import > Import folder > $rawFolder." $rawFolder
    $sum = Get-OutcomeSummary $r.attempts
    Add-Check $area 'Raw pack again: no Failed, no Unknown layout' '0' "failed $($sum.failed), unknown $($sum.unknown); all: $(@($sum.Keys | ForEach-Object { "$_=$($sum[$_])" }) -join ', ')" (Get-Result (($sum.failed -eq 0) -and ($sum.unknown -eq 0)))
    $attempts = @($r.attempts)
    $notDup = @($attempts | Where-Object { (Get-OutcomeClass ([string]$_.outcome)) -notin @('duplicate', 'notneeded') })
    $newRows = [int64](@($attempts) | Measure-Object -Property new_rows -Sum).Sum
    Add-Check $area 'Raw pack again: every file Duplicate or Not needed, 0 new rows (S007/S008 included)' "$($attempts.Count) attempts, none Imported/Failed, 0 new rows" "not duplicate or not needed: $(if ($notDup.Count) { @($notDup | ForEach-Object { "$($_.file_name) $($_.outcome)" }) -join '; ' } else { 'none' }); new rows $newRows" (Get-Result (($attempts.Count -ge 1) -and ($notDup.Count -eq 0) -and ($newRows -eq 0)))
    $pr = @($attempts | Where-Object { ([string]$_.file_name) -match '(?i)PURCH' })
    Add-Check $area 'Raw pack again: the two purchase registers (S007, S008) are Duplicate' '2 attempts, Duplicate' (@($pr | ForEach-Object { "$($_.report_code) $($_.outcome) new $($_.new_rows)" }) -join '; ') (Get-Result (($pr.Count -eq 2) -and (@($pr | Where-Object { (Get-OutcomeClass ([string]$_.outcome)) -eq 'duplicate' }).Count -eq 2)) 'REVIEW')

    $rf2 = Get-ReleaseFacts 'delta-after-import'
    $s2 = [string]$rf2.serviceLandingNonEmpty
    $bothLoaded = ($s2 -match 'etp_landing_s007=[1-9]') -and ($s2 -match 'etp_landing_s008=[1-9]')
    Add-Check $area 'After: S007 and S008 landing tables still hold rows' 'both non-empty' "S007/S008 non-empty=$bothLoaded (table list in release-facts-delta-after-import.json)" (Get-Result $bothLoaded)
    Add-Check $area 'After: Service rows unchanged by the duplicate re-import' "$landing0" "$($rf2.serviceLandingRows)" (Get-Result ([int64]$rf2.serviceLandingRows -eq $landing0))
    Test-Release195Schema $rf2 'Delta (after)'
    $null = Invoke-BackupAndDrill -Label 'delta' -WithDrill -RowCountLevel 'FAIL'
    $script:UiSteps.Add("Delta $NewVersion`: start ETP in the VM (ETPTest, not elevated). The left rail shows Service; its six tabs (Today, Pending, Jobs, Claims, Parts, Money) open; Reports no longer lists Service centre. Settings > Staff targets (or wherever targets are edited): as EtpTestManager the targets cannot be saved (0051), as ETPTest (Owner) they can (do not change a value).")
    $script:UiSteps.Add('Delta: Import > History for the raw pack re-import: the 00_ control file row must carry no BLOCKER lines (IF-027).')
    $script:UiSteps.Add('Then on Workpc: -Scenario Rehearsal -Stage ServiceUi (read-only SQL checks + timings), and the screen checks of SERVICE-UI-1.10.0-ACCEPTANCE.md sections 4-7 in the VM console.')
    Wait-NoEtpProcess
    New-Checkpoint ("VM1100 delta ($NewVersion over $from, raw pack again), " + (Get-Date -Format 'yyyy-MM-dd HH:mm'))
}

# 1.11.2 Delta: setup alone must change no row count of a table that existed before; only the journal grows (by the 18 migrations).
# A Retail, fact or key table that changes is a FAIL; any other table is REVIEW (a migration may seed or restate rows on purpose, and the
# five-minute automation task can log a run between the two readings; read the bootstrap log). Tables the migrations add are listed (INFO).
function Test-NoRowChanges($Before, $After, [string]$Area) {
    $b = [ordered]@{}; foreach ($row in @($Before.rowCounts)) { $b[[string]$row.table_name] = [int64]$row.row_count }
    $a = @{}; foreach ($row in @($After.rowCounts)) { $a[[string]$row.table_name] = [int64]$row.row_count }
    $hard = @(@($KeyTables) + @($RetailFactTables) + @($FactTables) | Select-Object -Unique)
    $hardChanged = @(); $softChanged = @()
    foreach ($t in @($b.Keys)) {
        if ($t -eq 'dbo.schema_migrations' -or -not $a.ContainsKey($t)) { continue }
        if ($a[$t] -ne $b[$t]) {
            $text = "$t $($b[$t]) -> $($a[$t])"
            if ($hard -contains $t) { $hardChanged += $text } else { $softChanged += $text }
        }
    }
    $newTables = @(@($a.Keys) | Where-Object { -not $b.Contains($_) } | Sort-Object | ForEach-Object { "$_ ($($a[$_]) rows)" })
    $hardText = 'unchanged'; if ($hardChanged.Count -gt 0) { $hardText = $hardChanged -join '; ' }
    $softText = 'unchanged'; if ($softChanged.Count -gt 0) { $softText = $softChanged -join '; ' }
    $newText = 'none'; if ($newTables.Count -gt 0) { $newText = $newTables -join '; ' }
    Add-Check $Area 'Retail, fact and key tables: row counts unchanged by setup' "$($hard -join ', '): same count before and after" $hardText (Get-Result ($hardChanged.Count -eq 0))
    Add-Check $Area 'Every other table that existed before: row count unchanged by setup (dbo.schema_migrations excepted)' 'no change' $softText (Get-Result ($softChanged.Count -eq 0) 'REVIEW')
    Add-Check $Area 'Tables added by the new migrations' 'recorded (0049 Tally cost centres; 0050 job index table; diag tables of 0054 and 0055)' $newText 'INFO'
}

# 0051 (1.10.0): staff targets are Owner-only. Proven with HAS_PERMS_BY_NAME under EXECUTE AS USER of the VM's store-manager test user
# (EtpTestManager, added by -UserChanges Script / Settings > Users); if that user is not in the database or is not a Store Manager, from
# sys.database_permissions (object, schema dbo and database level) and the role memberships of etp_store_manager. Read-only: EXECUTE AS
# only changes the session's security context, and REVERT ends it in the same batch.
$StaffTargetsBlock = {
    param([string]$Table, [string]$ManagerIdentity)
    $r = [ordered]@{ tableExists = $false; testIdentity = $ManagerIdentity; testUser = ''; testRole = ''; testActive = $null; asUser = $null; asUserError = ''
        permissions = @(); storeManagerRoles = @(); errors = @() }
    try { $r.tableExists = [bool]((Invoke-P6Sql -Database 'EtpReporting' -Parameters @{ t = $Table } -Query "SELECT CASE WHEN OBJECT_ID(@t, N'U') IS NULL THEN 0 ELSE 1 END AS e")[0].e) }
    catch { $r.errors += "table: $($_.Exception.Message)" }
    if (-not $r.tableExists) { return ($r | ConvertTo-Json -Depth 5 -Compress) }
    $permSql = @'
SELECT r.name AS role_name, p.class_desc, p.permission_name, p.state_desc
FROM sys.database_permissions AS p JOIN sys.database_principals AS r ON r.principal_id = p.grantee_principal_id
WHERE r.name IN (N'etp_store_manager', N'etp_owner', N'etp_viewer')
  AND ((p.class = 1 AND p.major_id = OBJECT_ID(@t)) OR (p.class = 3 AND p.major_id = SCHEMA_ID(N'dbo')) OR p.class = 0)
  AND p.permission_name IN (N'INSERT', N'UPDATE', N'DELETE', N'SELECT', N'CONTROL', N'ALTER')
ORDER BY r.name, p.class, p.permission_name;
'@
    try { $r.permissions = (Invoke-P6Sql -Database 'EtpReporting' -Parameters @{ t = $Table } -Query $permSql) } catch { $r.errors += "permissions: $($_.Exception.Message)" }
    try {
        $roles = Invoke-P6Sql -Database 'EtpReporting' -Query "SELECT rr.name AS role_name FROM sys.database_role_members AS m JOIN sys.database_principals AS rr ON rr.principal_id = m.role_principal_id WHERE m.member_principal_id = DATABASE_PRINCIPAL_ID(N'etp_store_manager') ORDER BY rr.name"
        $r.storeManagerRoles = @($roles | ForEach-Object { [string]$_.role_name })
    }
    catch { $r.errors += "role memberships: $($_.Exception.Message)" }
    if ($ManagerIdentity) {
        try {
            $u = Invoke-P6Sql -Database 'EtpReporting' -Parameters @{ id = $ManagerIdentity } -Query "SELECT dp.name AS user_name, u.role_code, CONVERT(int, u.is_active) AS is_active FROM dbo.application_users AS u JOIN sys.database_principals AS dp ON dp.sid = SUSER_SID(u.windows_identity) WHERE u.windows_identity = @id"
            if ($u.Count -gt 0) { $r.testUser = [string]$u[0].user_name; $r.testRole = [string]$u[0].role_code; $r.testActive = [int]$u[0].is_active }
        }
        catch { $r.errors += "test user: $($_.Exception.Message)" }
    }
    if ($r.testUser) {
        $asSql = @'
DECLARE @sql nvarchar(max) = N'EXECUTE AS USER = N''' + REPLACE(@u, N'''', N'''''') + N''';
SELECT USER_NAME() AS acting_as, IS_MEMBER(N''etp_store_manager'') AS in_store_manager,
  HAS_PERMS_BY_NAME(@t, N''OBJECT'', N''INSERT'') AS can_insert, HAS_PERMS_BY_NAME(@t, N''OBJECT'', N''UPDATE'') AS can_update,
  HAS_PERMS_BY_NAME(@t, N''OBJECT'', N''DELETE'') AS can_delete, HAS_PERMS_BY_NAME(@t, N''OBJECT'', N''SELECT'') AS can_select;
REVERT;';
EXEC sys.sp_executesql @sql, N'@t nvarchar(256)', @t = @t;
'@
        try {
            $rows = Invoke-P6Sql -Database 'EtpReporting' -Parameters @{ u = $r.testUser; t = $Table } -Query $asSql
            if ($rows.Count -gt 0) { $r.asUser = $rows[0] } else { $r.asUserError = 'no row returned' }
        }
        catch { $r.asUserError = $_.Exception.Message }
    }
    $r | ConvertTo-Json -Depth 5 -Compress
}

function Test-StaffTargetsOwnerOnly([string]$Area) {
    $manager = @($TestAccounts | Where-Object { $_.Role -eq 'STORE_MANAGER' }) | Select-Object -First 1
    $identity = ''
    if ($manager) { $identity = [string]$manager.Identity }
    $r = Invoke-GuestJson $StaffTargetsBlock @($StaffTargetsTable, $identity)
    Repair-RowSets $r @('permissions', 'storeManagerRoles') @('asUser')
    Save-Json ('staff-targets-0051-' + (($Area -replace '[^A-Za-z0-9]+', '-').Trim('-')) + '.json') $r
    foreach ($e in @($r.errors)) { if ($e) { Write-Host "    guest note: $e" -ForegroundColor Yellow } }
    $check = "0051: $StaffTargetsTable denies INSERT and UPDATE to etp_store_manager"
    if (-not $r.tableExists) { Add-Check $Area $check 'table present' "table missing $(@($r.errors) -join ' / ')" 'FAIL'; return }
    $perms = @($r.permissions)
    $sm = @($perms | Where-Object { [string]$_.role_name -eq 'etp_store_manager' })
    $isGrant = { param($row) @('GRANT', 'GRANT_WITH_GRANT_OPTION') -contains [string]$row.state_desc }
    $catalogueBlocks = {
        param([string]$Op)
        $denied = @($sm | Where-Object { [string]$_.class_desc -eq 'OBJECT_OR_COLUMN' -and ([string]$_.permission_name -eq $Op -or [string]$_.permission_name -eq 'CONTROL') -and [string]$_.state_desc -eq 'DENY' }).Count -gt 0
        $granted = @($sm | Where-Object { ([string]$_.permission_name -eq $Op -or [string]$_.permission_name -eq 'CONTROL') -and (& $isGrant $_) }).Count -gt 0
        $viaRole = @(@($r.storeManagerRoles) | Where-Object { @('db_datawriter', 'db_owner') -contains [string]$_ }).Count -gt 0
        return ($denied -or (-not $granted -and -not $viaRole))
    }
    $catOk = (& $catalogueBlocks 'INSERT') -and (& $catalogueBlocks 'UPDATE')
    $smText = 'no permission row'
    if ($sm.Count -gt 0) { $smText = (@($sm | ForEach-Object { "$($_.class_desc) $($_.permission_name) $($_.state_desc)" }) -join '; ') }
    $smText += "; member of: $(if (@($r.storeManagerRoles).Count) { @($r.storeManagerRoles) -join ', ' } else { 'no role' })"
    $as = $r.asUser
    $usable = $r.testUser -and $as -and -not $r.asUserError -and ([int]$as.in_store_manager -eq 1)
    if ($usable) {
        $ok = ([int]$as.can_insert -eq 0) -and ([int]$as.can_update -eq 0)
        Add-Check $Area "$check (HAS_PERMS_BY_NAME under EXECUTE AS USER $($r.testUser))" 'INSERT 0, UPDATE 0' "INSERT $($as.can_insert), UPDATE $($as.can_update), DELETE $($as.can_delete), SELECT $($as.can_select); acting as $($as.acting_as), member of etp_store_manager" (Get-Result $ok)
        Add-Check $Area '0051: the catalogue agrees (sys.database_permissions of etp_store_manager)' 'INSERT and UPDATE: DENY, or no GRANT at object, schema dbo or database level' $smText (Get-Result $catOk 'REVIEW')
    }
    else {
        $why = 'no store-manager test user in the harness'
        if ($identity -and -not $r.testUser) { $why = "$identity has no row in dbo.application_users or no database user" }
        elseif ($r.testUser -and $r.asUserError) { $why = "EXECUTE AS USER $($r.testUser) failed: $($r.asUserError)" }
        elseif ($r.testUser -and $as -and [int]$as.in_store_manager -ne 1) { $why = "$($r.testUser) is not a member of etp_store_manager (application_users role $($r.testRole), active=$($r.testActive))" }
        Add-Check $Area "$check (sys.database_permissions; $why)" 'INSERT and UPDATE: DENY, or no GRANT at object, schema dbo or database level' $smText (Get-Result $catOk)
    }
    $owner = @($perms | Where-Object { [string]$_.role_name -eq 'etp_owner' } | ForEach-Object { "$($_.class_desc) $($_.permission_name) $($_.state_desc)" })
    Add-Check $Area '0051: etp_owner permissions on staff targets' 'recorded (the Owner keeps INSERT/UPDATE)' $(if ($owner.Count) { $owner -join '; ' } else { 'no direct permission row (the Owner may write through a procedure or db_owner)' }) 'INFO'
}

# ================================================================ 1.10.0: -Stage ServiceUi (10 Oct 2026)
# The read-only SQL checks of docs\roadmap\SERVICE-UI-1.10.0-ACCEPTANCE.md (svcui-docs lane) against EtpReporting in the guest, after the
# Service imports, and the timings. SELECT only (plus sys.* catalogue reads). Only counts, dates, medians in days, names and PASS/FAIL leave
# the VM: no job number, customer name or amount is returned by these blocks.
$ServiceUiFactsBlock = {
    param([string]$ViewsCsv)
    $ErrorActionPreference = 'Continue'
    $expected = @($ViewsCsv -split ',' | Where-Object { $_ })
    $f = [ordered]@{ serviceViews = @(); missingViews = @(); jobShape = $null; stageCounts = @(); tat = @(); tat30 = @(); claimsBySource = @(); claimsUnion = @()
        claimsDupWithinType = $null; claimsWdcWra = $null; claimsJul1 = @(); gprc = $null; pending = @(); pendingNewColumns = @(); srnReceivedStatus = $null
        srnClosedRows = $null; privacy = @(); grants = @(); jobIndexObjects = @(); readings = $null; errors = @() }
    $db = 'EtpReporting'
    try {
        $v = Invoke-P6Sql -Database $db -Query "SELECT name FROM sys.views WHERE name LIKE N'v[_]service[_]%' ORDER BY name"
        $f.serviceViews = @($v | ForEach-Object { [string]$_.name })
        $f.missingViews = @($expected | Where-Object { $f.serviceViews -notcontains $_ })
    }
    catch { $f.errors += "views: $($_.Exception.Message)" }
    # v_service_job: one row per job, the Q1 key, dates, Q3 (closed jobs never open), stage counts.
    try {
        $f.jobShape = (Invoke-P6Sql -Database $db -Timeout 900 -Query @'
SET NOCOUNT ON;
SELECT COUNT_BIG(*) AS total, COUNT_BIG(DISTINCT job_order_number) AS distinct_jobs,
  SUM(CASE WHEN job_order_number IS NULL OR job_order_number NOT LIKE N'JOAW330%' OR LEN(job_order_number) NOT IN (15, 16)
            OR SUBSTRING(job_order_number, 8, 9) LIKE N'%[^0-9]%' THEN 1 ELSE 0 END) AS bad_keys,
  SUM(CASE WHEN booking_date IS NULL THEN 1 ELSE 0 END) AS no_booking_date,
  SUM(CASE WHEN stage_date IS NULL THEN 1 ELSE 0 END) AS no_stage_date,
  SUM(CASE WHEN tat_days < 0 OR tat_repair_days < 0 THEN 1 ELSE 0 END) AS negative_tat,
  SUM(CASE WHEN is_open = 1 THEN 1 ELSE 0 END) AS open_jobs,
  SUM(CASE WHEN is_open = 1 AND stage IN (N'DELIVERED', N'RWR') THEN 1 ELSE 0 END) AS open_but_closed_stage,
  SUM(CASE WHEN is_open = 1 AND stage IN (N'DC_ISSUED', N'RA_ISSUED') AND claim_raised = 1 THEN 1 ELSE 0 END) AS open_but_claimed,
  CONVERT(varchar(10), MAX(as_at), 23) AS as_at
FROM dbo.v_service_job;
'@)[0]
    }
    catch { $f.errors += "v_service_job shape: $($_.Exception.Message)" }
    try { $f.stageCounts = (Invoke-P6Sql -Database $db -Timeout 900 -Query "SELECT stage, CONVERT(int, is_open) AS is_open, COUNT_BIG(*) AS jobs FROM dbo.v_service_job GROUP BY stage, is_open ORDER BY stage, is_open") }
    catch { $f.errors += "stage counts: $($_.Exception.Message)" }
    # TAT medians (Q2): delivered jobs, Booking and Quick Billing apart; whole history and the Jobs screen's last 30 days.
    $tatSql = @'
SET NOCOUNT ON;
WITH d AS (
  SELECT CASE WHEN jo_type LIKE N'%quick%' THEN N'Quick Billing' ELSE N'Booking' END AS kind, CONVERT(float, tat_days) AS tat_days,
    CONVERT(float, tat_repair_days) AS tat_repair_days
  FROM dbo.v_service_job WHERE stage = N'DELIVERED' AND tat_days IS NOT NULL {WINDOW})
SELECT DISTINCT kind, COUNT(*) OVER (PARTITION BY kind) AS delivered,
  CONVERT(decimal(9, 2), PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY tat_days) OVER (PARTITION BY kind)) AS median_tat_days,
  CONVERT(decimal(9, 2), PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY tat_repair_days) OVER (PARTITION BY kind)) AS median_repair_days,
  SUM(CASE WHEN tat_days > 15 THEN 1 ELSE 0 END) OVER (PARTITION BY kind) AS over_15
FROM d ORDER BY kind;
'@
    try { $f.tat = (Invoke-P6Sql -Database $db -Timeout 900 -Query ($tatSql.Replace('{WINDOW}', ''))) } catch { $f.errors += "TAT: $($_.Exception.Message)" }
    try { $f.tat30 = (Invoke-P6Sql -Database $db -Timeout 900 -Query ($tatSql.Replace('{WINDOW}', 'AND stage_date >= DATEADD(day, -30, as_at)'))) } catch { $f.errors += "TAT 30 days: $($_.Exception.Message)" }
    # Claims (Q12 and the 10 Oct S026 rule): lines per type and family; the union by document equals the expected union of the families'
    # winning readings (GPRC = v_service_gprc_claims; MB = S024; WDC = S039 + S025; WRA = S040 + S026 without the WDC documents).
    try { $f.claimsBySource = (Invoke-P6Sql -Database $db -Query "SELECT claim_type, report_code, COUNT_BIG(*) AS lines, COUNT_BIG(DISTINCT document_number) AS documents FROM dbo.v_service_claims GROUP BY claim_type, report_code ORDER BY claim_type, report_code") }
    catch { $f.errors += "claims by source: $($_.Exception.Message)" }
    $unionSql = @'
SET NOCOUNT ON;
WITH s024 AS (SELECT DISTINCT NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100), r.documentnum))), N'') AS d FROM dbo.etp_landing_s024 AS r
    JOIN dbo.v_service_datelog_readings AS w ON w.report_code = N'S024' AND w.reading_rank = 1 AND w.import_file_id = r.import_file_id AND w.business_date = r.transdate),
s025 AS (SELECT DISTINCT NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100), r.documentnum))), N'') AS d FROM dbo.etp_landing_s025 AS r
    JOIN dbo.v_service_datelog_readings AS w ON w.report_code = N'S025' AND w.reading_rank = 1 AND w.import_file_id = r.import_file_id AND w.business_date = r.transdate),
s026 AS (SELECT DISTINCT NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100), r.documentnum))), N'') AS d FROM dbo.etp_landing_s026 AS r
    JOIN dbo.v_service_datelog_readings AS w ON w.report_code = N'S026' AND w.reading_rank = 1 AND w.import_file_id = r.import_file_id AND w.business_date = r.transdate),
s039 AS (SELECT DISTINCT NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100), r.document_number))), N'') AS d FROM dbo.etp_landing_s039 AS r
    JOIN dbo.v_service_datelog_readings AS w ON w.report_code = N'S039' AND w.reading_rank = 1 AND w.import_file_id = r.import_file_id AND w.business_date = r.transaction_date),
s040 AS (SELECT DISTINCT NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100), r.document_number))), N'') AS d FROM dbo.etp_landing_s040 AS r
    JOIN dbo.v_service_datelog_readings AS w ON w.report_code = N'S040' AND w.reading_rank = 1 AND w.import_file_id = r.import_file_id AND w.business_date = r.transaction_date),
gprc AS (SELECT DISTINCT CONVERT(nvarchar(100), document_number) AS d FROM dbo.v_service_gprc_claims),
wdc_docs AS (SELECT d FROM s025 WHERE d IS NOT NULL UNION SELECT d FROM s039 WHERE d IS NOT NULL),
expected AS (
  SELECT N'GPRC' AS claim_type, d FROM gprc WHERE d IS NOT NULL
  UNION SELECT N'MB', d FROM s024 WHERE d IS NOT NULL
  UNION SELECT N'WDC', d FROM wdc_docs
  UNION SELECT N'WRA', d FROM s040 WHERE d IS NOT NULL
  UNION SELECT N'WRA', d FROM s026 WHERE d IS NOT NULL AND d NOT IN (SELECT d FROM wdc_docs)),
actual AS (SELECT DISTINCT CONVERT(nvarchar(10), claim_type) AS claim_type, CONVERT(nvarchar(100), document_number) AS d FROM dbo.v_service_claims WHERE document_number IS NOT NULL)
SELECT t.claim_type,
  (SELECT COUNT(*) FROM expected AS e WHERE e.claim_type = t.claim_type) AS expected_documents,
  (SELECT COUNT(*) FROM actual AS a WHERE a.claim_type = t.claim_type) AS view_documents,
  (SELECT COUNT(*) FROM expected AS e WHERE e.claim_type = t.claim_type AND NOT EXISTS (SELECT 1 FROM actual AS a WHERE a.claim_type = e.claim_type AND a.d = e.d)) AS missing_in_view,
  (SELECT COUNT(*) FROM actual AS a WHERE a.claim_type = t.claim_type AND NOT EXISTS (SELECT 1 FROM expected AS e WHERE e.claim_type = a.claim_type AND e.d = a.d)) AS extra_in_view
FROM (VALUES (N'GPRC'), (N'MB'), (N'WDC'), (N'WRA')) AS t(claim_type);
'@
    try { $f.claimsUnion = (Invoke-P6Sql -Database $db -Timeout 900 -Query $unionSql) } catch { $f.errors += "claims union: $($_.Exception.Message)" }
    try { $f.claimsDupWithinType = [int64]((Invoke-P6Sql -Database $db -Query "SELECT COUNT_BIG(*) AS n FROM (SELECT claim_type, document_number FROM dbo.v_service_claims WHERE document_number IS NOT NULL GROUP BY claim_type, document_number HAVING COUNT(DISTINCT report_code) > 1) AS x")[0].n) }
    catch { $f.errors += "claims duplicate within type: $($_.Exception.Message)" }
    try { $f.claimsWdcWra = [int64]((Invoke-P6Sql -Database $db -Query "SELECT COUNT_BIG(*) AS n FROM (SELECT document_number FROM dbo.v_service_claims WHERE claim_type IN (N'WDC', N'WRA') AND document_number IS NOT NULL GROUP BY document_number HAVING COUNT(DISTINCT claim_type) > 1) AS x")[0].n) }
    catch { $f.errors += "claims WDC and WRA: $($_.Exception.Message)" }
    try { $f.claimsJul1 = (Invoke-P6Sql -Database $db -Query "SELECT claim_type, report_code, COUNT_BIG(*) AS lines FROM dbo.v_service_claims WHERE business_date = '2026-07-01' GROUP BY claim_type, report_code ORDER BY claim_type, report_code") }
    catch { $f.errors += "claims 1 Jul: $($_.Exception.Message)" }
    try { $f.gprc = (Invoke-P6Sql -Database $db -Query "SELECT (SELECT COUNT_BIG(*) FROM dbo.v_service_claims WHERE claim_type = N'GPRC') AS view_lines, (SELECT COUNT_BIG(*) FROM dbo.v_service_gprc_claims) AS source_lines")[0] }
    catch { $f.errors += "GPRC lines: $($_.Exception.Message)" }
    # Pending (0050 section 8, Q5): rows per family, the new columns, S011 open SRNs only.
    try { $f.pending = (Invoke-P6Sql -Database $db -Query "SELECT report_code, COUNT_BIG(*) AS row_count, COUNT_BIG(DISTINCT job_order_number) AS jobs FROM dbo.v_service_pending_current GROUP BY report_code ORDER BY report_code") }
    catch { $f.errors += "pending: $($_.Exception.Message)" }
    try {
        $c = Invoke-P6Sql -Database $db -Query "SELECT c.name FROM sys.columns AS c WHERE c.object_id = OBJECT_ID(N'dbo.v_service_pending_current') AND c.name IN (N'edd', N'jo_status', N'spare_required', N'indent_date', N'repair_date', N'srn_to_status') ORDER BY c.column_id"
        $f.pendingNewColumns = @($c | ForEach-Object { [string]$_.name })
    }
    catch { $f.errors += "pending columns: $($_.Exception.Message)" }
    try { $f.srnReceivedStatus = [int64]((Invoke-P6Sql -Database $db -Query "SELECT COUNT_BIG(*) AS n FROM dbo.v_service_pending_current WHERE report_code = N'S011' AND srn_to_status LIKE N'%Received%'")[0].n) }
    catch { $f.errors += "SRN status: $($_.Exception.Message)" }
    try {
        $f.srnClosedRows = [int64]((Invoke-P6Sql -Database $db -Query @'
SET NOCOUNT ON;
SELECT COUNT_BIG(*) AS n FROM dbo.v_service_pending_current AS p
WHERE p.report_code = N'S011' AND EXISTS (SELECT 1 FROM dbo.etp_landing_s011 AS s WHERE s.import_file_id = p.import_file_id
  AND NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100), s.joborder_number))), N'') = p.job_order_number
  AND (s.srn_received_date IS NOT NULL OR s.repaired_date IS NOT NULL OR CONVERT(nvarchar(80), s.to_status) LIKE N'%Received%'));
'@)[0].n)
    }
    catch { $f.errors += "SRN closed rows: $($_.Exception.Message)" }
    # Privacy (design 1.8): no contact column in ANY v_service_* view. Column names only.
    try { $f.privacy = (Invoke-P6Sql -Database $db -Query "SELECT v.name AS view_name, c.name AS column_name FROM sys.views AS v JOIN sys.columns AS c ON c.object_id = v.object_id WHERE v.name LIKE N'v[_]service[_]%' AND (c.name LIKE N'%mobile%' OR c.name LIKE N'%email%' OR c.name LIKE N'%e[_]mail%' OR c.name LIKE N'%phone%' OR c.name LIKE N'%landline%' OR c.name LIKE N'%contact%' OR c.name LIKE N'%address%') ORDER BY v.name, c.name") }
    catch { $f.errors += "privacy: $($_.Exception.Message)" }
    # Grants per role on every v_service_* view (object level).
    try { $f.grants = (Invoke-P6Sql -Database $db -Query "SELECT v.name AS view_name, r.name AS grantee, p.permission_name, p.state_desc FROM sys.database_permissions AS p JOIN sys.views AS v ON v.object_id = p.major_id AND p.class = 1 JOIN sys.database_principals AS r ON r.principal_id = p.grantee_principal_id WHERE v.name LIKE N'v[_]service[_]%' ORDER BY v.name, r.name, p.permission_name") }
    catch { $f.errors += "grants: $($_.Exception.Message)" }
    try { $f.jobIndexObjects = (Invoke-P6Sql -Database $db -Query "SELECT o.name, o.type_desc FROM sys.objects AS o WHERE o.is_ms_shipped = 0 AND (o.name LIKE N'%service[_]job[_]index%' OR o.name LIKE N'refresh[_]service[_]job%') ORDER BY o.name") }
    catch { $f.errors += "job index objects (0050): $($_.Exception.Message)" }
    try { $f.readings = (Invoke-P6Sql -Database $db -Query "SELECT CONVERT(varchar(10), MAX(snapshot_date), 23) AS latest_snapshot, COUNT_BIG(*) AS readings FROM dbo.v_service_readings")[0] }
    catch { $f.errors += "readings: $($_.Exception.Message)" }
    $f | ConvertTo-Json -Depth 6 -Compress
}

# Timings: one untimed warm-up and $Runs timed runs of SELECT COUNT(*) FROM dbo.v_service_job and of SELECT * from each v_service_* view
# (every row read and thrown away, nothing returned), on one connection. Seconds and row counts only.
$ServiceUiTimingBlock = {
    param([int]$Runs)
    $out = New-Object System.Collections.Generic.List[object]
    $connection = $null
    $lastError = $null
    foreach ($source in @('lpc:.\SQLEXPRESS', '.\SQLEXPRESS')) {
        $candidate = New-Object System.Data.SqlClient.SqlConnection ('Data Source=' + $source + ';Initial Catalog=EtpReporting;Integrated Security=SSPI;Connect Timeout=30;Application Name=ETP VM acceptance (Service UI timing)')
        try { $candidate.Open(); $connection = $candidate; break }
        catch { $lastError = $_.Exception; $candidate.Dispose() }
    }
    if (-not $connection) { return (ConvertTo-Json -InputObject @([ordered]@{ name = '(connection)'; kind = ''; rows = $null; seconds = @(); median = $null; error = [string]$lastError.Message }) -Compress) }
    try {
        $views = New-Object System.Collections.Generic.List[string]
        $cmd = $connection.CreateCommand()
        $cmd.CommandText = "SELECT name FROM sys.views WHERE name LIKE N'v[_]service[_]%' ORDER BY name"
        $rd = $cmd.ExecuteReader()
        while ($rd.Read()) { $views.Add($rd.GetString(0)) }
        $rd.Close()
        $queries = New-Object System.Collections.Generic.List[object]
        $queries.Add([pscustomobject]@{ name = 'v_service_job'; kind = 'COUNT(*)'; sql = 'SELECT COUNT(*) FROM dbo.v_service_job' })
        foreach ($v in $views) { $queries.Add([pscustomobject]@{ name = $v; kind = 'full read'; sql = 'SELECT * FROM dbo.[' + $v.Replace(']', ']]') + ']' }) }
        foreach ($q in $queries) {
            $row = [ordered]@{ name = $q.name; kind = $q.kind; rows = $null; seconds = @(); median = $null; error = '' }
            try {
                for ($i = 0; $i -le $Runs; $i++) {
                    $c = $connection.CreateCommand()
                    $c.CommandText = $q.sql
                    $c.CommandTimeout = 900
                    $sw = [Diagnostics.Stopwatch]::StartNew()
                    $reader = $c.ExecuteReader()
                    $n = 0
                    while ($reader.Read()) { $n++ }
                    $reader.Close()
                    $sw.Stop()
                    if ($i -gt 0) { $row.seconds += [math]::Round($sw.Elapsed.TotalSeconds, 3) }
                    $row.rows = $n
                }
                $sorted = @($row.seconds | Sort-Object)
                if ($sorted.Count -gt 0) { $row.median = $sorted[[int][math]::Floor(($sorted.Count - 1) / 2)] }
            }
            catch { $row.error = $_.Exception.Message }
            $out.Add([pscustomobject]$row)
        }
    }
    finally { $connection.Dispose() }
    ConvertTo-Json -InputObject @($out.ToArray()) -Depth 4 -Compress
}

function Test-ServiceUiSql([string]$Area) {
    Write-Step 'Service UI: the read-only SQL checks of SERVICE-UI-1.10.0-ACCEPTANCE.md (sections 1.4, 6, 7; decision 25)'
    $s = Invoke-GuestJson $ServiceUiFactsBlock @(($ServiceUiViews -join ','))
    Repair-RowSets $s @('serviceViews', 'missingViews', 'stageCounts', 'tat', 'tat30', 'claimsBySource', 'claimsUnion', 'claimsJul1', 'pending', 'pendingNewColumns', 'privacy', 'grants', 'jobIndexObjects') @('jobShape', 'gprc', 'readings')
    Save-Json 'service-ui\sql-checks.json' $s
    $errs = @(@($s.errors) | Where-Object { $_ })
    foreach ($e in $errs) { Write-Host "    guest note: $e" -ForegroundColor Yellow }
    Add-Check $Area 'Every Service UI query ran' 'no query error' $(if ($errs.Count) { $errs -join ' / ' } else { 'none' }) (Get-Result ($errs.Count -eq 0))
    $views = @($s.serviceViews | ForEach-Object { [string]$_ })
    $missing = @($s.missingViews | ForEach-Object { [string]$_ })
    Add-Check $Area '0050 views present (runbook 1.4)' "$($ServiceUiViews.Count): $($ServiceUiViews -join ', ')" $(if ($missing.Count) { 'missing: ' + ($missing -join ', ') } else { "all $($ServiceUiViews.Count); $($views.Count) v_service_* views in all" }) (Get-Result (($views.Count -gt 0) -and ($missing.Count -eq 0)))

    # v_service_job
    $j = $s.jobShape
    if ($j) {
        Add-Check $Area 'v_service_job: one row per job' 'rows = distinct job numbers' "$($j.total) rows, $($j.distinct_jobs) jobs" (Get-Result (([int64]$j.total -gt 0) -and ([int64]$j.total -eq [int64]$j.distinct_jobs)))
        Add-Check $Area 'Q1: every key is JOAW330 + 8 or 9 digits' '0 other keys' "$($j.bad_keys)" (Get-Result ([int64]$j.bad_keys -eq 0))
        Add-Check $Area 'v_service_job: booking date and stage date on every job' '0 without' "no booking date $($j.no_booking_date), no stage date $($j.no_stage_date)" (Get-Result (([int64]$j.no_booking_date -eq 0) -and ([int64]$j.no_stage_date -eq 0)) 'REVIEW')
        Add-Check $Area 'Q3: delivered/RWR and claimed DC/RA jobs are never open' '0' "open DELIVERED/RWR $($j.open_but_closed_stage), open claimed DC/RA $($j.open_but_claimed)" (Get-Result (([int64]$j.open_but_closed_stage -eq 0) -and ([int64]$j.open_but_claimed -eq 0)))
        Add-Check $Area 'No negative TAT' '0' "$($j.negative_tat)" (Get-Result ([int64]$j.negative_tat -eq 0))
    }
    else { Add-Check $Area 'v_service_job readable' 'a result' 'no result (see the query errors)' 'FAIL' }
    $stages = @($s.stageCounts)
    Save-Csv 'service-ui\job-stage-counts.csv' $stages
    $unknown = @($stages | Where-Object { $ServiceJobStages -notcontains [string]$_.stage } | ForEach-Object { "$($_.stage) ($($_.jobs))" })
    Add-Check $Area 'v_service_job: every stage is a known stage' ($ServiceJobStages -join ', ') $(if ($unknown.Count) { 'unknown: ' + ($unknown -join ', ') } else { 'all known' }) (Get-Result (($stages.Count -gt 0) -and ($unknown.Count -eq 0)))
    $perStage = @($ServiceJobStages | ForEach-Object {
            $st = $_
            $open = [int64](@($stages | Where-Object { [string]$_.stage -eq $st -and [int]$_.is_open -eq 1 } | Measure-Object -Property jobs -Sum).Sum)
            $closed = [int64](@($stages | Where-Object { [string]$_.stage -eq $st -and [int]$_.is_open -ne 1 } | Measure-Object -Property jobs -Sum).Sum)
            "$st $($open + $closed) (open $open)"
        })
    Add-Check $Area 'Row counts per stage of v_service_job (runbook 1.5; compare with the live counts in the runbook)' 'recorded' ("as at $($j.as_at): " + ($perStage -join '; ')) 'INFO'

    # TAT (Q2)
    $tat = @($s.tat)
    $tatRows = @(@($tat | Select-Object @{ n = 'window'; e = { 'all delivered' } }, kind, delivered, median_tat_days, median_repair_days, over_15) +
        @(@($s.tat30) | Select-Object @{ n = 'window'; e = { 'closed in the last 30 days' } }, kind, delivered, median_tat_days, median_repair_days, over_15))
    Save-Csv 'service-ui\tat-medians.csv' $tatRows
    $booking = @($tat | Where-Object { [string]$_.kind -eq 'Booking' }) | Select-Object -First 1
    $quick = @($tat | Where-Object { [string]$_.kind -eq 'Quick Billing' }) | Select-Object -First 1
    $tatText = (@($tat | ForEach-Object { "$($_.kind): median $($_.median_tat_days) d to delivery, $($_.median_repair_days) d to repair, $($_.delivered) delivered, $($_.over_15) over 15 d" }) -join '; ')
    Add-Check $Area 'Q2: TAT median present for Booking jobs (delivered)' 'a median >= 0 over > 0 delivered jobs (live 3 Oct: 496 Booking jobs, average 18.24 d)' $(if ($tatText) { $tatText } else { 'no delivered job with a TAT' }) (Get-Result ($booking -and ($null -ne $booking.median_tat_days) -and ([decimal]$booking.median_tat_days -ge 0) -and ([int64]$booking.delivered -gt 0)))
    Add-Check $Area 'Q2: TAT median present for Quick Billing jobs, kept apart' 'a median >= 0 (live figure in the runbook)' $(if ($quick) { "median $($quick.median_tat_days) d, $($quick.delivered) delivered" } else { 'no Quick Billing job delivered' }) (Get-Result ($quick -and ($null -ne $quick.median_tat_days) -and ([decimal]$quick.median_tat_days -ge 0)) 'REVIEW')
    Add-Check $Area 'J3: TAT line of the Jobs screen (closed in the last 30 days)' 'recorded; the screen shows the same medians rounded to whole days' $(if (@($s.tat30).Count) { (@($s.tat30 | ForEach-Object { "$($_.kind) median $($_.median_tat_days) d ($($_.delivered) delivered, $($_.over_15) over 15 d)" }) -join ' / ') } else { 'no delivered job in the last 30 days of the data' }) 'INFO'

    # Claims (Q12)
    $bySource = @($s.claimsBySource)
    Save-Csv 'service-ui\claims-by-source.csv' $bySource
    $wrongSource = @($bySource | Where-Object { -not $ServiceClaimSources.Contains([string]$_.claim_type) -or (@($ServiceClaimSources[[string]$_.claim_type]) -notcontains [string]$_.report_code) } | ForEach-Object { "$($_.claim_type)/$($_.report_code)" })
    Add-Check $Area 'Claims: each claim type reads only its own families' (@($ServiceClaimSources.Keys | ForEach-Object { "$_ = $(@($ServiceClaimSources[$_]) -join '+')" }) -join '; ') $(if ($wrongSource.Count) { 'other: ' + ($wrongSource -join ', ') } else { (@($bySource | ForEach-Object { "$($_.claim_type)/$($_.report_code) $($_.lines) lines $($_.documents) docs" }) -join '; ') }) (Get-Result (($bySource.Count -gt 0) -and ($wrongSource.Count -eq 0)))
    $union = @($s.claimsUnion)
    Save-Csv 'service-ui\claims-union.csv' $union
    $unionBad = @($union | Where-Object { [int64]$_.missing_in_view -ne 0 -or [int64]$_.extra_in_view -ne 0 } | ForEach-Object { "$($_.claim_type): $($_.missing_in_view) missing, $($_.extra_in_view) extra" })
    Add-Check $Area 'Claims union = expected, by document (GPRC = S023+S041 via v_service_gprc_claims; MB = S024; WDC = S039+S025; WRA = S040+S026 less WDC documents)' 'per type: documents in the view = documents of the families'' winning readings' $(if ($unionBad.Count) { $unionBad -join '; ' } else { (@($union | ForEach-Object { "$($_.claim_type) $($_.view_documents)/$($_.expected_documents)" }) -join '; ') }) (Get-Result (($union.Count -eq 4) -and ($unionBad.Count -eq 0)))
    Add-Check $Area 'CL2/Q12: no document counted from two families of one claim type' '0' "$($s.claimsDupWithinType)" (Get-Result (($null -ne $s.claimsDupWithinType) -and ([int64]$s.claimsDupWithinType -eq 0)))
    Add-Check $Area 'Claims: no document counted both as WDC and as WRA (S026 rule, 10 Oct)' '0' "$($s.claimsWdcWra)" (Get-Result (($null -ne $s.claimsWdcWra) -and ([int64]$s.claimsWdcWra -eq 0)))
    $g = $s.gprc
    Add-Check $Area 'Claims: GPRC lines = v_service_gprc_claims (S041 wins per document)' 'equal' $(if ($g) { "view $($g.view_lines), source $($g.source_lines)" } else { 'not read' }) (Get-Result ($g -and ([int64]$g.view_lines -eq [int64]$g.source_lines)))
    Add-Check $Area 'Claims: the 1 Jul 2026 join (S025/S039, S026/S040), lines per family' 'recorded (counted once)' $(if (@($s.claimsJul1).Count) { (@($s.claimsJul1 | ForEach-Object { "$($_.claim_type)/$($_.report_code) $($_.lines)" }) -join '; ') } else { 'no claim line on 1 Jul 2026' }) 'INFO'

    # Pending (Q5)
    $pending = @($s.pending)
    $cols = @($s.pendingNewColumns | ForEach-Object { [string]$_ })
    Add-Check $Area 'v_service_pending_current amended by 0050 (SD-02, SD-12)' 'edd, jo_status, spare_required, indent_date, repair_date, srn_to_status' $(if ($cols.Count) { $cols -join ', ' } else { 'none of them' }) (Get-Result ($cols.Count -eq 6))
    $srn = @($pending | Where-Object { [string]$_.report_code -eq 'S011' }) | Select-Object -First 1
    $srnRows = 0; if ($srn) { $srnRows = [int64]$srn.row_count }
    Add-Check $Area 'Q5: open SRNs only in v_service_pending_current (S011)' "$ServiceSrnOpenExpected on the 29 Sep + 3 Oct Service data (count from the expected-figures file)" "$srnRows rows; per family: $(@($pending | ForEach-Object { "$($_.report_code) $($_.row_count)" }) -join ', ')" (Get-Result (($null -ne $ServiceSrnOpenExpected) -and ($srnRows -eq $ServiceSrnOpenExpected)) 'REVIEW')
    Add-Check $Area 'Q5: no SRN with a Received status on the pending list' '0' "$($s.srnReceivedStatus)" (Get-Result (($null -ne $s.srnReceivedStatus) -and ([int64]$s.srnReceivedStatus -eq 0)))
    Add-Check $Area 'Q5: no listed SRN job whose winning S011 reading also holds a closed SRN line (received date, repaired date or Received)' '0' "$($s.srnClosedRows)" (Get-Result (($null -ne $s.srnClosedRows) -and ([int64]$s.srnClosedRows -eq 0)) 'REVIEW')

    # Privacy
    $priv = @($s.privacy | ForEach-Object { "$($_.view_name).$($_.column_name)" })
    Add-Check $Area 'Privacy: no phone, mobile, e-mail, landline, contact or address column in any v_service_* view (sys.columns)' '0 columns' $(if ($priv.Count) { $priv -join ', ' } else { "none in $($views.Count) views" }) (Get-Result (($views.Count -gt 0) -and ($priv.Count -eq 0)))

    # Grants per role
    $grants = @($s.grants)
    Save-Csv 'service-ui\view-grants.csv' $grants
    $isGrant = { param($row) @('GRANT', 'GRANT_WITH_GRANT_OPTION') -contains [string]$row.state_desc }
    $rows72 = @($grants | Where-Object {
            ($ServiceUiViews -contains [string]$_.view_name) -and (
                ([string]$_.permission_name -eq 'SELECT' -and [string]$_.state_desc -eq 'GRANT' -and @('etp_viewer', 'etp_store_manager', 'etp_owner') -contains [string]$_.grantee) -or
                (@('INSERT', 'UPDATE', 'DELETE') -contains [string]$_.permission_name -and [string]$_.state_desc -eq 'DENY' -and @('etp_viewer', 'etp_store_manager') -contains [string]$_.grantee)) }).Count
    Add-Check $Area 'Grants on the 0050 views (runbook 1.4)' "$ServiceUiGrantRows rows: 8 views x (SELECT to etp_viewer, etp_store_manager, etp_owner + DENY INSERT/UPDATE/DELETE to etp_viewer, etp_store_manager)" "$rows72" (Get-Result ($rows72 -eq $ServiceUiGrantRows))
    $noSelect = @()
    foreach ($v in $views) {
        foreach ($role in @('etp_viewer', 'etp_store_manager', 'etp_owner')) {
            if (@($grants | Where-Object { [string]$_.view_name -eq $v -and [string]$_.grantee -eq $role -and [string]$_.permission_name -eq 'SELECT' -and (& $isGrant $_) }).Count -eq 0) { $noSelect += "$v/$role" }
        }
    }
    Add-Check $Area 'Q13: every v_service_* view grants SELECT to etp_viewer, etp_store_manager and etp_owner' 'all' $(if ($noSelect.Count) { 'missing: ' + ($noSelect -join ', ') } else { "all $($views.Count) views x 3 roles" }) (Get-Result (($views.Count -gt 0) -and ($noSelect.Count -eq 0)))
    $writes = @($grants | Where-Object { @('INSERT', 'UPDATE', 'DELETE', 'ALTER', 'CONTROL', 'TAKE OWNERSHIP') -contains [string]$_.permission_name -and (& $isGrant $_) } | ForEach-Object { "$($_.view_name): $($_.permission_name) to $($_.grantee)" })
    Add-Check $Area 'No write permission granted on any v_service_* view' 'none' $(if ($writes.Count) { $writes -join '; ' } else { 'none' }) (Get-Result ($writes.Count -eq 0))
    $perRole = @($grants | Group-Object grantee | Sort-Object Name | ForEach-Object { "$($_.Name): " + (@($_.Group | Group-Object { "$($_.state_desc) $($_.permission_name)" } | Sort-Object Name | ForEach-Object { "$($_.Name) x$($_.Count)" }) -join ', ') })
    Add-Check $Area 'Grants per role on v_service_* views (sys.database_permissions)' 'recorded' ($perRole -join ' | ') 'INFO'
    $idx = @($s.jobIndexObjects | ForEach-Object { "$($_.name) ($($_.type_desc))" })
    Add-Check $Area '0050: job view performance objects (service_job_index / refresh_service_job*)' 'recorded (the performance work is folded into 0050; it may instead only rewrite v_service_job)' $(if ($idx.Count) { $idx -join ', ' } else { 'none by those names' }) 'INFO'
    if ($s.readings) { Add-Check $Area 'Service readings (freshness strip source)' 'recorded' "latest snapshot $($s.readings.latest_snapshot), $($s.readings.readings) readings" 'INFO' }
}

# PASS < 1 s, REVIEW from 1 s up to and including 3 s, FAIL > 3 s (the rule is unchanged from 1.11.4; only its input was wrong there).
function Get-TimingLevel([double]$Seconds) {
    if ($Seconds -gt $TimingFailSeconds) { return 'FAIL' }
    if ($Seconds -ge $TimingPassSeconds) { return 'REVIEW' }
    return 'PASS'
}

function Test-ServiceUiTimings([string]$Area) {
    Write-Step "Service UI: timing COUNT(*) of v_service_job and one full read of each v_service_* view ($TimingRuns timed runs after a warm-up; median: PASS < $TimingPassSeconds s, REVIEW to $TimingFailSeconds s, FAIL above)"
    $t = Expand-Rows (Invoke-GuestJson $ServiceUiTimingBlock @($TimingRuns))
    $csv = @($t | ForEach-Object { [pscustomobject]@{ name = $_.name; kind = $_.kind; rows = $_.rows; median_seconds = $_.median; runs_seconds = (@($_.seconds) -join ' / '); error = $_.error } })
    Save-Csv 'service-ui\timings.csv' $csv
    if ($csv.Count -eq 0) { Add-Check $Area 'Service view timings' 'one line per view' 'nothing measured' 'FAIL'; return }
    $slow = @()
    foreach ($x in $csv) {
        $label = "Timing: $($x.kind) $($x.name)"
        if ($x.error) { Add-Check $Area $label "median < $TimingPassSeconds s" "error: $($x.error)" 'FAIL'; continue }
        # 1.12.0 fix: the $csv rows carry the median as median_seconds. The 1.11.4 script read $x.median, which does not exist on them, so [double]$null = 0
        # and every view printed 0.00 s and passed (12 Oct run: runs 1.061 / 1.189 / 1.011 s judged PASS, and "views over 1 s" said none).
        if ($null -eq $x.median_seconds -or [string]$x.median_seconds -eq '') { Add-Check $Area $label "median < $TimingPassSeconds s" 'no median recorded (no timed run)' 'FAIL'; continue }
        $m = [double]$x.median_seconds
        $level = Get-TimingLevel $m
        $mText = $m.ToString('0.000', [Globalization.CultureInfo]::InvariantCulture)
        if ($level -ne 'PASS') { $slow += "$($x.name) $mText s" }
        Add-Check $Area $label "median < $TimingPassSeconds s (REVIEW to $TimingFailSeconds s)" ("median $mText s (runs $($x.runs_seconds)); $($x.rows) rows") $level
    }
    $over1 = @($csv | Where-Object { -not $_.error -and ($null -ne $_.median_seconds) -and ([string]$_.median_seconds -ne '') -and ([double]$_.median_seconds -ge $TimingPassSeconds) } | ForEach-Object { "$($_.name) $(([double]$_.median_seconds).ToString('0.000', [Globalization.CultureInfo]::InvariantCulture)) s" })
    Add-Check $Area 'Service views over 1 s (summary)' 'none (PASS < 1 s, REVIEW 1-3 s, FAIL > 3 s)' $(if ($over1.Count) { "$($over1.Count): $($over1 -join '; ')" } else { 'none' }) 'INFO'
    if ($slow.Count -gt 0) {
        $script:Notes.Add("Service UI timing above $TimingPassSeconds s: $($slow -join '; '). This is the SD-11 decision (0050 / service_job_index, or Sagar accepts the wait), not a defect in the numbers; record the Service landing row count with it (release-facts-serviceui.json).")
    }
}

function Invoke-ServiceUiStage {
    Open-GuestForStage
    $area = 'Service UI'
    $etp = [int](Invoke-Guest $NoEtpProcessBlock)
    if ($etp -gt 0) { $script:Notes.Add("ServiceUi: ETP was running in the VM ($etp process) during the checks; if a timing is near a threshold, close ETP and run -Stage ServiceUi again.") }
    $f0 = Get-Facts 'serviceui'
    $ver = [string]$f0.exeProductVersion
    $j = @(Get-MigrationIds $f0.journal | Sort-Object -Unique)
    $latest = 'none'
    if ($j.Count -gt 0) { $latest = $j[-1] }
    Add-Check $area "The VM is on $NewVersion" "$NewVersion*, $ExpectedMigrationCount migrations, latest $ExpectedLatestMigration" "$ver; $($j.Count) migrations, latest $latest" (Get-Result (($ver -like "$NewVersion*") -and ($j.Count -eq $ExpectedMigrationCount) -and ($latest -like $ExpectedLatestMigration)))
    if ($ver -notlike "$NewVersion*") { throw "The VM is not on $NewVersion (it reports '$ver'). Run -Stage Delta (or -Stage Install) first." }
    $rf = Get-ReleaseFacts 'serviceui'
    Add-Check $area 'Service data imported (the checks need it)' 'landing rows > 0' "$($rf.serviceLandingRows) rows, $($rf.dataUsedMb) MB used" (Get-Result ([int64]$rf.serviceLandingRows -gt 0))
    if ([int64]$rf.serviceLandingRows -le 0) { throw 'No Service rows in the VM database. Run -Stage Service (or Delta from a VM that has them) first.' }
    $script:RunInfo['Service landing rows'] = "$($rf.serviceLandingRows) (timings are at this size, not at three more weekly readings)"
    Test-ServiceUiSql $area
    Test-ServiceUiTimings $area
    Test-StaffTargetsOwnerOnly $area
    Test-Release1111Checks $area
    $script:UiSteps.Add("Service UI screens in the VM console (vmconnect.exe localhost $VmName, ETPTest, ETP started normally): SERVICE-UI-1.10.0-ACCEPTANCE.md sections 4-7 (C1-C9, T1-T11, P1-P10, J1-J9, CL1-CL7, PA1-PA6, M1-M5). The SQL side of P1/J2/J3/CL1-CL3/Q5 is in service-ui\ here; a screen number that differs from it is a 1.10.0 defect.")
    $script:UiSteps.Add('Stopwatch timing per tab (runbook section 3), three times, middle value; write "timing at N landing rows, not 3 weeks" unless three more weekly readings were imported.')
    $script:UiSteps.Add('C7 (Q13): sign in to Windows as EtpTestViewer, start ETP: all six Service tabs open. As EtpTestManager: staff targets cannot be saved (0051); sign out of the test accounts afterwards.')
}
# ================================================================ 1.11.2 additions (11 Oct 2026)
# Light read-only checks of what migrations 0052-0061 and setup leave behind. SELECT / sys.* / Test-Path only.
$Release1111FactsBlock = {
    param([string]$ExportsFolder, [string]$RetiredCsv, [string]$ViewerIdentity)
    $f = [ordered]@{ customerNameColumn = $false; viewerUser = ''; viewerRole = ''; viewerRows = $null; viewerNamed = $null; viewerError = ''; healthColumns = @(); diagSchema = $false; diagTables = @(); diagProcedures = @(); moneyShopColumn = $false; moneyShops = @(); retiredInputs = @()
        healthExportsExists = $false; healthExportFiles = 0; appDir = ''; onedriveScript = $false; thirdParty = $false; landingTable = ''; landingError = 0; landingMessage = ''; landingRead = $false; invoiceRows = $null; invoiceNamed = $null; invoiceError = ''; errors = @() }
    $db = 'EtpReporting'
    # 1.11.1 (0056, 0057, 0058), read-only: COL_LENGTH, one metadata describe (the procedure is not run) and a SELECT under EXECUTE AS USER.
    try { $f.customerNameColumn = [bool]((Invoke-P6Sql -Database $db -Query "SELECT CASE WHEN COL_LENGTH(N'dbo.v_service_job', N'customer_name') IS NULL THEN 0 ELSE 1 END AS e")[0].e) }
    catch { $f.errors += "v_service_job.customer_name: $($_.Exception.Message)" }
    try { $f.healthColumns = (Invoke-P6Sql -Database $db -Query "SELECT name FROM sys.dm_exec_describe_first_result_set_for_object(OBJECT_ID(N'dbo.load_database_operational_health'), NULL) WHERE name IS NOT NULL ORDER BY column_ordinal") }
    catch { $f.errors += "load_database_operational_health columns: $($_.Exception.Message)" }
    if ($f.customerNameColumn -and $ViewerIdentity) {
        try {
            $u = Invoke-P6Sql -Database $db -Parameters @{ id = $ViewerIdentity } -Query "SELECT dp.name AS user_name, u.role_code FROM dbo.application_users AS u JOIN sys.database_principals AS dp ON dp.sid = SUSER_SID(u.windows_identity) WHERE u.windows_identity = @id"
            if ($u.Count -gt 0) { $f.viewerUser = [string]$u[0].user_name; $f.viewerRole = [string]$u[0].role_code }
        }
        catch { $f.errors += "viewer user: $($_.Exception.Message)" }
        if ($f.viewerUser) {
            $asSql = @'
DECLARE @sql nvarchar(max) = N'EXECUTE AS USER = N''' + REPLACE(@u, N'''', N'''''') + N''';
SELECT COUNT(*) AS total_rows, COUNT(customer_name) AS named_rows FROM (SELECT TOP (500) customer_name FROM dbo.v_service_job) AS j;
REVERT;';
EXEC sys.sp_executesql @sql;
'@
            try {
                $rows = Invoke-P6Sql -Database $db -Parameters @{ u = $f.viewerUser } -Query $asSql
                if ($rows.Count -gt 0) { $f.viewerRows = [int]$rows[0].total_rows; $f.viewerNamed = [int]$rows[0].named_rows } else { $f.viewerError = 'no row returned' }
            }
            catch { $f.viewerError = $_.Exception.Message }
        }
    }
    if (-not $f.viewerUser -and $ViewerIdentity) {
        try {
            $u2 = Invoke-P6Sql -Database $db -Parameters @{ id = $ViewerIdentity } -Query "SELECT dp.name AS user_name, u.role_code FROM dbo.application_users AS u JOIN sys.database_principals AS dp ON dp.sid = SUSER_SID(u.windows_identity) WHERE u.windows_identity = @id"
            if ($u2.Count -gt 0) { $f.viewerUser = [string]$u2[0].user_name; $f.viewerRole = [string]$u2[0].role_code }
        }
        catch { $f.errors += "viewer user (1.11.2): $($_.Exception.Message)" }
    }
    if ($f.viewerUser) {
        # 1.11.2 (0059), read-only, one batch each, REVERT in the same batch: the Viewer reading a landing table is DENIED (error 229);
        # dbo.v_invoice_customer_names gives the Viewer NULL customer names. Nothing is written.
        $f.landingTable = 'dbo.etp_landing_r023'
        $landSql = @'
DECLARE @sql nvarchar(max) = N'EXECUTE AS USER = N''' + REPLACE(@u, N'''', N'''''') + N''';
DECLARE @n int = 0, @m nvarchar(2000) = N'''';
BEGIN TRY SELECT TOP (1) * FROM dbo.etp_landing_r023; SET @n = 0; END TRY
BEGIN CATCH SET @n = ERROR_NUMBER(); SET @m = ERROR_MESSAGE(); END CATCH;
REVERT;
SELECT @n AS err_no, @m AS err_msg;';
EXEC sys.sp_executesql @sql;
'@
        try {
            $lr = Invoke-P6Sql -Database $db -Parameters @{ u = $f.viewerUser } -Query $landSql
            if ($lr.Count -gt 0) { $f.landingError = [int]$lr[0].err_no; $f.landingMessage = [string]$lr[0].err_msg; $f.landingRead = ($f.landingError -eq 0) } else { $f.landingMessage = 'no row returned' }
        }
        catch { $f.landingMessage = $_.Exception.Message }
        $invSql = @'
DECLARE @sql nvarchar(max) = N'EXECUTE AS USER = N''' + REPLACE(@u, N'''', N'''''') + N''';
SELECT COUNT(*) AS total_rows, COUNT(customer_name) AS named_rows FROM (SELECT TOP (500) customer_name FROM dbo.v_invoice_customer_names) AS j;
REVERT;';
EXEC sys.sp_executesql @sql;
'@
        try {
            $ir = Invoke-P6Sql -Database $db -Parameters @{ u = $f.viewerUser } -Query $invSql
            if ($ir.Count -gt 0) { $f.invoiceRows = [int]$ir[0].total_rows; $f.invoiceNamed = [int]$ir[0].named_rows } else { $f.invoiceError = 'no row returned' }
        }
        catch { $f.invoiceError = $_.Exception.Message }
    }
    # 1.11.2 (setup): the OneDrive receiver script and the third-party notices are in the installed app folder.
    try {
        $f.appDir = [string](Get-P6AppDir)
        $f.onedriveScript = [bool](Test-Path -LiteralPath (Join-Path $f.appDir 'scripts\onedrive\Receive-ShopDailyFiles.ps1') -PathType Leaf)
        $f.thirdParty = [bool](Test-Path -LiteralPath (Join-Path $f.appDir 'THIRD-PARTY-NOTICES.txt') -PathType Leaf)
    }
    catch { $f.errors += "app folder files: $($_.Exception.Message)" }
    try { $f.diagSchema = [bool]((Invoke-P6Sql -Database $db -Query "SELECT CASE WHEN SCHEMA_ID(N'diag') IS NULL THEN 0 ELSE 1 END AS e")[0].e) }
    catch { $f.errors += "diag schema: $($_.Exception.Message)" }
    if ($f.diagSchema) {
        try { $f.diagTables = (Invoke-P6Sql -Database $db -Query "SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID(N'diag') ORDER BY name") }
        catch { $f.errors += "diag tables: $($_.Exception.Message)" }
        try { $f.diagProcedures = (Invoke-P6Sql -Database $db -Query "SELECT name FROM sys.procedures WHERE schema_id = SCHEMA_ID(N'diag') ORDER BY name") }
        catch { $f.errors += "diag procedures: $($_.Exception.Message)" }
    }
    try { $f.moneyShopColumn = [bool]((Invoke-P6Sql -Database $db -Query "SELECT CASE WHEN COL_LENGTH(N'dbo.stores', N'is_service_money_shop') IS NULL THEN 0 ELSE 1 END AS e")[0].e) }
    catch { $f.errors += "money shop column: $($_.Exception.Message)" }
    if ($f.moneyShopColumn) {
        try { $f.moneyShops = (Invoke-P6Sql -Database $db -Query "SELECT store_code, CONVERT(int, is_service_money_shop) AS flag FROM dbo.stores ORDER BY store_code") }
        catch { $f.errors += "money shop stores: $($_.Exception.Message)" }
    }
    $list = (@($RetiredCsv -split ',' | Where-Object { $_ } | ForEach-Object { "N'" + ($_ -replace "'", "''") + "'" })) -join ', '
    try { $f.retiredInputs = (Invoke-P6Sql -Database $db -Query "SELECT field_code, applies_to, CONVERT(int, is_active) AS is_active FROM dbo.manual_input_definitions WHERE applies_to = N'Stock' AND field_code IN ($list) ORDER BY field_code") }
    catch { $f.errors += "retired manual inputs: $($_.Exception.Message)" }
    $f.healthExportsExists = [bool](Test-Path -LiteralPath $ExportsFolder -PathType Container)
    if ($f.healthExportsExists) { $f.healthExportFiles = @(Get-ChildItem -LiteralPath $ExportsFolder -Filter 'health-*.json' -File -ErrorAction SilentlyContinue).Count }
    $f | ConvertTo-Json -Depth 5 -Compress
}

function Test-Release1111Checks([string]$Area) {
    $viewer = @($TestAccounts | Where-Object { $_.Role -eq 'VIEWER' }) | Select-Object -First 1
    $viewerIdentity = ''
    if ($viewer) { $viewerIdentity = [string]$viewer.Identity }
    $d = Invoke-GuestJson $Release1111FactsBlock @($HealthExportsFolder, ($RetiredManualInputs -join ','), $viewerIdentity)
    Repair-RowSets $d @('diagTables', 'diagProcedures', 'moneyShops', 'retiredInputs', 'healthColumns')
    $slug = (($Area -replace '[^A-Za-z0-9]+', '-').Trim('-')).ToLowerInvariant()
    Save-Json "release-facts-1111-$slug.json" $d
    $errs = @(@($d.errors) | Where-Object { $_ })
    foreach ($e in $errs) { Write-Host "    guest note: $e" -ForegroundColor Yellow }
    Add-Check $Area '1.11.2 read-only checks ran' 'no query error' $(if ($errs.Count) { $errs -join ' / ' } else { 'none' }) (Get-Result ($errs.Count -eq 0))
    # 0054 + 0055: schema diag, its tables and procedures.
    Add-Check $Area 'Schema diag exists (0054)' 'present' "$($d.diagSchema)" (Get-Result ([bool]$d.diagSchema))
    $tables = @($d.diagTables | ForEach-Object { [string]$_.name })
    $missingT = @($DiagTables | Where-Object { $tables -notcontains $_ })
    $extraT = @($tables | Where-Object { $DiagTables -notcontains $_ })
    Add-Check $Area "diag tables (0054, 0055): $($DiagTables.Count)" ($DiagTables -join ', ') $(if ($missingT.Count) { 'missing: ' + ($missingT -join ', ') } else { "all present ($($tables.Count) in the schema" + $(if ($extraT.Count) { '; also ' + ($extraT -join ', ') } else { '' }) + ')' }) (Get-Result (($tables.Count -gt 0) -and ($missingT.Count -eq 0)))
    $procs = @($d.diagProcedures | ForEach-Object { [string]$_.name })
    $missingP = @($DiagProcedures | Where-Object { $procs -notcontains $_ })
    $extraP = @($procs | Where-Object { $DiagProcedures -notcontains $_ })
    Add-Check $Area "diag procedures (0054, 0055): $($DiagProcedures.Count)" ($DiagProcedures -join ', ') $(if ($missingP.Count) { 'missing: ' + ($missingP -join ', ') } else { "all present ($($procs.Count) in the schema" + $(if ($extraP.Count) { '; also ' + ($extraP -join ', ') } else { '' }) + ')' }) (Get-Result (($procs.Count -gt 0) -and ($missingP.Count -eq 0)))
    # 0052: dbo.stores.is_service_money_shop, set for WLMHW only.
    $flagged = @($d.moneyShops | Where-Object { [int]$_.flag -eq 1 } | ForEach-Object { [string]$_.store_code })
    $moneyText = "column=$($d.moneyShopColumn); flagged: $(if ($flagged.Count) { $flagged -join ', ' } else { 'none' }); stores $(@($d.moneyShops).Count)"
    Add-Check $Area 'dbo.stores.is_service_money_shop set for WLMHW only (0052)' "column present; only $MoneyShopStore flagged" $moneyText (Get-Result ([bool]$d.moneyShopColumn -and ($flagged.Count -eq 1) -and ($flagged[0] -eq $MoneyShopStore)))
    # 0053: the five retired manual inputs are inactive (rows and old values stay).
    $ri = @($d.retiredInputs)
    $foundCodes = @($ri | ForEach-Object { [string]$_.field_code })
    $notFound = @($RetiredManualInputs | Where-Object { $foundCodes -notcontains $_ })
    $stillActive = @($ri | Where-Object { [int]$_.is_active -ne 0 } | ForEach-Object { [string]$_.field_code })
    $riText = "$(@($ri | Where-Object { [int]$_.is_active -eq 0 }).Count) of $($RetiredManualInputs.Count) inactive"
    if ($notFound.Count) { $riText += '; no definition row: ' + ($notFound -join ', ') }
    if ($stillActive.Count) { $riText += '; still active: ' + ($stillActive -join ', ') }
    Add-Check $Area 'Retired manual inputs inactive (0053): DISPLAY_STOCK, BACKSTOCK, DEFECTIVE_STOCK, Y_LOCATION_STOCK, PHYSICAL_STOCK' 'all five is_active = 0 (rows kept)' $riText (Get-Result (($stillActive.Count -eq 0) -and ($notFound.Count -eq 0)))
    # 0056: dbo.v_service_job carries customer_name (shown by role).
    Add-Check $Area 'dbo.v_service_job has a customer_name column (0056)' 'column present' "present=$($d.customerNameColumn)" (Get-Result ([bool]$d.customerNameColumn))
    # 0056/0057: as the Viewer role the name reads NULL. Read-only: EXECUTE AS USER of the VM's viewer test user, REVERT in the same batch.
    $vCheck = '0056: customer_name is NULL for the Viewer role (SELECT TOP 500 from dbo.v_service_job under EXECUTE AS USER)'
    if ($d.viewerUser -and ($null -ne $d.viewerNamed) -and -not $d.viewerError) {
        Add-Check $Area "$vCheck - $($d.viewerUser)" 'non-NULL names = 0' "non-NULL names $($d.viewerNamed) of $($d.viewerRows) rows read" (Get-Result ([int]$d.viewerNamed -eq 0))
    }
    else {
        $why = 'no viewer test user in the harness, or the column is missing'
        if ($viewerIdentity -and $d.customerNameColumn -and -not $d.viewerUser) { $why = "$viewerIdentity has no row in dbo.application_users or no database user" }
        elseif ($d.viewerError) { $why = "EXECUTE AS USER $($d.viewerUser) failed: $($d.viewerError)" }
        Add-Check $Area $vCheck 'skipped: needs the EtpTestViewer user (added by -UserChanges Script / Settings > Users)' $why 'INFO'
    }
    # 0058: load_database_operational_health gains the trailing open_imports column (described from metadata, the procedure is not run).
    $hc = @($d.healthColumns | ForEach-Object { [string]$_.name })
    Add-Check $Area 'dbo.load_database_operational_health returns an open_imports column (0058)' 'open_imports present (trailing column)' $(if ($hc.Count) { "columns: $($hc -join ', ')" } else { 'no column described' }) (Get-Result ($hc -contains 'open_imports'))
    # 1.11.2 (0059): as the Viewer, SELECT on a landing table is DENIED (229); v_invoice_customer_names shows no customer name.
    $lc = '0059: Viewer SELECT on dbo.etp_landing_r023 is denied (error 229, EXECUTE AS USER, REVERT in the same batch)'
    if ($d.viewerUser -and $d.landingTable) {
        $landMsg = ''
        if ($d.landingMessage) { $landMsg = ': ' + $d.landingMessage }
        Add-Check $Area "$lc - $($d.viewerUser)" 'error 229 (permission denied)' "error $($d.landingError)$landMsg" (Get-Result ([int]$d.landingError -eq 229))
    }
    else { Add-Check $Area $lc 'skipped: needs the EtpTestViewer user (added by -UserChanges Script / Settings > Users)' 'no viewer database user' 'INFO' }
    $ic = '0059: customer_name is NULL for the Viewer in dbo.v_invoice_customer_names (SELECT TOP 500 under EXECUTE AS USER)'
    if ($d.viewerUser -and ($null -ne $d.invoiceNamed) -and -not $d.invoiceError) {
        $invText = "non-NULL names $($d.invoiceNamed) of $($d.invoiceRows) rows read"
        if ([int]$d.invoiceRows -eq 0) { $invText += ' (no R024 rows imported: nothing proven)' }
        Add-Check $Area "$ic - $($d.viewerUser)" 'non-NULL names = 0' $invText (Get-Result ([int]$d.invoiceNamed -eq 0))
    }
    else {
        $why2 = 'no viewer database user'
        if ($d.invoiceError) { $why2 = "EXECUTE AS USER $($d.viewerUser) failed: $($d.invoiceError)" }
        Add-Check $Area $ic 'skipped: needs the EtpTestViewer user' $why2 'INFO'
    }
    # 1.11.2 (installer): files in the installed app folder.
    Add-Check $Area 'scripts\onedrive\Receive-ShopDailyFiles.ps1 is in the app folder (1.11.2)' 'present' "present=$($d.onedriveScript) (app folder $($d.appDir))" (Get-Result ([bool]$d.onedriveScript))
    Add-Check $Area 'THIRD-PARTY-NOTICES.txt is in the app folder (1.11.2)' 'present' "present=$($d.thirdParty) (app folder $($d.appDir))" (Get-Result ([bool]$d.thirdParty))
    # The health check id count (54) is not checkable in SQL: skipped on purpose.
    # Setup creates the folder the health exports go to.
    Add-Check $Area 'HealthExports folder exists after setup' $HealthExportsFolder "exists=$($d.healthExportsExists); health-*.json files: $($d.healthExportFiles)" (Get-Result ([bool]$d.healthExportsExists))
    Test-Release112Checks $Area
}

# ================================================================ 1.12.0 additions (13 Oct 2026)
# Read-only checks of what migrations 0062-0066 and the 1.12 setup scripts leave behind (112-W1-INTEGRATION-DONE.md "Left for the VM",
# 112-W1-REVIEW.md B-1, B-2, M-3, m-4; lane notes C1, A3, B1, D1, D2). SELECT / sys.* / Get-Acl / Test-Path only; the Viewer probes use
# EXECUTE AS USER with REVERT in the same batch. Nothing is written, no task is started. Evidence: counts, folder names, account short names.
# No customer data is read: the three denied tables are only probed for the error number.
$ObjectTablesExpected = @('schema_objects', 'schema_object_history', 'bank_accounts', 'collection_providers', 'bank_statement_lines', 'provider_settlement_lines', 'collections_setup_history', 'cash_deposit_confirmations', 'cash_deposit_confirmation_history')
$ObjectColumnsExpected = @('product_settings.collections_evidence_root', 'product_settings.update_feed_path', 'product_settings.update_check_enabled', 'tally_profiles.test_machine_name')   # 0063, 0065, 0066
$ViewerDeniedTables = @('dbo.bank_statement_lines', 'dbo.provider_settlement_lines', 'dbo.cash_deposit_confirmations')   # m-4: DENY SELECT to etp_viewer (0063, 0064)
$ShopResultsFolders = @('Operations\ShopResults\pending', 'Operations\ShopResults\published', 'Operations\ShopResults\rejected')
$EvidenceFolders = @('CollectionsEvidence', 'TallyEvidence')
$UpdatesFolder = 'Updates'
$ShopResultScripts = @('scripts\onedrive\ShopResultCommon.ps1', 'scripts\onedrive\Publish-ShopResults.ps1', 'scripts\onedrive\Show-ShopResults.ps1', 'scripts\onedrive\Register-WorkpcTasks.ps1', 'scripts\onedrive\Register-ShopDailyTask.ps1', 'scripts\remove-etp-scheduled-tasks.ps1', 'scripts\initialize-etp-operation-folders.ps1')
$ShopResultTasks = @('ETP Publish Shop Results', 'ETP Show Shop Results')   # registered by Register-WorkpcTasks.ps1 / Register-ShopDailyTask.ps1 (not by setup); removed by uninstall
$script:Ui112Added = $false

$Release112FactsBlock = {
    param([string]$ViewerIdentity, [string]$TablesCsv, [string]$ColumnsCsv, [string]$DeniedCsv, [string]$FoldersCsv, [string]$ScriptsCsv, [string]$TasksCsv)
    $f = [ordered]@{ appDir = ''; objectsDirExists = $false; objectFiles = $null; objectsTable = $false; objectRows = $null; historyRows = $null; filesNotInJournal = @(); journalNotInFiles = @()
        tables = @(); columns = @(); scripts = @(); removeScriptHasTasks = $false; tasks = @()
        automationAccount = ''; automationSid = ''; ownerSids = @(); managerSids = @(); ownerNames = @(); managerNames = @(); folders = @()
        viewerUser = ''; viewerErrors = @(); health = $null; healthResults = $null; errors = @() }
    $db = 'EtpReporting'
    $mod = [int][Security.AccessControl.FileSystemRights]::Modify
    $wr = [int][Security.AccessControl.FileSystemRights]::Write
    $full = [int][Security.AccessControl.FileSystemRights]::FullControl
    try {
        $f.appDir = [string](Get-P6AppDir)
        $objDir = Join-Path $f.appDir 'database\objects'
        $f.objectsDirExists = [bool](Test-Path -LiteralPath $objDir -PathType Container)
        if ($f.objectsDirExists) { $files = @(Get-ChildItem -LiteralPath $objDir -Filter '*.sql' -File -ErrorAction Stop | ForEach-Object { $_.Name }); $f.objectFiles = $files.Count }
    }
    catch { $f.errors += "object files: $($_.Exception.Message)" }
    try {
        $names = ($TablesCsv -split ',' | Where-Object { $_ } | ForEach-Object { "N'" + ($_ -replace "'", "''") + "'" }) -join ', '
        $f.tables = (Invoke-P6Sql -Database $db -Query "SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID(N'dbo') AND name IN ($names) ORDER BY name")
        $f.objectsTable = [bool](@($f.tables | Where-Object { $_.name -eq 'schema_objects' }).Count -gt 0)
    }
    catch { $f.errors += "tables: $($_.Exception.Message)" }
    try {
        $present = New-Object System.Collections.Generic.List[object]
        foreach ($c in ($ColumnsCsv -split ',' | Where-Object { $_ })) {
            $pair = $c -split '\.'
            $r = Invoke-P6Sql -Database $db -Parameters @{ t = ('dbo.' + $pair[0]); c = $pair[1] } -Query "SELECT CASE WHEN COL_LENGTH(@t, @c) IS NULL THEN 0 ELSE 1 END AS e"
            $present.Add([pscustomobject]@{ column = $c; present = [bool]([int]$r[0].e) })
        }
        $f.columns = $present.ToArray()
    }
    catch { $f.errors += "columns: $($_.Exception.Message)" }
    if ($f.objectsTable) {
        try {
            $f.objectRows = [int]((Invoke-P6Sql -Database $db -Query "SELECT COUNT(*) AS n FROM dbo.schema_objects")[0].n)
            $f.historyRows = [int]((Invoke-P6Sql -Database $db -Query "SELECT COUNT(*) AS n FROM dbo.schema_object_history")[0].n)
            if ($f.objectFiles -ne $null) {
                $journalFiles = @((Invoke-P6Sql -Database $db -Query "SELECT file_name FROM dbo.schema_objects ORDER BY file_name") | ForEach-Object { [string]$_.file_name })
                $diskFiles = @(Get-ChildItem -LiteralPath (Join-Path $f.appDir 'database\objects') -Filter '*.sql' -File | ForEach-Object { $_.Name })
                $f.filesNotInJournal = @($diskFiles | Where-Object { $journalFiles -notcontains $_ } | Select-Object -First 10)
                $f.journalNotInFiles = @($journalFiles | Where-Object { $diskFiles -notcontains $_ } | Select-Object -First 10)
            }
        }
        catch { $f.errors += "schema_objects: $($_.Exception.Message)" }
    }
    # DB-07 and the check count of the newest health run (read-only; empty when no run is recorded yet).
    try {
        if ([bool]((Invoke-P6Sql -Database $db -Query "SELECT CASE WHEN OBJECT_ID(N'diag.health_check_results', N'U') IS NULL THEN 0 ELSE 1 END AS e")[0].e)) {
            $h = Invoke-P6Sql -Database $db -Query "SELECT TOP (1) r.health_check_run_id AS run_id, r.outcome, r.finding_code, h.trigger_kind, CONVERT(varchar(19), h.started_utc, 126) AS started_utc FROM diag.health_check_results AS r JOIN diag.health_check_runs AS h ON h.health_check_run_id = r.health_check_run_id WHERE r.check_id = 'DB-07' ORDER BY r.health_check_run_id DESC"
            if ($h.Count -gt 0) { $f.health = $h[0] }
            $n = Invoke-P6Sql -Database $db -Query "SELECT COUNT(*) AS n FROM diag.health_check_results WHERE health_check_run_id = (SELECT MAX(health_check_run_id) FROM diag.health_check_runs)"
            if ($n.Count -gt 0) { $f.healthResults = [int]$n[0].n }
        }
    }
    catch { $f.errors += "DB-07: $($_.Exception.Message)" }
    # Scripts shipped in the app folder and the uninstall script naming both new tasks.
    try {
        $found = New-Object System.Collections.Generic.List[object]
        foreach ($s in ($ScriptsCsv -split ',' | Where-Object { $_ })) { $found.Add([pscustomobject]@{ script = $s; present = [bool](Test-Path -LiteralPath (Join-Path $f.appDir $s) -PathType Leaf) }) }
        $f.scripts = $found.ToArray()
        $rm = Join-Path $f.appDir 'scripts\remove-etp-scheduled-tasks.ps1'
        if (Test-Path -LiteralPath $rm -PathType Leaf) {
            $all = $true
            foreach ($t in ($TasksCsv -split ',' | Where-Object { $_ })) { if (-not (Select-String -LiteralPath $rm -SimpleMatch -Pattern $t -Quiet)) { $all = $false } }
            $f.removeScriptHasTasks = $all
        }
    }
    catch { $f.errors += "scripts: $($_.Exception.Message)" }
    try {
        $tk = New-Object System.Collections.Generic.List[object]
        foreach ($t in ($TasksCsv -split ',' | Where-Object { $_ })) {
            $x = @(Get-ScheduledTask -TaskName $t -ErrorAction SilentlyContinue)
            if ($x.Count -gt 0) { $tk.Add([pscustomobject]@{ name = $t; registered = $true; state = [string]$x[0].State; user = [string]$x[0].Principal.UserId }) } else { $tk.Add([pscustomobject]@{ name = $t; registered = $false; state = ''; user = '' }) }
        }
        $f.tasks = $tk.ToArray()
    }
    catch { $f.errors += "tasks: $($_.Exception.Message)" }
    # Who should hold Modify: the automation account (pending only), active Owners (Updates) and Owners + Store Managers (evidence folders), as setup reads them.
    foreach ($candidate in @('EtpAutomation', "$env:COMPUTERNAME\EtpAutomation")) {
        if ($f.automationSid) { break }
        try { $f.automationSid = ([Security.Principal.NTAccount]::new($candidate)).Translate([Security.Principal.SecurityIdentifier]).Value; $f.automationAccount = $candidate } catch { }
    }
    try {
        $u = Invoke-P6Sql -Database $db -Query "SELECT windows_identity, role_code FROM dbo.application_users WHERE role_code IN ('OWNER','STORE_MANAGER') AND is_active = 1 ORDER BY windows_identity"
        $own = New-Object System.Collections.Generic.List[string]; $mgr = New-Object System.Collections.Generic.List[string]
        $ownN = New-Object System.Collections.Generic.List[string]; $mgrN = New-Object System.Collections.Generic.List[string]
        foreach ($row in $u) {
            $sid = $null
            try { $sid = ([Security.Principal.NTAccount]::new([string]$row.windows_identity)).Translate([Security.Principal.SecurityIdentifier]).Value } catch { }
            $short = ([string]$row.windows_identity -split '\\')[-1]
            if ([string]$row.role_code -eq 'OWNER') { $ownN.Add($short); if ($sid) { $own.Add($sid) } } else { $mgrN.Add($short); if ($sid) { $mgr.Add($sid) } }
        }
        $f.ownerSids = $own.ToArray(); $f.managerSids = $mgr.ToArray(); $f.ownerNames = $ownN.ToArray(); $f.managerNames = $mgrN.ToArray()
    }
    catch { $f.errors += "active Owners and Store Managers: $($_.Exception.Message)" }
    # Folder ACLs: Allow entries only; rights reduced to modify / write / full booleans per account.
    try {
        $base = Join-Path $env:ProgramData 'EtpReporting'
        $list = New-Object System.Collections.Generic.List[object]
        foreach ($rel in ($FoldersCsv -split ',' | Where-Object { $_ })) {
            $p = Join-Path $base $rel
            $row = [ordered]@{ folder = $rel; exists = $false; isProtected = $false; entries = @() }
            if (Test-Path -LiteralPath $p -PathType Container) {
                $row.exists = $true
                $acl = Get-Acl -LiteralPath $p
                $row.isProtected = [bool]$acl.AreAccessRulesProtected
                $ent = New-Object System.Collections.Generic.List[object]
                foreach ($r in $acl.Access) {
                    if ([string]$r.AccessControlType -ne 'Allow') { continue }
                    $sid = ''
                    try { $sid = ([Security.Principal.NTAccount]::new([string]$r.IdentityReference)).Translate([Security.Principal.SecurityIdentifier]).Value } catch { $sid = [string]$r.IdentityReference }
                    $rights = [int]$r.FileSystemRights
                    $ent.Add([pscustomobject]@{ sid = $sid; name = [string]$r.IdentityReference; modify = (($rights -band $mod) -eq $mod); write = (($rights -band $wr) -ne 0); fullControl = (($rights -band $full) -eq $full); inherited = [bool]$r.IsInherited })
                }
                $row.entries = $ent.ToArray()
            }
            $list.Add([pscustomobject]$row)
        }
        $f.folders = $list.ToArray()
    }
    catch { $f.errors += "folder ACLs: $($_.Exception.Message)" }
    # m-4: the Viewer is denied SELECT on the three collections tables. One batch, EXECUTE AS USER ... REVERT; only the error number is read.
    if ($ViewerIdentity) {
        try {
            $vu = Invoke-P6Sql -Database $db -Parameters @{ id = $ViewerIdentity } -Query "SELECT dp.name AS user_name FROM dbo.application_users AS u JOIN sys.database_principals AS dp ON dp.sid = SUSER_SID(u.windows_identity) WHERE u.windows_identity = @id"
            if ($vu.Count -gt 0) { $f.viewerUser = [string]$vu[0].user_name }
        }
        catch { $f.errors += "viewer user (1.12): $($_.Exception.Message)" }
        if ($f.viewerUser) {
            $probes = New-Object System.Collections.Generic.List[string]
            $i = 0
            foreach ($tbl in ($DeniedCsv -split ',' | Where-Object { $_ })) {
                $i++
                $parts = $tbl -split '\.'
                $q = '[' + ($parts[0] -replace '\]', ']]') + '].[' + ($parts[1] -replace '\]', ']]') + ']'
                $probes.Add("DECLARE @e$i int = 0, @x$i int; BEGIN TRY SELECT TOP (1) @x$i = 1 FROM $q; END TRY BEGIN CATCH SET @e$i = ERROR_NUMBER(); END CATCH;")
            }
            $sel = (1..$i | ForEach-Object { "@e$_ AS e$_" }) -join ', '
            $batch = "DECLARE @sql nvarchar(max) = N'EXECUTE AS USER = N''' + REPLACE(@u, N'''', N'''''') + N''';`r`n" + ($probes -join "`r`n") + "`r`nREVERT;`r`nSELECT $sel;';`r`nEXEC sys.sp_executesql @sql;"
            try {
                $vr = Invoke-P6Sql -Database $db -Parameters @{ u = $f.viewerUser } -Query $batch
                if ($vr.Count -gt 0) {
                    $out = New-Object System.Collections.Generic.List[object]; $k = 0
                    foreach ($tbl in ($DeniedCsv -split ',' | Where-Object { $_ })) { $k++; $out.Add([pscustomobject]@{ table = $tbl; error = [int]$vr[0].("e$k") }) }
                    $f.viewerErrors = $out.ToArray()
                }
                else { $f.errors += 'viewer probes: no row returned' }
            }
            catch { $f.errors += "viewer probes: $($_.Exception.Message)" }
        }
    }
    $f | ConvertTo-Json -Depth 7 -Compress
}

function Add-Release112UiSteps {
    if ($script:Ui112Added) { return }
    $script:Ui112Added = $true
    # Manual checks for Sagar (112-W1-INTEGRATION-DONE.md "Left for the VM"; 112-W1-REVIEW.md integrator checklist). Synthetic data only; no customer names or phone numbers anywhere.
    $script:UiSteps.Add('1.12.0 B-1 shop zip -> result: run Register-WorkpcTasks.ps1 in the VM (elevated) so ETP Publish Shop Results exists and create the shop key (shop-keys\<ROLE>.key, key ceremony, you type the passphrase). Put ONE synthetic shop zip through the automation run (ETP --automation-once as EtpAutomation, or the five-minute task): a file appears in C:\ProgramData\EtpReporting\Operations\ShopResults\pending. Start-ScheduledTask "ETP Publish Shop Results" (or Publish-ShopResults.ps1): the file moves to published and a signed result lands in the OneDrive Results folder; Show-ShopResults.ps1 on the "shop" shows it. Mark done only if all four happen.')
    $script:UiSteps.Add('1.12.0 B-1 negative: take Modify off EtpAutomation on ShopResults\pending (icacls, then restore it), run the automation once: the automation-run line (Import > History / automation-runs) carries a plain sentence naming why no result file was written. Restore the ACL and re-run -Stage Release112.')
    $script:UiSteps.Add('1.12.0 B-2 Store Manager bank import: sign in as EtpTestManager (not elevated), start ETP, import a SYNTHETIC bank statement file (made up; never a real statement). It must import without "ask the owner to run ETP setup again"; a copy appears in C:\ProgramData\EtpReporting\CollectionsEvidence, and its name keeps only the last four digits of any run of five or more digits (m-5). Do the same for a Tally file (TallyEvidence) when the Tally step below is reached.')
    $script:UiSteps.Add('1.12.0 B-2 negative: as EtpTestViewer, SELECT on the three collections tables is denied (this script proves it in SQL); the Collections screens must show nothing from them.')
    $script:UiSteps.Add('1.12.0 B1 update rehearsal (B1 note watch-point 7): in the VM console run the update from a signed test feed (Settings > Updates, feed path set in 0065 columns) with the installer of a HIGHER test version if one exists, else the same 1.12.0 kit: (a) postinstall under /SILENT, (b) runasoriginaluser, (c) AppMutex wait with ETP open, (d) ProductVersion reads 1.12.0 after, (e) decline UAC -> NotStarted and the app is unchanged, (f) a held installer launches, (g) PreUpdate backup exists (receipt PRE_UPDATE) before the installer starts, (h) the Updates folder ACL is as this script reports. Take a checkpoint BEFORE you start; the rehearsal changes the VM.')
    $script:UiSteps.Add('1.12.0 D1 Tally send in Educational mode (VM only; Tally Prime Educational must be running with a TEST company): in SQL on the VM set dbo.tally_profiles.test_machine_name to the VM computer name (there is no Settings field until wave 2), port 9000, build label EDUCATIONAL, TEST profile. Verify (1) the licence request shape: TYPE=Function, ID=$$LicenseInfo, PARAM IsEducationalMode, answer <RESULT>Yes</RESULT> (until verified, sends refuse: safe); (2) SendAsync posts one synthetic voucher batch; (3) it REFUSES with another PC name in the profile, and (4) refuses when the licence answer is not Yes, before any attempt row; (5) D1 unverified list (Day Book without SVCURRENTCOMPANY, masters tags, import answer shape, allowed Educational days). Never point a profile at real books.')
    $script:UiSteps.Add('1.12.0 tasks as designed: after Register-WorkpcTasks.ps1 / Register-ShopDailyTask.ps1 run in the VM, re-run -Stage Release112: the two tasks show registered. Then take a checkpoint and UNINSTALL ETP in the VM: both ETP Publish Shop Results and ETP Show Shop Results are gone (remove-etp-scheduled-tasks.ps1), then restore the checkpoint.')
    $script:UiSteps.Add('1.12.0 key ceremony (m-9): on a fixed-drive VM only, the ceremony with -AllowFixedDrive needs the typed FIXED <letter> confirmation; the system drive is refused first. Try the refusal once.')
}

function Test-Release112Checks {
    param([string]$Area, [switch]$RequireHealth)
    $viewer = @($TestAccounts | Where-Object { $_.Role -eq 'VIEWER' }) | Select-Object -First 1
    $viewerIdentity = ''
    if ($viewer) { $viewerIdentity = [string]$viewer.Identity }
    $d = Invoke-GuestJson $Release112FactsBlock @($viewerIdentity, ($ObjectTablesExpected -join ','), ($ObjectColumnsExpected -join ','), ($ViewerDeniedTables -join ','), (($ShopResultsFolders + $EvidenceFolders + @($UpdatesFolder)) -join ','), ($ShopResultScripts -join ','), ($ShopResultTasks -join ','))
    Repair-RowSets $d @('tables', 'columns', 'scripts', 'tasks', 'folders', 'viewerErrors', 'filesNotInJournal', 'journalNotInFiles', 'ownerSids', 'managerSids', 'ownerNames', 'managerNames') @('health')
    $slug = (($Area -replace '[^A-Za-z0-9]+', '-').Trim('-')).ToLowerInvariant()
    Save-Json "release-facts-112-$slug.json" $d
    $errs = @(@($d.errors) | Where-Object { $_ })
    foreach ($e in $errs) { Write-Host "    guest note: $e" -ForegroundColor Yellow }
    Add-Check $Area '1.12.0 read-only checks ran' 'no query error' $(if ($errs.Count) { $errs -join ' / ' } else { 'none' }) (Get-Result ($errs.Count -eq 0))

    # 0062-0066: tables and columns.
    $haveT = @($d.tables | ForEach-Object { [string]$_.name })
    $missT = @($ObjectTablesExpected | Where-Object { $haveT -notcontains $_ })
    Add-Check $Area "1.12.0 tables exist (0062-0064): $($ObjectTablesExpected.Count)" ($ObjectTablesExpected -join ', ') $(if ($missT.Count) { 'missing: ' + ($missT -join ', ') } else { 'all present' }) (Get-Result ($missT.Count -eq 0))
    $missC = @($d.columns | Where-Object { -not [bool]$_.present } | ForEach-Object { [string]$_.column })
    Add-Check $Area '1.12.0 columns exist (0063 collections_evidence_root, 0065 update_feed_path + update_check_enabled, 0066 tally_profiles.test_machine_name)' ($ObjectColumnsExpected -join ', ') $(if ($missC.Count -or @($d.columns).Count -eq 0) { 'missing: ' + ($missC -join ', ') } else { 'all present' }) (Get-Result (($missC.Count -eq 0) -and (@($d.columns).Count -eq $ObjectColumnsExpected.Count)))

    # A3: schema objects journal = the object files in the installed app folder.
    Add-Check $Area 'database\objects folder is in the app folder (A3 packaging)' 'present, with .sql files' "exists=$($d.objectsDirExists); files=$($d.objectFiles); app folder $($d.appDir)" (Get-Result ([bool]$d.objectsDirExists -and ($null -ne $d.objectFiles) -and ([int]$d.objectFiles -gt 0)))
    if ([bool]$d.objectsTable -and $null -ne $d.objectFiles -and $null -ne $d.objectRows) {
        $nf = @($d.filesNotInJournal) -join ', '; $nj = @($d.journalNotInFiles) -join ', '
        $txt = "$($d.objectRows) journal rows, $($d.objectFiles) object files"
        if ($nf) { $txt += "; file(s) not in the journal: $nf" }
        if ($nj) { $txt += "; journal row(s) with no file: $nj" }
        Add-Check $Area 'dbo.schema_objects rows = object files in database\objects (A3, setup adopted every object)' 'equal, none missing either way' $txt (Get-Result (([int]$d.objectRows -eq [int]$d.objectFiles) -and ([int]$d.objectFiles -gt 0) -and -not $nf -and -not $nj))
        Add-Check $Area 'dbo.schema_object_history has at least one row per object' ">= $($d.objectRows)" "$($d.historyRows) rows (append-only; ADOPT on a first upgrade, APPLY after)" (Get-Result ([int]$d.historyRows -ge [int]$d.objectRows) 'REVIEW')
    }
    else { Add-Check $Area 'dbo.schema_objects rows = object files in database\objects' 'journal table and files readable' "table=$($d.objectsTable); rows=$($d.objectRows); files=$($d.objectFiles)" 'FAIL' }

    # DB-07 "Database objects match this release" from the newest health run (needs one: -Stage HealthNightOne or Settings > Database > Health check).
    if ($d.health) {
        $ok = ([string]$d.health.outcome -eq 'Pass')
        Add-Check $Area 'DB-07 Database objects match this release (newest health run)' 'Pass (OK)' "$($d.health.outcome) ($($d.health.finding_code)); run $($d.health.run_id) $($d.health.trigger_kind) at $($d.health.started_utc) UTC; $($d.healthResults) checks in the newest run" (Get-Result $ok $(if ([string]$d.health.outcome -eq 'Skipped') { 'REVIEW' } else { 'FAIL' }))
        Add-Check $Area 'Health check runs 55 checks (A3: the screen says 55)' '55 results in the newest run' "$($d.healthResults)" (Get-Result ([int]$d.healthResults -eq 55) 'REVIEW')
    }
    else {
        $lvl = 'INFO'; if ($RequireHealth) { $lvl = 'REVIEW' }
        Add-Check $Area 'DB-07 Database objects match this release (newest health run)' 'Pass (OK)' 'no DB-07 result recorded yet: run -Stage HealthNightOne (after 02:30 VM time) or the Owner health check in Settings > Database, then -Stage Release112' $lvl
    }

    # Folders and ACLs.
    $auto = [string]$d.automationSid
    $ownerSids = @($d.ownerSids); $managerSids = @($d.managerSids)
    $writerSids = @($ownerSids + $managerSids)
    $wellKnownOk = @('S-1-5-18', 'S-1-5-32-544')
    $broadSids = @('S-1-1-0', 'S-1-5-32-545', 'S-1-5-11', 'S-1-5-4', 'S-1-5-32-546')
    $short = { param($e) ([string]$e.name -split '\\')[-1] }
    foreach ($rel in $ShopResultsFolders) {
        $row = @($d.folders | Where-Object { [string]$_.folder -eq $rel }) | Select-Object -First 1
        $leaf = ($rel -split '\\')[-1]
        if (-not $row -or -not [bool]$row.exists) { Add-Check $Area "ShopResults\$leaf exists (B-1)" 'present under Operations (setup / initialize-etp-operation-folders.ps1)' 'missing' 'FAIL'; continue }
        $autoEntries = @($row.entries | Where-Object { $auto -and [string]$_.sid -eq $auto })
        if ($leaf -eq 'pending') {
            if ($auto) { Add-Check $Area 'ShopResults\pending: the automation account has Modify (B-1)' "$($d.automationAccount) Modify" $(if ($autoEntries.Count) { ($autoEntries | ForEach-Object { "modify=$($_.modify) write=$($_.write)" }) -join '; ' } else { 'no entry for the account' }) (Get-Result (@($autoEntries | Where-Object { [bool]$_.modify }).Count -ge 1)) }
            else { Add-Check $Area 'ShopResults\pending: the automation account has Modify (B-1)' 'EtpAutomation Modify' 'no EtpAutomation account in the VM yet (add it in Settings > Users / -UserChanges Script, run setup again)' 'REVIEW' }
        }
        else {
            Add-Check $Area "ShopResults\$leaf`: the automation account cannot write (read only, B-1)" 'no write entry for the automation account' $(if ($autoEntries.Count) { ($autoEntries | ForEach-Object { "write=$($_.write)" }) -join '; ' } else { 'no direct entry (read comes from the Operations rule)' }) (Get-Result (@($autoEntries | Where-Object { [bool]$_.write }).Count -eq 0))
        }
        $broad = @($row.entries | Where-Object { $broadSids -contains [string]$_.sid } | ForEach-Object { & $short $_ })
        Add-Check $Area "ShopResults\$leaf`: no broad access" 'no Everyone, Users, Authenticated Users, Interactive, Guests' $(if ($broad.Count) { $broad -join ', ' } else { 'none' }) (Get-Result ($broad.Count -eq 0))
    }
    foreach ($rel in $EvidenceFolders) {
        $row = @($d.folders | Where-Object { [string]$_.folder -eq $rel }) | Select-Object -First 1
        if (-not $row -or -not [bool]$row.exists) { Add-Check $Area "$rel exists (B-2)" 'present under C:\ProgramData\EtpReporting (setup)' 'missing' 'FAIL'; continue }
        $haveMod = @($row.entries | Where-Object { [bool]$_.modify -and ($writerSids -contains [string]$_.sid) } | ForEach-Object { [string]$_.sid })
        $lacking = @($writerSids | Where-Object { $haveMod -notcontains $_ })
        Add-Check $Area "$rel`: active Owner and Store Manager accounts have Modify (B-2)" "$($ownerSids.Count) Owner(s) [$(@($d.ownerNames) -join ', ')] and $($managerSids.Count) Store Manager(s) [$(@($d.managerNames) -join ', ')] with Modify" $(if ($writerSids.Count -eq 0) { 'no active Owner/Store Manager SID resolved' } elseif ($lacking.Count) { "$($lacking.Count) of $($writerSids.Count) lack Modify (if users were added after the last setup run, run setup again)" } else { "all $($writerSids.Count) have Modify" }) (Get-Result (($writerSids.Count -gt 0) -and ($lacking.Count -eq 0)))
        $unexpected = @($row.entries | Where-Object { ($wellKnownOk -notcontains [string]$_.sid) -and ($writerSids -notcontains [string]$_.sid) } | ForEach-Object { & $short $_ })
        Add-Check $Area "$rel`: nobody else (SYSTEM and Administrators full, the writers Modify)" 'no other account, no broad group' $(if ($unexpected.Count) { $unexpected -join ', ' } else { 'none' }) (Get-Result ($unexpected.Count -eq 0))
        Add-Check $Area "$rel`: does not inherit from ProgramData" 'protected ACL' "protected=$($row.isProtected)" (Get-Result ([bool]$row.isProtected))
    }
    $up = @($d.folders | Where-Object { [string]$_.folder -eq $UpdatesFolder }) | Select-Object -First 1
    if (-not $up -or -not [bool]$up.exists) { Add-Check $Area 'Updates folder exists (B1 / A3 bootstrap)' 'present under C:\ProgramData\EtpReporting' 'missing' 'FAIL' }
    else {
        $upMod = @($up.entries | Where-Object { [bool]$_.modify -and ($ownerSids -contains [string]$_.sid) } | ForEach-Object { [string]$_.sid })
        $upLack = @($ownerSids | Where-Object { $upMod -notcontains $_ })
        Add-Check $Area 'Updates folder: active Owners have Modify (B1)' "$($ownerSids.Count) Owner(s) with Modify" $(if ($ownerSids.Count -eq 0) { 'no active Owner SID resolved' } elseif ($upLack.Count) { "$($upLack.Count) of $($ownerSids.Count) lack Modify" } else { "all $($ownerSids.Count) have Modify" }) (Get-Result (($ownerSids.Count -gt 0) -and ($upLack.Count -eq 0)))
        $upOther = @($up.entries | Where-Object { ($wellKnownOk -notcontains [string]$_.sid) -and ($ownerSids -notcontains [string]$_.sid) } | ForEach-Object { & $short $_ })
        Add-Check $Area 'Updates folder: nobody else, not the automation account (B1)' 'SYSTEM, Administrators and Owners only' $(if ($upOther.Count) { $upOther -join ', ' } else { 'none' }) (Get-Result ($upOther.Count -eq 0))
        Add-Check $Area 'Updates folder: does not inherit from ProgramData' 'protected ACL' "protected=$($up.isProtected)" (Get-Result ([bool]$up.isProtected))
    }

    # m-4: the Viewer is denied SELECT on the three collections tables.
    $vc = 'm-4: Viewer SELECT on bank_statement_lines, provider_settlement_lines, cash_deposit_confirmations is denied (error 229, EXECUTE AS USER, REVERT in the same batch)'
    if ($d.viewerUser -and @($d.viewerErrors).Count -gt 0) {
        $bad = @($d.viewerErrors | Where-Object { [int]$_.error -ne 229 } | ForEach-Object { "$($_.table) error $($_.error)" })
        Add-Check $Area "$vc - $($d.viewerUser)" 'error 229 on all three' $(if ($bad.Count) { $bad -join '; ' } else { 'error 229 on all three' }) (Get-Result (($bad.Count -eq 0) -and (@($d.viewerErrors).Count -eq $ViewerDeniedTables.Count)))
    }
    else { Add-Check $Area $vc 'skipped: needs the EtpTestViewer user (added by -UserChanges Script / Settings > Users)' 'no viewer database user' 'INFO' }

    # New scheduled tasks: setup does not register them (the OneDrive registrar scripts do); uninstall removes them. Scripts must ship.
    $missS = @($d.scripts | Where-Object { -not [bool]$_.present } | ForEach-Object { [string]$_.script })
    Add-Check $Area '1.12.0 shop-result scripts ship in the app folder (C1 / m-8 packaging)' ($ShopResultScripts -join ', ') $(if ($missS.Count -or @($d.scripts).Count -eq 0) { 'missing: ' + ($missS -join ', ') } else { 'all present' }) (Get-Result (($missS.Count -eq 0) -and (@($d.scripts).Count -eq $ShopResultScripts.Count)))
    Add-Check $Area 'remove-etp-scheduled-tasks.ps1 names ETP Publish Shop Results and ETP Show Shop Results (M-6: uninstall removes them)' 'both names present' "both=$($d.removeScriptHasTasks)" (Get-Result ([bool]$d.removeScriptHasTasks))
    Add-Check $Area 'ETP Publish Shop Results / ETP Show Shop Results tasks (registered by the OneDrive registrar scripts, not by setup)' 'absent after setup alone; registered after Register-WorkpcTasks.ps1 / Register-ShopDailyTask.ps1' (@($d.tasks | ForEach-Object { "$($_.name): $(if ([bool]$_.registered) { "registered, $($_.state), $($_.user)" } else { 'not registered' })" }) -join '; ') 'INFO'
}

function Invoke-Release112Stage {
    Open-GuestForStage
    $area = '1.12.0 checks'
    $f0 = Get-Facts 'release112'
    $ver = [string]$f0.exeProductVersion
    $j = @(Get-MigrationIds $f0.journal | Sort-Object -Unique)
    $latest = 'none'
    if ($j.Count -gt 0) { $latest = $j[-1] }
    Add-Check $area "The VM is on $NewVersion" "$NewVersion*, $ExpectedMigrationCount migrations, latest $ExpectedLatestMigration" "$ver; $($j.Count) migrations, latest $latest" (Get-Result (($ver -like "$NewVersion*") -and ($j.Count -eq $ExpectedMigrationCount) -and ($latest -like $ExpectedLatestMigration)))
    if ($ver -notlike "$NewVersion*") { throw "The VM is not on $NewVersion (it reports '$ver'). Run -Scenario Upgrade -Stage Install first." }
    $missingNames = @($ExpectedNewMigrations | Where-Object { $p = $_; @($j | Where-Object { $_ -like $p }).Count -ne 1 })
    Add-Check $area 'Journal holds 0049-0066 by name' ($ExpectedNewMigrations -join ', ') $(if ($missingNames.Count) { 'missing: ' + ($missingNames -join ', ') } else { 'all present' }) (Get-Result ($missingNames.Count -eq 0))
    $etp = [int](Invoke-Guest $NoEtpProcessBlock)
    if ($etp -gt 0) { $script:Notes.Add("Release112: ETP was running in the VM ($etp process) during the checks; the checks are read-only and do not depend on it.") }
    Test-Release112Checks $area -RequireHealth
    Add-Release112UiSteps
}

# ================================================================ 1.11.2: -Stage HealthNightOne
# The design's "night-one dry run on a restored copy, never live": in the guest, after the install/upgrade, run ETP's automation task command once
# and read what the nightly health check wrote. The app has NO other headless health switch (checked read-only in release-1.11.2
# src\Etp.Reporting.Desktop: DesktopStartupCoordinator.Route knows only --initialize-database, --initialize-configured-database and
# --automation-once; the manual "Run health check" is the Owner's button in Settings > Database > Health check). --automation-once runs the
# watch-folder step, then NightlyHealthSchedule.RunIfDueAsync, which runs the Nightly check only when guest LOCAL time is 02:30 or later and no
# Nightly run is recorded for yesterday (D-1); a failed attempt is retried at most 3 times per business date, 30 minutes apart. So:
#  - before 02:30 guest local time the stage runs nothing (REVIEW, run it again after 02:30; the guest time is not changed by this script);
#  - if the guest's own five-minute task already recorded the Nightly run for D-1, nothing is run again: its export is read (REVIEW);
#  - after the run the stage ticks once more and expects NO second run (the same night is never repeated).
# It writes one Nightly row to diag.health_check_runs and one file to HealthExports on the VM only. No checkpoint is taken: restore the
# Delta/Install checkpoint to repeat it. Evidence: the export (counts, codes, dates, version numbers; privacy-checked by the app), not the
# stdout/stderr, which stay inside the VM.
$HealthPrepBlock = {
    param([string]$ExportsFolder)
    $now = Get-Date
    $r = [ordered]@{ localNow = $now.ToString('yyyy-MM-ddTHH:mm:ss'); minutesIntoDay = [int]$now.TimeOfDay.TotalMinutes; businessDate = $now.Date.AddDays(-1).ToString('yyyy-MM-dd')
        appExe = (Join-Path (Get-P6AppDir) 'Etp.Reporting.Desktop.exe'); exportsExists = [bool](Test-Path -LiteralPath $ExportsFolder -PathType Container)
        exports = @(); lastNightly = ''; maxRunId = 0; runCount = 0; errors = @() }
    $r.exports = @(Get-ChildItem -LiteralPath $ExportsFolder -Filter 'health-*.json' -File -ErrorAction SilentlyContinue | ForEach-Object { $_.Name })
    try {
        $q = (Invoke-P6Sql -Database 'EtpReporting' -Query "SELECT CONVERT(varchar(10), MAX(CASE WHEN trigger_kind = 'Nightly' THEN business_date END), 23) AS last_nightly, ISNULL(MAX(health_check_run_id), 0) AS max_id, COUNT_BIG(*) AS runs FROM diag.health_check_runs")[0]
        $r.lastNightly = [string]$q.last_nightly; $r.maxRunId = [int64]$q.max_id; $r.runCount = [int64]$q.runs
    }
    catch { $r.errors += "diag.health_check_runs: $($_.Exception.Message)" }
    $r | ConvertTo-Json -Depth 4 -Compress
}

$HealthAfterBlock = {
    param([string]$ExportsFolder, [string]$MaxIdBefore, [string]$BeforeCsv)
    $before = @($BeforeCsv -split '\|' | Where-Object { $_ })
    $r = [ordered]@{ newRuns = @(); allFiles = @(); newFiles = @(); errors = @() }
    try {
        $r.newRuns = (Invoke-P6Sql -Database 'EtpReporting' -Parameters @{ id = $MaxIdBefore } -Query "SELECT health_check_run_id, trigger_kind, CONVERT(varchar(10), business_date, 23) AS business_date, app_version, run_by_role, overall, pass_count, warning_count, fail_count, known_count, skipped_count, export_file_name FROM diag.health_check_runs WHERE health_check_run_id > CONVERT(bigint, @id) ORDER BY health_check_run_id")
    }
    catch { $r.errors += "diag.health_check_runs: $($_.Exception.Message)" }
    $files = @(Get-ChildItem -LiteralPath $ExportsFolder -Filter 'health-*.json' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | ForEach-Object { $_.Name })
    $r.allFiles = $files
    $r.newFiles = @($files | Where-Object { $before -notcontains $_ })
    $r | ConvertTo-Json -Depth 4 -Compress
}

$HealthStderrBlock = {
    param([string]$Dir)
    $text = (@(Get-ChildItem -LiteralPath $Dir -Filter 'stderr-*.txt' -File -ErrorAction SilentlyContinue | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw -ErrorAction SilentlyContinue }) -join "`n")
    $r = [ordered]@{ healthRunFailed = [bool]($text -match 'ETP-HEALTH-RUN-FAILED'); anyText = [bool]($text.Trim().Length -gt 0) }
    $r | ConvertTo-Json -Compress
}

function Invoke-HealthTick([string]$Label, [string]$AppExe, [string]$GuestDir, [int]$Number) {
    Wait-NoEtpProcess
    [void](Invoke-Guest $StartProcBlock @($AppExe, '--automation-once', "$GuestDir\stdout-$Number.txt", "$GuestDir\stderr-$Number.txt"))
    return (Wait-GuestProcess -What "automation tick ($Label)" -TimeoutMinutes $ImportTimeoutMinutes)
}

function Invoke-HealthNightOneStage {
    Open-GuestForStage
    $area = 'Health night one'
    Wait-NoEtpProcess
    $f0 = Get-Facts 'healthnightone'
    $ver = [string]$f0.exeProductVersion
    $j = @(Get-MigrationIds $f0.journal | Sort-Object -Unique)
    $latest = 'none'
    if ($j.Count -gt 0) { $latest = $j[-1] }
    Add-Check $area "The VM is on $NewVersion" "$NewVersion*, $ExpectedMigrationCount migrations, latest $ExpectedLatestMigration" "$ver; $($j.Count) migrations, latest $latest" (Get-Result (($ver -like "$NewVersion*") -and ($j.Count -eq $ExpectedMigrationCount) -and ($latest -like $ExpectedLatestMigration)))
    if ($ver -notlike "$NewVersion*") { throw "The VM is not on $NewVersion (it reports '$ver'). Run -Stage Delta (or -Stage Install) first." }
    Test-Release1111Checks $area
    $p = Invoke-GuestJson $HealthPrepBlock @($HealthExportsFolder)
    Repair-RowSets $p @('exports')
    Save-Json 'health-night-one\prep.json' $p
    foreach ($e in @($p.errors)) { if ($e) { Write-Host "    guest note: $e" -ForegroundColor Yellow } }
    $script:RunInfo['Guest local time at start'] = [string]$p.localNow
    $script:RunInfo['Nightly business date (D-1)'] = [string]$p.businessDate
    $already = (([string]$p.lastNightly) -ne '') -and (([string]$p.lastNightly) -ge ([string]$p.businessDate))
    $dueByClock = ([int]$p.minutesIntoDay -ge $HealthNightlyMinutes)
    Add-Check $area 'Guest local time is 02:30 or later (the nightly check is due from then)' '02:30 or later' ([string]$p.localNow) (Get-Result $dueByClock 'REVIEW')
    Add-Check $area 'Start: Nightly run for D-1 not yet recorded' "no Nightly run for $($p.businessDate)" $(if ($already) { "already recorded for $($p.lastNightly) (the guest's own task ran first)" } else { "last Nightly business date: $(if ($p.lastNightly) { $p.lastNightly } else { 'none' }); $($p.runCount) health runs recorded" }) (Get-Result (-not $already) 'REVIEW')
    if (-not $dueByClock -and -not $already) {
        $script:Notes.Add('HealthNightOne: guest local time was before 02:30, so the nightly check is not due and nothing was run. Run -Stage HealthNightOne again after 02:30 in the VM (the host and guest share the time zone; the script does not change the clock).')
        $script:UiSteps.Add('HealthNightOne was not run (before 02:30 in the VM). Run the same command again after 02:30 VM time.')
        return
    }
    $guestOnly = "$($script:GuestRunDir)\vm-only\health-night-one"
    Invoke-Guest { param($d) New-Item -ItemType Directory -Path $d -Force | Out-Null } @($guestOnly) | Out-Null
    $beforeCsv = (@($p.exports) -join '|')
    $a = $null
    $exitCodes = @()
    if ($already) {
        $a = Invoke-GuestJson $HealthAfterBlock @($HealthExportsFolder, '0', $beforeCsv)
        Repair-RowSets $a @('newRuns', 'allFiles', 'newFiles')
    }
    else {
        Write-Step 'Night-one health run: ETP automation once in the guest (--automation-once), as the nightly check would run it'
        for ($attempt = 1; $attempt -le 3; $attempt++) {
            $exit = Invoke-HealthTick "health $attempt" ([string]$p.appExe) $guestOnly $attempt
            $exitCodes += "$exit"
            $a = Invoke-GuestJson $HealthAfterBlock @($HealthExportsFolder, ([string]$p.maxRunId), $beforeCsv)
            Repair-RowSets $a @('newRuns', 'allFiles', 'newFiles')
            if (@($a.newRuns | Where-Object { [string]$_.trigger_kind -eq 'Nightly' }).Count -gt 0) { break }
            if ($attempt -lt 3) { Write-Host '  no Nightly run recorded yet (another unattended run may hold the lease); waiting 60 s and ticking again'; Start-Sleep -Seconds 60 }
        }
        $err = Invoke-GuestJson $HealthStderrBlock @($guestOnly)
        Add-Check $area 'Automation tick: exit code' '0 (a health Fail never changes it; 1 = import sources failed)' ($exitCodes -join ', ') (Get-Result ($exitCodes.Count -gt 0 -and $exitCodes[-1] -eq '0') 'REVIEW')
        Add-Check $area 'Automation tick: no ETP-HEALTH-RUN-FAILED on stderr (text stays in the VM)' 'none' "healthRunFailed=$($err.healthRunFailed)" (Get-Result (-not [bool]$err.healthRunFailed))
    }
    foreach ($e in @($a.errors)) { if ($e) { Write-Host "    guest note: $e" -ForegroundColor Yellow } }
    Save-Csv 'health-night-one\new-runs.csv' @($a.newRuns)
    $nightlyRows = @($a.newRuns | Where-Object { [string]$_.trigger_kind -eq 'Nightly' })
    $nightly = $null
    if ($nightlyRows.Count -gt 0) { $nightly = $nightlyRows[-1] }
    Add-Check $area 'A Nightly run was recorded in diag.health_check_runs' 'one Nightly row' $(if ($nightly) { "run $($nightly.health_check_run_id), business date $($nightly.business_date), $($nightlyRows.Count) row(s)" } else { 'none (see the tick exit codes; the schedule retries 3 times per business date, 30 minutes apart)' }) (Get-Result ([bool]$nightly -and ($nightlyRows.Count -eq 1)))
    if (-not $nightly) {
        $script:UiSteps.Add('HealthNightOne: no Nightly run was recorded. In the VM check Settings > Database > Health check and %LOCALAPPDATA%\EtpReporting\Logs\nightly-health-attempt.json of the account that ran the tick; fix, then run the stage again (the attempt gate allows 3 tries per business date).')
        return
    }
    Add-Check $area 'Nightly run is for D-1 and for this build' "business date $($p.businessDate); app version $NewVersion*" "business date $($nightly.business_date); app version $($nightly.app_version); run by role $($nightly.run_by_role)" (Get-Result (([string]$nightly.business_date -eq [string]$p.businessDate) -and ([string]$nightly.app_version -like "$NewVersion*")))
    $counts = "pass $($nightly.pass_count), warning $($nightly.warning_count), fail $($nightly.fail_count), known $($nightly.known_count), skipped $($nightly.skipped_count)"
    $script:RunInfo['Health night one'] = "overall $($nightly.overall); $counts"
    # The export file named by the run row.
    $name = [string]$nightly.export_file_name
    $namePattern = '^health-\d{8}-\d{6}-Nightly\.json$'
    $fileHere = ($name -ne '') -and (@($a.allFiles | ForEach-Object { [string]$_ }) -contains $name)
    Add-Check $area 'Export file written and named in the run row' 'health-yyyyMMdd-HHmmss-Nightly.json in HealthExports' "row: '$name'; in folder: $fileHere; new files this run: $(@($a.newFiles).Count)" (Get-Result (($name -match $namePattern) -and $fileHere))
    if (-not $fileHere) { $name = @($a.allFiles | ForEach-Object { [string]$_ } | Where-Object { $_ -match $namePattern }) | Select-Object -First 1 }
    if (-not $name) { throw 'No health-*-Nightly.json export is in HealthExports to copy.' }
    $dest = Join-Path $script:Out 'health-night-one'
    if (-not (Test-Path -LiteralPath $dest)) { New-Item -ItemType Directory -Path $dest -Force | Out-Null }
    Copy-Item -FromSession $script:Session -LiteralPath "$HealthExportsFolder\$name" -Destination $dest -Force
    $local = Join-Path $dest $name
    $hostHash = (Get-FileHash -LiteralPath $local -Algorithm SHA256).Hash
    $guestHash = [string](Invoke-Guest { param($p) (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash } @("$HealthExportsFolder\$name"))
    Add-Check $area 'Export copied to the evidence folder unchanged' 'SHA-256 equal' "$name ($([int](Get-Item -LiteralPath $local).Length) bytes); copy hash matches=$($hostHash -eq $guestHash)" (Get-Result ($hostHash -eq $guestHash))
    $script:RunInfo['Health export (evidence)'] = $local
    # Read it: schema, trigger, version, overall, counts.
    $x = Get-Content -LiteralPath $local -Raw -Encoding UTF8 | ConvertFrom-Json
    $results = @($x.results)
    Add-Check $area 'Export schema and trigger' 'schema etp-health-export/1, trigger Nightly' "schema $($x.schema), trigger $($x.run.trigger)" (Get-Result (([string]$x.schema -eq 'etp-health-export/1') -and ([string]$x.run.trigger -eq 'Nightly')))
    Add-Check $area 'Export version and business date' "$NewVersion*, $($p.businessDate)" "app version $($x.run.appVersion), business date $($x.run.businessDate)" (Get-Result (([string]$x.run.appVersion -like "$NewVersion*") -and ([string]$x.run.businessDate -eq [string]$p.businessDate)))
    $c = $x.run.counts
    $jsonCounts = "pass $($c.pass), warning $($c.warning), fail $($c.fail), known $($c.known), skipped $($c.skipped)"
    $sameAsRow = ([string]$x.run.overall -eq [string]$nightly.overall) -and ([int]$c.pass -eq [int]$nightly.pass_count) -and ([int]$c.warning -eq [int]$nightly.warning_count) -and ([int]$c.fail -eq [int]$nightly.fail_count) -and ([int]$c.known -eq [int]$nightly.known_count) -and ([int]$c.skipped -eq [int]$nightly.skipped_count)
    Add-Check $area 'Export overall and counts equal the run row in the database' "overall $($nightly.overall); $counts" "overall $($x.run.overall); $jsonCounts" (Get-Result $sameAsRow)
    $sum = [int]$c.pass + [int]$c.warning + [int]$c.fail + [int]$c.known + [int]$c.skipped
    Add-Check $area 'Export counts add up to the number of check results' "$($results.Count) results" "counts add up to $sum" (Get-Result ($sum -eq $results.Count) 'REVIEW')
    $red = $null
    if ($x.privacy) { $red = $x.privacy.redactions }
    Add-Check $area 'Export privacy guard replaced nothing' '0 redactions' "$red" (Get-Result (($null -ne $red) -and ([int]$red -eq 0)) 'REVIEW')
    # The outcome itself is the thing Sagar reads: Pass is PASS; Warning or Fail on a VM copy is REVIEW (a VM has no store folders, may lack backups).
    $notPass = @($results | Where-Object { [string]$_.outcome -ne 'Pass' } | ForEach-Object { "$($_.id) $($_.outcome)$(if ($_.finding) { ' ' + $_.finding })" })
    $overallLevel = 'REVIEW'
    if ([string]$x.run.overall -eq 'Pass') { $overallLevel = 'PASS' }
    Add-Check $area 'Night-one health outcome (read it; a VM copy may legitimately Warn or Fail)' 'Pass, or each Warning/Fail explained' "overall $($x.run.overall); $jsonCounts; not Pass: $(if ($notPass.Count) { $notPass -join '; ' } else { 'none' })" $overallLevel
    Save-Json 'health-night-one\health-summary.json' ([ordered]@{ file = $name; overall = [string]$x.run.overall; counts = $jsonCounts; notPass = $notPass; runId = $nightly.health_check_run_id; sha256 = $hostHash })
    # The same night is never run twice.
    if (-not $already) {
        $beforeRows = @($a.newRuns).Count
        $exit2 = Invoke-HealthTick 'second tick, must not repeat' ([string]$p.appExe) $guestOnly 9
        $a2 = Invoke-GuestJson $HealthAfterBlock @($HealthExportsFolder, [string]$nightly.health_check_run_id, $beforeCsv)
        Repair-RowSets $a2 @('newRuns', 'allFiles', 'newFiles')
        Add-Check $area 'A second tick the same night records no further run' '0 new runs after the Nightly run' "new runs $(@($a2.newRuns).Count); new files since the first tick: $(@($a2.newFiles).Count - @($a.newFiles).Count); exit $exit2" (Get-Result ((@($a2.newRuns).Count -eq 0) -and ((@($a2.newFiles).Count - @($a.newFiles).Count) -eq 0)))
    }
    $script:UiSteps.Add("HealthNightOne: in the VM console (ETPTest, Owner) Settings > Database > Health check shows the run above (overall $($nightly.overall); $counts) and its export in Open export folder; Error register opens. As EtpTestManager / EtpTestViewer neither screen is offered, only the one-line System check tile on Today.")
    $script:UiSteps.Add('HealthNightOne wrote one Nightly row and one export to the VM only. No checkpoint was taken; restore the Delta (or Install) checkpoint to repeat the night-one run.')
}

# ---------------------------------------------------------------- summary
function Write-Summary {
    $fails = @($script:Checks | Where-Object { $_.Result -eq 'FAIL' }).Count
    $reviews = @($script:Checks | Where-Object { $_.Result -eq 'REVIEW' }).Count
    $verdict = 'PASSED (automated part)'
    if ($script:LockBusy) { $verdict = 'NOT RUN: the heavy-job lock was busy' }
    elseif ($fails -gt 0) { $verdict = "FAILED: $fails check(s) failed" }
    elseif ($reviews -gt 0) { $verdict = "PASSED (automated part), with $reviews item(s) to review" }
    $title = "VM acceptance $NewVersion (clean install / upgrade from 1.9.2 or $DeltaFromVersion / delta $($script:DeltaFrom) -> $NewVersion / Service UI checks / night-one health run / shop rehearsal): $Scenario"
    if ($Scenario -eq 'Upgrade') { $title += " (from $UpgradeFrom)" }
    if ($Scenario -eq 'Rehearsal') { $title += " (decision 19 session on the rehearsal base; users: $UserChanges)" }
    $titan = New-Object System.Text.StringBuilder
    if ($script:TitanRows.Count -gt 0 -or $script:CountsAfter.Count -gt 0) {
        [void]$titan.AppendLine('## Held Titan files: expected counts for the live import')
        [void]$titan.AppendLine('')
        [void]$titan.AppendLine('Imported one at a time through the watch folder on the VM copy of live (1 Oct data, upgraded to 1.9.5), in this order. On live, import the same files (same SHA-256) in the same order and expect the same counts. Counts only; no business values.')
        [void]$titan.AppendLine('')
        [void]$titan.AppendLine('| # | File (in the 29 Sep package) | SHA-256 (first 8) | Report | Store | Outcome | Rows | New | Present | Conflicts | Predicted (new/present) |')
        [void]$titan.AppendLine('|---|---|---|---|---|---|---|---|---|---|---|')
        foreach ($r in $script:TitanRows) {
            $pred = 'none (set by this run)'
            if ($null -ne $r.ExpectedNew) { $pred = "$($r.ExpectedNew) / $($r.ExpectedPresent)" }
            [void]$titan.AppendLine("| $($r.Order) | $(Format-Cell $r.File) | $($r.Sha256First8) | $(Format-Cell $r.Report) | $(Format-Cell $r.Store) | $(Format-Cell $r.Outcome) | $($r.Rows) | $($r.New) | $($r.Present) | $($r.Conflicts) | $pred |")
        }
        [void]$titan.AppendLine('')
        if ($script:CountsAfter.Count -gt 0) {
            [void]$titan.AppendLine('| Count | Value |')
            [void]$titan.AppendLine('|---|---|')
            foreach ($k in @($script:CountsAfter.Keys)) { [void]$titan.AppendLine("| $(Format-Cell $k) | $(Format-Cell $script:CountsAfter[$k]) |") }
            [void]$titan.AppendLine('')
        }
    }
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("# $title, stage $Stage")
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine("**Verdict: $verdict.**")
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine("$NewVersion VM acceptance is complete only when every stage run (Install [+ AfterUi], Titan, Service, Final, Delta, ServiceUi, HealthNightOne and Release112 for 1.12.0) has no FAIL - including the bootstrap-log check after each setup run - and Sagar has ticked every UI line of VM-ACCEPTANCE-1.9.5-PLAN.md sections 4, 6 and 7 and of SERVICE-UI-1.10.0-ACCEPTANCE.md sections 4-8, and every line of the 1.12.0 checklist in this summary (shop result, bank import, update rehearsal, Tally in Educational mode).")
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('| Item | Value |')
    [void]$sb.AppendLine('|---|---|')
    [void]$sb.AppendLine("| Run | $RunId, $(Get-Date -Format 'yyyy-MM-dd HH:mm') on $env:COMPUTERNAME |")
    foreach ($k in @($script:RunInfo.Keys)) { [void]$sb.AppendLine("| $(Format-Cell $k) | $(Format-Cell $script:RunInfo[$k]) |") }
    [void]$sb.AppendLine("| Setup launcher | $SetupLauncher |")
    [void]$sb.AppendLine("| Import mode | $ImportMode |")
    [void]$sb.AppendLine("| Evidence in the VM | $($script:GuestRunDir) |")
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## Checks')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('| # | Area | Check | Expected | Actual | Result |')
    [void]$sb.AppendLine('|---|---|---|---|---|---|')
    foreach ($c in $script:Checks) { [void]$sb.AppendLine("| $($c.No) | $(Format-Cell $c.Area) | $(Format-Cell $c.Check) | $(Format-Cell $c.Expected) | $(Format-Cell $c.Actual) | **$($c.Result)** |") }
    [void]$sb.AppendLine('')
    if ($titan.Length -gt 0) { [void]$sb.Append($titan.ToString()) }
    if ($script:UiSteps.Count -gt 0) {
        [void]$sb.AppendLine('## Still to do in the VM console (Sagar)')
        [void]$sb.AppendLine('')
        foreach ($s in $script:UiSteps) { [void]$sb.AppendLine("- [ ] $s") }
        [void]$sb.AppendLine('')
    }
    if ($script:Notes.Count -gt 0) {
        [void]$sb.AppendLine('## Notes')
        [void]$sb.AppendLine('')
        foreach ($n in $script:Notes) { [void]$sb.AppendLine("- $n") }
        [void]$sb.AppendLine('')
    }
    [void]$sb.AppendLine('## Files here')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine("- checks-$Stage.csv: the table above. facts-*.json: the guest state at each point (no business data).")
    [void]$sb.AppendLine('- <setup label>\: setup-inno.log, bootstrap-*.log, setup-result.json. <import label>\: import-attempts.csv, automation-runs.csv, row-counts.csv, store-totals.csv (counts and a hash of the totals), report-pack-files.csv.')
    [void]$sb.AppendLine('- Rehearsal: user-changes.json (-UserChanges Script), operations\ and after-titan\ (task results, receipts with row counts), titan-<n>-<report>\ per held file, titan-expected-counts.csv.')
    [void]$sb.AppendLine('- 1.10.0: upgrade-row-counts.csv (Delta), staff-targets-0051-*.json, service-ui\ (sql-checks.json, job-stage-counts.csv, tat-medians.csv, claims-*.csv, view-grants.csv, timings.csv): counts, dates, day medians and names only.')
    [void]$sb.AppendLine("- run-$Stage-$RunId.log: the full console transcript.")
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## Roll back')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine("The script never deletes a checkpoint. To put the VM back: Restore-VMSnapshot -VMName $VmName -Name '<checkpoint name>' -Confirm:`$false (see runbook section 10). After an import the VM and the checkpoints taken after it hold shop data; deleting those checkpoints is Sagar's decision.")
    $name = 'SUMMARY.md'
    if ($Stage -ne 'Install') { $name = "SUMMARY-$($Stage.ToLowerInvariant()).md" }
    Write-Utf8 (Join-Path $script:Out $name) $sb.ToString()
    Save-Csv "checks-$Stage.csv" $script:Checks
    if ($Stage -ne 'Install') {
        $main = Join-Path $script:Out 'SUMMARY.md'
        if (Test-Path -LiteralPath $main) {
            [IO.File]::AppendAllText($main, "`r`n## Stage $Stage ($(Get-Date -Format 'yyyy-MM-dd HH:mm'))`r`n`r`n$verdict. See $name.`r`n`r`n" + $titan.ToString(), (New-Object System.Text.UTF8Encoding($false)))
        }
    }
    Write-Host ''
    Write-Host "VERDICT: $verdict" -ForegroundColor $(if ($fails -gt 0 -or $script:LockBusy) { 'Red' } elseif ($reviews -gt 0) { 'Yellow' } else { 'Green' })
    Write-Host "Summary: $(Join-Path $script:Out $name)"
}

# ---------------------------------------------------------------- main
$exitCode = 1
$lock = $null
$lockHeld = $false
$transcribing = $false
try {
    Assert-Host
    if ($Stage -eq 'Install') { $script:Out = New-OutputFolder } else { $script:Out = Resolve-OutputFolder }
    Start-Transcript -Path (Join-Path $script:Out "run-$Stage-$RunId.log") | Out-Null
    $transcribing = $true
    $script:RunInfo['Scenario'] = $Scenario
    if ($script:From) { $script:RunInfo['Upgrade from'] = $script:From }
    if ($Scenario -eq 'Rehearsal') { $script:RunInfo['User changes'] = $UserChanges; $script:RunInfo['Upgrade path'] = $(if ($IntermediateInstallerPath) { "1.9.2 -> $IntermediateVersion -> $NewVersion" } else { "1.9.2 -> $NewVersion (direct, as live)" }) }
    if ($Scenario -ne 'Upgrade' -and $PSBoundParameters.ContainsKey('UpgradeFrom')) { Write-Host "  -UpgradeFrom is ignored for -Scenario $Scenario." -ForegroundColor Yellow }
    $script:RunInfo['Output folder'] = $script:Out
    Write-Step "VM acceptance ${NewVersion}: $Scenario, stage $Stage. Evidence: $($script:Out)"

    Write-Host "  waiting for the heavy-job lock (up to $LockWaitMinutes min)"
    $lock = New-Object System.Threading.Mutex($false, 'Global\EtpHeavyJobLock')
    try { $lockHeld = $lock.WaitOne([TimeSpan]::FromMinutes($LockWaitMinutes)) }
    catch [System.Threading.AbandonedMutexException] { $lockHeld = $true }
    if (-not $lockHeld) {
        $script:LockBusy = $true
        throw "The heavy-job lock (Global\EtpHeavyJobLock) stayed busy for $LockWaitMinutes minutes: a build or test is running. Run the same command again later."
    }

    Invoke-Preflight
    $script:Credential = Get-Credential -UserName $GuestUser -Message "Password for $GuestUser in $VmName (typed by Sagar; never stored)"
    if (-not $script:Credential) { throw 'No password was entered.' }

    switch ($Stage) {
        'Install' { Invoke-InstallStage }
        'AfterUi' { Invoke-AfterUiStage }
        'Titan' { Invoke-TitanStage }
        'Service' { Invoke-ServiceStage }
        'Final' { Invoke-FinalStage }
        'Delta' { Invoke-DeltaStage }
        'ServiceUi' { Invoke-ServiceUiStage }
        'HealthNightOne' { Invoke-HealthNightOneStage }
        'Release112' { Invoke-Release112Stage }
    }
    $exitCode = 0
    if (@($script:Checks | Where-Object { $_.Result -eq 'FAIL' }).Count -gt 0) { $exitCode = 1 }
}
catch {
    Write-Host "STOPPED: $($_.Exception.Message)" -ForegroundColor Red
    if (-not $script:LockBusy) { Add-Check 'Run' 'The run finished' 'no error' $_.Exception.Message 'FAIL' }
    $exitCode = 1
    if ($script:LockBusy) { $exitCode = 75 }
}
finally {
    if ($script:Out) { try { Write-Summary } catch { Write-Host "Could not write the summary: $($_.Exception.Message)" -ForegroundColor Red } }
    if ($script:Session) { Remove-PSSession -Session $script:Session -ErrorAction SilentlyContinue }
    if ($script:TempDir -and (Test-Path -LiteralPath $script:TempDir)) { Remove-Item -LiteralPath $script:TempDir -Recurse -Force -ErrorAction SilentlyContinue }
    if ($lockHeld) { try { $lock.ReleaseMutex() } catch { } }
    if ($lock) { $lock.Dispose() }
    if ($transcribing) { Stop-Transcript | Out-Null }
}
exit $exitCode
