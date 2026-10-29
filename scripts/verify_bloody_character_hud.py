"""Compare mounted CharacterHUD decisions/lifecycle outputs with original ARM64.

Engine calls are observed at their original ABI. This is not an APK replay.
The C# comparison executes the actual recovered component with bounded UI shims.
"""
import json
from pathlib import Path
import struct
import subprocess
from verify_bloody_ai_native import NativeAI,f32
from unicorn.arm64_const import UC_ARM64_REG_X0,UC_ARM64_REG_X1,UC_ARM64_REG_LR,UC_ARM64_REG_PC

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'output/bloody-full-port-20261028/ai-check/character-hud'
UNITY=Path('D:/Unity/Editors/6000.4.0f1/Editor/Data')


class NativeHUD(NativeAI):
    VITALITY=0x10013000;RECT=0x10014000;STAMINA=0x10014100;FILL=0x10014200;EMPTY=0x10014300;GRADIENT=0x10014400;GRADIENT2=0x10014500;FLASH=0x10014600;GROUP=0x10014700;BUTTON=0x10014800
    CONFIG=0x10017f00;COROUTINE=0x1001d000;TWEEN=0x1001e000
    def __init__(self):
        super().__init__()
        for address in (0x720d1d8,0x720dcd8,0x720d298,0x7212228,0x7212578,0x7212560,0x7212818,0x7212820,0x7212828,0x7212830,0x7212838,0x7212840,0x7212848,0x7212850,0x72187d0):self.ptr(address,0x10015000)
        self.cpu.mem_write(0x10015304,struct.pack('<I',8)) # Mathf.Epsilon * 8, denormals enabled.
        for cell,klass,statics in ((0x7212238,0x10017000,0x10017200),(0x7212370,0x10017500,0x10017700),(0x7212368,0x10017a00,0x10017c00),(0x7212868,0x1001b000,0x1001b200)):
            self.ptr(cell,klass);self.ptr(klass,klass+0x100);self.cpu.mem_write(klass+0x100+0xe4,struct.pack('<I',1));self.ptr(klass+0x100+0xb8,statics)
        self.ptr(0x1001b200,0x1001b400)
        for image in (self.FILL,self.EMPTY):self.ptr(image,0x10016000)
        self.ptr(0x10016000+0x2a8,0x1001f000)
        self.ptr(self.TWEEN,0x1001a000);self.ptr(0x1001a000+0x1a8,0x1001f010)
        self.reset()
    def ptr(self,address,value):self.cpu.mem_write(address,struct.pack('<Q',value))
    def reset(self):
        self.calls=[];self.color_ratios=[];self.colors=[];self.flash=False;self.anchor=[.42,.93];self.group_alpha=.5;self.blocks=False;self.startup=False;self.next_allocation=0x10019000;self.pips={};self.rounds=3;self.wins=(0,0)
    def _hook(self,cpu,address,size,data):
        x0=cpu.reg_read(UC_ARM64_REG_X0);x1=cpu.reg_read(UC_ARM64_REG_X1)
        if address==0x6acbd7c:cpu.reg_write(UC_ARM64_REG_X0,int(x0==x1))
        elif address==0x6aca1c8:cpu.reg_write(UC_ARM64_REG_X0,int(x0!=x1))
        elif address==0x6ad7070:self.setf(0,self.time_scale)
        elif address==0x36f9ae8:self.calls.append(dict(kind='kill'))
        elif address==0x33e8c08:self.calls.append(dict(kind='tween',x=self.sf(0),y=self.sf(1),duration=self.sf(2),snapping=x1));cpu.reg_write(UC_ARM64_REG_X0,0x10018000)
        elif address==0x6adb3e4:self.anchor=[self.sf(0),self.sf(1)];self.calls.append(dict(kind='snap',x=self.sf(0),y=self.sf(1)))
        elif address==0x6ab1e08:
            value=self.sf(0);self.color_ratios.append(value)
            for index,value in enumerate((value,0,0,1)):self.setf(index,value)
        elif address==0x1001f000:self.colors.append([self.sf(index)for index in range(4)])
        elif address==0x6aca694:
            if x0==self.FLASH:self.flash=bool(x1)
            elif x0==self.BUTTON:self.calls.append(dict(kind='button',active=bool(x1)))
            else:self.pips[x0]=bool(x1)
        elif address in (0x316b7cc,0x516bd14,0x5c6ff00,0x365d210,0x365b63c,0x350c83c,0x350c974,0x341c744):pass
        elif address==0x316bb40:cpu.reg_write(UC_ARM64_REG_X0,self.next_allocation);self.next_allocation+=0x80
        elif address==0x6accc44:self.calls.append(dict(kind='stop_coroutine'))
        elif address==0x6ac65ac:pass
        elif address==0x6dde3ac:self.group_alpha=self.sf(0)
        elif address==0x6dde2f8:self.setf(0,self.group_alpha)
        elif address==0x6dde6ac:self.blocks=bool(x1)
        elif address==0x36441b4:cpu.reg_write(UC_ARM64_REG_X0,self.rounds&0xffffffffffffffff)
        elif address in (0x364404c,0x3643fe0):cpu.reg_write(UC_ARM64_REG_X0,(((self.wins[0 if address==0x364404c else 1]+128)&255)-128)&0xffffffffffffffff)
        elif address==0x6aca44c:pass
        elif address==0x6adf268:cpu.reg_write(UC_ARM64_REG_X0,x0+0x40)
        elif address==0x1001f010:self.startup=True;cpu.reg_write(UC_ARM64_REG_X0,0)
        elif address==0x3711768:pass
        else:return super()._hook(cpu,address,size,data)
        cpu.reg_write(UC_ARM64_REG_PC,cpu.reg_read(UC_ARM64_REG_LR))
    def bar(self,kind,value,maximum,previous,time_scale):
        self.reset();self.time_scale=time_scale
        for offset,pointer in ((0xa8,self.VITALITY),(0x28,self.RECT),(0x50,self.STAMINA),(0x30,self.FILL),(0x38,self.EMPTY),(0x40,self.GRADIENT),(0x48,self.GRADIENT2),(0x58,self.FLASH)):self.ptr(self.SELF+offset,pointer)
        self.cpu.mem_write(self.VITALITY+0x78,struct.pack('<2f',value,value));self.cpu.mem_write(self.VITALITY+0xa4,struct.pack('<f',maximum))
        self.cpu.mem_write(self.SELF+0xc0,struct.pack('<2f',previous,previous))
        self.call(0x3431b04 if kind=='health'else 0x3431ccc,self.SELF)
        cached=struct.unpack('<f',self.cpu.mem_read(self.SELF+(0xc0 if kind=='health'else 0xc4),4))[0]
        action=1 if any(c['kind']=='snap'for c in self.calls)else 2 if any(c['kind']=='tween'for c in self.calls)else 0
        return dict(action=action,cached=cached,anchor=self.anchor,colors=self.color_ratios,flash=self.flash,calls=self.calls)
    def slot(self,screen,local):
        self.cpu.mem_write(self.SELF+0x20,struct.pack('<i',screen));self.ptr(0x10017700,0 if local<0 else 0x10017e00);self.cpu.mem_write(0x10017e68,struct.pack('<i',local))
        self.call(0x3431690,self.SELF);v=self.cpu.reg_read(UC_ARM64_REG_X0)&0xffffffff;return v-0x100000000 if v>=0x80000000 else v
    def alpha(self,screen,mode):
        self.reset();self.cpu.mem_write(self.SELF+0x20,struct.pack('<i',screen));self.cpu.mem_write(0x10017294,struct.pack('<i',mode));self.ptr(self.SELF+0xa0,self.GROUP);self.ptr(self.SELF+0x70,0)
        self.call(0x3430f94,self.SELF);return dict(alpha=self.group_alpha,blocks=self.blocks)
    def pip(self,screen,local,rounds,wins1,wins2):
        self.reset();self.rounds=rounds;self.wins=(wins1,wins2);self.slot(screen,local)
        self.cpu.mem_write(self.COROUTINE+0x10,struct.pack('<i',1));self.ptr(self.COROUTINE+0x20,self.SELF);self.ptr(self.SELF+0x88,0x1001b800)
        self.cpu.mem_write(0x1001b818,struct.pack('<i',5))
        for i in range(5):self.ptr(0x1001b820+i*8,0x1001c000+i*0x100)
        self.call(0x34325c8,self.COROUTINE)
        return [dict(visible=self.pips[0x1001c000+i*0x100],won=self.pips[0x1001c040+i*0x100])for i in range(5)]
    def clear(self,coroutine,config,vitality,button):
        self.reset();self.ptr(self.SELF+0xb8,self.COROUTINE if coroutine else 0);self.ptr(self.SELF+0xb0,self.CONFIG if config else 0);self.ptr(self.SELF+0xa8,self.VITALITY if vitality else 0);self.ptr(self.SELF+0x70,self.BUTTON if button else 0);self.ptr(self.SELF+0x80,0x1001a500)
        self.cpu.mem_write(self.SELF+0xc0,struct.pack('<2f',.35,.67));self.call(0x3431424,self.SELF)
        return dict(coroutine=struct.unpack('<Q',self.cpu.mem_read(self.SELF+0xb8,8))[0],config=struct.unpack('<Q',self.cpu.mem_read(self.SELF+0xb0,8))[0],vitality=struct.unpack('<Q',self.cpu.mem_read(self.SELF+0xa8,8))[0],url=struct.unpack('<Q',self.cpu.mem_read(self.SELF+0x80,8))[0],cache=list(struct.unpack('<2f',self.cpu.mem_read(self.SELF+0xc0,8))),calls=self.calls)
    def delta(self,delta):
        self.reset();self.cpu.mem_write(self.TWEEN+0x28,struct.pack('<f',1));self.cpu.mem_write(self.TWEEN+0x54,b'\0');self.cpu.mem_write(self.TWEEN+0xe8,b'\1');self.cpu.mem_write(self.TWEEN+0x110,b'\1');self.cpu.mem_write(self.TWEEN+0x118,b'\1');self.cpu.mem_write(self.TWEEN+0x101,b'\0')
        self.call(0x3711b54,self.TWEEN,0,floats=(delta,delta*2));return self.startup


