"""Compare actual CountdownView Update outputs/tweens against ARM64."""
import json
from pathlib import Path
import struct
import subprocess
from verify_bloody_ai_native import NativeAI
from unicorn.arm64_const import UC_ARM64_REG_X0,UC_ARM64_REG_X1,UC_ARM64_REG_LR,UC_ARM64_REG_PC

ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'output/bloody-full-port-20261028/ai-check/ui';UNITY=Path('D:/Unity/Editors/6000.4.0f1/Editor/Data')


class NativeCountdown(NativeAI):
    COUNTDOWN=0x10013000;FIGHT=0x10014000
    def __init__(self):
        super().__init__()
        for a in(0x720d1d8,0x7212868,0x7211ae0,0x72124b8,0x7217a20,0x7217a28,0x7217a30,0x7217a38,0x7217a40):self.cpu.mem_write(a,struct.pack('<Q',0x10015000))
        self.cpu.mem_write(0x10015300,struct.pack('<Q',self.PROFILE));self.cpu.mem_write(self.PROFILE+0x38,struct.pack('<Q',1))
        self.cpu.mem_write(self.SELF+0x20,struct.pack('<4Q',self.COUNTDOWN,self.FIGHT,1,2));self.cpu.mem_write(self.SELF+0x40,struct.pack('<2f',7,12))
        for ptr in(self.COUNTDOWN,self.FIGHT):self.cpu.mem_write(ptr,struct.pack('<Q',0x10016000))
        self.cpu.mem_write(0x10016000+0x558,struct.pack('<Q',0x1001f000))
    def _hook(self,cpu,a,size,data):
        if a==0x6acbd7c:cpu.reg_write(UC_ARM64_REG_X0,int(cpu.reg_read(UC_ARM64_REG_X0)==cpu.reg_read(UC_ARM64_REG_X1)))
        elif a==0x3647b30:self.setf(0,self.remaining)
        elif a==0x6ac65ac:pass
        elif a==0x6aca694:self.state['countdown'if cpu.reg_read(UC_ARM64_REG_X0)==self.COUNTDOWN else'fight']['active']=bool(cpu.reg_read(UC_ARM64_REG_X1))
        elif a==0x5cfeb98:
            self.text=struct.unpack('<i',cpu.mem_read(cpu.reg_read(UC_ARM64_REG_X0),4))[0];cpu.reg_write(UC_ARM64_REG_X0,0x10019000)
        elif a==0x1001f000:self.state['countdown']['text']=str(self.text)
        elif a==0x69a67d4:self.state['countdown'if cpu.reg_read(UC_ARM64_REG_X0)==self.COUNTDOWN else'fight']['alpha']=self.sf(0)
        elif a==0x69a82e4:pass
        elif a==0x6adba64:self.state['countdown'if cpu.reg_read(UC_ARM64_REG_X0)==self.COUNTDOWN else'fight']['position']=[self.sf(i)for i in range(3)]
        elif a in(0x33e6dec,0x36f4544):
            target='countdown'if cpu.reg_read(UC_ARM64_REG_X0)==self.COUNTDOWN else'fight'
            self.tweens.append(dict(kind='fade'if a==0x33e6dec else'move',target=target,end=self.sf(0),duration=self.sf(1)))
            cpu.reg_write(UC_ARM64_REG_X0,0x10017000)
        elif a in(0x4180acc,0x4180ff0,0x41811ac,0x4180fa4):
            key={0x4180acc:'ease',0x4180ff0:'relative',0x41811ac:'independent',0x4180fa4:'auto_kill'}[a];self.tweens[-1][key]=cpu.reg_read(UC_ARM64_REG_X1)
        elif a==0x351c864:self.sounds.append('second'if cpu.reg_read(UC_ARM64_REG_X0)==1 else'fight')
        else:return super()._hook(cpu,a,size,data)
        cpu.reg_write(UC_ARM64_REG_PC,cpu.reg_read(UC_ARM64_REG_LR))
    def invoke(self,remaining,previous):
        self.remaining=remaining;self.state={name:dict(active=False,alpha=.42,text='',position=[1,2,3])for name in('countdown','fight')};self.tweens=[];self.sounds=[]
        self.cpu.mem_write(self.SELF+0x48,struct.pack('<i',previous));self.call(0x368c34c,self.SELF)
        return dict(state=self.state,tweens=self.tweens,sounds=self.sounds,last=struct.unpack('<i',self.cpu.mem_read(self.SELF+0x48,4))[0])
    def enable(self,countdown,fight):
        self.state={'countdown':dict(active=countdown),'fight':dict(active=fight)};self.cpu.mem_write(self.SELF+0x48,struct.pack('<i',99));self.call(0x368c2f0,self.SELF)
        return dict(state=self.state,last=struct.unpack('<i',self.cpu.mem_read(self.SELF+0x48,4))[0])


