# Recovery track R1–R4 — verification report

| | |
|---|---|
| Branch | `recovery/opus-r1-r4` |
| Worktree | `C:\Codex\Reporting Manger\opus-recovery` |
| Base | `phase-5/secondary-modules` |
| Database used | `EtpOpusRecovery` on `.\SQLEXPRESS` — disposable, created from the committed migrations |
| Live database | `EtpReporting` was **not** opened, read or written at any point |
| Date | 17 September 2026 |

The track named four items and gave each a written acceptance test (A-R1…A-R4), plus a
gate (A-R0). Three of the four items turned out to be already built by Codex, so most of
this work was **verification** rather than implementation — and the verification found
five real defects, each listed under the item that owns it.

---

## Verdict

| | Item | Verdict |
|---|---|---|
| **A-R0** | Gate: Release build and tests green, regression tests exist, no migration added, rebases on `phase-5/secondary-modules` | **Met** |
| **A-R1** | Durable import history and problems survive a restart | **Met** — one defect found and fixed |
| **A-R2** | Advanced report filters agree on screen, in Excel and in PDF | **Met** — already correct, verified |
| **A-R3** | Retry re-runs only the failed file | **Met for the button path**, with one honest limit — one defect found and fixed |
| **A-R4** | Database and recovery health visible to the owner | **Met after fixing the screen** — three defects found and fixed, one of which made the block effectively invisible |

---

## A-R0 — the gate

`dotnet build Etp.Reporting.slnx -c Release -m:1 -nodeReuse:false`, then
`dotnet test Etp.Reporting.slnx -c Release --no-build -m:1 -nodeReuse:false`:

| Project | Result |
|---|---|
| Desktop | 363 passed, 2 skipped |
| Domain | 12 passed |
| Import | 110 passed |
| Reporting | 63 passed |
| SqlServer.IntegrationTests | 88 passed, 1 skipped (5 m 6 s, against real SQL Server) |
| SqlServer | 239 passed |
| **Total** | **875 passed, 3 skipped, 0 failed** — `BUILD=0`, `TEST=0` |

Test coverage was extended during this pass (see A-R4), and the gate was re-run after the A-R4
screen fix and again after the rebase. The final numbers are at the end of this report.

- **No migration was added or edited.** `git diff --name-only phase-5/secondary-modules...HEAD`
  returns nothing under `database/migrations/`. The one new piece of data this work needed —
  last successful import — is read by a **separate read-only query** beside
  `dbo.load_database_operational_health` rather than by changing the committed procedure,
  because the migration runner is checksum fail-closed and a committed migration must stay
  byte-identical.
- The three skipped tests are the pre-existing live-capture and UI-smoke tests, skipped when
  their fixture database is not configured. They were skipped before this branch as well;
  nothing was disabled to make this gate pass.

---

## A-R1 — durable import history and problems

Evidence: `tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/DurableImportProblemsTests.cs`.

The restart is a **real process restart**: the test launches `Etp.Reporting.HistoryRestartHost`
as a separate process, waits for it to exit, and then reads the problems back in a fresh host.
Nothing is carried in memory across the boundary.

- `Assert.Empty(firstProblems)` — a clean first import produces no problems.
- `Assert.Equal(3, secondProblems.Count)` with `Assert.All(... Contains("Duplicate"))` —
  re-importing the same folder yields Duplicate rows and the earlier rows remain.
- `Assert.Equal(3, afterRestart.Count)` and `Assert.Contains("3 problems", message)` — the
  count survives the restart and the on-screen sentence agrees with the list beneath it.
- No leakage: `Assert.DoesNotContain("\\", problem.File)`, `Assert.DoesNotContain(":", problem.File)`,
  `Assert.DoesNotContain(scratch, problem.Detail)` — no path and no scratch directory in either column.
- `Viewer_may_read_the_outcomes_problems_are_built_from_and_may_not_change_them` — a Viewer can
  read the outcomes and is refused the write with SQL error **229**, at the database rather than
  only in the user interface.

### Defect 1 — a successful retry looked like it did nothing

`import_attempts` is append-only, so the failed attempt outlives the retry that fixed it.
The Problems tab called `ImportProblems.From(...)`, which classifies each row on its own, so a
file that failed and was then imported cleanly **stayed on the problems list permanently**.
An owner pressing "Retry failed", watching the file import successfully, and then seeing the
same problem still listed would reasonably conclude the button was broken.

Fixed with `ImportProblems.FromHistory(...)`, which drops a failure once a later clean import of
the same file exists, and by changing the Problems dispatch in `Shell/TaskNavigator.cs` to pass
`(RecordedUtc, Result)` pairs so the comparison has a timestamp to work with.

This is the one item also proven **live, through the running application** — see the screenshots.

---

## A-R2 — advanced report filters

