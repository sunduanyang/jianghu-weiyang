param(
    [string]$TaskRoot='D:/codex-bloody-workspace-20261028',
    [string]$Catalog='ghidra-full-catalog.json',
    [string]$Destination='native-full-c'
)
$ErrorActionPreference='Stop'
$taskGhidra='D:/codex-tools/bloody-port-20261028/ghidra_12.2_PUBLIC'
$taskOutput="$TaskRoot/output/bloody-full-port-20261028"
$taskJava=(Get-Content -LiteralPath "$taskOutput/tools/java-home.txt" -Raw).Trim()+'/bin/java.exe'
$taskJavaArgs=@('-Xmx4G')
foreach($taskLine in Get-Content -LiteralPath "$taskGhidra/support/launch.properties") {
    if($taskLine -match '^VMARGS=(.+)$' -and $Matches[1] -notmatch '=$') {$taskJavaArgs+=$Matches[1]}
}
$taskJavaArgs+=@('-Dapplication.settingsdir=D:/codex-tools/bloody-port-20261028/settings',
    '-Djava.awt.headless=true','-cp',"$taskGhidra/Ghidra/Framework/Utility/lib/Utility.jar",
    'ghidra.Ghidra','ghidra.app.util.headless.AnalyzeHeadless',
    $taskOutput,'GhidraCombat','-process','libil2cpp.so','-noanalysis',
    '-scriptPath',"$TaskRoot/scripts/ghidra",'-postScript','ExportCombatPlain.java',
    "$taskOutput/$Catalog","$taskOutput/$Destination",'-log',"$taskOutput/$Destination.log")
& $taskJava @taskJavaArgs
exit $LASTEXITCODE
