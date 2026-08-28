# ETP closure sprint baseline — 28 August 2026

Status: **Phase 0 baseline captured; state-changing production acceptance remains gated**

## Source identity

- Branch: `ui/uiux-v4-touch-first-redesign`
- Commit: `e48b1d5b153d5d86b790472fff1178b077f7fad7`
- Initial non-Graphify worktree: clean
- Existing generated `graphify-out/` churn was excluded from the product baseline.

## Environment

- Windows 10 Pro `10.0.19045`, x64
- .NET SDK `10.0.400`; runtime `10.0.11`
- PowerShell `7.6.4`
- Node.js `24.19.0`; npm `11.17.0`
- SQL Server Express `16.0.1000.6`, 64-bit; `MSSQL$SQLEXPRESS` running
- Fourteen migrations inventoried from `0001_foundation.sql` through `0014_productisation.sql`

## Executed baseline evidence

| Gate | Result |
|---|---|
| Locked solution restore | Passed for all 13 solution projects |
| Release build | Passed; 0 warnings and 0 errors |
| Pre-ratchet .NET suite | 227 passed; 0 failed (Domain 12, Import 40, Reporting 51, Desktop 76, SQL Server 48) |
| Post-ratchet focused Desktop suite | 79 passed; 0 failed; 0 skipped |
| Real-file import smoke | 12/12 approved workbooks passed: R003, R013, R022, R025, Closing Stock and Stock Ledger for both stores |
| Synthetic DSR PDF smoke | Passed |
| Synthetic visual Excel smoke | Passed |
| Synthetic visual PDF smoke | Passed |
| Headless WPF smoke | 11 production-shell views rendered; 190 accessible named elements found |
| Performance smoke | 250,000 sales rows in 599 ms; 100,000 stock keys in 391 ms; 50,000 tender documents in 239 ms; all under the 30-second gate |
| Read-only SQL availability | Passed |

The synthetic export smoke gates prove generation and parsing only. They do not replace visual inspection of every rendered page, real Excel opening, printer acceptance or live production-data reconciliation.

## Desktop architecture baseline

- Five `MainWindow` C# partials: 2,319 physical lines
- `MainWindow.xaml`: 222 physical lines
- Combined MainWindow surface: 2,541 physical lines
- Largest partial: `MainWindow.xaml.cs`, 1,299 physical lines
- Current concrete infrastructure constructions in MainWindow: 83, protected by a decrease-only ratchet test

These measurements are diagnostics, not the completion definition. The final architecture gate is responsibility ownership: MainWindow must contain only window lifecycle, chrome and workspace hosting, with no feature workflow or infrastructure construction.

## Safely not executed during Phase 0

The following checks change machine, database or external state and were not treated as passed:

- full `LiveSmoke`, which imports sources, writes manual inputs and finalises/reopens validation state;
- live DSR against saved production-like configuration;
- backup and isolated restore/recovery drill;
- elevated installer/upgrade/repair/uninstall lifecycle;
- Microsoft Excel, printer, Narrator, high-contrast and physical keyboard/touch UAT;
- production database migration, import, export or recovery;
- code signing and runtime licensing activation.

They remain explicit `EXTERNAL_VALIDATION_BLOCKED` or later-phase verification rows in `docs/PROJECT_CLOSURE_TRACEABILITY.md`. Environment absence is never recorded as a pass or skip.

## Baseline conclusion

The functional baseline is green enough to begin controlled modular extraction. It does not establish overall product completion. Each later structural change must compare affected workflows, database effects and renderer-neutral outputs to this baseline and must receive independent verification before its requirement rows move to `VERIFIED`.
