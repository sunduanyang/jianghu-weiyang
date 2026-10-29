"""Run original crowd Awake/Start/delayed normal appearance against C#."""
import json,struct,subprocess
from pathlib import Path
from verify_bloody_ai_native import NativeAI,f32
from verify_bloody_environment import SHIMS,UNITY
from unicorn.arm64_const import *
ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'output/bloody-full-port-20261028/environment-check/bystanders'
class NativeBystander(NativeAI):
    T=0x10012000;TORSO=0x10012100;COROUTINE=0x10013000
    def __init__(self):
        self.trace=[];super().__init__();self.positions={}
        for got in (0x720d1d8,0x7212238):self.ptr(got,0x10015000)
        self.ptr(0x72122c0,0x10016000);self.ptr(0x10016000,0x10016100);self.ptr(0x100161b8,0x10016300);self.ptr(0x10016300,0x10016500);self.ptr(0x10016520,0x10016700)
        self.ptr(self.SELF+0xd0,self.T);self.ptr(self.SELF+0xd8,self.TORSO)
        for i,offset in enumerate((0x28,0x30,0x38,0x40,0x48,0x50)):self.ptr(self.SELF+offset,0x10016800+i*0x20)
        for i,offset in enumerate((0x60,0x68,0x70,0x78,0x80,0x88)):self.ptr(self.SELF+offset,0x10017000+i*0x100)
        for i,(offset,count) in enumerate(zip((0xa0,0xa8,0xb0,0xb8,0xc0,0xc8),(4,2,1,6,5,4))):
            array=0x10018000+i*0x100;self.ptr(self.SELF+offset,array);self.i32(array+0x18,count)
            for j in range(count):self.ptr(array+0x20+j*8,0x10019000+i*100+j)
        self.ptr(self.SELF+0x58,0x10018c00);self.i32(0x10018c18,5)
        for i in range(5):self.ptr(0x10018c20+i*8,0x10017000+i*0x20)
        self.ptr(self.SELF+0x98,0x100168e0);self.ptr(self.SELF+0x90,0x10018d00);self.i32(0x10018d18,5)
        for i in range(5):self.ptr(0x10018d20+i*8,0x10019000+600+i)
        self.ptr(self.COROUTINE+0x20,self.SELF)
    def ptr(self,a,v):self.cpu.mem_write(a,struct.pack('<Q',v))
    def i32(self,a,v):self.cpu.mem_write(a,struct.pack('<I',v&0xffffffff))
    def _hook(self,cpu,a,size,data):
        x0=cpu.reg_read(UC_ARM64_REG_X0);x1=cpu.reg_read(UC_ARM64_REG_X1)
        if a==0x6ac6408:cpu.reg_write(UC_ARM64_REG_X0,self.T)
        elif a==0x6adeb14:cpu.reg_write(UC_ARM64_REG_X0,self.sibling)
        elif a==0x6ac65ac:pass
        elif a==0x6aca694:self.trace.append(['active',bool(x1)])
        elif a==0x6ac3330:cpu.reg_write(UC_ARM64_REG_X0,self.frame)
        elif a==0x6abf964:self.trace.append(['seed',struct.unpack('<i',struct.pack('<I',x0&0xffffffff))[0]])
        elif a==0x6abfadc:
            lo=x0&0xffffffff;hi=x1&0xffffffff;self.trace.append(['int',lo,hi]);cpu.reg_write(UC_ARM64_REG_X0,lo+min(hi-lo-1,int((hi-lo)*self.fraction)))
        elif a==0x6abfa9c:
            lo,hi=self.sf(0),self.sf(1);self.trace.append(['float',lo,hi]);self.setf(0,f32(lo+f32(f32(hi-lo)*self.fraction)))
        elif a==0x6adb99c:
            for i,v in enumerate(self.positions[x0]):self.setf(i,v)
        elif a==0x6adba64:self.positions[x0]=tuple(self.sf(i) for i in range(3));self.trace.append(['position',*self.positions[x0]])
        elif a==0x36c12f4:cpu.reg_write(UC_ARM64_REG_X0,self.COROUTINE)
        elif a==0x6acc9e8:self.trace.append(['coroutine'])
        elif a==0x6ab1e08:self.trace.append(['gradient',self.sf(0)])
        elif a==0x6a6fa74:self.trace.append(['sprite',x1-0x10019000])
        elif a==0x6a6fe8c:pass
        elif a==0x6a8ed80:self.trace.append(['renderer_enabled',bool(x1)])
        elif a in (0x316bb50,0x316bb58):raise RuntimeError(f'Original crowd source threw at {a:x}')
        else:return super()._hook(cpu,a,size,data)
        cpu.reg_write(UC_ARM64_REG_PC,cpu.reg_read(UC_ARM64_REG_LR))
    def invoke(self,seed,sibling,frame,chance,fraction,spooky=False):
        self.sibling=sibling;self.frame=frame;self.fraction=f32(fraction);self.trace=[];self.positions={self.T:(1,2,3),self.TORSO:(10,20,30)}
        self.i32(self.SELF+0x20,chance);self.i32(0x10015300+0x104,seed);self.cpu.mem_write(self.SELF+0xe0,struct.pack('<4f',-1,1,-.05,.05))
        self.cpu.mem_write(0x10016720,bytes([spooky]))
        self.call(0x36c1114,self.SELF);awake=self.trace.copy();self.trace=[]
        self.call(0x36c1174,self.SELF);start=self.trace.copy();self.trace=[]
        self.i32(self.COROUTINE+0x10,1);self.call(0x36c14bc,self.COROUTINE);appearance=self.trace.copy()
        return dict(awake=awake,start=start,appearance=appearance)
