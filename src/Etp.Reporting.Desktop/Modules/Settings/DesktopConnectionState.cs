namespace Etp.Reporting.Desktop.Modules.Settings;

public sealed class DesktopConnectionState
{
    public DesktopConnectionState(string defaultConnectionString)
    {
        var validation = ConnectionStringValidation.Validate(defaultConnectionString);
        if (!validation.IsValid)
            throw new ArgumentException(validation.Error, nameof(defaultConnectionString));

        ConnectionString = validation.ConnectionString!;
    }

    public string ConnectionString { get; private set; }

    public bool TryUpdate(string? connectionString, out string? error)
    {
        var validation = ConnectionStringValidation.Validate(connectionString);
        error = validation.Error;
        if (!validation.IsValid) return false;

        ConnectionString = validation.ConnectionString!;
        return true;
    }
}
