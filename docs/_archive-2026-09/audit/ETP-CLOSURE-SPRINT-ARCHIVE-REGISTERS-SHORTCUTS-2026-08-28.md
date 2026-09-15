# Closure Sprint — Archive, Registers and Shortcut Parity

Date: 28 August 2026
Branch: `ui/uiux-v4-touch-first-redesign`

## Implemented

- Archive search, integrity-checked open and comparison now flow through an Application archive contract and a SQL Server adapter composed outside `MainWindow`.
- Sharing contacts and Digital Registers now flow through Application services and Windows-integrated SQL adapters while retaining the existing Owner enforcement, locked-day protection and operational audit writes.
- `Ctrl+R` executes the existing failed-import retry only in the Imports workspace and only while retry is enabled.
- Executable Help shortcut rows derive their gesture text from the executable registry. Unsupported edit/new/save claims were removed, and native WPF shortcuts are explicitly classified by the parity tests.
- Help topic navigation now moves keyboard focus into the topic actions when Help owns or is acquiring focus.
- Daily Workflow and Manual Entry now cross separate read, command and report-pack Application ports. The SQL facade delegates to the existing controlled repositories and pack service, retaining missing-versus-zero semantics, locked-day enforcement, administrator-approved reopening and report hashes.
- Source Inbox/OCR reads, review, intake and integrity verification now cross an Application service while preserving immutable evidence, duplicate detection, quarantine decisions and SHA-256 checks.
- Tested Application/SQL boundaries for Reports and Accounting are prepared for the next UI integration step; they are not counted as extracted presentation workflows yet.

No report formula, mapping, database schema, archive integrity rule or import persistence behavior changed.

## Verification

- Release build: passed with 0 warnings and 0 errors.
- Full automated suite: 363 passed, 0 failed, 0 skipped.
- WPF production-shell smoke: 11 views rendered; 190 accessible named elements.
- Archive adapter: SHA-256 verification remains delegated to the existing verified archive load path.
- Register/contact adapters: focused mapping, permission propagation, connection-policy and cancellation tests pass.
- Direct MainWindow SQL-infrastructure construction inventory: 81 reduced to 60.

## Remaining boundary

The archive, register, contact, Daily Workflow and Source Inbox presentation controls still reside in the MainWindow XAML/code-behind. This slice establishes and connects their application boundaries; it does not claim that the shell-only architecture gate is complete. Reports and Accounting contracts are ready but not yet connected; Import persistence, Operations and Administration remain subsequent extraction waves.

Live SQL role testing and installed WhatsApp/email client behavior remain target-PC acceptance activities.
