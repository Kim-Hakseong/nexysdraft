# RUN-DRAFT2 — NexysDraft: 타 CAD 호환 DWG/DXF · PDF 내장 · CLI+스킬 · 저장소. 끝까지 자율 진행.
전제: C:\rea-work\draft\ draft1 위에서 계속. 공통 규칙 동일(질문 금지·blockers·progress/findings/review·숫자 관문·UI 금지 목록). 총 예산 8h, 일찍 끝나면 백로그.
전제 변경: 용도는 본인+동료(사내) 공용. 포터블 exe + tools\ 폴더, 외부 설치 요구 0. ODA File Converter는 제품에 절대 포함·호출하지 않고, 개발 PC 검증 관문으로만 쓴다(경로는 F0에서 자동 탐색해 config\dev.json에 기록, 없으면 관문 SKIP 표시). LibreDWG는 CLI 분리 유지, 필요 시 소스 패치 후 재빌드하고 패치·소스·COPYING 동봉.
관문 조정(확정): 열기 사용자 도면(≤3만) ≤1 s, 10만 ≤2 s. 스트레스 메모리는 첫 열기 이후 20→40회 증가 ≤100 MB.
nanoCAD 실측(사용자 확인): selftest_saved.dwg는 RECOVER 필요(AcDbLayout ModelType flag invalid), 치수·해치 미표시. selftest_saved.dxf는 복구 없이 열리나 24/25개, 한글 TEXT가 "??"로 표시(DXF에서도).

