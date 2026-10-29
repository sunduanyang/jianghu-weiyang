"""Original TransitionView call/RNG oracle and byte-identical asset checks."""
import hashlib
import json
from pathlib import Path
import struct
import subprocess
from verify_bloody_ai_native import NativeAI
from unicorn.arm64_const import UC_ARM64_REG_X0,UC_ARM64_REG_X1,UC_ARM64_REG_X2,UC_ARM64_REG_LR,UC_ARM64_REG_PC

ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'output/bloody-full-port-20261028/ai-check/transition-view';UNITY=Path('D:/Unity/Editors/6000.4.0f1/Editor/Data');SOURCE=ROOT/'recovery/bloody/Runtime/Resources/OriginalTransitionViewSource.json'


class NativeView(NativeAI):
    def __init__(self):
        super().__init__();self.library=json.loads(SOURCE.read_text(encoding='utf-8'));self.ptr(self.SELF+0x20,0x10013200);self.ptr(self.SELF+0x60,0x10013000);self.tokens={};self.audio={}
        self.ptr(0x720d298,0x10015000)
        for cell,slot,token in((0x7217fd8,0x10018b00,101),(0x7217fe0,0x10018b08,102)):self.ptr(cell,slot);self.ptr(slot,token)
        for i,key in enumerate(('initialize','slideIn','slideOut')):self.ptr(self.SELF+0x28+i*8,0x10013600+i*0x80)
        self.clip_fields={0x10013600+i*0x80:key for i,key in enumerate(('initialize','slideIn','slideOut'))}
        for i,(key,row)in enumerate(self.library['audio_objects'].items()):
            obj=0x10014000+i*0x100;arr=0x10014400+i*0x100;self.ptr(self.SELF+{'panelsSlideIn':0x40,'panelsUnlock':0x48,'panelsSlideOut':0x50,'panelsContact':0x58}[key],obj);self.ptr(obj+0x18,arr);self.cpu.mem_write(obj+0x24,struct.pack('<ffff',row['volume']['min'],row['volume']['max'],row['pitch']['min'],row['pitch']['max']));self.cpu.mem_write(arr+0x18,struct.pack('<i',len(row['clips'])))
            for j,guid in enumerate(row['clips']):
                address=0x10016000+i*0x100+j*0x20;self.ptr(arr+0x20+j*8,address);self.audio[address]=self.library['audio'][guid]['resource']
    def ptr(self,a,n):self.cpu.mem_write(a,struct.pack('<Q',n))
    def _hook(self,cpu,a,size,data):
        x0=cpu.reg_read(UC_ARM64_REG_X0);x1=cpu.reg_read(UC_ARM64_REG_X1)
        if a==0x316bb40:self.alloc+=0x80;cpu.reg_write(UC_ARM64_REG_X0,self.alloc)
        elif a==0x5c6ff00:self.tokens[x0]=cpu.reg_read(UC_ARM64_REG_X2)
        elif a in(0x36886e8,0x368952c,0x3688a60,0x36895e8):self.calls.append(dict(kind={0x36886e8:'add_switching',0x368952c:'add_switched',0x3688a60:'remove_switching',0x36895e8:'remove_switched'}[a],token=self.tokens[x0]))
        elif a==0x332cbfc:self.calls.append(dict(kind='native_animancer',field=self.clip_fields[x1],fade=self.sf(0),mode=cpu.reg_read(UC_ARM64_REG_X2)))
        elif a==0x6abfa9c:
            lo,hi=self.sf(0),self.sf(1);self.calls.append(dict(kind='float_rng',lo=lo,hi=hi));self.setf(0,lo+(hi-lo)*self.fraction)
        elif a==0x6abfadc:self.calls.append(dict(kind='int_rng',lo=x0,hi=x1));cpu.reg_write(UC_ARM64_REG_X0,self.index)
        elif a==0x6a64e84:self.calls.append(dict(kind='pitch',value=self.sf(0)))
        elif a==0x6a65560:self.calls.append(dict(kind='oneshot',resource=self.audio[x1],volume=self.sf(0)))
        elif a in(0x316bb50,0x316bb58):raise RuntimeError('Source threw '+hex(a))
        else:return super()._hook(cpu,a,size,data)
        cpu.reg_write(UC_ARM64_REG_PC,cpu.reg_read(UC_ARM64_REG_LR))
    def invoke(self,method,fraction=.5,index=0):
        self.calls=[];self.alloc=0x1001c000;self.fraction=fraction;self.index=index
        self.call({'enable':0x36afb34,'disable':0x36afbf0,'switching':0x36afcac,'switched':0x36afcd4,'PlaySlideIn':0x36afcfc,'PlayUnlock':0x36afd98,'PlaySlideOut':0x36afda0,'PlayContact':0x36afda8}[method],self.SELF)
        return self.calls


