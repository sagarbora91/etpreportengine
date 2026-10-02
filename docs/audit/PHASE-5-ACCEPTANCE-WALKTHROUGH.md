# Phase 5 acceptance walkthrough — A5.1 and A5.2

Printable checklist for proving **A5.1** (every screen a role can reach works for that role) and **A5.2** (no staff screen is a copy of another) on the **installed** app with a **populated** database, as **Owner**, **Store Manager** and **Viewer**. This is the evidence that `PHASE-5-REPORT.md` and `PHASE-5-AUDIT-FINDINGS.md` section 8 say is still missing.

Print it, tick boxes with a pen, write problems in the notes column, then photograph or scan the pages and file them with the date next to this document.

| | |
|---|---|
| Date | ________________ |
| PC name | ________________ |
| ETP version (shown in the window title bar) | ________________ |
| Database | `EtpReporting` ☐   other: ________________ |
| Business date used for checks | ________________ |
| Tested by | ________________ |

**How to mark each box:** `✓` works as expected · `✗` problem (write what you saw) · `—` not applicable.
- **works** = the screen opens, the status line shows a sensible message (not blank, not an error), grids have column headings, and buttons you are allowed to use respond.
- **hidden** = the screen does **not** appear in that role's navigation **and** typing its name in the search box does not find it. If a "hidden" screen can be opened, mark `✗` — that is a security problem.

Time needed: about 45 minutes per role, 2½ hours in all.

---

## 1. Before you start (Owner, about 20 minutes)

1. ☐ The app is installed from the release you are accepting (Start Menu → ETP Reporting Engine opens; version written above).
2. ☐ The database has real data: at least one full month imported for every active store, including the business date you will use. Pick a date with sales, returns, a cash entry and at least one register entry if possible.
3. ☐ Create two local Windows test accounts (Start → Settings → Accounts → Other users → Add someone → "I don't have this person's sign-in information" → "Add a user without a Microsoft account"):
   - `EtpTestManager` (password: write it on paper, not here)
   - `EtpTestViewer`
4. ☐ In ETP as Owner, **Settings → Users**: add `<PC name>\EtpTestManager` as **Store Manager, active** and `<PC name>\EtpTestViewer` as **Viewer, active**. Save; the status line should confirm.
5. ☐ Run the database checks in section 2.

**To open ETP as a test account:** hold **Shift**, right-click the ETP shortcut (or `Etp.Reporting.Desktop.exe` in the install folder), choose **Run as different user**, enter `.\EtpTestManager` or `.\EtpTestViewer` and the password. Check the role shown in **Settings → Help → Current profile** before starting that role's column.

**When finished:** set both test accounts to **inactive** in Settings → Users (do not delete them — the audit history keeps their names), and remove the Windows accounts if you do not need them again.

---

## 2. Database checks (Owner, about 5 minutes)

Open **Command Prompt** (not as administrator) and run:

```bat
sqlcmd -S .\SQLEXPRESS -E -d EtpReporting -Q "SELECT migration_id, applied_utc FROM dbo.schema_migrations WHERE migration_id >= '0033' ORDER BY migration_id"
```

☐ The list shows all five, in order:
`0033_accounting_foundation`, `0034_sharing_and_automatic_import`, `0035_registers_approvals`, `0036_retire_unused_master_values`, `0037_accounting_invariants` (newer ones after them are fine).

```bat
sqlcmd -S .\SQLEXPRESS -E -d EtpReporting -Q "SELECT pr.name AS role_name, o.name AS table_name, COUNT(*) AS denies FROM sys.database_permissions p JOIN sys.database_principals pr ON p.grantee_principal_id = pr.principal_id JOIN sys.objects o ON p.major_id = o.object_id WHERE p.state_desc = 'DENY' AND o.name LIKE 'accounting%' AND pr.name IN ('etp_store_manager','etp_viewer') GROUP BY pr.name, o.name ORDER BY 1, 2"
```

☐ For **each** of `etp_store_manager` and `etp_viewer`, the tables `accounting_batch_invoices`, `accounting_batches`, `accounting_entries`, `accounting_export_receipts`, `accounting_mappings` appear, each with `denies` = **4** (SELECT, INSERT, UPDATE, DELETE). After database update 0038 (Phase 7) `accounting_status_history`, `accounting_voucher_reservations` and `accounting_vouchers` also appear with 4 each; that is expected.

