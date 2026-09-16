# Phase 3 — Claude audit

Auditor: Claude. Date: 16 September 2026. Branch: `phase-3/touch-shell` at `12f108a` (pushed, matches origin).
Method: clean Release build and full test run from an isolated worktree, then the shell driven natively on the 1366x768 screen against a disposable database holding the real two-store data, with UI Automation used to measure control heights and detect scrollbars. Codex's evidence was rendered offscreen; mine is the native maximised window.
The live `EtpReporting` database was never written to and `settings.json` was never modified.

---

## 1. Verdict

**PHASE 3 REOPENED** — the shell is genuinely rebuilt and most of it is good, but the Reports list scrolls at 1366x768 with real data, the content area is 6 DIP short of the budget, the status footer clips instead of wrapping, one item on the explicit deletion list survives, and the header date reverts to US format after a typed edit.

Codex's own report says Phase 3 cannot close without native acceptance. That was the right call, and the native run is what found these.

---

## 2. Acceptance A3.1 – A3.8

| ID | Result | Evidence |
|---|---|---|
| **A3.1** DSR in 0 taps; import and walk-ins in 2 | **PASS** | Launching the app lands directly on Today → Sales with the DSR rendered. No Continue click: the welcome overlay auto-dismisses once a role resolves and now only survives for the database-setup case, which the plan allows. Import is rail → section (the OS folder chooser is extra, as Codex notes). Walk-ins is rail → tab. |
| **A3.2** Six screens without scrollbars at 1366x768 | **FAIL on one of six** | Measured natively with real data. Today/Sales **0 scrollbars**, Walk-ins **0**, Import **0**, Stock **0**, Settings **0**. **Reports list: 1 visible scrollbar, 24x580 at the right edge.** Codex's synthetic fixtures did not show this; the real report catalogue is longer than the fixture. Screenshots `p3-*.png`. |
| **A3.3** Compact and 125% DPI reachable | **NOT VERIFIED** | Codex rendered at 816x440 and at a 1.25 bitmap scale, which simulates size but is not Windows DPI. I did not change the monitor DPI either. This needs a deliberate DPI session and remains open. |
| **A3.4** No interactive control under 44 px | **PASS** | UI Automation sweep of every on-screen Button, ComboBox, Edit, CheckBox, RadioButton, TabItem and ListItem across all six screens. Controls under 44 px: **only the Windows title-bar Minimize/Restore/Close at 14 px** (OS chrome, outside the rule) and **three 17 px scrollbar arrows** on the Reports screen, which fall under the plan's own 24 px scrollbar exception. Every application control measured 44 or more. Today/Sales: 20 controls, 0 under 44. |
| **A3.5** Header date carries into Walk-ins | **PASS** | Setting the header to 25 Aug 2026 moved the DSR to that date and the period line read "FTD 25 Aug 2026 · MTD 01 Aug–25 Aug 2026". Codex's test also covers a revisited editor. |
| **A3.6** Blank walk-ins shows inline text, never a parameter name | **PASS** | Asserted in `Phase3ShellTests` with the exact string "Enter the walk-in count", raised before the command service is constructed, and `DesktopFriendlyError` strips the trailing `(Parameter …)` suffix. |
| **A3.7** No user-facing "governed/canonical/lineage/immutable" | **PASS** | The plan's exact grep returns four hits, none in XAML: a Help **search keyword**, a report-code `switch` arm, an XML doc comment and a dictionary key. None is rendered. |
| **A3.8** Timed touch-only walkthrough under three minutes | **NOT RUN** | Needs a member of shop staff and a touch device. Codex says so plainly. This is the one criterion neither of us can close from a keyboard. |

---

## 3. Chrome budget — FAIL by 6 DIP

The plan allows one 48 px header and a 28 px footer, and requires at least 610 px of content at 1366x728.

The window has **three** chrome rows, not two: header 48, an unbudgeted section-tab row, and footer 28. The tab row inherits the 44 px touch target plus 4 px of margin, so it is about 48. Content is therefore **728 − 48 − 48 − 28 = 604 DIP**, six short.

