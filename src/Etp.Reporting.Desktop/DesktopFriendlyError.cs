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

    // 1.9.9: the same numbers mean the same thing here as in ConnectionHealth.Classify (lane
    // startup-connection, IE-CODE-07/08): a missing database is not a permission problem, and
    // -2 (SqlClient's own timeout) is "took too long", handled before this in DescribeSql.
    internal static string? DescribeConnectionFailure(int number) => number switch
    {
        18456 or 18452 or 18470 or 18486 or 18487 or 18488 => "SQL Server login failed. Check your Windows account access.",
        4060 or 911 => "The ETP database is not available on this SQL Server. Check the database name in Settings > Database > Connection, or restore the database.",
        229 or 230 or 262 or 297 or 300 or 916 => "SQL Server permission denied. Ask the Owner to grant access to this database.",
        -1 or 2 or 26 or 40 or 53 or 64 or 233 or 258 or 1225 or 10053 or 10054 or 10060 or 10061 or 11001 =>
            "SQL Server is unreachable. Check that the SQL Server service is running and the instance name is correct.",
        _ => null
    };

    public const string GenericFailureMessage =
        "The action could not be completed. Technical details are available in the support package.";

    // RA-EXPORT-12 / IE-CODE-14. A timeout (-2) is a slow answer, not a lost server, so it no
    // longer says "unreachable".
    public const string TookTooLongMessage =
        "The database took too long to answer, so ETP stopped waiting. Nothing was changed by this step. Try again in a moment; for a report, choose a shorter date range. If it keeps happening, restart the PC and send the support package.";

    // IE-CODE-14. 1205 deadlock victim, 1222 lock timeout, and SqlClient's "Timeout expired ...
    // from the pool" when every connection is in use.
    public const string DatabaseBusyMessage =
        "The database was busy with another task. Wait a moment and try again.";

    public const string ConstraintRefusedMessage =
        "The database refused this value: it is out of range, or the item is still used elsewhere. Check the entries and try again.";

    private const string SupportPackage = "send the support package (Settings > Database > Support package)";
    private const string ReopenDay = "Ask the Owner to reopen that day (Today > Close day > Reopen day), then try again.";
    private const string OwnerOnly = "Only the Owner can do this. Ask the Owner.";
    private const string OwnerOrManager = "Only the Owner or a Store Manager can do this. Ask one of them.";
    private const string UpgradeStopped = "The database update stopped and changed nothing. Keep using the current version of ETP and " + SupportPackage + " to ETP support.";
    private const string ImportFault = "This file was not imported because of an ETP fault. Import it again; if it fails again, " + SupportPackage + ".";
    private const string HistoryKept = "This history is kept for audit and cannot be changed or deleted.";
    private const string DecidedBatch = "An approved, rejected or exported accounting batch cannot be changed. Prepare a new batch instead.";
    private const string ReplacementNeedsOwner = "This file would change figures that were already imported for this store and period, so it cannot replace them automatically. The Owner can replace them with a restatement from the Import screen.";

    /// <summary>
    /// IE-CODE-04. Every product error number (THROW 5xxxx) raised in database\migrations and in
    /// the repositories' inline SQL, with the Owner's wording. A number that means more than one
    /// thing is told apart by the start of its fixed SQL text (product-authored, never a name or
    /// value); the entry with no prefix is that number's default. The SQL text itself is never
    /// shown. The full table, with the procedure each comes from, is in 199-SQL-ERRORS-DONE.md.
    /// </summary>
    internal static IReadOnlyList<(int Number, string? SqlTextStartsWith, string Message)> ProductErrors { get; } =
    [
        // Imports
        (51001, null, "This file has an invoice whose date differs from the same invoice already imported. Check you picked the right export for this store and date. If the earlier import was wrong, the Owner can replace it with a restatement from the Import screen."),
        (51021, null, "This file covers a business day that is finalised, so it cannot be imported over it. " + ReopenDay),
        (51039, null, "Enter a reason for the restatement (not only spaces), then try again."),
        (51040, null, "The import this file replaces is no longer the current one. Refresh Import > History > Imports, choose the current import and try again."),
        (51041, null, "The replacement file must be for the same store and report, and cover at least the same dates as the import it replaces. Check the file and the chosen import."),
        (51042, null, "A business day covered by this import is finalised. " + ReopenDay),
        (51243, null, "This file covers a business day that is finalised, so it cannot be imported over it. " + ReopenDay),
        (51244, null, "Another import of this store and report is running. Wait for it to finish, then import again."),
        (51421, "Owner permission", "Only the Owner can restate an import that changes saved figures. Ask the Owner."),
        (51421, null, OwnerOrManager),
        (51422, null, ImportFault),
        (51423, null, "The replacement file must be an import of the same store and report as the one it replaces. Check the file and the chosen import."),
        (51424, "Legacy source upgrades require Owner", "Only the Owner can replace an import made by an earlier ETP version. Ask the Owner."),
        (51424, null, ReplacementNeedsOwner),
        (51425, null, "This file looked like a repeat of an earlier import, but its contents differ. Import it again; if it is a corrected export, the Owner can restate the earlier import from the Import screen."),
        (51427, null, "The kept source file does not match its import. Choose the original file again."),
        (51555, null, "The replacement file does not match the import it replaces (store, report or dates). Check you picked the right file and the right import."),
        (51556, null, "This replacement needs the Owner's approval first. Request approval, and import again after the Owner decides (Settings > Control centre > Approvals)."),
        (51750, null, "The kept source file is damaged or incomplete. Choose the original file again."),
        (51751, null, "The kept source file is damaged or incomplete. Choose the original file again."),
        (51752, null, "A source file can be kept only with the import it came from. Choose the file that was imported."),
        (51760, null, "This stock file is not the kind of report ETP expected for it. Check you chose the right export, then import again."),

        // Finalised days and manual inputs
        (51020, null, "This business day is finalised, so its manual entries cannot change. " + ReopenDay),
        (51022, null, "This entry field is no longer in use. Refresh the screen and try again."),
        (51023, null, "This day is already finalised, or someone else is finalising it. Refresh Today > Close day."),
        (51024, null, "Only a finalised day can be reopened, and this one is already open. Refresh Today > Close day."),
        (51025, null, "Generate the store pack for this day before finalising it (Today > Close day > Store pack)."),
        (51030, null, "This business day is finalised, so its sales cannot change. " + ReopenDay),
        (51031, null, "This business day is finalised, so its invoices cannot change. " + ReopenDay),
        (51032, null, "This business day is finalised, so its payments cannot change. " + ReopenDay),
        (51033, null, "This business day is finalised, so its stock movements cannot change. " + ReopenDay),
        (51034, null, "This business day is finalised, so its stock figures cannot change. " + ReopenDay),
        (51035, null, "This business day is finalised, so its sales details cannot change. " + ReopenDay),
        (51036, null, "This business day is finalised, so its physical stock counts cannot change. " + ReopenDay),
        (51037, null, "A business day this staff target covers is finalised, so the target cannot change. " + ReopenDay),
        (51038, null, "This business day is finalised, so its invoices cannot change. " + ReopenDay),
        (51043, null, "This business day is finalised, so its imported files cannot change. " + ReopenDay),
        (51210, null, "This business day is finalised. Reopen it before making changes."),
        (51300, null, "A finalised business day cannot be moved or deleted. " + ReopenDay),
        (51301, null, "Only the Owner can reopen a finalised business day. Ask the Owner."),
        (51302, null, "Enter a reason before reopening the business day."),

        // History that is kept for good
        (51044, null, "Generated reports are kept as they are and cannot be changed or deleted. Generate the pack again to get a new version."),
        (51045, null, "Generated reports are kept as they are and cannot be changed or deleted. Generate the pack again to get a new version."),
        (51046, null, HistoryKept),
        (51047, null, HistoryKept),
        (51200, null, "A saved report package cannot be changed. Create a new package instead."),
        (51201, null, HistoryKept),
        (51311, null, HistoryKept),
        (51340, null, HistoryKept),
        (51343, null, HistoryKept),
        (51573, null, HistoryKept),

        // Users, permissions, approvals and audit
        (51100, null, "Keep at least one active Owner. Add another Owner before changing this account."),
        (51211, null, "This request has already been decided. Refresh Settings > Control centre > Approvals."),
        (51310, null, "ETP could not write this step to the audit trail. Try again; if it repeats, " + SupportPackage + "."),
        (51312, "Keep at least thirty days", "The last 30 days of audit history must stay. Choose an earlier date to archive up to."),
        (51312, "Select a valid application role", "Choose one of the listed roles for this account and save again. Nothing was changed."),
        (51312, null, OwnerOnly),
        (51313, "Owner or Store Manager", OwnerOrManager),
        (51313, null, OwnerOnly),
        (51314, "Choose a decision", "Choose Approve or Reject and enter a reason."),
        (51314, null, "Enter a reason (not only spaces) and try again."),
        (51315, null, "Only the Owner can approve or reject requests. Ask the Owner."),
        (51420, null, OwnerOrManager),
        (51550, null, OwnerOrManager),
        (51554, null, "This kind of request has its own screen: use Settings > Control centre > Adjustment request, or restate the import from the Import screen."),
        (51472, null, OwnOwnerAccessMessage),
        (51473, null, LoginRightKeptMessage),
        (51474, null, LoginRightChainMessage),

        // Registers and evening brand rows
        (51401, null, "This brand row was deleted or changed on another PC. Refresh Settings > Stores & masters > Brands and targets and enter it again."),
        (51402, null, "One of these source brands is already in another brand row of this store. Remove it from that row first, then save this one."),
        (51426, "Enter valid source brand codes", "Enter the source brand codes as they appear in the export, one per line, and save again."),
        (51426, null, "Choose a store and enter a brand row name. ETP's own report labels (such as VOL, VALUE, AUPT, INVOICE or Other / unmapped) cannot be used as names."),
        (51551, "Enter a store and document number", "Choose a store and enter the document number."),
        (51551, "Choose Draft or Verified", "Choose Draft or Verified for this entry."),
        (51551, null, "Enter a reason for this register change."),
        (51552, null, "Only the Owner can verify an entry or change a verified one. Ask the Owner."),
        (51553, null, "Save the entry as Draft first, then verify it."),

        // Accounting and Tally (51220-51222 and 51430-51460 keep their wording from Phase 5)
        (51202, null, DecidedBatch),
        (51212, null, DecidedBatch),
        (51220, null, "Finalise the report generation before preparing accounting."),
        (51221, null, "Only a balanced, unblocked draft can be approved. Correct the setup and reject/reprepare a blocked batch."),
        (51222, null, "Approve the accounting batch before export. A rejected or exported batch cannot be exported again."),
        (51223, null, "This data-quality issue no longer exists. Refresh Settings > Control centre > Data quality."),
        (51224, null, "An approved accounting mapping request is needed first. Ask the Owner to approve it in Settings > Control centre > Approvals."),
        (51227, null, "This sharing contact no longer exists. Refresh Settings > Integrations > Sharing contacts."),
        (51430, null, "Owner permission is required to reject accounting batches."),
        (51431, null, "Enter a rejection reason of at most 1000 characters."),
        (51432, null, "Only an unexported, unrejected batch can be rejected."),
        (51450, null, "Existing accounting batches cover the same invoice. Review and reject duplicate unexported batches before updating the database."),
        (51451, null, "Accounting is busy. Try again."),
        (51453, null, "The invoice does not belong to this batch store and business date."),
        (51454, null, "Accounting history cannot be deleted. Reject an unexported batch with a reason."),
        (51455, null, "A rejected or exported batch cannot change status."),
        (51456, null, "This accounting status change is not allowed."),
        (51457, null, "A balanced, unblocked batch and an approval reason are required."),
        (51458, null, "Invoice reservations are controlled by the accounting batch status."),
        (51459, null, "Accounting export receipts cannot be changed or deleted."),
        (51460, null, "Set the intended TEST Tally company in Settings and refresh before approving or exporting (D12/D18)."),
        (51579, null, "This Tally step does not match its batch or Tally company. Refresh the accounting screen and try again."),

        // Backups, recovery drill and operations
        (51101, null, "This report-pack schedule was removed. Refresh the screen."),
        (51320, null, "This SQL Server edition cannot make encrypted backups. Nothing was changed. Ask your IT support which SQL Server edition is installed."),
        (51341, null, "Only the Owner or ETP's automation account can record a backup or recovery drill. Run it as the Owner."),
        (51342, null, "The backup or drill result was incomplete, so it was not recorded. Run the backup or drill again."),
        (51344, null, "The recovery drill result did not add up, so it was not recorded. Run the recovery drill again; if it repeats, " + SupportPackage + "."),

        // Database update (upgrade) refusals, met in setup or Settings > Database
        (51240, null, "Some imported invoices share a number within one financial year. " + UpgradeStopped),
        (51260, null, "Two staff targets for the same staff member and store start in the same month. " + UpgradeStopped),
        (51560, null, "Two accounting batches cover the same store and business date. " + UpgradeStopped),
        (51561, null, "An accounting batch has more than one export receipt. " + UpgradeStopped),
        (51562, null, "An accounting batch has an approval reason made only of spaces. " + UpgradeStopped),
        (51700, null, "Some invoices carry a year other than the financial year of their date. " + UpgradeStopped),
        (51701, null, "An invoice has more than one revenue control. " + UpgradeStopped),
        (51702, null, "An invoice has the same payment type twice. " + UpgradeStopped),
    ];

    /// <summary>
    /// The text for an exception, without a diagnostics reference. Unknown product numbers and
    /// unexpected failures say "send the support package"; use <see cref="DescribeWithReference"/>
    /// when the caller has recorded the failure and holds its reference id.
    /// </summary>
    public static string Describe(Exception exception) => DescribeWithReference(exception, null);

    /// <summary>
    /// As <see cref="Describe(Exception)"/>, but a message that asks for the support package also
    /// names the reference id of the diagnostics entry, so it can be found in the log.
    /// </summary>
    public static string DescribeWithReference(Exception exception, string? referenceId)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            UnauthorizedAccessException => "Your Windows account does not have permission for this action.",
            FileNotFoundException => "The selected file is no longer available. Select it again.",
            IOException => "The file could not be read. Close it in other applications and try again.",
            SqlException sql => DescribeSql(sql, referenceId),
            ImportSourceException or ImportConflictException => exception.Message,
            // SqlClient's "Timeout expired. The timeout period elapsed prior to obtaining a connection from the pool."
            InvalidOperationException when exception.Message.StartsWith("Timeout expired", StringComparison.Ordinal) => DatabaseBusyMessage,
            ArgumentException when IsProductMessage(exception) => Regex.Replace(exception.Message, @"\s*\(Parameter .*?\)\s*$", ""),
            InvalidOperationException when IsProductMessage(exception) => exception.Message,
            _ when InnerSqlException(exception) is { } inner => DescribeSql(inner, referenceId),
            _ => WithReference(GenericFailureMessage, referenceId)
        };
    }

    private static string DescribeSql(SqlException sql, string? referenceId)
    {
        if (IsCommandTimeout(sql)) return TookTooLongMessage;
        if (DescribeConnectionFailure(sql.Number) is { } connection) return connection;
        if (DescribeBusinessFailure(sql) is { } business) return business;
        foreach (SqlError error in sql.Errors)
        {
            switch (error.Number)
            {
                case 2601 or 2627: return "This item already exists.";
                case 1205 or 1222: return DatabaseBusyMessage;
                case 547: return ConstraintRefusedMessage;
                // RA-EXPORT-12: a missing column, table, procedure or function is a database that
                // is older than this ETP, not a source or manual input to review.
                case 207 or 208 or 2812 or 4121:
                    return $"This screen needs a part of the database that is missing (error {error.Number}). Run the latest ETP setup to update the database, then try again. If it persists, {SupportPackage}" + ReferenceSuffix(referenceId);
            }
        }
        var product = sql.Errors.Cast<SqlError>().FirstOrDefault(error => error.Number >= 50000);
        if (product is not null)
            return $"ETP's database refused this action (error {product.Number}). Try again; if it repeats, {SupportPackage}" + ReferenceSuffix(referenceId);
        return WithReference(GenericFailureMessage, referenceId);
    }

    // -2 is SqlClient's own timeout, while opening or while running a command: in both the
    // server did not answer in time (ConnectionHealth classifies it as Timeout, not unreachable).
    private static bool IsCommandTimeout(SqlException sql) =>
        sql.Errors.Cast<SqlError>().Any(error => error.Number == -2);

    // IE-CODE-13. ETP's own validation throws InvalidOperationException or ArgumentException with
    // a sentence for the user; .NET and SqlClient throw the same types with developer text
    // ("Sequence contains no elements", "Nullable object must have a value"). The method that
    // threw tells them apart: product text comes from an Etp.* assembly. The await and
    // exception-dispatch plumbing is skipped, so an exception handed back in a faulted Task (never
    // thrown where it was made) counts as the code that awaited it. An exception that was never
    // thrown at all is product text built by the caller.
    internal static bool IsProductMessage(Exception exception)
    {
        foreach (var frame in new System.Diagnostics.StackTrace(exception, false).GetFrames())
        {
            var type = frame.GetMethod()?.DeclaringType;
            if (type is null || IsAsyncPlumbing(type.Namespace)) continue;
            return type.Assembly.GetName().Name?.StartsWith("Etp.", StringComparison.Ordinal) == true;
        }
        return true;
    }

    private static bool IsAsyncPlumbing(string? ns) =>
        ns is "System.Runtime.CompilerServices" or "System.Threading.Tasks" or "System.Runtime.ExceptionServices" or "System.Threading";

    private static SqlException? InnerSqlException(Exception exception)
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
            if (inner is SqlException sql) return sql;
        return null;
    }

    private static string WithReference(string message, string? referenceId) =>
        string.IsNullOrWhiteSpace(referenceId) ? message : $"{message.TrimEnd('.')} (reference {referenceId.Trim()}).";

    private static string ReferenceSuffix(string? referenceId) =>
        string.IsNullOrWhiteSpace(referenceId) ? "." : $" and quote reference {referenceId.Trim()}.";

    private static string? DescribeBusinessFailure(SqlException exception)
    {
        // A trigger can append additional SQL errors. Only reviewed numbers supply user-facing text.
        foreach (SqlError error in exception.Errors)
        {
            var message = error.Number switch
            {
                51452 => DescribeDuplicateBatch(error.Message),
                // Migration 0048: trg_stores_service_unit_inactive refuses an active Service Centre store
                // (51900) and a move of it out of the SERVICE business unit (51904).
                Etp.Reporting.Infrastructure.SqlServer.ServiceCentreStores.ActivationRefusedSqlError => Etp.Reporting.Infrastructure.SqlServer.ServiceCentreStores.ActivationRefusedMessage,
                Etp.Reporting.Infrastructure.SqlServer.ServiceCentreStores.UnitMoveRefusedSqlError => Etp.Reporting.Infrastructure.SqlServer.ServiceCentreStores.UnitMoveRefusedMessage,
                _ => DescribeUserAccessFailure(error.Number) ?? DescribeProductError(error.Number, error.Message)
            };
            if (message is not null) return message;
        }
        return null;
    }

    internal static string? DescribeProductError(int number, string? sqlText)
    {
        string? fallback = null;
        foreach (var (candidate, prefix, message) in ProductErrors)
        {
            if (candidate != number) continue;
            if (prefix is null) fallback ??= message;
            else if (sqlText is not null && sqlText.StartsWith(prefix, StringComparison.Ordinal)) return message;
        }
        return fallback;
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
