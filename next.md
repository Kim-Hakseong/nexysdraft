# next — NexysDraft 다음 런 후보 (draft4 종료 시점, 2026-10-30)

## 사용자 확인이 먼저 (docs/report-draft4.md §사용자 확인)
1. nanoCAD 5로 work/d4_user_check/font_matrix.dwg·.dxf를 열어 한글이 보이는 변형 문자(A~G) → `FontCompat.DefaultMode` 한 줄을 바꾸고 v0.3.2.
2. u04_hthp_pfd.nexys.dwg·.dxf(R2000) 열림·한글, u06_panel.noimage.dwg 열림.

## 기술 부채·관찰
- dxf2dwg가 ACAD 외 앱 xdata를 핸들 0으로 쓴다(u04 266개, ODA `_APP0`). R2000 저장에서 이 xdata를 뺄지, 업스트림 이슈(ACAD 0x12 고정·이름 해석 실패)를 낼지 결정.
- 10만 열기: 부하가 낮을 때 v0.3.0과 번갈아 다시 잴 것(이번 2.26~3.78 s, CPU 52 %). 바이너리 전송은 백로그.
- app/dist 갱신: 사용자 draft1 창(pid 46024)을 닫은 뒤 `powershell -File tools\package.ps1`.
- D1 DWG 왕복: u07 POLYLINE 2개 좌표 비교 오차 0.1(UNVERIFIED). dwg2dxf master가 u04에서 "Invalid LWPOLYLINE.widths ×4"를 낸다.

## 백로그 (run-draft3 명시)
- 레이아웃 탭 UI, 동적 블록 → 정적 변환, 자체 DWG R2000 라이터(플랜 C), 설치판(Inno Setup·.dwg 연결), OCR 옵션, 바이너리 전송, 측정 도구, 트림·필렛.
