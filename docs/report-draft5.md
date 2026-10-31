# NexysDraft draft5 보고서 — AutoCAD 조작 체계 · 캔버스 독립 테마 · PDF 출력 구성 → v0.4.0

2026-10-31. 기준은 v0.3.1(a94e68f)이다. 사용자 피드백(실사용 1일차): "선 그릴 때 Shift로 수직/수평 고정이 안 된다. AutoCAD 쓰던 사람이 바로 쓸 수 있게 자주 쓰는 키·조작을 맞춰 달라."

## 요약

| 블록 | 판정 | 핵심 수치 |
|---|---|---|
| I1 AutoCAD 조작 체계 | 완료 | 셀프테스트 관문 (a)~(f) + 직접 거리 + DI/ID 모두 PASS. Shift·F8 직교 선 20개 각도 오차 0, `@100<45` → (70.710678, 70.710678) |
| I2 캔버스 독립 테마 | 완료 | 앱 2 × 캔버스 2 캡처 4장. 색 7 대비 17.03:1(밝은 배경) / 18.46:1(어두운 배경), 기준 ≥ 4.5 |
| I3 PDF 출력 구성 | 완료 | u05 파일 12개(합본 10쪽 + 개별 10 + 개요 1), u01 3개, 모든 파일 pdftotext 추출됨 |
| I4 패키지 | 완료 | dotnet test 37/37, --selftest(PASS 37)·--skilltest PASS, 새 폴더 zip 스모크 PASS, Release v0.4.0 |

## I1 AutoCAD 조작 체계

구현 내용(키·별칭 전체 표는 docs/keys.md):

- **그리기 보조**
  - Shift를 누르고 있는 동안 임시 직교(ORTHO가 켜져 있으면 반대로 잠시 꺼짐).
  - F8 직교, F10 극좌표 추적(기본 90°, `POLAR 45`, 각도선 ±5° 안에서 붙음), F3 객체 스냅, F7 그리드 표시, F9 그리드 스냅(`GRID n`·`SNAP n`).
  - 객체 스냅이 잡히면 직교·극좌표보다 우선한다(AutoCAD와 같음).
  - 직교·극좌표가 켜져 있으면 기준점에서 추적선(점선)을 그린다. 좌표 표시에 `길이 < 각도`를 함께 보여 준다.
- **상태줄 알약**: SNAP · GRID · ORTHO · POLAR · OSNAP(nanoCAD식). 켜짐은 panel-alt 배경과 굵기로 표시하고 색 테두리는 쓰지 않는다. 클릭해도 바뀐다.
- **명령줄**
  - 캔버스에 포커스가 있어도 글자를 치면 명령줄로 간다.
  - Enter·Space는 확정, 명령이 없을 때는 마지막 명령을 반복한다. 명령줄 안에서도 Space = Enter다.
  - Esc는 취소이고, 명령이 없을 때는 선택을 해제한다.
  - 우클릭 기본은 Enter(확정·반복)다. `RIGHTCLICK MENU`로 메뉴 방식으로 바꿀 수 있고, 이 설정은 기억된다.
  - 좌표 입력: `x,y`, `@dx,dy`, `@길이<각도`, `길이<각도`.
  - 숫자만 치면 직접 거리 입력이다. 지금의 직교·극좌표·Shift 방향을 따른다.
- **마우스**
  - 휠은 커서 기준 줌, 휠 드래그는 팬, **휠 더블클릭은 전체 보기**다.
  - Shift+휠 드래그는 아무 동작도 하지 않는다(AutoCAD 궤도와 충돌하지 않게).
  - 창 선택은 파란 실선, 교차 선택은 초록 점선이다. Shift+클릭으로 선택을 더하거나 뺀다.
