# NexysDraft draft3 보고서 (run-draft3.md, 2026-10-30)

결론: **LibreDWG를 master 소스 빌드로 교체했다(플랜 B 아님).** 사용자 도면 7개 중 6개의 DWG가 ODA 대리 관문을 통과하고, 가장 큰 도면도 2초 안에 저장된다. 사전 점검은 7개 모두의 결과를 맞혔다. 포터블 zip v0.3.0을 만들어 새 폴더 스모크를 통과했다. 전체 테스트 32/32, 셀프테스트·스킬 테스트·스트레스 40회 모두 PASS.

## G0 LibreDWG 재빌드 판정 (1순위)

| 도면 | R2000 DXF | 0.14 | master + draft2 라이터 | **master + draft3 라이터 (채택)** |
|---|---|---|---|---|
| u01 | 0.5 MB | FAIL 빈 출력 | FAIL 빈 출력 | **PASS 0.16 s** |
| u02 | 3.9 MB | FAIL 300 s 초과 | PASS 0.63 s | **PASS 0.68 s** |
| u03 | 10.1 MB | FAIL 300 s 초과 | PASS 1.7 s | **PASS 1.8 s** (스플라인 961/961) |
| u04 | 2.2 MB | PASS 126 s | PASS 0.33 s | **PASS 0.36 s** |
| u05 | 4.7 MB | FAIL 300 s 초과 | PASS 0.67 s | **PASS 0.71 s** (페이퍼 2/2) |
| u06 | 0.5 MB | FAIL | FAIL | FAIL — IMAGE 1개, 사전 점검이 예측 |
| u07 | 2.3 MB | FAIL | FAIL 빈 출력 | **PASS 0.34 s** |
| **사용자 합계** | | **1/7** | 4/7 | **6/7** |
| g01·g02·g03·g04·g06·selftest | | g04 FAIL | g04 FAIL | **6/6 PASS** |

- 판정 기준(≥5/7 그리고 u05 ≤60 s) **충족 → 교체 확정.** 근거: work/gates/f2_oda_dwg_v014.txt, f2_oda_dwg_master.txt, f2_oda_dwg.txt.
- PASS 조건: ODA 읽기 오류 0, 레이어·엔티티 수 일치, TEXT/MTEXT 손실 0, 한글 100 %, 페이퍼 스페이스 보존.
- 빌드: MSYS2(winget 무인 설치) + mingw64, master `33bfac15`(0.14.8679), 정적 링크, 패치 없음, 1회차 성공. 기록 docs/libredwg-build.md, 재현 스크립트 tools/build-libredwg.ps1·tools/build-libredwg.sh, 해시 app/tools/libredwg/SHA256SUMS.txt, SOURCE.txt·COPYING 동봉.
- 처음에는 master도 4/7이라 기준 미달이었다. 실패 도면을 이분 탐색해 원인 셋을 찾았다.
  - **u01·u07 빈 출력**: 원본 ByLayer 선종류의 핸들이 `B`였다. 엔티티를 전부 지워도 실패했고, 핸들 B만 새 번호로 바꾸자 통과했다. 라이터가 표준 선종류 3개에 새 핸들을 주도록 고쳤다. draft2가 "0.14 자체 결함"으로 추정한 것은 틀렸다.
  - **u06**: IMAGE 엔티티 1개가 원인이다. 사전 점검이 "읽기 불가"로 막는다.
  - **g04 스플라인 소실**: 생성 표본의 매듭 수가 틀렸다(10개, 9개여야 함). 표본을 고쳤다.
- 덤: 10만 엔티티 R2000 저장이 300 s 초과에서 **6.4 s**로 줄었고 ODA가 읽는다.

## §결정
- **플랜 B 아님.** LibreDWG master 빌드로 교체했다. DWG 저장의 기본 버튼은 사전 점검 결과를 따른다.
- 사전 점검 규칙을 실측에 맞췄다.
  - 막는 사유는 IMAGE, R2000 이후 객체, 저장 시간 초과다. 이 경우 DWG 버튼에 "(beta)"가 붙는다.
  - 동적 블록은 DWG가 열리지만 run-draft3 결정대로 DXF를 기본으로 권한다.
  - 이름 없는 블록, 깨진 블록 참조, 매듭 수가 틀린 스플라인은 막지 않고 빠진다고 안내한다.
  - draft2의 "MTEXT 100개 초과" 규칙은 지웠다. 진짜 원인은 핸들 B였다.

