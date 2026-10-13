using System.Windows;
using System.Windows.Controls;

namespace Etp.Reporting.Desktop;

/// <summary>
/// One shell-wide notice shown above every screen (1.12 A1 seam): an update ready to install (B2), a shop result
/// waiting (U1), and the like. The banner owns its own content and its actions; the shell only shows and hides it.
/// Follow <c>UIUX\ETP-DESIGN-SYSTEM.md</c> (decisions 45/46) for its look.
/// </summary>
public interface IShellBanner
{
    /// <summary>A stable key ("update-ready"), so the same notice is never shown twice.</summary>
    string Key { get; }

    /// <summary>Higher shows first when several notices want the slot.</summary>
    int Priority { get; }

    /// <summary>The banner's content, created once and reused while it is shown.</summary>
    UIElement Content { get; }

    /// <summary>Raised when the banner is finished (acted on or dismissed); the shell then removes it.</summary>
    event EventHandler? Closed;
}

/// <summary>
/// The <c>ShellBannerHost</c> slot of <c>MainWindow.xaml</c>: holds the notices and shows the one with the highest
/// priority (the earliest of equal priority). Empty means collapsed, so no space is taken.
/// </summary>
public sealed class ShellBannerSlot
{
    private readonly ContentControl host;
    private readonly List<IShellBanner> banners = [];

    public ShellBannerSlot(ContentControl host)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        Refresh();
    }

    public IShellBanner? Current { get; private set; }

    public IReadOnlyList<IShellBanner> Pending => banners;

    /// <summary>Adds a notice; one with the same key replaces the earlier one.</summary>
    public void Show(IShellBanner banner)
    {
        ArgumentNullException.ThrowIfNull(banner);
        var index = banners.FindIndex(existing => existing.Key == banner.Key);
        if (index >= 0)
        {
            banners[index].Closed -= OnClosed;
            banners[index] = banner;
        }
        else banners.Add(banner);
        banner.Closed += OnClosed;
        Refresh();
    }

    public void Remove(string key)
    {
        var index = banners.FindIndex(existing => existing.Key == key);
        if (index < 0) return;
        banners[index].Closed -= OnClosed;
        banners.RemoveAt(index);
        Refresh();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (sender is IShellBanner banner) Remove(banner.Key);
    }

    private void Refresh()
    {
        // OrderBy is stable, so the earliest of equal priority stays first.
        Current = banners.OrderByDescending(banner => banner.Priority).FirstOrDefault();
        host.Content = Current?.Content;
        host.Visibility = Current is null ? Visibility.Collapsed : Visibility.Visible;
    }
}

public partial class MainWindow
{
    private ShellBannerSlot? shellBanners;

    /// <summary>The shell's notice slot (1.12 A1). Wave-2 lanes show an <see cref="IShellBanner"/> here.</summary>
    internal ShellBannerSlot ShellBanners => shellBanners ??= new ShellBannerSlot(ShellBannerHost);
}
