# Service UI 1.10.0: acceptance runbook (staging copy)

**For:** Sagar and the 1.10.0 coordinator. **Written:** 10 Oct 2026 by lane docs (U5), from the design review `docs/roadmap/SERVICE-CENTRE-UI-DESIGN-REVIEW-2026-10-10.md`, the lane DONE files in `Reference\Work in progress 2026-10-09\110-*-DONE.md`, and migration 0050 as it stood on `svcui/sql` (7d91a57).
**Status:** not run yet. Run it after the 1.10.0 gate has passed and the installer is built, before 1.10.0 goes to live Workpc or the shop PC.

**What it proves:**

- Every number on the six Service tabs equals a read-only SELECT against the same database.
- Each screen opens in about 1 s once the database holds three more weekly readings (design SD-11).
- Each of the 15 decision-25 answers is visible in the product (section 8).

**Ground rules:**

- **Never against live `EtpReporting`.** Everything runs on a scratch copy, named `EtpAccept_SVC110` below.
- **Every SQL in this runbook is a SELECT**, except the restore (step 1.3), which Sagar runs elevated.
- **Elevated steps, VM power, installs on a real PC and dropping a database are Sagar's** (see the memory note on live operations boundaries). Claude may prepare commands, but never works around a refusal.
- **Keep the evidence** in `E:\ETP\Reference\Work in progress <date>\service-ui-acceptance (CONTAINS BUSINESS DATA)\`.
  - Job numbers and customer names appear on some screens. Keep them in that folder, and redact screenshots before sharing them anywhere else.
  - The PASS/FAIL sheet itself records counts, dates and PASS/FAIL only.
- **Never filter the output.** Keep each sqlcmd output whole (`-o <file>`).

---

## 1. Prepare the scratch copy

### 1.1 Choose where to run it

| Option | Where | Use when |
|---|---|---|
| **A (recommended)** | The Hyper-V VM (VM195), as in the 9 Oct shop rehearsal (`Reference\VM195-20261009-1904-NewPc`) | Always possible; nothing on Workpc changes. 1.10.0 setup upgrades whatever `EtpReporting` it finds, so in the VM the restored copy can simply be called `EtpReporting` and the real installer can be used. |
| B | Workpc, scratch database `EtpAccept_SVC110` | When the VM is not available. **Do not run the 1.10.0 installer on Workpc for this test**: it would upgrade live `EtpReporting`. Apply the migrations to the scratch database with the built 1.10.0 app instead (1.4 B). |

In option A, read `EtpReporting` wherever this runbook says `EtpAccept_SVC110`.

### 1.2 Pick the source backup

Use one of:

- **(a) The 9 Oct staging backup:** `E:\ETP\Installers\Shop PC database 2026-10-09\EtpStaging_20261009-1.9.6-20261009-185628.bak`.
  - Check its SHA-256 against `SHA256SUMS.txt` and the `.receipt.json` beside it.
  - It holds 48 migrations and the Service data of 29 Sep (consolidated) and 3 Oct (raw), 28,244 landing rows.
- **(b) A fresh verified live backup:** the newest `EtpReporting-*.bak` in `%ProgramData%\EtpReporting\Backups` with its receipt, taken after the 9 Oct section E import (28,732 Service rows). Verify the receipt hash first.

Write the file name, SHA-256 and size in the evidence sheet (section 9).

### 1.3 Restore under the scratch name (Sagar, elevated)

- **Option A (VM):** run the installed helper, as in the shop rehearsal: `restore-etp-database.ps1 -BackupPath <bak> -ReceiptPath <receipt>`. It restores as `EtpReporting` and accepts `EtpStaging_*` backups.
- **Option B (Workpc):** the helper always restores as the configured `EtpReporting` and refuses because live exists. Restore by hand, with `MOVE` to new file names and never `REPLACE`, into SQL Server's default folders on C: (the E: drive's 16 KB sectors do not suit SQL Server data files):

```sql
-- elevated sqlcmd, SQL administrator; read the logical names first
RESTORE FILELISTONLY FROM DISK = N'<full path of the .bak>';
-- then, with the two logical names it printed and the instance default paths
-- (SELECT SERVERPROPERTY('InstanceDefaultDataPath'), SERVERPROPERTY('InstanceDefaultLogPath')):
RESTORE DATABASE [EtpAccept_SVC110] FROM DISK = N'<full path of the .bak>'
  WITH MOVE N'<data logical name>' TO N'<data path>\EtpAccept_SVC110.mdf',
       MOVE N'<log logical name>'  TO N'<log path>\EtpAccept_SVC110_log.ldf',
       CHECKSUM, STATS = 10;
