using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Import.Conversion;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Stock;

public sealed record ParsedStockMovement(string StoreCode,string DocumentNumber,DateOnly DocumentDate,string ProductCode,string SourceTransactionType,string? FromLocation,string? ToLocation,decimal OpeningQuantity,decimal TransactionQuantity,decimal ClosingQuantity,SourceLineage Lineage);
public sealed record ParsedStockSnapshot(string StoreCode,DateOnly SnapshotDate,string ProductCode,string? Ean,string? BrandCode,string? Cluster,string? Gender,string? BatchNumber,string? SourceUid,decimal Quantity,decimal? UnitCost,decimal? TotalCost,SourceLineage Lineage);
public sealed record StockWorkbookParseResult(string ReportCode,IReadOnlyList<ParsedStockMovement> Movements,IReadOnlyList<ParsedStockSnapshot> Snapshots,IReadOnlyList<ImportDiagnostic> Diagnostics)
{
    public bool HasBlockers => Diagnostics.Any(x => x.Severity == ImportDiagnosticSeverity.Blocker);
}

public sealed class StockWorkbookParser
{
    private static readonly HashSet<string> KnownTypes = new(StringComparer.OrdinalIgnoreCase) { "INV", "SR", "Purchase Return", "Purchase Receipt" };
    private readonly TypedCellConverter converter = new();

    public StockWorkbookParseResult Parse(WorkbookSnapshot workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        if (workbook.Sheets.Count != 1) return Blocked("WORKBOOK_SHEET_COUNT", "A stock workbook must contain exactly one worksheet.");
        var normalized = WorkbookLayoutNormalizer.Normalize(workbook.Sheets[0]);
        if (normalized.Sheet is null) return new("UNKNOWN", [], [], normalized.Diagnostics);
        var sheet = normalized.Sheet;
        var profile = new ImportProfileMatcher().Match(
            sheet.Headers,
            ApprovedImportProfileRegistry.All.Where(candidate =>
                candidate.ReportCode is "STOCK_LEDGER" or "CLOSING_STOCK"));
        if (profile is null) return new("UNKNOWN", [], [], normalized.Diagnostics.Append(new("UNKNOWN_STOCK_LAYOUT",ImportDiagnosticSeverity.Blocker,"The stock layout is not an approved exact-header profile.",sheet.Name)).ToArray());
        return profile.ReportCode == "STOCK_LEDGER" ? ParseLedger(workbook.Sha256,sheet,normalized.Diagnostics) : ParseClosing(workbook.Sha256,sheet,normalized.Diagnostics);
    }

