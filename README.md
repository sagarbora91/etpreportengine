# ETP Reporting Engine — Windows application

The new SQL Server-backed Windows reporting application is documented in
[`docs/14_WINDOWS_QUICK_START.md`](docs/14_WINDOWS_QUICK_START.md). Its .NET solution is
`Etp.Reporting.slnx`; run `scripts/build-windows-release.ps1` to build, test and publish it.
The legacy Android Business Control Centre retained below is source/reference material and
is not the Windows reporting database.

The Windows application now supports a complete business-date workflow: deterministic batch import for R003/R013/R022/R025 and stock profiles, controlled restatement, detailed physical counts, staff targets, FTD/MTD/YTD/LY reporting, traceable invoice detail, service/cash/stock/staff controls, exception diagnostics, full multi-sheet Excel and paginated PDF report packs, immutable report generations, audited finalise/reopen protection, Windows-integrated roles, unattended watch-folder import, scheduled combined packs, archive comparison/re-export, controlled masters and a management/data-quality operations center. See [`docs/21_DAILY_REPORTING_IMPLEMENTATION_MAP.md`](docs/21_DAILY_REPORTING_IMPLEMENTATION_MAP.md), [`docs/22_REPORT_TO_SOURCE_MATRIX.md`](docs/22_REPORT_TO_SOURCE_MATRIX.md), [`docs/23_MASTER_PROMPT_COMPLETION_AUDIT.md`](docs/23_MASTER_PROMPT_COMPLETION_AUDIT.md), and [`docs/24_PHASE2_OPERATIONS.md`](docs/24_PHASE2_OPERATIONS.md).

Project business knowledge, report rules, mappings and architecture decisions are indexed in the Obsidian-compatible [`knowledge/00-Home/ETP Knowledge Home.md`](knowledge/00-Home/ETP%20Knowledge%20Home.md) vault. AI agents should begin with [`knowledge/AI-CONTEXT.md`](knowledge/AI-CONTEXT.md) and retrieve only the task-specific notes selected by [`knowledge/AI-ROUTER.md`](knowledge/AI-ROUTER.md) before using Graphify for code relationships.

The accepted dual-layer offline licensing design is engineering-complete but intentionally not enforced until the functional product is complete. See [`docs/security/ETP_LICENSING_ENGINEERING_SPEC.md`](docs/security/ETP_LICENSING_ENGINEERING_SPEC.md) and the deferred implementation backlog beside it. No production key or activation secret belongs in this repository.

# Saagar Traders — Business Control Centre → Offline Android APK

This project packages the **Business Control Centre shell** (`www/index.html`, with all
**10** business modules base64-embedded) into a **fully offline Android app** that stores
business data for months and writes an automatic daily backup to the phone's **Documents**
folder.

- 100% offline — no internet ever required (the shell has zero external resources).
- All data stays on the phone in the app's private WebView storage. The app ships with the
  **SQLite-primary storage engine ("Option C", `storage-core.js`) LIVE** on `main`: an
  in-memory `Map` is the synchronous source of truth, persisted to an on-device SQLite file
  via the Capacitor Filesystem plugin, so the old ~5 MB `localStorage` ceiling is gone. See
  section 5 and **`ARCHITECTURE.md`** for the full storage stack.
- A dated JSON backup is written every day to `Documents/SaagarBCC-Backups/`
  as a safety net (recover after uninstall / move to a new phone).
- The 10 modules behave as they always did — they still call the synchronous `localStorage`
  API; the storage engine transparently backs that API with SQLite.

> See **`ARCHITECTURE.md`** for how the shell, the base64-embedded modules, the `postMessage`
> rail, the storage stack, and the cross-module integration bus fit together.

---

## Current build facts

These are the **authoritative** values and where they live in code. Treat the named
constants/files as the single source of truth — prose elsewhere can drift, the constants can't.

| Fact | Value | Source of truth |
|---|---|---|
| App version | `APP_VERSION` → **V5** | `www/index.html` (~line 1636) |
| APK build | `APK_BUILD` → **2.7** | `www/index.html` (~line 1636) |
| In-app About page | (shows the two above) | populated via JS in `www/index.html` (~line 5014) |
| Storage engine | `STORAGE_CORE_ENABLED` → **LIVE on `main`** | `www/storage-core.js` |
| Module count | **10** | the files in `_extracted_modules/` |
| QMS archival window / threshold | `QMS_KEEP_DAYS` / `QMS_ARCHIVE_MIN` → **45 / 300** | `www/index.html` (~line 4731) |

