using System.Data;
using Etp.Reporting.Application.Access;
using Etp.Reporting.Application.Collections;
using Etp.Reporting.Infrastructure.SqlServer.Connections;
using Microsoft.Data.SqlClient;
using static Etp.Reporting.Infrastructure.SqlServer.Collections.CollectionsAccess;

namespace Etp.Reporting.Infrastructure.SqlServer.Collections;

/// <summary>
/// Reports > "Collections: expected vs evidence" (store x date x mode, "totals only, not matched yet") and Reports > Deposits in
/// transit. Reads only the object views v_collection_expected, v_collection_evidence and v_cash_deposit_status (no PII column
/// is in any of them); <see cref="CollectionsStatementBuilder"/> words the lines. Every role opens the statement; a Viewer gets
/// it without bank references, and the in-transit list is Owner and Store Manager. On a database without the views (an older
/// release's) the statement is empty rather than an error.
/// </summary>
public sealed class SqlServerCollectionsStatementQuery : ICollectionsStatementQuery
{
    private const int MaximumDays = 366;
    private readonly ICollectionsSession session;

    public SqlServerCollectionsStatementQuery(IEtpConnectionFactory connections) : this(new SqlCollectionsSession(connections)) { }

    internal SqlServerCollectionsStatementQuery(ICollectionsSession session) => this.session = session ?? throw new ArgumentNullException(nameof(session));

    public async Task<CollectionsStatement> LoadStatementAsync(CollectionsStatementRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.To < request.From) throw new ArgumentException("The end date is before the start date.");
        if (request.To.DayNumber - request.From.DayNumber >= MaximumDays) throw new ArgumentException("Choose at most one year.");
        var role = await RequireAsync(session, CanView, "ReportRun", ViewRequired, cancellationToken);
        var hideReferences = role == AccessRole.Viewer;

        const string sql = """
            IF OBJECT_ID(N'dbo.v_collection_expected',N'V') IS NULL OR OBJECT_ID(N'dbo.v_collection_evidence',N'V') IS NULL
               OR OBJECT_ID(N'dbo.v_cash_deposit_status',N'V') IS NULL
            BEGIN
              SELECT CONVERT(bit,0);
              RETURN;
            END;
            SELECT CONVERT(bit,1);
            SELECT store_code,business_date,mode,provider_key,unmapped_tender_code,evidence_kind,expected_amount
            FROM dbo.v_collection_expected
            WHERE business_date BETWEEN @from AND @to AND (@stores IS NULL OR store_code IN(SELECT CONVERT(varchar(30),[value]) FROM OPENJSON(@stores)));
            SELECT store_code,business_date,provider_key,gross_total,net_total,line_count
            FROM dbo.v_collection_evidence
            WHERE business_date BETWEEN @from AND @to AND (@stores IS NULL OR store_code IN(SELECT CONVERT(varchar(30),[value]) FROM OPENJSON(@stores)));
            SELECT store_code,deposit_business_date,sent_amount,credited_amount,credited_date,bank_reference
            FROM dbo.v_cash_deposit_status
            WHERE deposit_business_date BETWEEN @from AND @to AND (@stores IS NULL OR store_code IN(SELECT CONVERT(varchar(30),[value]) FROM OPENJSON(@stores)));
            """;
        await using var connection = await session.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        Add(command, "@from", SqlDbType.Date, Date(request.From));
        Add(command, "@to", SqlDbType.Date, Date(request.To));
        Add(command, "@stores", SqlDbType.NVarChar, StoresJson(request.StoreCodes), -1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || !reader.GetBoolean(0)) return new([], hideReferences);

        await reader.NextResultAsync(cancellationToken);
        var expected = new List<ExpectedCollection>();
        while (await reader.ReadAsync(cancellationToken))
            expected.Add(new(reader.GetString(0), RequiredDate(reader, 1), reader.GetString(2), OptionalString(reader, 3), OptionalString(reader, 4),
                reader.GetString(5), reader.IsDBNull(6) ? 0 : reader.GetDecimal(6)));
        await reader.NextResultAsync(cancellationToken);
        var evidence = new List<SettlementEvidence>();
        while (await reader.ReadAsync(cancellationToken))
            evidence.Add(new(reader.GetString(0), RequiredDate(reader, 1), reader.GetString(2), OptionalDecimal(reader, 3), OptionalDecimal(reader, 4), reader.GetInt64(5)));
        await reader.NextResultAsync(cancellationToken);
        var deposits = new List<CashDepositStatus>();
        while (await reader.ReadAsync(cancellationToken))
            deposits.Add(new(reader.GetString(0), RequiredDate(reader, 1), OptionalDecimal(reader, 2), OptionalDecimal(reader, 3), OptionalDate(reader, 4), OptionalString(reader, 5)));

        return new(CollectionsStatementBuilder.Build(expected, evidence, deposits, request.AsOf, hideReferences), hideReferences);
    }

    public async Task<IReadOnlyList<DepositInTransit>> LoadDepositsInTransitAsync(IReadOnlyList<string>? storeCodes, CancellationToken cancellationToken = default)
    {
        await RequireAsync(session, CanImport, DepositAudit, ImportRequired, cancellationToken);
        return await SqlServerCashDepositService.LoadInTransitCoreAsync(session, storeCodes, cancellationToken);
    }
}
