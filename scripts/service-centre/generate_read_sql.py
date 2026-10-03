"""Generate section C_SERVICE_READ of migration 0048: the Service read-rule views.

Service interim (S-2), decision 15, 3 Oct 2026. Lane L4.

Writes the eleven views (dbo.v_service_families, v_service_reading_windows, v_service_readings,
v_service_datelog_readings, v_service_status_view_rows, v_service_job_readings, v_service_job_status_current,
v_service_pending_current, v_service_job_list_events, v_service_s004_daily, v_service_money_changes), each
CREATE OR ALTER through EXEC, then GRANT SELECT to the three roles and DENY writes. The read rules mirror
ServiceInterimFamilies.ReadRules (L0); every column name is checked against the frozen
scripts/service-centre/families.spec.json before anything is written.

Only the text between the C_SERVICE_READ markers of 0048 is replaced; nothing else in the file changes, and
the file's line endings are kept. The output is deterministic. ServiceReadModelTextTests checks the result.

  python scripts/service-centre/generate_read_sql.py            # rewrite section C in place
  python scripts/service-centre/generate_read_sql.py --check    # exit 1 if section C is not current
  python scripts/service-centre/generate_read_sql.py --spec <json> --migration <sql>   # other inputs

The script never reads real workbooks and never connects to a database.
"""
import argparse
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SPEC = ROOT / "scripts" / "service-centre" / "families.spec.json"
MIGRATION = ROOT / "database" / "migrations" / "0048_service_centre_interim.sql"
SECTION = "C_SERVICE_READ"
BEGIN_MARKER = f"-- >>> {SECTION} begin"
END_MARKER = f"-- <<< {SECTION} end"


parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
parser.add_argument("--spec", default=str(SPEC))
parser.add_argument("--migration", default=str(MIGRATION))
parser.add_argument("--check", action="store_true", help="exit 1 if the section is not what the spec gives")
ARGS = parser.parse_args()
spec = {f["FamilyCode"]: f for f in json.load(open(ARGS.spec, encoding="utf-8"))}

IMPORTABLE = ["S002", "S003", "S004"] + [f"S{n:03d}" for n in range(6, 27)] + [f"S{n:03d}" for n in range(29, 38)] + ["S039", "S040"]
assert len(IMPORTABLE) == 35

DATELOG = {"S003": "trans_date", "S004": "billingdate", "S007": "grn_date", "S008": "grn_date", "S013": "stm_date",
           "S019": "repairdate", "S022": "invoice_date", "S023": "transdate", "S024": "transdate", "S025": "transdate",
           "S026": "transdate", "S029": "repair_date", "S039": "transaction_date", "S040": "transaction_date"}
STATE = ["S006", "S009", "S010"]
STATUS = {  # code: (label, lifecycle rank, status date column)
    "S032": ("PR", 1, "jodate"), "S015": ("IR", 2, "indentdate"), "S033": ("SRN", 3, "srnissueddate"),
    "S035": ("SRNINV", 4, "srnreturndate"), "S014": ("DC", 5, "wdcdate"), "S016": ("RA", 6, "wradate"),
    "S034": ("REPAIRED", 7, "jorepairdate"), "S017": ("RWR", 8, "normalrwrdate"), "S031": ("PD", 9, "jorepairdate"),
    "S018": ("DELIVERED", 10, "deliverydate")}
JOBLIST_OTHER = {  # code: (job column, status date column, list label)
    "S002": ("job_order_no", "created_date", "Job booking"), "S011": ("joborder_number", "srn_date", "SRN status"),
    "S012": ("jonumber", "srnrepaireddate", "SRN history"), "S020": ("jobordernumber", "radate", "Replacement"),
    "S021": ("jobordernumber", "dcdate", "Depreciation"), "S030": ("job_order_number", "running_test_date", "Running tests"),
    "S036": ("job_order_no", "created_date", "Delivery report"), "S037": ("job_order_no", "created_date", "Repair report")}
STATE_JOB = {"S009": ("jonumber", "jodate", "Pending repair"), "S010": ("jonumber", "jodate", "Pending delivery")}
JOBLIST = sorted(list(STATUS) + list(JOBLIST_OTHER))
assert len(DATELOG) + len(STATE) + len(JOBLIST) == 35
assert sorted(list(DATELOG) + STATE + JOBLIST) == IMPORTABLE

def col(code, name):
    cols = {c["CanonicalField"]: c["DataType"] for c in spec[code]["Columns"]}
    assert name in cols, (code, name)
    return cols[name]