ALTER DATABASE [EtpAccept_SVC110] SET TRUSTWORTHY OFF;
DBCC CHECKDB ([EtpAccept_SVC110]) WITH NO_INFOMSGS;
```

**Check:** `SELECT COUNT(*) FROM dbo.schema_migrations` on the copy gives 48. PASS/FAIL.

### 1.4 Install 1.10.0 on the copy

- **A (VM):** run the 1.10.0 installer. Setup takes its verified pre-migration backup, then applies 0049 and 0050.
- **B (Workpc):** from the 1.10.0 release build folder (`artifacts\windows-release-<commit>`), apply the migrations to the copy only:

```powershell
$psi = [System.Diagnostics.ProcessStartInfo]::new('<release folder>\Etp.Reporting.Desktop.exe')
$psi.ArgumentList.Add('--connection-string')
$psi.ArgumentList.Add('Server=.\SQLEXPRESS;Database=EtpAccept_SVC110;Integrated Security=true;Encrypt=Optional')
$psi.ArgumentList.Add('--initialize-database')
$p = [System.Diagnostics.Process]::Start($psi); $p.WaitForExit(); $p.ExitCode   # 0 = applied
```

Use `ProcessStartInfo.ArgumentList` as above, never `Start-Process -ArgumentList`: the latter splits the connection string at its spaces.

Open the app the same way, but without `--initialize-database`. Check the title or Settings to see which database it opened.

**Checks (sqlcmd -S .\SQLEXPRESS -d EtpAccept_SVC110 -E -C -W):**

```sql
SET NOCOUNT ON;
SELECT TOP 3 migration_id FROM dbo.schema_migrations ORDER BY migration_id DESC;
-- expect 0050_service_centre_ui, 0049_tally_store_cost_centres (or PR #3's final name), 0048_service_centre_interim
SELECT name FROM sys.views WHERE name IN ('v_service_status_view_facts','v_service_claims','v_service_job','v_service_job_timeline',
  'v_service_parts','v_service_parts_transit','v_service_stock_summary','v_service_pending_current') ORDER BY name;   -- expect 8
-- privacy (design 1.8): no contact column in any 0050 view; expect 0 rows
SELECT v.name, c.name FROM sys.views v JOIN sys.columns c ON c.object_id = v.object_id
WHERE v.name IN ('v_service_status_view_facts','v_service_claims','v_service_job','v_service_job_timeline','v_service_parts',
  'v_service_parts_transit','v_service_stock_summary','v_service_pending_current')
  AND (c.name LIKE '%mobile%' OR c.name LIKE '%email%' OR c.name LIKE '%phone%' OR c.name LIKE '%landline%'
       OR c.name LIKE '%contact%' OR c.name LIKE '%address%');
-- grants: expect 8 views x (3 GRANT SELECT + 6 DENY) = 72 rows
SELECT COUNT(*) FROM sys.database_permissions p JOIN sys.views v ON v.object_id = p.major_id
JOIN sys.database_principals r ON r.principal_id = p.grantee_principal_id
WHERE v.name IN ('v_service_status_view_facts','v_service_claims','v_service_job','v_service_job_timeline','v_service_parts',
  'v_service_parts_transit','v_service_stock_summary','v_service_pending_current')
  AND ((p.permission_name = 'SELECT' AND p.state_desc = 'GRANT' AND r.name IN ('etp_viewer','etp_store_manager','etp_owner'))
    OR (p.permission_name IN ('INSERT','UPDATE','DELETE') AND p.state_desc = 'DENY' AND r.name IN ('etp_viewer','etp_store_manager')));
```

Each of the four is a PASS/FAIL line.

### 1.5 Baseline, before the three readings

```powershell
sqlcmd -S .\SQLEXPRESS -d EtpAccept_SVC110 -E -C -i scripts\service-centre\measure-service-growth.sql -W -s "|" -o before.txt
```

```sql
SET NOCOUNT ON;
SELECT stage, is_open, COUNT(*) jobs FROM dbo.v_service_job GROUP BY stage, is_open ORDER BY stage, is_open;
SELECT MAX(as_at) as_at, COUNT(*) jobs FROM dbo.v_service_job;
```

Then time every screen once (section 3) and keep the figures as "baseline".

---

## 2. Import three weekly Service readings

1. **Use real exports.** These are three consolidated Service folders a week apart, plus the raw daily packs of those weeks if Sagar has them.
   - Copy each into a dated folder, for example `Service Centre till 12 oct 2026`.
   - Import them in date order through the app opened on the copy: Import → choose the folder.
2. **What each consolidated folder must show:**
   - 35 Imported.
   - Not needed for S001, S005, S038, S027, S028 and `00_`. S027/S028 staying Not needed proves Q6.
   - No Failed.
3. **Import one of the three folders a second time.** Expect every file Duplicate or Already present, and no change in `v_service_readings`. PASS/FAIL.
4. **If three real weekly exports are not available yet**, the screens can still be accepted on the data there is.
   - The timing target then has not been tested at the planned size. Write "timing at N landing rows, not 3 weeks" on the sheet, and repeat section 3 when the weeks exist.
   - Do not fake readings by renaming one week's files to other dates: the snapshot dates would be false, and Today and the freshness strip would show them.
5. **After the imports:**

```powershell
sqlcmd -S .\SQLEXPRESS -d EtpAccept_SVC110 -E -C -i scripts\service-centre\measure-service-growth.sql -W -s "|" -o after.txt
```

Record the total Service landing rows: about 130k after three consolidated weeks, by the design's estimate.

---

## 3. Timing: each screen on activation (target about 1 s)

For every tab (Today, Pending, Jobs (job list), Job history (one job), Claims, Parts, Money):

1. Start the app on the copy and open the Service section once, so that first-load costs are not counted.
2. Leave the tab, come back to it, and time from the click on the tab to the grid or cards being filled. Use a stopwatch, or a screen recording with frame times.
3. Do this three times and record the middle value.
4. **Also time the query the screen sends**, with its output thrown away:

```powershell
$cs = @('-S','.\SQLEXPRESS','-d','EtpAccept_SVC110','-E','-C','-o','NUL','-Q')
$jobCols = 'job_order_number,booking_date,jo_type,exported_status,brand,model,product_category,customer_name,guarantee,customer_type,edd,stage,stage_date,pending_at,spare_required,indent_date,srn_date,srn_to_store,repair_date,delivery_date,rwr_date,rwr_reason,wdc_date,wdc_number,wra_date,radc_number,claim_raised,spare_value,labour_charge,revenue_labour_charge,revenue_spare_charge,revenue_net_incl_tax,revenue_documents,tat_days,tat_repair_days,age_days,days_in_stage,is_overdue,is_open,last_reading_date,as_at'
$queries = [ordered]@{
  'job model (Today, Pending, Jobs)' = "SELECT $jobCols FROM dbo.v_service_job"
  'timeline (one job)'               = "SELECT job_order_number,snapshot_date,report_code,list_label,event_date,status_text,pending_store,document_number,amount,source_kind,import_file_id FROM dbo.v_service_job_timeline WHERE job_order_number = (SELECT MIN(job_order_number) FROM dbo.v_service_job WHERE stage='DELIVERED')"
  'claims'                           = 'SELECT claim_type,business_date,document_number,job_order_number,item_id,quantity,net_amount_inc_tax,ucp_value,account_number,report_code,snapshot_date FROM dbo.v_service_claims'
  'parts'                            = 'SELECT invoice_number,invoice_date,item_id,shipped_quantity,received_quantity,net_amount,grn_number,grn_date,received_date,status,from_location,days_open,snapshot_date FROM dbo.v_service_parts'
  'freshness'                        = 'SELECT report_code,snapshot_date,row_count,import_file_id,imported_utc,source_kind,is_latest FROM dbo.v_service_readings'
}
foreach ($q in $queries.GetEnumerator()) {
  $t = 1..3 | ForEach-Object { (Measure-Command { & sqlcmd @cs $q.Value }).TotalSeconds }
  '{0}: {1}' -f $q.Key, (($t | Sort-Object)[1].ToString('0.00'))
}
```

**Line per screen:** `Timing <tab>: baseline x.x s, after 3 weeks y.y s (query z.z s) - PASS (<= ~1 s) / FAIL`.

**What a FAIL means.** Lane sql measured `v_service_job` at about 2.5 s on live with 28k rows. Today, Pending and Jobs all read it in full, and Claims and Parts read parts of it, so expect those to be the slow ones. A FAIL is not a defect in the numbers; it is the SD-11 decision.

- If any screen is over about 1 s after the three weeks, the coordinator opens migration 0052 `service_job_index`: a table filled after each Service import batch by `refresh_service_job_index`, which `v_service_job` then reads (design 4.1 and 4.5).
- Write the slowest screen, its time and the landing row count on the sheet.
- 1.10.0 itself can still ship if Sagar accepts the wait. That is his call, and it goes on the sheet.

---

## 4. Common checks (every Service tab)

Run these first. Section 5 sets the variables the later queries use (`@asAt` and the others).

| # | Check | How | Expect |
|---|---|---|---|
| C1 | Rail | Left rail | Today, Import, Reports, Stock, **Service**, Settings; Reports no longer lists "Service centre" |
| C2 | Tabs | Service | Today, Pending, Jobs, Claims, Parts, Money; Service opens on Today |
| C3 | Store picker | Header | Disabled; every screen says "Service Centre AW330 - read only" |
| C4 | As-at line | Any tab | "Service data as at <date>" = `SELECT MAX(snapshot_date) FROM dbo.v_service_readings` |
| C5 | Freshness strip | Any tab | 9 chips, as in the query below (date, source kind, colour) |
| C6 | Status line (SD-10) | Open a Retail report, then a Service tab | The bottom status line no longer shows the Retail report's text |
| C7 | Viewer (Q13) | Sign in as a Viewer account, if the copy has one | All six Service tabs open; nothing asks for Store Manager. If the copy has no Viewer account, write "not run - covered by `TaskNavigationTests` (62 Viewer destinations)" |
| C8 | Export privacy | Every export of sections 5-7 | No column header contains Phone, Mobile, E-mail, Email, Landline, Contact or Address |
| C9 | Retail unchanged | Reports → DSR for a date before the copy's last date | Same totals as on the source database before 1.10.0 (note one date and the net sales) |

Freshness expectation (C5). `GETDATE()` is the PC's date, which is what the strip uses.

```sql
SET NOCOUNT ON;
WITH g(grp, ord, code) AS (SELECT * FROM (VALUES
  ('Jobs',1,'S002'),('Jobs',1,'S036'),('Jobs',1,'S037'),
  ('Status views',2,'S014'),('Status views',2,'S015'),('Status views',2,'S016'),('Status views',2,'S017'),('Status views',2,'S018'),
  ('Status views',2,'S031'),('Status views',2,'S032'),('Status views',2,'S033'),('Status views',2,'S034'),('Status views',2,'S035'),
  ('Pending lists',3,'S009'),('Pending lists',3,'S010'),
  ('SRN',4,'S011'),('SRN',4,'S012'),('SRN',4,'S013'),
  ('Money',5,'S003'),('Money',5,'S004'),
  ('Claims',6,'S023'),('Claims',6,'S024'),('Claims',6,'S025'),('Claims',6,'S026'),('Claims',6,'S039'),('Claims',6,'S040'),('Claims',6,'S041'),
  ('Parts',7,'S006'),('Parts',7,'S007'),('Parts',7,'S008'),
  ('Tests',8,'S030'),('Deftran',9,'S029')) v(grp, ord, code)),
latest AS (SELECT report_code, MAX(snapshot_date) d FROM dbo.v_service_readings GROUP BY report_code)
SELECT g.grp,
  CASE WHEN COUNT(l.d) = COUNT(*) THEN MIN(l.d) END chip_date,          -- NULL: "no export yet" / "some families never exported"
  DATEDIFF(day, MIN(l.d), CAST(GETDATE() AS date)) age_days,
  CASE WHEN COUNT(l.d) < COUNT(*) THEN 'no data'
       WHEN DATEDIFF(day, MIN(l.d), CAST(GETDATE() AS date)) > 14 THEN 'red'
       WHEN DATEDIFF(day, MIN(l.d), CAST(GETDATE() AS date)) > 7 THEN 'amber' ELSE 'none' END colour
FROM g LEFT JOIN latest l ON l.report_code = g.code
GROUP BY g.grp, g.ord ORDER BY g.ord;
-- source kind on the chip = source_kind of the oldest family's latest reading:
-- SELECT report_code, source_kind FROM dbo.v_service_readings WHERE is_latest = 1;
```

On the 9 Oct data, four groups (Status views, SRN, Claims, Tests) date from 29 Sep and five from 3 Oct. On 10 Oct the query gave amber for the four and none for the five. The 29 Sep groups turn red after 13 Oct, and the 3 Oct groups after 17 Oct, unless newer exports are imported. The screen must show the same colours as the query on the day of the test; that proves Q14.

---

## 5. Today

Open Service → Today without choosing a date.

```sql
SET NOCOUNT ON;
DECLARE @asAt date = (SELECT MAX(as_at) FROM dbo.v_service_job);
DECLARE @d date = @asAt;                                   -- Today's default business date (Q15)
DECLARE @m date = DATEFROMPARTS(YEAR(@d), MONTH(@d), 1);
SELECT @d business_date,
  SUM(CASE WHEN booking_date = @d THEN 1 ELSE 0 END) booked_today,
  SUM(CASE WHEN booking_date = @d AND jo_type NOT LIKE '%quick%' THEN 1 ELSE 0 END) booked_today_booking,
  SUM(CASE WHEN booking_date = @d AND jo_type LIKE '%quick%' THEN 1 ELSE 0 END) booked_today_quick,
  SUM(CASE WHEN booking_date BETWEEN @m AND @d THEN 1 ELSE 0 END) booked_month,
  SUM(CASE WHEN booking_date BETWEEN @m AND @d AND jo_type LIKE '%quick%' THEN 1 ELSE 0 END) booked_month_quick,
  SUM(CASE WHEN stage = 'DELIVERED' AND delivery_date = @d THEN 1 ELSE 0 END) delivered_today,
  SUM(CASE WHEN stage = 'DELIVERED' AND delivery_date BETWEEN @m AND @d THEN 1 ELSE 0 END) delivered_month,
  SUM(CASE WHEN stage = 'RWR' AND rwr_date = @d THEN 1 ELSE 0 END) rwr_today,
  SUM(CASE WHEN is_open = 1 AND stage IN ('ON_BENCH','INDENT_RAISED') THEN 1 ELSE 0 END) on_bench,
  SUM(CASE WHEN is_open = 1 AND stage = 'INDENT_RAISED' THEN 1 ELSE 0 END) indent_raised,
  SUM(CASE WHEN is_open = 1 AND stage IN ('ON_BENCH','INDENT_RAISED') AND edd < @asAt THEN 1 ELSE 0 END) edd_passed,
  SUM(CASE WHEN is_open = 1 AND stage = 'READY_FOR_DELIVERY' THEN 1 ELSE 0 END) ready,
  SUM(CASE WHEN is_open = 1 AND stage = 'IN_TRANSIT_BACK' THEN 1 ELSE 0 END) in_transit,
  SUM(CASE WHEN is_open = 1 AND age_days > 15 THEN 1 ELSE 0 END) open_over_15
FROM dbo.v_service_job;
SELECT tender, SUM(amount) amount FROM dbo.v_service_s004_daily
WHERE business_date = @d AND tender IN ('CASH','CARD','UPI') GROUP BY tender;          -- Collection card; total = the three
SELECT COUNT(*) entries, SUM(amount) amount FROM dbo.v_service_manual_money
WHERE is_service_money_shop = 1 AND business_date = @d
  AND field_code IN ('SERVICE_CASH','SERVICE_CARD','SERVICE_UPI');                     -- 0 = "not entered"
SELECT COUNT(DISTINCT COALESCE(document_number, N'')) documents, SUM(net_amount_inc_tax) value
FROM dbo.v_service_claims WHERE business_date BETWEEN @m AND @d;                         -- Claims raised this month
```

| # | Check | Expect |
|---|---|---|
| T1 | Default date (Q15) | The screen's date = `@d` (the newest Service snapshot), not today's calendar date; no all-zero cards on a day without an export |
| T2-T8 | Each card | Equals its column above (Booked with its two splits; Delivered with RWR; On the bench with indent and EDD passed; Ready with in transit; Collection with entered/not entered; Open over 15 days; Claims raised this month) |
| T9 | A date after the newest export | The screen says the date is past the latest export |
| T10 | Drill-down | Each card opens its list: Pending on that stage, Jobs, Money on that date, Claims. Back returns to Today |
| T11 | Export | One sheet with Card, Value, Detail, and the same values as the cards; period = the business date |

---

## 6. Pending

```sql
SET NOCOUNT ON;
SELECT stage, COUNT(*) jobs FROM dbo.v_service_job WHERE is_open = 1 GROUP BY stage;   -- one group per stage
SELECT COUNT(*) open_jobs,
  SUM(CASE WHEN edd IS NOT NULL THEN CASE WHEN edd < as_at THEN 1 ELSE 0 END
           ELSE CASE WHEN days_in_stage > CASE stage WHEN 'ON_BENCH' THEN 7 WHEN 'INDENT_RAISED' THEN 15 WHEN 'SRN_OUT' THEN 30
                                                   WHEN 'IN_TRANSIT_BACK' THEN 15 WHEN 'READY_FOR_DELIVERY' THEN 7 END
                     THEN 1 ELSE 0 END END) overdue,                                    -- Q4
  SUM(CASE WHEN age_days > 30 THEN 1 ELSE 0 END) over_30_days,
  SUM(CASE WHEN stage = 'IN_TRANSIT_BACK' THEN 1 ELSE 0 END) in_transit,
  SUM(CASE WHEN stage = 'INDENT_RAISED' THEN 1 ELSE 0 END) parts_awaited
FROM dbo.v_service_job WHERE is_open = 1;
SELECT CASE WHEN age_days <= 7 THEN '0-7' WHEN age_days <= 15 THEN '8-15' WHEN age_days <= 30 THEN '16-30'
            WHEN age_days <= 60 THEN '31-60' ELSE '60+' END band, COUNT(*) jobs
FROM dbo.v_service_job WHERE is_open = 1 AND age_days IS NOT NULL
GROUP BY CASE WHEN age_days <= 7 THEN '0-7' WHEN age_days <= 15 THEN '8-15' WHEN age_days <= 30 THEN '16-30'
              WHEN age_days <= 60 THEN '31-60' ELSE '60+' END;                       -- age band filter counts
SELECT COUNT(*) FROM dbo.v_service_pending_current WHERE report_code = 'S011';          -- open SRNs only (Q5); 10 on the 9 Oct data
SELECT stage, COUNT(*) FROM dbo.v_service_job
WHERE stage IN ('DELIVERED','RWR') OR (stage IN ('DC_ISSUED','RA_ISSUED') AND claim_raised = 1) GROUP BY stage;  -- never on the board
```

| # | Check | Expect |
|---|---|---|
| P1 | Groups | In stage order: Booked, On the bench, Indent raised, SRN out, Ready for delivery, In transit, DC issued, RA issued; each group's count = the first query |
| P2 | Five numbers | Open jobs, Overdue, Over 30 days, In transit, Parts awaited = the second query, and unchanged when a filter is applied |
| P3 | Closed jobs (Q3, Q8) | No DELIVERED or RWR row, and no DC/RA row with a claim raised. Search the board for one job from the last query: it is not there |
| P4 | Sort | Within a group, days in stage descending |
| P5 | Age band filter | Choosing each band gives the band count above |
| P6 | Overdue only | Row count = the overdue number |
| P7 | No reason invented | No "reason" column; spare required and pending at are shown |
| P8 | No customer name | No customer name column on the board |
| P9 | Drill-down | Double-click a row: Job history of that job; Back returns to the board |
| P10 | Export | One sheet per stage shown, with the grid's columns; ticking one stage gives one sheet |

---

## 7. Jobs, Claims, Parts, Money

### 7.1 Jobs list and Job history

```sql
SET NOCOUNT ON;
DECLARE @asAt date = (SELECT MAX(as_at) FROM dbo.v_service_job);
-- default "Closed in the last 30 days" (Q8)
SELECT COUNT(*) closed_30 FROM dbo.v_service_job
WHERE (stage IN ('DELIVERED','RWR') OR (stage IN ('DC_ISSUED','RA_ISSUED') AND claim_raised = 1))
  AND stage_date >= DATEADD(day, -30, as_at);
SELECT COUNT(*) all_jobs FROM dbo.v_service_job;                                    -- "All jobs"
-- TAT line for the default choice (Q2): delivered jobs only, Booking and Quick Billing apart
WITH shown AS (SELECT CASE WHEN jo_type LIKE '%quick%' THEN 'Quick Billing' ELSE 'Booking' END kind, tat_days
  FROM dbo.v_service_job WHERE stage = 'DELIVERED' AND tat_days IS NOT NULL AND stage_date >= DATEADD(day, -30, as_at))
SELECT DISTINCT kind, PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY tat_days) OVER (PARTITION BY kind) median_days,
  COUNT(*) OVER (PARTITION BY kind) delivered, SUM(CASE WHEN tat_days > 15 THEN 1 ELSE 0 END) OVER (PARTITION BY kind) over_15
