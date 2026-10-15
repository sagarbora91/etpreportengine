# 1.12.0 WAVE 2 copy (integrator, 13 Oct 2026): same as the wave-1 script, pointed at the integrate/112-w2 worktree.
# After Build-Installer-1.12.0.ps1 exits 0: copy the installer + release.json + build log to Installers\ETP Reporting Engine 1.12.0\ with SHA256SUMS.
$ErrorActionPreference = 'Stop'
$wt = 'E:\ETP\Code\Worktrees\integrate-112-w2'
$src = Get-ChildItem "$wt\artifacts" -Directory -Filter 'installer-*' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $src) { throw 'No artifacts\installer-* folder found.' }
$dst = 'E:\ETP\Installers\ETP Reporting Engine 1.12.0'
# Plan section 5: the wave-1 installer is kept as 1.12.0-wave1 evidence and never placed in the OneDrive feed.
$wave1 = "$dst-wave1"
if ((Test-Path $dst) -and -not (Test-Path $wave1)) { Rename-Item -LiteralPath $dst -NewName (Split-Path $wave1 -Leaf) }
if (Test-Path $dst) { $old = "$dst\superseded-$(Get-Date -Format yyyyMMdd-HHmm)"; New-Item -ItemType Directory $old | Out-Null; Get-ChildItem $dst -File | Move-Item -Destination $old }
New-Item -ItemType Directory -Force $dst | Out-Null
Copy-Item "$($src.FullName)\*" $dst -Recurse
Get-ChildItem 'E:\ETP\Migration 2026-10-04' -Filter 'installer-build-1.12.0-*-w2b1.log' | Sort-Object LastWriteTime | Select-Object -Last 1 | Copy-Item -Destination $dst
Get-ChildItem $dst -File | Where-Object Name -ne 'SHA256SUMS.txt' | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash, $_.Name } | Set-Content "$dst\SHA256SUMS.txt"
Get-Content "$dst\SHA256SUMS.txt"