SHIMS=r'''
using System;using System.Collections;using System.Collections.Generic;using System.Linq;using Jianghu;
namespace UnityEngine {
 public class Object {}
 public class Component:Object {public GameObject gameObject;public Transform transform=>gameObject.transform;public T GetComponent<T>()where T:Component=>gameObject.GetComponent<T>();}
 public class GameObject:Object {public bool activeSelf=true;public bool activeInHierarchy=>activeSelf;public Transform transform;public GameObject(){transform=new Transform{gameObject=this};}public void SetActive(bool b){activeSelf=b;}public T GetComponent<T>()where T:Component=>null;public T AddComponent<T>()where T:Component,new()=>new T{gameObject=this};}
 public class Transform:Component,IEnumerable {public string name;public List<Transform>children=new List<Transform>();public IEnumerator GetEnumerator()=>children.GetEnumerator();public Transform GetChild(int i)=>children[i];}
 public class RectTransform:Transform {public Vector2 anchorMax=new Vector2(.42f,.93f);}
 public class MonoBehaviour:Component {public bool isActiveAndEnabled=>gameObject==null||gameObject.activeInHierarchy;public int stopped;public Coroutine StartCoroutine(IEnumerator r){r.MoveNext();return new Coroutine{routine=r};}public void StopCoroutine(Coroutine c){stopped++;}}
 public class Coroutine {public IEnumerator routine;}
 public class WaitUntil {public Func<bool> predicate;public WaitUntil(Func<bool> p){predicate=p;}}
 public class CanvasGroup:Component {public float alpha;public bool blocksRaycasts,interactable;}
 public class Animation:Component {public bool Play()=>true;}
 public class Sprite:Object {}public class TextAsset{public string text;}
 public static class Resources{public static T Load<T>(string p)where T:class=>null;}
 public static class Application{public static void OpenURL(string url){throw new Exception("Unexpected external action");}}
 public struct Vector2 {public float x,y;public Vector2(float a,float b){x=a;y=b;}public static Vector2 LerpUnclamped(Vector2 a,Vector2 b,float t)=>new Vector2(a.x+(b.x-a.x)*t,a.y+(b.y-a.y)*t);}
 public struct Color {public float r,g,b,a;public Color(float r,float g,float b,float a){this.r=r;this.g=g;this.b=b;this.a=a;}}
 public class Gradient {public float last;public Color Evaluate(float t){last=t;return new Color(t,0,0,1);}}
 public static class Time {public static float timeScale=1,deltaTime;}
 public static class Mathf {public static float Clamp01(float f)=>f<0?0:f>1?1:f;public static bool Approximately(float a,float b)=>Math.Abs(b-a)<Math.Max(1e-6f*Math.Max(Math.Abs(a),Math.Abs(b)),float.Epsilon*8);}
}
namespace UnityEngine.UI {
 public class Image:UnityEngine.Component {public UnityEngine.Color color;public UnityEngine.Sprite sprite;}
 public class Slider:UnityEngine.Component {public float value;}
 public class Button:UnityEngine.Component {public Event onClick=new Event();public class Event{public void AddListener(Action a){}public void RemoveListener(Action a){}}}
}
namespace TMPro { [Flags]public enum FontStyles{Normal=0,Underline=4}public class TMP_Text:UnityEngine.Component {public string text;public FontStyles fontStyle;} }
public class OriginalRig:UnityEngine.Component {public float health,healthMax,stamina,staminaMax;public string OriginalCharacterName;}
public static class OriginalVisualCatalog {public static UnityEngine.Gradient Gradient(J j)=>new UnityEngine.Gradient();}
'''

