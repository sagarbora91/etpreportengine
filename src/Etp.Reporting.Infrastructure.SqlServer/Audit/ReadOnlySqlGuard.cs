using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer.Audit;

/// <summary>
/// Refuses any SQL text that could write, lock or run code (design 6.4.1): DML (<c>INSERT</c>, <c>UPDATE</c>, <c>DELETE</c>,
/// <c>MERGE</c>, <c>TRUNCATE</c>), DDL (<c>CREATE</c>, <c>ALTER</c>, <c>DROP</c>), permissions (<c>GRANT</c>, <c>REVOKE</c>,
/// <c>DENY</c>), <c>EXEC</c>, <c>INTO</c>, temporary tables (<c>#</c>), the lock hints <c>UPDLOCK</c>, <c>HOLDLOCK</c>,
/// <c>XLOCK</c>, <c>TABLOCKX</c>, <c>BACKUP</c>, <c>RESTORE</c>, <c>DBCC</c>, and any <c>sp_</c> or <c>xp_</c> procedure.
/// The one exception is <c>EXEC sys.sp_executesql</c>, which <c>check-import-upgrade.sql</c> uses for SELECT-only dynamic
/// reads: then every string literal (the constant fragments the dynamic text is built from) must pass the same rules.
/// Comments and string literals are not code: a word inside them is not a statement.
/// </summary>
public static partial class ReadOnlySqlGuard
{
    private static readonly HashSet<string> Forbidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "INSERT", "UPDATE", "DELETE", "MERGE", "TRUNCATE", "CREATE", "ALTER", "DROP", "GRANT", "REVOKE", "DENY", "EXEC", "EXECUTE",
        "INTO", "UPDLOCK", "HOLDLOCK", "XLOCK", "TABLOCKX", "BACKUP", "RESTORE", "DBCC", "OPENROWSET", "OPENQUERY", "OPENDATASOURCE",
        "SHUTDOWN", "KILL", "RECONFIGURE", "WRITETEXT", "UPDATETEXT", "BULK", "TRAN", "TRANSACTION", "COMMIT", "ROLLBACK"
    };

    /// <summary>The reasons <paramref name="sql"/> is refused; empty when it is read-only.</summary>
    public static IReadOnlyList<string> Violations(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        var (code, literals) = Split(sql);
        var violations = new List<string>();
        var dynamic = false;
        var words = Word().Matches(code).Select(match => match.Value).ToArray();
        for (var index = 0; index < words.Length; index++)
        {
            var word = words[index];
            if (word.Equals("EXEC", StringComparison.OrdinalIgnoreCase) || word.Equals("EXECUTE", StringComparison.OrdinalIgnoreCase))
            {
                var target = index + 1 < words.Length ? words[index + 1] : "";
                if (target.Equals("sys.sp_executesql", StringComparison.OrdinalIgnoreCase) || target.Equals("sp_executesql", StringComparison.OrdinalIgnoreCase))
                {
                    dynamic = true;
                    index++;
                    continue;
                }
            }
            var bare = word.Contains('.') ? word[(word.LastIndexOf('.') + 1)..] : word;
            if (Forbidden.Contains(bare)) violations.Add(bare.ToUpperInvariant());
            else if (bare.StartsWith("sp_", StringComparison.OrdinalIgnoreCase) || bare.StartsWith("xp_", StringComparison.OrdinalIgnoreCase))
                violations.Add(bare.ToLowerInvariant());
        }
        if (code.Contains('#')) violations.Add("#");
        if (dynamic)
            foreach (var literal in literals)
                foreach (var violation in Violations(literal))
                    violations.Add("dynamic " + violation);
        return violations.Distinct(StringComparer.Ordinal).ToArray();
    }

    public static bool IsReadOnly(string sql) => Violations(sql).Count == 0;

    /// <summary>Throws when <paramref name="sql"/> is not read-only; every audit command calls this before it runs a text.</summary>
    public static void Require(string sql)
    {
        var violations = Violations(sql);
        if (violations.Count > 0)
            throw new InvalidOperationException("ImportAudit refused to run a SQL text that is not read-only: " + string.Join(", ", violations) + ".");
    }

    [GeneratedRegex(@"[A-Za-z_@][A-Za-z0-9_@$]*(\.[A-Za-z_][A-Za-z0-9_$]*)*", RegexOptions.CultureInvariant)]
    private static partial Regex Word();

    /// <summary>The text with comments and string literals blanked out, and the contents of each string literal.</summary>
    private static (string Code, IReadOnlyList<string> Literals) Split(string sql)
    {
        var code = new StringBuilder(sql.Length);
        var literals = new List<string>();
        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n') i++;
                code.Append('\n');
            }
            else if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? sql.Length : end + 1;
                code.Append(' ');
            }
            else if (c == '\'')
            {
                var literal = new StringBuilder();
                for (i++; i < sql.Length; i++)
                {
                    if (sql[i] == '\'' && i + 1 < sql.Length && sql[i + 1] == '\'') { literal.Append('\''); i++; }
                    else if (sql[i] == '\'') break;
                    else literal.Append(sql[i]);
                }
                literals.Add(literal.ToString());
                code.Append(" '' ");
            }
            else if (c == '[')
            {
                // A bracketed name is a name, whatever it spells.
                var end = sql.IndexOf(']', i + 1);
                i = end < 0 ? sql.Length : end;
                code.Append(" name ");
            }
            else code.Append(c);
        }
        return (code.ToString(), literals);
    }
}

