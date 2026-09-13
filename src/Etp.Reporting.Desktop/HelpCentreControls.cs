using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Etp.Reporting.Desktop;

public sealed record HelpNavigationEventArgs(string TopicId, string? Destination = null, string? FeatureCode = null);

public sealed class HelpCentreView : UserControl
{
    private readonly TextBox searchInput = new();
    private readonly WrapPanel tiles = new();
    private readonly ContentControl contentHost = new();
    private readonly TextBlock resultSummary = new();
    private string currentTopicId = HelpCentreRegistry.HomeTopicId;
    private string? currentCategory;

    public event EventHandler<HelpNavigationEventArgs>? NavigationRequested;
    public event EventHandler? CloseRequested;
    public Func<string, bool> CanNavigateTopic { get; set; } = _ => true;

    public string CurrentTopicId => currentTopicId;

    public HelpCentreView()
    {
        Focusable = true;
        Content = BuildLayout();
        ShowHome();
        AutomationProperties.SetName(this, "Help Centre");
    }

    public void OpenTopic(string? topicId)
    {
        if (string.IsNullOrWhiteSpace(topicId) || topicId == HelpCentreRegistry.HomeTopicId) { ShowHome(); return; }
        var topic = HelpCentreRegistry.Find(topicId);
        if (topic is null) { ShowHome(); return; }
        var shouldMoveFocus = IsKeyboardFocusWithin || !IsVisible;
        currentTopicId = topic.Id;
        contentHost.Content = topic.Id == HelpCentreRegistry.KeyboardShortcutsTopicId
            ? BuildKeyboardShortcutsTopic()
            : BuildTopic(topic);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (shouldMoveFocus && IsVisible)
                contentHost.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        });
    }

    public void ShowContextHelp(string? destination, string? featureCode = null) =>
        OpenTopic(ContextHelpRouter.ResolveTopicId(destination, featureCode));

    private UIElement BuildLayout()
    {
        var root = new Grid { Background = Brush("WorkspaceBackground", Brushes.White) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());

        var header = new Border { Background = Brush("Surface", Brushes.White), BorderBrush = Brush("Divider", Brushes.LightGray), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(16, 8, 16, 8) };
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition());
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = "Help Centre", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brush("PrimaryText", Brushes.Black), VerticalAlignment = VerticalAlignment.Center });
        headerGrid.Children.Add(heading);
        var close = new Button { Content = "Close", MinWidth = 76, VerticalAlignment = VerticalAlignment.Center };
        close.SetResourceReference(MinHeightProperty, "ActiveTargetHeight");
        close.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        AutomationProperties.SetName(close, "Close Help Centre");
        var categories = new Button { Content = "Categories", Margin = new Thickness(0,0,8,0) };
        categories.SetResourceReference(MinHeightProperty, "ActiveTargetHeight");
        categories.Click += (_, _) => { currentCategory = null; searchInput.Clear(); ShowHome(); PopulateTiles(HelpCentreRegistry.Topics); };
        var headerActions = new StackPanel { Orientation = Orientation.Horizontal }; headerActions.Children.Add(categories); headerActions.Children.Add(close);
        Grid.SetColumn(headerActions, 1); headerGrid.Children.Add(headerActions); header.Child = headerGrid; root.Children.Add(header);

        contentHost.Margin = new Thickness(16, 10, 16, 12);
        Grid.SetRow(contentHost, 1); root.Children.Add(contentHost);
        return root;
    }

    private void ShowHome()
    {
        if (currentTopicId == HelpCentreRegistry.HomeTopicId && contentHost.Content is Grid)
        {
            searchInput.Focus();
            return;
        }
        currentTopicId = HelpCentreRegistry.HomeTopicId;
        (searchInput.Parent as Panel)?.Children.Remove(searchInput);
        (resultSummary.Parent as Panel)?.Children.Remove(resultSummary);
        if (tiles.Parent is ScrollViewer oldScroll) oldScroll.Content = null;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());

        searchInput.SetResourceReference(MinHeightProperty, "ActiveTargetHeight");
        searchInput.Padding = new Thickness(12, 8, 12, 8);
        searchInput.FontSize = 14;
        searchInput.ToolTip = "Search help topics";
        AutomationProperties.SetName(searchInput, "Search Help");
        searchInput.TextChanged -= SearchInput_TextChanged;
        searchInput.TextChanged += SearchInput_TextChanged;
        root.Children.Add(searchInput);

        resultSummary.Margin = new Thickness(2, 10, 0, 10);
        resultSummary.Foreground = Brush("SecondaryText", Brushes.DimGray);
        AutomationProperties.SetLiveSetting(resultSummary, AutomationLiveSetting.Polite);
        Grid.SetRow(resultSummary, 1); root.Children.Add(resultSummary);

        tiles.HorizontalAlignment = HorizontalAlignment.Stretch;
        var scroll = new ScrollViewer { Content = tiles, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 2); root.Children.Add(scroll);
        contentHost.Content = root;
        PopulateTiles(HelpCentreRegistry.Topics);
        searchInput.Focus();
    }

    private void SearchInput_TextChanged(object sender, TextChangedEventArgs e) => PopulateTiles(HelpCentreRegistry.Search(searchInput.Text));

    private void PopulateTiles(IReadOnlyList<HelpTopicDefinition> topics)
    {
        tiles.Children.Clear();
        if (string.IsNullOrWhiteSpace(searchInput.Text) && currentCategory is null)
        {
            resultSummary.Text = "Choose a help category";
            foreach (var group in topics.GroupBy(topic => HelpCentreRegistry.Category(topic.Id)))
            {
                var button = new Button { Content = new TextBlock { Text = group.Key, TextWrapping = TextWrapping.Wrap, FontSize = 16 }, Width = 260, MinHeight = 88, Margin = new Thickness(6) };
                AutomationProperties.SetName(button, group.Key + " help category");
                button.Click += (_, _) => { currentCategory = group.Key; PopulateTiles(HelpCentreRegistry.Topics); };
                tiles.Children.Add(button);
            }
            return;
        }
        if (string.IsNullOrWhiteSpace(searchInput.Text) && currentCategory is not null)
        {
            topics = topics.Where(topic => HelpCentreRegistry.Category(topic.Id) == currentCategory).ToArray();
        }
        resultSummary.Text = topics.Count == 1 ? "1 help topic" : $"{topics.Count} help topics";
        foreach (var topic in topics)
        {
            var tile = new HelpTopicTile(topic) { Width = 260 };
            tile.Click += (_, _) => OpenTopic(topic.Id);
            tiles.Children.Add(tile);
        }
        if (topics.Count == 0)
            tiles.Children.Add(new EmptyState("No help topics found", "Try a module name, task or keyboard command.") { Width = 560, Margin = new Thickness(6) });
    }

    private UIElement BuildTopic(HelpTopicDefinition topic)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var back = new Button { Content = "← Help categories", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 4, 12, 4) };
        back.SetResourceReference(MinHeightProperty, "ActiveTargetHeight");
        back.Visibility = Visibility.Collapsed; // Categories is persistently available in the help action bar.
        back.Click += (_, _) => ShowHome();
        AutomationProperties.SetName(back, "Return to all Help Centre topics"); root.Children.Add(back);

        var card = new Border { Background = Brush("Surface", Brushes.White), BorderBrush = Brush("Divider", Brushes.LightGray), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Margin = new Thickness(0, 8, 0, 0) };
        var content = new StackPanel { MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left };
        content.Children.Add(new TextBlock { Text = topic.Title, FontSize = 25, FontWeight = FontWeights.SemiBold, Foreground = Brush("PrimaryText", Brushes.Black) });
        content.Children.Add(new TextBlock { Text = topic.Description, FontSize = 14, Foreground = Brush("SecondaryText", Brushes.DimGray), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 20) });
        content.Children.Add(new TextBlock { Text = topic.Overview, FontSize = 14, LineHeight = 22, Foreground = Brush("PrimaryText", Brushes.Black), TextWrapping = TextWrapping.Wrap });
        if (topic.Availability != HelpTopicAvailability.Available)
        {
            content.Children.Add(new Border
            {
                Background = Brush("SurfaceSecondary", Brushes.GhostWhite), CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Margin = new Thickness(0, 20, 0, 0),
                Child = new TextBlock { Text = "Detailed step-by-step guide coming soon", FontWeight = FontWeights.SemiBold, Foreground = Brush("SecondaryText", Brushes.DimGray) }
            });
        }
        if (!string.IsNullOrWhiteSpace(topic.Destination))
        {
            var open = new Button { Content = $"Open {topic.Title}", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 8, 0, 0) };
            open.SetResourceReference(MinHeightProperty, "ActiveTargetHeight");
            open.IsEnabled = CanNavigateTopic(topic.Id);
            if (!open.IsEnabled) open.ToolTip = "Your role can read this guide but cannot open its restricted task.";
            open.Click += (_, _) => NavigationRequested?.Invoke(this, new HelpNavigationEventArgs(topic.Id, topic.Destination, topic.FeatureCode));
            AutomationProperties.SetName(open, $"Open {topic.Title} workspace"); Grid.SetRow(open, 2); root.Children.Add(open);
        }
        card.Child = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(card, 1); root.Children.Add(card);
        return root;
    }

    private UIElement BuildKeyboardShortcutsTopic()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        var back = new Button { Content = "← Help categories", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 4, 12, 4) };
        back.SetResourceReference(MinHeightProperty, "ActiveTargetHeight");
        back.Visibility = Visibility.Collapsed;
        back.Click += (_, _) => ShowHome();
        AutomationProperties.SetName(back, "Return to all Help Centre topics");
        root.Children.Add(back);
        var shortcuts = new KeyboardShortcutsView { Margin = new Thickness(0, 14, 0, 0) };
        Grid.SetRow(shortcuts, 1); root.Children.Add(shortcuts);
        return root;
    }

    private static Brush Brush(string key, Brush fallback) => Application.Current?.TryFindResource(key) as Brush ?? fallback;
}

