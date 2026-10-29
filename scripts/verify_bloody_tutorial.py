"""Execute original TutorialController instructions against actual recovered C#.

UITransitionAnimator/engine services are bounded call-boundary observers. This
does not claim an APK trajectory or complete global service reconstruction.
"""
import json
from pathlib import Path
import struct
import subprocess
from verify_bloody_ai_native import NativeAI
from unicorn.arm64_const import UC_ARM64_REG_X0,UC_ARM64_REG_X1,UC_ARM64_REG_X2,UC_ARM64_REG_LR,UC_ARM64_REG_PC

ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'output/bloody-full-port-20261028/ai-check/tutorial';UNITY=Path('D:/Unity/Editors/6000.4.0f1/Editor/Data')


class NativeTutorial(NativeAI):
    CONTAINER=0x10013000;TOP=0x10013100;SCREENS=0x10012000;HANDS=0x10012800
    def __init__(self):
        super().__init__()
        for cell in(0x720d1d8,0x720d298,0x72120f8,0x7212e00):self.ptr(cell,0x10015000)
        for cell,handle,statics in((0x7212238,0x10017000,0x10017200),(0x7212588,0x10017500,0x10017700),(0x7212df8,0x10017a00,0x10017c00),(0x7212868,0x10017d00,0x10017f00)):
            self.ptr(cell,handle);self.ptr(handle,handle+0x100);self.cpu.mem_write(handle+0x100+0xe4,struct.pack('<I',1));self.ptr(handle+0x100+0xb8,statics)
        for cell,slot,token in((0x7212de8,0x10015d00,1),(0x7212df0,0x10015d08,2)):
            self.ptr(cell,slot);self.ptr(slot,token)
        self.ptr(self.SELF+0x20,self.CONTAINER);self.ptr(self.SELF+0x28,self.SCREENS);self.ptr(self.SELF+0x30,self.HANDS);self.ptr(self.SELF+0x38,self.TOP)
        for i in range(4):self.ptr(self.SCREENS+0x20+i*8,0x10014000+i*0x100)
        self.cpu.mem_write(self.SCREENS+0x18,struct.pack('<i',4))
        for i in range(5):self.ptr(self.HANDS+0x20+i*8,0x10014800+i*0x100)
        self.names={self.CONTAINER:'container',self.TOP:'top'}|{0x10014000+i*0x100:'screen'+str(i)for i in range(4)}
        self.reset()
    def ptr(self,a,v):self.cpu.mem_write(a,struct.pack('<Q',v))
    def reset(self,mode=2,progress=True,passed=False,network=True,state=4,transition=True,step=0,hands=3):
        self.calls=[];self.delegate_tokens={};self.next_alloc=0x10019600;self.passed=passed;self.tween_index=-1
        self.ptr(0x10017700,0x10018000 if progress else 0);self.ptr(0x10017c00,0x10018100 if transition else 0);self.ptr(0x10017f00,0x10018200 if network else 0)
        self.cpu.mem_write(0x10017294,struct.pack('<i',mode));self.cpu.mem_write(self.SELF+0x48,struct.pack('<i',step));self.cpu.mem_write(self.HANDS+0x18,struct.pack('<i',hands));self.state=state
    def _hook(self,cpu,a,size,data):
        x0=cpu.reg_read(UC_ARM64_REG_X0);x1=cpu.reg_read(UC_ARM64_REG_X1)
        if a==0x6acbd7c:cpu.reg_write(UC_ARM64_REG_X0,int(x0==x1))
        elif a==0x316bb40:cpu.reg_write(UC_ARM64_REG_X0,self.next_alloc);self.next_alloc+=0x80
        elif a==0x5c6ff00:self.delegate_tokens[x0]=cpu.reg_read(UC_ARM64_REG_X2)
        elif a in(0x35132e8,0x3513460,0x35133a4,0x351351c):self.calls.append(dict(kind={0x35132e8:'subscribe_pressed',0x3513460:'subscribe_released',0x35133a4:'unsubscribe_pressed',0x351351c:'unsubscribe_released'}[a]))
        elif a==0x365e4a4:cpu.reg_write(UC_ARM64_REG_X0,int(self.passed))
        elif a==0x3640c34:cpu.reg_write(UC_ARM64_REG_X0,self.state)
        elif a==0x6aca694:self.calls.append(dict(kind='active',target=self.names[x0],value=bool(x1)))
        elif a==0x6ac65ac:pass
        elif a in(0x36b7524,0x36b7448):self.calls.append(dict(kind='exit'if a==0x36b7524 else'enter',target=self.names[x0]))
        elif a in(0x35dd03c,0x35dd0e8):cpu.reg_write(UC_ARM64_REG_X0,0x10019000 if a==0x35dd03c else 0x10019100)
        elif a in(0x6acc03c,0x6acc308):
            call=dict(kind='invoke'if a==0x6acc03c else'cancel',target='HideInGameTopBar'if x1==0x10019000 else'ShowAllTutorialHands')
            if a==0x6acc03c:call['delay']=self.sf(0)
            self.calls.append(call)
        elif a==0x366130c:self.calls.append(dict(kind='mark'));self.passed=True
        elif a in(0x36618b0,0x3661578):self.calls.append(dict(kind='level'if a==0x36618b0 else'stage',value=x1))
        elif a==0x3689dfc:self.calls.append(dict(kind='transition',value=x1))
        elif a==0x33e6aac:
            self.calls.append(dict(kind='fade',target='hand'+str((x0-0x10014800)//0x100),value=self.sf(0),duration=self.sf(1)));self.tween_index=len(self.calls)-1;cpu.reg_write(UC_ARM64_REG_X0,0x10018500)
        elif a==0x4180acc:self.calls[self.tween_index]['ease']=x1
        elif a==0x351c864:self.calls.append(dict(kind='sound',value=x0,source=x1))
        else:return super()._hook(cpu,a,size,data)
        cpu.reg_write(UC_ARM64_REG_PC,cpu.reg_read(UC_ARM64_REG_LR))
    def invoke(self,name,**arguments):
        self.reset(**arguments)
        address={'enable':0x3443c98,'disable':0x3443f90,'next':0x344404c,'no':0x3444134,'fight':0x3444138,'pressed':0x3444388,'released':0x3444398,'hide':0x34444f8,'show_top':0x34444dc,'show_hands':0x34444d4,'duplicate_hide':0x3444390}[name]
        self.call(address,self.SELF)
        return dict(calls=self.calls,step=struct.unpack('<i',self.cpu.mem_read(self.SELF+0x48,4))[0],passed=self.passed)


SHIMS=r'''
using System;using System.Collections;using System.Collections.Generic;using Jianghu;
public static class TestLog {public static List<J> calls=new List<J>();public static void Add(J c)=>calls.Add(c);}
namespace UnityEngine {
 public class Object {}
 public class Component:Object {public GameObject gameObject;public Transform transform=>gameObject.transform;public T GetComponent<T>()where T:Component=>null;public T[]GetComponents<T>()where T:Component=>Array.Empty<T>();}
 public class GameObject:Object {public string name;public bool activeSelf=true;public Transform transform;public GameObject(string name=""){this.name=name;transform=new Transform{gameObject=this};}public void SetActive(bool b){activeSelf=b;TestLog.Add(J.O("kind","active","target",name,"value",b));}public T AddComponent<T>()where T:Component,new()=>new T{gameObject=this};}
 public class Transform:Component,IEnumerable {public string name=>gameObject.name;public IEnumerator GetEnumerator()=>Array.Empty<Transform>().GetEnumerator();}
 public class MonoBehaviour:Component {public bool isActiveAndEnabled=true;public void Invoke(string s,float delay){TestLog.Add(J.O("kind","invoke","target",s,"delay",delay));}public void CancelInvoke(string s){TestLog.Add(J.O("kind","cancel","target",s));}}
 public class CanvasGroup:Component {public float alpha=1;}
 public static class Resources {public static T Load<T>(string p)where T:class=>null;}
 public class TextAsset {public string text;}
 public class AudioSource:Component {public int stops;public void Stop(){stops++;}}
 public static class Time {public static float deltaTime;}
 public static class Mathf {public static float Clamp01(float f)=>f<0?0:f>1?1:f;}
}
namespace UnityEngine.Events {public delegate void UnityAction();}
namespace UnityEngine.UI {public class Button:UnityEngine.Component {public Event onClick=new Event();public class Event {public void AddListener(UnityEngine.Events.UnityAction a){}public void RemoveListener(UnityEngine.Events.UnityAction a){}}}}
namespace TMPro {public class TMP_Text:UnityEngine.Component {public string text;}}
public class OriginalUITransitionAnimator:UnityEngine.MonoBehaviour {public void Exit(bool b=false){TestLog.Add(J.O("kind","exit","target",gameObject.name));}public void Enter(bool b=false){TestLog.Add(J.O("kind","enter","target",gameObject.name));}}
public static class OriginalUIAudio {public static int plays;public static void Play(UnityEngine.AudioSource s,J r,J l){plays++;}}
'''

PROGRAM=r'''
using System;using System.Collections;using System.IO;using System.Linq;using System.Reflection;using Jianghu;using UnityEngine;
class Program {
 static int checks,localChecks;static void Check(bool b,string m,bool local=false){if(local)localChecks++;else checks++;if(!b)throw new Exception(m);}
 static void Set(object o,string k,object v)=>o.GetType().GetField(k,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(o,v);
 static object Get(object o,string k)=>o.GetType().GetField(k,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o);
 static object Invoke(object o,string k)=>o.GetType().GetMethod(k,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(o,null);
 static bool Equal(J a,J b){if(a.D.Count!=b.D.Count)return false;foreach(var p in a.D){if(p.Value.value is bool||p.Value.value is string){if(p.Value.S!=b[p.Key].S)return false;}else if(Math.Abs(p.Value.N-b[p.Key].N)>.000002)return false;}return true;}
 static void Main(string[]args){var oracle=J.Parse(File.ReadAllText(args[0]));foreach(var c in oracle["cases"].A){
  var p=c["arguments"];bool passed=p["passed"].B;var ctx=new OriginalTutorialContext{GameMode=()=>p["mode"].I,HasProgress=()=>p["progress"].B,IsTutorialPassed=()=>passed,HasNetworkController=()=>p["network"].B,NetworkState=()=>p["state"].I,HasTransition=()=>p["transition"].B,
   MarkTutorialPassed=()=>{TestLog.Add(J.O("kind","mark"));passed=true;},SetCampaignLevel=v=>TestLog.Add(J.O("kind","level","value",v)),SetCampaignStage=v=>TestLog.Add(J.O("kind","stage","value",v)),TransitionToArena=v=>TestLog.Add(J.O("kind","transition","value",v))};
  var driver=new OriginalTutorialController{gameObject=new GameObject()};var scheduler=new OriginalTutorialAlphaTweens{gameObject=new GameObject()};var screens=Enumerable.Range(0,4).Select(i=>new OriginalUITransitionAnimator{gameObject=new GameObject("screen"+i)}).ToArray();var hands=Enumerable.Range(0,p["hands"].I).Select(i=>new CanvasGroup{gameObject=new GameObject("hand"+i)}).ToArray();
  Set(driver,"context",ctx);Set(driver,"fades",scheduler);Set(driver,"tutorialContainer",new GameObject("container"));Set(driver,"topBar",new GameObject("top"));Set(driver,"screens",screens);Set(driver,"hands",hands);Set(driver,"step",p["step"].I);Set(driver,"initialized",true);TestLog.calls.Clear();
  string name=c["name"].S;switch(name){case "enable":Invoke(driver,"OnEnable");break;case "disable":Invoke(driver,"OnDisable");break;case "next":driver.OnClickedNext();break;case "no":driver.OnClickedNo();break;case "fight":driver.OnClickedFight();break;case "pressed":case "duplicate_hide":Invoke(driver,"OnHandInputPressed");break;case "released":Invoke(driver,"OnHandInputReleased");break;case "hide":driver.HideInGameTopBar();break;case "show_top":Invoke(driver,"ShowInGameTopBar");break;case "show_hands":driver.ShowAllTutorialHands();break;default:throw new Exception(name);}
  var expected=c["expected"];var wanted=expected["calls"].A.Where(r=>!r["kind"].S.StartsWith("subscribe_")&&!r["kind"].S.StartsWith("unsubscribe_")&&r["kind"].S!="fade").ToList();Check(TestLog.calls.Count==wanted.Count,"native callback count "+name);for(int i=0;i<wanted.Count;i++)Check(Equal(TestLog.calls[i],wanted[i]),"native action/order "+name+" "+i+" actual="+TestLog.calls[i]+" expected="+wanted[i]);
  Check(driver.CurrentStep==expected["step"].I,"native step cache "+name);Check(passed==expected["passed"].B,"native progress call "+name);
  var fades=(IList)Get(scheduler,"active");var nativeFades=expected["calls"].A.Where(x=>x["kind"].S=="fade").ToList();Check(fades.Count==nativeFades.Count,"native three-group fade count");for(int i=0;i<fades.Count;i++){var f=fades[i];var e=nativeFades[i];Check(((CanvasGroup)Get(f,"target")).gameObject.name==e["target"].S,"native group reference/order");Check(Math.Abs((float)Get(f,"to")-e["value"].N)<1e-6&&Math.Abs((float)Get(f,"duration")-e["duration"].N)<1e-6&&e["ease"].I==9,"native fade endpoint/duration/ease");}
  if(name=="enable"){bool listening=expected["calls"].A.Any(r=>r["kind"].S=="subscribe_pressed");Check(driver.ListeningForHandInput==listening,"native subscription gate");ctx.RaiseHandPressed();Check(scheduler.ActiveCount==nativeFades.Count+(listening?hands.Length:0),"live mapped Pressed handler");int calls=TestLog.calls.Count;ctx.RaiseHandReleased();Check(TestLog.calls.Count==calls+(listening?2:0),"live mapped Released handler");Invoke(driver,"OnDisable");calls=TestLog.calls.Count;int fadeCount=scheduler.ActiveCount;ctx.RaiseHandPressed();ctx.RaiseHandReleased();Check(TestLog.calls.Count==calls&&scheduler.ActiveCount==fadeCount,"native unsubscribe on disable");}
 }
 foreach(var e in oracle["ease"].A)Check(Math.Abs(OriginalTutorialAlphaTweens.OutCubic((float)e["t"].N)-e["expected"].N)<.000002,"native OutCubic");
 {var group=new CanvasGroup{gameObject=new GameObject("hand0")};var s=new OriginalTutorialAlphaTweens{gameObject=new GameObject()};s.To(group,0,.1f);Time.deltaTime=0;Invoke(s,"Update");Check(group.alpha==1&&s.ActiveCount==1,"paused fade startup",true);Time.deltaTime=.05f;Invoke(s,"Update");Check(Math.Abs(group.alpha-.125f)<.000002,"fade interpolation",true);s.To(group,1,.1f);Check(s.ActiveCount==2,"no fade kill on overlapping callback",true);group.gameObject.activeSelf=false;Time.deltaTime=.1f;Invoke(s,"Update");Check(s.ActiveCount==0&&Math.Abs(group.alpha-1)<.000002,"fade continues while target inactive",true);}
 {var source=new AudioSource();var p=new OriginalTutorialPopupSFX{gameObject=new GameObject()};OriginalUIAudio.plays=0;p.Initialize(source,J.O("open",J.O("kind","audio_object","key","open"),"close",J.O("kind","audio_object","key","close")),new J());Invoke(p,"OnDisable");Check(source.stops==2&&OriginalUIAudio.plays==2,"popup source UISFX Default Stop/Play",true);}
 Console.WriteLine("{\"native_tutorial_checks\":"+checks+",\"local_lifecycle_checks\":"+localChecks+",\"failures\":0}");
 }
}
'''


def main():
    OUT.mkdir(exist_ok=True,parents=True);n=NativeTutorial();cases=[]
    default=dict(mode=2,progress=True,passed=False,network=True,state=4,transition=True,step=0,hands=3)
    def add(name,**changes):
        args=default|changes;cases.append(dict(name=name,arguments=args,expected=n.invoke(name,**args)))
    for mode in range(6):
        for progress in(False,True):
            for passed in(False,True):add('enable',mode=mode,progress=progress,passed=passed,step=2)
    for network in(False,True):
        for state in range(8):
            for step in range(5):add('next',network=network,state=state,step=step)
    for name in('no','fight'):
        for progress in(False,True):
            for transition in(False,True):
                for passed in(False,True):add(name,progress=progress,transition=transition,passed=passed)
    for name in('pressed','duplicate_hide','released','hide','show_top','show_hands','disable'):
        for hands in(0,1,3,5):add(name,hands=hands)
    ease=[]
    for i in range(101):
        t=i/100;n.call(0x37143c8,9,0,floats=(t,1,1.70158,0));ease.append(dict(t=t,expected=n.sf(0)))
    oracle=OUT/'native-tutorial-oracle.json';oracle.write_text(json.dumps(dict(cases=cases,ease=ease),indent=2))
    (OUT/'Shims.cs').write_text(SHIMS);(OUT/'Comparison.cs').write_text(PROGRAM)
    runtime=next((UNITY/'NetCoreRuntime/shared/Microsoft.NETCore.App').iterdir());target=OUT/'CompareNativeTutorial.dll'
    cmd=[str(UNITY/'NetCoreRuntime/dotnet.exe'),str(UNITY/'DotNetSdkRoslyn/csc.dll'),'-nologo','-target:exe','-nostdlib+',f'-out:{target}']+[f'-r:{p}'for p in runtime.glob('*.dll')if(p.name.startswith('System.')and'.Native'not in p.name)or p.name in('mscorlib.dll','netstandard.dll')]
    cmd += [str(OUT/'Shims.cs'),str(OUT/'Comparison.cs'),str(ROOT/'recovery/bloody/Runtime/OriginalTutorialController.cs'),str(ROOT/'unity/JianghuUnity/Assets/Scripts/Json.cs')]
    compiled=subprocess.run(cmd,cwd=ROOT,text=True,capture_output=True);(OUT/'compile.txt').write_text(compiled.stdout+compiled.stderr)
    if compiled.returncode:print(compiled.stdout+compiled.stderr);raise SystemExit(compiled.returncode)
    target.with_suffix('.runtimeconfig.json').write_text(json.dumps(dict(runtimeOptions=dict(tfm='net8.0',framework=dict(name='Microsoft.NETCore.App',version=runtime.name)))))
    result=subprocess.run([str(UNITY/'NetCoreRuntime/dotnet.exe'),str(target),str(oracle)],cwd=ROOT,text=True,capture_output=True);(OUT/'comparison.txt').write_text(result.stdout+result.stderr);print(result.stdout,end='');print(result.stderr,end='')
    if result.returncode:raise SystemExit(result.returncode)


if __name__=='__main__':main()
