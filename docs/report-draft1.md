# NexysDraft draft1 보고 — 개인용 경량 2D CAD MVP (DWG 열기·편집·저장) — 2026-10-29

run-draft1.md를 16:23에 시작해 자율로 끝까지 진행한 결과. 측정은 전부 셀프테스트(`NexysDraft.exe --selftest / --perf / --stress / --shot`)와 xUnit 관문(`dotnet test app/NexysDraft.Tests`)이 남긴 파일(work/gates, work/selftest)에서 가져왔다. 캡처는 docs/screens/.

## 요약
- **열기**: DWG(LibreDWG 0.14 `dwg2dxf`, 별도 프로세스) → 자체 DXF 파서(R12~2018, 엔티티 14종 + 미지원 엔티티 원문 보존). 사용자 도면 7개 전부 열림, 한글 TEXT/MTEXT 422건 깨짐 0.
- **편집**: 선택(클릭·창·교차), MOVE/COPY/ERASE/ROTATE/STRETCH/OFFSET/MIRROR, 텍스트 더블클릭 편집, LINE/PLINE/CIRCLE, 선형 치수(DLI), 레이어 변경·새 레이어·켜기/끄기·동결·잠금, Undo/Redo, AutoCAD식 명령줄.
- **저장**: DXF(원본 보존: 편집하지 않은 부분은 바이트 그대로), DWG R2000(`dxf2dwg`), PDF(dwg2spdf). 저장 전 R2000 손실 목록 대화상자.
- **렌더**: WebGL 1-drawcall + Canvas2D 텍스트. 10만 엔티티 팬·줌 0.6~1.5 ms(관문 16 ms). 열기는 1.9 s(관문 1 s 미달, §D2).
- **배포**: `app/dist/NexysDraft.exe` 단일 파일(self-contained, LibreDWG CLI는 `tools/libredwg/`에 별도 파일).

