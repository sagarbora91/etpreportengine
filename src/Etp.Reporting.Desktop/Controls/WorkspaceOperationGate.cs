using System.Windows;
namespace Etp.Reporting.Desktop;

/// <summary>Serialises actions on a retained task and freezes its submitted fields until completion.</summary>
public sealed class WorkspaceOperationGate
{
    public bool IsBusy { get; private set; }
    public IDisposable? TryEnter(UIElement owner)
    {
        if (IsBusy) return null;
        IsBusy = true;
        var enabled = owner.IsEnabled; owner.IsEnabled = false;
        return new Completion(() => { owner.IsEnabled = enabled; IsBusy = false; });
    }
    private sealed class Completion(Action complete) : IDisposable
    {
        private Action? callback = complete;
        public void Dispose() { var action = callback; callback = null; action?.Invoke(); }
    }
}
