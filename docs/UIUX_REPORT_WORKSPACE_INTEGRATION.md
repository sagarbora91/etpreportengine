# Report workspace integration contract

`ReportWorkspaceControls.cs` provides the fixed-toolbar report workspace without changing report calculations, SQL queries, controls or exporters.

## Dedicated DSR route

Create one `DailySalesReportWorkspace` instance in the main workspace host. When the `dsr` catalogue code is selected:

1. Replace the current workspace content with that instance immediately.
2. Set `BusinessDatePicker.SelectedDate` from the shell business date.
3. Subscribe once to `ActionRequested`.
4. For `Refresh`, call the existing DSR load path and pass its `DailySalesReportDocument` to `SetReport`.
5. Route `ExportPdf` and `ExportExcel` through the existing selected-report exporters.
6. Route `GenerateReportPack` through the existing daily-pack handlers.
7. Open the configured report-pack folder for `OpenExportFolder`.
8. Navigate to the existing Manual Entry destination for `OpenManualEntry`.
9. Return to the Sales workspace for `BackToReports`.

`SetReport` constructs a fresh `DailySalesReportView`, which avoids re-parenting a WPF logical child. `ShowLoading` and `ShowFailure` provide non-scrolling progress and failure states.

## Shared report routes

Create or cache a `ReportWorkspaceControl` for each definition in `ReportWorkspaceRegistry.All`. The registry partitions every `ProductReportCatalogue` entry exactly once across Sales, Stock, Tender/Cash/Service, Staff, Exceptions, Management and Investigation.

- `ReportSelected` supplies the existing catalogue code to the current `RunCatalogueReport_Click` routing logic.
- `ActionRequested` supplies the selected report code, dates and store scope.
- `SetPreview`, `ShowLoading` and `ShowUnavailable` update only the internally scrolling result area.
- The toolbar remains outside the result `ScrollViewer`, so filters and export actions remain visible.

## Main-window hook points

The integration owner should add these hooks in the shared main-window files:

- A content host occupying the existing Reports workspace area.
- A `ShowReportWorkspace(string reportCode)` method which uses `ReportWorkspaceRegistry.ForReport(reportCode)` and selects the matching menu item.
- A special case for `dsr` which opens `DailySalesReportWorkspace` rather than the generic Sales screen.
- An adapter from `ReportWorkspaceActionRequest` to existing handlers (`RunDsr_Click`, `ExportPdf_Click`, `ExportExcel_Click`, pack generation and Manual Entry navigation).
- `RenderDsrReport` should call the active DSR workspace's `SetReport` when that workspace is visible; the legacy visual panel remains only as a compatibility fallback.

The DSR scope defaults to `Combined (Titan + Helios)`. Scope selection must not suppress required Combined, Titan or Helios blocks unless the underlying approved DSR contract is deliberately revised.
