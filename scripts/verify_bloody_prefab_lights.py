"""Validate the exact light recovery scope and compile Editor APIs read-only.

Does not open Unity, save any prefab, or operate the retained Android guest.
The parent-owned build invokes the actual Editor mutation and emits its report.
"""
import hashlib,json,re,subprocess
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'output/bloody-full-port-20261028/environment-check'
UNITY=Path('D:/Unity/Editors/6000.4.0f1/Editor/Data')
def main():
    d=json.loads((ROOT/'output/bloody-full-port-20261028/original-visual-catalog.json').read_text(encoding='utf-8'))
    source=ROOT/'recovery/bloody/Runtime/Editor/OriginalRecoveryBuild.Lights.cs'
    text=source.read_text(encoding='utf-8');groups=re.findall(r'\{"([^"]+)",new long\[\]\{([\d,]+)\}\}',text)
    assert len(groups)==7
    expected=[]
    for root,ids in groups:
        path='Assets/Resources/sourceprefabs/handitem/'+root+'.prefab' if root=='RI_HandItem_2HandedCannon' else 'Assets/GameObject/'+root+'.prefab'
        assert (ROOT/'unity/BloodyBastardsRecovery'/path).is_file(),path
        records=[]
        for source_id in map(int,ids.split(',')):
            identity='sharedassets0.assets:'+str(source_id)
            r=next(r for r in d['scene'] if r['source']==dict(file='sharedassets0.assets',id=source_id) and r['script']=='Light2D')
            assert d['hierarchy'][identity]['root']==root
            assert r['data']['m_LocalBounds'].keys()=={'m_Center','m_Extent'}
            for field in ('m_LightCookieSprite','m_DeprecatedPointLightCookieSprite'):
                sprite=r['assets'][field]
                if sprite:assert d['assets'][sprite]['type']=='Sprite' and d['assets'][sprite]['asset_paths']
            records.append(dict(identity=identity,path=r['scene_path'],hierarchy=d['hierarchy'][identity],cookie=r['assets']['m_LightCookieSprite']))
        assert len(records)==2
        expected.append(dict(root=root,prefab=path,lights=records))
    assert sum(len(g['lights']) for g in expected)==14
    scene_lights=[r for r in d['scene'] if r['script']=='Light2D' and r['source']['file']=='level0']
    assert len(scene_lights)==5
    # Compile the new partial with the exact existing CopyProperties and
    # ApplyProperty source, without importing or editing the active project.
    host=(ROOT/'recovery/bloody/Runtime/Editor/OriginalRecoveryBuild.cs').read_text(encoding='utf-8')
    helper=host[host.index('    static void CopyProperties('):host.index('    static void RestorePipeline(')]
    helper_source=OUT/'PrefabLightHostPartial.cs'
    helper_source.write_text('using System.Linq;using Jianghu;using UnityEditor;using UnityEngine;public static partial class OriginalRecoveryBuild {\n'+helper+'\n}',encoding='utf-8')
    runtime=next((UNITY/'NetCoreRuntime/shared/Microsoft.NETCore.App').iterdir())
    args=['-nologo','-target:library','-nostdlib+','-nowarn:0436',f'-out:{OUT/"OriginalPrefabLightsCompile.dll"}']
    args += [f'-r:{p}' for p in runtime.glob('*.dll') if(p.name.startswith('System.')and'.Native'not in p.name)or p.name in ('mscorlib.dll','netstandard.dll')]
    args += [f'-r:{p}' for p in (UNITY/'Managed').glob('*.dll')]
    args += [f'-r:{p}' for p in (UNITY/'Managed/UnityEngine').glob('*.dll')]
    args += [f'-r:{p}' for p in (ROOT/'unity/BloodyBastardsRecovery/Library/ScriptAssemblies').glob('*.dll')]
    args += [str(source),str(helper_source),str(ROOT/'recovery/bloody/Runtime/OriginalEnviroProp.cs'),str(ROOT/'recovery/bloody/Runtime/OriginalVisualCatalog.cs')]
    response=OUT/'prefab-light-compile.rsp';response.write_text('\n'.join('"'+a.replace('"','\\"')+'"' for a in args),encoding='utf-8-sig')
    result=subprocess.run([str(UNITY/'NetCoreRuntime/dotnet.exe'),str(UNITY/'DotNetSdkRoslyn/csc.dll'),'@'+str(response)],cwd=ROOT,text=True,capture_output=True)
    (OUT/'prefab-light-compile.txt').write_text(result.stdout+result.stderr)
    print(result.stdout,end='');print(result.stderr,end='')
    if result.returncode:raise SystemExit(result.returncode)
    (OUT/'prefab-light-scope-audit.json').write_text(json.dumps(dict(prefab_count=7,light_count=14,excluded_scene_lights=5,source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
        prefabs=expected,compile='Official Unity Editor/URP API assemblies plus exact existing property copier compiled successfully; no Unity mutation invoked by this verification',
        runtime_verification='Pending parent-owned Build call; actual Editor routine writes prefab-light-restoration.json with added/reused counts'),ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print('ORIGINAL_PREFAB_LIGHT_SCOPE_7_14_OK; UNITY_EDITOR_ASSEMBLY_COMPILE_OK; NO_PREFABS_MUTATED')
if __name__=='__main__':main()
