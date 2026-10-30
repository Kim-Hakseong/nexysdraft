# 확정 사실 누적 (findings) — NexysDraft

규칙: 확정 사실만 기록, 각 항목에 근거. 추정은 UNVERIFIED. 새 세션은 이 파일부터 읽는다.

## 2026-10-29 draft1

### 환경·도구
- .NET SDK 8.0.425(`%LOCALAPPDATA%\Microsoft\dotnet`), Node 24.22, Vite 6.4, WebView2 Evergreen(Edge). Python 3.13(기본)에는 PIL이 없고 `Python312`에 PIL+numpy가 있다(tools/pixel_diff.py는 Python312로 실행).
- LibreDWG 0.14 win64가 latest 릴리스(GitHub API). `dwg2dxf` 읽기 R13~R2018, `dxf2dwg` 쓰기 r12/r14/**r2000(기본)**/r2004, r2007+ 미지원. 상세 docs/libredwg.md.
- dwg2spdf(C:\Projects\dwg2spdf-ralph-starter\dwg2spdf)는 `uv run --no-sync --project <dir> dwg2spdf convert <dwg> --out <dir> --merge --converter libredwg`로 호출. `.dwg`만 입력으로 받는다(cli.py `_stage_files`).

### DXF/DWG 형식
- LibreDWG `dwg2dxf` 출력은 AC1024(2010) 파일도 문자열을 `$DWGCODEPAGE`(ANSI_949) 바이트로 쓴다(UTF-8 아님). 사용자 샘플 7개 모두 ANSI_949. 값 단위로 "ASCII → 엄격 UTF-8 → 코드페이지" 순으로 디코딩하면 한글 깨짐 0건(work/gates/d1_korean.txt).
- MTEXT 서식: 샘플에 `\fGulimChe|b1|i0|c129|p49;`, `\f굴림|…`, `\fHeadline R|…`, `\P`, `\W`, `\T`, `\H…x`, `\U+2460`, `{}`가 실제로 쓰인다. `\M+` 이스케이프는 샘플에 없음.
- `dxf2dwg`(0.14)는 **MTEXT text_height를 0으로 기록**한다(자기 DXF 왕복에서도, u01 187개 전부). 대책: R2000 내보내기 시 `\H<높이>;` 절대 높이 코드 접두.
- `dxf2dwg`는 BLOCK_RECORD 핸들이 0인 블록을 찾지 못해(`Unknown INSERT.block_header`) 블록 내용을 버린다. `$HANDSEED`보다 큰 핸들이 테이블/VERTEX/ATTRIB에 있는 DXF가 있어, 새 핸들은 파일 안의 모든 5/105 코드 최대값+1부터 발급해야 충돌이 없다.
- `dxf2dwg`에 MTEXT 코드 50(회전각)을 주면 "Invalid DXF code 50 for MTEXT"로 실패한다 → 항상 11/21/31(X축 방향)로 쓴다.
- `dxf2dwg` 속도는 파일 크기에 초선형: 200 KB 2 s, 600 KB 109 s, 1.0 MB 278 s, 1.7 MB·21 MB는 300 s 초과.
- 우리 R2000 DXF를 dxf2dwg→dwg2dxf로 돌리면 LINE·LWPOLYLINE·POLYLINE·CIRCLE·ARC·ELLIPSE·INSERT 좌표가 1e-12 이내로 일치한다(사용자 도면 6개, work/gates/d1b_dwg_roundtrip.txt). TEXT는 u01 1개·u05 4개가 사라진다(UNVERIFIED 원인). u06(동적 블록 도면)은 `*U` 표현 블록 내용이 사라져 LINE 752→357.
- 리더는 OCS 법선 (0,0,-1)인 CIRCLE/ARC/LWPOLYLINE/TEXT/INSERT를 읽을 때 WCS로 정규화한다(x 부호 반전, 각도 180-a, bulge 부호 반전).

