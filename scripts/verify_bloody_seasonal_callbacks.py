"""Run native Christmas/Spooky/Reverb methods with bounded Unity hooks."""
import json,struct,subprocess
from pathlib import Path
from verify_bloody_ai_native import NativeAI,f32
from verify_bloody_environment import UNITY
from unicorn.arm64_const import *
ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'output/bloody-full-port-20261028/environment-check/specials'


class NativeSeasonal(NativeAI):
    BATS=0x10012000;HOWLS=0x10012100
    def __init__(self):
        self.trace=[];super().__init__();self.fraction=.5
        self.ptr(0x72122c0,0x1001a000);self.ptr(0x1001a000,0x1001a100);self.ptr(0x1001a1b8,0x1001a300);self.ptr(0x1001a300,0x1001a500);self.ptr(0x1001a520,0x1001a700)
        self.ptr(0x720e0d8,0x1001a000);self.ptr(0x72120f8,0x1001a000)
        self.owners={0x10015000:'bats',0x10015100:'howls'}
    def ptr(self,a,v):self.cpu.mem_write(a,struct.pack('<Q',v))
    def i32(self,a,v):self.cpu.mem_write(a,struct.pack('<I',v&0xffffffff))
    def _hook(self,cpu,a,size,data):
        x0=cpu.reg_read(UC_ARM64_REG_X0);x1=cpu.reg_read(UC_ARM64_REG_X1)
        if a==0x316b7cc:pass
        elif a==0x6aca694:self.trace.append(['active',x0-0x10019000,bool(x1)])
        elif a==0x6accf58:self.trace.append(['stop_all'])
        elif a==0x3412b4c:cpu.reg_write(UC_ARM64_REG_X0,self.BATS)
        elif a==0x3412bb8:cpu.reg_write(UC_ARM64_REG_X0,self.HOWLS)
        elif a==0x6acc9e8:self.trace.append(['start','bats'if x1==self.BATS else'howls'])
        elif a==0x6abfa9c:
            lo,hi=self.sf(0),self.sf(1);self.trace.append(['float',lo,hi]);self.setf(0,f32(lo+f32(f32(hi-lo)*self.fraction)))
        elif a==0x6abfadc:
            self.trace.append(['int',x0,x1]);cpu.reg_write(UC_ARM64_REG_X0,x0+min(x1-x0-1,int((x1-x0)*self.fraction)))
        elif a==0x316bb40:cpu.reg_write(UC_ARM64_REG_X0,0x10012200)
        elif a==0x6ad35b4:self.trace.append(['wait',self.sf(0)])
        elif a==0x6b52564:self.trace.append(['particles',True])
        elif a==0x6a64e84:self.trace.append(['pitch',self.owners[x0],self.sf(0)])
        elif a==0x6a6563c:self.trace.append(['stop_audio',self.owners[x0]])
        elif a==0x6a65560:self.trace.append(['one_shot',self.owners[x0],x1-0x10019000,self.sf(0)])
        elif a==0x6a680b4:cpu.reg_write(UC_ARM64_REG_X0,0x10013000)
        elif a==0x35d8e64:cpu.reg_write(UC_ARM64_REG_X0,0x10013100)
        elif a==0x6a67c3c:self.trace.append(['reverb',self.sf(0)])
        elif a in (0x316bb50,0x316bb58):raise RuntimeError('Original seasonal callback threw '+hex(a))
        else:return super()._hook(cpu,a,size,data)
        cpu.reg_write(UC_ARM64_REG_PC,cpu.reg_read(UC_ARM64_REG_LR))
    def christmas(self,active,christmas):
        self.trace=[];self.cpu.mem_write(self.SELF+0x20,bytes([active]));self.cpu.mem_write(0x1001a721,bytes([christmas]));self.ptr(self.SELF+0x28,0x10018000);self.i32(0x10018018,3)
        for i in range(3):self.ptr(0x10018020+i*8,0x10019000+i)
        self.call(0x34448f0,self.SELF);return self.trace.copy()
    def refresh(self,hour,spooky):
        self.trace=[];self.cpu.reg_write(UC_ARM64_REG_X2,int(spooky));self.call(0x3412adc,self.SELF,hour);return self.trace.copy()
    def reverb(self,value):
        self.trace=[];self.ptr(self.SELF+0x20,0x10014000);self.cpu.mem_write(self.SELF+0x28,struct.pack('<f',value));self.call(0x343d570,self.SELF);return self.trace.copy()
    def loop(self,kind,fraction):
        self.trace=[];self.fraction=f32(fraction);self.cpu.mem_write(self.SELF+0x20,struct.pack('<4f',5,30,20,40));self.ptr(self.SELF+0x30,0x10013000);self.ptr(self.SELF+0x38,0x10015200);self.ptr(self.SELF+0x40,0x10014000)
        for i,at in enumerate((0x10013000,0x10014000)):
            audio=0x10016000+i*0x100;array=0x10018000+i*0x100;source=0x10015100-i*0x100
            self.ptr(at+0x20,audio);self.ptr(at+0x30,source);self.ptr(audio+0x18,array);self.cpu.mem_write(audio+0x24,struct.pack('<4f',.3,.6,.8,1.2));self.i32(array+0x18,3)
            for j in range(3):self.ptr(array+0x20+j*8,0x10019000+j)
        coroutine=self.BATS if kind=='bats'else self.HOWLS;self.ptr(coroutine+0x20,self.SELF);self.i32(coroutine+0x10,0)
        for _ in range(3):self.call(0x3412ce0 if kind=='bats'else 0x3412e0c,coroutine)
        return self.trace.copy()


