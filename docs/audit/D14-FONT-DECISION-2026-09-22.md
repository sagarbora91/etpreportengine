# D14 — PDF font: accept Windows' Segoe UI, or bundle a font?

- Date: 22 September 2026
- Status: **waiting for Sagar's decision**
- Worktree `C:\Codex\Reporting Manger\opus-recovery`, branch `recovery/opus-r1-r4`, HEAD `375e3b2`
- Origin: audit finding D14 (Medium) in `docs/audit/claude-audit-2026-09/01-reports-engine.md:36`, still open in `docs/audit/WORKING-STATE-2026-09-22.md:62`
- Line numbers refer to HEAD `375e3b2`.
- No product code, database, task or setting was changed to write this. Everything below was checked by reading code, measuring the font files on this PC, reading the PDFsharp 6.2.4 source, and running the real PDFsharp 6.2.4 library in a scratch PowerShell session. The app was not opened and nothing was built.

## The short version

**Recommendation: option A.** Keep using Windows' own Segoe UI. Microsoft's Windows 10 and Windows 11 font lists put it in the standard desktop font set, not in an optional package, and Microsoft's licence does not allow us to ship the Segoe UI file with the app (without buying extra rights from Monotype). **But fix how a missing font is reported first.** Today the failure does not say "font". The screen shows a generic error, and the log records only a `NullReferenceException`. That fix is small and does not touch the layout.

Two things matter more than the headline question:

1. **This is not only the DSR.** The same font code serves **every PDF the product makes**: the DSR, report PDFs, the management summary, and report packs, including the packs the unattended automation writes. (The product registers the automation task to run every five minutes, `scripts/install-etp-automation-task.ps1`; each run writes a pack only after an automatic import or for a scheduled pack that is due.)
2. **The current "fail clearly" design does not work.** The code throws a clear message ("Segoe UI is required for DSR PDF export."). PDFsharp swallows it, then fails further on with an unrelated error (section 1.3).

---

## 1. What the code does today

### 1.1 Where fonts are loaded

The PDF library is **PDFsharp 6.2.4**, Core build: package `PDFsharp`, `src/Etp.Reporting.Reporting/Etp.Reporting.Reporting.csproj:11`. The Core build does not look up Windows fonts by itself. PDFsharp's "use Windows fonts" switch (`GlobalFontSettings.UseWindowsFontsUnderWindows`) is off by default, and the code never turns it on. Every font must therefore come from the app's own font resolver.

| What | Where | What it does |
|---|---|---|
| The only code that reads a font file | `DsrPdfFontResolver`, `src/Etp.Reporting.Reporting/DailySalesReportPdfExporter.cs:138-149` | `ResolveTypeface` (line 142) ignores the family name and italic. Every request becomes `SegoeUI-Regular` or `SegoeUI-Bold`. `GetFont` (lines 143-148) reads `segoeui.ttf` or `segoeuib.ttf` from the Windows Fonts folder (`Environment.SpecialFolder.Fonts`, i.e. `C:\Windows\Fonts`). If the file is missing, it throws `FileNotFoundException("Segoe UI is required for DSR PDF export.")` (line 147). |
| Registration | same file, line 141 (`EnsureRegistered`) | Sets `GlobalFontSettings.FontResolver` once per process. It is called by `DailySalesReportPdfExporter.Export` (line 17) and `VisualReportPdfDocument.ExportMany` (`VisualReportPdfDocument.cs:31`). |
| Fonts requested: live DSR | `src/Etp.Reporting.Reporting/EveningDsrPdf.cs:45-46` | `new XFont("Segoe UI", size, Bold/Regular)` for every text item |
| Fonts requested: older fixed-box DSR layout | `DailySalesReportPdfExporter.cs:133` | same |
| Fonts requested: all other PDFs | `src/Etp.Reporting.Reporting/VisualReportPdfDocument.cs:13-15` | 9 pt regular, 9 pt bold, 18 pt bold |

**Every PDF depends on it:**

| PDF | Code path | Started from |
|---|---|---|
| Daily Sales Report | `DailySalesReportPdfExporter` → `EveningDsrPdf` | Reports workspace, Save PDF (`ReportExportCoordinator.cs:39`) |
| Report PDF (visual or tabular), management summary | `SimplePdfVisualReportExporter` / `SimplePdfReportExporter` → `VisualReportPdfDocument` | Reports workspace; Dashboard (`DashboardView.cs:237`) |
| Report pack | `SimplePdfReportPackExporter` → `VisualReportPdfDocument.ExportMany` | Daily Workflow (`DailyWorkflowWorkspaceView.xaml.cs:523`); **unattended automation** (`AutomatedOperationsService.cs:150`); `ReportPackageService.cs:39` |

