# Local Windows acceptance VM preparation — 11 September 2026

User chose a VM on this computer. Host checks found Windows 10 Pro, Lenovo 20N8S01H00, i5-8265U with virtualization extensions/SLAT, approximately 16 GB memory and 294 GiB available disk. Firmware virtualization was disabled and no hypervisor was active.

Hyper-V was enabled using a user-approved elevated Windows process with -NoRestart. The result is FEATURE_ENABLE_COMPLETED, restartNeeded=true, at 2026-09-11T14:14:27Z. No reboot, firmware change, VM creation or installer test has been performed.

Preparation kit: artifacts/acceptance-lab-20260911. Includes 1.8.5 and 1.8.6 installers, 12 synthetic workbooks, expected totals, role/coverage/issue registers, checksums, a documented VM creation script and instructions. Scripts parsed successfully but VM creation is unexecuted.

VM definition prepared: ETP-Acceptance-186, Generation 2, two virtual CPUs, fixed 6 GB RAM, 100 GB dynamic disk, virtual TPM, Windows secure boot, Default Switch. Refuses existing VM/directory and leaves the new VM off.

Official Windows Enterprise evaluation ISO download started via BITS job 4584103c-fc3e-4b97-a595-398ea5e04424. Expected size from Microsoft response: 7,092,807,680 bytes. Download metadata is iso-download.json; Microsoft hash reference downloaded as Microsoft-Windows11-HashValues.pdf. Complete-BitsTransfer and SHA-256 comparison remain required; do not run the ISO before verification.

Next user action: save work, restart, use F1 at Lenovo startup, enable Intel Virtualization Technology in firmware (typically Security > Virtualization), save and boot Windows. Do not alter unrelated firmware settings. Then resume this task; verify HypervisorPresent and firmware virtualization, finish/hash-check ISO, create VM and install Windows in the guest, checkpoint clean state, then run acceptance. Any guest Windows account/licence interaction is user-owned.

Sources:
- https://pcsupport.lenovo.com/us/en/solutions/ht500006
- https://learn.microsoft.com/en-us/windows-server/virtualization/hyper-v/get-started/install-hyper-v
- https://www.microsoft.com/en-us/evalcenter/download-windows-11-enterprise

Production data and existing ETP installation were not test targets. Acceptance gates remain open.

## After reboot — 11 September 2026

HypervisorPresent is true; vmms and vmcompute are running. The CPU capability properties report false under the active hypervisor and are not being used to negate successful Hyper-V operation.

ETP-Acceptance-186 was created successfully at 2026-09-11T15:05:16Z and left off. ID: F515A7DD-7BD9-4F4C-B679-A41281F63952. Configuration is Generation 2, two CPUs, 6 GB RAM and 100 GB dynamic virtual disk, with virtual TPM and Windows secure boot. VM creation had a slow initial Hyper-V response; subsequent VMMS logs and CIM inventory confirmed success.

Windows 11 Enterprise evaluation 25H2 EN-US completed and was verified at 2026-09-11T15:13:23Z. Size 7,092,807,680 bytes; SHA256 A61ADEAB895EF5A4DB436E0A7011C92A2FF17BB0357F58B13BBC4062E535E7B9 matches Microsoft's downloaded hash PDF (relevant page visually inspected). BITS job completed.

ETP-Acceptance-Kit.iso was created from 19 test files, with each file read back from the finished ISO and hash-compared. Size 98,109,440 bytes; SHA256 11E2D67F7033D8D5AD5D993688BBCEA7A8ACBA45D48DD983645A910F982FC8D7. This disk carries the two candidate installers and synthetic fixtures; no working host folders need sharing.

Attaching both verified images and opening the console is the next step. Windows guest installation, clean checkpoint and all installed acceptance scenarios remain pending until observed. Logs and machine-readable receipts remain under artifacts/acceptance-lab-20260911.

## Console handoff

Both verified ISO images were attached successfully at 2026-09-11T15:14:15Z. VMConnect is open on the host. Start-VM succeeded; Hyper-V reports Running (State=2), Operating normally, 6 GB assigned memory, two CPUs. Windows setup completion is not yet verified.

Computer Use screenshot capture again failed with SetIsBorderRequired / 0x80004002. Text-only recovery reports that VMConnect has higher Windows integrity than the helper and exposes only the window/title bar. Guest setup cannot be reliably automated with these tools. User was directed to press a key in the VM if the optical boot prompt appears and complete Windows installation. No guest account, licence acceptance, Windows disk partitioning or ETP installer action was automated.

Next: user reports Windows desktop visible; verify guest state, detach Windows installation media when appropriate, take a clean Windows checkpoint, then execute isolated install/upgrade and role UAT. Keep the ETP test-data ISO attached. No host ETP installation or production database was modified.
