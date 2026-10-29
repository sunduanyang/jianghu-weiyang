"""Original ARM64 alert predicates/count loops versus the actual C# service."""
import json,struct,subprocess
from pathlib import Path
from verify_bloody_ai_native import NativeAI
from verify_bloody_progress import SHIMS
from unicorn.arm64_const import *
ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'output/bloody-full-port-20261028/environment-check/new-items'
UNITY=Path('D:/Unity/Editors/6000.4.0f1/Editor/Data')
class NativeAlerts(NativeAI):
    INVENTORY=0x10017000;TYPECELL=0x10016000;KLASS=0x10016100;STATICS=0x10016500
    META=0x10012000;VIEWED=0x10013000;PIPS=0x10020000;LIST=0x10022000;ARRAY=0x10024000;DATA=0x10018000
    def __init__(self,source):
        self.mode='alerts';super().__init__();self.cpu.mem_map(0x10020000,0x20000)
        self.source=source;self.meta={i['id']:i for i in source['items']};self.viewed=set();self.overrides={};self.rank=0;self.completed=0;self.season=0
        for got in (0x720d1d8,0x72125b0,0x7212238,0x7212a88):self.ptr(got,self.TYPECELL)
        self.ptr(self.TYPECELL,self.KLASS);self.i32(self.KLASS+0xe4,1);self.ptr(self.KLASS+0xb8,self.STATICS);self.ptr(self.STATICS,self.INVENTORY)
        for offset,pointer in ((0x20,self.DATA),(0x28,self.PIPS),(0x30,0x1001b000),(0x38,0x1001b100),(0x48,self.VIEWED),(0x50,self.META)):
            self.ptr(self.SELF+offset,pointer)
        self.i32(self.PIPS+0x18,len(source['kind_pips']))
        for index,pip in enumerate(source['kind_pips']):
            obj=self.PIPS+0x100+index*0x20;self.ptr(self.PIPS+0x20+index*8,obj);self.i32(obj+0x10,pip['kind']);self.ptr(obj+0x18,0)
        self.ptr(self.INVENTORY+0x28,self.DATA);self.ptr(self.DATA+0x10,self.LIST);self.ptr(self.LIST+0x10,self.ARRAY)
    def ptr(self,a,n):self.cpu.mem_write(a,struct.pack('<Q',n))
    def i32(self,a,n):self.cpu.mem_write(a,struct.pack('<I',n&0xffffffff))
    def _hook(self,cpu,a,size,data):
        x0=cpu.reg_read(UC_ARM64_REG_X0);x1=cpu.reg_read(UC_ARM64_REG_X1);x2=cpu.reg_read(UC_ARM64_REG_X2)
        if a==0x3438984:pass # Lazy cache construction is bounded by exported real catalog.
        elif a==0x52c498c:
            m=self.meta.get(x1&65535);cpu.reg_write(UC_ARM64_REG_X0,int(m is not None))
            if m:cpu.mem_write(x2,struct.pack('<H2x5i?3x',m['id'],m['kind'],m['region'],m['unlock_rule'],m['unlocks_at_level'],m['seasonal_type'],m['empty_shop_slot']))
        elif a==0x5737680:cpu.reg_write(UC_ARM64_REG_X0,int((x1&65535) in self.viewed))
        elif a==0x351edd8:
            m=self.meta[x1&65535];owned=self.overrides.get(m['id'],m['max_ownable'] if m['unlock_rule'] in (1,3,4) else 0)
            cpu.reg_write(UC_ARM64_REG_X0,int(owned>0))
        elif a==0x3453c74:cpu.reg_write(UC_ARM64_REG_X0,self.season)
        elif a in (0x3437b34,0x3438764):cpu.reg_write(UC_ARM64_REG_X0,self.completed)
        elif a==0x3437c7c:cpu.reg_write(UC_ARM64_REG_X0,self.rank)
        elif a==0x3438850:
            region=x2&0xffffffff;kind=x1&0xffffffff;ranked=bool(cpu.reg_read(UC_ARM64_REG_X3))
            ids=[m['id'] for m in self.source['items'] if m['kind']==kind and ((m['unlock_rule'] in (3,4)) if ranked else m['region']==region)]
            self.i32(self.LIST+0x18,len(ids));self.ptr(self.LIST+0x10,self.ARRAY)
            cpu.mem_write(self.ARRAY+0x20,struct.pack('<'+'H'*len(ids),*ids) if ids else b'\0\0')
            cpu.reg_write(UC_ARM64_REG_X0,self.LIST)
        elif a==0x457166c:cpu.reg_write(UC_ARM64_REG_X0,struct.unpack('<H',cpu.mem_read(self.ARRAY+0x20+(x1&0xffffffff)*2,2))[0])
        elif a==0x351eb34:
            m=self.owned_meta;self.ptr(x2,self.DATA);self.i32(cpu.reg_read(UC_ARM64_REG_X3),m['unlock_rule']);cpu.reg_write(UC_ARM64_REG_X0,1)
        elif a==0x351ece8:cpu.reg_write(UC_ARM64_REG_X0,self.owned_meta['max_ownable'])
        elif a==0x4623ea4:cpu.reg_write(UC_ARM64_REG_X0,self.owned_meta['id']|(self.owned_entry<<16))
        elif a==0x3696608:self.pips[x0]=x1&0xffffffff
        elif a==0x6aca1c8:cpu.reg_write(UC_ARM64_REG_X0,int(x0!=x1))
        elif a==0x6acbd7c:cpu.reg_write(UC_ARM64_REG_X0,int(x0==x1))
        elif a in (0x316bb50,0x316bb58):raise RuntimeError(f'Original source threw at {a:x}')
        else:return super()._hook(cpu,a,size,data)
        cpu.reg_write(UC_ARM64_REG_PC,cpu.reg_read(UC_ARM64_REG_LR))
    def invoke(self,a,x1=0,x2=0,x3=0):
        cpu=self.cpu;cpu.reg_write(UC_ARM64_REG_SP,0x10010000);cpu.reg_write(UC_ARM64_REG_LR,self.RETURN)
        for reg,v in ((UC_ARM64_REG_X0,self.SELF),(UC_ARM64_REG_X1,x1),(UC_ARM64_REG_X2,x2),(UC_ARM64_REG_X3,x3)):cpu.reg_write(reg,v)
        cpu.emu_start(a,self.RETURN,count=2000000)
        if cpu.reg_read(UC_ARM64_REG_PC)!=self.RETURN:raise RuntimeError('Original alert routine failed to return')
        return cpu.reg_read(UC_ARM64_REG_X0)&0xffffffff
    def predicate(self,m,owned,viewed,completed,rank,season):
        self.meta={m['id']:m};self.overrides={m['id']:owned};self.viewed={m['id']} if viewed else set();self.season=season
        return bool(self.invoke(0x3437d50,m['id'],completed,rank))
    def count(self,region,completed,rank,ranked):
        self.meta={m['id']:m for m in self.source['items']};self.overrides={};self.viewed=set();self.completed=completed;self.rank=rank;self.season=0
        value=self.invoke(0x343864c,region,int(ranked))
        self.i32(self.STATICS+0x110,region);self.cpu.mem_write(self.STATICS+0x151,bytes([ranked]));self.pips={}
        self.invoke(0x34377bc)
        return dict(count=value,top=self.pips.get(0x1001b000),bottom=self.pips.get(0x1001b100))
    def owned(self,m,entry):
        self.owned_meta=m;self.owned_entry=0 if entry is None else entry
        self.i32(self.LIST+0x18,0 if entry is None else 1)
        # The original inventory method takes its own service, not alert self.
        self.cpu.reg_write(UC_ARM64_REG_SP,0x10010000);self.cpu.reg_write(UC_ARM64_REG_LR,self.RETURN)
        self.cpu.reg_write(UC_ARM64_REG_X0,self.INVENTORY);self.cpu.reg_write(UC_ARM64_REG_X1,m['id'])
        self.cpu.emu_start(0x351ea10,self.RETURN,count=10000)
        return self.cpu.reg_read(UC_ARM64_REG_X0)&255

