param([string]$Skin = 'm-youth-farmer')
$ErrorActionPreference = 'Stop'
$demoSave = Join-Path $PSScriptRoot 'validation\rig-demo-save-20261027'
& (Join-Path $PSScriptRoot 'launch-game.ps1') '-paperDemo' 'true' '-paperSkin' $Skin '-screen-width' '1680' '-screen-height' '960' '-saveDir' $demoSave
