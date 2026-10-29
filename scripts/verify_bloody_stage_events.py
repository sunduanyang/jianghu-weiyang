"""Exercise the real stage event adapter with bounded Unity hierarchy shims."""
import json,subprocess
from pathlib import Path
from verify_bloody_environment import UNITY
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'output/bloody-full-port-20261028/environment-check/stage-movement'
SHIMS=r'''using System;using System.Collections.Generic;using System.Linq;using Jianghu;
namespace UnityEngine {public static class Time{public static int frameCount;}public class Transform {public string name;public Transform parent;public List<object> components=new List<object>();public T[] GetComponentsInChildren<T>(bool includeInactive)=>components.OfType<T>().ToArray();}}
public class OriginalVisualCatalog {public J Data=J.O("scene",new J(new List<J>()));}
public class OriginalEnvironmentCustomizer {public class Context {public int? GameMode;public bool? IsMainMenuState;public bool IsThroneRoom;}}
public class OriginalBystanderMovement {public bool isActiveAndEnabled=true,HasStarted;}
public class OriginalBystandersController {public bool isActiveAndEnabled=true,HasStarted;}public class OriginalStageChristmasActivation {public bool isActiveAndEnabled=true,HasStarted;}public class OriginalInteriorBloodSurface {public bool isActiveAndEnabled=true,HasStarted;}
public static class OriginalEnvironmentSource {public static string Identity(J r)=>r["source"]["file"].S+":"+r["source"]["id"].S;}
'''
PROGRAM=r'''using System;using UnityEngine;
class Program {
static int checks;static void Check(bool b){checks++;if(!b)throw new Exception("Stage adapter check "+checks);}static void Throws(Action f){bool got=false;try{f();}catch(InvalidOperationException){got=true;}Check(got);}
static int Main(){var context=new OriginalEnvironmentCustomizer.Context{GameMode=0,IsMainMenuState=false};var catalog=new OriginalVisualCatalog();var player=new Transform{name="player"};OriginalStageActors.Prepare(catalog,context,()=>16,true);Check(OriginalStageActors.Hour==16&&OriginalStageActors.GameMode==0&&!OriginalStageActors.IsMainMenuState&&OriginalStageActors.IsThroneRoom);Throws(()=>OriginalStageActors.NotifyLocalPlayerSpawned(player));int spawned=0,despawned=0,states=0;OriginalStageActors.LocalPlayerSpawned+=p=>{Check(p==player);spawned++;};OriginalStageActors.LocalPlayerDespawned+=p=>{Check(p==player);despawned++;};OriginalStageActors.NetworkedStateChanged+=(a,b)=>{Check(a==3&&b==4);states++;};var root=new Transform{name=OriginalStageActors.FirstStageRoot+"(Clone)"};var controller=new OriginalBystandersController();var a=new OriginalBystanderMovement();var b=new OriginalBystanderMovement{isActiveAndEnabled=false};root.components.Add(controller);root.components.Add(a);root.components.Add(b);OriginalStageActors.SetInstance(root);var wait=OriginalStageActors.WaitForStart();Check(wait.MoveNext());Throws(()=>OriginalStageActors.NotifyLocalPlayerSpawned(player));Check(spawned==0);controller.HasStarted=true;Check(wait.MoveNext());a.HasStarted=true;Check(wait.MoveNext());Time.frameCount++;Check(!wait.MoveNext());var seasonal=new OriginalStageChristmasActivation();root.components.Add(seasonal);Check(!OriginalStageActors.StartsReady);seasonal.HasStarted=true;Check(OriginalStageActors.StartsReady);var blood=new OriginalInteriorBloodSurface();root.components.Add(blood);Check(!OriginalStageActors.StartsReady);blood.HasStarted=true;Check(OriginalStageActors.StartsReady);b.isActiveAndEnabled=true;Check(!OriginalStageActors.StartsReady);Throws(()=>OriginalStageActors.NotifyLocalPlayerSpawned(player));b.HasStarted=true;Check(OriginalStageActors.StartsReady&&spawned==0);OriginalStageActors.NotifyLocalPlayerSpawned(player);Check(spawned==1);OriginalStageActors.ApplyGameState(3,4);Check(states==1);OriginalStageActors.NotifyLocalPlayerDespawned(player);Check(despawned==1);var node=new Transform{name="child",parent=root};Check(OriginalStageActors.InstanceRoot(node,OriginalStageActors.FirstStageRoot)==root);Throws(()=>OriginalStageActors.InstanceRoot(node,"other"));OriginalStageActors.Prepare(catalog,new OriginalEnvironmentCustomizer.Context(),()=>9);Throws(()=>{int n=OriginalStageActors.GameMode;});Throws(()=>{bool n=OriginalStageActors.IsMainMenuState;});OriginalStageActors.SetInstance(new Transform{name="ordinary other stage"});var ordinaryWait=OriginalStageActors.WaitForStart();Check(ordinaryWait.MoveNext());Time.frameCount++;Check(!ordinaryWait.MoveNext());OriginalStageActors.NotifyLocalPlayerSpawned(player);OriginalStageActors.NotifyLocalPlayerDespawned(player);OriginalStageActors.ApplyGameState(3,4);Check(spawned==1&&despawned==1&&states==1);Console.WriteLine("{\"stage_event_adapter_checks\":"+checks+",\"failures\":0}");return 0;}}
'''


def main():
    OUT.mkdir(parents=True,exist_ok=True);(OUT/'EventShims.cs').write_text(SHIMS);(OUT/'EventComparison.cs').write_text(PROGRAM)
    runtime=next((UNITY/'NetCoreRuntime/shared/Microsoft.NETCore.App').iterdir());target=OUT/'StageEventAdapter.dll'
    args=['-nologo','-target:exe','-nostdlib+',f'-out:{target}']+[f'-r:{p}'for p in runtime.glob('*.dll')if(p.name.startswith('System.')and'.Native'not in p.name)or p.name in ('mscorlib.dll','netstandard.dll')]
    args += [str(OUT/'EventShims.cs'),str(OUT/'EventComparison.cs'),str(ROOT/'recovery/bloody/Runtime/OriginalStageActors.cs'),str(ROOT/'unity/JianghuUnity/Assets/Scripts/Json.cs')]
    response=OUT/'events-compile.rsp';response.write_text('\n'.join('"'+arg+'"'for arg in args),encoding='utf-8-sig')
    result=subprocess.run([str(UNITY/'NetCoreRuntime/dotnet.exe'),str(UNITY/'DotNetSdkRoslyn/csc.dll'),'@'+str(response)],cwd=ROOT,text=True,capture_output=True)
    (OUT/'events-compile.txt').write_text(result.stdout+result.stderr)
    if result.returncode:print(result.stdout+result.stderr);raise SystemExit(result.returncode)
    target.with_suffix('.runtimeconfig.json').write_text(json.dumps(dict(runtimeOptions=dict(tfm='net8.0',framework=dict(name='Microsoft.NETCore.App',version=runtime.name)))))
    result=subprocess.run([str(UNITY/'NetCoreRuntime/dotnet.exe'),str(target)],cwd=ROOT,text=True,capture_output=True)
    (OUT/'events-comparison.txt').write_text(result.stdout+result.stderr);print(result.stdout,end='');print(result.stderr,end='')
    if result.returncode:raise SystemExit(result.returncode)


if __name__=='__main__':main()
