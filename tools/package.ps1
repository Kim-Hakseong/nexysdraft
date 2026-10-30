# Build the portable release zip: dist\release\NexysDraft-v<ver>-win64.zip
#   NexysDraft.exe (self-contained single file) + wwwroot\ + tools\libredwg\ (binaries, COPYING, SOURCE.txt, build script)
#   + skills\nexysdraft\SKILL.md + skills\cad-headless\SKILL.md + README.md (Korean, 1 page) + LICENSES.md
# Usage (PowerShell, repository root):  powershell -ExecutionPolicy Bypass -File tools\package.ps1 -Version 0.3.0
param([string]$Version = "0.3.0")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"; $dotnet = "$env:DOTNET_ROOT\dotnet.exe"
Push-Location "$root\app\web"; npm run build | Out-Host; Pop-Location
# publish into a fresh folder (app\dist\NexysDraft.exe may be open in a running instance), then refresh app\dist when it is not locked
$dist = "$root\app\dist\release\_publish"; if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }
& $dotnet publish "$root\app\NexysDraft.App\NexysDraft.App.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true -o $dist | Out-Host
if ($LASTEXITCODE -ne 0) { throw "publish failed" }
Get-ChildItem $dist -Include *.xml, *.pdb -Recurse | Remove-Item -Force
try {
    Get-ChildItem "$root\app\dist" -Exclude release | Remove-Item -Recurse -Force -ErrorAction Stop
    Copy-Item "$dist\*" "$root\app\dist" -Recurse
    Write-Host "app\dist refreshed"
} catch { Write-Warning "app\dist not refreshed (in use: $($_.Exception.Message)) — the release zip is built from $dist" }
$stage = "$root\app\dist\release\NexysDraft-v$Version-win64"; New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item "$dist\NexysDraft.exe" $stage
Copy-Item -Recurse "$dist\wwwroot" "$stage\wwwroot"
New-Item -ItemType Directory -Force "$stage\tools\libredwg" | Out-Null
Copy-Item "$root\app\tools\libredwg\*" "$stage\tools\libredwg\" -Recurse
Copy-Item "$root\tools\build-libredwg.sh" "$stage\tools\libredwg\build-libredwg.sh"
if (Test-Path "$root\patches") { Copy-Item -Recurse "$root\patches" "$stage\tools\libredwg\patches" }
New-Item -ItemType Directory -Force "$stage\skills\nexysdraft", "$stage\skills\cad-headless" | Out-Null
Copy-Item "$root\skills\nexysdraft\SKILL.md" "$stage\skills\nexysdraft\"
Copy-Item "$root\skills\cad-headless\SKILL.md" "$stage\skills\cad-headless\"
Copy-Item "$root\packaging\README.md", "$root\packaging\LICENSES.md" $stage
$zip = "$root\app\dist\release\NexysDraft-v$Version-win64.zip"; if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$stage\*" -DestinationPath $zip
$h = (Get-FileHash $zip -Algorithm SHA256).Hash
"$h  NexysDraft-v$Version-win64.zip" | Set-Content -Encoding ascii "$root\app\dist\release\SHA256SUMS.txt"
Write-Host "ZIP $zip $((Get-Item $zip).Length) bytes SHA256 $h"
