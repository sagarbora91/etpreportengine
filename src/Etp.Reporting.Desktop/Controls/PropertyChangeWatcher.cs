using System.Windows;
using System.Windows.Data;

namespace Etp.Reporting.Desktop;

/// <summary>Calls back when one element's dependency property changes; use instead of DependencyPropertyDescriptor.AddValueChanged.</summary>
/// <remarks>
/// WPF caches one descriptor per property for the whole process and keeps its listeners in a Dictionary
/// without a lock, so two UI threads adding listeners at once corrupt it, and it holds every element until
/// RemoveValueChanged. This watcher is a binding on the element's own thread, kept alive only by that element.
/// </remarks>
internal sealed class PropertyChangeWatcher : DependencyObject, IDisposable
{
    private static readonly DependencyProperty ValueProperty = DependencyProperty.Register("Value", typeof(object), typeof(PropertyChangeWatcher),
        new PropertyMetadata(null, (watcher, _) => ((PropertyChangeWatcher)watcher).changed?.Invoke()));
    private static readonly DependencyProperty WatchersProperty = DependencyProperty.RegisterAttached("Watchers", typeof(List<PropertyChangeWatcher>), typeof(PropertyChangeWatcher));

    private readonly DependencyObject element;
    private Action? changed;

    private PropertyChangeWatcher(DependencyObject element, DependencyProperty property, Action changed)
    {
        this.element = element;
        BindingOperations.SetBinding(this, ValueProperty, new Binding { Source = element, Path = new PropertyPath(property), Mode = BindingMode.OneWay });
        // Armed after the initial transfer: like AddValueChanged, only later changes are reported.
        this.changed = changed;
    }

    public static PropertyChangeWatcher Watch(DependencyObject element, DependencyProperty property, Action changed)
    {
        var watcher = new PropertyChangeWatcher(element, property, changed);
        if (element.GetValue(WatchersProperty) is not List<PropertyChangeWatcher> watchers)
            element.SetValue(WatchersProperty, watchers = []);
        watchers.Add(watcher);
        return watcher;
    }

    public void Dispose()
    {
        changed = null;
        BindingOperations.ClearBinding(this, ValueProperty);
        (element.GetValue(WatchersProperty) as List<PropertyChangeWatcher>)?.Remove(this);
    }
}
