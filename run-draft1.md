# RUN-DRAFT1 — NexysDraft: 개인용 경량 2D CAD(DWG 열기·편집·저장) MVP. 끝까지 자율 진행.
전제: Nexys VIEW 뼈대(C:\rea-work\diadem\app, C# .NET 8 + WebView2, DESIGN v3 토큰, 셀프테스트·publish 체계) 재사용. 개발 원본은 C:\rea-work\draft\. 저장소는 나중에 결정(이 런에서는 만들지 않음). CLAUDE.md는 diadem 것을 복사해 타깃만 교체. Nexys VIEW 코드는 셸·브리지·설정·테스트 체계만 복사하고 측정 뷰어 로직은 섞지 않는다. 공통 규칙: 질문 금지·blockers.md·progress/findings/review·숫자 관문·UI 금지 목록·독자 구현. 총 예산 8h, 일찍 끝나면 백로그.
라이선스: LibreDWG(GPL)는 별도 프로세스(CLI)로만 호출, 링크·코드 복사 금지. dwg2spdf의 LibreDWG 호출 방식 재사용. ODA File Converter는 쓰지 않는다.
한글/인코딩: DXF $DWGCODEPAGE(ANSI_949 등) 기반 바이트 디코딩과 2007+ UTF-8을 모두 처리. MTEXT 서식 코드(\U+XXXX·\P·\f·\H·{}) 파싱. .shx 글꼴 없으면 시스템 TTF(맑은 고딕)로 폴백. 관문: 사용자 샘플의 한글 TEXT/MTEXT 깨짐 0건, 캡처를 docs\screens\에 첨부.
제외(MVP에서 만들지 않음): 트림·연장·필렛·동적 블록·해치 편집·3D·XREF 편집·플롯 스타일.

== D1 포맷 (2h) ==
- DXF 파서(ASCII R12~2018): LINE·LWPOLYLINE·POLYLINE·CIRCLE·ARC·ELLIPSE·TEXT·MTEXT·INSERT(블록)·HATCH(경계만)·DIMENSION(그려진 형태)·레이어·선종류·색. DXF 라이터(R2000 기준).
- DWG: LibreDWG CLI로 DWG→DXF, DXF→DWG(r2000). 사용 중인 LibreDWG 버전·지원 읽기/쓰기 범위를 docs\libredwg.md에 기록. r2000 저장 시 손실되는 요소(동적 블록 등)를 감지해 저장 경고 목록으로 반환.
- 관문: 샘플 DWG 10개(samples\user\ + samples\gen\). (a) DXF 자체 왕복: 엔티티 수·레이어 수·좌표 전부 1e-6 일치. (b) DWG 왕복(LibreDWG 경유): LINE·LWPOLYLINE·POLYLINE·CIRCLE·ARC·ELLIPSE·TEXT·MTEXT·INSERT 좌표 1e-6, HATCH·DIMENSION은 개수 일치. 불일치는 findings에 UNVERIFIED로 사유 기록. nanoCAD에서 열림은 사용자 확인 항목. 사용자 도면은 저장소에 넣지 않는다.
== D2 렌더 (1.5h) ==
- 캔버스 렌더러: 위 엔티티 전부, 레이어 켜기/끄기·동결, 줌·팬·전체 보기, 스냅(끝점·중점·교차점, 공간 인덱스 사용). 글꼴 폴백 규칙 적용. 관문: 엔티티 10만 개 도면(samples\gen\에 생성) 팬·줌 ≤16 ms, 열기 ≤1 s.
== D3 편집 (2h) ==
- 선택(클릭·창·교차), 이동·복사·삭제·회전·STRETCH·OFFSET·MIRROR, 텍스트 더블클릭 수정, 선·폴리라인·원 그리기, 선형 치수 1종, 레이어 변경·새 레이어, Undo/Redo. 명령줄(L, M, CO, E, S, O, MI, Z 등 AutoCAD식 별칭).
== D4 UI (1h) ==
- 목업 HTML 먼저(design\mockup\) → PNG → DESIGN v3 범위에서 구현. 레일: 파일·레이어·속성·명령줄. 라이트 기본, 다크 지원.
== D5 출력·검증·배포 (1h) ==
- PDF는 dwg2spdf 호출. 셀프테스트(실제 사용자 경로: 열기→편집→저장→재열기 일치→DWG 내보내기→PDF). 포터블 exe(dist 비우고 publish). docs\report-draft1.md(블록 판정·관문 수치·캡처·§결정 필요·§사용자 확인), docs\screens\, next.md.
== [백로그] == 트림·필렛, 해치 생성, 블록 편집, 측정 도구, DWG 2018 쓰기(LibreDWG 상태에 따라), 저장소·배포 방식
