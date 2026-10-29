"""Run recovered environment instructions with narrow Unity API hooks.

The APK, retained live guest, Unity project, and native instructions are not
modified. Hooked deterministic random values test call order, not Unity RNG.
"""
import json, math, struct, subprocess
from pathlib import Path
from verify_bloody_ai_native import NativeAI,f32
from unicorn.arm64_const import *
class SourceBoundsException(Exception):pass
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'output/bloody-full-port-20261028/environment-check'
UNITY=Path('D:/Unity/Editors/6000.4.0f1/Editor/Data')
class NativeEnvironment(NativeAI):
    RENDERER=0x10012000;TRANSFORM=0x10012100;PARENT=0x10012200
    GRADIENT=0x10013000;ROOT=0x10014000;ARRAY=0x10015000
    TYPECELL=0x10016000;KLASS=0x10016100;STATICS=0x10016500
    def __init__(self):
        self.trace=[];self.mode='prop';super().__init__()
        for got in (0x720d1d8,0x7212238):self.cpu.mem_write(got,struct.pack('<Q',self.TYPECELL))
        self.cpu.mem_write(self.TYPECELL,struct.pack('<Q',self.KLASS))
        self.cpu.mem_write(self.KLASS+0xe4,struct.pack('<I',1));self.cpu.mem_write(self.KLASS+0xb8,struct.pack('<Q',self.STATICS))
    def signed(self,x):return struct.unpack('<i',struct.pack('<I',x&0xffffffff))[0]
    def _hook(self,cpu,a,size,data):
        if self.mode=='bounds':
            if a==0x36c9b88:cpu.reg_write(UC_ARM64_REG_PC,self.RETURN);return
            if a==0x6a8de98:
                renderer=cpu.reg_read(UC_ARM64_REG_X0);i=(renderer-self.RENDERER)//0x100
                self.trace.append(['bounds',i]);cpu.mem_write(cpu.reg_read(UC_ARM64_REG_X8),struct.pack('<6f',101+i,201+i,301+i,self.extents[i],401+i,501+i))
            elif a in (0x6aca44c,0x6ac6408):pass
            elif a==0x6adba64:
                self.positions[cpu.reg_read(UC_ARM64_REG_X0)]=[self.sf(i) for i in range(3)]
                self.trace.append(['position',*self.positions[cpu.reg_read(UC_ARM64_REG_X0)]])
            elif a==0x6adb99c:
                for i,v in enumerate(self.positions[cpu.reg_read(UC_ARM64_REG_X0)]):self.setf(i,v)
            else:return super()._hook(cpu,a,size,data)
        elif a==0x6ac6408:cpu.reg_write(UC_ARM64_REG_X0,self.TRANSFORM)
        elif a==0x6adc610:cpu.reg_write(UC_ARM64_REG_X0,self.PARENT)
        elif a==0x6ade780:cpu.reg_write(UC_ARM64_REG_X0,self.child_count)
        elif a==0x6adeb14:cpu.reg_write(UC_ARM64_REG_X0,self.parent_sibling if cpu.reg_read(UC_ARM64_REG_X0)==self.PARENT else self.sibling)
        elif a==0x6abf964:self.trace.append(['seed',self.signed(cpu.reg_read(UC_ARM64_REG_X0))])
        elif a==0x6abfadc:
            lo=self.signed(cpu.reg_read(UC_ARM64_REG_X0));hi=self.signed(cpu.reg_read(UC_ARM64_REG_X1));self.trace.append(['int',lo,hi])
            cpu.reg_write(UC_ARM64_REG_X0,lo if hi<=lo else lo+min(hi-lo-1,int((hi-lo)*self.fraction)))
        elif a==0x6abfa9c:
            lo,hi=self.sf(0),self.sf(1);self.trace.append(['float',lo,hi]);self.setf(0,f32(lo+f32(f32(hi-lo)*self.fraction)))
        elif a==0x6aca1c8:cpu.reg_write(UC_ARM64_REG_X0,int(cpu.reg_read(UC_ARM64_REG_X0)!=0))
        elif a==0x6ac65ac:pass
        elif a==0x6aca694:self.trace.append(['active',bool(cpu.reg_read(UC_ARM64_REG_X1))])
        elif a==0x6a6fa74:self.trace.append(['sprite',cpu.reg_read(UC_ARM64_REG_X1)-0x10017000])
        elif a==0x6ab1e08:self.trace.append(['gradient',self.sf(0)])
        elif a==0x6a6fe8c:pass
        elif a==0x6a8f624:cpu.reg_write(UC_ARM64_REG_X0,40)
        elif a==0x6a8f6d8:self.trace.append(['sorting',self.signed(cpu.reg_read(UC_ARM64_REG_X1))])
        elif a==0x6a6fc20:self.setf(0,4);self.setf(1,5)
        elif a==0x6a6fcec:self.trace.append(['size',self.sf(0),self.sf(1)])
        elif a==0x6adb99c:
            for i,v in enumerate((1,2,3)):self.setf(i,v)
        elif a==0x6adba64:self.trace.append(['position',self.sf(0),self.sf(1),self.sf(2)])
        elif a==0x6a70014:self.trace.append(['flip',bool(cpu.reg_read(UC_ARM64_REG_X1))])
        elif a==0x316bb58:raise SourceBoundsException('Original array bounds exception')
        else:return super()._hook(cpu,a,size,data)
        cpu.reg_write(UC_ARM64_REG_PC,cpu.reg_read(UC_ARM64_REG_LR))
    def prop(self,c):
        self.mode='prop';self.trace=[];self.fraction=f32(c['fraction']);self.sibling=c['sibling'];self.parent_sibling=c['parent_sibling'];self.child_count=c['children']
        p=self.cpu.mem_write
        p(self.SELF+0x20,struct.pack('<i4xQQ?7xQQ4f??',c['chance'],self.RENDERER,self.ARRAY,c['can_flip'],self.GRADIENT,self.ROOT,-.7,1.2,-2.3,4.1,c['auto'],c['parent_seed']))
        p(self.ARRAY+0x18,struct.pack('<Q',c['count'])+b''.join(struct.pack('<Q',0x10017000+i) for i in range(c['count'])))
        p(self.STATICS+0x104,struct.pack('<i',c['current_seed']))
        seed=float(c['seed'])
        self.call(0x36c3438 if c['start'] else 0x36c356c,self.SELF,self.ARRAY,floats=(seed,));return self.trace.copy()
    def bounds(self,extents,divisor):
        self.mode='bounds';self.trace=[];self.extents=extents;self.positions={}
        for offset,pointer in ((0x70,self.RENDERER),(0xb0,self.RENDERER+0x100),(0xb8,self.RENDERER+0x200),(0x90,0x10018000),(0xa0,0x10018100)):
            self.cpu.mem_write(self.SELF+offset,struct.pack('<Q',pointer))
        self.cpu.mem_write(self.SELF+0xa8,struct.pack('<f',divisor))
        self.cpu.reg_write(UC_ARM64_REG_X19,self.SELF);self.cpu.reg_write(UC_ARM64_REG_X20,0x10018300);self.cpu.reg_write(UC_ARM64_REG_X21,0x10018200)
        self.call(0x36c9a40);return self.trace.copy()

