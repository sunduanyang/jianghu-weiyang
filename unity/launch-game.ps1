$ErrorActionPreference = 'Stop'
$buildPath = Join-Path $PSScriptRoot 'Build'
$activeBuildFile = Join-Path $buildPath 'active-build.txt'
$activeBuildSelected = $false
if (Test-Path -LiteralPath $activeBuildFile) {
    $activeBuildName = (Get-Content -LiteralPath $activeBuildFile -Raw).Trim()
    if ($activeBuildName -match '^[A-Za-z0-9_-]+$') {
        $activeBuildPath = Join-Path $buildPath $activeBuildName
        if (Test-Path -LiteralPath $activeBuildPath -PathType Container) { $buildPath = $activeBuildPath; $activeBuildSelected = $true }
    }
}
# Current releases can be installed while a previous root-level player is running.
$currentBuildPath = Join-Path $buildPath 'Current'
if (!$activeBuildSelected -and @(Get-ChildItem -LiteralPath $currentBuildPath -Filter '*.exe' -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne 'UnityCrashHandler64.exe' }).Count -eq 1) { $buildPath = $currentBuildPath }
$players = @(Get-ChildItem -LiteralPath $buildPath -Filter '*.exe' -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne 'UnityCrashHandler64.exe' })
if ($players.Count -ne 1) {
    Write-Host 'Build the Unity project first. See unity/README.md.'
    Read-Host 'Press Enter to close'
    exit 1
}
if ($args.Count -gt 0) {
    $playerArguments = @('-force-d3d11') + @($args | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' })
    Start-Process -FilePath $players[0].FullName -ArgumentList $playerArguments -WindowStyle Normal
} else {
    Start-Process -FilePath $players[0].FullName -ArgumentList @('-force-d3d11') -WindowStyle Normal
}
