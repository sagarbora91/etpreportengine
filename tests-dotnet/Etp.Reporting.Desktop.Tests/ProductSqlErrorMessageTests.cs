using System.Text.RegularExpressions;
using Etp.Reporting.TestSupport;

namespace Etp.Reporting.Desktop.Tests;

// 1.9.9, IE-CODE-04, 13, 14 and RA-EXPORT-12. Until 1.9.9, 66 of the 95 product error numbers
// read "The database rejected this change. Review the inputs and day status.", a query timeout
// read "SQL Server is unreachable", and .NET's own InvalidOperationException text reached the screen.
public sealed class ProductSqlErrorMessageTests
{
    private const string OldGenericRefusal = "The database rejected this change. Review the inputs and day status.";
    private const string RawSqlText = "private database detail from SQL";

    private static string Describe(int number, string sqlText = RawSqlText, string? reference = null) =>
        DesktopFriendlyError.DescribeWithReference(SqlExceptionFactory.Create(new SqlExceptionFactory.Error(number, sqlText)), reference);

    [Fact]
    public void Every_product_error_number_in_the_migrations_and_repositories_has_a_curated_message()
    {
        var root = RepositoryRoot();
        var sources = Directory.GetFiles(Path.Combine(root, "database", "migrations"), "*.sql")
            .Concat(Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)));
        var numbers = sources
            .SelectMany(path => Regex.Matches(File.ReadAllText(path), @"THROW\s+(5\d{4})\s*,").Select(match => int.Parse(match.Groups[1].Value)))
            .ToHashSet();

        Assert.True(numbers.Count >= 95, $"Only {numbers.Count} product error numbers were found; the scan is broken.");
        foreach (var number in numbers)
        {
            var described = Describe(number);
            Assert.False(described.Contains($"(error {number})", StringComparison.Ordinal), $"{number} falls back to the unknown-number text: {described}");
            Assert.NotEqual(OldGenericRefusal, described);
            Assert.DoesNotContain(RawSqlText, described, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(51020)]
    [InlineData(51021)]
    [InlineData(51030)]
    [InlineData(51034)]
    [InlineData(51037)]
    [InlineData(51042)]
    [InlineData(51243)]
    [InlineData(51300)]
    public void A_finalised_day_says_to_ask_the_Owner_to_reopen_it(int number)
    {
        var described = Describe(number);

        Assert.Contains("finalised", described, StringComparison.Ordinal);
        Assert.Contains("Today > Close day > Reopen day", described, StringComparison.Ordinal);
    }

    // Settings > Stores & masters > Brands and targets: Sagar's E9 brand rows.
    [Theory]
    [InlineData(51401, "The brand row no longer exists in this store.", "Refresh Settings > Stores & masters > Brands and targets")]
    [InlineData(51402, "This source brand is already assigned. Remove it from the other row first.", "Remove it from that row first")]
    [InlineData(51426, "Enter a store and a non-reserved brand label.", "cannot be used as names")]
    [InlineData(51426, "Enter valid source brand codes.", "source brand codes")]
    public void Brand_row_refusals_say_what_to_change(int number, string sqlText, string advice) =>
        Assert.Contains(advice, Describe(number, sqlText), StringComparison.Ordinal);

    [Theory]
    [InlineData(51420, "Owner or Store Manager permission is required.", "Only the Owner or a Store Manager")]
    [InlineData(51550, "Owner or Store Manager permission is required.", "Only the Owner or a Store Manager")]
    [InlineData(51313, "Owner or Store Manager permission is required to request a restatement.", "Only the Owner or a Store Manager")]
    [InlineData(51313, "Owner permission is required to submit an adjustment.", "Only the Owner can")]
    [InlineData(51312, "Owner permission is required to manage access.", "Only the Owner can")]
    [InlineData(51312, "Select a valid application role.", "Choose one of the listed roles")]
    [InlineData(51312, "Keep at least thirty days of recent audit history.", "last 30 days")]
    [InlineData(51301, "Owner permission is required to reopen a business date.", "Only the Owner can reopen")]
    [InlineData(51315, "Owner permission is required to decide a request.", "Only the Owner can approve or reject")]
    [InlineData(51421, "Owner permission is required for corrective restatement.", "Only the Owner can restate")]
    [InlineData(51421, "Owner or Store Manager permission is required.", "Only the Owner or a Store Manager")]
    public void Permission_refusals_name_who_can_do_it(int number, string sqlText, string expected) =>
        Assert.Contains(expected, Describe(number, sqlText), StringComparison.Ordinal);

    [Theory]
    [InlineData(51302, "Enter a reason before reopening the business date.", "Enter a reason before reopening")]
    [InlineData(51314, "Enter an adjustment reason.", "Enter a reason")]
    [InlineData(51314, "Choose a decision and enter its reason.", "Choose Approve or Reject")]
    [InlineData(51039, "A restatement reason is required.", "Enter a reason for the restatement")]
    [InlineData(51551, "Enter a register change reason.", "Enter a reason for this register change")]
    [InlineData(51551, "Enter a store and document number.", "document number")]
    [InlineData(51551, "Choose Draft or Verified.", "Choose Draft or Verified")]
    public void Missing_inputs_say_which_one(int number, string sqlText, string expected) =>
        Assert.Contains(expected, Describe(number, sqlText), StringComparison.Ordinal);

    [Theory]
    [InlineData(51044)]
    [InlineData(51046)]
    [InlineData(51201)]
    [InlineData(51311)]
    [InlineData(51340)]
    [InlineData(51573)]
    public void Kept_history_says_it_cannot_change(int number) =>
        Assert.Contains("cannot be changed", Describe(number), StringComparison.Ordinal);

    [Theory]
    [InlineData(51424, "Changed legacy sources require explicit Owner restatement.", "restatement from the Import screen")]
    [InlineData(51424, "Legacy source upgrades require Owner permission.", "Only the Owner can replace")]
    [InlineData(51556, "This exact replacement and reason require Owner approval before import.", "Request approval")]
    [InlineData(51555, "The replacement source identity is invalid.", "does not match the import it replaces")]
    [InlineData(51422, "Import writes require an enclosing transaction.", "Import it again")]
    [InlineData(51244, "Another import is using this store and report. Retry shortly.", "Wait for it to finish")]
    public void Import_refusals_say_what_to_do_next(int number, string sqlText, string expected) =>
        Assert.Contains(expected, Describe(number, sqlText), StringComparison.Ordinal);

    [Theory]
    [InlineData(51240)]
    [InlineData(51260)]
    [InlineData(51560)]
    [InlineData(51561)]
    [InlineData(51562)]
    [InlineData(51700)]
    [InlineData(51701)]
    [InlineData(51702)]
    public void Database_update_refusals_say_nothing_changed_and_ask_for_the_support_package(int number)
    {
        var described = Describe(number);

        Assert.Contains("The database update stopped and changed nothing.", described, StringComparison.Ordinal);
        Assert.Contains("Support package", described, StringComparison.Ordinal);
        Assert.DoesNotContain("day status", described, StringComparison.Ordinal);
    }

    // The Phase 5 accounting wording is unchanged; 51202/51212 are new.
    [Theory]
    [InlineData(51220, "Finalise the report generation before preparing accounting.")]
    [InlineData(51451, "Accounting is busy. Try again.")]
    [InlineData(51457, "A balanced, unblocked batch and an approval reason are required.")]
    [InlineData(51210, "This business day is finalised. Reopen it before making changes.")]
    [InlineData(51212, "An approved, rejected or exported accounting batch cannot be changed. Prepare a new batch instead.")]
    [InlineData(51202, "An approved, rejected or exported accounting batch cannot be changed. Prepare a new batch instead.")]
    public void Accounting_refusals_keep_their_reviewed_wording(int number, string expected) =>
        Assert.Equal(expected, Describe(number));

    [Fact]
    public void A_refusal_behind_another_error_in_the_same_batch_is_still_found()
    {
        var exception = SqlExceptionFactory.Create(
            new SqlExceptionFactory.Error(3609, "The transaction ended in the trigger. The batch has been aborted."),
            new SqlExceptionFactory.Error(51402, "This source brand is already assigned. Remove it from the other row first."));

        Assert.Contains("Remove it from that row first", DesktopFriendlyError.Describe(exception), StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_product_number_gives_the_number_and_the_reference_and_never_the_SQL_text()
    {
        var withReference = Describe(51999, reference: "a1b2c3d4e5");
        var without = Describe(51999);

        Assert.Contains("(error 51999)", withReference, StringComparison.Ordinal);
        Assert.Contains("reference a1b2c3d4e5", withReference, StringComparison.Ordinal);
        Assert.Contains("Support package", withReference, StringComparison.Ordinal);
        Assert.DoesNotContain(RawSqlText, withReference, StringComparison.Ordinal);
        Assert.Contains("(error 51999)", without, StringComparison.Ordinal);
        Assert.DoesNotContain("reference", without, StringComparison.Ordinal);
        Assert.EndsWith(".", without, StringComparison.Ordinal);
        Assert.Equal(without, DesktopFriendlyError.Describe(SqlExceptionFactory.Create(new SqlExceptionFactory.Error(51999, RawSqlText))));
    }

    [Fact]
    public void An_unexpected_failure_keeps_the_generic_text_and_adds_the_reference_when_given()
    {
        var failure = new NotSupportedException("developer detail");

        Assert.Equal(DesktopFriendlyError.GenericFailureMessage, DesktopFriendlyError.Describe(failure));
        Assert.Equal(
            "The action could not be completed. Technical details are available in the support package (reference a1b2).",
            DesktopFriendlyError.DescribeWithReference(failure, "a1b2"));
    }

    // RA-EXPORT-12 / IE-CODE-14.
    [Theory]
    [InlineData(-2, "Execution Timeout Expired.  The timeout period elapsed prior to completion of the operation or the server is not responding.")]
    [InlineData(258, "The wait operation timed out.")]
    public void A_query_that_takes_too_long_says_so_not_unreachable(int number, string sqlText)
    {
        var described = Describe(number, sqlText);

        Assert.Equal(DesktopFriendlyError.TookTooLongMessage, described);
        Assert.Contains("took too long", described, StringComparison.Ordinal);
        Assert.DoesNotContain("unreachable", described, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_connection_that_times_out_while_opening_is_still_unreachable() =>
        Assert.Contains("unreachable", Describe(-2, "Connection Timeout Expired.  The timeout period elapsed while attempting to consume the pre-login handshake acknowledgement."), StringComparison.Ordinal);

    [Theory]
    [InlineData(1205, "Transaction (Process ID 61) was deadlocked on lock resources with another process and has been chosen as the deadlock victim. Rerun the transaction.")]
    [InlineData(1222, "Lock request time out period exceeded.")]
    public void A_busy_database_says_try_again(int number, string sqlText) =>
        Assert.Equal(DesktopFriendlyError.DatabaseBusyMessage, Describe(number, sqlText));

    [Fact]
    public void An_exhausted_connection_pool_says_the_database_was_busy() =>
        Assert.Equal(DesktopFriendlyError.DatabaseBusyMessage, DesktopFriendlyError.Describe(new InvalidOperationException(
            "Timeout expired.  The timeout period elapsed prior to obtaining a connection from the pool.  This may have occurred because all pooled connections were in use and max pool size was reached.")));

    [Theory]
    [InlineData(207, "Invalid column name 'brand_segment'.")]
    [InlineData(208, "Invalid object name 'dbo.vw_service_daily'.")]
    [InlineData(2812, "Could not find stored procedure 'dbo.save_service_input'.")]
    [InlineData(4121, "Cannot find either column \"dbo\" or the user-defined function or aggregate \"dbo.fn_x\".")]
    public void A_missing_database_part_asks_for_the_database_update_with_its_number(int number, string sqlText)
    {
        var described = Describe(number, sqlText, "ref42");

        Assert.Contains("Run the latest ETP setup", described, StringComparison.Ordinal);
        Assert.Contains($"(error {number})", described, StringComparison.Ordinal);
        Assert.Contains("reference ref42", described, StringComparison.Ordinal);
        Assert.DoesNotContain("dbo.", described, StringComparison.Ordinal);
    }

    [Fact]
    public void A_constraint_refusal_says_to_check_the_entries() =>
        Assert.Equal(DesktopFriendlyError.ConstraintRefusedMessage,
            Describe(547, "The INSERT statement conflicted with the CHECK constraint \"CK_monthly_targets_amount\"."));

    // IE-CODE-13.
    [Fact]
    public void Framework_InvalidOperationException_text_is_not_shown()
    {
        var empty = Assert.Throws<InvalidOperationException>(() => Array.Empty<int>().Single());
        int? missing = null;
        var nullable = Assert.Throws<InvalidOperationException>(() => missing!.Value);

        foreach (var exception in new Exception[] { empty, nullable })
        {
            Assert.Equal(DesktopFriendlyError.GenericFailureMessage, DesktopFriendlyError.Describe(exception));
            Assert.Contains("reference r9", DesktopFriendlyError.DescribeWithReference(exception, "r9"), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Framework_ArgumentException_text_is_not_shown()
    {
        var blank = Assert.Throws<ArgumentException>(() => ArgumentException.ThrowIfNullOrWhiteSpace(" "));

        Assert.Equal(DesktopFriendlyError.GenericFailureMessage, DesktopFriendlyError.Describe(blank));
    }

    [Fact]
    public void ETP_validation_text_is_still_shown()
    {
        var invalid = Assert.Throws<InvalidOperationException>(() => ThrowProductValidation("Select both report dates."));
        var argument = Assert.Throws<ArgumentException>(() => ThrowProductArgument("Enter a store code."));

        Assert.Equal("Select both report dates.", DesktopFriendlyError.Describe(invalid));
        Assert.Equal("Enter a store code.", DesktopFriendlyError.Describe(argument));
        Assert.Equal("Enter a store.", DesktopFriendlyError.Describe(new InvalidOperationException("Enter a store.")));
    }

    private static void ThrowProductValidation(string message) => throw new InvalidOperationException(message);

    private static void ThrowProductArgument(string message) => throw new ArgumentException(message, "store");

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Etp.Reporting.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the repository root containing Etp.Reporting.slnx.");
    }
}
