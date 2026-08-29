using Etp.Reporting.Domain.Imports;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

internal static class SqlServerImportProfileResolver
{
    public static async Task<int> ResolveOrRegisterAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        ImportProfileIdentity profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        const string findSql = """
            SELECT import_profile_id,report_code,layout_version,profile_version,header_signature_sha256,is_active
            FROM dbo.import_profiles WITH(UPDLOCK,HOLDLOCK)
            WHERE report_code=@report AND layout_version=@layout AND profile_version=@profile;
            """;
        await using (var find = new SqlCommand(findSql, connection, transaction))
        {
            Bind(find, profile);
            await using var reader = await find.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetInt32(0);
                var stored = new ImportProfileIdentity(
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4));
                var active = reader.GetBoolean(5);
                if (stored != profile)
                    throw new InvalidOperationException(
                        "The registered import profile version does not match the approved profile identity.");
                if (!active)
                    throw new InvalidOperationException("The matched import profile is inactive in the database.");
                return id;
            }
        }

        const string insertSql = """
            INSERT dbo.import_profiles(report_code,layout_version,profile_version,header_signature_sha256,is_active)
            VALUES(@report,@layout,@profile,@signature,1);
            SELECT CONVERT(int,SCOPE_IDENTITY());
            """;
        await using var insert = new SqlCommand(insertSql, connection, transaction);
        Bind(insert, profile);
        return Convert.ToInt32(await insert.ExecuteScalarAsync(cancellationToken));
    }

    private static void Bind(SqlCommand command, ImportProfileIdentity profile)
    {
        command.Parameters.AddWithValue("@report", profile.ReportCode);
        command.Parameters.AddWithValue("@layout", profile.LayoutVersion);
        command.Parameters.AddWithValue("@profile", profile.ProfileVersion);
        command.Parameters.AddWithValue("@signature", profile.HeaderSignatureSha256);
    }
}
