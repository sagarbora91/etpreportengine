using App = Etp.Reporting.Application.Distribution;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed class SqlServerInvestigationQuery : App.IInvestigationQuery
{
    private readonly IDistributionSqlGateway gateway;
    private readonly Func<CancellationToken, Task<ApplicationAccess>> loadAccess;

    public SqlServerInvestigationQuery(string connectionString)
    {
        var validated = SqlAdapterConnection.RequireWindowsIntegrated(connectionString, nameof(connectionString));
        gateway = new ProductisationDistributionGateway(new ProductisationRepository(validated));
        loadAccess = new Phase2OperationsRepository(validated).LoadCurrentAccessAsync;
    }

    internal SqlServerInvestigationQuery(
        IDistributionSqlGateway gateway,
        Func<CancellationToken, Task<ApplicationAccess>> loadAccess)
    {
        this.gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        this.loadAccess = loadAccess ?? throw new ArgumentNullException(nameof(loadAccess));
    }

    public async Task<IReadOnlyList<App.InvestigationHit>> SearchAsync(
        string term,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        await RequireViewAsync(loadAccess, cancellationToken).ConfigureAwait(false);
        return (await gateway.SearchAsync(term, limit, cancellationToken).ConfigureAwait(false))
            .Select(row => new App.InvestigationHit(row.ResultType, row.PrimaryReference, row.Scope,
                row.BusinessDate, row.Summary, row.NavigationHint))
            .ToArray();
    }

    internal static async Task RequireViewAsync(
        Func<CancellationToken, Task<ApplicationAccess>> loadAccess,
        CancellationToken cancellationToken)
    {
        if (!(await loadAccess(cancellationToken).ConfigureAwait(false)).CanView)
            throw new UnauthorizedAccessException("This Windows account does not have application access.");
    }
}

public sealed class SqlServerReportDistributionService : App.IReportDistributionService<ReportPackDocument>
{
    private readonly IDistributionSqlGateway gateway;
    private readonly Func<CancellationToken, Task<ApplicationAccess>> loadAccess;
    private readonly Func<string, ReportPackDocument, int, string, bool, string, CancellationToken, Task<ReportPackageResult>> createPackage;
    private readonly Func<string, bool> fileExists;
    private readonly Func<string, long> fileLength;

    public SqlServerReportDistributionService(string connectionString)
    {
        var validated = SqlAdapterConnection.RequireWindowsIntegrated(connectionString, nameof(connectionString));
        gateway = new ProductisationDistributionGateway(new ProductisationRepository(validated));
        loadAccess = new Phase2OperationsRepository(validated).LoadCurrentAccessAsync;
        var packages = new ReportPackageService();
        createPackage = packages.CreateAsync;
        fileExists = File.Exists;
        fileLength = path => new FileInfo(path).Length;
    }

    internal SqlServerReportDistributionService(
        IDistributionSqlGateway gateway,
        Func<CancellationToken, Task<ApplicationAccess>> loadAccess,
        Func<string, ReportPackDocument, int, string, bool, string, CancellationToken, Task<ReportPackageResult>> createPackage,
        Func<string, bool>? fileExists = null,
        Func<string, long>? fileLength = null)
    {
        this.gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        this.loadAccess = loadAccess ?? throw new ArgumentNullException(nameof(loadAccess));
        this.createPackage = createPackage ?? throw new ArgumentNullException(nameof(createPackage));
        this.fileExists = fileExists ?? File.Exists;
        this.fileLength = fileLength ?? (path => new FileInfo(path).Length);
    }

    public async Task<App.ReportPackageReceipt> CreatePackageAsync(
        App.CreateReportPackage<ReportPackDocument> command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.GenerationId <= 0) throw new ArgumentOutOfRangeException(nameof(command), "A report generation is required.");
        await SqlServerInvestigationQuery.RequireViewAsync(loadAccess, cancellationToken).ConfigureAwait(false);

        var result = await createPackage(command.OutputPath, command.Document, command.GenerationNumber,
            command.StoreCode, command.IsFinal, command.CreatedBy, cancellationToken).ConfigureAwait(false);
        await gateway.RecordPackageAsync(command.GenerationId,
            string.Equals(command.StoreCode, "COMBINED", StringComparison.OrdinalIgnoreCase) ? "COMBINED" : "DAILY",
            result.Path, result.ManifestJson, result.Sha256, command.IsFinal, cancellationToken).ConfigureAwait(false);
        return new(result.Path, result.Sha256, result.ManifestJson,
            result.Files.Select(file => new App.ReportPackageFile(file.RelativePath, file.SizeBytes, file.Sha256)).ToArray());
    }

    public async Task<App.EmailAttachmentPolicy> ValidateEmailAttachmentAsync(
        string attachmentPath,
        CancellationToken cancellationToken = default)
    {
        await SqlServerInvestigationQuery.RequireViewAsync(loadAccess, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(attachmentPath) || !fileExists(attachmentPath))
            throw new FileNotFoundException("The report attachment was not found.", attachmentPath);
        var settings = await gateway.LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        if (fileLength(attachmentPath) > settings.MaximumAttachmentMb * 1024L * 1024L)
            throw new InvalidOperationException($"The attachment exceeds the configured {settings.MaximumAttachmentMb} MB email limit.");
        return new(settings.ShareFolderPath, settings.MaximumAttachmentMb);
    }

    public async Task RecordAttemptAsync(
        App.RecordDistributionAttempt command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await SqlServerInvestigationQuery.RequireViewAsync(loadAccess, cancellationToken).ConfigureAwait(false);
        await gateway.RecordShareAttemptAsync(command.GenerationId, command.PackageId, command.Channel,
            command.DestinationSafe, command.AttachmentPath, command.Outcome, command.SafeMessage,
            cancellationToken).ConfigureAwait(false);
    }
}

internal interface IDistributionSqlGateway
{
    Task<IReadOnlyList<InvestigationResult>> SearchAsync(string term, int limit, CancellationToken cancellationToken);
    Task<ProductSettings> LoadSettingsAsync(CancellationToken cancellationToken);
    Task RecordPackageAsync(long generationId, string packageType, string path, string manifestJson, string sha256, bool isFinal, CancellationToken cancellationToken);
    Task RecordShareAttemptAsync(long generationId, long? packageId, string channel, string? destinationSafe, string attachmentName, string outcome, string message, CancellationToken cancellationToken);
}

internal sealed class ProductisationDistributionGateway(ProductisationRepository repository) : IDistributionSqlGateway
{
    public Task<IReadOnlyList<InvestigationResult>> SearchAsync(string term, int limit, CancellationToken token) => repository.SearchAsync(term, limit, token);
    public Task<ProductSettings> LoadSettingsAsync(CancellationToken token) => repository.LoadSettingsAsync(token);
    public Task RecordPackageAsync(long generationId, string packageType, string path, string manifestJson, string sha256, bool isFinal, CancellationToken token) => repository.RecordPackageAsync(generationId, packageType, path, manifestJson, sha256, isFinal, token);
    public Task RecordShareAttemptAsync(long generationId, long? packageId, string channel, string? destinationSafe, string attachmentName, string outcome, string message, CancellationToken token) => repository.RecordShareAttemptAsync(generationId, packageId, channel, destinationSafe, attachmentName, outcome, message, token);
}
