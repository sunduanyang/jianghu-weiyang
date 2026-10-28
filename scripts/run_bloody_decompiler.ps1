param(
    [string]$Ghidra='D:/codex-tools/bloody-port-20261028/ghidra_12.2_PUBLIC',
    [string]$TaskRoot='D:/codex-bloody-workspace-20261028',
    [string]$Catalog='ghidra-catalog.json',
    [string]$Destination='decompiled'
)
$ErrorActionPreference='Stop'
$taskOutput="$TaskRoot/output/bloody-full-port-20261028"
$taskJava=(Get-Content -LiteralPath "$taskOutput/tools/java-home.txt" -Raw).Trim()+'/bin/java.exe'
$taskJavaArgs=@('-Xmx4G')
foreach ($taskLine in Get-Content -LiteralPath "$Ghidra/support/launch.properties") {
    if ($taskLine -match '^VMARGS=(.+)$' -and $Matches[1] -notmatch '=$') {
        $taskJavaArgs+=$Matches[1]
    }
}
$taskJavaArgs+=@("-Dapplication.settingsdir=D:/codex-tools/bloody-port-20261028/settings",
    '-Djava.awt.headless=true','-cp',"$Ghidra/Ghidra/Framework/Utility/lib/Utility.jar",
    'ghidra.Ghidra','ghidra.app.util.headless.AnalyzeHeadless',
    $taskOutput,'GhidraCombat','-process','libil2cpp.so','-noanalysis',
    '-scriptPath',"$TaskRoot/scripts/ghidra",'-postScript','ExportCombat.java',
    "$taskOutput/$Catalog","$taskOutput/$Destination",'-log',"$taskOutput/ghidra.log")
& $taskJava @taskJavaArgs
exit $LASTEXITCODE
