# Current workflow guide — development candidate

This describes implemented actions, not production deployment acceptance. See the Phase 5 report for remaining work.

## Import and reports

Choose the header date/store before reviewing data. Import → History → Imports shows saved outcomes after restart; Received files is the separate retained-document inbox. Repeated imports appear as Duplicate without adding facts. Undetected periods use the attempt's recorded UTC date.

For failures in the current import session, correct the source and open Import → Problems → Retry failed. Only failed paths are retried; Ctrl+R remains available.

On supported reports, expand Filters, set query criteria and Apply. The applied scope describes both screen and Excel/PDF output. Clear restores the unfiltered scope. Detail-row search only filters the displayed list.

## Accounting — Owner decisions

Open Settings → Accounting → Prepare batch. Select the store/date and preview a final report generation. Owner-approved adjustments require an approved ADJUSTMENT ledger mapping; pending adjustments are excluded. Save a balanced preview for review.

Select a saved batch and enter a Batch decision reason. Approve selected stores the reason. Reject selected records a rejection reason, actor and time while preserving earlier approval evidence. Blank reasons and non-Owner decisions are refused. Exported and already-rejected batches cannot be rejected.

The unified accounting screen, revised statuses, invoice duplication guard, configurable company/environment and permanent export history are still pending. Do not treat an XML export as proof of import into Tally or production readiness.