PROGRAM=r'''using System;using System.IO;using System.Reflection;using System.Collections.Generic;using Jianghu;using UnityEngine;class Program {
static int checks;static void Field(object o,string name,object value)=>o.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(o,value);static void Call(object o,string n)=>o.GetType().GetMethod(n,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(o,null);
static void Eq(J expected){var actual=UnityEngine.Random.trace;if(expected.A.Count!=actual.Count)throw new Exception("crowd trace length "+expected+" "+new J(actual));for(int i=0;i<actual.Count;i++)for(int j=0;j<actual[i].A.Count;j++){if(j==0||expected[i][j].value is bool){if(expected[i][j].S!=actual[i][j].S)throw new Exception("crowd call order");}else if(Math.Abs(expected[i][j].N-actual[i][j].N)>1e-5)throw new Exception("crowd value "+expected[i]+" "+actual[i]);checks++;}}
static J A(int n){var a=new List<J>();for(int i=0;i<n;i++)a.Add(new J(1));return new J(a);}static int Main(string[]args){var cases=J.Parse(File.ReadAllText(args[0]));foreach(var c in cases.A){OriginalStageActors.Context=new ActorContext{ArenaSeed=c["seed"].I,SpookyMode=c["spooky"].B};Time.frameCount=c["frame"].I;var t=new Transform{sibling=c["sibling"].I,gameObject=new GameObject()};t.Initial(new Vector3(1,2,3));var torso=new Transform();torso.Initial(new Vector3(10,20,30));var p=new OriginalBystanderCustomization{transform=t,gameObject=t.gameObject};J data=J.O("chanesToDeleteOutOf10",c["chance"].I,"randomPosition",J.O("min",-1,"max",1),"randomHeight",J.O("min",-.05,"max",.05));J record=J.O("data",data,"assets",J.O());var fields=new[]{"possibleHeadSprites","possibleEyesSprites","possibleHatSprites","possibleTopL1Sprites","possibleTopL2Sprites","possibleBottomSprites","spookySprites"};int[] lengths={4,2,1,6,5,4,5};for(int i=0;i<fields.Length;i++){data[fields[i]]=A(lengths[i]);for(int j=0;j<lengths[i];j++)record["assets"][fields[i]+"["+j+"]"]=new J((i*100+j).ToString());}Field(p,"record",record);Field(p,"data",data);Field(p,"rootTransform",t);Field(p,"torsoTransform",torso);foreach(string field in new[]{"head","eyes","hat","top1","top2","bottom"})Field(p,field,new SpriteRenderer());Field(p,"skin",new[]{new SpriteRenderer(),new SpriteRenderer(),new SpriteRenderer(),new SpriteRenderer(),new SpriteRenderer()});UnityEngine.Random.trace.Clear();UnityEngine.Random.fraction=(float)c["fraction"].N;Call(p,"Awake");Eq(c["expected"]["awake"]);UnityEngine.Random.trace.Clear();Call(p,"Start");Eq(c["expected"]["start"]);UnityEngine.Random.trace.Clear();p.ApplyOrdinaryAppearance();Eq(c["expected"]["appearance"]);}Console.WriteLine("{\"native_bystander_checks\":"+checks+",\"failures\":0}");return 0;}}'''
def main():
    OUT.mkdir(parents=True,exist_ok=True);native=NativeBystander();cases=[]
    for seed in (0,2147483647):
        for sibling in (0,3):
            for frame in (1,123):
                for chance in (0,1,10):
                    for fraction in (0,.5,.99):
                        for spooky in (False,True):cases.append(dict(seed=seed,sibling=sibling,frame=frame,chance=chance,fraction=fraction,spooky=spooky,expected=native.invoke(seed,sibling,frame,chance,fraction,spooky)))
    oracle=OUT/'native-bystander-oracle.json';oracle.write_text(json.dumps(cases,indent=2))
    shims=SHIMS.replace('public T Get<T>(string id) where T:UnityEngine.Object=>null;','public T Get<T>(string id) where T:UnityEngine.Object=>new UnityEngine.Sprite{id=int.Parse(id)} as T;')
    shims=shims.replace('public bool enabled;','public bool enabled{set{H.T("renderer_enabled",value);}}')
    shims=shims.replace('public class MonoBehaviour:Component {}','public class MonoBehaviour:Component {protected void StartCoroutine(System.Collections.IEnumerator e){H.T("coroutine");}}')
    shims=shims.replace('public static class OriginalStageActors {public static void Prepare(OriginalVisualCatalog c,OriginalEnvironmentCustomizer.Context context,System.Func<int> hour,bool? throne=null){}public static void SetInstance(UnityEngine.Transform t){}}','public sealed class ActorContext {public int ArenaSeed;public bool SpookyMode;} public static class OriginalStageActors {public static ActorContext Context=new ActorContext();public static OriginalVisualCatalog Catalog=new OriginalVisualCatalog();public static J Record(string id)=>null;public static UnityEngine.Transform InstanceRoot(UnityEngine.Transform t,string s)=>null;}')
    shims+='\nnamespace UnityEngine {public static class Time {public static int frameCount;}public class WaitForSecondsRealtime {public WaitForSecondsRealtime(float s){}}}'
    # These unrelated dummy host types only need a signature for compilation.
    shims=shims.replace('public class OriginalEnvironmentCustomizer', 'public class OriginalEnvironmentCustomizer')
    (OUT/'Shims.cs').write_text(shims);(OUT/'Comparison.cs').write_text(PROGRAM)
    runtime=next((UNITY/'NetCoreRuntime/shared/Microsoft.NETCore.App').iterdir());target=OUT/'CompareNativeBystander.dll'
    cmd=[str(UNITY/'NetCoreRuntime/dotnet.exe'),str(UNITY/'DotNetSdkRoslyn/csc.dll'),'-nologo','-target:exe','-nostdlib+',f'-out:{target}']+[f'-r:{p}'for p in runtime.glob('*.dll')if(p.name.startswith('System.')and'.Native'not in p.name)or p.name in ('mscorlib.dll','netstandard.dll')]
    cmd += [str(OUT/'Shims.cs'),str(OUT/'Comparison.cs'),str(ROOT/'recovery/bloody/Runtime/OriginalBystanderCustomization.cs'),str(ROOT/'recovery/bloody/Runtime/OriginalEnviroProp.cs'),str(ROOT/'unity/JianghuUnity/Assets/Scripts/Json.cs')]
    result=subprocess.run(cmd,cwd=ROOT,text=True,capture_output=True);(OUT/'compile.txt').write_text(result.stdout+result.stderr)
    if result.returncode:print(result.stdout+result.stderr);raise SystemExit(result.returncode)
    target.with_suffix('.runtimeconfig.json').write_text(json.dumps(dict(runtimeOptions=dict(tfm='net8.0',framework=dict(name='Microsoft.NETCore.App',version=runtime.name)))))
    result=subprocess.run([str(UNITY/'NetCoreRuntime/dotnet.exe'),str(target),str(oracle)],cwd=ROOT,text=True,capture_output=True)
    (OUT/'comparison.txt').write_text(result.stdout+result.stderr);print(result.stdout,end='');print(result.stderr,end='')
    if result.returncode:raise SystemExit(result.returncode)
if __name__=='__main__':main()
