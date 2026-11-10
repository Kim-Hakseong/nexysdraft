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

### 최종 관문 (2026-10-30 저녁)
- F1 ODA DXF→DXF: 12/12 PASS(읽기 오류 0, 엔티티 동일, 레이어 집합 동일, 한글 글자 동일). 리더가 모든 문자열의 U+ 이스케이프를 읽을 때 풀도록 바꿈(레이어·블록명 포함).
- F2 ODA DWG→DXF: u02(238 s)·u04(136 s, HATCH 54/54)·g01·g02·g03·selftest PASS. u01 0바이트(무오류), u06·u07 BlockBegin owner invalid, u05 타임아웃, u03 SKIP, g04 SPLINE 소실.
- F3 pdftotext: 11/12 ≥99 %(u06 96 %). g02 68.75 → 100 %(MTEXT 범위 추정).
- `--selftest` 전 항목 PASS(UI 정지 0 — 감시자를 페이지 준비 뒤에 시작), `--skilltest` 8/8, `--stress --iter 40`: 20→40회 +20.8 MB(관문 100 MB PASS), 정지 1회 439 ms(테스트 스위트의 dxf2dwg와 동시 실행 중).
- dist: app/dist/NexysDraft.exe 211 MB + tools/libredwg + wwwroot, `--shot`·`--script` 스모크 OK.
- F5: 새 clone(github.com/Kim-Hakseong/nexysdraft, private) 빌드 → dotnet test 26/26(15 s) → --selftest PASS → 태그 v0.2.0-draft2. 추적 파일에 사용자 도면·회사명 0건.

## 2026-10-30 draft3

### LibreDWG (G0) — docs/libredwg.md, docs/libredwg-build.md
- MSYS2(winget) + mingw64로 LibreDWG master `33bfac15`(0.14.8679) 정적 빌드 1회차 성공. 패치 없음. 번들 교체.
- ODA 대리 관문(사용자 도면 7개): 0.14 **1/7**(u04 126 s, u02·u03·u05 300 s 초과) → master **6/7**, 최대 1.7 s(u03 10 MB). u05 0.67 s. 10만 엔티티 R2000 DXF(21.5 MB) 6.4 s, ODA 읽기 정상.
- draft2에서 "dxf2dwg 0.14 결함(UNVERIFIED)"로 남긴 u01·u07 빈 출력의 원인은 **원본 ByLayer 선종류 핸들 `B`** (이분 탐색: 엔티티 0개로도 실패, LTYPE 항목 제거 시 통과, B만 바꾸면 통과). 0.14에서도 같은 수정으로 u01 통과. 라이터가 표준 선종류 3개에 새 핸들을 준다.
- u06 실패는 IMAGE 1개(핸들 5F). g04 스플라인 소실은 표본의 매듭 수 오류(10개, 9개여야 함).

### 공간·글꼴 (G1)
- 7개 도면 모형/페이퍼 엔티티 수가 ODA와 일치(work/gates/g1_spaces.txt). u04는 페이퍼 스페이스 엔티티가 없다(ODA도 Layout1 빈 배치).
- u04 "모형 뷰의 도곽"은 페이퍼 스페이스가 아니라, LibreDWG가 이름을 잃은 블록(LINE 2,255개)을 이름 "" 로 내보내고 INSERT도 "" 를 가리켜 8.6배로 그려진 것. 리더가 이름 없는 블록을 `*NONAME~n`으로 분리 → u04 범위 = 두 시트 범위(폭 비 1.129, 높이 비 1.000, work/gates/g1_u04_extents.txt).
- 현재 R2000 라이터(OBJECTS = 루트 사전)로 페이퍼 스페이스 엔티티가 dxf2dwg→ODA에서 보존됨(g06_paper 8/8, u05 2/2). LAYOUT 객체를 다시 넣을 필요 없음.
- 글꼴 호환: 한글을 실제로 쓰는 스타일 중 PC에 없는 글꼴만 치환(u01 0, u02 0, u03 2, u04 3, u05 3, u06 1, u07 1; 치환 후 없는 글꼴 참조 0, work/gates/g1_fonts.txt).

### 저장 정책 (G2)
- 사전 점검의 "읽기 불가 예상"(IMAGE·R2000 이후 객체·시간 초과)이 ODA 결과와 **7/7 일치**(work/gates/g2_check.txt). 동적 블록(u02·u03)은 읽히지만 정책상 DXF 기본.
- 이름 없는 블록·해석 불가 삽입은 막지 않고 손실 안내(라이터가 빼고, u04는 ODA 통과).

