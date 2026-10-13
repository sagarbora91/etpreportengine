# Test gates and continuous integration

Two gates plus a nightly run. Status: checked against the A2 scripts at the 1.12.0 wave-1 integration (13 Oct 2026). Wave 2 adds new SQL integration test files (one per lane); the integrator runs `Apply-SqlFact.ps1` once after the merges so they carry `[SqlFact]`, and they run in the release gate and the nightly run, never on CI. Wave-2 integration fast gate (13 Oct 2026, unit projects only): 5,566 tests, 0 failed apart from the permissions guard that waits for the elevated gate to regenerate `ExpectedRolePermissions.json`; the elevated gate (`Chain-Gate-Then-Installer-1.12.0-w2.ps1`) had not run yet. The scripts live in `E:\ETP\Migration 2026-10-04\` on Workpc, not in the repository.

## 1. Everyday check: GitHub CI
Workflow `.github/workflows/ci.yml` (GitHub Actions, `windows-latest`). Runs on every push and pull request to `main` and `release/**`; a newer push cancels the older run.
1. Restore and Release build of `Etp.Reporting.slnx` (.NET 10).
2. PowerShell syntax check of every script under `scripts\`.
3. Unit tests, one project at a time, with `--filter "Category!=Sql"`: Domain, Import, Reporting, SqlServer, Desktop and Infrastructure.Http.

`Etp.Reporting.SqlServer.IntegrationTests` never runs on CI, and GitHub never runs code on the PC that holds the books. `ETP_DIAGNOSTICS_DIRECTORY` points at a temp folder; no secrets are used.

## 2. Fast gate (everyday, on Workpc)
`Start-FastGate.ps1 -Worktree <path> [-Label <name>]`: no elevation; waits for the heavy-job lock but does not pause other lanes' builds. Release build, the unit projects in parallel with `--logger trx`, the PowerShell parse check and the OneDrive script tests (run in Windows PowerShell 5.1 with PowerShell 7's module path removed). Results and the last line `gate: fast <label> PASSED|FAILED ...` go to `E:\ETP\Reference\FastGate\<label>-<time>\`. The 1.12.0 integration runs took about 2-3 minutes. Tests that open SQL are tagged `Category=Sql` and run only in the release gate. Lanes run this before writing their note.

## 3. Release gate
`Start-Gate.ps1 -Version <x.y.z> -Worktree <path> [-CleanLeftovers]`: elevated (Sagar approves UAC); pauses lane builds, takes the heavy-job lock, takes a fresh verified backup, runs the pre-checks, then all seven test projects one at a time with a timeout per step, and always writes `gate-<version>-exit.txt` with a `gate: <version> PASSED|FAILED|STOPPED|TIMED-OUT ...` line. More than two `ETP-FLAKE-RETRY` lines fail it. Run once per release candidate (a per-release `Chain-Gate-Then-Installer-<version>.ps1` wraps it), then the VM install check. Never filter its output: keep the whole log or the trx.

## 4. Nightly integration (Workpc, 00:30)
- Runs the integration suite on the newest `release/*` in its own worktree, as Sagar, under the heavy-job lock.
- Uses a **separate SQL Server Express instance `.\ETPTEST`**, never the live instance.
- Results go to `E:\ETP\Reference\Nightly\<date>\` (whole trx and log, never filtered) with `SUMMARY.txt`: pass or fail, and the names of any retried tests.
- More than two automatic retries in one run fails it.

### Setting up `.\ETPTEST` (once, elevated, Sagar; not done yet on 13 Oct 2026)
1. Run `Install-EtpTestInstance.ps1 -MediaPath <SQL media>`: it installs SQL Server Express as a **named instance `ETPTEST`** (not the default instance, not the live one) on C:, not E:, then apply the latest CU.
2. Enable Shared Memory; enable TCP only if the transport experiments need it.
3. Give the nightly account (Sagar) sysadmin on this instance only. Check with `sqlcmd -S .\ETPTEST -C -Q "select @@servername"`.
4. Run `Register-NightlyIntegration.ps1` (elevated). Confirm the task with `schtasks /query`.
5. Next morning read `SUMMARY.txt`. On a failure read the whole log, not a filtered view.

## Reading a failure
- CI: open the failed step. "Unit tests" names the failing project and tests. Download the `test-results-<run id>` artifact for the `.trx` files.
- Reproduce locally: `dotnet test tests-dotnet\<project>\<project>.csproj -c Release`.
- A Shared Memory "transport-level" error at connection time is a known flake (about 1 run in 4 before 1.12). Look for `ETP-FLAKE-RETRY` in `SUMMARY.txt`.
- A failure only on CI in Desktop tests may be a hosted-runner difference, not a code bug.
