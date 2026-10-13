using System.Globalization;

namespace Etp.Reporting.Application.HealthCheck;

/// <summary>
/// Fixed explanation templates and finding codes of the runner and of the Database and Security checks (design 5.1).
/// Every template is filled with tokens only: counts, dates, version numbers, SQL error numbers, role, schema and
/// object names, migration ids and check ids. Never an account name, a path, an exception message or a row value.
/// </summary>
public static class HealthCheckTexts
{
    /// <summary><c>diag.health_check_results.explanation</c> is nvarchar(400).</summary>
    public const int MaxExplanationLength = 400;

    /// <summary><c>diag.health_check_results.finding_code</c> is varchar(60).</summary>
    public const int MaxFindingCodeLength = 60;

    /// <summary><c>diag.health_check_results.fix_task_id</c> is varchar(40).</summary>
    public const int MaxFixTaskIdLength = 40;

    // ---- Finding codes the runner writes -------------------------------------------------------------------

    public const string Ok = "OK";
    public const string TimeBudget = "TIME_BUDGET";
    public const string DatabaseUnreachable = "DB_UNREACHABLE";
    public const string NotAvailable = "NOT_AVAILABLE";
    public const string ProbeFailed = "PROBE_FAILED";
    public const string ProbeNoResult = "PROBE_NO_RESULT";
    public const string NotPermitted = "NOT_PERMITTED";
    /// <summary>Sub-result key the runner adds when it trims sub-results to fit <c>detail_json</c>.</summary>
    public const string MoreSubResultsKey = "MORE";

    // ---- Runner texts --------------------------------------------------------------------------------------

    public static string SkippedTimeBudget(HealthCheckArea area) =>
        $"Skipped: the time budget of this run was used up before this check ({area} area).";

    public const string SkippedDatabaseUnreachable = "Skipped: the database is not reachable (see DB-01).";

    public const string SkippedNotAvailable = "Skipped: this check is not available in this run.";

    public const string SkippedNotPermitted = "Skipped: this account cannot read what this check needs; the nightly run checks it.";

    public static string ProbeFailedText(string exceptionTypeName) =>
        $"The check itself failed ({Token(exceptionTypeName)}). The error register has the details.";

    public const string ProbeNoResultText = "The check returned no result for this line.";

    public static string KnownSuffix(DateOnly until) => $" Known until {Date(until)}.";

    public static string PartlyKnownSuffix(int knownCount) =>
        $" {Count(knownCount)} accepted as Known.";

    // ---- Database (DB-01..05) ------------------------------------------------------------------------------

    public static string DbConnected(string database, TimeSpan elapsed) =>
        $"Connected to {Token(database)} in {Seconds(elapsed)}.";

    public static string DbConnectedSlow(string database, TimeSpan elapsed) =>
        $"Connected to {Token(database)}, but it took {Seconds(elapsed)} (more than 5 s). SQL Server may be starting a closed database (AUTO_CLOSE) or be busy.";

    public static string DbWrongDatabase(string expected, string actual) =>
        $"Connected, but to database {Token(actual)} instead of {Token(expected)}. Check the connection in Settings > Database > Connection.";

    public const string DbConfigInvalid = "The SQL Server connection settings are invalid. Fix them in Settings > Database > Connection.";

    public static string DbConnectFailed(string kindText, int? sqlNumber) =>
        sqlNumber is { } number
            ? $"{kindText} (SQL error {number.ToString(CultureInfo.InvariantCulture)}). All other database checks are skipped."
            : $"{kindText}. All other database checks are skipped.";

    public const string DbServerUnreachableText = "SQL Server could not be reached";
    public const string DbTimeoutText = "SQL Server took too long to answer";
    public const string DbLoginRefusedText = "SQL Server refused the Windows login of this account";
    public const string DbDatabaseMissingText = "The ETP database could not be opened";
    public const string DbPermissionDeniedText = "SQL Server refused this account permission to use the database";

    public static string DbMigrationsCurrent(int applied, string latest) =>
        $"All {Count(applied)} migrations are applied (latest {Token(latest)}).";

