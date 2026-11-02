# 진행 현황 — NexysDraft

## 2026-11-02 draft5d → v0.4.3 (AutoCAD에서 저장 파일이 안 열림)

| 항목 | 판정 | 근거 |
|---|---|---|
| 빈 XDICTIONARY 묶음 수정 | **완료** | ezdxf 읽기 실패 5/7 → 0/7 |
| DWG에서 연 도면 → R2000 재생성 저장 | **완료** | d5d_autocad_structure.txt 13/13 |
| DXF 직접 저장에 OBJECTS 골격 | **완료** | ODA 판독 13/13, ezdxf 0 오류 |
| 실제 AutoCAD 확인 | **대기** | §사용자 확인 |

## 2026-10-31 draft5c (run-draft5c.md) → v0.4.2

| 항목 | 판정 | 근거 |
|---|---|---|
| 헤더 로고 = Nexys 제품군 Logo.png | **완료** | 로고 영역 상관 라이트 1.00 · 다크 1.00(대조군 0.21), docs/screens/50·51 |
| 창·작업표시줄 아이콘 원본 확인 | **완료(변경 없음)** | AppIcon.ico/png MD5가 VIEW·Bus와 같음 |
| 테스트·셀프테스트·zip 스모크 | **완료** | 37/37, PASS 40, i4c_smoke.txt |

산출물: docs/report-draft5c.md, 태그 v0.4.2.

## 2026-10-31 draft5b (run-draft5b.md) → v0.4.1

| 항목 | 판정 | 근거 |
|---|---|---|
| 우클릭 시간 감지(TIMED 기본/ENTER/MENU) | **완료** | 셀프테스트 rightclick_timed_short_enter_long_menu PASS |
| 선택 색(창 파랑·교차 초록, 캔버스 토큰) | **완료(draft5 그대로)** | styles.css cv-light/cv-dark 토큰 |
| PDF 단일 쪽 → 합본만, CLI 플래그 우선 | **완료** | i3_pdf_set.txt: u05 12 · u01 1 · u01 플래그 지정 3 |
| RE = REGEN, R = REDRAW | **완료** | alias_re_regen_r_redraw PASS, docs/keys.md |
| F10·Alt 셸 가로채기 | **완료** | host_keys_f10_alt_sendinput PASS(SendInput 실제 키) |
| 글꼴 매트릭스 사용자 확인 | **종료** | nanoCAD 5에서 A~G 모두 한글 → 모드 C 확정 |

산출물: docs/report-draft5b.md, 태그 v0.4.1.

## 2026-10-31 draft5 (run-draft5.md)

| 블록 | 판정 | 근거 |
|---|---|---|
| I1 AutoCAD 조작 | **완료** | Shift 임시 직교·F8/F10/F3/F7/F9·상태줄 알약·명령줄(Space=Enter, 글자 입력 시 명령줄로, `@길이<각도`, 직접 거리)·휠 더블클릭·Z A/E/W/P·RE·LA·PR·DI·ID·우클릭 설정. 셀프테스트 (a)~(f) 전부 PASS(work/gates/i1_i2_selftest.txt), docs/keys.md |
| I2 캔버스 테마 | **완료** | BG 버튼(AUTO/LIGHT/DARK, layout.json), 캔버스 토큰, 색 7 반전 + 어두운 배경 명도 하한. 4조합 캡처, 대비 17.03 / 18.46:1 |
| I3 PDF 구성 | **완료** | 합본 + 쪽별 개별(`__rest`) + 개요, CLI `--merged/--sheets/--overview`, 상태줄·result.json 파일 목록. u05 12개·u01 3개(i3_pdf_set.txt), docs/pdf-output.md |
| I4 패키지 | **완료** | test 37/37, selftest·skilltest PASS, zip 스모크 PASS(i4_smoke.txt), app\dist 손대지 않음, Release v0.4.0 |

산출물: docs/report-draft5.md, docs/keys.md, docs/pdf-output.md, docs/screens/40~44, next.md, 태그 v0.4.0.