```bat
sqlcmd -S .\SQLEXPRESS -E -d EtpReporting -Q "SELECT DISTINCT status FROM dbo.accounting_batches"
```

☐ Only values from: `DRAFT`, `BLOCKED`, `APPROVED_READY`, `EXPORTED_AWAITING_IMPORT`, `REJECTED` (or no rows).

Paste or photograph the three outputs and attach them.

---

## 3. Every screen, every role (A5.1)

Work **one role at a time, down its column**. For each row: open the screen from the navigation (section → tab → screen), wait for it to load, read the status line, then mark the box.

Also once per role, type three hidden screens' names into the search box (e.g. *Users*, *Approvals*, *Import folder* for the Viewer) and confirm nothing opens. ☐ Owner  ☐ Store Manager  ☐ Viewer

The tables below were generated from the app's own navigation list and role rules (`src/Etp.Reporting.Desktop/TaskNavigation.cs`), so "hidden" is what the code is meant to do.

### Today

| # | Path | Screen | Owner | Store Manager | Viewer | Look for / notes |
|---|---|---|---|---|---|---|
| 1 | Today → Walk-ins | **Walk-ins** | ☐ works | ☐ works | ☐ hidden |  |
| 2 | Today → Close day | **Close day** | ☐ works | ☐ works | ☐ works |  |
| 3 | Today → Close day | **Finalise day** | ☐ works | ☐ works | ☐ hidden | SM/Owner: Finalise enabled only when day is ready; Reopen is Owner-only |
| 4 | Today → Close day | **Store pack** | ☐ works | ☐ works | ☐ works |  |
| 5 | Today → Close day | **All stores pack** | ☐ works | ☐ works | ☐ works |  |
| 6 | Today → Cash | **Expense entry** | ☐ works | ☐ works | ☐ hidden |  |
| 7 | Today → Cash | **Cash and service entries** | ☐ works | ☐ works | ☐ hidden | Cash-book fields (cash quick tiles) must be visible — audit F-01 |
| 8 | Today → Close day | **Credit notes** | ☐ works | ☐ works | ☐ hidden |  |
| 9 | Today → Close day | **Service receipts** | ☐ works | ☐ works | ☐ hidden |  |
| 10 | Today → Close day | **Vendor invoices** | ☐ works | ☐ works | ☐ hidden |  |
| 11 | Today → Close day | **Courier** | ☐ works | ☐ works | ☐ hidden |  |

### Import

| # | Path | Screen | Owner | Store Manager | Viewer | Look for / notes |
|---|---|---|---|---|---|---|
| 12 | Import → Import | **Import folder** | ☐ works | ☐ works | ☐ hidden |  |
| 13 | Import → Problems | **Problems** | ☐ works | ☐ works | ☐ works |  |
| 14 | Import → History | **Imports** | ☐ works | ☐ works | ☐ works |  |
| 15 | Import → History | **Received files** | ☐ works | ☐ works | ☐ works |  |

### Reports

| # | Path | Screen | Owner | Store Manager | Viewer | Look for / notes |
|---|---|---|---|---|---|---|
| 16 | Reports → Investigation | **Investigation** | ☐ works | ☐ works | ☐ hidden | No "Refresh approvals" button in the toolbar — F-02 |
| 17 | Reports → All reports | **All reports** | ☐ works | ☐ works | ☐ works |  |
| 18 | Reports → Archive | **Report archive** | ☐ works | ☐ works | ☐ works | See section 5 (Archive share buttons) |
| 19 | Reports → Favourites | **Favourites** | ☐ works | ☐ works | ☐ works |  |

### Stock

| # | Path | Screen | Owner | Store Manager | Viewer | Look for / notes |
|---|---|---|---|---|---|---|
| 20 | Stock → Physical count | **Physical count** | ☐ works | ☐ works | ☐ hidden |  |
| 21 | Stock → Registers | **Inward** | ☐ works | ☐ works | ☐ hidden |  |
| 22 | Stock → Registers | **Outward** | ☐ works | ☐ works | ☐ hidden |  |
| 23 | Stock → Registers | **Stock transfers** | ☐ works | ☐ works | ☐ hidden |  |

### Settings

