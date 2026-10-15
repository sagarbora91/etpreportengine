# ETP security: what we protect, against whom, and with what

Written 15 Oct 2026 (lane C1) from the approved code-protection plan
(`E:\ETP\Reference\Work in progress 2026-10-15\CODE-PROTECTION-PLAN.md`, Sagar's answers D1-D10 and decision 65).
This file contains no secret, key, passphrase or private-key fingerprint, and never will.

Companion documents in this folder:

| File | What it covers | Lane |
|---|---|---|
| `SECURITY.md` (this file) | threat model, what protects what, key register (ids and places only) | C1 |
| `RECOVERY.md` | rebuilding the development set-up on a fresh PC, and the recovery acceptance test | C1 |
| `RELEASE-PROTECTION.md` | the `-Protect` release pipeline, Obfuscar version and settings, symbol archive, per-release checklist | O1 |
| `CODE-SIGNING.md` | the Authenticode certificate ceremony, signing, trusting the certificate on each PC, renewal | S1 |
| `VERIFICATION.md` | decompiler before/after findings, regression and performance results per release | V1 |

## 1. What ETP is protecting

1. **The source code** - C#, SQL migrations and object files, PowerShell scripts, design documents. Repository
   `sagarbora91/etpreportengine` on GitHub.
2. **The shipped program** - the installer and what it puts in Program Files on Workpc and the shop PCs.
3. **The business data** - Saagar Traders' sales, stock, customers (names and phone numbers), bank and settlement lines.
   It lives in the `EtpReporting` database, its backups and the `(CONTAINS BUSINESS DATA)` folders on E:, never in git.
4. **The keys** - the release signing key, the code-signing certificate, the OneDrive backup key.

## 2. Who could cause harm (threat model, in plain English)

ETP is not sold or distributed. It runs on Workpc (office) and, in time, three shop PCs on one LAN; OneDrive carries
daily files and the update feed. Users are Sagar (Owner), a Store Manager login, and later Viewers.

| # | Threat | Likelihood | What protects against it |
|---|---|---|---|
| T1 | Anyone on the internet reads the GitHub repository. **It was public from 26 Aug to 15 Oct 2026.** No secrets were in it (history checked), but the whole engine and its design were readable. Any copy taken then cannot be recalled; accepted. | happened | Repository made **private** (D1). Nothing else needed; no history rewrite. |
| T2 | Sagar's GitHub account is taken over (phishing, reused password, stolen token) and a poisoned commit is pushed that Claude then builds. | low, high impact | Two-factor sign-in with a passkey; recovery codes on paper in the USB-kit envelope; collaborators, apps and tokens reviewed; only Sagar pushes `main` (D2: no paid branch protection for now). |
| T3 | Someone copies the installed program from a shop PC and passes it on. | medium | No debug symbols (PDBs) shipped; private C# names and SQL text hidden by obfuscation in the release build (D3). The program is also of little use without Saagar Traders' data, exports and routine. |
| T4 | A laptop, USB stick or Workpc disk is stolen. | low | BitLocker on C:, E:, G: (Sagar's list). Keys on the sticks are passphrase-encrypted; passphrases are on paper, kept apart. |
| T5 | A former staff member keeps an old login or an old copy. | medium | Account removal procedure in `docs/OPERATIONS.md`; same program protection as T3. |
| T6 | A tampered installer or script is put on a USB stick or in the shared OneDrive and run as administrator. | medium | Signed `release.json` for updates (1.12, B1); signed shop manifests (1.11.2); **Authenticode signing of exe, DLLs, scripts and installer** (D5, 1.13.0) closes AUDIT-2 S-14. |
| T7 | A competitor reverse-engineers the report logic. | low | Obfuscation slows it down (days instead of minutes for the C# half). It cannot stop it - see section 4. |
| T8 | Supply chain: a bad NuGet package, GitHub action or build tool. | low | NuGet audit fails the build on advisories; GitHub actions pinned to commit SHAs; Obfuscar and ilspycmd pinned by version and SHA-256 under `E:\Tools`. |

Deliberately **not** done, because it would cost more than it protects: source encryption in git (decision 65 - Claude
must read plain files, every worktree would need the key, a lost key loses the source); paid control-flow obfuscators
(break WPF and JSON, slow the app); native AOT and trimming (unsupported for WPF); a GitHub Actions release pipeline holding
keys in cloud secrets (moves keys off the offline sticks); SQL `WITH ENCRYPTION` on views (reversible in seconds, breaks the
object-file journal and support).

## 3. What protects what

| Layer | Measure | State | Where documented |
|---|---|---|---|
| Source on GitHub | private repository, 2FA/passkey, access review | Sagar, 15 Oct 2026 | plan section 4.1 |
| Source on disk | BitLocker; no secrets in the tree (`.gitignore` blocks `*.pfx`, `*.p12`, `secrets/`, `private-keys/`) | BitLocker pending (Sagar) | this file |
| CI | `ci.yml`: build + unit tests only, `contents: read`, no secrets, pinned actions; no SQL, never touches the books | in place | `docs/operations/CI.md` |
| Release build | runs only on Workpc, elevated, after the full gate; tests -> publish (folder) -> obfuscate -> protected smoke vs unprotected -> sign -> Inno -> sign installer -> archive symbols | 1.13.0 | `RELEASE-PROTECTION.md` |
| Shipped files | no `*.pdb`; obfuscated non-UI assemblies; Authenticode signatures; SHA-256 sums; signed `release.json` | PDB removal and signing from 1.13.0 | `RELEASE-PROTECTION.md`, `CODE-SIGNING.md` |
| Updates | in-app update checks the ECDSA signature on `release.json` before running setup | 1.12 (key list filled at ceremony) | `docs/operations/UPDATES.md` |
| Installed PCs | certificate trusted by hand per PC after comparing the fingerprint with the paper card (D10); never by setup | 1.13.0 | `CODE-SIGNING.md` |
| Database | Windows logins only, three roles, column DENYs on customer and bank data for Viewer, Owner-only money inputs, encrypted OneDrive backups | in place | `docs/OPERATIONS.md`, AUDIT-2 (12 Oct 2026) |
| Development | dev builds are never obfuscated or signed; Claude never handles keys | rule | `CLAUDE.md` section 10 |

## 4. What obfuscation does and does not do

**Does:** strips the names Claude and the compiler gave to private classes, methods and variables, and hides the text of
SQL statements and messages inside the program, so a decompiler shows `a.b(c)` instead of
`SalesReportingService.Summarize(lines)`. Together with shipping no PDBs, a reader must work for days instead of minutes
to understand the C# half.

**Does not:** hide the structure or the arithmetic (control flow is untouched); hide anything in the SQL files, the
database, the PowerShell scripts, `ErrorFixManifest.json` or the help screenshots; stop anyone copying or running the
program; stop a determined expert. It does not change a single number in a report - the protected-build smoke proves that
for every release.

About half of ETP's logic is SQL (views, functions and procedures under `database/objects`, plus migrations). It ships as
plain text by design and is readable inside the database by any local administrator. Nothing in this plan hides it.

## 5. Signing: what it does and does not do

**Does:** makes every shipped file tamper-evident (a changed byte breaks the signature); lets shop scheduled tasks move to
`AllSigned`; UAC shows a named publisher on PCs that trust the certificate.
**Does not:** earn SmartScreen reputation (a self-signed certificate never does; staff may see "unrecognised app"); hide or
protect code.

## 6. Key register (ids, places and dates only - never values)

| Key | Purpose | Made | Where it lives | Rotation |
|---|---|---|---|---|
| Release signing key (ECDSA P-256), `etp-release-signing-key.json` | signs `release.json` for in-app updates | ceremony `scripts/release/New-EtpReleaseSigningKey.ps1` (date: ______) | passphrase-encrypted file on **both USB sticks**; public key line in `src/Etp.Reporting.Application/Updates/ReleaseSigningKeys.cs`; passphrase on paper | lost sticks or passphrase = new key; next release installed once by hand |
| Code-signing certificate (RSA-3072, self-signed, 10 years), `etp-codesign.pfx` | Authenticode on exe, DLLs, scripts, installer | ceremony `scripts/release/New-EtpCodeSigningCertificate.ps1` (lane S1; date: ______) | PFX with passphrase on **both USB sticks**, never imported into Workpc's store; public `etp-codesign.cer` in `installer/certs/`; thumbprint on the paper card | new ceremony + trust script again on every PC |
| OneDrive backup key | encrypts the nightly backups copied to OneDrive | `scripts/onedrive` `-NewKey` | key file on the USB sticks (`key\`), never in OneDrive or on the Desktop | new key makes older `.bak.enc` unreadable unless the old key is kept |
| GitHub account | source access | - | Sagar's sign-in, passkey; recovery codes on paper in the USB-kit envelope | - |

Public fingerprints may be written in `CODE-SIGNING.md` and on the paper card after each ceremony. Private-key material,
passphrases and passwords are never written in the repository, OneDrive, notes, logs or chat.

## 7. Database security and audits

Database security (roles, DENYs, Owner grant option, folder ACLs, encrypted backups, DPDP work) is described in
`docs/OPERATIONS.md` and was audited on 12 Oct 2026 (AUDIT-2, security and privacy:
`E:\ETP\Reference\Work in progress 2026-10-12\AUDIT-2-SECURITY-PRIVACY.md`). Open item closed by this plan: S-14 (unsigned releases and scripts).
Later phases (8: collections matching, 9: DPDP purge) keep their own plans.

## 8. If something goes wrong

- **A key stick is lost:** the other stick still works. Make a new pair of sticks from it at the next convenient time. If
  both are lost, run the ceremony again (new key), then follow the rotation column above.
- **GitHub account suspected compromised:** Sagar changes the password, revokes all sessions/tokens/apps, checks the
  branch list and recent pushes against `E:\ETP\Code` (every branch exists locally), and tells Claude to stop pushing until
  checked.
- **A shipped file fails its signature check:** do not run it. Take the installer from the USB kit and compare its SHA-256
  with the sums file.
