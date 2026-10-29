"""Read-only source-scope audit and official Unity Editor API compilation."""
import hashlib
import json
import subprocess
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'output/bloody-full-port-20261028/environment-check'
UNITY=Path('D:/Unity/Editors/6000.4.0f1/Editor/Data')


def main():
    catalog=json.loads((ROOT/'output/bloody-full-port-20261028/original-visual-catalog.json').read_text(encoding='utf-8'))
    stage='2_GraphicsContainer_BK_Monastery'
    scripts={'BystanderCustomization':7,'LightActivator':7,'CustomizeGroundSpriteShapes':1}
    rows=[r for r in catalog['scene'] if r['script'] in scripts and
          catalog['hierarchy'][r['source']['file']+':'+str(r['source']['id'])]['root']==stage]
    assert {k:sum(r['script']==k for r in rows) for k in scripts}==scripts
    refs={v for r in rows if r['script']=='BystanderCustomization' for v in r['assets'].values()
          if v and catalog['assets'][v]['type']=='Sprite'}
    assert len(refs)==51
    for identity in refs:
        asset=catalog['assets'][identity]
        assert asset['sprite_binding_method']=='original render data key' and len(asset['asset_paths'])==1
    movement=[r for r in catalog['scene'] if r['script']=='BystanderMovement' and catalog['hierarchy'][r['source']['file']+':'+str(r['source']['id'])]['root']==stage]
    controllers=[r for r in catalog['scene'] if r['script']=='BystandersController' and catalog['hierarchy'][r['source']['file']+':'+str(r['source']['id'])]['root']==stage]
    assert len(movement)==7 and len(controllers)==1
    ordered=[controllers[0]['assets']['BystandersMovements['+str(i)+']'] for i in range(7)]
    assert [catalog['hierarchy'][identity]['indices'] for identity in ordered]==[[10,i]for i in range(7)]
    for row in movement:
        for key in ('baseTransform','headTransform','animator'):
            assert catalog['hierarchy'][row['assets'][key]]['root']==stage
        assert row['data']['MovementCurve']['m_Curve'][0]['time']==0 and row['data']['MovementCurve']['m_Curve'][-1]['time']==1
    sources=['OriginalEnvironmentCustomizer.cs','OriginalEnvironmentCustomizer.Specials.cs','OriginalEnvironmentSpecials.cs',
             'OriginalEnviroProp.cs','OriginalGroundSource.cs','OriginalTimeOfDay.cs','OriginalWeather.cs',
             'OriginalVisualCatalog.cs','OriginalLightActivator.cs','OriginalStageActors.cs',
             'OriginalStageGroundEnable.cs','OriginalStageLightEnable.cs','OriginalBystanderCustomization.cs',
             'OriginalBystanderMovement.cs','OriginalBystandersController.cs','OriginalStageTweenHost.cs','OriginalInteriorBloodSurface.cs',
             'OriginalEnvironmentSpecialSource.cs','OriginalSpookyExtras.cs','OriginalStageChristmasActivation.cs','OriginalStageReverb.cs',
             'Editor/OriginalRecoveryBuild.StageActors.cs','Editor/OriginalRecoveryBuild.StageMovement.cs','Editor/OriginalRecoveryBuild.Specials.cs']
    runtime=next((UNITY/'NetCoreRuntime/shared/Microsoft.NETCore.App').iterdir())
    args=['-nologo','-target:library','-nostdlib+','-nowarn:0436',f'-out:{OUT/"OriginalStageActorsCompile.dll"}']
    args += [f'-r:{p}' for p in runtime.glob('*.dll') if(p.name.startswith('System.')and'.Native'not in p.name)or p.name in ('mscorlib.dll','netstandard.dll')]
    args += [f'-r:{p}' for directory in (UNITY/'Managed',UNITY/'Managed/UnityEngine',ROOT/'unity/BloodyBastardsRecovery/Library/ScriptAssemblies') for p in directory.glob('*.dll')]
    args += [str(ROOT/'recovery/bloody/Runtime'/name) for name in sources]
    response=OUT/'stage-actors-compile.rsp'
    response.write_text('\n'.join('"'+a.replace('"','\\"')+'"' for a in args),encoding='utf-8-sig')
    result=subprocess.run([str(UNITY/'NetCoreRuntime/dotnet.exe'),str(UNITY/'DotNetSdkRoslyn/csc.dll'),'@'+str(response)],cwd=ROOT,text=True,capture_output=True)
    (OUT/'stage-actors-compile.txt').write_text(result.stdout+result.stderr,encoding='utf-8')
    print(result.stdout,end='');print(result.stderr,end='')
    if result.returncode:raise SystemExit(result.returncode)
    (OUT/'stage-actors-scope-audit.json').write_text(json.dumps(dict(stage=stage,counts=scripts,separate_movement_group=dict(movements=7,controllers=1,source_order=ordered),sprite_identities=sorted(refs),
        sources={name:hashlib.sha256((ROOT/'recovery/bloody/Runtime'/name).read_bytes()).hexdigest() for name in sources},
        compile='Official Unity/UnityEditor API assemblies; runtime and Editor partial compiled together',
        no_unity_mutation=True,visual_parity='Parent-owned future Build and real screenshot required; no hard-coded visible crowd count'),indent=2)+'\n',encoding='utf-8')
    print('FIRST_STAGE_SOURCE_SCOPE_7_7_1_OK; SPRITE_IDENTITIES_51_OK; UNITY_EDITOR_ASSEMBLY_COMPILE_OK')


if __name__=='__main__':main()