| # | Path | Screen | Owner | Store Manager | Viewer | Look for / notes |
|---|---|---|---|---|---|---|
| 24 | Settings → Display | **Display** | ☐ works | ☐ works | ☐ works |  |
| 25 | Settings → Database | **Connection** | ☐ works | ☐ hidden | ☐ hidden |  |
| 26 | Settings → Database | **Database health** | ☐ works | ☐ hidden | ☐ hidden | No "Support package" button here (only under Support package) — F-07 |
| 27 | Settings → Database | **Backups** | ☐ works | ☐ hidden | ☐ hidden | Explanatory text present |
| 28 | Settings → Database | **Recovery drill** | ☐ works | ☐ hidden | ☐ hidden | Explanatory text and a heading, not a lone button — F-04 |
| 29 | Settings → Database | **Support package** | ☐ works | ☐ hidden | ☐ hidden | Explanatory text and a heading, not a lone button — F-04 |
| 30 | Settings → Database | **Audit trail** | ☐ works | ☐ hidden | ☐ hidden |  |
| 31 | Settings → Users | **Users** | ☐ works | ☐ hidden | ☐ hidden | Lists the test accounts with correct roles |
| 32 | Settings → Users | **Import profiles** | ☐ works | ☐ hidden | ☐ hidden |  |
| 33 | Settings → Stores & masters | **Brands and targets** | ☐ works | ☐ works | ☐ hidden |  |
| 34 | Settings → Stores & masters | **Stores** | ☐ works | ☐ hidden | ☐ hidden | Only the active stores; names correct |
| 35 | Settings → Stores & masters | **Calculations** | ☐ works | ☐ hidden | ☐ hidden | Status line and "Integration health" heading above the grid — F-03 |
| 36 | Settings → Stores & masters | **Tender mapping** | ☐ works | ☐ hidden | ☐ hidden |  |
| 37 | Settings → Stores & masters | **Staff targets** | ☐ works | ☐ hidden | ☐ hidden |  |
| 38 | Settings → Automatic import | **Automatic import** | ☐ works | ☐ hidden | ☐ hidden | A "Refresh operations" button is present — F-05; shows Windows task state, last result, next run |
| 39 | Settings → Integrations | **Email, sharing and Tally** | ☐ works | ☐ hidden | ☐ hidden |  |
| 40 | Settings → Integrations | **Sharing contacts** | ☐ works | ☐ hidden | ☐ hidden |  |
| 41 | Settings → Accounting | **Prepare → Review → Export** | ☐ works | ☐ hidden | ☐ hidden | Status names only DRAFT / BLOCKED / APPROVED_READY / EXPORTED_AWAITING_IMPORT / REJECTED |
| 42 | Settings → Control centre | **Open items** | ☐ works | ☐ hidden | ☐ hidden |  |
| 43 | Settings → Control centre | **Data quality** | ☐ works | ☐ hidden | ☐ hidden |  |
| 44 | Settings → Control centre | **Approvals** | ☐ works | ☐ hidden | ☐ hidden | Shows pending and decided history |
| 45 | Settings → Control centre | **Adjustment request** | ☐ works | ☐ hidden | ☐ hidden |  |
| 46 | Settings → Help | **Current profile** | ☐ works | ☐ works | ☐ works |  |

### Reports (all three roles can open every report)

For each: set the date/store, **Run**, check rows appear, then **Excel** and **PDF** once per role.