**No font dependency:**

- Excel export. `ReportWorksheetWriter.cs:48` sets only a font size, with no font name, and loads no font file. Excel uses its own default font.
- The SVG chart. `VisualReporting.cs:120,129` writes the name "Segoe UI" as text only. Whatever displays the SVG substitutes a font if needed.
- The app's own screens (`Themes/Typography.xaml`, `Themes/Controls.xaml`) ask for "Segoe UI Variable Text" and "Segoe UI Variable Display". That is a different font from Segoe UI, and Microsoft lists it as added in Windows 11. It is not installed on this Windows 10 PC, so WPF shows a substitute. The screens load no font file themselves.

### 1.2 Which DSR layout is live

The live DSR always uses `EveningDsrPdf`. `OperationalReportRepository.cs:309` always attaches the evening sheets, and `EveningReportRepository.cs:59` always builds three of them: WLMHW, HEMW and COMBINED. `EveningDsrPdf.Write` (lines 43-48) measures each piece of text and shrinks it in 0.2 pt steps until it fits its box (minimum 3 pt). Text is never clipped: a wider font just prints a little smaller.

The older fixed-box layout (`DailySalesReportPdfExporter.Draw`, lines 37-63) runs only when there are no evening sheets. Today that means the `tools/Etp.Reporting.DsrSmoke` fixture. It uses PDFsharp's `XTextFormatter`, which silently drops lines that fall below a box and lets a single over-long word run past it. That matters only if that layout comes back.

### 1.3 What happens today if the font is missing or unreadable

I traced this through the PDFsharp 6.2.4 source. I then reproduced it with the real `PdfSharp.dll` 6.2.4 from the NuGet cache and a PowerShell re-creation of the resolver (same logic, same message) pointed at a folder with no font files in it. The app was not run.