## G1 페이퍼 스페이스·글꼴
- 모형/페이퍼 엔티티 수가 7개 도면 모두 ODA와 일치한다(work/gates/g1_spaces.txt). 페이퍼 스페이스는 DXF·DWG 저장 뒤에도 보존된다. g06_paper 8/8, u05 2/2이고, 한글 제목도 남는다.
- **u04 "모형 뷰의 도곽"의 실제 원인**은 페이퍼 스페이스가 아니었다. u04에는 페이퍼 엔티티가 0개다(ODA에서도 Layout1이 비어 있음). LibreDWG가 이름을 잃은 블록(LINE 2,255개)을 이름 "" 로 내보냈고, 이름 "" 인 INSERT가 그 블록을 8.6배로 그렸다. 리더가 이름 없는 블록을 분리하도록 고쳤다. 이제 u04 범위는 두 시트 범위와 같다. 폭 비는 1.129, 높이 비는 1.000이다(work/gates/g1_u04_extents.txt). 화면은 docs/screens/30_u04_model_view.png.
- 그래서 사용자가 본 "(b) Layout1 빈 배치"는 원본부터 비어 있던 것이다. 손실이 아니다.
- R2000 라이터에 LAYOUT 객체를 다시 넣을 필요는 없었다. 루트 사전만 쓰는 현재 형식으로 페이퍼 엔티티가 dxf2dwg와 ODA를 거쳐 그대로 남는다.
- **글꼴 호환**(저장 대화상자 체크박스, 기본 켜짐, 설정 기억). 치환 결과는 아래와 같다.

| 도면 | 치환 | 내용 |
|---|---|---|
| u01 | 0 | 변경 없음(관문) |
| u02 | 0 | |
| u03 | 2 | Deun txt.shx+whgtxt.shx → 굴림, GHS romans.shx+ghs.shx → 굴림 |
| u04 | 3 | Standard·굴림 Gulim.ttf → 굴림, 돋움 Dotum.ttf → 돋움 |
| u05 | 3 | Standard GulimChe.ttf → 굴림, ROMANS·굴림 Gulim.ttf → 굴림 |
| u06 | 1 | Standard ROMANS+whgtxt.shx → 굴림 |
| u07 | 1 | Standard ROMANS+whgtxt.shx → 굴림 |

치환 뒤 한글 스타일이 참조하는 글꼴 중 PC에 없는 것은 0개다(work/gates/g1_fonts.txt). 치환 형식은 u01의 'PDF Arial' 스타일 기록 방식을 따랐다. 그룹 3·4를 비우고 ACAD xdata 1000에 패밀리 이름, 1071에 플래그를 쓴다. run-draft3가 말한 u01의 '굴림' 스타일은 u01 STYLE 테이블에 없다. u01의 한글은 MTEXT 인라인 `\f굴림`이다.

## G2 저장 정책
- 사전 점검 예측이 ODA 결과와 **7/7 일치**한다(관문 ≥6/7, work/gates/g2_check.txt).
- 기본 버튼은 u01·u04·u05·u07이 DWG, u02·u03이 DXF(동적 블록), u06이 DXF(IMAGE, "(beta)")다.
- 대화상자는 사유를 유형·개수·핸들로 보여 주고, 사유 줄을 더블클릭하면 그 객체로 줌한다. 화면은 docs/screens/31~33.
- 셀프테스트 두 경로가 모두 PASS다. 하나는 "DWG 저장 가능"에 DWG 기본, 다른 하나는 "DXF 권장"에 DXF 기본과 beta 표시다.

## G3 PDF
| 도면 | 용지 | 쪽 | 추출률 | 용지상 글자 중앙값 |
|---|---|---|---|---|
| u01 | A3 가로 | 1 | 99.25 % | 1.06 mm |
| u02 | A4 가로 | 1 | 100 % | 0.90 mm |
| u03 | A3 가로 | 1 | 100 % | 0.28 mm |
| u04 | A3 가로 | **2** | 99.75 % | 1.18 mm |
| u05 | A3 가로 | 10 | 99.52 % | 0.76 mm |
| u06 | A4 가로 | 1 | 100 % | 1.74 mm |
| u07 | A3 가로 | 2 | 99.75 % | 1.34 mm |

