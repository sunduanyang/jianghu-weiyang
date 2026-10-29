"""Execute the original ARM64 OnDrag and compare the shipped input adapter.

Unity icalls supply explicit screen geometry. The complete native OnDrag and
DynamicJoystick clamp/drift methods execute; native fixed-point instructions
also execute unchanged. A portable C# harness uses the actual adapter source.
Bound Canvas camera projection and UGUI event dispatch are outside this check.
"""
import json
from pathlib import Path
import random
import struct
import subprocess
from verify_bloody_ai_native import NativeAI, f32
from unicorn.arm64_const import UC_ARM64_REG_X0, UC_ARM64_REG_X1, UC_ARM64_REG_PC, UC_ARM64_REG_LR, UC_ARM64_REG_SP

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'output/bloody-full-port-20261028/ai-check/input'
UNITY=Path('D:/Unity/Editors/6000.4.0f1/Editor/Data')


class NativeJoystick(NativeAI):
    EVENT=0x10016000
    BG=0x10018000
    HANDLE=0x10018100
    CANVAS=0x10018200
    KLASS=0x10019000

    def __init__(self,scale,origin,fixed):
        super().__init__()
        self.scale=f32(scale);self.center=[f32(p/scale) for p in origin];self.handle=[0,0]
        for a,p in [(self.SELF,self.KLASS),(self.KLASS+0x1c8,0x36c68d4),
                    (self.SELF+0x30,self.BG),(self.SELF+0x38,self.HANDLE),(self.SELF+0x48,self.CANVAS),
                    (0x7211f58,0x10015000),(0x720d378,0x10015500),(0x10015500,0x10015600),(0x100156b8,0x10015800)]:
            self.cpu.mem_write(a,struct.pack('<Q',p))
        self.cpu.mem_write(0x100156e4,struct.pack('<I',1))
        self.cpu.mem_write(self.SELF+0x20,struct.pack('<ffI',1,0,0))
        self.cpu.mem_write(self.SELF+0x60,struct.pack('<f',1000 if fixed else 1))

    def _hook(self,cpu,a,size,data):
        if a==0x6de1784:cpu.reg_write(UC_ARM64_REG_X0,0)
        elif a==0x6adc6e0:
            self.setf(0,f32(self.center[0]*self.scale));self.setf(1,f32(self.center[1]*self.scale));self.setf(2,0)
        elif a==0x6de0e18:pass
        elif a==0x6adb640:self.setf(0,300);self.setf(1,300)
        elif a==0x6de1a88:self.setf(0,self.scale)
        elif a==0x6adb4ac:self.setf(0,self.center[0]);self.setf(1,self.center[1])
        elif a==0x6adb578:
            target=cpu.reg_read(UC_ARM64_REG_X0)
            if target==self.BG:self.center=[self.sf(0),self.sf(1)]
            elif target==self.HANDLE:self.handle=[self.sf(0),self.sf(1)]
            else:raise ValueError(hex(target))
        elif a==0x316b7cc:pass
        else:return super()._hook(cpu,a,size,data)
        cpu.reg_write(UC_ARM64_REG_PC,cpu.reg_read(UC_ARM64_REG_LR))

    def drag(self,p):
        self.cpu.mem_write(self.EVENT+0x144,struct.pack('<ff',*p))
        self.call(0x36cbf90,self.SELF,self.EVENT)
        return self.handle.copy()

    def quantized(self,p):
        cpu=self.cpu;stack=0x10010000
        cpu.reg_write(UC_ARM64_REG_SP,stack)
        cpu.mem_write(stack,struct.pack('<f12xf12x',*p))
        cpu.emu_start(0x36566a0,0x3656718,count=1000)
        x,y=struct.unpack('<II',cpu.mem_read(stack+0x28,8))
        return [f32(((n>>1)^-(n&1))/1024)for n in (x,y)]


