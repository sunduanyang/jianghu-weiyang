$ErrorActionPreference = 'Stop'
$modularFolder = Join-Path $PSScriptRoot 'Build\ModularPuppetNeck'
$modularExe = Get-ChildItem -LiteralPath $modularFolder -Filter '*.exe' -File | Where-Object { $_.Name -ne 'UnityCrashHandler64.exe' } | Select-Object -First 1
if (!$modularExe) { throw 'The modular puppet demo has not been built.' }
$modularLogs = Join-Path $PSScriptRoot '..\output\modular-puppet-demo\play'
New-Item -ItemType Directory -Path $modularLogs -Force | Out-Null
Start-Process -FilePath $modularExe.FullName -ArgumentList @('-force-d3d11','-screen-fullscreen','0','-screen-width','1680','-screen-height','1000','-referenceCombat','1','-modularPuppet','1','-logFile',('"' + (Join-Path $modularLogs 'player.log') + '"')) -WindowStyle Normal
