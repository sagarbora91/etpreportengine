using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;
using Etp.Reporting.Import.Batch;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Desktop;

public static class DesktopFriendlyError
{
    private static readonly Regex DuplicateBatchMessage = new(
        @"\A(?:This day|Invoice [\s\S]*) is already in (?<exported>exported )?batch (?<id>[0-9]{1,19})\. (?<advice>Reject that unexported batch before preparing another\.|An exported batch is final; it cannot be replaced\.)\z",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static bool IsDatabaseAvailabilityFailure(Exception exception) =>
        exception is SqlException or InvalidOperationException;

    public static bool IsAuditFailure(Exception exception) =>
        exception is SqlException or InvalidOperationException or ArgumentException;

    public static string Describe(Exception exception, string safeUnauthorizedMessage) =>
        exception is UnauthorizedAccessException ? safeUnauthorizedMessage : Describe(exception);

    internal static string? DescribeConnectionFailure(int number) => number switch
    {
        18456 or 18452 => "SQL Server login failed. Check your Windows account access.",
        229 or 230 or 262 or 916 or 4060 => "SQL Server permission denied. Ask the Owner to grant access to this database.",
        0 or -2 or -1 or 2 or 26 or 40 or 53 or 64 or 233 or 258 or 10060 or 10061 or 11001 =>
            "SQL Server is unreachable. Check that the SQL Server service is running and the instance name is correct.",
        _ => null
    };

    public static string Describe(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "Your Windows account does not have permission for this action.",
        FileNotFoundException => "The selected file is no longer available. Select it again.",
        IOException => "The file could not be read. Close it in other applications and try again.",
        SqlException sql when DescribeConnectionFailure(sql.Number) is { } message => message,
        SqlException { Number: 2601 or 2627 } => "This item already exists.",
        SqlException { Number: 51210 } => "This business day is finalised. Reopen it before making changes.",
        SqlException sql when DescribeBusinessFailure(sql) is { } message => message,
        SqlException sql when sql.Number >= 51000 => "The database rejected this change. Review the inputs and day status.",
        ImportSourceException => exception.Message,
        ArgumentException => System.Text.RegularExpressions.Regex.Replace(exception.Message, @"\s*\(Parameter .*?\)\s*$", ""),
        InvalidOperationException => exception.Message,
        _ => "The action could not be completed. Technical details are available in the support package."
    };

    private static string? DescribeBusinessFailure(SqlException exception)
    {
        // A trigger can append additional SQL errors. Only reviewed numbers supply user-facing text.
        foreach (SqlError error in exception.Errors)
        {
            var message = error.Number switch
            {
                51220 => "Finalise the report generation before preparing accounting.",
                51221 => "Only a balanced, unblocked draft can be approved. Correct the setup and reject/reprepare a blocked batch.",
                51222 => "Approve the accounting batch before export. A rejected or exported batch cannot be exported again.",
                51430 => "Owner permission is required to reject accounting batches.",
                51431 => "Enter a rejection reason of at most 1000 characters.",
                51432 => "Only an unexported, unrejected batch can be rejected.",
                51450 => "Existing accounting batches cover the same invoice. Review and reject duplicate unexported batches before updating the database.",
                51451 => "Accounting is busy. Try again.",
                51452 => DescribeDuplicateBatch(error.Message),
                51453 => "The invoice does not belong to this batch store and business date.",
                51454 => "Accounting history cannot be deleted. Reject an unexported batch with a reason.",
                51455 => "A rejected or exported batch cannot change status.",
                51456 => "This accounting status change is not allowed.",
                51457 => "A balanced, unblocked batch and an approval reason are required.",
                51458 => "Invoice reservations are controlled by the accounting batch status.",
                51459 => "Accounting export receipts cannot be changed or deleted.",
                51460 => "Set the intended TEST Tally company in Settings and refresh before approving or exporting (D12/D18).",
                _ => null
            };
            if (message is not null) return message;
        }
        return null;
    }

    private static string DescribeDuplicateBatch(string detail)
    {
        const string fallback = "This day is already in an accounting batch. Refresh the batch list and review its status before preparing another.";
        Match match;
        try { match = DuplicateBatchMessage.Match(detail); }
        catch (RegexMatchTimeoutException) { return fallback; }
        if (!match.Success || !long.TryParse(match.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            return fallback;
        var exported = match.Groups["exported"].Success;
        if (exported != (match.Groups["advice"].Value == "An exported batch is final; it cannot be replaced.")) return fallback;
        var batch = id.ToString(CultureInfo.InvariantCulture);
        return exported
            ? $"This day is already in exported batch {batch}. An exported batch is final; it cannot be replaced."
            : $"This day is already in batch {batch}. Reject that unexported batch before preparing another.";
    }
}
