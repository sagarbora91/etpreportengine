# Visual reporting dependency decision

LiveCharts2, ScottPlot and OxyPlot were considered for the Windows chart surface. This release uses native WPF drawing plus the internal SVG/PDF vector renderers.

The choice keeps the application offline-first, avoids an additional plotting dependency and its transitive update surface, preserves consistent values across all outputs, and allows accessible text to remain next to each mark. It also avoids screenshot-based PDF/Excel output. If interactive zooming or very large time series become a validated requirement, the chart surface can be replaced behind `IChartRenderer` without changing report queries or the `VisualReportModel`.

DocumentFormat.OpenXml remains the Excel dependency. PDFsharp 6.2.4 is used only by the finalized DSR exporter so the one-page vector PDF can embed Segoe UI and preserve Unicode characters such as the rupee sign and em dash. No JavaScript, browser runtime, cloud service or AI calculation is introduced.

| Candidate | Version reviewed | Purpose | Licence / maintenance on 2026-08-27 | Production decision | Runtime / installer impact |
|---|---:|---|---|---|---|
| [LiveCharts2](https://github.com/Live-Charts/LiveCharts2) | 2.0.4 | General WPF business charts | MIT; active 2026 release | Not added; native renderer meets the current static analytical requirement | Would add chart packages and Skia-related transitive dependencies |
| [ScottPlot](https://github.com/ScottPlot/ScottPlot) | 5.1.58 | High-volume scientific/time-series plotting | MIT; active 2026 release | Not added; current reports do not require its specialised large-series plotting surface | Additional assemblies |
| [OxyPlot](https://github.com/oxyplot/oxyplot) | 2.2.0 | Lightweight WPF plots | MIT; maintained release line | Not added; export consistency is better served by the shared internal model | Additional assemblies |
| [DocumentFormat.OpenXml](https://github.com/dotnet/Open-XML-SDK) | 3.3.0 installed; 3.5.1 reviewed | Deterministic Excel files and native charts | MIT; active .NET Foundation project | Existing pinned dependency retained to avoid an unrelated upgrade in this phase | Already in installer |
| Existing PDF writer | Repository version 1.8 | Offline vector PDF and pagination | Internal and actively tested | Extended through `SimplePdfVisualReportExporter` | No new runtime |
| [PDFsharp](https://github.com/empira/PDFsharp) | 6.2.4 | Unicode, fixed-layout DSR PDF | MIT; actively maintained | Added for the finalized one-page DSR; existing generic report PDFs remain unchanged | Adds the PDFsharp assembly to the offline application |
| MathNet.Numerics | Future statistical analytics | MIT | Not required; no statistical calculations introduced | None |
| ECharts / Tabulator | Browser charts/tables | Apache-2.0 / MIT | Rejected for the native WPF path | Would require a browser/JavaScript surface |
| XlsxWriter / WeasyPrint | Python Excel/PDF generation | BSD-2-Clause / BSD-3-Clause | Rejected; duplicate runtime and reporting pipeline | Would materially enlarge offline installer |

Version and maintenance status must be rechecked before any other deferred package is adopted.