SHIMS=r'''
using System;using System.Collections.Generic;using Jianghu;
public static class Observed {public static List<J> calls=new List<J>();public static string clip;public static int starts;}
namespace UnityEngine {
 public class Object {} public class Component:Object{public GameObject gameObject=new GameObject();public T GetComponent<T>()where T:class=>null;}
 public class GameObject:Object{public void SetActive(bool b){}public T AddComponent<T>()where T:new()=>new T();}
 public class Transform:Component{public Transform Find(string p)=>null;}
 public class MonoBehaviour:Component{public bool isActiveAndEnabled=true;}
 public enum AnimatorUpdateMode{Normal,AnimatePhysics,UnscaledTime}public enum AnimatorCullingMode{AlwaysAnimate}
 public class Animator:Component{public AnimatorUpdateMode updateMode;public AnimatorCullingMode cullingMode;public bool enabled;}
 public class AnimationClip:Object{public string name;}public class AudioClip:Object{public string name;}
 public class AudioSource:Component{public float pitch {set{Observed.calls.Add(J.O("kind","pitch","value",value));}}public void PlayOneShot(AudioClip c,float v){Observed.calls.Add(J.O("kind","oneshot","resource",c.name,"volume",v));}}
 public class TextAsset{public string text;}public static class Resources{public static T Load<T>(string p)where T:class{if(typeof(T)==typeof(AudioClip))return new AudioClip{name=p}as T;if(typeof(T)==typeof(AnimationClip))return new AnimationClip{name=p}as T;return null;}}
 public static class Random{public static float fraction;public static int index;public static float Range(float lo,float hi){Observed.calls.Add(J.O("kind","float_rng","lo",lo,"hi",hi));return lo+(hi-lo)*fraction;}public static int Range(int lo,int hi){Observed.calls.Add(J.O("kind","int_rng","lo",lo,"hi",hi));return index;}}
 public static class Application{public static bool isBatchMode;}public static class Time{public static float unscaledDeltaTime;}
 public class WaitForSecondsRealtime{public WaitForSecondsRealtime(float n){}}public class WaitUntil{public WaitUntil(Func<bool> f){}}
}
namespace UnityEngine.Playables{public enum DirectorUpdateMode{UnscaledGameTime}public struct PlayableGraph{public static PlayableGraph Create(string s)=>new PlayableGraph();public void SetTimeUpdateMode(DirectorUpdateMode m){}public void Connect(object a,int x,object b,int y){}public bool IsValid()=>true;public void Play(){}public void Stop(){}public void Destroy(){}}}
namespace UnityEngine.Animations{using UnityEngine;using UnityEngine.Playables;public struct AnimationMixerPlayable{public static AnimationMixerPlayable Create(PlayableGraph g,int n)=>new AnimationMixerPlayable();public void SetInputCount(int n){}public void SetInputWeight(int n,float w){}}
 public struct AnimationPlayableOutput{public static AnimationPlayableOutput Create(PlayableGraph g,string n,Animator a)=>new AnimationPlayableOutput();public void SetSourcePlayable(AnimationMixerPlayable m){}}
 public struct AnimationClipPlayable{AnimationClip clip;public static AnimationClipPlayable Create(PlayableGraph g,AnimationClip c)=>new AnimationClipPlayable{clip=c};public void SetApplyFootIK(bool b){}public void SetApplyPlayableIK(bool b){}public void SetTime(double t){if(t!=0)throw new Exception("Expected FromStart");Observed.clip=clip.name;Observed.starts++;}public void SetSpeed(double s){if(s!=1)throw new Exception("Expected native speed1");}public AnimationClip GetAnimationClip()=>clip;}}
'''
PROGRAM=r'''
using System;using System.Collections;using System.IO;using System.Reflection;using Jianghu;
class Program{
 static int checks,local;static void Check(bool b,string m,bool l=false){if(l)local++;else checks++;if(!b)throw new Exception(m);}
 static void Set(object o,string key,object v)=>o.GetType().GetField(key,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(o,v);
 static void Invoke(object o,string key)=>o.GetType().GetMethod(key,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).Invoke(o,null);
 static bool Equal(J a,J b){if(a.D.Count!=b.D.Count)return false;foreach(var p in a.D){if(p.Value.value is bool||p.Value.value is string){if(p.Value.S!=b[p.Key].S)return false;}else if(Math.Abs(p.Value.N-b[p.Key].N)>.000002)return false;}return true;}
 static void Main(string[]args){var oracle=J.Parse(File.ReadAllText(args[0]));var library=J.Parse(File.ReadAllText(args[1]));var seq=new OriginalArenaTransitionSequence(J.O("cover_delay",.35,"network_controller_spawn_timeout",15),new OriginalArenaTransitionContext{RunnerActive=()=>false,NetworkControllerExists=()=>true,InterstitialPlaying=()=>false,TearDownRunner=()=>null,SpawnRunner=_=>{},RequestLocalState=_=>{},SetContextObjects=_=>{}},false);var view=new OriginalTransitionView();Set(view,"sequence",seq);Set(view,"library",library);Set(view,"audioSource",new UnityEngine.AudioSource());Set(view,"initialized",true);
 foreach(var c in oracle["cases"].A){string method=c["method"].S;Observed.calls.Clear();UnityEngine.Random.fraction=(float)c["fraction"].N;UnityEngine.Random.index=c["index"].I;int priorStarts=Observed.starts;
 switch(method){case "enable":Invoke(view,"OnEnable");Check(view.Listening,"native enable subscription");Check(seq.GetType().GetField("OnSwitching",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(seq)!=null&&seq.GetType().GetField("OnSwitched",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(seq)!=null,"native two event owners");Check(c["expected"].A[0]["kind"].S=="add_switching"&&c["expected"].A[1]["kind"].S=="add_switched","native subscription order");Check(Observed.starts==priorStarts,"native enable does not play initialize");break;
 case "disable":Invoke(view,"OnDisable");Check(!view.Listening,"native disable subscription");Check(seq.GetType().GetField("OnSwitching",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(seq)==null&&seq.GetType().GetField("OnSwitched",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(seq)==null,"native unsubscribe owners");Check(c["expected"].A[0]["kind"].S=="remove_switching"&&c["expected"].A[1]["kind"].S=="remove_switched","native unsubscribe order");break;
 case "switching":case "switched":Invoke(view,method=="switching"?"OnSwitching":"OnSwitched");var e=c["expected"][0];Check(e["fade"].N==0&&e["mode"].I==2,"native FromStart mode and zero fade");Check(Observed.clip==library["clips"][e["field"].S]["resource"].S,"native source clip field");Check(Observed.starts==priorStarts+1,"native restart on every callback");break;
 default:Invoke(view,method);var wanted=c["expected"].A;Check(wanted.Count==Observed.calls.Count,"native sound call count");for(int i=0;i<wanted.Count;i++)Check(Equal(wanted[i],Observed.calls[i]),"native source RNG/audio call order "+method+" "+i+" got="+Observed.calls[i]+" wanted="+wanted[i]);break;}
 }
 int starts=Observed.starts;Invoke(view,"OnEnable");seq.CreateToArena(2).MoveNext();Check(Observed.starts==starts+1,"real Sequence.OnSwitching reaches bound view",true);Invoke(view,"OnDisable");starts=Observed.starts;seq.CreateToArena(2).MoveNext();Check(Observed.starts==starts,"disabled view has no event listener",true);Console.WriteLine("{\"native_transition_view_checks\":"+checks+",\"local_lifecycle_checks\":"+local+",\"failures\":0}");}
}
'''


