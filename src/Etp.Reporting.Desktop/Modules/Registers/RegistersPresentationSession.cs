extern alias EtpApplication;

using DigitalRegisterEntry = EtpApplication::Etp.Reporting.Application.Registers.DigitalRegisterEntry;
using DigitalRegisterEntryDraft = EtpApplication::Etp.Reporting.Application.Registers.DigitalRegisterEntryDraft;
using DigitalRegisterService = EtpApplication::Etp.Reporting.Application.Registers.IDigitalRegisterService;

namespace Etp.Reporting.Desktop.Modules.Registers;

public sealed class RegistersPresentationSession
{
    private readonly Func<DigitalRegisterService> serviceFactory;

    public RegistersPresentationSession(Func<DigitalRegisterService> serviceFactory) =>
        this.serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));

    public IReadOnlyList<DigitalRegisterEntry> Entries { get; private set; } = [];

    public async Task<IReadOnlyList<DigitalRegisterEntry>> RefreshAsync(
        string? search,
        CancellationToken cancellationToken = default,
        string? registerType = null)
    {
        Entries = await serviceFactory().LoadAsync(search, cancellationToken: cancellationToken, registerType: registerType).ConfigureAwait(false);
        return Entries;
    }

    public Task<long> SaveAsync(
        DigitalRegisterEntryDraft entry,
        string reason,
        CancellationToken cancellationToken = default) =>
        serviceFactory().SaveAsync(entry, reason, cancellationToken);
}
