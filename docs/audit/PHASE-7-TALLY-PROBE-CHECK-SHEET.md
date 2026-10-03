# Phase 7 — Tally probe check sheet (plan task 5)

A printable sheet for **one visit to the PC that runs TallyPrime**, with the accountant. It records how the installed Tally behaves, so ETP's Tally file (task 8) and its read-back (task 9) are built on what this Tally actually does, not on published examples.

**Nothing on this sheet changes the real books.** Everything happens in the **TEST** company. Parts B and C are keyed by hand in the TEST company. Part D only *reads* from Tally: every request asks Tally to export, none asks it to import. A request that imports is refused by the script itself.

Time needed: about one hour. Print this sheet, fill it in by hand, and keep the files from Parts C and D together in one folder.

| | |
|---|---|
| Date of visit | ________________ |
| Done by | ________________ (owner) and ________________ (accountant) |
| Decision sheet signed? | ☐ yes ☐ not yet — the D18 table there is filled in during this visit |

---

## Part A — Before you start (5 minutes)

| # | Check | Done |
|---|---|---|
| A1 | The **TEST** company exists, is a copy of the real company, and its name contains "TEST" (for example "TEST - Saagar Traders"). Write its exact name: ________________________ | ☐ |
| A2 | A Tally backup of the **real** company was taken today (Tally: Data → Backup). Write where it was saved: ________________ | ☐ |
| A3 | Only the **TEST** company is open in Tally. The real company is closed. | ☐ |
| A4 | Fill in the **D18 table** on the decision sheet (Tally build from Help → About, port, numbering method of the Sales and Credit Note voucher types, prevent duplicates). | ☐ |
| A5 | In the TEST company, the two cost centres exist with these exact names (D12 = one company, store as cost centre): Titan World: ________________ Helios: ________________ | ☐ |
| A6 | Which ledgers have "Cost centres are applicable" set to Yes? (Usually only the sales ledger.) ________________________ | ☐ |
| A7 | Exact ledger names in the TEST company for: Cash ________________ · Card receivable ________________ · UPI receivable ________________ · Sales ________________ · Output CGST 9% ________________ · Output SGST 9% ________________ · Round off ________________ | ☐ |

---

## Part B — Key three vouchers by hand in the TEST company (15 minutes)

These are the "anchor" vouchers: exactly what the accountant wants ETP's vouchers to look like. Use a made-up customer; **never a real customer's name or phone number**. Use today's date.

| # | Voucher | How to key it | Done |
|---|---|---|---|
| B1 | **Cash sale** | Voucher type **Sales**. Cash 1,180.00 debit. Sales 1,000.00 credit, cost centre **Titan World**. Output CGST 9% 90.00 credit. Output SGST 9% 90.00 credit. Voucher number: **PROBE-1** if numbering is Manual. Narration, copied exactly: `ETP:WLMHW:2027:PROBE-1:SALES:1 \| ETP probe sale` | ☐ |
| B2 | **Credit note** (a return of B1) | Voucher type **Credit Note**, the reverse of B1. Narration: `ETP:WLMHW:2027:PROBE-2:CREDIT_NOTE:1 \| ETP probe return` | ☐ |
| B3 | **Cancelled sale** | Key a copy of B1 with narration `ETP:WLMHW:2027:PROBE-3:SALES:1 \| ETP probe cancelled`, save it, then cancel it (Alt+X on the voucher in the Day Book). | ☐ |

Record:

| # | Question | Answer |
|---|---|---|
| B4 | Which screen did the accountant key B1 in? | ☐ Accounting voucher view ☐ Invoice view ☐ Item invoice |
| B5 | After saving B1, what voucher number does Tally show? (If you typed PROBE-1 and Tally shows another number, numbering is automatic.) | ________________ |
| B6 | Did the narration stay exactly as typed, including the `\|`? | ☐ yes ☐ no — shown as: ________________ |

---

## Part C — Export the Day Book by hand (10 minutes)

This is how ETP's first read-back works: the operator exports the Day Book from Tally and loads the file into ETP.

1. In the TEST company, open **Day Book** for today's date.
2. **Export** (Alt+E). File format **XML (Data Interchange)**. Labels may differ slightly between Tally builds; write what you chose: ________________
3. Save the file as `C1-daybook-manual.xml` in the probe folder.

