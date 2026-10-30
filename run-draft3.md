# RUN-DRAFT3 — NexysDraft: LibreDWG 재빌드 판정 · 페이퍼 스페이스 보존 · nanoCAD 글꼴 호환 · DWG 저장 정책 · PDF 마무리 · 공유 패키지. 끝까지 자율 진행.
전제: C:\rea-work\draft\ draft2(v0.2.0-draft2) 위에서 계속. 공통 규칙 동일(질문 금지·blockers·progress/findings/review·숫자 관문·UI 금지 목록). ODA는 config\dev.json 경로로 개발 검증 전용. 총 예산 9h, 일찍 끝나면 백로그.
결정(확정): 동적 블록 도면은 DWG 저장 시 경고+DXF 권장(변환기는 백로그). LibreDWG 재빌드 허용(MSYS2 설치 가능). PDF 용지 자동. 공유는 포터블 zip.
nanoCAD 실측(사용자 확인): u02·u04 r2000.dwg는 리커버 없이 열림(ODA 대리 관문 유효). u04는 (a) NexysDraft 모형 뷰에 페이퍼 스페이스 도곽이 같이 그려짐(67 무시), (b) 저장한 DWG의 Layout1이 비어 있음(배치 내용 소실), (c) 한글이 ?로 표시되나 텍스트 편집창에는 한글이 정상(데이터 손실 아님). 원인: nanoCAD 로그 "Cannot find: GulimChe.ttf", "AllInOneUni/AllInOneBig.shx substituted as txt.shx". 대조: 원본 u01을 nanoCAD 5로 열면 스타일 글꼴 '굴림'·'PDF Arial'로 한글이 정상 표시됨.

