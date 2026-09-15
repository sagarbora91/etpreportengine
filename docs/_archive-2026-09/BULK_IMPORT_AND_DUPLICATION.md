# Bulk Import and Duplication

Bulk import accepts a workbook, a recursively scanned folder or a safety-limited ZIP. Processing is sequential and transactional per workbook, reports progress, supports cancellation between rows/files, retries transient I/O failures and keeps per-file outcomes for retry.

## Three protection levels

1. Exact file: the existing unique SHA-256 identity returns `Exact duplicate` and creates no second import.
2. Same business identity and content: SQL procedures record `ALREADY_PRESENT`; the incoming source row remains evidence but no second canonical fact is created.
3. Same business identity with different content: SQL records `CONFLICT`, stores both content hashes and a safe difference, leaves the existing canonical fact unchanged and routes the item to controlled restatement review.

This allows overlapping periods. A July–August workbook can add August, skip identical July facts and expose changed July facts without rejecting the whole workbook or silently overwriting history. Conflict dates do not trigger automatic report-pack generation.

The completion summary exposes files processed, exact duplicates, rows processed, new rows, already-present rows, conflicts, failures and cancellations.