PROGRAM=r'''
using System;using System.Collections;using System.IO;using System.Linq;using System.Reflection;using Jianghu;using UnityEngine;using UnityEngine.UI;
class Program {
 static int nativeChecks,localChecks;static void Check(bool v,string m,bool local=false){if(local)localChecks++;else nativeChecks++;if(!v)throw new Exception(m);}
 static void Set(object o,string f,object v)=>o.GetType().GetField(f,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(o,v);
 static object Get(object o,string f)=>o.GetType().GetField(f,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o);
 static object Invoke(object o,string f)=>o.GetType().GetMethod(f,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(o,null);
 static bool Near(float actual,J expected)=>Math.Abs(actual-(float)expected.N)<.000002f;
 static void Main(string[]args) {
  J oracle=J.Parse(File.ReadAllText(args[0]));foreach(var c in oracle["bars"].A){
   bool health=c["kind"].S=="health";var driver=new OriginalCharacterHUD{gameObject=new GameObject()};var rect=new RectTransform{gameObject=new GameObject()};var scheduler=new OriginalHUDAnchorTweens{gameObject=new GameObject()};var fill=new Image();var empty=new Image();var flash=new GameObject{activeSelf=false};var rig=new OriginalRig{health=(float)c["value"].N,healthMax=(float)c["maximum"].N,stamina=(float)c["value"].N};
   Set(driver,"bound",rig);Set(driver,"tweens",scheduler);Set(driver,health?"healthBar":"staminaBar",rect);Set(driver,health?"cachedHealth":"cachedStamina",(float)c["previous"].N);Set(driver,"healthFill",fill);Set(driver,"healthEmpty",empty);Set(driver,"fillGradient",new Gradient());Set(driver,"emptyGradient",new Gradient());Set(driver,"staminaFlash",flash);
   scheduler.To(rect,new Vector2(.8f,1),1);Time.timeScale=(float)c["time_scale"].N;Invoke(driver,health?"RefreshHealth":"RefreshStamina");J expected=c["expected"];
   Check(Near(health?driver.CachedHealth:driver.CachedStamina,expected["cached"]),"native cached ratio");Check(Near(rect.anchorMax.x,expected["anchor"][0])&&Near(rect.anchorMax.y,expected["anchor"][1]),"native snap vs unchanged anchor");
   Check(scheduler.ActiveCount==(expected["action"].I==1?0:expected["action"].I==2?2:1),"native snap kills / new tween overlaps");
   if(health){Check(expected["colors"].A.Count==2,"native both gradient calls");Check(Near(fill.color.r,expected["colors"][0])&&Near(empty.color.r,expected["colors"][1]),"native raw gradient inputs during slow motion");}else Check(flash.activeSelf==expected["flash"].B,"native stamina flash");
   var decision=OriginalHUDBarUpdate.Decide((float)c["value"].N,health?(float)c["maximum"].N:100,(float)c["previous"].N,Time.timeScale);Check((int)decision.action==expected["action"].I,"native source action");
   if(expected["action"].I==2) {var t=((IList)Get(scheduler,"active"))[1];Check(Math.Abs((float)Get(t,"duration")-.35f)<1e-7,"source duration");var end=(Vector2)Get(t,"end");var native=expected["calls"].A.First(x=>x["kind"].S=="tween");Check(Near(end.x,native["x"])&&Near(end.y,native["y"])&&native["snapping"].I==0,"native anchor endpoint/no snapping");}
  }
  foreach(var c in oracle["slots"].A)Check(OriginalCharacterHUD.NativePlayerSlot(c["screen"].I,c["local"].I)==c["expected"].I,"native local player slot mapping");
  foreach(var c in oracle["alpha"].A){float a=OriginalCharacterHUD.NativeAlpha(c["screen"].I,c["mode"].I);Check(a==c["expected"]["alpha"].N&&(a>0)==c["expected"]["blocks"].B,"native canvas alpha/raycast");}
  foreach(var c in oracle["pips"].A){
   var driver=new OriginalCharacterHUD{gameObject=new GameObject()};var pips=Enumerable.Range(0,5).Select(i=>new GameObject()).ToArray();foreach(var pip in pips)pip.transform.children.Add(new GameObject().transform);
   var ctx=new OriginalCharacterHUDContext{Wins=i=>c[i==0?"wins1":"wins2"].I,RoundsToWin=()=>c["rounds"].I,LocalPlayerSlot=()=>c["local"].I};Set(driver,"context",ctx);Set(driver,"screenSlot",c["screen"].I);Set(driver,"pips",pips);var routine=(IEnumerator)Invoke(driver,"SetPipsWhenReady");Check(routine.MoveNext()&&routine.Current is WaitUntil,"native one WaitUntil yield before pip writes");Check(!routine.MoveNext(),"native pip coroutine completes once");
   for(int i=0;i<5;i++){Check(pips[i].activeSelf==c["expected"][i]["visible"].B,"native rounds pip visibility");Check(pips[i].transform.GetChild(0).gameObject.activeSelf==c["expected"][i]["won"].B,"native signed-byte wins pip");}
  }
  foreach(var c in oracle["resets"].A){
   var driver=new OriginalCharacterHUD{gameObject=new GameObject()};var button=c["button"].B?new Button{gameObject=new GameObject()}:null;Set(driver,"nameButton",button);Set(driver,"bound",c["vitality"].B?new OriginalRig():null);Set(driver,"profileUrl","https://example.invalid");Set(driver,"cachedHealth",.35f);Set(driver,"cachedStamina",.67f);Set(driver,"pipRoutine",c["coroutine"].B?new Coroutine():null);Invoke(driver,"ResetBinding");var e=c["expected"];
   Check(driver.BoundCharacter==null&&e["vitality"].N==0,"native unbind vitality");Check(Get(driver,"profileUrl")==null&&e["url"].N==0,"native clear profile URL");Check(Get(driver,"pipRoutine")==null&&e["coroutine"].N==0,"native clear pip coroutine");Check(Near(driver.CachedHealth,e["cache"][0])&&Near(driver.CachedStamina,e["cache"][1]),"native reset retains ratio caches");Check(driver.stopped==e["calls"].A.Count(x=>x["kind"].S=="stop_coroutine"),"native stop pending pip coroutine");if(button!=null)Check(!button.gameObject.activeSelf&&e["calls"].A.Any(x=>x["kind"].S=="button"&&!x["active"].B),"native hide name button");
  }
  foreach(var c in oracle["ease"].A)Check(Math.Abs(OriginalHUDAnchorTweens.OutQuad((float)c["t"].N)-c["expected"].N)<.000002,"native DOTween OutQuad");
  foreach(var c in oracle["delta"].A){var scheduler=new OriginalHUDAnchorTweens{gameObject=new GameObject()};var r=new RectTransform();scheduler.To(r,new Vector2(.5f,1),.35f);Time.deltaTime=(float)c["value"].N;Invoke(scheduler,"Update");var list=(IList)Get(scheduler,"active");Check((bool)Get(list[0],"started")==c["expected"].B,"native delta threshold before startup");}
  // Bounded local Render adapter lifecycle, separate from ARM64 assertions.
  {var driver=new OriginalCharacterHUD{gameObject=new GameObject{activeSelf=false}};var rig=new OriginalRig{health=90,healthMax=100};Set(driver,"initialized",true);Set(driver,"bound",rig);driver.NotifyHealthChanged();Check(driver.CachedHealth==-1,"inactive driver receives no local vitality notification",true);}
  {var r=new RectTransform();var scheduler=new OriginalHUDAnchorTweens{gameObject=new GameObject()};scheduler.To(r,new Vector2(.9f,1),.35f);Time.deltaTime=0;Invoke(scheduler,"Update");Check(r.anchorMax.x==.42f&&scheduler.ActiveCount==1,"paused tween does not startup or complete",true);Time.deltaTime=.1f;Invoke(scheduler,"Update");float previous=r.anchorMax.x;scheduler.To(r,new Vector2(.2f,1),.35f);Check(r.anchorMax.x==previous&&scheduler.ActiveCount==2,"new tween preserves existing target and overlap",true);Time.deltaTime=.1f;Invoke(scheduler,"Update");var list=(IList)Get(scheduler,"active");var newer=list[1];Check(((Vector2)Get(newer,"start")).x>previous,"overlapping tween captures getter at its update startup",true);r.gameObject=new GameObject{activeSelf=false};Time.deltaTime=1;Invoke(scheduler,"Update");Check(scheduler.ActiveCount==0&&Math.Abs(r.anchorMax.x-.2f)<.000002f,"tween completes while source HUD target inactive",true);}
  foreach(var value in new[]{"https://","HTTPS://example.invalid","http://example.invalid","  https://bad","ftp://bad","",null})Check(OriginalCharacterHUD.HasOriginalProfileUrl(value)==(!string.IsNullOrWhiteSpace(value)&&(value.StartsWith("https://",StringComparison.OrdinalIgnoreCase)||value.StartsWith("http://",StringComparison.OrdinalIgnoreCase))),"source string prefix policy",true);
  Console.WriteLine("{\"native_character_hud_checks\":"+nativeChecks+",\"local_lifecycle_checks\":"+localChecks+",\"failures\":0}");
 }
}
'''


