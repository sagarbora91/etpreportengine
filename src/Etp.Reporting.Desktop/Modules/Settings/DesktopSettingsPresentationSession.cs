extern alias EtpApplication;

using System.Globalization;
using System.IO;
using ProductConfiguration = EtpApplication::Etp.Reporting.Application.OperationsAdministration.ProductConfiguration;
using SaveProductConfiguration = EtpApplication::Etp.Reporting.Application.OperationsAdministration.SaveProductConfiguration;

namespace Etp.Reporting.Desktop.Modules.Settings;

public sealed record DesktopConnectionCandidate(
    bool IsValid,
    string? ConnectionString,
    string? Error);

public sealed record DesktopConnectionPresentationState(
    bool IsConnected,
    string ConnectionResult,
    string ConnectionStatus,
    string ImportStatus,
    string ApplicationStatus,
    bool SettingsPersisted);

public sealed record DesktopProductSettingsPresentation(
    string DocumentRepositoryPath,
    string ShareFolderPath,
    string OcrHelperPath,
    string OcrModelPath,
    string SmtpHost,
    string SmtpPort,
    string SmtpFromAddress,
    string MaximumAttachmentMb);

public sealed class DesktopSettingsPresentationSession
{
    private const string SettingsWarning =
        "Connection succeeded, but the local settings file could not be updated safely.";

    private readonly DesktopSettingsStore store;
    private readonly DesktopConnectionState connectionState;

    public DesktopSettingsPresentationSession(
        DesktopSettingsStore store,
        DesktopConnectionState connectionState)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.connectionState = connectionState ?? throw new ArgumentNullException(nameof(connectionState));
        Current = new(false, string.Empty, "Connection failed",
            "Waiting for a valid Windows-integrated connection", string.Empty, false);
    }

    public string ConnectionString => connectionState.ConnectionString;

    public DesktopConnectionPresentationState Current { get; private set; }

    public DesktopProductSettingsPresentation? ProductSettings { get; private set; }

    public string LoadConnectionString()
    {
        var saved = store.Load();
        if (saved is not null) connectionState.TryUpdate(saved.ConnectionString, out _);
        return connectionState.ConnectionString;
    }

    public DesktopConnectionCandidate ValidateCandidate(string? value)
    {
        var validation = ConnectionStringValidation.Validate(value);
        if (validation.IsValid)
            return new(true, validation.ConnectionString, null);

        Current = new(false, validation.Error ?? "The connection string is invalid.", "Connection failed",
            "Waiting for a valid Windows-integrated connection",
            validation.Error ?? "The connection string is invalid.", false);
        return new(false, null, validation.Error);
    }

    public DesktopConnectionPresentationState CompleteHealthCheck(
        DesktopConnectionCandidate candidate,
        bool connected,
        string message,
        string? serverVersion)
    {
        RequireValid(candidate);
        var persisted = connected && AcceptAndPersist(candidate.ConnectionString!);
        var applicationStatus = connected
            ? $"Connected to SQL Server {serverVersion}."
            : message;
        if (connected && !persisted) applicationStatus = SettingsWarning;

        Current = new(
            connected,
            message,
            connected ? "Connected" : "Connection failed",
            connected ? "Ready to validate or report" : "Waiting for connection",
            applicationStatus,
            persisted);
        return Current;
    }

    public DesktopConnectionPresentationState CompleteBootstrap(
        DesktopConnectionCandidate candidate,
        string resultMessage)
    {
        RequireValid(candidate);
        var persisted = AcceptAndPersist(candidate.ConnectionString!);
        Current = new(true, resultMessage, "Connected", "Ready to import",
            persisted ? resultMessage : SettingsWarning, persisted);
        return Current;
    }

    public DesktopProductSettingsPresentation ShowProductSettings(ProductConfiguration settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ProductSettings = new(
            settings.DocumentRepositoryPath,
            settings.ShareFolderPath,
            settings.OcrHelperPath ?? string.Empty,
            settings.OcrModelPath ?? string.Empty,
            settings.SmtpHost ?? string.Empty,
            settings.SmtpPort?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            settings.SmtpFromAddress ?? string.Empty,
            settings.MaximumAttachmentMb.ToString(CultureInfo.InvariantCulture));
        return ProductSettings;
    }

    public static SaveProductConfiguration CreateProductConfiguration(
        string documentRepositoryPath,
        string shareFolderPath,
        string ocrHelperPath,
        string ocrModelPath,
        string smtpHost,
        string smtpPortText,
        string smtpFromAddress,
        string maximumAttachmentText,
        string reason)
    {
        if (!int.TryParse(maximumAttachmentText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maximum))
            throw new InvalidOperationException("Enter a valid maximum attachment size in MB.");

        int? port = null;
        if (!string.IsNullOrWhiteSpace(smtpPortText))
        {
            if (!int.TryParse(smtpPortText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPort))
                throw new InvalidOperationException("Enter a valid SMTP port.");
            port = parsedPort;
        }

        return new(documentRepositoryPath, shareFolderPath, ocrHelperPath, ocrModelPath,
            smtpHost, port, true, smtpFromAddress, maximum, reason);
    }

    private bool AcceptAndPersist(string connectionString)
    {
        if (!connectionState.TryUpdate(connectionString, out var error))
            throw new InvalidOperationException(error);

        try
        {
            store.Save(connectionState.ConnectionString);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    private static void RequireValid(DesktopConnectionCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!candidate.IsValid || string.IsNullOrWhiteSpace(candidate.ConnectionString))
            throw new InvalidOperationException(candidate.Error ?? "The connection string is invalid.");
    }
}
