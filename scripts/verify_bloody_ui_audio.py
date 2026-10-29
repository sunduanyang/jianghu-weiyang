"""Compare bg.PlaySound's original ARM64 RNG/setting order with actual C#."""
import json
from pathlib import Path
import struct
import subprocess
from verify_bloody_ai_native import NativeAI
from unicorn.arm64_const import UC_ARM64_REG_X0,UC_ARM64_REG_X1,UC_ARM64_REG_LR,UC_ARM64_REG_PC

ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'output/bloody-full-port-20261028/ai-check/ui';UNITY=Path('D:/Unity/Editors/6000.4.0f1/Editor/Data')


class NativeAudio(NativeAI):
    SOURCE=0x10012000;SETTINGS=0x10013000;ARRAY=0x10014000;CLIP=0x10018000
    def __init__(self):
        super().__init__();self.trace=[]
        for a in(0x720d1d8,0x720d168,0x72120f8):self.cpu.mem_write(a,struct.pack('<Q',0x10015000))
    def _hook(self,cpu,a,size,data):
        if a==0x6acbd7c:cpu.reg_write(UC_ARM64_REG_X0,int(cpu.reg_read(UC_ARM64_REG_X0)==cpu.reg_read(UC_ARM64_REG_X1)))
        elif a==0x6ac5b48:cpu.reg_write(UC_ARM64_REG_X0,int(self.enabled))
        elif a==0x6abfadc:
            lo,hi=cpu.reg_read(UC_ARM64_REG_X0),cpu.reg_read(UC_ARM64_REG_X1);self.trace.append(['int',lo,hi]);cpu.reg_write(UC_ARM64_REG_X0,hi-1)
        elif a==0x6abfa9c:
            lo,hi=self.sf(0),self.sf(1);self.trace.append(['float',lo,hi]);self.setf(0,lo+(hi-lo)*.37)
        elif a==0x6a64dac:self.trace.append(['volume',self.sf(0)])
        elif a==0x6a64e84:self.trace.append(['pitch',self.sf(0)])
        elif a==0x6a65914:
            self.trace.append(['loop',cpu.reg_read(UC_ARM64_REG_X1)!=0]);cpu.reg_write(UC_ARM64_REG_PC,self.RETURN);return
        else:return super()._hook(cpu,a,size,data)
        cpu.reg_write(UC_ARM64_REG_PC,cpu.reg_read(UC_ARM64_REG_LR))
    def invoke(self,count,loop,enabled,offset):
        self.trace=[];self.enabled=enabled
        self.cpu.mem_write(self.SETTINGS+0x18,struct.pack('<Q?3x4f',self.ARRAY,loop,.2,.8,.7,1.3))
        self.cpu.mem_write(self.ARRAY+0x18,struct.pack('<Q',count)+struct.pack('<Q',self.CLIP)*count)
        self.call(0x3411e0c,self.SOURCE,self.SETTINGS,floats=(offset,));return self.trace.copy()


