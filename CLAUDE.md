# CLAUDE.md - standing instructions for the ETP Reporting Engine

Read this first in every session. It holds no secrets. Current state of the work is NOT here: read the top block of
`E:\ETP\NEW-PC-HANDOFF.md` and the memory index `C:\Users\Sagar\.claude\projects\E--ETP\memory\MEMORY.md` before acting.

## 1. What ETP is

A Windows desktop reporting app for Saagar Traders (Titan World and Helios stores, Latur; Owner: Sagar). It imports the
daily ETP point-of-sale export workbooks (and consolidated workbooks), stores them in SQL Server Express (`.\SQLEXPRESS`,
live database `EtpReporting`), and produces the Daily Sales Report, sales, stock, tender, service-centre and Tally/
collections reports with Excel and PDF export, plus backups, health checks and in-app updates. .NET 10 WPF, no DI
container (`DesktopCompositionRoot` is hand-wired), `TreatWarningsAsErrors`, deterministic builds. Users: Owner, Store
Manager, Viewer. Runs on Workpc (office) and, later, three shop PCs on one LAN with OneDrive carrying files.

## 2. Where things are

| What | Where |
|---|---|
| Repository (GitHub `sagarbora91/etpreportengine`, private) | main clone `E:\ETP\Code\SaagarCC-V6-ETP-Source-and-Report-Engine-605002f`; lane worktrees `E:\ETP\Code\Worktrees\<lane>` |
| Your worktree | the one you were started in. Never `cd` into another worktree, never edit another lane's files |
| Release/gate/VM scripts (NOT in the repo) | `E:\ETP\Migration 2026-10-04\` (`Start-FastGate.ps1`, `Start-Gate.ps1`, `Chain-Gate-Then-Installer-<ver>.ps1`, `Build-Installer.ps1`, `Publish-Release.ps1`, `Run-VM-<ver>-Sequence.ps1`) |
| Build lock | `E:\ETP\Code\Worktrees\_tools\Invoke-Serialized.ps1` (+ `PAUSE-LANE-BUILDS`, `heavy-jobs.log`) |
| Lane notes, plans, reviews | `E:\ETP\Reference\Work in progress <date>\` |
| Gate results | `E:\ETP\Reference\FastGate\`, `E:\ETP\Reference\Gate-<ver>-<time>\` |
| Installers | `E:\ETP\Installers\ETP Reporting Engine <ver>\` (copied to the USB kit and OneDrive feed - never put symbols there) |
| Tools | `E:\Tools` (Inno Setup 6, PowerShell 7 at `E:\Tools\PowerShell\7\pwsh.exe`, NuGet cache) |
| Helper scripts for driving the app | `E:\ETP\.claude\tools\` (shot/ui/dlg/walk, README inside) |
| Folders named `(CONTAINS BUSINESS DATA)` | customer data. Read only when the task needs it; never copy into the repo, notes or chat |
| `E:\` root | not writable unelevated; create folders under `E:\ETP\` |

Pointers: `README.md`, `ARCHITECTURE.md`, `CHANGELOG.md` (decisions are cited there by number), `docs/operations/CI.md`
(gates), `docs/OPERATIONS.md`, `database/objects/README.md`, `docs/security/` (`SECURITY.md`, `RECOVERY.md`,
`RELEASE-PROTECTION.md`, `CODE-SIGNING.md`, `VERIFICATION.md`). `docs/audit/CLAUDE-HANDOFF.md` is September history only.

## 3. Solution layout (`Etp.Reporting.slnx`)

- `src/` - `Etp.Reporting.Domain`, `.Application` (incl. `Updates/`), `.Import` (workbook profiles, e.g.
  `Profiles/RetailSalesProfiles.cs`), `.Reporting`, `.Infrastructure.SqlServer` (bootstrapper, `SchemaObjects.cs`,
  `HealthCheck/ExpectedRolePermissions.json`), `.Infrastructure.Http`, `.Desktop` (WPF, the exe).
- `tests-dotnet/` - `*.Domain.Tests`, `*.Import.Tests`, `*.Reporting.Tests`, `*.SqlServer.Tests`, `*.Desktop.Tests`,
  `*.Infrastructure.Http.Tests` (unit), `Etp.Reporting.SqlServer.IntegrationTests` (real SQL), `HistoryRestartHost`,
  `TestSupport/` (`DiagnosticsIsolation.cs`, `SqlFlakeRetry.cs` = `[SqlFact]`), `fixtures/etp-sample/` (artificial data).
- `tools/` - ImportAudit, DsrSmoke, ExportSmoke, FunctionAudit, LiveSmoke, PerformanceSmoke, Smoke, UiSmoke.
- `database/migrations/` (0001-0069, append-only) and `database/objects/` (one file per view/proc/function).
- `scripts/` - release build, installer, signing, backup/restore/drill, health, `onedrive/`, `release/` (signing key).
- `installer/EtpReportingEngine.iss` (Inno Setup 6, `E:\Tools\Inno Setup 6\ISCC.exe`).
- `.github/workflows/` - `ci.yml` (build + unit tests, no SQL, no secrets), `vulnerable-packages.yml` (weekly).

## 4. Build and test

All heavy commands (build, test, sqlcmd against SQL Express) go through the build lock, one at a time on the PC:

```powershell
pwsh -NoProfile -File E:\ETP\Code\Worktrees\_tools\Invoke-Serialized.ps1 -Label <lane> -WorkingDirectory <worktree> `
  -Command "dotnet build Etp.Reporting.slnx -c Release -m:1 -nodeReuse:false"
```

