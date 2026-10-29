"""Bounded original Practice death/event verification, no running-game injury.

The C# case compiles the current host OnOriginalDeath method verbatim, with
observable service boundaries. It does not claim a live death/restart replay or
an undiscovered HP-reset subscriber. No health or physics is set in a game.
"""
import hashlib
import json
from pathlib import Path
import re
import struct
import subprocess
from verify_bloody_ai_native import NativeAI
from unicorn.arm64_const import UC_ARM64_REG_X0,UC_ARM64_REG_LR,UC_ARM64_REG_PC

ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'output/bloody-full-port-20261028/ai-check/practice-death';UNITY=Path('D:/Unity/Editors/6000.4.0f1/Editor/Data')


class NativePracticeDeath(NativeAI):
    def __init__(self):
        super().__init__();self.cpu.mem_write(0x7215d78,struct.pack('<Q',0x10017000));self.cpu.mem_write(0x10017000,struct.pack('<Q',0x10017100));self.cpu.mem_write(0x100171b8,struct.pack('<Q',0x10017300))
        self.cpu.mem_write(0x10018018,struct.pack('<Q',0x10000200));self.calls=0
    def _hook(self,cpu,a,size,data):
        if a!=0x10000200:return super()._hook(cpu,a,size,data)
        self.calls+=1;cpu.reg_write(UC_ARM64_REG_PC,cpu.reg_read(UC_ARM64_REG_LR))
    def invoke(self,method,listener):
        self.calls=0;self.cpu.mem_write(0x10017348,struct.pack('<Q',0x10018000 if listener else 0));before=bytes(range(256));self.cpu.mem_write(self.SELF,before)
        self.call(method,self.SELF)
        return dict(calls=self.calls,verdict=self.cpu.reg_read(UC_ARM64_REG_X0)&255,rule_bytes_unchanged=bytes(self.cpu.mem_read(self.SELF,256))==before)


def method_source(text,name):
    found=re.search(r'void\s+'+name+r'\s*\([^)]*\)\s*\{',text);start=found.start();end=found.end();depth=1
    while depth:
        if text[end]=='{':depth+=1
        elif text[end]=='}':depth-=1
        end+=1
    return text[start:end]


PROGRAM=r'''
using System;
public sealed class OriginalRig {}
public sealed class OriginalPreview {
 public int OriginalState,GameMode,WinsPlayer=7,WinsEnemy=6,Finished;
 public OriginalRig enemy=new OriginalRig();public event Action OriginalHpResetRequested;
 void FinishOriginalRound(bool won){Finished++;if(won)WinsPlayer++;else WinsEnemy++;OriginalState=6;}
 METHOD
 public void InvokeDeath(OriginalRig rig)=>OnOriginalDeath(rig);
}
class Program {
 static void Main(){int checks=0;foreach(int mode in new[]{0,2,5})foreach(int state in new[]{0,1,2,3,4,5,6})foreach(bool listener in new[]{false,true}) {
  var host=new OriginalPreview{GameMode=mode,OriginalState=state};int events=0;bool sawPriorState=true;
  if(listener)host.OriginalHpResetRequested+=()=>{events++;sawPriorState=host.OriginalState==4&&host.WinsPlayer==7&&host.WinsEnemy==6&&host.Finished==0;};
  host.InvokeDeath(new OriginalRig());bool practice=mode==2&&state==4;bool ordinary=mode!=2&&state==4;
  if(events!=(practice&&listener?1:0)||!sawPriorState||host.Finished!=(ordinary?1:0)||host.WinsPlayer!=7||host.WinsEnemy!=(ordinary?7:6)||host.OriginalState!=(ordinary?6:state))throw new Exception("Actual host death routing mismatch mode="+mode+" state="+state+" listener="+listener);
  checks+=6;
 }
 Console.WriteLine("{\"actual_host_source_slice_checks\":"+checks+",\"failures\":0}");}
}
'''


def main():
    OUT.mkdir(exist_ok=True,parents=True);native=NativePracticeDeath();cases=[]
    for name,va in [('practice_verdict',0x3644448),('raise',0x351b034),('raise_alias_oee',0x351bc74),('raise_alias_mzm',0x351c0b4)]:
        for listener in(False,True):
            actual=native.invoke(va,listener);assert actual['calls']==int(listener)and actual['rule_bytes_unchanged']
            if name=='practice_verdict':assert actual['verdict']==0
            cases.append(dict(method=name,va=hex(va),listener=listener,observed=actual))
    source_path=ROOT/'recovery/bloody/Runtime/OriginalPreview.Match.cs';source=source_path.read_text(encoding='utf-8');body=method_source(source,'OnOriginalDeath')
    (OUT/'ActualHostDeathSlice.cs').write_text(PROGRAM.replace('METHOD',body),encoding='utf-8')
    runtime=next((UNITY/'NetCoreRuntime/shared/Microsoft.NETCore.App').iterdir());target=OUT/'ComparePracticeDeath.dll'
    cmd=[str(UNITY/'NetCoreRuntime/dotnet.exe'),str(UNITY/'DotNetSdkRoslyn/csc.dll'),'-nologo','-target:exe','-nostdlib+',f'-out:{target}']+[f'-r:{p}'for p in runtime.glob('*.dll')if(p.name.startswith('System.')and'.Native'not in p.name)or p.name in('mscorlib.dll','netstandard.dll')]+[str(OUT/'ActualHostDeathSlice.cs')]
    compiled=subprocess.run(cmd,cwd=ROOT,text=True,capture_output=True);(OUT/'compile.txt').write_text(compiled.stdout+compiled.stderr,encoding='utf-8')
    if compiled.returncode:print(compiled.stdout+compiled.stderr);raise SystemExit(compiled.returncode)
    target.with_suffix('.runtimeconfig.json').write_text(json.dumps(dict(runtimeOptions=dict(tfm='net8.0',framework=dict(name='Microsoft.NETCore.App',version=runtime.name)))))
    result=subprocess.run([str(UNITY/'NetCoreRuntime/dotnet.exe'),str(target)],cwd=ROOT,text=True,capture_output=True);(OUT/'comparison.txt').write_text(result.stdout+result.stderr,encoding='utf-8')
    report=dict(native=cases,host_source_sha256=hashlib.sha256(source.encode()).hexdigest(),host_slice=body,host_comparison=json.loads(result.stdout)if result.returncode==0 else None,
                source_subscriber='unresolved; canonical add/remove have no direct B/BL caller in the static ELF scan, not proof against reflection/indirect registration',
                source_death_order='CharacterVitality.Render caches Dead before cmu; cmu invokes expression, starts its death coroutine synchronously through stage0, then invokes OnDeath. This bridge never clears Dead/HP, cancels the coroutine or reconstructs joints/body parts.',
                live_death_pause_restart_verified=False,runtime_parity_verified=False)
    (OUT/'practice-death-check.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8');print(result.stdout,end='');print(result.stderr,end='')
    if result.returncode:raise SystemExit(result.returncode)


if __name__=='__main__':main()