SHIMS=r'''
using System;
namespace UnityEngine {
 public class Component {public Transform transform;public GameObject gameObject=new GameObject();public T GetComponent<T>()where T:Component=>null;public T GetComponentInParent<T>()where T:Component=>null;}
 public class Transform:Component {public Vector3 localPosition,position;public Transform Find(string p)=>null;}
 public class RectTransform:Transform {public Vector2 sizeDelta,pivot,anchorMin,anchorMax,anchoredPosition;}
 public class GameObject {public void SetActive(bool a){}}
 public struct Vector2 {
  public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}public static Vector2 zero=>new Vector2();
  public float magnitude=>MathF.Sqrt(x*x+y*y);public Vector2 normalized=>magnitude>1e-5f?new Vector2(x/magnitude,y/magnitude):zero;
  public static Vector2 operator*(Vector2 a,float b)=>new Vector2(a.x*b,a.y*b);
  public static Vector2 operator+(Vector2 a,Vector2 b)=>new Vector2(a.x+b.x,a.y+b.y);
  public static Vector2 operator-(Vector2 a,Vector2 b)=>new Vector2(a.x-b.x,a.y-b.y);
  public static Vector2 Scale(Vector2 a,Vector2 b)=>new Vector2(a.x*b.x,a.y*b.y);
  public static Vector2 ClampMagnitude(Vector2 a,float b){float sq=a.x*a.x+a.y*a.y;if(sq>b*b){float m=MathF.Sqrt(sq);return new Vector2(a.x/m*b,a.y/m*b);}return a;}
 }
 public struct Vector3 {public float x,y,z;public static explicit operator Vector2(Vector3 p)=>new Vector2(p.x,p.y);}
 public class Camera:Component{}
 public class Canvas:Component {public float scaleFactor;public RenderMode renderMode;public Camera worldCamera;public static void ForceUpdateCanvases(){}}
 public enum RenderMode {ScreenSpaceOverlay,ScreenSpaceCamera,WorldSpace}
 public class CanvasGroup:Component {public float alpha;}
 public class RectOffset {public RectOffset(int a,int b,int c,int d){}}
 public static class RectTransformUtility {public static bool ScreenPointToLocalPointInRectangle(RectTransform r,Vector2 p,Camera c,out Vector2 l){l=p;return false;}public static Vector2 WorldToScreenPoint(Camera c,Vector3 p)=>(Vector2)p;}
 public class TextAsset {public string text;}
 public static class Resources {public static T Load<T>(string p)where T:class=>null;}
 public static class Screen {public static int width=1920,height=1080;}
 public static class Mathf {
  public static float Min(float a,float b)=>MathF.Min(a,b);public static float Max(float a,float b)=>MathF.Max(a,b);
  public static float Pow(float a,float b)=>MathF.Pow(a,b);public static float Log(float a,float b)=>MathF.Log(a,b);
  public static float Lerp(float a,float b,float t)=>a+(b-a)*Math.Clamp(t,0,1);
 }
}
namespace UnityEngine.UI {
 public class CanvasScaler:UnityEngine.Component {public enum ScaleMode {ConstantPixelSize,ScaleWithScreenSize}public enum ScreenMatchMode {MatchWidthOrHeight,Expand,Shrink}public ScaleMode uiScaleMode;public ScreenMatchMode screenMatchMode;public UnityEngine.Vector2 referenceResolution;}
 public class HorizontalLayoutGroup:UnityEngine.Component {public UnityEngine.RectOffset padding;}
}
public class OriginalRig {public UnityEngine.Vector2 leftInput,rightInput;public bool leftActive,rightActive;public void SetOriginalHandInputs(UnityEngine.Vector2 a,UnityEngine.Vector2 b,bool c,bool d){leftInput=a;rightInput=b;rightActive=c;leftActive=d;}}
'''

