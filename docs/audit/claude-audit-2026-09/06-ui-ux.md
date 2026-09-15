# Audit 06 — UI/UX implementation (15 Sep 2026)

Read-only code audit of the WPF desktop shell and module views. Paths relative to repo root; `D/` = `src/Etp.Reporting.Desktop/`. Nothing was built or run; screen-fit predictions are arithmetic from the declared sizes (Button/TextBox/ComboBox/DatePicker `MinHeight = ActiveTargetHeight` = 48 comfortable / 34 compact, `D/Themes/Controls.xaml:23,100,120,122,137,143`; DataGrid row 46/30, `:143`).

Not re-reported (fixed today): window maximised/hard-coded size, dashboard "short window" card hiding, business date defaulting to yesterday. Note only: the source as read still contains `DateTime.Today.AddDays(-1)` at `D/Shell/TaskNavigator.cs:21`, `D/Modules/DailyWorkflow/DailyWorkflowWorkspaceView.xaml.cs:72,74`, `D/Modules/Imports/ImportWorkspaceView.xaml.cs:27`, `D/Modules/Reports/ReportsWorkspaceView.xaml.cs:55` — assumed superseded by the uncommitted fix.

Owner requirements used as the yardstick: 1366x768 touch desktop (working area 1366x728 at 100 %), finger-usable, no scrolling on main screens when maximised, nothing lost on resize, shop-floor staff, task-first.

## Headline findings

1. **Critical — shell chrome eats 231 of 728 px before any task content is drawn.** Header = padding 16 + row 1 (search field: 12 px label + 48 px TextBox = 64) + row 2 (6 + 48 for breadcrumb Buttons / store ComboBox / DatePicker) + 1 border = 135 px (`D/MainWindow.xaml:37-49`, `D/Shell/TaskNavigator.cs:111-113`, breadcrumb links are full 48 px Buttons `D/Shell/TaskNavigator.cs:345`); footer = 8+48+8+1 = 65 px because "Status details" is a Button with the 48 px MinHeight (`D/MainWindow.xaml:96`, `D/Shell/TaskNavigator.cs:88-91`); Windows caption 31. Content area at 1366x768 = **497 px**; at 1280x720 = 449 px; at 816x480 (the declared `MinHeight`) = 249 px.
2. **Critical — the DSR, the one screen staff will open every day, scrolls at 1366x768 in the default (comfortable) density.** DailySalesReportWorkspace loses 26 (margins) + 132 (toolbar: 48 px action row + period line + footer line + paddings, `D/ReportWorkspaceControls.cs:325,423-445`) + 8 + 52 (TabItem 48) → ~279 px per tab. The Titan/Helios tabs need ~340-360 px (StorePeriodCard ~190 + OperationalMetricTable ~140, `D/Modules/Reports/DailySalesFocusedView.cs:14-17`, `D/DailySalesReportControls.cs:30-61`) and "Service & targets" ~300 px. Every report's "Detail rows" DataGrid gets ~219 px → header + **3 rows visible** (`D/Modules/Reports/ReportPresentationControl.cs:86-94`).
3. **High — every module status/result message is forced onto one ellipsis-trimmed line with no tooltip.** `D/Controls/TaskBodyLayout.cs:20-21` pulls out any TextBlock named `*Status`/`*Result`, sets `NoWrap` + `CharacterEllipsis`. "Batch completed: 12 processed, 3 exact duplicate files, 1,240 new rows, … 2 failed" (`ImportWorkspaceView.xaml.cs:256-258`) and every report `ReportResult` line is cut; the only way to read it is the footer "Status details" dialog. Same at `D/ReportWorkspaceControls.cs:266,276,443`.
4. **High — three levels of identical text tiles before any work happens.** Home (module tiles, `D/MainWindow.Shell.cs:108-128`) → module overview (category tiles) → category (task tiles) → task (`D/Shell/TaskNavigator.cs:304-338`). Reports is four levels (`ShowReportCategories` `:361-380`). Import today's files = 12 taps from launch (§3).
5. **High — the first screen is an IT/import dashboard, not today's sales.** After the mandatory "Continue" on the welcome overlay (`D/MainWindow.Shell.cs:64-71`), staff land on "Today overview" whose four cards are: navigation buttons, SOURCE FILES / SOURCE ROWS counts, "% imported", SQL health + last backup (`D/Modules/Dashboard/DashboardView.cs:161-176`). No sales, walk-ins, cash or conversion figure anywhere on it.
6. **High — raw .NET exception text reaches the user.** `D/DesktopFriendlyError.cs:27` returns `ArgumentException.Message` verbatim; the repositories throw `new ArgumentException("A value is required.", name)` (`src/Etp.Reporting.Infrastructure.SqlServer/DailyReportingWorkflowRepository.cs:240-241`, used at `:80,:150`; `OperationalCompletionRepository.cs:97,155`; `Phase2OperationsRepository.cs:62,190,229,259`), so a walk-in saved without a reason shows "Manual input was not saved: A value is required. (Parameter 'reason')" (`DailyWorkflowWorkspaceView.xaml.cs:179` → `DailyWorkflowPresentationSession.cs:161-165`). There is no client-side required-field check (`DailyWorkflowPresentationSession.cs:106` just `reason.Trim()`).
7. **High — the DSR paints negative growth green.** `D/DailySalesReportControls.cs:48` colours every growth cell `#07965C` when the value is not "—"; `:60` does the same for the Change column. A −12.5 % day reads as good news.
8. **High — dead navigation surface ≈ 40 % of the shell.** The whole `LegacyWorkspaceScroll` panel set, the ContextSidebar (populated then immediately hidden on every route), `BreadcrumbText` (never made visible), the ReportWorkspaceControl report menu, the DSR availability badges, four dashboard card builders, `ModuleTile`/`StatusBadge`/`DetailDrawer`, `WorkspaceModuleOwnershipRegistry`, `WorkspaceLocation`/`WorkspaceNavigationHistory` and the `Stock Reports`/`Masters` route aliases have no live path (§8).

---

## 1. Layout robustness

### 1.1 Fixed Width / Height / MaxHeight inventory

