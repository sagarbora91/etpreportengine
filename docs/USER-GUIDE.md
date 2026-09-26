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
