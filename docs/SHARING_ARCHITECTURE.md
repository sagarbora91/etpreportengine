# Sharing Architecture

Sharing always begins with one selected immutable report generation.

- ZIP packaging uses `System.IO.Compression`, produces Excel and PDF under the business-date/generation/store layout, and includes a JSON manifest containing scope, generation, creator, UTC/local timestamps, final/draft status, file list and SHA-256 hashes.
- WhatsApp uses the official `wa.me` deep link. The application copies the ZIP path, highlights the local file and opens WhatsApp. It records `Share initiated`; it never claims attachment or delivery.
- Email creates a standards-based `.eml` draft with the ZIP attached and opens the default mail client. The user reviews recipients and sends. Recipient-safe metadata is audited, not message content.
- Attachment size is checked against the controlled product setting. Direct SMTP remains optional; no password is stored in SQL or settings files.
- The Archive includes an Owner-managed, audited address book. Selecting a contact fills the safe WhatsApp/email handoff fields; Store Managers cannot administer contacts.

Report packages and share-attempt history are append-only. Restatement creates a new generation/package; old shared artefacts remain traceable.
