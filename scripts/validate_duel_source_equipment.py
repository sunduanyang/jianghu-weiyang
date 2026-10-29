"""Compile the production adapter and DuelWeapon data class with small Unity stubs.

This exercises real C# numerics/JSON parsing; it is not Unity physics acceptance.
"""
import json
import subprocess
from build_bloody_item_catalog import plain_float
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'output/source-equipment-probe-20261028'
SCRIPTS = ROOT / 'unity/JianghuUnity/Assets/Scripts'

STUBS = '''using System; using System.IO;
namespace UnityEngine {
 public struct Vector2 { public float x,y; public Vector2(float x,float y){this.x=x;this.y=y;} public static Vector2 one=>new Vector2(1,1);public float magnitude=>(float)Math.Sqrt(x*x+y*y);public static Vector2 Scale(Vector2 a,Vector2 b)=>new Vector2(a.x*b.x,a.y*b.y); }
 public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;} }
 public static class Mathf {public const float PI=(float)Math.PI; public static float Cos(float x)=>(float)Math.Cos(x); public static float Sin(float x)=>(float)Math.Sin(x); public static int Max(int x,int y)=>Math.Max(x,y);public static float Max(float x,float y)=>Math.Max(x,y);public static float Min(float x,float y)=>Math.Min(x,y);public static float Abs(float x)=>Math.Abs(x);public static float Clamp(float x,float a,float b)=>Math.Clamp(x,a,b);public static float Clamp01(float x)=>Clamp(x,0,1); }
 public class TextAsset {public string text;}
 public static class Resources {public static string root; public static T Load<T>(string key) where T:class =>new TextAsset{text=File.ReadAllText(Path.Combine(root,"equipment.json"))} as T; }
}
'''


def main():
    subprocess.run(['python', 'scripts/build_duel_source_equipment.py', '--check'], cwd=ROOT, check=True)
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / 'EquipmentProbe.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><EnableDefaultCompileItems>true</EnableDefaultCompileItems><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>', encoding='utf-8')
    (OUT / 'UnityStubs.cs').write_text(STUBS, encoding='utf-8')
    weapon = (SCRIPTS / 'PhysicsCombat/DuelWeapons.cs').read_text(encoding='utf-8').split('public sealed partial class PhysicsDuel')[0]
    (OUT / 'DuelWeapon.cs').write_text(weapon, encoding='utf-8')
    for src in (SCRIPTS / 'Json.cs', SCRIPTS / 'PhysicsCombat/DuelSourceEquipment.cs', SCRIPTS / 'PhysicsCombat/DuelReferenceRules.cs', ROOT / 'tests/probes/DuelSourceEquipmentProbe.cs'):
        (OUT / src.name).write_text(src.read_text(encoding='utf-8'), encoding='utf-8')
    result = subprocess.run(['dotnet', 'run', '--project', str(OUT / 'EquipmentProbe.csproj'), '--', str(SCRIPTS.parent / 'Resources/CombatSource')], cwd=ROOT, check=True, capture_output=True, text=True)
    print(result.stdout)
    evidence = dict(checks=result.stdout.strip(), scope='production equipment adapter C# only; no Unity build/physics')
    source = ROOT / 'output/bloody-full-port-20261028/original-item-catalog.json'
    if source.exists():
        original = json.loads(source.read_text(encoding='utf-8'))
        count = 0
        for item in original['items']:
            for key, expected in item['stats'].items():
                assert plain_float(item['fields'][key]) == expected, (item['item_id'], key)
                count += 1
        names = {'HandItem_Dagger_1','HandItem_Sword_1','HandItem_2HandedSword_1','HandItem_Shield_1','HRE_HandItem_Shield_26'}
        evidence['protected_value_rechecks'] = count
        evidence['source_samples'] = [{key: row[key] for key in ('name','item_id','category','two_handed','stats')}
                                      for row in original['items'] if row['name'] in names]
        assert len(evidence['source_samples']) == 5
        evidence['source_apk_sha256'] = original['source_apk_sha256']
        deps = json.loads((ROOT / 'output/bloody-full-port-20261028/dependencies.json').read_text(encoding='utf-8'))
        mapping = json.loads((SCRIPTS.parent / 'Resources/CombatSource/equipment.json').read_text(encoding='utf-8'))
        held_checks = 0
        for held in mapping['held_profiles'].values():
            item = next(i for i in original['items'] if i['name'] == held['sample_name'])
            assert item['item_id'] == held['sample_item_id']
            bodies = {p['id']:p['data'] for p in deps['item_graphs'][item['source']['file']]['physics'] if p['type']=='Rigidbody2D'}
            for slot, prefix in [('ItemBaseRigidbody2D',''),('CounterweightRigidbody2D','counterweight_')]:
                body=bodies[int(item['fields'][slot]['id'])]
                for source_key, bridge_key in [('m_LinearDamping',prefix+'linear_damping'),('m_AngularDamping',prefix+'angular_damping'),('m_GravityScale','counterweight_gravity' if prefix else 'body_gravity')]:
                    assert abs(body[source_key]-held[bridge_key]) < 1e-6
                    held_checks += 1
        evidence['held_prefab_property_checks'] = held_checks
    (OUT / 'result.json').write_text(json.dumps(evidence, ensure_ascii=False, indent=2), encoding='utf-8')


if __name__ == '__main__':
    main()
