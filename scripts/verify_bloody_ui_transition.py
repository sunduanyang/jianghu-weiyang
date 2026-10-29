"""Compare actual transition-driver calls/yields with the original ARM64."""
import json
from pathlib import Path
import struct
import subprocess
from verify_bloody_ai_native import NativeAI
from unicorn.arm64_const import UC_ARM64_REG_X0,UC_ARM64_REG_X1,UC_ARM64_REG_X8,UC_ARM64_REG_PC,UC_ARM64_REG_LR

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'output/bloody-full-port-20261028/ai-check/ui'
UNITY=Path('D:/Unity/Editors/6000.4.0f1/Editor/Data')


class NativeTransition(NativeAI):
    OWNER=0x10012000
    ANIMATOR=0x10013000
    GO=0x10014000
    HANDLE=0x10018000
    LABELS={0x10019000:'Enter',0x10019100:'EnterDelayed',0x10019200:'Exit'}
    def __init__(self):
        super().__init__();self.trace=[];self.active=True;self.normalized=0;self.animator_enabled=True
        self.cpu.mem_write(0x72120f8,struct.pack('<Q',0x10015000))
        self.cpu.mem_write(self.OWNER+0x20,struct.pack('<QQQ',self.GO,self.ANIMATOR,0))
    def _hook(self,cpu,a,size,data):
        if a==0x6a565f4:cpu.mem_write(cpu.reg_read(UC_ARM64_REG_X8),struct.pack('<12xf20x',self.normalized))
        elif a==0x6aca694:
            was_active=self.active;self.active=bool(cpu.reg_read(UC_ARM64_REG_X1));self.trace.append('active:'+str(self.active).lower())
            if was_active and not self.active:
                cpu.reg_write(UC_ARM64_REG_X0,self.OWNER);cpu.reg_write(UC_ARM64_REG_PC,0x36b7ea8);return
        elif a in (0x6aca758,0x6aca80c):cpu.reg_write(UC_ARM64_REG_X0,int(self.active))
        elif a==0x6ac65ac:cpu.reg_write(UC_ARM64_REG_X0,self.GO)
        elif a==0x6ac5cc0:cpu.reg_write(UC_ARM64_REG_X0,int(self.animator_enabled and self.active))
        elif a==0x6a54534:self.trace.append('trigger:'+self.LABELS[cpu.reg_read(UC_ARM64_REG_X1)])
        elif a in (0x35c4244,0x35c42ec,0x35c404c):cpu.reg_write(UC_ARM64_REG_X0,{0x35c4244:0x10019000,0x35c42ec:0x10019100,0x35c404c:0x10019200}[a])
        elif a==0x6accc44:self.trace.append('stop')
        elif a==0x36b7e14:cpu.reg_write(UC_ARM64_REG_X0,0x10017000)
        elif a==0x6acc9e8:self.trace.append('start');cpu.reg_write(UC_ARM64_REG_X0,self.HANDLE)
        elif a==0x316b7cc:pass
        else:return super()._hook(cpu,a,size,data)
        cpu.reg_write(UC_ARM64_REG_PC,cpu.reg_read(UC_ARM64_REG_LR))
    def invoke(self,name,flag,active,pending,enabled=True):
        self.trace=[];self.active=active;self.animator_enabled=enabled
        self.cpu.mem_write(self.OWNER+0x30,struct.pack('<Q',self.HANDLE if pending else 0))
        self.call({'Enter':0x36b7448,'Exit':0x36b7524,'OnDisable':0x36b7ea8}[name],self.OWNER,int(flag))
        return dict(trace=self.trace.copy(),active=self.active,pending=bool(struct.unpack('<Q',self.cpu.mem_read(self.OWNER+0x30,8))[0]))
    def iterator(self,values):
        self.active=True;self.trace=[];self.cpu.mem_write(self.SELF+0x10,struct.pack('<I',0));self.cpu.mem_write(self.SELF+0x20,struct.pack('<Q',self.OWNER));steps=[]
        for v in values:
            self.normalized=v;self.call(0x36b7ff8,self.SELF);result=bool(self.cpu.reg_read(UC_ARM64_REG_X0));steps.append(dict(result=result,active=self.active))
            if not result:break
        return steps


SHIMS=r'''
using System;using System.Collections;using System.Collections.Generic;using System.Reflection;
namespace UnityEngine {
 public class GameObject {public static readonly List<string> trace=new List<string>();public bool activeSelf=true;public bool activeInHierarchy=>activeSelf;public MonoBehaviour driver;public void SetActive(bool b){bool old=activeSelf;activeSelf=b;trace.Add("active:"+b.ToString().ToLowerInvariant());if(old&&!b)driver?.GetType().GetMethod("OnDisable",BindingFlags.NonPublic|BindingFlags.Instance)?.Invoke(driver,null);}}
 public class Coroutine {public IEnumerator body;public bool stopped;}
 public class MonoBehaviour {public string name="source";public GameObject gameObject;public Coroutine StartCoroutine(IEnumerator e){GameObject.trace.Add("start");var c=new Coroutine{body=e};e.MoveNext();return c;}public void StopCoroutine(Coroutine c){c.stopped=true;GameObject.trace.Add("stop");}}
 public struct AnimatorStateInfo {public float normalizedTime;}
 public class Animator {public GameObject gameObject;public bool enabled=true;public float normalized;public bool isActiveAndEnabled=>enabled&&gameObject.activeInHierarchy;public void SetTrigger(string s){GameObject.trace.Add("trigger:"+s);}public AnimatorStateInfo GetCurrentAnimatorStateInfo(int layer)=>new AnimatorStateInfo{normalizedTime=normalized};}
}
'''

