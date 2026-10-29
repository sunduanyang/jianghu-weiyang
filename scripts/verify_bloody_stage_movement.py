"""Original ARM movement/controller branches vs actual C# with engine hooks.

Does not launch Unity or Android. Frame scheduling/tween interpolation require
the real integration run; this checks native branch/RNG/callback semantics.
"""
import hashlib,itertools,json,struct,subprocess
from pathlib import Path
from verify_bloody_ai_native import NativeAI,f32
from verify_bloody_environment import UNITY
from unicorn.arm64_const import *

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'output/bloody-full-port-20261028/environment-check/stage-movement'


class NativeMovement(NativeAI):
    def __init__(self):
        self.trace=[];self.mode='init';super().__init__()
        for got in (0x72120f8,0x7212238):self.ptr(got,0x10015000)
        for got in (0x72122c8,0x72122c0):self.ptr(got,0x10016000)
        self.ptr(0x10016000,0x10016100);self.ptr(0x100161b8,0x10016300);self.ptr(0x10016300,0x10016400);self.ptr(0x10016420,0x10016500)
        for got in range(0x7211798,0x72117d0,8):self.ptr(got,0x10015000)
        self.names={0x35c7184:'SetIdle',0x35c722c:'AnimationTick',0x35c72d4:'FollowingTick',0x35c737c:'StartCheeringAnimation',0x35c761c:'BystandersEndCheer'}
        self.strings={va:0x1001d000+i*8 for i,va in enumerate(self.names)}
        self.back={self.strings[va]:name for va,name in self.names.items()}
    def ptr(self,a,v):self.cpu.mem_write(a,struct.pack('<Q',v))
    def i32(self,a,v):self.cpu.mem_write(a,struct.pack('<I',v&0xffffffff))
    def byte(self,a,v):self.cpu.mem_write(a,bytes([int(v)]))
    def owner(self,address):return self.owners.get(address,-1)
    def _hook(self,cpu,a,size,data):
        x0=cpu.reg_read(UC_ARM64_REG_X0);x1=cpu.reg_read(UC_ARM64_REG_X1)
        if a in getattr(self,'names',{}):cpu.reg_write(UC_ARM64_REG_X0,self.strings[a])
        elif a==0x316b7cc:pass
        elif a==0x6abfadc:
            lo=x0&0xffffffff;hi=x1&0xffffffff;self.trace.append(['int',lo,hi]);cpu.reg_write(UC_ARM64_REG_X0,lo+min(hi-lo-1,int((hi-lo)*self.fraction)))
        elif a==0x6abfa9c:
            lo,hi=self.sf(0),self.sf(1);self.trace.append(['float',lo,hi]);self.setf(0,f32(lo+f32(f32(hi-lo)*self.fraction)))
        elif a==0x6a57040:self.trace.append(['speed',self.owner(x0),self.sf(0)])
        elif a==0x6a546d4:self.trace.append(['trigger',self.owner(x0),x1])
        elif a==0x6acc03c:self.trace.append(['invoke',self.owner(x0),self.back[x1],self.sf(0)])
        elif a==0x6acc22c:self.trace.append(['repeat',self.owner(x0),self.back[x1],self.sf(0),self.sf(1)])
        elif a==0x6acc308:self.trace.append(['cancel',self.owner(x0),self.back[x1]])
        elif a==0x6acbf90:self.trace.append(['cancel_all',self.owner(x0)])
        elif a==0x6ac6408:cpu.reg_write(UC_ARM64_REG_X0,self.transforms[x0])
        elif a==0x6adcb28:
            for i,v in enumerate((0,0,0,1)):self.setf(i,v)
        elif a==0x6ab6e7c:
            for i in range(3):self.setf(i,0)
        elif a==0x6ab7498:pass
        elif a==0x6adc6e0:
            for i,v in enumerate(self.positions[x0]):self.setf(i,v)
        elif a==0x36f3ac8:self.trace.append(['move',self.sf(0),self.sf(1),self.sf(2),self.sf(3),bool(x1)]);cpu.reg_write(UC_ARM64_REG_X0,0x1001f000)
        elif a==0x4180a04:self.trace.append(['curve'])
        elif a==0x6ab6de0:
            values=struct.unpack('<3f',self.cpu.mem_read(x0,12));self.trace.append(['rotation',*[round(v*180/3.141592653589793,4) for v in values]])
            for i,v in enumerate((0,0,0,1)):self.setf(i,v)
        elif a==0x6add540:pass
        elif a==0x6add09c:self.trace.append(['scale',self.sf(0),self.sf(1),self.sf(2)])
        elif a==0x6ade780:cpu.reg_write(UC_ARM64_REG_X0,self.children)
        elif a==0x363fb48:cpu.reg_write(UC_ARM64_REG_X0,int(self.menu))
        elif a==0x6ac65ac:pass
        elif a==0x6aca694:self.trace.append(['active',self.owner(x0),bool(x1)])
        elif a==0x6a66288:self.trace.append(['mute',bool(x1)])
        elif a==0x6a657ac:cpu.reg_write(UC_ARM64_REG_X0,int(self.playing))
        elif a==0x6ac5bfc:self.enabled=bool(x1);self.trace.append(['enabled',self.enabled])
        elif a==0x6a64dac:self.trace.append(['volume',self.sf(0)])
        elif a==0x3411e0c:
            self.trace.append(['audio'])
            if self.enabled:
                self.trace.extend([['int',0,1],['float',1,1],['volume',1],['float',1,1],['pitch',1],['loop',True],['play']]);self.playing=True
        elif a==0x316bb40:cpu.reg_write(UC_ARM64_REG_X0,self.allocate);self.allocate+=0x80
        elif a in (0x5d447a0,0x5118948,0x5119a74,0x418106c):pass
        elif a in (0x36e70cc,0x33e242c):self.trace.append(['fade',self.sf(0),self.sf(1)]);cpu.reg_write(UC_ARM64_REG_X0,0x1001f000)
        elif a in (0x316bb50,0x316bb58):raise RuntimeError(f'Source stage actor throw {a:x}')
        else:return super()._hook(cpu,a,size,data)
        cpu.reg_write(UC_ARM64_REG_PC,cpu.reg_read(UC_ARM64_REG_LR))
    def movement(self,c):
        self.mode='movement';self.fraction=f32(c['fraction']);self.trace=[];self.owners={self.SELF:0,0x10018000:0};self.transforms={self.SELF:0x10012000}
        self.positions={0x10012000:(1,2,3),0x10012100:(4,5,6),0x10012200:(c['target'],0,0)}
        self.ptr(self.SELF+0x20,0x10012100);self.ptr(self.SELF+0x30,0x10018000);self.ptr(self.SELF+0x48,0x10012300)
        self.cpu.mem_write(self.SELF+0x40,struct.pack('<2f',2,5));self.i32(self.SELF+0x50,c['sit']);self.i32(self.SELF+0x54,c['cheer'])
        for off,value in ((0x58,10),(0x5c,11),(0x60,12)):self.i32(self.SELF+off,value)
        self.cpu.mem_write(self.SELF+0x64,bytes(0x34));self.cpu.mem_write(self.SELF+0x74,struct.pack('<9f',0,180,0,1,1,1,1,1,-1))
        for va,arg in [(0x36c1b70,0x10012200),(0x36c1d80,0),(0x36c1d80,0),(0x36c1ee8,0),(0x36c213c,0),(0x36c20fc,0),(0x36c2208,0),(0x36c1d50,0),(0x36c1ee8,0)]:
            self.call(va,self.SELF,arg)
            target=struct.unpack('<Q',self.cpu.mem_read(self.SELF+0x38,8))[0]
            flags=list(self.cpu.mem_read(self.SELF+0x64,2));self.trace.append(['flags',bool(flags[0]),bool(flags[1]),'self'if target==0x10012000 else'target'])
        return self.trace.copy()
    def controller(self,c):
        self.mode='controller';self.trace=[];self.fraction=f32(c['fraction']);self.menu=c['menu'];self.children=c['children'];self.enabled=c['enabled'];self.playing=c['playing'];self.allocate=0x10019000
        self.owners={self.SELF:-1};self.transforms={self.SELF:0x10012000}
        self.ptr(self.SELF+0x20,0x10018200);self.i32(0x10018218,7);self.ptr(self.SELF+0x28,0x10018400);self.ptr(self.SELF+0x30,0x10018500);self.byte(self.SELF+0x38,False)
        self.i32(0x10015394,c['game_mode']);self.byte(0x100153e0,c['throne']);self.byte(0x10016520,c['spooky'])
        for i in range(7):
            actor=0x10017000+i*0x100;self.ptr(0x10018220+i*8,actor);self.owners[actor]=i;self.transforms[actor]=actor+0x80;self.byte(actor+0x64,True);self.byte(actor+0x65,i%2==0)
        self.call(0x36c2378,self.SELF)
        self.trace.append(['muted',bool(self.cpu.mem_read(self.SELF+0x38,1)[0])])
        for state in (0,4,5,6):
            self.cpu.reg_write(UC_ARM64_REG_X2,state);self.call(0x36c2694,self.SELF,0)
        self.call(0x36c27c8,self.SELF)
        return self.trace.copy()