public sealed class HelpTopicTile : Button
{
    public HelpTopicDefinition Definition { get; }

    public HelpTopicTile(HelpTopicDefinition definition)
    {
        Definition = definition;
        MinHeight = 96;
        Margin = new Thickness(6);
        Padding = new Thickness(12);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        Background = Application.Current?.TryFindResource("Surface") as Brush ?? Brushes.White;
        BorderBrush = Application.Current?.TryFindResource("Divider") as Brush ?? Brushes.LightGray;
        BorderThickness = new Thickness(1);
        Content = BuildContent(definition);
        AutomationProperties.SetName(this, $"{definition.Title}. {definition.Description}. {StatusText(definition)}.");
    }

    private static UIElement BuildContent(HelpTopicDefinition definition)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = definition.Title, FontSize = 16, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, Foreground = Application.Current?.TryFindResource("PrimaryText") as Brush ?? Brushes.Black });
        content.Children.Add(new TextBlock { Text = definition.Description, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Foreground = Application.Current?.TryFindResource("SecondaryText") as Brush ?? Brushes.DimGray, Margin = new Thickness(0,6,0,0) });
        if (definition.Availability != HelpTopicAvailability.Available)
            content.Children.Add(new TextBlock { Text = StatusText(definition), FontSize = 12, Margin = new Thickness(0,6,0,0) });
        return content;
    }
    private static string StatusText(HelpTopicDefinition definition) => definition.Availability switch
    {
        HelpTopicAvailability.Available => "GUIDE AVAILABLE",
        HelpTopicAvailability.ComingSoon => "COMING SOON",
        _ => "OVERVIEW AVAILABLE"
    };
}

