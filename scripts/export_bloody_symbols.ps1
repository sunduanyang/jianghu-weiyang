param(
    [Parameter(Mandatory=$true)][string]$Assembly,
    [Parameter(Mandatory=$true)][string]$Cecil,
    [Parameter(Mandatory=$true)][string]$Output
)
# Read metadata only. Dummy IL2CPP assemblies do not contain the game logic.
$ErrorActionPreference='Stop'
Add-Type -Path (Resolve-Path -LiteralPath $Cecil).Path
$catalogAssembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $Assembly).Path)
function Read-NamedAttribute($provider,[string]$name) {
    $values=[ordered]@{}
    foreach($attribute in $provider.CustomAttributes) {
        if($attribute.AttributeType.Name -ne $name){continue}
        foreach($field in $attribute.Fields){$values[$field.Name]=$field.Argument.Value}
        foreach($property in $attribute.Properties){$values[$property.Name]=$property.Argument.Value}
    }
    return $values
}
$catalogQueue=[Collections.Generic.Queue[object]]::new()
foreach($type in $catalogAssembly.MainModule.Types){$catalogQueue.Enqueue($type)}
$catalogTypes=[Collections.Generic.List[object]]::new()
while($catalogQueue.Count -gt 0) {
    $type=$catalogQueue.Dequeue()
    foreach($nested in $type.NestedTypes){$catalogQueue.Enqueue($nested)}
    $methods=@(foreach($method in $type.Methods) {
        $address=Read-NamedAttribute $method 'AddressAttribute'
        [ordered]@{name=$method.Name;signature=$method.FullName;token=$method.MetadataToken.ToString();static=$method.IsStatic;attributes=$address}
    })
    $fields=@(foreach($field in $type.Fields) {
        [ordered]@{name=$field.Name;type=$field.FieldType.FullName;static=$field.IsStatic;offset=(Read-NamedAttribute $field 'FieldOffsetAttribute');constant=$(if($field.HasConstant){$field.Constant}else{$null})}
    })
    $catalogTypes.Add([ordered]@{type=$type.FullName;base=$(if($type.BaseType){$type.BaseType.FullName}else{$null});fields=$fields;methods=$methods})
}
$catalogPath=[IO.Path]::GetFullPath($Output)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($catalogPath))|Out-Null
[ordered]@{assembly=$catalogAssembly.Name.Name;types=$catalogTypes}|ConvertTo-Json -Depth 14|Set-Content -LiteralPath $catalogPath -Encoding utf8
Write-Output ('Metadata types: '+$catalogTypes.Count+'; output: '+$catalogPath)