Evidence: `tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/ReportFilterClosureTests.cs`.
Already built by Codex; verified, not changed.

- `Assert.Equal(767m, all)` and `Assert.Equal(531m, segment)` — unfiltered and brand-segment
  totals taken from SQL over the fixture.
- On screen: `Assert.Contains("Sales incl. GST 531.00", ... "ReportTaskStatus")`; the row total agrees.
- Excel: the scope-line cell contains `Brand segments: SEG-X`, and
  `Assert.DoesNotContain(... cell.InnerText == "BRAND-B")` — the excluded brand is genuinely absent
  from the exported file, not merely hidden on screen.
- PDF: `Assert.Contains("Applied scope:", pdfText)`, `Brand segments: SEG-X`, `531.00`, and
  `Assert.DoesNotContain("BRAND-B", pdfText)`.
- Clear: `Brand segments: All` and the unfiltered 767 return.
- A Viewer write is refused with SQL **229**.
- The orphaned `report-filters` dispatch the brief asked about is **gone** — no match in
  `Shell/TaskNavigator.cs`.

---

## A-R3 — retry failed

Evidence: `tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/ImportRetryClosureTests.cs`.
Already built by Codex; verified, with one addition.

- `Assert.Equal("Failed", ... "R022_revenue.xlsx" ... .Status)` — the corrupted workbook fails.
- `retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))` — the retry is driven through the
  real WPF button, not by calling the method behind it.
- `Assert.Equal(new[] { bad }, reader.Reads.ToArray())` — **only the failed file was re-read**.
  This is the assertion that actually proves the requirement; the rest is supporting evidence.
- The successful file's result object is unchanged after the retry.
- `Assert.Equal(access.CanImport, retry.IsEnabled)` — a Viewer sees the button disabled.
- Ctrl+R still resolves to the same command.

### Defect 2 — a Viewer saw a dead button and no reason for it

The button was correctly disabled for a Viewer, but nothing on screen explained why.
Added an inline `"Owner or store manager can retry"` notice beside the button, driven by a new
`ImportWorkspaceView.CanRetryByRole`. The distinction matters: retry is also disabled while an
import is running and when nothing has failed, and **those two are obvious from the screen** —
only the role case needs a sentence, so only the role case shows one.

### The honest limit

The brief says *"press Retry on the touch screen"*. What is proven here is the **button path**,
driven by an automated WPF click. That is not a finger on a 1366×768 touch panel. The same limit
applied to A3.8 in the Phase 3 audit and is recorded here for the same reason: an automated
`Button.ClickEvent` cannot detect a target too small to hit, a control hidden under the on-screen
keyboard, or a gesture the shell swallows. **A-R3 still needs a physical touch pass.**

---

## A-R4 — database and recovery health

Evidence: `tests-dotnet/Etp.Reporting.Desktop.Tests/DatabaseRecoveryPresentationTests.cs` and
`tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/DatabaseRecoveryHealthTests.cs`.

The rule the block enforces: **evidence that is absent reads `Missing`, evidence that is too old
reads `Stale (n days)`, and neither may ever read as healthy** — because an owner who sees
"Healthy" beside a backup that was never taken has been told the opposite of the truth.

- `Absent_backup_and_drill_read_Missing_and_never_Healthy` — backup, drill and last import all read
  `Missing`, and no line claims health.
- `Old_evidence_reads_Stale_with_the_age_in_days` — `Stale (4 days)`, `Stale (100 days)`, and
  `Stale (1 day)` in the singular.
- `Current_evidence_shows_its_time_and_fingerprint` — a present receipt reads neither `Missing` nor
  `Stale`, and its detail contains `Fingerprint cccccccccccccccc`.
- `Unreadable_health_says_so_rather_than_showing_an_empty_block`.
- Integration: `A_failed_import_with_no_receipts_reads_as_failed_and_missing_never_healthy`; then
  recording real receipts and reloading through a **fresh repository** returns the timestamps and
  both fingerprints.
- `A_store_manager_cannot_record_evidence_that_would_make_the_block_look_healthy` — a Store Manager
  attempting to write that evidence is refused with SQL **229**. This is the strong form of the
  brief's "Store Manager cannot open the page": the lockout is enforced at the database, not only
  by hiding a menu entry.

### Defect 3 — the block was on the screen but effectively invisible

**Found by the screenshots, not by any test.** Navigating to Settings → Database → Database health
as an Owner showed an "Integration health" table and a second tab labelled **"Guidance"** — and
nothing else. The Database and recovery block was inside that "Guidance" tab.

The cause: `TaskBodyLayout` gives a named tab only to a `DataGrid` that is a **direct child** of the
task body. The recovery grid was nested inside a `Border`, so it never became a tab and fell into
the unnamed catch-all, under a label that gives an owner no reason to click it. Every unit and
integration test passed throughout, because they all exercise `DatabaseRecoveryPresentation`
directly and never build the screen.

