# next — NexysDraft 다음 런 후보 (draft5 종료 시점, 2026-10-31)

## 사용자 확인이 먼저
- docs/report-draft5.md §사용자 확인: Shift 직교·F8·`@100<45`·직접 거리·휠 조작 체감, 실제 키보드 F10, BG 배경 전환.
- draft4에서 남은 것: nanoCAD 5 font_matrix 변형 문자(A~G) → `FontCompat.DefaultMode` 한 줄, u04·u06 저장본 열림.

## §결정 필요 (report-draft5)
- 우클릭 기본(Enter vs 메뉴), 창 선택 색(파랑), 시트 1개 도면의 개별·개요 PDF 생략 여부, RE = REDRAW vs REGEN.

## 백로그 (run-draft5 명시)
- 트림·연장·필렛·배열(TR·EX·F·AR), SC·CHA, 동적 입력(커서 옆 좌표), 레이아웃 탭, 블록 삽입 UI.
- 이전 백로그: 동적 블록 → 정적 변환, 자체 DWG R2000 라이터, 설치판(Inno Setup), 바이너리 전송(10만 열기), 측정 도구 확장.

## 기술 부채·관찰
- dxf2dwg가 ACAD 외 앱 xdata를 핸들 0으로 쓴다(u04 266개). 10만 열기는 부하가 낮을 때 다시 잴 것.
- app\dist는 사용자 draft1 창(pid 46024)이 쓰고 있었다 — 갱신은 `package.ps1 -RefreshDist`로, 창을 닫은 뒤에.
