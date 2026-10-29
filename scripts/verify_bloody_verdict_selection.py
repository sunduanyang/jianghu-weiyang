"""Exercise shipped verdict selection with original serialized view bindings.

This guards container timing, text-before-audio ordering and the different
RoundEnd roundWon/roundLost versus MatchEnd deathWon/deathLost field names.
"""
import json
from pathlib import Path
import subprocess
from verify_bloody_hud_text import SHIMS as HUD_SHIMS

ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'output/bloody-full-port-20261028/ai-check/ui';UNITY=Path('D:/Unity/Editors/6000.4.0f1/Editor/Data')

SHIMS=HUD_SHIMS.replace('public class Object {}','public class Object {public static void Destroy(Object o){}}')
SHIMS=SHIMS.replace('public T[]GetComponents<T>()where T:Component=>Array.Empty<T>();','public T[]GetComponents<T>()where T:Component=>component is T t?new[]{t}:Array.Empty<T>();',1)
# The first replacement above belongs to Component, which has no component
# field; forward it to the fake GameObject, then replace the GameObject one.
SHIMS=SHIMS.replace('=>component is T t?new[]{t}:Array.Empty<T>();','=>gameObject.GetComponents<T>();',1)
SHIMS=SHIMS.replace('public T[]GetComponents<T>()where T:Component=>Array.Empty<T>();','public T[]GetComponents<T>()where T:Component=>component is T t?new[]{t}:Array.Empty<T>();',1)
SHIMS=SHIMS.replace('public static class Resources {public static T Load<T>(string path)where T:class=>null;}','public static class Resources {public static string source;public static T Load<T>(string path)where T:class=>new TextAsset{text=source} as T;}')
SHIMS=SHIMS.replace('sounds.Add(data["key"].S);','if(data["kind"].S=="audio_object")sounds.Add(data["key"].S);')
SHIMS+='''public class OriginalUIPopupResult {} public class OriginalUIPopupDriver:UnityEngine.MonoBehaviour {public OriginalUIPopupResult Result;public void Initialize(OriginalUIPopups host,J record,J source){}}'''

PROGRAM=r'''
using System;using System.Collections.Generic;using System.IO;using System.Linq;using Jianghu;using UnityEngine;
class Program {
 static int checks;static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
 static void Main(string[]args){Resources.source=File.ReadAllText(args[0]);var source=J.Parse(Resources.source);var ui=J.Parse(File.ReadAllText(args[1]));var root=new GameObject();root.transform.name="CanvasAndCamera";var nodes=new Dictionary<string,GameObject>{{"CanvasAndCamera",root}};
 GameObject Make(string path){if(nodes.TryGetValue(path,out var old))return old;int at=path.LastIndexOf('/');var parent=Make(path.Substring(0,at));var go=new GameObject();go.transform.name=path.Substring(at+1);parent.transform.children.Add(go.transform);nodes.Add(path,go);return go;}
 foreach(var r in source["records"].A)Make(r["path"].S);var canvas=Make("CanvasAndCamera/Canvas");canvas.component=new AudioSource{gameObject=canvas};var popups=new OriginalUIPopups(root.transform,ui);
 foreach(bool match in new[]{false,true})foreach(bool won in new[]{false,true})foreach(int mode in new[]{0,1,2,3,4,5})foreach(int reason in new[]{0,1,2}){
  var view=ui["records"].A.First(r=>r["script"].S==(match?"MatchEndView":"RoundEndView"))["data"];var container=nodes[view["popupsContainer"]["path"].S];container.activeSelf=true;foreach(Transform child in container.transform)child.gameObject.activeSelf=true;OriginalUIAudio.sounds.Clear();bool prepared=false;
  var selected=popups.ActivateVerdict(match,won,mode,matchEndReason:reason,prepareText:()=>{Check(!container.activeSelf,"text waits behind hidden container");Check(container.transform.children.All(c=>!c.gameObject.activeSelf),"text prepared before selected child");Check(OriginalUIAudio.sounds.Count==0,"text prepared before result sound");prepared=true;});
  bool forfeited=match&&(reason==1||reason==2)&&mode!=4;var expected=forfeited?new[]{view["opponentForfeitedPopup"]["path"].S}:view["popups"].A.Where(p=>p["mode"].I==mode&&p["isWin"].B==won).Select(p=>p["popup"]["path"].S);
  Check(prepared,"prepare callback");Check(!container.activeSelf,"parent original clip owns container reveal");Check(string.Join("|",selected.Select(g=>g.transform.name))==string.Join("|",expected.Select(p=>nodes[p].transform.name)),"source popup selection");Check(selected.All(g=>g.activeSelf),"source activeSelf selection");
  var soundView=source["views"][match?"MatchEndView":"RoundEndView"];string soundField=match?(forfeited||won?"deathWon":"deathLost"):(won?"roundWon":"roundLost");string expectedSound=soundView[soundField]["key"].S;
  Check(OriginalUIAudio.sounds.Count==selected.Count,"source result sound count");if(selected.Count>0)Check(OriginalUIAudio.sounds.All(k=>k==expectedSound&&k.Length>0),"correct source round/death sound binding");
 }
 Console.WriteLine("{\"verdict_selection_checks\":"+checks+",\"failures\":0}");}
}
'''


def main():
    OUT.mkdir(exist_ok=True);(OUT/'VerdictSelectionShims.cs').write_text(SHIMS);(OUT/'VerdictSelectionComparison.cs').write_text(PROGRAM)
    runtime=next((UNITY/'NetCoreRuntime/shared/Microsoft.NETCore.App').iterdir());target=OUT/'CompareVerdictSelection.dll'
    command=[str(UNITY/'NetCoreRuntime/dotnet.exe'),str(UNITY/'DotNetSdkRoslyn/csc.dll'),'-nologo','-target:exe','-nostdlib+',f'-out:{target}']
    command += [f'-r:{p}'for p in runtime.glob('*.dll')if(p.name.startswith('System.')and'.Native'not in p.name)or p.name in('mscorlib.dll','netstandard.dll')]
    command += [str(OUT/'VerdictSelectionShims.cs'),str(OUT/'VerdictSelectionComparison.cs'),str(ROOT/'recovery/bloody/Runtime/OriginalUIPopups.cs'),str(ROOT/'unity/JianghuUnity/Assets/Scripts/Json.cs')]
    subprocess.run(command,check=True,cwd=ROOT)
    target.with_suffix('.runtimeconfig.json').write_text(json.dumps(dict(runtimeOptions=dict(tfm='net8.0',framework=dict(name='Microsoft.NETCore.App',version=runtime.name)))))
    result=subprocess.run([str(UNITY/'NetCoreRuntime/dotnet.exe'),str(target),str(ROOT/'recovery/bloody/Runtime/Resources/OriginalUIPopupSource.json'),str(ROOT/'output/bloody-full-port-20261028/original-ui-source.json')],cwd=ROOT,text=True,capture_output=True)
    (OUT/'verdict-selection-comparison.txt').write_text(result.stdout+result.stderr);print(result.stdout,end='');print(result.stderr,end='')
    if result.returncode:raise SystemExit(result.returncode)


if __name__=='__main__':main()
