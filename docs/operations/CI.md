# Continuous integration

Workflow: `.github/workflows/ci.yml` (GitHub Actions, `windows-latest`).

## When it runs
On every push and pull request to `main` and `release/**`. A newer push to the same branch cancels the older run.

## What it runs
1. Restore and Release build of `Etp.Reporting.slnx` (.NET 10).
2. PowerShell syntax check of every script under `scripts\`.
3. Unit tests, one project at a time: Domain, Import, Reporting, SqlServer, Desktop.
   `Etp.Reporting.SqlServer.IntegrationTests` is never run on CI (it needs a local elevated SQL Server); run it in the release gate on the work PC.

`ETP_DIAGNOSTICS_DIRECTORY` points at a temp folder so tests never touch a real diagnostics log. No secrets are used.

## Reading a failure
- Open the failed run, then the step: "Build" shows compiler errors; "Unit tests" names the failing project in its last line ("Failed test projects: ...") and the failing test names above it.
- Download the `test-results-<run id>` artifact for the `.trx` files (open in Visual Studio, or search for `outcome="Failed"`).
- A test stopped by the 10 minute hang limit is named in the log by the blame logger.
- Reproduce locally with `dotnet test tests-dotnet\<project>\<project>.csproj -c Release`.
- A failure only on CI in Desktop tests (scripts, ACLs, fonts, UI threads) may be a hosted-runner difference, not a code bug; see the integrator note before changing tests.
