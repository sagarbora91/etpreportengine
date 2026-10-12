using System.Globalization;
using System.Text;
using System.Xml;
using Etp.Reporting.Application.Accounting;

namespace Etp.Reporting.Infrastructure.SqlServer.Tally;

/// <summary>One store of the Tally company. <paramref name="CostCentre"/> is the name saved in Settings → Tally companies (0049).</summary>
public sealed record TallyMastersStore(string StoreCode, string StoreName, string? CostCentre);

/// <summary>A GST output component and rate seen in the stores' GST detail imports, such as CGST 9.</summary>
public sealed record TallyMastersGstRate(string Component, decimal Rate);

/// <summary>Everything the masters are made from. No customer data: stores, payment modes, GST rates and ledger names only.</summary>
/// <param name="Mappings">Every version of the Tally ledger mappings; only active ones in force on <paramref name="Today"/> are used.</param>
public sealed record TallyMastersSource(
    TallyProfile Profile,
    IReadOnlyList<TallyMastersStore> Stores,
    IReadOnlyList<string> TenderModes,
    IReadOnlyList<TallyMastersGstRate> GstRates,
    IReadOnlyList<TallyLedgerMappingRow> Mappings,
    DateOnly Today);

/// <summary>Decision 49 (3): plans the masters of a brand-new Tally company from ETP's own data. A ledger takes the name of the
/// Owner's approved mapping when there is one, so the masters and the vouchers always agree; otherwise ETP proposes a name and
/// lists the mapping the Owner must approve. Pure: the caller loads the source.</summary>
public static class TallyMastersPlanner
{
    public const string CardUpiGroup = "ETP Card and UPI Receivable";
    public const string CreditNoteGroup = "ETP Credit Notes Redeemed";
    public const string OtherTenderGroup = "ETP Other Tenders";
    public const string DefaultSalesLedger = "Sales - GST Retail";
    public const string DefaultRoundOffLedger = "Round Off";

    private static readonly string[] ComponentOrder = ["CGST", "SGST", "IGST"];