for code, c in DATELOG.items(): assert col(code, c) == "Date", (code, c)
for code, (_, _, c) in STATUS.items(): assert col(code, c) == "Date"; assert col(code, "jobordernumber")
for code, (j, d, _) in {**JOBLIST_OTHER, **STATE_JOB}.items(): col(code, j); assert col(code, d) == "Date", (code, d)

def table(code): return f"dbo.etp_landing_{code.lower()}"
def job(expr): return f"NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),{expr}))),N'''')"
def txt(expr, n=200): return f"CONVERT(nvarchar({n}),{expr})"

def labels():
    rows = []
    for code in IMPORTABLE:
        if code in DATELOG: rule, label = "DateLog", None
        elif code in STATE: rule, label = "StateSnapshot", None
        else: rule, label = "JobList", None
        status_label, rank = (STATUS[code][0], STATUS[code][1]) if code in STATUS else (None, None)
        list_label = (f"Status {STATUS[code][0]}" if code in STATUS else JOBLIST_OTHER.get(code, (None, None, None))[2]
                      or STATE_JOB.get(code, (None, None, None))[2])
        if code == "S006": list_label = "Closing stock"
        if list_label is None: list_label = spec[code]["Name"]
        q = lambda v: "NULL" if v is None else (str(v) if isinstance(v, int) else f"''{v}''")
        list_key = {"S009": "PENDING_REPAIR", "S010": "PENDING_DELIVERY", "S011": "SRN_STATUS"}.get(code)
        rows.append(f" ({q(code)},{q(rule)},{q(DATELOG.get(code))},{q(list_label)},{q(list_key)},{q(status_label)},{q(rank)})")
    return ",\n".join(rows)

codes_in = ",".join(f"''{c}''" for c in IMPORTABLE)
P = []
def emit(s): P.append(s)

emit("""-- Owner: lane L4. Read-rule views over the Service landing tables (design section 4).
-- Every view reads only live readings: import_files with is_superseded = 0, data_truth_version = 1, a Completed
-- batch, and a report_code of the 35 importable Service families. A reading is one import file; its snapshot date is
-- period_end (the folder date of a consolidated workbook, the window end in a raw export's name). Rows are chosen by
-- the family's read rule (ServiceInterimFamilies.ReadRule), never by the import order:
--   StateSnapshot (S006, S009, S010): rows of the reading with the greatest snapshot date.
--   DateLog(column): for each business date D, rows dated D of the reading with the greatest snapshot date whose
--     window [least row date, snapshot date] contains D. A restated row therefore replaces its old version.
--   JobList(job column): per family and job, the rows of the reading with the greatest snapshot date holding the job.
-- Ties at the same snapshot date: a reading that holds rows beats one that holds none (an "Already present" import
-- keeps an import_files row but lands no rows), then the greater import_file_id.
-- No view exposes a phone, e-mail or address column; a customer name only where the read contract has CustomerName.
-- Nothing here writes: a job leaving a list is history (v_service_job_list_events), never a review item.
-- Indexes: the landing tables already carry IX_etp_landing_snnn_file on (import_file_id); no other index is added.
-- Each view is CREATE OR ALTER through EXEC, granted SELECT to the three roles and denied writes, as 0041 does.
""")

# 1. constants
emit(f"""-- The read rule, list label, pending-list key (ServicePendingLists) and status-view constants per importable family
-- (ServiceInterimFamilies, L0).
-- Lifecycle rank breaks a same-date tie between status views only: PR < IR < SRN < SRNINV < DC < RA < REPAIRED < RWR
-- < PD < DELIVERED.
EXEC(N'CREATE OR ALTER VIEW dbo.v_service_families AS
SELECT v.report_code,v.read_rule,v.date_column,v.list_label,v.pending_list,v.status_label,v.lifecycle_rank
FROM (VALUES
{labels()}
) v(report_code,read_rule,date_column,list_label,pending_list,status_label,lifecycle_rank)');
""")

# 2. reading windows
stats = []
for i, code in enumerate(IMPORTABLE):
    names = (" report_code", " row_count", " least_date", " greatest_date") if i == 0 else ("", "", "", "")
    if code in DATELOG:
        d = DATELOG[code]
        stats.append(f"  SELECT ''{code}''{names[0]},import_file_id,COUNT_BIG(*){names[1]},MIN({d}){names[2]},MAX({d}){names[3]} FROM {table(code)} GROUP BY import_file_id")
    else:
        stats.append(f"  SELECT ''{code}''{names[0]},import_file_id,COUNT_BIG(*){names[1]},CONVERT(date,NULL){names[2]},CONVERT(date,NULL){names[3]} FROM {table(code)} GROUP BY import_file_id")