SHIMS=r'''
using System;using System.Collections;using System.Collections.Generic;using Jianghu;
namespace UnityEngine {
 public class Object {}
 public class Component:Object {public GameObject gameObject;public Transform transform=>gameObject.transform;public T GetComponent<T>()where T:Component=>null;public T[]GetComponents<T>()where T:Component=>Array.Empty<T>();}
 public class GameObject:Object {public Transform transform;public bool activeSelf;public bool activeInHierarchy=>activeSelf;public GameObject(){transform=new Transform{gameObject=this};}public void SetActive(bool b){activeSelf=b;}public T AddComponent<T>()where T:Component,new()=>new T{gameObject=this};public T GetComponent<T>()where T:Component=>null;public T[]GetComponents<T>()where T:Component=>Array.Empty<T>();}
 public class Transform:Component,IEnumerable {public string name;public Vector3 localPosition=new Vector3(1,2,3);public IEnumerator GetEnumerator()=>Array.Empty<Transform>().GetEnumerator();}
 public class MonoBehaviour:Component {public bool isActiveAndEnabled=true;}
 public class TextAsset {public string text;}public static class Resources {public static T Load<T>(string path)where T:class=>null;}
 public struct Vector2 {public float x,y;public Vector2(float a,float b){x=a;y=b;}public static implicit operator Vector2(Vector3 v)=>new Vector2(v.x,v.y);}
 public struct Vector3 {public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}}
 public struct Color {public float a;}
 public static class Mathf {public static int CeilToInt(float v)=>(int)Math.Ceiling(v);public static float Sqrt(float v)=>(float)Math.Sqrt(v);public static float Clamp01(float v)=>v<0?0:v>1?1:v;public static float LerpUnclamped(float a,float b,float t)=>a+(b-a)*t;}
 public static class Time {public static float unscaledDeltaTime;}
 public class AudioSource:Component {public static int stops;public void Stop(){stops++;}}
}
namespace UnityEngine.UI {public class Graphic:UnityEngine.Component {public UnityEngine.Color color=new UnityEngine.Color{a=.42f};}}
namespace TMPro {public class TMP_Text:UnityEngine.UI.Graphic {public string text="";public float alpha {get=>color.a;set {color=new UnityEngine.Color{a=value};}}}}
public static class OriginalUIAudio {public static List<string> sounds=new List<string>();public static void Play(UnityEngine.AudioSource a,J data,J library){sounds.Add(data["key"].S);}}
'''
PROGRAM=r'''
using System;using System.Collections;using System.IO;using System.Linq;using System.Reflection;using Jianghu;using UnityEngine;using TMPro;
class Program {
 static int checks;static void Check(bool value,string name){checks++;if(!value)throw new Exception(name);}
 static void Set(object o,string f,object v)=>o.GetType().GetField(f,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(o,v);
 static object Get(object o,string f)=>o.GetType().GetField(f,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o);
 static void Invoke(object o,string method)=>o.GetType().GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(o,null);
 static void Main(string[]args){J oracle=J.Parse(File.ReadAllText(args[0]));foreach(var c in oracle["updates"].A){
  var driver=new OriginalCountdownView{gameObject=new GameObject()};var countdown=new TMP_Text{gameObject=new GameObject()};var fight=new TMP_Text{gameObject=new GameObject()};var scheduler=new OriginalCountdownTweens{gameObject=new GameObject()};
  Set(driver,"countdownText",countdown);Set(driver,"fightText",fight);Set(driver,"remaining",(Func<float>)(()=>(float)c["remaining"].N));Set(driver,"lastSecond",c["previous"].I);Set(driver,"initialPosition",new Vector2(7,12));Set(driver,"initialized",true);Set(driver,"tweens",scheduler);Set(driver,"audioSourceDefault",new AudioSource());Set(driver,"data",J.O("countdownSecondSFX",J.O("key","second"),"countdownFightSFX",J.O("key","fight")));Set(driver,"library",new J());
  OriginalUIAudio.sounds.Clear();AudioSource.stops=0;Invoke(driver,"Update");var expected=c["expected"];
  Check(driver.LastSecond==expected["last"].I,"native ceil/last second");Check(string.Join(",",OriginalUIAudio.sounds)==string.Join(",",expected["sounds"].A.Select(x=>x.S)),"UI sound choice/order");Check(AudioSource.stops==OriginalUIAudio.sounds.Count,"UISFX Stop before each sound");
  foreach(string name in new[]{"countdown","fight"}){var t=name=="countdown"?countdown:fight;var e=expected["state"][name];Check(t.gameObject.activeSelf==e["active"].B,"active");Check(Math.Abs(t.alpha-e["alpha"].N)<1e-6,"alpha");Check(t.text==e["text"].S,"text");var p=t.transform.localPosition;Check(p.x==e["position"][0].N&&p.y==e["position"][1].N&&p.z==e["position"][2].N,"local position");}
  var tweens=(IList)Get(scheduler,"active");Check(tweens.Count==expected["tweens"].A.Count,"native tween count");
  for(int i=0;i<tweens.Count;i++){var t=tweens[i];var e=expected["tweens"][i];bool fade=Get(t,"graphic")!=null;Check(fade==(e["kind"].S=="fade"),"tween type");Check((float)Get(t,"duration")==e["duration"].N,"duration");Check((float)Get(t,"end")==e["end"].N+(fade?0:12),"relative Y/end alpha");Check((bool)Get(t,"outCirc")== (e["ease"].I==21),"native ease");Check(e["independent"].I==1&&e["auto_kill"].I==1,"source independent/autokill");}
 }
 foreach(var c in oracle["enables"].A){var driver=new OriginalCountdownView{gameObject=new GameObject()};var a=new TMP_Text{gameObject=new GameObject{activeSelf=c["countdown"].B}};var b=new TMP_Text{gameObject=new GameObject{activeSelf=c["fight"].B}};Set(driver,"countdownText",a);Set(driver,"fightText",b);Set(driver,"initialized",true);Set(driver,"lastSecond",99);Invoke(driver,"OnEnable");Check(driver.LastSecond==c["expected"]["last"].I,"native enable counter reset");Check(a.gameObject.activeSelf==c["expected"]["state"]["countdown"]["active"].B&&b.gameObject.activeSelf==c["expected"]["state"]["fight"]["active"].B,"native enable label reset");}
 foreach(var c in oracle["ease"].A){float result=OriginalCountdownTweens.Ease((float)c["t"].N,c["out"].B);Check(Math.Abs(result-c["expected"].N)<2e-6,"native DOTween circ ease");}
 // Independent source tweens continue while their target GO is inactive.
 {var t=new TMP_Text{gameObject=new GameObject()};var s=new OriginalCountdownTweens{gameObject=new GameObject()};s.MoveY(t.transform,-25,.5f,false);Time.unscaledDeltaTime=.5f;Invoke(s,"Update");Check(!t.gameObject.activeSelf&&t.transform.localPosition.y==-23,"inactive target tween completion");}
 Console.WriteLine("{\"native_countdown_checks\":"+checks+",\"failures\":0}");}
}
'''


