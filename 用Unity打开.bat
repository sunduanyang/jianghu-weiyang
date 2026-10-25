@echo off
set "EDITOR=C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe"
if not exist "%EDITOR%" (
  echo Unity 6000.6.7f1 was not found. Add unity\JianghuUnity in Unity Hub.
  pause
  exit /b 1
)
start "" "%EDITOR%" -force-d3d11 -projectPath "%~dp0unity\JianghuUnity" -executeMethod Jianghu.Editor.BuildTools.CreateScene -logFile "%~dp0unity\editor-open.log"