| # | Question (open the file in Notepad) | Answer |
|---|---|---|
| C2 | Does the file name the company? Look for `SVCURRENTCOMPANY` near the top. | ☐ yes, shows: ________________ ☐ no |
| C3 | Does it show the dates it covers? Look for `SVFROMDATE` and `SVTODATE`. | ☐ yes: ________ to ________ ☐ no |
| C4 | Are all three vouchers in it, including the cancelled one? Is there an `ISCANCELLED` tag with `Yes` on B3? | ☐ all three ☐ B3 missing — `ISCANCELLED`: ☐ seen ☐ not seen |
| C5 | Is each narration exactly as keyed (Part B)? | ☐ yes ☐ no |
| C6 | Does B1 have `VOUCHERNUMBER`, `GUID`, `MASTERID` and `ALTERID`? | ☐ VOUCHERNUMBER ☐ GUID ☐ MASTERID ☐ ALTERID |
| C7 | Where is the cost centre on B1? Look for `CATEGORYALLOCATIONS.LIST` or `COSTCENTREALLOCATIONS.LIST` under the Sales line. | ☐ found ☐ not found |
| C8 | Which ledger lists does B1 use: `ALLLEDGERENTRIES.LIST`, `LEDGERENTRIES.LIST`, or lines inside `ALLINVENTORYENTRIES.LIST`? | ________________ |
| C9 | Is the party shown as `PARTYLEDGERNAME`? Which ledger? | ________________ |
| C10 | Do you see `OBJVIEW`, `PERSISTEDVIEW` or `ISINVOICE`? Write their values. | ________________ |

---

## Part D — Read-only requests to Tally from this PC (20 minutes)

Only if Tally acts as a server (D18 18.5 = Both or Server). If it is set to None, skip Part D and tick here: ☐ skipped. ETP then reads Tally only through hand-exported files.

**Safety.** These requests ask Tally to *export*. The script refuses any request that contains `Import`. Run it on the Tally PC only, with only the TEST company open.

Open **Windows PowerShell** on the Tally PC and paste this block once. Change the three values on the first lines first.