== G0 LibreDWG master 재빌드·판정 (2h) ==
- MSYS2를 winget으로 무인 설치(winget install MSYS2.MSYS2 --silent --accept-package-agreements --accept-source-agreements). UAC로 실패하면 blockers에 적고 G0는 "사용자 설치 후 재실행"으로 남기고 G1로.
- mingw64에서 LibreDWG master(커밋 해시 기록) 빌드: autogen → configure --disable-bindings --disable-python --disable-docs → make. 실패하면 최신 성공 커밋이나 0.14 이후 태그로 후퇴해 재시도(최대 3회), 시도 로그 docs\libredwg-build.md.
- 새 dxf2dwg로 사용자 도면 7개 + g01~g04 + selftest 재측정: ODA 읽기 오류, 엔티티·레이어·TEXT/MTEXT·DIM/HATCH/SPLINE 수, 페이퍼 스페이스 엔티티 수, 한글, 시간(300 s 타임아웃). 0.14와 나란히 표(docs\libredwg.md).
- 판정 관문: 사용자 도면 ODA 오류 0이 ≥5/7 이고 u05 저장 ≤60 s → 교체(tools\libredwg\, tools\build-libredwg.ps1, 커밋 해시·SOURCE.txt·COPYING, 패치는 patches\). 미달 → 0.14 유지하고 report §결정에 "플랜 B 확정" 명시.
== G1 페이퍼 스페이스·nanoCAD 글꼴 호환 (2h) ==
- 읽기: 엔티티 67 플래그와 소속 블록(*Model_Space/*Paper_Space*)을 모델에 보존. 모형 뷰에는 모형 공간만 그림(페이퍼 스페이스는 숨김, 레이아웃 탭 UI는 백로그). 파일의 레이어 동결/끄기 초기 상태 존중. query extents·시트 감지·PDF는 모형 공간만.
- 쓰기(DXF Preserve·R2000 둘 다): 페이퍼 스페이스 엔티티를 *Paper_Space 블록(67=1)으로 그대로 기록. R2000 라이터는 OBJECTS 최소화(루트 사전)를 유지하되 LAYOUT 1개(Model+Layout1)와 BLOCK_RECORD 340 링크를 최소 정규형으로 다시 넣는 변형을 시험해 "ODA 오류 0 + Layout1에 엔티티 있음" 조합을 찾는다. 못 찾으면 R2000Check에 "배치 내용 n개 소실" 경고를 넣고 조용한 소실 0.
- 글꼴 호환(저장 대화상자 체크박스, 기본 ON, 설정 기억): STYLE의 글꼴 파일(그룹 3)이 이 PC의 %WINDIR%\Fonts·사용자 Fonts 폴더·앱 fonts\에 실제로 없거나 bigfont(그룹 4)가 없는 .shx인 스타일만 치환. 치환 형식은 samples\user\u01의 '굴림' 스타일이 DXF에 기록된 방식(그룹 3 값·그룹 4·ACAD xdata 1000/1071 글꼴 패밀리)을 그대로 복사한다 — nanoCAD 5에서 한글 표시가 확인된 형식. 이름 매핑: GulimChe/Gulim→굴림, BatangChe/Batang→바탕, DotumChe/Dotum→돋움, 그 외·한글 bigfont .shx→굴림. 있는 글꼴은 건드리지 않음. 치환 목록(스타일·원래 글꼴·새 글꼴)을 저장 결과·result.json에 표시.
- 관문: u04 → NexysDraft 모형 뷰 extents = 두 시트 범위(페이퍼 도곽 제외); DXF·DWG 저장 후 ODA DWG→DXF에서 *Paper_Space 엔티티 수가 원본과 일치(불가 시 경고 수 일치); 사용자 도면 7개 모형/페이퍼 엔티티 수 분리 표; u04 저장본의 STYLE 테이블에 PC에 없는 글꼴 참조 0개(스크립트로 검사), u01 STYLE은 변경 0.
== G2 DWG 저장 정책 (1h) ==
- R2000Check 사전 점검: 동적 블록·SPLINE·IMAGE·R2000 이후 객체·배치 손실(G1 결과)·추정 저장 시간·글꼴 치환 목록. 대화상자에 "DWG 저장 가능 / DXF 권장(사유 목록: 핸들·유형·개수, 더블클릭 줌)". 기본 선택은 점검 통과면 DWG, 아니면 DXF. 플랜 B면 기본 항상 DXF, DWG 버튼에 beta 표기. 셀프테스트에 두 경로.
- 관문: u01~u07 사전 점검 결과가 G0 실측(ODA 통과/실패)과 일치 ≥6/7.
== G3 PDF 마무리 (1.5h) ==
- 용지 자동: 도곽 비율로 ISO A4~A0 최근접 + 가로/세로, 여백 10 mm, 선택 근거 기록.
- 가독 관문: 용지상 TEXT·치수 문자 ≥1.5 mm. CLI new 도면은 extents 기반 텍스트·DIMTXT 자동(용지상 2.5 mm). rack_panel.pdf에서 600·2000이 육안으로 보이게(캡처).
- 색: LWPOLYLINE·POLYLINE·HATCH 경계의 ByLayer/엔티티 색 PDF 반영 단위 테스트(rack_panel PANEL 레이어 빨강). MTEXT run 단위 높이·폭 비율.
- 관문: 사용자 도면 추출률 유지(≥99 %, u06 ≥96 %), u04 PDF 2쪽(페이퍼 도곽 제외), 용지 자동 선택이 u01·u05·u07 = A3, rack_panel 치수 문자 ≥2.5 mm.
== G4 공유 패키지 (1h) ==
- dist\release\NexysDraft-v0.3.0-win64.zip: NexysDraft.exe + tools\libredwg\(바이너리·COPYING·SOURCE.txt·빌드 스크립트) + skills\nexysdraft\SKILL.md + README.md(한글 1쪽: 설치 없음, SmartScreen "추가 정보→실행", 지원 포맷, "nanoCAD 5가 못 여는 2018 DWG를 열어 R2000으로 저장" 사용 사례, nanoCAD 글꼴 호환(한글 ? 해결), DWG 저장 정책, PDF, CLI 한 줄 예시, 알려진 제한) + LICENSES.md.
- zip을 새 폴더에 풀어 --selftest·--skilltest·u01 열기→PDF 스모크. GitHub Release v0.3.0(private)에 첨부. 설치판은 백로그.
== G5 측정 보완 (0.5h) ==
- 스트레스 메모리 관문(첫 열기 이후 20→40회 ≤100 MB) 재측정. 24/25는 유형별 수 표로.
== G6 (일찍 끝나면) cad-skills 시작 (1h) ==
- skills\cad-headless\SKILL.md: kicad-cli·FreeCADCmd·blender 자동 탐지 + 설치된 것만 레시피 1개씩(DXF 내보내기 → NexysDraft CLI → PDF).
== 보고 == docs\report-draft3.md(G0 판정 표 1순위·G1 페이퍼 스페이스·글꼴 치환 결과·블록 판정·관문 수치·§결정 필요·§사용자 확인: nanoCAD로 u04 저장본(DWG·DXF)의 한글 표시와 Layout1 도곽), docs\screens\, next.md. 태그 v0.3.0.
== [백로그] == 레이아웃 탭 UI, 동적 블록→정적 변환, 자체 DWG R2000 라이터(플랜 C), 설치판, OCR 옵션, 바이너리 전송, 측정 도구, 트림·필렛