    public static TallyMastersPlan Plan(TallyMastersSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var profile = source.Profile ?? throw new ArgumentException("A Tally company is required.", nameof(source));
        var company = (profile.CompanyName ?? "").Trim();
        if (company.Length == 0) throw new InvalidOperationException("The Tally company has no name.");
        var stores = source.Stores.Where(store => profile.StoreCodes.Contains(store.StoreCode, StringComparer.Ordinal))
            .OrderBy(store => store.StoreCode, StringComparer.Ordinal).ToArray();
        if (stores.Length == 0) throw new InvalidOperationException($"No store is linked to {company}. Add the stores in Settings → Tally companies first.");

        var review = new List<string>();
        var toApprove = new List<ProposedLedgerMapping>();

        // Cost centres (D12, 0049): one per store when the company holds several stores, or when one is already set.
        var centres = new List<TallyMasterCostCentre>();
        var several = stores.Length > 1;
        foreach (var store in stores)
        {
            var saved = string.IsNullOrWhiteSpace(store.CostCentre) ? null : store.CostCentre.Trim();
            if (saved is not null) centres.Add(new(saved, store.StoreCode, false));
            else if (several)
            {
                var proposed = Cut($"{store.StoreName.Trim()} ({store.StoreCode})", 100);
                centres.Add(new(proposed, store.StoreCode, true));
                review.Add($"Cost centre '{proposed}' for {store.StoreCode} is ETP's proposal. Save the name you keep in Settings → Tally companies, or vouchers for {store.StoreCode} stay blocked (COST_CENTRE_MISSING).");
            }
        }
        var centresOn = centres.Count > 0;
        var duplicateCentre = centres.GroupBy(centre => centre.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicateCentre is not null)
            review.Add($"Cost centre '{duplicateCentre.Key}' is given to more than one store ({string.Join(", ", duplicateCentre.Select(centre => centre.StoreCode))}). Tally needs one cost centre per store.");

        var ledgers = new List<TallyMasterLedger>();
        var groups = new List<TallyMasterGroup>();
        void Group(string name, string parent, string purpose)
        {
            if (!groups.Any(group => string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase))) groups.Add(new(name, parent, purpose));
        }
        void Ledger(string businessEvent, string store, string defaultName, string parent, string purpose, string? dutyHead = null, decimal? rate = null)
        {
            var (name, approved) = MappedName(source, businessEvent, store);
            if (!approved)
            {
                name = defaultName;
                toApprove.Add(new(store, businessEvent, name));
            }
            var existing = ledgers.FindIndex(ledger => string.Equals(ledger.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing < 0)
            {
                ledgers.Add(new(name, parent, purpose, businessEvent, [store], dutyHead, rate, centresOn, approved));
                return;
            }
            var found = ledgers[existing];
            if (!string.Equals(found.Parent, parent, StringComparison.Ordinal) || found.BusinessEvent != businessEvent)
                review.Add($"Ledger '{name}' is used for {found.BusinessEvent} and for {businessEvent}. One Tally ledger should serve one purpose; check the ledger mappings.");
            ledgers[existing] = found with
            {
                StoreCodes = found.StoreCodes.Append(store).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                FromApprovedMapping = found.FromApprovedMapping && approved
            };
        }

        var modes = source.TenderModes.Select(mode => (mode ?? "").Trim()).Where(mode => mode.Length > 0 && !mode.StartsWith("Service", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(mode => mode, StringComparer.Ordinal).ToArray();
        var rates = source.GstRates.Where(rate => ComponentOrder.Contains(rate.Component, StringComparer.Ordinal) && rate.Rate > 0)
            .Select(rate => new TallyMastersGstRate(rate.Component, decimal.Round(rate.Rate, 2, MidpointRounding.AwayFromZero))).Distinct()
            .OrderBy(rate => Array.IndexOf(ComponentOrder, rate.Component)).ThenBy(rate => rate.Rate).ToArray();

        foreach (var store in stores)
        {
            Ledger("SALES_REVENUE", store.StoreCode, DefaultSalesLedger, "Sales Accounts",
                "Sales value before GST (the invoice's net amount). One ledger for the firm; the cost centre tells the stores apart.");
            foreach (var rate in rates)
            {
                var percent = rate.Rate.ToString("0.##", CultureInfo.InvariantCulture);
                Ledger(TallySalesVoucherComposer.TaxEvent(rate.Component, rate.Rate), store.StoreCode, $"Output {rate.Component} {percent}%", "Duties & Taxes",
                    $"{rate.Component} collected on sales at {percent}%.", DutyHead(rate.Component), rate.Rate);
            }
            Ledger(TallySalesVoucherComposer.RoundOffCode, store.StoreCode, DefaultRoundOffLedger, "Indirect Expenses",
                "Paise rounded off on the bill (ETP's ROUND_OFF tender row).");
            foreach (var mode in modes)
            {
                var (defaultName, parent, purpose) = TenderLedger(mode, store.StoreCode);
                if (parent == CardUpiGroup) Group(CardUpiGroup, "Current Assets", "Card and UPI takings the bank or provider still has to pay in.");
                else if (parent == CreditNoteGroup) Group(CreditNoteGroup, "Current Liabilities", "Store credit notes the customer used as payment.");
                else if (parent == OtherTenderGroup) Group(OtherTenderGroup, "Current Assets", "Other payment modes, such as cheques, gift vouchers and loyalty points.");
                Ledger(TallySalesVoucherComposer.TenderEvent(mode), store.StoreCode, defaultName, parent, purpose);
            }
        }

        // Bank ledgers per store: not used by Sales vouchers yet; cash deposits arrive with the collections work.
        foreach (var store in stores)
        {
            var name = $"Bank - {store.StoreCode}";
            if (ledgers.Any(ledger => string.Equals(ledger.Name, name, StringComparison.OrdinalIgnoreCase))) continue;
            ledgers.Add(new(name, "Bank Accounts", $"The bank account {store.StoreCode}'s cash is deposited into. Not used by Sales vouchers yet.",
                null, [store.StoreCode], null, null, false, false));
        }
        review.Add("The bank ledgers 'Bank - <store>' are placeholders. Rename each to the real bank account (last four digits only) before the real company is set up.");

        var party = profile.SinglePartyLedger?.Trim() ?? "";
        if (profile.PartyPolicy == "SINGLE_LEDGER" && party.Length > 0
            && !ledgers.Any(ledger => string.Equals(ledger.Name, party, StringComparison.OrdinalIgnoreCase)))
        {
            ledgers.Add(new(party, "Sundry Debtors",
                "The one retail customer ledger named in Settings → Tally companies. With payment inside the voucher (D16) the vouchers post to the tender ledger, so this ledger stays empty for now.",
                null, stores.Select(store => store.StoreCode).ToArray(), null, null, false, true));
        }

        if (rates.Length == 0)
            review.Add("No GST rate was found in the GST detail imports of these stores, so no GST output ledger is listed. Import the GST detail report (R018) first.");
        else
            review.Add($"GST output ledgers are made for the rates found in the GST detail imports: {string.Join(", ", rates.Select(rate => $"{rate.Component} {rate.Rate.ToString("0.##", CultureInfo.InvariantCulture)}%"))}.");
        if (rates.Any(rate => rate.Component == "IGST"))
            review.Add("IGST appears in the imports: a sale to another state. Check with the accountant that these bills are right.");
        review.Add("Round off is placed under Indirect Expenses, card and UPI takings under Current Assets and redeemed credit notes under Current Liabilities. Change the groups here if the accountant prefers others; the ledger names must stay as listed.");
        review.Add("No opening balance is set for any ledger. The accountant enters opening balances by hand.");
        if (toApprove.Count > 0)
            review.Add($"{toApprove.Count} ledger name(s) are ETP's proposals. After the accountant agrees, approve them in Settings → Tally ledgers; until then those vouchers stay blocked (MAPPING_MISSING).");

        var voucherTypes = new[]
        {
            new TallyMasterVoucherType(TallySalesVoucherComposer.VoucherType, "Sales", true,
                "Tally's own Sales voucher type, used as is (D13). In the test company set its numbering to Manual, so Tally keeps ETP's invoice numbers.")
        };

        return new(company, profile.Environment, groups, centres, ledgers, voucherTypes, SetupSteps(company, profile, centresOn), review,
            toApprove.Distinct().ToArray());
    }

    private static (string Name, bool Approved) MappedName(TallyMastersSource source, string businessEvent, string store)
    {
        var inForce = source.Mappings.Where(row => row.IsActive && row.BusinessEvent == businessEvent
                && (row.StoreCode is null || row.StoreCode == store)
                && row.EffectiveFrom <= source.Today && (row.EffectiveTo is null || row.EffectiveTo >= source.Today))
            .OrderBy(row => row.StoreCode is null ? 1 : 0).ThenByDescending(row => row.Version).FirstOrDefault();
        if (inForce is null) return ("", false);
        var name = businessEvent.StartsWith("TENDER_", StringComparison.Ordinal) ? inForce.DebitLedger : inForce.CreditLedger;
        return (name.Trim(), true);
    }

    private static (string Name, string Parent, string Purpose) TenderLedger(string mode, string store) => mode.ToUpperInvariant() switch
    {
        "CASH" => ($"Cash - {store}", "Cash-in-Hand", $"Cash taken at {store}."),
        "CARD" => ($"Card Settlement - {store}", CardUpiGroup, $"Card payments at {store}, until the bank pays them in."),
        "UPI" => ($"UPI Settlement - {store}", CardUpiGroup, $"UPI payments at {store}, until the provider pays them in."),
        "CN" => ($"Credit Note Redeemed - {store}", CreditNoteGroup, $"Store credit notes used as payment at {store}."),
        "TC" => ($"Other Tender - {store}", OtherTenderGroup, $"Cheques, gift vouchers and loyalty points taken at {store}."),
        _ => ($"{mode} - {store}", OtherTenderGroup, $"{mode} payments at {store}.")
    };

    private static string DutyHead(string component) => component switch
    {
        "CGST" => "Central Tax",
        "SGST" => "State Tax",
        _ => "Integrated Tax"
    };

    private static IReadOnlyList<string> SetupSteps(string company, TallyProfile profile, bool centresOn)
    {
        var steps = new List<string>
        {
            $"Create the company '{company}' in TallyPrime exactly with this name (Alt+K → Create). Financial year from 1 April.",
            "In F11 Features: Enable GST = Yes. In the test company a sample GSTIN is fine; the real company uses the firm's own GSTIN.",
            "In F11 Features: Integrate accounts with inventory = No. ETP sends accounting vouchers only (D17).",
            centresOn ? "In F11 Features: Maintain cost centres = Yes. Every store is a cost centre (D12)." : "Cost centres are not needed: this company holds one store.",
            "Import this file: Import → Masters → choose the XML file → Behaviour: Combine (keep existing). Then open the List of Accounts and compare it with this list.",
            "Open the Sales voucher type (Alter → Voucher Type → Sales) and set Method of voucher numbering to Manual.",
        };
        if (profile.Environment == "TEST")
        {
            steps.Add("Test PC only: in F1 Help → Settings → Connectivity, set TallyPrime to act as Server on port 9000, so ETP can read back (decision 49). Never on the real company's PC.");
            steps.Add("TallyPrime in Educational mode accepts vouchers only on certain days of each month. ETP's test data uses those days; a voucher on another day is refused by Tally.");
        }
        return steps;
    }

    private static string Cut(string value, int length) => value.Length <= length ? value : value[..length];
}

/// <summary>Writes a <see cref="TallyMastersPlan"/> as Tally's "All Masters" import envelope and as a plain-English list for the
/// accountant. Groups first, then cost centres, then ledgers, so every parent exists before its child.</summary>
public static class TallyMastersWriter
{
    public static byte[] Xml(TallyMastersPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        using var stream = new MemoryStream();
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, NewLineChars = "\r\n", CheckCharacters = true };
        using (var writer = XmlWriter.Create(stream, settings))
        {
            writer.WriteStartElement("ENVELOPE");
            writer.WriteStartElement("HEADER");
            writer.WriteElementString("TALLYREQUEST", "Import Data");
            writer.WriteEndElement();
            writer.WriteStartElement("BODY");
            writer.WriteStartElement("IMPORTDATA");
            writer.WriteStartElement("REQUESTDESC");
            writer.WriteElementString("REPORTNAME", "All Masters");
            writer.WriteStartElement("STATICVARIABLES");
            writer.WriteElementString("SVCURRENTCOMPANY", plan.CompanyName);
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteStartElement("REQUESTDATA");
            foreach (var group in plan.Groups)
                Master(writer, "GROUP", group.Name, () =>
                {
                    writer.WriteElementString("PARENT", group.Parent);
                    writer.WriteElementString("ISSUBLEDGER", "No");
                    writer.WriteElementString("ISBILLWISEON", "No");
                    writer.WriteElementString("ISCOSTCENTRESON", "No");
                });
            foreach (var centre in plan.CostCentres)
                Master(writer, "COSTCENTRE", centre.Name, () =>
                {
                    writer.WriteElementString("CATEGORY", TallySalesVoucherXmlWriter.PrimaryCostCategory);
                });
            foreach (var ledger in plan.Ledgers)
                Master(writer, "LEDGER", ledger.Name, () =>
                {
                    writer.WriteElementString("PARENT", ledger.Parent);
                    writer.WriteElementString("ISBILLWISEON", "No");
                    writer.WriteElementString("AFFECTSSTOCK", "No");
                    writer.WriteElementString("ISCOSTCENTRESON", ledger.CostCentresOn ? "Yes" : "No");
                    if (ledger.GstDutyHead is not null && ledger.GstRate is { } rate)
                    {
                        writer.WriteElementString("TAXTYPE", "GST");
                        writer.WriteElementString("GSTDUTYHEAD", ledger.GstDutyHead);
                        writer.WriteElementString("RATEOFTAXCALCULATION", rate.ToString("0.##", CultureInfo.InvariantCulture));
                    }
                });
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        }
        return stream.ToArray();
    }

    /// <summary>The list the accountant reads before anything is imported into a real company.</summary>
    public static string Review(TallyMastersPlan plan, DateTime generatedLocal)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var text = new StringBuilder();
        void Line(string value = "") => text.Append(value).Append("\r\n");
        Line($"Tally masters for '{plan.CompanyName}' ({(plan.Environment == "TEST" ? "TEST company" : "LIVE company")})");
        Line($"Made by ETP on {generatedLocal.ToString("dd-MMM-yyyy HH:mm", CultureInfo.InvariantCulture)} from ETP's stores, payment modes, GST rates and ledger mappings.");
        Line("Review this list with the accountant before the file is imported into any real company.");
        Line();
        Line("1. Company setup in Tally");
        for (var index = 0; index < plan.SetupSteps.Count; index++) Line($"   {index + 1}. {plan.SetupSteps[index]}");
        Line();
        Line("2. Ledger groups ETP creates");
        if (plan.Groups.Count == 0) Line("   None. Only Tally's own groups are used.");
        foreach (var group in plan.Groups) Line($"   - {group.Name}  (under {group.Parent}): {group.Purpose}");
        Line();
        Line("3. Cost centres (one per store)");
        if (plan.CostCentres.Count == 0) Line("   None. This company holds one store.");
        foreach (var centre in plan.CostCentres) Line($"   - {centre.Name}  for store {centre.StoreCode}{(centre.Proposed ? "  [ETP's proposal]" : "")}");
        Line();
        Line("4. Ledgers");
        foreach (var ledger in plan.Ledgers)
        {
            var tax = ledger.GstDutyHead is null ? "" : $", GST {ledger.GstDutyHead} {ledger.GstRate?.ToString("0.##", CultureInfo.InvariantCulture)}%";
            var origin = ledger.BusinessEvent is null ? "" : ledger.FromApprovedMapping ? "  [approved in ETP]" : "  [ETP's proposal]";
            Line($"   - {ledger.Name}  (under {ledger.Parent}{tax}; stores {string.Join(", ", ledger.StoreCodes)}){origin}");
            Line($"       {ledger.Purpose}");
        }
        Line();
        Line("5. Voucher types");
        foreach (var type in plan.VoucherTypes) Line($"   - {type.Name}{(type.BuiltIn ? "  (built into Tally; nothing is created)" : $"  (under {type.Parent})")}: {type.Note}");
        Line();
        Line("6. Points to agree with the accountant");
        foreach (var point in plan.ReviewPoints) Line($"   - {point}");
        if (plan.MappingsToApprove.Count > 0)
        {
            Line();
            Line("7. Ledger names to approve in ETP (Settings → Tally ledgers) once agreed");
            foreach (var mapping in plan.MappingsToApprove) Line($"   - {mapping.StoreCode}  {mapping.BusinessEvent}  →  {mapping.LedgerName}");
        }
        return text.ToString();
    }

    private static void Master(XmlWriter writer, string element, string name, Action body)
    {
        writer.WriteStartElement("TALLYMESSAGE");
        writer.WriteStartElement(element);
        writer.WriteAttributeString("NAME", name);
        writer.WriteAttributeString("ACTION", "Create");
        writer.WriteStartElement("LANGUAGENAME.LIST");
        writer.WriteStartElement("NAME.LIST");
        writer.WriteAttributeString("TYPE", "String");
        writer.WriteElementString("NAME", name);
        writer.WriteEndElement();
        writer.WriteEndElement();
        body();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }
}
