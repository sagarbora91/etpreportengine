# ETP licensing requirements traceability

Status legend:

- **Engineered** — design/data/test/operation authority is complete.
- **Deferred** — product implementation is deliberately held until final integration.
- **Final input** — an owner/environment value is intentionally collected at implementation time.

| # | Requirement | Engineering disposition | Authority |
|---:|---|---|---|
| 1 | critical offline/copy/security requirements | Engineered | Engineering spec §§1–2, 4, 10 |
| 2 | separate identity/signature/device controls | Engineered | Spec §§2, 5–8; LIC-009 |
| 3 | inspect actual repository | Engineered | Spec §3; Graphify/CRG audit evidence |
| 4 | no MainWindow licensing logic | Engineered | Spec §12; LIC-001 |
| 5 | modular target architecture | Engineered | Spec §§5, 12 |
| 6 | supported Microsoft identity/MSAL | Engineered; package selected later | Spec §6; app-registration guide |
| 7 | Outlook/personal account support | Engineered | App-registration guide account audience |
| 8 | not email-only authorization | Engineered | Spec §6 `(tid, oid)` policy |
| 9 | owner allowlist, no desktop secret | Engineered; owner IDs are Final input | Spec §§6, 19 |
| 10 | event-based owner authentication | Engineered | Spec §§6, 10 |
| 11 | offline normal startup | Engineered; Deferred | Spec §§8, 10 |
| 12 | first activation flow | Engineered; Deferred | Spec §10 |
| 13 | issuance-mode decision | Engineered: separate owner utility selected | Spec §§1, 5 |
| 14 | private/public key architecture | Engineered | Spec §§7, 11 |
| 15 | established cryptography | Engineered | Spec §7 |
| 16 | versioned licence payload | Engineered | Spec §7; licence JSON schemas |
| 17 | robust device binding | Engineered | Spec §8 |
| 18 | Windows machine-protected storage | Engineered; Deferred | Spec §8; test LIC-T100–T115 |
| 19 | secure non-install storage | Engineered | Spec §8; Operations guide |
| 20 | human device ID | Engineered | Spec §8 |
| 21 | activation screen | Engineered; Deferred | Spec §14 |
| 22 | official Microsoft UI | Engineered; Deferred | Spec §6; app-registration guide |
| 23 | minimal auth result/token retention | Engineered | Spec §6; app-registration guide |
| 24 | login does not replace licensing | Engineered | Spec §§2, 10; LIC-009 |
| 25 | employees cannot issue licence | Engineered | Spec §§5, 11–12; LIC-011 |
| 26 | copied install attack | Engineered test; Deferred execution | Test LIC-T105; mandatory copy attack |
| 27 | copied install + licence | Engineered test; Deferred execution | Test LIC-T104/T106 |
| 28 | copied machine state | Engineered test; Deferred execution | Test LIC-T103/T106 |
| 29 | fake licence | Engineered test | Test LIC-T002 |
| 30 | tampered licence | Engineered test | Test LIC-T003/T004 |
| 31 | unapproved Microsoft account | Engineered test | Test LIC-T202/T203 |
| 32 | Microsoft password security | Engineered | Spec §6; app-registration guide |
| 33 | offline restart | Engineered test; Deferred execution | Test LIC-T304/T305 |
| 34 | no recurring token refresh | Engineered | Spec §§6, 10 |
| 35 | perpetual device licence | Engineered | Spec §7; payload schema |
| 36 | PC replacement | Engineered | Spec §10; Operations guide |
| 37 | Windows reinstall/drive/motherboard | Engineered | Spec §8 hardware-change policy |
| 38 | normal hardware changes survive | Engineered | Spec §8; tests LIC-T112–T114 |
| 39 | owner administration utility | Engineered; Deferred | Spec §§5, 12, 14 |
| 40 | licence administration record | Engineered | Operations guide history schema |
| 41 | authorization audit | Engineered; Deferred | Spec §13 |
| 42 | centralized startup enforcement | Engineered; Deferred last | Spec §§3, 10, 12; backlog phase 6 |
| 43 | licence service API | Engineered | Spec §12 |
| 44 | device service | Engineered | Spec §12 |
| 45 | owner auth service | Engineered | Spec §§6, 12 |
| 46 | activation coordinator | Engineered | Spec §§5, 12 |
| 47 | explicit status model | Engineered | Spec §12 |
| 48 | fail closed | Engineered | Spec §§7, 15 |
| 49 | safe user-facing errors | Engineered | Spec §14 |
| 50 | data security separate | Engineered audit | Spec §18 |
| 51 | licence signing vs Authenticode | Engineered | Spec §18 |
| 52 | unsigned-EXE limitation | Engineered | Spec §§4, 17 |
| 53 | optional obfuscation later | Engineered deferment | Backlog phase 7 |
| 54 | no hard-coded secrets | Engineered controls | Spec §§11, 19; `.gitignore`; scans |
| 55 | Microsoft app-registration guide | Engineered | `ETP_MICROSOFT_APP_REGISTRATION.md` |
| 56 | no unnecessary Graph permissions | Engineered | App-registration guide Permissions |
| 57 | Microsoft-managed MFA | Engineered | Spec §6; app-registration guide |
| 58 | Graphify audit/re-index | Audit complete; implementation re-index Deferred | Spec §3; backlog phases |
| 59 | Obsidian licensing architecture | Engineered | `knowledge/01-Architecture/ETP Licensing Architecture.md` |
| 60 | incremental implementation phases | Engineered; all runtime phases Deferred | Implementation backlog |
| 61 | test matrix | Engineered | `ETP_LICENSING_TEST_MATRIX.md` |
| 62 | reporting/import regression | Engineered; current baseline green | Test matrix Regression gate |
| 63 | performance | Engineered | Spec §15 |
| 64 | LIC-001–LIC-010 guardrails | Engineered and extended | Spec §16 |
| 65 | required A–M pre-code report | Engineered | Spec §§3, 6–8, 10–12, 17 |
| 66 | no big-bang implementation | Engineered | Implementation backlog/commit sequence |
| 67 | final PC-A/PC-B acceptance | Engineered test; Deferred execution | Test matrix Mandatory final copy attack |
| 68 | security model summary | Engineered | Spec §2 and architecture note |
| 69 | practical maintainable design | Engineered | ADR-006 and spec §1 |

## Current release state

```text
Engineering authority: COMPLETE
Production private key: NOT CREATED
Microsoft app registration values: NOT STORED
Product licensing code: NOT IMPLEMENTED
Startup enforcement: DISABLED / ABSENT
Existing application behavior: UNCHANGED
```

This is the intentional state requested before final product integration.
