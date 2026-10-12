# Collections synthetic fixtures (1.12 wave 1, lane D2)

Invented data only: no real bank, account, UTR, merchant id, customer name or VPA. Account numbers are masked
(`XXXXXXXX0001`). These files stand in for the D19 sample exports, which do not exist yet; they exercise the layout
conventions only and must not be read as any real bank's or provider's format.

| File | Layout it tests |
|---|---|
| `bank-two-columns.csv` | preamble, header on row 3, `dd/MM/yyyy`, Withdrawal / Deposit columns, Indian grouping, one footer row |
| `bank-two-columns-reexport.csv` | the same lines with another preamble: a different file, no new line (Duplicate content) |
| `bank-two-columns-superset.csv` | the same lines plus three on 5 Aug: adds exactly three and supersedes the first file |
| `bank-signed-amount.csv` | one signed amount column with `Cr` / `Dr` suffix and a leading minus, `dd-MMM-yy`, one unreadable amount |
| `bank-drcr-flag.csv` | amount plus a `Dr/Cr` marker column, `dd/MM/yy`, two identical lines (ordinals 1 and 2) |
| `provider-card-gross.csv` | card acquirer shape: gross, MDR and GST on MDR, net, a FAILED status, a negative REFUND, payer name and VPA |

Hand-worked expectations used by the tests: two-column file = 5 lines (credits 12,500.00, 1,23,456.78 and 8,000.00; debits
118.00 and 25,000.00); superset = 8 lines; card file for MID-SYN-1 on 1 Aug = sale 1,000.00 minus refund 200.00 = 800.00
(the failed 500.00 sale does not count); MID-SYN-2 has no store, so its 2,000.00 is not in any store's total.