Codex states this figure itself and argues the target is met "for the section canvas, not for the focused task host". That is an honest framing of a miss. Either the tab row counts as chrome and the budget is exceeded, or the plan should be amended to allow a third row. My reading is that a row of tabs the user cannot remove is chrome.

---

## 4. Defects returned to Codex

**P3-1 — The Reports list scrolls at 1366x768 (A3.2).** One visible 24x580 scrollbar with the real catalogue. *Fixed looks like:* the report list fits at full screen in Touch density with the real catalogue, or the plan's A3.2 is amended to exclude it with a reason.

**P3-2 — Content area is 604 DIP against a 610 target.** *Fixed looks like:* recover 6 DIP, most obviously by folding the section tabs into the 48 px header row or trimming the tab row's margin, or get the plan amended.

**P3-3 — The status footer clips instead of wrapping to three lines.** The footer is a fixed 28 DIP row holding a `TextWrapping="Wrap"` block with no line cap, so a long message shows roughly one line and the rest is cut. The plan asks for up to three lines with a "More" affordance. The "More details" affordance exists and the toast does cap at three lines; the footer does not. *Fixed looks like:* the footer grows to at most three lines, or shows one line plus a visible count of what is hidden.

**P3-4 — `DetailDrawer` survives.** It is on the plan's explicit deletion list, but `DetailDrawerHost` is still in the main window and still opened from the profile handler. Codex's report states the dead tile and drawer classes were removed. *Fixed looks like:* delete it, or get it struck from the deletion list with a reason.

**P3-5 — The header date reverts to US format after a typed edit.** At startup it reads "16 Sep 2026". After typing a date and pressing Enter it reads **"8/25/2026"** and stays that way. The rest of the screen still formats correctly. Task 9 asks for `dd MMM yyyy` everywhere. *Fixed looks like:* the date box re-applies the Indian format after every edit, with a test that types a date and asserts the displayed text.

**P3-6 — A control-count assertion was added.** `Phase3ShellTests` asserts exactly five rail buttons. Plan rule 3 bans tests that count controls, and Phase 0 spent effort removing exactly this pattern. *Fixed looks like:* assert on the section names from the navigation table instead.

**P3-7 — Three or four numeric inputs have no number input scope.** The scope is applied by a name-matching regex in the global style, so anything outside the vocabulary silently loses the touch keypad. The clearest miss is the box labelled "Staff CRO number"; the schedule time and contact phone boxes are also affected. *Fixed looks like:* set the scope explicitly on those controls, and prefer an explicit property over name matching for new ones.

**P3-8 — Mixed spelling in user-facing Help.** One Help sentence contains both "Operations Center" and "Approval Centre". Task 9 asks for one spelling.

**P3-9 — Commit granularity.** One commit, 79 files, +1539/−1676. Same point as Phase 2.

Lower priority: the density enum is still named `Comfortable`/`Compact` in code and in the persisted preferences file though the labels read Touch and Desktop; `HeadingLevel` is set on three elements only, not on the DSR card titles; three navigation registries remain where the plan wanted one table plus a small route map.

---

## 5. What is genuinely good

Worth recording, because the deletion list was long and most of it was honoured. Every other named item is gone: the task overview, report categories, the legacy scroll panel and its twelve hosts, the context sidebar, breadcrumbs, access status, module tiles, status badges, the ownership, location, navigation-history and presentation-metadata registries, and the "Stock Reports" and "Masters" routes, with a negative test asserting the routes stay unreachable. The rail is five labelled always-visible sections with a selected state. `en-IN` is set for both UI culture and number parsing at startup, and the DSR renders lakh grouping correctly. All seven destructive actions are behind a confirmation sheet. F6 reaches the rail and the footer. The manifest declares PerMonitorV2. One breakpoint at 1000 DIP, used consistently in four places.

Build and tests from clean: **0 warnings, 0 errors**, and **680 tests passed, 0 failed, 0 skipped**, matching Codex's claim exactly.

---

## 6. Merge topology — read this before merging

`phase-3/touch-shell` is **37 commits ahead of `main`** and contains the whole of Phase 1 and Phase 2. Merging Phase 3 merges Phase 2 with it, including Phase 2's open acceptance items. Codex's report notes Phase 2 items remain open but never says that merging Phase 3 lands them.

