# Rebuild the LibreDWG CLI tools with MSYS2 (mingw64) and install them into app\tools\libredwg.
# Usage: powershell -ExecutionPolicy Bypass -File tools\build-libredwg.ps1 [-Ref <commit>] [-Install]
param([string]$Ref = "33bfac155464cd560f41537ae627efbd66ed7348", [switch]$Install)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$bash = "C:\msys64\usr\bin\bash.exe"
if (-not (Test-Path $bash)) { throw "MSYS2 not found (winget install MSYS2.MSYS2)" }
$env:MSYSTEM = "MINGW64"; $env:CHERE_INVOKING = "1"
$src = "/c/rea-work/draft/work/g0/libredwg"; $out = "/c/rea-work/draft/work/g0/out_$($Ref.Substring(0, [Math]::Min(8, $Ref.Length)))"
& $bash -lc "pacman -S --noconfirm --needed git base-devel autoconf automake libtool make texinfo mingw-w64-x86_64-gcc mingw-w64-x86_64-libiconv mingw-w64-x86_64-pcre2 python && bash /c/rea-work/draft/tools/build-libredwg.sh $Ref $src $out"
if ($LASTEXITCODE -ne 0) { throw "build failed" }
if ($Install) {
    $win = $out -replace '^/c/', 'C:\' -replace '/', '\'
    $dst = "$root\app\tools\libredwg"
    Get-ChildItem $dst -Include *.exe, *.dll -Recurse | Remove-Item -Force
    Copy-Item "$win\*" $dst
    Get-ChildItem $dst -Include *.exe, *.dll | Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash)  $(Split-Path -Leaf $_.Path)" } | Set-Content -Encoding ascii "$dst\SHA256SUMS.txt"
    Write-Host "installed into $dst"
}
