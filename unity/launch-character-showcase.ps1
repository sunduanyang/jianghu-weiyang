param([switch]$Interaction)
$ErrorActionPreference = 'Stop'
$characterSave = Join-Path $PSScriptRoot '..\output\character-modules\showcase'
$characterLog = Join-Path $PSScriptRoot '..\output\character-modules\showcase-player.log'
New-Item -ItemType Directory -Path $characterSave -Force | Out-Null
$characterMode=if($Interaction){'interaction'}else{'1'}
$characterWidth=if($Interaction){'2560'}else{'1680'}
$characterHeight=if($Interaction){'1440'}else{'1000'}
& (Join-Path $PSScriptRoot 'launch-game.ps1') -screen-fullscreen 0 -screen-width $characterWidth -screen-height $characterHeight -saveDir $characterSave -characterShowcase $characterMode -logFile $characterLog