| # | Path | Report | Owner | Store Manager | Viewer | Look for / notes |
|---|---|---|---|---|---|---|
| 47 | Today → Sales | **Sales** | ☐ works | ☐ works | ☐ works |  |
| 48 | Reports → Sales | **Store Sales Summary** | ☐ works | ☐ works | ☐ works |  |
| 49 | Reports → Sales | **Combined Sales Summary** | ☐ works | ☐ works | ☐ works |  |
| 50 | Reports → Sales | **Customer-wise Invoices** | ☐ works | ☐ works | ☐ works |  |
| 51 | Reports → Sales | **Returns** | ☐ works | ☐ works | ☐ works |  |
| 52 | Reports → Sales | **Brand-wise Sales** | ☐ works | ☐ works | ☐ works |  |
| 53 | Reports → Sales | **Brand-Segment Sales** | ☐ works | ☐ works | ☐ works |  |
| 54 | Reports → Sales | **Item-wise Sales** | ☐ works | ☐ works | ☐ works |  |
| 55 | Stock → Closing stock | **Closing Stock** | ☐ works | ☐ works | ☐ works |  |
| 56 | Stock → Physical count | **Physical Stock** | ☐ works | ☐ works | ☐ works |  |
| 57 | Stock → Variance | **Stock Variance** | ☐ works | ☐ works | ☐ works |  |
| 58 | Stock → Movement | **Stock Movement** | ☐ works | ☐ works | ☐ works |  |
| 59 | Stock → Brand stock | **Brand Stock** | ☐ works | ☐ works | ☐ works |  |
| 60 | Stock → Slow stock | **Slow / Exception Stock** | ☐ works | ☐ works | ☐ works |  |
| 61 | Reports → Staff | **Staff/CRO Performance** | ☐ works | ☐ works | ☐ works |  |
| 62 | Reports → Tender & service | **Tender Reconciliation** | ☐ works | ☐ works | ☐ works |  |
| 63 | Today → Cash | **Cash Book** | ☐ works | ☐ works | ☐ works |  |
| 64 | Reports → Tender & service | **Tender Diagnostics** | ☐ works | ☐ works | ☐ works |  |
| 65 | Reports → Tender & service | **Service Sales** | ☐ works | ☐ works | ☐ works |  |
| 66 | Reports → Exceptions | **Daily Exception Report** | ☐ works | ☐ works | ☐ works |  |
| 67 | Reports → Management | **Management Trend** | ☐ works | ☐ works | ☐ works | Rows, dates and chart filled for the chosen range — see section 6 |
| 68 | Reports → Management | **Invoice Source Drill-down** | ☐ works | ☐ works | ☐ works |  |

### Settings → Help topics (all roles)

| # | Topic | Owner | Store Manager | Viewer |
|---|---|---|---|---|
| 69 | **Getting Started** | ☐ | ☐ | ☐ |
| 70 | **Today** | ☐ | ☐ | ☐ |
| 71 | **Business Day** | ☐ | ☐ | ☐ |
| 72 | **Import ETP** | ☐ | ☐ | ☐ |
| 73 | **Import History** | ☐ | ☐ | ☐ |
| 74 | **Daily Sales Report** | ☐ | ☐ | ☐ |
| 75 | **Sales Reports** | ☐ | ☐ | ☐ |
| 76 | **Stock Reports** | ☐ | ☐ | ☐ |
| 77 | **Tender, Cash & Service** | ☐ | ☐ | ☐ |
| 78 | **Staff / CRO** | ☐ | ☐ | ☐ |
| 79 | **Exception Centre** | ☐ | ☐ | ☐ |
| 80 | **Management** | ☐ | ☐ | ☐ |
| 81 | **Investigation** | ☐ | ☐ | ☐ |
| 82 | **Digital Registers** | ☐ | ☐ | ☐ |
| 83 | **Accounting** | ☐ | ☐ | ☐ |
| 84 | **Operations & Support** | ☐ | ☐ | ☐ |
| 85 | **Administration** | ☐ | ☐ | ☐ |
| 86 | **Report Archive** | ☐ | ☐ | ☐ |
| 87 | **Backup & Recovery** | ☐ | ☐ | ☐ |
| 88 | **Troubleshooting** | ☐ | ☐ | ☐ |

---

## 4. Refused actions and kept drafts (A5.1)

A role that can *see* a screen may still be blocked from some actions. The app must refuse **with a clear message**, not crash or silently do nothing.

| # | Check | Store Manager | Viewer |
|---|---|---|---|
| D1 | **Close day**: *Reopen day* button and reason box are disabled | ☐ | ☐ |
| D2 | **Close day**: Viewer cannot save manual input, stock count or finalise (buttons disabled) | — | ☐ |
| D3 | **Close day** day-register list: Viewer sees "Owner or Store Manager permission is required to open registers." | — | ☐ |
| D4 | **Any register** (e.g. Credit notes): *Verify* is disabled for Store Manager; creating and editing a draft works | ☐ | — |
| D5 | **Register on a finalised (locked) day**: editing an entry is refused with a message | ☐ | — |
| D6 | **Import → Problems**: Viewer can read problems; *Retry failed* is disabled with an explanation beside it | — | ☐ |
| D7 | **Report archive**: *Test send* (SMTP) is disabled | ☐ | ☐ |
| D8 | **Changed overlapping import**: Store Manager can *request* a restatement with a reason but cannot approve it; the request appears for the Owner under Settings → Control centre → Approvals | ☐ | — |

