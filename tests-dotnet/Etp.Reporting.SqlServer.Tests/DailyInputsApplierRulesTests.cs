using Etp.Reporting.Import.DailyInputs;
using Etp.Reporting.Infrastructure.SqlServer;
using Stored = Etp.Reporting.Infrastructure.SqlServer.SqlServerDailyInputsApplier.StoredInput;
using Definition = Etp.Reporting.Infrastructure.SqlServer.SqlServerDailyInputsApplier.InputDefinition;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// The database-free rules of the daily-inputs applier (1101-DAILY-INPUTS-DESIGN.md section 3): what counts as a
/// change, the definition checks that refuse a whole file, the change reason the history keeps, and the permission
/// check that runs before any connection is opened.
/// </summary>
public sealed class DailyInputsApplierRulesTests
{
    private static readonly DateOnly Day = new(2026, 10, 10);

    [Fact]
    public void A_value_is_changed_only_when_no_row_exists_or_it_differs_at_four_decimals_or_as_trimmed_text()
    {
        Assert.True(SqlServerDailyInputsApplier.IsChanged(Money(100m), null));
        Assert.False(SqlServerDailyInputsApplier.IsChanged(Money(100m), new Stored(100.0000m, null)));
        Assert.False(SqlServerDailyInputsApplier.IsChanged(Money(100.00001m), new Stored(100m, null)));
        Assert.True(SqlServerDailyInputsApplier.IsChanged(Money(100.0001m), new Stored(100m, null)));
        Assert.True(SqlServerDailyInputsApplier.IsChanged(Money(-5m), new Stored(5m, null)));
        Assert.True(SqlServerDailyInputsApplier.IsChanged(Money(1m), new Stored(null, "1")));

        Assert.False(SqlServerDailyInputsApplier.IsChanged(Text("Rain"), new Stored(null, " Rain ")));
        Assert.True(SqlServerDailyInputsApplier.IsChanged(Text("Rain"), new Stored(null, "rain")));
        Assert.True(SqlServerDailyInputsApplier.IsChanged(Text("Rain"), new Stored(null, null)));
    }

