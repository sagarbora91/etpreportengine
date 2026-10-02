# Tally decisions D12–D18 — decision sheet

For **the Owner and the accountant** of Saagar Traders (Titan World and Helios, Latur), to fill in together. Printable: tick one box per decision, add notes, sign the last page.

These seven choices decide how ETP's sales will appear in Tally. Until they are made, ETP can check, store and compare Tally data, but it cannot turn an invoice into a voucher. The master plan ([ETP-MASTER-AUDIT-AND-PHASED-PLAN.md](ETP-MASTER-AUDIT-AND-PHASED-PLAN.md), section 3) records them as OPEN; once this sheet is signed they are frozen there.

**Time needed:** about an hour with the accountant, with Tally open. D18 needs Tally on the PC that will run it.

**How to read each section**
- *The question* in plain words.
- *Options*: what each one means for the shop, for the accountant and for ETP.
- *Recommendation*: what ETP's developer suggests and why. Accounting and GST choices are the **accountant's call**; where the recommendation depends on a fact only the accountant knows, the sheet says so.
- *Decision*: tick one box and write any condition in the notes.

| | |
|---|---|
| Date | ________________ |
| Owner | ________________ |
| Accountant | ________________ |

---

## Facts to collect first (accountant)

These decide several answers below. Fill them in before going through the decisions.

