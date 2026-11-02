# Build the portable release zip: dist\release\NexysDraft-v<ver>-win64.zip
#   NexysDraft.exe (self-contained single file) + wwwroot\ + tools\libredwg\ (dwg2dxf, dxf2dwg, libiconv, COPYING, SOURCE.txt, build script;
#   dwgread.exe stays in the repository for development only — draft3 decision) + skills\*\SKILL.md + README.md (Korean, 1 page) + LICENSES.md
# Usage (PowerShell, repository root):  powershell -ExecutionPolicy Bypass -File tools\package.ps1 -Version 0.4.4
param([string]$Version = "0.4.4", [switch]$RefreshDist)   # -RefreshDist: also replace app\dist (off by default — draft5: never touch a deployed copy unasked)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"; $dotnet = "$env:DOTNET_ROOT\dotnet.exe"
Push-Location "$root\app\web"; npm run build | Out-Host; Pop-Location
# publish into a fresh folder; app\dist is refreshed only when no NexysDraft runs from it and its exe is not locked
# (draft3: a running draft1 window kept the exe locked and a delete-then-copy refresh removed wwwroot and tools under it)
$dist = "$root\app\dist\release\_publish"; if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }
& $dotnet publish "$root\app\NexysDraft.App\NexysDraft.App.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true -o $dist | Out-Host
if ($LASTEXITCODE -ne 0) { throw "publish failed" }
Get-ChildItem $dist -Include *.xml, *.pdb -Recurse | Remove-Item -Force
$distExe = "$root\app\dist\NexysDraft.exe"
$running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -and ($_.Path -ieq $distExe) })
$locked = $false
if (Test-Path $distExe) { try { $fs = [System.IO.File]::Open($distExe, 'Open', 'ReadWrite', 'None'); $fs.Close() } catch { $locked = $true } }
if (-not $RefreshDist) {
    Write-Host "app\dist left as it is (pass -RefreshDist to replace it) — the release zip is built from $dist"
} elseif ($running.Count -gt 0 -or $locked) {
    Write-Warning "app\dist not refreshed: NexysDraft is running from it (pid $($running.Id -join ', ')) or its exe is locked — the release zip is built from $dist"
} else {
    Get-ChildItem "$root\app\dist" -Exclude release | Remove-Item -Recurse -Force
    Copy-Item "$dist\*" "$root\app\dist" -Recurse
    Write-Host "app\dist refreshed"
}
$stage = "$root\app\dist\release\NexysDraft-v$Version-win64"; if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }; New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item "$dist\NexysDraft.exe" $stage
Copy-Item -Recurse "$dist\wwwroot" "$stage\wwwroot"
New-Item -ItemType Directory -Force "$stage\tools\libredwg" | Out-Null
Copy-Item "$root\app\tools\libredwg\*" "$stage\tools\libredwg\" -Recurse -Exclude dwgread.exe
(Get-Content "$root\app\tools\libredwg\SHA256SUMS.txt") | Where-Object { $_ -notmatch 'dwgread' } | Set-Content -Encoding ascii "$stage\tools\libredwg\SHA256SUMS.txt"
Copy-Item "$root\tools\build-libredwg.sh" "$stage\tools\libredwg\build-libredwg.sh"
if (Test-Path "$root\patches") { Copy-Item -Recurse "$root\patches" "$stage\tools\libredwg\patches" }
New-Item -ItemType Directory -Force "$stage\skills\nexysdraft", "$stage\skills\cad-headless" | Out-Null
Copy-Item "$root\skills\nexysdraft\SKILL.md" "$stage\skills\nexysdraft\"
Copy-Item "$root\skills\cad-headless\SKILL.md" "$stage\skills\cad-headless\"
Copy-Item "$root\packaging\README.md", "$root\packaging\LICENSES.md" $stage
New-Item -ItemType Directory -Force "$stage\docs" | Out-Null
Copy-Item "$root\docs\keys.md", "$root\docs\pdf-output.md" "$stage\docs\"
if (Test-Path "$stage\tools\libredwg\dwgread.exe") { throw "dwgread.exe must not be in the release" }
$zip = "$root\app\dist\release\NexysDraft-v$Version-win64.zip"; if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$stage\*" -DestinationPath $zip
$h = (Get-FileHash $zip -Algorithm SHA256).Hash
"$h  NexysDraft-v$Version-win64.zip" | Set-Content -Encoding ascii "$root\app\dist\release\SHA256SUMS.txt"
Write-Host "ZIP $zip $((Get-Item $zip).Length) bytes SHA256 $h"
