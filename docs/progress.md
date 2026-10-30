# 진행 현황 — NexysDraft

## 2026-10-30 draft2 (run-draft2.md, 11:10 시작)

| 블록 | 판정 | 근거 |
|---|---|---|
| F0 ODA 자동 탐색 | **완료** | Program Files·레지스트리·시작 메뉴 세 경로에서 27.1.0 발견, 더미 변환 OK, config/dev.json(.gitignore), tools/oda.py, docs/libredwg.md |
| F1 DXF 라이터 호환 | **완료** | 순수 ASCII(`\U+XXXX`) 출력, 새 도면 Standard = malgun.ttf, ODA DXF→DXF 관문 work/gates/f1_oda_dxf.txt(읽기 오류 0·개수 일치·한글 동일). 24/25는 ODA에서 재현 불가(UNVERIFIED, blockers #11) |
| F2 DWG 쓰기 | **완료(조건부)** | OBJECTS 최소화·핸들 재발급·해치 폴리라인 루프로 ODA 읽기 오류 0(사용자 도면 4/6 + 생성 4 + selftest; u03 SKIP, u06 UNVERIFIED). 백그라운드 저장+경과/예상+취소. LibreDWG 재빌드는 불가(blockers #8). work/gates/f2_oda_dwg.txt |
| F3 PDF 내장 | **완료** | 자체 PDF 라이터(맑은 고딕 서브셋·ToUnicode·시트 분할·per-sheet 파일), dwg2spdf 의존 제거. pdftotext 추출률 사용자 7개 중 6개 ≥99 %(u06 96 %), u05 10쪽 2.1 s. work/gates/f3_pdf.txt |
| F4 CLI + 스킬 | **완료** | `--script`(명령 19종, result.json, exit code), skills/nexysdraft/SKILL.md 예시 3개, `--skilltest` PASS(랙 패널 → PDF → pdftotext 600·2000) |
| F5 실사용·저장소 | **완료** | 드롭·Ctrl+S·닫기 경고(draft1)·최근 10개, git 저장소 + GitHub Kim-Hakseong/nexysdraft private, 새 clone 빌드·테스트·셀프테스트, 태그 v0.2.0-draft2, 회사명 문자열 0건 |

산출물: docs/report-draft2.md, docs/libredwg.md(결함·우회 표), docs/blockers.md #8~#12, docs/findings.md, docs/screens/20~23, skills/nexysdraft/SKILL.md, tools/oda.py, config/dev.json, next.md.

## 2026-10-29 draft1 (run-draft1.md)

레벨: D1 포맷 / D2 렌더 / D3 편집 / D4 UI / D5 출력·검증·배포.

| 블록 | 판정 | 근거 |
|---|---|---|
| D1 포맷 | 완료 (DWG 왕복은 6/7 UNVERIFIED 항목 있음) | DXF 파서/라이터(Preserve·R2000), 코드페이지·MTEXT 파서, LibreDWG CLI 래퍼, R2000 손실 감지. 관문 (a) work/gates/d1a_dxf_roundtrip.txt, (b) d1b_dwg_roundtrip.txt, 한글 d1_korean.txt |
| D2 렌더 | 완료 (열기 ≤1 s 관문 미달: 1.9 s) | WebGL 선 + Canvas2D 텍스트/채움, 레이어, 줌·팬·FIT, 스냅, 글꼴 폴백. 팬·줌 0.6~1.5 ms |
| D3 편집 | 완료 | 선택, MOVE/COPY/ERASE/ROTATE/STRETCH/OFFSET/MIRROR, 텍스트 편집, LINE/PLINE/CIRCLE, DLI, 레이어, Undo/Redo, 명령줄 |
| D4 UI | 완료 (순서 역전: 구현 뒤 목업) | design/mockup/draft.html·png, tools/pixel_diff.py, 라이트/다크 |
| D5 출력·검증·배포 | 완료 | PDF(당시 dwg2spdf), `--selftest`, `--stress`, app/dist/NexysDraft.exe, docs/report-draft1.md |
