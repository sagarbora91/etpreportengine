namespace Etp.Reporting.Application.Access;

public enum AccessRole
{
    None,
    Viewer,
    StoreManager,
    Owner
}

public sealed record AccessSession(
    string WindowsIdentity,
    string DisplayName,
    AccessRole Role,
    bool IsActive)
{
    public bool HasAssignedRole => Role is not AccessRole.None;
    public bool CanView => IsActive && HasAssignedRole;
    public bool CanImport => IsActive && Role is AccessRole.StoreManager or AccessRole.Owner;
    public bool CanEnterOperations => CanImport;
    public bool CanAdminister => IsActive && Role is AccessRole.Owner;
}

public interface IAccessSessionQuery
{
    Task<AccessSession> LoadCurrentAsync(CancellationToken cancellationToken = default);
}
