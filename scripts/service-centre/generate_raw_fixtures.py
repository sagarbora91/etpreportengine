"""Generate the synthetic RAW Service export fixtures (lane L9, Service interim, decision 15, 3 Oct 2026).

The daily raw ETP Service pack is mostly CSV, with some XLSX, named after the window it covers
("JOB REPORT 06.10.2026 TO 09.10.2026.csv") or the one day it lists ("PENDING REPORT 09.10.2026.csv").
These fixtures copy only that SHAPE: headers come from families.spec.json, every value is synthetic.
Nothing is read from a real export.

  python scripts/service-centre/generate_raw_fixtures.py [--spec PATH] [--out DIR]

Output (byte-stable; re-running over an unchanged spec rewrites the same bytes):
  tests-dotnet/fixtures/service-interim/raw/
    JOB REPORT 06.10.2026 TO 09.10.2026.csv                      S002, UTF-8, CRLF
    TENDER COLLECTION 05.10.2026 TO 09.10.2026.csv               S004, UTF-8, LF; 05 Oct restates a week2 date
    PENDING REPORT 09.10.2026.csv                                S009, one-day snapshot
    PENDING DELIVERY 09.10.2026.csv                              S010, one-day snapshot (not S031: other headers)
    R R DELIVERD 06.10.2026 TO 09.10.2026.csv                    S018 (83-column view; the name decides)
    R R PENDING DELIVERY 06.10.2026 TO 09.10.2026.csv            S031 (83-column view; the name decides)
    RENVENUE REPORT 06.10.2026 TO 09.10.2026.csv                 S003, saved as Windows-1252, a quoted comma
    TENDER COLLECTIN SUMMARY 06.10.2026 TO 09.10.2026.csv        S005 -> Not needed
    TECHNICIAN PRODUCIVITY REPORT 06.10.2026 TO 09.10.2026.xlsx  S028 raw header -> Not needed (deferred)
    EMPOWERMENT REPORT 06.10.2026 TO 09.10.2026.xlsx             S022 header on row 12, under 11 title rows
    GPRC CLAIM 06.10.2026 TO 09.10.2026.xlsx                     new claims layout -> Unknown layout

Synthetic vocabulary (L0's rules): jobs JOAW330SYN01nn, bills BIAW330SYN00nn, phones 9XXXXXX000,
e-mails @example.invalid, names "Sample Customer NN". Spellings in the file names are ETP's own.
"""
import argparse
import csv
import datetime
import io
import json
import re
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_SPEC = Path(__file__).with_name("families.spec.json")
DEFAULT_OUT = ROOT / "tests-dotnet" / "fixtures" / "service-interim" / "raw"
CONSOLIDATION_COLUMNS = ["SourcePeriodFrom", "SourcePeriodTo", "SourceFile"]
FIXED_TIME = datetime.datetime(2026, 10, 9, 0, 0, 0)
WINDOW = [datetime.date(2026, 10, d) for d in (6, 7, 8, 9)]
JOB = re.compile(r"job_?order|^jo_?(no|number)$|^jonumber$|joborder")
BILL = re.compile(r"(invoice|billing|document|bill)_?(no|number)$|^billingnumber$|cashmemo")


def load_spec(path):
    return {family["FamilyCode"]: family for family in json.loads(Path(path).read_text(encoding="utf-8"))}


def raw_columns(family):
    """The raw export's columns: the spec's, without the consolidated SourcePeriodFrom/To/SourceFile (review M2)."""
    return [column for column in family["Columns"] if column["SourceHeader"] not in CONSOLIDATION_COLUMNS]


def date_text(value, style):
    if style == "d-M-yyyy":
        return f"{value.day}-{value.month}-{value.year}"
    if style == "dd-MM-yyyy":
        return value.strftime("%d-%m-%Y")
    raise ValueError(style)


def sample(code, column, row, day, style, job_base=101):
    """One synthetic cell. `row` numbers the data rows from 0; `day` is the row's business date."""
    name, kind = column["CanonicalField"], column["DataType"]
    if kind == "Date":
        # A pending list's job date is older than the snapshot, so its age is not zero.
        if code in ("S009", "S010") and name in ("jodate", "dop", "edd", "indentdate", "repairdate"):
            day = datetime.date(2026, 9, 20 + row)
        return day if style is None else date_text(day, style)
    if kind == "Integer":
        return {"month": day.month, "year": day.year}.get(name, 1)
    if kind == "Decimal":
        return f"{100 + 10 * row}.00"
    if kind == "Identifier":
        if name == "store_code":
            return "AW330"
        if JOB.search(name):
            return f"JOAW330SYN{job_base + row:04d}"
        if BILL.search(name):
            return f"BIAW330SYN{row + 1:04d}"
        if re.search(r"mobile|landline|(^|_)phone|contact", name):
            return "9XXXXXX000"
        return f"SYN-{row + 1:04d}"
    if re.search(r"cust.*name|^name$|endcustomername", name):
        return f"Sample Customer {row + 1:02d}"
    if re.search(r"e_?mail", name):
        return f"sample{row + 1:02d}@example.invalid"
    if re.search(r"contact|mobile|phone", name):
        return "9XXXXXX000"
    return "SAMPLE"