    /// <summary>1.12 A3: DB-02 also counts this installation's database object files.</summary>
    public static string DbMigrationsCurrent(int applied, string latest, int objectFiles) =>
        $"All {Count(applied)} migrations are applied (latest {Token(latest)}); this release has {Count(objectFiles)} database object files.";

    // ---- DB-07 (1.12 A3): database objects match this release -------------------------------------------------

    public static string DbObjectsCurrent(int objects) =>
        $"All {Count(objects)} database objects (views, procedures and functions) match this release.";

    public const string DbObjectsNotFound = "Skipped: the database object files of this installation were not found. Run ETP setup on this PC.";

    public const string DbObjectsNotRecorded = "The database has no object journal (dbo.schema_objects). Run ETP setup on this PC.";

    public const string DbObjectFilesInvalid = "The database object files of this installation could not be read. Run ETP setup again; if it stays, send the support package.";

    public static string DbObjectsChangedByHand(int count, string first) =>
        $"{Count(count)} database object(s) were changed outside ETP setup (first {Token(first)}). Run ETP setup again to put back this release's version; if it stays, send the support package.";

    public static string DbObjectsMissing(int count, string first) =>
        $"{Count(count)} database object(s) of this release are missing (first {Token(first)}). Run ETP setup again.";

    public static string DbObjectsPending(int count, string first) =>
        $"{Count(count)} database object(s) differ from this release (first {Token(first)}). Run ETP setup on this PC.";

    public static string DbObjectsUnknown(int count, string first) =>
        $"The database records {Count(count)} object(s) this release has no file for (first {Token(first)}). Send the support package.";

    public static string DbMigrationsPending(int pending, string first) =>
        $"{Count(pending)} migration(s) not applied yet (first {Token(first)}). Run ETP setup on this PC.";

    public static string DbMigrationChecksum(int count, string first) =>
        $"{Count(count)} applied migration(s) differ from this release's copy (first {Token(first)}). Do not change the database; send the support package.";

    public static string DbMigrationUnknown(int count, string first) =>
        $"The database holds {Count(count)} migration(s) this release does not know (first {Token(first)}). Send the support package.";

    public static string DbDatabaseNewer(string newest) =>
        $"The database was upgraded by a newer ETP (it holds {Token(newest)}). Update ETP on this PC.";

    public const string DbMigrationsNotFound = "Skipped: the migration scripts of this installation were not found. Run ETP setup on this PC.";

    public const string DbNotInitialised = "The database has no migration journal (dbo.schema_migrations). Run ETP setup on this PC.";

    public static string DbIntegrityOk(DateOnly lastGoodCheckDb) =>
        $"No suspect pages, page verify CHECKSUM, last good integrity check {Date(lastGoodCheckDb)}.";

    public static string DbCheckDbStale(DateOnly? lastGoodCheckDb, int maximumDays) =>
        lastGoodCheckDb is { } date
            ? $"The last good integrity check (CHECKDB) was on {Date(date)}, more than {Count(maximumDays)} days ago. The monthly recovery drill runs it."
            : "No good integrity check (CHECKDB) is recorded for this database. The monthly recovery drill runs it.";

    public static string DbSuspectPages(int count) =>
        $"SQL Server recorded {Count(count)} suspect page(s) for this database. Restore from the latest verified backup; send the support package.";

    public static string DbPageVerify(string option) =>
        $"Page verify is {Token(option)}, not CHECKSUM. Run ETP setup again as administrator.";

    public const string DbOptionsOk = "AUTO_CLOSE is off; the database is ONLINE, read-write and MULTI_USER.";

    public const string DbAutoCloseOn = "AUTO_CLOSE is on for the ETP database. Run ETP setup again as administrator.";

    public static string DbNotOnline(string state) =>
        $"The database is {Token(state)}, not ONLINE. Send the support package.";

    public const string DbReadOnly = "The database is read-only. Run ETP setup again as administrator.";

    public static string DbUserAccess(string access) =>
        $"The database is in {Token(access)} mode. Run ETP setup again as administrator.";

