# Current workflow guide — development candidate

This describes the Phase 5 development candidate. See the [Phase 5 report](audit/PHASE-5-REPORT.md) for test evidence and deployment acceptance still required.

## Import and reports

Choose the header date/store before reviewing data. Import → History → Imports shows saved outcomes after restart; Received files is the separate retained-document inbox. Repeated imports appear as Duplicate without adding facts. Undetected periods use the attempt's recorded UTC date.

For failures in the current import session, correct the source and open Import → Problems → Retry failed. Only failed paths are retried; Ctrl+R remains available.

On supported reports, expand Filters, set query criteria and Apply. The applied scope describes both screen and Excel/PDF output. Clear restores the unfiltered scope. Detail-row search only filters the displayed list.

## Accounting — Owner decisions

Open Settings → Accounting → Prepare → Review → Export. Select the store/date in the header and preview a final report generation. Owner-approved adjustments require an approved ADJUSTMENT ledger mapping; pending adjustments are excluded. Missing mappings or a missing company produce a BLOCKED batch with a reason. A balanced, configured draft can be saved for review.

Select a saved batch and enter a Batch decision reason. Approve selected stores the reason. Reject selected records a rejection reason, actor and time while preserving earlier approval evidence. Blank reasons and non-Owner decisions are refused. Exported and already-rejected batches cannot be rejected.

Configure the company and TEST environment in Settings → Integrations → Email, sharing and Tally. Leave the company blank until it is known. Saving PRODUCTION requires typing the exact company name; production export remains blocked pending the later Tally acceptance gate.

Batch statuses are DRAFT, BLOCKED, APPROVED_READY, EXPORTED_AWAITING_IMPORT and REJECTED. Each invoice can belong to only one non-rejected batch. Rejecting an unexported batch frees its invoices for a replacement; exported batches remain reserved. Export history records the exact file path, SHA-256, company, environment, actor and time. XML export is not evidence of import into Tally.

## Registers, investigation and approvals

Inward, Outward and Stock transfers are under Stock → Registers. Credit notes, Service receipts, Vendor invoices and Courier are under Today → Close day; Expense entry is under Today → Cash. The Close day workspace also lists the selected store-day's entries and opens a selected register. Store Managers and Owners create/edit drafts. Only Owners verify entries with a reason; locked-day edits are refused. Starting a new entry asks before discarding unsaved changes.

Reports → Investigation searches invoices, items, source files, saved packs, received documents and registers. Open a result to carry its date, store and reference into the relevant workspace. Role restrictions still apply.

For a changed overlapping import, request a restatement with a reason. An Owner reviews it under Settings → Control centre → Approvals. Approval binds the exact file hash, store and period; retry that same file after approval. Requests and approval alone do not replace reporting facts. Approvals show pending and decided history. Adjustment requests and decisions are Owner-only under the recorded owner policy.

## Saved packs and sharing

Reports → Archive provides Open, Excel, PDF, ZIP and Share for each saved pack. Choose a contact or enter the recipient. PDF is prepared from the selected immutable generation. Email uses configured SMTP; its history distinguishes Started, Accepted by mail server, Failed and Unconfirmed. Server acceptance does not prove recipient delivery. Check the mailbox/server before retrying an unconfirmed attempt.

WhatsApp opens the desktop handoff and copies the PDF path. Attach the PDF and send it yourself. History records only that the handoff is ready.

Owners manage contacts under Settings → Integrations → Sharing contacts, and SMTP server/sender settings under Email, sharing and Tally. In the Archive's Filters & input tab, each sender can save optional personal SMTP credentials; the Owner can explicitly confirm a test send to the entered recipient. Credentials are protected for the Windows account that saves them; other accounts need their own credentials when authentication is required.

## Stores and automatic import

Settings → Stores & masters → Stores is the active store catalogue. Enabled stores drive selectors, combined reports and automatic-import scope detection. Single-store reports require one store; combined reports include all active stores. Advanced custom store filters remain selected through refresh. Brands, targets and tender mappings retain their dedicated screens.

Settings → Automatic import combines watch folders, report schedules, recent runs and the installed Windows task's state, last result and next run. Saving Enabled does not install a scheduled task. Run now uses saved settings; unattended operation requires the separately installed Windows task. The obsolete polling-minutes field is removed. XLSX and ZIP imports remain supported; unsupported workbooks are marked Not needed. Received files remains a separate document inbox; it does not extract financial facts from PDF/images.