def csv_bytes(headers, rows, newline, encoding="utf-8"):
    buffer = io.StringIO()
    writer = csv.writer(buffer, quoting=csv.QUOTE_ALL, lineterminator=newline)
    writer.writerow(headers)
    writer.writerows(rows)
    return buffer.getvalue().encode(encoding)


def stable_zip(data):
    # openpyxl stamps each zip entry and docProps/core.xml with the current time; fix both so output is byte-stable.
    source, target = zipfile.ZipFile(io.BytesIO(data)), io.BytesIO()
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as output:
        for info in source.infolist():
            content = source.read(info.filename)
            if info.filename == "docProps/core.xml":
                content = re.sub(rb"(<dcterms:(?:created|modified)[^>]*>)[^<]*", rb"\g<1>2026-10-09T00:00:00Z", content)
            entry = zipfile.ZipInfo(info.filename, date_time=(2026, 10, 9, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_DEFLATED
            output.writestr(entry, content)
    return target.getvalue()


def xlsx_bytes(sheet_name, title_rows, headers, rows):
    import openpyxl
    workbook = openpyxl.Workbook()
    workbook.properties.creator = "openpyxl"
    workbook.properties.created = workbook.properties.modified = FIXED_TIME
    sheet = workbook.active
    sheet.title = sheet_name
    for row in title_rows:
        sheet.append(row)
    sheet.append(headers)
    for values in rows:
        sheet.append(values)
        for cell in sheet[sheet.max_row]:
            if isinstance(cell.value, (datetime.date, datetime.datetime)):
                cell.number_format = "dd-mm-yyyy"
    buffer = io.BytesIO()
    workbook.save(buffer)
    return stable_zip(buffer.getvalue())


def family_rows(code, columns, days, style, job_base=101, per_day=1):
    rows, index = [], 0
    for day in days:
        for _ in range(per_day):
            rows.append([sample(code, column, index, day, style, job_base) for column in columns])
            index += 1
    return rows


def build(spec):
    files = {}

    def csv_file(name, code, days, style, newline="\r\n", job_base=101, per_day=1, change=None, encoding="utf-8"):
        columns = raw_columns(spec[code])
        rows = family_rows(code, columns, days, style, job_base, per_day)
        if change:
            change(columns, rows)
        files[name] = csv_bytes([column["SourceHeader"] for column in columns], rows, newline, encoding)

    csv_file("JOB REPORT 06.10.2026 TO 09.10.2026.csv", "S002", WINDOW, "d-M-yyyy")

    def tender(columns, rows):
        # Three tenders over the dates; 05 Oct restates a week2 billing date with a different amount.
        names = [column["CanonicalField"] for column in columns]
        for index, row in enumerate(rows):
            for tender_name in ("cashamount", "cardamount", "upi"):
                row[names.index(tender_name)] = "0.00"
            tender_name = ("cashamount", "cardamount", "upi")[index % 3]
            amount = f"{250 + 25 * index}.00"
            row[names.index(tender_name)] = amount
            row[names.index("totalamount")] = amount
            for other in ("bharatpe", "chequeamount", "rtgsamount", "advanceamount"):
                row[names.index(other)] = "0.00"
            row[names.index("phonepe")] = "0"
    csv_file("TENDER COLLECTION 05.10.2026 TO 09.10.2026.csv", "S004",
             [datetime.date(2026, 10, 5)] + WINDOW, "d-M-yyyy", newline="\n", per_day=2, change=tender)

    # Snapshots: one day, several jobs. JOAW330SYN0001/0002 are week1/week2 jobs still pending; 01nn are new.
    def pending(columns, rows):
        names = [column["CanonicalField"] for column in columns]
        job = next(i for i, n in enumerate(names) if JOB.search(n))
        rows[0][job], rows[1][job] = "JOAW330SYN0001", "JOAW330SYN0002"
    snapshot = [datetime.date(2026, 10, 9)]
    csv_file("PENDING REPORT 09.10.2026.csv", "S009", snapshot, "d-M-yyyy", newline="\n", per_day=4, change=pending)
    csv_file("PENDING DELIVERY 09.10.2026.csv", "S010", snapshot, "d-M-yyyy", newline="\n", job_base=121, per_day=3)

    def lines(columns, rows):
        # One delivered job with two line rows of different spare value.
        names = [column["CanonicalField"] for column in columns]
        job = names.index("jobordernumber")
        rows[1][job] = rows[0][job]
        rows[1][names.index("sparevalue")] = "45.00"
    csv_file("R R DELIVERD 06.10.2026 TO 09.10.2026.csv", "S018", WINDOW, "dd-MM-yyyy", job_base=141, change=lines)
    csv_file("R R PENDING DELIVERY 06.10.2026 TO 09.10.2026.csv", "S031", WINDOW[:3], "dd-MM-yyyy", job_base=161)

    def revenue(columns, rows):
        # Saved by Excel as "CSV (Comma delimited)": Windows-1252, and a text value holding a comma (so it is quoted).
        names = [column["CanonicalField"] for column in columns]
        rows[0][names.index("sc_name")] = "Sample Café, Main Road"
        rows[1][names.index("brand_name")] = "Sample – Brand"
    csv_file("RENVENUE REPORT 06.10.2026 TO 09.10.2026.csv", "S003", WINDOW, "dd-MM-yyyy", job_base=181,
             change=revenue, encoding="cp1252")

    csv_file("TENDER COLLECTIN SUMMARY 06.10.2026 TO 09.10.2026.csv", "S005", WINDOW[:1], None, newline="\n", per_day=2)

    # XLSX exports.
    s028 = raw_columns(spec["S028"])
    files["TECHNICIAN PRODUCIVITY REPORT 06.10.2026 TO 09.10.2026.xlsx"] = xlsx_bytes(
        "TechnicianProductivityReport", [], [c["SourceHeader"] for c in s028],
        family_rows("S028", s028, WINDOW[:2], None))

    s022 = raw_columns(spec["S022"])
    title = [["Report Name", "Empowerment Report"], ["Service Centre", "AW330"], ["Brand"], ["Region"], ["Territory"],
             ["Location"], ["Status"], ["From Date", "06-10-2026"], ["To Date", "09-10-2026"]]
    # Rows 10 and 11 are blank, as in the real export; openpyxl writes no row for an empty list, so pad them.
    files["EMPOWERMENT REPORT 06.10.2026 TO 09.10.2026.xlsx"] = xlsx_bytes(
        "EmpowermentReport", title + [[None], [None]], [c["SourceHeader"] for c in s022],
        family_rows("S022", s022, WINDOW[:2], "d-M-yyyy", job_base=201))

    s039 = raw_columns(spec["S039"])
    gprc_headers = []
    for column in s039:
        gprc_headers += ["Value", "UCP Value"] if column["SourceHeader"] == "UCP Amount" else [column["SourceHeader"]]
    gprc_rows = [row[:-1] + ["10.00", row[-1]] if s039[-1]["SourceHeader"] == "UCP Amount" else row
                 for row in family_rows("S039", s039, WINDOW[:2], None, job_base=221)]
    if s039[-1]["SourceHeader"] != "UCP Amount":
        raise SystemExit("S039's last column is no longer UCP Amount; update the GPRC CLAIM layout")
    files["GPRC CLAIM 06.10.2026 TO 09.10.2026.xlsx"] = xlsx_bytes("GPRC Claims Report", [], gprc_headers, gprc_rows)
    return files


def check(out, spec, files):
    import openpyxl
    for name in files:
        path = out / name
        assert path.read_bytes() == files[name], name
        if name.endswith(".csv"):
            encoding = "cp1252" if name.startswith("RENVENUE") else "utf-8"
            header = next(csv.reader(io.StringIO(path.read_bytes().decode(encoding))))
        else:
            book = openpyxl.load_workbook(path, read_only=True)
            rows = [r for r in book.worksheets[0].iter_rows(values_only=True)]
            header = [v for v in max(rows, key=lambda r: sum(v is not None for v in r)) if v is not None]
            book.close()
        assert len(header) == len(set(header)), name
    # Privacy: no run of 10 or more digits anywhere in a CSV.
    for name in files:
        if name.endswith(".csv"):
            assert not re.search(rb"\d{10,}", files[name]), name


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--spec", default=str(DEFAULT_SPEC))
    parser.add_argument("--out", default=str(DEFAULT_OUT))
    args = parser.parse_args()
    spec = load_spec(args.spec)
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    files = build(spec)
    for name, data in files.items():
        (out / name).write_bytes(data)
    check(out, spec, files)
    print(f"{out}: {len(files)} raw Service fixtures")


if __name__ == "__main__":
    main()
