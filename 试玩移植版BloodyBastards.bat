@echo off
setlocal
cd /d "%~dp0"
if not exist "%~dp0Build\BloodyBastardsRecovery-28\BloodyBastardsRecovery.exe" (
  echo Bloody Bastards playtest build is missing.
  pause
  exit /b 1
)
set "playtestSave=%~dp0output\bloody-full-port-20261028\held-control-27\native"
if not exist "%playtestSave%" mkdir "%playtestSave%"
start "" "%~dp0Build\BloodyBastardsRecovery-28\BloodyBastardsRecovery.exe" -force-d3d11 -playtestFullscreen -playtestSetup -originalGameMode 0 -originalProgressPath "%playtestSave%\progress.json" -logFile "%playtestSave%\player-latest.log"
endlocal
