param(
    [string]$Executable = (Join-Path $PSScriptRoot '..\unity\Build\PhysicsCoreCurrent20261029\江湖未央.exe'),
    [string]$Output = (Join-Path $PSScriptRoot ('..\output\physics-comparison-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))),
    [int]$Frames = 1200,
    [string]$Weapon = '',
    [string[]]$Backends = @('legacy','core'),
    [string]$Python = 'D:\python\python.exe',
    [switch]$ShowGame
)
$ErrorActionPreference = 'Stop'
$comparisonRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\output'))
$comparisonOutput = [IO.Path]::GetFullPath($Output)
if (!$comparisonOutput.StartsWith($comparisonRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Comparison output and saves must stay under repository output.' }
if ($Frames -lt 120 -or $Frames -gt 10800) { throw 'Frames must be between 120 and 10800.' }
$comparisonExecutable = (Resolve-Path -LiteralPath $Executable).Path
New-Item -ItemType Directory -Force $comparisonOutput | Out-Null
foreach ($comparisonBackend in $Backends) {
    if ($comparisonBackend -notin @('legacy','core')) { throw 'Unknown physics backend.' }
    $comparisonDestination = Join-Path $comparisonOutput $comparisonBackend
    if (Test-Path -LiteralPath $comparisonDestination) { throw 'Choose a fresh output directory; prior evidence will not be overwritten.' }
    New-Item -ItemType Directory $comparisonDestination | Out-Null
    $comparisonSave = Join-Path $comparisonOutput ('save-' + $comparisonBackend)
    & $Python (Join-Path $PSScriptRoot 'prepare_reference_acceptance.py') --save-dir $comparisonSave
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create isolated fixture.' }
    $comparisonArguments = @('-force-d3d11','-screen-fullscreen','0','-screen-width','1200','-screen-height','720',
        '-physicalCombatSmoke',('"'+$comparisonDestination+'"'),'-physicalCombatPhase','backend-comparison',
        '-combatPhysics',$comparisonBackend,'-comparisonFrames',"$Frames",'-saveDir',('"'+$comparisonSave+'"'),
        '-python',('"'+$Python+'"'),'-logFile',('"'+(Join-Path $comparisonOutput ($comparisonBackend+'.log'))+'"'))
    if ($Weapon) { $comparisonArguments += @('-comparisonWeapon',$Weapon) }
    $comparisonStyle = if ($ShowGame) { 'Normal' } else { 'Hidden' }
    $comparisonProcess = Start-Process -FilePath $comparisonExecutable -ArgumentList $comparisonArguments -WindowStyle $comparisonStyle -PassThru
    Write-Output ("Comparing {0}, PID {1}" -f $comparisonBackend,$comparisonProcess.Id)
    if (!$comparisonProcess.WaitForExit(900000)) { Stop-Process -Id $comparisonProcess.Id; throw 'Comparison timed out.' }
    if (!(Test-Path -LiteralPath (Join-Path $comparisonDestination 'comparison.json'))) { throw 'Native comparison did not produce measurements.' }
    $comparisonResult = Get-Content -LiteralPath (Join-Path $comparisonDestination 'acceptance.json') -Raw | ConvertFrom-Json
    Write-Output ("{0}: acceptance={1}, checks={2}" -f $comparisonBackend,$comparisonResult.passed,@($comparisonResult.checks).Count)
    if (!$comparisonResult.passed) { Write-Warning "Native acceptance failed for $comparisonBackend; retaining the failure and continuing the comparison." }
}
if ((Test-Path -LiteralPath (Join-Path $comparisonOutput 'legacy\input-sequence.json')) -and (Test-Path -LiteralPath (Join-Path $comparisonOutput 'core\input-sequence.json'))) {
    $comparisonLegacyHash = (Get-FileHash -LiteralPath (Join-Path $comparisonOutput 'legacy\input-sequence.json')).Hash
    $comparisonCoreHash = (Get-FileHash -LiteralPath (Join-Path $comparisonOutput 'core\input-sequence.json')).Hash
    if ($comparisonLegacyHash -ne $comparisonCoreHash) { throw 'The two input schedules differ.' }
    $comparisonBackendRoot=Join-Path ([IO.Path]::GetDirectoryName($comparisonExecutable)) '江湖未央_Data\StreamingAssets\Backend'
    @(Get-ChildItem -LiteralPath $comparisonBackendRoot -Recurse -Filter '*.py' -File | Sort-Object FullName | ForEach-Object { [ordered]@{ file=$_.FullName.Substring($comparisonBackendRoot.Length+1); sha256=(Get-FileHash -LiteralPath $_.FullName).Hash } }) | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $comparisonOutput 'backend-python.json') -Encoding utf8
    [ordered]@{ input_sha256=$comparisonCoreHash; frames=$Frames; executable=$comparisonExecutable; executable_sha256=(Get-FileHash -LiteralPath $comparisonExecutable).Hash; assembly_sha256=(Get-FileHash -LiteralPath (Join-Path ([IO.Path]::GetDirectoryName($comparisonExecutable)) '江湖未央_Data\Managed\Assembly-CSharp.dll')).Hash; backend_python_manifest=(Join-Path $comparisonOutput 'backend-python.json') } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $comparisonOutput 'provenance.json') -Encoding utf8
}
