# Visual reporting architecture

The visual layer is deliberately downstream of the governed report query:

`ReportResult → VisualReportComposer → VisualReportModel → WPF / Excel / PDF / SVG renderers`

`VisualReportModel` contains metadata, KPI cards, chart definitions, the unchanged detail table, controls and footnotes. Renderers do not query SQL and do not recalculate sales, stock, tender or staff measures. A chart failure is isolated: the detail result remains usable and an audit event is written by the desktop application.

`IChartRenderer` is renderer-independent. `SvgChartRenderer` produces accessible SVG without screenshots. The WPF renderer uses native retained controls and the PDF renderer draws vector primitives from the same model. Excel receives a stable five-sheet analytical workbook.

The representative rollout covers Daily Sales/DSR, Brand Sales, Closing Stock, Staff Performance, Tender Reconciliation, Management Trend and Daily Exceptions. Other reports still receive the generic governed summary and can be given a specialised definition later without changing SQL.