SHIMS=r'''using System;using System.Collections.Generic;using Jianghu;public static class H {public static J A(params object[] a){var list=new List<J>();foreach(var o in a)list.Add(o as J??new J(o));return new J(list);}}namespace UnityEngine {
public class AudioClip {} public static class Debug {public static void LogWarning(string s){}}
public static class Resources {public static T Load<T>(string p) where T:new()=>new T();}
public static class Random {public static readonly List<J> trace=new List<J>();public static int Range(int lo,int hi){trace.Add(H.A("int",lo,hi));return hi-1;}public static float Range(float lo,float hi){trace.Add(H.A("float",lo,hi));return lo+(hi-lo)*.37f;}}
public class AudioSource {public bool enabled=true;public float volume {set=>Random.trace.Add(H.A("volume",value));}public float pitch {set=>Random.trace.Add(H.A("pitch",value));}public bool loop {get;set;} public AudioClip clip;public void Play(){}public void PlayOneShot(AudioClip c){}}
}'''
PROGRAM=r'''using System;using System.IO;using Jianghu;using UnityEngine;class Program {static int Main(string[]args){J oracle=J.Parse(File.ReadAllText(args[0]));int checks=0;foreach(var c in oracle.A){int n=c["count"].I;J clips=H.A();for(int i=0;i<n;i++)clips.A.Add(J.O("key","clip"));J settings=J.O("audioClip",clips,"volume",J.O("min",.2,"max",.8),"pitch",J.O("min",.7,"max",1.3),"loop",c["loop"]);J library=J.O("audio_objects",J.O("object",settings),"audio",J.O("clip",J.O("resource","clip")));var source=new AudioSource{enabled=c["enabled"].B};UnityEngine.Random.trace.Clear();OriginalUIAudio.Play(source,J.O("kind","audio_object","key","object"),library,(float)c["offset"].N);if(source.enabled&&n>0)UnityEngine.Random.trace.Add(H.A("loop",source.loop));var expected=c["trace"].A;var actual=UnityEngine.Random.trace;if(expected.Count!=actual.Count)throw new Exception("Audio RNG call count");for(int i=0;i<expected.Count;i++)for(int k=0;k<expected[i].A.Count;k++){if(k==0||expected[i][k].S=="True"||expected[i][k].S=="False"){if(actual[i][k].S!=expected[i][k].S)throw new Exception("Audio call order");}else if(Math.Abs(actual[i][k].N-expected[i][k].N)>1e-6)throw new Exception("Audio bounds/value");checks++;}}Console.WriteLine("{\"native_ui_audio_checks\":"+checks+",\"failures\":0}");return 0;}}'''


def main():
    OUT.mkdir(exist_ok=True);n=NativeAudio();cases=[]
    for count in(0,1,2,7):
        for loop in(False,True):
            for enabled in(False,True):
                for offset in(-.2,0,.5):cases.append(dict(count=count,loop=loop,enabled=enabled,offset=offset,trace=n.invoke(count,loop,enabled,offset)))
    oracle=OUT/'native-ui-audio-oracle.json';oracle.write_text(json.dumps(cases,indent=2))
    (OUT/'UIAudioShims.cs').write_text(SHIMS);(OUT/'UIAudioComparison.cs').write_text(PROGRAM)
    runtime=next((UNITY/'NetCoreRuntime/shared/Microsoft.NETCore.App').iterdir());target=OUT/'CompareNativeUIAudio.dll'
    command=[str(UNITY/'NetCoreRuntime/dotnet.exe'),str(UNITY/'DotNetSdkRoslyn/csc.dll'),'-nologo','-target:exe','-nostdlib+',f'-out:{target}']
    command += [f'-r:{p}'for p in runtime.glob('*.dll')if(p.name.startswith('System.')and'.Native'not in p.name)or p.name in('mscorlib.dll','netstandard.dll')]
    command += [str(OUT/'UIAudioShims.cs'),str(OUT/'UIAudioComparison.cs'),str(ROOT/'recovery/bloody/Runtime/OriginalUIAudio.cs'),str(ROOT/'unity/JianghuUnity/Assets/Scripts/Json.cs')]
    subprocess.run(command,check=True,cwd=ROOT)
    target.with_suffix('.runtimeconfig.json').write_text(json.dumps(dict(runtimeOptions=dict(tfm='net8.0',framework=dict(name='Microsoft.NETCore.App',version=runtime.name)))))
    result=subprocess.run([str(UNITY/'NetCoreRuntime/dotnet.exe'),str(target),str(oracle)],cwd=ROOT,text=True,capture_output=True)
    (OUT/'native-ui-audio-comparison.txt').write_text(result.stdout+result.stderr);print(result.stdout,end='');print(result.stderr,end='')
    if result.returncode:raise SystemExit(result.returncode)


if __name__=='__main__':main()