    public static string DbSizeOk(decimal sizeMb, decimal? limitMb) =>
        limitMb is { } limit
            ? $"Data files use {Megabytes(sizeMb)} of the {Megabytes(limit)} Express limit ({Percent(sizeMb, limit)})."
            : $"Data files use {Megabytes(sizeMb)}; this SQL Server edition has no database size limit.";

    public static string DbSizeHigh(decimal sizeMb, decimal limitMb) =>
        $"Data files use {Megabytes(sizeMb)} of the {Megabytes(limitMb)} Express limit ({Percent(sizeMb, limitMb)}). Plan archiving or a larger edition.";

    public static string DbSizeCritical(decimal sizeMb, decimal limitMb) =>
        $"Data files use {Megabytes(sizeMb)} of the {Megabytes(limitMb)} Express limit ({Percent(sizeMb, limitMb)}). Imports will fail at the limit; act now.";

    // ---- Security (SEC-01..03) -----------------------------------------------------------------------------

    public static string SecMatrixOk(int count) =>
        $"The four ETP roles have exactly the {Count(count)} expected permissions.";

    public static string SecMatrixUnsafe(int unsafeCount, string first) =>
        $"{Count(unsafeCount)} permission(s) give a role more than the design allows (first: {first}). Run ETP setup again as administrator; if it stays, send the support package.";

    public static string SecMatrixMissing(int missingCount, string first) =>
        $"{Count(missingCount)} expected permission(s) are missing (first: {first}); a feature may refuse with error 229. Run ETP setup again as administrator.";

    public static string SecMatrixOther(int count, string first) =>
        $"{Count(count)} permission(s) differ from the expected matrix (first: {first}). Run ETP setup again as administrator.";

    public static string SecDecisionViolated(string decision, string first) =>
        $"Decision {Token(decision)} is not enforced by the database: {first}. Run ETP setup again as administrator.";

    public const string SecMatrixMissingResource = "The expected permission matrix is missing from this ETP build. Send the support package.";

    /// <summary>"etp_store_manager GRANT INSERT on dbo.staff_sales_targets".</summary>
    public static string SecPermission(string role, string state, string permission, string? schema, string? @object, string classDesc) =>
        $"{Token(role)} {Token(state)} {Token(permission)} on " + (@object is not null
            ? $"{Token(schema ?? "?")}.{Token(@object)}"
            : schema is not null ? $"schema {Token(schema)}" : Token(classDesc));

    public static string SecMembershipOk(int activeOwners) =>
        $"Every member of an ETP role is an active ETP user with that role; {Count(activeOwners)} active Owner(s).";

    public const string SecNoActiveOwner = "No active Owner is a member of etp_owner. Run ETP setup as administrator and add the Owner in Settings > Users.";

    public static string SecMemberNotUser(string role, int count) =>
        $"{Count(count)} member(s) of {Token(role)} are not active ETP users with that role (an access left behind). Remove or correct them in Settings > Users.";

    public const string SecAutomationNotInRole =
        "The automation account is not a member of etp_automation; backups and the recovery drill will fail. Add the account in Settings > Users, then run the operations install script.";

    public const string SecAutomationNotStoreManager =
        "The automation account is not a member of etp_store_manager; automatic import will fail. Add it in Settings > Users as a Store Manager.";

    public const string SecGrantOptionNotVisible =
        "Skipped: this account cannot see server permissions; a health check run by the Owner checks this.";

    public const string SecGrantOptionOk = "Every active Owner can manage users in Settings > Users.";

    public static string SecOwnersWithoutGrantOption(int count) =>
        $"{Count(count)} active Owner account(s) cannot save changes in Settings > Users from an unelevated ETP. Run ETP setup again as administrator.";

    // ---- Helpers -------------------------------------------------------------------------------------------

    public static string Date(DateOnly date) => Etp.Reporting.Application.Formatting.DisplayText.Date(date);

    public static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Seconds(TimeSpan value) => value.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";

    public static string Megabytes(decimal value) =>
        value >= 1024m
            ? (value / 1024m).ToString("0.0", CultureInfo.InvariantCulture) + " GB"
            : value.ToString("0", CultureInfo.InvariantCulture) + " MB";

