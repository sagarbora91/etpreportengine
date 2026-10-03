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

    /// <summary>
    /// Settings > Users. Every user change ends with GRANT or REVOKE ALTER ANY LOGIN, which
    /// since 1.9.2 an Owner can make only from an elevated ETP (sysadmin through
    /// BUILTIN\Administrators). Findings A and B, 2 Oct 2026: the screen used to say only
    /// "The action could not be completed".
    /// </summary>
    public const string UserAccessNeedsElevationMessage =
        "User changes need ETP started as administrator. Close ETP, right-click it, choose Run as administrator, and save again. Nothing was changed.";

    /// <summary>
    /// Security review 1.9.3, F5. Deactivating an account that has no SQL Server login (one from
    /// a retired PC, which the restore helper asks the Owner to deactivate) changes nothing at
    /// server level, so it works unelevated; Settings > Users keeps Save on for it.
    /// </summary>
    public const string UserDeactivationWorksUnelevatedMessage =
        "Deactivating an account that has no SQL Server login, such as one from a retired PC (untick Active), works without it.";

    // 15401 is SQL Server's wording, from a database before 0042; 51471 is 0042's own refusal.
    // Since 0042 an account of a retired PC can be deactivated, so both say so.
    public const string UserAccountNotFoundMessage =
        @"Windows cannot find this account. Check it is typed as DOMAIN\User or COMPUTER\User and exists on this PC or domain. An account of a PC that no longer exists can only be deactivated: untick Active and save. Nothing was changed.";

    // Migration 0043. Owners hold ALTER ANY LOGIN WITH GRANT OPTION, so demoting one revokes
    // it WITH CASCADE; dbo.configure_application_role refuses the three cases that would
    // otherwise leave the server-level right wrong. Every message ends "Nothing was changed",
    // which holds because Settings > Users runs the procedure in a transaction.
    public const string OwnOwnerAccessMessage =
        "You cannot take away your own Owner access. Ask another Owner to change your account. Nothing was changed.";

    public const string LoginRightKeptMessage =
        "SQL Server kept this account's right to manage logins because another account granted it. Close ETP, right-click it, choose Run as administrator, and save again. Nothing was changed.";

    public const string LoginRightChainMessage =
        @"This account gave you your own right to manage logins, so taking away its Owner access would take yours too. Nothing was changed. A SQL administrator can help: docs\OPERATIONS.md, Owners and SQL Server logins.";

    // 4611 "To revoke or deny grantable privileges, specify the CASCADE option." comes only
    // from a procedure older than 0043 meeting an Owner who holds the grant option.
    public const string UserAccessNeedsUpdateMessage =
        "This change needs ETP's latest database update. Run the ETP setup, then save again. Nothing was changed.";

    public static string DescribeUserAccessFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is SqlException sql)
        {
            foreach (SqlError error in sql.Errors)
            {
                switch (error.Number)
                {
                    // 4613 "Grantor does not have GRANT permission." is the one seen on Workpc.
                    // 15247 "User does not have permission to perform this action." and 15151
                    // "Cannot find the login ..., or you do not have permission." are the same
                    // missing server right, met at CREATE LOGIN or at the GRANT itself.
                    case 4613 or 15247 or 15151: return UserAccessNeedsElevationMessage;
                    // 15401 "Windows NT user or group '...' not found." (Finding C).
                    case 15401 or 51471: return UserAccountNotFoundMessage;
                    case 4611: return UserAccessNeedsUpdateMessage;
                    case 51472: return OwnOwnerAccessMessage;
                    case 51473: return LoginRightKeptMessage;
                    case 51474: return LoginRightChainMessage;
                }
            }
        }
        return Describe(exception, "Owner permission is required.");
    }

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
        ImportSourceException or ImportConflictException => exception.Message,
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
                _ => DescribeUserAccessFailure(error.Number)
            };
            if (message is not null) return message;
        }
        return null;
    }

    // Settings > Users. 51230 is the last-Owner guard (trigger and, since migration 0042, the
    // procedure). 51471 is 0042's refusal to give access to an account Windows cannot find;
    // 15401 is SQL Server's own wording of the same thing, from a database before 0042.
    internal static string? DescribeUserAccessFailure(int number) => number switch
    {
        51230 => "Keep at least one active Owner. Add another Owner before changing this account.",
        51471 or 15401 => UserAccountNotFoundMessage,
        _ => null
    };

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
