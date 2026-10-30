# NexysDraft — 개인용 경량 2D CAD (DWG 열기·편집·저장) MVP

## 목적
run-draft1.md의 MVP를 끝까지 자율 구현한다. 개발 원본은 `C:\rea-work\draft\`. Nexys VIEW(C:\rea-work\diadem\app)의
셸(WPF 창 1개 + WebView2 페이지)·브리지·설정·셀프테스트·publish 체계만 가져오고 측정 뷰어 로직은 섞지 않는다.
저장소는 아직 없다(이 런에서는 만들지 않음). 사용자 도면(samples/user)은 저장소에 넣지 않는다.

## 구성
- `app/NexysDraft.sln`: Core(DXF 리더/라이터·코드페이지·MTEXT·LibreDWG CLI 래퍼·편집 명령·공간 인덱스) / App(WPF + WebView2, Bridge·Engine·LayoutStore·SelfTest) / Tests(xUnit 관문).
- 프론트 `app/web/` (Vite + TypeScript, 프레임워크 없음): WebGL 선 렌더 + Canvas2D 텍스트/오버레이, 레일(파일·레이어·속성·명령줄).
  `npm run build` → `app/web/dist` → App 빌드가 `wwwroot/`로 복사. **UI를 바꾸면 npm run build 후 dotnet build.** 정답 화면 design/mockup/draft.html.
- 빌드: `dotnet build app/NexysDraft.App/NexysDraft.App.csproj -c Release` (SDK `%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe` 8.0.425, `DOTNET_ROOT` 지정).
- 테스트/관문: `dotnet test app/NexysDraft.Tests` → work/gates/*.txt. 셀프테스트 `NexysDraft.exe --selftest --out <dir>`, 캡처 `--shot --open <file>`.
- 배포본 `app/dist/NexysDraft.exe` (dotnet publish self-contained single file, dist 비우고 publish).
- Python 실행은 `python -X utf8 -I`.

## 외부 프로그램 (링크·코드 복사 금지)
- LibreDWG 0.14 (GPLv3): `app/tools/libredwg/` dwg2dxf.exe·dxf2dwg.exe — 별도 프로세스로만 호출. 버전·지원 범위 docs/libredwg.md.
- dwg2spdf (C:\Projects\dwg2spdf-ralph-starter\dwg2spdf, `uv run --project … dwg2spdf convert`): PDF 출력. ODA File Converter는 쓰지 않는다.

## 규칙
1. 질문 금지. 막히면 docs/blockers.md에 기록하고 대체 경로.
2. 모든 결론에 근거(파일·수치·캡처). 확정 / 추정(UNVERIFIED) 구분. 확정 사실은 docs/findings.md, 진행은 docs/progress.md, 회고는 docs/review.md.
3. 숫자 관문: DXF 왕복 1e-6, DWG 왕복(LibreDWG) 좌표 1e-6·HATCH/DIMENSION 개수, 10만 엔티티 팬·줌 ≤16 ms·열기 ≤1 s, 한글 깨짐 0건.
4. UI 금지 목록(DESIGN v3): 그림자·그라데이션·글래스 금지, accent 색 테두리/좌측 띠 금지(강조는 panel-alt 배경+굵기), 토큰은 styles.css CSS 변수만, 라이트 기본·다크 지원.
5. 독자 구현: DXF 파서/라이터·렌더·편집은 직접 작성. LibreDWG·dwg2spdf는 프로세스 호출만.
6. 제외(MVP): 트림·연장·필렛·동적 블록·해치 편집·3D·XREF 편집·플롯 스타일.
