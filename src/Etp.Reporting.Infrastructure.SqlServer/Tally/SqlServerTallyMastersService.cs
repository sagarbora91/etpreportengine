using System.Data;
using System.Security.Cryptography;
using System.Text;
using Etp.Reporting.Application.Accounting;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer.Tally;

/// <summary>Decision 49 (3) for the Owner: loads a Tally company's stores, cost centres, payment modes, GST rates and ledger
/// mappings, plans the masters with <see cref="TallyMastersPlanner"/>, and writes the import file and the accountant's list.
/// Reads only; nothing in the database changes and nothing is sent to Tally.</summary>
public sealed class SqlServerTallyMastersService(string connectionString, Func<DateOnly>? today = null, Func<DateTime>? now = null) : ITallyMastersService
{
    public async Task<TallyMastersPlan> PlanAsync(int tallyProfileId, CancellationToken cancellationToken = default)
    {
        await RequireOwnerAsync(cancellationToken);
        var profile = (await new SqlServerTallyProfileService(connectionString).LoadAsync(cancellationToken)).FirstOrDefault(item => item.Id == tallyProfileId)
            ?? throw new InvalidOperationException("This Tally company no longer exists. Refresh the list.");
        if (profile.StoreCodes.Count == 0) throw new InvalidOperationException($"No store is linked to {profile.CompanyName}. Add the stores in Settings → Tally companies first.");

        const string sql = """
            SELECT s.store_code,s.store_name FROM dbo.stores s
            WHERE s.store_code IN(SELECT CONVERT(varchar(30),value) COLLATE DATABASE_DEFAULT FROM OPENJSON(@stores)) ORDER BY s.store_code;
            SELECT DISTINCT mode FROM dbo.tender_modes WHERE active=1;
            WITH gst AS (
              SELECT r.cgst_rate,r.cgst_amount,r.sgst_utgst_rate,r.sgst_utgst_amount,r.igst_rate,r.igst_amount
              FROM dbo.[etp_r018] r JOIN dbo.import_files f ON f.import_file_id=r.import_file_id AND f.is_superseded=0
              WHERE CAST(r.store_code AS nvarchar(80)) COLLATE DATABASE_DEFAULT IN(SELECT CONVERT(nvarchar(80),value) COLLATE DATABASE_DEFAULT FROM OPENJSON(@stores)))
            SELECT DISTINCT 'CGST',cgst_rate FROM gst WHERE cgst_amount<>0 AND cgst_rate IS NOT NULL
            UNION SELECT DISTINCT 'SGST',sgst_utgst_rate FROM gst WHERE sgst_utgst_amount<>0 AND sgst_utgst_rate IS NOT NULL
            UNION SELECT DISTINCT 'IGST',igst_rate FROM gst WHERE igst_amount<>0 AND igst_rate IS NOT NULL;
            """;
        var stores = new List<TallyMastersStore>();
        var modes = new List<string>();
        var rates = new List<TallyMastersGstRate>();
        await using (var connection = await OpenAsync(cancellationToken))
        await using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.Add("@stores", SqlDbType.NVarChar, -1).Value = System.Text.Json.JsonSerializer.Serialize(profile.StoreCodes);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                stores.Add(new(reader.GetString(0), reader.GetString(1), profile.CostCentreFor(reader.GetString(0))));
            await reader.NextResultAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) modes.Add(reader.GetString(0));
            await reader.NextResultAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) rates.Add(new(reader.GetString(0), reader.GetDecimal(1)));
        }
        var mappings = await new SqlServerTallyLedgerMappingService(connectionString).LoadAsync(cancellationToken);
        return TallyMastersPlanner.Plan(new(profile, stores, modes, rates, mappings, today?.Invoke() ?? DateOnly.FromDateTime(DateTime.Now)));
    }

    public async Task<TallyMastersExportReceipt> ExportAsync(int tallyProfileId, string folder, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder))
            throw new ArgumentException("Choose a full folder path for the Tally masters files.", nameof(folder));
        var plan = await PlanAsync(tallyProfileId, cancellationToken);
        var profileCode = (await new SqlServerTallyProfileService(connectionString).LoadAsync(cancellationToken)).First(item => item.Id == tallyProfileId).ProfileCode;
        var root = Path.GetFullPath(folder);
        RejectLinks(root);
        Directory.CreateDirectory(root);
        var xmlPath = Path.Combine(root, $"tally-masters-{profileCode}.xml");
        var reviewPath = Path.Combine(root, $"tally-masters-{profileCode}-review.txt");
        if (File.Exists(xmlPath) || File.Exists(reviewPath))
            throw new InvalidOperationException("Tally masters files for this company are already in that folder. Choose another folder; files are never overwritten.");

        var xml = TallyMastersWriter.Xml(plan);
        var review = Encoding.UTF8.GetBytes(TallyMastersWriter.Review(plan, now?.Invoke() ?? DateTime.Now));
        await WriteNewAsync(xmlPath, xml, cancellationToken);
        try
        {
            await WriteNewAsync(reviewPath, review, cancellationToken);
        }
        catch
        {
            TryDelete(xmlPath);
            throw;
        }
        return new(xmlPath, reviewPath, Convert.ToHexString(SHA256.HashData(xml)).ToLowerInvariant(), plan.Groups.Count, plan.CostCentres.Count, plan.Ledgers.Count);
    }

    private static async Task WriteNewAsync(string path, byte[] content, CancellationToken token)
    {
        var created = false;
        try
        {
            await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            created = true;
            await stream.WriteAsync(content, token);
            await stream.FlushAsync(token);
        }
        catch when (created)
        {
            TryDelete(path);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void RejectLinks(string folder)
    {
        for (var candidate = folder; !string.IsNullOrEmpty(candidate); candidate = Path.GetDirectoryName(candidate))
            if (Directory.Exists(candidate) && (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Tally masters files cannot be written into a linked folder.");
    }

    private async Task RequireOwnerAsync(CancellationToken token)
    {
        if (!(await new Phase2OperationsRepository(connectionString).LoadCurrentAccessAsync(token)).CanAdminister)
            throw new UnauthorizedAccessException("Owner permission is required.");
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken token)
    {
        var connection = new SqlConnection(SqlAdapterConnection.RequireWindowsIntegrated(connectionString, nameof(connectionString)));
        try { await connection.OpenAsync(token); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }
}
