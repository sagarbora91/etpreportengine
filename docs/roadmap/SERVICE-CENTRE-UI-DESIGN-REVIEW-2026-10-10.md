# Service Centre UI: design review for a dedicated "Service" rail section (1.10.0)

**Date:** 10 Oct 2026 (night of 9 Oct). **For:** Sagar (owner) and the 1.10.0 coordinator. **Status:** proposal; nothing built, nothing run in the app, no code or migration changed.
**Method:** read-only. Facts come from `SET NOCOUNT ON; SELECT ...` through `sqlcmd` against the live `EtpReporting` on Workpc (after the 9 Oct Service import: consolidated workbook dated 29 Sep 2026 plus the raw pack dated 3 Oct 2026), from migration `0048_service_centre_interim.sql`, the four interim screens in `src/Etp.Reporting.Desktop/Modules/Service`, `SqlServerServiceReportQuery.cs`, and the Service documents listed at the end. Every SELECT quoted here can be re-run as written.
**Privacy:** this document holds no customer name, phone number, e-mail or address. Job numbers are described by their shape only; counts, dates and column names are given. Daily money totals are deliberately not copied here (the money check screen shows them).

**Premise (Sagar, 9 Oct):** Service is a job lifecycle (SRN raised -> indent -> GIT -> repaired -> pending delivery -> delivered, plus repeat returns, replacements, depreciation, GPRC/WDC/WRA/MB claims and parts purchase registers), not money-by-date like Retail. It gets its own rail section, shaped around the job, ageing/TAT, pending-by-reason and claims. The manual `SERVICE_CASH/CARD/UPI` money check (decision 16) stays.

**The two findings that shape everything else**

1. **One job number ties every family.** Every Service family that is about a job carries the same key, `JOAW330` followed by 8 or 9 digits (15 or 16 characters), never blank, and it joins at 100 % between the job booking (S002), the ten status views, the delivery/repair reports, the revenue log, the pending lists, the SRN lists, the claim registers and the Deftran register (`srfno`). The only differences are the column names (`job_order_no`, `jobordernumber`, `jonumber`, `joborder_number`, `job_order_number`, `jo_number`, `srfno`) and a whitespace trim. No composite key is needed.
2. **The lifecycle stage cannot be read from one column.** S002 `Current Status` is stale for old jobs (25 jobs it calls `Indent_Raised` were delivered according to the status views), `Delivery report` and `Repair report` (S036/S037) carry the job's booking date, not the delivery or repair date, and the pending lists carry no reason. The stage has to be derived from the status views, the pending lists and the SRN list together (section 4.2).

---

## 1. Facts from the live landing tables

### 1.1 What was imported (row counts and readings)

