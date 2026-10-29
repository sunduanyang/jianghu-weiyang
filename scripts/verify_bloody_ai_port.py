"""Compare the shipped C# numeric source with the original ARM64 oracle.

The unmodified OriginalStrikeCurves class and actual scalar expressions are
compiled with portable Unity math shims, allowing a check without opening the
shared Unity project. Transform/physics and engine scheduling are not covered.
"""
import json
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "output/bloody-full-port-20261028/ai-check"
UNITY = Path("D:/Unity/Editors/6000.4.0f1/Editor/Data")

SHIMS = r'''
using System;
namespace UnityEngine {
public struct Vector2 {
 public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}
 public float magnitude=>MathF.Sqrt(x*x+y*y);
 public static Vector2 operator*(Vector2 v,float f)=>new Vector2(v.x*f,v.y*f);
}
public struct Vector3 {
 public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
 public static Vector3 operator-(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
}
public class Transform {public Vector3 lossyScale=new Vector3(1,1,1);public Vector3 InverseTransformDirection(Vector3 v)=>v;}
public static class Mathf {
 public const float Rad2Deg=57.29578f,Deg2Rad=.0174532924f;
 public static float Clamp01(float f)=>Clamp(f,0,1);
 public static float Clamp(float f,float a,float b)=>f<a?a:f>b?b:f;
 public static float Min(float a,float b)=>a<b?a:b;
 public static float Max(float a,float b)=>a>b?a:b;
 public static float Lerp(float a,float b,float t)=>a+(b-a)*Clamp01(t);
 public static float LerpAngle(float a,float b,float t){float d=Clamp(b-a-MathF.Floor((b-a)/360)*360,0,360);if(d>180)d-=360;return a+d*Clamp01(t);}
 public static float Atan2(float y,float x)=>MathF.Atan2(y,x);
 public static float Cos(float f)=>MathF.Cos(f);public static float Sin(float f)=>MathF.Sin(f);
 public static int RoundToInt(float f)=>(int)MathF.Round(f);
}
public static class Random {
 public static float fraction=.5f,lo,hi;
 public static float Range(float a,float b){lo=a;hi=b;return a+(b-a)*fraction;}
}
}
'''

PROGRAM = r'''
using System;
using System.IO;
using Jianghu;
using UnityEngine;
using Random=UnityEngine.Random;
class Program {
 static int checkedValues,failures;static float largestError;
 static void Compare(float a,float b,string name) {
  checkedValues++;float error=MathF.Abs(a-b);largestError=MathF.Max(error,largestError);
  if(error>0.00002f){failures++;if(failures<=12)Console.WriteLine(name+": actual="+a+" expected="+b+" delta="+error);}
 }
 static OriginalStrikeCurves.Profile Read(J v)=>new OriginalStrikeCurves.Profile {
  duration=(float)v[0].N,windupSnap=(float)v[1].N,windupEnd=(float)v[2].N,strikeEnd=(float)v[3].N,
  windupAngle=(float)v[4].N,windupRadius=(float)v[5].N,strikeFromAngle=(float)v[6].N,strikeToAngle=(float)v[7].N,
  strikeRadius=(float)v[8].N,followAngle=(float)v[9].N,followRadius=(float)v[10].N,holdExtension=v[11].B};
 static void Profile(OriginalStrikeCurves.Profile p,J e,string name) {
  float[] values={p.duration,p.windupSnap,p.windupEnd,p.strikeEnd,p.windupAngle,p.windupRadius,p.strikeFromAngle,p.strikeToAngle,p.strikeRadius,p.followAngle,p.followRadius};
  for(int i=0;i<values.Length;i++)Compare(values[i],(float)e[i].N,name+"["+i+"]");
  checkedValues++;if(p.holdExtension!=e[11].B)failures++;
 }
 static int Main(string[] args) {
  J data=J.Parse(File.ReadAllText(args[0]));
  foreach(J c in data["profiles"].A)Profile(OriginalStrikeCurves.Profile.Default((OriginalStrikeCurves.Kind)c["kind"].I,c["two"].B),c["values"],"Default");
  foreach(J c in data["curves"].A) {
   var r=new Vector2((float)c["rest"][0].N,(float)c["rest"][1].N);
   var v=OriginalStrikeCurves.Evaluate(Read(c["profile"]),(float)c["t"].N,r,(float)c["aim"].N);
   Compare(v.x,(float)c["expected"][0].N,"Evaluate.X t="+c["t"].S);Compare(v.y,(float)c["expected"][1].N,"Evaluate.Y t="+c["t"].S);
  }
  foreach(J c in data["varied"].A) {
   Random.fraction=(float)c["fraction"].N;
   var p=OriginalStrikeCurves.Profile.Default((OriginalStrikeCurves.Kind)c["kind"].I,c["two"].B).Varied((float)c["amount"].N);
   Profile(p,c["expected"],"Varied");
  }
  foreach(J c in data["twohand"].A) {
   OriginalStrikeCurves.Evaluate2H(Read(c["profile"]),(float)c["t"].N,(float)c["aim"].N,out var l,out var r,c["dominant"].B,(float)c["radius"].N,(float)c["angle"].N);
   Compare(l.x,(float)c["expected"][0].N,"TwoHand.LX");Compare(l.y,(float)c["expected"][1].N,"TwoHand.LY");
   Compare(r.x,(float)c["expected"][2].N,"TwoHand.RX");Compare(r.y,(float)c["expected"][3].N,"TwoHand.RY");
  }
  foreach(J c in data["scalars"].A) {
   var p=new ScalarProbe{aiAggressionUnit=(float)c["aggression"].N};
   Compare(p.Stamina,(float)c["stamina"].N,"Stamina");Compare(p.Abort,(float)c["abort"].N,"Abort");
   p.Reaction();Compare(Random.lo,(float)c["reactionBounds"][0].N,"Reaction.Min");Compare(Random.hi,(float)c["reactionBounds"][1].N,"Reaction.Max");
  }
  foreach(J c in data["pulses"].A) {
   uint p=OriginalExpressionPulse.Pack((byte)c["sequence"].I,c["clip"].I,(float)c["a"].N,(float)c["b"].N);
   checkedValues++;if(p!=(uint)c["expected"].N){failures++;if(failures<=12)Console.WriteLine("Pulse actual="+p+" expected="+c["expected"].S);}
  }
  Console.WriteLine("{\"checked_values\":"+checkedValues+",\"failures\":"+failures+",\"largest_error\":"+largestError.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}");
  return failures==0?0:1;
 }
}
'''