    public StockWorkbookParseResult Parse(MatchedImportEnvelope accepted)
    {
        ArgumentNullException.ThrowIfNull(accepted);
        var reportCode = accepted.ProfileIdentity.ReportCode;
        if (reportCode is not ("STOCK_LEDGER" or "CLOSING_STOCK"))
            return new("UNKNOWN", [], [], accepted.Diagnostics.Append(new(
                "UNKNOWN_STOCK_LAYOUT",
                ImportDiagnosticSeverity.Blocker,
                "The accepted import is not an approved stock profile.",
                accepted.MatchedSheet.Name)).ToArray());

        var diagnostics = accepted.Diagnostics.ToList();
        if (reportCode == "STOCK_LEDGER")
        {
            var movements = new List<ParsedStockMovement>();
            foreach (var row in accepted.Staging.Rows)
            {
                var values = row.Values;
                var type = Required<string>(values, "source_transaction_type");
                var opening = Required<decimal>(values, "opening_quantity");
                var transaction = Required<decimal>(values, "transaction_quantity");
                var closing = Required<decimal>(values, "closing_quantity");
                if (!KnownTypes.Contains(type))
                    diagnostics.Add(new("UNKNOWN_STOCK_TRANSACTION_TYPE", ImportDiagnosticSeverity.Blocker,
                        "The source transaction type is not approved for stock reporting.", accepted.MatchedSheet.Name,
                        row.SourceRowNumber, "TRANS_TYPE"));
                if (opening + transaction != closing)
                    diagnostics.Add(new("STOCK_BALANCE_MISMATCH", ImportDiagnosticSeverity.Blocker,
                        "Closing quantity does not equal opening plus source transaction quantity.",
                        accepted.MatchedSheet.Name, row.SourceRowNumber));
                movements.Add(new(
                    Required<string>(values, "store_code"),
                    Required<string>(values, "document_number"),
                    Required<DateOnly>(values, "document_date"),
                    Required<string>(values, "product_code"),
                    type,
                    Optional<string>(values, "from_location"),
                    Optional<string>(values, "to_location"),
                    opening,
                    transaction,
                    closing,
                    new(accepted.Workbook.Sha256, accepted.MatchedSheet.Name, row.SourceRowNumber)));
            }
            return new(reportCode, movements, [], diagnostics);
        }

        var snapshots = accepted.Staging.Rows.Select(row =>
        {
            var values = row.Values;
            return new ParsedStockSnapshot(
                Required<string>(values, "store_code"),
                Required<DateOnly>(values, "snapshot_date"),
                Required<string>(values, "product_code"),
                Optional<string>(values, "ean"),
                Optional<string>(values, "brand_code"),
                Optional<string>(values, "cluster"),
                Optional<string>(values, "gender"),
                Optional<string>(values, "batch_number"),
                Optional<string>(values, "source_uid"),
                Required<decimal>(values, "quantity"),
                OptionalValue<decimal>(values, "unit_cost"),
                OptionalValue<decimal>(values, "total_cost"),
                new(accepted.Workbook.Sha256, accepted.MatchedSheet.Name, row.SourceRowNumber));
        }).ToArray();
        return new(reportCode, [], snapshots, diagnostics);
    }

    private StockWorkbookParseResult ParseLedger(string hash,WorkbookSheet sheet,IReadOnlyList<ImportDiagnostic> initial)
    {
        var rows=new List<ParsedStockMovement>();var diagnostics=initial.ToList();var h=HeaderMap(sheet);
        foreach(var row in sheet.Rows)
        {
            if(!Try(row,h,"TRANS_TYPE",CanonicalDataType.Text,true,out string? type,diagnostics,sheet.Name) || !Try(row,h,"STORE CODE",CanonicalDataType.Identifier,true,out string? store,diagnostics,sheet.Name) || !Try(row,h,"DOCUMENTNUMBER",CanonicalDataType.Identifier,true,out string? doc,diagnostics,sheet.Name) || !Try(row,h,"DOCUMENTDATE",CanonicalDataType.Date,true,out DateOnly date,diagnostics,sheet.Name) || !Try(row,h,"ITEMNUMBER",CanonicalDataType.Identifier,true,out string? product,diagnostics,sheet.Name) || !Try(row,h,"OPENING_QTY",CanonicalDataType.Decimal,true,out decimal opening,diagnostics,sheet.Name) || !Try(row,h,"TRANS_QTY",CanonicalDataType.Decimal,true,out decimal trans,diagnostics,sheet.Name) || !Try(row,h,"CLOSING_QTY",CanonicalDataType.Decimal,true,out decimal closing,diagnostics,sheet.Name)) continue;
            if(!KnownTypes.Contains(type!)) diagnostics.Add(new("UNKNOWN_STOCK_TRANSACTION_TYPE",ImportDiagnosticSeverity.Blocker,"The source transaction type is not approved for stock reporting.",sheet.Name,row.RowNumber,"TRANS_TYPE"));
            if(opening+trans!=closing) diagnostics.Add(new("STOCK_BALANCE_MISMATCH",ImportDiagnosticSeverity.Blocker,"Closing quantity does not equal opening plus source transaction quantity.",sheet.Name,row.RowNumber));
            Try(row,h,"FROM LOCATION",CanonicalDataType.Identifier,false,out string? from,diagnostics,sheet.Name);Try(row,h,"TO LOCATION",CanonicalDataType.Identifier,false,out string? to,diagnostics,sheet.Name);
            rows.Add(new(store!,doc!,date,product!,type!,from,to,opening,trans,closing,new(hash,sheet.Name,row.RowNumber)));
        }
        return new("STOCK_LEDGER",rows,[],diagnostics);
    }

