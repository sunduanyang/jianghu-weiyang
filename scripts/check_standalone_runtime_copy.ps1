param([ValidatePattern('^[a-z0-9-]+$')][string]$RunDirectory='runtime-final')
$ErrorActionPreference = 'Stop'
$copyWorkspace = Split-Path -Parent $PSScriptRoot
$copyExecutable = Join-Path $copyWorkspace 'standalone\PhysicsCombatCopy\Build\PhysicsCombat.exe'
$copyEvidence = Join-Path (Join-Path $copyWorkspace 'output\standalone-runtime-copy-20261030') $RunDirectory
if (!(Test-Path -LiteralPath $copyExecutable)) { throw 'The standalone executable has not been built.' }

$copyRuns = @(
    @{ Name = 'friendly'; Mode = 5; Save = 'friendly' },
    @{ Name = 'campaign'; Mode = 0; Save = 'campaign' },
    @{ Name = 'campaign-reload'; Mode = 0; Save = 'campaign' }
)
$copyResults = @()
foreach ($copyRun in $copyRuns) {
    $copyRunOutput = Join-Path $copyEvidence ('runtime-' + $copyRun.Name)
    $copySaveFolder = Join-Path $copyEvidence ('save-' + $copyRun.Save)
    New-Item -ItemType Directory -Force -Path $copyRunOutput, $copySaveFolder | Out-Null
    $copyProgressFile = Join-Path $copySaveFolder 'progress.json'
    $copyPlayerLog = Join-Path $copyRunOutput 'player.log'
    $copyArguments = @(
        '-force-d3d11', '-screen-width', '1280', '-screen-height', '720', '-screen-fullscreen', '0',
        '-runtimeCheck', ('"' + $copyRunOutput + '"'),
        '-arenaGameMode', [string]$copyRun.Mode, '-arenaEnvironmentHour', '16',
        '-arenaProgressPath', ('"' + $copyProgressFile + '"'),
        '-logFile', ('"' + $copyPlayerLog + '"')
    )
    $copyProcess = Start-Process -FilePath $copyExecutable -ArgumentList $copyArguments -WindowStyle Hidden -PassThru
    $copyDeadline = [DateTime]::UtcNow.AddSeconds(85)
    while (!$copyProcess.HasExited -and [DateTime]::UtcNow -lt $copyDeadline) {
        $null = $copyProcess.WaitForExit(1000)
        $copyProcess.Refresh()
    }
    $copyTimedOut = !$copyProcess.HasExited
    if ($copyTimedOut) {
        # This PID belongs exclusively to the check started above.
        Stop-Process -Id $copyProcess.Id -Force
        $null = $copyProcess.WaitForExit(5000)
    }
    $copyReportPath = Join-Path $copyRunOutput 'runtime-check.json'
    $copyReport = if (Test-Path -LiteralPath $copyReportPath) { Get-Content -LiteralPath $copyReportPath -Raw | ConvertFrom-Json } else { $null }
    $copyPass = !$copyTimedOut -and $null -ne $copyReport -and $copyReport.pass -and (Test-Path -LiteralPath $copyProgressFile)
    $copyResult = [ordered]@{ name = $copyRun.Name; pass = $copyPass; timeout = $copyTimedOut; report = $copyReportPath; save = $copyProgressFile; result = $copyReport }
    $copyResults += $copyResult
    Write-Output ($copyResult | ConvertTo-Json -Compress -Depth 12)
    if (!$copyPass) { break }
}
$copyOverall = [ordered]@{
    pass = $copyResults.Count -eq $copyRuns.Count -and @($copyResults | Where-Object { !$_.pass }).Count -eq 0
    checks = $copyResults
}
$copyOverall | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath (Join-Path $copyEvidence 'runtime-validation.json') -Encoding utf8
if (!$copyOverall.pass) { exit 1 }