`APP_VERSION` and `APK_BUILD` are the two constants in `www/index.html`; the in-app About page
reads them through JS rather than hardcoding, so the screen can never disagree with the constants.
`_extracted_modules/` is produced by `extract-modules.js` (one decoded HTML per embedded module),
so its file count *is* the module count.

> Prose elsewhere should reference these constants by name, not copy the literal value.

---

## 1. What you need (one-time setup on a Windows PC)

| Tool | Version | Link |
|---|---|---|
| Node.js LTS | 18 or 20 | https://nodejs.org |
| Java JDK | 17 | https://adoptium.net (Temurin 17) |
| Android Studio | latest | https://developer.android.com/studio |

After installing Android Studio, open it once and let it download the
**Android SDK** (Tools → SDK Manager → install "Android SDK Platform 34" and
"Android SDK Build-Tools"). Set the `JAVA_HOME` environment variable to the JDK 17
folder.

> No coding required after setup — the build is two commands.

---

## 2. Build the APK (the short version)

Open **PowerShell** or **Command Prompt** in this folder
(`SaagarBCC-Android`) and run:

```bat
npm install
npm run add:android
npm run build:apk
```

The signed-for-testing APK appears at:

```
android\app\build\outputs\apk\debug\app-debug.apk
```

Copy that file to the phone and tap it to install
(enable "Install from unknown sources" when prompted).

**Even simpler:** just double-click **`build.bat`** in this folder — it runs all
three steps and tells you where the APK is.

---

## 3. Build the APK (with Android Studio UI, recommended first time)

```bat
npm install
npm run add:android
npm run sync
npm run open
```

`npm run open` launches Android Studio with the project. Then in Android Studio:

1. Wait for "Gradle sync" to finish (bottom status bar).
2. Menu **Build → Build Bundle(s) / APK(s) → Build APK(s)**.
3. Click **locate** in the popup → that is your `app-debug.apk`.

---

## 4. A proper release APK (optional, for long-term use)

A debug APK works fine for in-store use. For a clean signed release build:

```bat
keytool -genkey -v -keystore saagar.keystore -alias saagar -keyalg RSA -keysize 2048 -validity 10000
```

Then create `android\key.properties`:

```
storeFile=../../saagar.keystore
storePassword=YOUR_PASSWORD
keyAlias=saagar
keyPassword=YOUR_PASSWORD
```

…and run `npm run build:release`. (Android Studio's **Build → Generate Signed
Bundle / APK** wizard does the same thing with a UI.)

---

## 5. Data storage & durability (this is the important part)

**Where the data lives.** Everything you enter (stock, payroll, expenses, staff,
service jobs, leave, tax, etc.) is stored inside the app's private data directory.

The storage engine is **`storage-core.js` ("Option C", SQLite-primary), and it is LIVE
on `main`** (`STORAGE_CORE_ENABLED = true`). Once it has booted, the working store is an
**in-memory `Map` (`MEM`) that is the source of truth**, and the durable persistence layer
is a **SQLite database** (`sql.js` WebAssembly, exported to a `bcc.sqlite` file via the
Capacitor Filesystem plugin). Native `localStorage` is no longer the live store after
boot — modules still call the synchronous `localStorage` API, but the engine overrides
`Storage.prototype` so those reads/writes hit `MEM`/SQLite. Because the data no longer
lives in `localStorage` proper, **the old ~5 MB `localStorage` quota no longer applies.**

Durability / data-safety properties of the engine (see `ARCHITECTURE.md` and the
`storage-core.js` header for detail):

- **WAL journaling** — every set/remove/clear is written to a small, sequenced
  synchronous native-`localStorage` write-ahead log first, so a crash before the next
  SQLite export loses nothing; it is replayed on the next boot.
- **Atomic file writes** — the SQLite blob is written `.tmp` → rename, and the previous
  good file is promoted to `.bak` first, so an interrupted write never leaves two bad
  files. The whole-file export is debounced and also flushed on pause/visibility-hidden.
- **Corrupt-DB recovery chain** — on boot the engine validates each candidate file with
  `PRAGMA quick_check` and falls through `live → .tmp → .bak → fresh` so a torn file is
  rejected rather than adopted. A hard boot timeout falls back to (and later self-heals
  from) the data hydrated at startup.

`sqlite-store.js` (the older "Design A" durable-mirror engine) **stands down** whenever
`storage-core.js` is enabled — it detects `window.SaagarStore.enabled` and returns early,
so only one engine owns `Storage.prototype` and the `bcc.sqlite` file at a time.

> **Historical note (now reconciled).** Earlier `storage-core.js` carried a stale in-file
> comment implying `STORAGE_CORE_ENABLED` should be a test-branch-only flag with `main` false;
> the flag is now `true` and the engine is **LIVE on `main`**, which is the authoritative state.

