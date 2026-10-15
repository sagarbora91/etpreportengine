<#
.SYNOPSIS
Lane V1 (code-protection plan 4.3.5 / 5.4). Proves a PROTECTED (obfuscated) ETP publish behaves exactly like the UNPROTECTED publish
of the same commit: same schema, same imported fact tables, same report-pack data, same Excel cells, same PDF content.
Exit 0 ONLY when every compared item is identical and every step was actually driven.

.DESCRIPTION
Run by Sagar, ELEVATED, on Workpc or the VM, after build-windows-release.ps1 -Protect (contract 5.1: protected publish in
<OutputDirectory>, unprotected publish in <OutputDirectory>-unprotected). Nothing here runs during development or in the gate.

What it does, per build (unprotected first, then protected), against its OWN scratch database:
  1. The shipped exe creates the database: `Etp.Reporting.Desktop.exe --initialize-database --connection-string ...`.
  2. `--plan-database-changes --connection-string ...` must report no pending migrations or objects.
  3. A small probe host (generated here, compiled once with the dev SDK, run once per build; it loads the build's own Etp.*.dll
     files) imports the fixture folder with FolderImportService, builds the combined daily report pack with
     DailyReportingPackService, and exports it to .xlsx and .pdf with the shipped exporters.
  4. This script then checksums every fact table (columns and tables listed in $Script:ExcludedNondeterministic are skipped), reads
     every Excel cell, normalises the PDF, runs DBCC CHECKDB, and compares the two builds.

Scratch databases are named <Database>_U and <Database>_P and must match EtpAccept_*. The script refuses if either already exists,
creates them only through the exe, and drops only those two at the end (unless -KeepDatabases). EtpReporting is never touched: the
database name is validated against ^EtpAccept_[A-Za-z0-9_]+$ before anything runs, and the machine-wide operations.json (used by
--automation-once) is never read or written.

Exit codes: 0 identical and complete; 1 a difference was found; 2 refused or bad usage; 3 incomplete (a step could not be driven or failed).

.EXAMPLE
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\release\Test-EtpProtectedBuild.ps1 -Protected E:\ETP\Installers\build\win -Unprotected E:\ETP\Installers\build\win-unprotected -FixtureFolder tests-dotnet\fixtures\etp-sample -Database EtpAccept_V1
#>
[CmdletBinding()]
param(
    [string]$Protected,
    [string]$Unprotected,
    [string]$FixtureFolder,
    [string]$Database,
    [string]$Server = '.\SQLEXPRESS',
    [string]$OutputFolder,
    [string]$Date,
    [switch]$KeepDatabases,
    # Tests and Measure-EtpPerformance.ps1 dot-source this file with -LoadFunctionsOnly to reuse the functions.
    [switch]$LoadFunctionsOnly
)
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------------------------------------------------------
# The explicit list of NONDETERMINISTIC metadata that is left out of the comparison. Everything not listed here must be identical.
# Adding an entry needs a reason; the report prints this list so a reader sees exactly what was not compared.
# ---------------------------------------------------------------------------------------------------------------------------
$Script:ExcludedNondeterministic = [ordered]@{
    'sql.column.types'  = 'timestamp/rowversion, uniqueidentifier, datetime, datetime2, smalldatetime, datetimeoffset, xml, text, ntext, image, geography, geometry, hierarchyid, sql_variant columns are not part of the checksum (clock stamps, generated ids, types BINARY_CHECKSUM cannot hold). `date` columns (business dates) ARE compared.'
    'sql.table.names'   = 'Tables whose name matches (^|_)(log|logs|audit|diagnostic|diagnostics|heartbeat|lease|session|sessions|health_check_run|health_check_runs)($|_) are compared by row count only: they record when and by whom something ran.'
    'xlsx.parts'        = 'docProps/core.xml and docProps/app.xml (created/modified time, application name). Every worksheet cell, shared string and sheet name is compared.'
    'pdf.metadata'      = '/CreationDate, /ModDate, /ID and everything after the last "xref" (byte offsets); the decoded page content streams, page count, fonts and object structure are compared.'
    'pack.generatedUtc' = 'The probe host replaces ReportPackDocument.GeneratedUtc with a fixed instant (2000-01-01T00:00:00Z) before export in BOTH builds, so no generated-at text reaches the files.'
    'import.guid'       = 'Batch ids (Guid) and file timings are not written to the import summary; counts, codes, periods, status and messages are.'
}
$Script:ExcludedTablePattern = '(^|_)(log|logs|audit|diagnostic|diagnostics|heartbeat|lease|session|sessions|health_check_run|health_check_runs)($|_)'
$Script:ExcludedColumnTypes = @('timestamp', 'uniqueidentifier', 'datetime', 'datetime2', 'smalldatetime', 'datetimeoffset', 'xml', 'text', 'ntext', 'image', 'geography', 'geometry', 'hierarchyid', 'sql_variant')
$Script:EtpAssemblies = @('Etp.Reporting.Domain', 'Etp.Reporting.Application', 'Etp.Reporting.Import', 'Etp.Reporting.Infrastructure.SqlServer', 'Etp.Reporting.Infrastructure.Http', 'Etp.Reporting.Reporting')

