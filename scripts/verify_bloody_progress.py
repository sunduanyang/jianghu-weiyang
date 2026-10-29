"""Run original campaign/wallet ARM64 with bounded engine/collection services.

No APK state, Unity project, save data or Ghidra session is modified. The original
reward, record-result and per-region instructions execute in Unicorn; C# under
test is the actual recovered service, not a second reward implementation.
"""
import json
from pathlib import Path
import struct
import subprocess
from verify_bloody_ai_native import NativeAI
from unicorn.arm64_const import UC_ARM64_REG_X0,UC_ARM64_REG_X1,UC_ARM64_REG_X2,UC_ARM64_REG_X3,UC_ARM64_REG_LR,UC_ARM64_REG_PC

ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'output/bloody-full-port-20261028/ai-check/progress';UNITY=Path('D:/Unity/Editors/6000.4.0f1/Editor/Data')
SOURCE=ROOT/'recovery/bloody/Runtime/Resources/OriginalProgressSource.json'


def signed(n):return n-0x100000000 if n>=0x80000000 else n


class NativeProgress(NativeAI):
    WALLET=0x10012000;CONFIG=0x10012800;WALLET_DATA=0x10012a00;REGION_CONFIG=0x10012b00
    DATA=0x10013000;CAMPAIGN=0x10013200;REGION=0x10013400;LEVELS=0x10013800;ITEMS=0x10014000
    HQ=0x10017200;WALLET_STATICS=0x10017700;PROGRESS_STATICS=0x10017c00
    def __init__(self):
        super().__init__();self.source=json.loads(SOURCE.read_text(encoding='utf-8'))
        for cell in(0x720d168,0x720d1d8,0x72120f8):self.ptr(cell,0x10015000)
        for cell,handle,statics in((0x7212238,0x10017000,self.HQ),(0x7212d38,0x10017500,self.WALLET_STATICS),(0x7212588,0x10017a00,self.PROGRESS_STATICS)):
            self.ptr(cell,handle);self.ptr(handle,handle+0x100);self.i32(handle+0x100+0xe4,1);self.ptr(handle+0x100+0xb8,statics)
        self.ptr(self.WALLET_STATICS,self.WALLET);self.ptr(self.WALLET+0x20,self.CONFIG);self.ptr(self.WALLET+0x28,0x10012c00);self.ptr(self.WALLET+0x38,self.WALLET_DATA);self.ptr(self.WALLET+0x40,0x10012d00)
        self.ptr(self.SELF+0x38,self.DATA);self.ptr(self.DATA+0x18,self.CAMPAIGN);self.ptr(self.REGION+0x18,self.LEVELS);self.ptr(self.LEVELS+0x10,self.ITEMS);self.i32(self.ITEMS+0x18,1024)
        for i,key in enumerate(('campaignNormalStarRewards','campaignMiniBossRewards','campaignBossRewards','campaignFinaBossRewards')):
            address=0x10018000+i*0x100;self.ptr(self.CONFIG+0x18+i*8,address);self.i32(address+0x18,3)
            for j,value in enumerate(self.source['wallet_protected_values'][key]):
                self.cpu.mem_write(address+0x20+j*16,struct.pack('<iiiI',value['hash'],value['hiddenValue'],value['currentCryptoKey'],0))
        self.obscured(self.CONFIG+0x58,self.source['wallet']['doubledMultiplier'])
        self.ptr(self.PROGRESS_STATICS+0x10,0x1001a000);self.ptr(0x1001a000+0x18,0x10000200)
        self.ptr(self.WALLET_STATICS+0x10,0x1001a100);self.ptr(0x1001a100+0x18,0x10000204)
        self.ptr(self.SELF+0x28,0x1001be00);self.ptr(0x1001be00+0x18,0x1001bf00);self.i32(0x1001bf00+0x18,len(self.source['rank_config']))
        for i,row in enumerate(self.source['rank_protected_values']):
            obj=0x1001c100+i*0x80;self.ptr(0x1001bf00+0x20+i*8,obj);value=row['RankStartsAt'];self.cpu.mem_write(obj+0x30,struct.pack('<iiiI',value['hash'],value['hiddenValue'],value['currentCryptoKey'],0))
        self.ptr(0x720d5b8,0x10015000);self.ptr(0x720e908,0x10015000)
        self.ptr(0x1001f000,0x1001f100);self.ptr(0x1001f100+0x178,0x5d13024);self.ptr(0x1001f100+0x1a8,0x5d13260)
        self.reset()
    def ptr(self,a,n):self.cpu.mem_write(a,struct.pack('<Q',n))
    def i32(self,a,n):self.cpu.mem_write(a,struct.pack('<I',n&0xffffffff))
    def read32(self,a):return struct.unpack('<i',self.cpu.mem_read(a,4))[0]
    def obscured(self,a,n):self.cpu.mem_write(a,struct.pack('<IIII',0,n&0xffffffff,0,0))
    def reset(self,level=1,region=1,levels=(),gold=0):
        self.errors=0;self.progress_events=0;self.wallet_events=0
        self.i32(self.HQ+0x98,region);self.obscured(self.HQ+0xac,level);self.i32(self.REGION_CONFIG+0x30,self.source['regions'][str(region)]['number_of_levels'])
        self.i32(self.WALLET_DATA+0x10,gold);self.i32(self.LEVELS+0x18,len(levels));self.i32(self.LEVELS+0x1c,0);self.cpu.mem_write(self.ITEMS+0x20,bytes(levels)+bytes(1024-len(levels)))
    def levels(self):return list(self.cpu.mem_read(self.ITEMS+0x20,self.read32(self.LEVELS+0x18)))
    def _hook(self,cpu,a,size,data):
        x0=cpu.reg_read(UC_ARM64_REG_X0);x1=cpu.reg_read(UC_ARM64_REG_X1);x2=cpu.reg_read(UC_ARM64_REG_X2)
        if a==0x6acbd7c:cpu.reg_write(UC_ARM64_REG_X0,int(x0==x1))
        elif a==0x32dd384:
            hidden=(x0>>32)&0xffffffff;key=x1&0xffffffff;cpu.reg_write(UC_ARM64_REG_X0,((hidden-key)^key)&0xffffffff)
        elif a==0x351dca4:cpu.reg_write(UC_ARM64_REG_X0,self.REGION_CONFIG)
        elif a==0x365dcec:cpu.reg_write(UC_ARM64_REG_X0,self.REGION) # Bounded existing-region service; lookup checked separately.
        elif a in(0x3445e2c,0x368223c):pass # er integrity guard boundary, no fabricated guard outcome.
        elif a==0x4416488:
            items=struct.unpack('<Q',cpu.mem_read(x0+0x10,8))[0];cpu.reg_write(UC_ARM64_REG_X0,cpu.mem_read(items+0x20+(x1&0xffffffff),1)[0])
        elif a==0x44164dc:
            items=struct.unpack('<Q',cpu.mem_read(x0+0x10,8))[0];cpu.mem_write(items+0x20+(x1&0xffffffff),bytes([x2&255]))
        elif a==0x44eebd0:
            items=struct.unpack('<Q',cpu.mem_read(x0+0x10,8))[0];cpu.reg_write(UC_ARM64_REG_X0,struct.unpack('<Q',cpu.mem_read(items+0x20+(x1&0xffffffff)*8,8))[0])
        elif a in(0x35a1324,0x35a13cc,0x35a1474,0x35a151c,0x35a238c,0x35a2434,0x35dd444,0x35dd4f0,0x35dd6f4,0x35ddd00,0x35a2194):cpu.reg_write(UC_ARM64_REG_X0,0x1001b000)
        elif a in(0x5b7e150,0x5b889a0,0x5b88a28):cpu.reg_write(UC_ARM64_REG_X0,0x1001b000)
        elif a==0x316b994:
            array=0x1001f400 if x1==56 else 0x1001b100
            cpu.reg_write(UC_ARM64_REG_X0,array);self.cpu.mem_write(array+0x20,bytes(int(x1)*4));self.i32(array+0x18,x1)
        elif a==0x316bb40:cpu.reg_write(UC_ARM64_REG_X0,0x1001f000)
        elif a==0x4f26114:self.i32(x0,x1);self.ptr(x0+8,x2)
        elif a==0x316ba2c:cpu.reg_write(UC_ARM64_REG_X0,0) # Logging boxes only; null avoids runtime cast dependency.
        elif a==0x316b7cc:pass
        elif a==0x6a7cd4c:self.errors+=1
        elif a==0x6a7c700:pass
        elif a==0x10000200:self.progress_events+=1
        elif a==0x10000204:self.wallet_events+=1
        elif a in(0x316bb50,0x316bb58):raise RuntimeError('Native source threw at '+hex(a))
        else:return super()._hook(cpu,a,size,data)
        cpu.reg_write(UC_ARM64_REG_PC,cpu.reg_read(UC_ARM64_REG_LR))
    def invoke(self,kind,level=1,region=1,levels=(),gold=0,stars=3,previous=0,doubled=False,index=0,stage=0):
        self.reset(level,region,levels,gold)
        if kind=='calculate':
            self.cpu.reg_write(UC_ARM64_REG_X2,int(doubled));self.call(0x3445e74,self.WALLET,stars)
        elif kind=='award':
            self.cpu.reg_write(UC_ARM64_REG_X2,stars);self.cpu.reg_write(UC_ARM64_REG_X3,int(doubled));self.call(0x34463a4,self.WALLET,previous)
        elif kind=='bonus':self.call(0x3446760,self.WALLET,stars)
        elif kind=='record':self.call(0x365f8fc,self.SELF,stars)
        elif kind=='complete':
            self.cpu.reg_write(UC_ARM64_REG_X2,index&0xffffffff);self.cpu.reg_write(UC_ARM64_REG_X3,stars);self.call(0x365f304,self.SELF,region)
        elif kind=='stagefull':self.call(0x3661ea4,self.REGION,stage&0xffffffff)
        elif kind=='unlocked':self.call(0x3661f88,self.REGION,stage&0xffffffff)
        elif kind=='stages':self.call(0x3661e68,self.REGION)
        elif kind=='total':self.call(0x3661dd0,self.REGION)
        elif kind=='getstars':self.call(0x3661f04,self.REGION,index&0xffffffff)
        elif kind=='getstage':self.call(0x3661554,level&0xffffffff)
        elif kind=='rank':self.call(0x3660270,self.SELF,level&0xffffffff)
        elif kind=='leveltype':self.call(0x366175c,region,stage&0xffffffff)
        elif kind=='gold':self.call(0x3445630,self.WALLET)
        elif kind=='completed':
            self.ptr(self.CAMPAIGN+0x10,0x10013c00);self.ptr(0x10013c00+0x10,0x10013d00);self.i32(0x10013c00+0x18,3);self.i32(0x10013d00+0x18,3)
            for i,(region_id,count)in enumerate(((1,len(levels)),(2,7),(1,91))):
                obj=0x10013400+i*0x100;lst=self.LEVELS if i==0 else 0x10013900+i*0x100
                self.i32(obj+0x10,region_id);self.ptr(obj+0x18,lst);self.i32(lst+0x18,count);self.ptr(0x10013d00+0x20+i*8,obj)
            self.call(0x3661528,self.SELF,index)
        else:raise ValueError(kind)
        word=self.cpu.reg_read(UC_ARM64_REG_X0)
        out=dict(result=signed(word&0xffffffff),gold=self.read32(self.WALLET_DATA+0x10),levels=self.levels(),errors=self.errors,progress_events=self.progress_events,wallet_events=self.wallet_events)
        if kind=='record':out['outcome']=dict(StarsEarned=word&255,PreviousStars=(word>>8)&255,NewStars=(word>>16)&255,Coins=signed((word>>32)&0xffffffff))
        return out


