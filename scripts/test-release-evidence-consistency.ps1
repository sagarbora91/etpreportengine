[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$')]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$SourceCommit,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ReleaseApplicationPath,

    [ValidateNotNullOrEmpty()]
    [string]$InstallerPath,

    [ValidateNotNullOrEmpty()]
    [string]$OfflinePackagePath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ProvenancePath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$CycloneDxPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-EvidenceFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LiteralPath,

        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    if (-not (Test-Path -LiteralPath $LiteralPath -PathType Leaf)) {
        throw "$Label file does not exist: $LiteralPath"
    }

    return Get-Item -LiteralPath $LiteralPath
}

function Read-JsonEvidence {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo]$File,

        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    try {
        $value = Get-Content -LiteralPath $File.FullName -Raw | ConvertFrom-Json
    }
    catch {
        throw "$Label is not valid JSON: $($File.FullName). $($_.Exception.Message)"
    }

    if ($null -eq $value -or $value -is [System.Array]) {
        throw "$Label must contain exactly one JSON object: $($File.FullName)"
    }

    return $value
}

function Get-RequiredProperty {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Object,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) {
        throw "$Context is missing required property '$Name'."
    }

    return $property.Value
}

function Get-RequiredString {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Object,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    $value = Get-RequiredProperty -Object $Object -Name $Name -Context $Context
    if ($value -isnot [string] -or [string]::IsNullOrWhiteSpace($value)) {
        throw "$Context property '$Name' must be a non-empty string."
    }

    return $value
}

function Assert-StringEqual {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Actual,

        [Parameter(Mandatory = $true)]
        [string]$Expected,

        [Parameter(Mandatory = $true)]
        [string]$Label,

        [System.StringComparison]$Comparison = [System.StringComparison]::Ordinal
    )

    if (-not [string]::Equals($Actual, $Expected, $Comparison)) {
        throw "$Label mismatch. Expected '$Expected'; found '$Actual'."
    }
}

function Assert-TrueBoolean {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    if ($Value -isnot [bool] -or -not $Value) {
        throw "$Label must be the JSON boolean true."
    }
}

function Test-EvidencePathMatches {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RecordedPath,

        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo]$ActualFile
    )

    $recorded = $RecordedPath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    if ([System.IO.Path]::IsPathRooted($recorded)) {
        return [string]::Equals(
            [System.IO.Path]::GetFullPath($recorded),
            $ActualFile.FullName,
            [System.StringComparison]::OrdinalIgnoreCase)
    }

    $recorded = $recorded.TrimStart('.', '\', '/')
    if ([string]::IsNullOrWhiteSpace($recorded)) {
        return $false
    }

    $suffix = [System.IO.Path]::DirectorySeparatorChar + $recorded
    return $ActualFile.FullName.EndsWith($suffix, [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-FileIdentity {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo]$File
    )

    return [pscustomobject]@{
        File = $File
        Bytes = $File.Length
        Sha256 = (Get-FileHash -LiteralPath $File.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
    }
}

function Read-ExactBytes {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.Stream]$Stream,

        [Parameter(Mandatory = $true)]
        [byte[]]$Buffer
    )

    $offset = 0
    while ($offset -lt $Buffer.Length) {
        $read = $Stream.Read($Buffer, $offset, $Buffer.Length - $offset)
        if ($read -eq 0) { return $false }
        $offset += $read
    }
    return $true
}

