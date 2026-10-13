# Tally test setup (Sagar and the accountant)

Decisions 48, 49 and 53: all Tally work in 1.12 uses a **TEST company** in a **test copy of Tally inside the VM**. ETP never talks to the real Tally. Status: checked against the D1 code at the 1.12.0 wave-1 integration (13 Oct 2026). **1.12.0 ships the Tally engine only**; the buttons named below ("Write Tally masters", "Write Tally file" for vouchers, "Read back from Tally", "Send to the test Tally") come with the wave-2 screens (U3). The manual read-back and Compare already exist.

## Part 1: install Tally in the VM (Sagar)
1. Start the ETP test VM. Do not do this on Workpc or any shop PC.
2. Install **TallyPrime Educational** (the free learning mode). It accepts only some dates (ETP assumes the 1st, 2nd and 31st of a month; check this at the VM visit); use those for test data.
3. Create a company called exactly **Saagar Traders Titan**. ETP talks to Tally over HTTP only for this exact name in a TEST profile. (A file-only TEST profile may also use a name containing the word TEST.)
4. In Tally: F1 Help > Settings > Connectivity > Client/Server configuration. Set TallyPrime to act as **Server**, **Port 9000**. ETP refuses any other port. The port is for the VM itself only; do not open it in any firewall.
5. Keep the company open in Tally whenever ETP is reading back or sending.

## Part 2: set up in ETP (inside the VM)
1. Settings > Tally companies: add a TEST profile with the company name, the address `http://127.0.0.1:9000/` and a build label containing **EDUCATIONAL** (then ETP keeps voucher dates to the allowed days).
2. To let ETP send vouchers (not only read back), the profile also needs delivery **HTTP** and the **test PC name**: the VM's computer name exactly as `$env:COMPUTERNAME` shows it inside the VM (column `tally_profiles.test_machine_name`, migration 0066; the Settings field comes with wave 2). ETP sends only on that PC, and only after Tally itself reports Educational mode.
3. **Write Tally masters**: ETP writes `tally-masters-<code>.xml` (ledger groups, one cost centre per store, sales, GST per rate, round-off, a tender ledger per payment mode and store, bank placeholders, the party ledger) and `tally-masters-<code>-review.txt`, the list for the accountant. Neither file is ever overwritten.
4. In Tally: Import > Masters, and choose that file.
5. Open the ledger list and compare it with the review list. Approve the proposed ledger names in ETP's mappings; fix names in ETP, not by hand in Tally.

## Review list for the accountant
Please check, in the VM copy:
- Sales ledger names and their parent group.
- Party ledger rules (cash, UPI, card, Service customers) and whether sundry debtors are needed.
- GST ledgers (CGST, SGST, IGST) and rounding.
- The voucher type used (the built-in Sales type, manual numbering).
- The narration format (ETP puts a reference key in it).
- Anything Tally rejects on import, with Tally's message.

Record decisions in the decision log. Do not edit masters by hand without telling Sagar.

## Part 3: vouchers
1. In ETP, prepare a batch on educational-mode dates and approve it (Owner). Bills cancelled the same day are left out (decision 41) and do not block the day.
2. **Write Tally file**: one Sales voucher per invoice, GST-inclusive, checked by reading the file back. Then either import it in Tally (Import > Vouchers), or **Send to the test Tally**: ETP first reads back and refuses if Tally already holds any of the vouchers, then sends, and keeps Tally's answer as evidence.
3. In ETP, **Read back from Tally** (port 9000), or export a Day Book from Tally and use **Load a file exported from Tally**.
4. **Compare**. RECONCILED means ETP and Tally agree. Anything else lists each difference. Never import the same file twice; see "When Tally and ETP disagree" in `docs\OPERATIONS.md`.

## Plain errors
- "Tally is not answering on this PC": Tally is not running, or it is not a server on port 9000.
- "Tally is running but the company X is not open": open the company in Tally.
- "Sending to Tally is allowed only on the test PC named for this company": you are not in the VM, or the profile's test PC name differs from the VM's computer name.
- "Tally does not report Educational mode": this is not the Educational test Tally; nothing was sent.