SHIMS=r'''using System;using System.Collections.Generic;using System.Linq;using Jianghu;
public static class H{public static List<J> trace=new List<J>();public static void T(params object[] a)=>trace.Add(new J(a.Select(o=>o as J??new J(o)).ToList()));}
namespace UnityEngine {
public class Object{}public class Component:Object{public Transform transform;public GameObject gameObject;}
public class MonoBehaviour:Component{public int owner;protected void Invoke(string n,float d)=>H.T("invoke",owner,n,d);protected void InvokeRepeating(string n,float d,float r)=>H.T("repeat",owner,n,d,r);protected void CancelInvoke()=>H.T("cancel_all",owner);protected void CancelInvoke(string n)=>H.T("cancel",owner,n);}
public class GameObject:Object{public int owner;public void SetActive(bool a)=>H.T("active",owner,a);}
public class Transform:Component{public Vector3 position;public Vector3 localScale{set{H.T("scale",value.x,value.y,value.z);}}public Quaternion localRotation;public int childCount=7;public void SetPositionAndRotation(Vector3 p,Quaternion q){position=p;}}
public struct Vector3{public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}public static Vector3 LerpUnclamped(Vector3 a,Vector3 b,float t)=>a;}
public struct Quaternion{public Vector3 eulerAngles;public static Quaternion Euler(Vector3 v){H.T("rotation",v.x,v.y,v.z);return new Quaternion{eulerAngles=v};}}
public class Animator:Component{public int owner;public float speed{set{H.T("speed",owner,value);}}public void SetTrigger(int id)=>H.T("trigger",owner,id);public static int StringToHash(string s)=>s=="OnWalk"?10:s=="OnIdle"?11:12;}
public static class Time{public static float deltaTime;}
public static class Random{public static float fraction;public static int Range(int l,int h){H.T("int",l,h);return l+Math.Min(h-l-1,(int)((h-l)*fraction));}public static float Range(float l,float h){H.T("float",l,h);return l+(h-l)*fraction;}}
public static class Mathf{public static float Clamp01(float v)=>Math.Clamp(v,0,1);public static float LerpUnclamped(float a,float b,float t)=>a+(b-a)*t;}
public enum WeightedMode{}public struct Keyframe{public WeightedMode weightedMode;public float inWeight,outWeight;public Keyframe(float a,float b,float c,float d){weightedMode=0;inWeight=outWeight=0;}}
public class AnimationCurve{public AnimationCurve(params Keyframe[] k){}public float Evaluate(float t)=>t;}
public class AudioClip:Object{}public class AudioSource:Component{public bool enabled=true,isPlaying;float v;bool l;public bool mute{set{H.T("mute",value);}}public float volume{get=>v;set{v=value;H.T("volume",value);}}public float pitch{set{H.T("pitch",value);}}public bool loop{get=>l;set{l=value;H.T("loop",value);}}public AudioClip clip;public void Play(){H.T("play");isPlaying=true;}public void PlayOneShot(AudioClip c)=>H.T("play");}
}
public class Context{public bool SpookyMode;}
public class OriginalVisualCatalog{public T Get<T>(string id)where T:UnityEngine.Object=>new UnityEngine.AudioClip() as T;}
public static class OriginalStageActors{public static OriginalVisualCatalog Catalog=new OriginalVisualCatalog();public static Context Context=new Context();public static bool IsMainMenuState,IsThroneRoom;public static int GameMode;public static event Action<UnityEngine.Transform> LocalPlayerSpawned,LocalPlayerDespawned;public static event Action<int,int> NetworkedStateChanged;public static J Record(string s)=>J.O("data",J.O("audioClip",new J(new List<J>{new J(1)}),"volume",J.O("min",1,"max",1),"pitch",J.O("min",1,"max",1),"loop",true),"assets",J.O("audioClip[0]","clip"));public static UnityEngine.Transform InstanceRoot(UnityEngine.Transform t,string n)=>t;}
public static class OriginalEnvironmentSource{public static UnityEngine.Transform Node(OriginalVisualCatalog c,J r,string k,UnityEngine.Transform t,string n)=>null;public static T Component<T>(OriginalVisualCatalog c,J r,string k,UnityEngine.Transform t,string n)where T:UnityEngine.Component=>null;}
public static class OriginalStageTweenHost{public static void Add(Func<float,bool> step){}}
'''

