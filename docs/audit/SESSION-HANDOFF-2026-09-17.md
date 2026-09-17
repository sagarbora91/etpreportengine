# Session handoff — 17 September 2026

The one file to read before continuing this work. It supersedes earlier progress notes
for everything dated 17 September 2026.

| | |
|---|---|
| Branch | `recovery/opus-r1-r4`, pushed. Last code commit `9d0afbb`; documentation after it. |
| `main` | the whole product line (Phases 0-5) merged in tonight by the owner's decision, and carrying identical source to the branch |
| Gate | 925 passed, 3 skipped, 0 failed, `BUILD=0` `TEST=0` |
| Installer | `artifacts/installer-9d0afbb/EtpReportingEngine-Setup-1.8.8-x64.exe`, 822 MB, unsigned, SQL Express media embedded |

**Phase 4 is not closed.** Code and decisions are done and merged; the deployment evidence
Phase 4 defines for itself is not produced, and one newly found defect (P4-13) blocks
producing it on SQL Server Express. Merging is not certifying.

---

## What changed tonight

| Commit | What |
|---|---|
| `bc89227` `3719010` | R1 durable import problems; R4 database and recovery health |
| `d87dec9` | Five defects the R1–R4 verification found |
| `3c80035` | The R1–R4 verification report, four screenshots with accessibility trees |
| `b123fb6` | Shell lists show every section; tasks read as their title, not a record dump |
| `514a105` | D9 revised for Express, unsigned releases accepted, P4-11 recorded and fixed |
| `51cf3d7` | Migration `0031`: the failed-import count is what Problems actually shows |
| `10d622c` | Runbook and quick start corrected to match the code |
| `9d0afbb` | P4-12: setup reported success after failing, and hung when silent |

Nine defects found and fixed across the session. The two that mattered most were invisible
to every test: the database and recovery block was rendered inside an unlabelled "Guidance"
tab where no owner would look, and setup reported a failed install as a success.

---

## Machine state, as left

### This machine (shop candidate)

- Application installed at `C:\Program Files\Saagar Traders\ETP Reporting Engine`,
  version `1.8.8+10d622c`. **Its setup did not complete** — see P4-12 below. It was
  installed from the pre-P4-12 build.
- `EtpReporting` migrated **`0016` → `0031`** by the owner, elevated, using
  `--initialize-configured-database`. Broker and health procedures present.
- Independent pre-upgrade backup taken before that migration and **verified**:
  `EtpReporting-pre-0031-20260917-151407.bak`, full, CHECKSUM, copy-only,
  12,869,632 bytes, `RESTORE VERIFYONLY` passed, `key_algorithm=NONE`, in the SQL
  default backup directory. Keep it until a verified backup exists through the product.
- The three ETP scheduled tasks were disabled while setup was incomplete and have been
  **re-enabled** by the owner. The daily backup will fail nightly until the operations
  module is installed (see P4-13).
- `EtpReportingHelios` is still at **`0020`** and has had none of this treatment. If that
  database is live for the Helios store it is now a schema behind, with no pre-upgrade
  backup. **Decide before relying on it.**

### Acceptance VM `ETP-Acceptance-186`

- Running. Reachable by PowerShell Direct as `ETPTest`.
- Has SQL Express and the application files, but **no `operations.json` and no
  `EtpAutomation` account**, so setup cannot complete there either.
- Used tonight to prove P4-12 end to end.

---

## Open defects

### P4-13 — the D9 revision was only half applied (blocks Phase 4 closure on Express)

`backup-etp-database.ps1` honours revised D9: on Express it warns, takes an unencrypted
backup and records `"encryption": "NONE"`. `install-etp-sql-operations.ps1` does not. It
still requires `##MS_DatabaseMasterKey##` and grants `VIEW DEFINITION` on `EtpBackupCert`,
and its failure message is `Create the backup certificate first.`

On Express — the edition this shop runs — the operations module therefore cannot be
installed without creating an encryption certificate that will never encrypt anything.
Without that module there is no verified backup and no recovery drill, so three of the
four Phase 4 closure items cannot be produced.

The fix is not to drop the requirement. Two things are conflated:

- **Module signing** needs a database master key and the `EtpOperationsModuleSigner`
  certificate. Required on every edition.
- **Backup encryption** needs `EtpBackupCert`. Required only where the edition can encrypt.

Separate them, and make the message name the one that is actually missing. Creating a
master key needs a password with real custody consequences, so it stays a deliberate
operator step, not something a script generates silently.

Measured preconditions on this machine:

