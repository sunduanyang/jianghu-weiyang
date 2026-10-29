"""Execute original transition coroutine instructions vs actual sequence C#.

Runner/ad/engine calls are bounded observations. Arbitrary iterator states are
fixtures in an isolated CPU/CLR, not injected state in a running game.
"""
import json
from pathlib import Path
import struct
import subprocess
from verify_bloody_ai_native import NativeAI
from unicorn.arm64_const import UC_ARM64_REG_X0,UC_ARM64_REG_X1,UC_ARM64_REG_LR,UC_ARM64_REG_PC

ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'output/bloody-full-port-20261028/ai-check/arena-transition';UNITY=Path('D:/Unity/Editors/6000.4.0f1/Editor/Data')


class NativeTransition(NativeAI):
    CONTROLLER=0x10012000;STATICS=0x10017300;HQ=0x10017800;NGC=0x10017d00
    def __init__(self):
        super().__init__()
        for cell in(0x720d930,0x720d1d8,0x7212780,0x7212878,0x720cfd8):self.ptr(cell,0x10015000)
        for cell,h,st in((0x7212df8,0x10017000,self.STATICS),(0x7212238,0x10017500,self.HQ),(0x7212868,0x10017a00,self.NGC),(0x72179d8,0x1001a000,0x1001a300),(0x7212460,0x1001a500,0x1001a800),(0x72122c8,0x1001b000,0x1001b300)):
            self.ptr(cell,h);self.ptr(h,h+0x100);self.i32(h+0x100+0xe4,1);self.ptr(h+0x100+0xb8,st)
        self.ptr(0x1001a808,0x1001ab00)
        self.ptr(0x1001b300,0x1001b500)
        for index,offset in enumerate((0x10,0x18,0x20,0x28,0x30)):
            p=0x10018000+index*0x100;self.ptr(self.STATICS+offset,p);self.ptr(p+0x18,0x10000200+index*4)
        self.ptr(self.CONTROLLER+0x48,0x10013000);self.ptr(self.CONTROLLER+0x50,0x10013100)
        self.cpu.mem_write(self.CONTROLLER+0x58,struct.pack('<ff',15,.35))
    def ptr(self,a,v):self.cpu.mem_write(a,struct.pack('<Q',v))
    def i32(self,a,v):self.cpu.mem_write(a,struct.pack('<I',v&0xffffffff))
    def _hook(self,cpu,a,size,data):
        x0=cpu.reg_read(UC_ARM64_REG_X0);x1=cpu.reg_read(UC_ARM64_REG_X1)
        if a in(0x6acbd7c,0x6aca1c8):cpu.reg_write(UC_ARM64_REG_X0,int(x0==x1)if a==0x6acbd7c else int(x0!=x1))
        elif a==0x6a734f8:cpu.reg_write(UC_ARM64_REG_X0,int(self.args['batch']))
        elif a==0x316bb40:
            self.alloc+=0x80;cpu.reg_write(UC_ARM64_REG_X0,self.alloc)
        elif a==0x6ad36b0:self.objects[x0]=dict(kind='real_wait',seconds=self.sf(0))
        elif a==0x6ad37b0:self.objects[x0]=dict(kind='until_interstitial',ready=not self.args['interstitial'])
        elif a in(0x56ac338,0x316b7cc):pass
        elif a==0x366a28c:cpu.reg_write(UC_ARM64_REG_X0,int(self.args['runner']))
        elif a==0x366bfb8:self.calls.append(dict(kind='teardown'));cpu.reg_write(UC_ARM64_REG_X0,0x10019000)
        elif a==0x366b124:self.calls.append(dict(kind='spawn',mode=x1))
        elif a==0x3689c18:self.calls.append(dict(kind='objects',domain='menu'if x0==0x10013000 else'arena',active=bool(x1)))
        elif a==0x3689f94:self.objects[0x10019100]=dict(kind='menu',state=x1);cpu.reg_write(UC_ARM64_REG_X0,0x10019100)
        elif a==0x3640dc4:self.calls.append(dict(kind='local_state',state=x1))
        elif a==0x6ad6f60:self.setf(0,self.args['delta'])
        elif 0x10000200<=a<=0x10000210:
            index=(a-0x10000200)//4;event=dict(kind=['switching','switched','menu_event','arena_event','context_event'][index],in_arena=bool(self.cpu.mem_read(self.STATICS+8,1)[0]),transitioning=bool(self.cpu.mem_read(self.STATICS+9,1)[0]));
            if index==4:event['value']=bool(x1)
            self.calls.append(event)
        elif a in(0x316bb50,0x316bb58):raise RuntimeError('Native source throw '+hex(a))
        else:return super()._hook(cpu,a,size,data)
        cpu.reg_write(UC_ARM64_REG_PC,cpu.reg_read(UC_ARM64_REG_LR))
    def step(self,kind,state,runner,ngc,interstitial,batch,elapsed,delta,in_arena,transitioning):
        self.args=locals();self.calls=[];self.objects={0:dict(kind='null'),0x10019000:dict(kind='teardown')};self.alloc=0x1001c000
        self.cpu.mem_write(self.STATICS+8,bytes([int(in_arena),int(transitioning)]));self.ptr(self.NGC,0x10018c00 if ngc else 0);self.cpu.mem_write(self.HQ+0x150,bytes([int(interstitial)]));self.ptr(0x1001a308,0)
        self.i32(self.SELF+0x10,state);self.ptr(self.SELF+0x18,0);self.ptr(self.SELF+0x20,self.CONTROLLER);self.i32(self.SELF+0x28,2 if kind=='arena'else 7);self.cpu.mem_write(self.SELF+0x2c,struct.pack('<f',elapsed))
        self.call(0x368a3f8 if kind=='arena'else 0x368a9a8,self.SELF)
        return dict(result=bool(self.cpu.reg_read(UC_ARM64_REG_X0)),state=struct.unpack('<i',self.cpu.mem_read(self.SELF+0x10,4))[0],current=self.objects[struct.unpack('<Q',self.cpu.mem_read(self.SELF+0x18,8))[0]],elapsed=struct.unpack('<f',self.cpu.mem_read(self.SELF+0x2c,4))[0]if kind=='arena'else 0,
                    in_arena=bool(self.cpu.mem_read(self.STATICS+8,1)[0]),transitioning=bool(self.cpu.mem_read(self.STATICS+9,1)[0]),calls=self.calls)


