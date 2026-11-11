# 예제: 볼트 체결 플랜지 (A3 제작도)

NexysDraft 명령줄만으로 만든 도면입니다. 도면 틀·구역 표시·표제란·개정 이력·정면도·단면도(해치)·중심선(선 종류)·지름 치수·주기가 들어 있습니다.

- `job.txt` — 작업 스크립트(명령 218줄). 다시 만들려면 이 폴더에서:
  ```
  ..\..\NexysDraft.exe --script job.txt --out . --json
  ```
  (PowerShell에서는 끝에 `| Out-String`을 붙여야 끝날 때까지 기다립니다.)
- `flange.dwg` · `flange.dxf` · `flange.pdf` — 그 결과. DWG TrueView 2027에서 열리는 것을 확인했습니다.

에이전트(Antigravity CLI, Claude Code)에게 "이 예제처럼 표제란 있는 도면으로 ○○을 그려줘"라고 하면 이 파일을 본보기로 씁니다.
