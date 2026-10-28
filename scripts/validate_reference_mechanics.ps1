param(
    [string]$Executable = (Join-Path $PSScriptRoot '..\unity\Build\BloodyMechanicsVerified\江湖未央.exe'),
    [string]$Output = (Join-Path $PSScriptRoot ('..\output\bloody-mechanics-validation-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))),
    [string]$Python = 'D:\python\python.exe',
    [string[]]$Phases = @('reference-mechanics','reference-stress','resume','reference','binds','kinetic'),
    [string[]]$ExtraArguments = @(),
    [switch]$ShowGame
)
$ErrorActionPreference = 'Stop'
$combatExecutable = (Resolve-Path -LiteralPath $Executable).Path
$combatOutput = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force -Path $combatOutput | Out-Null
$combatResults = @()
foreach ($combatPhase in $Phases) {
    $combatDestination = Join-Path $combatOutput $combatPhase
    if (Test-Path -LiteralPath (Join-Path $combatDestination 'acceptance.json')) {
        throw "Choose a fresh Output directory; $combatPhase already has a result."
    }
    New-Item -ItemType Directory -Force -Path $combatDestination | Out-Null
    $combatSaveName = if ($combatPhase -in @('resume','city-resume')) { 'reference-mechanics' } elseif ($combatPhase -eq 'injury-resume') { 'injury' } elseif ($combatPhase -eq 'source-program-resume') { 'source-program' } elseif ($combatPhase -eq 'reference-port-resume') { 'reference-port' } else { $combatPhase }
    $combatSave = Join-Path $combatOutput ('save-' + $combatSaveName)
    New-Item -ItemType Directory -Force -Path $combatSave | Out-Null
    if ($combatPhase -in @('injury','reference-mechanics','reference','source-program','reference-port')) {
        & $Python (Join-Path $PSScriptRoot 'prepare_reference_acceptance.py') --save-dir $combatSave
        if ($LASTEXITCODE -ne 0) { throw "Could not prepare isolated combat fixture: $combatPhase" }
    }
    $combatArguments = @('-force-d3d11','-screen-fullscreen','0','-screen-width','1200','-screen-height','720',
        '-physicalCombatSmoke',('"'+$combatDestination+'"'),'-physicalCombatPhase',$combatPhase,
        '-saveDir',('"'+$combatSave+'"'),'-python',('"'+$Python+'"'),
        '-logFile',('"'+(Join-Path $combatOutput ($combatPhase+'.log'))+'"'))
    $combatArguments += $ExtraArguments
    $combatWindowStyle = if ($ShowGame) { 'Normal' } else { 'Hidden' }
    $combatProcess = Start-Process -FilePath $combatExecutable -ArgumentList $combatArguments -WindowStyle $combatWindowStyle -PassThru
    Write-Output ("Running {0}, PID {1}" -f $combatPhase,$combatProcess.Id)
    $combatDeadline = [DateTime]::UtcNow.AddMinutes(10)
    while (!$combatProcess.WaitForExit(30000)) {
        if ([DateTime]::UtcNow -gt $combatDeadline) {
            Stop-Process -Id $combatProcess.Id
            throw "Native acceptance timed out: $combatPhase"
        }
    }
    $combatResult = Get-Content -LiteralPath (Join-Path $combatDestination 'acceptance.json') -Raw | ConvertFrom-Json
    $combatResults += [ordered]@{ phase=$combatPhase; passed=$combatResult.passed; checks=@($combatResult.checks).Count }
    $combatResults | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $combatOutput 'summary.json') -Encoding utf8
    Write-Output ("{0}: {1}, {2} checks" -f $combatPhase,$combatResult.passed,@($combatResult.checks).Count)
    if (!$combatResult.passed) {
        $combatResult.checks | Where-Object { !$_.passed } | ForEach-Object {
            [ordered]@{ check=$_.check; facts=($_.facts | Select-Object * -ExcludeProperty path,samples,windup,impact) }
        } | ConvertTo-Json -Depth 6 | Write-Output
        throw "Native acceptance failed: $combatPhase"
    }
}
