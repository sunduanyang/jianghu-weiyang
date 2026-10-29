"""Compare original MatchHUD instructions with actual C# and source terms."""
import json
from pathlib import Path
import struct
import subprocess
from verify_bloody_ai_native import NativeAI
from verify_bloody_countdown import SHIMS as COUNTDOWN_SHIMS
from unicorn.arm64_const import UC_ARM64_REG_X0,UC_ARM64_REG_X1,UC_ARM64_REG_X2,UC_ARM64_REG_LR,UC_ARM64_REG_PC

ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'output/bloody-full-port-20261028/ai-check/ui';UNITY=Path('D:/Unity/Editors/6000.4.0f1/Editor/Data')


class NativeMatchHUD(NativeAI):
    TEXT=0x10013000;GRAPHICS=0x10014000
    def __init__(self):
        super().__init__()
        for a in(0x720d1d8,0x72120f8,0x7212238,0x7212868,0x720cf58):self.cpu.mem_write(a,struct.pack('<Q',0x10015000))
        self.cpu.mem_write(0x10015300,struct.pack('<Q',self.PROFILE));self.cpu.mem_write(self.PROFILE+0x38,struct.pack('<Q',1))
        self.cpu.mem_write(self.SELF+0x20,struct.pack('<2Q',self.TEXT,self.GRAPHICS));self.cpu.mem_write(self.TEXT,struct.pack('<Q',0x10016000));self.cpu.mem_write(0x10016000+0x558,struct.pack('<Q',0x1001f000))
    def _hook(self,cpu,a,size,data):
        if a==0x6acbd7c:cpu.reg_write(UC_ARM64_REG_X0,int(cpu.reg_read(UC_ARM64_REG_X0)==cpu.reg_read(UC_ARM64_REG_X1)))
        elif a==0x3647b90:self.setf(0,self.remaining)
        elif a==0x359470c:cpu.reg_write(UC_ARM64_REG_X0,0x10019000)
        elif a==0x32ed9f8:cpu.reg_write(UC_ARM64_REG_X0,0x10019100)
        elif a==0x5b86394:cpu.reg_write(UC_ARM64_REG_X0,int(cpu.reg_read(UC_ARM64_REG_X0)!=cpu.reg_read(UC_ARM64_REG_X1)))
        elif a==0x6aca694:self.active=bool(cpu.reg_read(UC_ARM64_REG_X1))
        elif a==0x6aca758:cpu.reg_write(UC_ARM64_REG_X0,int(self.active))
        elif a==0x35c03ec:cpu.reg_write(UC_ARM64_REG_X0,0x10019300)
        elif a==0x316ba2c:
            value=struct.unpack('<i',cpu.mem_read(cpu.reg_read(UC_ARM64_REG_X1),4))[0];self.boxes.append(value);cpu.reg_write(UC_ARM64_REG_X0,0x10018000+4*(len(self.boxes)-1))
        elif a==0x5b889a0:
            self.formatted=f'{self.boxes[-2]:02d}:{self.boxes[-1]:02d}';cpu.reg_write(UC_ARM64_REG_X0,0x10019200)
        elif a==0x1001f000:
            self.text={0x10019000:'',0x10019100:str(self.number),0x10019200:self.formatted}.get(cpu.reg_read(UC_ARM64_REG_X1),'INVALID')
        else:return super()._hook(cpu,a,size,data)
        cpu.reg_write(UC_ARM64_REG_PC,cpu.reg_read(UC_ARM64_REG_LR))
    def snapshot(self):return dict(active=self.active,text=self.text,last=struct.unpack('<i',self.cpu.mem_read(self.SELF+0x30,4))[0],timed=bool(self.cpu.mem_read(self.SELF+0x34,1)[0]))
    def enable(self,mode,number):
        self.number=number;self.text='OLD';self.active=True;self.formatted='';self.cpu.mem_write(0x10015300+0x94,struct.pack('<i',mode));self.call(0x36a0cf0,self.SELF);return self.snapshot()
    def update(self,remaining,previous,timed,active):
        self.number=0;self.remaining=remaining;self.boxes=[];self.text='OLD';self.active=active;self.formatted=''
        self.cpu.mem_write(self.SELF+0x30,struct.pack('<i?',previous,timed));self.call(0x36a0eb8,self.SELF);return self.snapshot()


