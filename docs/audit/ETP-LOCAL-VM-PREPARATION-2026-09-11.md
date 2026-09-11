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