PROGRAM=r'''
using System;using System.IO;using Jianghu;using UnityEngine;
class Program {
 static int count,failures;static float largest;
 static void Compare(float actual,float expected,string name){count++;float d=MathF.Abs(actual-expected);largest=MathF.Max(largest,d);if(d>.0002f){failures++;if(failures<12)Console.WriteLine(name+" actual="+actual+" expected="+expected+" delta="+d);}}
 static Vector2 V(J p)=>new Vector2((float)p[0].N,(float)p[1].N);
 static int Main(string[] args){var original=J.Parse(File.ReadAllText(args[0]));var data=J.Parse(File.ReadAllText(args[1]));
  foreach(var c in data["sequences"].A){var a=new OriginalInputAdapter(original);a.SetViewport(c["width"].I,c["height"].I);a.SetOptions(true,c["fixed"].B);a.PCMode=c["pc"].B;Compare(a.CanvasScale,(float)c["scale"].N,"CanvasScale");
   if(!a.PointerDown(17,V(c["origin"]),true))throw new Exception("Pointer capture");Compare(a.GetNub(true).magnitude,0,"Down resets handle");
   foreach(var p in c["points"].A){a.PointerDrag(17,V(p["position"]));Vector2 n=a.GetNub(true),q=a.GetNetworkNub(true);Compare(n.x,(float)p["handle"][0].N,"Native OnDrag X");Compare(n.y,(float)p["handle"][1].N,"Native OnDrag Y");Compare(q.x,(float)p["network"][0].N,"Native quantized X");Compare(q.y,(float)p["network"][1].N,"Native quantized Y");}
   var rig=new OriginalRig();a.ApplyToRig(rig);Compare(rig.rightInput.x,a.GetNetworkNub(true).x,"Native right nub field");if(!rig.leftActive||rig.rightActive)throw new Exception("Native cross hand flags");
   a.PointerUp(17);Compare(a.GetNub(true).magnitude,0,"Up resets handle");if(a.IsDown(true))throw new Exception("Up releases");
   a.PointerDown(9,V(c["origin"]),false);a.PointerDrag(9,V(c["points"][0]["position"]));a.ApplyToRig(rig);Compare(rig.leftInput.x,a.GetNetworkNub(false).x,"Native left nub field");if(!rig.rightActive||rig.leftActive)throw new Exception("Native cross hand flags right");a.Reset();if(a.IsDown(false))throw new Exception("Reset releases");
  }
  Console.WriteLine("{\"checked_values\":"+count+",\"failures\":"+failures+",\"maximum_absolute_error\":"+largest.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}");return failures==0?0:1;
 }
}
'''


def main():
    OUT.mkdir(exist_ok=True)
    source=json.loads((ROOT/'recovery/bloody/Runtime/Resources/OriginalInputSource.json').read_text())
    rng=random.Random(503);sequences=[]
    for width,height in [(1920,1080),(1280,720),(960,540),(2560,1080),(1280,1024)]:
        for pc in (False,True):
            ref=source['tablet_canvas']['pc_width'if pc else'tablet_width'if f32(width/height)<source['tablet_canvas']['aspect_threshold']else'standard_width']
            scale=f32(min(f32(width/ref),f32(height/1080)))
            for fixed in (False,True):
                origin=[f32(width*.25),f32(height*.5)];native=NativeJoystick(scale,origin,fixed);points=[]
                positions=[origin,[origin[0]+25,origin[1]+10],[origin[0]+500,origin[1]+300],[origin[0]+475,origin[1]+275],origin]
                positions += [[rng.randrange(0,width),rng.randrange(0,height)]for _ in range(30)]
                for p in positions:
                    handle=native.drag(p);points.append(dict(position=p,handle=handle,network=native.quantized(handle)))
                sequences.append(dict(width=width,height=height,pc=pc,fixed=fixed,origin=origin,scale=scale,points=points))
    oracle=OUT/'native-input-oracle.json';oracle.write_text(json.dumps(dict(sequences=sequences),indent=2))
    (OUT/'InputShims.cs').write_text(SHIMS);(OUT/'InputComparison.cs').write_text(PROGRAM)
    runtime=next((UNITY/'NetCoreRuntime/shared/Microsoft.NETCore.App').iterdir());target=OUT/'CompareNativeInput.dll'
    command=[str(UNITY/'NetCoreRuntime/dotnet.exe'),str(UNITY/'DotNetSdkRoslyn/csc.dll'),'-nologo','-target:exe','-nostdlib+',f'-out:{target}']
    command += [f'-r:{p}'for p in runtime.glob('*.dll')if(p.name.startswith('System.')and'.Native'not in p.name)or p.name in('mscorlib.dll','netstandard.dll')]
    command += [str(OUT/'InputShims.cs'),str(OUT/'InputComparison.cs'),str(ROOT/'recovery/bloody/Runtime/OriginalInputAdapter.cs'),str(ROOT/'unity/JianghuUnity/Assets/Scripts/Json.cs')]
    subprocess.run(command,check=True,cwd=ROOT)
    target.with_suffix('.runtimeconfig.json').write_text(json.dumps(dict(runtimeOptions=dict(tfm='net8.0',framework=dict(name='Microsoft.NETCore.App',version=runtime.name)))))
    run=subprocess.run([str(UNITY/'NetCoreRuntime/dotnet.exe'),str(target),str(ROOT/'recovery/bloody/Runtime/Resources/OriginalInputSource.json'),str(oracle)],cwd=ROOT,text=True,capture_output=True)
    (OUT/'native-input-comparison.txt').write_text(run.stdout+run.stderr);print(run.stdout,end='');print(run.stderr,end='')
    if run.returncode:raise SystemExit(run.returncode)


if __name__=='__main__':main()
