# 블로커와 대체 경로 — NexysDraft draft1 (2026-10-29)

| # | 블록 | 블로커 | 판단 근거 | 대체 경로 / 결과 |
|---|---|---|---|---|
| 1 | D1 | LibreDWG 0.14 `dxf2dwg`가 큰 DXF에서 분 단위로 느리고(600 KB 109 s, 1.0 MB 278 s) 1.7 MB(u03)·10만 엔티티(21 MB)는 300 s 안에 끝나지 않는다 | work/gates/d1b_dwg_roundtrip.txt, samples/gen/g05 타임아웃 | DWG 저장은 LibreDWG 경로 그대로(사용자 도면 7개 중 6개 성공), u03은 UNVERIFIED. 10만 엔티티 샘플은 DXF로만 두고 렌더·열기 관문에 사용. 저장 시 상태줄에 소요 시간 표시. 백로그: LibreDWG git 최신 빌드 속도 확인 |
| 2 | D1 | `dxf2dwg`가 MTEXT 높이를 0으로 쓴다(자기 DXF를 돌려도 같음, `dwgread -O json` text_height 0) | docs/libredwg.md | R2000 내보내기 때 MTEXT 문자열 앞에 `\H<높이>;` 절대 높이 코드를 붙인다(AutoCAD 호환 인라인 코드). nanoCAD 표시 크기는 사용자 확인 항목 |
| 3 | D1 | DXF→DWG 왕복에서 TEXT 일부(u01 1개, u05 4개)와 동적 블록 표현 블록(u06 `*U…`)이 사라진다 | d1b 관문 mismatches | 원인 미확정(UNVERIFIED). R2000 손실 목록(동적 블록·주석 축척·R2000 이후 객체)은 저장 전 대화상자로 안내 |
| 4 | D2 | 10만 엔티티 열기 ≤1 s 관문: DXF 토큰화 0.5 s + 엔티티 생성 1.0 s + JSON 직렬화 0.6 s + 페이지 기하 0.6 s로 1 s를 넘는다 | work/selftest/perf/gates.txt | 직렬화는 Utf8JsonWriter 직접 쓰기로 1.35→0.63 s, 파서는 span 파싱·단일 패스, 페이지는 클로저 없는 고속 경로로 줄임. 남은 수치는 보고서에 분해해서 기록(팬·줌 ≤16 ms 관문은 1 ms 수준으로 통과). 백로그: 공유 버퍼 바이너리 전송·병렬 토큰화 |
| 5 | D5 | dwg2spdf는 `.dwg` 입력만 받고 내부에서 LibreDWG로 DXF를 만든 뒤 ezdxf로 읽는다 → 우리 DWG의 재변환 DXF에 핸들 0 객체(ENDBLK)가 있으면 "Invalid handle 0"으로 실패 | work/selftest/full/gates.txt 1차 | 원인(블록 레코드 핸들 0)을 R2000 라이터에서 제거 → PDF 관문 PASS. dwg2spdf 코드는 수정하지 않음 |
| 6 | 공통 | Bash 도구가 heredoc 안의 `\\`를 바꿔 C#/TS의 백슬래시 리터럴이 깨진다 | D1FormatTests 151행, render.ts | 백슬래시가 있는 줄은 Edit/Write 도구로만 수정 |
| 7 | D4 | 목업 HTML → PNG → 구현 순서를 지키지 못하고 구현(DESIGN v3 토큰 직접 적용) 뒤에 목업을 작성했다 | 시간 배분 | 목업 draft.html을 구현과 같은 토큰으로 작성해 헤드리스 캡처, tools/pixel_diff.py로 구조 대조(docs/report-draft1.md §D4) |

