param([switch]$Interaction)
$ErrorActionPreference = 'Stop'
$characterExe = Join-Path $PSScriptRoot 'Build\CharactersSharp\江湖未央.exe'
$characterActive = Join-Path $PSScriptRoot 'Build\active-build.txt'
if (Test-Path -LiteralPath $characterActive) {
    $characterBuildName = (Get-Content -LiteralPath $characterActive -Raw).Trim()
    if ($characterBuildName -match '^[A-Za-z0-9_-]+$') {
        $characterCurrent = Join-Path $PSScriptRoot ('Build\' + $characterBuildName + '\江湖未央.exe')
        if (Test-Path -LiteralPath $characterCurrent) { $characterExe = $characterCurrent }
    }
}
if (!(Test-Path -LiteralPath $characterExe)) { throw 'The character release has not been built.' }
$characterSave = Join-Path $PSScriptRoot '..\output\character-modules\showcase'
$characterBuildLabel = Split-Path (Split-Path $characterExe -Parent) -Leaf
$characterLog = Join-Path $PSScriptRoot ('..\output\character-modules\showcase-' + $characterBuildLabel + '-player.log')
New-Item -ItemType Directory -Path $characterSave -Force | Out-Null
$characterMode=if($Interaction){'interaction'}else{'1'}
$characterWidth=if($Interaction){'2560'}else{'1680'}
$characterHeight=if($Interaction){'1440'}else{'1000'}
Start-Process -FilePath $characterExe -ArgumentList @('-force-d3d11','-screen-fullscreen','0','-screen-width',$characterWidth,'-screen-height',$characterHeight,'-saveDir',('"'+$characterSave+'"'),'-characterShowcase',$characterMode,'-logFile',('"'+$characterLog+'"')) -WindowStyle Normal
