using Etp.Reporting.Import.Documents;

namespace Etp.Reporting.Import.Profiles;

/// <summary>
/// How a catalogue column takes part in identity and change detection (spec 7.1).
/// A Descriptive column is never a Key; timestamps are Ignored, never Descriptive.
/// </summary>
public enum ColumnRole
{
    /// <summary>Part of the document key, and of every fact row.</summary>
    Key,
    /// <summary>A change needs review, except under the automatic rules of spec 8. The default.</summary>
    Fact,
    /// <summary>Master, reference or status data: latest known export wins, archived and audited (OD-3).</summary>
    Attribute,
    /// <summary>Customer, loyalty and store description: never in identity or change detection.</summary>
    Descriptive,
    /// <summary>ETP's own year labels (INVOICEYEAR, REFERENCEYEAR): kept, never compared.</summary>
    Label,
    /// <summary>Timestamp columns: ignored in every comparison.</summary>
    Ignored
}

public enum BusinessUnit { Retail, Service }

/// <summary>How the year of a <see cref="DocumentScope.Document"/> key is found (spec 7.1; OD-1).</summary>
public enum YearRule { None, FinancialYearOfPrimaryDate }

/// <summary>How rows of one document are counted inside one block (spec 7.2).</summary>
public enum RowRule { Multiset, SingleRowPerDocument, StockUnitChain, SnapshotItems }

/// <summary>Whether a newer export changes a stored document by review or by the latest-reading rule (spec 8.2).</summary>
public enum ChangePolicy { Review, LatestReadingWins }

/// <summary>Which facts a family's documents produce (spec 7.1); <see cref="Landing"/> families have no typed facts.</summary>
public enum FamilyRoute { Landing, Sales, Revenue, Enrichment, StockMovement, StockSnapshot }

/// <summary>
/// A family's document identity (spec 7.1, <c>Identity</c> in <c>EtpReportFamilies.json</c>). Field names are
/// canonical field names of the family's columns. Any change here raises <see cref="RulesetVersion"/>.
/// </summary>
public sealed record EtpFamilyIdentity
{
    public DocumentScope Scope { get; init; } = DocumentScope.Date;
    /// <summary>Canonical fields of the key (Document scope); the year comes from <see cref="YearRule"/>.</summary>
    public IReadOnlyList<string> DocumentKey { get; init; } = [];
    public YearRule YearRule { get; init; } = YearRule.None;
    public RowRule RowRule { get; init; } = RowRule.Multiset;
    /// <summary>SnapshotItems: fields that pair rows between two readings of one snapshot.</summary>
    public IReadOnlyList<string> RowKey { get; init; } = [];
    public ChangePolicy ChangePolicy { get; init; } = ChangePolicy.Review;
    /// <summary><c>Column:&lt;field&gt;</c> or <c>Block</c> for snapshot families; null otherwise.</summary>
    public string? SnapshotDate { get; init; }
    /// <summary>Canonical fields that may be NULL on v0 facts and are filled without review (spec 8.2 rule 10).</summary>
    public IReadOnlyList<string> LegacyNullable { get; init; } = [];
    public FamilyRoute Route { get; init; } = FamilyRoute.Landing;
    public int RulesetVersion { get; init; } = 1;

    /// <summary>Typed(d) of spec 8.1: the family's facts live in typed fact tables.</summary>
    public bool HasTypedFacts => Route != FamilyRoute.Landing;

    /// <summary>The field of <c>Column:&lt;field&gt;</c>; null when the snapshot date is not a column.</summary>
    public string? SnapshotDateColumn =>
        SnapshotDate is { } text && text.StartsWith("Column:", StringComparison.Ordinal) ? text["Column:".Length..].Trim() : null;

    /// <summary>True when each block of the source carries its own snapshot date (spec 6.4).</summary>
    public bool SnapshotDateFromBlock => string.Equals(SnapshotDate, "Block", StringComparison.Ordinal);
}