### 앱·렌더
- WebGL 선 패스(1회 drawArrays, 190만 세그먼트) + Canvas2D 오버레이(텍스트·솔리드 채움·선택·스냅)로 10만 엔티티 팬·줌 중앙값 0.6~1.5 ms(work/selftest/perf/gates.txt). 글꼴은 STYLE의 TTF 파일명→패밀리 매핑, .shx·미상은 'Malgun Gothic' 폴백.
- 10만 엔티티(21.5 MB DXF) 열기 분해: 토큰화 0.31~0.54 s, 엔티티 생성 0.61~0.90 s, JSON 직렬화 0.49~0.65 s(Utf8JsonWriter), 페이지 기하 0.44~0.52 s, 첫 프레임 0.15 s. Server GC(`DOTNET_gcServer=1`)에서 합계 2.08 s(워크스테이션 GC 2.76 s).
- 사용자 경로 셀프테스트(`--selftest`) 전 항목 PASS: 사용자 DWG 열기(LibreDWG 118 ms) → 한글 TEXT/MTEXT 12건 깨짐 0 → 창 선택 4 → MOVE/COPY 명령줄 → 스냅 END → LINE/CIRCLE/PLINE/DLI 그리기(치수 100.00) → 더블클릭 텍스트 편집 "한글 수정 TEXT" → 새 레이어·끄기 → Undo/Redo → DXF 저장·재열기 동일(25=25) → DWG 저장(99 ms)·재열기 좌표 25/25 → PDF(dwg2spdf, 2.0 s) → accent 테두리 0 → WS 187 MB → UI 정지 0.

### 관문 최종 (2026-10-29 저녁)
- xUnit 22/22 통과(13 m 33 s, dxf2dwg 시간 포함). DXF 자체 왕복(a) 12/12 PASS, 좌표 maxErr 0.0. 한글 7,673건 중 422건 한글, 깨짐 0.
- `--stress --iter 20`: 예외 0, 300 ms 정지 0, 메모리 +153 MB(최종, Server GC; 이전 +147) / +113 MB(워크스테이션 GC, 정지 1회); 40회 +165 MB → 20→40회 구간 +18 MB(초기 상승 후 완만, 원인 UNVERIFIED).
- 10만 엔티티 열기 1.86 s(Server GC·QuickJitForLoops off, csproj에 고정), 팬·줌 0.6~1.4 ms.
- 문서 교체 시 강제 gen2 GC(Server GC, 비동시)는 UI 정지 >300 ms로 나타난다 → 제거. Close()의 Optimized 수집만 유지.
- 목업 vs 구현(tools/pixel_diff.py): 헤더 0.87·파일 레일 0.81·푸터 0.97 OK, 우측 레일 0.58·툴바 0.58·명령줄 0.67 DIFF(행 수·자리표시자 차이), 패널 열 경계 좌측 레일 2 px 차이.

## 2026-10-30 draft2

