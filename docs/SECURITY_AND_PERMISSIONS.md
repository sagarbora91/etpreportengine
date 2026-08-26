# Security and Permissions

Windows-integrated identity remains the authentication boundary.

| Role | Product authority |
|---|---|
| Owner | All Store Manager work plus users, settings, masters, mappings, approvals, accounting approval/export, reopen/restatement, backup and recovery. |
| Store Manager | Import, Source Inbox, document extraction/review, manual operations, report generation/finalisation, registers, draft accounting batches and sharing. |
| Viewer | Read-only reports, archive, registers, search and health. |

Database permissions reinforce UI checks. Store Managers cannot change users, settings, KPI definitions, contacts or accounting mappings; cannot decide approvals; cannot approve/edit accounting batches; cannot edit schema history; and cannot delete canonical/audit evidence. Approved/exported accounting lines and report packages are immutable. The last active Owner remains protected.

No SMTP/OCR/Tally secret is stored in application JSON, logs or SQL. External helpers run without a shell and with validated local paths. Support packages contain aggregate health and logs but no confidential rows.