## draft2 (2026-10-30)
| # | 블록 | 블로커 | 판단 근거 | 대체 경로 / 결과 |
|---|---|---|---|---|
| 8 | F2 | LibreDWG 업스트림 재빌드·패치 불가: 개발 PC에 네이티브 gcc/MSYS2 없음(NI Linux 크로스 컴파일러뿐), 업스트림 CI(cmake·msvc·mingw·msys)도 2026-10-22·29 실패, Windows 아티팩트 없음 | GitHub API actions/artifacts, where.exe gcc | DXF 쪽에서 우회(OBJECTS 최소화·핸들 재발급·해치 폴리라인 루프·`\H`). 재빌드는 백로그(MSYS2 설치 후) |
| 9 | F2 | u06(동적 블록 `*U` 표현 블록 + AcAecLayerStandard xdata) DWG는 우회 후에도 ODA "Object improperly read" | work/oda/exp4 (xdata 제거 변형 포함) | UNVERIFIED 원인. R2000 손실 목록에 동적 블록 경고 유지, 이런 도면은 DXF 저장 권장 |
| 10 | F2 | u03(1.7 MB) dxf2dwg 300 s 초과 — DWG 관문 SKIP | d1b/f2 | 저장은 가능하나 수 분(백그라운드·취소 가능). 예상 시간 표시 |
| 11 | F1 | nanoCAD "24/25 엔티티" — ODA 재변환(DXF→DXF, DWG→DXF 모두)에서는 25/25로 빠진 엔티티를 특정하지 못함 | work/oda/out1, f1 관문 | UNVERIFIED. §사용자 확인에 "nanoCAD에서 빠진 엔티티 유형" 요청 |
| 12 | F3 | 생성 샘플 g02 MTEXT가 pdftotext에서 빠짐(68.75 %) — dxf2dwg 왕복 뒤 rect_width 1.0 때문에 MTEXT 범위 추정이 좁아 페이지 밖으로 나간 것 | work/gates/f3_pdf.txt, pymupdf 좌표 | 범위 추정을 긴 줄 기준으로 바꿔 100 %. 남은 FAIL은 u06 96 %(MTEXT 1건 줄바꿈 순서) |
| 13 | F2 | u01·u07 DWG: ODA가 오류 없이 0바이트, u06·u07: "BlockBegin owner Id is invalid" — 엔티티 유형 11종 제거·LTYPE/STYLE/LAYER/VPORT 재생성·xdata 제거로도 변화 없음, LibreDWG 자신의 DXF→DWG도 동일 | work/oda/exp5~exp8 | UNVERIFIED(dxf2dwg 0.14 결함 추정). u02·u04·생성 샘플·selftest는 PASS. nanoCAD 수동 확인 요청 |
| 14 | F2 | dxf2dwg가 SPLINE을 버린다(g04 1→0) | f2 관문 | UNVERIFIED. 손실 목록에 SPLINE 경고 추가는 백로그 |

