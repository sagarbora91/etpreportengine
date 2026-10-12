# Shop evening routine (staff guide)

For the three shop PCs under the two-PC plan (decisions 30 to 34). Written 11 Oct 2026 for ETP 1.10.1.

**The idea.** The shop PCs do not run ETP's database. Each evening a shop PC does two things: it saves the day's reports (exports) from the billing system, and it fills in one small Excel sheet with the day's walk-ins, cash and service money. A small program then copies these to OneDrive. The office PC (Workpc) picks them up the next morning. You do not need to open ETP on the shop PC for this.

**Three PCs, three sheets, three file names.** The word after the store code tells them apart. Service money is counted under Titan World, so the Service sheet carries the code WLMHW too. Check the last word before you save:

| PC | Sheet file name (change the date) | Ends with |
|---|---|---|
| Titan cashier | `ETP-Daily-Inputs-WLMHW-RETAIL-<date>.xlsx` | RETAIL (Titan) |
| Helios | `ETP-Daily-Inputs-HEMW-RETAIL-<date>.xlsx` | RETAIL (Helios) |
| Service Centre | `ETP-Daily-Inputs-WLMHW-SERVICE-<date>.xlsx` | **SERVICE** |

**Three PCs, three pages:**

| PC | Store | Page |
|---|---|---|
| Titan cashier PC | Titan World (WLMHW) | Page 1 |
| Helios PC | Helios (HEMW) | Page 2 |
| Service Centre PC | Service Centre (its money is counted under Titan World, WLMHW) | Page 3 |

All three PCs use the same shared Microsoft account in OneDrive. Do not sign out of OneDrive and do not change its password. The sheets hold figures only. They never hold customer names or phone numbers. Keep it that way: do not type any customer detail into the sheet, including the remark box.

**Rules that apply to all three PCs**

- Do this after the shop has closed, when the day's figures are final. The scheduled copy runs at about 9 pm (Sagar may set a different time). Finish before that time.
- Save the files in the two local folders: `C:\ETP-Exports` for the reports and `C:\ETP-DailyInputs` for the Excel sheet. Do not save them inside OneDrive yourself. The program copies them.
- Close Excel when you finish. A file that is still open is skipped.
- Leave the PC on and signed in until the copy time has passed.
- Never delete or rename the files after saving them.

---

## Page 1: Titan cashier PC (Titan World, WLMHW)

### A. Take these reports every evening

Take each one for the day that has just ended. Save them in `C:\ETP-Exports` as Excel (.xlsx) files. Keep the name the system gives. Do not rename them.

| Report | What it is |
|---|---|
| R025 SDB VariantwiseSales | Sales lines for the day |
| R022 Revenue Report | Invoices and how they were paid |
| R011 Closing Stock | Stock on the shelves. **Take it every day, after closing** (not before the last bill) |
| R030 Variant Stock Ledger | Stock in and out for the day |

If a report has no rows that day, still save it.

### B. Fill the daily sheet

1. **Get the blank sheet.** Open the file `ETP-Daily-Inputs-WLMHW-RETAIL` from `C:\ETP-DailyInputs`. If you do not have a blank one, the Owner or a Store Manager gets it from ETP: open **Import**, press **Get blank daily sheet**, pick the store and sheet (here: Titan World, retail), keep today's date, press **Choose where to save** and save it; the date is already filled in on the sheet. It is saved in the Exports folder by default, already named for the day. Copy it to this PC's `C:\ETP-DailyInputs` (for example on a USB stick or through OneDrive).
2. Type **today's date** in the Business date box (example 11-Oct-2026). Only dates from the last 31 days up to today are accepted.
3. Type your name in "Filled by". **This is required (from 1.11.2): ETP refuses a sheet whose "Filled by" is empty**, even when every box holds a number, because the zeros on a blank sheet prove nothing.
4. Fill the white boxes in the Value column:
   - Walk-ins: how many people came in (a whole number).
   - Opening cash: cash in the drawer at the start of the day.
   - Expenses: cash spent from the drawer.
   - Cash deposit: cash taken to the bank.
   - Cash adjustment: only if needed. This is the only box that may be a minus number.
   - Closing cash counted: the cash you counted at the end.
   - Remark: a short note, no customer details.
