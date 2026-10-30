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
