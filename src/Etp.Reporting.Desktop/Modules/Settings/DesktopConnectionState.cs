using Etp.Reporting.Infrastructure.SqlServer.Connections;

namespace Etp.Reporting.Desktop.Modules.Settings;

/// <summary>
/// The connection the running application uses. Settings changes it; everything else reads <see cref="Factory"/>
/// through the composition root (1.12 A1), so a screen never holds a connection string.
/// </summary>
public sealed class DesktopConnectionState
{
    public DesktopConnectionState(string defaultConnectionString)
    {
        var validation = ConnectionStringValidation.Validate(defaultConnectionString);
        if (!validation.IsValid)
            throw new ArgumentException(validation.Error, nameof(defaultConnectionString));

        ConnectionString = validation.ConnectionString!;
        Factory = new EtpConnectionFactory(ConnectionString);
    }

    /// <summary>The validated string, for Settings (the connection editor) and the composition root only.</summary>
    public string ConnectionString { get; private set; }

    /// <summary>The factory for the current connection; replaced when Settings accepts a new one.</summary>
    public IEtpConnectionFactory Factory { get; private set; }

    public bool TryUpdate(string? connectionString, out string? error)
    {
        var validation = ConnectionStringValidation.Validate(connectionString);
        error = validation.Error;
        if (!validation.IsValid) return false;

        ConnectionString = validation.ConnectionString!;
        Factory = new EtpConnectionFactory(ConnectionString);
        return true;
    }
}