function Get-NormalizedProductVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    $match = [regex]::Match(
        $Value.Trim(),
        '^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:\+[0-9A-Za-z.-]+)?$',
        [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if (-not $match.Success) {
        throw "$Label '$Value' is not a supported product version. Expected major.minor.patch with optional +build metadata."
    }

    return "$($match.Groups['major'].Value).$($match.Groups['minor'].Value).$($match.Groups['patch'].Value)"
}

function Assert-ReleaseApplicationVersion {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo]$File,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedVersion
    )

    try {
        $stream = $File.OpenRead()
        try {
            $dosHeader = [byte[]]::new(64)
            if (-not (Read-ExactBytes -Stream $stream -Buffer $dosHeader) -or $dosHeader[0] -ne 0x4d -or $dosHeader[1] -ne 0x5a) {
                throw 'DOS header is missing or invalid.'
            }

            $peOffset = [BitConverter]::ToInt32($dosHeader, 0x3c)
            if ($peOffset -lt 64 -or $peOffset -gt ($File.Length - 4)) {
                throw 'PE header offset is outside the application file.'
            }

            $stream.Position = $peOffset
            $signature = [byte[]]::new(4)
            if (-not (Read-ExactBytes -Stream $stream -Buffer $signature) -or
                $signature[0] -ne 0x50 -or $signature[1] -ne 0x45 -or
                $signature[2] -ne 0x00 -or $signature[3] -ne 0x00) {
                throw 'PE signature is missing or invalid.'
            }
        }
        finally {
            $stream.Dispose()
        }
    }
    catch {
        throw "Release application is not a valid readable PE file: $($File.FullName). $($_.Exception.Message)"
    }

    try {
        $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($File.FullName)
    }
    catch {
        throw "Release application embedded version information is unreadable: $($File.FullName). $($_.Exception.Message)"
    }

    if ([string]::IsNullOrWhiteSpace($versionInfo.ProductVersion)) {
        throw "Release application embedded ProductVersion is missing: $($File.FullName)."
    }
    if ([string]::IsNullOrWhiteSpace($versionInfo.FileVersion)) {
        throw "Release application embedded FileVersion is missing: $($File.FullName)."
    }

    $normalizedProductVersion = Get-NormalizedProductVersion -Value $versionInfo.ProductVersion -Label 'Release application embedded ProductVersion'
    if (-not [string]::Equals($normalizedProductVersion, $ExpectedVersion, [System.StringComparison]::Ordinal)) {
        throw "Release application embedded ProductVersion mismatch. Expected '$ExpectedVersion'; found '$($versionInfo.ProductVersion)' (normalized '$normalizedProductVersion')."
    }

    $expectedFileVersion = "$ExpectedVersion.0"
    if (-not [string]::Equals($versionInfo.FileVersion.Trim(), $expectedFileVersion, [System.StringComparison]::Ordinal)) {
        throw "Release application embedded FileVersion mismatch. Policy requires '$expectedFileVersion'; found '$($versionInfo.FileVersion)'."
    }
}

function Assert-ArtifactRecord {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Record,

        [Parameter(Mandatory = $true)]
        [object]$ExpectedIdentity,

        [Parameter(Mandatory = $true)]
        [string]$Role
    )

    $context = "Provenance artifact '$Role'"
    $recordedPath = Get-RequiredString -Object $Record -Name 'path' -Context $context
    if (-not (Test-EvidencePathMatches -RecordedPath $recordedPath -ActualFile $ExpectedIdentity.File)) {
        throw "$context path mismatch. Recorded '$recordedPath'; supplied '$($ExpectedIdentity.File.FullName)'."
    }

    $recordedBytes = Get-RequiredProperty -Object $Record -Name 'bytes' -Context $context
    $parsedBytes = 0L
    if (-not [long]::TryParse([string]$recordedBytes, [ref]$parsedBytes) -or $parsedBytes -lt 0) {
        throw "$context property 'bytes' must be a non-negative integer."
    }
    if ($parsedBytes -ne $ExpectedIdentity.Bytes) {
        throw "$context size mismatch. Expected '$($ExpectedIdentity.Bytes)'; found '$parsedBytes'."
    }

    $recordedHash = Get-RequiredString -Object $Record -Name 'sha256' -Context $context
    if ($recordedHash -notmatch '^[0-9a-fA-F]{64}$') {
        throw "$context property 'sha256' must be a 64-character hexadecimal SHA-256 value."
    }
    Assert-StringEqual -Actual $recordedHash -Expected $ExpectedIdentity.Sha256 -Label "$context SHA-256" -Comparison OrdinalIgnoreCase
}