SHIMS=r'''using System;using System.Collections;using System.Collections.Generic;using System.Linq;using Jianghu;
public static class H{public static List<J> trace=new List<J>();public static void T(params object[] args)=>trace.Add(new J(args.Select(o=>o as J??new J(o)).ToList()));}
namespace UnityEngine {public class Object{}public class Component:Object{public Transform transform;public GameObject gameObject;}public class MonoBehaviour:Component{protected void StopAllCoroutines()=>H.T("stop_all");protected void StartCoroutine(IEnumerator e)=>H.T("start",e.GetType().Name.Contains("Bats")?"bats":"howls");}public class Transform:Component{public string name;}public class GameObject:Object{public int id;public void SetActive(bool a)=>H.T("active",id,a);}public class AudioClip:Object{public int id;}public class AudioSource:Component{public string id;public float pitch{set{H.T("pitch",id,value);}}public void Stop()=>H.T("stop_audio",id);public void PlayOneShot(AudioClip clip,float volume)=>H.T("one_shot",id,clip.id,volume);}public class ParticleSystem:Component{public void Play(bool children)=>H.T("particles",children);}public class WaitForSeconds{public WaitForSeconds(float v){H.T("wait",v);}}public static class Random{public static float fraction;public static float Range(float l,float h){H.T("float",l,h);return l+(h-l)*fraction;}public static int Range(int l,int h){H.T("int",l,h);return l+Math.Min(h-l-1,(int)((h-l)*fraction));}}}
namespace UnityEngine.Audio{public class AudioMixerGroup:UnityEngine.Object{public AudioMixer audioMixer=new AudioMixer();}public class AudioMixer{public bool SetFloat(string name,float v){if(name!="SFXReverb")throw new Exception("Source parameter mismatch");H.T("reverb",v);return true;}}}
public class OriginalVisualCatalog{public J Data;public T Get<T>(string id)where T:UnityEngine.Object=>new UnityEngine.AudioClip{id=int.Parse(id)}as T;}
public static class OriginalEnvironmentSource{public static string Identity(J row)=>"";}
public class Context{public bool ChristmasMode;}public static class OriginalStageActors{public static Context Context=new Context();public static UnityEngine.Transform InstanceRoot(UnityEngine.Transform t,string s)=>t;}
public static class OriginalEnvironmentSpecialSource{public static J Data,Row;public static J Record(string s)=>Row;public static UnityEngine.Transform Node(J row,string field,UnityEngine.Transform root,string name)=>new UnityEngine.Transform{gameObject=new UnityEngine.GameObject{id=int.Parse(field.Split('[')[1].TrimEnd(']'))}};public static T Component<T>(J row,string field,UnityEngine.Transform root,string name)where T:UnityEngine.Component=>null;}
'''
PROGRAM=r'''using System;using System.IO;using System.Collections;using System.Reflection;using System.Collections.Generic;using Jianghu;using UnityEngine;
class Program{static int checks;static void Set(object o,string f,object v)=>o.GetType().GetField(f,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(o,v);static object Call(object o,string n)=>o.GetType().GetMethod(n,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(o,null);static J List(int n){var a=new List<J>();for(int i=0;i<n;i++)a.Add(new J(i));return new J(a);}static void Eq(J e){if(e.A.Count!=H.trace.Count)throw new Exception("Seasonal trace length "+e+" actual "+new J(H.trace));for(int i=0;i<H.trace.Count;i++)for(int j=0;j<H.trace[i].A.Count;j++){var a=H.trace[i][j];if(e[i][j].value is string||e[i][j].value is bool){if(a.S!=e[i][j].S)throw new Exception("Seasonal trace order "+e[i]+" "+H.trace[i]);}else if(Math.Abs(a.N-e[i][j].N)>1e-5)throw new Exception("Seasonal trace value "+e[i]+" "+H.trace[i]);checks++;}}
static int Main(string[]args){var d=J.Parse(File.ReadAllText(args[0]));foreach(var c in d["christmas"].A){OriginalEnvironmentSpecialSource.Row=J.O("data",J.O("activeOnChristmas",c["active"].B,"gameObjects",List(3)));OriginalStageActors.Context.ChristmasMode=c["christmas"].B;var p=new OriginalStageChristmasActivation{transform=new Transform()};H.trace.Clear();Call(p,"Start");Eq(c["trace"]);}var extras=new OriginalSpookyExtras();Set(extras,"initialized",true);foreach(var c in d["refresh"].A){H.trace.Clear();extras.RefreshSpookyCoroutines(c["hour"].I,c["spooky"].B);Eq(c["trace"]);}foreach(var c in d["reverb"].A){var p=new OriginalStageReverb{SFXMixerGroup=new UnityEngine.Audio.AudioMixerGroup(),ReverbValue=(float)c["value"].N};H.trace.Clear();Call(p,"Awake");Eq(c["trace"]);}foreach(var c in d["loops"].A){var p=new OriginalSpookyExtras();Set(p,"catalog",new OriginalVisualCatalog());Set(p,"data",J.O("DelayBetweenBats",J.O("min",20,"max",40),"DelayBetweenHowls",J.O("min",5,"max",30)));var row=J.O("data",J.O("pitch",J.O("min",.8,"max",1.2),"volume",J.O("min",.3,"max",.6),"audioClip",List(3)),"assets",J.O("audioClip[0]","0","audioClip[1]","1","audioClip[2]","2"));Set(p,"batSfx",row);Set(p,"howlSfx",row);Set(p,"batAudio",new AudioSource{id="bats"});Set(p,"howlAudio",new AudioSource{id="howls"});Set(p,"bats",new ParticleSystem());UnityEngine.Random.fraction=(float)c["fraction"].N;var e=(IEnumerator)Call(p,c["kind"].S=="bats"?"Bats":"Howls");H.trace.Clear();for(int i=0;i<3;i++)e.MoveNext();Eq(c["trace"]);}Console.WriteLine("{\"native_seasonal_callback_checks\":"+checks+",\"failures\":0}");return 0;}}
'''


