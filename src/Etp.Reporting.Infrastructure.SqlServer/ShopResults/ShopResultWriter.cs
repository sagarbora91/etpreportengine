using System.Globalization;
using System.Text;

namespace Etp.Reporting.Infrastructure.SqlServer.ShopResults;

/// <summary>
/// Writes a shop result into <c>%ProgramData%\EtpReporting\Operations\ShopResults\pending</c>. The file appears under its final name
/// only once complete (temp name, then rename), so <c>Publish-ShopResults.ps1</c> never reads half a file. A failing write is never
/// allowed to change an import outcome: <see cref="TryWrite"/> swallows the error and returns null.
/// </summary>
public sealed class ShopResultWriter(string pendingFolder)
{
    public static string DefaultPendingFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EtpReporting", "Operations", "ShopResults", "pending");

    public ShopResultWriter() : this(DefaultPendingFolder()) { }

    /// <summary><c>ROLE-yyyy-MM-dd-seq.json</c>: Publish-ShopResults.ps1 reads the role, day and sequence from this name.</summary>
    public static string FileNameFor(ShopResult result) =>
        string.Create(CultureInfo.InvariantCulture, $"{result.Role}-{result.BusinessDate:yyyy-MM-dd}-{result.Sequence}.json");

    public string Write(ShopResult result)
    {
        Directory.CreateDirectory(pendingFolder);
        var path = Path.Combine(pendingFolder, FileNameFor(result));
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, ShopResultBuilder.ToJson(result), new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
        return path;
    }

    public string? TryWrite(ShopResult result)
    {
        try { return Write(result); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }
}
