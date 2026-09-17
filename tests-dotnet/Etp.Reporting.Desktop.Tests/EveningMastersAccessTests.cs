using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Etp.Reporting.Desktop.Modules.Settings;

namespace Etp.Reporting.Desktop.Tests;

public sealed class EveningMastersAccessTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Brand_editor_keeps_monthly_targets_owner_only(bool owner, bool canEditBrands)
    {
        RunSta(() =>
        {
            var view = new EveningMastersView(() => throw new InvalidOperationException("No database call expected."),
                () => owner, () => canEditBrands);

            Assert.Equal(canEditBrands, FindButton(view, "New row").IsEnabled);
            Assert.Equal(canEditBrands, FindButton(view, "Save brand row").IsEnabled);
            Assert.Equal(canEditBrands, FindButton(view, "Refresh").IsEnabled);
            Assert.Equal(owner, FindButton(view, "Save monthly target").IsEnabled);
        });
    }

    [Fact]
    public void Late_role_assignment_and_revocation_refresh_embedded_owner_controls()
    {
        RunSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "EtpMastersAccessTests", Guid.NewGuid().ToString("N"));
            var settings = new SettingsWorkspaceView(
                new(new DesktopSettingsStore(root), new DesktopConnectionState(
                    @"Server=.\SQLEXPRESS;Database=EtpReporting;Integrated Security=True;TrustServerCertificate=True")),
                _ => throw new InvalidOperationException("No lifecycle call expected."),
                _ => throw new InvalidOperationException("No administration call expected."), root);
            var embedded = ((Panel)settings.FindName("DataTruthMastersHost")).Children.OfType<EveningMastersView>().Single();

            Assert.False(FindButton(embedded, "Save brand row").IsEnabled);
            Assert.False(FindButton(embedded, "Save monthly target").IsEnabled);
            settings.UpdateAccess(new(true, true));
            Assert.True(FindButton(embedded, "Save brand row").IsEnabled);
            Assert.True(FindButton(embedded, "Save monthly target").IsEnabled);
            settings.UpdateAccess(new(true, false));
            Assert.False(FindButton(embedded, "Save brand row").IsEnabled);
            Assert.False(FindButton(embedded, "Save monthly target").IsEnabled);

            var managerEditor = settings.CreateEveningMastersView(() => true);
            Assert.True(FindButton(managerEditor, "Save brand row").IsEnabled);
            Assert.False(FindButton(managerEditor, "Save monthly target").IsEnabled);
        });
    }

    [Fact]
    public void Revoked_brand_permission_is_rechecked_before_action_and_updates_buttons()
    {
        RunSta(() =>
        {
            var permitted = true;
            var connectionRequests = 0;
            var view = new EveningMastersView(() => { connectionRequests++; throw new InvalidOperationException("No database call expected."); },
                () => false, () => permitted);
            var save = FindButton(view, "Save brand row");
            Assert.True(save.IsEnabled);

            permitted = false;
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(0, connectionRequests);
            Assert.False(save.IsEnabled);
            Assert.False(FindButton(view, "New row").IsEnabled);
            Assert.False(FindButton(view, "Save monthly target").IsEnabled);
        });
    }

    private static Button FindButton(DependencyObject root, string title) =>
        Descendants(root).OfType<Button>().Single(button => Equals(button.Content, title));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "STA test did not complete.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
