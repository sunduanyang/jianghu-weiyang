param([string]$EditorPath = $env:UNITY_EDITOR_PATH, [switch]$RegenerateAssets, [switch]$BundlePython)
$ErrorActionPreference = 'Stop'
$migrationRoot = $PSScriptRoot
$projectPath = Join-Path $migrationRoot 'JianghuUnity'
if (!$EditorPath) { $EditorPath = 'C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe' }
if (!(Test-Path -LiteralPath $EditorPath)) { throw '找不到 Unity 6000.6.7f1，请传入 -EditorPath 或设置 UNITY_EDITOR_PATH。' }
$prepareArgs = @('-X', 'utf8', (Join-Path $migrationRoot 'prepare.py'))
if (!$RegenerateAssets -and !$BundlePython) { $prepareArgs += '--backend-only' }
if ($BundlePython) { $prepareArgs += '--bundle-python' }
python @prepareArgs
if ($LASTEXITCODE -ne 0) { throw '资源准备失败' }
if ($RegenerateAssets) {
    node (Join-Path $migrationRoot 'bake_puppets.cjs')
    if ($LASTEXITCODE -ne 0) { throw '皮影资源准备失败，请先 npm ci。' }
}
$process = Start-Process -FilePath $EditorPath -ArgumentList @('-batchmode','-nographics','-quit','-projectPath',('"' + $projectPath + '"'),'-executeMethod','Jianghu.Editor.BuildTools.BuildWindows','-logFile',('"' + (Join-Path $migrationRoot 'build.log') + '"')) -WindowStyle Hidden -PassThru
$process.WaitForExit()
if ($process.ExitCode -ne 0) { throw 'Unity 构建失败，请查看 unity/build.log。若工程已在编辑器打开，请从「江湖/构建 Windows 游戏」菜单构建。' }
Write-Output (Join-Path $migrationRoot 'Build\江湖未央.exe')
