using System.Globalization;
using System.IO.Compression;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Etp.Reporting.Application.NoSaleDays;
using Etp.Reporting.Import.DailyInputs;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.NoSaleDays;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed partial class PhaseFiveFullWindowCaptureTests
{
    private sealed partial class SignOffWalk
    {
        // The walk's days. Each scenario has its own store-day so one step cannot spoil another.
        // Every day is in the past: the header date box does not take a future date. Month start (18-19) is October 2026:
        // September's targets are seeded for all three test shops, October's only for WALKA (so Copy asks Replace / Keep).
        private static readonly DateOnly MorningDay = new(2026, 9, 10);      // steps 7-8: WALKA and WALKB ready (marked no-sale), WALKC blocked
        private static readonly DateOnly NoSaleDay = new(2026, 9, 12);       // steps 12-15: WALKC, no sales file
        private static readonly DateOnly EntryDay = new(2026, 9, 13);        // step 15: WALKB has a non-zero daily entry
        private static readonly DateOnly WrongNameDay = new(2026, 9, 14);    // step 16: sheet dated 14 Sep in a file named 15 Sep
        private static readonly DateOnly NoFilledByDay = new(2026, 9, 16);   // step 16: sheet without "Filled by"
        private static readonly DateOnly LockedDay = new(2026, 9, 17);       // step 17: a finalised day gets a corrected sheet
        private static readonly DateOnly MonthStartDay = new(2026, 10, 2);   // steps 18-19: header date; the walk's clock says 3 Oct
        private static readonly DateOnly CollectionsDay = new(2026, 8, 1);   // steps 26-29: the synthetic bank file's deposit day
        private const string SheetStore = "HEMW";                            // the seeded store the daily-sheet tests use

        /// <summary>The synthetic sales fixture's store and first day (step 13b and the refusal of step 15).</summary>
        private string salesStore = "";
        private DateOnly salesDay;

        public async Task SeedAsync()
        {
            Log("Seeding synthetic data");
            await ScalarAsync("""
                INSERT dbo.application_users(windows_identity,display_name,role_code,is_active,modified_by,change_reason)
                 VALUES(N'WALK\BackupOwner',N'Walk backup Owner','OWNER',1,N'Sign-off walk',N'Keep the last-Owner guard while the walk changes roles');
                UPDATE dbo.application_users SET display_name=N'Walk Owner',role_code='OWNER',is_active=1 WHERE windows_identity=SUSER_SNAME();
                INSERT dbo.stores(store_code,store_name,is_active) VALUES('WALKA',N'Walk test shop A',1),('WALKB',N'Walk test shop B',1),('WALKC',N'Walk test shop C',1);
                INSERT dbo.monthly_targets(store_code,target_month,target_sales,modified_by) VALUES
                 ('WALKA','20260901',100000,N'walk'),('WALKB','20260901',90000,N'walk'),('WALKC','20260901',80000,N'walk'),
                 ('WALKA','20261001',99999,N'walk');
                INSERT dbo.staff_sales_targets(store_code,cro_number,period_start,period_end,target_sales,entered_by,modified_by,change_reason) VALUES
                 ('WALKA','C1','20260901','20260930',5000,N'walk',N'walk',N'Synthetic target'),('WALKB','C2','20260901','20260930',5000,N'walk',N'walk',N'Synthetic target'),
                 ('WALKC','C3','20260901','20260930',5000,N'walk',N'walk',N'Synthetic target');
                INSERT dbo.manual_operational_inputs(store_code,business_date,field_code,numeric_value,entered_by,modified_by,change_reason)
                 VALUES('WALKB','20260913','WALK_INS',5,N'walk',N'walk',N'Synthetic entry for the no-sale refusal');
                INSERT dbo.manual_operational_inputs(store_code,business_date,field_code,numeric_value,entered_by,modified_by,change_reason)
                 VALUES('WALKA','20260801','CASH_DEPOSIT',12500,N'walk',N'walk',N'Synthetic deposit'),
                       ('WALKA','20260803','CASH_DEPOSIT',7777,N'walk',N'walk',N'Synthetic deposit');
                INSERT dbo.daily_reporting_days(store_code,business_date,status,finalised_by,finalised_utc)
                 VALUES('WALKA','20260801','LOCKED',N'walk',SYSUTCDATETIME()),('WALKA','20260803','LOCKED',N'walk',SYSUTCDATETIME());
                """);
            var staff = new DataTruthMasterRepository(connectionString);
            foreach (var (store, code, name) in new[] { ("WALKA", "C1", "Walk staff one"), ("WALKB", "C2", "Walk staff two"), ("WALKC", "C3", "Walk staff three") })
                await staff.SaveStaffAsync(new StaffMasterRow(store, code, name, true));

            // Steps 7-8: two stores ready for the Morning page's Finalise (a marked no-sale day with targets needs no files).
            var marks = new SqlServerNoSaleDayService(factory);
            foreach (var store in new[] { "WALKA", "WALKB" })
                await marks.MarkAsync(store, MorningDay, NoSaleDayText.ClosedHoliday, "Synthetic walk: test day for the Morning page");

            // Step 13b and 15: the synthetic R025 sales fixture (sanitised, names replaced).
            var sales = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample"), "R025_*.xlsx").Single();
            var imported = await new FolderImportService(new SqlServerImportPersistenceUseCase(connectionString)).RunAsync(sales, new("Synthetic Owner"));
            if (!imported.Files.Any(file => file.Status == "Imported")) throw new InvalidOperationException("The synthetic sales fixture did not import.");
            salesStore = await TextAsync("SELECT TOP(1) store_code FROM dbo.sales_invoices ORDER BY transaction_date,store_code") ?? "";
            salesDay = DateOnly.FromDateTime(Convert.ToDateTime(await ScalarAsync($"SELECT MIN(transaction_date) FROM dbo.sales_invoices WHERE store_code='{salesStore}'"), English));
            Log($"Synthetic sales: {salesStore} on {salesDay:yyyy-MM-dd}");

            // Step 36: the synthetic Service Centre weeks (as ETP_PHASE5_SERVICE_FIXTURE=week1,week2 does for the help pictures).
            foreach (var week in new[] { "week1", "week2" })
            {
                var summary = await new FolderImportService(new SqlServerImportPersistenceUseCase(connectionString))
                    .RunAsync(Path.Combine(RepositoryRoot(), "tests-dotnet", "fixtures", "service-interim", week), new("Synthetic Owner"));
                if (!summary.Files.Any(file => file.Status == "Imported")) Log($"Service fixture {week} imported nothing.");
            }
        }

        /// <summary>
        /// The real daily-inputs template for <paramref name="store"/> and <paramref name="sheetDay"/>, saved under the name the
        /// template gives <paramref name="nameDay"/> (a different day makes the "wrong date in the file name" case). No
        /// "Filled by" when <paramref name="filledBy"/> is null. Written as ImportFixReapplySqlTests writes its sheets.
        /// </summary>
        private static async Task<string> WriteSheetAsync(string directory, string store, DateOnly sheetDay, DateOnly nameDay, decimal walkIns, string? filledBy)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, DailyInputsTemplate.FileName(store, DailyInputsKind.Retail, nameDay));
            await using var stream = new MemoryStream();
            DailyInputsTemplateWriter.Write(stream, DailyInputsKind.Retail, store, "Synthetic store", sheetDay);
            stream.Position = 0;
            using (var document = SpreadsheetDocument.Open(stream, true))
            {
                var sheet = document.WorkbookPart!.WorksheetParts.Single().Worksheet!;
                if (filledBy is not null) SetSheetValue(sheet, DailyInputsTemplate.FilledByKey, filledBy);
                SetSheetValue(sheet, "WALK_INS", walkIns);
                sheet.Save();
            }
            await File.WriteAllBytesAsync(path, stream.ToArray());
            return path;
        }

        private static void SetSheetValue(Worksheet sheet, string key, object value)
        {
            var row = sheet.Descendants<Row>().Single(r =>
                r.Elements<Cell>().FirstOrDefault(c => c.CellReference?.Value == $"A{r.RowIndex!.Value}")?.InnerText == key);
            var cell = row.Elements<Cell>().Single(c => c.CellReference?.Value == $"C{row.RowIndex!.Value}");
            cell.InlineString = null; cell.CellValue = null; cell.DataType = null;
            if (value is string text)
            {
                cell.DataType = CellValues.InlineString;
                cell.InlineString = new InlineString(new Text(text));
            }
            else cell.CellValue = new CellValue(Convert.ToString(value, CultureInfo.InvariantCulture)!);
        }

        /// <summary>A shop zip as the shop PC sends it (<c>ROLE-yyyy-MM-dd.zip</c>), old enough for the watch folder's wait.</summary>
        private static string ZipShopFile(string sheet, string inbound, string zipName)
        {
            Directory.CreateDirectory(inbound);
            var zip = Path.Combine(inbound, zipName);
            ZipFile.CreateFromDirectory(Path.GetDirectoryName(sheet)!, zip, CompressionLevel.Fastest, includeBaseDirectory: false);
            File.SetLastWriteTimeUtc(zip, DateTime.UtcNow.AddMinutes(-5));
            return zip;
        }
    }
}
