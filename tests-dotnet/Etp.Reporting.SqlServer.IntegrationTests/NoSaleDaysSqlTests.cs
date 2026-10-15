using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Application.NoSaleDays;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.Connections;
using Etp.Reporting.Infrastructure.SqlServer.HealthCheck;
using Etp.Reporting.Infrastructure.SqlServer.NoSaleDays;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.12 lane N1, Appendix B test 3 (cross-layer, C# meets SQL): a store-day is marked as a no-sale day through the C# service
/// and dbo.mark_no_sale_day; the readiness query then asks only for its targets; DATA-01 is OK with the count; the day
/// finalises and locks through the existing workflow; the DSR reads "No sales (marked by ...)"; the lock protects the mark;
/// after a reopen the mark is withdrawn, with history and both audit types. Refusals (51980-51989) reach C# as plain
/// sentences: sales held, a non-zero entry, a LOCKED day, a future date. A Viewer gets 230 on the reason and 229 on EXECUTE
/// (non-pooled connections, REVERT in the batch). The test login is the Owner (sysadmin), as in every SQL test.
/// Synthetic store codes only (ZN0xx); no names, no phones.
/// </summary>
public sealed class NoSaleDaysSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    private static readonly DateOnly Day = new(2026, 8, 21);

    [SqlFact]
    public async Task Mark_waives_readiness_finalises_shows_on_the_dsr_is_locked_and_is_withdrawn_after_a_reopen()
    {
        await AddStoreAsync("ZN001");
        await database.ExecuteAsync("""
            INSERT dbo.monthly_targets(store_code,target_month,target_sales,modified_by) VALUES('ZN001','20260801',100000,'test');
            INSERT dbo.staff_sales_targets(store_code,cro_number,period_start,period_end,target_sales,entered_by,modified_by,change_reason)
             VALUES('ZN001','C1','20260801','20260831',5000,'test','test','Synthetic target');
            """);
        var service = new SqlServerNoSaleDayService(new TestConnections(database.ConnectionString));
        var readinessQuery = new SqlServerDailyReadinessQuery(database.ConnectionString);

        var before = await readinessQuery.LoadAsync(new(Day, ["ZN001"], false));
        Assert.Contains(before.Missing, x => x.Code == "R025");
        Assert.Empty(before.NoSaleStoreCodes);

        await service.MarkAsync("ZN001", Day, NoSaleDayText.ClosedHoliday, "Synthetic holiday");

        var mark = Assert.Single(await service.LoadAsync(Day), x => x.StoreCode == "ZN001");
        Assert.Equal((NoSaleDayText.ClosedHoliday, "Synthetic holiday"), (mark.ReasonCode, mark.Reason));
        Assert.False(string.IsNullOrWhiteSpace(mark.MarkedBy));
        Assert.Equal(Day, mark.BusinessDate);

        // Readiness: exports and entries waived, targets set, so nothing is missing; DATA-01 is OK and counts the mark.
        var readiness = await readinessQuery.LoadAsync(new(Day, ["ZN001"], false));
        Assert.True(readiness.IsComplete, string.Join("; ", readiness.Missing.Select(x => x.Message)));
        Assert.Equal(["ZN001"], readiness.NoSaleStoreCodes);
        var data01 = DataProbes.EvaluateCompleteness(Day, readiness, TimeSpan.Zero);
        Assert.Equal(HealthOutcome.Pass, data01.Outcome);
        Assert.EndsWith("(1 store-day marked no-sale)", data01.Explanation, StringComparison.Ordinal);

        // Finalise through the existing workflow: the day needs no source report and no entry, and locks as any other day.
        var workflow = new DailyReportingWorkflowRepository(database.ConnectionString);
        var snapshot = await workflow.LoadAsync("ZN001", Day);
        Assert.True(snapshot.CanFinalise);
        Assert.Empty(snapshot.MissingReports);
        Assert.Empty(snapshot.MissingRequiredInputs);
        Assert.Equal(DailyReportingWorkflowRepository.NoSaleDayStatusMessage, snapshot.StatusMessage);
        Assert.True(snapshot.IsNoSaleDay);
        // The pack generates; a section that would be blocked by the missing sources is "not run" on a no-sale day, so Close
        // day (which refuses only on Blocked sections) lets the day be finalised.
        var pack = await new DailyReportingPackService(database.ConnectionString).GenerateAsync("ZN001", Day, "test");
        Assert.DoesNotContain(pack.Sections, x => x.Status == Etp.Reporting.Reporting.ReconciliationStatus.Blocked);
        Assert.Contains(pack.Sections, x => x.Message.StartsWith(DailyReportingPackService.NoSaleDaySectionPrefix, StringComparison.Ordinal));
        await workflow.FinaliseAsync("ZN001", Day, "test", hasBlockingReconciliationExceptions: false);
        Assert.Equal("LOCKED", await ScalarAsync("SELECT status FROM dbo.daily_reporting_days WHERE store_code='ZN001' AND business_date='20260821'"));

        // The DSR reads the line instead of "not imported", with a real zero for the day.
        var dsr = await new OperationalReportRepository(database.ConnectionString).LoadDailySalesReportDocumentAsync(Day);
        var line = dsr.NoSaleLine("ZN001");
        Assert.NotNull(line);
        Assert.StartsWith("No sales (marked by ", line, StringComparison.Ordinal);
        Assert.Equal(0m, dsr.Stores.Single(x => x.StoreCode == "ZN001").Periods[0].TyValue);

        // The lock protects the mark: no withdraw on a finalised day (51983), and the table refuses a direct change too.
        var locked = await Assert.ThrowsAsync<NoSaleDayRefusedException>(() => service.WithdrawAsync("ZN001", Day, "Changed my mind"));
        Assert.Equal("This business day is finalised. Reopen it before marking or withdrawing a no-sale day.", locked.Message);
        Assert.Equal(51983, await ErrorNumberAsync("UPDATE dbo.no_sale_days SET withdrawn_by=N'x',withdrawn_utc=SYSUTCDATETIME(),withdraw_reason=N'x' WHERE store_code='ZN001'"));

        // Reopen (Owner) and withdraw: the day asks for its exports and entries again.
        await workflow.ReopenAsync("ZN001", Day, "test", "Synthetic reopen");
        await service.WithdrawAsync("ZN001", Day, "Sales file arrived");
        Assert.DoesNotContain(await service.LoadAsync(Day), x => x.StoreCode == "ZN001");
        var after = await readinessQuery.LoadAsync(new(Day, ["ZN001"], false));
        Assert.Contains(after.Missing, x => x.Code == "R025");
        Assert.Contains(after.Missing, x => x.Code == "WALK_INS");
        Assert.Empty(after.NoSaleStoreCodes);

        Assert.Equal("MARKED,WITHDRAWN", await ScalarAsync(
            "SELECT STRING_AGG(action,',') WITHIN GROUP (ORDER BY history_id) FROM dbo.no_sale_day_history WHERE store_code='ZN001'"));
        Assert.True(Convert.ToInt32(await ScalarAsync("SELECT COUNT(*) FROM dbo.operational_audit WHERE event_type='NoSaleDayMarked'"), System.Globalization.CultureInfo.InvariantCulture) >= 1);
        Assert.True(Convert.ToInt32(await ScalarAsync("SELECT COUNT(*) FROM dbo.operational_audit WHERE event_type='NoSaleDayWithdrawn'"), System.Globalization.CultureInfo.InvariantCulture) >= 1);

        // A withdrawn day can be marked again (the row is reused) and the history keeps all three changes.
        await service.MarkAsync("ZN001", Day, NoSaleDayText.ClosedOther, "Synthetic closure");
        Assert.Equal("MARKED,WITHDRAWN,MARKED", await ScalarAsync(
            "SELECT STRING_AGG(action,',') WITHIN GROUP (ORDER BY history_id) FROM dbo.no_sale_day_history WHERE store_code='ZN001'"));
        // History is append-only and a mark is never deleted.
        Assert.Equal(51989, await ErrorNumberAsync("DELETE dbo.no_sale_days WHERE store_code='ZN001'"));
        Assert.Equal(51989, await ErrorNumberAsync("UPDATE dbo.no_sale_day_history SET reason=N'x' WHERE store_code='ZN001'"));
    }

    /// <summary>
    /// Review M-1 (cross-layer): the sales check of dbo.mark_no_sale_day runs only when the mark is made. A sales file imported
    /// for the store-day afterwards (here the real R025 fixture through the real importer) must void the mark everywhere:
    /// readiness asks for the files and entries again, Close day cannot finalise (and says the mark no longer applies), the pack
    /// does not turn sections into "No-sale day, nothing to reconcile", the DSR prints no "No sales" line and DATA-01 does not
    /// count it. The mark can still be withdrawn. Two fresh databases: the first only tells which store-day the fixture holds.
    /// </summary>
    [SqlFact]
    public async Task A_sales_file_imported_after_the_mark_voids_it_and_finalise_needs_the_normal_checks()
    {
        var path = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample"), "R025_*.xlsx").Single();
        string store;
        DateOnly day;
        var probe = new SqlDatabaseFixture();
        try
        {
            await probe.InitializeAsync();
            var first = Assert.Single((await new FolderImportService(new SqlServerImportPersistenceUseCase(probe.ConnectionString))
                .RunAsync(path, new("Synthetic Owner"))).Files);
            Assert.Equal("Imported", first.Status);
            store = Convert.ToString(await probe.ExecuteAsync("SELECT TOP(1) store_code FROM dbo.sales_invoices ORDER BY transaction_date,store_code"),
                System.Globalization.CultureInfo.InvariantCulture)!;
            day = DateOnly.FromDateTime(Convert.ToDateTime(await probe.ExecuteAsync(
                $"SELECT MIN(transaction_date) FROM dbo.sales_invoices WHERE store_code='{store}'"), System.Globalization.CultureInfo.InvariantCulture));
        }
        finally { await probe.DisposeAsync(); }

        var fresh = new SqlDatabaseFixture();
        try
        {
            await fresh.InitializeAsync();
            await fresh.ExecuteAsync(
                $"IF NOT EXISTS(SELECT 1 FROM dbo.stores WHERE store_code='{store}') INSERT dbo.stores(store_code,store_name) VALUES('{store}',N'Synthetic no-sale store');");
            var service = new SqlServerNoSaleDayService(new TestConnections(fresh.ConnectionString));
            var readinessQuery = new SqlServerDailyReadinessQuery(fresh.ConnectionString);
            var workflow = new DailyReportingWorkflowRepository(fresh.ConnectionString);

            await service.MarkAsync(store, day, NoSaleDayText.ClosedHoliday, "Synthetic holiday");
            Assert.True((await workflow.LoadAsync(store, day)).IsNoSaleDay);
            Assert.False(Assert.Single(await service.LoadAsync(day), x => x.StoreCode == store).NoLongerApplies);

            // The shop's sales file for that day arrives after the mark (late file, next-morning automation).
            var imported = Assert.Single((await new FolderImportService(new SqlServerImportPersistenceUseCase(fresh.ConnectionString))
                .RunAsync(path, new("Synthetic Owner"))).Files);
            Assert.Equal("Imported", imported.Status);
            Assert.True(Convert.ToInt32(await fresh.ExecuteAsync(
                $"SELECT COUNT(*) FROM dbo.sales_invoices WHERE store_code='{store}' AND transaction_date='{day:yyyyMMdd}'"),
                System.Globalization.CultureInfo.InvariantCulture) > 0);

            // The mark is still there (only a person withdraws it) but no longer applies.
            var mark = Assert.Single(await service.LoadAsync(day), x => x.StoreCode == store);
            Assert.True(mark.NoLongerApplies);

            // Readiness and DATA-01: the day asks for its entries again and is not counted as a no-sale day.
            var readiness = await readinessQuery.LoadAsync(new(day, [store], false));
            Assert.Empty(readiness.NoSaleStoreCodes);
            Assert.Contains(readiness.Missing, x => x.StoreCode == store && x.Code == "WALK_INS");
            var data01 = DataProbes.EvaluateCompleteness(day, readiness, TimeSpan.Zero);
            Assert.DoesNotContain("no-sale", data01.Explanation, StringComparison.Ordinal);

            // Close day: the normal checks stand, the Owner is told the mark no longer applies, and Finalise is refused.
            var snapshot = await workflow.LoadAsync(store, day);
            Assert.False(snapshot.IsNoSaleDay);
            Assert.True(snapshot.NoSaleMarkNoLongerApplies);
            Assert.False(snapshot.CanFinalise);
            Assert.Contains("WALK_INS", snapshot.MissingRequiredInputs);
            Assert.StartsWith(NoSaleDayText.NoLongerAppliesSentence, snapshot.StatusMessage, StringComparison.Ordinal);
            var pack = await new DailyReportingPackService(fresh.ConnectionString).GenerateAsync(store, day, "test");
            Assert.DoesNotContain(pack.Sections, x => x.Message.StartsWith(DailyReportingPackService.NoSaleDaySectionPrefix, StringComparison.Ordinal));
            await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.FinaliseAsync(store, day, "test", hasBlockingReconciliationExceptions: false));
            Assert.NotEqual("LOCKED", Convert.ToString(await fresh.ExecuteAsync(
                $"SELECT status FROM dbo.daily_reporting_days WHERE store_code='{store}' AND business_date='{day:yyyyMMdd}'"),
                System.Globalization.CultureInfo.InvariantCulture));

            // The DSR shows the sales and no "No sales (marked by ...)" line.
            var dsr = await new OperationalReportRepository(fresh.ConnectionString).LoadDailySalesReportDocumentAsync(day);
            Assert.Null(dsr.NoSaleLine(store));

            // The Owner can withdraw the stale mark; the day then reads as an ordinary day.
            await service.WithdrawAsync(store, day, "Sales file arrived");
            Assert.DoesNotContain(await service.LoadAsync(day), x => x.StoreCode == store);
            Assert.False((await workflow.LoadAsync(store, day)).NoSaleMarkNoLongerApplies);
        }
        finally { await fresh.DisposeAsync(); }
    }

    [SqlFact]
    public async Task Mark_is_refused_in_plain_words_for_sales_held_a_non_zero_entry_a_locked_day_a_future_date_and_twice()
    {
        foreach (var store in new[] { "ZN002", "ZN003", "ZN004", "ZN005", "ZN006" }) await AddStoreAsync(store);
        await database.ExecuteAsync("""
            INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES('ZN002','SYN-1',2027,'20260821');
            INSERT dbo.manual_operational_inputs(store_code,business_date,field_code,numeric_value,entered_by,modified_by,change_reason)
             VALUES('ZN003','20260821','WALK_INS',5,'test','test','Synthetic entry');
            INSERT dbo.manual_operational_inputs(store_code,business_date,field_code,numeric_value,entered_by,modified_by,change_reason)
             VALUES('ZN005','20260821','WALK_INS',0,'test','test','A zero entry does not stop the mark');
            INSERT dbo.daily_reporting_days(store_code,business_date,status,finalised_by,finalised_utc) VALUES('ZN004','20260821','LOCKED','test',SYSUTCDATETIME());
            INSERT dbo.stores(store_code,store_name,is_active) VALUES('ZN008',N'Synthetic inactive store',0);
            """);
        var service = new SqlServerNoSaleDayService(new TestConnections(database.ConnectionString));

        async Task<string> Refused(Func<Task> action) => (await Assert.ThrowsAsync<NoSaleDayRefusedException>(action)).Message;

        Assert.Equal("ETP already holds sales for this shop on this day, so it cannot be marked as a no-sale day.",
            await Refused(() => service.MarkAsync("ZN002", Day, NoSaleDayText.OpenNoSale, "Synthetic")));
        Assert.Equal("A daily entry other than zero is already saved for this shop on this day, so it cannot be marked as a no-sale day.",
            await Refused(() => service.MarkAsync("ZN003", Day, NoSaleDayText.OpenNoSale, "Synthetic")));
        Assert.Equal("This business day is finalised. Reopen it before marking or withdrawing a no-sale day.",
            await Refused(() => service.MarkAsync("ZN004", Day, NoSaleDayText.ClosedHoliday, "Synthetic")));
        Assert.Equal("A no-sale day cannot be marked for a date after today.",
            await Refused(() => service.MarkAsync("ZN006", DateOnly.FromDateTime(DateTime.Today).AddDays(2), NoSaleDayText.ClosedHoliday, "Synthetic")));
        Assert.Equal("Choose a shop that ETP knows and a business date.",
            await Refused(() => service.MarkAsync("ZN999", Day, NoSaleDayText.ClosedHoliday, "Synthetic")));
        // Review m-2 (1.12.1 P4: its own sentence, THROW 52010): an inactive Retail store is refused and no OPEN day row
        // is created for it.
        Assert.Equal("This shop is inactive in ETP, so a no-sale day cannot be marked for it. Ask the Owner to enable it in Settings > Stores first.",
            await Refused(() => service.MarkAsync("ZN008", Day, NoSaleDayText.ClosedHoliday, "Synthetic")));
        Assert.Equal("This day is not marked as a no-sale day, so there is nothing to withdraw.",
            await Refused(() => service.WithdrawAsync("ZN006", Day, "Synthetic")));

        // A zero entry does not stop the mark; a second mark is refused.
        await service.MarkAsync("ZN005", Day, NoSaleDayText.OpenNoSale, "Synthetic");
        Assert.Equal("This day is already marked as a no-sale day.",
            await Refused(() => service.MarkAsync("ZN005", Day, NoSaleDayText.OpenNoSale, "Synthetic")));

        // Nothing was written for the refused store-days.
        Assert.Equal(0, Convert.ToInt32(await ScalarAsync("SELECT COUNT(*) FROM dbo.no_sale_days WHERE store_code IN('ZN002','ZN003','ZN004','ZN006','ZN008')"),
            System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(0, Convert.ToInt32(await ScalarAsync("SELECT COUNT(*) FROM dbo.daily_reporting_days WHERE store_code='ZN008'"),
            System.Globalization.CultureInfo.InvariantCulture));
    }

    [SqlFact]
    public async Task A_viewer_reads_the_marks_without_the_reason_and_cannot_mark_while_a_store_manager_can()
    {
        await AddStoreAsync("ZN007");
        await database.ExecuteAsync("""
            IF DATABASE_PRINCIPAL_ID(N'nsd_viewer') IS NULL BEGIN CREATE USER nsd_viewer WITHOUT LOGIN; ALTER ROLE etp_viewer ADD MEMBER nsd_viewer; END;
            IF DATABASE_PRINCIPAL_ID(N'nsd_manager') IS NULL BEGIN CREATE USER nsd_manager WITHOUT LOGIN; ALTER ROLE etp_store_manager ADD MEMBER nsd_manager; END;
            """);
        await new SqlServerNoSaleDayService(new TestConnections(database.ConnectionString))
            .MarkAsync("ZN007", Day, NoSaleDayText.ClosedOther, "Synthetic private note");

        // 230: a column denied; 229: the object (EXECUTE or the table write) denied.
        Assert.Equal(230, await ProbeAsync("nsd_viewer", "SELECT reason FROM dbo.v_no_sale_days_current"));
        Assert.Equal(230, await ProbeAsync("nsd_viewer", "SELECT reason FROM dbo.no_sale_days"));
        Assert.Equal(0, await ProbeAsync("nsd_viewer", "SELECT store_code,business_date,marked_by FROM dbo.v_no_sale_days_current"));
        Assert.Equal(229, await ProbeAsync("nsd_viewer", "EXEC dbo.mark_no_sale_day 'ZN007','20260820','CLOSED_OTHER',N'x'"));
        Assert.Equal(229, await ProbeAsync("nsd_viewer", "EXEC dbo.withdraw_no_sale_day 'ZN007','20260821',N'x'"));
        Assert.Equal(0, await ProbeAsync("nsd_manager", "SELECT reason FROM dbo.v_no_sale_days_current"));
        Assert.Equal(229, await ProbeAsync("nsd_manager", "INSERT dbo.no_sale_days(store_code,business_date,reason_code,reason) VALUES('ZN007','20260820','CLOSED_OTHER',N'x')"));
        // The Store Manager marks through the procedure (ownership chain); the probe rolls it back.
        Assert.Equal(0, await ProbeAsync("nsd_manager", "EXEC dbo.mark_no_sale_day 'ZN007','20260820','CLOSED_OTHER',N'Synthetic'"));

        // The service's own read as the Viewer: the mark comes back, the reason does not.
        var reason = await ScalarAsync($"""
            SET NOCOUNT ON;
            DECLARE @rows TABLE(store_code varchar(30),business_date date,reason_code varchar(30),reason nvarchar(500),marked_by nvarchar(100),marked_utc datetime2(3),has_day_data bit);
            EXECUTE AS USER='nsd_viewer';
            BEGIN TRY
              INSERT @rows EXEC sys.sp_executesql N'{SqlServerNoSaleDayService.LoadSql.Replace("'", "''", StringComparison.Ordinal)}',N'@date date',@date='20260821';
            END TRY BEGIN CATCH REVERT; THROW; END CATCH;
            REVERT;
            SELECT COALESCE(reason,N'(hidden)') FROM @rows WHERE store_code='ZN007';
            """, pooling: false);
        Assert.Equal("(hidden)", reason);
    }

    private Task AddStoreAsync(string store) => database.ExecuteAsync(
        $"IF NOT EXISTS(SELECT 1 FROM dbo.stores WHERE store_code='{store}') INSERT dbo.stores(store_code,store_name) VALUES('{store}',N'Synthetic no-sale store');");

    private async Task<object?> ScalarAsync(string sql, bool pooling = true)
    {
        await using var connection = new SqlConnection(TestSqlConnections.ForDatabase(database.Name, pooling));
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    /// <summary>Runs a statement as the test Owner and returns the SQL error number (0 when it succeeded); always rolled back.</summary>
    private async Task<int> ErrorNumberAsync(string sql)
    {
        var escaped = sql.Replace("'", "''", StringComparison.Ordinal);
        return Convert.ToInt32(await ScalarAsync($"""
            SET NOCOUNT ON; DECLARE @r int=0;
            BEGIN TRY BEGIN TRANSACTION; EXEC sys.sp_executesql N'{escaped}'; ROLLBACK; END TRY
            BEGIN CATCH SET @r=ERROR_NUMBER(); IF @@TRANCOUNT>0 ROLLBACK; END CATCH;
            SELECT @r;
            """), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Runs a statement as a database user on a non-pooled connection, reverting in the same batch; 0 or the error number.</summary>
    private async Task<int> ProbeAsync(string user, string sql)
    {
        var escaped = sql.Replace("'", "''", StringComparison.Ordinal);
        var batch = $"""
            SET NOCOUNT ON; DECLARE @r int=0;
            EXECUTE AS USER='{user}';
            BEGIN TRY BEGIN TRANSACTION; EXEC sys.sp_executesql N'{escaped}'; IF @@TRANCOUNT>0 ROLLBACK; END TRY
            BEGIN CATCH SET @r=ERROR_NUMBER(); IF @@TRANCOUNT>0 ROLLBACK; END CATCH;
            REVERT;
            SELECT @r AS probe_result;
            """;
        await using var connection = new SqlConnection(TestSqlConnections.ForDatabase(database.Name, pooling: false));
        await connection.OpenAsync();
        await using var command = new SqlCommand(batch, connection) { CommandTimeout = 120 };
        int? result = null;
        await using (var reader = await command.ExecuteReaderAsync())
        {
            do
            {
                if (reader.FieldCount == 1 && reader.GetName(0) == "probe_result" && await reader.ReadAsync()) result = reader.GetInt32(0);
            } while (await reader.NextResultAsync());
        }
        Assert.NotNull(result);
        return result.Value;
    }

    /// <summary>The 1.12 connection factory contract (A1) over the test database.</summary>
    private sealed class TestConnections(string connectionString) : IEtpConnectionFactory
    {
        public string DatabaseName => new SqlConnectionStringBuilder(connectionString).InitialCatalog;

        public SqlConnection Create(EtpConnectionPurpose purpose = EtpConnectionPurpose.Default) => new(connectionString);

        public async Task<SqlConnection> OpenAsync(EtpConnectionPurpose purpose = EtpConnectionPurpose.Default, CancellationToken ct = default)
        {
            var connection = Create(purpose);
            try { await connection.OpenAsync(ct); return connection; }
            catch { await connection.DisposeAsync(); throw; }
        }
    }
}
