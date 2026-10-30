# NexysDraft

개인·사내용 경량 2D CAD (DWG/DXF 열기·편집·저장, 검색 가능한 PDF, 헤드리스 CLI). C# .NET 8 + WebView2 (TypeScript, WebGL).

- 열기: DWG(LibreDWG `dwg2dxf`, 별도 프로세스) / DXF(자체 파서, R12~2018, 한글 cp949·UTF-8·`\U+`).
- 편집: 선택·이동·복사·회전·대칭·간격띄우기·신축·삭제, 선/폴리선/원/선형 치수, 텍스트 편집, 레이어, Undo/Redo, AutoCAD식 명령줄.
- 저장: DXF(원본 보존, 순수 ASCII) / DWG R2000(LibreDWG `dxf2dwg`) / PDF(자체 라이터, 맑은 고딕 서브셋, 시트 분할).
- CLI: `NexysDraft.exe --script job.txt --out dir --json` (skills/nexysdraft/SKILL.md).

빌드: `cd app/web && npm install && npm run build`, `dotnet build app/NexysDraft.App/NexysDraft.App.csproj -c Release`. 테스트: `dotnet test app/NexysDraft.Tests`.
배포본: `dotnet publish app/NexysDraft.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o app/dist` (+ `tools/libredwg/`).
LibreDWG(GPLv3)은 `app/tools/libredwg/`에 바이너리·COPYING·SOURCE.txt로 동봉되며 링크하지 않는다. 문서: docs/.