def oracle():
    native=NativeEnvironment();cases=[]
    for start in (False,True):
        for fraction in (0,.5,.99):
            for chance in (0,3,10):
                for flip in (False,True):
                    for parent_seed,children in ((False,5),(False,1),(True,5)):
                        c=dict(start=start,auto=True,seed=-8.75,current_seed=2147483600 if parent_seed else -9,sibling=3,parent_sibling=7,children=children,chance=chance,can_flip=flip,parent_seed=parent_seed,count=4,fraction=fraction)
                        cases.append(dict(**c,trace=native.prop(c)))
    c=dict(cases[0]);c.pop('trace');c.update(start=True,auto=False);cases.append(dict(**c,trace=native.prop(c)))
    seeds=[]
    for seed in (-3e30,-2147483648,-123.75,0,3.99,2147483648,3e30,'Infinity','-Infinity','NaN'):
        c=dict(cases[0]);c.pop('trace');c.update(start=False,seed=seed)
        seeds.append(dict(seed=seed,expected=native.signed(native.prop(c)[0][1]-c['sibling'])))
    bounds=[]
    for extents in ((2,6,10),(101.5,.25,3.125),(0,0,0)):
        for divisor in (f32(8.2),2):bounds.append(dict(extents=extents,divisor=divisor,trace=native.bounds(extents,divisor)))
    stages=[]
    native.mode='seed'
    for region in (0,1,2,3,4,100000):
        for stage in (0,1,49,2147483647):
            native.call(0x36618a4,region,stage);stages.append(dict(region=region,stage=stage,expected=native.signed(native.cpu.reg_read(UC_ARM64_REG_X0))))
    empty=[]
    for chance in (0,10):
        c=dict(cases[0]);c.pop('trace');c.update(count=0,chance=chance)
        try:native.prop(c);raise AssertionError('Original empty sprite array unexpectedly returned')
        except SourceBoundsException:empty.append(dict(**c,trace=native.trace.copy(),throws_bounds=True))
    return dict(prop=cases,seeds=seeds,bounds=bounds,stage_seeds=stages,empty=empty,limits='Unity APIs and deterministic RNG returns are hooks; Unity Random implementation and full frame parity are not tested')