- 용지는 도면의 모델 플롯 설정에서 가장 가까운 ISO 규격을 고르고, 설정이 없으면 A3를 쓴다. 가로/세로는 시트 비율로 정한다. 여백은 10 mm이고, 선택 근거를 결과에 기록한다(work/gates/g3_pdf.txt).
- 관문 "u01·u05·u07 = A3", "u04 2쪽", "추출률 ≥99 %(u06 ≥96 %)"는 모두 PASS다.
- 색 관문도 PASS다(work/gates/g3_colours.txt). PANEL 레이어 폴리라인은 빨강 `1 0 0 RG`, 해치는 파랑으로 나온다. MTEXT run 높이 비 2.00, 폭 비율 50 %도 반영된다.
- CLI 새 도면의 글자·DIMTXT는 용지상 2.5 mm로 자동이다. rack_panel PDF에서 600과 2000이 보인다(docs/screens/34). 스킬 테스트도 PASS다.
- **"용지상 TEXT ≥1.5 mm"는 사용자 도면에서 미달이다.** A3 관문을 지키면 원 도면의 작은 주석이 0.28~1.74 mm로 나온다. 원 도면 자체가 그 크기로 설계돼 있다. §결정 필요 1.
- 고친 결함: MTEXT 파서가 절대 높이 `\H4;` 뒤의 상대 높이 `\H1.5x;`를 6배 크기로 그렸다. 새 dwg2dxf가 MTEXT 폭을 제대로 넘기면서 드러났다(g02 "큰 글자").

## G4 공유 패키지
- `app/dist/release/NexysDraft-v0.3.0-win64.zip` (102.6 MB, SHA256 `57AF46DE79258AC7A81EE0D1B255A3E5507ECE16E4D35DA4ABD3FCE0E8FC0CFA`).
- 구성: NexysDraft.exe(자체 포함 단일 파일), tools\libredwg\(바이너리, COPYING, SOURCE.txt, COMMIT.txt, SHA256SUMS, 빌드 스크립트, patches\README), skills\nexysdraft·cad-headless, README.md(한글 1쪽), LICENSES.md. 앱 버전도 0.3.0으로 올렸다.
- 새 폴더 스모크(work/gates/g4_smoke.txt): `--selftest` FAIL 0, `--skilltest` FAIL 0, u01 열기→PDF(A3 1쪽, 텍스트 288)가 모두 PASS다. LibreDWG는 zip 안의 tools\libredwg가 쓰였다.
- GitHub Release v0.3.0(private)에 zip을 첨부했다.
- **주의**: `app/dist/NexysDraft.exe`는 갱신하지 못했다. 오후 2:23부터 사용자가 띄운 draft1 창(u04 열림)이 그 파일을 잡고 있었다. 첫 패키징 시도가 그 폴더의 wwwroot·tools를 지웠고, 열린 창이 DWG를 다시 열 수 있도록 tools\libredwg만 새 빌드로 되돌려 놓았다. 그 창을 닫고 zip의 exe를 쓰거나 `tools\package.ps1`을 다시 실행하면 된다.

## G5 측정 보완
- 스트레스 40회(work/selftest/stress40_d3d): 오류 0, UI 멈춤 0, 메모리 20→40회 **+30.6 MB**(관문 ≤100 MB)로 PASS다.
- 처음 측정에서는 u03을 열 때마다 UI가 380~440 ms 멈췄다. 25,652개 엔티티 문서를 `PostWebMessageAsJson`으로 보내면 UI 스레드에서 JSON 검증이 돈다. 문자열로 보내고 페이지가 파싱하도록 바꿔 멈춤이 0이 됐다.
- 24/25 유형별 표(work/gates/g5_type_table.txt, 셀프테스트 저장본):

| 유형 | NexysDraft DXF | DWG → LibreDWG | DWG → ODA |
|---|---|---|---|
| CIRCLE | 1 | 1 | 1 |
| DIMENSION | 4 | 4 | 4 |
| HATCH | 1 | 1 | 1 |
| INSERT | 16 | 16 | 16 |
| LINE | 1 | 1 | 1 |
| LWPOLYLINE | 1 | 1 | 1 |
| TEXT | 1 | 1 | 1 |
| 합계 | 25 | 25 | 25 |

nanoCAD의 24/25는 여전히 재현되지 않는다(UNVERIFIED, §사용자 확인 3).
- 그대로인 관문: 10만 엔티티 열기 2.0 s(관문 ≤1 s, draft1부터 미달). 팬·줌은 0.7/0.6 ms로 PASS다.

