namespace Etp.Reporting.Import.Service;

// TEMPORARY STAND-IN (lane L3). Lane L0 owns this file and its real content (SERVICE-LANES.md 4.1 step 4). This copy
// holds only the members L3 routes on, so the routing compiles before L0 merges. When feature/service-interim brings
// L0's file, L0's version replaces this one and L3 adapts to its member names.

/// <summary>The Service Centre families of the interim import (decision 15, 3 Oct 2026).</summary>
public static class ServiceInterimFamilies
{
    /// <summary>The store a Service Centre file is imported under when it names none (inactive, unit SERVICE).</summary>
    public const string ServiceStoreCode = "AW330";

    /// <summary>The 35 families the interim lands in a table.</summary>
    public static IReadOnlySet<string> Importable { get; } = Set(
        [2, 3, 4, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26,
         29, 30, 31, 32, 33, 34, 35, 36, 37, 39, 40]);

    /// <summary>A consolidation-made union with no raw export.</summary>
    public static IReadOnlySet<string> Derived { get; } = Set([1]);

    /// <summary>Not needed by the Owner (S005) or retired (S038).</summary>
    public static IReadOnlySet<string> NotNeeded { get; } = Set([5, 38]);

    /// <summary>Deferred to P8.</summary>
    public static IReadOnlySet<string> Deferred { get; } = Set([27, 28]);

    /// <summary>Diagnostic codes of the Service interim (L0's docs/service-centre/SERVICE-INTERIM-NUMBERS.md).</summary>
    public static class Codes
    {
        public const string FamilyDerived = "FAMILY_DERIVED";
        public const string ServiceFamilyNotNeeded = "SERVICE_FAMILY_NOT_NEEDED";
        public const string ServiceFamilyDeferred = "SERVICE_FAMILY_DEFERRED";
        public const string ServiceSnapshotDateNeeded = "SERVICE_SNAPSHOT_DATE_NEEDED";
        public const string ServiceSnapshotDateDiffersFromHistory = "SERVICE_SNAPSHOT_DATE_DIFFERS_FROM_HISTORY";
        public const string ServiceStoreDefaulted = "SERVICE_STORE_DEFAULTED";
    }

    private static IReadOnlySet<string> Set(int[] numbers) =>
        numbers.Select(number => $"S{number:000}").ToHashSet(StringComparer.OrdinalIgnoreCase);
}