SHIMS=r'''
using System;namespace UnityEngine {public static class Resources {public static T Load<T>(string p)where T:class=>null;}public class TextAsset{public string text;}public static class Debug{public static int errors;public static void LogError(object m){errors++;}}}
public sealed class OriginalUIPopupResult {public int StarsEarned,PreviousStars,NewStars,Coins,CampaignProgressForNextButtons;public bool AdsRemoved;public Func<bool,int> CalculateReward;public Action AwardRewardBonus,RefreshNewItemsPips;public Func<int> GetNewItemsCount;public Action<Action,Action> RequestRewardedVideo;public Action<bool> SetGamepadNextEnabled;}
'''

PROGRAM=r'''
using System;using System.IO;using System.Linq;using Jianghu;using UnityEngine;
class Program {
 static int checks,local;static void Check(bool b,string m,bool l=false){if(l)local++;else checks++;if(!b)throw new Exception(m);}
 static void Main(string[]args){var oracle=J.Parse(File.ReadAllText(args[0]));var source=J.Parse(File.ReadAllText(args[1]));
 foreach(var c in oracle["cases"].A){var p=c["arguments"];int region=p["region"].I;var levels=p["levels"];var regions=new System.Collections.Generic.List<J>{J.O("RegionId",region,"Levels",levels)};if(c["kind"].S=="completed"){regions.Add(J.O("RegionId",2,"Levels",new J(Enumerable.Repeat(new J(3),7).ToList())));regions.Add(J.O("RegionId",1,"Levels",new J(Enumerable.Repeat(new J(1),91).ToList())));}var snapshot=J.O("Progress",J.O("TutorialPassed",false,"Campaign",J.O("Regions",new J(regions))),"Wallet",J.O("Gold",p["gold"].I));var state=OriginalLocalProgress.FromSource(source,snapshot);state.SetCurrentCampaignRegion(region);state.SetCurrentCampaignLevel(p["level"].I);int pe=0,we=0;state.OnProgressChanged+=()=>pe++;state.OnWalletChanged+=()=>we++;Debug.errors=0;int result=0;OriginalCampaignOutcome outcome=null;
 switch(c["kind"].S){case "calculate":result=state.CalculateCampaignReward((byte)p["stars"].I,p["doubled"].B);break;case "award":result=state.AwardCampaignReward((byte)p["previous"].I,(byte)p["stars"].I,p["doubled"].B);break;case "bonus":result=state.AwardCampaignRewardBonus((byte)p["stars"].I);break;case "record":outcome=state.RecordCampaignWin((byte)p["stars"].I);break;case "complete":result=state.OnLevelCompleted(region,p["index"].I,(byte)p["stars"].I);break;case "stagefull":result=state.IsStageFullyCompleted(region,p["stage"].I)?1:0;break;case "unlocked":result=state.GetLevelsUnlockedInStage(region,p["stage"].I);break;case "stages":result=state.GetStagesUnlockedCount(region);break;case "total":result=state.GetTotalStarsEarned(region);break;case "getstars":result=state.GetStarsForLevel(region,p["index"].I);break;case "getstage":result=OriginalLocalProgress.GetStageForLevel(p["level"].I);break;case "rank":result=state.GetRankForPoints(p["level"].I);break;case "leveltype":state.SetCurrentCampaignStage(p["stage"].I);result=state.LevelType;break;case "gold":result=state.Gold;break;case "completed":result=state.GetCompletedLevelsForRegion(p["index"].I);break;default:throw new Exception(c["kind"].S);}
 var e=c["expected"];string label=c["kind"].S+" "+p; if(outcome!=null){var o=e["outcome"];Check(outcome.StarsEarned==o["StarsEarned"].I&&outcome.PreviousStars==o["PreviousStars"].I&&outcome.NewStars==o["NewStars"].I&&outcome.Coins==o["Coins"].I,"native result struct "+label);}else Check(result==e["result"].I,"native return "+label+" got="+result+" want="+e["result"]);
 Check(state.Gold==e["gold"].I,"native wallet "+label);Check(J.Write(state.Snapshot()["Progress"]["Campaign"]["Regions"][0]["Levels"])==J.Write(e["levels"]),"native retained byte list "+label);Check(Debug.errors==e["errors"].I,"native error count "+label);Check(pe==e["progress_events"].I&&we==e["wallet_events"].I,"native mutation events "+label);
 }
 var s=OriginalLocalProgress.FromSource(source);Check(s.Gold==0&&!s.TutorialPassed&&s.GameMode==0&&s.Region==1&&s.Stage==0&&s.Level==1&&s.Seed==0&&s.LevelType==0&&s.ShopRegion==1,"source release defaults",true);
 int changed=0;s.OnProgressChanged+=()=>changed++;s.MarkTutorialPassed();s.MarkTutorialPassed();s.MarkTutorialNotPassed();s.MarkTutorialNotPassed();Check(changed==2&&!s.TutorialPassed,"tutorial flag event guard",true);
 foreach(float hp in new[]{-1f,0f,33f,33.001f,66f,66.001f,100f,float.NaN,float.PositiveInfinity,float.NegativeInfinity}){byte expected=hp<=33? (byte)1:hp<=66?(byte)2:(byte)3;Check(OriginalLocalProgress.StarsForVictory(hp)==expected,"absolute health star threshold",true);}
 var r=s.RecordVictory(33);Check(r.Coins==100&&r.GoldAwarded==100&&s.Gold==100,"native caller record then wallet sequence",true);r=s.RecordVictory(80);Check(r.PreviousStars==1&&r.NewStars==2&&r.Coins==200&&r.GoldAwarded==300&&s.Gold==400,"reward difference versus whole-tier award",true);r=s.RecordVictory(33);Check(r.PreviousStars==3&&r.NewStars==0&&r.Coins==-200&&r.GoldAwarded==0&&s.Gold==400,"worse replay preserves max and negative result coins",true);
 var popup=s.CreatePopupResult(r,null);bool canceled=false;popup.RequestRewardedVideo(()=>throw new Exception("invented ad completion"),()=>canceled=true);Check(canceled&&!popup.AdsRemoved&&popup.GetNewItemsCount==null&&popup.RefreshNewItemsPips==null,"missing services never fabricate ad/inventory data",true);
 string path=Path.GetFullPath(args[2]);if(File.Exists(path))File.Delete(path);s=OriginalLocalProgress.OpenFromSource(source,path);s.MarkTutorialPassed();s.SetCurrentCampaignRegion(2);s.SetCurrentCampaignStage(49);s.SetCurrentCampaignLevel(999);s.GameMode=5;s.SetCurrentShopRegion(4);s.RecordVictory(100);s.Save();var loaded=OriginalLocalProgress.OpenFromSource(source,path);Check(loaded.TutorialPassed&&loaded.Gold==300&&loaded.Region==2&&loaded.Stage==49,"isolated persisted progress wallet prefs",true);Check(loaded.GameMode==0&&loaded.Level==1&&loaded.Seed==0&&loaded.LevelType==0&&loaded.ShopRegion==1,"native static session defaults on next launch",true);
 var detached=loaded.Snapshot();detached["Wallet"]["Gold"]=new J(555);Check(loaded.Gold==300,"save snapshot detached from private wallet",true);Check(OriginalLocalProgress.GetLevelType(1,49)==4,"source stage49 special type",true);Check(loaded.GetCurrentRank()==0,"source default saved rank points maps to rank index0",true);
 Console.WriteLine("{\"native_progress_wallet_checks\":"+checks+",\"local_adapter_checks\":"+local+",\"failures\":0}");
 }
}
'''


