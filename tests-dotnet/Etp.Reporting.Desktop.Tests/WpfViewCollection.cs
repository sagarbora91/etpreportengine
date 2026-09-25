namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// Every test that builds a WPF view belongs here. `Application.LoadComponent` reads the
/// pack resources through `System.IO.Packaging`, whose part and stream state is shared by
/// the whole process and is not thread-safe, so two test classes building views on their
/// own STA threads at the same time can tear it: the symptom is a NullReferenceException
/// inside `PackagePart.IsStreamClosed` while a XAML `InitializeComponent` runs, in whichever
/// test happened to be second. Disabling parallelisation for this collection runs them one
/// at a time; the rest of the assembly still runs in parallel.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WpfViewCollection
{
    public const string Name = "WPF views";
}