| # | Fact | Answer |
|---|---|---|
| F1 | Are Titan World and Helios one firm (one PAN) or separate firms? | ☐ one firm ☐ separate firms |
| F2 | GST registrations: one GSTIN for both stores, or one per store? | ☐ one GSTIN ☐ one per store — GSTIN(s): ________________ |
| F3 | Today, are the two stores' books kept in one Tally company or in two? | ☐ one ☐ two ☐ not in Tally yet |
| F4 | Are any sales made to GST-registered businesses (B2B invoices that need the buyer's GSTIN in GSTR-1)? | ☐ never ☐ sometimes ☐ often |
| F5 | Is the GSTR-1 HSN summary prepared from Tally, from ETP/Titan's system, or by the accountant separately? | ☐ Tally ☐ ETP/Titan ☐ separately |
| F6 | GST rates actually sold at (both halves together): | ☐ 3% ☐ 5% ☐ 12% ☐ 18% ☐ 28% ☐ other: ______ |
| F7 | Is stock (quantity and value) kept in Tally today? | ☐ yes ☐ no |

---

## D12 — One Tally company per store, or one for the firm?

**The question.** Do Titan World and Helios go into two separate Tally companies, or into one company with the store marked on each voucher?

| Option | What it means |
|---|---|
| ☐ **(a) Two companies**, one per store | Each store's sales, GST and cash are in its own books. Natural when each store is a separate firm or has its own GSTIN. The accountant switches company to see each store. ETP links each store to its own Tally company (already supported). |
| ☐ **(b) One company, store as a cost centre** | One set of books for the firm; each voucher carries a cost centre "Titan World" or "Helios", so store-wise reports still work. Natural with one firm and one GSTIN. The accountant must create the two cost centres; ETP adds the cost centre to every voucher. |
| ☐ **(c) One company, store as a godown only** | Only makes sense if stock is kept in Tally (see D17). Store-wise accounting reports are weaker than with cost centres. |

**Recommendation.** Follow how the books are legally kept: **(a)** if F1 = separate firms or F2 = one GSTIN per store; otherwise **(b)**. Avoid (c) unless D17 = with stock items. Whatever is chosen, start with a **TEST** company that is a copy of it (D18).

**Decision:** ☐ (a) ☐ (b) ☐ (c)   Notes: ______________________________________________

---

## D13 — One voucher per invoice, or one summary per day?

**The question.** Does every ETP invoice become its own Sales voucher in Tally, or does each store's day become one summary voucher?

| Option | What it means |
|---|---|
| ☐ **Per invoice** | About 7 vouchers a day at Titan World and 1–2 at Helios (a recent month-to-date count: 178 and 37 invoices). Each voucher carries the ETP invoice number, so any invoice can be traced in Tally, and ETP can prove each one was recorded exactly once. Returns become Credit Notes against a real invoice. |
| ☐ **Daily summary** | One voucher per store per day with the day's totals. Fewer entries for the accountant to look through; invoice-level detail stays only in ETP. ETP can prove the day's totals but not individual invoices. A return on a later day cannot point to its invoice in Tally. |

**Recommendation.** **Per invoice.** It is the only shape where ETP can prove "this invoice is in Tally once, with these amounts", which is the main purpose of the Tally link. The volume is small. The first Tally steps are already built for it.

Note for ETP: today one batch is allowed per store per day; that suits both choices. Per-invoice batches that send only some of a day's invoices would need that rule narrowed later.

**Decision:** ☐ per invoice ☐ daily summary   Notes: ______________________________________________

---

## D14 — Customers: one retail ledger, or a ledger per customer?

**The question.** Who is the "party" on each Sales voucher?

| Option | What it means |
|---|---|
| ☐ **One retail ledger per store** ("Cash Sales" / "Retail Customers") | No customer ledgers to create or maintain. Customer names and phone numbers stay in ETP (they never leave it, as decided in D3). The customer name can still appear in the voucher narration if wanted. |
| ☐ **Named customer ledgers** | A Tally ledger for each customer from the ETP customer report. Hundreds of ledgers a year, most used once; names are not unique and phone numbers would have to go into Tally, which D3 does not allow. Needs D16 = clearing ledger (ETP refuses named ledgers with payments inside the voucher). |

**Recommendation.** **One retail ledger per store**, unless F4 shows regular B2B sales. If there are B2B sales, the accountant should say whether those few customers need their own ledger with GSTIN; that can be added as an exception later without changing the rest.

**Ledger name to use:** ________________ (store 1)   ________________ (store 2)

**Customer name in the narration?** ☐ yes ☐ no

**Decision:** ☐ one retail ledger ☐ named ledgers   Notes: ______________________________________________

---

## D15 — GST: which tax ledgers, and HSN on items?

**The question (accountant).** Which output-tax ledgers does Tally use, and must each line carry HSN and taxable value?

ETP never works tax out itself. It uses the GST breakdown from ETP's own GST detail report (CGST, SGST, IGST and cess per line), and blocks an invoice whose breakdown is missing or does not add up.

| Option | What it means |
|---|---|
| ☐ **One ledger per tax and rate** (e.g. "Output CGST 9%", "Output SGST 9%", "Output CGST 1.5%"…) | Matches the usual Tally GST setup and makes rate-wise checks easy. The accountant lists every ledger once (table below). |
| ☐ **One ledger per tax** ("Output CGST", "Output SGST", "Output IGST") | Fewer ledgers; the rate is visible only in the voucher. Rate-wise totals must come from elsewhere. |
| ☐ **Something else** (describe) | |

**HSN and taxable value per line:** ☐ not needed in Tally (F5 = not from Tally) ☐ needed — this usually means D17 = with stock items, or HSN set on the sales ledger; the accountant to say which: ______________

**Cess:** ☐ never charged ☐ charged — ledger: ________________

**Tax ledger names** (exactly as in Tally):

| Tax | Rate | Tally ledger name |
|---|---|---|
| CGST | | |
| SGST | | |
| CGST | | |
| SGST | | |
| IGST | | |

**Recommendation.** One ledger per tax and rate, no HSN in Tally, unless F5 says the HSN summary comes from Tally. This is the accountant's decision.

**Decision:** ☐ per tax and rate ☐ per tax ☐ other   Notes: ______________________________________________

---

## D16 — Payments: inside the voucher, or a clearing ledger settled later?

**The question (accountant).** How is the way the customer paid (cash, card, UPI, credit note, gift card…) recorded?

ETP's tender master has these modes: Cash, Card, UPI, CN, TC, Gift Card, Bank, Service Cash, Service Card, Service UPI. Round-off is recorded separately, never as a payment.

| Option | What it means |
|---|---|
| ☐ **Inside the Sales voucher** | Each voucher debits the payment ledger directly: "Cash" for cash, and for card and UPI a receivable ledger such as "Card receivable – <card provider>" or "UPI receivable – <UPI provider>". When the bank credits the money, the accountant (or Phase 8) moves it from that receivable to the bank. Simple and complete in one voucher. Only one payment mode per voucher is supported in the first step; split payments come next. |
| ☐ **Clearing ledger per mode, settled by Receipt vouchers** | The Sales voucher is made against a clearing ledger; separate Receipt vouchers record each payment when it arrives. Needed with named customer ledgers. More vouchers; matches settlement timing more closely. |

**Ledgers per payment mode** (exactly as in Tally):

| Mode | Tally ledger | | Mode | Tally ledger |
|---|---|---|---|---|
| Cash | | | Gift Card | |
| Card | | | Bank | |
| UPI | | | Service Cash | |
| CN (credit note) | | | Service Card | |
| TC | | | Service UPI | |
| Round off | | | | |

**Recommendation.** **Inside the voucher**, with a receivable ledger for each non-cash mode, unless D14 = named ledgers. It keeps one voucher per invoice and still lets Phase 8 match bank settlements later. Credit-note and gift-card tenders need the accountant to name their ledgers; until then those invoices stay blocked.

**Decision:** ☐ inside the voucher ☐ clearing ledger + receipts   Notes: ______________________________________________

---

## D17 — Accounting only, or with stock items?

**The question.** Do Tally vouchers carry stock items and quantities, or only ledger amounts?

| Option | What it means |
|---|---|
| ☐ **Accounting only** | Vouchers carry amounts per ledger. Stock stays in ETP, which already reports closing stock, movement and variance. No stock items, units or godowns to keep in Tally. |
| ☐ **With stock items** | Every ETP product code becomes a Tally stock item (thousands of watch variants), with unit, godown per store and HSN. Tally can then show stock and HSN-wise sales, but every new product must exist in Tally before its sale can be sent, and stock must be kept in two places. |

**Recommendation.** **Accounting only**, unless F7 = stock is already kept in Tally or F5 = the HSN summary must come from Tally.

**Decision:** ☐ accounting only ☐ with stock items   Notes: ______________________________________________

---

## D18 — The Tally installation (facts, on the Tally PC)

These are facts, not choices. Fill them in at the PC that will run Tally for ETP.

| # | Fact | Where to find it | Answer |
|---|---|---|---|
| 18.1 | TallyPrime release and build | TallyPrime: **Help → About** (copy the whole line) | ________________________ |
| 18.2 | PC that runs Tally | Windows **Settings → System → About**, "Device name" | ________________ |
| 18.3 | TEST company name(s) — must contain "TEST", e.g. "TEST - Saagar Traders A" | Created by the accountant as a copy of the real company | ________________ |
| 18.4 | Real (live) company name(s) | Company list in TallyPrime | ________________ |
| 18.5 | Is Tally set to act as a server, and on which port? | **F1 Help → Settings → Connectivity → Client/Server configuration**: "TallyPrime acts as" (Both / Server) and "Port" (often 9000). Labels may differ slightly by build. | ☐ Both ☐ Server ☐ None — Port: ______ |
| 18.6 | Sales voucher type numbering | Alter the **Sales** voucher type → "Method of voucher numbering" | ☐ Automatic ☐ Manual ☐ Multi-user auto |
| 18.7 | Credit Note voucher type numbering | Same, for **Credit Note** | ☐ Automatic ☐ Manual ☐ Multi-user auto |
| 18.8 | "Prevent duplicates" on those voucher types | Same screen | ☐ yes ☐ no |
| 18.9 | Who in Tally will import ETP files (user name), and is it a restricted user? | Tally users and passwords | ________________ |

**Why 18.6 matters.** With *Manual* numbering Tally keeps ETP's invoice number as the voucher number, so ETP can check it. With *Automatic*, Tally gives its own number and ETP checks the voucher by the ETP key in its narration instead. Both work; ETP needs to know which.

**Also on the Tally PC (no answer needed here):** with the accountant, key **one ordinary cash sale** by hand in the TEST company exactly as you want ETP's vouchers to look, and export it (and one Credit Note) as XML. Keep the originals outside the ETP code; ETP's tests use a copy with the customer details removed.

---

## Also ask the accountant (not numbered decisions)

| Question | Answer |
|---|---|
| Returns (SR) and cancellations: Credit Note voucher type name in Tally? | ________________ |
| Are exchanges recorded as a return plus a new sale? | ☐ yes ☐ no ☐ other: ______ |
| From which date should vouchers be sent (first business date)? | ________________ |
| Service-centre income: same company and ledgers, or separate? | ________________ |

---

## Sign-off

| | Name | Signature | Date |
|---|---|---|---|
| Owner | | | |
| Accountant | | | |

**After signing:** send a photo or scan of every page in a Claude session. The decisions are then recorded as frozen in the master plan, the Tally company defaults in ETP are set to match, and the work each one unblocks starts:

| Decision | Unblocks |
|---|---|
| D12, D18 | Setting up the TEST company in Settings → Integrations → Tally companies; the Tally check on the PC (plan task 5) |
| D13, D14, D15, D16 | Turning an invoice into a voucher (task 6) and writing the Tally file (task 8) |
| D16 (CN, gift card), returns | Returns, split payments and credit-note tenders (tasks 15, 16) |
| D17 | Stock-item vouchers, or confirming they are not needed (task 17a) |
| 18.6 | Whether ETP compares voucher numbers |
