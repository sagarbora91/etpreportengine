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
