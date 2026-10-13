# Collections statement: expected vs evidence (1.12, first slice)

Status: checked against the D2 code at the 1.12.0 wave-1 integration (13 Oct 2026). **1.12.0 ships the collections engine and database only**; the screens named below (Settings > Collections setup, Import > Bank and settlement files, Reports > Collections: expected vs evidence, Reports > Deposits in transit) come with wave 2 (U4). Matching individual payments to bank lines is not in this slice.

## What it answers
For each store, date and way of payment: how much did the sales say we should receive, and what outside evidence (bank credit, provider settlement) is there?

| Payment | Expected | Evidence | Shown as |
|---|---|---|---|
| Cash | The deposit the daily sheet says was sent | Bank credit accepted by the Owner, or not yet | "Deposit in transit", with its age in days |
| UPI and card | Day's total for that mode | Provider settlement total for the business date | The difference, labelled "totals only, not matched yet" |
| Credit note, TC, Gift card | - | - | "No outside evidence expected" |
| Mode with no provider set up | - | - | "Provider not set up" |

A second list, **Deposits in transit**, shows every deposit sent and not yet credited, oldest first.

## Who sees what
- Owner and Store Manager see both lists. A Store Manager may import statement files.
- A Viewer sees the statement as totals only, without bank references, and not the Deposits in transit list. The Viewer cannot read the statement-line or deposit tables directly at all (decision 53); the report's views still work.
- Bank narrations, payer names, UPI IDs and the raw file rows are Owner only, because they can carry customer names.

## Setup (Owner, once)
Collections setup: bank accounts (last 4 digits only; a name may not hold a longer number), payment providers and merchant IDs, file layouts (a column map; use **Test on this file** before saving) and the evidence folder (default `C:\ProgramData\EtpReporting\CollectionsEvidence`, which setup prepares for active Owners and Store Managers). A new bank or provider needs only a new layout, not new software.

## Daily use
1. Import bank statement and provider settlement files. Importing the same file twice adds nothing; a longer export of the same statement replaces the shorter one; lines that look like ones already imported are held until you choose. ETP keeps a copy of each file; a long number in the file name (such as an account number) is stored with only its last four digits.
2. ETP proposes the bank line for each cash deposit (same amount, credited 0-3 days later, an account that takes cash deposits). **It only proposes; the Owner accepts.** Banking facts can be added after a day is finalised; an accepted credit can only be withdrawn, with a reason.
3. Open the statement and read the differences.

## Limits
A difference is a question, not an error. Nothing posts to Tally from here.