SHIMS=r'''
using System;namespace UnityEngine {public static class Application {public static bool isBatchMode;}public static class Time {public static float unscaledDeltaTime;}public class WaitForSecondsRealtime {public float waitTime;public WaitForSecondsRealtime(float t){waitTime=t;}}public class WaitUntil {public Func<bool> predicate;public WaitUntil(Func<bool> f){predicate=f;}}}
'''
PROGRAM=r'''
using System;using System.IO;using System.Reflection;using System.Collections;using System.Collections.Generic;using System.Linq;using Jianghu;using UnityEngine;
class Empty:IEnumerator {public object Current=>null;public bool MoveNext()=>false;public void Reset(){}}
class Program {
 static int checks;static void Check(bool b,string m){checks++;if(!b)throw new Exception(m);}
 static FieldInfo Field(object o,string name)=>o.GetType().GetField(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
 static bool Equal(J a,J b){if(a.value is Dictionary<string,J>){if(a.D.Count!=b.D.Count)return false;return a.D.All(p=>Equal(p.Value,b[p.Key]));}if(a.value is List<J>){return a.A.Count==b.A.Count&&a.A.Zip(b.A,(x,y)=>Equal(x,y)).All(v=>v);}if(a.value is bool||a.value is string)return a.S==b.S;return Math.Abs(a.N-b.N)<.000002;}
 static J Yield(object current){if(current==null)return J.O("kind","null");if(current is WaitForSecondsRealtime w)return J.O("kind","real_wait","seconds",w.waitTime);if(current is WaitUntil u)return J.O("kind","until_interstitial","ready",u.predicate());if(current is Empty)return J.O("kind","teardown");return J.O("kind","menu","state",(int)Field(current,"localState").GetValue(current));}
 static void Main(string[]args){var root=J.Parse(File.ReadAllText(args[0]));var source=J.O("cover_delay",.35,"network_controller_spawn_timeout",15);foreach(var c in root["cases"].A){var p=c["arguments"];var calls=new List<J>();OriginalArenaTransitionSequence seq=null;var ctx=new OriginalArenaTransitionContext {RunnerActive=()=>p["runner"].B,NetworkControllerExists=()=>p["ngc"].B,InterstitialPlaying=()=>p["interstitial"].B,BatchMode=()=>p["batch"].B,TearDownRunner=()=>{calls.Add(J.O("kind","teardown"));return new Empty();},SpawnRunner=mode=>calls.Add(J.O("kind","spawn","mode",mode)),RequestLocalState=state=>calls.Add(J.O("kind","local_state","state",state)),SetContextObjects=arena=>{calls.Add(J.O("kind","objects","domain","menu","active",!arena));calls.Add(J.O("kind","objects","domain","arena","active",arena));}};
 seq=new OriginalArenaTransitionSequence(source,ctx,p["in_arena"].B);typeof(OriginalArenaTransitionSequence).GetProperty("IsTransitioning").SetValue(seq,p["transitioning"].B);
 Action<string> Log=kind=>calls.Add(J.O("kind",kind,"in_arena",seq.InArena,"transitioning",seq.IsTransitioning));seq.OnSwitching+=()=>Log("switching");seq.OnSwitched+=()=>Log("switched");seq.OnSwitchedToArena+=()=>Log("arena_event");seq.OnSwitchedToMenu+=()=>Log("menu_event");seq.OnContextChanged+=value=>calls.Add(J.O("kind","context_event","in_arena",seq.InArena,"transitioning",seq.IsTransitioning,"value",value));
 IEnumerator iter=p["kind"].S=="arena"?seq.CreateToArena(2):seq.CreateToMenu(7);Field(iter,"<>1__state").SetValue(iter,p["state"].I);var elapsed=iter.GetType().GetFields(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).FirstOrDefault(f=>f.Name.StartsWith("<elapsed>"));if(elapsed!=null)elapsed.SetValue(iter,(float)p["elapsed"].N);Time.unscaledDeltaTime=(float)p["delta"].N;bool result=iter.MoveNext();var e=c["expected"];string label=p.ToString();Check(result==e["result"].B,"native yield return "+label);Check((int)Field(iter,"<>1__state").GetValue(iter)==e["state"].I,"native coroutine state "+label);Check(Equal(Yield(iter.Current),e["current"]),"native yielded object "+label+" got="+Yield(iter.Current)+" wanted="+e["current"]);Check(Math.Abs((elapsed==null?0:(float)elapsed.GetValue(iter))-e["elapsed"].N)<.000002,"native unscaled timeout cache "+label);Check(seq.InArena==e["in_arena"].B&&seq.IsTransitioning==e["transitioning"].B,"native flags "+label);Check(Equal(new J(calls),e["calls"]),"native callback order "+label+" got="+new J(calls)+" wanted="+e["calls"]);
 }Console.WriteLine("{\"native_arena_transition_checks\":"+checks+",\"failures\":0}");}
}
'''