FROM shown;
-- pick two jobs for the history check: one delivered Booking job, one open job
SELECT TOP 1 job_order_number, stage, booking_date, delivery_date, tat_days, tat_repair_days FROM dbo.v_service_job
WHERE stage = 'DELIVERED' AND jo_type NOT LIKE '%quick%' AND tat_days > 3 ORDER BY delivery_date DESC;
SELECT TOP 1 job_order_number, stage, stage_date, age_days, days_in_stage, edd, pending_at FROM dbo.v_service_job
WHERE is_open = 1 ORDER BY days_in_stage DESC;
-- then, for each of the two:  SELECT COUNT(*), MAX(snapshot_date) FROM dbo.v_service_job_timeline WHERE job_order_number = N'<job>';
```

| # | Check | Expect |
|---|---|---|
| J1 | Default choice | "Closed in the last 30 days"; row count = `closed_30` |
| J2 | All jobs | Row count = `all_jobs` |
| J3 | TAT line (Q2) | "Booking median X days (n delivered, k over 15 days) · Quick Billing median Y days (m delivered)" = the TAT query, with the medians rounded to whole days; Quick Billing never merged into the Booking figure |
| J4 | No S036/S037 choices (Q10) | "Show" offers no "Delivery report" or "Repair report" |
| J5 | History header | For both jobs: stage, booked on, Booking/Quick Billing, TAT (delivered job) or "Open N days · M in this stage" (open job), EDD and pending at, as in the query |
| J6 | Timeline (SD-01) | Row count = the timeline count; newest export first; no "Left the list" rows; S036/S037 rows labelled "Delivery-type jobs" / "Repair-type jobs" |
| J7 | Open from a grid | Typing the number and double-clicking it on Pending give the same history; Back works |
| J8 | Unknown number | "No Service family holds job … Check the number." |
| J9 | Exports | Jobs: rows shown, with the TAT line in the message, period = 30 days. History: timeline, header in the message, period = first to last export |

### 7.2 Claims

```sql
SET NOCOUNT ON;
SELECT DATEFROMPARTS(YEAR(business_date), MONTH(business_date), 1) claim_month, claim_type,
  COUNT(DISTINCT document_number) documents, COUNT(*) lines, COUNT(DISTINCT job_order_number) jobs,
  SUM(net_amount_inc_tax) net_incl_tax, SUM(ucp_value) ucp_value