Fixed by flattening the block — heading, status line, grid and the Support package button are now
direct children of the panel — and **appending it at the end of the view** so that every index below
it keeps the position it always had. The health task now selects `{16,17,13,14}` for the body with
`{18}` as its action, which renders as: the recovery status and administration status docked at the
top, a **"Database and recovery"** tab beside **"Integration health"**, and Support package in the
toolbar.

### Defect 4 — the block could announce health while a Critical row sat beneath it

`AdministrationWorkspaceView` counted only rows whose status was `Missing` or `Stale`. Warning rows
carry their severity in `Status` (`Critical`, `Warning`), so a block containing a Critical warning
could still print *"Backup and recovery evidence is present and current."* The count now also
includes `line.Item == "Warning"`.

### Defect 5 — the support package skipped the role check

Every other handler in that view re-checks the role rather than trusting the screen to be
Owner-only. `SupportPackage_Click` was the exception. Added `RequireOwnerAccess()`.

### Tests added during this pass

- `TaskNavigationTests` asserted the owner gate for `connection`, `users` and `recovery` — a sample
  of three. R4 put the database and recovery block behind that same gate, so the test now names
  **every** owner-only Settings task, `health` included. Without it, A-R4's final clause was
  enforced by code that no test mentioned.
- The administration view's child positions are now pinned by name, including the invariant behind
  the tab (`children[17]` is a `DataGrid` whose automation name is "Database and recovery"). The
  index coupling between `TaskNavigator` and the workspace panels has now broken twice — once in a
  build that reached the shop — and nothing caught either time. It fails in the test suite now.

  These assertions were folded into the existing administration test rather than added as a new
  test class. A separate class meant a second STA thread loading XAML, which raced
  `Phase3ShellTests` inside `System.IO.Packaging` and produced a `NullReferenceException` in
  `Application.LoadComponent`. That failure was infrastructure, not product, but a flaky gate is
  worse than no gate, so the assertions run on a view the suite already builds.

---

## Fixture data used for the live screenshots

The integration suite creates and drops its own database per test, so `EtpOpusRecovery` was empty.
Synthetic rows were inserted so the screens had something to render:

- Six `import_attempts` rows: one `Failed`, one `Conflict` (6 conflicting rows), one
  `Unknown layout`, one `Duplicate content`, and a **pair** — `R008_CashBook.xlsx` failing at
  T−120 min and importing cleanly at T−90 min.
- One `import_batches` row with `status='Failed'` inside 24 hours, because
  `dbo.load_database_operational_health` counts failed **batches**, not failed attempts. That is
  what "one failed import" means to the shipped procedure.

No backup or drill receipt was recorded, which is the other half of A-R4's precondition:
`FAILED_BATCHES_24H=1`, `BACKUP_RECEIPTS=0`.

This data is synthetic and is named as such here. It proves layout, classification and suppression.
It proves nothing about real ETP exports.

---

## Screenshots

`docs/audit/recovery-shots/`, captured from the Release build of this branch **after the rebase**,
against `EtpOpusRecovery`.

The window title in every image reads **"ETP Reporting Engine 1.8.8 (3074dbb)"**, so each
screenshot states which commit produced it and cannot be mistaken for an earlier build.

`3074dbb` is not the head commit, and it cannot be: a screenshot committed in commit X can never
show X's own hash. It is the commit the photographed build came from — this branch's code plus an
earlier draft of this report. Between it and the head, `git diff 3074dbb..HEAD -- src tests-dotnet
database` is **empty**: the only changes are this report's wording and the images themselves. The
photographed binary and the pushed code are the same code.

Each screenshot has a `.tree.txt` beside it holding the on-screen accessibility tree at the
moment of capture, and `capture-log.txt` records the **measured** window and client size and the
window DPI rather than the size that was requested. The tree is the evidence; the image
illustrates it. That distinction earned its keep: the first three capture attempts produced
four plausible-looking PNGs of the **wrong screens**, and the trees are what exposed it.

| File | Screen | Window | Client | DPI |
|---|---|---|---|---|
| `import-problems-1366x768.png` | Import → Problems | 1366×768 | 1350×729 | 97 |
| `import-problems-816x480.png` | Import → Problems | 816×480 | 800×441 | 97 |
| `database-health-1366x768.png` | Settings → Database → Database health | 1366×768 | 1350×729 | 97 |
| `database-health-816x480.png` | Settings → Database → Database health | 816×480 | 800×441 | 97 |

### What Import → Problems shows

**"4 problems. Select a status to filter."** and four rows — `R045_CustomerVisits.xlsx`
(Duplicate content, Helios), `R031_BrandMix.xlsx` (Unknown layout, Helios),
`R014_StockOnHand.xlsx` (Conflict, Titan World, 6 conflicts), `R022_RevenueDetails.xlsx`
(Failed, Titan World).