5. **One rule: type 0 when the figure is zero.** A quiet day with no walk-ins, no expenses or no deposit is a 0, not an empty box. An empty box means "I did not say": ETP keeps whatever was there before and, if nothing was ever entered, the Today checklist keeps showing it as not entered (for example "Walk-ins has not been entered"). Leave a box empty only for something you really do not know yet, and tell Sagar.
6. Grey boxes are locked on purpose. Do not try to change them.
7. Click **File, Save As** and save in `C:\ETP-DailyInputs` with exactly this name, using today's date:
   `ETP-Daily-Inputs-WLMHW-RETAIL-2026-10-11.xlsx`
   Be careful to change the date in the name each day. The date in the name must match the date in the sheet, or the sheet is refused. This stops yesterday's file being sent again by mistake.

### C. See that OneDrive has synced

Look at the OneDrive cloud icon by the clock: no red cross and no spinning arrows. Open the OneDrive folder, then `ETP-Inbound`, then `TITAN`, then today's date folder. Your files should each show a **green tick**. A blue circle with arrows means "still copying". Wait.

### D. If something goes wrong

- The sheet will not accept a number: check the box wants a whole number (walk-ins) or a plain amount (money). No letters.
- OneDrive shows a red cross, or no green tick after 15 minutes: check the internet, then call Sagar.
- You made a mistake after saving: fix it, save with the same name, close Excel, and tell Sagar so the file is sent again.
- The day was already closed in the office and your correction is refused: Sagar must reopen the day. Call him.
- For anything else, **call Sagar**. Tell him the PC name, the time, and what the screen said. Do not tell him customer details.

### Print and stick: Titan cashier PC checklist

1. Shop closed: take R025, R022, R011 (after closing) and R030 into `C:\ETP-Exports`.
2. Open the WLMHW-RETAIL sheet, type the date, fill every box (type 0 if the figure is zero).
3. Save As `ETP-Daily-Inputs-WLMHW-RETAIL-<today yyyy-mm-dd>.xlsx` in `C:\ETP-DailyInputs`.
4. Close Excel. Leave the PC on and signed in.
5. Check OneDrive: green ticks in `ETP-Inbound\TITAN\<today>`.
6. Problem? Call Sagar.

---

## Page 2: Helios PC (Helios, HEMW)

### A. Take these reports every evening

Save in `C:\ETP-Exports` as .xlsx, keeping the system's name.

| Report | What it is |
|---|---|
| R025 SDB VariantwiseSales | Sales lines for the day |
| R022 Revenue Report | Invoices and how they were paid |
| R011 Closing Stock | **Every day, after closing** |
| R030 Variant Stock Ledger | Stock in and out for the day |

### B. Fill the daily sheet

Same steps as Page 1, but open `ETP-Daily-Inputs-HEMW-RETAIL`. If you need a blank sheet, get it as in Page 1 step 1, choosing Helios, retail. The boxes are the same seven: walk-ins, opening cash, expenses, cash deposit, cash adjustment, closing cash counted, remark. The Helios sheet has no service-money boxes. Only the Service Centre PC enters those.