FROM dbo.v_service_claims
GROUP BY DATEFROMPARTS(YEAR(business_date), MONTH(business_date), 1), claim_type
ORDER BY claim_month DESC, claim_type;
-- union by document (Q12): no document counted from two report families; expect 0 rows
SELECT claim_type, document_number FROM dbo.v_service_claims WHERE document_number IS NOT NULL
GROUP BY claim_type, document_number HAVING COUNT(DISTINCT report_code) > 1;
-- the 1 Jul 2026 join: lines per source on that date
SELECT claim_type, report_code, COUNT(*) lines FROM dbo.v_service_claims WHERE business_date = '2026-07-01' GROUP BY claim_type, report_code;
-- not yet claimed
SELECT stage, COUNT(*) jobs FROM dbo.v_service_job WHERE stage IN ('DC_ISSUED','RA_ISSUED') AND claim_raised = 0 GROUP BY stage;
-- GPRC gap (Q11)
SELECT MAX(CASE WHEN report_code IN ('S023','S041') THEN snapshot_date END) latest_gprc,
       MAX(CASE WHEN report_code IN ('S014','S016') THEN snapshot_date END) latest_dc_ra
FROM dbo.v_service_readings;
```

| # | Check | Expect |
|---|---|---|
| CL1 | By month and claim type | Every row = the first query (for the month range chosen; widen the range to all months for this check) |
| CL2 | No double count (Q12) | The second query returns 0 rows; the 1 Jul 2026 lines are counted once |
| CL3 | Not yet claimed | WDC due / WRA due = the DC_ISSUED / RA_ISSUED counts; the list ignores the month range |
| CL4 | GPRC gap (Q11) | When `latest_gprc` < `latest_dc_ra`, the bold warning names both dates; otherwise there is no warning. On the 9 Oct data the warning is shown |
| CL5 | Raised only (Q9) | The note says no export carries settlement; the words "outstanding" and "unsettled" appear nowhere on the screen or in its exports |
| CL6 | Drill-down | A month row shows its lines; a line or a waiting job opens Job history; a line with no job number says so |
| CL7 | Exports | "Service claims by month" / "Service claim lines" / "Service claims not yet raised" = the grid on screen; period = the month range |

### 7.3 Parts

```sql
SET NOCOUNT ON;
DECLARE @asAt date = (SELECT MAX(snapshot_date) FROM dbo.v_service_readings);
DECLARE @m date = DATEFROMPARTS(YEAR(@asAt), MONTH(@asAt), 1);
WITH inv AS (
  SELECT invoice_number, MAX(CASE WHEN status = 'Open' THEN 1 ELSE 0 END) is_open, SUM(net_amount) net_amount,
         MAX(days_open) days_open, MAX(grn_date) grn_date
  FROM dbo.v_service_parts WHERE invoice_number IS NOT NULL GROUP BY invoice_number)
