"""Original complete duf control flow with bounded Unity object services."""
import json,struct,subprocess
from pathlib import Path
from verify_bloody_ai_native import NativeAI
from verify_bloody_environment import UNITY
from unicorn.arm64_const import *
ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'output/bloody-full-port-20261028/environment-check/specials'
class NativeSpecials(NativeAI):
    GLOBALS=0x10017500;REGION=0x10014400;STAGE=0x10014000;NORMAL=0x10014100;THRONE=0x10014500
    def __init__(self):
        self.trace=[];super().__init__()
        for got in (0x720d1d8,0x72120f8):self.ptr(got,0x10016000)
        self.ptr(0x10016000,0x10016100);self.i32(0x100161e4,1)
        self.ptr(0x7212238,0x10017000);self.ptr(0x10017000,0x10017100);self.i32(0x100171e4,1);self.ptr(0x100171b8,self.GLOBALS)
        self.ptr(0x72122c0,0x10018000);self.ptr(0x10018000,0x10018100);self.ptr(0x100181b8,0x10018300);self.ptr(0x10018300,0x10018500);self.ptr(0x10018520,0x10018700)
        self.ptr(0x10015040,0x10015100)
        for i,offset in enumerate((0x78,0x80,0x88,0x90,0x98,0xa0)):self.ptr(self.SELF+offset,0x10012000+i*0x100)
        for i,offset in enumerate((0x70,0xb0,0xb8)):self.ptr(self.SELF+offset,0x10013000+i*0x100)
        self.ptr(self.SELF+0xc0,0x10014600);self.ptr(self.SELF+0xd0,self.REGION);self.i32(self.SELF+0xd8,48);self.f32(self.SELF+0xa8,8.2)
        self.ptr(self.STAGE+0x18,self.NORMAL);self.ptr(self.STAGE+0x30,0x10014200);self.ptr(self.REGION+0x90,self.THRONE)
        for i,offset in enumerate((0x10,0x18,0x20,0x28)):self.ptr(0x10014200+offset,0x10014300+i*0x20)
    def ptr(self,a,v):self.cpu.mem_write(a,struct.pack('<Q',v))
    def i32(self,a,v):self.cpu.mem_write(a,struct.pack('<I',v&0xffffffff))
    def f32(self,a,v):self.cpu.mem_write(a,struct.pack('<f',v))
    def _hook(self,cpu,a,size,data):
        x0=cpu.reg_read(UC_ARM64_REG_X0);x1=cpu.reg_read(UC_ARM64_REG_X1)
        if a in (0x6ac65ac,0x6aca44c,0x6ac6408,0x3eef3d8):pass
        elif a==0x50921a4:cpu.reg_write(UC_ARM64_REG_X0,0x10015000)
        elif a==0x6a680b4:cpu.reg_write(UC_ARM64_REG_X0,0x10015200)
        elif a==0x35c8d14:cpu.reg_write(UC_ARM64_REG_X0,0x10015300)
        elif a==0x6a67c3c:self.trace.append(['ambience',self.sf(0)])
        elif a==0x36c937c:cpu.reg_write(UC_ARM64_REG_X0,self.STAGE)
        elif a==0x32dd384:cpu.reg_write(UC_ARM64_REG_X0,self.stage)
        elif a==0x343e26c:
            self.cpu.mem_write(self.GLOBALS+0xe0,bytes([bool(x0)]));self.trace.append(['throne_flag',bool(x0)])
        elif a==0x4043ab4:self.trace.append(['prefab','throne' if x0==self.THRONE else 'ordinary'])
        elif a==0x36c356c:self.trace.append(['prop',(x0-0x10012000)//0x100])
        elif a in (0x6a8ed80,0x6aca694):pass
        elif a==0x6a8de98:cpu.mem_write(cpu.reg_read(UC_ARM64_REG_X8),struct.pack('<6f',101,202,303,4,5,6))
        elif a==0x6adba64:
            self.positions[x0]=tuple(self.sf(i) for i in range(3))
        elif a==0x6adb99c:
            for i,v in enumerate(self.positions[x0]):self.setf(i,v)
        elif a in (0x316bb50,0x316bb58):raise RuntimeError(f'Original duf threw at {a:x}')
        else:return super()._hook(cpu,a,size,data)
        cpu.reg_write(UC_ARM64_REG_PC,cpu.reg_read(UC_ARM64_REG_LR))
    def invoke(self,level_type,in_arena,mode,stage,previous):
        self.trace=[];self.positions={};self.stage=stage;self.i32(self.GLOBALS+0x108,level_type);self.i32(self.GLOBALS+0x94,mode);self.cpu.mem_write(self.GLOBALS+0xe0,bytes([previous]))
        self.call(0x36c9440,self.SELF,int(in_arena))
        return dict(is_throne_room=bool(self.cpu.mem_read(self.GLOBALS+0xe0,1)[0]),trace=self.trace.copy())
PROGRAM=r'''using System;using System.IO;using Jianghu;class Program {static int Main(string[]args){J oracle=J.Parse(File.ReadAllText(args[0])),catalog=J.Parse(File.ReadAllText(args[1]));int checks=0;foreach(var c in oracle["throne"].A){bool result=OriginalEnvironmentSpecials.ResolveThroneRoom(c["type"].I,c["in_arena"].B,c["mode"].I,c["stage"].I,c["previous"].B);if(result!=c["expected"]["is_throne_room"].B)throw new Exception("Original throne condition "+c);checks++;}foreach(var region in catalog["scene"].A){if(region["script"].S!="RegionConfiguration")continue;for(int type=0;type<5;type++)foreach(bool flag in new[]{false,true}){string key=OriginalEnvironmentSpecials.GraphicsKey(region,type,flag,out int index);if(index!=type||!region["assets"][key].Has)throw new Exception("Source special prefab binding");bool throne=key=="throneRoomGraphics";if(throne!=(type==4&&flag))throw new Exception("Source throne prefab gate");checks++;}}Console.WriteLine("{\"native_environment_special_checks\":"+checks+",\"failures\":0}");return 0;}}'''
def main():
    OUT.mkdir(parents=True,exist_ok=True);native=NativeSpecials();cases=[]
    for level_type in range(5):
        for arena in (False,True):
            for mode in (0,1,2,5,-1):
                for stage in (0,1,48,49,50,99):
                    for previous in (False,True):cases.append(dict(type=level_type,in_arena=arena,mode=mode,stage=stage,previous=previous,expected=native.invoke(level_type,arena,mode,stage,previous)))
    oracle=OUT/'native-environment-special-oracle.json';oracle.write_text(json.dumps(dict(throne=cases),indent=2))
    (OUT/'Comparison.cs').write_text(PROGRAM);runtime=next((UNITY/'NetCoreRuntime/shared/Microsoft.NETCore.App').iterdir());target=OUT/'CompareNativeEnvironmentSpecials.dll'
    cmd=[str(UNITY/'NetCoreRuntime/dotnet.exe'),str(UNITY/'DotNetSdkRoslyn/csc.dll'),'-nologo','-target:exe','-nostdlib+',f'-out:{target}']
    cmd += [f'-r:{p}' for p in runtime.glob('*.dll') if(p.name.startswith('System.')and'.Native'not in p.name)or p.name in ('mscorlib.dll','netstandard.dll')]
    cmd += [str(OUT/'Comparison.cs'),str(ROOT/'recovery/bloody/Runtime/OriginalEnvironmentSpecials.cs'),str(ROOT/'unity/JianghuUnity/Assets/Scripts/Json.cs')]
    result=subprocess.run(cmd,cwd=ROOT,text=True,capture_output=True);(OUT/'compile.txt').write_text(result.stdout+result.stderr)
    if result.returncode:print(result.stdout+result.stderr);raise SystemExit(result.returncode)
    target.with_suffix('.runtimeconfig.json').write_text(json.dumps(dict(runtimeOptions=dict(tfm='net8.0',framework=dict(name='Microsoft.NETCore.App',version=runtime.name)))))
    result=subprocess.run([str(UNITY/'NetCoreRuntime/dotnet.exe'),str(target),str(oracle),str(ROOT/'output/bloody-full-port-20261028/original-visual-catalog.json')],cwd=ROOT,text=True,capture_output=True)
    (OUT/'comparison.txt').write_text(result.stdout+result.stderr);print(result.stdout,end='');print(result.stderr,end='')
    if result.returncode:raise SystemExit(result.returncode)
if __name__=='__main__':main()