emit(f"""-- One row per live Service reading: its snapshot date, row count and, for a DateLog family, its window
-- [window_from, window_to]. window_from is the least row date; window_to is the snapshot date, or a later row date
-- when a reading holds rows dated after its snapshot date (so such rows are never hidden).
EXEC(N'CREATE OR ALTER VIEW dbo.v_service_reading_windows AS
WITH live AS (
  SELECT f.import_file_id,f.report_code,COALESCE(f.period_end,f.business_date) snapshot_date
  FROM dbo.import_files f JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
  WHERE f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Completed''
    AND f.report_code IN({codes_in})
), stats AS (
{chr(10).join(('  UNION ALL ' + s.strip()) if i else s for i, s in enumerate(stats))}
)
SELECT l.import_file_id,l.report_code,r.read_rule,l.snapshot_date,COALESCE(s.row_count,0) row_count,
  s.least_date window_from,
  CASE WHEN s.least_date IS NULL THEN NULL WHEN s.greatest_date>l.snapshot_date THEN s.greatest_date ELSE l.snapshot_date END window_to
FROM live l JOIN dbo.v_service_families r ON r.report_code=l.report_code
LEFT JOIN stats s ON s.import_file_id=l.import_file_id AND s.report_code=l.report_code
WHERE l.snapshot_date IS NOT NULL');
""")

# 3. v_service_readings
emit("""-- 1. The refresh log: every live Service reading, with is_latest = 1 for the latest reading of each family. It is
-- also the StateSnapshot rule (the is_latest reading) and the base of the growth measurement. source_kind is
-- CONSOLIDATED for a builder workbook named after its code (S009_PendingRepair.xlsx), otherwise RAW.
EXEC(N'CREATE OR ALTER VIEW dbo.v_service_readings AS
SELECT w.report_code,w.read_rule,w.snapshot_date,w.window_from,w.window_to,w.import_file_id,w.row_count,
  COALESCE(b.completed_utc,b.started_utc) imported_utc,
  CONVERT(varchar(12),CASE WHEN f.original_file_name LIKE ''S[0-9][0-9][0-9][^0-9]%'' THEN ''CONSOLIDATED'' ELSE ''RAW'' END) source_kind,
  CONVERT(bit,CASE WHEN ROW_NUMBER() OVER(PARTITION BY w.report_code
    ORDER BY w.snapshot_date DESC,CASE WHEN w.row_count>0 THEN 1 ELSE 0 END DESC,w.import_file_id DESC)=1 THEN 1 ELSE 0 END) is_latest
FROM dbo.v_service_reading_windows w
JOIN dbo.import_files f ON f.import_file_id=w.import_file_id
JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id');
""")

# 4. datelog readings helper
dates = []
for i, (code, d) in enumerate(sorted(DATELOG.items())):
    head = "  " if i == 0 else "  UNION ALL "
    alias = " report_code" if i == 0 else ""
    alias2 = " business_date" if i == 0 else ""
    dates.append(f"{head}SELECT ''{code}''{alias},import_file_id,{d}{alias2} FROM {table(code)} WHERE {d} IS NOT NULL GROUP BY import_file_id,{d}")
emit(f"""-- The DateLog rule: for each family and business date D that any live reading holds, every live reading whose window
-- contains D, ranked by snapshot date (reading_rank 1 wins, 2 is the next-best covering reading).
EXEC(N'CREATE OR ALTER VIEW dbo.v_service_datelog_readings AS
WITH dated AS (
{chr(10).join(dates)}
), held AS (
  SELECT DISTINCT d.report_code,d.business_date
  FROM dated d JOIN dbo.v_service_reading_windows w ON w.import_file_id=d.import_file_id AND w.report_code=d.report_code
)
SELECT h.report_code,h.business_date,w.import_file_id,w.snapshot_date,
  ROW_NUMBER() OVER(PARTITION BY h.report_code,h.business_date ORDER BY w.snapshot_date DESC,w.import_file_id DESC) reading_rank
FROM held h JOIN dbo.v_service_reading_windows w ON w.report_code=h.report_code AND w.read_rule=''DateLog''
  AND h.business_date BETWEEN w.window_from AND w.window_to');
""")

