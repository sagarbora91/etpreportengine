# Tally test setup (Sagar and the accountant)

Decisions 48, 49 and 53: all Tally work in 1.12 uses a **TEST company** in a **test copy of Tally inside the VM**. ETP never talks to the real Tally. Status: engine checked against the D1 code at the 1.12.0 wave-1 integration (13 Oct 2026). The screens and buttons named below are the wave-2 U3 lane, checked against its code at the wave-2 integration. All Tally screens are Owner-only (approve, write, send and profile edits).

## Part 1: install Tally in the VM (Sagar)
1. Start the ETP test VM. Do not do this on Workpc or any shop PC.
2. Install **TallyPrime Educational** (the free learning mode). It accepts only some dates (ETP assumes the 1st, 2nd and 31st of a month; check this at the VM visit); use those for test data.
3. Create a company called exactly **Saagar Traders Titan**. ETP talks to Tally over HTTP only for this exact name in a TEST profile. (A file-only TEST profile may also use a name containing the word TEST.)
4. In Tally: F1 Help > Settings > Connectivity > Client/Server configuration. Set TallyPrime to act as **Server**, **Port 9000**. ETP refuses any other port. The port is for the VM itself only; do not open it in any firewall.
5. Keep the company open in Tally whenever ETP is reading back or sending.

## Part 2: set up in ETP (inside the VM)
1. Settings > Tally companies: add a TEST profile with the company name, the address `http://127.0.0.1:9000/` and a build label containing **EDUCATIONAL** (then ETP keeps voucher dates to the allowed days).
2. To let ETP send vouchers (not only read back), the profile also needs delivery **HTTP** and the **test PC name**: the VM's computer name exactly as `$env:COMPUTERNAME` shows it inside the VM (column `tally_profiles.test_machine_name`, migration 0066; the Settings field is **Test PC name**, and the screen shows "This PC is <name>" beside it so you can compare). ETP sends only on that PC, and only after Tally itself reports Educational mode.
3. **Write Tally masters**: ETP writes `tally-masters-<code>.xml` (ledger groups, one cost centre per store, sales, GST per rate, round-off, a tender ledger per payment mode and store, bank placeholders, the party ledger) and `tally-masters-<code>-review.txt`, the list for the accountant. Neither file is ever overwritten.
4. In Tally: Import > Masters, and choose that file.
5. Open the ledger list and compare it with the review list. Approve the proposed ledger names in ETP's mappings; fix names in ETP, not by hand in Tally.

## What the screen shows
- The profile card has the company name, the address (port 9000 is fixed and shown, not editable), delivery (file or HTTP), the build label with an "EDUCATIONAL" hint, and the Test PC name. A chip says **TEST** or **LIVE** in words.
- Accounting: plain buttons, the next one highlighted: **Prepare**, **Save**, **Approve**, **Reject**, **Write file** (no step numbers).
- Settings > Tally vouchers has a **Send to Tally** card for the chosen company: **Write masters** (asks for a folder, then shows the mappings the accountant must approve and the path of the review file), **Write file**, **Read back**, **Read back from file** (a Day Book exported by hand), **Send**, **Compare** and **Open evidence folder**. The next step of the selected batch is the highlighted button; a button that cannot be used says why in its tooltip.
- **Send to the test Tally is shown only on the PC named in the profile** (decision 57). On any other PC, including Workpc, there is no **Send** button; a sentence says "Sending is set up for <test PC name> only. On this PC, write the file and import it in Tally by hand, then read back." (with no test PC named: "Sending to Tally is not set up for any PC..."). That is on purpose.
- A batch moves through PLANNED, APPROVED_READY, EXPORTED_AWAITING_IMPORT and RECONCILED; the buttons that make no sense for the state are not offered.

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
