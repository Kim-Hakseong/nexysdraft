# NexysDraft draft2 보고 — 타 CAD 호환 DWG/DXF · PDF 내장 · CLI+스킬 · 저장소 — 2026-10-30

run-draft2.md를 11:10에 시작해 자율로 진행한 결과. 수치는 전부 xUnit 관문(work/gates/*.txt)과 셀프테스트(work/selftest/*/gates.txt)가 남긴 파일에서 가져왔다. ODA File Converter는 개발 PC 검증에만 썼고(config/dev.json, .gitignore) 제품·배포물·기본 셀프테스트는 참조하지 않는다.

## 요약
- **DXF**: 순수 ASCII(`\U+XXXX`) 출력으로 바꿔 nanoCAD의 "??"를 없앴다(ODA 재변환 관문: 사용자 7 + 생성 5 + selftest 전부 읽기 오류 0, 엔티티·레이어 수 일치, 한글 동일).
- **DWG**: ODA·nanoCAD가 LibreDWG 0.14 출력을 거부하던 원인을 변형 실험으로 좁혀(OBJECTS의 LAYOUT/사전 객체, HATCH의 330 경계 핸들, 낮은 핸들 충돌, 원문 LTYPE/STYLE 엔트리) **OBJECTS를 루트 사전 하나로** 줄이고 테이블·핸들을 재생성해 **u02·u04와 생성 샘플·selftest는 ODA 오류 0**(해치 54/54, 치수, 한글 100 %)이 됐다. u01·u06·u07은 dxf2dwg 자체 결함으로 남았고(UNVERIFIED), u03·u05는 속도. 저장은 백그라운드 + 경과/예상 시간(≈1+12·MB² s) + 취소. LibreDWG 재빌드는 툴체인 부재로 못 했다(blockers #8).
- **PDF**: dwg2spdf 의존을 없애고 자체 PDF 1.4 라이터(벡터 + 맑은 고딕 서브셋 실제 텍스트, 시트 자동 분할, 시트별 파일)를 넣었다. pdftotext 추출률 사용자 도면 6/7 ≥99 %(u06 96 %), u05 10쪽 2.1 s.
- **CLI/스킬**: `NexysDraft.exe --script job.txt --out dir --json`(명령 19종, result.json, exit code), skills/nexysdraft/SKILL.md, `--skilltest` PASS.
- **저장소**: GitHub Kim-Hakseong/nexysdraft(private), 사용자 도면·회사명 0건, 태그 v0.2.0-draft2(§F5).

## 블록 판정
| 블록 | 판정 | 비고 |
|---|---|---|
| F0 ODA 탐색 | 완료 | 27.1.0, 세 경로 일치 |
| F1 DXF 라이터 호환 | 완료 | 24/25는 ODA에서 재현 불가(UNVERIFIED) |
| F2 DWG 쓰기 | 부분 완료 | ODA 오류 0: u02·u04 + 생성 3 + selftest. u01·u06·u07 dxf2dwg 결함(UNVERIFIED), u03·u05 속도, g04 SPLINE 소실 |
| F3 PDF 내장 | 완료 | 12개 중 11개 ≥99 %(u06 96 %: MTEXT 줄바꿈 1건) |
| F4 CLI + 스킬 | 완료 | |
| F5 실사용·저장소 | 완료 | |

## F0 ODA 자동 탐색
- 탐색: where.exe(없음) → `C:\Program Files\ODA\**`(발견) → 레지스트리 Uninstall(DisplayName "ODAFileConverter" 27.1.0, InstallLocation 비어 있음) → 시작 메뉴 `ODA\ODA File Converter 27.1.0.lnk`(Installer 스텁 대상). 버전 FileVersion 27.1.0.0.
- 더미 변환(selftest_saved.dxf DXF→DXF) exit 0, 1.2 s → config/dev.json 기록. tools/oda.py(폴더 스테이징·.err 수집), OdaGateTests(dev.json 없으면 SKIP 행).

## F1 DXF 라이터 호환 (work/gates/f1_oda_dxf.txt)
| 파일 | ODA 읽기 오류 | ASCII | 레이어 | 한글 | 엔티티 수 |
|---|---|---|---|---|---|
| u01 | 0 | ✓ | 15/15 | 12/12 동일 | 동일(LINE 1045·MTEXT 187·HATCH 45 …) |
| u02 | 0 | ✓ | 17/17 | 2/2 | 동일(INSERT 1194 …) |
| u03 | 0 | ✓ | 22/22 | 48/48 | 동일(LINE 27708 …) |
| u04 | 0 | ✓ | 52/52(+Defpoints) | 74/74 | 동일 |
| u05 | 0 | ✓ | 36/36 | 194/194 | 동일(TEXT 4683 …) |
| u06 | 0 | ✓ | 6/6 | 16/16 | 동일 |
| u07 | 0 | ✓ | 27/27 | 64/64 | 동일 |
| g01~g04·selftest | 0 | ✓ | 동일(+Defpoints) | 동일 | 동일 |
- 한글: TEXT·MTEXT·ATTRIB·DIMENSION뿐 아니라 모든 문자열(레이어명 포함)의 비ASCII를 `\U+XXXX`로 쓴다(AC1015·AC1018·AC1024·AC1032 공통). 읽기는 `\U+`·`\M+`·cp949·UTF-8 전부 수용(D1 회귀 22/22 유지).
- STYLE: 원본 STYLE 테이블 보존(Preserve 라이터는 원문 그대로), 새 텍스트는 현재 스타일, 새 도면(`new`)의 Standard = `malgun.ttf`(코드 3).
- 24/25: selftest_saved.dxf를 ODA로 DXF→DXF·DWG→DXF 변환해도 25/25(INSERT 16·ATTRIB 16·DIMENSION 4·HATCH 1·LINE 1·CIRCLE 1·LWPOLYLINE 1·TEXT 1)라 빠진 1개를 특정하지 못했다(UNVERIFIED, blockers #11). §사용자 확인에 요청.

## F2 DWG 쓰기 (docs/libredwg.md 결함·우회 표, work/gates/f2_oda_dwg.txt)
- **Layout 플래그**: 변형 실험 base(현행) / A(플롯 플래그 ModelType 제거) / B(OBJECTS 삭제) / C(B + 340 링크 삭제) / D(LAYOUT만 삭제) / E(루트 DICTIONARY만) → ODA 읽기 성공은 **E뿐**. 라이터 기본값을 E로. nanoCAD RECOVER 원인과 같은 뿌리로 추정(사용자 재확인).
- **DIMENSION·HATCH 소실**: `*D` 익명 블록은 이미 명시 기록(우회 1). 해치는 dxf2dwg가 엣지형 경계를 버리므로(DWG JSON에 paths 없음) 폴리라인 루프로 테셀레이션해 기록. draft1 리더가 파일의 해치 루프를 버리던 결함도 수정(ODA HATCH N/0의 원인 절반). ODA는 치수 블록을 자기 식으로 재생성하므로 치수는 개수로 비교.
- **TEXT 소실(u01 1·u05 4)**: 1차 관문에서 u01·u07이 ODA 0바이트(핸들 충돌)로 비교 불능이었고, 핸들 재발급 뒤의 수치는 아래 표. 조용한 소실 0을 위해 F2 관문이 TEXT+MTEXT 수를 비교한다.
- **저장 UX**: `SaveAsync`가 R2000 DXF를 쓴 뒤 크기로 예상 시간(≈2+250·MB² s)을 계산, 0.5 s마다 상태줄에 "경과 / ~예상", 푸터 CANCEL SAVE 알약 → 프로세스 kill. UI 스레드는 기다리지 않는다(Task.Run).
- **LibreDWG git 최신**: Windows 아티팩트 없음·CI 실패·네이티브 gcc 없음 → 재측정 불가(blockers #8). 버전표는 docs/libredwg.md.

| 파일 | dxf2dwg | ODA 읽기 | 레이어 | TEXT+MTEXT | DIM / HATCH | 한글 | 모델 엔티티 수 | 판정 |
|---|---|---|---|---|---|---|---|---|
| u01 (1,213) | 2.4 s | 오류 메시지 없이 **0바이트 출력** | — | — | — | — | — | FAIL (UNVERIFIED, libredwg.md 7b) |
| u02 (37 + 블록 1194 INSERT) | 238 s | 오류 0 | 17/17 | 18/18 | 0/0 · 0/0 | 2/2 동일 | 동일 | **PASS** |
| u03 (25,652) | > 300 s | — | | | | | | SKIP(속도) |
| u04 (3,757) | 136 s | 오류 0 | 52/52 | 517/517 | 0/0 · **54/54** | 74/74 동일 | 동일 | **PASS** |
| u05 (18,833) | 타임아웃 300 s | — | | | | | | FAIL(속도) |
| u06 (722, 동적 블록·IMAGE) | 2.0 s | "BlockBegin owner Id is invalid" + "Object improperly read" | | | | | | FAIL (blockers #9) |
| u07 (2,984) | 64 s | "BlockBegin owner Id is invalid" | | | | | | FAIL (UNVERIFIED) |
| g01·g02·g03·selftest | 0.1 s | 오류 0 | 동일(+Defpoints) | 동일 | 3/3 · 1/1 | 동일 | 동일 | **PASS** (블록·치수·해치·한글 포함) |
| g04 곡선 | 0.1 s | 오류 0 | 4/4 | — | 0/0 · 1/1 | — | SPLINE 1→0 | FAIL (dxf2dwg가 SPLINE을 버림, UNVERIFIED) |
TEXT·MTEXT 소실 0(읽힌 파일 전부), 한글 100 %(읽힌 파일 전부). u01·u06·u07의 원인은 엔티티 유형별 제거·테이블 재생성 실험으로도 좁혀지지 않았고 LibreDWG 자신의 DXF→DWG도 같은 증상이라 dxf2dwg 0.14의 실도면 처리 결함으로 본다(UNVERIFIED). nanoCAD는 ODA보다 관대할 수 있어 §사용자 확인에 파일을 올린다.

## F3 PDF 내장 (work/gates/f3_pdf.txt)
- 구현: `Core/Pdf/TrueTypeFont`(cmap 4/12·hmtx·loca/glyf·OS/2 sCapHeight, 글리프 제로잉 서브셋 + 합성 글리프 추적), `PdfWriter`(PDF 1.4, Flate, Type0/CIDFontType2/Identity-H, ToUnicode bfchar, W 배열), `SheetDetector`(dwg2spdf 규칙: 프레임 블록 ≥2·겹침 ≤5 %·중심 포함 ≥60 % / 같은 크기 사각형 반복 / 없으면 1장, 읽기 순서 행→열, rest 페이지), `DrawingPdf`(A3 가로 fit, 인쇄 색(7→검정, 밝은 색 어둡게), 선가중치 mm/100, TEXT 정렬 9종·회전·폭 비율, MTEXT 줄·attach·폭 줄바꿈, 해치 솔리드 채움/패턴 경계, 블록·치수 블록 재귀).
- 결과(최종 실행): u01 1쪽 99.25 % 257 ms · u02 100 % · u03 100 % 1.5 s · u04 3쪽(감지) 99.75 % · u05 10쪽(9시트+rest) 99.52 % 1.9 s · u06 96.0 % · u07 2쪽(감지) 99.26 % · g01~g04·selftest 100 %. 1 MB 도면(u05) ≤5 s PASS. 생성 샘플 g02는 MTEXT 범위 추정 수정(dxf2dwg 왕복 뒤 rect_width 1.0) 뒤 68.75 → 100 %. 누락 예: "CAPA:713m/hr x2EA"(줄바꿈/특수문자), u06 "투명케이스(케이스가 있는 버튼 사용 가능)"(MTEXT 줄바꿈 뒤 pdftotext 순서).
- 셀프테스트: `pdf_text_searchable` — 편집한 "한글 수정 TEXT"와 치수 "100"을 pdftotext가 찾는다(PASS). 캡처 docs/screens/20~22.

## F4 CLI + 스킬
- `NexysDraft.exe --script job.txt [--out dir] [--json]`: new/open/save/pdf/select/layer/l/pl/c/dli/m/co/e/ro/mi/o/s/text/replace/undo/redo/query(entities|layers|extents|texts|selection). 오류 시 `errors[0].line/.error`, exit 1; 파일 없음 exit 2. GUI 없음(AttachConsole로 stdout).
- skills/nexysdraft/SKILL.md: 문법·예시 3개(텍스트 치환→DWG / 랙 패널+치수→PDF / 통계 JSON)·관문. `--skilltest`: SKILL.md에서 예시 2를 추출해 실행 → result.json ok, PDF 1쪽·텍스트 2, pdftotext에 600·2000 — 전 항목 PASS, 0.63 s.

## F5 실사용·저장소
- 드래그앤드롭·Ctrl+S·수정 후 닫기 경고(draft1 유지), 최근 파일 10개.
- 저장소: GitHub **Kim-Hakseong/nexysdraft (private)**, `.gitignore`(samples/user, work, app/dist, config/dev.json, node_modules, bin/obj, web/dist). 커밋 4개(a8641b7), 회사명·사용자 도면 문자열 `git grep` 0건(LibreDWG README의 "Downloads"만 업스트림 문장).
- 관문(새 clone C:ea-work\draft\work\clone_check): `npm install && npm run build` → `dotnet build` App·Tests 오류 0 → `dotnet test` **26/26 통과(15 s; 사용자 도면·ODA 없는 환경에서는 해당 행 SKIP)** → clone 빌드의 `--selftest` exit 0(20 PASS, 사용자 도면 부분은 건너뜀) → 태그 **v0.2.0-draft2** 푸시.
- 테스트 클래스는 순차 실행으로 바꿨다(병렬 실행 때 공유 샘플 파일 경쟁으로 clone에서 F1이 한 번 실패).

## §결정 필요
1. **u06 같은 동적 블록 도면의 DWG 저장**: ODA가 여전히 거부. (a) 저장 대화상자에서 "이 도면은 DXF로 저장 권장" 경고로 끝낼지, (b) `*U` 표현 블록을 정적 블록으로 풀어 쓰는 변환을 다음 런에 할지.
2. **LibreDWG 재빌드**: MSYS2 설치(개발 PC)를 허용하면 업스트림 master를 빌드해 속도·MTEXT 높이·해치를 재측정할 수 있다.
3. **PDF 용지**: 현재 A3 가로 고정. 도곽 비율로 A4/A2 자동 선택이 필요한지.
4. **공유 방식**: 포터블 zip(exe + tools/)를 GitHub Release에 올릴지, 사내 공유 폴더로 할지.

## §사용자 확인 (nanoCAD로 열어볼 파일과 볼 것)
| 파일 | 볼 것 |
|---|---|
| work/oda/u01_pid_2504.r2000.dwg, u02_template.r2000.dwg, u04_hthp_pfd.r2000.dwg, u05_hthp_pid.r2000.dwg, u07_kj_pid.r2000.dwg, selftest_saved.r2000.dwg | RECOVER 없이 열리는지, 치수·해치가 보이는지, MTEXT 크기(`\H` 접두) |
| work/oda/u01_pid_2504.ours.dxf … (사용자 7개) + selftest_saved.ours.dxf | 한글 TEXT/MTEXT가 "??" 없이 보이는지, selftest 엔티티 25/25(빠지면 어떤 유형인지) |
| work/pdf/u05_hthp_pid.pdf(10쪽), u01_pid_2504.pdf | Ctrl+F 한글·영문 검색, 시트 분할 순서, 글자 크기·위치 |
| work/selftest/skill/skill/rack_panel.pdf | CLI로 그린 패널·치수 |