function Stop-EtpRefused([string]$Message) {
    $exception = New-Object System.InvalidOperationException("ETP-REFUSED: $Message")
    throw $exception
}

# ------------------------------------------------------------------ guards
function Assert-EtpScratchDatabase([string]$Name) {
    if ([string]::IsNullOrWhiteSpace($Name)) { Stop-EtpRefused 'Provide -Database EtpAccept_<name>.' }
    if ($Name -cnotmatch '^EtpAccept_[A-Za-z0-9_]{1,100}$') { Stop-EtpRefused "Database '$Name' is not an EtpAccept_* scratch name (letters, digits, underscore; case-sensitive). EtpReporting and every other database are refused." }
}
function Assert-EtpLocalServer([string]$Instance) {
    $machine = [regex]::Escape($env:COMPUTERNAME)
    if ($Instance -notmatch ('^(\.|\(local\)|localhost|' + $machine + ')(\\[A-Za-z0-9_]+)?$')) { Stop-EtpRefused "Server '$Instance' is not a SQL Server instance on this computer." }
}
function Test-EtpElevated {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}
function Get-EtpScratchNames([string]$Base) { return [pscustomobject]@{ Unprotected = ($Base + '_U'); Protected = ($Base + '_P') } }

function Assert-EtpBuildFolder([string]$Folder, [string]$Label) {
    if ([string]::IsNullOrWhiteSpace($Folder) -or -not (Test-Path -LiteralPath $Folder -PathType Container)) { Stop-EtpRefused "$Label folder does not exist: $Folder" }
    if (-not (Test-Path -LiteralPath (Join-Path $Folder 'Etp.Reporting.Desktop.exe'))) { Stop-EtpRefused "$Label folder has no Etp.Reporting.Desktop.exe: $Folder" }
    foreach ($name in $Script:EtpAssemblies) {
        if (-not (Test-Path -LiteralPath (Join-Path $Folder ($name + '.dll')))) {
            Stop-EtpRefused "$Label folder has no $name.dll beside the exe. A single-file bundle cannot be probed: decision D4 makes the release a folder publish, and the unprotected comparison build must be a folder publish too."
        }
    }
    foreach ($sub in 'database\migrations', 'database\objects') {
        if (-not (Test-Path -LiteralPath (Join-Path $Folder $sub))) { Stop-EtpRefused "$Label folder has no $sub." }
    }
}
function Assert-EtpIsReallyProtected([string]$ProtectedFolder, [string]$UnprotectedFolder) {
    $pdbs = @(Get-ChildItem -LiteralPath $ProtectedFolder -Recurse -Filter '*.pdb' -File -ErrorAction SilentlyContinue)
    if ($pdbs.Count -gt 0) { Stop-EtpRefused ("The protected folder contains debug symbols (" + (($pdbs | Select-Object -First 3 | ForEach-Object { $_.Name }) -join ', ') + "). Symbols are archived beside it, never inside (plan 4.2).") }
    $same = @()
    foreach ($name in $Script:EtpAssemblies) {
        $a = (Get-FileHash -LiteralPath (Join-Path $ProtectedFolder ($name + '.dll')) -Algorithm SHA256).Hash
        $b = (Get-FileHash -LiteralPath (Join-Path $UnprotectedFolder ($name + '.dll')) -Algorithm SHA256).Hash
        if ($a -eq $b) { $same += $name }
    }
    # Domain/Application/Http may be configured out of obfuscation, but Reporting and SqlServer are the point of the exercise.
    foreach ($must in 'Etp.Reporting.Reporting', 'Etp.Reporting.Infrastructure.SqlServer') {
        if ($same -contains $must) { Stop-EtpRefused "$must.dll is byte-identical in both folders: the 'protected' build is not protected (or both folders are the same build). Nothing was compared." }
    }
    return $same
}

# ------------------------------------------------------------------ SQL helpers (Windows PowerShell 5.1: System.Data.SqlClient)
function New-EtpSqlConnection([string]$Instance, [string]$DatabaseName) {
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder['Data Source'] = $Instance
    $builder['Initial Catalog'] = $DatabaseName
    $builder['Integrated Security'] = $true
    $builder['Connect Timeout'] = 15
    $builder['Encrypt'] = $false
    $connection = New-Object System.Data.SqlClient.SqlConnection($builder.ConnectionString)
    $connection.Open()
    return $connection
}
function Invoke-EtpSqlTable([string]$Instance, [string]$DatabaseName, [string]$Query) {
    $connection = New-EtpSqlConnection $Instance $DatabaseName
    try {
        $command = $connection.CreateCommand(); $command.CommandText = $Query; $command.CommandTimeout = 600
        $table = New-Object System.Data.DataTable
        $adapter = New-Object System.Data.SqlClient.SqlDataAdapter($command)
        [void]$adapter.Fill($table)
        return , $table
    } finally { $connection.Dispose() }
}
function Invoke-EtpSqlNonQuery([string]$Instance, [string]$DatabaseName, [string]$Query) {
    $connection = New-EtpSqlConnection $Instance $DatabaseName
    try { $command = $connection.CreateCommand(); $command.CommandText = $Query; $command.CommandTimeout = 600; [void]$command.ExecuteNonQuery() } finally { $connection.Dispose() }
}
function Test-EtpDatabaseExists([string]$Instance, [string]$Name) {
    $table = Invoke-EtpSqlTable $Instance 'master' ("SELECT COUNT(*) FROM sys.databases WHERE name = N'" + $Name.Replace("'", "''") + "'")
    return ([int]$table.Rows[0][0]) -gt 0
}
function Remove-EtpScratchDatabase([string]$Instance, [string]$Name) {
    Assert-EtpScratchDatabase $Name
    Invoke-EtpSqlNonQuery $Instance 'master' "IF DB_ID(N'$Name') IS NOT NULL BEGIN ALTER DATABASE [$Name] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$Name]; END"
}
function Get-EtpConnectionStringArgument([string]$Instance, [string]$DatabaseName) {
    # Windows authentication, Encrypt=Optional (the app's LocalSqlConnectionPolicy accepts it); passed quoted as ONE argument.
    return "Server=$Instance;Database=$DatabaseName;Integrated Security=True;Encrypt=Optional;TrustServerCertificate=True"
}

