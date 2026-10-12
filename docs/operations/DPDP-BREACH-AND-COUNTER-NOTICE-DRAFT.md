# Personal-data breach procedure and counter notice (DRAFTS for Sagar's CA)

**These are drafts, not legal advice.** Written 12 Oct 2026 from the security audit's DPDP checklist (AUDIT-2, items D1, D7, D8, D12). Take both to your CA or a lawyer before use. Check the current Digital Personal Data Protection Act 2023 and its Rules (the forms, timings and how the Data Protection Board is reached) at the time you need them; this page does not state them and they may have changed. Nothing here contains a customer's data. Keep it that way.

The shop is the data fiduciary. Microsoft (OneDrive) and anyone supporting the PCs are processors. ETP holds customer name, mobile, e-mail, GSTIN (business customers), purchases and service jobs, and staff names with sales and targets.

## Part 1. One-page breach procedure

**1. What counts as a breach (any of these, even if you are not sure):**
- A shop PC, Workpc, a USB stick or a backup file is lost or stolen.
- Someone not allowed gets into the shared Microsoft account, a Windows login, an ETP login or the SQL server.
- A daily pack, export or report that shows customer names or phones goes to the wrong person (wrong e-mail, wrong WhatsApp chat).
- A backup key file, password or recovery key is lost, seen by the wrong person, or sent in a message.
- A staff member copies customer data out for their own use.

**2. First hour (contain; do not delete anything):**
1. Tell Sagar at once. Write the time you noticed and who noticed. Sagar decides; nobody else decides alone.
2. Stop the leak: change the shared Microsoft password and sign out all devices; if a PC is lost, sign it out of OneDrive from the Microsoft account page and disable its Windows login; if a pack went to the wrong person, ask them in writing to delete it and not to forward it.
3. In ETP (run as administrator): Settings > Users, deactivate any account that should not have access, with a reason.
4. Do not wipe, reinstall or switch off the affected PC before Sagar has looked: the logs are evidence. Do not delete files or log entries.

**3. Rotate the secrets (Sagar does these):**
- OneDrive: new password, MFA on, sign out everywhere, then sign in again on the four PCs.
- Backup key: if the key file may have been seen, create a new key on Workpc (`Copy-EtpBackupToOneDrive.ps1 -NewKey`), keep the old key safe only as long as you need to open the old backups, then destroy it. Old `.bak.enc` files stay readable only with the old key; after the next nightly copy, delete the old encrypted files from OneDrive if the old key was exposed.
- SMTP passwords: change at the mail provider and re-enter in ETP.
- Windows passwords of the affected logins.

**4. Write it down the same day** (a dated page, no customer details in it): what happened, when, which PC or file, what kind of data (names, phones, e-mails, GSTIN, purchases), how many people roughly, what was done. Keep it.

**5. Decide about notifying (Sagar with the CA, within the day):**
- Is personal data involved at all? A lost PC with an encrypted disk (BitLocker on) and no sign anyone opened it may be treated differently from a sent file. Ask the CA.
- If yes: the Act requires the fiduciary to inform the Data Protection Board and each affected person. The CA tells you the form, the deadline and the channel in force now. Fill in here once known: Board channel ____________ ; deadline ____________ ; CA contact ____________.
- Affected customers are told in plain words: what happened, what data, what they should do (for example be careful of calls claiming to be from the shop), and who to contact. Draft by the CA; send from the shop's own phone or e-mail, not from ETP.

**6. After:** fix the cause (a setting, a rule, a habit), note the change in the handoff file, tell staff what changed, and review this page.

**Contacts to fill in:** Sagar ____________ ; CA ____________ ; grievance contact named on the counter notice ____________ .

## Part 2. Counter notice text (draft)

For the counter, the job card and the bill footer (audit item D1). Short version for the bill footer and the job card, long version for a sign at the counter. Change the words in square brackets. Have the CA read it and say whether it must also be in another language. Print an English and a local-language version if customers need it.

**Counter sign (long version)**

> **How we use your details**
>
> When you buy from or service with [shop name: Titan World / Helios / Service Centre, Saagar Traders], we note your name, mobile number, e-mail address (if you give one), GST number (for a business bill), what you bought and paid, and, for a repair, the product and the fault.
>
> We use them to make your bill, to repair and follow up your product, to honour your warranty, to meet tax and accounting rules, and to understand how our business is doing. We do not use them to send promotions unless you have said yes separately.
>
> We keep them as long as the law or your warranty needs, and then remove or hide them. [CA to confirm: period.]
>
> Your details are kept on our computers and in our cloud storage (Microsoft OneDrive), which may be stored outside India. Only our staff who need them can see them.
>
> You can ask to see, correct or erase your details, or complain about how we handled them. Ask at the counter or contact [grievance contact: name, phone, e-mail]. If something goes wrong with your data, we will tell you and the authorities as the law requires.

**Bill footer / job card (short version)**

> Your name, phone and purchase details are used for billing, warranty, service follow-up and accounts. To see, correct or erase them: [phone / e-mail]. Details: notice at the counter.

**Staff notice (audit D12, for the notice board or the joining letter)**

> The shop's computer system records each person's sales, cash handling and targets so that the shop can run its reports and pay correctly. Targets are visible to the Owner only. Customer details in the system are for work only: do not copy them, photograph them or share them outside the shop's systems.

**To decide with the CA:** retention periods (sales detail is often kept as long as GST records are required; warranty period for service jobs), whether the notice needs a separate consent line for any marketing, how a customer's request for access or erasure is handled by hand (the Owner finds the customer by mobile or ID in the database; there is no ETP tool for it yet), and whether a business Microsoft account should replace the shared consumer one.
