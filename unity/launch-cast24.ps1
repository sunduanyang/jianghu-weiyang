$ErrorActionPreference = 'Stop'
$castBuild = 'Cast24Bound20261029'
$castMarker = Join-Path $PSScriptRoot 'Build\active-build.txt'
if (Test-Path -LiteralPath $castMarker) {
    $castSelected = (Get-Content -LiteralPath $castMarker -Raw).Trim()
    if ($castSelected -match '^[A-Za-z0-9_-]+$') { $castBuild = $castSelected }
}
$castExe = Join-Path $PSScriptRoot ('Build\'+$castBuild+'\江湖未央.exe')
if (!(Test-Path -LiteralPath $castExe)) { throw 'The 24-character release has not been built.' }
$castSave = Join-Path $PSScriptRoot '..\output\cast24-binding-20261029\showcase-save'
$castLog = Join-Path $PSScriptRoot ('..\output\cast24-binding-20261029\showcase-'+$castBuild+'.log')
New-Item -ItemType Directory -Path $castSave -Force | Out-Null
Start-Process -FilePath $castExe -ArgumentList @('-force-d3d11','-screen-fullscreen','0','-screen-width','1680','-screen-height','1000','-saveDir',('"'+$castSave+'"'),'-cast24Showcase','1','-logFile',('"'+$castLog+'"')) -WindowStyle Normal
