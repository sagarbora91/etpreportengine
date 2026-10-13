<#
.SYNOPSIS
Writes the objects baseline: one database/objects file per view, procedure and function of a scratch database (1.12 A3).

.DESCRIPTION
Read-only. Reads sys.objects, sys.sql_modules (OBJECT_DEFINITION), sys.sql_expression_dependencies and
sys.database_permissions for the views, procedures and functions of the dbo and diag schemas, and writes:
  - <layer>-<schema>.<name>.sql per object. The body is the stored definition with its first header written
    CREATE OR ALTER, then the line "-- >>> GRANTS", then one GRANT/DENY line per state, permission and column for the
    database roles (public and users are left out). Layer: procedures 40; functions that read no view or function 10;
    views that read no view or function 20; anything that reads another view or function 30 + (depth - 1), at most 39.
  - the generated section of EXCLUDED.md: objects that a CHECK or DEFAULT constraint, a computed column, a security policy
    or a schema-bound object uses, objects with no readable definition, and definitions holding a GO line.
It never overwrites an object file that exists (any layer), so lane files stay as written. Those are listed as kept.

Run it ONCE, at integration, against a fresh scratch database migrated by the test fixture with the objects step OFF,
never against live: SchemaObjectBaselineExportTests (IntegrationTests) does exactly that when
ETP_EXPORT_OBJECTS_BASELINE is set to the repository's database\objects folder. Then commit the result as one commit
"objects baseline". Nothing in the database is changed.

.EXAMPLE
powershell -NoProfile -File scripts\Export-EtpSchemaObjects.ps1 -ServerInstance .\ETPTEST -Database EtpPhase0Test_0123456789abcdef0123456789abcdef -OutputDirectory database\objects
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ServerInstance,
    [Parameter(Mandatory)][string]$Database,
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'database\objects'),
    # The scratch databases the fixture creates are EtpPhase0Test_<32 hex>; anything else needs this switch.
    [switch]$AllowNonScratchDatabase
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

if (-not $AllowNonScratchDatabase -and $Database -notmatch '^EtpPhase0Test_[0-9a-f]{32}$') {
    throw "Refused: '$Database' is not a scratch database made by the test fixture (EtpPhase0Test_<32 hex>). The baseline is exported from a fresh scratch database, never from live."
}
if ($Database -notmatch '^[A-Za-z0-9_-]{1,128}$') { throw 'The database name may hold only letters, digits, underscores and hyphens.' }

$builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
$builder['Data Source'] = $ServerInstance
$builder['Initial Catalog'] = $Database
$builder['Integrated Security'] = $true
$builder['Application Name'] = 'ETP objects baseline export (read-only)'
$builder['Encrypt'] = $true
$builder['TrustServerCertificate'] = $true

function Invoke-EtpReadQuery {
    param([Parameter(Mandatory)][System.Data.SqlClient.SqlConnection]$Connection, [Parameter(Mandatory)][string]$Sql)
    $command = $Connection.CreateCommand()
    $command.CommandText = $Sql
    $command.CommandTimeout = 300
    $table = New-Object System.Data.DataTable
    $reader = $command.ExecuteReader()
    try { $table.Load($reader) } finally { $reader.Dispose(); $command.Dispose() }
    return ,$table
}

# The same rule as LiveObjectsSql in SchemaObjects.cs (keep them equal).
$modulesSql = @"
SET NOCOUNT ON;
SELECT o.object_id,s.name AS schema_name,o.name AS object_name,RTRIM(o.type) AS type_code,OBJECT_DEFINITION(o.object_id) AS definition,
 CASE
  WHEN EXISTS(SELECT 1 FROM sys.sql_expression_dependencies d JOIN sys.objects r ON r.object_id=d.referencing_id
              WHERE d.referenced_id=o.object_id AND r.type IN ('C','D')) THEN N'a constraint uses it'
  WHEN EXISTS(SELECT 1 FROM sys.sql_expression_dependencies d JOIN sys.objects r ON r.object_id=d.referencing_id
              WHERE d.referenced_id=o.object_id AND r.type='U') THEN N'a computed column uses it'
  WHEN EXISTS(SELECT 1 FROM sys.sql_expression_dependencies d JOIN sys.objects r ON r.object_id=d.referencing_id
              WHERE d.referenced_id=o.object_id AND r.type='SP') THEN N'a security policy uses it'
  WHEN EXISTS(SELECT 1 FROM sys.security_predicates p
              WHERE p.predicate_definition LIKE N'%' + REPLACE(o.name,N'_',N'[_]') + N'%') THEN N'a security policy uses it'
  WHEN EXISTS(SELECT 1 FROM sys.sql_expression_dependencies d
              WHERE d.referenced_id=o.object_id AND d.referencing_id<>o.object_id AND d.is_schema_bound_reference=1) THEN N'a schema-bound object uses it'
 END AS pinned_by
