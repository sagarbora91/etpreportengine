using Etp.Reporting.Infrastructure.SqlServer.Connections;

namespace Etp.Reporting.Desktop.Modules.Settings;

/// <summary>
/// The Settings screen's verdict on a typed connection string. Since 1.12 (A1) the parsing and the endpoint policy
/// live in <see cref="SettingsConnectionCheck"/> (Infrastructure); the sentences are unchanged.
/// </summary>
public sealed record ConnectionStringValidation(
    bool IsValid,
    string? ConnectionString,
    string? Error)
{
    public static ConnectionStringValidation Validate(string? value)
    {
        var (connectionString, error) = SettingsConnectionCheck.Check(value);
        return connectionString is null ? new(false, null, error) : new(true, connectionString, null);
    }
}
