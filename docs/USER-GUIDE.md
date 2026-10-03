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

Help is searchable from Settings → Help and includes screenshots of the five navigation areas. It describes the implemented workflows and role limits.

## Service Centre (interim)

ETP imports the Service Centre (AW330) exports and shows them on four read-only screens. This is the interim Service import; the full Service import, with review of changes, comes with 1.10.0. Nothing in Retail changes.

### Weekly refresh (consolidated workbooks)

Copy the consolidated Service folder into a folder whose name ends with the date, for example `Service Centre till 05 oct 2026`, and import that folder (Import → choose the folder). ETP dates every file in it with the folder date. If the folder has no date, set the snapshot date on the Import screen instead.

A folder without a date is refused with "Put the Service files in a folder whose name ends with the date, e.g. 'Service Centre till 05 oct 2026', or set the snapshot date on the Import screen." The package folder `Service Centre (no importer profile)` has no date, so copy its files into a dated folder first.

Expect 35 files Imported. These are reported Not needed, which is correct:

- S001 Repair register: it repeats the ten status lists, so it would count every job twice.
- S005 Tender collection summary: not needed; S004 has the tender detail.
- S038 SRN report: a retired name for S011.
- S027 TAT and S028 Technician productivity: not imported yet (they come with 1.10.0).
- The `00_` consolidation control file.

When the folder date differs from the latest date on the "Snapshot History" sheet of S006, S009 or S010, the import shows a warning and still imports. Check the folder name.

### Daily raw exports

Drop the ETP Service export files as they come, CSV or XLSX, into the import (or the automatic-import watch folder). Their names look like `JOB REPORT 30.09.2026 TO 03.10.2026.csv` or `PENDING REPORT 03.10.2026.csv`. ETP takes the date from the file name: the end date of the window, or the single date. Keep the names as ETP exports them.

The raw files cover a few days each. The screens combine them with the weekly workbook: for each day, job and list, the latest file that covers it wins, so a raw file never hides older weeks and importing in a different order gives the same result.

Not imported yet: TAT (TATA REPORT), technician productivity, the tender collection summary (reported Not needed), and the new GPRC CLAIM layout (shown as an unknown layout).

### The four screens (Reports → Service centre)

Every screen shows "Service data as at <date> (refreshed <date>)", or "No Service data imported yet". There is no store picker: Service is always "Service Centre AW330". Viewers, Store Managers and the Owner can open them. No screen shows a customer phone number, e-mail or address.

- **Service jobs by status.** One row per job with its current status (one of the ten status lists), job date, EDD, brand, model, customer name, spare value, labour, line count and whether the job is also in other lists. Filter by status; Export.
- **Service pending lists.** Pending repair, Pending delivery and SRN status, oldest first, with the age in days. Export.
- **Service job history.** Type a job number. Shows each list the job was in, when it was first seen and when it left ("Left the list on or before <date>"). A job leaving a list is history, not a problem: nothing needs to be approved.
- **Service money check.** Choose a date range. Shows the S004 tender amount per day and tender beside the manual Service cash/card/UPI entries, the difference, and which shops' manual entries were added up ("Manual entries from: ..."). Below it, "Money changed since the previous refresh" lists each day whose total changed between two refreshes, with both amounts.

### Good to know

- Service files appear in Import → History under "All stores", named "Service Centre (AW330)".
- Service has no day locking, so nothing stops a later file from correcting earlier days.
- AW330 is listed in Settings → Stores & masters → Stores as a Service centre. It is not a shop store and cannot be switched on, so it never appears in the shop store lists, the DSR, evening reports or daily packs.
- Importing the same week again changes nothing (Duplicate or Already present). A changed file for a date already imported is refused: use a new date (a new dated folder), or request a restatement as for any changed import (see Registers, investigation and approvals).
- Service files in the watch folder are imported but never start an automatic report pack.
- The manual Service cash/card/UPI entries, the Service Sales report, the DSR service card and the cash book stay exactly as they are.
