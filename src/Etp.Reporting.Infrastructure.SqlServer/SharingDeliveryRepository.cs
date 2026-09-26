using Etp.Reporting.Application.Distribution;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed partial class ProductisationRepository
{
    public async Task<IReadOnlyList<ShareDeliveryHistory>> LoadShareHistoryAsync(long generationId, CancellationToken token = default)
    {
        const string sql = """
            SELECT share_attempt_id,channel,destination_safe,attachment_file_name,outcome,safe_message,initiated_by,initiated_utc,attempt_key
            FROM dbo.share_attempts WHERE daily_report_generation_id=@id
            ORDER BY share_attempt_id DESC;
            """;
        await using var connection = await OpenAsync(token);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", generationId);
        await using var reader = await command.ExecuteReaderAsync(token);
        var rows = new List<ShareDeliveryHistory>();
        while (await reader.ReadAsync(token)) rows.Add(new(reader.GetInt64(0),reader.GetString(1),OptionalString(reader,2),
            reader.GetString(3),reader.GetString(4),reader.GetString(5),reader.GetString(6),reader.GetDateTime(7),reader.IsDBNull(8)?null:reader.GetGuid(8)));
        // Show the newest outcome for an attempt; preserve every transition in the append-only SQL log.
        return rows.Where(row => row.AttemptKey is null).Concat(rows.Where(row => row.AttemptKey is not null)
            .GroupBy(row => row.AttemptKey).Select(group => group.First())).OrderByDescending(row => row.Id).ToArray();
    }
}