public sealed class KeyboardShortcutsView : UserControl
{
    private readonly TextBox search = new();
    private readonly ComboBox category = new();
    private readonly StackPanel results = new();
    private readonly TextBlock summary = new();

    public KeyboardShortcutsView()
    {
        Content = BuildLayout();
        Populate();
        AutomationProperties.SetName(this, "Keyboard Shortcuts guide");
    }

    private UIElement BuildLayout()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.Children.Add(new TextBlock { Text = "Keyboard Shortcuts", FontSize = 25, FontWeight = FontWeights.SemiBold, Foreground = Brush("PrimaryText", Brushes.Black) });

        var filters = new Grid { Margin = new Thickness(0, 14, 0, 8) };
        filters.ColumnDefinitions.Add(new ColumnDefinition());
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        search.MinHeight = 40; search.Padding = new Thickness(11, 7, 11, 7); search.ToolTip = "Search keys or actions";
        AutomationProperties.SetName(search, "Search keyboard shortcuts"); search.TextChanged += (_, _) => Populate(); filters.Children.Add(search);
        category.MinHeight = 40; category.Margin = new Thickness(10, 0, 0, 0); category.Items.Add("All");
        foreach (var value in HelpCentreRegistry.Shortcuts.Select(x => x.Category).Distinct()) category.Items.Add(value);
        category.SelectedIndex = 0; category.SelectionChanged += (_, _) => Populate(); AutomationProperties.SetName(category, "Shortcut category");
        Grid.SetColumn(category, 1); filters.Children.Add(category); Grid.SetRow(filters, 1); root.Children.Add(filters);