PROGRAM=r'''
using System;using System.Collections;using System.IO;using System.Linq;using System.Reflection;using Jianghu;using UnityEngine;
class Program {
 static int count;
 static void Check(bool b,string name){count++;if(!b)throw new Exception(name);}
 static int Main(string[]args){J data=J.Parse(File.ReadAllText(args[0]));
 foreach(J c in data["calls"].A){var go=new GameObject{activeSelf=c["active"].B};var animator=new Animator{gameObject=go,enabled=c["enabled"].B};var driver=new OriginalUITransitionAnimator{gameObject=go};go.driver=driver;driver.Initialize(go,animator,null);
  if(c["pending"].B)typeof(OriginalUITransitionAnimator).GetField("exitRoutine",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(driver,new Coroutine());GameObject.trace.Clear();
  if(c["name"].S=="Enter")driver.Enter(c["flag"].B);else if(c["name"].S=="Exit")driver.Exit(c["flag"].B);else typeof(OriginalUITransitionAnimator).GetMethod("OnDisable",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(driver,null);
  string[] actual=GameObject.trace.ToArray();
  Check(string.Join(",",actual)==string.Join(",",c["expected"]["trace"].A.Select(x=>x.S)),c["name"].S+" native call trace "+string.Join(",",actual));
  Check(go.activeSelf==c["expected"]["active"].B,"active result");Check(driver.Exiting==c["expected"]["pending"].B,"pending result");
 }
 foreach(J c in data["iterators"].A){var go=new GameObject();var animator=new Animator{gameObject=go};var driver=new OriginalUITransitionAnimator{gameObject=go};go.driver=driver;driver.Initialize(go,animator,null);var body=(IEnumerator)typeof(OriginalUITransitionAnimator).GetMethod("WaitForExit",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(driver,null);
  for(int i=0;i<c["expected"].A.Count;i++){animator.normalized=(float)c["values"][i].N;Check(body.MoveNext()==c["expected"][i]["result"].B,"native iterator yield");Check(go.activeSelf==c["expected"][i]["active"].B,"native iterator close time");}
 }
 // An Enter must cancel the pending exit; leaving that iterator alive would
 // close the newly re-entered page on the following completed state.
 {var go=new GameObject();var animator=new Animator{gameObject=go};var d=new OriginalUITransitionAnimator{gameObject=go};go.driver=d;d.Initialize(go,animator,null);d.Exit();var pending=(Coroutine)typeof(OriginalUITransitionAnimator).GetField("exitRoutine",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(d);d.Enter();Check(pending.stopped&&!d.Exiting&&go.activeSelf,"reentry cancels pending close");}
 Console.WriteLine("{\"native_transition_checks\":"+count+",\"failures\":0}");return 0;}
}
'''


def main():
    OUT.mkdir(exist_ok=True);native=NativeTransition();calls=[]
    for name in ['Enter','Exit','OnDisable']:
        for active in (False,True):
            for pending in(False,True):
                for flag in(False,True):
                    for enabled in(False,True):
                        calls.append(dict(name=name,active=active,pending=pending,flag=flag,enabled=enabled,
                                          expected=native.invoke(name,flag,active,pending,enabled)))
    iterators=[dict(values=v,expected=native.iterator(v))for v in [[0,.5,.9999,1],[1,1],[-.1,0,1],[5,5],[0,0,0,2],[.999,1]]]
    oracle=OUT/'native-transition-oracle.json';oracle.write_text(json.dumps(dict(calls=calls,iterators=iterators),indent=2))
    (OUT/'TransitionShims.cs').write_text(SHIMS);(OUT/'TransitionComparison.cs').write_text(PROGRAM)
    runtime=next((UNITY/'NetCoreRuntime/shared/Microsoft.NETCore.App').iterdir());target=OUT/'CompareNativeTransition.dll'
    command=[str(UNITY/'NetCoreRuntime/dotnet.exe'),str(UNITY/'DotNetSdkRoslyn/csc.dll'),'-nologo','-target:exe','-nostdlib+',f'-out:{target}']
    command += [f'-r:{p}'for p in runtime.glob('*.dll')if(p.name.startswith('System.')and'.Native'not in p.name)or p.name in('mscorlib.dll','netstandard.dll')]
    command += [str(OUT/'TransitionShims.cs'),str(OUT/'TransitionComparison.cs'),str(ROOT/'recovery/bloody/Runtime/OriginalUITransitionAnimator.cs'),str(ROOT/'unity/JianghuUnity/Assets/Scripts/Json.cs')]
    subprocess.run(command,check=True,cwd=ROOT)
    target.with_suffix('.runtimeconfig.json').write_text(json.dumps(dict(runtimeOptions=dict(tfm='net8.0',framework=dict(name='Microsoft.NETCore.App',version=runtime.name)))))
    result=subprocess.run([str(UNITY/'NetCoreRuntime/dotnet.exe'),str(target),str(oracle)],cwd=ROOT,text=True,capture_output=True)
    (OUT/'native-transition-comparison.txt').write_text(result.stdout+result.stderr);print(result.stdout,end='');print(result.stderr,end='')
    if result.returncode:raise SystemExit(result.returncode)


if __name__=='__main__':main()