| Where | Value | Consequence |
|---|---|---|
| `D/MainWindow.xaml:5` | `MinHeight=480 MinWidth=816` | Permits a window in which no task screen fits (§1.6). |
| `D/MainWindow.xaml:23` | `RailColumn Width=70` | fine |
| `D/MainWindow.xaml:30` | Sidebar collapse Button `Width=40` | dead (sidebar hidden) |
| `D/MainWindow.xaml:45` | `ShellStoreSelector Width=160`, `ShellBusinessDateSelector Width=150` | "All authorised stores" (~150 px at 14 px) is clipped in a 160 px ComboBox once the drop arrow is counted. |
| `D/MainWindow.xaml:98` | `DetailDrawerHost Width=390` | At 816 px the drawer covers 52 % of the content column. |
| `D/MainWindow.xaml:100` | Welcome right column `Width=480`, left margins 70 | At 816 px the left panel is 336−140 = 196 px; the 30 px "ETP Reporting Engine" TextBlock (`:100`) has no wrap → clipped. |
| `D/Shell/TaskNavigator.cs:94,96` | search `MinWidth=180 MaxWidth=320`; results `MaxHeight=380 MinWidth=460` | results popup 460 wide anchored under a ≤320 field; fine at 1366, overflows right edge at 816. |
| `D/Shell/TaskNavigator.cs:335` | tile detail `MaxHeight=30` | Wrapped purpose text is clipped mid-line, no tooltip — **content lost**. |
| `D/ReportWorkspaceControls.cs:128-130,342-343` | DatePickers `Width=180`, Scope `Width=190` | fine; `FocusedTaskLayout.LabelFields` also forces every DatePicker `MinWidth=180` (`D/FocusedTaskLayout.cs:99`). |
| `D/Modules/Reports/ReportPresentationControl.cs:176` | bar `Width = 420 × ratio` inside a stretch track | Track is Star-sized; at 816 px the track is ~360 px so a 100 % bar overflows the track (clipped by Border) — misleading proportions below ~1000 px. |
| `D/Modules/Reports/ReportPresentationControl.cs:190-191` | line chart `Canvas Width=760 Height=190`, `HorizontalAlignment=Left` | Inside a `HorizontalScrollBarVisibility=Disabled` ScrollViewer (`:92`) → right-hand points and legend clipped below ~900 px window width. |
| `D/Modules/Reports/ReportPresentationControl.cs:171-173` | label col 135, value col 130 | fine |
| `D/DailySalesReportControls.cs:88` | DSR date card column `Width=185` | only in the (unused) full `DailySalesReportView` header |
| `D/HelpCentreControls.cs:132,146,151` | category buttons `Width=260`, tiles `Width=260`, empty state `Width=560` | 560 > 816−70−32 content? no (714) — ok; at 125 % DPI tiles still 3-up. |
| `D/HelpCentreControls.cs:275,305,307` | `Width=210` combo column; shortcut rows 190/190 | fine |
| `D/Modules/Reports/ReportScopeDialog.cs:13` | Window `Width=400 Height=370` | ok (separate window) |
| `D/Controls/StatusDetailsDialog.cs:11` | `Height=320` | ok |
| `D/Shell/DraftNavigationDialog.cs:15` | `Width=480` | ok |
| `D/UiControls.cs:18,112` | ModuleTile `MinHeight=164`; DetailDrawer `Width=390` | dead code |
| Module XAML | ~70 `Width="…"` on TextBox/ComboBox/DatePicker (e.g. `DailyWorkflowWorkspaceView.xaml:13-16,46-51,63-70,77-81,89`, `ImportWorkspaceView.xaml:10-19`, `SourceInboxWorkspaceView.xaml:17-35,48`, `AccountingWorkspaceView.xaml:11-45`, `ArchiveWorkspaceView.xaml:12-63`, `RegistersWorkspaceView.xaml:16-51`, `OperationsWorkspaceView.xaml:7,10,14-15`, `AdministrationWorkspaceView.xaml:7,10`, `InvestigationApprovalsWorkspaceView.xaml:7,9-10`, `ReportsWorkspaceView.xaml:10-11,24`) | All sit in WrapPanels so nothing is clipped, but rows wrap unpredictably: the stock-count WrapPanel (`DailyWorkflow:62-73`) is ~1,060 px wide → 2 rows at 1366, 3 rows at 1100. The 78-90 px numeric boxes (`:64-68`) are narrower than the 48 px MinHeight is tall — hard to hit with a finger. |
| `D/Modules/DailyWorkflow/DailyWorkflowWorkspaceView.xaml:48,51` etc. | inner TextBox `Width=150`, `Margin=8,0,0,0` inside a `Width=162` StackPanel | Field is 8 px narrower than its label column; visually misaligned. |

No `UniformGrid` sets `Rows` (grep: 0). All UniformGrids are `Columns=…` (Home 2/3, overview 2/3, DSR KPIs 6→3, dashboard 2, report KPIs ≤4, `ReportsWorkspaceView.xaml:14-22` five 5-column catalogues (hidden engine), FavouriteReports 3, DensitySelector 2, Service tender 5 / periods 6). The Service card's 6-column period strip (`DailySalesReportControls.cs:70`) needs ~6×90 px; in the DSR tab at 816 px it is ~340 px wide → numbers overlap.

### 1.2 ActualHeight / ActualWidth based hiding
- `D/MainWindow.Shell.cs:111` — Home columns `ActualWidth >= 1000 || ActualHeight < 600 ? 3 : 2` (short-and-narrow → 3 columns, tall-and-narrow → 2; inverted intent). `:116` hides module descriptions below 600 px; `:117` shrinks padding. `BuildModuleHome()` is rebuilt on **every** `SizeChanged` (`:245`) — the whole tile set is thrown away during a drag-resize.
- `D/MainWindow.Shell.cs:222,244-245` — sidebar overlay/inline threshold 1100 and `PageDescription` hidden below 1100 px (so also hidden at 125 % DPI on 1366 → 1093 DIP).
- `D/Shell/TaskNavigator.cs:310,322` — overview columns `ActualWidth >= 1280 ? 3 : 2`, promoted to 3 when >6 tiles.
- `D/ReportWorkspaceControls.cs:271-280` — `compactFilters` when toolbar `< 780` px: date pickers and scope combo collapse behind a "Period & store" button that opens `ReportScopeDialog`. Acceptable adaptive behaviour; but `compactFilters` is only recomputed from `SizeChanged` and `updateToolbar()` so the first layout pass draws the wide variant then re-flows (visible jump).
- `D/ReportWorkspaceControls.cs:446-451` — DSR `shortWindow = ActualHeight < 300` hides `periodText` and the footer (status line + hidden availability panel). The trigger is the workspace's own height, so it fires at 816x480 (workspace ≈ 257 px) — the same case in which the toolbar has just wrapped to two rows, i.e. it hides the FTD/MTD/YTD period definition exactly when the user has least context. It never fires at 1366/1280.
- `D/Modules/Dashboard/DashboardView.cs:178-188` — `FitGroups()` 1 column below 700 px; fine.
- `D/FocusedTaskLayout.cs:82` — Watch Folder fields 2-up above 560 px; fine.

### 1.3 Unbounded StackPanels and ScrollViewer nesting
- Every module view root is a `StackPanel` (`ImportWorkspaceView.xaml:6`, `SourceInbox:6`, `Accounting:6`, `Archive:6`, `Registers:6`, `Operations:5`, `Administration:5`, `Investigation:5`, `Reports:7`, `DailyWorkflow:7` inside a Border). They are never displayed as-is (see §8); `FocusedTaskLayout` re-parents selected children into a new `StackPanel body` (`D/FocusedTaskLayout.cs:67-86`) which `TaskBodyLayout` wraps in a ScrollViewer or splits into tabs (`D/Controls/TaskBodyLayout.cs:12-49`). Net effect: a DataGrid tab fills the space (good, `:26` clears MaxHeight/Height); a form tab always scrolls if it exceeds ~330 px.
- Nested scroll: `DailySalesFocusedView` = TabControl → per-tab ScrollViewer (`DailySalesFocusedView.cs:20`) inside `previewHost`, itself inside `FocusedWorkspaceHost` (no outer scroll) — one level, fine. `ReportVisualPresenter.BuildFocusedPreview` Summary tab ScrollViewer (`ReportPresentationControl.cs:92`) hosting a StackPanel with a `Canvas` of fixed width — horizontal clipping (§1.1). `DashboardView` `historyContent/auditContent/chartContent` are ScrollViewers around StackPanels containing DataGrids with `MaxHeight 260/230` (`DashboardView.cs:33,42,138-140,416-417`) → **DataGrid scroll inside page scroll**: touch panning on the grid scrolls the grid until its end then the page — the classic nested-scroll trap; these routes are reachable via `SelectTask("import-history"|"audit"|"recent-activity"|"trends")` (`:131-134`, `TaskNavigator.cs:413`).
- `HelpCentre` topic card: ScrollViewer inside Border inside Grid row `*` — fine. `GeneralPreferencesView`: tab → ScrollViewer → StackPanel of 48 px CheckBoxes (29 favourite reports ≈ 1,400 px) — scrolls, acceptable for a settings page.
- `OpenDrawer` content: ScrollViewer in a 390 px drawer (`D/MainWindow.Shell.cs:284`) — fine.

