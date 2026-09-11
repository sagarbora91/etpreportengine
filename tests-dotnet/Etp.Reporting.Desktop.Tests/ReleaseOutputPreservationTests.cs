using System.Diagnostics;

namespace Etp.Reporting.Desktop.Tests;

public sealed class ReleaseOutputPreservationTests
{
    [Theory]
    [InlineData("build-windows-release.ps1")]
    [InlineData("build-windows-installer.ps1")]
    public async Task Existing_output_is_rejected_before_build_and_preserved(string script)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Etp.Reporting.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var output = Path.Combine(Path.GetTempPath(), "EtpReleasePreservation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var sentinel = Path.Combine(output, "previous-candidate.txt");
        File.WriteAllText(sentinel, "preserve previous candidate");
        try
        {
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(root.FullName, "scripts", script), "-OutputDirectory", output }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);
            Assert.NotEqual(0, process.ExitCode);
            Assert.Contains("output already exists", await stdout + await stderr, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve previous candidate", File.ReadAllText(sentinel));
        }
        finally { Directory.Delete(output, recursive: true); }
    }
}
