# RUN-DRAFT4 — NexysDraft: nanoCAD 5 호환 마무리(DXF 버전·한글 글꼴) + draft3 결정 반영 → v0.3.1. 끝까지 자율 진행.
전제: C:\rea-work\draft\ v0.3.0 위에서 계속. 공통 규칙 동일(질문 금지·blockers·progress/findings/review·숫자 관문·UI 금지 목록). ODA는 dev.json 경로로 개발 검증 전용. 총 예산 4h.
nanoCAD 5 실측(사용자 확인, d3_user_check): u01·u07 .nexys.dwg 오류 없음·한글 정상. selftest_saved.dwg 25/25. u04 .nexys.dwg는 오류 없음·두 시트 정상이나 한글이 ???로 표시(글꼴 호환 ON 저장본). u04 .nexys.dxf는 "This version of file format is not supported"(원본 AC1032 버전 그대로 저장됨; nanoCAD 5는 AC1027까지). u01에서 한글이 보이는 스타일 'PDF Arial'은 영문 패밀리 이름.
draft3 결정(확정): PDF는 도면 플롯 설정/A3 우선, 사용자 도면 1.5 mm 관문 폐기(CLI new 2.5 mm 유지). IMAGE 도면은 대화상자에 "IMAGE n개 제외하고 DWG 저장" 선택 버튼, 기본은 DXF 유지. dwgread.exe는 zip에서 제외(저장소·개발용은 유지). 10만 열기는 ≤2 s 기준으로 PASS 처리하고 종료.

== H0 글꼴 매트릭스 (1h) ==
- work\font_matrix\font_matrix.dxf(R2000)와 .dwg(R2000, LibreDWG)를 만든다. 스타일 6개, 각 스타일로 TEXT "X 한글테스트 ABC"(X=변형 문자, 높이 10, 세로로 간격)와 MTEXT 1개:
  A: 그룹 3 비움 + ACAD xdata 1000 "굴림"(현재 방식, \U+ 이스케이프)
  B: 그룹 3 비움 + xdata 1000 "Gulim"(ASCII 영문 패밀리)
  C: 그룹 3 "gulim.ttc" + xdata 1000 "Gulim"
  D: 그룹 3 "malgun.ttf" + xdata 1000 "Malgun Gothic"
  E: 그룹 3 "malgun.ttf", xdata 없음
  F: u01 'PDF Arial' 스타일을 그대로 복사
- 각 변형의 실제 기록 바이트(DXF 원문 STYLE 블록, dwg2dxf 재변환 결과)를 docs\font_matrix.md에 표로. ODA로 두 파일 읽기 오류 0 확인.
- u04 .nexys.dwg 재검사: TEXT 516개가 참조하는 스타일과 그 스타일의 기록 바이트, MTEXT 인라인 \f 유무를 findings에. 한글 TEXT가 치환 대상 스타일을 실제로 쓰는지 확인(안 쓰면 치환 누락 원인 수정).
- 기본 치환 방식 결정 규칙(사람 확인 전 잠정): C(파일명 + ASCII 패밀리)를 기본으로 구현하고, 모든 글꼴 관련 문자열(STYLE 이름 제외 그룹 3·4·xdata 패밀리)은 ASCII만 쓴다(\U+ 금지). 한글 패밀리→영문 매핑 표: 굴림/굴림체→Gulim·gulim.ttc, 돋움/돋움체→Dotum·gulim.ttc, 바탕/바탕체→Batang·batang.ttc, 맑은 고딕→Malgun Gothic·malgun.ttf, 그 외→Gulim. 파일 존재는 %WINDIR%\Fonts에서 확인.
- §사용자 확인 1순위: "nanoCAD 5로 font_matrix.dwg·.dxf를 열어 한글이 보이는 변형 문자를 알려 달라". 결과에 따라 한 줄로 바꿀 수 있게 치환 방식을 설정 상수(FontCompatMode = A~F) 하나로 둔다.
== H1 DXF 버전 (1h) ==
- 저장 대화상자·CLI에 DXF 버전 선택: "원본 유지(Preserve)" / "R2000 (nanoCAD 호환)". 원본 $ACADVER가 AC1027보다 새면 기본 R2000(모델 라이터, 글꼴 호환·페이퍼 스페이스·한글 동일 적용), 아니면 원본 유지. 선택 근거를 상태줄·result.json에. CLI: save PATH.dxf [--ver r2000|keep].
- 관문: 사용자 도면 7개 기본 DXF 저장본의 $ACADVER ≤ AC1027 = 7/7, ODA 읽기 오류 0, 엔티티·레이어·모형/페이퍼 수·한글 동일(f1 관문 재사용).
== H2 draft3 결정 반영 (1h) ==
- IMAGE: R2000Check에서 IMAGE가 유일한 막는 사유면 "IMAGE n개 제외하고 DWG 저장" 버튼. 제외한 엔티티 목록을 결과에 표시, 기본 선택은 DXF. 관문: u06 IMAGE 제외 DWG가 ODA 오류 0, 나머지 엔티티 수 일치.
- PDF 1.5 mm 관문 제거, 보고 항목(중앙값)만 유지. 10만 열기 관문 기준 ≤2 s로.
- package.ps1: dwgread.exe 제외, 실행 중 배포본을 건드리지 않는 검사 유지. 버전 0.3.1.
== H3 패키지·검증 (0.5h) ==
- 전체 테스트·--selftest·--skilltest·새 폴더 zip 스모크. work\d4_user_check\에 nanoCAD 확인용: font_matrix.dwg/.dxf, u04.nexys.dwg/.dxf(새 기본값), u06.noimage.dwg. GitHub Release v0.3.1(private), 태그.
== 보고 == docs\report-draft4.md(H0 매트릭스 표·u04 원인·H1 관문·블록 판정·§사용자 확인: font_matrix에서 보이는 문자, u04 DWG/DXF 한글·열림, u06 열림), next.md.