    public static string Percent(decimal part, decimal whole) =>
        whole <= 0 ? "0%" : (part / whole * 100m).ToString("0", CultureInfo.InvariantCulture) + "%";

    /// <summary>A token as written into a template: control characters removed, at most 80 characters.</summary>
    public static string Token(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var clean = new string(value.Where(character => !char.IsControl(character)).ToArray()).Trim();
        return clean.Length <= 80 ? clean : clean[..80];
    }

    /// <summary>
    /// A value fit for <c>diag.is_token</c> (sub-result keys and field values): every character outside
    /// <c>A-Z a-z 0-9 . _ : &lt; &gt; + -</c> becomes <c>_</c> (for example "TAKE OWNERSHIP"), at most <paramref name="maximum"/>.
    /// Only for values that are tokens already (role, object, permission names); never to launder free text.
    /// </summary>
    public static string SafeToken(string? value, int maximum = 100)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var chars = value.Select(character => (character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'
            or '.' or '_' or ':' or '<' or '>' or '+' or '-') ? character : '_').ToArray();
        var token = new string(chars);
        return token.Length <= maximum ? token : token[..maximum];
    }

    /// <summary>
    /// A sub-result key, field key or field value (and an error register operation) fit for <c>diag.is_token</c> and
    /// the export guard's token rule, keeping product wording readable (1.11.0 review findings 1 and 2): list
    /// separators ("," and ";", with any spaces round them) become <c>+</c>, every other character outside the token
    /// set becomes <c>_</c>, runs of <c>_</c> collapse and none is left at either end, at most <paramref name="maximum"/>.
    /// "Express Edition (64-bit)" becomes "Express_Edition_64-bit", "R022,R025" becomes "R022+R025". Probes still put
    /// only codes, counts and fixed wording into these values; this never makes row data safe.
    /// </summary>
    public static string DetailToken(string? value, int maximum = 100)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var builder = new System.Text.StringBuilder(value.Length);
        var text = value.Trim();
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            char mapped;
            if (character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or ':' or '<' or '>' or '+' or '-')
                mapped = character;
            else if (character is ',' or ';')
                mapped = '+';
            else
                mapped = '_';
            if (mapped == '+')
            {
                // A list separator swallows the spaces (now "_") round it: "a, b" is "a+b", not "a+_b".
                while (builder.Length > 0 && builder[^1] == '_') builder.Length--;
                builder.Append('+');
                while (index + 1 < text.Length && char.IsWhiteSpace(text[index + 1])) index++;
                continue;
            }
            if (mapped == '_' && builder.Length > 0 && builder[^1] is '_' or '+') continue;
            builder.Append(mapped);
        }
        var token = builder.ToString().Trim('_');
        if (token.Length > maximum) token = token[..maximum].TrimEnd('_');
        return token;
    }

    /// <summary>A finding code fit for <c>diag.is_code</c>: upper case, <c>A-Z 0-9 _</c> only, at most 60.</summary>
    public static string SafeCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Ok;
        var chars = value.Trim().ToUpperInvariant().Select(character => (character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_') ? character : '_').ToArray();
        var code = new string(chars);
        return code.Length <= MaxFindingCodeLength ? code : code[..MaxFindingCodeLength];
    }

    /// <summary>Control characters (line breaks, tabs, U+0080-U+009F, U+2028/9) become spaces: <c>diag.is_safe_text</c>.</summary>
    private const char LineSeparator = (char)0x2028;
    private const char ParagraphSeparator = (char)0x2029;

    public static string SingleLine(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return new string(value.Select(character => char.IsControl(character) || character == LineSeparator || character == ParagraphSeparator ? ' ' : character).ToArray());
    }

    /// <summary>Cuts a text to <paramref name="maximum"/> characters (an ellipsis marks the cut).</summary>
    public static string Clip(string? value, int maximum = MaxExplanationLength)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length <= maximum ? value : value[..(maximum - 3)] + "...";
    }
}
