param(
    [Parameter(Mandatory=$true)][string]$Left,
    [Parameter(Mandatory=$true)][string]$Right,
    [Parameter(Mandatory=$true)][string]$Cecil,
    [Parameter(Mandatory=$true)][string]$Output,
    [switch]$AllMethods,
    [switch]$NormalizeGeneratedNames
)
$ErrorActionPreference='Stop'
Add-Type -Path (Resolve-Path -LiteralPath $Cecil).Path
function Canonical-IL([string]$text){
    if(!$NormalizeGeneratedNames){return $text}
    $text=$text -replace '<>c__DisplayClass\d+_(\d+)','<>c__DisplayClassN_$1'
    $text=$text -replace 'b__\d+_(\d+)','b__N_$1'
    return $text -replace '<>9__\d+_(\d+)','<>9__N_$1'
}
function Combat-Methods([string]$path){
    $assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $path).Path)
    $queue=[Collections.Generic.Queue[object]]::new()
    foreach($type in $assembly.MainModule.Types){$queue.Enqueue($type)}
    $result=@{}
    while($queue.Count){
        $type=$queue.Dequeue()
        foreach($child in $type.NestedTypes){$queue.Enqueue($child)}
        if(!$AllMethods -and $type.FullName -notmatch '^(PhysicsDuel($|/)|DuelReferenceRules($|/)|DuelSourceMotion($|/)|DuelKick|DuelContact|DuelWeapon($|/))'){continue}
        foreach($method in $type.Methods){
            $parts=[Collections.Generic.List[string]]::new()
            $parts.Add($method.FullName)
            if($method.HasBody){
                $parts.Add('init-locals='+$method.Body.InitLocals)
                foreach($local in $method.Body.Variables){$parts.Add('local='+$local.VariableType.FullName)}
                foreach($instruction in $method.Body.Instructions){$parts.Add($instruction.OpCode.Name+' '+[string]$instruction.Operand)}
                foreach($handler in $method.Body.ExceptionHandlers){$parts.Add('handler='+$handler.HandlerType+' '+$handler.TryStart+' '+$handler.TryEnd+' '+$handler.HandlerStart+' '+$handler.HandlerEnd)}
            }
            $sha=[Security.Cryptography.SHA256]::Create()
            $key=Canonical-IL $method.FullName
            $hash=[Convert]::ToHexString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes((Canonical-IL ($parts -join "`n")))))
            if($result.ContainsKey($key) -and $result[$key] -ne $hash){throw "Canonical generated method collision: $key"}
            $result[$key]=$hash
            $sha.Dispose()
        }
    }
    return $result
}
$leftMethods=Combat-Methods $Left
$rightMethods=Combat-Methods $Right
$keys=@($leftMethods.Keys)+@($rightMethods.Keys)|Sort-Object -Unique
$differences=@(foreach($key in $keys){if($leftMethods[$key] -ne $rightMethods[$key]){[ordered]@{method=$key;left=$leftMethods[$key];right=$rightMethods[$key]}}})
$record=[ordered]@{left=[IO.Path]::GetFullPath($Left);right=[IO.Path]::GetFullPath($Right);methods=$keys.Count;equal=$differences.Count -eq 0;differences=$differences;left_hashes=$leftMethods;right_hashes=$rightMethods}
$record|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $Output -Encoding utf8
Write-Output ('Combat methods='+$keys.Count+'; differences='+$differences.Count)
if($differences.Count){foreach($row in $differences){Write-Output $row['method']}}
