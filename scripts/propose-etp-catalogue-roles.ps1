<#
.SYNOPSIS
    Proposes the column roles and document identity of the Retail families in EtpReportFamilies.json.

.DESCRIPTION
    Import specification sections 7.1, 7.3 and 7.4 (phase P2). For every column of R001-R031 and SOR_AGEING the
    first matching rule decides the role:

      1. Reviewed     a decision recorded below after the column-by-column review (includes the spec 7.3 table)
      2. Key          store_code, the DocumentKey fields, a Date family's business date, a snapshot date column
      3. Ignored      the canonical field contains "timestamp" (today's content_key already leaves these out)
      4. Label        ETP's own year labels: invoice_year, referenceyear
      5. Descriptive  store, customer, loyalty and GST-number columns (spec 7.4 patterns)
      6. Attribute    item master data and references to other documents (Owner decision OD-3)
      7. Fact         the default

    Identity follows spec 7.3 for the seven core families and 7.4 for the others. A family's RulesetVersion is kept
    as found (1 when absent): raise it by hand in the same change whenever a role or the identity of that family
    changes, because it marks the family's ledger stale (spec 7.1, 13).

    Without a switch the script prints the proposal and every difference from the catalogue. -Check exits 1 when
    they differ. -Write rewrites the catalogue in place, unchanged apart from the proposed fields.
    EtpReportFamilyCatalogueTests pins the result. Families of another business unit are left untouched.
#>
[CmdletBinding()]
param(
    [switch]$Write,
    [switch]$Check,
    [string]$CataloguePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $CataloguePath) {
    $CataloguePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'src/Etp.Reporting.Import/Profiles/EtpReportFamilies.json'
}

# Spec 7.3: the core families. Keyed by FamilyCode (R030 is STOCK_LEDGER, R011 is CLOSING_STOCK).
$coreIdentity = @{
    R025 = @{ Scope = 'Document'; DocumentKey = @('invoice_number'); YearRule = 'FinancialYearOfPrimaryDate'
              RowRule = 'Multiset'; ChangePolicy = 'Review'; Route = 'Sales'
              # v0 sales lines may hold NULL gross and tax: sales_lines.source_gross_amount is staged as source_net_amount.
              LegacyNullable = @('source_net_amount', 'source_tax_amount') }
    R022 = @{ Scope = 'Document'; DocumentKey = @('invoice_number'); YearRule = 'FinancialYearOfPrimaryDate'
              RowRule = 'SingleRowPerDocument'; ChangePolicy = 'Review'; Route = 'Revenue' }
    R013 = @{ Scope = 'Document'; DocumentKey = @('invoice_number'); YearRule = 'FinancialYearOfPrimaryDate'
              RowRule = 'Multiset'; ChangePolicy = 'Review'; Route = 'Enrichment' }
    R003 = @{ Scope = 'Document'; DocumentKey = @('invoice_number'); YearRule = 'FinancialYearOfPrimaryDate'
              RowRule = 'Multiset'; ChangePolicy = 'Review'; Route = 'Enrichment' }
    R030 = @{ Scope = 'Document'; DocumentKey = @('document_number'); YearRule = 'FinancialYearOfPrimaryDate'
              RowRule = 'StockUnitChain'; ChangePolicy = 'Review'; Route = 'StockMovement' }
    R011 = @{ Scope = 'Snapshot'; SnapshotDate = 'Column:snapshot_date'; RowRule = 'SnapshotItems'
              RowKey = @('product_code', 'COALESCE(source_uid,batch_number,ean)'); ChangePolicy = 'LatestReadingWins'
              Route = 'StockSnapshot' }
    R010 = @{ Scope = 'Snapshot'; SnapshotDate = 'Block'; RowRule = 'SnapshotItems'
              RowKey = @('itemnumber', 'COALESCE(uid,lotnumber)'); ChangePolicy = 'LatestReadingWins'
              Route = 'StockSnapshot' }
}

# Spec 7.4: undated landing families are snapshots dated by their block. RowKey pairs rows between two readings,
# so a newer reading that drops a row goes to review (SNAPSHOT_SHRINK); it names what identifies a row, never a figure.
$undatedRowKey = @{
    R023       = @('businessruleid', 'versionid', 'strategy_id', 'brand_code', 'cluster', 'itemnumber')
    SOR_AGEING = @('itemnumber', 'gr_number')
}

# The column-by-column review. Each entry overrides the rules below for one family; spec 7.3 entries first.
$reviewed = @{
    R003 = @{ activation_details = 'Attribute'; user_discount_details = 'Attribute' }   # spec 7.3
    R011 = @{ itemdescription = 'Descriptive' }                                          # spec 7.3
    R030 = @{ location = 'Descriptive' }   # spec 7.3: describes the store; FROM/TO LOCATION stay facts
    # Spec 7.4: credit-note status. ISSUETO names whom the note was issued to, like a customer column.
    R012 = @{ redeemed_date = 'Attribute'; redeemed_by = 'Attribute'; issued_cnno = 'Attribute'
              issued_cnamt = 'Attribute'; issueto = 'Descriptive' }
    # The purchase invoice an issue, receipt or SOR sale refers to.
    R004 = @{ purinvno = 'Attribute'; purinvdate = 'Attribute' }
    R007 = @{ purinvno = 'Attribute'; purinvdate = 'Attribute' }
    R028 = @{ purinvno = 'Attribute'; purinvdate = 'Attribute' }
    # Dispatch status, filled in after the issue was made.
    R005 = @{ couriername = 'Attribute'; couriernumber = 'Attribute'; courierdate = 'Attribute'
              airwaybillno = 'Attribute'; dispatchtype = 'Attribute' }
    # GST returns: state names describe the state codes beside them, which stay facts; UOM is item master data.
    R018 = @{ issue_state_name = 'Descriptive'; recipient_state_name = 'Descriptive'; uom = 'Attribute' }
    R019 = @{ issue_state_name = 'Descriptive'; recipient_state_name = 'Descriptive'; uom = 'Attribute' }
    # A scheme's brand and cluster are what it covers, so they stay facts; the names are master data.
    R023 = @{ brand_code = 'Fact'; cluster = 'Fact'; brand_name = 'Attribute'; businessrulename = 'Attribute'
              strategyname = 'Attribute' }
    # PRP: the job order and the follow-on ETP invoice, stock receipt, return and courier documents.
    R026 = @{ jo_no = 'Attribute'; jo_date = 'Attribute' }
    R027 = @{ etp_invoice_no = 'Attribute'; etp_invoice_date = 'Attribute'
              stock_reciept_doc_no = 'Attribute'; stock_reciept_doc_date = 'Attribute'
              stm_purchase_return_doc_no = 'Attribute'; stm_purchase_return_doc_date = 'Attribute'
              stm_courier_doc_no = 'Attribute'; stm_courier_doc_date = 'Attribute' }
    # OMNI sales: the ETP advance order and the shipment.
    R031 = @{ etpadvorder = 'Attribute'; etpadvdate = 'Attribute'; ocshipmentno = 'Attribute' }
}

$labels = @('invoice_year', 'referenceyear')
$storeColumns = @('store_name', 'storename', 'store_type', 'storetype', 'store_sap_code', 'channel', 'region', 'state', 'city')
$masterData = @('brand', 'brandname', 'brand_name', 'brand_code', 'source_brand_code', 'source_brand_name', 'cluster',
    'brand_segment_code', 'gender', 'gender_code', 'hsn_code', 'hsncode', 'ean_category')

function Test-Descriptive([string]$field) {
    ($storeColumns -contains $field) -or
    # The other location of an issue or receipt (TO_/FROM_ region, state, city) describes its location code.
    ($field -match '^(to|from)_(region|state|city)$') -or
    ($field -like 'customer*') -or
    # PHONEPE is a tender, not a phone number.
    ($field -like '*phone*' -and $field -notlike '*phonepe*') -or
    ($field -like '*contact*') -or ($field -like 'ulp*') -or ($field -like '*gstin*') -or ($field -like '*gstn*') -or
    ($field -like 'encircle*') -or ($field -like '*address*') -or ($field -like '*email*') -or ($field -eq 'cro_name')
}

function Test-Attribute([string]$field) {
    ($masterData -contains $field) -or ($field -like '*ref*' -and $field -notlike '*refund*')
}

function Get-RowKeyFields([string]$entry) {
    if ($entry -match '^COALESCE\((.+)\)$') { return @($Matches[1].Split(',') | ForEach-Object { $_.Trim() }) }
    return @($entry)
}

function Get-Identity($family, [string]$code) {
    $identity = [ordered]@{ Scope = 'Date'; DocumentKey = @(); YearRule = 'None'; RowRule = 'Multiset'; RowKey = @()
        ChangePolicy = 'Review'; SnapshotDate = $null; LegacyNullable = @(); Route = 'Landing'; RulesetVersion = 1 }
    if ($coreIdentity.ContainsKey($code)) {
        foreach ($entry in $coreIdentity[$code].GetEnumerator()) { $identity[$entry.Key] = $entry.Value }
    }
    elseif ($null -eq $family['PrimaryDateHeader']) {
        if (-not $undatedRowKey.ContainsKey($code)) { throw "$code has no business date and no reviewed RowKey." }
        $identity.Scope = 'Snapshot'; $identity.SnapshotDate = 'Block'; $identity.RowRule = 'SnapshotItems'
        $identity.RowKey = $undatedRowKey[$code]; $identity.ChangePolicy = 'LatestReadingWins'
    }
    $existing = $family['Identity']
    if ($null -ne $existing -and $null -ne $existing['RulesetVersion']) {
        $identity.RulesetVersion = $existing['RulesetVersion'].GetValue[int]()
    }
    return $identity
}

function Get-Roles($family, [string]$code, $identity) {
    $columns = @($family['Columns'] | ForEach-Object { $_['CanonicalField'].GetValue[string]() })
    # R023 and R027 carry no store code; their store comes from the import scope alone.
    $keys = @(@('store_code') | Where-Object { $columns -contains $_ }) + $identity.DocumentKey
    if ($identity.Scope -eq 'Date') {
        $header = $family['PrimaryDateHeader'].GetValue[string]()
        $keys += @($family['Columns'] | Where-Object { $_['SourceHeader'].GetValue[string]() -eq $header } |
            ForEach-Object { $_['CanonicalField'].GetValue[string]() })
    }
    if ($identity.SnapshotDate -like 'Column:*') { $keys += $identity.SnapshotDate.Substring('Column:'.Length) }
    $review = if ($reviewed.ContainsKey($code)) { $reviewed[$code] } else { @{} }
    foreach ($field in @($review.Keys) + $keys + $identity.LegacyNullable + @($identity.RowKey | ForEach-Object { Get-RowKeyFields $_ })) {
        if ($columns -notcontains $field) { throw "$code names '$field', which is not one of its columns." }
    }

    $roles = [ordered]@{}
    foreach ($field in $columns) {
        $roles[$field] =
            if ($review.ContainsKey($field)) { $review[$field] }
            elseif ($keys -contains $field) { 'Key' }
            elseif ($field -like '*timestamp*') { 'Ignored' }
            elseif ($labels -contains $field) { 'Label' }
            elseif (Test-Descriptive $field) { 'Descriptive' }
            elseif (Test-Attribute $field) { 'Attribute' }
            else { 'Fact' }
    }
    foreach ($field in $keys + @($identity.RowKey | ForEach-Object { Get-RowKeyFields $_ }) + $identity.LegacyNullable) {
        if ($roles[$field] -notin @('Key', 'Fact')) {
            throw "$code would give '$field' the role $($roles[$field]); identity fields must be Key or Fact."
        }
    }
    return $roles
}

# JSON arrays and objects are enumerable, so they are returned with the unary comma to stop PowerShell unrolling them.
function ConvertTo-Node($value) {
    if ($null -eq $value) { return $null }
    if ($value -is [System.Array]) {
        $array = [System.Text.Json.Nodes.JsonArray]::new()
        foreach ($item in $value) { $array.Add((ConvertTo-Node $item)) }
        return , $array
    }
    if ($value -is [System.Collections.IDictionary]) {
        $object = [System.Text.Json.Nodes.JsonObject]::new()
        foreach ($entry in $value.GetEnumerator()) { $object[$entry.Key] = ConvertTo-Node $entry.Value }
        return , $object
    }
    return [System.Text.Json.Nodes.JsonNode]$value
}

$options = [System.Text.Json.JsonSerializerOptions]::new()
$options.WriteIndented = $true
$options.Encoder = [System.Text.Encodings.Web.JavaScriptEncoder]::UnsafeRelaxedJsonEscaping
$options.NewLine = "`n"
function Format-Node($node) { if ($null -eq $node) { 'null' } else { $node.ToJsonString($options) } }

$text = [System.IO.File]::ReadAllText($CataloguePath)
$catalogue = [System.Text.Json.Nodes.JsonNode]::Parse($text)
$differences = [System.Collections.Generic.List[string]]::new()

foreach ($family in $catalogue.AsArray()) {
    $code = $family['FamilyCode'].GetValue[string]()
    $unit = $family['BusinessUnit']
    if ($code -notmatch '^(R\d{3}|SOR_AGEING)$' -or ($null -ne $unit -and $unit.GetValue[string]() -ne 'Retail')) { continue }

    $identity = Get-Identity $family $code
    $roles = Get-Roles $family $code $identity
    $proposed = [ordered]@{ BusinessUnit = 'Retail'; Derived = $false; ConsolidationColumns = @(); Identity = $identity }

    Write-Host "$code $($identity.Scope) key [$((@($roles.Keys | Where-Object { $roles[$_] -eq 'Key' })) -join ', ')]; $($identity.RowRule), $($identity.ChangePolicy), $($identity.Route), ruleset $($identity.RulesetVersion)"
    foreach ($role in 'Attribute', 'Descriptive', 'Label', 'Ignored') {
        $fields = @($roles.Keys | Where-Object { $roles[$_] -eq $role })
        if ($fields.Count -gt 0) { Write-Host "    ${role}: $($fields -join ', ')" }
    }

    foreach ($column in $family['Columns'].AsArray()) {
        $field = $column['CanonicalField'].GetValue[string]()
        $current = if ($null -eq $column['Role']) { '(absent)' } else { $column['Role'].GetValue[string]() }
        if ($current -cne $roles[$field]) { $differences.Add("$code.$field role $current -> $($roles[$field])") }
        $column['Role'] = ConvertTo-Node $roles[$field]
    }
    foreach ($entry in $proposed.GetEnumerator()) {
        $node = ConvertTo-Node $entry.Value
        $before = Format-Node $family[$entry.Key]
        $after = Format-Node $node
        if ($before -cne $after) { $differences.Add("$code.$($entry.Key) $($before -replace '\s+', ' ') -> $($after -replace '\s+', ' ')") }
        $family[$entry.Key] = $node
    }
}

if ($differences.Count -eq 0) { Write-Host 'The catalogue matches the proposal.' }
else {
    Write-Host "$($differences.Count) difference(s) from the catalogue:"
    $differences | ForEach-Object { Write-Host "    $_" }
    Write-Host 'Raise the RulesetVersion of every family whose roles or identity change.'
}

if ($Write -and $differences.Count -gt 0) {
    [System.IO.File]::WriteAllText($CataloguePath, $catalogue.ToJsonString($options) + "`n", [System.Text.UTF8Encoding]::new($false))
    Write-Host "Wrote $CataloguePath"
}
if ($Check -and $differences.Count -gt 0) { exit 1 }
