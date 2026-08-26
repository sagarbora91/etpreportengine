# ETP Reporting Engine — User Manual

## Start and connect

Open **ETP Reporting Engine** from the Start Menu or desktop shortcut. The dashboard shows whether SQL Server and the reporting database are ready. If the database is unavailable, use **Test Connection** and share the displayed non-confidential error with your administrator.

The application uses your Windows sign-in. The header shows your display name and role. An Owner can add a Windows identity as Owner, Store Manager or Viewer; accounts not in the register cannot open business data.

## Import reports

Import is authorized for the Owner/Admin and Store Manager. The Owner/Admin always retains full rights. Mapping, sign, tolerance, control-rule, SQL connection, and database-configuration changes require Owner/Admin approval.

1. Open **Import** and select individual Excel workbooks, a folder, or an approved ZIP package.
2. Review the discovered files before starting. Unsupported files are reported and are not imported.
3. Start the batch. Progress shows the active file and completed/failed counts. You can cancel between files; the currently committed file remains safely imported.
4. Review the summary. Duplicate files are skipped. Correct failed files and use retry; do not rename a file merely to bypass duplicate protection.

Do not place ETP source files inside the program folder or source-code repository.

## Complete one business date

1. Open **Home**, select the ETP business date and store in the business-day cockpit, and refresh.
2. Import R025, R022, R013, R003, Variant Stock ledger and Closing Stock for that same ETP report date. A store/date mismatch is blocked, including during batch import.
3. Enter only operational values that ETP does not supply. Required daily values are walk-ins, opening cash, cash deposit and expenses. Service cash/card/UPI, cash adjustment and counted closing cash drive the separate service and cash reports. Enter `0` when the confirmed value is zero; leave a value empty only when it is genuinely missing.
4. Enter inventory-group physical details (display, backstock, defective, Y-location, independent counted physical and remarks) and dated CRO targets when applicable. The component total and its variance from the independent count remain separate from the physical-versus-system variance.
5. Generate either the selected-store pack or the one-click Titan + Helios pack. Review invoice/DSR, source lineage, tender, service, cash, system/physical stock, staff, manual-input and exception sections. Failed controls remain visible and cannot be turned into passes by changing a tolerance on this screen.
6. Choose **Finalise day** only after every blocking pack section is resolved. The database then protects the business date, facts, source lineage, manual inputs and final report generation.
7. Reopening requires a Windows administrator and a reason. To replace a corrected ETP export after reopening, enable **Controlled restatement**, enter the reason and import the corrected file. Ordinary duplicate protection remains active.

FTD, MTD, YTD and equivalent LY periods come from the selected ETP business date—not from the time the workbook was imported. Growth is blank with a visible state when LY is zero or missing. DSR and staff UPT/ATV retain separate, labelled transaction-denominator policies.

## Dashboard and reports

The dashboard summarizes imports, sales, returns, stock controls, tender differences, and database warnings. Warnings require review but do not change source values.

Open **Reports** to see the category catalogue for Sales, Stock, Staff/CRO, Tender/Cash, Service, Exceptions and Management. Choose the period and optional filters, then click the named report. Every report uses the same searchable, sortable preview and the same **Export selected report to Excel/PDF** actions.

Use the report filters for date, store, brand segment, transaction type, item, search text, and variance-only views where available. `INV` is an invoice. `SR` is a sales return whose source values are already negative. `NETVALUE` is the GST-inclusive primary sales value. `CLUSTER` is displayed as Brand Segment.

The report screen also provides customer-safe invoice summaries, DSR, closing/physical/movement/brand/slow stock, staff/CRO performance, service sales, cash reconciliation, focused exceptions and management trends. Stock reports use the selected end date as the snapshot date. Focused exception reports require one store and one business date. Customer names and contact details are not stored or exported. Staff reconciliation shows the exact attributed-versus-canonical difference; it does not round away a small variance.

Select a summary row to open its supporting detail. Sorting and search affect the visible view; they do not modify the database.

## Source Inbox, registers, accounting and archive

- **Import > Source Inbox** shows retained ETP workbooks, PDFs and images by received/validated/imported/review/conflict lifecycle. Native PDF text is attempted before optional OCR; extracted financial fields always require verification.
- **Registers** creates audited Inward and other register entries linked to retained documents. A locked date must be reopened before correction.
- **Accounting** prepares balanced entries only from a final immutable report and Owner-approved ledger mappings. Store Managers may save review batches; only Owners approve and export Tally XML.
- **Control Centre** shows sales/control trends, conflicts, pending approvals, global investigation, active data-quality findings, automatic-import settings, morning/evening report schedules and recent unattended outcomes.
- Owners can change the four controlled local folders and schedules with a reason. Owners and Store Managers can run automation immediately.
- Place stable approved `.xlsx` files or ETP `.zip` packages in Inbound. Completed sources move to Processed; invalid sources move to Failed. Duplicate content is skipped and recorded.
- **Archive** stores every generated store and combined management pack with a document SHA-256. Open a generation to verify/re-export it, compare two generations, create a hashed ZIP manifest, or initiate an email/WhatsApp share.
- Owners can run a checksum backup, isolated recovery drill or privacy-safe support-package creation from Operations Center.

## Export and print

Choose Excel or PDF from a supported report, or export the complete pack. The complete Excel file has separate report sheets; the PDF has paginated report sections. Both retain title, business date, rule version, warning state and controls and contain no spreadsheet formulas. Verify filters and control totals before sharing. Save only to an approved access-controlled folder and close exported workbooks when finished.

## Common messages

- **Database unavailable:** confirm SQL Server is running, then test the connection or contact the administrator.
- **Duplicate:** the same content was already imported; no action is normally required.
- **Unsupported report:** the file is not one of the approved ETP profiles.
- **Import failed:** read the safe failure summary, correct the source/export issue, and retry.
- **Tender variance:** invoice and authoritative tender controls differ. Investigate; do not manually force the difference to zero.
- **Backup overdue / disk growth / failed imports:** notify the administrator.

## Keyboard and accessibility

- Use `Tab` and `Shift+Tab` to move between controls, arrow keys within tabs/tables, `Space` or `Enter` to activate, and `Esc` to close dialogs.
- Every action should expose an accessible name and visible keyboard focus. Report a missing label, trapped focus, clipped text at 200% scaling, or information conveyed by color alone.

## Getting support

Use the offline support-package action and send only the generated ZIP to the administrator. Never send original ETP workbooks, customer details, database backups, passwords, or screenshots containing confidential rows unless an authorized support process explicitly requires them.
