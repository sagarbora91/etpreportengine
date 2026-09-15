# Register Architecture

`register_entries` supplies one governed structure for Inward, Outward, Credit Note, Service Receipt, Courier, Stock Transfer, Expense and Vendor Invoice registers. The first operational target is the Inward Register.

Every entry records store, business date, document identity/date, counterparty, quantity/value, reference, receiver, verification status, remarks, actor, timestamp and reason. A source-document link is optional during draft entry and permanent once attached. Insert/update history is append-only. A finalised business date rejects register changes until an Owner reopens it through the existing audited workflow.

The source document remains in `source_documents`; register rows store only its identifier. This avoids large binaries in transactional tables while preserving integrity, search and retention evidence.

