# next — NexysDraft 다음 런 후보 (draft1 종료 시점, 2026-10-29)

## 사용자 확인이 먼저 (docs/report-draft1.md §사용자 확인)
1. nanoCAD에서 `work/selftest/full/files/selftest_saved.dwg`와 사용자 도면을 NexysDraft로 열고 DWG로 저장한 파일이 열리는지, MTEXT 크기(`\H` 접두)가 맞는지.
2. PDF(`work/selftest/full/files/selftest.pdf`) 글자 검색이 되는지.
3. 저장소·배포 방식(이 런에서는 만들지 않음): GitHub 저장소 이름, public/private, 설치형(Inno Setup) 필요 여부.

## 백로그 (run-draft1 명시)
- 트림·연장·필렛, 해치 생성, 블록 편집(블록 정의 편집·속성 편집), 측정 도구(거리·면적), DWG 2018 쓰기(LibreDWG git 최신 상태 확인 — 0.14는 r2000까지), 저장소·배포 방식.

## 성능·형식 (이번 런에서 확인된 한계)
- 10만 엔티티 열기 1.9 s(관문 1 s): JSON 대신 공유 버퍼 바이너리 레코드로 전송(직렬화 0.5 s + 페이지 파싱 0.55 s 제거 예상), DXF 토큰화 병렬화.
- `dxf2dwg` 속도(1 MB 이상 분 단위): 저장을 백그라운드 작업 + 진행 표시로, 또는 LibreDWG 최신 빌드 측정.
- DXF→DWG 왕복에서 사라지는 TEXT 5건·동적 블록 표현 블록(`*U`) 원인 추적(d1b UNVERIFIED).
- 스트레스 20회 메모리 증가 +153 MB(관문 100 MB): Server GC 세그먼트 보유가 원인으로 추정(UNVERIFIED) — 워크스테이션 GC 측정값과 비교해 결정.

## UI·편집 (MVP에서 미룬 것)
- 선종류(대시 패턴) 렌더, 선가중치, 비솔리드 해치 패턴 선, 치수 종류(정렬·각도·반지름), 폴리선 OFFSET의 호 구간, 그립 드래그 편집, 다중 창.
- 목업→구현 순서 복구: 파일 레일 버튼 배치 통일, 다크 목업.