Like any app data, the on-device data is lost only if the app is **uninstalled** or the
user taps **Settings → Apps → Saagar Control Centre → Storage → Clear storage** (which
also clears the `bcc.sqlite` file and the WAL). It otherwise survives closing the app,
phone restarts, and app updates (the origin is pinned to `https://localhost` in
`capacitor.config.json` so updates keep the same data), across months of daily use.
A full device-local **Factory Reset** is exposed via `window.SaagarStore._reset()`
(awaited, wipes `MEM` + SQLite files + WAL).

**The safety net.** Because those two actions wipe data, the app writes an
automatic backup every day:

```
Documents/SaagarBCC-Backups/backup-YYYY-MM-DD.json   (one per day, 90 kept)
Documents/SaagarBCC-Backups/latest.json              (always the newest)
```

These files are plain JSON and live in the phone's normal **Documents** folder,
so they survive an app uninstall and can be copied to a PC or a new phone.

**To restore** (new phone, or after a reset): install the app, open it, go to
**Configuration → Data & Backup → Restore from JSON**, and pick any
`backup-….json` file. (The app already has this Restore feature built in.)

**Manual backup any time:** the dev/console command `SaagarBackup.now()` forces an
immediate backup; `SaagarBackup.status()` shows the last backup date and log.
The on-screen **Backup** button in the app also still works.

> Tip: once a month, copy the `SaagarBCC-Backups` folder off the phone (USB,
> Google Drive, WhatsApp to yourself). That is your true long-term archive.

---

## 6. Storage permission note (older phones only)

On Android 11+ the Filesystem plugin writes to Documents without any runtime
permission. On **Android 9–10** add this line inside `<manifest>` in
`android\app\src\main\AndroidManifest.xml` (after `npm run add:android`):

```xml
<uses-permission android:name="android.permission.WRITE_EXTERNAL_STORAGE"
    android:maxSdkVersion="29" />
```

Capacitor will still keep `localStorage` working even if file backup is denied —
you simply lose the external safety-net copy, not the live data.

---

## 7. Updating the app later (new shell build)

1. Replace `www\index.html` with the new shell file (the 10 modules are base64-embedded
   into its `MODULES` array by the build pipeline).
2. Keep the storage + backup scripts wired in the same load order (see `ARCHITECTURE.md`):
   `sql-wasm.js` and `storage-core.js` in `<head>`, and `auto-backup.js` /
   `sqlite-store.js` near the end of `<body>`. Re-add them if a new file is missing them.
3. Run `npm run build:apk` and reinstall.

Because the app origin is pinned, **existing on-device data is preserved** across
the update.

---

## 8. Project layout

```
SaagarBCC-Android/
├── www/
│   ├── index.html         ← shell (all 10 modules base64-embedded) + storage/backup scripts
│   ├── storage-core.js    ← LIVE storage engine (Option C: SQLite-primary, MEM source-of-truth)
│   ├── sqlite-store.js    ← older Design-A mirror engine; stands down when storage-core is on
│   ├── photo-store.js     ← Filesystem-backed photo API (mechanism only; dormant — not yet wired)
│   ├── integration-bridge.js ← cross-module event bus (saagar_bus) reconciling the 10 modules
│   ├── auto-backup.js      ← daily offline JSON backup safety net
│   └── sql-wasm.js / sql-wasm.wasm ← sql.js WebAssembly (defines initSqlJs)
├── capacitor.config.json   ← app id, pinned origin (do not change hostname)
├── package.json            ← build scripts
├── build.bat               ← double-click to build the APK
├── ARCHITECTURE.md         ← shell / module / postMessage / storage / bus architecture
├── .gitignore
└── README.md               ← this file
```

> The list above shows the load-bearing files for the architecture; `www/` also contains
> the PDF/report/WhatsApp helper scripts and fonts referenced near the end of `index.html`.

`android/` and `node_modules/` are generated by the build commands and are not
shipped in this folder.

---

## 9. Troubleshooting

| Symptom | Fix |
|---|---|
| `cap: command not found` | Run `npm install` first (installs Capacitor CLI locally). |
| Gradle sync fails / SDK not found | Open Android Studio → SDK Manager → install Platform 34 + Build-Tools. Set `JAVA_HOME` to JDK 17. |
| APK installs but data resets each launch | Don't change `hostname`/`androidScheme` in `capacitor.config.json` — the origin must stay constant. |
| No backup files in Documents | Check section 6 (older-Android permission). Live data is still safe in the app. |
| White screen on launch | Confirm `www\index.html` exists and still ends with `</body></html>`; re-run `npm run sync`. |
