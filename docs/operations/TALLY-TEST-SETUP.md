# Tally test setup (Sagar and the accountant)

Decisions 48 and 49: all Tally work in 1.12 uses a **TEST company** in a **test copy of Tally inside the VM**. ETP never talks to the real Tally. Status: **CHECK AT INTEGRATION** against the D1 note for file names and menu wording.

## Part 1: install Tally in the VM (Sagar)
1. Start the ETP test VM. Do not do this on Workpc or any shop PC.
2. Install **TallyPrime Educational** (the free learning mode). It accepts only some dates (the 1st, 2nd and last day of a month); use those for test data.
3. Create a company called exactly **Saagar Traders Titan**. The ETP profile name must contain the word TEST; ETP refuses to write to a TEST profile without it.
4. In Tally: F1 Help > Settings > Connectivity > Client/Server configuration. Set TallyPrime to act as **Server**, **Port 9000**. The port is for the VM itself only; do not open it in any firewall.
5. Keep the company open in Tally whenever ETP is reading back.

## Part 2: set up in ETP
1. Settings > Tally companies: add a profile with the company name, port 9000 and a build label (Tally's release number).
2. Write the **masters file** (the ledgers and groups ETP needs) and save it.
3. In Tally: Import > Masters, and choose that file.
4. Open the ledger list and compare it with the accountant's review list below. Fix names in ETP, not by hand in Tally.

## Review list for the accountant
Please check, in the VM copy:
- Sales ledger names and their parent group.
- Party ledger rules (cash, UPI, card, Service customers) and whether sundry debtors are needed.
- GST ledgers (CGST, SGST, IGST) and rounding.
- The voucher type name used (Sales).
- The narration format (ETP puts a reference key in it).
- Anything Tally rejects on import, with Tally's message.

Record decisions in the decision log. Do not edit masters by hand without telling Sagar.

## Part 3: vouchers
1. In ETP, prepare a small batch on educational-mode dates and approve it (Owner).
2. **Write Tally file**, then in Tally: Import > Vouchers.
3. In ETP, **Read back from Tally** (port 9000), or export a Day Book from Tally and use **Load a file exported from Tally**.
4. **Compare**. RECONCILED means ETP and Tally agree. Anything else lists each difference. Never write the same file twice; see "When Tally and ETP disagree" in `docs\OPERATIONS.md`.

ETP does not post vouchers into Tally itself in 1.12. You always import the file.

## Plain errors
- "Tally is not answering on this PC": Tally is not running, or port 9000 is not set.
- "Tally is running but the company X is not open": open the company in Tally.
