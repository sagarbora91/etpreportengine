"""Read-only, independent workbook check against Phase 2 exported review files.
Usage: python tools/audit/verify_phase2_sources.py SOURCE_ROOT REVIEW_DIRECTORY
Requires openpyxl. Outputs only aggregate figures, never customer names.
"""
import sys,json,hashlib
from pathlib import Path
from collections import defaultdict
from decimal import Decimal
from datetime import date,datetime
import openpyxl

root,review=map(Path,sys.argv[1:3]);day=date(2026,8,25)
def dec(x):return Decimal(str(x or 0))
def records(path,header_row=1):
    w=openpyxl.load_workbook(path,read_only=True,data_only=True);s=w.active;s.reset_dimensions()
    it=s.iter_rows(values_only=True)
    for _ in range(header_row-1):next(it)
    header=[str(x or '').strip().upper() for x in next(it)]
    # ETP sometimes repeats the entire header to the right. First occurrence is authoritative.
    idx={h:header.index(h) for h in header if h}
    result=[{h:(row[i] if i<len(row) else None) for h,i in idx.items()} for row in it]
    w.close();return result
def dt(x):
    if isinstance(x,(int,float)):return datetime.strptime(str(int(x)),'%Y%m%d').date()
    if isinstance(x,datetime):return x.date()
    if isinstance(x,date):return x
    for fmt in ['%d/%m/%Y','%d-%m-%Y','%Y-%m-%d','%d-%b-%Y','%d %b %Y','%d/%m/%Y %H:%M:%S']:
        try:return datetime.strptime(str(x),fmt).date()
        except ValueError:pass
    raise ValueError('Unrecognised source date '+str(x))
def one(folder,pattern):return next(folder.glob(pattern))
def exported(store,name):return records(review/f'{store}-{name}.xlsx',8)
summary={}
for store,foldername in [('WLMHW','TITAN ALL REPORT 01 JULY 2026 TO 25 AUG 2026'),('HEMW','HELIOS ALL REPORT 01 JULY 2026 TO 25 AUG 2026')]:
    folder=root/store/foldername;source=one(folder,'*SDB-VariantwiseSales*.xlsx')
    sales=[x for x in records(source) if x.get('TRANS_TYPE') in ['INV','SR','BC']]
    ftd=[x for x in sales if dt(x['INVDATE'])==day]
    mtd=[x for x in sales if date(2026,8,1)<=dt(x['INVDATE'])<=day]
    value=sum(dec(x['NETAMOUNT']) for x in ftd);qty=sum(dec(x['QTY']) for x in ftd)
    invoices={str(x['INVNUMBER']) for x in ftd if x['TRANS_TYPE']=='INV'}
    matrix={x['METRIC']:x for x in exported(store,'DSR')}
    assert dec(matrix['VALUE']['FTD'])==value,(store,matrix['VALUE']['FTD'],str(value),len(ftd))
    assert dec(matrix['VOL']['FTD'])==qty
    assert dec(matrix['INVOICE']['FTD'])==len(invoices)
    assert dec(matrix['VALUE']['MTD'])==sum(dec(x['NETAMOUNT']) for x in mtd)
    assert dec(matrix['INVOICE']['MTD'])==len({x['INVNUMBER'] for x in mtd if x['TRANS_TYPE']=='INV'})
    customer=exported(store,'Invoice-Summary');customer=[x for x in customer if x['DATE']!='Total']
    assert sum(dec(x['NET VALUE']) for x in customer)==value
    assert sum(dec(x['QUANTITY']) for x in customer)==qty
    assert all(x['CUSTOMER'] and x['CUSTOMER']!='—' for x in customer)
    stock_source=records(one(folder,'*Closing Stock*.xlsx'))
    stock_total=sum(dec(x['QTY']) for x in stock_source if x.get('ITEMNUMBER'))
    stock=exported(store,'Physical-Stock');stock=[x for x in stock if x['STORE']!='Total']
    assert sum(dec(x['SYSTEM']) for x in stock)==stock_total
    # Stock source BRAND is a display label; validate each brand, not just the grand total.
    bybrand=defaultdict(Decimal)
    for x in stock_source:
        if x.get('ITEMNUMBER'):bybrand[str(x['BRAND']).strip()]+=dec(x['QTY'])
    assert dict(bybrand)=={str(x['BRAND']):dec(x['SYSTEM']) for x in stock}
    cro=[x for x in records(one(folder,'*CRO Wise Sales*.xlsx')) if x.get('TRANS_TYPE') in ['INV','SR','BC'] and dt(x['INVDATE'])==day]
    bycro=defaultdict(Decimal)
    for x in cro:bycro[str(x['CRO NUMBER'])]+=dec(x['NETAMOUNT'])*(-1 if dec(x['QTY'])<0 and dec(x['NETAMOUNT'])>0 else 1)
    staff=[x for x in exported(store,'Staff-Performance') if x['STORE']!='Control']
    assert dict(bycro)=={str(x['CRO']):dec(x['VALUE INCL. GST']) for x in staff}
    revenue=[x for x in records(one(folder,'*Revenue Report*.xlsx')) if x.get('TRANS_TYPE') in ['INV','SR','BC'] and dt(x['INVOICEDATE'])==day]
    modes={'Cash':['CASH','CASH REFUND'],'Card':['CARD'],'UPI':['BHIMUPI','PHONEPE','BHARATPE','PAYMENTTYPE25'],'Bank':['CHEQUE','CHEQUE/RTGS REFUND'],'Gift Card':['GIFTCARD']}
    ledger=exported(store,'Cash-Book')
    for mode,columns in modes.items():
        expected=sum(dec(x.get(c)) for x in revenue for c in columns)
        actual=sum(dec(x['CR — AMOUNT']) for x in ledger if x['CR — PARTICULAR']==mode)
        assert expected==actual,(store,mode,expected,actual)
    assert value==sum(dec(x['NETVALUE']) for x in revenue)
    service=[x for x in exported(store,'Service-Sales') if x['PERIOD']=='FTD'][0]
    assert dec(service['TOTAL'])==Decimal(3084) # Explicit synthetic manual fixture, not imported sales.
    assert sum(dec(service[x]) for x in ['WDC','CASH','CARD','UPI'])==dec(service['TOTAL'])
    summary[store]={'ftd_value':str(value),'ftd_qty':str(qty),'mtd_value':str(sum(dec(x['NETAMOUNT']) for x in mtd)),'mtd_inv_only':len({x['INVNUMBER'] for x in mtd if x['TRANS_TYPE']=='INV'}),'stock_system':str(stock_total),'brands':len(bybrand),'six_reports':'PASS; service and physical counts use labelled manual fixtures','source_sha256':hashlib.sha256(source.read_bytes()).hexdigest()}
history=root/'HEMW'/'till 6 sep 26'/'R025_SDB_VariantwiseSales.xlsx'
rows=[x for x in records(history) if x.get('TRANS_TYPE') in ['INV','SR','BC']]
summary['Helios_history']={'ly_ftd':str(sum(dec(x['NETAMOUNT']) for x in rows if dt(x['INVDATE'])==date(2025,8,25))),'ly_ytd':str(sum(dec(x['NETAMOUNT']) for x in rows if date(2025,4,1)<=dt(x['INVDATE'])<=date(2025,8,25)))}
(review/'independent-source-check.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
print(json.dumps(summary,indent=2))
