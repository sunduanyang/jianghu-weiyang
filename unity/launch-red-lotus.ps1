$ErrorActionPreference = 'Stop'
$trialRoot = Split-Path -Parent $PSScriptRoot
$trialExe = Join-Path $PSScriptRoot 'Build\RedLotusTrial\江湖未央.exe'
if (!(Test-Path -LiteralPath $trialExe)) { throw 'The red lotus trial has not been built.' }
$trialSave = Join-Path $trialRoot 'output\red-lotus\showcase'
$trialLog = Join-Path $trialRoot 'output\red-lotus\showcase-player.log'
if (!(Test-Path -LiteralPath (Join-Path $trialSave 'world.sqlite'))) {
    & python (Join-Path $trialRoot 'scripts\prepare_red_lotus_trial.py') $trialSave
    if ($LASTEXITCODE -ne 0) { throw 'Could not prepare the private trial save.' }
}
Start-Process -FilePath $trialExe -ArgumentList @('-force-d3d11','-screen-fullscreen','0','-screen-width','1680','-screen-height','1000','-saveDir',('"'+$trialSave+'"'),'-redLotusTrial','1','-logFile',('"'+$trialLog+'"')) -WindowStyle Normal
