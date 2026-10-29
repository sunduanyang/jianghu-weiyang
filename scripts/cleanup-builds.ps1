param([switch]$Apply)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$buildRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'unity\Build'))
$marker = Join-Path $buildRoot 'active-build.txt'
$policy = Get-Content -LiteralPath (Join-Path $projectRoot 'unity\build-retention.json') -Raw | ConvertFrom-Json
if (!(Test-Path -LiteralPath $marker -PathType Leaf)) { throw 'No active build marker. Select a verified build before cleanup.' }
$active = (Get-Content -LiteralPath $marker -Raw).Trim()
$keep = @($active, $policy.rollback_build) + @($policy.pinned_builds.PSObject.Properties.Name)
foreach ($name in $keep) {
    if (!$name -or $name -notmatch '^[A-Za-z0-9_-]+$') { throw "Invalid protected build name: $name" }
}
if (!(Test-Path -LiteralPath (Join-Path $buildRoot $active) -PathType Container)) { throw 'Active build is missing.' }
if ((Get-Item -LiteralPath $buildRoot -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Build root must not be a directory link.' }

function Get-BuildProcesses {
    # Fail closed if process inspection is unavailable. Recovery builds and Android tools are outside this script's scope.
    $processes = @(Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object { $_.Name -match '^(Unity|江湖未央)\.exe$' })
    foreach ($process in $processes) {
        if ($process.Name -eq 'Unity.exe') { throw 'Close Unity or wait for the current build to finish before cleanup.' }
        if (!$process.ExecutablePath) { throw "Cannot inspect running game process $($process.ProcessId)." }
    }
    return $processes
}
function Get-BuildSummary([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if ([IO.Path]::GetDirectoryName($full) -ne $buildRoot) { throw "Build path is outside the expected directory: $full" }
    $pending = [Collections.Generic.Stack[IO.DirectoryInfo]]::new()
    $pending.Push([IO.DirectoryInfo]::new($full))
    $bytes = [long]0; $count = 0; $latest = [long]0
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        if ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Directory link in build: $($directory.FullName)" }
        foreach ($entry in $directory.EnumerateFileSystemInfos()) {
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Link in build: $($entry.FullName)" }
            if ($entry -is [IO.DirectoryInfo]) { $pending.Push($entry); continue }
            if ($entry.Name -match '\.(sqlite|db)(-|$)|\.gguf$') { throw "Save or model found in build: $($entry.FullName)" }
            $bytes += $entry.Length; $count++
            $latest = [Math]::Max($latest, $entry.LastWriteTimeUtc.Ticks)
        }
    }
    return [pscustomobject]@{ Path=$full; Name=[IO.Path]::GetFileName($full); Bytes=$bytes; Files=$count; LatestWriteTicks=$latest }
}
$processes = @(Get-BuildProcesses)
foreach ($process in $processes) {
    if ($process.ExecutablePath.StartsWith($buildRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        $keep += $process.ExecutablePath.Substring($buildRoot.Length+1).Split('\')[0]
    }
}
$candidates = @(
    foreach ($directory in Get-ChildItem -LiteralPath $buildRoot -Directory -Force) {
        if ($directory.Name -in $keep) { continue }
        Get-BuildSummary $directory.FullName
    }
)
$candidates | Select-Object Name,Files,@{Name='GiB';Expression={[Math]::Round($_.Bytes/1GB,3)}} | Format-Table -AutoSize
$total = ($candidates | Measure-Object -Property Bytes -Sum).Sum
Write-Host ('Protected: {0}; candidates: {1}; reclaimable: {2:N3} GiB' -f ($keep -join ', '),$candidates.Count,($total/1GB))
if (!$Apply) { Write-Host 'Preview only. Run scripts/cleanup-builds.ps1 -Apply to delete these builds.'; return }
if ($candidates.Count -eq 0) { return }

$reportDirectory = Join-Path $projectRoot ('output\cleanup-builds-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $reportDirectory | Out-Null
$freeBefore = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($projectRoot)).AvailableFreeSpace
@{ ActiveBuild=$active; ProtectedBuilds=$keep; Candidates=$candidates; FreeBytesBefore=$freeBefore } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $reportDirectory 'manifest.json') -Encoding UTF8
$results = @()
foreach ($candidate in $candidates) {
    # Recheck the resolved target, marker, process use, and file inventory immediately before each recursive deletion.
    if ((Get-Content -LiteralPath $marker -Raw).Trim() -ne $active) { throw 'Active build changed during cleanup; stopping.' }
    $running = @(Get-BuildProcesses)
    foreach ($process in $running) {
        if ($process.ExecutablePath.StartsWith($candidate.Path + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Build is now running: $($candidate.Name)"
        }
    }
    $fresh = Get-BuildSummary $candidate.Path
    if ($fresh.Files -ne $candidate.Files -or $fresh.Bytes -ne $candidate.Bytes -or $fresh.LatestWriteTicks -ne $candidate.LatestWriteTicks) {
        throw "Build changed during cleanup: $($candidate.Name)"
    }
    Remove-Item -LiteralPath $fresh.Path -Recurse -Force -ErrorAction Stop
    $results += [pscustomobject]@{ Name=$fresh.Name; Bytes=$fresh.Bytes; Deleted=!(Test-Path -LiteralPath $fresh.Path) }
    $results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $reportDirectory 'deleted.json') -Encoding UTF8
}
$freeAfter = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($projectRoot)).AvailableFreeSpace
@{ ActiveBuild=$active; DeletedCount=$results.Count; DeletedBytes=($results | Measure-Object Bytes -Sum).Sum;
   FreeBytesBefore=$freeBefore; FreeBytesAfter=$freeAfter; FreeIncreaseBytes=($freeAfter-$freeBefore) } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $reportDirectory 'result.json') -Encoding UTF8
Write-Host "Cleanup record: $reportDirectory"
