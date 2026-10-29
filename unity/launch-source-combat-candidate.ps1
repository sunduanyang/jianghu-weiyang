# Ordinary game client, separate review save. Known long-bout failures are
# documented in BLOODY_SOURCE_PORT_VALIDATION.md; no default build is changed.
$ErrorActionPreference = 'Stop'
$sourceCandidateBuild = Join-Path $PSScriptRoot 'Build\SourceFormalReplacementVerified'
$sourceCandidatePlayers = @(Get-ChildItem -LiteralPath $sourceCandidateBuild -Filter '*.exe' -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne 'UnityCrashHandler64.exe' })
if ($sourceCandidatePlayers.Count -ne 1) {
    throw 'Source combat candidate is not built. See BLOODY_SOURCE_PORT_VALIDATION.md.'
}
$sourceCandidatePlayer = $sourceCandidatePlayers[0].FullName
$sourceCandidatePython = Join-Path $sourceCandidateBuild ($sourceCandidatePlayers[0].BaseName + '_Data\StreamingAssets\Python\python.exe')
if (!(Test-Path -LiteralPath $sourceCandidatePython -PathType Leaf)) {
    throw 'The candidate bundled Python runtime is missing.'
}
$sourceCandidateReview = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\output\source-combat-candidate-play'))
$sourceCandidateSave = Join-Path $sourceCandidateReview 'save'
New-Item -ItemType Directory -Force -Path $sourceCandidateSave | Out-Null
Start-Process -FilePath $sourceCandidatePlayer -WindowStyle Normal -ArgumentList @(
    '-force-d3d11', '-saveDir', ('"' + $sourceCandidateSave + '"'),
    '-python', ('"' + $sourceCandidatePython + '"'),
    '-logFile', ('"' + (Join-Path $sourceCandidateReview 'game.log') + '"')
)