### ODA File Converter (개발 PC 검증 전용)
- 27.1.0이 `C:\Program Files\ODA\ODAFileConverter 27.1.0\`에 있다(Program Files 탐색·레지스트리 Uninstall·시작 메뉴 .lnk 세 경로 모두 일치, where.exe에는 없음). config/dev.json에 기록, tools/oda.py·OdaGateTests가 사용. 더미 변환 DXF→DXF 1.2 s.
- ODA는 폴더 단위 변환이며 `.err` 파일로 오류를 돌려준다. 읽기 실패 시 0바이트 출력 파일이 남는 경우가 있다(u01·u07 DWG 1차).

### DXF 호환 (F1)
- nanoCAD는 AC1015/AC1018/AC1024 DXF의 cp949 바이트 문자열을 "??"로 보인다(사용자 실측) → 라이터는 모든 비ASCII를 `\U+XXXX`로 쓰는 순수 ASCII 출력으로 바꿨다. ODA DXF→DXF 재변환: 사용자 도면 7개 + 생성 5개 + selftest 모두 읽기 오류 0, 엔티티 수·레이어 수(ODA가 추가하는 Defpoints 제외) 일치, 한글 문자열(글자만 비교) 동일(work/gates/f1_oda_dxf.txt).
- ODA는 MTEXT 줄 끝 공백을 지우고 `\S1^2;`를 `\S1<U+FFF2>;`로 다시 쓴다. 치수가 있으면 Defpoints 레이어를 만든다.
- nanoCAD "24/25": ODA 재변환에서는 25/25(INSERT 16·ATTRIB 16·DIMENSION 4·HATCH 1·LINE·CIRCLE·LWPOLYLINE·TEXT) — 빠진 엔티티 미특정(UNVERIFIED).

### DWG 쓰기 (F2) — docs/libredwg.md 표
- ODA는 LibreDWG 0.14 dxf2dwg의 기본 출력(LAYOUT/PLOTSETTINGS 포함)을 하나도 읽지 못한다. OBJECTS를 루트 DICTIONARY 하나로 줄이고 BLOCK_RECORD 340 링크를 빼면 읽는다(varE). nanoCAD RECOVER 원인("AcDbLayout ModelType flag invalid")과 같은 뿌리로 추정(UNVERIFIED — nanoCAD 재확인 필요).
- 소스의 낮은 핸들(`*Paper_Space` ENDBLK 1F)이 dxf2dwg 내부 핸들과 충돌해 ODA가 0바이트를 낸다 → BLOCK/ENDBLK 핸들 재발급.
- dxf2dwg는 엣지형 HATCH 경계를 버린다(폴리라인·타원 루프는 유지) → R2000 내보내기에서 엣지 루프를 폴리라인 루프로 테셀레이션. dxf2dwg 왕복 뒤 MTEXT rect_width가 1.0이 된다 → PDF·페이지 렌더는 폭 < 2×높이를 "줄바꿈 없음"으로.
- draft1 리더 결함: HATCH 루프를 붙이지 않아 파일의 해치가 전부 경계 0개였다(nanoCAD 해치 미표시의 원인 일부). 수정.
- u06(동적 블록) DWG는 우회 후에도 ODA "Object improperly read"(blockers #9). u03은 dxf2dwg 300 s 초과(SKIP).

### PDF 내장 (F3)
- 자체 PDF 1.4 라이터(Pdf/PdfWriter·TrueTypeFont·SheetDetector·DrawingPdf): 맑은 고딕(C:\Windows\Fonts\malgun.ttf, 13 MB) 글리프 제로잉 서브셋(CIDFontType2·Identity-H·ToUnicode), Flate 콘텐츠. dwg2spdf 의존 제거.
- 시트 감지: 블록 인서트 ≥2(겹침 ≤5 %, 중심 포함 ≥60 %) 또는 같은 크기 사각형 반복. u05 9시트+rest 10쪽, u04 3쪽, u07 2쪽, 나머지 1쪽.
- pdftotext 추출률(work/gates/f3_pdf.txt): u01 99.25 %, u02 100 %, u03 100 %, u04 99.75 %, u05 99.52 %, u06 96.0 %, u07 99.01 %, selftest 100 %. 1 MB 도면(u05) 2.1 s, 1.7 MB(u03) 1.7 s. PDF 크기 28 KB~943 KB.
- Malgun Gothic OS/2 sCapHeight 기준 글꼴 크기 = 높이/CapHeightEm(≈0.72 em).

### CLI·스킬 (F4)
- `--script` 러너(ScriptRunner): 명령 19종, result.json(줄별 result/error/ms), exit 0/1/2, `AttachConsole`로 stdout. `--skilltest`: SKILL.md 예시 2 실행 → PDF 1쪽·텍스트 2 → pdftotext "600"·"2000" 발견, 0.63 s.
