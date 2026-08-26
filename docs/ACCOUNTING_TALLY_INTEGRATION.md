# Accounting and Tally Integration

The application prepares accounting; it does not replace Tally or invent accounting policy.

Only a final immutable report generation can supply a batch. Business events require an effective, versioned Owner-approved debit/credit mapping. Missing mappings block saving. Each event creates equal debit and credit lines, signed returns reverse the mapping, and SQL stores batch/report generation, generation number, totals, status, approval, export hash/time and optional Tally reference.

The first delivery is a one-way, reviewable Tally XML file. Owner approval and `Debit = Credit` are mandatory before export. Tally’s official guidance supports file-based XML transaction import, requires prerequisite masters and recommends checking import exceptions and transaction reports afterward: <https://help.tallysolutions.com/import-data-from-xml-or-json/>. Tally’s sample guidance also requires balanced debit/credit totals and `YYYYMMDD` dates: <https://help.tallysolutions.com/sample-xml/>.

The company name and exact production ledger/tax mappings remain Owner/Tally configuration. A later local HTTP send may use Tally’s documented XML interface, but only after target-company confirmation and response validation; reporting facts remain authoritative in SQL Server.

