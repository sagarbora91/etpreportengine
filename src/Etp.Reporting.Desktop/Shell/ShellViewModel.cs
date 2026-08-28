using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Etp.Reporting.Desktop;

public sealed class ShellViewModel : INotifyPropertyChanged
{
    private readonly IShellNavigationService navigation;
    private NavigationDecision lastNavigationDecision;
    private ShellPresentationMetadata currentPresentation;

    public ShellViewModel(IShellNavigationService navigation)
    {
        this.navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));

        var descriptor = ShellRouteRegistry.Find(navigation.Current.Destination)
            ?? throw new InvalidOperationException(
                $"The current shell destination '{navigation.Current.Destination}' is not registered.");

        currentPresentation = ShellPresentationMetadata.From(descriptor);
        lastNavigationDecision = NavigationDecision.Allowed(navigation.Current, descriptor);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public WorkspaceRoute CurrentRoute => navigation.Current;
    public IReadOnlyList<WorkspaceRoute> History => navigation.History;
    public bool CanGoBack => navigation.CanGoBack;
    public bool CanGoForward => navigation.CanGoForward;

    public NavigationDecision LastNavigationDecision
    {
        get => lastNavigationDecision;
        private set => SetField(ref lastNavigationDecision, value);
    }

    public ShellPresentationMetadata CurrentPresentation
    {
        get => currentPresentation;
        private set => SetField(ref currentPresentation, value);
    }

    public NavigationDecision Navigate(WorkspaceRoute route, ShellAccess access) =>
        Apply(() => navigation.Navigate(route, access));

    public NavigationDecision GoBack(ShellAccess access) =>
        Apply(() => navigation.GoBack(access));

    public NavigationDecision GoForward(ShellAccess access) =>
        Apply(() => navigation.GoForward(access));

    private NavigationDecision Apply(Func<NavigationDecision> navigate)
    {
        var previousRoute = CurrentRoute;
        var couldGoBack = CanGoBack;
        var couldGoForward = CanGoForward;
        var decision = navigate();

        LastNavigationDecision = decision;
        if (decision.IsAllowed && decision.Descriptor is { } descriptor)
            CurrentPresentation = ShellPresentationMetadata.From(descriptor);

        if (previousRoute != CurrentRoute) OnPropertyChanged(nameof(CurrentRoute));
        if (couldGoBack != CanGoBack) OnPropertyChanged(nameof(CanGoBack));
        if (couldGoForward != CanGoForward) OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(History));

        return decision;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
