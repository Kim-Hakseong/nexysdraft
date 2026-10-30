# Licenses — NexysDraft 0.3.0 portable package

| Component | Where | License | Notes |
|---|---|---|---|
| NexysDraft | `NexysDraft.exe` | All rights reserved (in-house use) | C# / .NET 8, WebView2 page |
| .NET 8 runtime | inside `NexysDraft.exe` (self-contained) | MIT | https://github.com/dotnet/runtime |
| Microsoft.Web.WebView2 SDK | inside `NexysDraft.exe` | BSD-3-Clause style (Microsoft WebView2 SDK license) | The WebView2 *runtime* is part of Windows / Edge and is not shipped |
| GNU LibreDWG | `tools\libredwg\` (dwg2dxf.exe, dxf2dwg.exe, dwgread.exe, DLLs) | **GPL-3.0-or-later** — full text in `tools\libredwg\COPYING` | Run as separate programs only; not linked. Source: `tools\libredwg\SOURCE.txt` (release / commit and download URL). Build script: `tools\libredwg\build-libredwg.sh` |
| libiconv (LibreDWG dependency) | `tools\libredwg\libiconv-2.dll` | LGPL-2.1-or-later | MSYS2 mingw-w64 package; LibreDWG is linked statically into each .exe (commit 33bfac15, see SOURCE.txt) |
| Inter, Noto Sans KR, JetBrains Mono, Instrument Serif | inside `NexysDraft.exe` (page fonts) | SIL Open Font License 1.1 | UI fonts |
| Malgun Gothic | not shipped | Windows system font | read from `%WINDIR%\Fonts` for PDF text (subset embedded into the PDFs you create) |

ODA File Converter is **not** part of this package and is never called by NexysDraft.
