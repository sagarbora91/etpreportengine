using System.Globalization;
using System.Text;

namespace Etp.Reporting.Infrastructure.SqlServer.ShopResults;

/// <summary>
/// Writes a shop result into <c>%ProgramData%\EtpReporting\Operations\ShopResults\pending</c>. The file appears under its final name
/// only once complete (temp name, then rename), so <c>Publish-ShopResults.ps1</c> never reads half a file. A failing write is never
/// allowed to change an import outcome: <see cref="TryWrite(ShopResult, out string?)"/> returns null and a reason code, which the
/// unattended run adds to its automation-run line (review B-1: the failure must not be silent). Setup
/// (<c>initialize-etp-operation-folders.ps1</c>) creates <c>ShopResults\{pending,published,rejected}</c> and gives the automation
/// account Modify on <c>pending</c> only.
/// </summary>
public sealed class ShopResultWriter(string pendingFolder)
{
    public static string DefaultPendingFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EtpReporting", "Operations", "ShopResults", "pending");

    public ShopResultWriter() : this(DefaultPendingFolder()) { }

    /// <summary><c>ROLE-yyyy-MM-dd-seq.json</c>: Publish-ShopResults.ps1 reads the role, day and sequence from this name.</summary>
    public static string FileNameFor(ShopResult result) =>
        string.Create(CultureInfo.InvariantCulture, $"{result.Role}-{result.BusinessDate:yyyy-MM-dd}-{result.Sequence}.json");

    /// <summary>
    /// The sequence for a new result of <paramref name="role"/> on <paramref name="day"/> (1.12 U2: a re-apply after a fix): one
    /// more than any result of that day in <c>pending</c> or in <c>published</c> beside it, so the shop's window shows the newest.
    /// 1 when there is none. An unreadable folder counts as empty.
    /// </summary>
    public int NextSequence(string role, DateOnly day)
    {
        var prefix = string.Create(CultureInfo.InvariantCulture, $"{role}-{day:yyyy-MM-dd}-");
        var highest = 0;
        var published = Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(pendingFolder)) ?? pendingFolder, "published");
        foreach (var folder in new[] { pendingFolder, published })
        {
            try
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var file in Directory.EnumerateFiles(folder, prefix + "*.json"))
                {
                    var tail = Path.GetFileNameWithoutExtension(file)[prefix.Length..];
                    if (int.TryParse(tail, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) && sequence > highest) highest = sequence;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        return highest + 1;
    }

    public string Write(ShopResult result)
    {
        Directory.CreateDirectory(pendingFolder);
        var path = Path.Combine(pendingFolder, FileNameFor(result));
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, ShopResultBuilder.ToJson(result), new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
        return path;
    }

    public const string FolderDenied = "SHOP_RESULT_FOLDER_DENIED";
    public const string WriteFailed = "SHOP_RESULT_WRITE_FAILED";

    public string? TryWrite(ShopResult result) => TryWrite(result, out _);

    /// <summary>The path written, or null with <paramref name="failure"/> = <see cref="FolderDenied"/> or <see cref="WriteFailed"/>.</summary>
    public string? TryWrite(ShopResult result, out string? failure)
    {
        failure = null;
        try { return Write(result); }
        catch (UnauthorizedAccessException) { failure = FolderDenied; return null; }
        catch (IOException) { failure = WriteFailed; return null; }
    }

    /// <summary>The sentence the automation-run line gets when a result could not be written; empty when it was.</summary>
    public static string FailureSentence(string? failure) => failure switch
    {
        null => "",
        FolderDenied => " The shop's result file could not be written (ETP may not write the ShopResults folder; run setup again), so the shop sees no answer for it.",
        _ => " The shop's result file could not be written, so the shop sees no answer for it.",
    };
}