Help is searchable from Settings → Help and includes screenshots of the navigation areas (Today, Import, Reports, Stock, Service and Settings; the Service picture arrives with the next screenshot run). It describes the implemented workflows and role limits.

## Service Centre

ETP imports the Service Centre (AW330) exports and shows them under **Service** on the left: six read-only tabs, Today, Pending, Jobs, Claims, Parts and Money. Nothing in Retail changes, and nothing on these screens writes to the database. Viewers, Store Managers and the Owner can open every Service tab. There is no store picker: Service is always "Service Centre AW330". No Service screen or export shows a customer phone number, e-mail or address.

### Weekly refresh (consolidated workbooks)

Copy the consolidated Service folder into a folder whose name ends with the date, for example `Service Centre till 05 oct 2026`, and import that folder (Import → choose the folder). ETP dates every file in it with the folder date. If the folder has no date, rename it or copy the files into a dated folder. The snapshot date box on the Import screen is used only when Restate is ticked and a reason is entered, which starts a restatement for approval; it is not a shortcut for a first import.

A folder without a date is refused with "Put the Service files in a folder whose name ends with the date, e.g. 'Service Centre till 05 oct 2026'. The Import screen's date is only for a restatement." Rename the folder or copy the files into a dated folder and import again. The package folder `Service Centre (no importer profile)` has no date, so copy its files into a dated folder first.

Expect 35 files Imported (ETP imports 36 Service families; the 36th, GPRC CLAIM, comes only as a raw export, below). These are reported Not needed, which is correct:

- S001 Repair register: it repeats the ten status lists, so it would count every job twice.
- S005 Tender collection summary: not needed; S004 has the tender detail.
- S038 SRN report: a retired name for S011.
- S027 TAT and S028 Technician productivity: not imported. ETP works out the turnaround time from the delivery dates itself, and the Deftran report already names the mechanic for each job (decision 25).
- The `00_` consolidation control file.

When the folder date differs from the latest date on the "Snapshot History" sheet of S006, S009 or S010, the import shows a warning and still imports. Check the folder name.

Some families come only in the consolidated workbook, never as a raw export: SRN status and history, goods in transit, the DC, RA, SRN and SRNINV status lists, repeat returns, replacement, depreciation, the old-format claim reports and the running tests. Import the consolidated workbook at least once a month, or those parts of the screens go stale; the freshness strip (below) shows which.

### Daily raw exports

Drop the ETP Service export files as they come, CSV or XLSX, into the import (or the automatic-import watch folder). Their names look like `JOB REPORT 30.09.2026 TO 03.10.2026.csv` or `PENDING REPORT 03.10.2026.csv`. ETP takes the date from the file name: the end date of the window, or the single date. Keep the names as ETP exports them.

The raw files cover a few days each. The screens combine them with the weekly workbook: for each day, job and list, the latest file that covers it wins, so a raw file never hides older weeks and importing in a different order gives the same result.

GPRC CLAIM (`GPRC CLAIM 01.08.2026 TO 07.08.2026.xlsx`) is imported as its own family, S041. The consolidated GPRC report (S023) holds the claims up to 5 Aug 2026 and GPRC CLAIM the claims after; where both hold the same claim document, GPRC CLAIM is used, so no claim is counted twice.

Not imported: technician productivity and the tender collection summary (both reported Not needed), and TAT (TATA REPORT, shown as an unknown layout). None of them is shown as Failed. In the automatic-import watch folder an unknown layout counts as a failed source, so a TATA REPORT dropped there goes to the Failed folder; leave it out.

### What every Service screen shows

- **The title, "Service Centre AW330 - read only"**, and the line "Service data as at <date>", where the date is that of the newest Service export imported. If nothing is imported yet, the screen says "No Service data imported yet" (the Money tab still shows the manual entries).
- **The freshness strip**: one chip per group of exports, with the date of its last export and whether that was the weekly workbook (consolidated) or a raw export, for example "Jobs: last export 03 Oct 2026 (raw)". The groups are Jobs, Status views, Pending lists, SRN, Money, Claims, Parts, Tests and Deftran. A group's date is that of its oldest report, so one stale report is enough to colour the chip.
  - **No colour**: exported in the last 7 days.
  - **Amber**: the last export is more than 7 days old.
  - **Red**: more than 14 days old.
  - Reports that come only in the monthly workbook (no raw export yet: SRN status and history, GIT, the DC and RA lists, SRN and SRNINV, repeat returns, replacements, depreciation, the GPRC/MB/WDC/WRA claim reports and test runs) are judged against a month instead: amber after 38 days, red after 45.
  - "no export yet" or "some families never exported": that part of the screens has nothing, or only part, to show.

  An amber or red chip does not mean the numbers are wrong. They are true as at that export date; import a newer export to bring them up to date.
