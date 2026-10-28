"""Compile the production adapter and DuelWeapon data class with small Unity stubs.

This exercises real C# numerics/JSON parsing; it is not Unity physics acceptance.
"""
import json
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'output/source-equipment-probe-20261028'
SCRIPTS = ROOT / 'unity/JianghuUnity/Assets/Scripts'

STUBS = '''using System; using System.IO;
namespace UnityEngine {
 public struct Vector2 { public float x,y; public Vector2(float x,float y){this.x=x;this.y=y;} }
 public static class Mathf {public const float PI=(float)Math.PI; public static float Cos(float x)=>(float)Math.Cos(x); public static float Sin(float x)=>(float)Math.Sin(x); public static int Max(int x,int y)=>Math.Max(x,y); }
 public class TextAsset {public string text;}
 public static class Resources {public static string root; public static T Load<T>(string key) where T:class =>new TextAsset{text=File.ReadAllText(Path.Combine(root,"equipment.json"))} as T; }
}
public static class DuelReferenceRules {public enum Surface {MetalBlade,MetalSpike,MetalBlunt,WoodBlunt,WoodHandle,MetalHandle,SpikePommel} public static bool IsHandle(Surface x)=>x==Surface.WoodHandle||x==Surface.MetalHandle||x==Surface.SpikePommel;}
'''


def main():
    subprocess.run(['python', 'scripts/build_duel_source_equipment.py', '--check'], cwd=ROOT, check=True)
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / 'EquipmentProbe.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><EnableDefaultCompileItems>true</EnableDefaultCompileItems><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>', encoding='utf-8')
    (OUT / 'UnityStubs.cs').write_text(STUBS, encoding='utf-8')
    weapon = (SCRIPTS / 'PhysicsCombat/DuelWeapons.cs').read_text(encoding='utf-8').split('public sealed partial class PhysicsDuel')[0]
    (OUT / 'DuelWeapon.cs').write_text(weapon, encoding='utf-8')
    for src in (SCRIPTS / 'Json.cs', SCRIPTS / 'PhysicsCombat/DuelSourceEquipment.cs', ROOT / 'tests/probes/DuelSourceEquipmentProbe.cs'):
        (OUT / src.name).write_text(src.read_text(encoding='utf-8'), encoding='utf-8')
    result = subprocess.run(['dotnet', 'run', '--project', str(OUT / 'EquipmentProbe.csproj'), '--', str(SCRIPTS.parent / 'Resources/CombatSource')], cwd=ROOT, check=True, capture_output=True, text=True)
    print(result.stdout)
    (OUT / 'result.json').write_text(json.dumps(dict(checks=result.stdout.strip(), scope='production equipment adapter C# only; no Unity build/physics'), indent=2), encoding='utf-8')


if __name__ == '__main__':
    main()