# Fact-table checksums. Returns an ordered map "schema.table" -> "rows=N;chk=...;sum=...;cols=K/M" (or "rows=N;listed-only").
function Get-EtpTableChecksums([string]$Instance, [string]$DatabaseName) {
    $columns = Invoke-EtpSqlTable $Instance $DatabaseName @"
SELECT s.name AS sn, t.name AS tn, c.name AS cn, ty.name AS tyn
FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
JOIN sys.columns c ON c.object_id = t.object_id
JOIN sys.types ty ON ty.user_type_id = c.user_type_id
WHERE t.is_ms_shipped = 0
ORDER BY s.name, t.name, c.column_id
"@
    $byTable = [ordered]@{}
    foreach ($row in $columns.Rows) {
        $key = '{0}.{1}' -f $row['sn'], $row['tn']
        if (-not $byTable.Contains($key)) { $byTable[$key] = New-Object System.Collections.ArrayList }
        [void]$byTable[$key].Add([pscustomobject]@{ Name = [string]$row['cn']; Type = [string]$row['tyn'] })
    }
    $result = [ordered]@{}
    foreach ($key in @($byTable.Keys)) {
        $parts = $key.Split([char[]]'.', 2)
        $quoted = '[{0}].[{1}]' -f $parts[0].Replace(']', ']]'), $parts[1].Replace(']', ']]')
        $kept = @($byTable[$key] | Where-Object { $Script:ExcludedColumnTypes -notcontains $_.Type })
        $listedOnly = ($parts[1] -match $Script:ExcludedTablePattern) -or ($kept.Count -eq 0)
        if ($listedOnly) {
            $count = Invoke-EtpSqlTable $Instance $DatabaseName "SELECT COUNT_BIG(*) FROM $quoted"
            $result[$key] = 'rows={0};listed-only' -f $count.Rows[0][0]
        } else {
            $list = (@($kept | ForEach-Object { '[' + $_.Name.Replace(']', ']]') + ']' })) -join ', '
            $row = (Invoke-EtpSqlTable $Instance $DatabaseName "SELECT COUNT_BIG(*) AS n, CHECKSUM_AGG(BINARY_CHECKSUM($list)) AS c, SUM(CONVERT(bigint, CHECKSUM($list))) AS s FROM $quoted").Rows[0]
            $result[$key] = 'rows={0};chk={1};sum={2};cols={3}/{4}' -f $row['n'], $row['c'], $row['s'], $kept.Count, $byTable[$key].Count
        }
    }
    return $result
}
function Get-EtpHealthFacts([string]$Instance, [string]$DatabaseName, [string]$PlanJson) {
    # DBCC CHECKDB raises an error on corruption; NO_INFOMSGS keeps a clean run silent.
    Invoke-EtpSqlNonQuery $Instance $DatabaseName "DBCC CHECKDB ([$DatabaseName]) WITH NO_INFOMSGS"
    $migrations = Invoke-EtpSqlTable $Instance $DatabaseName 'SELECT COUNT(*) FROM dbo.schema_migrations'
    return [ordered]@{ 'checkdb' = 'clean'; 'applied-migrations' = [string]$migrations.Rows[0][0]; 'plan-database-changes' = $PlanJson }
}

# ------------------------------------------------------------------ process helper
function Start-EtpProcess([string]$FilePath, [string]$Arguments, [hashtable]$Environment, [int]$TimeoutSeconds, [string]$WorkingDirectory) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $FilePath; $info.Arguments = $Arguments; $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true; $info.CreateNoWindow = $true
    if ($WorkingDirectory) { $info.WorkingDirectory = $WorkingDirectory }
    if ($Environment) { foreach ($k in $Environment.Keys) { $info.EnvironmentVariables[[string]$k] = [string]$Environment[$k] } }
    $process = New-Object System.Diagnostics.Process; $process.StartInfo = $info
    [void]$process.Start()
    $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        try { $process.Kill() } catch { }
        return [pscustomobject]@{ ExitCode = -1; TimedOut = $true; Stdout = ''; Stderr = 'timed out after ' + $TimeoutSeconds + ' s' }
    }
    $process.WaitForExit()
    return [pscustomobject]@{ ExitCode = $process.ExitCode; TimedOut = $false; Stdout = $stdout.Result; Stderr = $stderr.Result }
}