def main():
    OUT.mkdir(exist_ok=True,parents=True);n=NativeView();cases=[]
    for method in('enable','disable','switching','switching','switched'):
        cases.append(dict(method=method,fraction=.5,index=0,expected=n.invoke(method)))
    for method,key in [('PlaySlideIn','panelsSlideIn'),('PlayUnlock','panelsUnlock'),('PlaySlideOut','panelsSlideOut'),('PlayContact','panelsContact')]:
        for fraction in(0,.25,.5,.99):
            for index in range(len(n.library['audio_objects'][key]['clips'])):cases.append(dict(method=method,fraction=fraction,index=index,expected=n.invoke(method,fraction,index)))
    oracle=OUT/'native-transition-view-oracle.json';oracle.write_text(json.dumps(dict(cases=cases),indent=2));(OUT/'Shims.cs').write_text(SHIMS);(OUT/'Comparison.cs').write_text(PROGRAM)
    runtime=next((UNITY/'NetCoreRuntime/shared/Microsoft.NETCore.App').iterdir());target=OUT/'CompareTransitionView.dll'
    cmd=[str(UNITY/'NetCoreRuntime/dotnet.exe'),str(UNITY/'DotNetSdkRoslyn/csc.dll'),'-nologo','-target:exe','-nostdlib+',f'-out:{target}']+[f'-r:{p}'for p in runtime.glob('*.dll')if(p.name.startswith('System.')and'.Native'not in p.name)or p.name in('mscorlib.dll','netstandard.dll')]+[str(OUT/'Shims.cs'),str(OUT/'Comparison.cs'),str(ROOT/'recovery/bloody/Runtime/OriginalTransitionView.cs'),str(ROOT/'recovery/bloody/Runtime/OriginalArenaTransitionSequence.cs'),str(ROOT/'unity/JianghuUnity/Assets/Scripts/Json.cs')]
    compiled=subprocess.run(cmd,cwd=ROOT,text=True,capture_output=True);(OUT/'compile.txt').write_text(compiled.stdout+compiled.stderr,encoding='utf-8')
    if compiled.returncode:print(compiled.stdout+compiled.stderr);raise SystemExit(compiled.returncode)
    target.with_suffix('.runtimeconfig.json').write_text(json.dumps(dict(runtimeOptions=dict(tfm='net8.0',framework=dict(name='Microsoft.NETCore.App',version=runtime.name)))))
    result=subprocess.run([str(UNITY/'NetCoreRuntime/dotnet.exe'),str(target),str(oracle),str(SOURCE)],cwd=ROOT,text=True,capture_output=True);(OUT/'comparison.txt').write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout,end='');print(result.stderr,end='')
    if result.returncode:raise SystemExit(result.returncode)
    assets=ROOT/'output/bloody-full-port-20261028/Recovered/ExportedProject/Assets';checks=0
    for row in list(n.library['clips'].values())+list(n.library['audio'].values()):
        original=assets/row['source'];copied=next((SOURCE.parent).glob(row['resource']+'.*'));assert copied.read_bytes()==original.read_bytes()and hashlib.sha256(copied.read_bytes()).hexdigest()==row['sha256'];checks+=2
    bindings=0;events=0;paths=set(n.library['source_node_paths']);driver=(ROOT/'recovery/bloody/Runtime/OriginalTransitionView.cs').read_text(encoding='utf-8')
    root_image=next(r['data']['m_Script']for r in n.library['host_components']if r['kind']=='MonoBehaviour'and r['data'].get('m_Script',{}).get('fileID')==-765806418)
    for clip in n.library['clips'].values():
        for curve in clip['curves']:
            assert n.library['path']+('/'+curve['path']if curve['path']else'')in paths;checks+=1;bindings+=1
            if not curve['path']and curve['attribute']=='m_Enabled':assert curve['script']=={k:v for k,v in root_image.items()if k!='path'};checks+=1
        for event in clip['events']:assert 'public void '+event['functionName']+'()'in driver and event['objectReferenceParameter']['fileID']==0;checks+=2;events+=1
    (OUT/'asset-check.json').write_text(json.dumps(dict(byte_identical_source_assets=9,curve_bindings=bindings,animation_events=events,checks=checks,failures=0),indent=2));print(json.dumps(dict(asset_checks=checks,curve_bindings=bindings,animation_events=events,failures=0)))


if __name__=='__main__':main()