### 1.4 DataGrids with fixed heights
26 `AutoGenerateColumns="True"` grids. `MaxHeight` literals: `DashboardView.cs:33,42` (260/230 via `ReadOnlyGrid`), `DailyWorkflow.xaml:42` (210), `:60` (150), `:106` (260), `Import.xaml:37-38` (180/220), `SourceInbox.xaml:42,45` (260/190), `Accounting.xaml:24,28` (250/220), `Archive.xaml:26,47,71` (250/170/300), `Registers.xaml:21` (280), `Operations.xaml:9,10,15,16` (220/190/140/190), `Administration.xaml:8,11,12` (220×3/180), `Investigation.xaml:8,10` (240/220), `Reports.xaml:27` (360). `TaskBodyLayout.cs:26` neutralises them for focused tasks, so today the only live fixed-height grids are the three dashboard ones (§1.3). If the legacy panels were ever shown again every screen would be a 300 px-grid-inside-a-page-scroll.

### 1.5 TextTrimming without tooltip
`D/Controls/TaskBodyLayout.cs:21` (all status/result lines), `D/ReportWorkspaceControls.cs:266,276,443` (report status, compact scope line, DSR status), `D/Shell/TaskNavigator.cs:144` (search result purpose), `D/Modules/Reports/ReportPresentationControl.cs:174` (bar labels — brand/item names truncated with the value still shown; no way to read the full name on touch), `D/MainWindow.xaml:41` (PageTitle — long report names like "Customer-safe Invoice Sales Summary" trimmed at ~700 px when the search box is present). Only `ApplicationStatus` (`MainWindow.xaml:96`) binds its own text as ToolTip, and tooltips do not exist on touch anyway.

### 1.6 Predicted fit at the three sizes (comfortable density, 100 % DPI)

Content height = work area − 31 caption − 135 header − 65 footer.

| Screen | 1366x728 (497 px) | 1280x680 (449 px) | 816x480 window (249 px) |
|---|---|---|---|
| Welcome overlay | fits | fits | left brand text clipped (§1.1) |
| Home module tiles (3×3 for Owner, `MainWindow.Shell.cs:111`) | fits, ~157 px/tile | fits | fits (descriptions hidden), ~75 px tiles with 18 px titles |
| Module/category overviews (`TaskNavigator.cs:304-338`) | fits (Reports: 8 category + 3 tool tiles ≈ 350 px) | fits | scrolls |
| Dashboard "Today overview" | fits (2 rows ≈ 353 px of 425) | fits (377 available) | scrolls (177 available) |
| **DSR** Summary tab (6 KPIs, 3-up) | fits (~186 of ~279) | fits (~231 available) | scrolls; `shortWindow` hides period/status |
| **DSR** Titan / Helios tabs | **scrolls** (~350 needed / ~279) | scrolls | unusable (~50 px) |
| **DSR** Service & targets | **scrolls slightly** (~300 / ~279) | scrolls | unusable |
| Other reports: Summary tab (4 KPIs + 10-row bar chart + control line ≈ 330 px) | scrolls slightly (295 available) | scrolls | unusable |
| Other reports: Detail rows grid | header + **3 rows** (219 px) | header + 2 rows (171 px) | 0 rows |
| Walk-ins ("Entry fields" tab ≈ 340 px: scope row 76 + manual row 76 + readiness banner 150-190) | **borderline; scrolls once the "Missing ETP sources: …" line wraps** (331 available) | scrolls | unusable |
| Import Files "Entry fields" (≈ 232 px) | fits | fits | scrolls; 5-button action toolbar wraps to 2 rows |
| Import diagnostics / batch results tabs | ~6 rows | ~5 rows | 0-1 rows |
| Settings / Accounting / Registers / Operations forms | scroll (each ≥ 400 px of wrapped fields) | scroll | unusable |
| Help topic | fits (own scroll) | fits | scrolls |

Compact density (34 px) recovers ~50 px of chrome and ~30 px of toolbar: DSR store tabs then fit (~330 px) but 34 px targets fail the finger requirement, so compact is not the answer.

**125 % DPI on the 1366x768 panel** (WPF has no PerMonitorV2 manifest — no `*.manifest`, no `dpiAware` in `D/Etp.Reporting.Desktop.csproj`): logical size 1093x582 → content ≈ 383 px. Everything in the "1280" column gets worse by ~65 px; the DSR Summary tab (186 px) still fits, nothing else does. At 150 % (911x512 logical) the app behaves like the 816x480 column.

---

## 2. Touch

**Density resources applied** (`D/Themes/Controls.xaml`): Button `:23`, SidebarItemButton `:73`, TextBox `:100`, ComboBox `:120` (closed control only), DatePicker `:122` + template button 44 px `:130`, CheckBox `:136`, TabItem `:137`, DataGrid `RowHeight`/`ColumnHeaderHeight` `:143`. `ApplyDensity` swaps the window-level resource (`D/MainWindow.Shell.cs:248-255`).

**Not applied / missing:**
- `RadioButton` — no style; `DensitySelector` radios are default ~20 px (`D/DensitySelector.cs:42`).
- `Expander` — style sets Foreground/FontWeight only (`Controls.xaml:139`); header toggle is default ~22 px (only used in the dead sidebar, `MainWindow.Shell.cs:198`).
- `ComboBoxItem` — no style → every drop-down list (store selectors, manual field, status filter, master type, user role, sales dimension…) has ~22 px items. The most-used touch control in the app is the least finger-friendly.
- `ListBox`/`ListBoxItem` — master search rows are hand-set to 48 (`TaskNavigator.cs:145`); OK.
- `MenuItem` — hand-set 48 (`D/ReportActionMenu.cs:14`); OK.
- `ScrollBar` — no style (grep 0): default 17 px wide thumbs on every DataGrid and ScrollViewer. `PanningMode=VerticalOnly` on all ScrollViewers (`Controls.xaml:11`) and `Both` on DataGrid (`:151`) means finger-panning works, but wide report grids (14+ auto columns) can only be scrolled sideways by the 17 px bar or by panning the grid itself.
- `DataGridCell` Padding 10,5 — fine; row hover highlight `IsMouseOver` (`Controls.xaml:154`) meaningless on touch.
- DatePicker calendar: `CalendarOpened` sets `MinWidth 350`, `FontSize 14` and forces every `ButtonBase` (day cells, month arrows) to 44×44 (`D/Themes/Controls.xaml.cs:16-31`) — good, but 44 < the app's own 48 comfortable target and the popup is not re-measured for months with 6 rows (6×44 + header ≈ 330 px; fine at 1366, off-screen at 480). The text box part accepts typed dates only in the current culture's short pattern (§4).
- Numeric entry: no `InputScope` anywhere (grep 0) → the Windows touch keyboard opens full QWERTY for walk-ins, stock quantities, amounts, SMTP port. Add `InputScope="Number"` to `ManualValueInput`, `Stock*Input`, `StaffTargetValueInput`, `RegisterQuantity/AmountInput`, `AdjustmentAmountInput`, `SmtpPortInput`, `MaximumAttachmentInput`, `ScheduleTimeInput`.
- On-screen keyboard invocation relies on WPF's default (works on Win10 in tablet mode or with "show touch keyboard when not in tablet mode" enabled); nothing in code ensures it.

**Hover-only / tooltip-only information (unreachable on touch):**
- Rail icons have ToolTip labels only (`D/MainWindow.xaml:25-26`) — five unlabeled glyphs; no "current section" state (RailButton has only an `IsMouseOver` trigger, `Controls.xaml:70`).
- `ScopeSelector.ToolTip` explains *why* the store combo is disabled (`D/Modules/Reports/ReportWorkspaceSession.cs:35,51`) and `DateFromPicker.ToolTip` explains snapshot reports (`ReportWorkspaceControls.cs:187`) — tooltips never show on disabled controls without `ToolTipService.ShowOnDisabled` (grep 0), so the explanation is unreachable for everyone.
- Line-chart values are tooltips on 8 px dots (`ReportPresentationControl.cs:206`).
- `ReportDetailFilter.ToolTip` (`ReportDetailFilter.cs:21`), sidebar unavailable reasons (`MainWindow.Shell.cs:194`, dead), DSR availability badge details (`ReportWorkspaceControls.cs:408-411`, panel is `Collapsed` at `:441` so never shown at all; the "Availability" button → `StatusDetailsDialog` is the live path, `:434-438`).
- ~40 TextBoxes use `ToolTip` as their only label in XAML; `FocusedTaskLayout.LabelFields` (`D/FocusedTaskLayout.cs:93-110`) synthesises a visible 12 px label from `AutomationProperties.Name` for TextBox/ComboBox/DatePicker that are *direct children of a WrapPanel*. Inputs outside WrapPanels or already wrapped in a StackPanel keep tooltip-only hints (`DailyWorkflow.xaml:47-53` value/reason have XAML labels; `Accounting.xaml:48` has a label; `Settings.xaml:9,28-37` have labels) — coverage is accidental rather than designed.