## G6 cad-skills
skills/cad-headless/SKILL.md를 썼다. KiCad 10.0.5와 FreeCAD 1.1은 자동 탐지됐고 Blender는 없다. 레시피 두 개를 실제로 돌렸다.
- KiCad ecc83 PCB를 DXF(632개)로 내보내고 PDF로 만들었다.
- FreeCAD 판재를 DXF로 내보내고 `dli 200`을 넣은 뒤 PDF로 만들었다. PDF에서 "200"이 검색된다.

## 관문 수치
| 관문 | 결과 | 근거 |
|---|---|---|
| 전체 단위·관문 테스트 | 32/32 PASS | work/gates/d3_full_tests.log |
| DXF 자기 왕복 1e-6 | 13/13 PASS | d1a_dxf_roundtrip.txt |
| DWG 왕복(LibreDWG) | 사용자 5/7 + 생성 6/6 PASS, u06·u07 UNVERIFIED | d1b_dwg_roundtrip.txt |
| 한글 깨짐 | 0건 / 7,676 텍스트 | d1_korean.txt |
| ODA DWG(F2/G0) | 12 PASS, 예상 실패 1(u06), 예상 밖 실패 0 | f2_oda_dwg.txt |
| 사전 점검 일치(G2) | 7/7 | g2_check.txt |
| PDF(F3·G3) | 전부 PASS | f3_pdf.txt, g3_pdf.txt |
| 셀프테스트 / 스킬 / 스트레스 40 | PASS / PASS / PASS | work/selftest/d3_selftest, d3_skill, stress40_d3d |
| 10만 팬·줌 ≤16 ms | 0.7 / 0.6 ms PASS | work/selftest/d3_perf |
| 10만 열기 ≤1 s | 2.0 s FAIL(이월) | 같은 곳 |

u06의 왕복 미달은 IMAGE 때문이다. u07은 POLYLINE 2개의 비교 오차가 0.1로, 원인은 확인하지 못했다(UNVERIFIED).

## §결정 필요
1. **PDF 글자 크기 관문**: A3에 맞추면 사용자 도면 글자 중앙값이 0.28~1.74 mm다. "≥1.5 mm"를 지키려면 용지를 키우거나(u03은 A0급) 작은 글자 확대 옵션이 필요하다. 지금은 A3 우선이고 측정값만 보고한다. 어느 쪽을 기본으로 할지 정해 주세요.
2. **u06(IMAGE 포함) DWG**: 지금은 DXF를 권하고 DWG는 beta다. 다음 중 하나를 정해 주세요. (a) R2000 저장에서 IMAGE를 빼고 경고만 한다. (b) 이미지 정의 객체까지 쓰는 작업을 한다. (c) 현행 유지.
3. **dwgread.exe(18 MB)**: 제품은 쓰지 않고 진단용이다. zip 크기를 줄이려면 빼도 된다.

## §사용자 확인 (nanoCAD 5)
확인용 파일은 work/d3_user_check/에 있습니다. NexysDraft CLI로 글꼴 호환을 켜고 저장했고, ODA는 6개 모두 오류 없이 읽었습니다.
1. **u04 저장본의 한글과 Layout1**: u04_hthp_pfd.nexys.dwg와 u04_hthp_pfd.nexys.dxf를 nanoCAD 5로 열어 주세요. 글꼴 호환이 켜진 상태에서 한글이 "?" 없이 보이는지 확인해 주세요. Layout1은 원본부터 비어 있으므로 비어 있는 것이 정상입니다. 모형 공간에 두 시트만 있고 큰 테두리가 없는지도 봐 주세요.
2. **u01·u07 DWG**(이번에 처음 ODA 통과): u01_pid_2504.nexys.dwg와 u07_kj_pid.nexys.dwg가 RECOVER 없이 열리는지, 한글이 보이는지 확인해 주세요.
3. **24/25**: 셀프테스트 저장본 work/selftest/d3_full/files/selftest_saved.dwg를 nanoCAD에서 열었을 때도 24개인지, 그렇다면 빠진 유형이 무엇인지 알려 주세요.

## 산출물
docs/libredwg.md(비교표·원인 #9~#12), docs/libredwg-build.md, docs/findings.md·progress.md·review.md·blockers.md(#15~#18), docs/screens/30~35, next.md, app/tools/libredwg(master 빌드), tools/build-libredwg.ps1·package.ps1·bisect_sections.py·typecount.py, packaging/README.md·LICENSES.md, skills/cad-headless/SKILL.md, 태그 v0.3.0.
