# NexysDraft draft5b 보고서 — draft5 결정 반영 → v0.4.1

2026-10-31. 기준은 v0.4.0(2d49a3c)이다.

## 사용자 확인 기록 (종료된 대기 항목)
- draft4 글꼴 매트릭스: nanoCAD 5에서 DWG·DXF 모두 A~G 전부 한글이 보였다 → `FontCompat.DefaultMode` = C(gulim.ttc + xdata Gulim)로 확정했다. 코드는 바꾸지 않았다.
- u04 저장본(한글, Layout1)과 u06 IMAGE 제외본이 nanoCAD에서 모두 열렸다(draft4 확인 완료).

## 결정 반영
| # | 내용 | 결과 |
|---|---|---|
| 1 | 우클릭: AutoCAD 시간 감지 방식. 250 ms 미만 = Enter(확정·반복), 그 이상 = 상황 메뉴. 설정 3가지: TIMED(기본) / ENTER / MENU, `RIGHTCLICK` 명령으로 전환(기억됨) | 셀프테스트 PASS: 실제 pointer 이벤트로 버튼을 누르고 실제 시간을 기다린 뒤 뗐다. TIMED 40 ms → Enter(대기 중이면 LINE 반복, LINE 중이면 종료), 450 ms → 메뉴. ENTER 450 ms → Enter. MENU 40 ms → 메뉴 |
| 2 | 선택 색: 창(좌→우)은 파랑 실선 + 옅은 파랑 채움, 교차(우→좌)는 초록 점선 + 옅은 초록 채움 | draft5에서 이미 이대로 구현돼 있어 바꾸지 않았다. `--canvas-window(-fill)`·`--canvas-crossing(-fill)` 토큰이 cv-light와 cv-dark에 따로 있다 |
| 3 | PDF: 쪽이 1개뿐인 도면(시트 1개에 나머지 없음, 또는 시트 감지 안 됨)은 합본만 만든다. 상태줄과 result.json `note`에 "single sheet — merged only". CLI 플래그를 주면 그대로 따른다 | 관문 i3_pdf_set: u05는 파일 12개, u01은 1개, u01에 세 플래그를 모두 주면 3개 |
| 4 | 별칭: RE = REGEN(기하·렌더 버퍼 재생성), R = REDRAW(다시 그리기만) | 셀프테스트 PASS: RE를 치면 regen 횟수가 0 → 1, R은 1 → 1(그대로). docs/keys.md를 갱신했다 |
| 5 | F10·Alt: 셸의 `PreviewKeyDown`/`PreviewKeyUp`에서 Key.System(SystemKey F10, LeftAlt, RightAlt)을 가로채고 `e.Handled = true`로 처리했다. F10은 `hostkey` 메시지로 페이지에 전달한다(페이지 keydown과 겹치면 150 ms 안의 중복은 무시). 추가로 WndProc에서 문자 없는 `SC_KEYMENU`를 버려 메뉴 모드에 들어가지 않게 했다. Alt+Space(시스템 메뉴)는 그대로 둔다 | **SendInput 실제 키 입력 셀프테스트 PASS**: F10 → POLAR off → on, 이어서 Z가 명령줄에 'z'로 들어갔다. Alt 단독 다음 Z도 'z'. 다시 F10 → POLAR off. 셸이 F10을 2번 전달했다 |

- F10 테스트는 NexysDraft가 전경 창일 때만 키를 보낸다. 다른 프로그램에는 입력하지 않는다. 전경이 아니면 SKIP(UNVERIFIED)로 기록한다. 이번 실행에서는 전경을 얻어 실제로 검사했다.

## 관문
| 관문 | 결과 |
|---|---|
| dotnet test | 37/37 통과(work/gates/d5b_full_tests.log) |
| --selftest | PASS 40 · FAIL 0(work/selftest/d5b_selftest/gates.txt). 새 관문 rightclick_timed_short_enter_long_menu, alias_re_regen_r_redraw, host_keys_f10_alt_sendinput |
| --skilltest | PASS |
| PDF 관문 | u05 파일 12개(21쪽), u01 1개(`single sheet — merged only`), u01에 세 플래그 = 3개 |
| zip 스모크 | 새 폴더(work/d5b/unzip)에서 selftest PASS 40 · FAIL 0, skilltest PASS, CLI PDF 위와 같음(work/gates/i4b_smoke.txt). 여기서는 zip 사본이 전경을 얻지 못해 F10 실제 키 관문이 SKIP였다(개발 빌드 실행 2회는 PASS) |
| 패키지 | `NexysDraft-v0.4.1-win64.zip` 96,711,470 B, SHA256 `9304B8190AAA04E21633FBE91351D1116C85C8BD9AEBC48E9DD3F63691D7FFA0`, dwgread.exe 없음 |

- app\dist는 건드리지 않았다.

## §사용자 확인
1. 실제 키보드에서 **F10**: POLAR 알약이 바뀌는지, 그 다음에 친 글자가 명령줄에 들어가는지(메뉴 모드에 걸리지 않는지).
2. **Alt** 단독을 누른 뒤 다음 키가 먹히지 않는지. **Alt+왼쪽 드래그 팬**이 그대로 되는지(셸이 Alt 키 이벤트를 삼키므로 확인이 필요하다).
3. **우클릭 체감**: 짧게 누르면 확정·반복, 길게(0.25초 이상) 누르면 메뉴가 나온다. 기준 시간이 손에 맞는지.