def main():
    OUT.mkdir(exist_ok=True,parents=True);n=NativeTransition();cases=[]
    for kind in('arena','menu'):
        for state in(range(-1,14)if kind=='arena'else range(-1,4)):
            for runner in(False,True):
                for ngc in(False,True):
                    for batch in(False,True):
                        for elapsed in((0,14.99,15,16)if kind=='arena'and state in(5,6)else(0,)):
                            args=dict(kind=kind,state=state,runner=runner,ngc=ngc,interstitial=bool(state%2),batch=batch,elapsed=elapsed,delta=.25,in_arena=bool(state%2),transitioning=state!=0)
                            cases.append(dict(arguments=args,expected=n.step(**args)))
    oracle=OUT/'native-transition-oracle.json';oracle.write_text(json.dumps(dict(cases=cases),indent=2),encoding='utf-8');(OUT/'Shims.cs').write_text(SHIMS);(OUT/'Comparison.cs').write_text(PROGRAM)
    runtime=next((UNITY/'NetCoreRuntime/shared/Microsoft.NETCore.App').iterdir());target=OUT/'CompareArenaTransition.dll'
    cmd=[str(UNITY/'NetCoreRuntime/dotnet.exe'),str(UNITY/'DotNetSdkRoslyn/csc.dll'),'-nologo','-target:exe','-nostdlib+',f'-out:{target}']+[f'-r:{p}'for p in runtime.glob('*.dll')if(p.name.startswith('System.')and'.Native'not in p.name)or p.name in('mscorlib.dll','netstandard.dll')]+[str(OUT/'Shims.cs'),str(OUT/'Comparison.cs'),str(ROOT/'recovery/bloody/Runtime/OriginalArenaTransitionSequence.cs'),str(ROOT/'unity/JianghuUnity/Assets/Scripts/Json.cs')]
    compiled=subprocess.run(cmd,cwd=ROOT,text=True,capture_output=True);(OUT/'compile.txt').write_text(compiled.stdout+compiled.stderr,encoding='utf-8')
    if compiled.returncode:print(compiled.stdout+compiled.stderr);raise SystemExit(compiled.returncode)
    target.with_suffix('.runtimeconfig.json').write_text(json.dumps(dict(runtimeOptions=dict(tfm='net8.0',framework=dict(name='Microsoft.NETCore.App',version=runtime.name)))))
    result=subprocess.run([str(UNITY/'NetCoreRuntime/dotnet.exe'),str(target),str(oracle)],cwd=ROOT,text=True,capture_output=True);(OUT/'comparison.txt').write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout,end='');print(result.stderr,end='')
    if result.returncode:raise SystemExit(result.returncode)


if __name__=='__main__':main()
