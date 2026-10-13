using System.Data;
using System.Globalization;
using Etp.Reporting.Application.Stores;
using Etp.Reporting.Infrastructure.SqlServer.Connections;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>
/// A store row. <paramref name="BusinessUnitCode"/> is NULL for every Retail shop (the two seeded shops and
/// any shop the Owner adds); the Service Centre store seeded by 0048 belongs to the SERVICE unit.
/// <paramref name="IsServiceMoneyShop"/> is 0052's <c>dbo.stores.is_service_money_shop</c>: the one shop that enters the
/// Service centre's money (decision 31); false on a database below 0052.
/// <paramref name="UnitKind"/> and <paramref name="ServiceJobPrefix"/> are 0067's <c>business_units.unit_kind</c> and
/// <c>stores.service_job_prefix</c> (1.12 A4); null on a database below 0067.
/// </summary>
public sealed record StoreCatalogEntry(string Code, string Name, bool IsActive, string? BusinessUnitCode = null, bool IsServiceMoneyShop = false,
    string? UnitKind = null, string? ServiceJobPrefix = null)
{
    /// <summary>A Service Centre store is never a shop: no packs, no DSR, no store picker.</summary>
    public bool IsServiceCentre => ServiceCentreStores.IsServiceUnit(BusinessUnitCode) ||
        string.Equals(UnitKind?.Trim(), ServiceCentreStores.ServiceUnitKind, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Service interim (decision 15, 3 Oct 2026). Migration 0048 section A seeds the Service Centre store inactive under
/// the SERVICE business unit, and trigger trg_stores_service_unit_inactive refuses making any
/// SERVICE-unit store active (SQL error 51900) or moving it out of the SERVICE unit, which would make
/// it a Retail shop store (SQL error 51904). An active Service store would break the combined
/// pack date, the combined pack and the DSR/evening store lists, which all read is_active = 1.
/// 1.12 A4 (0067): a unit's kind (RETAIL or SERVICE) and a Service Centre's job-number prefix are data; the numbers
/// 51970-51975 are the 0067 triggers' and the store editor's refusals.
/// </summary>
public static class ServiceCentreStores
{
    public const string ServiceBusinessUnitCode = "SERVICE";

    /// <summary>0067 <c>business_units.unit_kind</c> of a Service Centre unit.</summary>
    public const string ServiceUnitKind = "SERVICE";
    public const int ActivationRefusedSqlError = 51900;
    public const int UnitMoveRefusedSqlError = 51904;

    /// <summary>0067 trigger numbers and the store editor's own refusals (lane A4 block 51970-51979).</summary>
    public const int PrefixOnShopStoreSqlError = 51970;
    public const int PrefixOverlapSqlError = 51971;
    public const int ServiceStoreActiveSqlError = 51972;
    public const int UnitKindChangeSqlError = 51973;
    public const int ServiceUnitMissingSqlError = 51974;
    public const int CodeIsShopStoreSqlError = 51975;

    public const string KindLabel = "Service, not a shop store";
    public const string ActiveToggleLockedToolTip =
        "This is the Service Centre store. It is not a shop store, so it stays inactive.";
    public const string ActivationRefusedMessage =
        "A Service Centre store is not a shop store and cannot be made active. Nothing was changed.";
    public const string UnitMoveRefusedMessage =
        "A Service Centre store must stay in the Service Centre business unit and cannot become a shop store. Nothing was changed.";
    public const string PrefixOnShopStoreMessage = "A job-number prefix is for a Service Centre store only. Nothing was changed.";
    public const string PrefixOverlapMessage =
        "Each Service Centre needs its own job-number prefix, and one prefix cannot begin with another. Nothing was changed.";
    public const string PrefixInvalidMessage =
        "The job-number prefix uses capital letters A-Z and digits only, 2 to 20 characters. Nothing was changed.";
    public const string UnitKindChangeMessage =
        "A business unit that has stores cannot change between Retail and Service. Nothing was changed.";
    public const string ServiceUnitMissingMessage =
        "The Service Centre business unit is missing from this database. Run setup again, then add the Service Centre.";
    public const string CodeIsShopStoreMessage =
        "This store code belongs to a shop store. Choose another code for the Service Centre. Nothing was changed.";

    public static bool IsServiceUnit(string? businessUnitCode) =>
        string.Equals(businessUnitCode?.Trim(), ServiceBusinessUnitCode, StringComparison.OrdinalIgnoreCase);

    /// <summary>"Service Centre (code)": the name shown wherever a Service store code appears.</summary>
    public static string Label(string code) => $"Service Centre ({code})";

    /// <summary>True when the SQL failure is the 0048 trigger refusing to activate a Service store.</summary>
    public static bool IsActivationRefusal(SqlException exception) => HasError(exception, ActivationRefusedSqlError);

    /// <summary>True when the SQL failure is the 0048 trigger refusing to move a Service store out of the SERVICE unit.</summary>
    public static bool IsUnitMoveRefusal(SqlException exception) => HasError(exception, UnitMoveRefusedSqlError);

    /// <summary>
    /// The Owner-facing message for a 0048 or 0067 refusal (51900, 51904, 51970-51975, or the 0067 prefix CHECK), or null
    /// for any other failure.
    /// </summary>
    public static string? DescribeRefusal(SqlException exception) =>
        IsActivationRefusal(exception) || HasError(exception, ServiceStoreActiveSqlError) ? ActivationRefusedMessage
        : IsUnitMoveRefusal(exception) ? UnitMoveRefusedMessage
        : HasError(exception, PrefixOnShopStoreSqlError) ? PrefixOnShopStoreMessage
        : HasError(exception, PrefixOverlapSqlError) ? PrefixOverlapMessage
        : HasError(exception, UnitKindChangeSqlError) ? UnitKindChangeMessage
        : HasError(exception, ServiceUnitMissingSqlError) ? ServiceUnitMissingMessage
        : HasError(exception, CodeIsShopStoreSqlError) ? CodeIsShopStoreMessage
        : HasError(exception, 547) && exception.Message.Contains("CK_stores_service_job_prefix", StringComparison.OrdinalIgnoreCase) ? PrefixInvalidMessage
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
    // database from before 0048 (every store then has no unit and is Retail). is_service_money_shop exists
    // since 0052 and unit_kind / service_job_prefix since 0067; each is read in its own statement only when its column
    // exists, so an older database still loads (the missing columns read 0 or NULL).
    internal const string LoadSql = """
        IF COL_LENGTH(N'dbo.stores', N'is_service_money_shop') IS NULL
            SELECT s.store_code,s.store_name,s.is_active,u.business_unit_code,CONVERT(bit,0) is_service_money_shop,
              CONVERT(varchar(10),NULL) unit_kind,CONVERT(varchar(20),NULL) service_job_prefix
            FROM dbo.stores s LEFT JOIN dbo.business_units u ON u.business_unit_id=s.business_unit_id
            ORDER BY s.store_id;
        ELSE IF COL_LENGTH(N'dbo.stores', N'service_job_prefix') IS NULL OR COL_LENGTH(N'dbo.business_units', N'unit_kind') IS NULL
            EXEC sp_executesql N'SELECT s.store_code,s.store_name,s.is_active,u.business_unit_code,s.is_service_money_shop,
              CONVERT(varchar(10),NULL) unit_kind,CONVERT(varchar(20),NULL) service_job_prefix
            FROM dbo.stores s LEFT JOIN dbo.business_units u ON u.business_unit_id=s.business_unit_id
            ORDER BY s.store_id';
        ELSE
            EXEC sp_executesql N'SELECT s.store_code,s.store_name,s.is_active,u.business_unit_code,s.is_service_money_shop,
              u.unit_kind,s.service_job_prefix
            FROM dbo.stores s LEFT JOIN dbo.business_units u ON u.business_unit_id=s.business_unit_id
            ORDER BY s.store_id';
        """;

    public async Task<IReadOnlyList<StoreCatalogEntry>> LoadAsync(CancellationToken token = default)
    {
        await using var connection = new SqlConnection(validatedConnectionString);
        await connection.OpenAsync(token);
        await using var command = new SqlCommand(LoadSql, connection);
        await using var reader = await command.ExecuteReaderAsync(token);
        var rows = new List<StoreCatalogEntry>();
        while (await reader.ReadAsync(token))
            rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetBoolean(2), reader.IsDBNull(3) ? null : reader.GetString(3),
                !reader.IsDBNull(4) && reader.GetBoolean(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        return rows;
    }

    /// <summary>
    /// Active shop stores. A Service store is inactive by the 0048 trigger; it is excluded here
    /// as well so that no Retail list can ever pick it up.
    /// </summary>
    public async Task<string[]> ActiveCodesAsync(CancellationToken token = default) =>
        (await LoadAsync(token)).Where(store => store.IsActive && !store.IsServiceCentre).Select(store => store.Code).ToArray();

    /// <summary>
    /// Adds a Service Centre store, or renames one and changes its job-number prefix (Settings > Stores, Owner only). The
    /// store goes into the SERVICE business unit and stays inactive (it is not a shop store). A code that belongs to a
    /// shop store is refused (51975); a missing SERVICE unit is refused (51974); an overlapping prefix (51971) or one that is
    /// not 2-20 capital letters or digits (the 0067 CHECK) is refused by the database. Every refusal comes back as an
    /// <see cref="InvalidOperationException"/> with a plain sentence, and nothing is written.
    /// </summary>
    public async Task SaveServiceCentreAsync(string code, string name, string jobNumberPrefix, string reason, CancellationToken token = default)
    {
        code = Required(code, "the store code").ToUpperInvariant();
        name = Required(name, "the store name");
        jobNumberPrefix = Required(jobNumberPrefix, "the job-number prefix").ToUpperInvariant();
        reason = Required(reason, "a reason");
        if (code.Length > 30 || name.Length > 200 || reason.Length > 500) throw new ArgumentException("The store code, name or reason is too long.");
        if (!ServiceCentreRules.IsValidPrefix(jobNumberPrefix)) throw new ArgumentException(ServiceCentreStores.PrefixInvalidMessage);
        await using var connection = new SqlConnection(validatedConnectionString);
        await connection.OpenAsync(token);
        await using (var owner = new SqlCommand(OwnerCheckSql, connection))
        {
            if (Convert.ToInt32(await owner.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) != 1)
                throw new UnauthorizedAccessException("Owner permission is required for this change.");
        }
        await using var command = new SqlCommand(SaveServiceCentreSql, connection);
        command.Parameters.Add("@code", SqlDbType.VarChar, 30).Value = code;
        command.Parameters.Add("@name", SqlDbType.NVarChar, 200).Value = name;
        command.Parameters.Add("@prefix", SqlDbType.VarChar, 20).Value = jobNumberPrefix;
        command.Parameters.Add("@reason", SqlDbType.NVarChar, 500).Value = reason;
        try { await command.ExecuteNonQueryAsync(token); }
        catch (SqlException exception) when (ServiceCentreStores.DescribeRefusal(exception) is { } refusal)
        {
            throw new InvalidOperationException(refusal, exception);
        }
    }

    internal const string OwnerCheckSql =
        "SELECT COUNT(*) FROM dbo.application_users WHERE windows_identity=SUSER_SNAME() AND role_code='OWNER' AND is_active=1";

    /// <summary>One batch, one transaction: the unit and shop-code checks, the MERGE and the audit row.</summary>
    internal const string SaveServiceCentreSql = """
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;
        DECLARE @unit int=(SELECT business_unit_id FROM dbo.business_units WITH(UPDLOCK,HOLDLOCK)
          WHERE business_unit_code='SERVICE' AND unit_kind='SERVICE');
        IF @unit IS NULL
          THROW 51974,'The Service Centre business unit is missing. Run setup again.',1;
        IF EXISTS(SELECT 1 FROM dbo.stores WITH(UPDLOCK,HOLDLOCK) WHERE store_code=@code AND (business_unit_id IS NULL OR business_unit_id<>@unit))
          THROW 51975,'This store code belongs to a shop store.',1;
        MERGE dbo.stores WITH(HOLDLOCK) target USING(SELECT CONVERT(varchar(30),@code) store_code) source ON target.store_code=source.store_code
        WHEN MATCHED THEN UPDATE SET store_name=@name,service_job_prefix=@prefix,modified_by=SUSER_SNAME(),modified_utc=SYSUTCDATETIME(),change_reason=@reason
        WHEN NOT MATCHED THEN INSERT(store_code,store_name,business_unit_id,is_active,service_job_prefix,modified_by,modified_utc,change_reason)
          VALUES(@code,@name,@unit,0,@prefix,SUSER_SNAME(),SYSUTCDATETIME(),@reason);
        EXEC dbo.record_operational_audit 'MasterDataChange','Succeeded',N'Service Centre store changed',N'database';
        COMMIT TRANSACTION;
        """;

    private static string Required(string? value, string what) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"Enter {what}.") : value.Trim();
}

/// <summary>
/// The Service Centres of the database (1.12 A4, frozen contract): every store of a business unit whose 0067
/// <c>unit_kind</c> is SERVICE, with its job-number prefix. On a database before 0067 (no unit_kind or prefix column) it
/// returns the stores of the business unit coded SERVICE (0048), each with the prefix JO plus its store code. A store with
/// no prefix also reads JO plus its code. Read-only; one SELECT.
/// </summary>
public sealed class SqlServerServiceCentreCatalog : IServiceCentreCatalog
{
    private readonly Func<CancellationToken, Task<SqlConnection>> open;

    public SqlServerServiceCentreCatalog(IEtpConnectionFactory connections)
    {
        ArgumentNullException.ThrowIfNull(connections);
        open = token => connections.OpenAsync(EtpConnectionPurpose.Default, token);
    }

    /// <summary>For the 1.11 adapters that hold a validated connection string (the folder import).</summary>
    public SqlServerServiceCentreCatalog(string connectionString)
    {
        var validated = LocalSqlConnectionPolicy.Validate(connectionString);
        open = async token =>
        {
            var connection = new SqlConnection(validated);
            try { await connection.OpenAsync(token).ConfigureAwait(false); return connection; }
            catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
        };
    }

    // Guarded by COL_LENGTH only (the condition reads no new column); the 0067 read is its own statement, so it compiles
    // only where the columns exist.
    internal const string LoadSql = """
        IF COL_LENGTH(N'dbo.business_units', N'unit_kind') IS NULL OR COL_LENGTH(N'dbo.stores', N'service_job_prefix') IS NULL
            SELECT s.store_code,s.store_name,CONVERT(varchar(20),NULL) service_job_prefix
            FROM dbo.stores s JOIN dbo.business_units u ON u.business_unit_id=s.business_unit_id
            WHERE u.business_unit_code='SERVICE'
            ORDER BY s.store_code;
        ELSE
            EXEC sp_executesql N'SELECT s.store_code,s.store_name,s.service_job_prefix
            FROM dbo.stores s JOIN dbo.business_units u ON u.business_unit_id=s.business_unit_id
            WHERE u.unit_kind=''SERVICE''
            ORDER BY s.store_code';
        """;

    public async Task<IReadOnlyList<ServiceCentre>> LoadAsync(CancellationToken ct = default)
    {
        await using var connection = await open(ct).ConfigureAwait(false);
        await using var command = new SqlCommand(LoadSql, connection);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var centres = new List<ServiceCentre>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var code = reader.GetString(0).Trim();
            var prefix = reader.IsDBNull(2) ? null : reader.GetString(2).Trim();
            centres.Add(new(code, reader.GetString(1).Trim(), string.IsNullOrEmpty(prefix) ? ServiceCentreRules.SuggestedPrefix(code) : prefix));
        }
        return centres;
    }
}

/// <summary>Settings > Stores' Service Centre save (1.12 A4) over <see cref="StoreCatalogRepository.SaveServiceCentreAsync"/>.</summary>
public sealed class SqlServerServiceCentreAdministration(IEtpConnectionFactory connections) : IServiceCentreAdministration
{
    private readonly IEtpConnectionFactory connections = connections ?? throw new ArgumentNullException(nameof(connections));

    public Task SaveServiceCentreAsync(string storeCode, string name, string jobNumberPrefix, string reason, CancellationToken ct = default) =>
        new StoreCatalogRepository(connections.LegacyConnectionString()).SaveServiceCentreAsync(storeCode, name, jobNumberPrefix, reason, ct);
}
