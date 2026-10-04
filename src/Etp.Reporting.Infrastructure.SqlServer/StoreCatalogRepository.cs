using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>
/// A store row. <paramref name="BusinessUnitCode"/> is NULL for every Retail shop (the two seeded shops and
/// any shop the Owner adds); the Service Centre store AW330 belongs to the SERVICE unit (0048).
/// </summary>
public sealed record StoreCatalogEntry(string Code, string Name, bool IsActive, string? BusinessUnitCode = null)
{
    /// <summary>A Service Centre store is never a shop: no packs, no DSR, no store picker.</summary>
    public bool IsServiceCentre => ServiceCentreStores.IsServiceUnit(BusinessUnitCode);
}

/// <summary>
/// Service interim (decision 15, 3 Oct 2026). Migration 0048 section A seeds AW330 inactive under
/// the SERVICE business unit, and trigger trg_stores_service_unit_inactive refuses making any
/// SERVICE-unit store active (SQL error 51900) or moving it out of the SERVICE unit, which would make
/// it a Retail shop store (SQL error 51904). An active Service store would break the combined
/// pack date, the combined pack and the DSR/evening store lists, which all read is_active = 1.
/// </summary>
public static class ServiceCentreStores
{
    public const string ServiceBusinessUnitCode = "SERVICE";
    public const int ActivationRefusedSqlError = 51900;
    public const int UnitMoveRefusedSqlError = 51904;
    public const string KindLabel = "Service, not a shop store";
    public const string ActiveToggleLockedToolTip =
        "This is the Service Centre store. It is not a shop store, so it stays inactive.";
    public const string ActivationRefusedMessage =
        "A Service Centre store is not a shop store and cannot be made active. Nothing was changed.";
    public const string UnitMoveRefusedMessage =
        "A Service Centre store must stay in the Service Centre business unit and cannot become a shop store. Nothing was changed.";

    public static bool IsServiceUnit(string? businessUnitCode) =>
        string.Equals(businessUnitCode?.Trim(), ServiceBusinessUnitCode, StringComparison.OrdinalIgnoreCase);

    /// <summary>"Service Centre (AW330)": the name shown wherever a Service store code appears.</summary>
    public static string Label(string code) => $"Service Centre ({code})";

    /// <summary>True when the SQL failure is the 0048 trigger refusing to activate a Service store.</summary>
    public static bool IsActivationRefusal(SqlException exception) => HasError(exception, ActivationRefusedSqlError);

    /// <summary>True when the SQL failure is the 0048 trigger refusing to move a Service store out of the SERVICE unit.</summary>
    public static bool IsUnitMoveRefusal(SqlException exception) => HasError(exception, UnitMoveRefusedSqlError);

    /// <summary>The Owner-facing message for a 0048 trigger refusal (51900 or 51904), or null for any other failure.</summary>
    public static string? DescribeRefusal(SqlException exception) =>
        IsActivationRefusal(exception) ? ActivationRefusedMessage
        : IsUnitMoveRefusal(exception) ? UnitMoveRefusedMessage
        : null;

    private static bool HasError(SqlException exception, int number)
    {
        ArgumentNullException.ThrowIfNull(exception);
        foreach (SqlError error in exception.Errors)
            if (error.Number == number) return true;
        return exception.Number == number;
    }
}

/// <summary>Store identity and presentation come from the same master used by imports.</summary>
public sealed class StoreCatalogRepository(string connectionString)
{
    private readonly string validatedConnectionString = LocalSqlConnectionPolicy.Validate(connectionString);

    // The LEFT JOIN keeps every store; business_unit_id exists since 0001, so this also reads a
    // database from before 0048 (every store then has no unit and is Retail).
    internal const string LoadSql = """
        SELECT s.store_code,s.store_name,s.is_active,u.business_unit_code
        FROM dbo.stores s LEFT JOIN dbo.business_units u ON u.business_unit_id=s.business_unit_id
        ORDER BY s.store_id
        """;

    public async Task<IReadOnlyList<StoreCatalogEntry>> LoadAsync(CancellationToken token = default)
    {
        await using var connection = new SqlConnection(validatedConnectionString);
        await connection.OpenAsync(token);
        await using var command = new SqlCommand(LoadSql, connection);
        await using var reader = await command.ExecuteReaderAsync(token);
        var rows = new List<StoreCatalogEntry>();
        while (await reader.ReadAsync(token))
            rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetBoolean(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        return rows;
    }

    /// <summary>
    /// Active shop stores. A Service store is inactive by the 0048 trigger; it is excluded here
    /// as well so that no Retail list can ever pick it up.
    /// </summary>
    public async Task<string[]> ActiveCodesAsync(CancellationToken token = default) =>
        (await LoadAsync(token)).Where(store => store.IsActive && !store.IsServiceCentre).Select(store => store.Code).ToArray();
}
