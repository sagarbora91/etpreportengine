#Requires -Version 7.5
# JsonSerializerOptions.NewLine below needs .NET 9, so PowerShell 7.5 or later.
<#
.SYNOPSIS
    Proposes the column roles and document identity of the Retail and Service families in EtpReportFamilies.json.

.DESCRIPTION
    Import specification sections 7.1, 7.3 and 7.4 (phase P2). For every column of R001-R031 and SOR_AGEING the
    first matching rule decides the role:

      1. Reviewed     a decision recorded below after the column-by-column review (includes the spec 7.3 table)
      2. Key          store_code, the DocumentKey fields, a Date family's business date, a snapshot date column
      3. Ignored      the canonical field contains "timestamp" (today's content_key already leaves these out)
      4. Label        ETP's own year labels: invoice_year, referenceyear
      5. Descriptive  store, customer, loyalty, GST-number and card/OTP columns (spec 7.4 patterns)
      6. Attribute    item master data and references to other documents (Owner decision OD-3)
      7. Fact         the default

    Identity follows spec 7.3 for the seven core families and 7.4 for the others. A family's RulesetVersion is kept
    as found (1 when absent): raise it by hand in the same change whenever a role or the identity of that family
    changes, because it marks the family's ledger stale (spec 7.1, 13).

    Without a switch the script prints the proposal and every difference from the catalogue. -Check exits 1 when
    they differ. -Write rewrites the catalogue in place, unchanged apart from the proposed fields.
    EtpReportFamilyCatalogueTests pins the result and runs -Check, so the catalogue and this script cannot drift apart:
    change a decision here, run -Write, then re-pin the tests.

    Service Centre families (S001-S040, BusinessUnit Service; Service interim S-2, decision 15, 3 Oct 2026) follow the
    review's section 5 rules. The first matching rule decides:

      1. Reviewed     a per-family decision recorded in $serviceReviewed
      2. Key          store_code (the exporting centre, AW330); no other key in the interim
      3. Ignored      the canonical field contains "timestamp"
      4. Descriptive  the Retail patterns, plus *customer*, cust_*, *landline*, staff and dealer names, customer
                      account and GST numbers, and store-description columns (centre name, type, channel, region)
      5. Attribute    master data, references (dealer, courier), endpoint locations, job status, free-text remarks, and every
                      job-progress date (a Date column other than the family's read-rule date and the booking date)
      6. Fact         the default, which covers every money column

    Every Service family is a dated snapshot in the interim (PrimaryDateHeader null): Scope Snapshot, SnapshotDate
    Block, RowRule Multiset (SnapshotItems for S006), no DocumentKey or RowKey, LatestReadingWins, Route Landing. The
    P8 targets are in docs/service-centre/P8-IDENTITY-TARGETS.md. S001 is Derived; S027 and S028 carry the raw header
    and list the consolidation's SourcePeriodFrom, SourcePeriodTo and SourceFile as ConsolidationColumns. Headers,
    canonical names, types and RawNamePatterns are not proposed here: they are frozen to
    scripts/service-centre/families.spec.json and the raw export names. Families of another business unit, or codes
    outside R001-R031, SOR_AGEING and S001-S040, are left untouched.
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
    # As in R030, LOCATION sits with the receiving store's region, state and city and describes that store;
    # the sending side is the FROM_* columns.
    R021 = @{ location = 'Descriptive' }
    # Spec 7.4: credit-note status. ISSUETO names whom the note was issued to, like a customer column.
    R012 = @{ redeemed_date = 'Attribute'; redeemed_by = 'Attribute'; issued_cnno = 'Attribute'
              issued_cnamt = 'Attribute'; issueto = 'Descriptive' }
    # The purchase invoice an issue, receipt or SOR sale refers to.
    R004 = @{ purinvno = 'Attribute'; purinvdate = 'Attribute' }
    R007 = @{ purinvno = 'Attribute'; purinvdate = 'Attribute' }
    R028 = @{ purinvno = 'Attribute'; purinvdate = 'Attribute' }
    # The purchase invoice a goods receipt came in on. Not part of the RowKey (itemnumber, gr_number).
    SOR_AGEING = @{ purchaseinvno = 'Attribute'; purchaseinvdt = 'Attribute' }
    # Payment instruments. Credit card numbers and OTPs are Descriptive by pattern (Test-Descriptive). The gift card
    # number, the approval number of a card, gift-card or loyalty payment and R002's SIGNET_NO identify what was
    # tendered or booked rather than who, so they stay facts; recorded here so the review is visible.
    R020 = @{ approvalnumber = 'Fact' }
    R016 = @{ approval_number = 'Fact' }
    R017 = @{ giftcardno = 'Fact'; approvalnumber = 'Fact' }
    R002 = @{ signet_no = 'Fact' }
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
    ($field -like '*phone*' -and $field -notlike '*phonepe*') -or ($field -like '*mobile*') -or
    ($field -like '*contact*') -or ($field -like 'ulp*') -or ($field -like '*gstin*') -or ($field -like '*gstn*') -or
    ($field -like 'encircle*') -or ($field -like '*address*') -or ($field -like '*email*') -or ($field -eq 'cro_name') -or
    # A customer's (masked) card number and a one-time password: ETP may re-mask them, and they are not figures.
    ($field -like '*creditcardno*') -or ($field -like 'otp_*')
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

# --- Service Centre families (review section 5; SERVICE-INTERIM-DESIGN.md sections 3 and 4) ---

# The consolidation adds these block-metadata columns after the raw header of S027 and S028 (review M2).
$serviceConsolidationColumns = @{
    S027 = @('SourcePeriodFrom', 'SourcePeriodTo', 'SourceFile')
    S028 = @('SourcePeriodFrom', 'SourcePeriodTo', 'SourceFile')
}

# The date a DateLog family's rows are read by (design section 4, ServiceInterimFamilies ReadRule). It stays a Fact,
# so a re-dated row is a change; the other dates of these logs are progress dates.
$serviceDateLogColumn = @{
    S003 = 'trans_date'; S004 = 'billingdate'; S007 = 'grn_date'; S008 = 'grn_date'; S013 = 'stm_date'
    S019 = 'repairdate'; S022 = 'invoice_date'; S023 = 'transdate'; S024 = 'transdate'; S025 = 'transdate'
    S026 = 'transdate'; S029 = 'repair_date'; S039 = 'transaction_date'; S040 = 'transaction_date'
}

# The date a job was booked never moves as the job progresses, so it is a Fact in every family that has it.
$serviceBookingDates = @('jodate', 'joborder_date', 'created_date', 'bookingdate', 'booking_date')

# Per-family decisions after the column review.
$serviceReviewed = @{
    S003 = @{ name = 'Descriptive' }   # the billed party's name, beside cust_name
    S019 = @{ name = 'Descriptive' }   # the customer of the repeat return
    S013 = @{ name = 'Attribute' }     # goods in transit: the item's name, beside item_id
}

# Store-description columns of the exporting centre (Retail's store_name, region and city counterparts).
$serviceStoreColumns = @('scname', 'sc_name', 'servicecentername', 'storenumber', 'store_channel', 'storechannel',
    'storestatus', 'sc_type', 'territory', 'area', 'regionname', 'franchisename', 'franchiseename')

# People other than the customer (technicians, staff, dealers) and the customer's account and GST identifiers.
$servicePersonColumns = @('full_name', 'technician_name', 'mechanic_name', 'mechanicresponsible', 'repairedby', 'raisedby',
    'jo_booked_by', 'jo_delivered_by', 'd2ddealername', 'identification_number', 'accountnum', 'account_number',
    'gst_number', 'custname')

# Master data of the watch, spare or product, beyond the Retail list.
$serviceMasterData = @('brandcode', 'brandid', 'brand_id', 'clusterid', 'cluster_id', 'vartype', 'vartype1', 'vartype2',
    'var_type', 'sparegroup', 'sparename', 'description', 'productdescription', 'product_description', 'producttype',
    'product_type', 'product_type_id', 'products', 'watchcategory', 'watch_category', 'helios_product_description', 'grade',
    'channeltype', 'channel_type', 'label')

# Other places a job or a document points to (endpoints, never the exporting centre).
$serviceEndpoints = @('location', 'place', 'tolocation', 'to_location', 'from_location', 'purchase_location',
    'repair_location', 'pendingstore', 'srnstorecode', 'tostorecode', 'srnrepairstore', 'fromstore', 'tostore', 'from_store',
    'to_store', 'brandstore', 'invent_location_id_from', 'invent_location_id_to')

# The dealer a job came through, and the courier dispatch filled in later (as R005's dispatch columns).
$serviceReferences = @('sapcode', 'sap_code', 'storesapcode', 'dealersapcode', 'dealerchannel', 'dealercafnumber',
    'dealercafdate', 'dealer_booking_date', 'd2duniquenumber', 'couriername', 'courierdetails', 'docketno', 'docketnumber',
    'cancellationdate', 'cancelled_date')

# Free text written as the job progresses.
$serviceRemarks = @('remarks', 'comment', 'reason', 'reasonforpending', 'complaint', 'complaintdetails',
    'complaintdescription', 'complaint_details', 'complaint_description', 'rwr_reason', 'repeatreturnreason', 'faileddueto',
    'cancellationreason', 'empowermentreason', 'empowerment_reason', 'invoice_empowerment_reason', 'radcempowermentreason')

function Test-ServiceDescriptive([string]$field) {
    (Test-Descriptive $field) -or ($serviceStoreColumns -contains $field) -or ($servicePersonColumns -contains $field) -or
    ($field -like '*customer*') -or ($field -like 'cust_*') -or ($field -like '*landline*')
}

function Test-ServiceAttribute([string]$field, [string]$dataType, [string]$code) {
    if ($dataType -eq 'Date') {
        return ($serviceDateLogColumn[$code] -ne $field) -and ($serviceBookingDates -notcontains $field)
    }
    (Test-Attribute $field) -or ($serviceMasterData -contains $field) -or ($serviceEndpoints -contains $field) -or
    ($serviceRemarks -contains $field) -or ($serviceReferences -contains $field) -or ($field -like '*status*') -or
    ($field -eq 'result')
}

function Get-ServiceIdentity($family, [string]$code) {
    if ($null -ne $family['PrimaryDateHeader']) { throw "$code must have no PrimaryDateHeader in the Service interim." }
    $identity = [ordered]@{ Scope = 'Snapshot'; DocumentKey = @(); YearRule = 'None'
        RowRule = $(if ($code -eq 'S006') { 'SnapshotItems' } else { 'Multiset' }); RowKey = @()
        ChangePolicy = 'LatestReadingWins'; SnapshotDate = 'Block'; LegacyNullable = @(); Route = 'Landing'; RulesetVersion = 1 }
    $existing = $family['Identity']
    if ($null -ne $existing -and $null -ne $existing['RulesetVersion']) {
        $identity.RulesetVersion = $existing['RulesetVersion'].GetValue[int]()
    }
    return $identity
}

function Get-ServiceRoles($family, [string]$code) {
    $columns = @($family['Columns'] | ForEach-Object { $_['CanonicalField'].GetValue[string]() })
    $review = if ($serviceReviewed.ContainsKey($code)) { $serviceReviewed[$code] } else { @{} }
    foreach ($field in @($review.Keys) + @($serviceDateLogColumn[$code] | Where-Object { $_ })) {
        if ($columns -notcontains $field) { throw "$code names '$field', which is not one of its columns." }
    }
    $roles = [ordered]@{}
    foreach ($column in $family['Columns']) {
        $field = $column['CanonicalField'].GetValue[string]()
        $dataType = $column['DataType'].GetValue[string]()
        $roles[$field] =
            if ($review.ContainsKey($field)) { $review[$field] }
            elseif ($field -eq 'store_code') { 'Key' }
            elseif ($field -like '*timestamp*') { 'Ignored' }
            elseif (Test-ServiceDescriptive $field) { 'Descriptive' }
            elseif (Test-ServiceAttribute $field $dataType $code) { 'Attribute' }
            else { 'Fact' }
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
    $unit = if ($null -eq $family['BusinessUnit']) { 'Retail' } else { $family['BusinessUnit'].GetValue[string]() }
    if ($unit -eq 'Service' -and $code -match '^S\d{3}$') {
        $identity = Get-ServiceIdentity $family $code
        $roles = Get-ServiceRoles $family $code
        # Assigned, not an if expression, which would unroll an empty array to $null.
        $consolidation = @()
        if ($serviceConsolidationColumns.ContainsKey($code)) { $consolidation = $serviceConsolidationColumns[$code] }
        $proposed = [ordered]@{ BusinessUnit = 'Service'; Derived = ($code -eq 'S001'); ConsolidationColumns = $consolidation
            Identity = $identity }
    }
    elseif ($unit -eq 'Retail' -and $code -match '^(R\d{3}|SOR_AGEING)$') {
        $identity = Get-Identity $family $code
        $roles = Get-Roles $family $code $identity
        $proposed = [ordered]@{ BusinessUnit = 'Retail'; Derived = $false; ConsolidationColumns = @(); Identity = $identity }
    }
    else { continue }

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