function Get-UniqueCycloneProperty {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Properties,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $matches = @($Properties | Where-Object {
        $propertyName = Get-RequiredString -Object $_ -Name 'name' -Context 'CycloneDX component property'
        [string]::Equals($propertyName, $Name, [System.StringComparison]::Ordinal)
    })

    if ($matches.Count -ne 1) {
        throw "CycloneDX component must contain exactly one '$Name' property; found $($matches.Count)."
    }

    return Get-RequiredString -Object $matches[0] -Name 'value' -Context "CycloneDX component property '$Name'"
}

$applicationFile = Resolve-EvidenceFile -LiteralPath $ReleaseApplicationPath -Label 'Release application'
Assert-ReleaseApplicationVersion -File $applicationFile -ExpectedVersion $Version
$application = Get-FileIdentity -File $applicationFile
$provenanceFile = Resolve-EvidenceFile -LiteralPath $ProvenancePath -Label 'Provenance'
$cycloneDxFile = Resolve-EvidenceFile -LiteralPath $CycloneDxPath -Label 'CycloneDX'

$expectedArtifacts = @{
    'self-contained-application' = $application
}
if ($PSBoundParameters.ContainsKey('InstallerPath')) {
    $expectedArtifacts['bootstrap-installer'] = Get-FileIdentity -File (Resolve-EvidenceFile -LiteralPath $InstallerPath -Label 'Installer')
}
if ($PSBoundParameters.ContainsKey('OfflinePackagePath')) {
    $expectedArtifacts['offline-deployment-package'] = Get-FileIdentity -File (Resolve-EvidenceFile -LiteralPath $OfflinePackagePath -Label 'Offline package')
}

$provenance = Read-JsonEvidence -File $provenanceFile -Label 'Provenance'
Assert-StringEqual -Actual (Get-RequiredString $provenance 'schema' 'Provenance') -Expected 'etp-release-evidence/v1' -Label 'Provenance schema'
Assert-StringEqual -Actual (Get-RequiredString $provenance 'product' 'Provenance') -Expected 'ETP Reporting Engine' -Label 'Provenance product'
Assert-StringEqual -Actual (Get-RequiredString $provenance 'version' 'Provenance') -Expected $Version -Label 'Provenance version'
$runtime = Get-RequiredString -Object $provenance -Name 'runtime' -Context 'Provenance'

$source = Get-RequiredProperty -Object $provenance -Name 'source' -Context 'Provenance'
$provenanceCommit = Get-RequiredString -Object $source -Name 'releaseSourceCommit' -Context 'Provenance source'
Assert-StringEqual -Actual $provenanceCommit -Expected $SourceCommit -Label 'Provenance source commit' -Comparison OrdinalIgnoreCase
Assert-TrueBoolean -Value (Get-RequiredProperty $source 'worktreeCleanAtBuild' 'Provenance source') -Label 'Provenance worktreeCleanAtBuild'
Assert-TrueBoolean -Value (Get-RequiredProperty $source 'exactCommittedSourceIdentityAvailable' 'Provenance source') -Label 'Provenance exactCommittedSourceIdentityAvailable'

$artifactRecords = @(Get-RequiredProperty -Object $provenance -Name 'artifacts' -Context 'Provenance')
if ($artifactRecords.Count -ne $expectedArtifacts.Count) {
    throw "Provenance artifact count mismatch. Expected exactly $($expectedArtifacts.Count); found $($artifactRecords.Count)."
}

$seenRoles = @{}
foreach ($record in $artifactRecords) {
    $role = Get-RequiredString -Object $record -Name 'role' -Context 'Provenance artifact'
    if (-not $expectedArtifacts.ContainsKey($role)) {
        throw "Provenance contains unexpected artifact role '$role'. Supply the matching optional file or remove the extra evidence before validation."
    }
    if ($seenRoles.ContainsKey($role)) {
        throw "Provenance contains duplicate artifact role '$role'."
    }

    Assert-ArtifactRecord -Record $record -ExpectedIdentity $expectedArtifacts[$role] -Role $role
    $seenRoles[$role] = $true
}
foreach ($role in $expectedArtifacts.Keys) {
    if (-not $seenRoles.ContainsKey($role)) {
        throw "Provenance is missing required artifact role '$role'."
    }
}