SELECT SUM(is_open) open_invoices, SUM(CASE WHEN is_open = 1 THEN net_amount END) open_value,
  MAX(CASE WHEN is_open = 1 THEN days_open END) oldest_open_days,
  SUM(CASE WHEN is_open = 0 AND grn_date BETWEEN @m AND @asAt THEN 1 ELSE 0 END) received_this_month,
  COUNT(*) invoices
FROM inv;
SELECT COUNT(*) jobs_waiting FROM dbo.v_service_job WHERE is_open = 1 AND stage = 'INDENT_RAISED';
SELECT COUNT(*) git_lines_30 FROM dbo.v_service_parts_transit WHERE business_date > DATEADD(day, -30, @asAt) AND business_date <= @asAt;
SELECT snapshot_date, items, quantity, value FROM dbo.v_service_stock_summary;
```

| # | Check | Expect |
|---|---|---|
| PA1 | Five numbers | Open invoices (count, value), oldest open (days), received this month, jobs waiting, GIT lines in 30 days = the queries |
| PA2 | Invoice grid | Row count = `invoices`; open first, oldest first; an open invoice shows "Open", a received one "Closed" |
| PA3 | Waiting panel | Row count = `jobs_waiting`; the sentence saying there is no link between a waiting job and an invoice is shown |
| PA4 | Stock line | = `v_service_stock_summary` |
| PA5 | Drill-down | An invoice shows its lines; a waiting job opens Job history |
| PA6 | Exports | The invoice grid, and the waiting list through its own button |

### 7.4 Money

```sql
SET NOCOUNT ON;
DECLARE @to date = (SELECT MAX(snapshot_date) FROM dbo.v_service_readings WHERE report_code = 'S004');
DECLARE @from date = DATEADD(day, -30, @to);
SELECT @from default_from, @to default_to;
SELECT business_date, tender, amount FROM dbo.v_service_s004_daily
WHERE business_date BETWEEN @from AND @to AND tender IN ('CASH','CARD','UPI') ORDER BY business_date, tender;
SELECT business_date, tender, SUM(amount) amount FROM dbo.v_service_manual_money
WHERE is_service_money_shop = 1 AND tender IS NOT NULL AND business_date BETWEEN @from AND @to
GROUP BY business_date, tender ORDER BY business_date, tender;
SELECT COUNT(*) other_shop_entries FROM dbo.v_service_manual_money
WHERE is_service_money_shop = 0 AND business_date BETWEEN @from AND @to;               -- Q7: listed apart, never added
```

| # | Check | Expect |
|---|---|---|
| M1 | Default range | From/To = `default_from` / `default_to` until a date is picked; after a pick, Refresh keeps the pick |
| M2 | Grid | S004 and manual per date and tender, and the difference = S004 − manual, as in the queries; nothing corrected |
| M3 | Other shops (Q7) | "Service entries at other shops (not added)" count = `other_shop_entries`; not in the totals; WDC not compared |
| M4 | Export | The comparison grid for the chosen range |
| M5 | SD-08 (optional) | On a copy with no Service reading (a backup from before 9 Oct, if one is at hand), the screen shows the manual entries and says "No S004 reading is imported yet, so only the manual entries are shown." Otherwise write "not run - covered by `ServiceScreenViewTests`" |

---

## 8. What each check proves (decision 25, 10 Oct 2026)

| Q | Answer (decision 25) | Proved by |
|---|---|---|
| 1 | The trimmed exported job order number is the one key | `SELECT COUNT(*) FROM dbo.v_service_job WHERE job_order_number NOT LIKE 'JOAW330%' OR LEN(job_order_number) NOT IN (15,16)` = 0; J5/J7 (the same job opens from every grid) |
| 2 | TAT headline = booking → delivered for Booking jobs; Quick Billing separate | J3, T2 (split), J5 (booking → repaired in the header) |
| 3 | Pending = not DELIVERED/RWR; claimed DC/RA = closed by claim | P1, P3, CL3 |
| 4 | Overdue = EDD passed, else per-stage limits 7/15/30/15/7 | P2 (overdue), P6 |
| 5 | SRN closed when received date, repaired date or "Received" status | Pending query `report_code='S011'` (10 on the 9 Oct data, where 0048 listed 141); stage SRN_OUT counts on P1 |
| 6 | No S027/S028 import | Section 2, step 2 (both Not needed) |
| 7 | Service money Titan-only | M2, M3 |
| 8 | Delivered off the board; Jobs list last 30 days with a show-all switch | P3, J1, J2 |
| 9 | Claims raised only | CL5, T8 wording |
| 10 | S036/S037 are job lists | J4, J6 labels |
| 11 | GPRC gap export / warning | CL4 (and the gap disappears once a GPRC CLAIM export up to the DC/RA date is imported) |
| 12 | Claims union by document, new header wins | CL2 |
| 13 | Viewer+ on every Service screen | C7 |
| 14 | Freshness amber after 7, red after 14 days | C5 |
| 15 | Today defaults to the latest snapshot date | T1 |

Also covered:

- SD-01 by J6, SD-02 by Q5's check, SD-04 by J4/J6, SD-05 by P1 (the Booked group), SD-08 by M5, SD-09 by J9/T11/CL7, SD-10 by C6.
- SD-11 by section 3, which decides whether 0052 is needed.
- Privacy by 1.4 and C8.

---

## 9. Evidence sheet (copy into the evidence folder and fill in)

```
Service UI 1.10.0 acceptance - <date>, <who>, option A (VM) / B (Workpc scratch EtpAccept_SVC110)
Source backup: <file>  SHA-256 <...>  size <...>  (staging 9 Oct / live <date>)
1.10.0 build: <commit>, installer <file> (A) or release folder (B)
1.3 restore + CHECKDB ................................. PASS/FAIL  migrations before: 48
1.4 migrations 0049, 0050 / 8 views / privacy 0 rows / 72 grants  PASS/FAIL
2   three weekly readings: <folder names>  35 imported each, no Failed  PASS/FAIL ; re-import Duplicate  PASS/FAIL
    landing rows before <n>  after <n>   (or: "timing at N rows, not 3 weeks")
3   timing (baseline / after / query):
    Today <..>  Pending <..>  Jobs <..>  Job history <..>  Claims <..>  Parts <..>  Money <..>   PASS/FAIL each
    SD-11 decision: none / 0052 needed (slowest <tab> <s>)
4   C1..C9 ................................................ PASS/FAIL each
5   T1..T11 ............................................... PASS/FAIL each (numbers: screen = SQL)
6   P1..P10 ............................................... PASS/FAIL each
7   J1..J9, CL1..CL7, PA1..PA6, M1..M5 ..................... PASS/FAIL each
8   decision 25 Q1..Q15 ................................... all proved / list the open ones
Open items: ...
Cleanup: scratch database EtpAccept_SVC110 kept / dropped by Sagar on <date> (option B only)
```

A FAIL row names the screen, the expected number (SQL), the screen's number and a screenshot file in the evidence folder. A wrong number is a 1.10.0 defect for the coordinator. A slow screen is the SD-11 decision (section 3).