# 5. status view rows
srows = []
for i, code in enumerate(sorted(STATUS)):
    head = "" if i == 0 else "UNION ALL "
    sd = STATUS[code][2]
    if i == 0:
        srows.append(f"{head}SELECT ''{code}'' report_code,import_file_id,{job('jobordernumber')} job_order_number,{sd} status_date,jodate job_date,edd,\n  {txt('brandname')} brand,{txt('variantnumber')} model,{txt('clusterid')} product_category,{txt('customername')} customer_name,sparevalue spare_value,labourcharge labour_charge\nFROM {table(code)}")
    else:
        srows.append(f"{head}SELECT ''{code}'',import_file_id,{job('jobordernumber')},{sd},jodate,edd,\n  {txt('brandname')},{txt('variantnumber')},{txt('clusterid')},{txt('customername')},sparevalue,labourcharge\nFROM {table(code)}")
emit(f"""-- Line rows of the ten status views (S014-S018, S031-S035) with the columns the screens use. status_date is the
-- row's own date for that status (information only): DC wdcdate, IR indentdate, RA wradate, RWR normalrwrdate,
-- DELIVERED deliverydate, PD and REPAIRED jorepairdate, PR jodate, SRN srnissueddate, SRNINV srnreturndate.
-- Model is the variant number and product category the cluster id (the status views carry no other model column).
EXEC(N'CREATE OR ALTER VIEW dbo.v_service_status_view_rows AS
{chr(10).join(srows)}');
""")

# 6. job readings
jr = ["  SELECT report_code,import_file_id,job_order_number,status_date FROM dbo.v_service_status_view_rows"]
for code in sorted({**JOBLIST_OTHER, **STATE_JOB}):
    j, d, _ = {**JOBLIST_OTHER, **STATE_JOB}[code]
    jr.append(f"  UNION ALL SELECT ''{code}'',import_file_id,{job(j)},{d} FROM {table(code)}")
emit(f"""-- 2. One row per (job, family, reading) for every JobList family and for S009/S010, mapping each family's own job
-- column; the base of the job status, pending and event views. status_date is the family's own date for the job
-- (S002/S036/S037 created_date, S011 srn_date, S012 srnrepaireddate, S020 radate, S021 dcdate, S030
-- running_test_date, S009/S010 jodate, the status views as above); line_count counts its line rows.
EXEC(N'CREATE OR ALTER VIEW dbo.v_service_job_readings AS
WITH job_rows AS (
{chr(10).join(jr)}
)
SELECT j.job_order_number,j.report_code,w.snapshot_date,j.import_file_id,
  MIN(j.status_date) first_status_date,MAX(j.status_date) status_date,COUNT_BIG(*) line_count
FROM job_rows j JOIN dbo.v_service_reading_windows w ON w.import_file_id=j.import_file_id AND w.report_code=j.report_code
WHERE j.job_order_number IS NOT NULL
GROUP BY j.job_order_number,j.report_code,w.snapshot_date,j.import_file_id');
""")

# 7. job status current
emit("""-- 3. One row per job: its current status among the ten status views under the JobList rule. Per view, the job's
-- winning reading is the latest reading that holds it; the current status is the view whose winning reading is
-- latest, a tie broken by the lifecycle rank (the later stage wins). Raw status exports are period-filtered event
-- lists and consolidated views their accumulated union; both mean "the job reached this status", so the latest wins.
-- Money is summed over the job's line rows in the winning reading; other_lists counts the other status views that
-- ever held the job.
EXEC(N'CREATE OR ALTER VIEW dbo.v_service_job_status_current AS
WITH per_view AS (
  SELECT r.job_order_number,r.report_code,r.snapshot_date,r.import_file_id,r.status_date,
    ROW_NUMBER() OVER(PARTITION BY r.job_order_number,r.report_code ORDER BY r.snapshot_date DESC,r.import_file_id DESC) view_rank
  FROM dbo.v_service_job_readings r JOIN dbo.v_service_families c ON c.report_code=r.report_code AND c.status_label IS NOT NULL
), winners AS (
  SELECT p.job_order_number,p.report_code,p.snapshot_date,p.import_file_id,p.status_date,c.status_label,c.lifecycle_rank,
    COUNT(*) OVER(PARTITION BY p.job_order_number) views_held,
    ROW_NUMBER() OVER(PARTITION BY p.job_order_number ORDER BY p.snapshot_date DESC,c.lifecycle_rank DESC,p.import_file_id DESC) job_rank
  FROM per_view p JOIN dbo.v_service_families c ON c.report_code=p.report_code
  WHERE p.view_rank=1
), details AS (
  SELECT s.report_code,s.import_file_id,s.job_order_number,MIN(s.job_date) job_date,MAX(s.edd) edd,MAX(s.brand) brand,
    MAX(s.model) model,MAX(s.product_category) product_category,MAX(s.customer_name) customer_name,
    SUM(s.spare_value) spare_value,SUM(s.labour_charge) labour_charge,COUNT_BIG(*) line_count
  FROM dbo.v_service_status_view_rows s
  WHERE s.job_order_number IS NOT NULL
  GROUP BY s.report_code,s.import_file_id,s.job_order_number
)
SELECT w.job_order_number,w.report_code status_view,w.status_label,w.lifecycle_rank,w.status_date,
  d.job_date,d.edd,d.brand,d.model,d.product_category,d.customer_name,d.spare_value,d.labour_charge,
  CONVERT(int,d.line_count) lines,w.snapshot_date,w.import_file_id,CONVERT(int,w.views_held-1) other_lists
FROM winners w JOIN details d ON d.report_code=w.report_code AND d.import_file_id=w.import_file_id
  AND d.job_order_number=w.job_order_number
WHERE w.job_rank=1');
""")

