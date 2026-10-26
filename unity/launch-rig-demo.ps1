param([string]$Skin = 'reference-plain')
$ErrorActionPreference = 'Stop'
$players = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Build\RigReference') -Filter '*.exe' -File | Where-Object { $_.Name -ne 'UnityCrashHandler64.exe' })
if ($players.Count -ne 1) { throw 'Build the rig demo before launching.' }
$player = $players[0].FullName
$demoSave = Join-Path $PSScriptRoot 'validation\rig-demo-save-20261026'
Start-Process -FilePath $player -ArgumentList @('-force-d3d11','-paperDemo','true','-paperSkin',$Skin,'-screen-width','1680','-screen-height','960','-saveDir',('"' + $demoSave + '"')) -WindowStyle Normal
