
## A3.8 — touch-only walkthrough for Sagar

**This is the one piece of evidence nobody but you can produce.** Every button path below is already covered by automated tests; what those tests cannot establish is whether a person can complete an evening on a touchscreen inside three minutes. Automated `Button.Click` events are not touch acceptance, and this audit does not claim them as such.

### Before you start

- Run it on the **shop PC**, maximised, in **Touch** density (Settings → Display → Display density → Touch). The shop PC is currently on Desktop density; change it first, and that change is itself part of what you are accepting.
- **No keyboard and no mouse.** Fingers only. If you reach for either, stop and note where — that point is the finding.
- Have a folder of the day's ETP exports ready, and start a stopwatch on the first tap.

### The walkthrough

| # | Step | What to check as you go |
|---|---|---|
| 1 | Open the app | It lands on **Today → Sales**, not a menu |
| 2 | **Import** → **Import today's folder** → pick the folder | Per-file results appear with counts |
| 3 | **Problems** tab | If any file failed, **Retry failed** is enabled |
| 4 | Tap **Retry failed** | Only the failed file re-runs; successful files keep their counts |
| 5 | **History** tab | Today's imports are listed with outcomes |
| 6 | **Today → Walk-ins** → enter the count | Number keypad appears — not the full keyboard |
| 7 | **Today → Cash** → enter cash figures | Same keypad behaviour |
| 8 | **Today → Sales** | DSR renders; brand rows show values, not zeros |
| 9 | **Export PDF** | File is produced |
| 10 | Stop the stopwatch | |

**Passes if:** under three minutes, finger-only throughout, no control too small to hit first time, and nothing needed horizontal scrolling to reach.

### What to write down

Only four things — I will turn them into the audit record:

1. **Total time.**
2. **Any step where you used a keyboard or mouse**, and why.
3. **Any control you missed on the first tap** — that is a touch-target finding, even if the second tap worked.
4. **Whether the DSR brand rows showed real values.** With migration 0027 they should; all fourteen were non-zero in my test against your real July–August data.

### Two things deliberately not in this list

**Retry needs a failed file to exist.** If every file imports cleanly you cannot test step 4 honestly. Either run it on a day with a genuine failure, or skip steps 3–4 and say so — do not manufacture a corrupt file on the shop PC.

**This does not test 125% scaling.** I ran that separately in the acceptance VM. If you personally run the shop PC at 125%, say so and I will re-run it at that setting.

### Outcome

Sagar reported on 22 September 2026 that he has done this walkthrough, with no finding raised, and it is recorded as accepted on his observation (see the addendum in `PHASE-3-AUDIT.md`). The four details above were not written down for this record.