PROGRAM=r'''using System;using System.IO;using System.Linq;using System.Collections.Generic;using Jianghu;
class Program {
static int checks;static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
static J A(params J[] rows)=>new J(rows.ToList());
static OriginalLocalProgress Progress(J source,int region,int completed,int rank){var p=OriginalLocalProgress.FromSource(source);J snapshot=p.Snapshot();snapshot["Progress"]["Campaign"]["Regions"]=A(J.O("RegionId",region,"Levels",new J(Enumerable.Range(0,completed).Select(_=>new J(1)).ToList())));snapshot["Progress"]["Ranked"]["CurrentRankPoints"]=source["rank_config"][rank]["RankStartsAt"];p=OriginalLocalProgress.FromSource(source,snapshot);p.SetCurrentShopRegion(region);return p;}
static void Main(string[] args){J source=J.Parse(File.ReadAllText(args[0])),progressSource=J.Parse(File.ReadAllText(args[1])),oracle=J.Parse(File.ReadAllText(args[2]));
foreach(var c in oracle["predicates"].A){var row=c["meta"];var library=J.O("items",A(row),"kind_pips",A(J.O("kind",6)),"inventory_defaults",source["inventory_defaults"]);var snapshot=J.O("Inventory",J.O("Entries",A(J.O("Id",row["id"],"Owned",c["owned"]))),"ViewedIds",c["viewed"].B?A(row["id"]):A());var s=OriginalNewItemsAlerts.FromSource(library,Progress(progressSource,1,c["completed"].I,c["rank"].I),snapshot);s.SeasonalType=c["season"].I;Check(s.IsItemNew((ushort)row["id"].I)==c["expected"].B,"native predicate "+c);}
foreach(var c in oracle["owned"].A){var row=c["meta"];var library=J.O("items",A(row),"kind_pips",A(),"inventory_defaults",source["inventory_defaults"]);J snapshot=c["entry"].Has?J.O("Inventory",J.O("Entries",A(J.O("Id",row["id"],"Owned",c["entry"]))),"ViewedIds",A()):null;var s=OriginalNewItemsAlerts.FromSource(library,Progress(progressSource,1,0,0),snapshot);Check(s.GetOwnedCount((ushort)row["id"].I)==c["expected"].I,"native default owned "+c);}
foreach(var c in oracle["counts"].A){var p=Progress(progressSource,c["region"].I,c["completed"].I,c["rank"].I);var s=OriginalNewItemsAlerts.FromSource(source,p);s.IncludeRankedForPips=c["ranked"].B;Check(s.GetNewItemsCount(c["region"].I,c["ranked"].B)==c["expected"]["count"].I,"native full catalog count "+c);s.RefreshAllPips();Check(s.TopPipCount==c["expected"]["top"].I&&s.BottomPipCount==c["expected"]["bottom"].I,"native top/bottom pips "+c);}
string path=Path.GetFullPath(args[3]);if(File.Exists(path))File.Delete(path);var progress=Progress(progressSource,1,1,0);var alert=OriginalNewItemsAlerts.OpenFromSource(source,path,progress);int before=alert.GetNewItemsCount(1,false);ushort viewed=(ushort)source["items"].A.First(r=>r["region"].I==1&&r["unlock_rule"].I==2&&r["unlocks_at_level"].I==1&&!r["empty_shop_slot"].B)["id"].I;alert.MarkItemViewed(viewed);var loaded=OriginalNewItemsAlerts.OpenFromSource(source,path,progress);Check(loaded.ViewedIds.Contains(viewed)&&loaded.GetNewItemsCount(1,false)==before-1,"viewed persistence decreases real alert count");Check(loaded.Snapshot()["Inventory"]["Entries"].A.Count==0,"new inventory retains empty source entries with automatic ownership");int events=0;loaded.OnPipsRefreshed+=()=>events++;loaded.MarkItemViewed(viewed);Check(events==0,"already viewed is a no-op");loaded.ClearViewedIds();Check(events==1&&loaded.GetNewItemsCount(1,false)==before,"native clear refresh");var reopened=OriginalNewItemsAlerts.OpenFromSource(source,path,progress);Check(reopened.ViewedIds.Contains(viewed),"native clear does not save without dwg");Console.WriteLine("{\"native_new_items_checks\":"+checks+",\"first_win_region1_count\":"+before+",\"failures\":0}");}}
'''
def main():
    OUT.mkdir(parents=True,exist_ok=True)
    source_path=ROOT/'recovery/bloody/Runtime/Resources/OriginalShopAlertSource.json';source=json.loads(source_path.read_text(encoding='utf-8'));n=NativeAlerts(source)
    predicates=[]
    for rule in range(6):
        for threshold in (-1,0,1,3):
            for empty in (False,True):
                m=dict(id=5000,kind=6,region=1,unlock_rule=rule,unlocks_at_level=threshold,seasonal_type=1,empty_shop_slot=empty,max_ownable=2)
                for owned in (0,1):
                    for viewed in (False,True):
                        for completed,rank,season in ((0,0,0),(3,3,0),(3,3,1),(3,3,2)):
                            predicates.append(dict(meta=m,owned=owned,viewed=viewed,completed=completed,rank=rank,season=season,expected=n.predicate(m,owned,viewed,completed,rank,season)))
    owned=[]
    for rule in range(6):
        for max_owned in (1,2):
            m=dict(id=5000,kind=6,region=1,unlock_rule=rule,unlocks_at_level=1,seasonal_type=0,empty_shop_slot=False,max_ownable=max_owned)
            for entry in (None,0,1,2):owned.append(dict(meta=m,entry=entry,expected=n.owned(m,entry)))
    counts=[]
    for region in (1,2,3,4):
        for completed in (0,1,9,10,100,1000):
            for rank in (0,3,31):
                for ranked in (False,True):counts.append(dict(region=region,completed=completed,rank=rank,ranked=ranked,expected=n.count(region,completed,rank,ranked)))
    oracle=OUT/'native-new-items-oracle.json';oracle.write_text(json.dumps(dict(predicates=predicates,owned=owned,counts=counts),indent=2))
    (OUT/'Shims.cs').write_text(SHIMS);(OUT/'Comparison.cs').write_text(PROGRAM)
    runtime=next((UNITY/'NetCoreRuntime/shared/Microsoft.NETCore.App').iterdir());target=OUT/'CompareNativeNewItems.dll'
    cmd=[str(UNITY/'NetCoreRuntime/dotnet.exe'),str(UNITY/'DotNetSdkRoslyn/csc.dll'),'-nologo','-target:exe','-nostdlib+',f'-out:{target}']
    cmd += [f'-r:{p}' for p in runtime.glob('*.dll') if(p.name.startswith('System.')and'.Native'not in p.name)or p.name in('mscorlib.dll','netstandard.dll')]
    cmd += [str(OUT/'Shims.cs'),str(OUT/'Comparison.cs'),str(ROOT/'recovery/bloody/Runtime/OriginalNewItemsAlerts.cs'),str(ROOT/'recovery/bloody/Runtime/OriginalLocalProgress.cs'),str(ROOT/'unity/JianghuUnity/Assets/Scripts/Json.cs')]
    result=subprocess.run(cmd,cwd=ROOT,text=True,capture_output=True);(OUT/'compile.txt').write_text(result.stdout+result.stderr)
    if result.returncode:print(result.stdout+result.stderr);raise SystemExit(result.returncode)
    target.with_suffix('.runtimeconfig.json').write_text(json.dumps(dict(runtimeOptions=dict(tfm='net8.0',framework=dict(name='Microsoft.NETCore.App',version=runtime.name)))))
    run=subprocess.run([str(UNITY/'NetCoreRuntime/dotnet.exe'),str(target),str(source_path),str(ROOT/'recovery/bloody/Runtime/Resources/OriginalProgressSource.json'),str(oracle),str(OUT/'adapter-save.json')],cwd=ROOT,text=True,capture_output=True)
    (OUT/'comparison.txt').write_text(run.stdout+run.stderr);print(run.stdout,end='');print(run.stderr,end='')
    if run.returncode:raise SystemExit(run.returncode)
if __name__=='__main__':main()