**Treat Phase 2 closure as a hard precondition for any Phase 3 merge.**

---

## 7. Cleanup

Audit database `EtpPhase1Test_ClaudeP3` created and dropped. Live `EtpReporting` unchanged at 490 invoices and 16 migrations. `settings.json` unchanged (SHA-256 `5A58FC54…`); the app ran on an explicit `--connection-string`. My application instance was closed. No source, test or migration file was modified.

---

## 8. Addendum — verification completed 16 September 2026

**A3.3 is now half closed, and the half I ran passes.**

I restored the window to a literal 816x480 and measured Today → Sales with real data. The five rail sections stay visible at full size, the action buttons wrap onto a second row rather than disappearing, a vertical scrollbar appears in the content area, and nothing is clipped or hidden. That is exactly what the criterion asks for: scroll appears, everything stays reachable.

Interactive controls under 44 px at compact size: **two**, both scrollbar arrow buttons at 17 px, which fall under the plan's own 24 px scrollbar exception. Every other control held its touch target as the window shrank.

Screenshot `p3-compact-sales.png`.

**The 125% DPI half remains unverified.** Changing the display scaling affects the whole desktop and, on this Windows build, wants a sign-out to apply cleanly to a running application. I did not want to disturb the machine mid-session for it. It is a five-minute check whenever the screen is free, and it is the only part of A3.3 still open.

**P3-5 confirmed again, and it is wider than the header.** The date-format regression is not limited to the shell header. The cash book's own From and To pickers also render `8/1/2026` and `8/25/2026` rather than `01 Aug 2026` and `25 Aug 2026`. So the US format appears on report date controls as well, not only after a typed edit in the header. The fix needs to cover every date picker, not just the one in the header.

**Unchanged:** the five items returned in section 4 all still stand. Nothing in this pass moved A3.2, the chrome budget, the footer, the detail drawer or the control-count test.

---

## 9. Re-audit of the integration fixes — 16 September 2026

Branch `integration/phase-2-3-4-fixes`, HEAD `123e1d4`, clean tree. This pass was done against the **running native application**, not a render harness: the real `Etp.Reporting.Desktop.exe` Release build launched with `--connection-string` against a disposable synthetic database, driven and measured through UI Automation, and captured with `Graphics.CopyFromScreen`.

### Native capture worked for me

Codex's report states native capture failed with `SetIsBorderRequired failed: No such interface supported (0x80004002)` and that its images are WPF client-area renders at 1366x728 and 816x440, not native screenshots. That caveat was correct and honestly made. It is a limitation of the computer-use screenshot API, **not of native capture generally** — a plain `System.Drawing` screen copy works on this machine, and the four images added in this commit with the `reaudit-` prefix are genuine native screenshots of the running application at a real 1366x768 screen. I did not count Codex's images as evidence for anything; every figure below is my own measurement.

### Verdict

**PHASE 3 CLOSED on coding, with one item returned to the plan rather than to Codex.** Eight of eight returned defects are fixed. P3-3 is fixed as a defect but does not implement the plan's wording, and the conflict is in the plan itself — see below. A3.3's 125% DPI half is still unverified, for the same reason as last time.

### Returned defects

