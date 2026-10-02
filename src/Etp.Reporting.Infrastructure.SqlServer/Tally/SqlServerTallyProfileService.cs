using System.Data;
using Etp.Reporting.Application.Accounting;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer.Tally;

/// <summary>Owner-only store for Tally companies (plan task 1). Saving never sets
/// <c>production_enabled_utc</c>: live books are enabled by a separate action (plan task 26).</summary>
public sealed class SqlServerTallyProfileService(string connectionString) : ITallyProfileService
{
    public async Task<IReadOnlyList<TallyProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await RequireOwnerAsync(cancellationToken);
        const string sql = """
            SELECT p.tally_profile_id,p.profile_code,p.company_name,p.environment,p.endpoint_url,p.default_delivery_mode,p.payload_format,
                   p.voucher_granularity,p.party_policy,p.single_party_ledger,p.tender_model,p.posting_model,p.voucher_view,
                   p.posting_from_date,p.posting_to_date,p.tally_build_label,p.is_enabled,p.production_enabled_utc,p.modified_by,p.modified_utc,
                   (SELECT STRING_AGG(s.store_code,',') WITHIN GROUP(ORDER BY s.store_code) FROM dbo.tally_profile_stores s WHERE s.tally_profile_id=p.tally_profile_id)
            FROM dbo.tally_profiles p ORDER BY p.environment DESC,p.profile_code;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<TallyProfile>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var stores = reader.IsDBNull(20) ? Array.Empty<string>() : reader.GetString(20).Split(',', StringSplitOptions.RemoveEmptyEntries);
            result.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), Optional(reader, 4),
                reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8), Optional(reader, 9),
                reader.GetString(10), reader.GetString(11), reader.GetString(12), OptionalDate(reader, 13), OptionalDate(reader, 14),
                Optional(reader, 15), reader.GetBoolean(16), stores,
                reader.IsDBNull(17) ? null : reader.GetDateTime(17), reader.GetString(18), reader.GetDateTime(19)));
        }
        return result;
    }

    public async Task<int> SaveAsync(TallyProfile profile, string reason, CancellationToken cancellationToken = default)
    {
        var value = TallyProfileRules.Normalise(profile, reason);
        await RequireOwnerAsync(cancellationToken);
        // Bindings are removed and re-added around the profile change, so an environment change
        // never fights the (profile, environment) foreign key of the store rows.
        const string sql = """
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            DECLARE @saved int=@id;
            IF @saved IS NULL
            BEGIN
              INSERT dbo.tally_profiles(profile_code,company_name,environment,endpoint_url,default_delivery_mode,payload_format,voucher_granularity,
                party_policy,single_party_ledger,tender_model,posting_model,voucher_view,posting_from_date,posting_to_date,tally_build_label,is_enabled,change_reason)
              VALUES(@code,@company,@environment,@endpoint,@delivery,@format,@granularity,@party,@ledger,@tender,@posting,@view,@from,@to,@build,@enabled,@reason);
              SET @saved=CONVERT(int,SCOPE_IDENTITY());
            END
            ELSE
            BEGIN
              -- Evidence folders, reservations and read-backs are keyed by these three, so they are fixed once used.
              IF EXISTS(SELECT 1 FROM dbo.tally_profiles p WHERE p.tally_profile_id=@saved
                  AND (p.profile_code<>@code OR p.environment<>@environment OR CONVERT(varbinary(400),p.company_name)<>CONVERT(varbinary(400),@company)))
                AND (EXISTS(SELECT 1 FROM dbo.accounting_batches WHERE tally_profile_id=@saved) OR EXISTS(SELECT 1 FROM dbo.tally_readbacks WHERE tally_profile_id=@saved))
                THROW 51579,'This Tally company already has batches, so its short code, company name and books cannot change. Add a new Tally company instead.',1;
              DELETE dbo.tally_profile_stores WHERE tally_profile_id=@saved;
              UPDATE dbo.tally_profiles SET profile_code=@code,company_name=@company,environment=@environment,endpoint_url=@endpoint,
                default_delivery_mode=@delivery,payload_format=@format,voucher_granularity=@granularity,party_policy=@party,single_party_ledger=@ledger,
                tender_model=@tender,posting_model=@posting,voucher_view=@view,posting_from_date=@from,posting_to_date=@to,tally_build_label=@build,
                is_enabled=@enabled,change_reason=@reason,modified_by=ORIGINAL_LOGIN(),modified_utc=SYSUTCDATETIME()
              WHERE tally_profile_id=@saved;
              IF @@ROWCOUNT<>1 THROW 51579,'This Tally company no longer exists. Refresh the list.',1;
            END;
            INSERT dbo.tally_profile_stores(tally_profile_id,environment,store_code)
            SELECT @saved,@environment,value FROM OPENJSON(@stores) WITH(value varchar(30) '$');
            EXEC dbo.record_operational_audit 'ConfigurationChange','Succeeded',N'Tally company settings changed',N'database';
            COMMIT TRANSACTION;
            SELECT @saved;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        Add(command, "@id", SqlDbType.Int, value.Id);
        Add(command, "@code", SqlDbType.VarChar, value.ProfileCode, 30);
        Add(command, "@company", SqlDbType.NVarChar, value.CompanyName, 200);
        Add(command, "@environment", SqlDbType.VarChar, value.Environment, 10);
        Add(command, "@endpoint", SqlDbType.NVarChar, value.EndpointUrl, 200);
        Add(command, "@delivery", SqlDbType.VarChar, value.DefaultDeliveryMode, 10);
        Add(command, "@format", SqlDbType.VarChar, value.PayloadFormat, 10);
        Add(command, "@granularity", SqlDbType.VarChar, value.VoucherGranularity, 20);
        Add(command, "@party", SqlDbType.VarChar, value.PartyPolicy, 20);
        Add(command, "@ledger", SqlDbType.NVarChar, value.SinglePartyLedger, 200);
        Add(command, "@tender", SqlDbType.VarChar, value.TenderModel, 20);
        Add(command, "@posting", SqlDbType.VarChar, value.PostingModel, 20);
        Add(command, "@view", SqlDbType.VarChar, value.VoucherView, 30);
        Add(command, "@from", SqlDbType.Date, value.PostingFromDate?.ToDateTime(TimeOnly.MinValue));
        Add(command, "@to", SqlDbType.Date, value.PostingToDate?.ToDateTime(TimeOnly.MinValue));
        Add(command, "@build", SqlDbType.NVarChar, value.TallyBuildLabel, 100);
        Add(command, "@enabled", SqlDbType.Bit, value.IsEnabled);
        Add(command, "@reason", SqlDbType.NVarChar, reason.Trim(), 500);
        Add(command, "@stores", SqlDbType.NVarChar, System.Text.Json.JsonSerializer.Serialize(value.StoreCodes), -1);
        try
        {
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627 && exception.Message.Contains("UQ_tally_profile_stores_store", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"A store in this list is already linked to another {(value.Environment == "TEST" ? "test" : "live")} Tally company. Remove it there first.", exception);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627 && exception.Message.Contains("UQ_tally_profiles_code", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Another Tally company already uses the short code {value.ProfileCode}.", exception);
        }
        catch (SqlException exception) when (exception.Number == 547 && exception.Message.Contains("FK_tally_profile_stores_store", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A store in this list is not in Settings → Stores & masters → Stores.", exception);
        }
        catch (SqlException exception) when (exception.Number == 547 && exception.Message.Contains("CK_tally_profiles_production_environment", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("This company has live books enabled, so it cannot be changed to test books.", exception);
        }
        catch (SqlException exception) when (exception.Number == 51579)
        {
            throw new InvalidOperationException(exception.Message, exception);
        }
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

    private static void Add(SqlCommand command, string name, SqlDbType type, object? value, int size = 0)
    {
        var parameter = command.Parameters.Add(name, type);
        if (size != 0) parameter.Size = size;
        parameter.Value = value ?? DBNull.Value;
    }

    private static string? Optional(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static DateOnly? OptionalDate(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : DateOnly.FromDateTime(reader.GetDateTime(ordinal));
}