# 8. pending current
emit(f"""-- 4. Pending lists: S009 (Pending repair) and S010 (Pending delivery) under the StateSnapshot rule (the is_latest
-- reading), plus S011 (SRN status) under the JobList rule. One row per job; age_days = snapshot date - job date.
-- S011 has no model column, and its pending store is to_store (where the SRN went).
-- S011 keeps the JobList rule (ServicePendingLists.SrnStatus: latest reading per job) because S011 is a period
-- export: a raw S011 lists only the SRNs of its own window, so restricting it to the latest reading would drop open
-- SRNs of earlier windows. An SRN that a later reading no longer holds therefore stays listed with its own snapshot
-- date. Reading closure from the row itself (to_status, srn_received_date) needs a decided rule; it is open to the
-- coordinator.
EXEC(N'CREATE OR ALTER VIEW dbo.v_service_pending_current AS
WITH state_rows AS (
  SELECT ''S009'' report_code,import_file_id,{job('jonumber')} job_order_number,jodate job_date,{txt('brand')} brand,
    {txt('variantnumber')} model,{txt('customername')} customer_name,{txt('pendingstore')} pending_store
  FROM {table('S009')}
  UNION ALL SELECT ''S010'',import_file_id,{job('jonumber')},jodate,{txt('brand')},
    {txt('variantnumber')},{txt('customername')},{txt('pendingstore')}
  FROM {table('S010')}
), state_lists AS (
  SELECT s.report_code,s.job_order_number,MIN(s.job_date) job_date,MAX(s.brand) brand,MAX(s.model) model,
    MAX(s.customer_name) customer_name,MAX(s.pending_store) pending_store,r.snapshot_date,r.import_file_id
  FROM state_rows s JOIN dbo.v_service_readings r ON r.import_file_id=s.import_file_id AND r.report_code=s.report_code AND r.is_latest=1
  WHERE s.job_order_number IS NOT NULL
  GROUP BY s.report_code,s.job_order_number,r.snapshot_date,r.import_file_id
), srn_winners AS (
  SELECT job_order_number,snapshot_date,import_file_id,
    ROW_NUMBER() OVER(PARTITION BY job_order_number ORDER BY snapshot_date DESC,import_file_id DESC) job_rank
  FROM dbo.v_service_job_readings WHERE report_code=''S011''
), srn_lists AS (
  SELECT ''S011'' report_code,w.job_order_number,MIN(s.joborder_date) job_date,MAX({txt('s.brand')}) brand,
    CONVERT(nvarchar(200),NULL) model,MAX({txt('s.customer_name')}) customer_name,MAX({txt('s.to_store')}) pending_store,
    w.snapshot_date,w.import_file_id
  FROM srn_winners w JOIN {table('S011')} s ON s.import_file_id=w.import_file_id
    AND {job('s.joborder_number')}=w.job_order_number
  WHERE w.job_rank=1
  GROUP BY w.job_order_number,w.snapshot_date,w.import_file_id
), lists AS (
  SELECT * FROM state_lists UNION ALL SELECT * FROM srn_lists
)
SELECT f.pending_list [list],l.report_code,f.list_label,l.job_order_number,l.job_date,
  CASE WHEN l.job_date IS NULL THEN NULL ELSE DATEDIFF(day,l.job_date,l.snapshot_date) END age_days,
  l.brand,l.model,l.customer_name,l.pending_store,l.snapshot_date,l.import_file_id
FROM lists l JOIN dbo.v_service_families f ON f.report_code=l.report_code');
""")

