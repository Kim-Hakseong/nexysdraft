# NexysDraft draft5f 보고서 — 저장한 DWG가 DWG TrueView에서 중단됨 → v0.4.5

2026-11-10. 동료 보고: v0.4.4에서 수정 후 저장한 DWG를 Autodesk DWG TrueView로 열면
`DWG TrueView Error Aborting — INTERNAL ERROR: !dbspace.cpp@2194: eOk`가 뜨고 프로그램이 닫힌다.

이번에는 개발 PC에 **DWG TrueView 2027**을 설치해서 Autodesk 판독기로 직접 확인했다. draft5d까지는 ezdxf·구조 검사 같은 대용 검사뿐이었다.

## 결론
- 동료 화면의 오류 문구 그대로는 **재현하지 못했다**. TrueView 2027은 v0.4.4 저장본 8개를 창에서 모두 열었다(동료의 TrueView 버전과 도면은 모른다).
- 대신 TrueView의 콘솔 엔진(`accoreconsole.exe`)이 저장본에서 **결함 4가지**를 보고했고, 모두 고쳤다. 그중 1번이 오류 문구(`dbspace` = 모형/종이 공간)와 맞는 유력한 원인이다(추정, UNVERIFIED).
- 수정 뒤 사용자 도면 7개 + 생성 샘플 5개 + 새 도면의 DXF·DWG 저장본 26개가 TrueView 2027에서 경고 없이 열린다.

## 측정한 결함과 수정

| # | TrueView 2027의 보고 | 원인 | 수정 |
|---|---|---|---|
| 1 | DWG: `Duplicate handle(s) ignored. Drawing needs recovery.` (u04). dwgread로 보면 8개 중 6개에 주인 없는 두 번째 `*Model_Space` 블록 레코드(핸들 1F, 소유자가 레이어 "0") | LibreDWG dxf2dwg는 모든 DWG를 자기 `*Model_Space`(1F)로 시작한다. 넘긴 DXF의 모형 공간 레코드가 다른 핸들이면 1F짜리가 남고, 다른 객체가 1F를 쓰면 핸들이 겹친다 | 새로 생성하는 파일은 `*Model_Space` 레코드를 항상 **1F**에 둔다. 1F를 쓰던 객체와 핸들을 맞바꾼다(`DxfWriter.SwapHandles`) |
| 2 | DXF: `Error in LAYER Table. Did not receive PlotStyleName` → 도면 폐기 | LAYER 레코드에 플롯 스타일 핸들(390)이 없다. **v0.4.3·v0.4.4가 저장한 DXF는 전부 해당**한다(draft5d 보고의 "DXF도 안 열림"의 실제 원인) | 생성 파일의 LAYER에 390(ACAD_PLOTSTYLENAME의 placeholder)을 쓴다. 원본 유지 모드의 새·수정 레이어는 원본 레이어의 390을 쓴다. 390 없는 AC1015+ 원본은 새로 생성한다(`DxfSave.NoPlotStyle`) |
| 3 | DXF: `HATCH: expected group code 98` (u07), 반대로 빼면 `expected group code 47` | 47(pixel size)은 경계 경로 중 하나라도 derived 비트(92 & 4)가 있을 때만 있어야 한다. 모서리 루프를 폴리라인 루프로 바꿔 쓰면서 비트는 지우고 47은 남겼다 | derived 경로가 있을 때만 47을 쓴다 |
| 4 | `Two vertices were added to a 2D pline which had no vertices` (u02·u04·u05) | 스플라인 맞춤 폴리라인의 곡선 점(VERTEX 70=8)을 프레임 제어점(16)과 함께 버렸다. **화면에서도 보이지 않던 도형**이다 | 16만 버린다. 저장할 때는 맞춤 플래그(2·4)를 지운 일반 정점으로 쓴다 |

시도했다가 버린 것: 배치(LAYOUT) 객체까지 넣은 DXF를 dxf2dwg에 넘기면 TrueView가 `Open Drawing - Damaged File`로 거부한다. DWG용 DXF는 계속 루트 사전만 쓴다(저장본 DWG에는 LAYOUT 객체가 없고, TrueView 2027은 그대로 연다).

## 관문
| 항목 | 결과 |
|---|---|
| 새 관문 `Gate_D5f_TrueView_OpensEverySave`: 13개 원본 × (편집 후 DXF 저장, DWG 저장) — 규칙 3종 + TrueView 콘솔 엔진으로 열기(종이 공간 전환 포함) | **13/13 PASS**, TrueView 보고 0 (work/gates/d5f_trueview.txt) |
| TrueView 창(dwgviewr.exe)에서 수정본 DWG 8개 + DXF 2개 | 오류 창 0 (work/d5f/gui_after.txt) |
| dwgread 구조: 주인 없는 블록 레코드·중복 핸들 | 수정 전 6/8·1/8 → 수정 후 0/8 (tools/dwgcheck.py) |
| 단위: 핸들 맞바꿈, 스플라인 맞춤 폴리라인, 390 없는 DXF의 저장 방식 | PASS |
| dotnet test | 45/45 |
| zip 스모크 (v0.4.5 새로 풀어서 저장 → TrueView → 셀프테스트) | work/gates/i4f_smoke.txt |

## 개발 PC 검사 도구 (제품에 포함하지 않음)
- `tools/trueview.sh FILE` — TrueView 콘솔 엔진으로 열고 보고 내용을 출력
- `tools/trueview_gui.ps1 FILE` — TrueView 창으로 열고 오류 창 여부를 보고("Foreign DWG"·"Missing SHX" 창은 자동으로 넘김)
- `tools/dwgcheck.py FILE.dwg` — dwgread로 본 DWG 구조(공간 블록 레코드, 주인 없는 레코드, 중복 핸들, 배치)
- TrueView가 없으면 관문의 TrueView 열은 SKIP(UNVERIFIED)으로 남는다.

## 확인하지 못한 것
- 동료 PC의 `!dbspace.cpp@2194` 중단 자체. TrueView 2027에서는 수정 전 파일도 중단되지 않았다. 구버전 TrueView/AutoCAD가 결함 1에서 중단하는지는 모른다.
- 저장본 DWG에 LAYOUT 객체가 없는 점은 그대로다. 종이 공간(배치)에 내용이 많은 도면에서 구버전이 어떻게 반응하는지 모른다.

## §사용자 확인
1. 동료 PC에서 v0.4.5로 **같은 도면을 다시 저장**한 DWG가 TrueView에서 열리는지.
2. 그래도 중단되면: TrueView/AutoCAD **버전**(도움말 → 정보)과, 가능하면 그 저장본 DWG(또는 원본에서 어떤 편집을 했는지).