`SELECT t.name, p.rows FROM sys.tables t JOIN sys.partitions p ON p.object_id=t.object_id AND p.index_id IN (0,1) WHERE t.name LIKE 'etp_landing_s0%'` -> 36 tables, 28,244 rows in total (the handoff's 28,732 counts import rows including the superseded duplicate S022 file and the S005/S027/S028 "Not needed" files that landed nothing). `SELECT ... FROM dbo.v_service_readings` -> 57 live readings: one consolidated reading per family dated **2026-09-29** (35 families) and one raw reading dated **2026-10-03** for 22 files (21 families; S022 came twice, same content).

| Family | Rows | Readings (snapshot dates) | Latest is | Note |
|---|---|---|---|---|
| S002 Job booking | 3,980 | 29 Sep (3,954) + 3 Oct (26) | RAW | 3,156 distinct jobs |
| S003 Revenue | 5,001 | 29 Sep + 3 Oct | RAW | 2,338 jobs, 2,037 documents |
| S004 Tender collection | 2,229 | 29 Sep + 3 Oct | RAW | 2,160 bills |
| S006 Closing stock | 1,001 | 29 Sep (497) + 3 Oct (504) | RAW | spares stock |
| S007 / S008 Purchase created / received | 1,564 / 1,551 | 29 Sep + 3 Oct | RAW | 299 / 285 invoices |
| S009 Pending repair | 117 | 29 Sep (63) + 3 Oct (54) | RAW | 46 jobs in the latest list |
| S010 Pending delivery | 257 | 29 Sep (128) + 3 Oct (129) | RAW | 84 jobs in the latest list |
| S011 SRN status | 145 | 29 Sep only | CONS. | 141 SRNs, 141 jobs |
| S012 SRN history | 172 | 29 Sep only | CONS. | 143 SRNs, 143 jobs |
| S013 GIT | 37 | 29 Sep only | CONS. | item level, no job column |
| S014 DC / S015 IR / S016 RA / S017 RWR / S018 DELIVERED | 110 / 159 / 31 / 453 / 3,618 | S015, S017, S018 have a 3 Oct raw reading | mixed | 83-column status views |
| S019 Repeat return | 6 | 29 Sep only | CONS. | 5 jobs |
| S020 Replacement / S021 Depreciation | 22 / 110 | 29 Sep only | CONS. | 15 / 108 jobs |
| S022 Empowerment | 194 | 29 Sep + 3 Oct | RAW | 153 jobs |
| S023 GPRC / S024 MB / S025 GPRC WDC / S026 GPRC WRA | 53 / 44 / 97 / 36 | 29 Sep only | CONS. | old-header claim logs |
| S029 Deftran | 3,888 | 29 Sep + 3 Oct | RAW | 2,968 jobs (`srfno`) |
| S030 Running tests (MIS) | 1,522 | 29 Sep only | CONS. | 629 jobs, data ends 2 Jul 2026 |
| S031 PD / S032 PR / S033 SRN / S034 REPAIRED / S035 SRNINV | 41 / 49 / 12 / 11 / 6 | S031, S032, S034 have a 3 Oct raw reading | mixed | 83-column status views |
| S036 Delivery report / S037 Repair report | 808 / 863 | 29 Sep + 3 Oct | RAW | 546 / 589 jobs |
| S039 WDC claim / S040 WRA claim | 17 / 15 | 29 Sep + 3 Oct | RAW | new-header claim logs from 1 Jul 2026 |
| S041 GPRC claim | 25 | 3 Oct only | RAW | 3 documents, 21 jobs |

Families with **no raw export yet** (consolidated only, so they will go stale first): S011, S012, S013, S014, S016, S019, S020, S021, S023-S026, S030, S033, S035. The data-freshness strip (section 3.7) must show this per family group.

Storage: `SELECT SUM(a.total_pages)*8/1024 FROM sys.tables ... WHERE t.name LIKE 'etp_landing_s0%'` -> **26 MB** for all Service tables; the whole database is 400 MB (`sys.database_files`). Load is not a concern for 1.10.0.

### 1.2 Which family holds which lifecycle stage

| Stage | Family (column that dates it) | Kind of list | Live evidence |
|---|---|---|---|
| Booked (job raised) | S002 `created_date`; also `jodate` in every status view, `srf_date` in S029 | job list, cumulative in the consolidated workbook, window-filtered in raw | 3,156 jobs 1 Oct 2024 - 2 Oct 2026; 2,495 of 3,980 rows are `Quick Billing`, 1,485 `Booking` |
| On the bench (pending repair) | S032 PR (`jodate`); S009 Pending repair list (`jodate`, `edd`, `jostatus`, `pendingstore`) | status view / state snapshot | S009 latest: 46 jobs, `jostatus` = Indent_Raised 25, Pending_Repair 25, SRN_Issued 3, SRN_For_Repair 1 (rows) |
| Indent raised (parts awaited) | S015 IR (`indentdate`); S009 rows with `indentid` | status view | S015: 97 jobs; S009 latest: 25 rows with an indent |
| SRN out to brand / other centre | S033 SRN (`srnissueddate`), S035 SRNINV (`srnreturndate`), S011 SRN status (`srn_date`, `to_store`, `to_status`, `srn_received_date`), S012 SRN history (`srnno`, `srnstatus`) | status views + SRN lists | S011: 141 SRNs, 10 without `srn_received_date` and `repaired_date` (open); `to_store` is where the watch went |
| In transit back | S010 rows with `pendingstore` = `In_Transit` | state snapshot | 97 of 129 latest S010 rows |
| Goods in transit (spares) | S013 GIT (`stm_date`, item level) | date log | 37 rows, no job column |
| Repaired | S034 REPAIRED (`jorepairdate`), S010 `repairdate` | status view / list | S034: 10 jobs; S010 latest: 84 jobs, all with `repairdate` |
| Pending delivery | S031 PD (`jorepairdate`), S010 Pending delivery (`jostatus`, `pendingstore`) | status view / state snapshot | S010 latest: 20 rows `Pending_Delivery` at AW330, 97 `SRN_After_Repair_DC_Created` in transit, 12 `SRN_After_Repair` at PUNS/CSCH |
| Running test | S030 (`running_test_date`, `result`) | job list | 629 jobs, Pass 562 / Fail 960 rows; **ends 2 Jul 2026** (no raw export) |
| Delivered | S018 DELIVERED (`deliverydate`, `jorepairdate`); S029 Deftran (`delivered_date`, `repair_date`, defect, mechanic, labour) | status view / register | S018: 2,465 jobs; S029: 2,968 jobs with `status` Delivered 3,367 / RWR 387 / DC 110 / RA 24 rows |
| Returned without repair | S017 RWR (`normalrwrdate`, `rwr_reason`) | status view | 372 jobs; top reasons: "Analysis watch - Not for repair" 243, "Spares not available" 79, "Estimation Not Approved" 18 |
| Depreciation (DC) | S014 DC (`wdcdate`, `wdcnumber`); S021 Depreciation (`dcdate`, `wdcvalue`) | status view / job list | S014 108 jobs, all with `wdcnumber`; S021 108 jobs (the same 108) |
| Replacement (RA) | S016 RA (`wradate`, `radcnumber`); S020 Replacement (`radate`) | status view / job list | S016 23 jobs, all with `radcnumber`; S020 15 jobs (subset) |
| Repeat return | S019 (`repairdate`, `previousjo`, `reason`) | date log | 6 rows, 5 jobs, 3 previous jobs; reason mostly blank |
| Empowerment | S022 (`invoice_date`, `empowerment_type`, `jo_status`) | date log | 194 rows: Depreciation coupon 112, Invoice 69, Replacement Advice 13 |
| Claims to Titan | S023 + S041 GPRC cell (`GPAW330...` documents, account `GPRCCell`); S024 Module Bank (`MCAW330...`, `Module Bank`); S025 + S039 WDC (`DSAW330...`, `GPRC`); S026 + S040 WRA (`RSAW330...`, `CCSF`/`CSFT`) | date logs | see 1.6 |
| Money | S003 revenue per document line (`trans_date`, `labour_charge`, `spare_charge`, `netamount_incl_tax`); S004 tender per bill (`billingdate`) | date logs | 2,029 of 2,160 S004 bills are in S003 (`billingnumber` = `documentnum`); the rest are the 129 `GDAW3` zero-value documents |
| Spares | S006 closing stock (snapshot), S007 purchase created, S008 purchase received (`grn_date`, `invoice_number`, `grn_no`) | snapshot / date logs | see 1.7 |

### 1.3 The job key, proven

Profile over every live row of every family with a job column (`q1`: `UNION ALL` of the 33 job columns, trimmed):

| Family (column) | Rows | Non-blank | Distinct jobs | Length | `JOAW330%` |
|---|---|---|---|---|---|
| S002 (`job_order_no`) | 3,980 | 3,980 | 3,156 | 15-16 | 100 % |
| S003 (`joborder_number`) | 5,001 | 5,001 | 2,338 | 15-16 | 100 % |
| S009 / S010 (`jonumber`) | 117 / 257 | all | 65 / 85 | 16 | 100 % |
| S011 (`joborder_number`) / S012 (`jonumber`) | 145 / 172 | all | 141 / 143 | 15-16 | 100 % |
| S014-S018, S031-S035 (`jobordernumber`) | 110 / 159 / 31 / 453 / 3,618 / 41 / 49 / 12 / 11 / 6 | all | 108 / 97 / 23 / 372 / 2,465 / 26 / 46 / 5 / 10 / 3 | 15-16 | 100 % |
| S019 (`jo_number`; `previousjo`) | 6 | 6; 4 | 5; 3 | 15-16 | 100 % |
| S020 / S021 (`jobordernumber`) | 22 / 110 | all | 15 / 108 | 15-16 | 100 % |
| S022 (`joborder_number`) | 194 | 194 | 153 | 15-16 | 100 % |
| S023 / S024 / S025 (`jonumber`) | 53 / 44 / 97 | all | 46 / 44 / 97 | 15-16 | 100 % |
| S026 (`jonumber`) | 36 | **8** | 8 | 16 | 100 % of non-blank |
| S029 (`srfno`; `repeat_return_jobordernumber`) | 3,888 | 3,888; 3 | 2,968; 2 | 15-16 | 100 % |
| S030 (`job_order_number`) | 1,522 | 1,522 | 629 | 15-16 | 100 % |
| S036 / S037 (`job_order_no`) | 808 / 863 | all | 546 / 589 | 16 | 100 % |
| S039 / S040 / S041 (`job_order_number`) | 17 / 15 / 25 | all | 15 / 14 / 21 | 16 | 100 % |

Format: `SELECT LEN(j), SUBSTRING(j,8,4), COUNT(*) FROM (DISTINCT S002 keys)` -> 16-character keys are `JOAW330` + financial-year code (`2526`: 1,544 jobs; `2627`: 1,044 jobs) + a 5-digit sequence; 15-character keys are `JOAW330` + `0000` + a 4-digit sequence (568 jobs, the FY 2024-25 numbering). `SELECT COUNT(*) ... WHERE LEN(a.j)=15 AND LEN(b.j)=16 AND STUFF(a.j,12,0,'0')=b.j` -> **0 collisions**, so the key is used as the exported text (trimmed), never padded or re-formatted. Families without a job column: S006 (stock), S007/S008 (purchases, `invoice_number`/`grn_no`), S013 (GIT, `stm_number`/`item_id`), S004 (bills, joined to jobs through S003 `documentnum`).

Join rates (`q2`: distinct jobs per family against S002 and against the union of the ten status views):

| Family | Distinct jobs | In S002 | In a status view | In neither |
|---|---|---|---|---|
| S003 | 2,338 | 2,338 | 2,338 | 0 |
| S009 | 65 | 63 | 60 | 1 |
| S010 | 85 | 85 | 14 | 0 |
| S011 / S012 | 141 / 143 | 141 / 143 | 40 / 40 | 0 |
| S014, S016, S017, S018, S031-S035, S020, S021, S022, S023-S025, S029, S036, S039-S041 | (as above) | **100 %** | 100 % (S037: 573 of 589; S030: 547 of 629) | 0 |
| S015 | 97 | 96 | 97 | 0 |
| S026 | 8 | 8 | 8 | 0 |

The other direction (`q2b`): of 3,156 S002 jobs, 3,044 are in a status view, 2,465 in S018 DELIVERED, 2,338 in S003, 2,968 in S029, 629 in S030. The 112 S002 jobs that no status view holds are all `Booking` type (10 of them booked since July 2026): a job list screen must include them from S002 with a stage of "Booked (no status yet)".

Lines per key within one reading (`q6a`): S002 3,954 rows / 3,138 jobs (max 5 lines, the lines differ only in line columns; 1 job carries two `current_status` values); S018 3,593 / 2,452 (max 12); S036 783 / 530; S037 828 / 565; S009 54 / 46; S010 129 / 84; S030 1,522 / 629 (test runs, max 18); S003 4,968 / 2,326 (document lines, max 8); S029 3,861 / 2,952. Rows are line items; the job identity view must aggregate them.

Raw vs consolidated overlap (`q6c`): the 3 Oct raw S002 (18 jobs) and S018 (13 jobs) hold no job that the 29 Sep workbook holds (new window only); the raw S009 (46 jobs) shares 36 jobs with the 29 Sep S009 (55 jobs). So raw windows add, and state lists shrink and grow, exactly as the 0048 read rules assume.

### 1.4 Date columns per family (for TAT and ageing)

`q4a`: min/max and null counts of every lifecycle date, all readings.

| Family | Column | Range | Nulls | Use |
|---|---|---|---|---|
| S002 | `created_date` | 1 Oct 2024 - 2 Oct 2026 | 0 | booking date |
| S009 | `jodate` / `edd` / `indentdate` | 16 Aug - 1 Oct 2026 / 30 Aug - 29 Nov 2026 / 11 Sep - 2 Oct 2026 | 0 / 0 / 64 of 117 | age, promised date, parts wait |
| S010 | `jodate` / `repairdate` | 19 Apr 2025 - 28 Sep 2026 / 25 Jun 2025 - 30 Sep 2026 | 0 / 0 | age since booking / since repair |
| S011 | `srn_date` / `srn_received_date` / `repaired_date` | 30 Oct 2024 - 24 Sep 2026 / 8 Nov 2024 - 28 Sep 2026 / 8 Nov 2024 - 17 Sep 2026 | 0 / 10 / 16 | SRN out / back / repaired |
| S012 | `jodate` / `srnrepaireddate` / `srnrecievedate` | 30 Oct 2024 - 24 Sep 2026 / ... / ... | 0 / 12 / 6 | SRN history |
| S013 | `stm_date` | 16 Oct 2024 - 22 Sep 2026 | 0 | GIT |
| S015 | `indentdate` | 3 Jun - 2 Oct 2026 | 0 | indent raised |
| S018 | `jodate` / `jorepairdate` / `deliverydate` / `edd` / `indentdate` / `srnissueddate` / `srnreceiveddate` / `wdcdate` / `wradate` | 1 Oct 2024 - 2 Oct 2026 (first three) / 3 Oct 2024 - 21 Nov 2026 / ... | 0 / 0 / 0 / **1,899** / 2,845 / 3,611 / 3,611 / 3,618 / 3,618 | TAT; EDD missing on half the delivered rows |
| S019 | `repairdate`, `previousjodate` | 21 Apr 2025 - 8 Sep 2026 | 0 | repeat return |
| S020 | `bookingdate` / `radate` | 1 Nov 2025 - 16 Sep 2026 / - 17 Sep 2026 | 0 | replacement |
| S021 | `bookingdate` / `dcdate` | 9 Oct 2024 - 22 Sep 2026 | 0 | depreciation |
| S022 | `invoice_date` | 4 Mar 2025 - 1 Oct 2026 | 129 of 194 | only Invoice-type rows carry it |
| S023 / S024 / S025 / S026 | `transdate` | 5 Nov 2024 - **5 Aug 2026** / 3 Feb - 9 Nov 2025 / 5 Nov 2024 - **1 Jul 2026** / 5 Nov 2024 - **1 Jul 2026** | 0 | old-header claims stop here |
| S029 | `srf_date` / `repair_date` / `delivered_date` | 1 Oct 2024 - 2 Oct 2026 (all three) | 0 | the fullest closed-job register |
| S030 | `running_test_date` | 2 Oct 2024 - 2 Jul 2026 | 0 | tests |
| S036 / S037 | `created_date` | 25 Apr - 2 Oct 2026 / 7 Feb - 2 Oct 2026 | 0 | **= the job's booking date** (below) |
| S039 / S040 | `transaction_date` | 1 Jul - 1 Oct 2026 | 0 | new-header claims |
| S041 | `transaction_date` | 1 - 2 Oct 2026 | 0 | GPRC claims (raw only) |
| S007 / S008 | `invoice_date`, `received_date`, `grn_date` | 14 Sep 2024 - 2 Oct 2026 | S007 `received_date`/`grn_date` null on 25 rows (the `Open` ones) | parts ageing |
| S003 / S004 | `trans_date` / `billingdate` | 1 Oct 2024 - 2 Oct 2026 | 0 | money |

**S036 and S037 do not carry a delivery or repair date.** `q4d/q4e`: of 546 S036 jobs, 440 are in S018 and on **all 440** `S036.created_date = S018.jodate` (316 also equal `deliverydate`, because most jobs are same-day); of 589 S037 jobs, 437 are in S018 and on all 437 `created_date = jodate`. The 0048 view `v_service_job_readings` uses `created_date` as the S036/S037 "status date" and labels them "Delivery report" / "Repair report"; they are job lists of the window's bookings with a `current_status` column, not delivery or repair events. TAT must come from S018/S029/S034/S031, not from S036/S037.

### 1.5 TAT today (S018 DELIVERED, consolidated reading 10164)

`q4b`: 2,452 delivered jobs; `deliverydate` and `jorepairdate` never null; `edd` null on 1,440. Average booking -> delivery **3.68 days**, booking -> repaired 2.41, repaired -> delivery 1.27; no negative TAT. Where an EDD exists: 789 delivered on or before the EDD, 223 after it.

`q4c` bands of booking -> delivery: 0 days **2,022**; 1-3 days 22; 4-7 days 47; 8-15 days 156; 16-30 days 127; 31-60 days 63; over 60 days 15. The 2,022 same-day jobs are the quick-billing counter work (battery, strap, minor). A TAT figure that includes them is meaningless for the workshop; the TAT screen must separate Quick Billing (S002 `jotype_booking_quickbilling`) from Booking, or report "jobs over N days" rather than an average.

### 1.6 Pending lists, statuses and reasons

`q3` (latest reading of each list unless noted):

- **S009 Pending repair (3 Oct):** 54 rows / 46 jobs. `jostatus`: Indent_Raised 25, Pending_Repair 25, SRN_Issued 3, SRN_For_Repair 1. `pendingstore`: AW330 48, PUNS 4, CSCH 2. `reasonforpending`: **NULL on all 54** (the column is empty in this export). `sparerequired`: 30 rows name a part, 24 blank. `pendingnoofdays` equals `DATEDIFF(day, jodate, snapshot)` on 54 of 54 rows; average 11 days; EDD already passed for 11 rows; 25 rows carry an indent.
- **S010 Pending delivery (3 Oct):** 129 rows / 84 jobs. `jostatus` x `pendingstore`: SRN_After_Repair_DC_Created at `In_Transit` 97; Pending_Delivery at AW330 20; SRN_After_Repair at PUNS 11 and CSCH 1. `reasonforpending` NULL on all. `pendingnoofdays` = days since **booking** (129 of 129), not since repair. `v_service_pending_current` ages: min 5, max 532, **average 227 days**; bands <=30: 5, 31-90: 8, 91-180: 12, 181-365: **52**, >365: 7. None of the 84 is in S018 DELIVERED; only 13 are in a status view (PD). So "pending delivery" is mostly watches that went out on an SRN after repair and are shown as in transit for months, which is either a Titan-side housekeeping backlog or a list that never closes; either way the board must group it by `jostatus` + `pendingstore`, not show one age.
- **S011 SRN status (29 Sep):** 145 rows / 141 SRNs. `to_status`: SRN_After_Repair_DC_Created 52, SRN_Returned_without_Repair_DC_Created 35, SRN_after_Repair_Received 13, SRN_For_Repair_DC_Created 12, SRN_Return_Received 12, SRN_Repaired 10, SRN_for_Repair_Received 4, SRN_Returned_without_Repair 4, SRN_Invoiced 3. `payment_received`: NO 122 / YES 23. `srn_received_date` set on 135, null on 10. Open SRNs by a sensible rule (`srn_received_date IS NULL AND repaired_date IS NULL`) = **10**, yet the interim pending view lists all 141 with an average age of 279 days.
- **S002 `current_status` (all rows):** Delivered 3,169; Returned_Without_Repair 370; DC_Issued 110; Indent_Raised 78; SRN_After_Repair_DC_Created 76; Pending_Repair 46; SRN_WithoutRepair_DC_Created 28; RA_Issued 24; Pending_Delivery 23; SRN_Repaired 17; SRN_After_Repair 12; SRN_For_Repair_DC_Created 8; SRN_Issued 7; SRN_Invoiced 5; Repaired 4; Running_Test 2; SRN_For_Repair 1 (17 values). Agreement with the 0048 derived status (`q7`): Delivered->DELIVERED 2,420, RWR->RWR 357, DC->DC 108, RA->RA 23 agree; but 25 `Indent_Raised` and 7 `Pending_Repair` jobs are DELIVERED by the status views, and 112 SRN_* statuses have no status-view row at all (the SRN views S033/S035 hold 5 and 3 jobs). S002's status is as of the export window that first held the job, so it is stale; the status views and lists are the truth.
- Other statuses: S003 `job_status` Delivered 4,981 / RWR 20, `document_type` Invoice 4,991 / Cancelled 10; S007 `status` Closed 1,539 / Open 25; S008 Closed 1,551; S012 `srnstatus` SRN_For_Repair_DC_Created 143 / after_Repair_Received 17 / Return_Received 12; S020 `wrastatus_1` RA_Issued 22; S021 `status` DC_Issued 110; S022 `jo_status` DC_Issued 112 / Delivered 65 / RA_Issued 13 / RWR 4; S029 `status` Delivered / RWR / DC_Issued / RA_Issued; S030 `result` Pass 562 / Fail 960.
- Dimensions worth a filter (S018): brand (TITAN 986 jobs, HELIOS 551, FASTRACK 243, RAGA 171, EDGE 169, SONATA 120, then long tail), `guarantee` Out_of_Warranty 3,318 / Under_Warranty 300 rows, `customertype` B2C 3,617 / B2B 1, `moa` (mode of arrival) null on 2,111 rows.

### 1.7 Claims and parts

Claims (`q5f/q5g`), documents / lines / jobs per family and period:

| Family | Claim type (account) | Document prefix | Period | Docs / lines / jobs |
|---|---|---|---|---|
| S023 GPRC_Report | GPRC cell (`GPRCCell`) | `GPAW3...` | Nov 2024 - 5 Aug 2026 | 22 / 53 / 46 |
| S041 GPRC CLAIM (raw) | GPRC cell (`GPRCCell`) | `GPAW3...` | 1-2 Oct 2026 | 3 / 25 / 21 |
| S024 GPRC_MB | Module Bank | `MCAW3...` | Feb - Nov 2025 | 21 / 44 / 44 |
| S025 GPRC_WDC | WDC (`GPRC`) | `DSAW3...` | Nov 2024 - 1 Jul 2026 | 28 / 97 / 97 |
| S039 WD_Claim (new header) | WDC (`GPRC`) | `DSAW3...` | 1 Jul - 1 Oct 2026 | 7 / 17 / 15 |
| S026 GPRC_WRA | WRA (`GPRC` 28 rows, `CCSF` 8) | `DSAW3...` 28, `RSAW3...` 5 | Nov 2024 - 1 Jul 2026 | 33 / 36 / 8 (job blank on 28 rows) |
| S040 WRA_Claim (new header) | WRA (`CCSF` 9, `CSFT` 6) | `RSAW3...` | 1 Jul - 1 Oct 2026 | 7 / 15 / 14 |

`v_service_gprc_claims` returns S023 53 lines / 22 documents and S041 25 / 3; `SELECT COUNT(*) ... S041 documents also in S023` -> 0 (no overlap in the data yet, because S023 stops on 5 Aug and the only S041 file covers 1-2 Oct). **The GPRC lines for 6 Aug - 29 Sep 2026 are in no table** (the known S023 gap; answers document Q8). Also note S025 ends and S039 starts on the same day, 1 Jul 2026: a document-number union like the one 0048 does for GPRC is needed for WDC and WRA too before the two are added together.

Every WDC claim job is in the DC view and every WRA claim job in the RA view (`q5h`: S039 15/15 in S014, S025 97/97, S040 14/14 in S016, S026 8/8); all 108 DC jobs carry a `wdcnumber` and all 23 RA jobs a `radcnumber`; S021 Depreciation holds the same 108 jobs as S014, S020 Replacement 15 of the 23 RA jobs. So "claims outstanding" by stage is answerable: a DC/RA job with no claim document yet is a claim not raised.

**No family carries a settlement or payment state for a claim.** The claim logs are Titan-facing invoices (`document_type` Invoice, `irn_number` blank). The only payment flag in the corpus is S011 `payment_received` (SRN invoices). "Outstanding vs settled" cannot be derived from the exports (section 5, Q9).

Parts (`q5i`): S007 299 invoices / 283 GRNs; S008 285 / 285; 284 invoices and 282 GRNs appear in both; **15 invoices created but not received**; S007 `status` Open on 25 rows with `received_date` and `grn_date` null; `from_location` is `CCPT` on every row (one supplier location). S008 invoice -> received averages 7.8 days, max 72. S009's `sparerequired`/`sparecode` name the awaited part, but no column links a pending job to a purchase invoice; the only job-to-parts link is the indent (`indentid`/`indentnumber`), which S007/S008 do not carry.

### 1.8 Privacy map (columns that must never reach a screen or export)

`SELECT t.name, STRING_AGG(c.name, ', ') FROM sys.tables t JOIN sys.columns c ... WHERE c.name LIKE '%mobile%' OR '%email%' OR '%phone%' OR '%landline%' OR '%contact%' OR '%address%'`:
S004 `phonepe` (a tender, not a phone; fine); S009, S010 `mobilenumber`; S012 `customermobile`, `customeremail`; S014-S018, S031-S035 `mobilenumber`, `email`, `endcustomercontactnumber`; S020, S021 `mobilenumber`; S029 `landline_no`, `mobile_no`, `email`. Every new view must select columns by name (as 0048 does) and the view text test must assert none of these names appears.

### 1.9 "Service Today" is answerable from the data

For 30 Sep, 1 Oct and 2 Oct 2026 (`q7`): jobs booked 6 / 9 / 3 (S002 `created_date`), delivered 2 / 7 / 4 (S018 `deliverydate`), returned without repair 1 / 0 / 2 (S017 `normalrwrdate`), and the S003 revenue total equals the S004 collection total to the rupee on all three days. Manual `SERVICE_*` entries on live: `SELECT COUNT(*) FROM dbo.manual_operational_inputs WHERE field_code LIKE 'SERVICE[_]%'` -> **0**, so the money check has an S004 side and no manual side today.

---

## 2. Review of the four interim screens and the 13 views

### 2.1 What they answer today

| Screen (task id) | Reads | Answers | Roles |
|---|---|---|---|
| Service jobs by status (`service-jobs`) | `v_service_job_status_current` | One row per job with the latest status view it reached (10 lists), job date, EDD, brand, model, product, customer name, spare value, labour, lines, "in other lists"; filter by list; export | Viewer+ |
| Service pending lists (`service-pending`) | `v_service_pending_current` | S009 / S010 (latest snapshot) and S011 (latest reading per job) with age = snapshot - job date, brand, model, pending store; oldest first; export | Viewer+ |
| Service job history (`service-job-history`) | `v_service_job_list_events` | For a typed job number: each list it was in, first seen, last listed, "left the list on or before", "back on" | Viewer+ |
| Service money check (`service-money`) | `v_service_s004_daily`, `v_service_manual_money`, `v_service_money_changes` | S004 per date and tender vs the Titan World shop's `SERVICE_CASH/CARD/UPI`, difference, entries at other shops, money changed between readings | Viewer+ |

The supporting views (`v_service_families`, `v_service_reading_windows`, `v_service_readings`, `v_service_datelog_readings`, `v_service_status_view_rows`, `v_service_job_readings`, `v_service_gprc_claims`) are sound building blocks and should be kept: the reading/window logic ("latest reading per date, per job or per list, never the latest file") is proven by `ServiceReadModelSqlTests` and by tonight's data (raw windows added, nothing lost).

### 2.2 What they cannot answer

- No job identity: there is no view that gives one row per job with its booking date, brand, stage, every lifecycle date and money. Each screen re-derives bits of it.
- No stage beyond "which status view": S009/S010 membership, `jostatus`/`pendingstore` and S011 closure are not folded in, so the "current status" of a job waiting in transit after an SRN is whatever view last held it (often none).
- No ageing bands, no TAT, no overdue (EDD passed), no "pending by reason" (and the reason column is empty; the usable reason is `jostatus` + `pendingstore` + `sparerequired` + indent presence).
- No claims screen: GPRC/MB/WDC/WRA raised per month, claim not yet raised for DC/RA jobs, S041-over-S023 union only for GPRC (not WDC/WRA).
- No parts screen: created vs received, open invoices, ageing, GIT, closing stock.
- No drill-down: the jobs and pending grids do not open the job's history; the number must be retyped.
- No freshness per family: "Service data as at <date>" takes the newest reading of any family (3 Oct) although 15 families are still at 29 Sep.
- Quick Billing is not separated from Booking anywhere.

### 2.3 Defects noticed (read-only; to fix in 1.10.0, never by editing 0048)

| Id | Where | What | Evidence | Fix |
|---|---|---|---|---|
| SD-01 | `v_service_job_list_events` (JobList branch) and the Job history screen | A raw period export makes every consolidated job "leave" the list. After the 3 Oct raw readings, the view holds **7,233 LeftList events**, among them S002 3,138, S018 2,452, S037 564, S036 530, S017 369, S015 94, dated 2026-10-03: every delivered job's history now says "Left the list on or before 03 Oct 2026". The 0048 comment knows ("LeftList there means no later reading of the family holds the job") but the screen wording does not. | `SELECT report_code, snapshot_date, COUNT(*) FROM dbo.v_service_job_list_events WHERE event_kind='LeftList' GROUP BY ...` | For JobList families, emit LeftList only when the later reading is CONSOLIDATED (cumulative), or drop LeftList for JobList families altogether and show the family's own dates; keep LeftList for S009/S010 state lists. |
| SD-02 | `v_service_pending_current`, SRN_STATUS branch | Lists all 141 SRNs ever (average age 279 days); the row's own closure (`srn_received_date`, `repaired_date`, `to_status` Received) is ignored; open to the coordinator in the 0048 comment. | `q3`: 10 open by the row rule; 141 shown | Apply the closure rule (Q5) in a 0050 `CREATE OR ALTER` of the view, or replace it with the stage view. |
| SD-03 | Pending screen, S010 | Age is days since booking (as the export's `pendingnoofdays`); for a job repaired and sent out, the useful age is since `repairdate` / since the SRN went out; the list is not grouped by `jostatus` or `pendingstore`, so 97 "in transit" rows look like one backlog. | `q5b` | Group by stage; two ages (since booking, since last event). |
| SD-04 | `v_service_job_readings`, S036/S037 | `created_date` is used as the "status date" of "Delivery report"/"Repair report", but it is the booking date (1.4). Labels and dates mislead. | `q4d/q4e` | Treat S036/S037 as job lists (like S002) in the job model; do not call them delivery or repair events. |
| SD-05 | Jobs by status | 112 S002 jobs that no status view holds are invisible; same for the S011-only SRN_* statuses. | `q5d` | The job identity view starts from the union of all job keys, not from the status views. |
| SD-06 | Jobs by status | `other_lists` counts views held, which for a cumulative consolidated set is always "every earlier stage"; it carries no information. | design | Replace with the stage path (dates). |
| SD-07 | `v_service_manual_money` | The Service-money shop is the literal `'WLMHW'` in the view (RA-OPS-21), not a store attribute. | `OBJECT_DEFINITION` | Decision 16 is Titan-only; keep the literal or add `stores.is_service_money_shop` in 0050 (Q7). |
| SD-08 | Money check gate (RA-OPS-07) | The screen loads nothing until some Service reading exists; now moot on live, still true on the shop PC until its first Service import. | `ServiceScreenView.ActivateAsync` | Let the money screen load the manual side alone. |
| SD-09 | All four screens | Export metadata period is "today - today" for jobs, pending and history (`ExportPeriod` default). | `ServiceScreenView.cs:594` | Use the snapshot date(s). |
| SD-10 | Shell | The application status line keeps the previous workspace's text on Service screens (RA-UI-26). | audit | Clear on navigation. |
| SD-11 | Performance | Every screen activation re-runs the union of ten 83-column tables and all readings; the job keys are `nvarchar(max)` and cannot be indexed. Fine at 28k rows; each weekly consolidated refresh adds about 35k rows. | design | Job identity as a view first, measured; materialise (`service_job_index`) when a screen passes about 1 s. |
| SD-12 | `v_service_pending_current` S009/S010 | `age_days` null when `jodate` is null (never on live) but `edd` and `jostatus` are not exposed although both exist. | columns | Expose `edd`, `jostatus`, `pendingstore`, `sparerequired`, indent flag. |

---

## 3. The Service module (proposal)

### 3.1 Placement

A new rail section **"Service"** in `TaskNavigation.Sections` (`["Today", "Import", "Reports", "Stock", "Service", "Settings"]`), destination `"Service Centre"`, with tabs **Today, Pending, Jobs, Claims, Parts, Money**. The four interim tasks move from Reports > "Service centre" to the new section (same task ids, so favourites and the Help topic keep working; `TaskNavigationTests` and `PhaseFiveFullWindowCaptureTests` pinned counts change). The store picker stays disabled for the section (as now). Every screen keeps the `ServiceScreenView` frame: title, "Service Centre AW330 - read only", the as-at line (now the freshness strip, 3.7), filter bar, grid, Refresh, Export to Excel. Roles follow the existing ladder (1 Viewer, 2 Store Manager, 3 Owner); all Service screens are read-only and Viewer+, except where noted.

### 3.2 Service Today (tab "Today", task `service-today`)

- **Purpose:** the morning view: what came in, what went out, what is waiting, is the money entered.
- **Sources:** `v_service_job` (section 4) for counts; `v_service_s004_daily` and `v_service_manual_money` for money; `v_service_readings` for freshness.
- **Cards (3-5 numbers):** (1) Booked today / this month (Booking vs Quick Billing split under the number); (2) Delivered today / this month, with RWR today beside it; (3) On the bench: pending repair count, of which indent raised, of which EDD passed; (4) Waiting to come back / to be delivered: in transit (SRN) count and ready-at-AW330 count; (5) Collection today (S004 cash/card/UPI total) with "manual entry: entered / not entered" from the money check. Below the cards: jobs over 15 days (count, link to the Pending board) and claims raised this month (count).
- **Filters:** business date (defaults to the latest Service snapshot date, not today, so the screen never shows zeros on a weekend); none else.
- **Drill-down:** every card opens the matching Pending group or Jobs filter.
- **Export:** the card values as one sheet.
- **Roles:** Viewer+.
- **Reuse:** `KpiCard` from `DailySalesReportControls.cs`.

### 3.3 Pending board (tab "Pending", task `service-pending`, replaces the interim list)

- **Purpose:** every open job, grouped by stage, with age band and the reason-like facts the exports hold.
- **Sources:** `v_service_job` rows with `stage` not in (DELIVERED, RWR, CLOSED-*), plus `v_service_pending_current` (0048, amended for S011 closure) for the list-of-record.
- **Groups (stage order):** Booked, no status yet (S002 only) -> On the bench (PR) -> Indent raised, parts awaited (IR; shows `sparerequired`, indent date, S007 open invoice count) -> SRN out for repair (S011/S033, `to_store`, SRN date) -> Repaired, awaiting delivery at AW330 (S034/S031, S010 `Pending_Delivery` at AW330) -> Sent back after repair, in transit (S010 `In_Transit`/PUNS/CSCH) -> DC/RA issued, claim not raised (S014/S016 without a claim document in S025/S039/S026/S040) -> Running test (S030, if fresh).
- **Key columns:** job number, stage, booked on, days since booking, days in stage (since the stage date), EDD and "overdue by" (EDD passed), brand, model, product, guarantee, customer type, pending at, spare required, last reading date.
- **Filters:** stage (multi), age band (0-7, 8-15, 16-30, 31-60, 60+), overdue only, brand, guarantee, Booking/Quick Billing.
- **Sort:** stage order then days in stage descending.
- **Drill-down:** row -> Job history (3.4) in place.
- **Export:** the visible group or all groups; one sheet per stage.
- **Roles:** Viewer+.
- **Numbers on the screen:** open jobs; overdue (EDD passed); over 30 days; in transit; parts awaited.

### 3.4 Job history (tab "Jobs", task `service-job-history`, extended)

- **Purpose:** one job, everything the Service Centre exported about it, newest first.
- **Sources:** `v_service_job` (header), `v_service_job_readings` + the family tables for the reading rows, `v_service_claims` (3.5), S003 lines, S030 test runs, S019 repeat returns, S022 empowerment.
- **Layout:** header card (job number, booked on, brand/model/product, guarantee, customer type, current stage, days open or TAT, labour/spares from S003); then a timeline grid: one row per reading of every family that holds the job, newest snapshot first: snapshot date, family (list label), the family's own event date (jodate, indentdate, srn_date, jorepairdate, deliverydate, normalrwrdate, wdcdate/wradate, transaction_date, running_test_date), status text as exported (`jostatus`, `to_status`, `current_status`, `result`), pending store, document number (claims, S003), amount where a money column exists, source kind (consolidated / raw). The 0048 "left the list" rows become plain reading rows (fixes SD-01 on screen; the view stays for S009/S010 history).
- **Filters:** job number (search box, as now) and "also open from any grid".
- **Sort:** snapshot date desc, then event date desc.
- **Export:** header + timeline.
- **Roles:** Viewer+. Customer name is shown (as today); never phone, e-mail, address.
- **Also:** a "Jobs" list (the interim "jobs by status", kept as a sub-tab) with the new `stage` column, Booking/Quick Billing, and TAT days for closed jobs.

### 3.5 Claims (tab "Claims", task `service-claims`)

- **Purpose:** what was claimed from Titan, per claim type and month; what should have been claimed and was not.
- **Sources:** new `v_service_claims` = union of GPRC cell (S023 + S041, S041 wins per document, as `v_service_gprc_claims`), Module Bank (S024), WDC (S025 + S039, new-header file wins per document), WRA (S026 + S040, same rule); plus S014/S016 for "DC/RA issued, no claim document", S021/S020 for depreciation/replacement values.
- **Key columns (by month x claim type):** documents, lines, jobs, net amount incl. tax, UCP value; detail grid per document: date, document number, job number, item, quantity, net incl. tax, source family.
- **Filters:** claim type, month range, "not yet claimed" (DC/RA jobs without a document, with days since DC/RA date).
- **Sort:** month desc.
- **Drill-down:** document -> its lines; job -> Job history.
- **Export:** summary + detail.
- **Roles:** Viewer+ to read. **Settlement:** no export carries it (1.7). Option A (recommended for 1.10.0): show "raised" only, with a note. Option B: an Owner-only "mark settled" entry (date, reference) in a small `service_claim_settlements` table (migration 0051), listed as outstanding vs settled by month. Decide in Q9.
- **Numbers on the screen:** claims raised this month (count, value); DC/RA jobs not yet claimed; oldest unclaimed (days); GPRC gap warning when the latest S041 reading date is earlier than the latest S014/S016 reading date.

### 3.6 Parts and purchases (tab "Parts", task `service-parts`)

- **Purpose:** spares bought vs received, what is open and how old; what the bench is waiting for.
- **Sources:** S007 (created) and S008 (received) under the DateLog rule; S013 GIT; S006 latest closing stock (count and value only); S009 `sparerequired` / indent rows.
- **Key columns:** invoice number, invoice date, GRN number, GRN date, items, shipped vs received quantity, net amount, status (Open/Closed), days open (snapshot - invoice date for open invoices; received - invoice for closed), from location.
- **Filters:** open only, month, item.
- **Sort:** open first, oldest first.
- **Drill-down:** invoice -> lines; "jobs waiting for parts" panel from S009 (job, part required, indent date, days waiting) -> Job history. No data link exists between a pending job and a purchase invoice (1.7), so the two panels sit side by side and are not joined.
- **Export:** both panels.
- **Roles:** Viewer+.
- **Numbers:** open invoices (count, value), oldest open (days), received this month, jobs waiting for parts, GIT lines in the last 30 days.

### 3.7 Money check (tab "Money", task `service-money`, existing) and the freshness strip

- Keep the decision-16 screen as is. Changes: load the manual side even without an S004 reading (SD-08); default the date range to the latest S004 snapshot minus 30 days; export period = the chosen range (already); keep `SERVICE_WDC` out; keep Titan-only (Q7).
- **Freshness strip** (every Service screen, replacing the one-line "as at"): one chip per family group with its latest snapshot date and source kind: Jobs (S002/S036/S037), Status views (S014-S018, S031-S035; show the oldest of the ten), Pending lists (S009/S010), SRN (S011/S012/S013), Money (S003/S004), Claims (S023-S026, S039-S041), Parts (S006-S008), Tests (S030), Deftran (S029). A chip turns amber when its date is older than 7 days and red when older than 14, with the text "last export dd MMM yyyy (consolidated/raw)". Source: `v_service_readings` grouped in C#; no new SQL.

### 3.8 Screens summary

| Screen | Task id | Main source | Roles | New SQL |
|---|---|---|---|---|
| Service Today | `service-today` | `v_service_job`, `v_service_s004_daily`, `v_service_manual_money` | Viewer+ | `v_service_job` |
| Pending board | `service-pending` | `v_service_job`, `v_service_pending_current` (amended) | Viewer+ | `v_service_job`, 0050 ALTER of pending view |
| Job history / Jobs | `service-job-history`, `service-jobs` | `v_service_job`, `v_service_job_readings`, family tables | Viewer+ | `v_service_job_timeline` |
| Claims | `service-claims` | `v_service_claims`, S014/S016 | Viewer+ (Owner for settlement entry, if Q9 = B) | `v_service_claims`, optional table |
| Parts | `service-parts` | S007/S008/S013/S006, S009 | Viewer+ | `v_service_parts` |
| Money check | `service-money` | as 1.9.5 | Viewer+ | none (optional `stores` flag) |

---

## 4. Data model

### 4.1 Job identity

**Key:** `job_order_number` = `NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100), <family job column>))), N'')`, exactly as `v_service_job_readings` already normalises it (section 1.3 proves the format is uniform; no padding, no upper-casing needed, but `UPPER` is harmless and recommended for safety). The universe of jobs is the union of every job column (S002, the ten status views, S009-S012, S019-S023-S026, S029 `srfno`, S030, S036, S037, S039-S041, S003), so a job appears even if only one family ever exported it.

**Readings vs transactions:** unchanged from 0048. JobList families contribute the rows of the latest reading that holds the job (per family); state lists (S009/S010) contribute only when the latest reading holds the job; DateLog families contribute the winning reading per business date. The job view only aggregates what the 0048 reading views already select, so a raw window never hides consolidated history and import order never matters.

**View or table?** Start as views in migration 0050 (`v_service_job`, `v_service_job_timeline`, `v_service_claims`, `v_service_parts`), because the data is small (28k rows, 26 MB) and views keep the "nothing is written" property of the interim. Measure the Pending board and Service Today on the staging copy with three more weekly readings (about 130k rows); if any screen takes over about a second, add in 0051 a table `dbo.service_job_index` (job key `nvarchar(100)` primary key, the same columns as `v_service_job`) filled by a procedure `dbo.refresh_service_job_index` that the import coordinator calls after a Service batch completes (Owner/Store Manager execute, as the `append_*` procedures), with the views then reading the table. Never a trigger on the landing tables (generated, 0048 text frozen).

### 4.2 Stage derivation rule (proposed)

Evaluate in this order per job; the first rule that fires gives the stage. "Latest" means under the 0048 read rules.

1. **DELIVERED** when the job is in S018 (latest reading holding it) or S029 `status` = Delivered with `delivered_date` set. `stage_date` = `deliverydate`.
2. **RETURNED WITHOUT REPAIR** when in S017 (or S029 status RWR). `stage_date` = `normalrwrdate`; `reason` = `rwr_reason`.
3. **DC ISSUED / RA ISSUED** when in S014 / S016 and not 1-2. `stage_date` = `wdcdate` / `wradate`; sub-state `claim_raised` = a document exists in `v_service_claims` for the job (WDC for DC, WRA for RA).
4. **IN TRANSIT BACK** when the latest S010 holds the job with `pendingstore` = `In_Transit` (or PUNS/CSCH). `stage_date` = `repairdate`; `pending_at` = `pendingstore`.
5. **READY FOR DELIVERY** when the latest S010 holds it with `jostatus` = Pending_Delivery, or the job is in S031 PD / S034 REPAIRED. `stage_date` = `repairdate` / `jorepairdate`.
6. **SRN OUT** when S011's latest row for the job is open (`srn_received_date IS NULL AND repaired_date IS NULL AND to_status NOT LIKE '%Received%'`), or the job is in S033/S035 and not above. `stage_date` = `srn_date`; `pending_at` = `to_store`.
7. **INDENT RAISED (parts awaited)** when the latest S009 row has `jostatus` = Indent_Raised or an `indentid`, or the job is in S015. `stage_date` = `indentdate`; `spare_required` from S009.
8. **ON THE BENCH** when the latest S009 holds it (`jostatus` Pending_Repair / SRN_For_Repair) or the job is in S032. `stage_date` = `jodate`.
9. **BOOKED, NO STATUS YET** otherwise (S002/S036/S037 only). `stage_date` = `created_date`.

Alternatives: (a) take S002 `current_status` as the stage: rejected, it is stale (1.6); (b) lifecycle rank of the status views only (0048's rule): rejected, it misses S009/S010/S011 and the 112 booked-only jobs. The rule above reproduces 0048's result for every DELIVERED/RWR/DC/RA job and improves the open ones. A SQL test will pin, on the week fixtures, one job per stage.

### 4.3 TAT and ageing definitions (proposed, with alternatives)

- **Booking date** = `MIN` over the job of S002 `created_date`, status-view `jodate`, S009/S010 `jodate`, S029 `srf_date`, S011 `joborder_date` (they agree where both exist; the MIN guards against a later re-export).
- **TAT (closed jobs)** = `DATEDIFF(day, booking_date, deliverydate)` from S018 (or S029 `delivered_date`), reported **separately for Booking and Quick Billing** (S002 `jotype_booking_quickbilling`; a job without an S002 row is treated as Booking). Alternatives: booking -> `jorepairdate` (workshop TAT, excludes the customer's pick-up delay; 2.41 vs 3.68 days on live) and booking -> RWR date for returned jobs. Recommendation: show both "to repaired" and "to delivered", headline the "to delivered" median for Booking jobs, and the count over 15 days. Q2 decides.
- **Age (open jobs)** = `DATEDIFF(day, booking_date, latest_snapshot_date_of_the_list_that_holds_it)`; **days in stage** = from `stage_date`. Bands 0-7, 8-15, 16-30, 31-60, 60+ (S009's own `pendingnoofdays` equals the first definition on every row, so the export and ETP will agree).
- **Overdue** = `edd IS NOT NULL AND edd < snapshot_date` (S009 `edd`, status-view `edd`); where EDD is null (half the jobs), fall back to "over N days in stage" with N per stage (bench 7, indent 15, SRN out 30, in transit 15, ready for delivery 7). Q4 decides N.

### 4.4 SQL sketch of the main view (0050; CREATE OR ALTER through EXEC, as 0048)

```sql
-- dbo.v_service_job: one row per Service job (key proven in SERVICE-CENTRE-UI-DESIGN-REVIEW-2026-10-10.md 1.3).
-- Reads only the 0048 reading views, so the read rules (latest reading per job / list / date) are inherited.
CREATE OR ALTER VIEW dbo.v_service_job AS
WITH keys AS (                                  -- the universe of job keys
  SELECT job_order_number FROM dbo.v_service_job_readings
  UNION SELECT NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),srfno))),N'') FROM dbo.etp_landing_s029
  UNION SELECT NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),joborder_number))),N'') FROM dbo.etp_landing_s003
  UNION SELECT NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jo_number))),N'') FROM dbo.etp_landing_s019
  UNION SELECT NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),joborder_number))),N'') FROM dbo.etp_landing_s022
  UNION SELECT job_order_number FROM dbo.v_service_claims
), latest_view AS (                             -- per status view, the latest reading holding the job (0048 rule)
  SELECT r.job_order_number, r.report_code, r.import_file_id, r.snapshot_date,
         ROW_NUMBER() OVER (PARTITION BY r.job_order_number, r.report_code ORDER BY r.snapshot_date DESC, r.import_file_id DESC) rn
  FROM dbo.v_service_job_readings r
), sv AS (                                      -- the status-view line rows of those readings, aggregated per job
  SELECT s.job_order_number,
         MIN(s.job_date) jodate, MAX(s.edd) edd, MAX(s.brand) brand, MAX(s.model) model, MAX(s.product_category) product_category,
         MAX(CASE WHEN s.report_code='S018' THEN s.status_date END) deliverydate,
         MAX(CASE WHEN s.report_code='S017' THEN s.status_date END) rwrdate,
         MAX(CASE WHEN s.report_code='S014' THEN s.status_date END) wdcdate,
         MAX(CASE WHEN s.report_code='S016' THEN s.status_date END) wradate,
         MAX(CASE WHEN s.report_code IN ('S034','S031') THEN s.status_date END) jorepairdate,
         MAX(CASE WHEN s.report_code='S033' THEN s.status_date END) srnissueddate,
         MAX(CASE WHEN s.report_code='S015' THEN s.status_date END) indentdate,
         MAX(CASE WHEN s.report_code='S018' THEN 1 ELSE 0 END) in_delivered, MAX(CASE WHEN s.report_code='S017' THEN 1 ELSE 0 END) in_rwr,
         MAX(CASE WHEN s.report_code='S014' THEN 1 ELSE 0 END) in_dc, MAX(CASE WHEN s.report_code='S016' THEN 1 ELSE 0 END) in_ra,
         MAX(CASE WHEN s.report_code IN ('S034','S031') THEN 1 ELSE 0 END) in_pd, MAX(CASE WHEN s.report_code IN ('S033','S035') THEN 1 ELSE 0 END) in_srn,
         MAX(CASE WHEN s.report_code='S015' THEN 1 ELSE 0 END) in_ir, MAX(CASE WHEN s.report_code='S032' THEN 1 ELSE 0 END) in_pr,
         SUM(s.spare_value) spare_value, SUM(s.labour_charge) labour_charge
  FROM dbo.v_service_status_view_rows s
  JOIN latest_view v ON v.job_order_number=s.job_order_number AND v.report_code=s.report_code AND v.import_file_id=s.import_file_id AND v.rn=1
  GROUP BY s.job_order_number
), booking AS (                                 -- S002 / S036 / S037 job lists: booking date and job type
  SELECT j.job_order_number, MIN(b.created_date) created_date,
         MAX(CONVERT(nvarchar(40), b.jotype_booking_quickbilling)) jo_type, MAX(CONVERT(nvarchar(60), b.current_status)) exported_status
  FROM latest_view j JOIN dbo.etp_landing_s002 b ON b.import_file_id=j.import_file_id
       AND NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),b.job_order_no))),N'')=j.job_order_number
  WHERE j.report_code='S002' AND j.rn=1
  GROUP BY j.job_order_number
), pend AS (                                    -- latest S009 / S010 state lists (membership = is_latest reading)
  SELECT p.job_order_number,
         MAX(CASE WHEN p.[list]='PENDING_REPAIR' THEN 1 ELSE 0 END) in_s009, MAX(CASE WHEN p.[list]='PENDING_DELIVERY' THEN 1 ELSE 0 END) in_s010,
         MAX(p.pending_store) pending_store, MAX(p.snapshot_date) list_snapshot_date
  FROM dbo.v_service_pending_current p WHERE p.[list] IN ('PENDING_REPAIR','PENDING_DELIVERY')
  GROUP BY p.job_order_number
), s009x AS (                                   -- jostatus / indent / spare from the latest S009 reading
  SELECT NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),s.jonumber))),N'') job_order_number,
         MAX(CONVERT(nvarchar(60),s.jostatus)) jostatus, MAX(s.indentdate) indentdate, MAX(s.edd) edd,
         MAX(CONVERT(nvarchar(200),s.sparerequired)) spare_required
  FROM dbo.etp_landing_s009 s JOIN dbo.v_service_readings r ON r.import_file_id=s.import_file_id AND r.report_code='S009' AND r.is_latest=1
  GROUP BY NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),s.jonumber))),N'')
), s010x AS (
  SELECT NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),s.jonumber))),N'') job_order_number,
         MAX(CONVERT(nvarchar(60),s.jostatus)) jostatus, MAX(s.repairdate) repairdate
  FROM dbo.etp_landing_s010 s JOIN dbo.v_service_readings r ON r.import_file_id=s.import_file_id AND r.report_code='S010' AND r.is_latest=1
  GROUP BY NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),s.jonumber))),N'')
), srn AS (                                     -- S011: latest reading per job, open when nothing says it came back
  SELECT w.job_order_number, MAX(s.srn_date) srn_date, MAX(CONVERT(nvarchar(60),s.to_store)) to_store,
         MAX(CONVERT(nvarchar(80),s.to_status)) to_status,
         MIN(CASE WHEN s.srn_received_date IS NULL AND s.repaired_date IS NULL AND CONVERT(nvarchar(80),s.to_status) NOT LIKE '%Received%' THEN 1 ELSE 0 END) srn_open
  FROM latest_view w JOIN dbo.etp_landing_s011 s ON s.import_file_id=w.import_file_id
       AND NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),s.joborder_number))),N'')=w.job_order_number
  WHERE w.report_code='S011' AND w.rn=1
  GROUP BY w.job_order_number
), claims AS (
  SELECT job_order_number, MAX(CASE WHEN claim_type='WDC' THEN 1 ELSE 0 END) wdc_claimed, MAX(CASE WHEN claim_type='WRA' THEN 1 ELSE 0 END) wra_claimed
  FROM dbo.v_service_claims GROUP BY job_order_number
), snap AS (SELECT MAX(snapshot_date) as_at FROM dbo.v_service_readings)
SELECT k.job_order_number,
  COALESCE(b.created_date, sv.jodate) booking_date, COALESCE(b.jo_type, 'Booking') jo_type, b.exported_status,
  sv.brand, sv.model, sv.product_category, COALESCE(sv.edd, s9.edd) edd,
  st.stage, st.stage_date, st.pending_at, s9.spare_required, sv.spare_value, sv.labour_charge,
  CASE WHEN st.stage IN ('DELIVERED','RWR') THEN DATEDIFF(day, COALESCE(b.created_date, sv.jodate), st.stage_date) END tat_days,
  CASE WHEN st.stage NOT IN ('DELIVERED','RWR') THEN DATEDIFF(day, COALESCE(b.created_date, sv.jodate), snap.as_at) END age_days,
  CASE WHEN st.stage NOT IN ('DELIVERED','RWR') THEN DATEDIFF(day, st.stage_date, snap.as_at) END days_in_stage,
  CASE WHEN st.stage NOT IN ('DELIVERED','RWR') AND COALESCE(sv.edd, s9.edd) < snap.as_at THEN 1 ELSE 0 END is_overdue,
  snap.as_at
FROM keys k
LEFT JOIN sv ON sv.job_order_number=k.job_order_number
LEFT JOIN booking b ON b.job_order_number=k.job_order_number
LEFT JOIN pend p ON p.job_order_number=k.job_order_number
LEFT JOIN s009x s9 ON s9.job_order_number=k.job_order_number
LEFT JOIN s010x s10 ON s10.job_order_number=k.job_order_number
LEFT JOIN srn ON srn.job_order_number=k.job_order_number
LEFT JOIN claims c ON c.job_order_number=k.job_order_number
CROSS JOIN snap
CROSS APPLY (SELECT                              -- section 4.2, first rule wins
  CASE WHEN sv.in_delivered=1 THEN 'DELIVERED'
       WHEN sv.in_rwr=1 THEN 'RWR'
       WHEN sv.in_dc=1 THEN 'DC_ISSUED'
       WHEN sv.in_ra=1 THEN 'RA_ISSUED'
       WHEN p.in_s010=1 AND p.pending_store<>'AW330' THEN 'IN_TRANSIT_BACK'
       WHEN p.in_s010=1 OR sv.in_pd=1 THEN 'READY_FOR_DELIVERY'
       WHEN srn.srn_open=1 OR sv.in_srn=1 THEN 'SRN_OUT'
       WHEN s9.jostatus='Indent_Raised' OR s9.indentdate IS NOT NULL OR sv.in_ir=1 THEN 'INDENT_RAISED'
       WHEN p.in_s009=1 OR sv.in_pr=1 THEN 'ON_BENCH'
       ELSE 'BOOKED' END stage,
  CASE WHEN sv.in_delivered=1 THEN sv.deliverydate WHEN sv.in_rwr=1 THEN sv.rwrdate WHEN sv.in_dc=1 THEN sv.wdcdate WHEN sv.in_ra=1 THEN sv.wradate
       WHEN p.in_s010=1 THEN s10.repairdate WHEN sv.in_pd=1 THEN sv.jorepairdate WHEN srn.srn_open=1 THEN srn.srn_date WHEN sv.in_srn=1 THEN sv.srnissueddate
       WHEN s9.indentdate IS NOT NULL THEN s9.indentdate WHEN sv.in_ir=1 THEN sv.indentdate ELSE COALESCE(b.created_date, sv.jodate) END stage_date,
  CASE WHEN p.in_s010=1 THEN p.pending_store WHEN srn.srn_open=1 THEN srn.to_store ELSE 'AW330' END pending_at) st
WHERE k.job_order_number IS NOT NULL;
```

Supporting views in the same migration: `v_service_claims` (union of the eight claim families to one shape: `claim_type` GPRC/MB/WDC/WRA, `business_date`, `document_number`, `job_order_number`, `item_id`, `quantity`, `net_amount_inc_tax`, `ucp_value`, `report_code`, `snapshot_date`; new-header family wins per `document_number`, as `v_service_gprc_claims`), `v_service_job_timeline` (one row per reading row per family for a job, with the family's event date and exported status, no phone/e-mail/address column), `v_service_parts` (S007 left-joined to S008 on `invoice_number`, days open, status), and a `CREATE OR ALTER` of `v_service_pending_current` with the S011 closure rule (SD-02) and the extra columns (SD-12). `v_service_job_list_events` is left as is for S009/S010 and no longer drives the Job history screen (SD-01).

### 4.5 Migrations needed (1.10.0 numbers from 0050; 0049 is the Tally cost-centre migration; 0048 is never edited)

| Number | Content | Writes data? |
|---|---|---|
| 0050 `service_job_model.sql` | `v_service_job`, `v_service_claims`, `v_service_job_timeline`, `v_service_parts`; `CREATE OR ALTER` of `v_service_pending_current`; grants/denies as 0048 section C; section markers per lane | no |
| 0051 `service_claim_settlements.sql` (only if Q9 = B) | table `service_claim_settlements` (document_number, claim_type, settled_on, reference, entered_by, entered_utc), procedure `record_service_claim_settlement` (Owner only), `v_service_claims` gains `settled_on` | Owner entries |
| 0052 `service_job_index.sql` (only if measured slow) | table `service_job_index` + `refresh_service_job_index`; `v_service_job` reads it | refresh after import |
| (optional, with 0050) | `stores.is_service_money_shop` + `v_service_manual_money` reading it (SD-07) | one UPDATE of the WLMHW row |

Every view text is pinned by a text test (as `ServiceReadModelTextTests`) asserting no phone/e-mail/address column name, and by a SQL test on the week fixtures.

---

## 5. Open questions for the owner (each with a recommended answer)

1. **Job key.** Use the exported job order number, trimmed, as the one key across all families (no padding, no composite). *Recommended: yes.* Evidence: 100 % join rates, 0 collisions between the 15- and 16-character forms (1.3).
2. **TAT definition.** Booking -> delivered (customer view) or booking -> repaired (workshop view)? *Recommended: show both; headline "booking -> delivered" median for Booking jobs, with Quick Billing excluded from the headline and shown as its own count.* On live the two averages are 3.68 and 2.41 days, and 2,022 of 2,452 delivered jobs are same-day quick billing.
3. **Which statuses count as "pending".** *Recommended:* everything that is not DELIVERED or RWR: on the bench, indent raised, SRN out, in transit back, ready for delivery, DC/RA issued without a claim, booked-no-status. Add "DC/RA issued and claimed" to a closed group ("closed by claim") so the board does not carry 108 DC jobs forever.
4. **What "overdue" means.** *Recommended:* EDD passed where an EDD exists (half the jobs have one); otherwise days-in-stage over a per-stage limit: bench 7, indent 15, SRN out 30, in transit 15, ready for delivery 7. Confirm the five numbers.
5. **SRN closure rule (S011).** *Recommended:* an SRN is closed when `srn_received_date` is set, or `repaired_date` is set, or `to_status` contains "Received"; otherwise open (10 of 141 on live). The interim view lists all 141.
6. **S027 TAT and S028 Technician productivity (deferred, Not needed today).** *Recommended: do not import them for 1.10.0.* TAT is computed from S018/S029 dates (1.5), and S029 Deftran already carries `mechanic_name`, `defect_code`, labour and spare cost per job (7 mechanics, 48 defect codes on live), which gives a productivity view without the consolidation-column layout problem. Revisit when the raw S028 header is confirmed.
7. **Service money stays Titan-only (decision 16).** *Recommended: yes, unchanged.* Optionally move the WLMHW literal to a `stores` flag (SD-07) so a shop change needs no migration; the comparison rules do not change.
8. **Do delivered jobs leave the board?** *Recommended:* the Pending board never shows DELIVERED/RWR; the Jobs list shows them with a default filter "closed in the last 30 days" (Quick Billing included) and a "show all" switch; Job history always finds any job.
9. **Claims: raised only, or outstanding vs settled?** No export carries settlement (1.7). *Recommended: A, raised-only in 1.10.0*, with "DC/RA issued, not yet claimed" as the actionable number; add the Owner-entered settlement table (0051) in a later release if Titan statements are to be reconciled in ETP.
10. **S036 / S037 meaning.** Their `created_date` is the booking date (1.4). *Recommended:* treat them as job lists ("Delivered-type jobs" and "Repair-type jobs" of the export window) and take delivery/repair dates from S018/S029/S034/S031; rename the labels in 0050's views. Confirm what the two reports are for at the counter.
11. **GPRC gap 6 Aug - 29 Sep 2026.** *Recommended:* export `GPRC CLAIM 01.08.2026 TO <today>` once (answers document Q8) before the Claims screen is accepted; the freshness strip will otherwise show GPRC as stale.
12. **WDC/WRA old vs new header overlap on 1 Jul 2026.** S025/S026 end and S039/S040 start on 1 Jul 2026. *Recommended:* union by `document_number` with the new-header family winning, as for GPRC; confirm no claim of 1 Jul is to be counted twice.
13. **Users and roles for the Service section.** *Recommended:* Viewer+ for every Service screen (as today), Owner for any entry (settlement, if Q9 = B); the Service Centre staff get Viewer accounts (live has 7 users, 2 active) and never Store Manager, so they cannot import Retail or enter cash.
14. **Refresh cadence and what triggers the freshness colours.** *Recommended:* raw daily pack on working days (amber at 7 days, red at 14), consolidated workbook monthly for the families that have no raw export (S011-S014, S016, S019-S021, S023-S026, S030, S033, S035) until ETP exports them raw.
15. **Where "Service today" counts come from on a day with no raw file yet.** *Recommended:* the screen defaults to the latest snapshot date and says so; it never shows today's date with zeros.

---

## 6. Phased plan

### 6.1 1.10.0 scope (Service UI wave)

In: the "Service" rail section with six tabs (3.1-3.7); migration 0050 (views, pending-view amendment); the freshness strip; Quick Billing split; TAT/age/overdue per section 4.3; claims raised-only (Q9 = A); parts created vs received; the SD-01..SD-12 fixes; User Guide, schema doc, CHANGELOG; tests below. Out (unless the owner says otherwise): S027/S028 import, claim settlement entry (0051), the materialised index (0052, only if measured), any Service writes, any change to Retail, the money-check rules of decision 16.

### 6.2 Lanes (parallel agents, branches `fix110/svcui-<lane>`, worktrees `Code\Worktrees\svcui-<lane>`)

| Lane | Scope | Owns | Depends on | Days |
|---|---|---|---|---|
| **U0 model** | 0050 skeleton with section markers; `v_service_job`, `v_service_claims`, `v_service_job_timeline`, `v_service_parts`; `CREATE OR ALTER` pending view; view-text tests; week-fixture SQL tests (one job per stage, SRN open/closed, claim union on 1 Jul, parts open) | `database/migrations/0050_service_job_model.sql`, `SqlServer.Tests/Service/ServiceJobModelTextTests.cs`, `IntegrationTests/Service/ServiceJobModelSqlTests.cs` | none | 3 |
| **U1 contracts + query** | `IServiceReportQuery` additions (`LoadTodayAsync`, `LoadPendingBoardAsync`, `LoadJobAsync`, `LoadClaimsAsync`, `LoadPartsAsync`, `LoadFreshnessAsync`), records, `SqlServerServiceReportQuery` methods, pure C# for bands/overdue/freshness colours | `Application/Service/*`, `SqlServerServiceReportQuery.cs`, unit tests | U0 column names (agreed on day 1 in the contract file) | 2.5 |
| **U2 shell + Today + Pending** | "Service" rail section, tab order, task ids, Help topic, freshness strip in `ServiceScreenView`, Service Today cards, Pending board with groups/bands/drill-down | `TaskNavigation.cs`, `ShellRouteRegistry.cs`, `TaskNavigator.cs`, `Modules/Service/ServiceTodayView.cs`, `ServicePendingBoardView.cs`, `ServiceScreenView.cs` (strip), `Desktop.Tests/Service/*`, pinned counts in `TaskNavigationTests`, `PhaseFiveFullWindowCaptureTests` | U1 contract | 3 |
| **U3 Job history + Jobs** | Header card, timeline grid, open-from-any-grid navigation, Jobs sub-tab with stage/TAT/Quick Billing, export periods (SD-09) | `ServiceJobHistoryView.cs`, `ServiceJobsView.cs`, tests | U1 | 2.5 |
| **U4 Claims + Parts + Money** | Claims screen (raised, not-yet-claimed, union rule), Parts screen (open invoices, ageing, jobs waiting for parts), money-check SD-08 and default range, status-line clear (SD-10) | `ServiceClaimsView.cs`, `ServicePartsView.cs`, `ServiceMoneyView.cs`, tests | U1 | 3 |
| **U5 docs + acceptance** | User Guide "Service" section rewrite, `03_DATABASE_SCHEMA.md` 0050, CHANGELOG, `SERVICE-INTERIM-NUMBERS.md` rows, acceptance runbook on the staging copy (import three weekly readings, time each screen, SD-11 decision), screenshots with the redaction check | docs, `Reference\Work in progress <date>\service-ui\` | U0-U4 merged | 2 |

Peak: 6 implementers + reviewers, well under the 20-agent cap; builds serialised through `Invoke-Serialized.ps1` as in 1.9.7. Calendar: about 5-6 working days with reviews, then the gate.

### 6.3 Tests

- SQL (IntegrationTests, week fixtures in `tests-dotnet/fixtures/service-interim/`): one fixture job per stage of 4.2; a job that moves bench -> indent -> SRN out -> in transit -> ready -> delivered across three readings keeps one row in `v_service_job` with the right stage each time; raw window after a consolidated reading changes only its jobs (reuse `ServiceReadModelSqlTests` scaffolding); SRN open/closed; WDC union across S025/S039 on the same date counts one document; S007 invoice without S008 is open with the right age; no phone/e-mail/address column in any 0050 view (`sys.columns` assertion); grants: Viewer can SELECT every 0050 view and cannot write.
- Text tests (SqlServer.Tests): 0050 contains only `CREATE OR ALTER VIEW` through `EXEC`, grants/denies for every view, none of the privacy column names.
- Unit (Application): stage bands, overdue rule with and without EDD, freshness colours, Quick Billing split, TAT median.
- Desktop (Desktop.Tests, as `ServiceScreenViewTests`): each new view with a fake query: cards, grouping, drill-down navigation, empty and "no data" states, export columns equal grid columns, redaction (no column header contains Phone/Mobile/E-mail/Address), navigation counts, role gating (Viewer sees all six; nothing requires Store Manager).
- Gate: full unfiltered run (never filter gate output); the number of SQL integration tests rises by about 12 and Desktop tests by about 25.

### 6.4 What is reused

`ServiceScreenView` frame, `ServiceExcelExport`, `ServiceGridRow`/`ServiceColumn`, `TablePresentation`, `KpiCard`/`Card` from `DailySalesReportControls.cs`, `TaskNavigation` registration and `HelpCentre` topic, the 0048 reading/window/job-readings/status-view-rows views and `v_service_gprc_claims` (its union rule generalised), `ServiceMoneyCheck` unchanged, the fixtures and the three SQL test classes of the interim, the growth script `measure-service-growth.sql`.

### 6.5 Risks

| Risk | Why it matters | Mitigation |
|---|---|---|
| Job-key ambiguity | Low on the evidence (1.3), but a future export could carry a different store prefix or a padded number | Key is the trimmed exported text; a SQL test asserts every key matches `JOAW330[0-9]{8,9}` on the week fixtures and a corpus-style check logs counts only; a job with another shape still appears (it is just a key) |
| Raw vs consolidated overlap | Consolidated rebuilds restate status; raw windows are period filtered; S025/S039 (and S026/S040) meet on 1 Jul 2026 | Keep the 0048 read rules; union claims by document number; the "LeftList" semantics fixed in SD-01 so raw windows never read as departures |
| Stale consolidated-only families | 15 families have no raw export; the board would show SRN/DC/RA states frozen at 29 Sep | Freshness strip per group; owner Q14 on cadence; stage rule prefers the fresher list when two disagree (status views over S002) |
| Empty reason columns | `reasonforpending` is null in every row; "pending by reason" has to be built from `jostatus`, `pendingstore`, `sparerequired`, indent and SRN facts | Say so on the screen; no fake reasons |
| S010 long tail | 73 of 84 "pending delivery" jobs are over 60 days, nearly all in transit after an SRN | Group by stage; two ages; owner Q3/Q4 decide whether in-transit jobs count as AW330's backlog |
| Claims settlement | Cannot be derived; a screen that implies "outstanding" would be wrong | Raised-only wording (Q9 = A) |
| PC load | 83-column unions on every activation; weekly readings add about 35k rows | Views first; measure on the staging copy with three extra readings; 0052 index if over about 1 s; heavy-job lock for the gate as 1.9.6/1.9.7 |
| Pinned navigation counts | Moving four tasks to a new rail changes pinned counts in two test classes | U2 updates them in the same commit |
| Privacy | 17 landing tables carry phone/e-mail columns | Column-by-name views, text tests and a Desktop header test (6.3) |

---

## Sources read

`E:\ETP\NEW-PC-HANDOFF.md` (top bullets); `CHANGELOG.md` [1.9.5]; `docs/roadmap/SERVICE-CENTRE-IMPORT-PLAN-2026-09-29.md` (worktree `service-centre-import`); `docs/service-centre/SERVICE-INTERIM-NUMBERS.md`, `P8-IDENTITY-TARGETS.md`; `docs/03_DATABASE_SCHEMA.md` (0048 section); `docs/USER-GUIDE.md` "Service Centre (interim)"; `Reference\Work in progress 2026-10-03\service\SERVICE-INTERIM-DESIGN.md`, `SERVICE-LANES.md`; `Reference\Work in progress 2026-10-04 (CONTAINS BUSINESS DATA)\SERVICE-OPEN-QUESTIONS-ANSWERS.md` (decision 16 answers); `Migration 2026-10-02\DECISIONS-2026-10-02-evening.md` (decisions 15-24); `Migration 2026-10-04\PR3-RECONCILE-AND-1.10.0-UPDATE.md`; `Reference\Work in progress 2026-10-03\PLAN-1.10.0.md`; `Reference\Work in progress 2026-10-09\REPORT-AUDIT\OPS-FINDINGS.md` (RA-OPS-07/08/09/21, manual-field table) and `UI-WALK-FINDINGS.md` (Service rows); `database/migrations/0048_service_centre_interim.sql` (section C, 13 views); `src/Etp.Reporting.Desktop/Modules/Service/*.cs`, `TaskNavigation.cs`, `DailySalesReportControls.cs`; `src/Etp.Reporting.Infrastructure.SqlServer/SqlServerServiceReportQuery.cs`; `src/Etp.Reporting.Application/Service/*.cs`; the Service test classes under `tests-dotnet`. The query files `q1_jobkeys.sql` .. `q6_misc.sql` used for section 1 are in the session scratchpad and contain only SELECTs.