- **키·별칭**
  - Ctrl+P(PDF)를 추가했다. Ctrl+Z/Y, Del, Ctrl+A, Ctrl+S, Ctrl+Shift+S, Ctrl+O는 기존 그대로다.
  - 별칭 추가: `Z`(A/E/W/P, 줌 이전 기록 30개), `RE`(다시 그리기), `LA`(레이어 레일), `PR`(속성 레일), `DI`(거리), `ID`(좌표). DI·ID 결과는 명령줄에만 나온다.
  - TR·EX·F·AR·SC·CHA는 백로그다.
- **결함 수정**: 캔버스 자체의 keydown 리스너와 document 리스너가 모두 Enter/Space를 처리해서, 캔버스에 포커스가 있을 때 반복이 두 번 실행될 수 있었다. document 하나로 합쳤다.

### 관문 (work/gates/i1_i2_selftest.txt, work/selftest/d5_selftest/gates.txt)

셀프테스트가 페이지 안에서 KeyboardEvent·PointerEvent를 실제로 발생시켜 검사한다.

| 관문 | 결과 |
|---|---|
| (a) Shift를 누른 채 찍은 선 10개(5개는 수평에 가깝게, 5개는 수직에 가깝게) | PASS — 각도 오차 최대 0 (기준 ±1e-9) |
| (b) F8 직교 켜고 Shift 없이 같은 선 10개, 상태줄 ORTHO 알약 켜짐, F8로 꺼짐 | PASS — 오차 0 |
| (c) `0,0` 다음 `@100<45` | PASS — (70.710678, 70.710678), 기준 1e-4 |
| 직접 거리: Shift 직교 방향으로 `80` | PASS — (380, −250) |
| (d) Space 반복으로 원 3개 | PASS — 0 → 3 |
| (e) LINE 중간(점 1개)과 MOVE 중간(기준점 지정)에서 Esc | PASS — 엔티티 25 → 25, 좌표 변화 없음, 도구 select |
| (f) 줌을 바꾼 뒤 휠 더블클릭 | PASS — 화면 중심·배율이 Z E와 같음 |
| DI `0,0`→`30,40` / ID `12.5,-3` | PASS — "Distance = 50", "X = 12.5" 출력, 엔티티 추가 없음 |

캡처: docs/screens/40_keys_modes_status_pills.png.

## I2 캔버스 독립 테마

- 도면 구역에 앱 테마와 별도로 배경 설정을 두었다(Nexys VIEW 차트 영역 방식).
  - 캔버스 툴바 오른쪽 끝 **BG** 버튼으로 AUTO(앱 테마 따름) → LIGHT → DARK 순서로 바뀐다.
  - 설정은 layout.json(`CanvasTheme`)에 기억된다.
- 캔버스 색은 모두 `.canvas-wrap.cv-light/.cv-dark` 토큰이다(styles.css): 배경, 그리드, 선택 강조, 스냅 표식, 십자 커서, 그립, 추적선, 창·교차 선택 상자, 좌표 알약.
- 색 7(ByLayer 흰/검)은 배경에 맞춰 반전된다. 어두운 배경에서는 가장 밝은 채널이 110 미만인 색(ACI 8, 250~251 같은 검정 계열)을 회색 쪽으로 밝힌다. 그 밖의 ACI 색은 그대로 둔다.
- 레이어 레일의 색 견본은 앱 패널 위에 있으므로 앱 테마를 따른다.

### 관문

| 앱 | 캔버스 | 색 7 SOLID / 배경 | 대비 |
|---|---|---|---|
| light | light | #17181A / #FBFAF8 | 17.03:1 PASS |
| light | dark | #FFFFFF / #121416 | 18.46:1 PASS |
| dark | light | #17181A / #FBFAF8 | 17.03:1 PASS |
| dark | dark | #FFFFFF / #121416 | 18.46:1 PASS |

AUTO + 앱 light → 캔버스 light: PASS. 캡처는 docs/screens/41~44에 있다.

## I3 PDF 출력 구성 (docs/pdf-output.md)

