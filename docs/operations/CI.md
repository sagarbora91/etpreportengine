# Test gates and continuous integration

Two gates plus a nightly run. Status: 1.12 design; **CHECK AT INTEGRATION** against the A2 note for script names and times.

## 1. Everyday check: GitHub CI
Workflow `.github/workflows/ci.yml` (GitHub Actions, `windows-latest`). Runs on every push and pull request to `main` and `release/**`; a newer push cancels the older run.
1. Restore and Release build of `Etp.Reporting.slnx` (.NET 10).
2. PowerShell syntax check of every script under `scripts\`.
3. Unit tests, one project at a time: Domain, Import, Reporting, SqlServer, Desktop (and the Http unit project from 1.12).

`Etp.Reporting.SqlServer.IntegrationTests` never runs on CI, and GitHub never runs code on the PC that holds the books. `ETP_DIAGNOSTICS_DIRECTORY` points at a temp folder; no secrets are used.

## 2. Fast gate (everyday, on Workpc, from wave 2)
`Start-FastGate.ps1 -Worktree <path>`: no elevation; waits for the heavy-job lock but does not pause other lanes' builds. Release build, the unit projects in parallel with `--logger trx`, the PowerShell parse check and the OneDrive script tests. Target: 6 minutes or less. Tests that open SQL are tagged `Category=Sql` and run only in the release gate. Lanes run this before writing their note.

## 3. Release gate
`Start-Gate.ps1 -Version <x.y.z>`: elevated; all six test projects one at a time, plus script tests. Run once per release candidate, then the VM install check. Never filter its output: keep the whole log or the trx.

## 4. Nightly integration (Workpc, 00:30)
- Runs the integration suite on the newest `release/*` in its own worktree, as Sagar, under the heavy-job lock.
- Uses a **separate SQL Server Express instance `.\ETPTEST`**, never the live instance.
- Results go to `E:\ETP\Reference\Nightly\<date>\` (whole trx and log, never filtered) with `SUMMARY.txt`: pass or fail, and the names of any retried tests.
- More than two automatic retries in one run fails it.

### Setting up `.\ETPTEST` (once, elevated, Sagar)
1. Install SQL Server Express as a **named instance `ETPTEST`** (not the default instance, not the live one). Put it on C:, not E:.
2. Enable Shared Memory; enable TCP only if the transport experiments need it.
3. Give the nightly account (Sagar) sysadmin on this instance only. Check with `sqlcmd -S .\ETPTEST -C -Q "select @@servername"`.
4. Run `Register-NightlyIntegration.ps1` (elevated). Confirm the task with `schtasks /query`.
5. Next morning read `SUMMARY.txt`. On a failure read the whole log, not a filtered view.

## Reading a failure
- CI: open the failed step. "Unit tests" names the failing project and tests. Download the `test-results-<run id>` artifact for the `.trx` files.
- Reproduce locally: `dotnet test tests-dotnet\<project>\<project>.csproj -c Release`.
- A Shared Memory "transport-level" error at connection time is a known flake (about 1 run in 4 before 1.12). Look for `ETP-FLAKE-RETRY` in `SUMMARY.txt`.
- A failure only on CI in Desktop tests may be a hosted-runner difference, not a code bug.
