namespace Etp.Reporting.Application.Service;

// Service Centre UI wave (1.10.0, lane parts, design section 3.6): the Parts and purchases read contract over
// v_service_parts (0050, lane sql) and the S009/S013/S006 reading views. Written by lane parts because lane sql's
// contract file was not committed yet (lane brief rule 6); the coordinator reconciles at merge.
// Privacy: no customer field of any kind; purchase invoices, GRNs, indents and GIT lines carry none.
// No column links a pending job to a purchase invoice (design 1.7): the two panels are separate lists.

/// <summary>
/// One Service purchase invoice (S007 created, S008 received, DateLog rule, one row per invoice). <c>Items</c> counts
/// its lines; the quantities and net amount are summed over them. <c>ReceivedDate</c> and <c>GrnDate</c> are null while
/// the invoice is created but not received. <c>SnapshotDate</c> is the latest reading that holds the invoice.
/// </summary>
public sealed record ServicePurchaseInvoice(
    string InvoiceNumber,
    DateOnly? InvoiceDate,
    string? GrnNumber,
    DateOnly? GrnDate,
    DateOnly? ReceivedDate,
    int Items,
    decimal? ShippedQuantity,
    decimal? ReceivedQuantity,
    decimal? NetAmount,
    string? FromLocation,
    DateOnly SnapshotDate);

/// <summary>One line of a purchase invoice (invoice -> lines drill-down).</summary>
public sealed record ServicePurchaseInvoiceLine(
    string InvoiceNumber,
    string? ItemCode,
    string? ItemDescription,
    decimal? ShippedQuantity,
    decimal? ReceivedQuantity,
    decimal? NetAmount);

/// <summary>
/// A job on the latest S009 Pending repair list whose spare is awaited (<c>sparerequired</c> / indent raised).
/// <c>DaysWaiting</c> = list snapshot date - indent date (job date when the indent date is blank).
/// </summary>
public sealed record ServiceJobWaitingForParts(
    string JobOrderNumber,
    string? SpareRequired,
    string? SpareCode,
    string? IndentNumber,
    DateOnly? IndentDate,
    int? DaysWaiting,
    string? Brand,
    string? Model,
    DateOnly SnapshotDate);

/// <summary>One S013 goods-in-transit line (DateLog rule).</summary>
public sealed record ServiceGitLine(
    DateOnly? TransactionDate,
    string? DocumentNumber,
    string? ItemCode,
    string? ItemDescription,
    decimal? Quantity,
    decimal? Value,
    string? FromLocation,
    string? ToLocation,
    DateOnly SnapshotDate);

/// <summary>The latest S006 closing stock reading: count and value only (design 3.6).</summary>
public sealed record ServiceClosingStock(DateOnly SnapshotDate, int Items, decimal? Quantity, decimal? Value);

/// <summary>Everything the Parts screen shows, read in one call (<see cref="IServiceReportQuery.LoadPartsAsync"/>).</summary>
public sealed record ServiceParts(
    IReadOnlyList<ServicePurchaseInvoice> Invoices,
    IReadOnlyList<ServicePurchaseInvoiceLine> Lines,
    IReadOnlyList<ServiceJobWaitingForParts> JobsWaitingForParts,
    IReadOnlyList<ServiceGitLine> GitLines,
    ServiceClosingStock? ClosingStock)
{
    public static ServiceParts Empty { get; } = new([], [], [], [], null);
}

/// <summary>The pure rules of the Parts screen (design 3.6): open/closed and days open, so the screen and its tests agree.</summary>
public static class ServicePartsRules
{
    public const string Open = "Open";
    public const string Closed = "Closed";

    /// <summary>An invoice is open until a received date or a GRN date is exported for it.</summary>
    public static bool IsOpen(ServicePurchaseInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        return invoice.ReceivedDate is null && invoice.GrnDate is null;
    }

    public static string Status(ServicePurchaseInvoice invoice) => IsOpen(invoice) ? Open : Closed;

    /// <summary>Snapshot date - invoice date while open; received (or GRN) date - invoice date once closed. Null without an invoice date.</summary>
    public static int? DaysOpen(ServicePurchaseInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        if (invoice.InvoiceDate is not { } invoiced) return null;
        var until = IsOpen(invoice) ? invoice.SnapshotDate : invoice.ReceivedDate ?? invoice.GrnDate!.Value;
        return Math.Max(0, until.DayNumber - invoiced.DayNumber);
    }
}