1. `GetFont` throws `FileNotFoundException` with the clear message.
2. PDFsharp catches every exception a font resolver throws (`XGlyphTypeface.GetOrCreateFrom`) and passes it to its own logger. The app never sets that logger up, and PDFsharp's default is a `NullLogger` (checked), so **the message is thrown away**.
3. Before it called `GetFont`, PDFsharp had already stored "Segoe UI bold → SegoeUI-Bold" in its cache (`FontFactory.RegisterResolverResult`). Its fallback step finds that cached entry, carries on with no font data, and crashes inside PDFsharp. The result is **`System.NullReferenceException`**, "Object reference not set to an instance of an object.", HResult `0x80004003`, thrown in `OpenTypeFontFace.CetOrCreateFrom`. The probe got the same result on the first try, on a second try in the same session, and for both bold and regular.
4. Because the cache entry stays, **the PDF font is broken until the app is closed and reopened**, even if the font is repaired meanwhile (from the source; I did not probe a repair).
5. **On screen:** "PDF export failed: The action could not be completed. Technical details are available in the support package." That is the catch-all branch, `DesktopFriendlyError.cs:39`, reached from `ReportExportCompletion.cs:31-33` and `ReportsWorkspaceView.xaml.cs:389-396`. Report packs show "Report-pack PDF export failed: …" and the management summary "Management summary export failed: …", with the same wording. No PDF is written and an existing file at that name is left alone: the Reports workspace and the Daily Workflow pack write through a temporary file (`ExportStaging.cs:7-19`), and the management summary, which writes directly, fails before anything is saved.
6. **In the log:** one line in `%LOCALAPPDATA%\EtpReporting\Logs\diagnostics-YYYYMM.jsonl`. It has event `REPORT_PDF_EXPORT_FAILED` (or `REPORT_PACK_EXPORT_FAILED` / `MANAGEMENT_SUMMARY_EXPORT_FAILED`), `ExceptionType` `System.NullReferenceException` and `HResult` `-2147467261`. The log entry carries no message and no path (the record's fields, `DesktopDiagnostics.cs:19-27`). The support package does not help either, even though the screen points to it: `scripts/new-etp-support-package.ps1` collects only database health, the Windows version and the scheduled-task states, and does not include this log. **Neither the screen, the log nor the support package mentions a font**, so it looks like a program bug.
7. **Unattended automation:** `AutomatedOperationsService.cs:154-157` catches the error. It records the run as Failed with "Report generation failed; review daily exceptions and application diagnostics." It writes **no** diagnostic entry, so nothing records the cause. Auto-import packs always request a PDF (line 94). Scheduled packs that ask for a PDF fail the same way, and the schedule is marked "The scheduled pack failed; review automation history." (lines 104-107). The Excel pack is written first (line 149), so an `.xlsx` is left in the output folder even though the run says Failed.
8. **File present but unreadable or damaged** (access denied, corrupt file): the same PDFsharp catch applies, so the result should be the same `NullReferenceException`. I read this from the code and did not probe it separately.
9. **Only the bold file missing:** regular text works and the first bold text fails. The DSR title is bold, so the DSR fails immediately.

**Whatever you decide, this reporting needs fixing.** The code meant to fail clearly and does not.

---

## 2. The facts that decide it

### 2.1 Is Segoe UI on every Windows the product supports?

- **Windows 10:** Microsoft's Windows 10 font list puts Segoe UI (`Segoeui.ttf`) and Segoe UI Bold (`Segoeuib.ttf`), version 5.62, in the desktop font set, not in any of the optional font packages (Features on Demand). The page notes that non-desktop editions (Xbox, HoloLens, Surface Hub) may lack desktop fonts; the product does not run on those. [Windows 10 font list][w10]
- **Windows 11:** same two files, version 5.62, in the main list, not in an optional package. [Windows 11 font list][w11]
- Microsoft's Segoe UI page says it has been the Windows interface font since Vista and is not available for download separately. [Segoe UI][segoe]
- **N editions:** Microsoft's page on N editions lists only media parts (Media Player, codecs, media apps and features that depend on them). It says nothing about fonts. [Media Feature Pack for Windows N][nfeat]
- **LTSC / LTSB: not verified.** I found no Microsoft page either way. My reasoning, not a sourced fact: Segoe UI is the font Windows itself draws its menus and settings with, so a desktop Windows without it would be visibly broken. Check any LTSC PC with section 5.
- **The installer** declares no minimum Windows version, only `ArchitecturesAllowed=x64compatible` (`installer/EtpReportingEngine.iss:25`). I did not check the .NET 10 minimum here.
- **This PC:** Windows 10 Pro, version 10.0.19045. Both files are present, version 5.62, and read without error. Their permissions let SYSTEM, Administrators and Users read them. Users includes every signed-in account through Authenticated Users (checked), so the `EtpAutomation` account and SYSTEM, which the automation tasks run as, can read them too.
- **Shop PC and VM `ETP-Acceptance-186`: not checked.** The VM is Saved and I did not start it. Section 5 gives the check.

### 2.2 Licence

- **Shipping `segoeui.ttf` with the app is not allowed.** Microsoft's font redistribution FAQ says that, apart from embedding in documents, fonts that come with Windows may not be redistributed, copied to other computers, or built into an app. Extended rights for Segoe UI can be bought from Monotype. [Font redistribution FAQ][faq]
- **Embedding Segoe UI in the PDFs, as the app does now, is allowed.** The same FAQ allows embedding in documents when the software follows the embedding permission stored in the font. On this PC that permission reads "Editable" for both files, which allows embedding. PDFsharp embeds only the characters used. **The current approach is licence-clean.**

### 2.3 Open-licensed alternatives

| Font | Licence | Get it from | Has ₹, —, ·, − | Closeness to Segoe UI |
|---|---|---|---|---|
| **Noto Sans** | SIL Open Font License 1.1; "Copyright 2022 The Noto Project Authors"; no Reserved Font Name | [notofonts/latin-greek-cyrillic][noto] (built files at [notofonts.github.io][notopages]) or [Google Fonts][notogf] | **Yes, all** (checked in version 2.015) | Measured, section 2.4: about 5% wider text, 6–7% wider numbers |
| Open Sans | SIL Open Font License | [googlefonts/opensans][opensans] | ₹ **not verified** | Not measured. Same designer as Segoe UI (Steve Matteson), and the current version was rebuilt from Noto Sans sources, so expect it close to Noto Sans. |
| Selawik | SIL Open Font License 1.1, Reserved Font Name "Selawik" | [microsoft/Selawik][selawik] (release 1.01); [Microsoft page][selawikms] | **No ₹**: the glyph list in its source has no rupee sign ([contents.plist][selawikglyphs]) | Microsoft calls it an open-source replacement for Segoe UI; third parties call it metric-compatible ([Debian request][selawikdeb]); its README says kerning does not match. Latin only. **Not recommended.** |

This PC happens to have Noto Sans in `C:\Windows\Fonts` (installed 23 Jul 2026 by something I could not identify). I used it only to measure. **Do not rely on it being on other PCs.** A bundled copy must come from the official source above, which needs your permission to download when the work is done.

### 2.4 What switching to Noto Sans would change in the layout (measured)

Method: I read the character widths from the font files and replayed `EveningDsrPdf`'s shrink-to-fit step with real strings and the real box widths:

- store tables 398 pt wide: label column 91.5 pt, number columns 51.1 pt
- combined table: number columns 103.4 pt
- HEMW has 19 rows, from its 8 brand rows in `dbo.brand_rows` (read-only query), so its rows are 16.5 pt high and its text starts at 8 pt

I cross-checked with PDFsharp 6.2.4's own `MeasureString`. The results agreed to 0.1 pt.

Overall, Noto Sans runs **5.4% wider** for regular text and **2.6%** for bold. Amounts written with commas and a decimal point run **7.4%** wider (the digits alone 6.1%). Line height at 8 pt is 10.90 pt against 10.64 pt.

| Live DSR item | Segoe UI width → printed size | Noto Sans width → printed size |
|---|---|---|
| Title, bold 16 pt | 240.9 pt → 16.0 | 247.8 pt → 16.0 |
| "STORE TGT 16,00,000.00 DAY TGT 53,333.33", 8 pt | 161.4 → 8.0 | 170.0 → 8.0 |
| Brand label "Other / unmapped", bold 8 pt | 70.0 → 8.0 | 73.8 → 8.0 |
| Store number "9,38,197.00", 8 pt | 39.7 → 8.0 | 43.0 → 8.0 |
| Store number "67,03,290.00", 8 pt | 44.0 → 8.0 | 47.6 → **7.4** |
| Store number "1,02,54,306.00", 8 pt | 50.1 → 7.2 | 54.3 → **6.6** |
| Same number in the combined table | 50.1 → 8.0 | 54.3 → 8.0 |
| Footer line, 6.7 pt | 393.8 → 6.7 | 417.6 → 6.7 |

What that means:

- **Live DSR:** nothing is clipped and nothing moves off the page, because the boxes are fixed and text shrinks to fit. Store-table amounts of ₹10 lakh or more (seven or more digits before the decimal point) print 0.6 pt smaller: from ₹10,00,000.00 up to ₹99,99,999.99 they drop from 8.0 to 7.4 pt, and from ₹1 crore they drop from 7.2 to 6.6 pt. Negative amounts are a little wider again: "-9,99,999.99" drops from 8.0 to 7.8 pt, and "-67,03,290.00" from 7.6 to 7.0 pt. Every other string I tested keeps its size, and it stays one A4 landscape page (enforced by `ValidateSingleA4LandscapePage`).
- **Report PDFs and packs:** column widths are measured (`VisualReportPdfDocument.cs:59-62`), so columns come out up to about 7% wider. Columns held at the 54 pt minimum or the 145 pt cap for text columns keep their width, but capped text may wrap onto more lines. A wide report may split into one more column section or page. By design nothing is clipped.
- **Older fixed-box DSR layout:** the tested strings still fit. The widest KPI value, "₹1,02,54,306" in bold 17 pt, is 101.8 pt in Segoe UI and 102.1 pt in Noto Sans, in a 106.8 pt box.
- **Look:** the PDF would no longer be in Segoe UI, the Windows interface font, and the approved DSR look would change. (The app's screens ask for Segoe UI Variable, a Windows 11 font, so they do not exactly match today's PDF either.)

---

## 3. The options

### Option A: accept Windows' Segoe UI, and fail clearly if it is missing

What it is: keep the font as it is. Add a check that runs **before PDFsharp is asked for any font**. The check confirms that both files exist and can be read. If not, it raises a clear error, for example: "PDF export needs the Windows font Segoe UI (segoeui.ttf and segoeuib.ttf in C:\Windows\Fonts). One of them is missing or cannot be read. Excel export is not affected." The error type should derive from `InvalidOperationException`, because `DesktopFriendlyError` already shows that type's message on screen. Running the check first also stops PDFsharp's cache from being broken for the rest of the session. The automation run should record the same reason, and a diagnostic entry, instead of the generic line. Optional: setup warns if the two files are missing.

- **Effort:** small. My estimate is about half a day including tests: one check function, one exception type, the automation message, and three or four tests.
- **Risk to the DSR layout:** none. Same font, same widths.
- **Licence:** clean. Segoe UI's "Editable" permission allows embedding it in PDFs (section 2.2), and nothing is redistributed.
- **Testing needed:**
  - A unit test that runs the check against an empty temporary folder. It must assert the error type and the exact message on the first and second attempt. It must call the check directly and not register a PDFsharp resolver, because PDFsharp's font settings are shared across the whole test process.
  - A test that `DesktopFriendlyError` shows that message.
  - A test that the automation run records the font reason.
  - The existing PDF tests, unchanged.
  - Nothing to look over by eye.

### Option B: bundle an open-licensed font (Noto Sans) and always use it

What it is: ship `NotoSans-Regular.ttf` and `NotoSans-Bold.ttf`, unmodified, inside the reporting library (about 1.7 MB together for the version 2.015 copies on this PC; the official download may differ slightly). The resolver returns those bytes and Windows fonts are no longer read. The installer must carry the SIL Open Font License text and a third-party notice. The app has no third-party notices file today.

- **Effort:** medium. My estimate is one to two days, most of it reviewing every PDF by eye and adding the licence notice to the installer.
- **Risk to the DSR layout:** low but visible. Store-table amounts of ₹10 lakh or more print about 0.6 pt smaller. Report PDFs may gain a column section or page. The PDFs are no longer in Segoe UI. The approved DSR look would need re-approval.
- **Licence:** clean. The OFL allows bundling with commercial software if the copyright notice and licence travel with the font ([OFL text][ofl]). Noto Sans has no Reserved Font Name, and shipping the files unmodified avoids any renaming question.
- **Testing needed:**
  - All PDF tests: `VisualPdfExportRegressionTests`, `SimplePdfReportExporterTests`, `ReportPackExporterTests`, `VisualReportingTests` and `ReportPackageServiceTests` (in `tests-dotnet/Etp.Reporting.Reporting.Tests`), plus the `tools/Etp.Reporting.DsrSmoke` fixture.
  - The SQL integration tests that write PDFs: `EveningReportsSqlTests.cs:122-133` (real DSR PDFs in `%TEMP%\EtpPhase2Review`) and `ReportFilterClosureTests.cs` (a filtered report PDF).
  - A side-by-side look at the DSR for both stores and at a wide report, against today's output.
  - A glyph check for ₹, —, · and −.
  - A check that setup installs the licence text.

### Option C: Segoe UI when present, bundled Noto Sans when not

What it is: option B's bundled files, plus a choice made in `ResolveTypeface`. It must be made there and not in `GetFont`, because PDFsharp caches before calling `GetFont`. If Segoe UI's files can be read, use them; otherwise use the bundled Noto Sans. The choice holds for the whole session.

- **Effort:** medium to large. It is option B's work plus about half a day for the fallback and for tests of both paths.
- **Risk to the DSR layout:** none on normal PCs. On a PC without Segoe UI you get option B's differences, on the path that is least often used and least looked at.
- **Licence:** the same as option B for the bundled font and option A for Segoe UI. Both are clean.
- **Testing needed:** everything in option A and option B, plus tests that each path is chosen correctly and that a PDF made on the fallback path still passes the one-page A4 check.

### Side by side

| | A: accept + clear failure | B: always Noto Sans | C: Segoe UI, else Noto Sans |
|---|---|---|---|
| Effort (estimate) | about ½ day | 1–2 days | 1½–2½ days |
| DSR layout change | none | amounts of ₹10 lakh or more about 0.6 pt smaller; new look | none, except on a PC without Segoe UI |
| Licence | clean | clean, must ship OFL text | clean, must ship OFL text |
| PDF works on a PC without Segoe UI | no, but says why | yes | yes |
| Download needed | no | yes, font from the official source | yes |
| Visual review needed | no | yes | yes, for the fallback path |

---

## 4. Recommendation: option A

Reasons:

1. **The risk it guards against is very small for this product.** Segoe UI is in Microsoft's standard desktop font list for Windows 10 and for Windows 11, not in an optional package, and N editions only drop media parts. (LTSC/LTSB is not confirmed; see section 6.) A shop PC without it would be a damaged Windows installation, where more than the PDF would be wrong.
2. **The real defect is the misleading failure, and option A fixes exactly that.** Today a missing font looks like a program crash and stays broken until restart. Option A turns it into a plain sentence on screen and in the automation record.
3. **Bundling cannot keep the approved look.** Segoe UI itself may not be shipped without buying extra rights from Monotype. Any substitute changes the DSR (measured above) and needs re-approval. Selawik, the closest in shape, lacks the rupee sign.
4. **Option A leaves the door open.** Option C can be added later without undoing any of option A's work.

**When to revisit:** move to option C if any real shop PC or VM turns out to lack either file (section 5), or if the product is ever asked to run on something other than desktop Windows.

---

## 5. What Sagar needs to do

1. **Decide.** Reply "D14: A" (or B or C). Nothing changes until you do. Option B or C would also need your permission to download Noto Sans from its official source.

2. **Optional, recommended: check the shop PC and the VM** (the next time the VM is running for the Phase 4 steps). Run the commands on the machine being checked: on the shop PC in its own PowerShell window, and for the VM inside the VM's own PowerShell window, not on this PC. These commands only read. They work in an elevated Windows PowerShell window, and elevation is not needed. Paste one at a time.

   a. Show which Windows this is.

   ```powershell
   Get-CimInstance Win32_OperatingSystem | Select-Object Caption, Version, BuildNumber
   ```

   What you should see: a small table with the headings `Caption`, `Version` and `BuildNumber` and one row under them, such as `Microsoft Windows 10 Pro   10.0.19045   19045` (that is this PC). If the caption says `LTSC`, `LTSB` or `N`, still do step b; those are the editions I could not confirm.

   b. Read the two font files the way the app does. This reads them with your own Windows account; the automation accounts (SYSTEM and `EtpAutomation`) were checked only on this PC (section 2.1).

   ```powershell
   foreach ($f in 'segoeui.ttf', 'segoeuib.ttf') { '{0}: {1:N0} bytes read' -f $f, [System.IO.File]::ReadAllBytes((Join-Path $env:WINDIR "Fonts\$f")).Length }
   ```

   What you should see: two lines. On this PC they are `segoeui.ttf: 955,804 bytes read` and `segoeuib.ttf: 951,724 bytes read`; other Windows versions may show slightly different sizes.

   If it fails: a red error such as `Could not find file` or `Access to the path is denied` means PDF export will fail on that PC. **Do not copy the font from another PC:** Microsoft's licence forbids it. Note which PC and the exact error and send them to me. That is the trigger for option C.

   c. Load the Windows font reader (prints nothing).

   ```powershell
   Add-Type -AssemblyName PresentationCore
   ```

   d. Show the font version, its embedding permission, and whether it has the rupee sign.

   ```powershell
   foreach ($f in 'segoeui.ttf', 'segoeuib.ttf') { $g = New-Object System.Windows.Media.GlyphTypeface ([uri](Join-Path $env:WINDIR "Fonts\$f")); '{0}: {1}, embedding {2}, rupee sign {3}' -f $f, $g.VersionStrings[[System.Globalization.CultureInfo]::GetCultureInfo('en-US')], $g.EmbeddingRights, $g.CharacterToGlyphMap.ContainsKey(0x20B9) }
   ```

   What you should see: `segoeui.ttf: Version 5.62, embedding Editable, rupee sign True`, and the same for `segoeuib.ttf`. A different version number is fine. If embedding is not `Editable` (or `Installable`), or the rupee sign is `False`, send me the output before deciding.

   If step c or d shows a red error but step b worked, the files can be read, but do not assume PDF export works: an error in step d can mean a damaged font file, and PDFsharp would fail on it too. Send me the exact error before deciding.

---

## 6. What I could not verify

- That Segoe UI is present on LTSC/LTSB editions. There is no Microsoft source either way, and I had no such machine.
- The shop PC and the VM. They have not been checked yet (section 5).
- That a file which is present but unreadable or corrupt gives the same `NullReferenceException`. I read this from the PDFsharp source and probed only the missing-file case.
- That repairing a font during a session still leaves PDFs broken until restart. I read this from the source and did not probe it.
- Open Sans: whether it has the rupee sign, and its widths. Not measured.
- Selawik's metric compatibility. It is stated by third parties and not measured, and its built TTF files were not checked. The missing rupee sign comes from its source glyph list.
- Where the Noto Sans copy in this PC's `C:\Windows\Fonts` came from.
- The effort figures. They are my estimates, not measurements.
- The minimum Windows version that .NET 10 requires. Not checked.

## 7. Sources

All web pages were read on 22 Sep 2026.

- Microsoft, Windows 10 font list: https://learn.microsoft.com/en-us/typography/fonts/windows_10_font_list
- Microsoft, Windows 11 font list: https://learn.microsoft.com/en-us/typography/fonts/windows_11_font_list
- Microsoft, Segoe UI font family: https://learn.microsoft.com/en-us/typography/font-list/segoe-ui
- Microsoft, font redistribution FAQ: https://learn.microsoft.com/en-us/typography/fonts/font-faq
- Microsoft, Media Feature Pack for Windows N: https://support.microsoft.com/en-us/windows/experience/platform-variants/media-feature-pack-for-windows-n
- Noto Sans source: https://github.com/notofonts/latin-greek-cyrillic; builds: https://notofonts.github.io/latin-greek-cyrillic/; Google Fonts: https://fonts.google.com/noto/specimen/Noto+Sans
- Open Sans source: https://github.com/googlefonts/opensans
- Selawik: https://github.com/microsoft/Selawik, https://learn.microsoft.com/en-us/typography/font-list/selawik; source glyph list: https://raw.githubusercontent.com/microsoft/Selawik/master/Source%20files/UFO/Selawik-Bold.ufo/glyphs/contents.plist; Debian packaging request: https://groups.google.com/g/linux.debian.bugs.dist/c/31AFGidM6Uo
- SIL Open Font License 1.1: https://openfontlicense.org/open-font-license-official-text/
- PDFsharp 6.2.4 source:
  - https://github.com/empira/PDFsharp/blob/v6.2.4/src/foundation/src/PDFsharp/src/PdfSharp/Fonts/FontFactory.cs (`ResolveTypeface`, `RegisterResolverResult`)
  - https://github.com/empira/PDFsharp/blob/v6.2.4/src/foundation/src/PDFsharp/src/PdfSharp/Drawing/XGlyphTypeface.cs (`GetOrCreateFrom`)
  - https://github.com/empira/PDFsharp/blob/v6.2.4/src/foundation/src/PDFsharp/src/PdfSharp/Drawing.Layout/XTextFormatter.cs
- Measurements on this PC, 22 Sep 2026:
  - fonts in `C:\Windows\Fonts`, read through WPF `GlyphTypeface` in Windows PowerShell 5.1
  - `PdfSharp.dll` 6.2.4 from `%USERPROFILE%\.nuget\packages\pdfsharp\6.2.4\lib\net10.0`, loaded in PowerShell 7.6.6 (.NET 10.0.12) with a PowerShell re-creation of the resolver
  - one read-only query of `dbo.brand_rows` in `EtpReporting`
  - the probe scripts live in the session scratch folder, not in the repository

[w10]: https://learn.microsoft.com/en-us/typography/fonts/windows_10_font_list
[w11]: https://learn.microsoft.com/en-us/typography/fonts/windows_11_font_list
[segoe]: https://learn.microsoft.com/en-us/typography/font-list/segoe-ui
[faq]: https://learn.microsoft.com/en-us/typography/fonts/font-faq
[nfeat]: https://support.microsoft.com/en-us/windows/experience/platform-variants/media-feature-pack-for-windows-n
[noto]: https://github.com/notofonts/latin-greek-cyrillic
[notopages]: https://notofonts.github.io/latin-greek-cyrillic/
[notogf]: https://fonts.google.com/noto/specimen/Noto+Sans
[opensans]: https://github.com/googlefonts/opensans
[selawik]: https://github.com/microsoft/Selawik
[selawikms]: https://learn.microsoft.com/en-us/typography/font-list/selawik
[selawikglyphs]: https://raw.githubusercontent.com/microsoft/Selawik/master/Source%20files/UFO/Selawik-Bold.ufo/glyphs/contents.plist
[selawikdeb]: https://groups.google.com/g/linux.debian.bugs.dist/c/31AFGidM6Uo
[ofl]: https://openfontlicense.org/open-font-license-official-text/