PROGRAM=r'''using System;using System.IO;using System.Reflection;using System.Linq;using Jianghu;using UnityEngine;
class Program{
static int checks;static void Set(object o,string f,object v){var t=o.GetType();var field=t.GetField(f,BindingFlags.Instance|BindingFlags.NonPublic);if(field==null)field=t.GetField("<"+f+">k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic);field.SetValue(o,v);}
static OriginalBystanderMovement Actor(int i){var t=new Transform{position=new Vector3(1,2,3),gameObject=new GameObject{owner=i}};var p=new OriginalBystanderMovement{owner=i,transform=t,gameObject=t.gameObject};Set(p,"record",J.O());Set(p,"data",J.O("chanceToSitAroundOutOf10",5,"chanceToCheerOutOf10",5,"followingFrequencyRange",J.O("min",2,"max",5)));Set(p,"baseTransform",new Transform{position=new Vector3(4,5,6)});Set(p,"animator",new Animator{owner=i});Set(p,"originalRotation",new Vector3(0,0,0));Set(p,"flippedRotation",new Vector3(0,180,0));Set(p,"originalScale",new Vector3(1,1,1));Set(p,"flippedScale",new Vector3(1,1,-1));Set(p,"walkHash",10);Set(p,"idleHash",11);Set(p,"cheerHash",12);return p;}
static void Eq(J expected){if(expected.A.Count!=H.trace.Count)throw new Exception("Trace count "+expected+" actual "+new J(H.trace));for(int i=0;i<H.trace.Count;i++)for(int j=0;j<H.trace[i].A.Count;j++){var e=expected[i][j];var a=H.trace[i][j];if(e.value is string||e.value is bool){if(e.S!=a.S)throw new Exception("Trace order "+expected[i]+" actual "+H.trace[i]);}else if(Math.Abs(e.N-a.N)>1e-4)throw new Exception("Trace value "+expected[i]+" actual "+H.trace[i]);checks++;}}
static void Flags(OriginalBystanderMovement p,Transform t)=>H.T("flags",p.IsMoving,p.CanCheer,p.FollowTarget==t?"self":"target");
static int Main(string[]args){var root=J.Parse(File.ReadAllText(args[0]));foreach(var c in root["movement"].A){var p=Actor(0);var t=p.transform;Set(p,"data",J.O("chanceToSitAroundOutOf10",c["sit"].I,"chanceToCheerOutOf10",c["cheer"].I,"followingFrequencyRange",J.O("min",2,"max",5)));UnityEngine.Random.fraction=(float)c["fraction"].N;H.trace.Clear();p.SetFollowTarget(new Transform{position=new Vector3((float)c["target"].N,0,0)});Flags(p,t);p.FollowingTick();Flags(p,t);p.FollowingTick();Flags(p,t);p.AnimationTick();Flags(p,t);p.SetCheer();Flags(p,t);p.SetIdle();Flags(p,t);p.StartCheeringAnimation();Flags(p,t);p.SafelyDisable();Flags(p,t);p.AnimationTick();Flags(p,t);Eq(c["trace"]);}foreach(var c in root["controller"].A){var actors=Enumerable.Range(0,7).Select(Actor).ToArray();foreach(var p in actors){Set(p,"CanCheer",p.owner%2==0);Set(p,"IsMoving",true);}var source=new AudioSource{enabled=c["enabled"].B,isPlaying=c["playing"].B};var host=new OriginalBystandersController{owner=-1,transform=new Transform{childCount=c["children"].I}};Set(host,"record",J.O("assets",J.O("BystandersCheerAudioObject","audio")));Set(host,"actors",actors);Set(host,"source",source);OriginalStageActors.GameMode=c["game_mode"].I;OriginalStageActors.IsMainMenuState=c["menu"].B;OriginalStageActors.IsThroneRoom=c["throne"].B;OriginalStageActors.Context.SpookyMode=c["spooky"].B;UnityEngine.Random.fraction=(float)c["fraction"].N;H.trace.Clear();host.ApplyInitialContext();H.T("muted",host.Muted);foreach(int state in new[]{0,4,5,6})host.ApplyGameState(0,state);host.BystandersEndCheer();Eq(c["trace"]);}Console.WriteLine("{\"native_stage_movement_checks\":"+checks+",\"failures\":0}");return 0;}}
'''