- **Refresh** reads the database again. **Export to Excel** writes what the screen shows: the grid's columns, with the period and the as-at line.
- **A job number in any job grid opens that job's history**: double-click the row, press Enter, or use **Open job history**. **Back** returns to the screen you came from.

### Where a job is: the stages

ETP puts every job in one stage, reading the newest exports together. The job report alone is not enough, because its status column is not updated once a job has been exported. The first stage that applies, in this order, is the job's stage:

1. **Delivered**: in the DELIVERED list (or Deftran says delivered).
2. **Returned without repair (RWR)**: in the RWR list.
3. **DC issued** and **RA issued**: a depreciation (DC) or replacement (RA) was issued. Once the WDC or WRA claim for it has been raised, the job counts as **closed by claim**.
4. **Sent back after repair, in transit**: the latest Pending delivery list holds the job at a place other than AW330 (in transit, PUNS, CSCH).
5. **Ready for delivery**: the latest Pending delivery list holds it at AW330, or it is in the PD or REPAIRED list.
6. **SRN out for repair**: the watch went out on an SRN that has not come back. An SRN counts as back when it has a received date or a repaired date, or its status says Received. The SRN and SRNINV lists only say a job once went out on an SRN, so they put a job here only when the SRN status report does not hold it.
7. **Indent raised, parts awaited**: an indent was raised for a part.
8. **On the bench**: on the Pending repair list (or the PR list) with no indent.
9. **Back from SRN**: when nothing above applies and the job's SRN has come back, the job is **DC issued** if the SRN status says a DC was created, otherwise **Ready for delivery**, dated by the day the SRN came back.
10. **Booked, no status yet**: only the job report holds it so far.

When the latest Pending repair list holds a job, that list decides between SRN out, indent raised and on the bench, because it is fresher than the monthly status lists.

On the Pending board the open stages are shown in the order work moves through the centre: Booked, On the bench, Indent raised, SRN out, Ready for delivery, In transit, then DC issued and RA issued that are not yet claimed.

**Open and closed.** A job is open until it is Delivered, Returned without repair, or closed by claim. Open jobs are on the Pending board; closed jobs are on the Jobs list.

**Age, days in stage and age bands.** "Days since booking" counts from the booking date to the as-at date. "Days in stage" counts from the date the job reached its stage (for example the indent date or the SRN date). The age bands used to filter the board are 0-7, 8-15, 16-30, 31-60 and over 60 days since booking.

**Overdue.** A job is overdue when its promised date (EDD) has passed. About half the jobs have no EDD; such a job is overdue when it has been in its stage longer than:

| Stage | Overdue after |
|---|---|
| On the bench | 7 days |
| Indent raised, parts awaited | 15 days |
| SRN out for repair | 30 days |
| Sent back after repair, in transit | 15 days |
| Ready for delivery | 7 days |

"Overdue by" is the number of days past the EDD, or past that limit. Booked and DC/RA jobs have no limit; they are overdue only when an EDD has passed.

**Turnaround time (TAT).** For a delivered job, TAT is the number of days from booking to delivery. Jobs returned without repair are closed but are not in the TAT headline. **Booking** jobs and **Quick Billing** jobs (battery, strap and other counter work, almost all delivered the same day) are always shown separately, because mixing them would make the workshop look faster than it is. The headline is the Booking median (the middle value: half the jobs took less, half took more), with the number of Booking jobs over 15 days. A job with no job-report row counts as Booking. The job's header also has booking to repaired, which leaves out the days the watch waited for the customer.

### Today

The morning view for one business date. The date defaults to the latest day the Service exports hold data for, not the calendar date. A raw export is named by the day it was taken and holds data up to the day before, so that is usually the day before the newest export. The screen never shows a row of zeros on a Sunday or before the day's export arrives. It names the date it shows, and warns when you pick a date after the newest export. The cards:

