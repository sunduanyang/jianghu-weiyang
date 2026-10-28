$ErrorActionPreference = 'Stop'
# Use the newer compiled combat code, while allowing subsequent art-trial
# rebuilds to take over again without pinning the artwork to an older build.
$trialCandidates = foreach ($trialRelative in @('Build\LacePuppetTrial','Build\BloodyContactResolved')) {
    $trialCandidateFolder = Join-Path $PSScriptRoot $trialRelative
    if (Test-Path -LiteralPath $trialCandidateFolder -PathType Container) {
        $trialAssembly = Get-ChildItem -LiteralPath $trialCandidateFolder -Filter 'Assembly-CSharp.dll' -File -Recurse | Select-Object -First 1
        if ($trialAssembly) { [pscustomobject]@{ Folder=$trialCandidateFolder; Compiled=$trialAssembly.LastWriteTimeUtc; ArtPriority=($trialRelative -eq 'Build\LacePuppetTrial') } }
    }
}
$trialChoice = $trialCandidates | Sort-Object -Property @{Expression='Compiled';Descending=$true},@{Expression='ArtPriority';Descending=$true} | Select-Object -First 1
if (!$trialChoice) { throw 'The puppet trial has not been built.' }
$trialFolder = $trialChoice.Folder
$trialExe = Get-ChildItem -LiteralPath $trialFolder -Filter '*.exe' | Where-Object { $_.Name -ne 'UnityCrashHandler64.exe' } | Select-Object -First 1
if (!$trialExe) { throw 'The puppet trial has not been built.' }
$trialLogs = Join-Path $PSScriptRoot '..\output\lace-puppet-binding\play'
New-Item -ItemType Directory -Path $trialLogs -Force | Out-Null
$trialLog = Join-Path $trialLogs 'player.log'
Start-Process -FilePath $trialExe.FullName -ArgumentList @('-force-d3d11','-screen-fullscreen','0','-screen-width','1680','-screen-height','960','-referenceCombat','1','-puppetArtTrial','1','-logFile',('"' + $trialLog + '"')) -WindowStyle Normal
