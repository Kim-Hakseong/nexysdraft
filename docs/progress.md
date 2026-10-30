# 진행 현황 — NexysDraft draft1 (run-draft1.md)

레벨: D1 포맷 / D2 렌더 / D3 편집 / D4 UI / D5 출력·검증·배포. 판정은 관문 파일(work/gates, work/selftest)과 캡처(docs/screens)로.

## 2026-10-29 draft1 (16:23 시작)

| 블록 | 판정 | 근거 |
|---|---|---|
| D1 포맷 | **완료** (DWG 왕복은 6/7 UNVERIFIED 항목 있음) | DXF 파서/라이터(Preserve·R2000), 코드페이지·MTEXT 파서, LibreDWG CLI 래퍼, R2000 손실 감지. 관문 (a) work/gates/d1a_dxf_roundtrip.txt, (b) d1b_dwg_roundtrip.txt, 한글 d1_korean.txt. docs/libredwg.md |
| D2 렌더 | **완료** (열기 ≤1 s 관문 미달: 1.9 s) | WebGL 선 + Canvas2D 텍스트/채움, 레이어 켜기/끄기·동결·잠금, 줌·팬·FIT, 스냅(끝·중·중심·교차·사분점, 그리드 인덱스), 글꼴 폴백. work/selftest/perf/gates.txt: 팬·줌 중앙값 0.6~1.5 ms |
| D3 편집 | **완료** | 선택(클릭·창·교차), MOVE/COPY/ERASE/ROTATE/STRETCH/OFFSET/MIRROR, 텍스트 더블클릭 편집, LINE/PLINE/CIRCLE, DLI 선형 치수(블록 생성), 레이어 변경·새 레이어, Undo/Redo, 명령줄 별칭. 단위 테스트 EditorTests 9개 + `--selftest` 사용자 경로 |
| D4 UI | **완료** (순서 역전: 구현 뒤 목업) | design/mockup/draft.html + draft.png(헤드리스 Edge), tools/pixel_diff.py 구조 대조, 라이트/다크, accent 테두리 0 |
| D5 출력·검증·배포 | **완료** | PDF(dwg2spdf) 관문 PASS, `--selftest` 전 항목 PASS, `--stress`, app/dist/NexysDraft.exe(self-contained single file), docs/report-draft1.md, docs/screens/, next.md |

산출물: app/ (Core·App·Tests·web·tools/libredwg), design/mockup/draft.html·png, docs/(libredwg·blockers·findings·progress·review·report-draft1), samples/gen (g01~g05), work/gates, work/selftest, tools/pixel_diff.py, next.md.