SHIMS=r'''using System;using System.Collections;using System.Collections.Generic;using System.Linq;using Jianghu;
public static class H {public static J A(params object[] a)=>new J(a.Select(o=>o as J??new J(o)).ToList());public static void T(params object[] a)=>UnityEngine.Random.trace.Add(A(a));}
public class OriginalVisualCatalog {public J Data;public T Get<T>(string id) where T:UnityEngine.Object=>null;public static UnityEngine.Gradient Gradient(J j)=>new UnityEngine.Gradient();}
public class OriginalTimeOfDay {public int TimeOfDay;public void ApplyEnvironmentContext(int r,int h,bool c,bool s,bool permit=true){}public void OnCustomizationFinished(){}}
public class OriginalWeather {public bool CanAlterWeatherType,isActiveAndEnabled=true;public void ApplyEnvironmentContext(int r,int seed,bool c,bool t){}public void ActivateWeatherType(){}}
public class OriginalGroundSource:UnityEngine.MonoBehaviour {public bool IsInitialized;public void ApplyEnvironmentContext(OriginalVisualCatalog c,int seed,bool snow){}}
public static class OriginalStageActors {public static void Prepare(OriginalVisualCatalog c,OriginalEnvironmentCustomizer.Context context,System.Func<int> hour,bool? throne=null){}public static void SetInstance(UnityEngine.Transform t){}}
public static class OriginalInteriorBloodSurface {public static void Attach(OriginalVisualCatalog c,UnityEngine.Transform t,string root){}}
public class OriginalSpookyExtras:UnityEngine.Component {public void Initialize(OriginalVisualCatalog c,UnityEngine.Transform t){}public void RefreshSpookyCoroutines(int h,bool s){}public void StopAllCoroutines(){}}
namespace UnityEngine {
public class Object {public string name;protected static GameObject Instantiate(GameObject p,Transform t,bool b)=>null;protected static void Destroy(GameObject g){}}
public class Component:Object {public Transform transform;public GameObject gameObject;public T GetComponent<T>() where T:Component=>gameObject.GetComponent<T>();}
public class MonoBehaviour:Component {}
public class GameObject:Object {public Transform transform;public bool activeSelf=true;Dictionary<Type,Component> components=new Dictionary<Type,Component>();public void SetActive(bool v){activeSelf=v;H.T("active",v);}public T GetComponent<T>() where T:Component=>components.TryGetValue(typeof(T),out var c)?(T)c:null;public T AddComponent<T>() where T:Component,new(){var c=new T{gameObject=this,transform=transform};components[typeof(T)]=c;return c;}}
public class Transform:Component,IEnumerable {public Transform parent;public int sibling;public int GetSiblingIndex()=>sibling;public List<Transform> children=new List<Transform>();public int childCount=>children.Count;Vector3 local;public Vector3 localPosition {get=>local;set{local=value;H.T("position",value.x,value.y,value.z);}}public Vector3 position;public Transform GetChild(int i)=>children[i];public Transform Find(string p)=>null;public void SetPositionAndRotation(Vector3 p,Quaternion q){}public IEnumerator GetEnumerator()=>children.GetEnumerator();public void Initial(Vector3 v)=>local=v;}
public struct Vector2 {public float x,y;public Vector2(float a,float b){x=a;y=b;}public static Vector2 operator+(Vector2 a,Vector2 b)=>new Vector2(a.x+b.x,a.y+b.y);}
public struct Vector3 {public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}public static Vector3 operator+(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}
public struct Quaternion {public static Quaternion Euler(float x,float y,float z)=>new Quaternion();}
public struct Bounds {public Vector3 extents;}
public struct Color {}
public class Gradient {public Color Evaluate(float t){H.T("gradient",t);return new Color();}}
public class Sprite:Object {public int id;}
public class SpriteRenderer:Component {Sprite _sprite;Vector2 _size;int _sorting;bool _flip;public Bounds bounds;public bool enabled;public Color color;public Sprite sprite {get=>_sprite;set{_sprite=value;H.T("sprite",value.id);}}public int sortingOrder {get=>_sorting;set{_sorting=value;H.T("sorting",value);}}public Vector2 size {get=>_size;set{_size=value;H.T("size",value.x,value.y);}}public bool flipX {get=>_flip;set{_flip=value;H.T("flip",value);}}public void Initial(){_size=new Vector2(4,5);_sorting=40;_flip=true;}}
public static class Random {public static float fraction;public static List<J> trace=new List<J>();public static void InitState(int seed)=>H.T("seed",seed);public static int Range(int lo,int hi){H.T("int",lo,hi);return lo+Math.Min(hi-lo-1,(int)((hi-lo)*fraction));}public static float Range(float lo,float hi){H.T("float",lo,hi);return lo+(hi-lo)*fraction;}}
}'''
PROGRAM=r'''using System;using System.IO;using System.Globalization;using Jianghu;using UnityEngine;class Program {
static int checks;static void Eq(J expected,J actual){if(expected.A.Count!=actual.A.Count)throw new Exception("trace item count "+expected+" "+actual);for(int i=0;i<expected.A.Count;i++){if(expected[i].A.Count!=actual[i].A.Count)throw new Exception("trace width");for(int j=0;j<expected[i].A.Count;j++){if(j==0||expected[i][j].value is bool){if(expected[i][j].S!=actual[i][j].S)throw new Exception("trace order "+expected[i]+" "+actual[i]);}else if(Math.Abs(expected[i][j].N-actual[i][j].N)>1e-5)throw new Exception("trace numeric "+expected[i]+" "+actual[i]);checks++;}}}
static int Main(string[]args){J oracle=J.Parse(File.ReadAllText(args[0]));foreach(var c in oracle["prop"].A){var parent=new Transform{sibling=c["parent_sibling"].I};for(int i=0;i<c["children"].I;i++)parent.children.Add(new Transform());var t=new Transform{parent=parent,sibling=c["sibling"].I};t.Initial(new Vector3(1,2,3));var renderer=new SpriteRenderer();renderer.Initial();var root=new Transform{gameObject=new GameObject()};var p=new OriginalEnviroProp{transform=t,chanesToDeleteOutOf10=c["chance"].I,spriteRenderer=renderer,possibleColors=new Gradient(),rootTransform=root,randomHeight=new Vector2(-.7f,1.2f),randomPosition=new Vector2(-2.3f,4.1f),AutoCustomize=c["auto"].B,SeedNotBasedOnSiblingIndex=c["parent_seed"].B,CurrentSeed=c["current_seed"].I,canBeFlipped=c["can_flip"].B};var sprites=new Sprite[c["count"].I];for(int i=0;i<sprites.Length;i++)sprites[i]=new Sprite{id=i};p.possibleSprites=sprites;UnityEngine.Random.trace.Clear();UnityEngine.Random.fraction=(float)c["fraction"].N;if(c["start"].B)p.RunAutoCustomize();else p.Customize(sprites,(float)c["seed"].N);Eq(c["trace"],new J(UnityEngine.Random.trace));}
foreach(var c in oracle["seeds"].A){float seed=float.Parse(c["seed"].S,CultureInfo.InvariantCulture);if(OriginalEnviroProp.NativeSeedInteger(seed)!=c["expected"].I)throw new Exception("seed cast "+c);checks++;}
foreach(var c in oracle["empty"].A){var parent=new Transform();parent.children.Add(new Transform());var t=new Transform{parent=parent,sibling=c["sibling"].I};var prop=new OriginalEnviroProp{transform=t,chanesToDeleteOutOf10=c["chance"].I,rootTransform=new Transform{gameObject=new GameObject()}};UnityEngine.Random.trace.Clear();UnityEngine.Random.fraction=(float)c["fraction"].N;bool threw=false;try{prop.Customize(Array.Empty<Sprite>(),(float)c["seed"].N);}catch(IndexOutOfRangeException){threw=true;}if(!threw)throw new Exception("empty array source bounds behavior");Eq(c["trace"],new J(UnityEngine.Random.trace));checks++;}
foreach(var c in oracle["stage_seeds"].A){if(OriginalEnvironmentCustomizer.GetSeedForStage(c["region"].I,c["stage"].I)!=c["expected"].I)throw new Exception("stage seed");checks++;}
foreach(var c in oracle["bounds"].A){float left=OriginalEnvironmentCustomizer.SideBuildingX((float)c["extents"][0].N,(float)c["extents"][1].N,(float)c["divisor"].N,false);float right=OriginalEnvironmentCustomizer.SideBuildingX((float)c["extents"][0].N,(float)c["extents"][2].N,(float)c["divisor"].N,true);if(Math.Abs(left-c["trace"][2][1].N)>1e-6||Math.Abs(right-c["trace"][5][1].N)>1e-6)throw new Exception("bounds formula");checks+=2;}
var data=J.Parse(File.ReadAllText(args[1]));var cat=new OriginalVisualCatalog{Data=data};
foreach(var record in data["scene"].A){if(record["script"].S!="RegionConfiguration")continue;for(int s=0;s<record["data"]["stages"].A.Count;s++)foreach(string field in new[]{"center","centerSnow","sides","sidesSnow"}){string key="stages["+s+"].bigBuildings."+field;int expected=record["data"]["stages"][s]["bigBuildings"][field].A.Count;var actual=OriginalEnvironmentSource.Sprites(cat,record,key);if(actual.Length!=expected)throw new Exception("nested source array "+key);if((field=="center"||field=="centerSnow"||s!=0)&&actual.Length==0)throw new Exception("Unexpected required empty stage sprites");checks++;}}
var env=new Transform{name="Environment"};foreach(var pair in data["hierarchy"].D){if(pair.Value["root"].S!="Environment")continue;var node=env;for(int i=0;i<pair.Value["indices"].A.Count;i++){int index=pair.Value["indices"][i].I;while(node.children.Count<=index)node.children.Add(new Transform());node=node.children[index];node.name=pair.Value["names"][i].S;}}var one=OriginalEnvironmentSource.IdentityNode(cat,"level0:17649",env,"Environment");var two=OriginalEnvironmentSource.IdentityNode(cat,"level0:18516",env,"Environment");var three=OriginalEnvironmentSource.IdentityNode(cat,"level0:22016",env,"Environment");if(object.ReferenceEquals(one,two)||object.ReferenceEquals(one,three)||object.ReferenceEquals(two,three))throw new Exception("duplicate Snow identity");checks+=3;
Console.WriteLine("{\"native_environment_checks\":"+checks+",\"failures\":0}");return 0;}}'''
def main():
    OUT.mkdir(exist_ok=True);data=oracle();oracle_path=OUT/'native-environment-oracle.json';oracle_path.write_text(json.dumps(data,indent=2))
    (OUT/'EnvironmentShims.cs').write_text(SHIMS);(OUT/'EnvironmentComparison.cs').write_text(PROGRAM)
    runtime=next((UNITY/'NetCoreRuntime/shared/Microsoft.NETCore.App').iterdir());target=OUT/'CompareNativeEnvironment.dll'
    command=[str(UNITY/'NetCoreRuntime/dotnet.exe'),str(UNITY/'DotNetSdkRoslyn/csc.dll'),'-nologo','-target:exe','-nostdlib+',f'-out:{target}']
    command += [f'-r:{p}' for p in runtime.glob('*.dll') if(p.name.startswith('System.') and '.Native' not in p.name)or p.name in ('mscorlib.dll','netstandard.dll')]
    command += [str(OUT/'EnvironmentShims.cs'),str(OUT/'EnvironmentComparison.cs'),str(ROOT/'recovery/bloody/Runtime/OriginalEnviroProp.cs'),str(ROOT/'recovery/bloody/Runtime/OriginalEnvironmentCustomizer.cs'),str(ROOT/'recovery/bloody/Runtime/OriginalEnvironmentCustomizer.Specials.cs'),str(ROOT/'recovery/bloody/Runtime/OriginalEnvironmentSpecials.cs'),str(ROOT/'unity/JianghuUnity/Assets/Scripts/Json.cs')]
    subprocess.run(command,check=True,cwd=ROOT)
    target.with_suffix('.runtimeconfig.json').write_text(json.dumps(dict(runtimeOptions=dict(tfm='net8.0',framework=dict(name='Microsoft.NETCore.App',version=runtime.name)))))
    result=subprocess.run([str(UNITY/'NetCoreRuntime/dotnet.exe'),str(target),str(oracle_path),str(ROOT/'output/bloody-full-port-20261028/original-visual-catalog.json')],cwd=ROOT,text=True,capture_output=True)
    (OUT/'native-environment-comparison.txt').write_text(result.stdout+result.stderr);print(result.stdout,end='');print(result.stderr,end='')
    if result.returncode:raise SystemExit(result.returncode)
    # Compile the rendering group against the existing read-only Unity build
    # assemblies. This does not sync files or open/import the active project.
    script_assemblies=ROOT/'unity/BloodyBastardsRecovery/Library/ScriptAssemblies'
    target_library=OUT/'OriginalEnvironmentCompile.dll'
    compile_command=command[:command.index(str(OUT/'EnvironmentShims.cs'))]
    compile_command=[c for c in compile_command if not c.startswith('-out:') and c!='-target:exe']+['-target:library','-nowarn:0436',f'-out:{target_library}']
    compile_command += [f'-r:{p}' for p in (UNITY/'Managed/UnityEngine').glob('*.dll')]
    compile_command += [f'-r:{p}' for p in script_assemblies.glob('*.dll')]
    compile_command += [str(ROOT/'recovery/bloody/Runtime'/name) for name in ('OriginalEnvironmentCustomizer.cs','OriginalEnvironmentCustomizer.Specials.cs','OriginalEnvironmentSpecials.cs','OriginalEnviroProp.cs','OriginalGroundSource.cs','OriginalTimeOfDay.cs','OriginalWeather.cs','OriginalVisualCatalog.cs','OriginalLightActivator.cs','OriginalStageActors.cs','OriginalStageGroundEnable.cs','OriginalStageLightEnable.cs','OriginalBystanderCustomization.cs','OriginalBystanderMovement.cs','OriginalBystandersController.cs','OriginalStageTweenHost.cs','OriginalInteriorBloodSurface.cs','OriginalEnvironmentSpecialSource.cs','OriginalSpookyExtras.cs','OriginalStageChristmasActivation.cs','OriginalStageReverb.cs')]
    response=OUT/'unity-environment-compile.rsp'
    response.write_text('\n'.join('"'+arg.replace('"','\\"')+'"' for arg in compile_command[2:]),encoding='utf-8-sig')
    compile_result=subprocess.run(compile_command[:2]+['@'+str(response)],cwd=ROOT,text=True,capture_output=True)
    (OUT/'unity-assembly-compile.txt').write_text(compile_result.stdout+compile_result.stderr)
    print(compile_result.stdout,end='');print(compile_result.stderr,end='')
    if compile_result.returncode:raise SystemExit(compile_result.returncode)
    print('UNITY_ENVIRONMENT_ASSEMBLY_COMPILE_OK')
if __name__=='__main__':main()