- 확인한 기존 구성: `이름.pdf`(시트별 쪽 + 시트 밖 내용 "나머지" 쪽)와 `이름__001.pdf…`(감지된 시트만, 시트가 1개 이하면 없음).
- 이번 구성:
  - 개별 파일을 **합본의 쪽마다 1개**로 정리했다. 나머지 쪽은 `__rest.pdf`, 시트가 감지되지 않은 도면은 `__001.pdf`.
  - `이름__overview.pdf`(모형 공간 전체 1장, 용지 자동)를 추가했고 기본으로 켜져 있다.
  - CLI: `pdf PATH [--merged] [--sheets] [--overview]`. 플래그가 없으면 셋 다 만든다.
  - 만든 파일 목록과 쪽수를 상태줄과 명령줄(`PDF: n file(s), m page(s) — …`), result.json의 `files`에 남긴다.
- 관문(work/gates/i3_pdf_set.txt):
  - u05: 파일 12개(합본 10쪽 = 시트 9 + 나머지 1, 개별 10, 개요 1). 모든 파일에서 글자가 추출됐다(개별 681~5,473자, 나머지 12자).
  - u01: 파일 3개.
  - 배포본 CLI로도 같다: u05는 12개 파일·21쪽, `--overview`만 주면 1개, u01은 3개.

## I4 패키지

- `dotnet test` 37/37(work/gates/d5_full_tests.log). 새 관문은 I3이다.
- `--selftest` PASS 37·FAIL 0, `--skilltest` PASS.
- zip `NexysDraft-v0.4.0-win64.zip`(96,681,929 B, SHA256 `12F40134F3CEACD1429CB0551F4E455C994E76A660ED14958A616CB882C0A9E0`). docs\keys.md와 docs\pdf-output.md를 포함하고 dwgread.exe는 넣지 않았다. 새 폴더 스모크 결과는 work/gates/i4_smoke.txt에 있다.
- README(저장소·zip)에 키 표 링크, 조작 요약, 도면 배경, PDF 구성을 넣었다. 앱 도움말(F1)도 갱신했다.
- **app\dist는 건드리지 않았다.** package.ps1은 이제 `-RefreshDist`를 줄 때만 app\dist를 바꾼다.
- 태그 v0.4.0, GitHub Release(private).

## §결정 필요

1. **우클릭 기본값**: 지시대로 Enter(확정·반복)로 정했다. AutoCAD의 출고 기본은 상황 메뉴라서, 메뉴를 기본으로 바꿀지 정해 주세요(`RIGHTCLICK MENU`는 이미 있음).
2. **창 선택 색**: AutoCAD처럼 파랑(창)과 초록(교차)으로 바꿨다. draft4까지는 주황(창)이었다.
3. **개별 PDF의 중복**: 시트가 1개뿐인 도면(u01)은 `__001.pdf`와 `__overview.pdf`가 합본과 같은 내용이다. 지시의 "u01 → 3개"에 맞춰 두었는데, 시트가 1개면 개별·개요를 생략할지 정해 주세요.
4. **`RE` 별칭**: 지시대로 다시 그리기(REDRAW)로 정했다. AutoCAD에서 RE는 REGEN이다(REGEN은 그대로 있음).

## §사용자 확인 (실사용 체감)

1. 선을 그리며 **Shift**를 누르고 있을 때 수평/수직으로 고정되는지, F8로 켰을 때와 같은지.
2. `@100<45`, `@30,0`, 숫자만 입력(직접 거리)이 기대대로 되는지.
3. 휠 줌·휠 드래그 팬·**휠 더블클릭 전체 보기**가 손에 맞는지.
4. 실제 키보드에서 **F10**이 극좌표로 동작하는지(셀프테스트는 페이지 안의 이벤트로 검사했다. Windows에서 F10은 메뉴 키라서 실제 키보드 입력은 확인하지 못했다, UNVERIFIED).
5. BG 버튼으로 도면 배경만 바꿨을 때 색 7과 선택·스냅 표시가 잘 보이는지.