**Right-click / double-click:** no ContextMenu is right-click-only (the "Actions ▾" menu opens on left click, `ReportActionMenu.cs:24`). Row details need `MouseDoubleClick` (`ReportsWorkspaceView.xaml:27`, `ReportPresentationControl.cs:84`) — double-tap works in WPF but is undiscoverable; the focused preview adds an "Open selected row details" button (`:88`), the legacy grid does not.

**Popups on touch:** master search opens the full task list on tap (`TaskNavigator.cs:101,150`) — 100+ rows in a 380 px popup; `StaysOpen=false` closes it on the first outside tap; fine. `DrawerOverlay` closes on tap outside (`MainWindow.Shell.cs:289`); fine.

---

## 3. Information architecture

### 3.1 Tap counts from launch (Owner/Store Manager, DB configured, default density)

| Goal | Path | Taps (excluding typing) |
|---|---|---|
| (a) View today's DSR | Continue (welcome) → Dashboard "Reports" (`DashboardView.cs:163`) → Reports overview "Sales · 9" tile (`TaskNavigator.cs:372`) → "Daily Sales Report" tile (`:321`) → report auto-runs for the header date (`:406`) | **4** |
| (a') via search | Continue → tap search box (DSR is ranked first when the query is empty, `TaskNavigation.cs:44`) → tap first row | **3** |
| (a'') via Home | Continue → rail Home → "Reports" tile → "Sales" → "Daily Sales Report" | 5 |
| (b) Import today's ETP files | Continue → rail Home (Dashboard has no import entry) → "Imports" tile → "Intake" category → "Import Files" → open Import store combo → pick WLMHW (`ImportWorkspaceView.xaml:12` `SelectedIndex=-1`, not driven by the shell store, `TaskNavigator.cs:48-58`) → "Browse workbook/ZIP…" → file dialog (≥2 taps) → "Validate workbook" → "Import validated rows" | **12** (+ repeat 7 for each additional file; or "Import batch" for a folder) |
| (c) Enter walk-ins | Continue → Dashboard "Manual inputs" → (view auto-refreshes on first visit, `TaskNavigator.cs:474`) → open field combo → pick "Walk-ins" (first item is auto-selected only if it happens to be first) → tap value → type → tap reason → type a reason → "Save input" (moved to top toolbar by `FocusedTaskLayout`) | **7** + 2 typing; store defaults to WLMHW regardless of the header selector; Helios walk-ins need the store combo (+2) then "Refresh status" (+1) |

Keyboard shortcuts exist for all three (Ctrl+K/Enter, Ctrl+O, Ctrl+S) but touch staff cannot use them.

### 3.2 Overview-tile pattern
`NavigateOverview` → `ShowTaskOverview` (`D/Shell/TaskNavigator.cs:298-338`) renders a `UniformGrid` of plain Buttons with title + "N tasks", for modules *and* categories *and* tasks. Reports adds a fourth level (`ShowReportCategories` `:361-380`: "Report categories" 2-up + "Favourites, filters and packs" 3-up). Tiles carry no icon, no status, no data; the `ModuleTile` class that had icons and status text is never instantiated (`D/UiControls.cs:10`). `MakeTaskTile` detail text is clipped at 30 px (`:335`). For shop staff every visit to the DSR is a tour through "Modules → Reports → Sales → Daily Sales Report".

### 3.3 Breadcrumb / title duplication
Four header elements describe the same location: `BreadcrumbText` (always `Collapsed`, `D/MainWindow.xaml:41`; still written in ≥10 places — `MainWindow.Shell.cs:90,167`, `MainWindow.Workspaces.cs:37,121`, `TaskNavigator.cs:325,357,377,408`), `BreadcrumbLinks` (three 48 px Buttons "← Back", module, category — `TaskNavigator.cs:340-351`), `PageTitle` (22 px), `PageDescription` (hidden < 1100 px; for non-report tasks it is the auto-generated "Open walk-ins in Dashboard." from `TaskNavigation.cs:11`). Plus the persistent task header that `FocusedTaskLayout` says makes the module's own title redundant (`D/FocusedTaskLayout.cs:72`), plus the DSR's own hidden title row (`ReportWorkspaceControls.cs:337`). Formats disagree: "TODAY  /  OVERVIEW" (`MainWindow.Shell.cs:167`), "Modules / Reports", "Reports / Sales / Daily Sales Report" (`Workspaces.cs:37`), "Modules → Reports", "Reports → Favourites → Favourite Reports" (`TaskNavigator.cs:325,357`), "Dashboard → Daily Close → Walk-ins" (`TaskNavigation.cs:10`).

### 3.4 Rail
Home, Search, Help, Settings, Profile — icons only, no labels, no selected state, identical treatment for the owner-only Settings (viewers are silently redirected to "Display & Preferences", `MainWindow.Shell.cs:152`). "Profile" opens a drawer of identity text (`:265`) — no value for staff. There is no rail entry for the three daily tasks.

### 3.5 ContextSidebar — dead
`ConfigureSidebar` populates the sidebar and immediately calls `HideSidebar()` (`D/MainWindow.Shell.cs:171-186`); `DisplayTaskRoute` and every focused/help/overview path also call `HideSidebar()` (`TaskNavigator.cs:306,354,407`, `Workspaces.cs:41,118`). `ShowSidebar()` is reachable only from `MainWindow_SizeChanged` when `!sidebarExplicitlyCollapsed` (`:244`) — but `HideSidebar` sets it `true` and nothing else resets it — and from `CloseHelpWorkspace` when `WasSidebarVisible` (`Workspaces.cs:163`), which is captured while the sidebar is hidden. `SidebarToggleButton` is created `Collapsed` (`MainWindow.xaml:43`) and only made visible inside `ShowSidebar`. Therefore `SidebarSearchInput`, `PopulateSidebar`, `SidebarItem_Click`, the Expander groups, the "— requires source" unavailable-item messaging and `EmptyState("No navigation matches")` (`:183-215`) are unreachable. The `UiNavigationRegistry` groups exist mainly to feed this sidebar.

### 3.6 Master search
"Search tasks · Ctrl+K" (`TaskNavigator.cs:111-113`) searches `TaskNavigation.All` (~90 tasks + 20 help topics + profile). Ranking prefers the DSR on an empty query (`TaskNavigation.cs:44`), which accidentally makes it the fastest DSR route. Results show "Module → Category → Title" paths that expose the internal IA ("Dashboard → Daily Close → Walk-ins", "Settings → Database & Recovery → Support Package"). It is a developer's command palette occupying the most valuable 64 px of the header on a shop-floor screen.

### 3.7 Duplicated date pickers
≈18 DatePicker declarations. On the DSR screen two are visible at once — header `ShellBusinessDateSelector` and `BusinessDatePicker` in the toolbar. Header → task is one-way (`ApplyBusinessDate`, `TaskNavigator.cs:71-84`); task → header only when the report is run (`ApplyWorkspaceScope`, `Workspaces.cs:93-97`) and never for the DSR (`RunFocusedReport` is wired to `ReportWorkspaceControl` only, `:56-59`; DSR refresh goes through `RefreshCurrentReport`, `TaskNavigator.ReportActions.cs:13-14`). For the six task workspaces the header date is copied only until the workspace has been visited once (`:77-82`) — after that, changing the header date does nothing to the walk-in/import/register screens except a footer sentence ("Open tasks retain their displayed task date…", `:83`). Staff will change the header date, open Walk-ins and save against the wrong day.

### 3.8 Store selector semantics
Header `ShellStoreSelector` items "Titan World / Helios / All authorised stores", default index 2 (`MainWindow.xaml:45`). `ShellStore_Changed` (`TaskNavigator.cs:48-58`) translates to "Titan/Helios/Combined" for the reports view, to "WLMHW/HEMW" for the daily-workflow store *only if not yet visited*, and to nothing for Import (store combo starts empty), Source Inbox, Registers, Accounting (defaults to text "WLMHW", `Accounting.xaml:11`), Archive ("All/COMBINED/WLMHW/HEMW", `Archive.xaml:14-15`). Five vocabularies for two shops: "Titan World", "Titan", "WLMHW"; "Helios", "HEMW"; "All authorised stores", "Combined (Titan + Helios)", "Combined", "COMBINED", "All", "Select one store". The DSR ignores the selector entirely (combo disabled, `ReportWorkspaceSession.cs:33-35`) while the header still shows "All authorised stores" as if it applied.

---

## 4. Consistency

### 4.1 Parallel style systems
1. `D/Themes/Colors.xaml` (21 colours), `Typography.xaml` (9 text styles), `Spacing.xaml`, `Controls.xaml` (implicit styles, `Card`/`SurfaceCard`/`InsetCard`/`DarkCard`/`StatusBanner`).
2. `D/MainWindow.xaml:10-20` re-declares `Card` (Divider border, radius 14, padding 18 — overrides the theme `Card` (SecondaryText border, radius 12) for the window only) and an unused `NavigationButton` style; hard-codes `#164651`, `#344454`, `#9B5C00`, `#5D6873`, `#172B3E`, `#44515E`, `#A9B6CC`, `#550C1628`.
3. `DsrUi` (`D/DailySalesReportControls.cs:10-16`): `Brush("#…")` per call, default text `#10252D`, a remap table (`#008D78`/`#07965C`→`#006B5C`, `#C97800`→`#A76500`, `#687285`→`#5D6873`) that silently rewrites the colours the callers ask for, `Math.Max(12, size)` that silently rewrites the 9-11.5 px sizes the callers ask for (`:14`; callers at `:35,37,41,44,48,55,57,60,68-70,79,88-91`). Cards re-declare white/`#D9E3E3`/radius 12 five times (`:15,22,34,58,67,78`).
4. `DashboardView` private static brushes (`D/Modules/Dashboard/DashboardView.cs:21-27`) duplicating `PrimaryText #10252D`, `SecondaryText #65757A`, `Accent #008D78`, `AccentSoft #E3F3F0`, `DarkSurface #082E3A`, `Divider #D9E3E3`, `SurfaceSecondary #F0F5F5`, plus literals `#C33D49`, `#A76500`, `#AFC4C9`, `#1B4651`, `#FAF1E3`.
5. `ReportWorkspaceControls` literals `#F4F7FB` (a background that is not `AppBackground #F3F7F7`), `#DCE4EF`, `#687285`, `#36506F`, `#E8F7F0/#80CEAC/#08764B`, `#FFF4DF/#E9B45C/#9B5C00` (`:113,253,267,324,400-408,423,438,442`).
6. `VisualReportTheme` (`src/Etp.Reporting.Reporting/VisualReporting.cs:20-29`: Navy `#17324D`, Blue `#247BA0`, Teal `#2A9D8F`, Red `#C94C4C`) drives all chart/KPI colours in `ReportPresentationControl.cs` — a third accent family next to the theme teal `#008D78` and the DSR store blues `#2269E8/#7137D4` (`DailySalesReportDocument.cs:46-47`).
7. `ModuleTile.Accent` per-module RGB literals (`D/UiControls.cs:52-60`, dead).
8. `Brushes.SeaGreen / DarkOrange / Firebrick` for status tones (`MainWindow.xaml.cs:221,231,257`, `DailyWorkflowWorkspaceView.xaml.cs:326-328`) instead of `Success/Warning/Critical`.
9. 83 literal `FontSize=` outside the theme; the `Typography.xaml` styles are used only on the welcome overlay and sidebar caption. Page title 22 (`MainWindow.xaml:41`) vs `PageTitleText` 25; module headings 18 (every module XAML line 7-8); DSR title 26/28; ReportWorkspace 24; Help 20/25; card titles 16/17.
10. Ten XAML views hard-code `Foreground="#5D6873"` for captions instead of `{DynamicResource SecondaryText}` (which is `#65757A` — two greys for the same role).

### 4.2 Wording and capitalisation
- Spelling: "Operations Center" (route/destination name, 28 hits) vs "Control Centre"/"Help Centre"/"Exception Centre"/"Reports Centre" (47 hits). "Favourite" vs alias "favorites". 
- The same screen has 3-4 names: Walk-ins / Manual Entry / Manual inputs / "Business-date reporting workflow"; Import ETP / Imports / Import Files / Import Overview / "ETP import source"; Admin / Settings / Settings / Administration / Masters / System Health; Dashboard / Today overview / Today Overview / Business day cockpit; Sales Reports / Reports / Reports Centre.
- Button labels mix sentence case ("Refresh status", "Save input", "Import validated rows") with title case ("Refresh Preview", "Back to Reports", "Export PDF") and trailing ellipsis inconsistently ("Export selected report to Excel…" vs "Export Excel" vs "Export archived Excel…" vs "Export complete pack Excel…").
- Group labels are SHOUTED in the registry ("BUSINESS DAY", `UiNavigation.cs:71`) then title-cased for tasks (`TaskNavigation.cs:64`); StatusBadge upper-cases everything (`UiControls.cs:74`); KPI labels upper-case ("COMBINED FTD", `DailySalesReportControls.cs:89`); DensitySelector "DISPLAY DENSITY".
- Copy is written for auditors, not staff: "Daily work stays on the surface while governed controls remain underneath." (`ShellRouteRegistry.cs:17`), "Business resolution never changes the underlying technical control result." (`:26`), "zero remains different from missing" (`DailyWorkflow.xaml:41`), "Source evidence and technical lineage remain available without leaving the report workspace." (`MainWindow.xaml.cs:114`).

### 4.3 Dates, culture, currency
- No culture is set for the UI: no `FrameworkElement.LanguageProperty.OverrideMetadata`, no `CultureInfo.CurrentCulture` assignment (`D/App.xaml.cs`), no `SelectedDateFormat`. Every `DatePicker` therefore renders `CultureInfo.CurrentCulture` short date — "9/13/2026" on an en-US Windows install, "13-09-2026" on en-IN — while every text label uses `dd MMM yyyy` (`TaskBodyLayout.cs:79`, `ReportWorkspaceControls.cs:81,275,372`, `DailySalesReportControls.cs:88,91`) except the DSR result line which uses `dd-MMM-yyyy` (`ReportsWorkspaceView.xaml.cs:262`).
- Auto-generated DataGrid columns show `DateTime` with time ("13/09/2026 00:00:00"), `DateOnly` in culture short form, decimals with four places and PascalCase headers ("TySales", "LyInvoices", "SourceSignedNetAmount", "CountedPhysicalQuantity") — 26 grids, no `DisplayName`/`StringFormat` anywhere.
- Numbers typed by staff are parsed with `CurrentCulture` (`DailyWorkflowPresentationSession.cs:100,137,177`, `RegistersWorkspaceView.xaml.cs:123`, `InvestigationApprovalsWorkspaceView.xaml.cs:81`) — correct only if Windows is en-IN.
- Currency: DSR uses `DsrDisplay.Currency` = `₹#,##0` with `en-IN` (lakh grouping, paise dropped, `DailySalesReportDocument.cs:128`) and `CompactCurrency` "₹1.23 L / ₹1.23 Cr" (`:133-140`); visual KPIs use `IndianNumberFormatter` `₹#,##0.00` (`VisualReporting.cs:40`); report status lines use `{x:N2}` with CurrentCulture (`ReportsWorkspaceView.xaml.cs:278,306,313`) and no ₹; grids show raw decimals; the DSR footer says "All amounts in INR" (`DailySalesReportControls.cs:91`). Three formats for the same rupee value on one screen (KPI card, status line, detail row).

---

## 5. Feedback and errors

**Channels.** (1) Footer `ApplicationStatus` — one trimmed line, `LiveSetting=Polite`, tooltip = own text, "Status details" dialog (`MainWindow.xaml:96`, `TaskNavigator.cs:88-91,130-133`). Every module's `NotificationRequested` and most shell messages land here (`MainWindow.xaml.cs:90,97,100,103,120,128,138`). (2) Per-view status TextBlocks (`WorkflowStatus/Message`, `ValidationResult`, `ReportResult`, `AccountingStatus`, …) — in focused mode these are ripped out and trimmed (§1.5). (3) `OpenDrawer` for "Access restricted" and row details (`MainWindow.xaml.cs:163`, `MainWindow.Shell.cs:267-286`). (4) `DashboardView.errorBanner` (red text only, `DashboardView.cs:46-47,109-110`). (5) `EmptyState` in report previews (`ReportWorkspaceControls.cs:204,225,387,417`). (6) `MessageBox` only for unhandled dispatcher exceptions with a generic sentence (`D/App.xaml.cs:46`). (7) `DraftNavigationDialog`, `StatusDetailsDialog`, `ReportScopeDialog`. There is no toast/snackbar; success and failure use the same TextBlock with the same style — only `WorkflowStatus` changes colour (`DailyWorkflowWorkspaceView.xaml.cs:324-329`).

**Progress.** Batch import: real `ProgressBar` + stage text (`ImportWorkspaceView.xaml:35`, `.cs:237-244`) and a Cancel button — the best in the app. Validation: text + disabled button only. Report load: `LoadingState` indeterminate bar (`UiControls.cs:93-103`). Pack generation / finalise / manual-input save: the *whole view* is disabled (`DailyWorkflowWorkspaceView.xaml.cs:133,305-313`) with "Working on the selected store and business date. Please wait…" in the banner — no bar, no cancel; on a slow DB the screen looks frozen. Exports (PDF/Excel/pack): nothing visible; the export buttons go disabled and closing the window is refused with a footer sentence (`TaskNavigator.cs:117-121`). Dashboard refresh, archive refresh, settings "Test connection": text only.

**Busy blocking.** `BlockWhileBusy` refuses *any* navigation while *any* of ten workspaces `IsBusy` (`TaskNavigator.cs:185-199`), the only feedback being a footer line. A tap on Home during an import appears to do nothing.

**Disabled-state explanations.** Export actions are disabled until a preview matches the current scope; the reason is shown only via the `EmptyState("Refresh required")` or when the keyboard shortcut is used (`TaskNavigator.cs:206`); the "Actions ▾" menu just greys them. `ReopenDayButton`/`ReopenReasonInput` disabled for non-owners with no text (`DailyWorkflowWorkspaceView.xaml.cs:127-128`). `ScopeSelector`/`DateFromPicker` disabled reasons are tooltips on disabled controls (§2). `PersistButton` and `FinaliseDayButton` are explained by adjacent text — good.

**Confirmations.** None before `Finalise day` (`DailyWorkflow.xaml:88` → `FinaliseDayAsync` runs immediately), `Reopen day`, `Reject and quarantine` (`SourceInbox.xaml:52`), `Waive` (`Operations.xaml:10`), `Cancel` batch, `Run automation now`, `Run checksum backup now`, `Run isolated recovery drill`. Unsaved-draft protection on navigation and close is thorough (`TaskNavigator.cs:221-290`) but produces up to seven sequential modal dialogs (one per draft type) when several drafts exist.

**Empty states.** Report previews: good, actionable. Every focused DataGrid: the same generic overlay "No rows to display. Refresh or adjust the task filters." (`TaskBodyLayout.cs:29`), including on the walk-ins grid where the right hint would be "No manual inputs saved for 13 Sep — enter walk-ins below". Dashboard chart: "No imported report activity yet." Favourites: text hint. Import diagnostics/batch grids: empty overlay with no instruction to validate first.

**Raw exception text.** `DesktopFriendlyError.Describe` (`D/DesktopFriendlyError.cs:18-29`) forwards `InvalidOperationException`/`ArgumentException` messages verbatim and any `SqlException` ≥ 51000 (`:25`) — RAISERROR text written for developers. Concrete user-visible strings: "A value is required. (Parameter 'reason')" (six repositories, §headline 6); "Enter a reason for the settings change. (Parameter 'reason')" (`ProductisationRepository.cs:52,150,190,260,267,390,403`, `SqlServerAccountingService.cs:264`) — the parameter suffix is appended by .NET to every `ArgumentException(message, paramName)`. Required fields are never validated in the view before the round-trip; the reason box is not marked required (only a tooltip says so, `DailyWorkflow.xaml:52`).

---

## 6. Accessibility

- **AutomationProperties.Name coverage is broad**: almost every input in XAML, all code-built tiles/cards/buttons, live regions on status lines (`LiveSetting=Polite` on 12 TextBlocks). Gaps: heading semantics — only one `HeadingLevel` in the whole app (`ReportPresentationControl.cs:59`); DataGrids announce PascalCase property names; `ContinueButton` OK; rail buttons OK.
- **Focus order**: `FocusedTaskLayout` re-parents controls so tab order follows the new visual order (good). `RememberContext` restores scroll/focus per route and otherwise focuses "Refresh Preview" or the first button (`TaskNavigator.cs:30-46`). The welcome overlay swallows every key and forces focus to Continue (`MainWindow.Shell.cs:294-299`). Drawer/search restore the previous focus (`:288`, `TaskNavigator.cs:162`).
- **F6** cycles exactly three regions — search box → store combo → workspace (`MainWindow.Shell.cs:372-376`, `KeyboardRegionNavigation.cs`) — skipping the rail, breadcrumb links, date picker and footer "Status details".
- **High contrast**: no `SystemParameters.HighContrast` handling, no `SystemColors` (grep 0). Button/TextBox/DatePicker have custom `ControlTemplate`s with fixed brushes (`Controls.xaml:33-50,104-118,125-134`), so Windows High Contrast themes cannot recolour them; the focus visual is a fixed dark/white double rectangle (`:7-9`).
- **DPI**: system-DPI aware only (no manifest); scaling works (WPF DIPs) but the fixed 1100/1000/780/700/300 px breakpoints and the 231 px chrome are in DIPs, so at 125 % the 1366 panel behaves like a 1093x582 window (§1.6).
- **Colour-only meaning**: growth green regardless of sign (§headline 7); store row bands `#EAF2FF`/`#F2ECFF` distinguish Titan/Helios by tint only (`DailySalesReportControls.cs:42`); `WorkflowStatus` tone is backed by the status word (OK); dashboard health metric colour is backed by text (OK); `errorBanner` is red text without icon or label. Contrast: `SecondaryText #65757A` on white 4.6:1 (pass), `#5D6873` 5.4:1, `#A76500` on `#FFF4DF` ≈ 4.4:1 (borderline for 12 px), NavigationMuted `#94B0B8` on `#062A36` ≈ 7:1.
- Font family "Segoe UI Variable Text/Display" (`Controls.xaml:14`, `Typography.xaml:3-4,9`) does not ship with Windows 10 (the owner's OS) — WPF falls back to Segoe UI; harmless but the `LineHeight` values were tuned for a font that is not there.
- All DSR text sizes below 12 are clamped to 12 (`DailySalesReportControls.cs:14`); 12 px is the floor across the app — small for a standing user at arm's length on a 1366 panel.

---

## 7. Startup

Sequence: `App.OnStartup` → `DesktopStartupCoordinator` → `MainWindow.Show()` with `WelcomeOverlay` at ZIndex 100 → `MainWindow_Loaded` → `RefreshAccessAsync` (DB round-trip) → `CompleteWelcomeState` enables "Continue" (`MainWindow.xaml.cs:143-151`, `MainWindow.Shell.cs:49-62`) → user taps Continue → `NavigateToDestination("Dashboard")` (`:67`).

- The overlay adds a mandatory tap on every launch to confirm a Windows identity the user already logged in with. Its text ("Continue securely with your Windows identity", "Your Windows identity and application role have been verified.") is compliance language. Value for a single-user shop PC: none; it should auto-dismiss once the role is known and only stay when `Role == None` (database setup required).
- The first working screen ("Today overview") is an operator dashboard (§headline 5). The DSR — the artefact the owner actually reads each morning — is three taps away and auto-runs only when reached.
- `PageTitle` for Home is "Owner Workspace" for owners and "Home" for others (`MainWindow.Shell.cs:91`); breadcrumb says "Modules"; rail tooltip says "Modules Home"; the footer says "Signed in as … — Owner." — four labels for one place.
- The Home grid for an Owner shows nine tiles including Registers, Approvals and System Health (`UiNavigation.cs:39-47`; `DefaultVisibility`/`PinAllowed` are never read — `IsVisibleTo` checks role only, `:17`) so the "surface" is the full admin surface.

---

## 8. Dead code and duplication

**Legacy panel stack vs `FocusedWorkspaceLayer`.** `LegacyWorkspaceScroll`/`WorkspaceStack` and the twelve hosted `*Panel` Borders (`D/MainWindow.xaml:50-92`) are shown only when `ApplyNavigationDecision` falls through `DisplayTaskRoute` (`MainWindow.xaml.cs:173-176`), which happens solely for the no-role "Settings" route (`MainWindow.Shell.cs:149-157` → `:155`). Every other route is `overview:`/`category:`/task and hides the legacy layer (`TaskNavigator.cs:308,354,416`; `Workspaces.cs:39,114`). Consequences: `ReadinessSummaryPanel` + `ConnectionStatus`/`ImportStatus` (with their `AutomationProperties` updates at `MainWindow.xaml.cs:121-125,256-261`) are never visible (`UpdateShellForDestination` collapses it, `MainWindow.Shell.cs:164`); `GettingStartedPanel`, `WorkspaceHeading`, `WorkspaceMessage`, `PrimaryAction` (`MainWindow.xaml.cs:179-183`) are written for nothing; `DashboardPanel` visibility toggling in `RefreshAccessAsync` (`:222,232`) is moot; `HideAllFeaturePanels` (`MainWindow.Shell.cs:391-394`) hides panels that are already hidden; `ShellRouteDescriptor.Heading/Message/ActionLabel/ActionDestination` (`ShellRouteRegistry.cs:17-30`) have no live consumer except that one path. The XAML-hosted views are detached from their `ContentControl` hosts on first use (`TaskNavigator.cs:415`).

**ContextSidebar** — dead (§3.5): `SidebarModuleTitle/Subtitle`, `SidebarSearchInput`, `SidebarItemsPanel`, `PopulateSidebar`, `SidebarItem_Click`, `SidebarSearch_TextChanged`, `ShowSidebar`, `ToggleSidebar_Click`, `sidebarOverlay`, `SidebarToggleButton`, `SidebarItemButton` style.

**Two (really six) navigation registries.** `UiNavigationRegistry` (modules/groups/items for the dead sidebar, `D/UiNavigation.cs:35-135`) is transformed by ~40 lines of id/category/destination rewrites into `TaskNavigation.All` (`D/TaskNavigation.cs:52-101`) plus 12 hand-added tasks (`:104-117`); `ShellRouteRegistry` (14 destinations, `ShellRouteRegistry.cs`); `WorkspaceModuleOwnershipRegistry` (`Shell/Navigation/WorkspaceModuleOwnershipRegistry.cs`, no reference outside itself); `ReportWorkspaceRegistry` (7 category workspaces, `ReportWorkspaceControls.cs:35-71`); `ReportTaskAliases`; `ReportsWorkspaceView.xaml:14-22` (a fourth, hand-written 34-button catalogue used only as the hidden report engine); `WorkspaceLocation`/`WorkspaceNavigationHistory` "compatibility types retained until MainWindow adopts IShellNavigationService" (`WorkspaceRoute.cs:8-47`) — MainWindow already uses `ShellViewModel`/`ShellNavigationService`; `ShellPresentationMetadata` is a field-for-field copy of `ShellRouteDescriptor` whose `CurrentPresentation` property is read by nobody.

**`ShellRouteRegistry` aliases.** "Stock Reports": `ShowFocusedReportWorkspace` computes it (`Workspaces.cs:26`) and never uses it; every report task is built with `destination = "Sales Reports"` (`TaskNavigation.cs:72`), `WorkspaceModuleOwnershipRegistry.Report()` likewise (`:85-86`) — unreachable. "Masters": mapped to "Settings" on entry (`MainWindow.Shell.cs:151`); only `MastersPanel` (dead) references it. "Admin / Settings" vs "Settings": both map to module "settings"; the rail Settings button is tagged "Admin / Settings" (`MainWindow.xaml:26`) then rewritten; `RefreshCurrentWorkspace` distinguishes them (`MainWindow.Shell.cs:367`) for a panel that is not shown. `NavigateToDestinationWithFeature` (`:143-158`) is a translation table between all of these.

**DashboardView.** `BuildDailyCloseCard`, `BuildReadinessCard`, `BuildGlanceCard`, `BuildSystemCard` and helpers `TaskCard`, `DarkMetric`, `StatusRow` (`DashboardView.cs:191-268,366-408`) are never called; with them die `completedBatchesMetric`, `failedImportsMetric`, `latestImportMetric`, `databaseSizeMetric`, `backupSpaceMetric`, `healthWarningsList`, `dailyCloseMessage` (all still assigned in `Apply`, `:88-123`).

**UiControls.cs.** `ModuleTile`, `StatusBadge`, `DetailDrawer` (`:10-67,69-78,105-131`) have no `new` anywhere; `MainWindow.xaml` implements its own drawer (`:98`).

**Report controls.** `ReportWorkspaceControl.reportMenu`/`BuildNavigation`/`BuildReportMenu`/`ReportSelected` — column width 0 and `Collapsed` (`ReportWorkspaceControls.cs:116,118,209-251`); `reportTitle` collapsed (`:146`); `DailySalesReportWorkspace.titleRow` + "Back to Reports" collapsed (`:337-341`, so `ReportWorkspaceAction.BackToReports` is unreachable); `availabilityPanel` collapsed (`:441`) so `UpdateAvailability`'s badges (`:393-412`) are built for nothing; `DailySalesReportView` header/footer rows are discarded by `DailySalesFocusedView` (`:11-12`).

**Other.** `NavigationButton` style (`MainWindow.xaml:10-16`) has no consumer; `HelpCentreView` "← Help categories" buttons are `Collapsed` (`HelpCentreControls.cs:162,200`); `ModuleDefinition.DefaultVisibility/PinAllowed/StatusDetail` unread; `UiPreferences.PinnedModuleIds` only reorders Home tiles; `BreadcrumbText` never visible (§3.3); `AccessStatus` `Collapsed` (`MainWindow.xaml:45`) yet updated with colour (`MainWindow.xaml.cs:220-221,230-231`); `Card` style defined twice (theme + window).

---

## 9. Prioritised UI changes (S ≤ ½ day, M 1-3 days, L > 3 days)

| # | Change | Sev | Effort |
|---|---|---|---|
| 1 | Collapse shell chrome to one 48 px header row (section title · date · store) and a 28 px footer; move search behind the rail icon (popup); make breadcrumb links `QuietButton` 32 px or replace with a single "← Back". Target: ≥ 610 px content at 1366x728. | Critical | M |
| 2 | Remove `NoWrap`/ellipsis from status/result lines in `TaskBodyLayout.cs:21` and `ReportWorkspaceControls.cs:266,276,443`; wrap to ≤ 3 lines with a "More" link to the details dialog. | High | S |
| 3 | DSR: one non-scrolling screen — KPI strip (6-up) + both store period tables side by side + service/targets in a third column; drop the duplicate period/footer lines from the toolbar; make "Export PDF" a visible primary button (not inside "Actions ▾"). | Critical | M |
| 4 | Welcome overlay: auto-continue when a role is resolved; keep it only for `Role == None`. Land on Today (DSR for the header date). | High | S |
| 5 | Required-field validation in the presentation sessions before any DB call; strip the " (Parameter 'x')" suffix in `DesktopFriendlyError.Describe` (`ArgumentException` → message only); map RAISERROR ≥ 51000 to plain sentences. Mark required boxes visibly ("Reason *"). | High | S |
| 6 | Fix growth colouring to sign-based (`DailySalesReportControls.cs:48,60`); use `Success`/`Critical` resources. | High | S |
| 7 | Touch styles: `ComboBoxItem` MinHeight 44, `RadioButton` 44, `ScrollBar` 24 px wide with 44 px thumb min, `ToolTipService.ShowOnDisabled` or inline help text, `InputScope=Number` on numeric TextBoxes, rail labels under icons + selected state. | High | S-M |
| 8 | Replace the three-level tile drill with the 5-section IA below; delete overview/category routes (`ShowTaskOverview`, `ShowReportCategories`) and the dashboard "Daily close" button card. | High | L |
| 9 | Friendly grids: explicit column definitions (or `[DisplayName]` + `StringFormat`) for the DSR rows, import diagnostics/results, manual inputs, stock counts, daily pack; `dd MMM yyyy` dates, ₹ with lakh grouping, right-aligned numbers, 2 dp. | High | M-L |
| 10 | One scope: header date + store drive every task two-way (bind, don't copy-once); remove the per-task pickers for walk-ins/import/registers or show them read-only with "Change" opening the header picker. Unify store vocabulary to "Titan World" / "Helios" / "Both stores". | High | M |
| 11 | Confirmation sheet for Finalise day, Reopen day, Reject & quarantine, Waive, Cancel batch, Run backup/recovery/automation now. | Medium | S |
| 12 | Set UI culture to `en-IN` at startup (`FrameworkElement.LanguageProperty` override + `CultureInfo.DefaultThreadCurrentCulture`), `SelectedDateFormat=Short` gives `dd-MM-yyyy`; standardise all text dates on `dd MMM yyyy`. | Medium | S |
| 13 | Progress + cancel for pack generation, finalise and exports (indeterminate bar in the action toolbar, `CancellationToken` plumbed); toast for success. | Medium | M |
| 14 | Theme consolidation: delete `DashboardView` brushes, `DsrUi.Brush` literals, `ReportWorkspaceControls` literals, `MainWindow.xaml` `Card`/`NavigationButton`; route chart colours through theme resources; use `Typography.xaml` styles for all headings. | Medium | M |
| 15 | Delete dead code listed in §8 (legacy panels, sidebar, dashboard cards, `ModuleTile`/`StatusBadge`/`DetailDrawer`, report menu, availability panel, `WorkspaceModuleOwnershipRegistry`, `WorkspaceLocation`/`History`, `ShellPresentationMetadata`, "Stock Reports"/"Masters" routes, `BreadcrumbText`, `AccessStatus`); collapse the registries to `TaskNavigation` + a small route table. | Medium | M |
| 16 | Add a PerMonitorV2 DPI manifest; convert the 1100/1000/780/700/300 px breakpoints to a single "compact below 1000 DIP" rule; test at 125 %. | Medium | S |
| 17 | Add a "Touch" density (44 px targets, 8 px paddings) as the default on the shop PC; keep Comfortable 48 for the office desktop. | Medium | S |
| 18 | Reword all user-facing copy for staff (≤ 12 words, imperative, no "governed/immutable/canonical/lineage"); sentence-case all buttons; one spelling (Centre). | Medium | M |
| 19 | Empty states per grid ("No walk-ins saved for 13 Sep — enter them below"); replace the generic overlay text in `TaskBodyLayout.cs:29`. | Low | S |
| 20 | F6 to include rail and footer; add `HeadingLevel` to page and card titles; high-contrast trigger on the three custom templates. | Low | S |

## 10. Proposed two-level IA for shop staff

Level 1 = five large labelled rail/tab buttons, always visible, badge for attention counts. Level 2 = tabs inside the section (no tiles, no categories). Owner-only items are hidden for Store Manager/Viewer, not greyed. The header carries only the section title, business date and store.

| Level 1 | Level 2 tabs | Existing screens mapped (task ids from `TaskNavigation`) |
|---|---|---|
| **Today** (landing) | Sales (DSR) · Cash · Walk-ins · Close day | `report-dsr` (`DailySalesReportWorkspace`, auto-run for the header date, export PDF/WhatsApp as primary buttons) · `report-cash` Daily Cash Reconciliation + `register-expense` entry · `walk-ins` (manual section of `DailyWorkflowWorkspaceView`, walk-ins field pre-selected, reason optional/defaulted "Daily count") · `readiness` + `finalisation` + `store-daily-pack`/`combined-pack` (readiness banner, Finalise, Generate pack) |
| **Import** | Import files · Source inbox · Problems | `import-files` (store/date from header, Browse + Validate + Import as one primary flow, batch progress) · `source-inbox`, `documents`, `native-pdf`, `ocr-review`, `extraction-history` · `import-history`, `quarantine`, `duplicates`, `already-present`, `conflicts`, `import-failures`, `unknown-layouts` (one grid with a status filter) |
| **Reports** | Sales · Staff · Tender & Service · Exceptions · Management · Packs & Archive · Favourites | `report-sales-titan/-helios/-combined/-brand/-segment/-item/-returns`, `report-invoice` · `report-staff` (+ `staff-target` entry as a button) · `report-tender`, `report-tender-diagnostic`, `report-service`, `report-cash` · `report-exceptions`, `report-exception-*` · `report-management-trend`, `report-invoice-lineage`, `trends` · `generations`, `final-packs`, `restatements`, `compare`, `re-export`, `shared`, `historical-packs`, `sharing-contacts` · `favourite-reports` |
| **Stock** | Closing stock · Brand stock · Physical count · Variance · Movement · Slow stock | `report-stock-closing` · `report-stock-brand` · `stock-count` (entry, from `DailyWorkflowWorkspaceView` stock section) + `report-stock-physical`/`report-stock-group` · `report-stock-variance` · `report-stock-movement` · `report-stock-slow` |
| **Settings** (Owner; Store Manager sees Display only) | Display · Database · Users · Stores & masters · Integrations · Accounting · Control centre · Registers · Help | `settings` (density, favourites) · `connection`, `health`, `backups`, `recovery`, `support-package`, `audit` · `users`, `profiles` · `stores`, `masters`, `kpi`, `tender-rules` · `watch-folder`, `scheduler`, `ocr`, `sharing`, `sharing-contacts` · whole Accounting module (`prepare-batch`, `ledger-mapping`, `mapping-review`, `validation`, `tally-export`, `export-history`, `accounting-reconciliation`, `accounting-approval`) · `open-items`, `data-quality`, `approval-centre`, `adjustment`, `investigation` (with an attention badge surfaced on Today) · `register-inward/-outward/-credit/-service/-transfer/-vendor` (+ `register-courier` when configured) · `help:*` topics, keyboard shortcuts, profile |

Removed as navigation surfaces: Home module grid, module/category overview tiles, ContextSidebar, master search (kept as a Settings › Help "Find a screen" box), Profile rail icon (identity moves to the Settings header), the "Dashboard" IT overview (its SQL/backup facts become a one-line health strip under Settings › Database and an attention badge on Today only when Critical).