Save As in `C:\ETP-DailyInputs` with this name:
`ETP-Daily-Inputs-HEMW-RETAIL-2026-10-11.xlsx` (change to today's date).

### C. See that OneDrive has synced

Cloud icon by the clock: no red cross. In the OneDrive folder open `ETP-Inbound`, `HELIOS`, today's date. Every file needs a **green tick**.

### D. If something goes wrong

Same as Page 1. Mistake after saving: fix, save with the same name, tell Sagar. Anything else: **call Sagar**.

### Print and stick: Helios PC checklist

1. Shop closed: take R025, R022, R011 (after closing) and R030 into `C:\ETP-Exports`.
2. Open the HEMW-RETAIL sheet, type the date, fill every box (type 0 if the figure is zero).
3. Save As `ETP-Daily-Inputs-HEMW-RETAIL-<today yyyy-mm-dd>.xlsx` in `C:\ETP-DailyInputs`.
4. Close Excel. Leave the PC on and signed in.
5. Check OneDrive: green ticks in `ETP-Inbound\HELIOS\<today>`.
6. Problem? Call Sagar.

---

## Page 3: Service Centre PC

### A. Take these reports every evening

The Service Centre (code AW330) sends its own set of reports. Its money sheet is the SERVICE one (file name above), not the Titan RETAIL one. Save them in `C:\ETP-Exports` as .xlsx and keep the system's names. These are the reports ETP reads (from the first full pack, 3 Oct 2026):

- Job Report
- Revenue Report
- Tender Collection and Tender Collection Summary
- Closing Stock (every day, after closing)
- Purchase Register by invoice created date and by invoice received date
- Pending Report and Pending Delivery
- R R Indent Raised, R R RWR, R R Delivered
- Empowerment Report
- SRN Report, GIT Report (Goods in Transit), GPRC Claim

*Still to confirm before printing (open items for Sagar): the exact daily Service export list above, and the Titan menu names for R025, R022, R011 and R030 as staff see them on the screen. Also check the shop PCs' Windows regional setting is India, so Excel reads dates and numbers the Indian way.*

Do not combine or edit the reports. Save them exactly as the system gives them.

### B. Fill the daily sheet

Open `ETP-Daily-Inputs-WLMHW-SERVICE`. If you need a blank sheet, get it as in Page 1 step 1, choosing the Service Centre sheet. (The Service money is counted under Titan World, so the code in the name is WLMHW. This is correct.) Type the date (a sheet from ETP already has it; check it is the right day), then fill the seven boxes:

- Service cash, Service card, Service UPI, Service WDC: the day's service money, by how it was paid.
- WCC walk-ins: number of walk-ins to the Service Centre (a whole number).
- WCC sales: the WCC sales amount.
- WDC bills: the number of WDC bills (a whole number).

Same rule as Page 1: type 0 when the figure is zero (a day without WDC bills is 0). Leave a box empty only if you do not know the figure yet. Save As in `C:\ETP-DailyInputs`:
`ETP-Daily-Inputs-WLMHW-SERVICE-2026-10-11.xlsx` (change to today's date). Remember the word SERVICE, so it never gets mixed up with the Titan cashier's sheet.

### C. See that OneDrive has synced

Open the OneDrive folder, then `ETP-Inbound`, `SERVICE`, today's date. Every file needs a **green tick**.

### D. If something goes wrong

Same as Page 1. Anything else: **call Sagar**.

### Print and stick: Service Centre PC checklist

1. Service closed: take the day's Service reports (including Closing Stock after closing) into `C:\ETP-Exports`.
2. Open the WLMHW-SERVICE sheet, type the date, fill every box (type 0 if the figure is zero).
3. Save As `ETP-Daily-Inputs-WLMHW-SERVICE-<today yyyy-mm-dd>.xlsx` in `C:\ETP-DailyInputs`.
4. Close Excel. Leave the PC on and signed in.
5. Check OneDrive: green ticks in `ETP-Inbound\SERVICE\<today>`.
6. Problem? Call Sagar.

---

## For Sagar: what Workpc does each morning, and how you see missing shops

**Each morning.**

1. `Receive-ShopDailyFiles.ps1` (scheduled about 07:00 under the automation account or as administrator) reads yesterday's `ETP-Inbound` folders from OneDrive. It checks every file against the shop's `manifest.json` (size and fingerprint).
2. Good files are put into `C:\ProgramData\EtpReporting\Inbound` as one zip per shop and day (`TITAN-2026-10-11.zip`, `HELIOS-...`, `SERVICE-...`). A corrected file sent later arrives as `...-r2.zip`.
3. ETP's automatic import (`--automation-once`) reads the zips. The daily-inputs sheet is recognised by its content (family D001) and writes the manual inputs for the sheet's store and date. The sheet is all or nothing: if any value is bad, nothing is written, the file goes to Failed, and the failure is recorded in the import failure register. A corrected sheet changes only the values that differ and the history keeps the old ones. The file wins over an earlier screen entry. A day already finalised refuses changes until you reopen it.
4. Bad or doubtful files are never imported. They go to `C:\ProgramData\EtpReporting\OneDriveReceive\Quarantine` with a `.reason.txt` beside each.
5. Later in the night, the verified backup is encrypted and copied to `OneDrive\ETP-Backups` (last 14 kept).

**Where you see a missing shop.**

- `OneDriveReceive\Logs\missing-<date>.txt` names each shop that sent nothing. If the file does not exist, all three arrived. The receive task also ends with exit code 2, which shows in Task Scheduler.
- `OneDriveReceive\Logs\receive.log.jsonl` lists every file taken or refused.
- In ETP, the Today checklist still shows what is not imported for the date (R025, R022, R011, R030, the Service pack, and any required manual input). There is no separate "file not received" line on Today in 1.10.1 (decision 31); that belongs to the 1.11.0 self-diagnostics.
- Import then History shows the D001 attempts, and the import failure register shows any rejected sheet.

**When a shop is missing:** phone the shop. Ask them to check the PC is on, signed in and OneDrive is syncing, and to run the send by hand. Then run Receive again. If a sheet was rejected for a name or date mismatch, ask them to correct the date and Save As again. A missing sheet leaves the Today checklist showing the manual inputs as missing, so the day cannot be finalised until it arrives. A sheet that arrived with a box left empty does the same for that box, which is why staff type 0 for a zero figure.