    [Fact]
    public void Every_bad_row_is_reported_and_none_is_written()
    {
        var definitions = new Dictionary<string, Definition>(StringComparer.OrdinalIgnoreCase)
        {
            ["WALK_INS"] = new("WALK_INS", "Quantity", "Store", true),
            ["EXPENSES"] = new("EXPENSES", "Money", "Cash", true),
            ["SALES_TARGET"] = new("SALES_TARGET", "Money", "Store", false),
            ["SERVICE_CASH"] = new("SERVICE_CASH", "Money", "Service", true),
            ["DISPLAY_STOCK"] = new("DISPLAY_STOCK", "Quantity", "Stock", true)
        };
        var document = Document(DailyInputsKind.Retail,
            new(7, "WALK_INS", DailyInputValueKind.Quantity, 12m, null),
            new(8, "EXPENSES", DailyInputValueKind.Quantity, 3m, null),
            new(9, "SALES_TARGET", DailyInputValueKind.Money, 1000m, null),
            new(10, "SERVICE_CASH", DailyInputValueKind.Money, 50m, null),
            new(11, "DISPLAY_STOCK", DailyInputValueKind.Quantity, 4m, null),
            new(12, "NOT_A_FIELD", DailyInputValueKind.Money, 1m, null));

        var errors = SqlServerDailyInputsApplier.Validate(document, definitions);

        Assert.Equal(new (int?, string?, string)[]
        {
            (8, "EXPENSES", "DAILY_INPUTS_FIELD_KIND_MISMATCH"),
            (9, "SALES_TARGET", "DAILY_INPUTS_FIELD_INACTIVE"),
            (10, "SERVICE_CASH", "DAILY_INPUTS_FIELD_NOT_FOR_SHEET"),
            (11, "DISPLAY_STOCK", "DAILY_INPUTS_FIELD_NOT_FOR_SHEET"),
            (12, "NOT_A_FIELD", "DAILY_INPUTS_FIELD_INACTIVE")
        }, errors.Select(error => (error.RowNumber, error.FieldCode, error.Code)));
        // Messages hold no value (they reach History and the register).
        Assert.All(errors, error => Assert.DoesNotContain("1000", error.Message, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(DailyInputsKind.Retail, "Store", true)]
    [InlineData(DailyInputsKind.Retail, "Cash", true)]
    [InlineData(DailyInputsKind.Retail, "Service", false)]
    [InlineData(DailyInputsKind.Retail, "Stock", false)]
    [InlineData(DailyInputsKind.Service, "Service", true)]
    [InlineData(DailyInputsKind.Service, "Cash", false)]
    [InlineData(DailyInputsKind.Service, "Store", false)]
    public void A_sheet_type_carries_only_its_own_fields(DailyInputsKind kind, string appliesTo, bool belongs) =>
        Assert.Equal(belongs, SqlServerDailyInputsApplier.BelongsTo(kind, appliesTo));

    [Fact]
    public void The_change_reason_names_the_file_its_short_hash_and_who_filled_it_and_fits_the_column()
    {
        var document = Document(DailyInputsKind.Retail) with { FileName = "ETP-Daily-Inputs-HEMW-RETAIL-2026-10-10.xlsx", FilledBy = " Staff A " };
        Assert.Equal("Daily inputs file ETP-Daily-Inputs-HEMW-RETAIL-2026-10-10.xlsx (0123456789ab); filled by Staff A",
            SqlServerDailyInputsApplier.Reason(document));
        Assert.Equal("Daily inputs file ETP-Daily-Inputs-HEMW-RETAIL-2026-10-10.xlsx (0123456789ab)",
            SqlServerDailyInputsApplier.Reason(document with { FilledBy = null }));
        var longName = document with { FileName = new string('x', 400) + ".xlsx", FilledBy = new string('y', 100) };
        Assert.Equal(SqlServerDailyInputsApplier.ReasonLength, SqlServerDailyInputsApplier.Reason(longName).Length);
    }

    [Theory]
    [InlineData(ApplicationRole.Viewer, true)]
    [InlineData(ApplicationRole.None, false)]
    [InlineData(ApplicationRole.StoreManager, false)]
    public async Task Without_import_permission_the_file_is_refused_before_any_connection(ApplicationRole role, bool active)
    {
        // An unreachable server: a Viewer or an inactive account is refused before the applier opens a connection.
        var applier = new SqlServerDailyInputsApplier("Server=.;Database=etp_never_opened;Integrated Security=true;TrustServerCertificate=true",
            _ => Task.FromResult(new ApplicationAccess("TEST\\user", "Test", role, active)));

        var result = await applier.ApplyAsync(Document(DailyInputsKind.Retail, new(7, "WALK_INS", DailyInputValueKind.Quantity, 1m, null)), "TEST\\user");

        Assert.Equal(("Failed", "DAILY_INPUTS_NO_PERMISSION", 0), (result.Outcome, result.FailureCode, result.Changed));
        Assert.Equal("DAILY_INPUTS_NO_PERMISSION", Assert.Single(result.Errors).Code);
    }

    private static DailyInputValue Money(decimal value) => new(7, "OPENING_CASH", DailyInputValueKind.Money, value, null);
    private static DailyInputValue Text(string value) => new(7, "OPERATIONAL_REMARK", DailyInputValueKind.Text, null, value);

    private static DailyInputsDocument Document(DailyInputsKind kind, params DailyInputValue[] values) =>
        new("ETP-Daily-Inputs-HEMW-RETAIL-2026-10-10.xlsx", "0123456789abcdef" + new string('0', 48), "ETP-DAILY-INPUTS-1", kind,
            "HEMW", Day, null, values);
}