        summary.Margin = new Thickness(2, 0, 0, 8); summary.Foreground = Brush("SecondaryText", Brushes.DimGray); AutomationProperties.SetLiveSetting(summary, AutomationLiveSetting.Polite);
        Grid.SetRow(summary, 2); root.Children.Add(summary);
        var scroll = new ScrollViewer { Content = results, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 3); root.Children.Add(scroll);
        return root;
    }

    private void Populate()
    {
        if (category.SelectedItem is null) return;
        var matches = HelpCentreRegistry.SearchShortcuts(search.Text, category.SelectedItem.ToString());
        results.Children.Clear(); summary.Text = matches.Count == 1 ? "1 shortcut" : $"{matches.Count} shortcuts";
        foreach (var group in matches.GroupBy(x => x.Category))
        {
            results.Children.Add(new TextBlock { Text = group.Key.ToUpperInvariant(), FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brush("SecondaryText", Brushes.DimGray), Margin = new Thickness(2, 14, 0, 6) });
            foreach (var shortcut in group) results.Children.Add(BuildShortcutRow(shortcut));
        }
    }

    private static UIElement BuildShortcutRow(ShortcutDefinition shortcut)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        var keys = new Border { Background = Brush("SurfaceSecondary", Brushes.GhostWhite), BorderBrush = Brush("Divider", Brushes.LightGray), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(9, 6, 9, 6), HorizontalAlignment = HorizontalAlignment.Left, Child = new TextBlock { Text = shortcut.Keys, FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.SemiBold, Foreground = Brush("PrimaryText", Brushes.Black) } };
        row.Children.Add(keys);
        var action = new TextBlock { Text = shortcut.Action, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("PrimaryText", Brushes.Black), Margin = new Thickness(8, 0, 8, 0) };
        Grid.SetColumn(action, 1); row.Children.Add(action);
        var scope = new TextBlock { Text = shortcut.Scope + (shortcut.RequiresPermission ? " · permission required" : string.Empty), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("SecondaryText", Brushes.DimGray), FontSize = 12 };
        Grid.SetColumn(scope, 2); row.Children.Add(scope);
        AutomationProperties.SetName(row, $"{shortcut.Keys}: {shortcut.Action}. {shortcut.Scope}.");
        return row;
    }

    private static Brush Brush(string key, Brush fallback) => Application.Current?.TryFindResource(key) as Brush ?? fallback;
}
