# Collections statement: expected vs evidence (1.12, first slice)

Status: from the wave plan; **CHECK AT INTEGRATION** against the D2 note for screen names. Matching individual payments to bank lines is not in this slice.

## What it answers
For each store, date and way of payment: how much did the sales say we should receive, and what outside evidence (bank credit, provider settlement) is there?

| Payment | Expected | Evidence | Shown as |
|---|---|---|---|
| Cash | The deposit the daily sheet says was sent | Bank credit found, or not yet | "Deposit in transit", with its age in days |
| UPI and card | Day's total for that mode | Provider settlement total for the business date | The difference, labelled "totals only, not matched yet" |
| Credit note, TC, Gift card | - | - | "No outside evidence expected" |
| Mode with no provider set up | - | - | "Provider not set up" |

A second list, **Deposits in transit**, shows every deposit sent and not yet credited, oldest first.

## Who sees what
Owner and Store Manager see both lists. A Viewer sees totals only. Bank narrations, payer names and UPI IDs are hidden from Store Manager and Viewer, because they can carry customer names.

## Setup (Owner, once)
Settings > Collections setup: bank accounts (last 4 digits only), payment providers and merchant IDs, file layouts (a column map; use **Test on this file** before saving) and the evidence folder. A new bank or provider needs only a new layout, not new software.

## Daily use
1. Import bank statement and provider settlement files (Import > Bank and settlement files). Importing the same file twice adds nothing.
2. In Today, tick "Bank credited". ETP proposes a match (same amount, credited within 0-3 days, the configured account). **It only proposes; you accept.** Banking facts can be added after a day is locked.
3. Open the statement and read the differences.

## Limits
A difference is a question, not an error. Nothing posts to Tally from here.