$sbom = Get-RequiredProperty -Object $provenance -Name 'sbom' -Context 'Provenance'
$recordedSbomPath = Get-RequiredString -Object $sbom -Name 'path' -Context 'Provenance SBOM'
if (-not (Test-EvidencePathMatches -RecordedPath $recordedSbomPath -ActualFile $cycloneDxFile)) {
    throw "Provenance SBOM path mismatch. Recorded '$recordedSbomPath'; supplied '$($cycloneDxFile.FullName)'."
}
Assert-StringEqual -Actual (Get-RequiredString $sbom 'format' 'Provenance SBOM') -Expected 'CycloneDX' -Label 'Provenance SBOM format'

$cycloneDx = Read-JsonEvidence -File $cycloneDxFile -Label 'CycloneDX'
Assert-StringEqual -Actual (Get-RequiredString $cycloneDx 'bomFormat' 'CycloneDX') -Expected 'CycloneDX' -Label 'CycloneDX bomFormat'
$cycloneSpecVersion = Get-RequiredString -Object $cycloneDx -Name 'specVersion' -Context 'CycloneDX'
Assert-StringEqual -Actual (Get-RequiredString $sbom 'specVersion' 'Provenance SBOM') -Expected $cycloneSpecVersion -Label 'CycloneDX specVersion'

$metadata = Get-RequiredProperty -Object $cycloneDx -Name 'metadata' -Context 'CycloneDX'
$component = Get-RequiredProperty -Object $metadata -Name 'component' -Context 'CycloneDX metadata'
Assert-StringEqual -Actual (Get-RequiredString $component 'name' 'CycloneDX metadata component') -Expected 'ETP Reporting Engine' -Label 'CycloneDX component name'
Assert-StringEqual -Actual (Get-RequiredString $component 'version' 'CycloneDX metadata component') -Expected $Version -Label 'CycloneDX component version'
Assert-StringEqual -Actual (Get-RequiredString $component 'bom-ref' 'CycloneDX metadata component') -Expected "ETP Reporting Engine@$Version" -Label 'CycloneDX component bom-ref'

$cycloneProperties = @(Get-RequiredProperty -Object $component -Name 'properties' -Context 'CycloneDX metadata component')
$cycloneCommit = Get-UniqueCycloneProperty -Properties $cycloneProperties -Name 'etp:base-commit'
Assert-StringEqual -Actual $cycloneCommit -Expected $SourceCommit -Label 'CycloneDX base commit' -Comparison OrdinalIgnoreCase
$cycloneClean = Get-UniqueCycloneProperty -Properties $cycloneProperties -Name 'etp:source-worktree-clean'
Assert-StringEqual -Actual $cycloneClean -Expected 'true' -Label 'CycloneDX source-worktree-clean'
$cycloneRuntime = Get-UniqueCycloneProperty -Properties $cycloneProperties -Name 'etp:runtime'
Assert-StringEqual -Actual $cycloneRuntime -Expected $runtime -Label 'CycloneDX runtime'
$cycloneApplicationHash = Get-UniqueCycloneProperty -Properties $cycloneProperties -Name 'etp:artifact-sha256'
Assert-StringEqual -Actual $cycloneApplicationHash -Expected $application.Sha256 -Label 'CycloneDX application SHA-256' -Comparison OrdinalIgnoreCase

$cycloneComponents = @(Get-RequiredProperty -Object $cycloneDx -Name 'components' -Context 'CycloneDX')
$recordedComponentCount = Get-RequiredProperty -Object $sbom -Name 'components' -Context 'Provenance SBOM'
$parsedComponentCount = 0
if (-not [int]::TryParse([string]$recordedComponentCount, [ref]$parsedComponentCount) -or $parsedComponentCount -lt 0) {
    throw "Provenance SBOM property 'components' must be a non-negative integer."
}
if ($parsedComponentCount -ne $cycloneComponents.Count) {
    throw "CycloneDX component count mismatch. Provenance records '$parsedComponentCount'; found '$($cycloneComponents.Count)'."
}

Write-Output "Release evidence is internally consistent for ETP Reporting Engine $Version at source commit $SourceCommit."