`R008_CashBook.xlsx` — which failed and then imported cleanly — is **absent from both sizes**.
That is defect 1 fixed, proven through the running application against real SQL rather than only
in a unit test. Without the fix it would be a fifth row that no retry could ever clear.

Both sizes keep File, Status, Store, Period and Detail readable; at 816×480 the grid scrolls
horizontally for Detail, which is expected at that width.

Note for anyone reading the image: "Retry failed" is greyed out because **nothing failed in the
current session's batch**, not because of the role. These rows were read back from the database,
which is the point of R1. The role case is the one that shows the
`"Owner or store manager can retry"` sentence, and this session ran as Owner.

### What Database health shows

The **"Database and recovery"** tab is now first and selected by default, beside "Integration
health". Above it: the Support package button in the toolbar, and
**"6 item(s) need attention. Missing or stale evidence is never reported as healthy."**

| Item | Status | Detail |
|---|---|---|
| Database size | 16 MB | No edition size limit |
| Backup folder free space | 203.9 GB | Warn below 20 GB, critical below 5 GB |
| Last successful import | **Missing** | No completed import has been recorded. |
| Last verified backup | **Missing** | No verified backup receipt has been recorded. |
| Last verified recovery drill | **Missing** | No verified recovery drill has been recorded. |
| Failed imports, last 24 hours | **1** | Open Import, then Problems, to see them. |
| Warning | **Critical** | No verified encrypted database backup is recorded. |
| Warning | Warning | No successful receipt-verified recovery drill is recorded. |
| Warning | Warning | One or more imports failed in the last 24 hours. |

This is A-R4's precondition rendered exactly as the brief specified: one failed import shown,
backup `Missing`, drill `Missing`, and nothing claiming health.

The count of **6** is defect 4 fixed. Counting only `Missing` and `Stale` would have printed
**3**, and the `Critical` row would have sat on screen beneath a sentence that did not count it.

At 816×480 the header wraps to two rows and the grid scrolls: the first three rows are visible
and the owner scrolls for the rest. Everything above the grid — button, count sentence and all
three tabs — stays on screen.

---

## Final gate, after the A-R4 screen fix and after the rebase

The branch was rebased onto `phase-5/secondary-modules` at `f41de93` and the full gate was run
again on the rebased tree. A clean rebase says nothing about behaviour, and index coupling is
this branch's whole subject, so the gate is what decides it.

| Project | Before rebase | After rebase |
|---|---|---|
| Desktop | 363 passed, 2 skipped | 406 passed, 2 skipped |
| Domain | 12 passed | 12 passed |
| Import | 110 passed | 110 passed |
| Reporting | 63 passed | 63 passed |
| SqlServer.IntegrationTests | 88 passed, 1 skipped | 89 passed, 1 skipped |
| SqlServer | 239 passed | 239 passed |
| **Total** | **875 passed, 3 skipped, 0 failed** | **919 passed, 3 skipped, 0 failed** |

`BUILD=0`, `TEST=0` in both runs. The increase is Codex's phase-5 tests arriving with the rebase,
not new tests of mine.

The rebase produced **no conflicts**: Codex's seven new commits touch none of the files this
branch changes. The administration view's child order survived intact, which the pinning test
asserts rather than my having eyeballed it.

---

## What this report does not claim

1. **A-R3 has not been tested by touch.** The retry path is proven by an automated WPF button
   event. A finger on the shop's 1366×768 panel is still outstanding, exactly as A3.8 was.
2. **The Viewer notice is not in a screenshot.** This session ran as Owner, so the
   `"Owner or store manager can retry"` sentence was correctly collapsed. It is covered by test,
   not by image.
3. **The fixture data is synthetic.** It proves classification, suppression and layout. It proves
   nothing about real ETP workbooks.
4. **"Last successful import" and "Failed imports" read different tables from Problems.**
   The health procedure counts `import_batches`; the Problems tab reads `import_attempts`. A
   history made only of retried attempts can therefore show problems while health reports zero
   failed imports. Fixing this means editing `dbo.load_database_operational_health`, which is a
   committed migration — out of bounds for this branch. **Recorded, not fixed.**
5. **"Last successful import" has no store predicate.** It reports the newest completed batch
   across both stores. Same reason as above.
6. **`docs/OPERATIONS.md` and `docs/INSTALL.md` still describe the pre-D9 behaviour** and
   contradict the code on unsigned installs and unencrypted Express backups. That belongs to the
   Phase 4 branch, not this one.
7. **The screenshots were taken on this development machine, not the shop PC.** The window was
   sized to the shop's resolution; it is not the shop's hardware, GPU or DPI setting.
