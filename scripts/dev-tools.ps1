param(
    [ValidateSet('menu','combat','cast24','characters','interaction','rig','model','puppet-trial','modular','red-lotus','source-candidate','physics-core','physics-legacy','cleanup')]
    [string]$Action = 'menu',
    [switch]$List
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$unityRoot = Join-Path $projectRoot 'unity'
$entries = @(
    @{ Action='combat'; Label='物理格斗试玩（当前游戏）' },
    @{ Action='cast24'; Label='二十四人物图鉴（当前游戏）' },
    @{ Action='characters'; Label='骨骼与贴图查看（当前游戏）' },
    @{ Action='interaction'; Label='清晰人物与交互查看（当前游戏）' },
    @{ Action='rig'; Label='演示骨骼系统（当前游戏）' },
    @{ Action='model'; Label='启动本地模型' },
    @{ Action='puppet-trial'; Label='试玩皮影绑定（历史专用版本）' },
    @{ Action='modular'; Label='皮影换脸换装（历史专用版本）' },
    @{ Action='red-lotus'; Label='试穿红莲皮影（历史专用版本）' },
    @{ Action='source-candidate'; Label='源图战斗候选（独立存档）' },
    @{ Action='physics-core'; Label='Physics Core 2D 战斗候选（独立存档）' },
    @{ Action='physics-legacy'; Label='同一候选的旧物理对照（独立存档）' },
    @{ Action='cleanup'; Label='查看旧构建清理清单（不删除）' }
)
function Invoke-DeveloperTool([string]$Tool) {
    switch ($Tool) {
        'combat' { & (Join-Path $unityRoot 'launch-reference-combat.ps1') }
        'cast24' { & (Join-Path $unityRoot 'launch-cast24.ps1') }
        'characters' { & (Join-Path $unityRoot 'launch-character-showcase.ps1') }
        'interaction' { & (Join-Path $unityRoot 'launch-character-showcase.ps1') -Interaction }
        'rig' { & (Join-Path $unityRoot 'launch-rig-demo.ps1') }
        'model' {
            Push-Location $projectRoot
            try { python (Join-Path $PSScriptRoot 'start_model.py') }
            finally { Pop-Location }
        }
        'puppet-trial' { & (Join-Path $unityRoot 'launch-puppet-trial.ps1') }
        'modular' { & (Join-Path $unityRoot 'launch-modular-puppet.ps1') }
        'red-lotus' { & (Join-Path $unityRoot 'launch-red-lotus.ps1') }
        'source-candidate' { & (Join-Path $unityRoot 'launch-source-combat-candidate.ps1') }
        'physics-core' { & (Join-Path $unityRoot 'launch-physics-core.ps1') -Backend core }
        'physics-legacy' { & (Join-Path $unityRoot 'launch-physics-core.ps1') -Backend legacy }
        'cleanup' { & (Join-Path $PSScriptRoot 'cleanup-builds.ps1') }
    }
}
if ($List) { $entries | ForEach-Object { [pscustomobject]$_ }; return }
if ($Action -ne 'menu') { Invoke-DeveloperTool $Action; return }
while ($true) {
    Write-Host ''
    Write-Host '江湖未央 · 开发工具'
    for ($index=0; $index -lt $entries.Count; $index++) {
        Write-Host ('{0,2}. {1}' -f ($index+1), $entries[$index].Label)
    }
    Write-Host ' 0. 退出'
    $choice = Read-Host '请选择工具'
    if ($choice -eq '0' -or $choice -eq '') { break }
    $number = 0
    if (![int]::TryParse($choice, [ref]$number) -or $number -lt 1 -or $number -gt $entries.Count) { continue }
    try { Invoke-DeveloperTool $entries[$number-1].Action }
    catch { Write-Host $_.Exception.Message -ForegroundColor Red }
    [void](Read-Host '按回车返回菜单')
}