SHIMS=COUNTDOWN_SHIMS.replace('public T GetComponent<T>()where T:Component=>null;','public T GetComponent<T>()where T:Component=>gameObject.GetComponent<T>();',1)
SHIMS=SHIMS.replace('public Transform transform;public bool activeSelf;','public object component;public Transform transform;public bool activeSelf;')
SHIMS=SHIMS.replace('public T GetComponent<T>()where T:Component=>null;','public T GetComponent<T>()where T:Component=>component as T;',1)
SHIMS=SHIMS.replace('public string name;public Vector3','public readonly List<Transform> children=new List<Transform>();public string name;public Vector3')
SHIMS=SHIMS.replace('Array.Empty<Transform>().GetEnumerator()','children.GetEnumerator()')
PROGRAM=r'''
using System;using System.IO;using System.Linq;using System.Reflection;using Jianghu;using UnityEngine;using TMPro;
class Program {
 static int checks;static void Check(bool b,string message){checks++;if(!b)throw new Exception(message);}
 static void Set(object o,string key,object v)=>o.GetType().GetField(key,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(o,v);
 static object Get(object o,string key)=>o.GetType().GetField(key,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o);
 static void Invoke(object o,string name)=>o.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(o,null);
 static void Assert(OriginalMatchHUD d,GameObject g,TMP_Text t,J e){Check(g.activeSelf==e["active"].B,"source graphics visibility");Check(t.text==e["text"].S,"source exact label "+t.text+" / "+e["text"].S);Check(d.LastSecond==e["last"].I,"source ceil cache");Check((bool)Get(d,"timed")==e["timed"].B,"source timed mode");}
 static void Main(string[]args){var oracle=J.Parse(File.ReadAllText(args[0]));var library=J.Parse(File.ReadAllText(args[1]));typeof(OriginalMatchHUD).Assembly.GetType("OriginalHUDTextLibrary").GetField("source",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,library);
 foreach(var c in oracle["enables"].A){var d=new OriginalMatchHUD();var g=new GameObject{activeSelf=true};var t=new TMP_Text{gameObject=new GameObject(),text="OLD"};Set(d,"initialized",true);Set(d,"graphicsContainer",g);Set(d,"matchText",t);Set(d,"gameMode",(Func<int>)(()=>c["mode"].I));Set(d,"modeNumber",(Func<int>)(()=>c["number"].I));Invoke(d,"OnEnable");Assert(d,g,t,c["expected"]);}
 foreach(var c in oracle["updates"].A){var d=new OriginalMatchHUD();var g=new GameObject{activeSelf=c["active"].B};var t=new TMP_Text{gameObject=new GameObject(),text="OLD"};Set(d,"initialized",true);Set(d,"graphicsContainer",g);Set(d,"matchText",t);Set(d,"timed",c["timed"].B);Set(d,"lastSecond",c["previous"].I);Set(d,"remaining",(Func<float>)(()=>(float)c["remaining"].N));Set(d,"timerFormat",library["timer_format"]["text"].S);Invoke(d,"Update");Assert(d,g,t,c["expected"]);}
 var root=new GameObject();var round=new TMP_Text{gameObject=new GameObject{activeSelf=true}};round.gameObject.component=round;round.transform.name="Round";var left=new TMP_Text{gameObject=new GameObject{activeSelf=true}};left.gameObject.component=left;left.transform.name="Left";root.transform.children.Add(round.transform);root.transform.children.Add(left.transform);
 var rows=new J(new System.Collections.Generic.List<J>{J.O("script","RoundEndView","data",J.O("playerXWonTheRoundTexts",new J(new System.Collections.Generic.List<J>{J.O("path","CanvasAndCamera/Round")}))),J.O("script","MatchEndView","data",J.O("opponentForfeitedText",J.O("path","CanvasAndCamera/Left")))});var helper=new OriginalVerdictText(root.transform,J.O("records",rows));
 foreach(var language in library["languages"].A){helper.LanguageCode=language["Code"].S;int i=library["languages"].A.IndexOf(language);string name="Player <b>A</b>";helper.Apply(false,name);Check(round.text==string.Format(library["terms"]["UI/ArenaMultiplayer/Player X Won The Round"]["translations"][i].S,name),"source round localization/newline");left.text="UNCHANGED";helper.Apply(true,name,5,0);Check(left.text=="UNCHANGED","ordinary MatchEnd no name write");helper.Apply(true,name,5,1);Check(left.text==string.Format(library["terms"]["UI/ArenaMultiplayer/Player X Left"]["translations"][i].S,name),"source forfeit localization/newline");left.text="RANKED";helper.Apply(true,name,4,1);Check(left.text=="RANKED","ranked forfeit source gate");}
 helper.LanguageCode="en";Check(helper.FormatRoundWinner("A")=="A\nwon the round!","exact English winner format");Check(helper.FormatPlayerLeft("A")=="A\nleft the match...","exact English forfeit format");helper.LanguageCode="unsupported";Check(helper.FormatRoundWinner("A")=="A\nwon the round!","source language fallback");
 Console.WriteLine("{\"native_matchhud_and_verdict_checks\":"+checks+",\"failures\":0}");}
}
'''


