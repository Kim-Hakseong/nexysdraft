# next — NexysDraft 다음 런 후보 (draft3 종료 시점, 2026-10-30)

## 사용자 확인이 먼저 (docs/report-draft3.md §사용자 확인)
1. nanoCAD 5로 work/d3_user_check/u04_hthp_pfd.nexys.dwg·.dxf를 열어 한글("?" 없음)과 모형 공간(두 시트, 큰 테두리 없음)을 확인. Layout1은 원본부터 비어 있다.
2. nanoCAD 5로 u01·u07 .nexys.dwg가 RECOVER 없이 열리는지 확인.
3. 셀프테스트 저장본의 24/25 재현 여부와 빠진 유형.

## §결정 필요 (보고서)
- PDF 글자 크기 관문(A3 우선 vs ≥1.5 mm), u06 IMAGE 처리(빼고 저장 / 이미지 정의까지 쓰기 / 현행), dwgread.exe 동봉 여부.

## 백로그 (run-draft3 명시)
- 레이아웃 탭 UI(페이퍼 스페이스 보기), 동적 블록 → 정적 변환, 자체 DWG R2000 라이터(플랜 C), 설치판(Inno Setup·.dwg 연결), OCR 옵션, 바이너리 전송(10만 엔티티 열기 2.0 s → ≤1 s), 측정 도구, 트림·필렛.

## 기술 부채·관찰
- D1 DWG 왕복: u07 POLYLINE 2개 좌표 비교 오차 0.1(UNVERIFIED). dwg2dxf master가 u04에 "Invalid LWPOLYLINE.widths ×4"를 낸다(개수는 ODA와 일치).
- LibreDWG 업스트림에 핸들 B 문제(dxf2dwg가 원본 표준 선종류 핸들을 그대로 쓸 때 ODA가 빈 도면으로 읽음) 재현 사례를 이슈로 낼지 검토.
- app/dist/NexysDraft.exe 갱신: 사용자 draft1 창을 닫은 뒤 `powershell -File tools\package.ps1 -Version 0.3.0`.