| Item | Status | Evidence |
|---|---|---|
| P3-1 Reports list scrolls at 1366x768 | **PASS** | Maximized on the real 1366x768 screen with the **real** catalogue: UI Automation reports **0 visible scrollbars**, 23 reports laid out in three columns with roughly 200 DIP of empty space below. Screenshot `reaudit-reports-catalogue-1366x768-native.png`. Previously I measured one 24x580 scrollbar here. |
| P3-2 content area 604 DIP against a 610 target | **PASS** | Section tabs now share the header band — visible in the captures, and confirmed by measurement: the header occupies a single 44 DIP row. Maximized, window DPI 97, client **1351.9 x 697.7 DIP**, content workspace **1240 x 622 DIP**. The plan's target is stated "at 1366x728", which is the basis Codex measured on and where the figure is 652; on the stricter real maximized client it is **622**. Both clear 610, so the criterion holds on either basis. |
| P3-3 status footer clips instead of wrapping | **Partial — returned to the plan, not to Codex** | See below. |
| P3-4 `DetailDrawer` survives | **PASS** | `grep -rn "DetailDrawer\|DetailDrawerHost" src/` over non-build paths returns nothing. Deleted. |
| P3-5 dates revert to US format | **PASS** | Verified by actually typing, which is the evidence Codex could not produce. Header: typed `25 Aug 2026`, pressed Enter, field reads back `25 Aug 2026`. Cash Book report pickers: typed `03 Aug 2026` into "Report start date", reads back `03 Aug 2026`; both pickers already held `01 Aug 2026` / `25 Aug 2026` on load. Previously these showed `8/25/2026` and `8/1/2026`. |
| P3-6 control-count assertion | **PASS** | The "exactly five rail buttons" assertion is gone, replaced by a comparison of section names against `TaskNavigation.Sections`. One count assertion remains at `Phase3ShellTests.cs:153` — `Assert.Equal(3, grid.Columns.Count)` — but that asserts an explicit column contract on a DataGrid, not a count of shell controls, and it is paired with `Assert.False(grid.AutoGenerateColumns)`. I am not returning it. |
| P3-7 numeric inputs without a number input scope | **PASS** | All four named inputs now set it explicitly: `StaffTargetCroInput` (`DailyWorkflowWorkspaceView.xaml:77`), `ScheduleTimeInput` (`OperationsWorkspaceView.xaml:15`), `SharePhoneInput` and `ContactPhoneInput` (`ArchiveWorkspaceView.xaml:38,56`). `EveningMastersView` and `BrandStockEntryWindow` set it programmatically for new fields. The name-matching fallback in `Controls.xaml.cs:17` remains as a backstop rather than the only mechanism. |
| P3-8 mixed Centre/Center spelling | **PASS** | The displayed Help sentence now reads "Open **Operations Centre** and choose Open Items, Data Quality or **Approval Centre**". Five `"Operations Center"` strings remain in `HelpCentre.cs` and `MainWindow.Shell.cs`, but they are internal destination keys feeding a route lookup, not text shown to staff. Worth normalising one day so nobody ever renders one; not a defect now. |
| Lower priority: density enum | **PASS** | `enum UiDensity { Touch, Desktop }`. `UiDensityJsonConverter` accepts legacy `"Comfortable"` to Touch, `"Compact"` to Desktop and numeric `0`/`1` positionally, which matches the old member order, and writes the new names. `UiPreferences.Default` is **Touch**, so a fresh shop PC gets 44 DIP. |

### P3-3: the footer is fixed as a defect but does not meet the plan's words

What Codex built: `ApplicationStatus` is `TextWrapping="NoWrap"` with `TextTrimming="CharacterEllipsis"` in a fixed 28 DIP row. `UpdateStatusOverflow()` (`MainWindow.Shell.cs:160-169`) measures the message as if wrapped at the available width, computes `hidden = ceil(height / 16) - 1`, and shows "N more lines · More details" beside it.

So the original defect is genuinely gone: nothing is silently cut any more, the count is honest, and the full text is one click away. But the plan's task 8 says status messages "**wrap (max 3 lines**, 'More' opens details)", and this does not wrap at all — it shows one line. The toast does comply: `ToastMessage` is `TextWrapping="Wrap"` with `MaxHeight="60"`, about three lines at a 16 DIP line height.

The reason I am returning this to the plan rather than to Codex is that tasks 2 and 8 are in tension, and the numbers decide it:

- A three-line footer needs roughly 52 DIP instead of 28 — about **24 DIP** more chrome.
- On the plan's stated basis (1366x728), content would fall from 652 to about **628 DIP** — still above the 610 floor. Affordable.
- On the real maximized client I measured (697.7 DIP), content would fall from 622 to about **598 DIP** — **below 610**. Not affordable.