# ------------------------------------------------------------------ the probe host (C#), generated and compiled by this script
$Script:ProbeProgram = @'
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Reporting;

// Usage: EtpProbeHost <dll> --build-dir <dir> --connection-string <cs> --fixture <dir> --out <dir> [--date yyyy-MM-dd]
// Loads the Etp.*, SqlClient, OpenXml and PdfSharp assemblies from <dir> (the build under test), never from its own folder.
internal static class Program
{
    private static string buildDir = "";

    public static async Task<int> Main(string[] args)
    {
        string Opt(string n) { var i = Array.IndexOf(args, n); return i >= 0 && i + 1 < args.Length ? args[i + 1] : throw new ArgumentException("Missing " + n); }
        buildDir = Path.GetFullPath(Opt("--build-dir"));
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var path = Path.Combine(buildDir, name.Name + ".dll");
            return name.Name is not null && File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };
        try { return await RunAsync(Opt("--connection-string"), Opt("--fixture"), Path.GetFullPath(Opt("--out")), args.Contains("--date") ? Opt("--date") : null).ConfigureAwait(false); }
        catch (Exception exception) { Console.Error.WriteLine("PROBE-FAILED " + exception.GetType().FullName + ": " + exception.Message); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<int> RunAsync(string cs, string fixture, string output, string? date)
    {
        Directory.CreateDirectory(output);
        var timings = new Dictionary<string, long>();
        var clock = Stopwatch.StartNew();

        var stores = await new StoreCatalogRepository(cs).ActiveCodesAsync().ConfigureAwait(false);
        var service = new FolderImportService(new SqlServerImportPersistenceUseCase(cs), knownStores: stores);
        var summary = await service.RunAsync(fixture, new FolderImportOptions("verification")).ConfigureAwait(false);
        timings["importMs"] = clock.ElapsedMilliseconds;
        var lines = summary.Files.OrderBy(f => f.FileName, StringComparer.Ordinal).Select(f => string.Join("|", new[]
        {
            f.FileName, f.ReportCode ?? "", f.StoreCode ?? "", f.PeriodStart?.ToString("yyyy-MM-dd") ?? "", f.PeriodEnd?.ToString("yyyy-MM-dd") ?? "",
            f.Status, f.RowsProcessed.ToString(), f.NewRows.ToString(), f.AlreadyPresentRows.ToString(), f.ConflictRows.ToString(), f.Message
        })).ToArray();
        File.WriteAllText(Path.Combine(output, "import.json"), JsonSerializer.Serialize(new { files = lines, failed = summary.Files.Count(f => f.Failed) }), new UTF8Encoding(false));

        var businessDate = date is not null
            ? DateOnly.ParseExact(date, "yyyy-MM-dd")
            : summary.Files.Where(f => !f.Failed && f.PeriodEnd is not null).Select(f => f.PeriodEnd!.Value).DefaultIfEmpty().Max();
        if (businessDate == default) throw new InvalidOperationException("No business date: the fixture import produced no dated file. Pass -Date.");

        clock.Restart();
        var pack = await new DailyReportingPackService(cs).GenerateCombinedAsync(businessDate, "verification").ConfigureAwait(false);
        timings["packMs"] = clock.ElapsedMilliseconds;
        // Explicit copy (not a 'with' expression): GeneratedUtc is fixed so no clock text reaches the files.
        var fixedPack = new ReportPackDocument(pack.Title, pack.DateFrom, pack.DateTo, pack.OverallStatus, pack.RuleVersion, pack.Message,
            DateTimeOffset.Parse("2000-01-01T00:00:00Z"), pack.Tables);
        File.WriteAllText(Path.Combine(output, "pack.json"), ReportPackArchiveCodec.Serialize(fixedPack), new UTF8Encoding(false));

        clock.Restart();
        new OpenXmlReportPackExporter().Export(Path.Combine(output, "pack.xlsx"), fixedPack);
        timings["excelMs"] = clock.ElapsedMilliseconds;
        clock.Restart();
        new SimplePdfReportPackExporter().Export(Path.Combine(output, "pack.pdf"), fixedPack);
        timings["pdfMs"] = clock.ElapsedMilliseconds;

        File.WriteAllText(Path.Combine(output, "timings.json"), JsonSerializer.Serialize(timings), new UTF8Encoding(false));
        Console.WriteLine("PROBE-OK date=" + businessDate.ToString("yyyy-MM-dd") + " tables=" + fixedPack.Tables.Count);
        return 0;
    }
}
'@

function New-EtpProbeHost([string]$ReferenceFolder, [string]$TargetFolder) {
    # Compiled once against the UNPROTECTED folder's assemblies (public API only); run against either build.
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Stop-EtpRefused 'dotnet (the .NET 10 SDK) is not on PATH; the probe host cannot be compiled.' }
    [void](New-Item -ItemType Directory -Path $TargetFolder -Force)
    $referenceNames = @($Script:EtpAssemblies) + @('Microsoft.Data.SqlClient', 'DocumentFormat.OpenXml', 'DocumentFormat.OpenXml.Framework')
    $extras = @(Get-ChildItem -LiteralPath $ReferenceFolder -Filter '*.dll' -File | Where-Object { $_.BaseName -like 'PdfSharp*' -or $_.BaseName -like 'MigraDoc*' } | ForEach-Object { $_.BaseName })
    $items = @()
    foreach ($name in ($referenceNames + $extras)) {
        $path = Join-Path $ReferenceFolder ($name + '.dll')
        if (Test-Path -LiteralPath $path) { $items += "    <Reference Include=`"$name`"><HintPath>$path</HintPath><Private>false</Private></Reference>" }
    }
    $project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <EnableWindowsTargeting>true</EnableWindowsTargeting>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>EtpProbeHost</AssemblyName>
    <SatelliteResourceLanguages>en</SatelliteResourceLanguages>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <ItemGroup>
$($items -join "`r`n")
  </ItemGroup>
</Project>
"@
    $sourceFolder = Join-Path $TargetFolder 'src'
    [void](New-Item -ItemType Directory -Path $sourceFolder -Force)
    $utf8 = New-Object Text.UTF8Encoding($false)
    [IO.File]::WriteAllText((Join-Path $sourceFolder 'EtpProbeHost.csproj'), $project, $utf8)
    [IO.File]::WriteAllText((Join-Path $sourceFolder 'Program.cs'), $Script:ProbeProgram, $utf8)
    # The host lives under %TEMP%, outside the repository, so Directory.Build.props does not apply to it.
    $binary = Join-Path $TargetFolder 'bin'
    $arguments = 'build "{0}" -c Release -o "{1}" -nodeReuse:false -m:1 --nologo' -f (Join-Path $sourceFolder 'EtpProbeHost.csproj'), $binary
    $build = Start-EtpProcess 'dotnet' $arguments @{ DOTNET_CLI_TELEMETRY_OPTOUT = '1'; DOTNET_NOLOGO = '1' } 600 $sourceFolder
    if ($build.ExitCode -ne 0) { throw ("Probe host build failed (exit {0}).`r`n{1}`r`n{2}" -f $build.ExitCode, $build.Stdout, $build.Stderr) }
    return (Join-Path $binary 'EtpProbeHost.dll')
}

# ------------------------------------------------------------------ file readers / comparers
function Read-EtpZipXml($Archive, [string]$Name) {
    $entry = $Archive.GetEntry($Name)
    if ($null -eq $entry) { return $null }
    $reader = New-Object IO.StreamReader($entry.Open(), [Text.Encoding]::UTF8)
    try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $xml = New-Object Xml.XmlDocument
    $xml.LoadXml($text)
    return $xml
}

# Every worksheet cell as "SheetName!A1" -> text (shared strings resolved, formulas kept). docProps are not read (excluded list).
function Get-EtpXlsxCells([string]$Path) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $cells = [ordered]@{}
    $zip = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $shared = @()
        $sst = Read-EtpZipXml $zip 'xl/sharedStrings.xml'
        if ($sst) { foreach ($si in $sst.DocumentElement.ChildNodes) { $shared += $si.InnerText } }
        $workbook = Read-EtpZipXml $zip 'xl/workbook.xml'
        $rels = Read-EtpZipXml $zip 'xl/_rels/workbook.xml.rels'
        $relTargets = @{}
        if ($rels) { foreach ($r in $rels.DocumentElement.ChildNodes) { $relTargets[$r.GetAttribute('Id')] = $r.GetAttribute('Target') } }
        $sheetIndex = 0
        foreach ($sheet in $workbook.DocumentElement.GetElementsByTagName('sheet')) {
            $sheetIndex++
            $rid = $sheet.GetAttribute('id', 'http://schemas.openxmlformats.org/officeDocument/2006/relationships')
            $target = [string]$relTargets[$rid]
            $part = if ($target.StartsWith('/')) { $target.TrimStart('/') } else { 'xl/' + $target }
            $sheetName = $sheet.GetAttribute('name')
            $cells["#sheet$sheetIndex"] = $sheetName
            $doc = Read-EtpZipXml $zip $part
            if ($null -eq $doc) { $cells["$sheetName!MISSING-PART"] = $part; continue }
            foreach ($c in $doc.DocumentElement.GetElementsByTagName('c')) {
                $type = $c.GetAttribute('t'); $value = ''
                $v = $null; foreach ($node in $c.GetElementsByTagName('v')) { $v = $node; break }
                if ($type -eq 's' -and $v) { $value = $shared[[int]$v.InnerText] }
                elseif ($type -eq 'inlineStr') { $value = $c.InnerText }
                elseif ($v) { $value = $v.InnerText }
                $formula = $null; foreach ($node in $c.GetElementsByTagName('f')) { $formula = $node; break }
                if ($formula) { $value = '=' + $formula.InnerText + ' -> ' + $value }
                if ($value -ne '') { $cells[($sheetName + '!' + $c.GetAttribute('r'))] = $value }
            }
        }
    } finally { $zip.Dispose() }
    return $cells
}

function ConvertFrom-EtpFlate([byte[]]$Bytes) {
    # PDF Flate streams carry a 2-byte zlib header; DeflateStream wants the raw deflate data after it.
    if ($Bytes.Length -le 2 -or $Bytes[0] -ne 0x78) { return $Bytes }
    try {
        $source = New-Object IO.MemoryStream($Bytes, 2, ($Bytes.Length - 2))
        $inflate = New-Object IO.Compression.DeflateStream($source, [IO.Compression.CompressionMode]::Decompress)
        $out = New-Object IO.MemoryStream
        $inflate.CopyTo($out)
        return $out.ToArray()
    } catch { return $Bytes }
}

# Pages, number of streams and a hash of the PDF with metadata removed and every stream replaced by the hash of its decoded bytes.
function Get-EtpPdfFingerprint([string]$Path) {
    $latin1 = [Text.Encoding]::GetEncoding(28591)
    $text = $latin1.GetString([IO.File]::ReadAllBytes($Path))
    $sha = [Security.Cryptography.SHA256]::Create()
    $count = 0
    $normalised = New-Object System.Text.StringBuilder
    $position = 0
    foreach ($m in [regex]::Matches($text, '(?s)stream\r?\n(.*?)\r?\nendstream')) {
        [void]$normalised.Append($text.Substring($position, $m.Groups[1].Index - $position))
        $decoded = ConvertFrom-EtpFlate ($latin1.GetBytes($m.Groups[1].Value))
        [void]$normalised.Append('[' + [BitConverter]::ToString($sha.ComputeHash($decoded)).Replace('-', '') + ']')
        $position = $m.Groups[1].Index + $m.Groups[1].Length
        $count++
    }
    [void]$normalised.Append($text.Substring($position))
    $body = $normalised.ToString()
    $cut = $body.LastIndexOf('xref')
    if ($cut -gt 0) { $body = $body.Substring(0, $cut) }
    $body = [regex]::Replace($body, '/CreationDate\s*\([^)]*\)', '/CreationDate()')
    $body = [regex]::Replace($body, '/ModDate\s*\([^)]*\)', '/ModDate()')
    $body = [regex]::Replace($body, '/ID\s*\[[^\]]*\]', '/ID[]')
    $pages = ([regex]::Matches($body, '/Type\s*/Page(?![A-Za-z])')).Count
    return [pscustomobject]@{ Pages = $pages; Streams = $count; Hash = [BitConverter]::ToString($sha.ComputeHash($latin1.GetBytes($body))).Replace('-', '') }
}

# Differences between two ordered maps as strings; an empty array means identical. Every comparison below goes through this.
function Compare-EtpMaps($Left, $Right, [string]$What, [string]$LeftName = 'unprotected', [string]$RightName = 'protected') {
    $differences = New-Object System.Collections.ArrayList
    $keys = New-Object System.Collections.Generic.List[string]
    foreach ($k in $Left.Keys) { if (-not $keys.Contains([string]$k)) { $keys.Add([string]$k) } }
    foreach ($k in $Right.Keys) { if (-not $keys.Contains([string]$k)) { $keys.Add([string]$k) } }
    foreach ($key in $keys) {
        $inLeft = $Left.Contains($key); $inRight = $Right.Contains($key)
        if ($inLeft -and -not $inRight) { [void]$differences.Add("$What [$key]: only in $LeftName") }
        elseif ($inRight -and -not $inLeft) { [void]$differences.Add("$What [$key]: only in $RightName") }
        elseif ([string]$Left[$key] -cne [string]$Right[$key]) { [void]$differences.Add(("{0} [{1}]: {2} = '{3}' but {4} = '{5}'" -f $What, $key, $LeftName, $Left[$key], $RightName, $Right[$key])) }
    }
    return @($differences)
}

function Get-EtpFileHashOrNull([string]$Path) { if (Test-Path -LiteralPath $Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash } else { return $null } }

# One build, end to end. Returns everything the comparison needs, or throws with the reason (the caller records INCOMPLETE).
function Invoke-EtpBuildProbe([string]$Label, [string]$BuildFolder, [string]$DatabaseName, [string]$Instance, [string]$HostDll, [string]$Fixture, [string]$WorkFolder, [string]$BusinessDate) {
    $exe = Join-Path $BuildFolder 'Etp.Reporting.Desktop.exe'
    $cs = Get-EtpConnectionStringArgument $Instance $DatabaseName
    $diagnostics = Join-Path $WorkFolder 'diagnostics'
    [void](New-Item -ItemType Directory -Path $diagnostics -Force)
    $childEnvironment = @{ ETP_DIAGNOSTICS_DIRECTORY = $diagnostics }

    $init = Start-EtpProcess $exe ('--initialize-database --connection-string "{0}"' -f $cs) $childEnvironment 600 $BuildFolder
    if ($init.ExitCode -ne 0) { throw "$Label --initialize-database exited $($init.ExitCode). stderr: $($init.Stderr)" }
    $plan = Start-EtpProcess $exe ('--plan-database-changes --connection-string "{0}"' -f $cs) $childEnvironment 300 $BuildFolder
    if ($plan.ExitCode -ne 0) { throw "$Label --plan-database-changes exited $($plan.ExitCode). stderr: $($plan.Stderr)" }
    $planJson = $plan.Stdout.Trim()
    $parsed = $planJson | ConvertFrom-Json
    if (@($parsed.pendingMigrations).Count -ne 0 -or @($parsed.pendingObjects).Count -ne 0) { throw "$Label database still has pending changes after initialize: $planJson" }

    $out = Join-Path $WorkFolder 'probe'
    $arguments = '"{0}" --build-dir "{1}" --connection-string "{2}" --fixture "{3}" --out "{4}"' -f $HostDll, $BuildFolder, $cs, $Fixture, $out
    if ($BusinessDate) { $arguments += ' --date ' + $BusinessDate }
    $probe = Start-EtpProcess 'dotnet' $arguments @{ DOTNET_CLI_TELEMETRY_OPTOUT = '1' } 3600 $BuildFolder
    if ($probe.ExitCode -ne 0) { throw "$Label probe host exited $($probe.ExitCode). stdout: $($probe.Stdout) stderr: $($probe.Stderr)" }

    $health = Get-EtpHealthFacts $Instance $DatabaseName $planJson
    $checksums = Get-EtpTableChecksums $Instance $DatabaseName
    $import = Get-Content -LiteralPath (Join-Path $out 'import.json') -Raw | ConvertFrom-Json
    $importMap = [ordered]@{}
    $n = 0
    foreach ($line in @($import.files)) { $n++; $importMap[('{0:D3}' -f $n)] = [string]$line }
    $importMap['failed-files'] = [string]$import.failed
    $timings = Get-Content -LiteralPath (Join-Path $out 'timings.json') -Raw | ConvertFrom-Json
    return [pscustomobject]@{
        Label = $Label; Database = $DatabaseName; Checksums = $checksums; Health = $health; Import = $importMap
        Cells = (Get-EtpXlsxCells (Join-Path $out 'pack.xlsx')); Pdf = (Get-EtpPdfFingerprint (Join-Path $out 'pack.pdf'))
        PackJsonHash = (Get-EtpFileHashOrNull (Join-Path $out 'pack.json')); Timings = $timings; ProbeOutput = $probe.Stdout.Trim()
    }
}

function Compare-EtpProbeResults($Unprotected, $Protected) {
    $d = New-Object System.Collections.ArrayList
    foreach ($x in (Compare-EtpMaps $Unprotected.Checksums $Protected.Checksums 'fact-table checksum')) { [void]$d.Add($x) }
    foreach ($x in (Compare-EtpMaps $Unprotected.Health $Protected.Health 'health')) { [void]$d.Add($x) }
    foreach ($x in (Compare-EtpMaps $Unprotected.Import $Protected.Import 'import result')) { [void]$d.Add($x) }
    foreach ($x in (Compare-EtpMaps $Unprotected.Cells $Protected.Cells 'DSR Excel cell')) { [void]$d.Add($x) }
    if ($Unprotected.PackJsonHash -ne $Protected.PackJsonHash) { [void]$d.Add("report pack data (pack.json): unprotected $($Unprotected.PackJsonHash) but protected $($Protected.PackJsonHash)") }
    if ($Unprotected.Pdf.Pages -ne $Protected.Pdf.Pages) { [void]$d.Add("PDF page count: $($Unprotected.Pdf.Pages) vs $($Protected.Pdf.Pages)") }
    if ($Unprotected.Pdf.Hash -ne $Protected.Pdf.Hash) { [void]$d.Add("PDF normalised content: unprotected $($Unprotected.Pdf.Hash) but protected $($Protected.Pdf.Hash)") }
    return @($d)
}

function Write-EtpVerificationReport([string]$Path, $Unprotected, $Protected, $Differences, [string]$Status, [string]$Sha) {
    $lines = New-Object System.Collections.ArrayList
    [void]$lines.Add('# Protected build verification')
    [void]$lines.Add('')
    [void]$lines.Add("Result: **$Status**. Run $((Get-Date).ToString('yyyy-MM-dd HH:mm')) on $env:COMPUTERNAME. Source: $Sha")
    [void]$lines.Add('')
    [void]$lines.Add('## Differences')
    if (@($Differences).Count -eq 0) { [void]$lines.Add('None.') } else { foreach ($x in $Differences) { [void]$lines.Add("- $x") } }
    if ($Unprotected -and $Protected) {
        [void]$lines.Add(''); [void]$lines.Add('## Compared')
        [void]$lines.Add(('- Fact tables: {0} (unprotected) / {1} (protected); Excel cells: {2} / {3}; PDF pages: {4} / {5}, decoded streams: {6} / {7}' -f $Unprotected.Checksums.Count, $Protected.Checksums.Count, $Unprotected.Cells.Count, $Protected.Cells.Count, $Unprotected.Pdf.Pages, $Protected.Pdf.Pages, $Unprotected.Pdf.Streams, $Protected.Pdf.Streams))
        [void]$lines.Add('- Import: ' + $Unprotected.Import.Count + ' result lines; DBCC CHECKDB clean; no pending migrations or objects in either database.')
    }
    [void]$lines.Add(''); [void]$lines.Add('## Excluded as nondeterministic (explicit list)')
    foreach ($k in $Script:ExcludedNondeterministic.Keys) { [void]$lines.Add("- **$k**: $($Script:ExcludedNondeterministic[$k])") }
    [void]$lines.Add(''); [void]$lines.Add('## Not driven by this script (see docs/security/VERIFICATION.md)')
    [void]$lines.Add('- `--automation-once` (reads the machine-wide operations.json, which names the live database), the in-app HealthCheckRunner, the WPF UI and the app''s own export dialogs.')
    [IO.File]::WriteAllText($Path, ($lines -join "`r`n"), (New-Object Text.UTF8Encoding($false)))
}

# ------------------------------------------------------------------ main
if ($LoadFunctionsOnly) { return }

$exitCode = 2
$created = New-Object System.Collections.ArrayList
$scratch = $null
try {
    if ([string]::IsNullOrWhiteSpace($Protected) -or [string]::IsNullOrWhiteSpace($Unprotected) -or [string]::IsNullOrWhiteSpace($FixtureFolder)) {
        Stop-EtpRefused 'Provide -Protected, -Unprotected, -FixtureFolder and -Database (see Get-Help).'
    }
    Assert-EtpScratchDatabase $Database
    Assert-EtpLocalServer $Server
    if (-not (Test-EtpElevated)) { Stop-EtpRefused 'Run this elevated (it creates and drops the EtpAccept_ scratch databases). Sagar runs it; it is not part of the gate.' }
    Assert-EtpBuildFolder $Protected 'Protected'; Assert-EtpBuildFolder $Unprotected 'Unprotected'
    $Protected = (Resolve-Path -LiteralPath $Protected).Path; $Unprotected = (Resolve-Path -LiteralPath $Unprotected).Path
    if ($Protected -ieq $Unprotected) { Stop-EtpRefused '-Protected and -Unprotected are the same folder.' }
    if (-not (Test-Path -LiteralPath $FixtureFolder -PathType Container)) { Stop-EtpRefused "Fixture folder not found: $FixtureFolder" }
    $FixtureFolder = (Resolve-Path -LiteralPath $FixtureFolder).Path
    if (@(Get-ChildItem -LiteralPath $FixtureFolder -Filter '*.xlsx' -File -Recurse).Count -eq 0) { Stop-EtpRefused 'The fixture folder has no .xlsx files.' }
    if ($Date -and $Date -notmatch '^\d{4}-\d{2}-\d{2}$') { Stop-EtpRefused '-Date must be yyyy-MM-dd.' }
    [void](Assert-EtpIsReallyProtected $Protected $Unprotected)

    $scratch = Get-EtpScratchNames $Database
    foreach ($n in $scratch.Unprotected, $scratch.Protected) {
        Assert-EtpScratchDatabase $n
        if (Test-EtpDatabaseExists $Server $n) { Stop-EtpRefused "Database $n already exists. This script drops only databases it created itself; pick another -Database base name or drop it by hand." }
    }
    if (-not $OutputFolder) { $OutputFolder = Join-Path ([IO.Path]::GetTempPath()) ('EtpProtectedBuild-' + (Get-Date).ToString('yyyyMMdd-HHmmss')) }
    [void](New-Item -ItemType Directory -Path $OutputFolder -Force)
    $sha = '(no SHA256SUMS.txt)'
    $sums = Join-Path $Protected 'SHA256SUMS.txt'
    if (Test-Path -LiteralPath $sums) { $sha = (Get-Content -LiteralPath $sums) -join ' ' }

    $exitCode = 3
    $hostDll = New-EtpProbeHost $Unprotected (Join-Path $OutputFolder 'probe-host')
    [void]$created.Add($scratch.Unprotected); [void]$created.Add($scratch.Protected)   # absent before; dropped in finally if they exist
    $u = Invoke-EtpBuildProbe 'unprotected' $Unprotected $scratch.Unprotected $Server $hostDll $FixtureFolder (Join-Path $OutputFolder 'unprotected') $Date
    $p = Invoke-EtpBuildProbe 'protected' $Protected $scratch.Protected $Server $hostDll $FixtureFolder (Join-Path $OutputFolder 'protected') $Date
    $differences = @(Compare-EtpProbeResults $u $p)
    $status = if ($differences.Count -eq 0) { 'IDENTICAL' } else { 'DIFFERENT' }
    $reportPath = Join-Path $OutputFolder 'protected-build-report.md'
    Write-EtpVerificationReport $reportPath $u $p $differences $status $sha
    foreach ($x in $differences) { Write-Host "DIFF  $x" }
    Write-Host "Report: $reportPath"
    if ($differences.Count -eq 0) { Write-Host 'IDENTICAL: the protected build produced exactly the same results.'; $exitCode = 0 } else { $exitCode = 1 }
}
catch {
    $message = $_.Exception.Message
    if ($message -like 'ETP-REFUSED:*') { Write-Host ('REFUSED: ' + $message.Substring('ETP-REFUSED:'.Length).Trim()); $exitCode = 2 }
    else {
        Write-Host "INCOMPLETE: $message"
        if ($OutputFolder -and (Test-Path -LiteralPath $OutputFolder)) {
            try { Write-EtpVerificationReport (Join-Path $OutputFolder 'protected-build-report.md') $null $null @("INCOMPLETE: $message") 'INCOMPLETE' '' } catch { }
        }
        $exitCode = 3
    }
}
finally {
    if (-not $KeepDatabases) {
        foreach ($n in $created) { try { Remove-EtpScratchDatabase $Server $n } catch { Write-Host "WARNING: could not drop $n : $($_.Exception.Message)" } }
    }
}
exit $exitCode