Exit 75 = lock busy or `PAUSE-LANE-BUILDS` present (a gate or VM job has priority): wait and repeat the same call. Never
delete the pause file yourself.

- **Unit tests:** `dotnet test tests-dotnet\<Project>\<Project>.csproj -c Release --filter "Category!=Sql"` (through the lock).
- **Fast gate** (everyday, unelevated, no SQL, ~3-6 min; lanes run it before writing their note, integrator before a
  merge): `& 'E:\ETP\Migration 2026-10-04\Start-FastGate.ps1' -Worktree <worktree> [-Label <name>]`. Last line:
  `gate: fast <label> PASSED|FAILED`; results under `E:\ETP\Reference\FastGate\`.
- **Elevated release gate** (per release candidate; Sagar approves UAC, the ETP app closed):
  `Start-Gate.ps1 -Version x.y.z -Worktree <path>` or the per-release `Chain-Gate-Then-Installer-<ver>.ps1`. It pauses
  lane builds, takes a fresh verified backup, then runs all test projects one at a time. **Integration tests need it**
  because they CREATE and DROP scratch databases (`EtpPhase0Test_*`, `EtpRecovery_*`) and install master procedures, which
  needs sysadmin - on Workpc only `BUILTIN\Administrators`, i.e. an elevated process, has it. Writes
  `gate-<ver>-exit.txt`. SQL tests carry `[SqlFact]` / `[Trait("Category","Sql")]` and run only here (and nightly on
  `.\ETPTEST`), never on CI. More than 2 `ETP-FLAKE-RETRY` lines fail it.
- **Never filter gate or test output** (no `| grep Passed`): keep the whole log or `--logger trx`. A filtered run once lost
  the only record of which test failed. The Shared Memory "transport-level error" flake is a known ~1-in-4 issue; never
  claim a fix for it from fewer than six runs.
- Test runs set `ETP_DIAGNOSTICS_DIRECTORY` (via `DiagnosticsIsolation.cs`) so they never write to the Owner's log; any new
  test assembly reaching `DesktopDiagnostics` must link it. `ETP_TEST_SQL_CONNECTION` picks another SQL instance.
- Running the app: `src\Etp.Reporting.Desktop\bin\Release\net10.0-windows\Etp.Reporting.Desktop.exe`.
  `%LOCALAPPDATA%\EtpReporting\settings.json` decides the database it opens - check it first. Point at a scratch DB with
  `--connection-string`, passed via `ProcessStartInfo.ArgumentList` (never `Start-Process -ArgumentList`, it splits).
  Scratch DB names: `EtpAccept_*`, `EtpPhase1Test_*`, `EtpReportingHelios`.
- Lanes told "code-only" do not build or run tests at all; the integrator does.

## 5. Worktree and lane conventions

- One lane = one branch = one worktree = an explicit file list. Touch nothing outside it. Integrator-only files:
  `Directory.Build.props`, every `*.csproj` / `*.slnx`, `CHANGELOG.md`, `README.md`, `ExpectedRolePermissions.json`,
  `EXCLUDED.md`. Migration numbers and THROW numbers are allotted by the wave plan; never pick your own.
- Commit small WIP checkpoints after every meaningful step (agents get cut off; only disk survives). Stage files by name
  (`git add <path>`), check `git status` first - another session may share the worktree. Commit messages end with:
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` (or your model's line).
- Never `git stash` bare (the stash is shared by all worktrees); use a WIP commit.
- Finish by writing `E:\ETP\Reference\Work in progress <date>\<wave>-<LANE>-NOTE.md`: what changed, tests, open points.
- Resuming a cut-off lane: read `git status`/`git diff` and your note, keep and finish the work.
- Never push `main` (Sagar's). Pushing `release/*` and tags happens only when the handoff/integrator brief says so.
- Multi-lane waves that span C# and SQL: a read-only review (Opus/Fable) before the gate, and at least one
  **cross-layer test per C#->SQL contract** (realistic values through C# into the real procedure/CHECK constraint).
  Per-layer tests passing proves nothing about the contract (1.11.0: spaces rejected by `diag` CHECKs).
- No more Codex: read "Codex" in older documents as "the implementer".

## 6. Database rules

- `database/migrations/NNNN_*.sql` are **append-only and checksummed whole** (comments included) in
  `dbo.schema_migrations`. Never edit a migration that any non-throwaway database (VM checkpoint, staging, live) has run;
  fix it in a new migration. A checksum mismatch stops setup on the shop PC.
- From 1.12 (lane A3), every view, procedure and function lives in `database/objects/<layer>-<schema>.<name>.sql`
  (`CREATE OR ALTER` + a `-- >>> GRANTS` block), applied by `SchemaObjectRunner` after the migrations. Never create or
  alter a view/proc/function in migration 0062 or later; a migration must not call a procedure that has a file. Rules
  and layering: `database/objects/README.md`.
- `ExpectedRolePermissions.json` must equal the GRANT/DENY rows; the integrator regenerates it at the elevated gate (one
  expected fast-gate failure until then). Lanes never edit it.
- **SQL `AND` does not short-circuit.** `IF OBJECT_ID(...) IS NOT NULL AND EXISTS(SELECT ... FROM thatTable)` fails on a
  fresh DB. Use a nested `IF`. Guard object reads with `OBJECT_ID(N'dbo.x', N'U')`.
- `LIKE` character classes: put the hyphen first (`[-a-z0-9]`), or it becomes a range.
- Tests that `EXECUTE AS` use a **non-pooled** connection or `REVERT` in the same batch/finally (pooled return -> SQL
  18059 in later tests).
- sqlcmd: use `-w 4000` (it wraps at 80 chars); `-W` cannot combine with `-y/-Y`; double quotes inside `-Q` are stripped.
- PowerShell: a function returning a one-element array unrolls to a scalar - call as `@(Fn)[0]`. Quote script paths with
  spaces for elevated launches. pwsh 7 `ConvertFrom-Json` turns ISO strings into `[datetime]` - format explicitly.
- A database rename keeps its file names; restore then refuses. Rename the files too.

## 7. Reporting accuracy rules

- Sales value is the **GST-inclusive `NETAMOUNT`** (`NET_SALES = SUM(R025.NETAMOUNT)`, migration 0046). `NETVALUE` is
  ex-GST. Never switch the measure silently.
- **Decision 41:** a bill cancelled the same day does not count in the DSR INVOICE count (`v_sales_cancelled_same_day`)
  and is left out of Tally sales vouchers. Invoice counts elsewhere: INV documents only; SR/BC counted as Returns.
- **Owner-only:** adjustments, store/CRO targets and staff targets (decisions 16 Sep, 27). An audit finding "Store Manager
  cannot reach X" for these is intended, not a regression.
- Blank report sections usually mean manual inputs never entered, not broken code; check data before code.
- Import failures are never fixed ad hoc: record a row in `docs/audit/IMPORT-FAILURE-REGISTER.md` (skill
  `etp-import-failure`).

## 8. Privacy

- Never put customer names, phone numbers, addresses or invoice-level personal data in outputs, notes, commits, test
  fixtures or chat. Use counts, ids and artificial fixtures (`tests-dotnet/fixtures/etp-sample`).
- Viewer carries column DENYs on customer data and on bank/settlement/deposit lines
  (decisions D37, 53). New views/columns holding personal data need the matching DENY rows; never widen a role silently.
- Support packages and logs must stay PII-free.

## 9. Live-operations boundaries

- The live database `EtpReporting` is **read-only for agents** (read-only `SELECT` via `sqlcmd -E` is fine). Writes,
  migrations and restores on live happen only through Sagar's elevated steps.
- Sagar's alone: pushing/merging `main`; anything needing UAC (elevated gate, chain, installer, backups, drills, task
  registration); Hyper-V VM `ETP-Acceptance-186` power/checkpoints; live installs and migrations; Windows/GitHub/Microsoft
  accounts and every password; USB sticks and keys.
- Prepare everything (exact command, runbook, checked merge), then stop and ask. A refused action is **never** worked around
  or retried another way. A declined UAC prompt means nothing ran.
- Workpc must not be overloaded: builds stay serialised even with up to 20 agents coding.

## 10. Security rules

- Never commit, log, paste or ask for keys, PFX files, passphrases, passwords, tokens or connection strings with
  credentials. `.gitignore` blocks `*.pfx`, `*.p12`, `secrets/`, `private-keys/`; do not weaken it.
- Keys live only on Sagar's two USB sticks (release ECDSA key, Authenticode PFX, OneDrive backup key), passphrases on
  paper. Claude never handles them; Sagar plugs the stick in for the step that needs it.
- Public artefacts are fine to commit: the public `.cer` (`installer/certs/`), public-key lines in `ReleaseSigningKeys.cs`.
- Dev builds are **never** obfuscated or signed. Obfuscation and signing happen only in
  `scripts\build-windows-release.ps1 -Protect` called from `Build-Installer.ps1`. Never add MSBuild hooks, csproj changes
  or `[Obfuscation]` attributes to do it elsewhere.
- Never ship `*.pdb` files; symbols and the obfuscation map go to `E:\ETP\Reference\Symbols\<ver>-<sha7>\` only.
- The protected-build smoke must pass before an installer exists. If a protected build misbehaves, reproduce on the
  unprotected publish first. Details: `docs/security/RELEASE-PROTECTION.md`, `CODE-SIGNING.md`, `VERIFICATION.md`.
- No source encryption (decision 65). Plain source is how Claude works.
