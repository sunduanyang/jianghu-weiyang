"""Run original ARM64 ranked-popup scalar paths against shipped C# math."""
import json
from pathlib import Path
import random
import struct
import subprocess
from verify_bloody_ai_native import NativeAI
from unicorn.arm64_const import UC_ARM64_REG_X0,UC_ARM64_REG_X1,UC_ARM64_REG_SP,UC_ARM64_REG_LR,UC_ARM64_REG_PC

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'output/bloody-full-port-20261028/ai-check/ui'
UNITY=Path('D:/Unity/Editors/6000.4.0f1/Editor/Data')


class NativePopup(NativeAI):
    def __init__(self):
        super().__init__()
        for a in (0x7218180,0x7218010,0x72117a0,0x72117a8,0x72117b0,0x7212e00,0x7218018,0x7218020,0x7218188,0x7218190,0x7218198,0x72181a0,0x7218028,0x7218030,0x7218038,0x7218040):
            self.cpu.mem_write(a,struct.pack('<Q',0x10015000))
        self.cpu.mem_write(0x10013000,struct.pack('<Q',0x10014000))
        self.cpu.mem_write(0x10014000+0x418,struct.pack('<Q',0x1001f000))
    def _hook(self,cpu,a,size,data):
        if a==0x316bb40:cpu.reg_write(UC_ARM64_REG_X0,0x10017000)
        elif a in (0x5d447a0,0x316b7cc,0x3411e0c):pass
        elif a==0x1001f000:self.setf(0,self.slider)
        else:return super()._hook(cpu,a,size,data)
        cpu.reg_write(UC_ARM64_REG_PC,cpu.reg_read(UC_ARM64_REG_LR))
    def visual(self,previous,target,slider,won):
        self.slider=slider
        self.cpu.mem_write(self.SELF+(0xf0 if won else 0xd8),struct.pack('<f',previous))
        self.cpu.mem_write(self.SELF+(0x50 if won else 0x48),struct.pack('<Q',0x10013000))
        self.cpu.mem_write(self.SELF+(0xd8 if won else 0xc0),bytes(8))
        self.cpu.reg_write(UC_ARM64_REG_X0,self.SELF);self.cpu.reg_write(UC_ARM64_REG_SP,0x10010000);self.cpu.reg_write(UC_ARM64_REG_LR,self.RETURN);self.setf(0,target)
        start,end=(0x36b9b58,0x36b9da0)if won else(0x36b1f30,0x36b21a0)
        self.cpu.emu_start(start,end,count=10000)
        if self.cpu.reg_read(UC_ARM64_REG_PC)!=end:raise RuntimeError('Native popup prefix failed')
        return self.sf(9)


SHIMS='''using System;namespace UnityEngine {public class Sprite {} public static class Mathf { public static float Min(float a,float b)=>Math.Min(a,b);public static float Max(float a,float b)=>Math.Max(a,b);public static float Abs(float a)=>Math.Abs(a);public static float Clamp01(float a)=>a<0?0:a>1?1:a;}}'''
PROGRAM='''using System;using System.IO;using Jianghu;class Program {static int Main(string[]args){var data=J.Parse(File.ReadAllText(args[0]));int checks=0;float maxError=0;foreach(var x in data["visual"].A){float result=OriginalUIPopupMath.VisualTarget((float)x["previous"].N,(float)x["target"].N,(float)x["slider"].N,x["won"].B);float error=Math.Abs(result-(float)x["expected"].N);maxError=Math.Max(error,maxError);if(error>0)throw new Exception("VisualTarget native mismatch "+x);checks++;}foreach(var x in data["nudge"].A){float result=OriginalUIPopupMath.NudgeFill((float)x["value"].N,x["force"].B);if(result!=(float)x["expected"].N)throw new Exception("NudgeFill native mismatch");checks++;}Console.WriteLine("{\\"native_popup_math_checks\\":"+checks+",\\"max_error\\":"+maxError+",\\"failures\\":0}");return 0;}}'''


def main():
    OUT.mkdir(exist_ok=True);n=NativePopup();visual=[];nudge=[]
    values=(-1,0,.001,.02,.03,.0499,.05,.1,.5,.949,.95,.97,.99,1,1.1)
    for won in(False,True):
        for previous in values:
            for target in values:
                for slider in (0,.049,.5,.951,1):
                    visual.append(dict(previous=previous,target=target,slider=slider,won=won,expected=n.visual(previous,target,slider,won)))
    rng=random.Random(941)
    for _ in range(300):
        p,t,s=[rng.uniform(-.2,1.2)for _ in range(3)];won=bool(rng.getrandbits(1));visual.append(dict(previous=p,target=t,slider=s,won=won,expected=n.visual(p,t,s,won)))
    for v in values:
        for force in(False,True):
            n.call(0x343cc74,int(force),floats=(v,));nudge.append(dict(value=v,force=force,expected=n.sf(0)))
    oracle=OUT/'native-popup-math-oracle.json';oracle.write_text(json.dumps(dict(visual=visual,nudge=nudge),indent=2))
    (OUT/'PopupMathShims.cs').write_text(SHIMS);(OUT/'PopupMathComparison.cs').write_text(PROGRAM)
    runtime=next((UNITY/'NetCoreRuntime/shared/Microsoft.NETCore.App').iterdir());target=OUT/'CompareNativePopupMath.dll'
    command=[str(UNITY/'NetCoreRuntime/dotnet.exe'),str(UNITY/'DotNetSdkRoslyn/csc.dll'),'-nologo','-target:exe','-nostdlib+',f'-out:{target}']
    command += [f'-r:{p}'for p in runtime.glob('*.dll')if(p.name.startswith('System.')and'.Native'not in p.name)or p.name in('mscorlib.dll','netstandard.dll')]
    command += [str(OUT/'PopupMathShims.cs'),str(OUT/'PopupMathComparison.cs'),str(ROOT/'recovery/bloody/Runtime/OriginalUIPopupResult.cs'),str(ROOT/'unity/JianghuUnity/Assets/Scripts/Json.cs')]
    subprocess.run(command,check=True,cwd=ROOT)
    target.with_suffix('.runtimeconfig.json').write_text(json.dumps(dict(runtimeOptions=dict(tfm='net8.0',framework=dict(name='Microsoft.NETCore.App',version=runtime.name)))))
    result=subprocess.run([str(UNITY/'NetCoreRuntime/dotnet.exe'),str(target),str(oracle)],cwd=ROOT,text=True,capture_output=True)
    (OUT/'native-popup-math-comparison.txt').write_text(result.stdout+result.stderr);print(result.stdout,end='');print(result.stderr,end='')
    if result.returncode:raise SystemExit(result.returncode)


if __name__=='__main__':main()
