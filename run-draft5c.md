# RUN-DRAFT5C — NexysDraft v0.4.2: 앱 헤더 로고를 Nexys 제품군과 통일. 끝까지 자율 진행.
전제: C:\rea-work\draft\ v0.4.1 위에서 계속. 공통 규칙 동일. 실행 중 배포본·app\dist는 건드리지 않는다. 예산 30분.
사용자 피드백: 작업표시줄·창 제목줄 아이콘은 정상인데, 앱 화면 왼쪽 위 헤더 로고가 Nexys VIEW·NexysBus와 다르다(draft1 자리표시자 "N" 원이 남아 있음).

== 작업 ==
- 기준 찾기: Nexys VIEW(C:\rea-work\diadem\app\web) 헤더의 로고 마크업·SVG/PNG 자산·CSS(크기·여백·라이트/다크 변형)와 브랜드 원본(C:\rea-work\diadem\design\brand)을 확인. NexysBus 소스 위치도 찾아 같은 방식인지 대조하고, 둘이 다르면 Nexys VIEW를 기준으로 한다(차이는 findings에 기록).
- 적용: 같은 자산을 app\web에 복사(경로·파일명은 VIEW와 동일 규칙), 헤더 마크업·크기·제품명 표기 방식(예: 로고 + "NexysDraft" + 버전 줄)을 VIEW와 일치. 라이트/다크 앱 테마 각각 올바른 변형. 창 아이콘·작업표시줄 아이콘(.ico)이 같은 원본에서 나온 것인지도 확인하고 아니면 맞춘다.
- 관문: 헤더 영역 캡처를 Nexys VIEW 헤더 캡처와 나란히(docs\screens\50_header_light.png, 51_header_dark.png), 로고 영역 픽셀 대조(tools\pixel_diff.py) 상관 ≥0.9. 전체 테스트·--selftest·zip 스모크. v0.4.2 태그·Release.
== 보고 == docs\report-draft5c.md 짧게(기준으로 쓴 파일 경로, 대조 수치, 캡처).