def main():
    OUT.mkdir(parents=True,exist_ok=True);n=NativeProgress();cases=[]
    default=dict(level=1,region=1,levels=[],gold=0,stars=3,previous=0,doubled=False,index=0,stage=0)
    def add(kind,**changes):
        args=default|changes;cases.append(dict(kind=kind,arguments=args,expected=n.invoke(kind,**args)))
    for region in(1,2,3,4):
        for level in(1,9,10,20,499,500,501,990,999,1000):
            for stars in(0,1,2,3,4,255):
                for doubled in(False,True):add('calculate',region=region,level=level,stars=stars,doubled=doubled)
    for level in(1,10,500,1000):
        for previous in range(4):
            for stars in range(4):
                for doubled in(False,True):add('award',level=level,previous=previous,stars=stars,doubled=doubled,gold=2147483600)
        for stars in range(5):add('bonus',level=level,stars=stars,gold=17)
    for length in(0,1,9,10,11,499,999,1000):
        levels=[1+(i%3)for i in range(length)]
        for index in sorted({-1,0,length-1,length,length+1,999,1000}):
            for stars in(0,1,2,3,4,255):add('complete',levels=levels,index=index,stars=stars)
            add('getstars',levels=levels,index=index)
        for stage in(-2,-1,0,1,2,49,99,100,2147483647):
            add('stagefull',levels=levels,stage=stage);add('unlocked',levels=levels,stage=stage)
        add('total',levels=levels);add('stages',levels=levels)
    for level in(1,2,10,500,1000):
        for previous in range(4):
            levels=[1]*level;levels[-1]=previous
            for stars in(0,1,2,3,4):add('record',level=level,levels=levels,stars=stars)
    for level in(-2147483648,-100,-1,0,1,10,11,1000,2147483647):add('getstage',level=level)
    for level in sorted({-2147483648,-1,0,2147483647}|{r['RankStartsAt']+d for r in n.source['rank_config']for d in(-1,0,1)}):add('rank',level=level)
    for region in(1,2,3,4):
        for stage in(-1,0,1,2,3,4,5,6,10,48,49,50,98,99,100,999,2147483647):add('leveltype',region=region,stage=stage)
    for gold in(-2147483648,-1,0,17,99999999,2147483647):add('gold',gold=gold)
    for length in(0,10,999):
        for query in(0,1,2,3,4):add('completed',levels=[1]*length,index=query)
    oracle=OUT/'native-progress-oracle.json';oracle.write_text(json.dumps(dict(cases=cases),indent=2))
    (OUT/'Shims.cs').write_text(SHIMS);(OUT/'Comparison.cs').write_text(PROGRAM)
    runtime=next((UNITY/'NetCoreRuntime/shared/Microsoft.NETCore.App').iterdir());target=OUT/'CompareNativeProgress.dll'
    cmd=[str(UNITY/'NetCoreRuntime/dotnet.exe'),str(UNITY/'DotNetSdkRoslyn/csc.dll'),'-nologo','-target:exe','-nostdlib+',f'-out:{target}']+[f'-r:{p}'for p in runtime.glob('*.dll')if(p.name.startswith('System.')and'.Native'not in p.name)or p.name in('mscorlib.dll','netstandard.dll')]
    cmd += [str(OUT/'Shims.cs'),str(OUT/'Comparison.cs'),str(ROOT/'recovery/bloody/Runtime/OriginalLocalProgress.cs'),str(ROOT/'unity/JianghuUnity/Assets/Scripts/Json.cs')]
    compiled=subprocess.run(cmd,cwd=ROOT,text=True,capture_output=True);(OUT/'compile.txt').write_text(compiled.stdout+compiled.stderr)
    if compiled.returncode:print(compiled.stdout+compiled.stderr);raise SystemExit(compiled.returncode)
    target.with_suffix('.runtimeconfig.json').write_text(json.dumps(dict(runtimeOptions=dict(tfm='net8.0',framework=dict(name='Microsoft.NETCore.App',version=runtime.name)))))
    result=subprocess.run([str(UNITY/'NetCoreRuntime/dotnet.exe'),str(target),str(oracle),str(SOURCE),str(OUT/'adapter-save.json')],cwd=ROOT,text=True,capture_output=True);(OUT/'comparison.txt').write_text(result.stdout+result.stderr);print(result.stdout,end='');print(result.stderr,end='')
    if result.returncode:raise SystemExit(result.returncode)


if __name__=='__main__':main()
