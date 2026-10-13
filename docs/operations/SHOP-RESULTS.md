# Shop results: what staff see each morning (1.12)

Status: written from the wave plan; **CHECK AT INTEGRATION** against the C1 lane note (script names, window wording).

## The idea
Until now a shop sent its files and heard nothing. From 1.12, Workpc sends back a short result file for each shop and day, so staff learn the same morning whether yesterday's files were accepted.

## What staff see
When the shop PC signs in, and again when the day's files are sent, a window opens:
- **Green**: "Yesterday's sheet: accepted."
- **Green with a note**: accepted with a warning. Example: the date in the file name and the date inside the sheet differ. ETP uses the date inside the sheet.
- **Red**: "Refused", with a plain reason and **Fix:** a one-line instruction (for example "Fill in 'Filled by' and send again").
- **Duplicate**: this file was already received; nothing to do.
- **"Result could not be checked"**: the result file did not carry the right signature. This is not a verdict. Tell Sagar; do not assume yesterday was accepted or refused.

The last message is also saved as `last-result.txt` beside the shop's scripts.

## What a result file contains
Only: shop role, business date, ETP version, time produced, and per file its name, fingerprint, verdict, a plain message and what to do. **No amounts, no customer names, no phone numbers.** It is signed with that shop's own key, so one shop cannot be shown a verdict meant for another.

## Timing
- Workpc publishes results at 07:40 and on demand from the Morning page.
- After a fix and re-send, a new result is published for the same day; the newest wins.
- Results appear in `OneDrive\ETP-Inbound\<TITAN, HELIOS or SERVICE>\Results\<date>.result.json` and reach the shop PC through OneDrive, so allow for sync delay.

## For Sagar
- Nothing shows after 08:30? Check OneDrive is syncing on the shop PC, then that the Workpc task "ETP Publish Shop Results" ran, then that the shop's key is present.
- A red result staff cannot fix themselves belongs on the Morning page's import problems list.

## CHECK AT INTEGRATION
Window text, `last-result.txt` location, task name and `Show-ShopResults.ps1` timing against the C1 note.
