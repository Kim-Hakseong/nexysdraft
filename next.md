# next — NexysDraft 다음 런 후보 (draft5b 종료 시점, 2026-10-31, v0.4.1)

## 사용자 확인이 먼저
- docs/report-draft5b.md §사용자 확인: 실제 키보드 F10·Alt(다음 키가 먹히지 않는지, Alt+드래그 팬), 우클릭 시간 감지 체감.
- docs/report-draft5.md §사용자 확인: Shift 직교·F8·`@100<45`·직접 거리·휠 조작 체감, BG 배경 전환.
- (종료) 글꼴 매트릭스 A~G 모두 한글 → 모드 C 확정. u04·u06 저장본 nanoCAD 열림 확인.

## §결정 (report-draft5) — draft5b에서 모두 반영
- 우클릭 시간 감지, 창 파랑/교차 초록, 단일 쪽 PDF = 합본만, RE = REGEN · R = REDRAW.

## 백로그 (run-draft5 명시)
- 트림·연장·필렛·배열(TR·EX·F·AR), SC·CHA, 동적 입력(커서 옆 좌표), 레이아웃 탭, 블록 삽입 UI.
- 이전 백로그: 동적 블록 → 정적 변환, 자체 DWG R2000 라이터, 설치판(Inno Setup), 바이너리 전송(10만 열기), 측정 도구 확장.

## 기술 부채·관찰
- dxf2dwg가 ACAD 외 앱 xdata를 핸들 0으로 쓴다(u04 266개). 10만 열기는 부하가 낮을 때 다시 잴 것.
- app\dist는 사용자 draft1 창(pid 46024)이 쓰고 있었다 — 갱신은 `package.ps1 -RefreshDist`로, 창을 닫은 뒤에.
