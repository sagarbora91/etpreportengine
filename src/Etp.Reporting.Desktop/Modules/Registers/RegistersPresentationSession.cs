extern alias EtpApplication;

using DigitalRegisterEntry = EtpApplication::Etp.Reporting.Application.Registers.DigitalRegisterEntry;
using DigitalRegisterEntryDraft = EtpApplication::Etp.Reporting.Application.Registers.DigitalRegisterEntryDraft;
using DigitalRegisterService = EtpApplication::Etp.Reporting.Application.Registers.IDigitalRegisterService;

namespace Etp.Reporting.Desktop.Modules.Registers;

public sealed class RegistersPresentationSession
{
    private readonly Func<string, DigitalRegisterService> serviceFactory;

    public RegistersPresentationSession(Func<string, DigitalRegisterService> serviceFactory) =>
        this.serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));

    public IReadOnlyList<DigitalRegisterEntry> Entries { get; private set; } = [];

    public async Task<IReadOnlyList<DigitalRegisterEntry>> RefreshAsync(
        string connectionString,
        string? search,
        CancellationToken cancellationToken = default)
    {
        Entries = await serviceFactory(connectionString).LoadAsync(search, cancellationToken: cancellationToken).ConfigureAwait(false);
        return Entries;
    }

    public Task<long> SaveAsync(
        string connectionString,
        DigitalRegisterEntryDraft entry,
        string reason,
        CancellationToken cancellationToken = default) =>
        serviceFactory(connectionString).SaveAsync(entry, reason, cancellationToken);
}