def main():
    OUT.mkdir(parents=True,exist_ok=True);native=NativeMovement();movement=[];controller=[]
    for fraction,sit,cheer,target in itertools.product((0,.5,.99),(0,5,10),(0,5,10),(-7,7)):
        c=dict(fraction=fraction,sit=sit,cheer=cheer,target=target);movement.append(dict(**c,trace=native.movement(c)))
    for menu,game_mode,throne,spooky,enabled,playing,children in itertools.product((False,True),(0,2),(False,True),(False,True),(False,True),(False,True),(0,7)):
        c=dict(menu=menu,game_mode=game_mode,throne=throne,spooky=spooky,enabled=enabled,playing=playing,children=children,fraction=.99);controller.append(dict(**c,trace=native.controller(c)))
    oracle=OUT/'native-stage-movement-oracle.json';oracle.write_text(json.dumps(dict(movement=movement,controller=controller),indent=2),encoding='utf-8')
    (OUT/'Shims.cs').write_text(SHIMS);(OUT/'Comparison.cs').write_text(PROGRAM)
    # Replace only the bounded engine backend calls with observation hooks.
    a=(ROOT/'recovery/bloody/Runtime/OriginalBystanderMovement.cs').read_text().replace('StartMove(new Vector3(x,y,z));','H.T("move",x,y,z,2f,false);H.T("curve");StartMove(new Vector3(x,y,z));')
    b=(ROOT/'recovery/bloody/Runtime/OriginalBystandersController.cs').read_text().replace('if(!source.enabled)return;','H.T("audio");if(!source.enabled)return;').replace('source.enabled=true;source.volume=0;','source.enabled=true;H.T("enabled",true);source.volume=0;').replace('var fade=new VolumeTween','H.T("fade",Mathf.Clamp01(value),duration);var fade=new VolumeTween')
    (OUT/'OriginalBystanderMovement.cs').write_text(a);(OUT/'OriginalBystandersController.cs').write_text(b)
    runtime=next((UNITY/'NetCoreRuntime/shared/Microsoft.NETCore.App').iterdir());target=OUT/'CompareStageMovement.dll'
    args=['-nologo','-target:exe','-nostdlib+','-nowarn:0067',f'-out:{target}']+[f'-r:{p}'for p in runtime.glob('*.dll')if(p.name.startswith('System.')and'.Native'not in p.name)or p.name in ('mscorlib.dll','netstandard.dll')]
    args += [str(OUT/name) for name in ('Shims.cs','Comparison.cs','OriginalBystanderMovement.cs','OriginalBystandersController.cs')]+[str(ROOT/'unity/JianghuUnity/Assets/Scripts/Json.cs')]
    response=OUT/'compile.rsp';response.write_text('\n'.join('"'+arg+'"'for arg in args),encoding='utf-8-sig')
    result=subprocess.run([str(UNITY/'NetCoreRuntime/dotnet.exe'),str(UNITY/'DotNetSdkRoslyn/csc.dll'),'@'+str(response)],cwd=ROOT,text=True,capture_output=True)
    (OUT/'compile.txt').write_text(result.stdout+result.stderr)
    if result.returncode:print(result.stdout+result.stderr);raise SystemExit(result.returncode)
    target.with_suffix('.runtimeconfig.json').write_text(json.dumps(dict(runtimeOptions=dict(tfm='net8.0',framework=dict(name='Microsoft.NETCore.App',version=runtime.name)))))
    result=subprocess.run([str(UNITY/'NetCoreRuntime/dotnet.exe'),str(target),str(oracle)],cwd=ROOT,text=True,capture_output=True)
    (OUT/'comparison.txt').write_text(result.stdout+result.stderr);print(result.stdout,end='');print(result.stderr,end='')
    if result.returncode:raise SystemExit(result.returncode)
    source_names=('OriginalBystanderMovement.cs','OriginalBystandersController.cs','OriginalStageTweenHost.cs','OriginalStageActors.cs','Editor/OriginalRecoveryBuild.StageMovement.cs')
    (OUT/'source-audit.json').write_text(json.dumps(dict(
        source_sha256={name:hashlib.sha256((ROOT/'recovery/bloody/Runtime'/name).read_bytes()).hexdigest() for name in source_names},
        source_native=dict(movement_start='0x36c1994',spawn_handler='0x36c1b44 -> 0x36c1b70',despawn_handler='0x36c1d4c -> 0x36c1d50',controller_start='0x36c22f0',controller_initial='0x36c2378',networked_state='0x36c2694',cheer='0x36c26d0',end_cheer='0x36c27c8',source_audio='0x3411e0c'),
        verified_cases=dict(movement=len(movement),controller=len(controller)),comparison=json.loads(result.stdout.strip()),
        source_tween_settings=dict(update_type=0,independent_update=False,time_scale=1,default_ease=6,safe_mode=True),
        integration_order=['Explicit GameMode/IsMainMenuState Context before stage Instantiate','Source prefab Awake/OnEnable','Source controller Start: initial context, then subscribe NGC state event','Source movement Start: subscribe local spawn/despawn','WaitForStart before Create player','NotifyLocalPlayerSpawned immediately after Create, before named config','ApplyGameState(prev,current) on networked-state publication','NotifyLocalPlayerDespawned before disposing player'],
        limits=['Original ARM executes intact between bounded Unity/RNG/tween/audio hooks.','bg.PlaySound engine boundary is a bounded source-order hook; audible output is not tested.','Tests do not establish Unity RNG implementation equivalence or actual frame scheduling.','Real Unity Build18 must verify MonoScript serialization, Start readiness, visuals, world motion and source audio bindings.','Tween interpolation uses the original Unity curve and source settings, but full DOTween manager update-order parity is not established.'],
        no_android_operation=True,no_unity_launch=True,no_prefab_mutation=True),indent=2)+'\n',encoding='utf-8')


if __name__=='__main__':main()
