# AI 에이전트로 도면 그리기 (Claude Code · Gemini CLI · Antigravity CLI)

NexysDraft는 창 없이 돌아가는 명령줄(`NexysDraft.exe --script job.txt --out dir --json`)이 있다. 에이전트는 이 명령줄 사용법이 적힌
**스킬 파일(SKILL.md)** 하나만 읽으면 "200×120 판에 구멍 4개 그려서 DWG로 저장해줘" 같은 말을 작업 스크립트로 바꿔 실행한다.

## 설치 (한 번)
셋 중 편한 방법 하나:
1. 압축을 푼 폴더의 **`install-ai-skill.bat` 더블클릭**
2. NexysDraft에서 **F1(도움말) → "AI 에이전트 스킬 설치" 버튼** (명령줄에 `SKILL`을 쳐도 됨)
3. 명령 창에서:
```
NexysDraft.exe --install-skill          # 세 에이전트 모두
NexysDraft.exe --install-skill claude   # 하나만: claude | gemini | agy
NexysDraft.exe --install-skill --uninstall
```
스킬 파일에 이 PC의 `NexysDraft.exe` 전체 경로를 써 넣어서 아래 폴더에 복사한다. 그 밖의 파일은 건드리지 않는다.
프로그램 폴더를 옮기면 다시 실행한다.

| 에이전트 | 설치되는 곳 | 확인 방법 |
|---|---|---|
| Claude Code | `%USERPROFILE%\.claude\skills\nexysdraft\SKILL.md` | 새 세션에서 `/skills` 또는 그냥 말로 요청 |
| Gemini CLI | `%USERPROFILE%\.gemini\skills\nexysdraft\SKILL.md` | `gemini skills list` 또는 `/skills list` |
| Antigravity CLI (`agy`) | `%USERPROFILE%\.gemini\config\skills\nexysdraft\SKILL.md` | 새 세션에서 말로 요청 |

`cad-headless` 스킬(KiCad·FreeCAD에서 DXF를 뽑아 NexysDraft로 넘기는 방법)도 같이 설치된다.

## 왜 MCP 서버나 플러그인이 아닌가
- 세 에이전트 모두 같은 **Agent Skills 형식**(폴더 + `SKILL.md`, 머리말에 `name`·`description`)을 읽는다. 파일 하나로 셋이 다 된다.
- NexysDraft의 명령줄은 "스크립트 파일 하나 → result.json 하나"라서 에이전트의 셸 도구만으로 충분하다. MCP 서버는 상주 프로세스와
  설정 파일이 에이전트마다 따로 필요하고, 얻는 것이 없다.
- 저장소를 통째로 쓰는 경우에는 설치 없이 작업 폴더에 `.claude\skills\` 또는 `.agents\skills\`(Gemini CLI·Antigravity 공용)로 복사해도 된다.

## 쓰는 법
새 세션을 열고 말로 시킨다. 치수가 모자라면 에이전트가 되묻는다.
```
가로 200, 세로 120 판을 그려줘. 네 모서리에서 20 안쪽에 지름 8 구멍 4개, 가운데에 60×20 장공.
가로·세로 치수 넣고 plate.dwg 와 plate.pdf 로 저장해줘.
```
창 없이 실행할 때:
```
claude -p "…요청…"
agy -p "…요청…" --dangerously-skip-permissions     # 명령 실행을 묻지 않고 허용 — 빈 작업 폴더에서만
gemini -p "…요청…" -y
```

## 할 수 있는 것 (스킬에 적힌 명령)
새 도면 · DWG/DXF 열기 · 선/폴리선/원/호/타원/다각형/사각형/점/텍스트/치수 · 레이어 · 선택(전체/창/교차/레이어/종류) ·
이동/복사/회전/대칭/신축/간격띄우기/**자르기(tr)/연장(ex)**/삭제 · 문자 바꾸기 · DXF/DWG 저장 · PDF(시트 자동 분할) · 통계 조회.
전체 표는 `skills/nexysdraft/SKILL.md`.

## 한계
- Windows 전용(프로그램이 Windows용).
- 에이전트는 결과를 숫자(result.json의 범위·객체 수)로 확인한다. 그림으로 보고 싶으면 PDF를 열어 본다.
- 필렛·모따기·배열·해치 그리기는 명령줄에 없다.
