using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed record BrandRowDefinition(int Id, string StoreCode, string Label, int Order, string SourceCodes);
public sealed record SourceBrandEvidence(string Store, string Code, string Name, string Cluster);

public sealed class EveningMasterRepository(string connectionString)
{
    private async Task<SqlConnection> Open(CancellationToken token)
    {
        var c = new SqlConnection(SqlAdapterConnection.RequireWindowsIntegrated(connectionString, nameof(connectionString)));
        try { await c.OpenAsync(token); return c; } catch { await c.DisposeAsync(); throw; }
    }

    public async Task<IReadOnlyList<BrandRowDefinition>> LoadBrandsAsync(CancellationToken token = default)
    {
        await using var c = await Open(token);
        await using var q = new SqlCommand("SELECT r.brand_row_id,r.store_code,r.row_label,r.sort_order,COALESCE(STRING_AGG(CONVERT(nvarchar(max),b.source_brand),N', '),N'') FROM dbo.brand_rows r LEFT JOIN dbo.brand_row_codes b ON b.brand_row_id=r.brand_row_id GROUP BY r.brand_row_id,r.store_code,r.row_label,r.sort_order ORDER BY r.store_code,r.sort_order,r.row_label", c);
        await using var reader = await q.ExecuteReaderAsync(token);
        var rows = new List<BrandRowDefinition>();
        while (await reader.ReadAsync(token)) rows.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetString(4)));
        return rows;
    }

    public async Task SaveBrandAsync(BrandRowDefinition row, CancellationToken token = default)
    {
        if (!(await new Phase2OperationsRepository(connectionString).LoadCurrentAccessAsync(token)).CanAdminister)
            throw new UnauthorizedAccessException("Owner permission is required.");
        if (string.IsNullOrWhiteSpace(row.StoreCode) || row.StoreCode.Length > 30 || string.IsNullOrWhiteSpace(row.Label) || row.Label.Length > 100)
            throw new ArgumentException("Enter a store and brand row label.");
        if(new[]{"Other / unmapped","VOL","VALUE","AUPT","AVPT","RETAIL WALKIN","INVOICE","CONVERSION %","WCC WALKIN","WCC SALES","WDC BILLS"}.Contains(row.Label.Trim(),StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("This label is reserved for a calculated DSR row. Choose a brand label.");
        var codes = row.SourceCodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (codes.Any(x => x.Length > 100)) throw new ArgumentException("Each source brand code must be at most 100 characters.");
        await using var c = await Open(token);
        await using var t = (SqlTransaction)await c.BeginTransactionAsync(token);
        try
        {
            await using var q = new SqlCommand("""
                DECLARE @id int=@requested;
                IF @id=0 BEGIN
                  INSERT dbo.brand_rows(store_code,row_label,sort_order) VALUES(@store,@label,@order);
                  SET @id=SCOPE_IDENTITY();
                END ELSE BEGIN
                  UPDATE dbo.brand_rows SET row_label=@label,sort_order=@order,modified_by=ORIGINAL_LOGIN(),modified_utc=SYSUTCDATETIME()
                  WHERE brand_row_id=@id AND store_code=@store;
                  IF @@ROWCOUNT<>1 THROW 51401,'The brand row no longer exists in this store.',1;
                END;
                DELETE dbo.brand_row_codes WHERE brand_row_id=@id;
                SELECT @id;
                """, c, t);
            q.Parameters.AddWithValue("@requested", row.Id); q.Parameters.AddWithValue("@store", row.StoreCode.Trim().ToUpperInvariant());
            q.Parameters.AddWithValue("@label", row.Label.Trim()); q.Parameters.AddWithValue("@order", row.Order);
            var id = Convert.ToInt32(await q.ExecuteScalarAsync(token));
            foreach (var code in codes)
            {
                await using var insert = new SqlCommand("IF EXISTS(SELECT 1 FROM dbo.brand_row_codes WITH(UPDLOCK,HOLDLOCK) WHERE store_code=@store AND source_brand=@code) THROW 51402,'This source brand is already assigned. Remove it from the other row first.',1; INSERT dbo.brand_row_codes(store_code,source_brand,brand_row_id) VALUES(@store,@code,@id)", c, t);
                insert.Parameters.AddWithValue("@store", row.StoreCode.Trim().ToUpperInvariant()); insert.Parameters.AddWithValue("@code", code); insert.Parameters.AddWithValue("@id", id);
                await insert.ExecuteNonQueryAsync(token);
            }
            await t.CommitAsync(token);
        }
        catch { await t.RollbackAsync(CancellationToken.None); throw; }
    }

    public async Task<IReadOnlyList<SourceBrandEvidence>> LoadSourceBrandsAsync(CancellationToken token = default)
    {
        await using var c = await Open(token);
        await using var q = new SqlCommand("""
            SELECT DISTINCT i.store_code,COALESCE(l.source_brand_code,''),COALESCE(l.source_brand_name,''),COALESCE(l.brand_segment,'')
            FROM dbo.sales_lines l JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id
            UNION SELECT DISTINCT store_code,COALESCE(brand_code,''),COALESCE(brand_name,''),COALESCE(cluster,'') FROM dbo.stock_snapshots
            ORDER BY 1,2,3,4;
            """, c);
        await using var r = await q.ExecuteReaderAsync(token); var rows = new List<SourceBrandEvidence>();
        while(await r.ReadAsync(token)) rows.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3)));
        return rows;
    }

    public async Task<IReadOnlyList<MonthlyTargetRow>> LoadTargetsAsync(CancellationToken token = default)
    {
        await using var c = await Open(token); await using var q = new SqlCommand("SELECT store_code,target_month,target_sales FROM dbo.monthly_targets ORDER BY target_month DESC,store_code",c);
        await using var r = await q.ExecuteReaderAsync(token); var rows = new List<MonthlyTargetRow>();
        while(await r.ReadAsync(token)) rows.Add(new(r.GetString(0),r.GetFieldValue<DateOnly>(1),r.GetDecimal(2)));
        return rows;
    }
}
