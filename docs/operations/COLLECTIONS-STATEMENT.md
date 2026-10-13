# Collections statement: expected vs evidence (1.12, first slice)

Status: engine checked against the D2 code at the 1.12.0 wave-1 integration (13 Oct 2026). The four screens named below are the wave-2 U4 lane (checked against its code at the wave-2 integration): **Settings > Integrations > Collections setup**, **Import > Bank and settlement files**, **Reports > Collections: expected vs evidence** and **Reports > Deposits in transit**. Matching individual payments to bank lines is not in this slice.

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
Collections setup: bank accounts (last 4 digits only; a name may not hold a longer number), payment providers and merchant IDs, file layouts (a column map; use **Test on this file** before saving) and the evidence folder (default `C:\ProgramData\EtpReporting\CollectionsEvidence`, which setup prepares: Owners change it, a Store Manager can only add files and read back the ones they added, decision 59). A new bank or provider needs only a new layout, not new software. Only the Owner can open Collections setup. A Store Manager who tries is told it is for the Owner.

**Test on this file.** In a layout, pick a real statement file and press **Test on this file**: ETP reads only the header row and shows which columns it found and which are missing. Nothing is saved or imported by the test, and you never type column numbers.

## Daily use
1. Open Import > Bank and settlement files, choose the file(s). ETP shows which layout matched each file; when several accounts could match, you must pick the account. Each file gets its own line saying what happened. Importing the same file twice adds nothing; a longer export of the same statement replaces the shorter one; lines that look like ones already imported are held until you choose. ETP keeps a copy of each file; a long number in the file name (such as an account number) is stored with only its last four digits.
2. ETP proposes the bank line for each cash deposit (same amount, credited 0-3 days later, an account that takes cash deposits). **It only proposes; the Owner accepts.** Banking facts can be added after a day is finalised; an accepted credit can only be withdrawn, with a reason.
3. Open the statement (Reports > Collections: expected vs evidence), choose store, date range and way of payment, and read the differences. "Totals only, not matched yet" is written on every UPI and card difference. A Viewer sees totals without bank references.
4. Open Deposits in transit to see every cash deposit sent and not yet credited, oldest first, with its age as a coloured chip. The Owner sees ETP's proposed bank line and presses **Bank credited** to accept it with a reason (default "Seen on the bank statement"). A Store Manager sees the deposits without bank details; a Viewer cannot open this screen. A missing figure shows as a dash; a way of payment without a provider shows "Provider not set up".
5. When a file holds lines that look like ones already imported, ETP holds them. Choose **Import as new lines** (they are different payments) or **Replace the earlier file** (the new file replaces the old lines); either needs a reason.

## Limits
A difference is a question, not an error. Nothing posts to Tally from here.