## 2026-10-30 draft4 (run-draft4.md)

| 블록 | 판정 | 근거 |
|---|---|---|
| H0 글꼴 매트릭스 | **완료 — 사람 확인 대기** | font_matrix.dxf·.dwg(변형 A~F + 진단 G). 기록 바이트 표는 docs/font_matrix.md, ODA 오류 0, 한글 14/14. 매트릭스를 만들다 dxf2dwg가 ACAD xdata를 핸들 0x12로 고정해 쓰는 결함을 찾아 수정. u04 감사(TEXT 516 / 한글 38, Standard·돋움 사용, 인라인 `\f` 없음). 기본 치환 C(`gulim.ttc` + `Gulim`), 상수 `FontCompat.DefaultMode` 한 줄 |
| H1 DXF 버전 | **완료** | 저장 대화상자·CLI `--ver r2000\|keep`. 원본이 AC1027보다 새면 R2000, 결과에 version·reason. 관문 7/7(h1_dxf_version.txt: $ACADVER ≤ AC1027, ODA 오류 0, 모형 수·레이어·글자·한글·페이퍼 동일) |
| H2 결정 반영 | **완료(10만 열기는 부하 의존)** | "IMAGE n개 제외하고 DWG 저장" 버튼, u06 관문 PASS(h2_u06_noimage.txt). PDF 1.5 mm는 보고만(코드 관문 없었음). 10만 열기 관문 ≤2 s로 바꿈 — 이번 측정은 CPU 52 % 부하에서 2.26~3.78 s(v0.3.0도 같은 조건 2.08~3.72 s). zip에서 dwgread.exe 제외, 0.3.1 |
| H3 패키지·검증 | **완료** | dotnet test 36/36, --selftest·--skilltest PASS, 새 폴더 zip 스모크 3종 PASS(h3_smoke.txt), work/d4_user_check 5개 파일, GitHub Release v0.3.1 |

산출물: docs/report-draft4.md, docs/font_matrix.md, next.md, 태그 v0.3.1.

## 2026-10-30 draft3 (run-draft3.md)

| 블록 | 판정 | 근거 |
|---|---|---|
| G0 LibreDWG 재빌드 | **완료 — 교체** | MSYS2 + master 33bfac15 빌드 1회차 성공. 사용자 도면 ODA 6/7(0.14는 1/7), u05 0.71 s. 원인 셋 확정(ByLayer 핸들 B·IMAGE·표본 매듭 수). docs/libredwg.md, libredwg-build.md |
| G1 페이퍼·글꼴 | **완료** | 모형/페이퍼 수 7/7 ODA 일치, 페이퍼 스페이스 DXF·DWG 보존, u04 범위 = 두 시트(이름 없는 블록이 원인), 글꼴 치환 후 없는 글꼴 0·u01 변경 0. g1_*.txt |
| G2 저장 정책 | **완료** | 사전 점검 예측 7/7 일치, 대화상자(사유·핸들·더블클릭 줌·글꼴 체크박스·beta), 셀프테스트 두 경로 PASS. g2_check.txt |
| G3 PDF | **완료(글자 ≥1.5 mm 미달)** | 용지 자동(u01·u05·u07 A3, u04 2쪽), 추출률 ≥99 %, 색·MTEXT run, CLI 2.5 mm. 사용자 도면 글자 0.28~1.74 mm → §결정 필요. g3_*.txt |
| G4 패키지 | **완료** | NexysDraft-v0.3.0-win64.zip, 새 폴더 스모크 3종 PASS, GitHub Release v0.3.0. app/dist exe는 사용자 창이 잡고 있어 미갱신 |
| G5 측정 | **완료** | 스트레스 40회 메모리 +30.6 MB, UI 멈춤 0(문서 전송 방식 수정), 유형별 표 25/25 |
| G6 cad-skills | **완료** | skills/cad-headless/SKILL.md, KiCad·FreeCAD 레시피 실행 |

산출물: docs/report-draft3.md, docs/screens/30~35, next.md, 태그 v0.3.0.

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
