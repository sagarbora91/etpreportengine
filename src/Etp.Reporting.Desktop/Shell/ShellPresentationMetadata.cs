namespace Etp.Reporting.Desktop;

public sealed record ShellPresentationMetadata(
    string Destination,
    string ModuleId,
    string Description,
    string Heading,
    string Message,
    string ActionLabel,
    string ActionDestination)
{
    public static ShellPresentationMetadata From(ShellRouteDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return new(
            descriptor.Destination,
            descriptor.ModuleId,
            descriptor.Description,
            descriptor.Heading,
            descriptor.Message,
            descriptor.ActionLabel,
            descriptor.ActionDestination);
    }
}
