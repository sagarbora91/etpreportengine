using System.Threading;
using System.Windows.Controls;
using Etp.Reporting.Application.Access;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Application.OperationsAdministration;
using Etp.Reporting.Application.Stores;
using Etp.Reporting.Desktop.Modules.Imports;
using Etp.Reporting.Desktop.Modules.OperationsAdministration;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.TestSupport;

namespace Etp.Reporting.Desktop.Tests.Service;

/// <summary>
/// Service interim lane L6 (decision 15, 3 Oct 2026). Migration 0048 adds the Service Centre store
/// AW330, inactive, under the SERVICE business unit. Retail scopes must not change, Import
/// History names it "Service Centre (AW330)", and Settings > Stores cannot make it active.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class StoreDisplayTests
{
    private static readonly StoreCatalogEntry[] Catalogue =
    [
        new("WLMHW", "Titan World", true),
        new("HEMW", "Helios", true),
        new("AW330", "Service Centre AW330", false, "SERVICE"),
    ];

    [Fact]
    public void Retail_store_scopes_are_unchanged_with_the_service_store_present()
    {
        var withService = new StoreScopeCatalog(); withService.Replace(Catalogue);
        var retailOnly = TestStoreCatalog.Create();

        Assert.Equal(retailOnly.Labels, withService.Labels);
        Assert.Equal(["WLMHW", "HEMW"], withService.Stores.Select(x => x.Code));
        foreach (var value in new[] { "WLMHW", "Titan World (WLMHW)", "helios", "WLMHW,HEMW", "Custom: UNKNOWN", "", "UNKNOWN" })
        {
            Assert.Equal(retailOnly.Resolve(value), withService.Resolve(value));
            Assert.Equal(retailOnly.Display(value), withService.Display(value));
        }
        Assert.Equal(StoreScopeCatalog.AllStores, withService.Display(null));
        // A bare Service code is not a shop scope, exactly as when AW330 was unknown.
        Assert.Null(withService.Resolve("AW330"));
    }

    [Fact]
    public void Service_store_is_named_service_centre_instead_of_custom_and_round_trips()
    {
        var catalog = new StoreScopeCatalog(); catalog.Replace(Catalogue);

        Assert.Equal("Service Centre (AW330)", catalog.Display("AW330"));
        Assert.Equal("Service Centre (AW330)", catalog.Display("aw330"));
        Assert.Equal("AW330", catalog.Resolve(catalog.Display("AW330")));
        Assert.True(catalog.IsServiceLabel("Service Centre (AW330)"));
        Assert.False(catalog.IsServiceLabel("Titan World (WLMHW)"));
        Assert.Equal("Service Centre (AW330)", catalog.HistoryStore("AW330"));
        Assert.Equal("WLMHW", catalog.HistoryStore("WLMHW"));
        Assert.Null(catalog.HistoryStore(null));
        Assert.DoesNotContain(catalog.Labels, label => label.Contains("AW330", StringComparison.Ordinal));

        // Before 0048 (no AW330 row) the code stays a Custom scope, as in 1.9.3.
        Assert.Equal("Custom: AW330", TestStoreCatalog.Create().Display("AW330"));
    }

    [Fact]
    public void A_service_store_is_never_a_shop_scope_even_if_marked_active()
    {
        var catalog = new StoreScopeCatalog();
        catalog.Replace([.. Catalogue[..2], new("AW330", "Service Centre AW330", true, "service")]);
        Assert.Equal(["WLMHW", "HEMW"], catalog.Stores.Select(x => x.Code));
        Assert.Single(catalog.ServiceStores);
    }

    [Fact]
    public void Settings_stores_row_reads_as_service_centre_not_a_shop_store()
    {
        var service = new ControlledMaster("STORE", "AW330", "Service Centre AW330", "APPROVED", false, null, null, "SERVICE");
        var retail = new ControlledMaster("STORE", "WLMHW", "Titan World", "APPROVED", true, null, null);

        Assert.Equal("Service Centre (AW330), Service, not a shop store", ControlledMasterPresentation.From(service).DisplayName);
        Assert.False(ControlledMasterPresentation.From(service).IsActive);
        Assert.Equal(new ControlledMasterPresentation("STORE", "WLMHW", "Titan World", "APPROVED", true, null, null), ControlledMasterPresentation.From(retail));
    }

    [Fact]
    public void Settings_stores_disables_the_active_toggle_for_the_service_store_only()
    {
        RunSta(async () =>
        {
            var service = new StoreAdministrationService();
            var view = new AdministrationWorkspaceView(new OperationsAdministrationPresentationSession(), () => service);
            view.UpdateAccess(new(true, true, true));
            await view.RefreshAsync();

            Assert.Equal(["Helios", "Service Centre (AW330), Service, not a shop store", "Titan World"], view.MasterDisplayNames.Order(StringComparer.Ordinal));
            Assert.True(view.CanToggleMasterActive);

            view.TypeMasterCode("AW330");
            Assert.False(view.CanToggleMasterActive);
            Assert.False(view.MasterActiveChecked);
            Assert.True(view.MasterActiveLockedForServiceStore);

            view.TypeMasterCode(" aw330 ");
            Assert.False(view.CanToggleMasterActive);

            view.TypeMasterCode("WLMHW");
            Assert.True(view.CanToggleMasterActive);
            Assert.False(view.MasterActiveLockedForServiceStore);
            // The Owner's tick comes back once the code is no longer AW330 ...
            Assert.True(view.MasterActiveChecked);

            // ... so the Retail store saved next stays active.
            Assert.True(await view.SaveMasterDraftAsync());
            Assert.True(service.LastSaved?.IsActive);
            Assert.Equal("WLMHW", service.LastSaved?.Code);
            Assert.True(view.MasterActiveChecked);

            // 1.12 A4: an existing Service Centre's code ticks "Service Centre" (locked: a store never changes kind), so its
            // save goes through the Service Centre save; a database refusal reaches the Owner as plain words.
            var centres = new CentreAdministration { SaveFailure = new InvalidOperationException(ServiceCentreStores.PrefixOverlapMessage) };
            view.ServiceCentreAdministrationFactory = () => centres;
            view.TypeMasterCode("AW330");
            Assert.True(view.MasterServiceChecked);
            Assert.False(view.CanToggleMasterService);
            Assert.True(view.CanTypeMasterPrefix);
            Assert.Equal("JOAW330", view.MasterPrefixText);
            view.TypeMasterNameAndReason("Service Centre AW330", "Synthetic rename");
            Assert.False(await view.SaveMasterDraftAsync());
            // 1.11.3 (U5 FW-20): Owner/admin failures end with "Ref: ..." so the Error register can find them.
            Assert.StartsWith("Master value was not saved: " + ServiceCentreStores.PrefixOverlapMessage + " Ref: ", view.StatusText, StringComparison.Ordinal);
            Assert.Null(centres.LastSaved);

            // A Retail code typed next drops the tick the Service code gave, and the shop save is used again.
            view.TypeMasterCode("HEMW");
            Assert.False(view.MasterServiceChecked);
            Assert.False(view.CanToggleMasterService);
            Assert.True(view.CanToggleMasterActive);
        });
    }

    [Fact]
    public void Settings_stores_adds_a_second_service_centre_with_its_prefix()
    {
        RunSta(async () =>
        {
            var service = new StoreAdministrationService();
            var centres = new CentreAdministration();
            var view = new AdministrationWorkspaceView(new OperationsAdministrationPresentationSession(), () => service)
                { ServiceCentreAdministrationFactory = () => centres };
            view.UpdateAccess(new(true, true, true));
            await view.RefreshAsync();
            view.ServiceCentres = [new ServiceCentre("AW330", "Service Centre AW330", "JOAW330")];

            view.TypeMasterCode("SC999");
            Assert.True(view.CanToggleMasterService);
            Assert.False(view.CanTypeMasterPrefix);
            view.SetMasterServiceCentre(true);
            Assert.True(view.CanTypeMasterPrefix);
            Assert.Equal("JOSC999", view.MasterPrefixText);   // JO and the code is offered
            Assert.False(view.CanToggleMasterActive);        // a Service Centre is never an active shop store
            Assert.False(view.MasterActiveChecked);
            view.TypeMasterNameAndReason("Synthetic Service Centre", "Second Service Centre (synthetic)");

            Assert.True(await view.SaveMasterDraftAsync());
            Assert.Equal(("SC999", "Synthetic Service Centre", "JOSC999", "Second Service Centre (synthetic)"), centres.LastSaved);
            Assert.Null(service.LastSaved);                  // the shop save was not used
            Assert.False(view.MasterServiceChecked);
            Assert.Equal("", view.MasterPrefixText);
        });
    }

    [Theory]
    [InlineData("jo-sc", "capital letters A-Z and digits only")]
    [InlineData("J", "capital letters A-Z and digits only")]
    [InlineData("JOAW3", "overlaps the prefix of Service Centre AW330")]
    public void Settings_stores_refuses_a_bad_service_centre_prefix_before_saving(string prefix, string reason)
    {
        RunSta(async () =>
        {
            var service = new StoreAdministrationService();
            var centres = new CentreAdministration();
            var view = new AdministrationWorkspaceView(new OperationsAdministrationPresentationSession(), () => service)
                { ServiceCentreAdministrationFactory = () => centres };
            view.UpdateAccess(new(true, true, true));
            await view.RefreshAsync();
            view.ServiceCentres = [new ServiceCentre("AW330", "Service Centre AW330", "JOAW330")];

            view.TypeMasterCode("SC999");
            view.SetMasterServiceCentre(true);
            view.TypeMasterPrefix(prefix);
            view.TypeMasterNameAndReason("Synthetic Service Centre", "Second Service Centre (synthetic)");

            Assert.False(await view.SaveMasterDraftAsync());
            Assert.StartsWith("Service Centre was not saved: ", view.StatusText, StringComparison.Ordinal);
            Assert.Contains(reason, view.StatusText, StringComparison.Ordinal);
            Assert.Null(centres.LastSaved);
            Assert.Null(service.LastSaved);
        });
    }

    [Theory]
    [InlineData(51970, ServiceCentreStores.PrefixOnShopStoreMessage)]
    [InlineData(51971, ServiceCentreStores.PrefixOverlapMessage)]
    [InlineData(51972, ServiceCentreStores.ActivationRefusedMessage)]
    [InlineData(51973, ServiceCentreStores.UnitKindChangeMessage)]
    [InlineData(51974, ServiceCentreStores.ServiceUnitMissingMessage)]
    [InlineData(51975, ServiceCentreStores.CodeIsShopStoreMessage)]
    public void The_0067_refusals_are_described_in_plain_words(int number, string expected)
    {
        var exception = SqlExceptionFactory.Create(new SqlExceptionFactory.Error(number, "synthetic", Procedure: "trg_stores_service_job_prefix", Line: 5));
        Assert.Equal(expected, ServiceCentreStores.DescribeRefusal(exception));
    }

    [Fact]
    public void The_prefix_check_refusal_is_described_in_plain_words()
    {
        var exception = SqlExceptionFactory.Create(new SqlExceptionFactory.Error(547,
            "The INSERT statement conflicted with the CHECK constraint \"CK_stores_service_job_prefix\"."));
        Assert.Equal(ServiceCentreStores.PrefixInvalidMessage, ServiceCentreStores.DescribeRefusal(exception));
        Assert.Null(ServiceCentreStores.DescribeRefusal(SqlExceptionFactory.Create(new SqlExceptionFactory.Error(547, "another CHECK constraint"))));
    }

    private sealed class CentreAdministration : IServiceCentreAdministration
    {
        public Exception? SaveFailure { get; set; }
        public (string Code, string Name, string Prefix, string Reason)? LastSaved { get; private set; }

        public Task SaveServiceCentreAsync(string storeCode, string name, string jobNumberPrefix, string reason, CancellationToken ct = default)
        {
            if (SaveFailure is not null) return Task.FromException(SaveFailure);
            LastSaved = (storeCode, name, jobNumberPrefix, reason);
            return Task.CompletedTask;
        }
    }

    // Lane L0 added 51904 to trg_stores_service_unit_inactive: a Service store moved out of the
    // SERVICE unit. Both trigger refusals reach the Owner as plain words, not the generic text.
    [Theory]
    [InlineData(51900, "A Service Centre store cannot be made an active shop store.", ServiceCentreStores.ActivationRefusedMessage)]
    [InlineData(51904, "A Service Centre store cannot be moved out of the Service Centre business unit.", ServiceCentreStores.UnitMoveRefusedMessage)]
    public void Service_store_trigger_refusals_are_described_in_plain_words(int number, string sqlMessage, string expected)
    {
        var exception = SqlExceptionFactory.Create(new SqlExceptionFactory.Error(number, sqlMessage, Procedure: "trg_stores_service_unit_inactive", Line: 9));

        Assert.Equal(expected, DesktopFriendlyError.Describe(exception));
        Assert.Equal(expected, ServiceCentreStores.DescribeRefusal(exception));
    }

    [Fact]
    public void Import_history_store_column_names_the_service_centre_and_keeps_retail_codes()
    {
        RunSta(async () =>
        {
            var catalog = new StoreScopeCatalog(); catalog.Replace(Catalogue);
            var serviceRow = new FolderImportFileResult("S009_PendingRepair.xlsx", "S009", "AW330", new(2026, 9, 28), new(2026, 9, 28), "Imported");
            var retailRow = new FolderImportFileResult("R025_sales.xlsx", "R025", "WLMHW", new(2026, 9, 28), new(2026, 9, 28), "Imported");
            var view = new ImportHistoryView(_ => Task.FromResult<IReadOnlyList<ImportHistoryEntry>>(
            [
                new("file:1", new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc), 1, serviceRow),
                new("file:2", new DateTime(2026, 9, 28, 10, 1, 0, DateTimeKind.Utc), 2, retailRow),
            ]));
            Assert.Equal("AW330", view.StoreText("AW330"));

            view.SetStoreLabels(catalog.HistoryStore);
            await view.ActivateAsync(new(new(2026, 9, 28), new(2026, 9, 28)));

            Assert.Equal(2, view.Entries.Count);
            Assert.Contains("All stores", view.StatusText, StringComparison.Ordinal);
            Assert.Equal("Service Centre (AW330)", view.StoreText("AW330"));
            Assert.Equal("WLMHW", view.StoreText("WLMHW"));
        });
    }

    private static void RunSta(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action().GetAwaiter().GetResult(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("STA test failed.", failure);
    }

    private sealed class StoreAdministrationService : IAdministrationService
    {
        public Exception? SaveFailure { get; set; }

        public Task<AdministrationDashboard> LoadAsync(string masterType, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdministrationDashboard(
                [
                    new ControlledMaster("STORE", "AW330", "Service Centre AW330", "APPROVED", false, null, null, "SERVICE"),
                    new ControlledMaster("STORE", "HEMW", "Helios", "APPROVED", true, null, null),
                    new ControlledMaster("STORE", "WLMHW", "Titan World", "APPROVED", true, null, null),
                ],
                [new ApplicationUser(1, @"DOMAIN\owner", "Owner", AccessRole.Owner, true, DateTime.UtcNow, "seed")],
                [],
                [],
                new ProductConfiguration("docs", "share", null, null, true, null, 20, DateTime.UtcNow, "owner")));

        public SaveControlledMaster? LastSaved { get; private set; }

        public Task SaveMasterAsync(SaveControlledMaster command, CancellationToken cancellationToken = default)
        {
            if (SaveFailure is not null) return Task.FromException(SaveFailure);
            LastSaved = command;
            return Task.CompletedTask;
        }
        public Task SaveUserAsync(SaveApplicationUser command, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveProductConfigurationAsync(SaveProductConfiguration command, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