# 9. events
emit("""-- 5. Job list events, information only (nothing is written). State lists S009/S010: each reading is compared with the
-- previous reading of the same list (every live reading date counts, an empty one too): FirstSeen, Reappeared,
-- LeftList (dated at the first reading without the job; previous_snapshot_date keeps the last reading with it) and
-- StillListed (held by the latest reading). JobList families, per (job, family), with the family's own status dates:
-- FirstSeen at the first reading holding the job; then StillListed when the family's latest reading still holds it
-- (and an earlier one did), or LeftList dated at the family's first reading after the last one holding it
-- (previous_snapshot_date keeps that last reading). A raw status export lists only its own window, so LeftList there
-- means "no later reading of the family holds the job", never that the job is still on the list.
-- This is the rule "a job leaving a list is history".
EXEC(N'CREATE OR ALTER VIEW dbo.v_service_job_list_events AS
WITH state_dates AS (
  SELECT d.report_code,d.snapshot_date,
    DENSE_RANK() OVER(PARTITION BY d.report_code ORDER BY d.snapshot_date) reading_seq,
    COUNT(*) OVER(PARTITION BY d.report_code) reading_count
  FROM (SELECT DISTINCT report_code,snapshot_date FROM dbo.v_service_reading_windows WHERE report_code IN(''S009'',''S010'')) d
), presence AS (
  SELECT r.report_code,r.job_order_number,d.reading_seq,d.reading_count,d.snapshot_date,MAX(r.status_date) status_date
  FROM dbo.v_service_job_readings r JOIN state_dates d ON d.report_code=r.report_code AND d.snapshot_date=r.snapshot_date
  GROUP BY r.report_code,r.job_order_number,d.reading_seq,d.reading_count,d.snapshot_date
), sequenced AS (
  SELECT p.*,
    LAG(p.reading_seq) OVER(PARTITION BY p.report_code,p.job_order_number ORDER BY p.reading_seq) previous_seq,
    LAG(p.snapshot_date) OVER(PARTITION BY p.report_code,p.job_order_number ORDER BY p.reading_seq) previous_seen,
    LEAD(p.reading_seq) OVER(PARTITION BY p.report_code,p.job_order_number ORDER BY p.reading_seq) next_seq
  FROM presence p
), state_events AS (
  SELECT s.job_order_number,s.report_code,CONVERT(varchar(12),CASE WHEN s.previous_seq IS NULL THEN ''FirstSeen'' ELSE ''Reappeared'' END) event_kind,
    s.snapshot_date,s.previous_seen previous_snapshot_date,s.status_date
  FROM sequenced s WHERE s.previous_seq IS NULL OR s.previous_seq<s.reading_seq-1
  UNION ALL
  SELECT s.job_order_number,s.report_code,''LeftList'',n.snapshot_date,s.snapshot_date,s.status_date
  FROM sequenced s JOIN state_dates n ON n.report_code=s.report_code AND n.reading_seq=s.reading_seq+1
  WHERE s.next_seq IS NULL OR s.next_seq>s.reading_seq+1
  UNION ALL
  SELECT s.job_order_number,s.report_code,''StillListed'',s.snapshot_date,s.previous_seen,s.status_date
  FROM sequenced s WHERE s.reading_seq=s.reading_count AND s.previous_seq=s.reading_seq-1
), job_spans AS (
  SELECT r.job_order_number,r.report_code,MIN(r.snapshot_date) first_seen,MAX(r.snapshot_date) last_seen,
    MIN(r.first_status_date) first_status_date,MAX(r.status_date) last_status_date
  FROM dbo.v_service_job_readings r JOIN dbo.v_service_families f ON f.report_code=r.report_code AND f.read_rule=''JobList''
  GROUP BY r.job_order_number,r.report_code
), list_dates AS (
  SELECT w.report_code,MAX(w.snapshot_date) latest_date
  FROM dbo.v_service_reading_windows w WHERE w.read_rule=''JobList''
  GROUP BY w.report_code
), events AS (
  SELECT * FROM state_events
  UNION ALL
  SELECT job_order_number,report_code,''FirstSeen'',first_seen,CONVERT(date,NULL),first_status_date FROM job_spans
  UNION ALL
  SELECT j.job_order_number,j.report_code,''StillListed'',j.last_seen,j.first_seen,j.last_status_date
  FROM job_spans j JOIN list_dates d ON d.report_code=j.report_code
  WHERE j.last_seen=d.latest_date AND j.last_seen>j.first_seen
  UNION ALL
  SELECT j.job_order_number,j.report_code,''LeftList'',n.next_date,j.last_seen,j.last_status_date
  FROM job_spans j CROSS APPLY (SELECT MIN(w.snapshot_date) next_date FROM dbo.v_service_reading_windows w
    WHERE w.report_code=j.report_code AND w.snapshot_date>j.last_seen) n
  WHERE n.next_date IS NOT NULL
)
SELECT e.job_order_number,e.report_code,f.list_label,e.event_kind,e.snapshot_date,e.previous_snapshot_date,e.status_date
FROM events e JOIN dbo.v_service_families f ON f.report_code=e.report_code');
""")

