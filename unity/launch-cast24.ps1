$ErrorActionPreference = 'Stop'
$castSave = Join-Path $PSScriptRoot '..\output\cast24-binding-20261029\showcase-save'
$castLog = Join-Path $PSScriptRoot '..\output\cast24-binding-20261029\showcase-player.log'
New-Item -ItemType Directory -Path $castSave -Force | Out-Null
& (Join-Path $PSScriptRoot 'launch-game.ps1') -screen-fullscreen 0 -screen-width 1680 -screen-height 1000 -saveDir $castSave -cast24Showcase 1 -logFile $castLog
