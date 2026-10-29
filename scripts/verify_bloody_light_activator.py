"""Execute original LightActivator.OnEnable and compare exact C# predicate."""
import json,struct,subprocess
from pathlib import Path
from verify_bloody_ai_native import NativeAI
from verify_bloody_environment import SHIMS,UNITY
from unicorn.arm64_const import *
ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'output/bloody-full-port-20261028/environment-check/light-activator'
class NativeActivator(NativeAI):
    def __init__(self):
        self.trace=[];super().__init__();self.cpu.mem_write(0x7212e08,struct.pack('<Q',0x10015000));self.cpu.mem_write(0x10015300,struct.pack('<Q',0x10017000))
    def _hook(self,cpu,a,size,data):
        if a==0x6ac65ac:cpu.reg_write(UC_ARM64_REG_X0,0x10018000)
        elif a==0x5f379e4:
            self.start=cpu.reg_read(UC_ARM64_REG_X0)&0xffffffff;self.count=cpu.reg_read(UC_ARM64_REG_X1)&0xffffffff
            self.start=self.start-2**32 if self.start>=2**31 else self.start
            if self.count>=2**31:raise RuntimeError('Unexpected invalid test Range')
            cpu.reg_write(UC_ARM64_REG_X0,0x10019000)
        elif a==0x3f258ec:
            hour=cpu.reg_read(UC_ARM64_REG_X1)&0xffffffff;cpu.reg_write(UC_ARM64_REG_X0,int(self.start<=hour<self.start+self.count))
        elif a==0x6aca694:self.active=bool(cpu.reg_read(UC_ARM64_REG_X1))
        else:return super()._hook(cpu,a,size,data)
        cpu.reg_write(UC_ARM64_REG_PC,cpu.reg_read(UC_ARM64_REG_LR))
    def invoke(self,lo,hi,inverted,hour):
        self.cpu.mem_write(self.SELF+0x20,struct.pack('<2f?',lo,hi,inverted));self.cpu.mem_write(0x10017020,bytes([hour&255]));self.active=None
        self.call(0x3444ad4,self.SELF);return dict(active=self.active,start=self.start,count=self.count)
PROGRAM=r'''using System;using System.IO;using Jianghu;class Program {static int Main(string[]args){J cases=J.Parse(File.ReadAllText(args[0]));int checks=0;foreach(var c in cases.A){bool result=OriginalLightActivator.SourceActive((float)c["min"].N,(float)c["max"].N,c["inverted"].B,c["hour"].I);if(result!=c["expected"]["active"].B)throw new Exception("Original LightActivator predicate "+c);checks++;}Console.WriteLine("{\"native_light_activator_checks\":"+checks+",\"failures\":0}");return 0;}}'''
def main():
    OUT.mkdir(parents=True,exist_ok=True);native=NativeActivator();cases=[]
    for lo,hi in ((4,22),(4.9,22.9),(0,24),(-4.9,3.3)):
        for inverted in (False,True):
            for hour in list(range(25))+[-1,255,256]:cases.append(dict(min=lo,max=hi,inverted=inverted,hour=hour,expected=native.invoke(lo,hi,inverted,hour)))
    oracle=OUT/'native-light-activator-oracle.json';oracle.write_text(json.dumps(cases,indent=2))
    shims=SHIMS.replace('public class MonoBehaviour:Component {}','public class MonoBehaviour:Component {public bool enabled=true;public bool isActiveAndEnabled=>enabled;}')
    shims+='\nnamespace UnityEngine {public class TextAsset{public string text;}public static class Resources {public static T Load<T>(string path)where T:class=>null;}}'
    (OUT/'Shims.cs').write_text(shims);(OUT/'Comparison.cs').write_text(PROGRAM)
    runtime=next((UNITY/'NetCoreRuntime/shared/Microsoft.NETCore.App').iterdir());target=OUT/'CompareNativeLightActivator.dll'
    cmd=[str(UNITY/'NetCoreRuntime/dotnet.exe'),str(UNITY/'DotNetSdkRoslyn/csc.dll'),'-nologo','-target:exe','-nostdlib+',f'-out:{target}']
    cmd += [f'-r:{p}' for p in runtime.glob('*.dll') if(p.name.startswith('System.')and'.Native'not in p.name)or p.name in ('mscorlib.dll','netstandard.dll')]
    cmd += [str(OUT/'Shims.cs'),str(OUT/'Comparison.cs'),str(ROOT/'recovery/bloody/Runtime/OriginalLightActivator.cs'),str(ROOT/'recovery/bloody/Runtime/OriginalEnviroProp.cs'),str(ROOT/'unity/JianghuUnity/Assets/Scripts/Json.cs')]
    result=subprocess.run(cmd,cwd=ROOT,text=True,capture_output=True);(OUT/'compile.txt').write_text(result.stdout+result.stderr)
    if result.returncode:print(result.stdout+result.stderr);raise SystemExit(result.returncode)
    target.with_suffix('.runtimeconfig.json').write_text(json.dumps(dict(runtimeOptions=dict(tfm='net8.0',framework=dict(name='Microsoft.NETCore.App',version=runtime.name)))))
    result=subprocess.run([str(UNITY/'NetCoreRuntime/dotnet.exe'),str(target),str(oracle)],cwd=ROOT,text=True,capture_output=True)
    (OUT/'comparison.txt').write_text(result.stdout+result.stderr);print(result.stdout,end='');print(result.stderr,end='')
    if result.returncode:raise SystemExit(result.returncode)
if __name__=='__main__':main()