FROM sys.objects o JOIN sys.schemas s ON s.schema_id=o.schema_id
WHERE o.type IN ('V','P','FN','IF','TF') AND s.name IN (N'dbo',N'diag') AND o.is_ms_shipped=0
ORDER BY s.name,o.name;
"@

$dependenciesSql = @"
SET NOCOUNT ON;
SELECT DISTINCT d.referencing_id,d.referenced_id
FROM sys.sql_expression_dependencies d
JOIN sys.objects a ON a.object_id=d.referencing_id JOIN sys.objects b ON b.object_id=d.referenced_id
WHERE d.referencing_id<>d.referenced_id AND a.type IN ('V','FN','IF','TF') AND b.type IN ('V','FN','IF','TF');
"@

$permissionsSql = @"
SET NOCOUNT ON;
SELECT p.major_id,p.state,p.permission_name,ISNULL(COL_NAME(p.major_id,p.minor_id),N'') AS column_name,g.name AS grantee
FROM sys.database_permissions p JOIN sys.database_principals g ON g.principal_id=p.grantee_principal_id
JOIN sys.objects o ON o.object_id=p.major_id
WHERE p.class=1 AND o.type IN ('V','P','FN','IF','TF') AND g.type='R' AND g.name<>N'public'
ORDER BY p.major_id,p.state,p.permission_name,column_name,g.name;
"@

$connection = New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString
$connection.Open()
try {
    $modules = Invoke-EtpReadQuery -Connection $connection -Sql $modulesSql
    $dependencies = Invoke-EtpReadQuery -Connection $connection -Sql $dependenciesSql
    $permissions = Invoke-EtpReadQuery -Connection $connection -Sql $permissionsSql
}
finally { $connection.Dispose() }

$byId = @{}
foreach ($row in $modules.Rows) { $byId[[int]$row.object_id] = $row }
$uses = @{}
foreach ($row in $dependencies.Rows) {
    $from = [int]$row.referencing_id
    if (-not $uses.ContainsKey($from)) { $uses[$from] = [Collections.Generic.List[int]]::new() }
    $uses[$from].Add([int]$row.referenced_id)
}
$depthCache = @{}
function Get-EtpDepth([int]$Id, [int]$Guard = 0) {
    if ($Guard -gt 50) { throw 'The view and function dependencies are deeper than 50 or circular.' }
    if ($depthCache.ContainsKey($Id)) { return $depthCache[$Id] }
    $depth = 0
    if ($uses.ContainsKey($Id)) {
        foreach ($other in $uses[$Id]) { if ($byId.ContainsKey($other)) { $depth = [Math]::Max($depth, 1 + (Get-EtpDepth $other ($Guard + 1))) } }
    }
    $depthCache[$Id] = $depth
    return $depth
}

function Format-EtpName([string]$Value) { if ($Value -match '^[A-Za-z_][A-Za-z0-9_]*$') { $Value } else { '[' + $Value.Replace(']', ']]') + ']' } }

$header = [regex]::new('^[ \t]*(?<verb>CREATE[ \t]+OR[ \t]+ALTER|CREATE|ALTER)(?<space>\s+)(?<kind>VIEW|PROCEDURE|PROC|FUNCTION)\b', [Text.RegularExpressions.RegexOptions]'Multiline, IgnoreCase, CultureInvariant')
$goLine = [regex]::new('^[ \t]*GO[ \t]*(--[^\r\n]*)?\r?$', [Text.RegularExpressions.RegexOptions]'Multiline, IgnoreCase, CultureInvariant')
$grantsLine = [regex]::new('^--[ \t]*>>>[ \t]*GRANTS[ \t]*\r?$', [Text.RegularExpressions.RegexOptions]'Multiline, CultureInvariant')

if (-not (Test-Path -LiteralPath $OutputDirectory -PathType Container)) { New-Item -ItemType Directory -Path $OutputDirectory | Out-Null }
$existing = @{}
foreach ($file in Get-ChildItem -LiteralPath $OutputDirectory -Filter '*.sql' -File) {
    if ($file.Name -match '^[0-9]{2}-(?<object>[A-Za-z_][A-Za-z0-9_]*\.[A-Za-z_][A-Za-z0-9_]*)\.sql$') { $existing[$Matches.object.ToLowerInvariant()] = $file.Name }
}