def main():
    OUT.mkdir(exist_ok=True,parents=True);native=NativeHUD();bars=[]
    for kind in ('health','stamina'):
        for previous in (-1,0,.25,.75,1):
            for value in (0,.001,10,25,50,75,100):
                for maximum in ((100,150)if kind=='health'else(100,)):
                    for scale in (0,.25,1,2):bars.append(dict(kind=kind,previous=previous,value=value,maximum=maximum,time_scale=scale,expected=native.bar(kind,value,maximum,previous,scale)))
    for previous,value in ((.25,25.000001),(.25,25.00004),(.75,75.00001),(.75,75.0001),(0,1e-43),(0,0)):
        bars.append(dict(kind='health',previous=f32(previous),value=f32(value),maximum=100,time_scale=1,expected=native.bar('health',value,100,previous,1)))
    slots=[dict(screen=s,local=l,expected=native.slot(s,l))for s in(-1,0,1,2)for l in(-1,0,1,2)]
    alpha=[dict(screen=s,mode=m,expected=native.alpha(s,m))for s in(0,1)for m in range(6)]
    pips=[dict(screen=s,local=l,rounds=r,wins1=w,wins2=2,expected=native.pip(s,l,r,w,2))for s in(0,1)for l in(0,1)for r in(0,1,3,5,6)for w in(-1,0,1,3,5,128,255,256)]
    resets=[dict(coroutine=c,config=a,vitality=v,button=b,expected=native.clear(c,a,v,b))for c in(False,True)for a in(False,True)for v in(False,True)for b in(False,True)]
    ease=[]
    for i in range(101):
        t=i/100;native.call(0x37143c8,6,0,floats=(t,1,1.70158,0));ease.append(dict(t=t,expected=native.sf(0)))
    delta=[dict(value=v,expected=native.delta(v))for v in(0,5e-7,9.99999e-7,1e-6,1.000001e-6,1/60,.25,-5e-7,-1e-6)]
    oracle=OUT/'native-character-hud-oracle.json';oracle.write_text(json.dumps(dict(bars=bars,slots=slots,alpha=alpha,pips=pips,resets=resets,ease=ease,delta=delta),indent=2))
    (OUT/'Shims.cs').write_text(SHIMS);(OUT/'Comparison.cs').write_text(PROGRAM)
    runtime=next((UNITY/'NetCoreRuntime/shared/Microsoft.NETCore.App').iterdir());target=OUT/'CompareNativeCharacterHUD.dll'
    command=[str(UNITY/'NetCoreRuntime/dotnet.exe'),str(UNITY/'DotNetSdkRoslyn/csc.dll'),'-nologo','-target:exe','-nostdlib+',f'-out:{target}']
    command += [f'-r:{p}'for p in runtime.glob('*.dll')if(p.name.startswith('System.')and'.Native'not in p.name)or p.name in('mscorlib.dll','netstandard.dll')]
    command += [str(OUT/'Shims.cs'),str(OUT/'Comparison.cs'),str(ROOT/'recovery/bloody/Runtime/OriginalCharacterHUD.cs'),str(ROOT/'unity/JianghuUnity/Assets/Scripts/Json.cs')]
    compiled=subprocess.run(command,cwd=ROOT,text=True,capture_output=True);(OUT/'compile.txt').write_text(compiled.stdout+compiled.stderr)
    if compiled.returncode:print(compiled.stdout+compiled.stderr);raise SystemExit(compiled.returncode)
    target.with_suffix('.runtimeconfig.json').write_text(json.dumps(dict(runtimeOptions=dict(tfm='net8.0',framework=dict(name='Microsoft.NETCore.App',version=runtime.name)))))
    compared=subprocess.run([str(UNITY/'NetCoreRuntime/dotnet.exe'),str(target),str(oracle)],cwd=ROOT,text=True,capture_output=True)
    (OUT/'comparison.txt').write_text(compared.stdout+compared.stderr);print(compared.stdout,end='');print(compared.stderr,end='')
    if compared.returncode:raise SystemExit(compared.returncode)


if __name__=='__main__':main()