So the plan's own two rules can both be satisfied only on the plan's own measurement basis, and not on the window the shop actually gets. That is Sagar's call, and it is a small one: either amend task 8 to accept one line plus a hidden-line count plus More in the footer (keeping the three-line wrap for toasts, where it already works), or accept a shorter content area. I recommend the former — it is what is built, it loses no information, and it protects the content budget.

### Acceptance

| ID | Status | Basis |
|---|---|---|
| A3.1 | **PASS** | Today is the landing section with Sales as its first tab; Import is rail then "Import today's folder" (2 taps); Walk-ins is rail then tab (2 taps). |
| A3.2 | **PASS** | Measured natively, maximized at 1366x768, **0 visible scrollbars** on all six named screens: Today/Sales, Today/Walk-ins, Import, Reports list, Stock, Settings. An early reading of 1 on "Today" was a stale Cash detail grid from my own prior navigation, not the Sales tab; re-checked per tab, all four Today tabs report 0. |
| A3.3 | **PARTIAL** | 816x480 half **passes**: window driven to a literal 816x480 (outer), client 791.8 x 436.5 DIP. Cash Book over a 25-day range keeps a complete detail row visible above the horizontal scrollbar with search, variance and "Open selected row details" all reachable; Customer-wise Invoices and Staff/CRO show their rows with no vertical scroll. **125% DPI half still not run** — see limitation below. |
| A3.4 | **PASS at the screens I measured** | UI Automation dump reported **0 interactive controls under 44 DIP** at maximized size. Worth noting this ran at **Desktop** density, where `ActiveTargetHeight` is a 36 DIP floor — the stricter case — and controls still rendered at 44-45 DIP. I measured Import, Reports, Today and Cash, not literally every screen. |
| A3.5 | **PASS** | Header date changes propagate; the Cash Book picked up the header scope and reported "Scope changed — refresh the preview" rather than silently showing stale data. |
| A3.6 | **NOT RE-RUN** | Unchanged from the earlier pass; no code in this area moved. |
| A3.7 | **PASS on intent** | The grep is not empty — four hits — but none is displayed text: `"invoice-lineage"` is a route id in `ReportsWorkspaceView.xaml.cs:120` and `ReportTaskAliases.cs:12` (whose *displayed* name is "Invoice Source Drill-down"), and "lineage" appears once as a Help **search keyword** in `HelpCentre.cs:97`. Nothing banned is rendered; the DSR and report captures confirm it. |
| A3.8 | **NOT RUN — manual acceptance** | The timed touch walkthrough needs a person at the shop PC. It is not something I can execute. |

### Honest limitation: 125% DPI, again

I did not change display scaling, and I want to be precise about why rather than repeat last time's sentence. Two reasons. First, it is a whole-desktop change that on this Windows build wants a sign-out to apply cleanly to a running app. Second — and decisive this time — **the owner was at the keyboard while I worked**: a voice conversation was open in another application partway through this pass, which I could see in my own screen captures. Changing display scaling out from under someone mid-conversation is not a reasonable thing to do for a test I can run any time the machine is free.

It remains a five-minute check: set 125%, sign out and in, launch, confirm nothing is clipped and every control stays reachable. It is the only part of A3.3 outstanding, and the last coding-side unknown in Phase 3.

### Side effect I caused, disclosed

Launching the app rewrote `%LOCALAPPDATA%\EtpReporting\ui-preferences.json` (SHA-256 `4E40CA7F...` to `9F803438...`). `settings.json` is **unchanged** (`5A58FC54...`), because `--connection-string` uses a temporary connection that does not persist.

I did not back the preferences file up first, which I should have. The effective setting is unchanged: I never touched the density control, so the app wrote back whatever it loaded, and it wrote `"Desktop"` — meaning the stored value already mapped to Desktop. What changed is the spelling, normalised from the legacy name to the current one by the converter. The file now reads `{"Density":"Desktop","PinnedModuleIds":[],"FavouriteReportCodes":["dsr","stock-closing","staff"]}`.

That surfaces something worth acting on: the shop PC is on **Desktop** density (36 DIP floor), while the plan's task 7 wants **Touch** as the default there and the app's own fresh-profile default is Touch. It is a two-second change in Settings → Display density, and it is worth doing before staff use the machine.