def main():
    source = (ROOT/"recovery/bloody/Runtime/OriginalHandAnimations.cs").read_text(encoding="utf-8-sig")
    curves = source[source.index("public static class OriginalStrikeCurves"):]
    expression=(ROOT/"recovery/bloody/Runtime/OriginalExpressions.cs").read_text(encoding="utf-8-sig")
    curves+='\n'+expression[expression.index("public static class OriginalExpressionPulse"):]
    (OUT/"CurvesUnderTest.cs").write_text("using UnityEngine;using Random=UnityEngine.Random;\n"+curves,encoding="utf-8")
    (OUT/"UnityMathShims.cs").write_text(SHIMS,encoding="utf-8")
    (OUT/"NativeComparison.cs").write_text(PROGRAM,encoding="utf-8")
    ai = (ROOT/"recovery/bloody/Runtime/OriginalRig.AI.cs").read_text(encoding="utf-8-sig")
    expressions = [re.search(r"    float "+name+r"[^\n]+",ai)[0] for name in ("AIReactionDelay", "AIStaminaThreshold", "AIAbortChance")]
    scalar = "using UnityEngine;using Random=UnityEngine.Random;\nclass ScalarProbe{public float aiAggressionUnit;bool aiRelentless=true;float OriginalAIReactionTime=1;float AIUnit=>Mathf.Clamp01(aiAggressionUnit);"+"\n".join(expressions)+"\npublic float Stamina=>AIStaminaThreshold;public float Abort=>AIAbortChance;public float Reaction()=>AIReactionDelay();}"
    (OUT/"ScalarsUnderTest.cs").write_text(scalar,encoding="utf-8")
    runtime = next((UNITY/"NetCoreRuntime/shared/Microsoft.NETCore.App").iterdir())
    target = OUT/"CompareNativeAI.dll"
    command=[str(UNITY/"NetCoreRuntime/dotnet.exe"),str(UNITY/"DotNetSdkRoslyn/csc.dll"),"-nologo","-target:exe","-nostdlib+",f"-out:{target}"]
    command += [f"-r:{p}" for p in runtime.glob("*.dll") if (p.name.startswith("System.") and ".Native" not in p.name) or p.name in ("mscorlib.dll","netstandard.dll")]
    command += [str(OUT/n) for n in ("CurvesUnderTest.cs","UnityMathShims.cs","NativeComparison.cs","ScalarsUnderTest.cs")]
    command += [str(ROOT/"unity/JianghuUnity/Assets/Scripts/Json.cs")]
    subprocess.run(command,check=True,cwd=ROOT)
    (OUT/"CompareNativeAI.runtimeconfig.json").write_text(json.dumps(dict(runtimeOptions=dict(tfm="net8.0",framework=dict(name="Microsoft.NETCore.App",version=runtime.name)))),encoding="utf-8")
    result=subprocess.run([str(UNITY/"NetCoreRuntime/dotnet.exe"),str(target),str(OUT/"native-ai-oracle.json")],cwd=ROOT,capture_output=True,text=True)
    print(result.stdout,end="")
    if result.stderr:print(result.stderr,file=__import__('sys').stderr)
    (OUT/"native-ai-comparison.txt").write_text(result.stdout+result.stderr,encoding="utf-8")
    if result.returncode:raise SystemExit(result.returncode)


if __name__ == "__main__":main()
