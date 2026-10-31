# RUN-DRAFT5B — NexysDraft v0.4.1: draft5 결정 반영 패치. 끝까지 자율 진행.
전제: C:\rea-work\draft\ v0.4.0 위에서 계속. 공통 규칙 동일. 실행 중 배포본·app\dist는 건드리지 않는다. 예산 1h.
사용자 확인(기록): draft4 글꼴 매트릭스는 nanoCAD 5에서 DWG·DXF 모두 A~G 전부 한글 표시 → FontCompatMode C 확정, 해당 대기 항목 종료. u04 저장본 한글·Layout1·u06 noimage 모두 nanoCAD 통과(draft4 확인 완료).

== 결정 반영 ==
1. 우클릭: AutoCAD 시간 감지 우클릭 — 250 ms 미만 클릭 = Enter(확정/반복), 이상 = 컨텍스트 메뉴. 설정 3가지(시간 감지 기본 / Enter 전용 / 메뉴 전용), RIGHTCLICK 명령으로 전환.
2. 선택 색: 창 선택(좌→우) 파랑 실선 테두리 + 옅은 파랑 채움, 교차 선택(우→좌) 초록 점선 테두리 + 옅은 초록 채움. 캔버스 배경 라이트/다크 둘 다 토큰.
3. PDF: 감지 시트가 1개(rest 없음)면 합본만 생성, 상태줄·result.json에 "single sheet — merged only". CLI 플래그를 명시하면 그대로 따른다.
4. 별칭: RE = REGEN(렌더 버퍼·기하 재생성), R = REDRAW(다시 그리기만). docs\keys.md 갱신.
5. F10: WPF 창에서 F10이 Key.System(SystemKey=F10)으로 메뉴 활성화에 먹히지 않도록 셸 PreviewKeyDown에서 가로채 WebView로 전달하고 e.Handled=true. Alt 단독 키도 메뉴로 가지 않게 동일 처리. WPF 레벨 단위 테스트 또는 SendInput 기반 실제 키 입력 테스트 1개 추가(가능하면). 불가하면 §사용자 확인.
== 관문 == 전체 테스트·--selftest·--skilltest·zip 스모크, u01 PDF 파일 1개·u05 12개, 우클릭 짧게/길게 셀프테스트(마우스 이벤트 시간 조작). v0.4.1 태그·Release.
== 보고 == docs\report-draft5b.md 짧게, §사용자 확인: 실제 키보드 F10·Alt, 우클릭 체감.
