@echo off
chcp 65001 >nul
set "DUEL_EXE=%~dp0demos\PhysicsDuelUnity\Build-v09\纸上争锋.exe"
if not exist "%DUEL_EXE%" (
  echo Demo build missing. Open demos\PhysicsDuelUnity in Unity and build it first.
  pause
  exit /b 1
)
start "" "%DUEL_EXE%" %*