```
master_key           = NO   <- guard 1 throws first
EtpBackupCert        = NO
ModuleSigner cert    = NO
automation SQL login = NO   <- the Windows account exists, the SQL login does not
store_manager member = NO
etp_automation role  = present
```

**The module is half-installed, not absent.** The attempted run created the broker before
throwing: `master.dbo.etp_operations_31736fb143c6912a` exists, targets
`C:\ProgramData\EtpReporting\Backups` and `BACKUP DATABASE [EtpReporting]`, and is
correct. What is missing is the signature and the grants:

```
procedure exists   = YES
signed             = NO    <- cannot use the elevated permissions it needs
signer certificate = NO
master key         = NO
```

An unsigned procedure still runs for a sysadmin, who bypasses the role check and already
holds BACKUP rights, so a backup started by the owner may now get further than it did
tonight. It will not work for the dedicated automation account, which is what the
scheduled tasks use - that needs the signature and `GRANT EXECUTE`.

### An unhandled ArgumentException the owner sees

Starting the application raises `DISPATCHER_UNHANDLED` / `System.ArgumentException` and
shows a modal dialog: *"The operation could not be completed. A diagnostic entry was
recorded. No source rows were written to the log."*

- **Pre-existing**, not from tonight's work: the identical exception was recorded at
  08:19, 08:20 and 08:21 UTC on 17 September, before any of these changes.
- **Reproduces on demand**, against both `EtpOpusRecovery` and `EtpWalkthrough`, so it is
  not data-specific.
- The privacy-safe diagnostics log records the type and HResult but no message or stack,
  so diagnosing it needs a targeted reproduction, not log reading.

This is the only defect on the list that a shop owner actually sees. It should be next.

### Smaller, recorded

- **The operations helper hides every SQL error behind one sentence.** `Invoke-EtpSql`
  deliberately never exposes stderr, so a missing stored procedure, a wrong database and a
  permissions refusal all read as *"The database operation failed. Check SQL permissions
  and operation prerequisites."* That cost three separate diagnoses tonight. The intent —
  not leaking connection detail to an operator — is sound; the result is unoperable.
- **Upgrading with ETP running** used to abort halfway and roll back. Fixed in `9d0afbb`,
  but the shop PC has not been upgraded with a build that contains the fix.
- **The Desktop test suite is flaky under load.** Two failures in roughly eight full runs,
  a different test each time, each passing in isolation; five consecutive clean runs when
  the machine was idle. Both failures were WPF/XAML loading races
  (`System.IO.Packaging`), not product logic.
- **Ctrl+K path** — reported as showing an incorrect path. Seventeen searches were
  measured, comparing the path shown against where Enter landed, with zero mismatches.
  Not reproduced; needs the specific search term.
- **The 11 low-severity review findings are unrecoverable.** They existed only in a
  workflow's output and were never written to a file. A fresh review of the current diff
  would be more useful than reconstructing them.

---

## Cleanup — done, 17 September 2026

Test residue from this work has been removed, with the owner's approval, so it cannot
mislead a later diagnosis the way it misled one tonight:

- Dropped `master.dbo.etp_operations_b26bdf59e7bd299f`, an operations broker for the
  throwaway `EtpD9Proof` database pointing at `C:\EtpD9Proof\Backups`. A name-only check
  found it and reported "the module is installed" when the module for the real database
  was not.
- Dropped the proof databases `EtpD9Proof`, `EtpWalkthrough`, `EtpOpusRecovery` and the
  integration-test leftovers `EtpPhase0Test_*`, `EtpPhase1Test_*`.
- Removed `C:\EtpD9Proof` from disk.

**Only `EtpReporting` and `EtpReportingHelios` remain**, and only one operations broker,
`etp_operations_31736fb143c6912a`, which serves `EtpReporting`. The removal ran behind an
explicit allow-list: a database not named on it was kept even if it looked like residue.

---

## To finish Phase 4

In order. Everything here is blocked behind the first item.

1. **Fix P4-13** so the operations module can be installed on Express.
2. **Complete the operational chain on this machine**: master key and certificate custody,
   the `EtpAutomation` SQL login and its roles, then the operations module.
3. **Produce the evidence**: a verified backup, a same-machine recovery drill. The script
   `scratchpad/recovery/phase4-evidence.ps1` does both and prints the receipt; it needs
   only step 2 to be complete.
4. **Provision the VM** and install there, which gives the clean-install success path and
   the **second-machine restore** — the fourth closure item.
5. **Verify the protected-folder ACLs** before and after.
6. **Then the shop PC**, which still runs `1.8.1+8e35d83`, using a build that contains the
   P4-12 fix, during a window when the shop is closed.

Phase 4 closes when those are evidenced, not when the code merges.