**Kept drafts** — do these once as Owner and once as Store Manager:

| # | Check | Owner | Store Manager |
|---|---|---|---|
| K1 | Start a new register entry, type something, then click **New entry** again → app asks before discarding | ☐ | ☐ |
| K2 | Start editing a register entry, navigate to another screen → app asks (or keeps the draft); come back and the typing is still there if you chose to keep it | ☐ | ☐ |
| K3 | Owner only: change a field in **Email, sharing and Tally**, navigate away → asked to save or discard | ☐ | — |
| K4 | Changing the header store/date by itself (no typing) does **not** produce a "discard changes?" prompt | ☐ | ☐ |

---

## 5. Report archive share buttons as Viewer (A5.1)

Log in as **Viewer** → Reports → Archive. Select a saved pack (create one first as Owner if the archive is empty: Today → Close day → Store pack).

| # | Button | Expected | Result |
|---|---|---|---|
| S1 | **Open** | The pack opens | ☐ |
| S2 | **Excel** | Saves an .xlsx that opens in Excel with the same period and totals | ☐ |
| S3 | **PDF** | Saves a PDF with period, page numbers and totals | ☐ |
| S4 | **ZIP** | Saves a ZIP containing the pack | ☐ |
| S5 | **Share → Email** (pick a contact or type your own address) | Sends, or gives a clear message if email is not configured; history shows *Started* then *Accepted by mail server* / *Failed* | ☐ |
| S6 | **Share → WhatsApp** | WhatsApp Desktop opens and the PDF path is copied; history says the handoff is ready | ☐ |
| S7 | **Test send** | Disabled for Viewer | ☐ |

Repeat S5 once as Owner with real SMTP settings to note whether TLS and sign-in work against the real mail server: ☐ accepted ☐ failed — message: ____________________

---

## 6. No duplicate screens (A5.2)

Do this as **Owner** (sees the most screens).

| # | Check | Result |
|---|---|---|
| A1 | Each section's tabs have **no two screens with the same name** (Today, Import, Reports, Stock, Settings) | ☐ |
| A2 | Search box: type *trend* → only **one** trend destination (Management Trend report) appears; there is no separate "Trends" screen | ☐ |
| A3 | **Management Trend** for a 30-day range shows rows for those dates, daily sales/units/invoices, and the chart is filled | ☐ |
| A4 | **Database health** has no "Support package" button; creating a package happens only under **Support package** | ☐ |
| A5 | Help topics list has no repeated title (in particular "Sales Reports" and "Stock Reports" are separate) | ☐ |
| A6 | Pairs that could look alike really show different content: **Store pack** vs **All stores pack** (one store vs all stores) · **Imports** vs **Received files** (import outcomes vs retained documents) · **Open items** vs **Data quality** · **Close day** vs **Finalise day** · **Store Sales Summary** vs **Combined Sales Summary** | ☐ |
| A7 | **Reports → Investigation**: search an invoice number, open the result → it lands on the right screen with date/store/reference carried over (Owner and Store Manager) | ☐ O ☐ SM |

Write any pair that looks like the same screen with the same data here: ________________________________________________

---

## 7. Result and sign-off

| Criterion | Result | Problems found (row numbers) |
|---|---|---|
| Database checks (section 2) | ☐ PASS ☐ FAIL | |
| **A5.1** every reachable screen works, hidden ones stay hidden (sections 3–5) | ☐ PASS ☐ FAIL | |
| **A5.2** no duplicate screens (section 6) | ☐ PASS ☐ FAIL | |
| Real SMTP (section 5, Owner) | ☐ PASS ☐ FAIL ☐ NOT RUN | |
| WhatsApp handoff (S6) | ☐ PASS ☐ FAIL ☐ NOT RUN | |

Signed (Owner): ____________________  Date: ____________

**After the walk:** send a photo of every page with a `✗`, plus a screenshot of each failing screen, in a Claude session with the row number. Each `✗` becomes a fix with a test. A5.1 and A5.2 are proven only when a walk with no `✗` is recorded.