- **Booked**: jobs booked on the date, split Booking / Quick Billing, and this month to the date.
- **Delivered**: jobs delivered on the date and this month, with jobs returned without repair on the date beside it.
- **On the bench**: open jobs on the bench or waiting for parts, of which indent raised, and of which the EDD has passed.
- **Ready for delivery**: jobs ready at AW330, with the number in transit back after repair.
- **Collection**: the S004 cash, card and UPI collected on the date, and whether the manual Service entry for that date was made ("entered" or "not entered").
- **Open over 15 days**: open jobs booked more than 15 days before the as-at date.
- **Claims raised this month**: claim documents raised with Titan this month to the date, with their value. Raised only (see Claims).

Select a card to open the list it counts: Booked and Delivered open the Jobs list for jobs booked or delivered on the date; On the bench opens the Pending board on the bench and indent groups; Ready for delivery opens that group; Collection opens the Money check on the date; Open over 15 days opens the board with the age choice "Over 15 days"; Claims opens Claims for the month. **Export** writes one sheet with each card's name, value and detail.

### Pending

Every open job, grouped by stage in the order above, longest in its stage first within each group. Five numbers at the top count the whole board, whatever the filters: open jobs, overdue, over 30 days since booking, in transit back, and parts awaited (indent raised).

Columns: job number, stage, booked on, days since booking, days in stage, EDD, overdue by (days), age band, brand, model, product, guarantee, customer type, Booking or Quick Billing, pending at (where the watch is), spare required, and the date of the last export that listed the job. The board shows no customer name.

Filters: tick one or more stages (none ticked = all), age since booking (a band, or "Over 15 days"), overdue only, brand, guarantee and Booking/Quick Billing. A DC or RA job whose claim has been raised is never overdue. The exports carry no "reason for pending" (the column is empty in every file), so the board shows the stage, where the watch is and the part required instead. It never invents a reason.

Open a job from the board to see its history. **Export** writes one sheet per stage shown, with the grid's columns; tick one stage to export only that group.

### Jobs: job history and the jobs list

**Job history.** Type a job number (as printed on the job card, `JOAW330` followed by digits), or open it from any job grid. The header shows:

- the job number, booked on, and Booking or Quick Billing;
- brand, model and product, guarantee, customer type and customer name;
- the current stage with its date, where the watch is, and whether the DC/RA claim was raised;
- the TAT for a closed job, or "Open N days · M in this stage" (and "EDD passed") for an open one;
- the EDD, and the labour and spares billed.

Below, the timeline lists every export that held the job, newest export first:

- the export date and the list (for example "Pending repair" or "DELIVERED") with its report code;
- the event date from that list (indent date, SRN date, repair date, delivery date, claim date and so on) and the status as exported;
- where the watch was, and the document number and amount where the list has one;
- whether the export was the weekly workbook or a raw file.

A job that drops off a list in a later export is simply not on that later export. That is history, not a problem, and nothing needs approving. If no export holds the number, the screen says "No Service family holds job <number>. Check the number."

**Export** writes the timeline. The header values go in the export's message line, and the period runs from the first to the last export shown.

**Jobs list.** One row per job: job number, stage, Booking or Quick Billing, booked on, stage date, TAT (days, closed jobs), days open (open jobs), EDD, brand, model, product, guarantee, customer name, spare value, labour, as at. "Show" chooses the rows:

- **Closed in the last 30 days** (the default): delivered, returned without repair or closed by claim within 30 days of the as-at date, Quick Billing included.
- **All jobs**: every job ETP has seen.
- **Open jobs**, or **one stage**.

The line under the grid gives the TAT of the delivered jobs shown, Booking and Quick Billing apart. For example (made-up figures): "TAT booking to delivered: Booking median 9 days (120 delivered, 30 over 15 days) · Quick Billing median 0 days (400 delivered)."

The "Delivery report" and "Repair report" exports are lists of jobs by their booking date. ETP does not use them for delivery or repair dates, and does not offer them as choices.

**Export** writes the rows shown, with the TAT line. The period is the 30 days for the default choice, otherwise the shown jobs' first booking date to the as-at date.

### Claims

What was claimed from Titan, by month: GPRC cell, Module Bank, WDC (depreciation) and WRA (replacement).

**Raised only.** No Service export says whether Titan has settled or paid a claim, so this screen never shows a claim as outstanding or settled. The note under the title says so.

Where the old-format and new-format reports (or GPRC and GPRC CLAIM) both hold the same claim document, it is counted once, from the newer format. The old WDC and WRA reports end, and the new ones start, on 1 Jul 2026; a 1 Jul claim is counted once. A few old WRA lines repeat a WDC document number; they are counted once, as WDC.

