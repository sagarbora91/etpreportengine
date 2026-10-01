using Etp.Reporting.Application.Imports;

namespace Etp.Reporting.Import.Batch;

/// <summary>
/// Source rows conflict with stored facts, so the whole workbook was rolled back (IF-017). It carries
/// the number of conflicting rows and up to <see cref="MaximumSamples"/> samples read from
/// <c>import_conflicts</c> before the rollback. A sample names the business identity, date, report and
/// the difference the database wrote; never a cell value.
/// </summary>
public sealed class ImportConflictException : Exception
{
    public const int MaximumSamples = 20;

    public ImportConflictException(int count, IReadOnlyList<ImportIssue> samples)
        : base($"{count:N0} conflicting rows. The complete file was rolled back. Review the source and use Restate.")
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        ArgumentNullException.ThrowIfNull(samples);
        Count = count;
        Samples = samples.Take(MaximumSamples).ToArray();
    }

    public string Code => ImportCodes.ImportConflict;
    public int Count { get; }
    public IReadOnlyList<ImportIssue> Samples { get; }
}