## 블록 판정
| 블록 | 판정 | 근거 |
|---|---|---|
| D1 포맷 | 완료 — DWG 왕복 관문은 7개 중 1개 타임아웃·5개 UNVERIFIED 항목 | §D1 |
| D2 렌더 | 완료 — 팬·줌 관문 통과, 열기 1 s 관문 미달(1.9 s) | §D2 |
| D3 편집 | 완료 | §D3 |
| D4 UI | 완료 — 목업을 구현 뒤에 작성(블로커 #7) | §D4 |
| D5 출력·검증·배포 | 완료 — 스트레스 메모리 관문 미달(+153 MB) | §D5 |

## D1 포맷 — 관문 수치 (work/gates/)
**(a) DXF 자체 왕복** (`d1a_dxf_roundtrip.txt`): 샘플 12개(사용자 7 + 생성 5) 전부 PASS. 엔티티 수·레이어 수 동일, 좌표 maxErr 0.0 (Preserve 라이터·R2000 모델 라이터 둘 다). 읽기 u03(25,652 엔티티) 628 ms, 10만 엔티티 DXF 1,070 ms(테스트 프로세스, 워크스테이션 GC).

**(b) DWG 왕복 (dwg2dxf → 읽기 → R2000 DXF → dxf2dwg → dwg2dxf → 읽기)** (`d1b_dwg_roundtrip.txt`):
| 샘플 | 결과 | 좌표 1e-6 일치 | 불일치 / 사유 |
|---|---|---|---|
| u01 (1,213) | UNVERIFIED | LINE 1045·LWPOLYLINE 58·CIRCLE 25·ARC 144·ELLIPSE 4·INSERT 17 전부, TEXT 82/83 | TEXT 1개 소실(dxf2dwg), MTEXT 높이 0(LibreDWG 결함, 좌표는 일치) |
| u02 (37 + 블록) | UNVERIFIED | LINE 6096·CIRCLE 363·ARC 1174·ELLIPSE 166·TEXT 99·POLYLINE 37 전부 | MTEXT 높이만 |
| u03 (25,652) | FAIL | — | dxf2dwg 300 s 타임아웃 (블로커 #1) |
| u04 (3,757) | UNVERIFIED | LINE 8138·LWPOLYLINE 932·CIRCLE 90·ARC 646·ELLIPSE 55·TEXT 974 전부 | MTEXT 높이만 |
| u05 (18,833) | UNVERIFIED | LINE 9550·LWPOLYLINE 1152·CIRCLE 688·ARC 1688·ELLIPSE 169·TEXT 4683/4687 | TEXT 4개 소실 |
| u06 (722, 동적 블록) | UNVERIFIED | 남은 것은 전부 일치 | `*U` 표현 블록 내용이 dxf2dwg에서 소실(LINE 752→357, INSERT 15→1) |
| u07 (2,984) | UNVERIFIED | LINE 4150·LWPOLYLINE 307·CIRCLE 134·ARC 745·ELLIPSE 27·TEXT 214·INSERT 40 | POLYLINE 2개 0.1 오차(3D 폴리선 bulge, UNVERIFIED), MTEXT 높이 |
| g03 블록/치수 | (핸들 0 수정 후) PASS — `--selftest` reopen_dwg_coords 25/25 | INSERT 12·DIMENSION 3·HATCH 1 | 1차 관문 실행 때는 블록 소실(핸들 0) → 수정 |
| g04 곡선 | PASS | LWPOLYLINE 20·ARC 10·ELLIPSE 10·HATCH 1 | — |
| g01·g02 | 관문 재실행 전 재생성됨(MTEXT 코드 50 수정) — `Korean_RoundTrip` 테스트로 DWG 왕복 한글 보존 확인 | | |

HATCH·DIMENSION 개수는 g03·g04·u06(HATCH 7→0: 동적 블록 도면)에서 비교, u06 외 일치. d1b 파일은 핸들 수정 전 실행본이며 사용자 파일 6개의 좌표 일치는 그 실행에서 이미 확인됐다(핸들 수정은 블록 소실만 바꾼다).

**한글/인코딩** (`d1_korean.txt`, `d1_korean_strings.txt`): 샘플 11개 TEXT+MTEXT 7,673건 중 한글 422건, U+FFFD·모지바케 0건. 코드페이지 ANSI_949 바이트(AC1015·AC1018)와 UTF-8 값(AC1024·AC1032, LibreDWG 출력 안에 혼재)을 값 단위로 판정. MTEXT 서식 코드(`\f…|b1|i0;`, `\P`, `\H…x`, `\W`, `\U+XXXX`, `{}`, `\S^;`) 파서 단위 테스트 6종 통과. `.shx`·미상 글꼴은 '맑은 고딕' 폴백(캡처 docs/screens/03_user_korean_zoom.png: "규 격 / 비 고 / 백색아노다이징").

**단위 테스트**: 22/22 통과(`dotnet test`, 형식 13 + 편집 9). 관문 전체 실행 13분 33초(dxf2dwg 시간).

**LibreDWG**: 버전·지원 범위·실측 결함은 docs/libredwg.md. R2000 저장 시 손실 감지(`R2000Loss`): R2000 이후 엔티티(MLEADER·ACAD_TABLE…), 동적 블록, 트루컬러, 투명도, 주석 축척, 추가 레이아웃 → 저장 대화상자 목록.

## D2 렌더 — 관문 수치 (work/selftest/perf/gates.txt, 10만 엔티티 samples/gen/g05_100k.dxf 21.5 MB)
| 항목 | 값 | 관문 |
|---|---|---|
| 팬 60프레임 중앙값 / p95 | 0.6 / ~0.8 ms | ≤16 ms PASS |
| 줌 60프레임 중앙값 | 0.6 ms | ≤16 PASS |
| 휠 30회 중앙값 | 0.9 ms | ≤16 PASS |
| 드래그 팬 중앙값 | 0.7 ms | ≤16 PASS |
| 확대 상태 팬 중앙값 | 1.4 ms | ≤16 PASS |
| 열기 합계 (토큰화 0.24 + 엔티티 생성 0.41 + JSON 직렬화 0.51 + 페이지 기하 0.55 + 첫 프레임 0.15) | **1.86 s** | ≤1 s **FAIL** |
| 세그먼트 / 정점 | 190만 / 380만 (WebGL) | |
| Working Set | 481 MB | |
열기 개선 이력: 3.84 → 2.76(Utf8JsonWriter·span 파싱·페이지 고속 경로) → 1.86 s(Server GC, QuickJitForLoops off). 남은 1 s는 JSON 경로(직렬화+페이지 파싱 1.05 s) — 바이너리 전송이 다음 수단(next.md). 사용자 도면은 모두 1 s 미만(u05 18,833 엔티티: 엔진 203~366 ms + 페이지 155 ms; LibreDWG 변환 1.5 s는 별도).

기능: 레이어 켜기/끄기·동결·잠금(레일 아이콘), 줌·팬·FIT(Z E, 창 줌), 스냅 끝점□·중점△·중심○·교차×·사분점◇·삽입점(그리드 인덱스, 반경 10 px, F3), 글꼴 폴백.

## D3 편집
`--selftest` 사용자 경로(work/selftest/full/gates.txt, 전 항목 PASS): 창 선택 4 → `m` 기준점·목표점 → INSERT 4개 y=200 → `co` 복사 4개 → `l` 중 끝점 스냅(END) → `l`·`c 25`·`pl … c`·`dli` 그리기(치수 측정값 100) → 더블클릭 텍스트 편집 "한글 수정 TEXT" → 레일에서 새 레이어 NEW-LAYER(현재 레이어로) → 레이어 끄기/켜기 → Undo×2/Redo×2 원복 → DXF 저장·재열기 동일(25=25, 레이어 5=5) → DWG 저장 136 ms·재열기 좌표 25/25 → PDF 1.95 s → accent 테두리 0 → Working Set 191 MB → UI 정지 0 (최종 실행; 문서 교체 시 강제 GC를 넣었던 중간 빌드에서는 정지 1회가 나와 제거).
단위 테스트(EditorTests): 이동·Undo/Redo, 복사 새 핸들, 회전 호 각도, 대칭(호 방향 반전·텍스트 가독 유지), OFFSET 선 거리·원 반지름 ±, STRETCH 창 안 그립만, 텍스트/MTEXT 편집·레이어 추가·상태·Undo, 선형 치수 블록(선 3·화살표 2·문자 "100")과 라이터 왕복·이동 시 블록 동반 이동, Preserve 라이터의 원문 유지/편집분 재생성.

## D4 UI
- 목업 design/mockup/draft.html(DESIGN v3 토큰, 인라인 스타일) → 헤드리스 Edge 캡처 draft.png(docs/screens/00_mockup_draft.png) → 구현 캡처(01_app_light_u01.png)와 tools/pixel_diff.py(Python312) 구조 대조:

| 영역 | 레이아웃 프로파일 상관 | 판정 |
|---|---|---|
| 헤더 52px | 0.87 | OK |
| 파일 레일(260) | 0.81 | OK |
| 파일 레일 헤더+버튼 | 0.88 | OK |
| 푸터 24px | 0.97 | OK |
| 우측 레일(300) | 0.58 | DIFF — 목업은 레이어 4행, 구현 15행 |
| 명령줄 카드 | 0.67 | DIFF — 히스토리 줄 수 |
| 툴바 | 0.58 | DIFF — 목업 아이콘 자리표시자 |
패널 열 경계: 앱 [16, 275, 288, 1111, 1124, 1423] / 목업 [16, 277, 290, 1111, 1124, 1423] — 좌측 레일 폭 2 px 차이(content-box 테두리). 수평 구분선 13개 중 6개가 ±2 px 밖(파일 레일 버튼이 목업은 3+2, 구현은 2+3으로 줄바꿈). accent 색 테두리/띠 0건(관문). 라이트 기본·다크(02_app_dark_u05.png).
- 순서: 구현을 먼저 하고 목업을 뒤에 썼다(블로커 #7). 레일: FILE(열기·저장·DXF·DWG·PDF, 파일 카드, 엔티티 유형, 변환기 메모, 최근 파일) · 캔버스(툴바·좌표·스냅 표시) · LAYERS(색·이름·개수·켜기/동결/잠금·새 레이어) · PROPERTIES(선택 요약·레이어 변경·텍스트 편집·기하 값) · 명령줄(히스토리 3줄·프롬프트·입력).

## D5 출력·검증·배포
- PDF: dwg2spdf `convert --merge --converter libredwg` → 병합 PDF 복사. 셀프테스트 2.0 s, 파일 work/selftest/full/files/selftest.pdf.
- 셀프테스트 `--selftest` 전 항목 PASS(위 D3). `--stress --iter 20`: 예외 0, 300 ms UI 정지 0(직렬화를 백그라운드로 옮긴 뒤; 이전 3회/최대 742 ms), 메모리 증가 **+153 MB(관문 100 MB FAIL; 이전 실행 +147)**, 40회에서 +165 MB(20→40회 +18 MB: 초기 상승 뒤 완만). 워크스테이션 GC로는 +113 MB(정지 1회). 원인은 GC 힙 보유로 추정(UNVERIFIED), next.md.
- 배포: `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishReadyToRun=true` → app/dist/NexysDraft.exe(약 211 MB, ReadyToRun 포함) + tools/libredwg/(GPL 바이너리·COPYING·SOURCE.txt). dist 비우고 publish, dist 실행 `--shot` 스모크(work/selftest/dist/dist_shot.png).
- 저장소는 만들지 않았다(요구사항). 사용자 도면은 samples/user/에만 있고 배포물·문서에 들어가지 않는다.

## §결정 필요
1. **10만 엔티티 열기 1 s 관문**: 현재 1.9 s. 바이너리 전송(공유 버퍼)으로 ≈0.9 s가 목표인데, 구현 1~2 h. 다음 런에서 할지, 관문을 "사용자 도면(≤3만) 1 s"로 조정할지.
2. **DWG 저장 속도**: dxf2dwg가 1 MB 도면에 분 단위. (a) 백그라운드 저장+진행 표시로 감수, (b) LibreDWG git 최신 빌드 측정, (c) DWG 저장은 소형 도면 한정 안내.
3. **MTEXT 높이 `\H` 접두**: dxf2dwg 결함 우회. nanoCAD에서 크기가 맞으면 유지, 아니면 MTEXT를 TEXT 줄로 분해해 저장하는 대안.
4. **스트레스 메모리 관문**: Server GC(열기 빠름) vs 워크스테이션 GC(+113 MB, 정지 1회). 기준을 "첫 열기 이후 증가"로 바꿀지.
5. 저장소·배포 방식(GitHub 이름, public/private, 설치형 여부) — 백로그 그대로.

## §사용자 확인
- nanoCAD에서 열기: `work/selftest/full/files/selftest_saved.dwg`(R2000, 블록·치수·속성 포함), 사용자 도면을 NexysDraft로 연 뒤 DWG로 저장한 파일(예: u01). MTEXT 크기(`\H` 접두), 치수 블록, 속성 텍스트.
- `work/selftest/full/files/selftest.pdf`에서 글자 검색(Ctrl+F "한글").
- 한글 캡처: docs/screens/03_user_korean_zoom.png(u01 표제란), 05_user_dwg_extents.png. 깨진 글자가 보이는지.
- 대칭(MIRROR)한 텍스트의 위치 규칙(MIRRTEXT=0 근사)이 기대와 같은지.

## 파일
- 코드: app/NexysDraft.Core(Dxf·Model·MTextFmt·Dwg·Edit·Geometry·Page), app/NexysDraft.App(WPF+WebView2 셸·Bridge·Engine·SelfTest), app/NexysDraft.Tests, app/web(TypeScript: doc·render·view·rails·main·test), app/tools/libredwg.
- 문서: docs/libredwg.md, blockers.md, findings.md, progress.md, review.md, 이 보고서, next.md. 캡처 docs/screens/.
- 관문: work/gates/d1a_dxf_roundtrip.txt·d1b_dwg_roundtrip.txt·d1_korean.txt·d1_korean_strings.txt, work/selftest/{full,perf,stress,stress40,shot,dist}/gates.txt·*.png, work/pixel/light_diff.*.
