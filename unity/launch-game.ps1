$ErrorActionPreference = 'Stop'
$buildPath = Join-Path $PSScriptRoot 'Build'
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
