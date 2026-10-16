# Collections statement: expected vs evidence (1.12; completed in 1.12.1)

Status: engine checked against the D2 code at the 1.12.0 wave-1 integration (13 Oct 2026). The four screens named below are the wave-2 U4 lane (checked against its code at the wave-2 integration): **Settings > Integrations > Collections setup**, **Import > Bank and settlement files**, **Reports > Collections: expected vs evidence** and **Reports > Deposits in transit**. Matching individual payments to bank lines is not in this slice: ETP never matches payments by itself, it only says what is late and proposes one cash bank line for you to accept.

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
- **Bank reference on screen (decision 70).** A Store Manager still sees the bank reference on the statement screen. It is the Excel export that leaves it out (see Export to Excel below).
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

## Settlement rules (Owner, from 1.12.1)
Settings > Integrations > Collections setup now has a seventh section, **Settlement rules**, between File layouts and Statement folder. A rule says when money should normally arrive, so ETP can tell you when something is late.

- A rule has: the way of payment, the shop (blank = all shops), the provider (blank = the whole way of payment), **settles within** (days), **overdue after** (days), a cash tolerance, a **From** date and an optional **To** date, and a reason. Each rule shows a chip: In force, Starts later or Ended.
- A shop's own rule beats an all-shops rule, and a provider's rule beats a whole-mode rule. Two rules of the same kind may not cover the same day: ETP says "Another settlement rule for the same mode, store and provider covers some of these dates. Give the other rule an end date first."
- To change a rule from a certain day, give the old rule an end date and add a new one starting after it. Rules cannot be deleted.
- ETP refuses a rule with no window, no overdue period and no tolerance; an overdue period shorter than the window; a provider on a Cash rule; and a cash tolerance on a rule that is not Cash. A provider that belongs to another way of payment is refused too.
- **What it changes today.** A Cash rule with "overdue after" makes an old deposit show **Overdue, N days** in red on Deposits in transit, for the Owner and for a Store Manager. With no rule nothing is called overdue, and the old age chip (amber after 3 days, red after 7) stays. "Settles within" and the cash tolerance are saved and checked but nothing uses them yet. Rules never match payments.

## Export to Excel (from 1.12.1)
Reports > Collections: expected vs evidence has **Export to Excel**. It writes the lines on screen (the way-of-payment filter applies) as `Collections_expected_vs_evidence_<from>_<to>.xlsx`. Columns: Store, Business date, Mode, Provider, Expected, Evidence, Difference, Cash tenders (rupees, as numbers), Status, Detail and Age (days). The top of the file says "Totals only, not matched yet", the scope, and what the file leaves out for your role.

| Role | Bank columns (Credited on, Bank reference) |
|---|---|
| Owner | Yes |
| Store Manager | No (decision 70: the screen still shows the reference, the file does not) |
| Viewer | No (decision 67: a Viewer never gets more than a Store Manager) |

## Record by hand and Withdraw a bank credit (Owner, from 1.12.1)
On Reports > Deposits in transit the Owner can mark a deposit as credited without a bank line in ETP.

1. Select the deposit and press **Record by hand**. Enter the credited date, the bank account (name only, digits are masked), the amount, the bank reference and a reason. Every field is checked in plain words. A date that has not come yet is refused ("The bank cannot have credited it on a day that has not come yet. Check the credited date.").
2. A different amount is allowed. The statement then says "Credited, different amount" and the notice is amber.
3. The deposit leaves the list and appears in the **Marked as credited** card under it (credits for the last 62 days' deposits, or marked in that time), with how it was done (From the bank line or Recorded by hand), Same / Different amount / Not on the daily sheet, and the reason.
4. To undo a mistake press **Withdraw** on that row, confirm, and give a reason. The deposit goes back to in transit. Nothing is deleted; both steps are kept in the history and the audit trail.

A Store Manager and a Viewer do not see the Marked as credited card or these buttons.

## Limits
A difference is a question, not an error. Nothing posts to Tally from here.
