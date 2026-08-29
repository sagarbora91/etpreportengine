# Saagar Business Control Centre legacy Android architecture

Status: **legacy/reference only**

This document preserves the architecture boundary for the Capacitor/WebView application under `www/`. It is not the architecture, installer, database, or release authority for the active ETP Reporting Engine. See the repository-root [`ARCHITECTURE.md`](../../ARCHITECTURE.md) for the .NET/WPF Windows product.

## Runtime shape

The legacy application is a fully local Capacitor WebView. `www/index.html` is the shell and `www/module-manifest.js` is the synchronous authority for 12 same-origin external module routes under `www/modules/`:

- Stock Register
- Watch Service Centre
- Queue Management
- CRO Login
- Expense Manager
- Grooming Checklist
- Store Manager
- Payroll
- Staff Leave Calendar
- Tax Compliance Calendar
- Business Planning
- Retail ETP Verification

The shell owns the single module iframe, navigation, role visibility, print/share bridges, portable backup controls, and other OS-facing behavior. A module is loaded from its local manifest route; module HTML is not base64-embedded in `index.html`.

## Shell-to-module boundary

The shell and active module communicate through a bounded `postMessage` rail. Module requests such as home navigation, cross-module navigation, audit, print, share, and WhatsApp composition are handled by the shell. Shell messages provide selected date, UI mode, feature navigation, and send acknowledgements. Native/plugin authority remains parent-owned rather than being exposed directly to every iframe.

`www/module-manifest.js` records the local route and file identity expected by the shell. Updates to the shell, manifest, shared assets, or external modules must be treated as one validated legacy package.

## Storage boundary

`www/storage-core.js` is the enabled Option-C storage engine for this legacy source:

1. an in-memory `Map` provides synchronous key/value behavior to callers;
2. `sql.js` persists the key/value database to the app-private `bcc.sqlite` file;
3. a bounded write-ahead journal protects writes that occur before the next full-file persist;
4. temporary and backup-file rotation protects whole-file replacement;
5. startup checks the live, temporary, and backup candidates before falling back;
6. `www/sqlite-store.js`, the older mirror design, stands down when `storage-core.js` is enabled.

Modules continue to use the synchronous Storage-compatible surface. The storage engine owns persistence and recovery behind that surface. The pinned Capacitor origin must remain stable so browser-origin data is not accidentally orphaned.

## Backup boundary

`www/auto-backup.js` provides the rolling text-data safety-net export used by the legacy application. Manual portable backup/restore logic is broader and governed by the shell's allowlists and restore validation. Sealed or re-derivable ETP facts, device-local security state, and large evidence files have separate inclusion rules; do not assume every browser/native store is present in the daily JSON backup.

## ETP hybrid boundary

The legacy Retail ETP module is a bounded hybrid. Presentation runs in its external iframe while privileged import, sealed-fact persistence, verified reads, and operational projections remain behind parent/native gateway contracts. The iframe must not receive unrestricted native-store or plugin authority.

This legacy model must not be used as evidence for the Windows SQL Server import, reporting, migration, installer, security, or acceptance boundaries.
