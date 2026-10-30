# next — NexysDraft 다음 런 후보 (draft2 종료 시점, 2026-10-30)

## 사용자 확인이 먼저 (docs/report-draft2.md §사용자 확인)
1. nanoCAD로 열기: work/oda/*.r2000.dwg(u01·u02·u04·u05·u07·selftest) — RECOVER 없이 열리는지, 치수·해치 표시, MTEXT 크기.
2. nanoCAD로 열기: work/oda/*.ours.dxf — 한글 TEXT가 제대로 보이는지("??" 사라졌는지), 엔티티 수 25/25.
3. PDF: work/pdf/u05_hthp_pid.pdf(10쪽)에서 Ctrl+F 한글·영문 검색, 쪽 수.

## 백로그 (run-draft2 명시)
- OCR 옵션(래스터 포함 도면), cad-skills 팩(KiCad·FreeCAD·Blender 헤드리스 레시피), 바이너리 전송(10만 엔티티 열기 1.9 s → ≈0.9 s), 설치판(Inno Setup·.dwg 연결), 측정 도구, 트림·필렛.

## LibreDWG
- MSYS2 설치 후 업스트림 master 빌드: dxf2dwg 속도·MTEXT 높이·해치 엣지 루프·레이아웃 객체가 고쳐졌는지 측정. 고쳐졌으면 소스 패치 없이 교체(COPYING·소스 동봉).
- u06(동적 블록 `*U` 블록) DWG가 ODA에서 "Object improperly read": dwgread JSON으로 블록 헤더 owner 추적.

## PDF
- MTEXT run 단위 높이·폭 비율·밑줄, 선종류 대시, 선가중치별 선폭 표, 페이지 크기 자동(A3 고정 → 도곽 비율), 북마크.

## 품질 관문
- 스트레스 메모리 관문(첫 열기 이후 20→40회 ≤100 MB)은 draft2에서 재측정하지 않았다 — 다음 런에서 측정.