```powershell
$port = 9000                             # D18 18.5
$company = 'TEST - Saagar Traders'       # exact TEST company name (A1)
$day = '20261003'                        # the date of the Part B vouchers, yyyyMMdd
$out = Join-Path $env:USERPROFILE 'Desktop\TallyProbe'
New-Item -ItemType Directory -Force -Path $out | Out-Null

function Send-TallyRead([string]$name, [string]$xml) {
  if ($xml -match 'Import') { throw 'This sheet never imports into Tally.' }
  $xml = $xml.Replace('__COMPANY__', $company).Replace('__DAY__', $day)
  try {
    $response = Invoke-WebRequest -Uri "http://127.0.0.1:$port/" -Method Post -UseBasicParsing -TimeoutSec 60 `
      -ContentType 'text/xml;charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($xml))
    $bytes = $response.RawContentStream.ToArray()
    [IO.File]::WriteAllBytes((Join-Path $out "$name.xml"), $bytes)
    $first = ($bytes | Select-Object -First 3 | ForEach-Object { $_.ToString('X2') }) -join ' '
    "{0}: {1} bytes, first bytes {2}, content type {3}" -f $name, $bytes.Length, $first, $response.Headers['Content-Type']
  } catch { "{0}: FAILED - {1}" -f $name, $_.Exception.Message }
}
```

Then paste each request below, one at a time, and write down the line PowerShell prints. The request texts are TallyPrime's published XML format. Whether this build answers them as expected is exactly what this part checks, so an error is a useful answer too.

**D1 — Which companies are open**

```powershell
Send-TallyRead 'D1-open-companies' @'
<ENVELOPE><HEADER><VERSION>1</VERSION><TALLYREQUEST>Export</TALLYREQUEST><TYPE>Collection</TYPE><ID>ETP Open Companies</ID></HEADER>
<BODY><DESC><STATICVARIABLES><SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT></STATICVARIABLES>
<TDL><TDLMESSAGE><COLLECTION NAME="ETP Open Companies" ISMODIFY="No"><TYPE>Company</TYPE><FETCH>Name</FETCH></COLLECTION></TDLMESSAGE></TDL>
</DESC></BODY></ENVELOPE>
'@
```

**D2 — Day Book of the TEST company for the Part B date**

```powershell
Send-TallyRead 'D2-daybook-named-company' @'
<ENVELOPE><HEADER><TALLYREQUEST>Export Data</TALLYREQUEST></HEADER><BODY><EXPORTDATA><REQUESTDESC><REPORTNAME>Day Book</REPORTNAME>
<STATICVARIABLES><SVCURRENTCOMPANY>__COMPANY__</SVCURRENTCOMPANY><SVFROMDATE>__DAY__</SVFROMDATE><SVTODATE>__DAY__</SVTODATE>
<SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT></STATICVARIABLES></REQUESTDESC></EXPORTDATA></BODY></ENVELOPE>
'@
```

**D3 — The same, without naming the company**

```powershell
Send-TallyRead 'D3-daybook-no-company' @'
<ENVELOPE><HEADER><TALLYREQUEST>Export Data</TALLYREQUEST></HEADER><BODY><EXPORTDATA><REQUESTDESC><REPORTNAME>Day Book</REPORTNAME>
<STATICVARIABLES><SVFROMDATE>__DAY__</SVFROMDATE><SVTODATE>__DAY__</SVTODATE>
<SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT></STATICVARIABLES></REQUESTDESC></EXPORTDATA></BODY></ENVELOPE>
'@
```

**D4 — Naming a company that is not open**

```powershell
Send-TallyRead 'D4-daybook-company-not-open' @'
<ENVELOPE><HEADER><TALLYREQUEST>Export Data</TALLYREQUEST></HEADER><BODY><EXPORTDATA><REQUESTDESC><REPORTNAME>Day Book</REPORTNAME>
<STATICVARIABLES><SVCURRENTCOMPANY>ETP PROBE NO SUCH COMPANY</SVCURRENTCOMPANY><SVFROMDATE>__DAY__</SVFROMDATE><SVTODATE>__DAY__</SVTODATE>
<SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT></STATICVARIABLES></REQUESTDESC></EXPORTDATA></BODY></ENVELOPE>
'@
```

**D5 — Vouchers as full objects (a collection instead of the Day Book report)**

```powershell
Send-TallyRead 'D5-voucher-collection' @'
<ENVELOPE><HEADER><VERSION>1</VERSION><TALLYREQUEST>Export</TALLYREQUEST><TYPE>Collection</TYPE><ID>ETP Probe Vouchers</ID></HEADER>
<BODY><DESC><STATICVARIABLES><SVCURRENTCOMPANY>__COMPANY__</SVCURRENTCOMPANY><SVFROMDATE>__DAY__</SVFROMDATE><SVTODATE>__DAY__</SVTODATE>
<SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT></STATICVARIABLES>
<TDL><TDLMESSAGE><COLLECTION NAME="ETP Probe Vouchers" ISMODIFY="No"><TYPE>Voucher</TYPE><FETCH>*</FETCH></COLLECTION></TDLMESSAGE></TDL>
</DESC></BODY></ENVELOPE>
'@
```

Record:

| # | Question | Answer |
|---|---|---|
| D1 | What did D1 return? Are the open company names listed? | ________________________ |
| D2 | Bytes and first bytes printed for D2. (`EF BB BF` means a byte-order mark; `3C` means the file starts directly with `<`.) | ________ bytes, first bytes ________ |
| D2a | Does D2 contain all three Part B vouchers with the same tags as the manual export (C4–C10)? | ☐ same ☐ different: ________________ |
| D2b | Does D2's answer name the company (`SVCURRENTCOMPANY`) anywhere in it? | ☐ yes ☐ no |
| D3 | Without a company, which company answered? | ☐ the open TEST company ☐ error: ________________ |
| D4 | What does Tally say for a company that is not open? Copy the first line of the message. | ________________________ |
| D5 | Did D5 return the vouchers? Does it carry more fields than D2 (for example `GUID`, `MASTERID`)? | ________________________ |
| D6 | Did anything appear in Tally's own screen while the requests ran (an error, a prompt)? | ☐ nothing ☐ yes: ________________ |

---

## Part E — What this visit does not cover

Two questions in plan task 5 need an actual import, so they are answered at the **first test import** of an ETP file into the TEST company (task 8), not on this read-only visit:

- whether Tally keeps ETP's voucher number when it imports a voucher (B5 tells us the numbering method; the import shows the behaviour);
- what Tally answers when it rejects an imported voucher.

---

## Part F — Bring back

| # | Item | Done |
|---|---|---|
| F1 | This sheet, filled in (photo or scan of every page) | ☐ |
| F2 | The decision sheet's D18 table | ☐ |
| F3 | The probe folder: `C1-daybook-manual.xml` and the `D1`–`D5` files | ☐ |
| F4 | An export of B1 and B2 alone, if the accountant can make one (the anchor vouchers) | ☐ |

Send them in a Claude session. They are then recorded in [PHASE-7-REPORT.md](PHASE-7-REPORT.md) under "Compatibility and protocol". The files become test fixtures, with anything that looks like a customer detail removed first. ETP's read-back and Tally file are then checked against them, and anything this Tally does differently from the published format is fixed before the first test import.

| | Name | Signature | Date |
|---|---|---|---|
| Owner | | | |
| Accountant | | | |