def main():
    OUT.mkdir(exist_ok=True);n=NativeMatchHUD();enables=[];updates=[]
    for mode in range(-1,9):
        for number in(0,7,123):enables.append(dict(mode=mode,number=number,expected=n.enable(mode,number)))
    for timed in(False,True):
        for active in(False,True):
            for previous in(-1,0,1,2,60,300):
                for remaining in(-3,-.1,0,.001,.99,1,1.001,59.99,60,60.01,299,299.01,300,300.01,6000.01):updates.append(dict(remaining=remaining,previous=previous,timed=timed,active=active,expected=n.update(remaining,previous,timed,active)))
    oracle=OUT/'native-matchhud-oracle.json';oracle.write_text(json.dumps(dict(enables=enables,updates=updates),indent=2))
    (OUT/'MatchHUDShims.cs').write_text(SHIMS);(OUT/'MatchHUDComparison.cs').write_text(PROGRAM)
    runtime=next((UNITY/'NetCoreRuntime/shared/Microsoft.NETCore.App').iterdir());target=OUT/'CompareNativeMatchHUD.dll'
    command=[str(UNITY/'NetCoreRuntime/dotnet.exe'),str(UNITY/'DotNetSdkRoslyn/csc.dll'),'-nologo','-target:exe','-nostdlib+',f'-out:{target}']
    command += [f'-r:{p}'for p in runtime.glob('*.dll')if(p.name.startswith('System.')and'.Native'not in p.name)or p.name in('mscorlib.dll','netstandard.dll')]
    command += [str(OUT/'MatchHUDShims.cs'),str(OUT/'MatchHUDComparison.cs'),str(ROOT/'recovery/bloody/Runtime/OriginalMatchHUD.cs'),str(ROOT/'recovery/bloody/Runtime/OriginalVerdictText.cs'),str(ROOT/'unity/JianghuUnity/Assets/Scripts/Json.cs')]
    subprocess.run(command,check=True,cwd=ROOT)
    target.with_suffix('.runtimeconfig.json').write_text(json.dumps(dict(runtimeOptions=dict(tfm='net8.0',framework=dict(name='Microsoft.NETCore.App',version=runtime.name)))))
    result=subprocess.run([str(UNITY/'NetCoreRuntime/dotnet.exe'),str(target),str(oracle),str(ROOT/'recovery/bloody/Runtime/Resources/OriginalHUDTextSource.json')],cwd=ROOT,text=True,capture_output=True)
    (OUT/'native-matchhud-comparison.txt').write_text(result.stdout+result.stderr);print(result.stdout,end='');print(result.stderr,end='')
    if result.returncode:raise SystemExit(result.returncode)


if __name__=='__main__':main()