== F0 ODA 자동 탐색 (10분) ==
- ODAFileConverter.exe를 자동으로 찾아 config\dev.json에 기록: 탐색 순서 (1) where.exe ODAFileConverter, (2) C:\Program Files\ODA\**, C:\Program Files (x86)\ODA\**, (3) 레지스트리 HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall 및 Wow6432Node에서 DisplayName에 "ODA File Converter" 포함 항목의 InstallLocation, (4) 시작 메뉴 바로가기(.lnk) 대상. 여러 개면 버전 최신. 찾으면 더미 변환으로 실행 확인 후 경로·버전을 dev.json과 docs\libredwg.md에 기록. 못 찾으면 blockers.md에 탐색한 경로 목록을 적고 ODA 관문은 전부 SKIP(사유 표시)으로 진행.
- dev.json은 .gitignore 대상. ODA는 개발 PC 검증 전용, 제품 코드·배포물·셀프테스트 기본 경로에서 참조 금지.
== F1 DXF 라이터 호환 (1.5h) ==
- 한글: TEXT·MTEXT·ATTRIB·DIMENSION 문자열의 비ASCII를 \U+XXXX로 써서 DXF를 순수 ASCII로 만든다(AC1015·AC1018·AC1024 공통). 동시에 STYLE: 원본 도면의 STYLE 테이블은 그대로 보존, 새 텍스트는 원본 스타일을 따르고, 새 도면의 Standard는 TTF 글꼴(malgun.ttf, 그룹 3)로 쓴다. 읽기 쪽은 \U+XXXX·CP949·UTF-8 전부 수용(이미 됨, 회귀 테스트).
- 24/25 불일치: selftest_saved.dxf를 ODA로 DXF→DXF 변환해 엔티티 수를 비교, 빠진 1개를 핸들·유형으로 특정하고 수정(0길이·잘못된 그룹코드·누락 필수 코드 의심).
- 관문: 사용자 도면 7개 + selftest를 우리 DXF로 저장 → ODA 읽기 오류 0 → 엔티티 수·레이어 수 일치, 한글 문자열 ODA 왕복 후 100% 동일.
== F2 DWG 쓰기 (2.5h) ==
- Layout 플래그: R2000 DXF의 OBJECTS(LAYOUT·DICTIONARY)와 BLOCK_RECORD(*Model_Space·*Paper_Space)를 최소 정규형으로 쓰거나 생략해 dxf2dwg가 기본값을 만들게 하고, ODA로 읽어 오류 0인 형태를 찾는다. 안 되면 LibreDWG 소스에서 LAYOUT 쓰기 부분을 패치해 재빌드.
- DIMENSION·HATCH 소실: dxf2dwg 출력을 ODA로 읽어 개수 비교로 재현. 우회 순서: (1) DIMENSION의 *D 익명 블록을 명시적으로 함께 기록, (2) 안 되면 저장 옵션 "치수·해치 분해해 저장"을 기본 ON으로 하고 저장 대화상자에 표시.
- TEXT 소실(u01 1·u05 4): 원인 특정(핸들·내용·특수문자·높이), 전처리 우회, 불가 시 손실 목록 대화상자(핸들·내용·위치, 더블클릭 줌). 조용한 소실 0.
- 저장 UX: 백그라운드 저장+진행률+취소, 예상 시간 표시(엔티티 수 실측 테이블), 저장 중 UI 정지 0. LibreDWG git 최신으로 u03 dxf2dwg 시간 재측정해 docs\libredwg.md 버전표 갱신, 빠르면 교체.
- 관문: 사용자 도면 6개(u03 제외 가능, 사유 기록) + selftest → DWG 저장 → ODA DWG→DXF 변환 오류 0, 엔티티 수·레이어 수 일치, TEXT·MTEXT 소실 0, 한글 100% 보존. nanoCAD 수동 열기는 §사용자 확인에 파일 목록으로.
== F3 PDF 내장 (2h) ==
- dwg2spdf 의존 제거. PdfSharp(MIT) 또는 자체 PDF 1.4 라이터로 벡터(선·호·원·타원·폴리라인·해치 경계·치수 블록)를 그리고, 글자는 실제 PDF 텍스트로 쓴다(맑은 고딕 서브셋 임베드, 회전·높이·정렬 반영, MTEXT 서식 제거). 레이어 색·선폭 반영, 다크 아닌 인쇄 색.
- 시트 분할: dwg2spdf와 같은 규칙(도면틀 자동 감지, 없으면 전체 1장) → 시트별 페이지 + __ALL.pdf. OCR은 백로그(래스터 포함 도면용 옵션).
- 관문: 사용자 도면 7개 PDF 생성 → pdftotext로 한글·영문 TEXT 문자열 추출률 ≥99%, 페이지 수 = 시트 수, 1 MB 도면 ≤5 s. 셀프테스트에 PDF 검색 단계 추가.
== F4 CLI + 스킬 (1.5h) ==
- NexysDraft.exe --script job.txt [--out dir] [--json]: 줄 단위 명령(open·명령줄 별칭 전부·layer·save dxf/dwg·pdf·query entities|layers|extents). GUI 없이 실행, 종료 코드·JSON 결과. 오류 시 줄 번호·사유.
- skills\nexysdraft\SKILL.md: CLI 문법·예시 3개(도면 열어 텍스트 치환 후 DWG, 빈 도면에 랙 패널 외곽+치수 그려 PDF, 도면 엔티티 통계 JSON)·관문. Claude Code가 읽고 스크립트를 생성하는 셀프테스트 1종(랙 패널 예시 실제 실행 → PDF 생성 → pdftotext 검증).
== F5 실사용·저장소 (0.5h) ==
- 드래그앤드롭 열기, Ctrl+S, 수정 후 닫기 경고, 최근 파일 10개.
- GitHub Kim-Hakseong/nexysdraft private, .gitignore에 samples\user\·work\·dist\·config\dev.json. 관문: 새 clone → 빌드 → dotnet test 전부 + --selftest PASS → 태그 v0.2.0-draft2. 사용자 도면·회사명 문자열 저장소 0건(grep).
== 보고 == docs\report-draft2.md(블록 판정·관문 수치·LibreDWG 결함별 우회 결과·§결정 필요·§사용자 확인: nanoCAD로 열어볼 파일 목록과 볼 것), docs\screens\, next.md.
== [백로그] == OCR 옵션, cad-skills 팩(KiCad·FreeCAD·Blender 헤드리스 레시피), 바이너리 전송, 설치판(Inno Setup·.dwg 연결), 측정 도구, 트림·필렛