/// <summary>
/// One read-only connection to a local database (design 4, 6.4): the application's connection policy (this computer,
/// Windows authentication, <c>Encrypt=Optional</c>), <c>ApplicationName=EtpImportAudit</c>, <c>ApplicationIntent=ReadOnly</c>,
/// <c>SET LOCK_TIMEOUT 5000</c> and the default READ COMMITTED isolation, no explicit transaction. Every text passes
/// <see cref="ReadOnlySqlGuard"/> before it is sent.
/// </summary>
public sealed class ReadOnlyAuditConnection : IAsyncDisposable
{
    private readonly SqlConnection connection;

    private ReadOnlyAuditConnection(SqlConnection connection, string server, string database)
    {
        this.connection = connection;
        Server = server;
        Database = database;
    }

    public string Server { get; }
    public string Database { get; }

    /// <summary>The connection string the policy accepts; throws <see cref="ArgumentException"/> for a remote server.</summary>
    public static string ConnectionString(string server, string database) =>
        LocalSqlConnectionPolicy.Validate(new SqlConnectionStringBuilder
        {
            DataSource = server, InitialCatalog = database, IntegratedSecurity = true, ConnectTimeout = 5,
            ApplicationName = "EtpImportAudit", ApplicationIntent = ApplicationIntent.ReadOnly
        }.ConnectionString);

    public static async Task<ReadOnlyAuditConnection> OpenAsync(string server, string database, CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(ConnectionString(server, database));
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var audit = new ReadOnlyAuditConnection(connection, server, database);
            await audit.ExecuteSettingAsync("SET LOCK_TIMEOUT 5000;", cancellationToken).ConfigureAwait(false);
            return audit;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Runs one read-only text and returns its first result set as rows of values (DBNull as null).</summary>
    public async Task<IReadOnlyList<object?[]>> QueryAsync(string sql, Action<SqlParameterCollection>? bind = null,
        CancellationToken cancellationToken = default)
    {
        var sets = await QuerySetsAsync(sql, bind, cancellationToken).ConfigureAwait(false);
        return sets.Count == 0 ? [] : sets[0].Rows;
    }

    /// <summary>Runs one read-only text and returns every result set, with its column names.</summary>
    public async Task<IReadOnlyList<(IReadOnlyList<string> Columns, IReadOnlyList<object?[]> Rows)>> QuerySetsAsync(string sql,
        Action<SqlParameterCollection>? bind = null, CancellationToken cancellationToken = default)
    {
        ReadOnlySqlGuard.Require(sql);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        bind?.Invoke(command.Parameters);
        var sets = new List<(IReadOnlyList<string>, IReadOnlyList<object?[]>)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        do
        {
            var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
            var rows = new List<object?[]>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var values = new object?[reader.FieldCount];
                for (var i = 0; i < values.Length; i++) values[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(values);
            }
            if (columns.Length > 0) sets.Add((columns, rows));
        } while (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false));
        return sets;
    }

    private async Task ExecuteSettingAsync(string sql, CancellationToken cancellationToken)
    {
        ReadOnlySqlGuard.Require(sql);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => connection.DisposeAsync();
}
