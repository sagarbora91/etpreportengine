using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed record StoreCatalogEntry(string Code, string Name, bool IsActive);

/// <summary>Store identity and presentation come from the same master used by imports.</summary>
public sealed class StoreCatalogRepository(string connectionString)
{
    private readonly string validatedConnectionString = LocalSqlConnectionPolicy.Validate(connectionString);

    public async Task<IReadOnlyList<StoreCatalogEntry>> LoadAsync(CancellationToken token = default)
    {
        await using var connection = new SqlConnection(validatedConnectionString);
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("SELECT store_code,store_name,is_active FROM dbo.stores ORDER BY store_id", connection);
        await using var reader = await command.ExecuteReaderAsync(token);
        var rows = new List<StoreCatalogEntry>();
        while (await reader.ReadAsync(token)) rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetBoolean(2)));
        return rows;
    }

    public async Task<string[]> ActiveCodesAsync(CancellationToken token = default) =>
        (await LoadAsync(token)).Where(store => store.IsActive).Select(store => store.Code).ToArray();
}