The four numbers:

- claims raised this month (documents and value);
- DC/RA jobs not yet claimed, with how many WDC and WRA claims are due;
- the oldest not yet claimed (days, job and stage);
- the GPRC claim export date against the DC/RA lists' date.

**GPRC gap warning.** When the newest GPRC export is older than the newest DC/RA list, a bold line names both dates and asks for a GPRC CLAIM export up to the DC/RA date. Until then, the GPRC claims after the last GPRC export are missing from the screen (on the 9 Oct data, 6 Aug to 29 Sep 2026).

"Show" has three views, each filtered by claim type and a From/To month range:

- **By month and claim type**: documents, lines, jobs, net amount incl. tax, UCP value; newest month first. Select a month row to see its claim lines.
- **Claim lines by document**: date, document number, job number, item, quantity, net incl. tax, source report, claim type.
- **Not yet claimed**: DC and RA jobs with no WDC/WRA claim document yet. Columns: job number, stage, issued on, days since issued, claim type due, DC/RA number, brand, model. Longest waiting first. This list ignores the month range, and it is the number to act on.

Open a job from a claim line or from the not-yet-claimed list to see its history. A few old WRA lines carry no job number; the screen says so. **Export** writes the view on screen ("Service claims by month", "Service claim lines" or "Service claims not yet raised") for the month range.

### Parts

Spare parts invoiced by Titan's warehouse against what was received.

- **Invoices.** Columns: invoice number and date, GRN number and date, items, quantity shipped and received, net amount, Open or Closed, days open, from location. Days open counts to the newest export for an open invoice, and to the GRN date for a received one. Open invoices come first, oldest first. Filters: open only, month, item. Select an invoice to see its lines.
- **Five numbers**: open invoices (count and value), the oldest open invoice (days), invoices received this month, jobs waiting for parts, and goods-in-transit lines in the last 30 days.
- **Jobs waiting for parts**: the open jobs with an indent raised. Columns: job number, part required, indent date, days waiting, brand, model, pending at. Longest wait first. Open a job from here to see its history. The exports have no link between a waiting job and a purchase invoice, so the two lists sit side by side and are not matched.
- **Goods in transit** (last 30 days) and the latest **closing stock** of spares (items, quantity, value).

**Export** writes the invoice grid; **Export jobs waiting to Excel** writes the waiting list.

### Money

The decision-16 money check; its rules have not changed. Choose a date range. Until you pick your own dates, it shows the 30 days up to the newest S004 export.

For each bill date it shows the S004 Cash, Card and UPI amounts beside the manual Service cash, card and UPI entries of the Titan World shop, where all Service money is entered, and the difference (S004 minus manual). Differences are shown, never corrected. Service WDC is not compared, and there are no job advances to deduct; an S004 advance, cheque or RTGS amount that is not zero is still shown, with no manual side.

A Service entry made at any other shop (for example Helios) is listed under "Service entries at other shops (not added)" with a note, and is never added in. Below, "Money changed since the previous refresh" lists each day whose total changed between two refreshes, with both amounts.

Before any Service export is imported (on a new PC, for example), the screen still shows the manual entries and says "No S004 reading is imported yet, so only the manual entries are shown." **Export** writes the comparison grid for the chosen range.

### Good to know

- Service files appear in Import → History under "All stores", named "Service Centre (AW330)".
- Service has no day locking, so nothing stops a later file from correcting earlier days.
- AW330 is listed in Settings → Stores & masters → Stores as a Service centre. It is not a shop store and cannot be switched on (error 51900) or moved to another business unit (error 51904), so it never appears in the shop store lists, the DSR, evening reports or daily packs.
- Importing the same week again changes nothing (Duplicate or Already present). A changed file for a date already imported is refused: use a new date (a new dated folder), or request a restatement as for any changed import (see Registers, investigation and approvals).
- Service files in the watch folder are imported but never start an automatic report pack.
- The manual Service cash/card/UPI entries, the Service Sales report, the DSR service card and the cash book stay exactly as they are.
- Before 1.10.0 the Service screens were under Reports → Service centre. Favourites to them keep working. Ctrl+K finds the tabs as "Service today", "Service pending board", "Service job history", "Service jobs", "Service claims", "Service parts and purchases" and "Service money check".
