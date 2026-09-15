# Security and Permissions

Windows-integrated identity remains the authentication boundary.

| Role | Product authority |
|---|---|
| Owner | All Store Manager work plus users, settings, masters, mappings, approvals, accounting approval/export, reopen/restatement, backup and recovery. |
| Store Manager | Import, Source Inbox, document extraction/review, manual operations, report generation/finalisation, registers, draft accounting batches and sharing. |
| Viewer | Read-only reports, archive, registers, search and health. |

Database permissions reinforce UI checks. Store Managers cannot change users, settings, KPI definitions, contacts or accounting mappings; cannot decide approvals; cannot approve/edit accounting batches; cannot edit schema history; and cannot delete canonical/audit evidence. Approved/exported accounting lines and report packages are immutable. The last active Owner remains protected.

No SMTP/OCR/Tally secret is stored in application JSON, logs or SQL. External helpers run without a shell and with validated local paths. Support packages contain aggregate health and logs but no confidential rows.

## Deferred device licensing

Windows-integrated application roles and device activation are separate controls. The accepted, deferred licensing design requires Microsoft-authenticated owner authorization for issuance plus an ECDSA-signed licence bound to DPAPI-protected installation state. Normal daily operation remains offline and does not require Microsoft sign-in.

Runtime licensing is not yet implemented. Its engineering authority, Microsoft registration procedure and security test plan are in:

- `docs/security/ETP_LICENSING_ENGINEERING_SPEC.md`
- `docs/security/ETP_MICROSOFT_APP_REGISTRATION.md`
- `docs/security/ETP_LICENSING_TEST_MATRIX.md`
- `docs/security/ETP_LICENSING_IMPLEMENTATION_BACKLOG.md`

Production private keys, Microsoft credentials/tokens, activation blobs and issued licences must never be stored in the repository or Obsidian vault.