    private StockWorkbookParseResult ParseClosing(string hash,WorkbookSheet sheet,IReadOnlyList<ImportDiagnostic> initial)
    {
        var rows=new List<ParsedStockSnapshot>();var diagnostics=initial.ToList();var h=HeaderMap(sheet);
        foreach(var row in sheet.Rows)
        {
            if(!Try(row,h,"STORE CODE",CanonicalDataType.Identifier,true,out string? store,diagnostics,sheet.Name) || !Try(row,h,"Date",CanonicalDataType.Date,true,out DateOnly date,diagnostics,sheet.Name) || !Try(row,h,"ITEMNUMBER",CanonicalDataType.Identifier,true,out string? product,diagnostics,sheet.Name) || !Try(row,h,"QTY",CanonicalDataType.Decimal,true,out decimal qty,diagnostics,sheet.Name)) continue;
            Try(row,h,"EAN NO",CanonicalDataType.Identifier,false,out string? ean,diagnostics,sheet.Name);Try(row,h,"BRAND",CanonicalDataType.Identifier,false,out string? brand,diagnostics,sheet.Name);Try(row,h,"CLUSTER",CanonicalDataType.Identifier,false,out string? cluster,diagnostics,sheet.Name);Try(row,h,"GENDER",CanonicalDataType.Text,false,out string? gender,diagnostics,sheet.Name);Try(row,h,"BATCHNUMBER",CanonicalDataType.Identifier,false,out string? batch,diagnostics,sheet.Name);Try(row,h,"UID_ITEM",CanonicalDataType.Identifier,false,out string? uid,diagnostics,sheet.Name);Try(row,h,"UCP",CanonicalDataType.Decimal,false,out decimal? unit,diagnostics,sheet.Name);Try(row,h,"TOTALUCP",CanonicalDataType.Decimal,false,out decimal? total,diagnostics,sheet.Name);
            rows.Add(new(store!,date,product!,ean,brand,cluster,gender,batch,uid,qty,unit,total,new(hash,sheet.Name,row.RowNumber)));
        }
        return new("CLOSING_STOCK",[],rows,diagnostics);
    }

    private bool Try<T>(WorkbookRow row,IReadOnlyDictionary<string,int> headers,string name,CanonicalDataType type,bool required,out T value,List<ImportDiagnostic> diagnostics,string sheet)
    {
        var index=headers[ImportProfile.NormalizeHeader(name)];
        var result=converter.Convert(index < row.Cells.Count ? row.Cells[index].Value : null,type,required);
        if(!result.IsSuccess){diagnostics.Add(new(result.ErrorCode!,ImportDiagnosticSeverity.Blocker,result.ErrorMessage!,sheet,row.RowNumber,name));value=default!;return false;}
        value=result.Value is null?default!:(T)result.Value;return true;
    }
    private static Dictionary<string,int> HeaderMap(WorkbookSheet s)=>s.Headers.Select((x,i)=>(x,i)).ToDictionary(x=>ImportProfile.NormalizeHeader(x.x),x=>x.i,StringComparer.Ordinal);
    private static StockWorkbookParseResult Blocked(string code,string message)=>new("UNKNOWN",[],[],[new(code,ImportDiagnosticSeverity.Blocker,message)]);
    private static T Required<T>(IReadOnlyDictionary<string, object?> values, string key) =>
        values.TryGetValue(key, out var value) && value is T typed
            ? typed
            : throw new InvalidOperationException($"Required staged stock field '{key}' is missing.");
    private static T? Optional<T>(IReadOnlyDictionary<string, object?> values, string key) where T : class =>
        values.TryGetValue(key, out var value) ? value as T : null;
    private static T? OptionalValue<T>(IReadOnlyDictionary<string, object?> values, string key) where T : struct =>
        values.TryGetValue(key, out var value) && value is T typed ? typed : null;
}
