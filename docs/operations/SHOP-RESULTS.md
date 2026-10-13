# Shop results: what staff see each morning (1.12)

Status: checked against the C1 code at the 1.12.0 wave-1 integration (13 Oct 2026).

## The idea
Until now a shop sent its files and heard nothing. From 1.12, ETP writes a short result for each file a shop sends, Workpc signs it and puts it in that shop's OneDrive folder, and the shop PC shows it, so staff learn the same morning whether yesterday's files were accepted.

## What staff see
When the shop PC signs in (task "ETP Show Shop Results"), and again each time the day's files are sent, a window opens:
- **Green**: "Sheet for <date>: accepted."
- **Amber**: "Sheet for <date>: accepted, with a warning." Example: the date in the file name and the date inside the sheet differ. ETP imports it under the date inside the sheet.
- **Red**: "Sheet for <date>: REFUSED. ETP did not take a file.", with each refused file, a plain reason and **Fix:** a one-line instruction.
- **Already had**: ETP already has this exact file; nothing to do.
- **"Could not be checked"**: the result did not carry the right signature, or belongs to another shop, day or send. This is not a verdict. Ask the office; do not assume the files were accepted or refused.

A refused result is shown again at every sign-in until a newer one arrives. The last message is also saved as `last-result.txt` in the shop PC's log folder (`%LOCALAPPDATA%\EtpOneDrive`, the same folder as `last-send.txt`).

## What a result file contains
Only: shop role, business date, send number, ETP version, time produced, and per file its name, fingerprint, verdict, a plain message and what to do. **No amounts, no customer names, no phone numbers.** It is signed with that shop's own key, so one shop cannot be shown a verdict meant for another.

## Timing
- ETP writes the result when its unattended import takes the shop's zip. Workpc publishes results at **07:40** (task "ETP Publish Shop Results"). Publishing from the Morning page is planned for wave 2; until then Sagar can run the task by hand (`Start-ScheduledTask 'ETP Publish Shop Results'`).
- After a fix and re-send, a new result is published for the same day with the send number in its name; the newest wins.
- Results appear in `OneDrive\ETP-Inbound\<TITAN, HELIOS or SERVICE>\Results\<date>.result.json` (or `<date>-<n>.result.json` for a later send) and reach the shop PC through OneDrive, so allow for sync delay. Results older than 30 days are removed from OneDrive.

## For Sagar
- Setup creates `C:\ProgramData\EtpReporting\Operations\ShopResults\{pending,published,rejected}`; the automation account may write `pending` only. If ETP cannot write a result, that run's line in the automation history says so.
- Nothing shows after 08:30? Check OneDrive is syncing on the shop PC, then that the Workpc task "ETP Publish Shop Results" ran, then that the shop's key is in Workpc's `shop-keys` folder (a role without a key stays in `pending`).
- After installing 1.12, run `Register-WorkpcTasks.ps1` again on Workpc and `Register-ShopDailyTask.ps1` again on each shop PC, so the two new tasks exist.
- A red result staff cannot fix themselves belongs on the Morning page's import problems list (wave 2).