### PDF (G3)
- 용지 자동: 모델 플롯 설정 → 최근접 ISO, 없으면 A3. u01·u03·u04·u05·u07 A3 가로, u02·u06 A4 가로. 추출률 99.25~100 %, u04 2쪽, u05 10쪽(work/gates/g3_pdf.txt).
- 사용자 도면의 용지상 글자 높이 중앙값 0.28~1.74 mm — "≥1.5 mm" 와 "A3" 를 함께 만족할 수 없다(blockers #18).
- MTEXT 파서 결함: 절대 `\H4;` 뒤의 상대 `\H1.5x;`가 절대 표시를 지워 기본 높이의 6배로 그렸다(g02 "큰 글자"). 수정.

## 2026-10-30 draft4

### nanoCAD 실측 (사용자, d3_user_check)
- u01·u07 .nexys.dwg: 오류 없음, 한글 정상. selftest_saved.dwg 25/25(blockers #11 해소).
- u04 .nexys.dwg: 오류 없음, 두 시트 정상, 한글 "???"(글꼴 호환 ON).
- u04 .nexys.dxf: "This version of file format is not supported". 원본 AC1032 그대로 저장한 것이 원인이고, nanoCAD 5는 AC1027까지만 연다.

### DWG의 ACAD xdata (H0, 확정)
- LibreDWG dxf2dwg(master 33bfac15)는 DXF의 `1001 ACAD` xdata APPID 참조를 DWG에 **항상 핸들 0x12**로 쓴다.
  - 근거: `dwgread -O JSON`의 STYLE eed handle `[5,1,18]`. u04(ACAD = 0x20), u01·u07(ACAD = 0x26)에서 같았다. APPID 표 순서, ACAD만 남기기, ACAD 핸들 0xFFFF0 모두 0x12였다.
  - ACAD가 아닌 앱 이름은 0으로 쓴다(u04 266개 → ODA `_APP0`).
  - 예외: 생성 도면(매트릭스)에서 APPID 표를 STYLE 앞에 두면 실제 핸들로 풀렸다. u04에서는 효과가 없었다.
- draft3 DWG(u01·u04·u07)의 STYLE 패밀리 xdata는 모두 없는 객체를 가리켰다(ODA: `1001 _APP12`).
- 수정: R2000 라이터가 ACAD APPID에 핸들 12를 준다. 12가 이미 쓰이면 경고를 남기고 예전대로 쓴다. 이후 u04 DWG의 Standard·돋움·굴림은 ODA에서 `1001 ACAD`로 읽힌다(work/gates/h0_u04_styles.txt).
- draft3에서 이 결함을 못 본 이유: `tools/oda.py`가 `x.dwg`와 `x.dxf`의 결과를 같은 `x.oda.dxf`로 써서 DWG 결과가 덮어쓰였다. 이제 `x.dwg.oda.dxf`로 쓴다.

### u04 글꼴 (H0)
- TEXT는 모형 516개(블록 포함 974개)이고, 모형의 한글 TEXT 38개는 Standard 31개, 돋움 7개다. MTEXT는 모형에 1개, 인라인 `\f`는 0개다.
  - 치환 대상은 Standard·돋움·굴림 3개로 맞다(원본 `Gulim.ttf`·`Dotum.ttf`는 PC에 없음, Windows에는 gulim.ttc). 치환 누락은 없다.
- draft3가 실제로 쓴 형식은 **B**(그룹 3 비움 + xdata `Gulim`/`Dotum`, 1071 = 33074)이고, 지시서의 A(`\U+` 한글 패밀리)가 아니다.
- u01 'PDF Arial'(그룹 3 비움 + `Arial`, 336)도 draft3 DWG에서 xdata가 끊겨 있었는데 nanoCAD에서 한글이 보였다. 그래서 u04 "???"가 끊긴 xdata 탓인지는 **UNVERIFIED**이고, 매트릭스 확인으로 판정한다.
- 기본 치환을 C(그룹 3 `gulim.ttc` + xdata `Gulim`)로 바꿨다. 한글 패밀리 이름을 가진 스타일도 치환 대상에 넣고, 글꼴 문자열은 ASCII만 쓴다(docs/font_matrix.md).
- 매트릭스 DWG를 LibreDWG dwg2dxf로 읽으면 D·E에 `ACAD malgun 34` xdata가 하나 더 붙는다. ODA로 읽으면 쓴 그대로다. dwg2dxf 쪽 산물이다.

### DXF 캐럿 (H0)
- DXF 문자열의 `^X`는 제어 문자 부호다. u04의 SHX 한글(스타일 SHS, ses+shs, 이름 없는 블록 안 30개 중 17개에 `^`)은 LibreDWG dwg2dxf가 `^*`처럼 부호 없이 내보낸다.
  - 우리가 그것을 그대로 쓰면 ODA는 U+FFEA 같은 문자로 읽었다(d3 DXF).
  - 이제 쓰기에서 `^` → `^ `, 읽기에서 `^ ` → `^`로 처리한다. ODA 재읽기에서 30/30이 같았다.
  - 이 텍스트는 이름 없는 블록 안에 있어 R2000 저장에서는 원래 빠진다.

### DXF 버전 (H1)
- 기본 DXF 저장: 원본 > AC1027이면 R2000 모델 라이터, 아니면 원본 유지. 사용자 도면 7개 중 u04·u05가 AC1032 → AC1015로 저장된다.
- 7/7: $ACADVER ≤ AC1027, ODA 오류 0, 모형 엔티티 수·레이어·글자 수·한글·페이퍼 수가 같다(work/gates/h1_dxf_version.txt).

### IMAGE 제외 DWG (H2)
- u06(IMAGE 1개, 핸들 5F)을 IMAGE를 빼고 DWG로 저장했다. dxf2dwg 245 ms, ODA 오류 0, 나머지 모형 엔티티 수와 한글 16/16이 같다(work/gates/h2_u06_noimage.txt).

### 10만 엔티티 열기 (H2)
- 관문을 ≤2 s로 바꿨다. 이번 측정은 다른 프로그램 부하(CPU 52 %)에서 2.26~3.78 s였다.
- 같은 조건에서 v0.3.0 배포본은 2.08~3.72 s였다. 열기 경로에서 바뀐 코드는 캐럿 처리 한 줄뿐이다(문자열 값 67만 개에 짧은 IndexOf, 10~20 ms로 추정). 그래서 회귀가 아니라 측정 환경 탓이다.
- draft3의 1.86 s는 부하가 낮을 때 잰 값이다.

## 2026-10-31 draft5

### 입력 (I1)
- 페이지의 keydown은 document 리스너 하나로 처리한다. draft4까지는 캔버스 자체의 keydown 리스너도 Enter/Space에서 `key()`를 불러서, 캔버스에 포커스가 있으면 확정·반복이 두 번 실행될 수 있었다.
- 점 입력 순서: 객체 스냅(F3)이 잡히면 그 점, 아니면 그리드 스냅(F9)을 적용한 뒤 직교(F8, Shift를 누르면 반대) 또는 극좌표 추적(F10, ±5°)을 적용한다. 직교는 기준점의 x 또는 y를 그대로 써서 각도 오차가 0이다(관문 (a)(b), 선 20개).
- 셀프테스트는 페이지 안의 DOM KeyboardEvent·PointerEvent로 검사한다. OS 수준의 실제 키 입력(특히 Windows 메뉴 키인 F10)은 확인하지 못했다(UNVERIFIED).

### 캔버스 테마 (I2)
- 색 7 SOLID와 배경의 대비: 밝은 배경 #17181A/#FBFAF8 17.03:1, 어두운 배경 #FFFFFF/#121416 18.46:1. 캡처 PNG 픽셀로 쟀고, 4조합 모두 앱 테마와 무관했다.

### PDF (I3)
- u05의 합본은 10쪽이고, 시트는 9개다(나머지 1쪽 포함). u01은 시트가 감지되지 않아 도면 전체 1쪽이다.
- 개별 파일을 "합본의 쪽마다 1개"로 정리하자 u05 12개, u01 3개가 됐다(work/gates/i3_pdf_set.txt). 나머지 쪽 파일(`__rest.pdf`)의 추출 글자는 12자다.

## 2026-10-31 draft5b
- 사용자 확인: nanoCAD 5에서 글꼴 매트릭스 DWG·DXF의 A~G가 모두 한글로 보였다. `FontCompat.DefaultMode` = C를 확정한다.
- F10과 Alt 단독은 WebView2 WPF 컨트롤이 셸의 `PreviewKeyDown`으로 다시 올려 준다(Key.System, SystemKey F10/LeftAlt). 셸에서 가로채 F10을 `hostkey`로 페이지에 넘기자 SendInput 실제 키 입력에서 F10 → POLAR 전환, 이어진 Z가 명령줄로 들어갔다. Alt 단독 뒤의 Z도 들어갔다(host_keys_f10_alt_sendinput, 셸 전달 2회). 대조군(가로채기 없는 빌드)은 돌리지 않았다.
- Windows는 셀프테스트 프로세스가 전경을 가져가는 것을 막는다. 전경 스레드에 `AttachThreadInput`으로 잠깐 붙어서 `SetForegroundWindow`를 하자 전경을 얻었다.
- 우클릭 시간 감지는 놓을 때(pointerup) 누른 시간으로 정한다. 250 ms 미만이면 Enter, 그 이상이면 메뉴다.

## 2026-10-31 draft5c
- Nexys 제품군 헤더 로고: diadem(Nexys VIEW)·nexys-view·nexys-bus 모두 `app/web/public/brand/Logo.png`(MD5 2e4a3722773dd9268311033e3ce05b79)를 18 px 높이로 쓰고, 다크 테마는 `brightness(1.15)`다. AppIcon.ico(b2a3abd9…)·AppIcon.png(91d77b77…)도 세 제품과 NexysDraft가 같다.
- 차이: VIEW 헤더는 로고 + 버전 줄뿐이고, NexysBus는 로고 + 굵은 제품명 + 버전 줄이다. NexysDraft는 지시서 예시에 따라 Bus 방식(제품명 줄 있음)으로 했다.
- 로고 영역 픽셀 상관: VIEW 대비 라이트·다크 모두 1.00, 이전 "N" 원 헤더는 0.21(work/gates/d5c_header_logo.txt).

## 2026-11-02 draft5d
- LibreDWG dwg2dxf의 DXF 출력은 AutoCAD 규칙에 맞지 않는다. u04는 중복 핸들 5,210개와 끊어진 포인터 376개, u01은 루트 사전이 첫 객체가 아니고 중복 핸들 2개다(work/d5e/raw, strict.py·ezdxf). 이 객체를 옮겨 쓰는 저장은 AutoCAD에서 열릴 수 없다. DWG에서 연 도면은 항상 R2000으로 새로 생성해서 저장한다.
- `DxfWriter.Common`의 102 그룹 단축 평가 버그로 빈 `{ACAD_XDICTIONARY }`가 쓰였다. 사용자 도면 R2000 저장 7개 중 5개를 ezdxf가 읽지 못했고, 수정 뒤에는 7개 모두 audit 수정 0·오류 0이다.
- ezdxf 1.4.5(scratchpad venv)를 AutoCAD 대용 검사기로 쓸 수 있다. ODA File Converter는 이 결함들을 모두 받아들여서 AutoCAD 호환성 검증에는 부족했다.

## 2026-11-02 draft5e
- `Document.CreateNew()`에서 만든 새 도면을 원본 유지 모드로 저장하면 OBJECTS가 비고, BLOCK/ENDBLK의 330이 존재하지 않는 레코드(103·106)를 가리켰다(D5dTests.Check). 원본 구조가 없는 도면은 R2000으로 생성해야 한다(`DxfSave.Choose`).
- 기본 도형 7종을 실제 명령줄 입력으로 그렸을 때 좌표가 기대값과 1e-9 안에서 같았다(셀프테스트 shape_*).

## 2026-11-10 draft5f
- DWG TrueView 2027에는 콘솔 엔진 `accoreconsole.exe`가 들어 있다. `/i 파일 /s 스크립트`로 창 없이 DWG·DXF를 열고, 결함을 글로 보고한다(Git Bash에서는 `MSYS_NO_PATHCONV=1` 필요, 출력은 UTF-16).
- LibreDWG dxf2dwg는 DWG를 자기 `*Model_Space` 레코드(핸들 1F)로 시작한다. 넘긴 DXF의 모형 공간 레코드가 1F가 아니면 1F짜리가 주인 없이 남는다(v0.4.4 저장본 8개 중 6개). 다른 객체가 1F를 쓰면 TrueView가 "Duplicate handle(s) ignored. Drawing needs recovery"를 낸다(u04).
- AutoCAD는 AC1015+ DXF의 LAYER에 390(플롯 스타일)이 없으면 도면을 버린다("Did not receive PlotStyleName"). ezdxf audit·ODA·nanoCAD는 이 결함을 통과시켰다.
- HATCH의 47(pixel size)은 경계 경로에 derived 비트(92 & 4)가 있을 때만 있어야 한다. 있으면 안 될 때 있으면 "expected group code 98", 있어야 할 때 없으면 "expected group code 47"이다.
- 폴리라인 VERTEX 플래그 8은 스플라인 맞춤으로 생긴 곡선 위의 점이고, 16이 프레임 제어점이다. 8까지 버려서 스플라인 맞춤 폴리라인이 정점 0개가 됐었다.
- LAYOUT 객체를 포함한 DXF를 dxf2dwg(0.14.8679)에 넘기면 TrueView 2027이 "Damaged File"로 거부한다. 루트 사전만 넘긴 DWG(LAYOUT 없음)는 연다.
- 동료가 본 `INTERNAL ERROR: !dbspace.cpp@2194: eOk`는 TrueView 2027에서 재현되지 않았다(UNVERIFIED: 구버전에서 주인 없는 `*Model_Space`가 원인인지).
