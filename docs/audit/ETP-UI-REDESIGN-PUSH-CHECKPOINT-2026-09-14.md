# Source and artifact commit checkpoint — 14 September 2026

This checkpoint commits the r10/r11 density, personal-preference access and accessibility-description changes, tests, current audit/handoff/route records and generated code graphs on `ui/uiux-v4-touch-first-redesign`. It supersedes earlier statements that this work is uncommitted or that the selected artifacts below are local only. The build itself still identifies starting HEAD d24a525; the unchanged immutable source archives identify its actual build inputs. Committing does not require rebuilding or relabelling the installed candidate.

Included portable artifacts under `artifacts/ui-redesign/`:

- r10/r11 installers, release/source manifests and immutable source archives.
- Resumption scripts, build/install logs, failed attempts, installed hash/data-preservation receipts, the r11 evidence archive and final consistency receipt.
- User-supplied Settings screenshot and its observation receipt.
- Retained r9 installer, source archive/manifests and evidence archive/receipts for the original handoff.

VM disks/checkpoints, original database backups and private settings, dependencies, intermediate build directories and duplicate unpacked binaries remain local. The installer contains the executable; its recorded SHA256 identifies the exact installed binary. No production deployment or public release is performed.

Validation: r11 previously passed 669 Release tests; the pre-commit check verifies current runtime source still matches the frozen r11 source manifest. No application source has changed since that successful build. Source line endings may be normalized by Git; the original byte-exact build snapshot is retained separately.

Acceptance remains **INCOMPLETE**. The latest supplied image establishes the Settings overview only. The original sidebar/caption-button reports are unreproduced, the visible `1 tasks` labels are a recorded pending wording defect, and full installed journeys remain UNVERIFIED because native screen capture fails. See [current acceptance](ETP-UI-REDESIGN-R10-2026-09-13.md).

Publication verified: commit **bdba5a79142fd7271156df60206d5f5d22a60057** matched the remote branch; working tree was clean before the subsequent documentation handoff. See [current handoff](ETP-SESSION-HANDOFF-2026-09-14-UI-REDESIGN.md).