def main():
    OUT.mkdir(exist_ok=True);n=NativeCountdown();updates=[];ease=[]
    for previous in(-1,0,1,2,3,4):
        for remaining in(-3,-.1,0,.001,.49,.99,1,1.0001,1.5,2.1,2.9999,3,3.0001,20):updates.append(dict(previous=previous,remaining=remaining,expected=n.invoke(remaining,previous)))
    for out in(False,True):
        for i in range(101):
            t=i/100;n.call(0x37143c8,21 if out else 20,0,floats=(t,1,1.70158,0));ease.append(dict(t=t,out=out,expected=n.sf(0)))
    enables=[dict(countdown=a,fight=b,expected=n.enable(a,b))for a in(False,True)for b in(False,True)]
    oracle=OUT/'native-countdown-oracle.json';oracle.write_text(json.dumps(dict(updates=updates,ease=ease,enables=enables),indent=2))
    (OUT/'CountdownShims.cs').write_text(SHIMS);(OUT/'CountdownComparison.cs').write_text(PROGRAM)
    runtime=next((UNITY/'NetCoreRuntime/shared/Microsoft.NETCore.App').iterdir());target=OUT/'CompareNativeCountdown.dll'
    command=[str(UNITY/'NetCoreRuntime/dotnet.exe'),str(UNITY/'DotNetSdkRoslyn/csc.dll'),'-nologo','-target:exe','-nostdlib+',f'-out:{target}']
    command += [f'-r:{p}'for p in runtime.glob('*.dll')if(p.name.startswith('System.')and'.Native'not in p.name)or p.name in('mscorlib.dll','netstandard.dll')]
    command += [str(OUT/'CountdownShims.cs'),str(OUT/'CountdownComparison.cs'),str(ROOT/'recovery/bloody/Runtime/OriginalCountdownView.cs'),str(ROOT/'unity/JianghuUnity/Assets/Scripts/Json.cs')]
    subprocess.run(command,check=True,cwd=ROOT)
    target.with_suffix('.runtimeconfig.json').write_text(json.dumps(dict(runtimeOptions=dict(tfm='net8.0',framework=dict(name='Microsoft.NETCore.App',version=runtime.name)))))
    result=subprocess.run([str(UNITY/'NetCoreRuntime/dotnet.exe'),str(target),str(oracle)],cwd=ROOT,text=True,capture_output=True)
    (OUT/'native-countdown-comparison.txt').write_text(result.stdout+result.stderr);print(result.stdout,end='');print(result.stderr,end='')
    if result.returncode:raise SystemExit(result.returncode)


if __name__=='__main__':main()