$utf8 = New-Object System.Text.UTF8Encoding $false
$written = 0; $kept = [Collections.Generic.List[string]]::new(); $excluded = [Collections.Generic.List[object]]::new()
foreach ($row in $modules.Rows) {
    $schema = [string]$row.schema_name; $name = [string]$row.object_name; $type = [string]$row.type_code
    $objectName = "$schema.$name"
    if ($row.pinned_by -isnot [DBNull]) { $excluded.Add([pscustomobject]@{ Name = $objectName; Why = [string]$row.pinned_by }); continue }
    if ($row.definition -is [DBNull]) { $excluded.Add([pscustomobject]@{ Name = $objectName; Why = 'no readable definition (encrypted or no permission)' }); continue }
    $definition = ([string]$row.definition).Trim()
    if ($goLine.IsMatch($definition) -or $grantsLine.IsMatch($definition)) { $excluded.Add([pscustomobject]@{ Name = $objectName; Why = 'its definition holds a GO or GRANTS line' }); continue }
    if ($name -notmatch '^[A-Za-z_][A-Za-z0-9_]*$' -or $schema -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { $excluded.Add([pscustomobject]@{ Name = $objectName; Why = 'its name is not letters, digits and underscores' }); continue }
    $match = $header.Match($definition)
    if (-not $match.Success) { $excluded.Add([pscustomobject]@{ Name = $objectName; Why = 'no CREATE header found in its definition' }); continue }
    if ($existing.ContainsKey($objectName.ToLowerInvariant())) { $kept.Add($existing[$objectName.ToLowerInvariant()]); continue }

    $layer = if ($type -eq 'P') { 40 } else {
        $depth = Get-EtpDepth ([int]$row.object_id)
        if ($depth -eq 0) { if ($type -eq 'V') { 20 } else { 10 } } else { 30 + [Math]::Min($depth - 1, 9) }
    }
    $body = $definition.Substring(0, $match.Index) + 'CREATE OR ALTER ' + $match.Groups['kind'].Value + $definition.Substring($match.Index + $match.Length)

    $grants = [Collections.Generic.List[string]]::new()
    $rows = @($permissions.Rows | Where-Object { [int]$_.major_id -eq [int]$row.object_id })
    $groups = $rows | Group-Object -Property { "$($_.state)|$($_.permission_name)|$($_.column_name)" }
    foreach ($group in ($groups | Sort-Object Name)) {
        $first = $group.Group[0]
        $grantees = (@($group.Group | ForEach-Object { Format-EtpName ([string]$_.grantee) } | Sort-Object -Unique)) -join ','
        $column = if ([string]$first.column_name) { ' (' + (Format-EtpName ([string]$first.column_name)) + ')' } else { '' }
        $target = "$(Format-EtpName $schema).$(Format-EtpName $name)$column"
        switch ([string]$first.state) {
            'G' { $grants.Add("GRANT $($first.permission_name) ON $target TO $grantees;") }
            'W' { $grants.Add("GRANT $($first.permission_name) ON $target TO $grantees WITH GRANT OPTION;") }
            'D' { $grants.Add("DENY $($first.permission_name) ON $target TO $grantees;") }
        }
    }
    $text = $body.Replace("`r`n", "`n").Replace("`n", "`r`n") + "`r`n-- >>> GRANTS`r`n" + ((@($grants) | ForEach-Object { "$_`r`n" }) -join '')
    $path = Join-Path $OutputDirectory ('{0:00}-{1}.{2}.sql' -f $layer, $schema, $name)
    [IO.File]::WriteAllText($path, $text, $utf8)
    $written++
}

$excludedPath = Join-Path $OutputDirectory 'EXCLUDED.md'
$begin = '<!-- BEGIN GENERATED EXCLUSIONS -->'; $end = '<!-- END GENERATED EXCLUSIONS -->'
$section = [Collections.Generic.List[string]]::new()
$section.Add($begin)
$section.Add("Exported from a scratch database on $((Get-Date).ToString('yyyy-MM-dd')).")
$section.Add('')
$section.Add('| Object | Why |')
$section.Add('|---|---|')
foreach ($item in ($excluded | Sort-Object Name)) { $section.Add("| ``$($item.Name)`` | $($item.Why) |") }
$section.Add($end)
$generated = $section -join "`n"
if (Test-Path -LiteralPath $excludedPath -PathType Leaf) {
    $current = [IO.File]::ReadAllText($excludedPath).Replace("`r`n", "`n")
    $start = $current.IndexOf($begin); $stop = $current.IndexOf($end)
    $updated = if ($start -ge 0 -and $stop -gt $start) { $current.Substring(0, $start) + $generated + $current.Substring($stop + $end.Length) } else { $current.TrimEnd() + "`n`n" + $generated + "`n" }
}
else { $updated = "# Objects that stay in the migrations`n`n" + $generated + "`n" }
[IO.File]::WriteAllText($excludedPath, $updated, $utf8)

Write-Output "Objects baseline: $written file(s) written, $($kept.Count) existing file(s) kept, $($excluded.Count) object(s) excluded (EXCLUDED.md)."
foreach ($name in $kept) { Write-Output "Kept as written: $name" }
foreach ($item in ($excluded | Sort-Object Name)) { Write-Output "Excluded: $($item.Name) - $($item.Why)" }
