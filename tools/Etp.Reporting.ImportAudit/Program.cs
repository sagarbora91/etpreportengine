using Etp.Reporting.Application.Imports;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

var database=Option("--database")??"EtpReportingHelios";
if(database!="EtpReportingHelios" && !(database.StartsWith("EtpPhase1Test_",StringComparison.Ordinal) && database.All(c=>char.IsAsciiLetterOrDigit(c)||c=='_')))
    throw new ArgumentException("This audit tool only writes EtpReportingHelios or a generated EtpPhase1Test_ database.");
var folders=args.Select((value,index)=>(value,index)).Where(x=>x.value=="--folder" && x.index+1<args.Length).Select(x=>args[x.index+1]).ToArray();
// The last residual of P4-5: this tool used to set TrustServerCertificate=true by hand and
// connect without the check every other caller goes through. It now takes the same boundary
// as the application - a SQL Server on this computer, Windows authentication, Encrypt=Optional
// - so a --server elsewhere is refused here rather than quietly trusted.
var connectionString=LocalSqlConnectionPolicy.Validate(new SqlConnectionStringBuilder
    { DataSource=Option("--server")??@".\SQLEXPRESS",InitialCatalog=database,IntegratedSecurity=true,ConnectTimeout=5 }.ConnectionString);
if(args.Contains("--rebuild"))
{
    var master=LocalSqlConnectionPolicy.Validate(new SqlConnectionStringBuilder(connectionString){InitialCatalog="master"}.ConnectionString);
    await using var connection=new SqlConnection(master);await connection.OpenAsync();
    await using var command=new SqlCommand($"IF DB_ID(N'{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END",connection);
    await command.ExecuteNonQueryAsync();
}
await new SqlServerDatabaseBootstrapper(connectionString,new DirectoryMigrationSource(Path.Combine(AppContext.BaseDirectory,"database","migrations"))).BootstrapAsync();
Console.WriteLine($"Audit database ready: {database}");
if(folders.Length>0)
{
    var service=new FolderImportService(new SqlServerImportPersistenceUseCase(connectionString));
    var summary=folders.Length==1 ? await service.RunAsync(folders[0],new(Environment.UserName))
        : await service.RunFilesAsync(folders.SelectMany(path=>Directory.EnumerateFiles(path,"*.xlsx",SearchOption.AllDirectories))
            .Where(path=>!Path.GetFileName(path).StartsWith("~$")).ToArray(),new(Environment.UserName));
    foreach(var f in summary.Files)
        Console.WriteLine($"{f.FileName} | {f.ReportCode} | {f.StoreCode} | {f.PeriodStart:yyyy-MM-dd}..{f.PeriodEnd:yyyy-MM-dd} | {f.Status} | rows={f.RowsProcessed} new={f.NewRows} present={f.AlreadyPresentRows} conflicts={f.ConflictRows} | {f.Message}");
    foreach(var f in summary.Files.Where(f=>f.Failed))
        foreach(var d in f.Diagnostics??[]) Console.WriteLine($"  {d.Code}: {d.Message}");
    if(summary.Files.Any(f=>f.Failed)) Environment.ExitCode=1;
}
await using(var connection=new SqlConnection(connectionString))
{
    await connection.OpenAsync();
    const string sql="""
      SELECT 'sales_lines' metric,CONVERT(varchar(80),COUNT(*)) value FROM dbo.sales_lines
      UNION ALL SELECT 'sales_invoices',CONVERT(varchar(80),COUNT(*)) FROM dbo.sales_invoices
      UNION ALL SELECT 'conflict_outcomes',CONVERT(varchar(80),COUNT(*)) FROM dbo.import_row_outcomes WHERE outcome='CONFLICT'
      UNION ALL SELECT 'stock_movements',CONVERT(varchar(80),COUNT(*)) FROM dbo.stock_movements;
      SELECT i.store_code,FORMAT(i.transaction_date,'yyyy-MM') month,COUNT(DISTINCT i.sales_invoice_id) documents,
        SUM(CASE WHEN l.source_transaction_type='INV' THEN 0 ELSE 1 END) return_lines,
        SUM(l.source_quantity) qty,SUM(l.source_gross_amount) netamount,SUM(l.source_net_amount) netvalue,SUM(l.source_tax_amount) tax
      FROM dbo.sales_lines l JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id
      GROUP BY i.store_code,FORMAT(i.transaction_date,'yyyy-MM') ORDER BY i.store_code,month;
      SELECT store_code,document_number,invoice_year,transaction_date FROM dbo.sales_invoices WHERE document_number='100000068' ORDER BY store_code,invoice_year;
      """;
    await using var query=new SqlCommand(sql,connection);await using var reader=await query.ExecuteReaderAsync();
    do
    {
        Console.WriteLine(string.Join(" | ",Enumerable.Range(0,reader.FieldCount).Select(reader.GetName)));
        while(await reader.ReadAsync()) Console.WriteLine(string.Join(" | ",Enumerable.Range(0,reader.FieldCount).Select(i=>Convert.ToString(reader.GetValue(i),System.Globalization.CultureInfo.InvariantCulture))));
    } while(await reader.NextResultAsync());
}
string? Option(string name)
{
    var index=Array.IndexOf(args,name);
    return index>=0 && index+1<args.Length?args[index+1]:null;
}