def main():
    OUT.mkdir(parents=True,exist_ok=True);n=NativeSeasonal();cases=dict(christmas=[],refresh=[],reverb=[],loops=[])
    for a in (False,True):
        for c in (False,True):cases['christmas'].append(dict(active=a,christmas=c,trace=n.christmas(a,c)))
    for h in range(-2,258):
        for spooky in (False,True):cases['refresh'].append(dict(hour=h,spooky=spooky,trace=n.refresh(h,spooky)))
    for value in (-5000,-1000,-500,0,120):cases['reverb'].append(dict(value=value,trace=n.reverb(value)))
    for kind in ('bats','howls'):
        for fraction in (0,.5,.99):cases['loops'].append(dict(kind=kind,fraction=fraction,trace=n.loop(kind,fraction)))
    oracle=OUT/'native-seasonal-callback-oracle.json';oracle.write_text(json.dumps(cases,indent=2),encoding='utf-8');(OUT/'SeasonalShims.cs').write_text(SHIMS);(OUT/'SeasonalComparison.cs').write_text(PROGRAM)
    runtime=next((UNITY/'NetCoreRuntime/shared/Microsoft.NETCore.App').iterdir());target=OUT/'CompareSeasonalCallbacks.dll'
    args=['-nologo','-target:exe','-nostdlib+',f'-out:{target}']+[f'-r:{p}'for p in runtime.glob('*.dll')if(p.name.startswith('System.')and'.Native'not in p.name)or p.name in ('mscorlib.dll','netstandard.dll')]
    args += [str(OUT/'SeasonalShims.cs'),str(OUT/'SeasonalComparison.cs')]+[str(ROOT/'recovery/bloody/Runtime'/name)for name in ('OriginalSpookyExtras.cs','OriginalStageChristmasActivation.cs','OriginalStageReverb.cs')]+[str(ROOT/'unity/JianghuUnity/Assets/Scripts/Json.cs')]
    response=OUT/'seasonal-compile.rsp';response.write_text('\n'.join('"'+arg+'"'for arg in args),encoding='utf-8-sig')
    result=subprocess.run([str(UNITY/'NetCoreRuntime/dotnet.exe'),str(UNITY/'DotNetSdkRoslyn/csc.dll'),'@'+str(response)],cwd=ROOT,text=True,encoding='utf-8',errors='replace',capture_output=True);(OUT/'seasonal-compile.txt').write_text(result.stdout+result.stderr,encoding='utf-8')
    if result.returncode:print(result.stdout+result.stderr);raise SystemExit(result.returncode)
    target.with_suffix('.runtimeconfig.json').write_text(json.dumps(dict(runtimeOptions=dict(tfm='net8.0',framework=dict(name='Microsoft.NETCore.App',version=runtime.name)))))
    result=subprocess.run([str(UNITY/'NetCoreRuntime/dotnet.exe'),str(target),str(oracle)],cwd=ROOT,text=True,capture_output=True);(OUT/'seasonal-comparison.txt').write_text(result.stdout+result.stderr);print(result.stdout,end='');print(result.stderr,end='')
    if result.returncode:raise SystemExit(result.returncode)


if __name__=='__main__':main()