# 10. s004 daily
emit(f"""-- 6. S004 tender collection under the DateLog rule (BillingDate): the amount per date and tender of the winning
-- reading. Tenders: CASH cashamount, CARD cardamount, UPI upi + bharatpe + phonepe (UPI apps; phonepe is typed
-- Identifier in the spec, so it is read with TRY_CONVERT), CHEQUE chequeamount, RTGS rtgsamount, ADVANCE advanceamount.
-- totalamount is not a tender; v_service_money_changes uses it.
EXEC(N'CREATE OR ALTER VIEW dbo.v_service_s004_daily AS
WITH winning AS (
  SELECT r.billingdate business_date,w.snapshot_date,w.import_file_id,r.cashamount,r.cardamount,r.upi,r.bharatpe,
    TRY_CONVERT(decimal(19,4),REPLACE(CONVERT(nvarchar(60),r.phonepe),N'','',N'''')) phonepe,
    r.chequeamount,r.rtgsamount,r.advanceamount
  FROM {table('S004')} r JOIN dbo.v_service_datelog_readings w ON w.report_code=''S004'' AND w.reading_rank=1
    AND w.import_file_id=r.import_file_id AND w.business_date=r.billingdate
)
SELECT x.business_date,t.tender,SUM(t.amount) amount,CONVERT(int,COUNT_BIG(*)) row_count,MAX(x.snapshot_date) snapshot_date,MAX(x.import_file_id) import_file_id
FROM winning x CROSS APPLY (VALUES
  (''CASH'',x.cashamount),(''CARD'',x.cardamount),
  (''UPI'',CASE WHEN x.upi IS NULL AND x.bharatpe IS NULL AND x.phonepe IS NULL THEN NULL ELSE COALESCE(x.upi,0)+COALESCE(x.bharatpe,0)+COALESCE(x.phonepe,0) END),
  (''CHEQUE'',x.chequeamount),(''RTGS'',x.rtgsamount),(''ADVANCE'',x.advanceamount)
) t(tender,amount)
GROUP BY x.business_date,t.tender');
""")