## draft3 (2026-10-30)
| # | 블록 | 블로커 / 이전 항목 갱신 | 판단 근거 | 대체 경로 / 결과 |
|---|---|---|---|---|
| 8 → 해결 | G0 | MSYS2를 winget으로 무인 설치(UAC 없이 성공), master 1회차 빌드 성공 | docs/libredwg-build.md | master `33bfac15`로 교체 |
| 13 → 해결 | G0 | u01·u07 빈 출력의 원인 = 원본 ByLayer 선종류 핸들 `B` | tools/bisect_sections.py, work/g0/rehandle_* | R2000 라이터가 표준 선종류 3개에 새 핸들. u01·u07 PASS |
| 9 → 원인 확정 | G0 | u06 = IMAGE 1개(핸들 5F) | work/g0/bisect_u06 | 사전 점검 "읽기 불가 예상", DXF 기본 |
| 14 → 해결 | G0 | g04 SPLINE 소실은 표본 매듭 수 오류(10개, 9개여야 함) | work/g0/spline_g04 | 표본 수정, 사전 점검에 비차단 경고 |
| 15 | G0 | 교체 판정 4/7(master + draft2 라이터)이 기준 ≥5/7에 미달해 한때 플랜 B 조건이었다 | f2_oda_dwg_master.txt | 원인을 찾아 라이터를 고친 뒤 재측정 6/7(f2_oda_dwg_master2.txt) → 교체. 플랜 B 아님 |
| 16 | G1 | 글꼴 호환 규칙을 글자 그대로 적용하면 u01의 스타일도 바뀐다(관문 "u01 STYLE 변경 0"과 충돌) | work/gates/g1_fonts.txt | 치환 대상을 "한글 TEXT/ATTRIB 또는 인라인 \f 없는 한글 MTEXT가 실제로 쓰는 스타일"로 좁힘 → u01 0건, 나머지 PC에 없는 글꼴 참조 0 |
| 17 | G1 | run-draft3의 "u01 '굴림' 스타일" 은 u01 STYLE 테이블에 없다(u01 한글은 MTEXT 인라인 `\f굴림`) | u01 DXF STYLE 테이블 | 같은 PDF Arial 형식(그룹 3/4 비움 + ACAD xdata 1000 패밀리·1071)을 복사 |
| 18 | G3 | "용지상 TEXT ≥1.5 mm" 와 "u01·u05·u07 = A3" 를 동시에 만족할 수 없다: A3에 맞추면 사용자 도면 글자 중앙값 0.28~1.74 mm(원 도면이 A3 플롯 기준이라도 작은 주석 글자가 많음) | work/gates/g3_pdf.txt | 용지 A3 관문을 우선. 글자 크기는 측정값만 보고(§결정 필요). CLI 새 도면은 2.5 mm 자동 |

## draft4 (2026-10-30)

| # | 블록 | 블로커 | 판단 근거 | 대체 경로 / 결과 |
|---|---|---|---|---|
| 11 → 해결 | 사용자 | nanoCAD 24/25 | 사용자 확인: selftest_saved.dwg 25/25 | 해소 |
| 18 → 결정 | G3 | PDF 글자 ≥1.5 mm vs A3 | draft3 결정: 플롯 설정/A3 우선 | 1.5 mm 관문 폐기, 중앙값만 보고. CLI 새 도면 2.5 mm 유지 |
| 19 | H0 | dxf2dwg가 `1001 ACAD` xdata를 항상 핸들 0x12로 쓴다. ACAD 외 앱 xdata는 0(끊김) | dwgread JSON eed handle, work/gates/h0_u04_styles.txt | ACAD APPID에 핸들 12를 준다(12가 쓰이면 경고). ACAD 외 xdata 266개(u04)는 끊긴 채 남음 — 읽기 오류는 아님. 업스트림 이슈 후보 |
| 20 | H0 | u04 한글 "???"의 원인을 nanoCAD 없이 확정할 수 없다(u01은 같은 끊긴 xdata로도 한글이 보였다) | docs/font_matrix.md | 매트릭스 A~G를 사용자가 nanoCAD 5로 확인. 기본은 C로 잠정, 상수 한 줄로 교체 |
| 21 | H0 | 지시서의 "A = 현재 방식"과 달리 draft3 코드는 B(ASCII `Gulim`)를 썼다 | FontCompat.MapFamily(draft3), d3 DWG의 ODA 읽기 | A와 B를 둘 다 매트릭스에 넣음 |
| 22 | H2 | 10만 열기 ≤2 s: 이번 측정 2.26~3.78 s(CPU 52 % 부하) | work/selftest/d4_perf*, v0.3.0 A/B 2.08~3.72 s | 같은 조건의 기준 빌드와 차이 없음 → 회귀 아님. 부하가 낮을 때 다시 잴 것(draft3 1.86 s) |
| 23 | H3 | `app\dist`(사용자 draft1 창, pid 46024 실행 중)에 tools·wwwroot가 없었다. draft3 마지막 패키징 시도가 다시 지운 것으로 보임 | ls app/dist | package.ps1이 실행 중이거나 잠긴 배포본은 아예 건드리지 않게 바꿈. tools\libredwg만 다시 복사. 창을 닫은 뒤 package.ps1을 다시 돌리거나 zip 사용 |
