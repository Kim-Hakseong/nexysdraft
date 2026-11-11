@echo off
chcp 65001 >nul
title NexysDraft - AI agent skill
echo NexysDraft 스킬을 Antigravity CLI / Claude Code / Gemini CLI 에 설치합니다...
echo.
start "" /wait "%~dp0NexysDraft.exe" --install-skill all --log "%~dp0skill-install.log"
if exist "%~dp0skill-install.log" (type "%~dp0skill-install.log") else (echo 설치하지 못했습니다. NexysDraft.exe 가 이 파일과 같은 폴더에 있는지 확인하세요.)
echo.
echo 에이전트(Antigravity CLI 등)를 새로 열고 말로 요청하세요.
echo 예: 200x120 판에 구멍 4개 그려서 DWG로 저장해줘
echo.
pause