# 11. money changes
emit(f"""-- 7. Money changes, S003 and S004: per business date, the total of the winning reading against the total of the
-- previous one. The previous reading is the one the winning reading restated (Restate at the same snapshot date: the
-- superseded import file, whose landing rows are kept) when its window covers the date, otherwise the next-best
-- covering live reading. Only dates whose total changed are listed; this is how a restated money row reaches the
-- Owner in the interim (the Service money screen), never as a review item.
-- Totals: S003 netamount_incl_tax (GST inclusive, as decision D1 for Retail), S004 totalamount. A covering reading
-- with no row on the date counts as 0.
EXEC(N'CREATE OR ALTER VIEW dbo.v_service_money_changes AS
WITH ranked AS (
  SELECT report_code,business_date,import_file_id,snapshot_date,reading_rank
  FROM dbo.v_service_datelog_readings WHERE report_code IN(''S003'',''S004'') AND reading_rank<=2
), amounts AS (
  SELECT ''S003'' report_code,import_file_id,trans_date business_date,SUM(netamount_incl_tax) amount
  FROM {table('S003')} WHERE trans_date IS NOT NULL GROUP BY import_file_id,trans_date
  UNION ALL
  SELECT ''S004'',import_file_id,billingdate,SUM(totalamount)
  FROM {table('S004')} WHERE billingdate IS NOT NULL GROUP BY import_file_id,billingdate
), restated AS (
  SELECT f.superseded_by_import_file_id current_import_file_id,f.import_file_id,f.report_code,
    COALESCE(f.period_end,f.business_date) snapshot_date,MIN(a.business_date) window_from,MAX(a.business_date) greatest_date
  FROM dbo.import_files f JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
  JOIN amounts a ON a.report_code=f.report_code AND a.import_file_id=f.import_file_id
  WHERE f.is_superseded=1 AND f.data_truth_version=1 AND b.status=''Completed'' AND f.report_code IN(''S003'',''S004'')
    AND COALESCE(f.period_end,f.business_date) IS NOT NULL
  GROUP BY f.superseded_by_import_file_id,f.import_file_id,f.report_code,COALESCE(f.period_end,f.business_date)
), previous AS (
  SELECT c.report_code,c.business_date,c.import_file_id current_import_file_id,c.snapshot_date current_snapshot_date,
    COALESCE(x.import_file_id,p.import_file_id) import_file_id,COALESCE(x.snapshot_date,p.snapshot_date) snapshot_date
  FROM ranked c
  LEFT JOIN ranked p ON p.report_code=c.report_code AND p.business_date=c.business_date AND p.reading_rank=2
  OUTER APPLY (SELECT TOP (1) r.import_file_id,r.snapshot_date FROM restated r
    WHERE r.current_import_file_id=c.import_file_id AND r.report_code=c.report_code
      AND c.business_date BETWEEN r.window_from AND CASE WHEN r.greatest_date>r.snapshot_date THEN r.greatest_date ELSE r.snapshot_date END
    ORDER BY r.import_file_id DESC) x
  WHERE c.reading_rank=1 AND (x.import_file_id IS NOT NULL OR p.import_file_id IS NOT NULL)
), pairs AS (
  SELECT v.report_code,v.business_date,v.snapshot_date previous_snapshot_date,v.import_file_id previous_import_file_id,
    COALESCE(pa.amount,0) previous_amount,v.current_snapshot_date,v.current_import_file_id,
    COALESCE(ca.amount,0) current_amount
  FROM previous v
  LEFT JOIN amounts ca ON ca.report_code=v.report_code AND ca.import_file_id=v.current_import_file_id AND ca.business_date=v.business_date
  LEFT JOIN amounts pa ON pa.report_code=v.report_code AND pa.import_file_id=v.import_file_id AND pa.business_date=v.business_date
)
SELECT report_code,business_date,previous_snapshot_date,previous_import_file_id,previous_amount,
  current_snapshot_date,current_import_file_id,current_amount,current_amount-previous_amount difference
FROM pairs WHERE current_amount<>previous_amount');
""")

views = ["v_service_families", "v_service_reading_windows", "v_service_readings", "v_service_datelog_readings",
         "v_service_status_view_rows", "v_service_job_readings", "v_service_job_status_current",
         "v_service_pending_current", "v_service_job_list_events", "v_service_s004_daily", "v_service_money_changes"]
emit("-- Read access as 0041 grants it (0022 already grants SELECT ON SCHEMA::dbo); no role may write through a view.")
for v in views:
    emit(f"GRANT SELECT ON dbo.{v} TO etp_viewer,etp_store_manager,etp_owner;")
    emit(f"DENY INSERT,UPDATE,DELETE ON dbo.{v} TO etp_store_manager,etp_viewer;")

SECTION_TEXT = "\n".join(P) + "\n"


def splice(text, section):
    newline = "\r\n" if "\r\n" in text else "\n"
    normal = text.replace("\r\n", "\n")
    begin = normal.find(BEGIN_MARKER + "\n")
    end = normal.find("\n" + END_MARKER)
    if begin < 0 or end < 0 or normal.count(BEGIN_MARKER) != 1 or normal.count(END_MARKER) != 1:
        sys.exit(f"The migration must hold exactly one '{BEGIN_MARKER}' line and one '{END_MARKER}' line.")
    start = begin + len(BEGIN_MARKER) + 1
    if end + 1 < start:
        sys.exit("The section markers are out of order.")
    updated = normal[:start] + section + normal[end + 1:]
    return updated.replace("\n", newline)


def main():
    args = ARGS
    section = SECTION_TEXT
    migration = Path(args.migration)
    raw = migration.read_bytes()
    bom = raw.startswith(b"\xef\xbb\xbf")
    text = raw[3:].decode("utf-8") if bom else raw.decode("utf-8")
    updated = splice(text, section)
    if args.check:
        if updated != text:
            print(f"{migration.name}: section {SECTION} is not current; run generate_read_sql.py", file=sys.stderr)
            return 1
        print(f"{migration.name}: section {SECTION} is current")
        return 0
    if updated != text:
        migration.write_bytes((b"\xef\xbb\xbf" if bom else b"") + updated.encode("utf-8"))
    print(f"{migration.name}: section {SECTION} written")
    return 0


if __name__ == "__main__":
    sys.exit(main())
