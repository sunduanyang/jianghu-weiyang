using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
// 关节绑定：每块拆件只标自己的关节点（起点接上一级，终点接下一级），
// 全身共用一个比例，部件只旋转、不拉伸。坐标为拆件自身像素，x 向右，y 向下，人物面朝右。
public sealed class RigPointDef {
    public readonly string Key,Label;public readonly bool Optional;
    public RigPointDef(string key,string label,bool optional=false){Key=key;Label=label;Optional=optional;}
}
public sealed class RigSlotDef {
    public string Id,Label,Group,Parent;public bool Optional;public RigPointDef[] Points;
}
public static class RigSlots {
    static RigPointDef P(string k,string l,bool o=false)=>new RigPointDef(k,l,o);
    static RigSlotDef S(string id,string label,string group,string parent,params RigPointDef[] pts)=>new RigSlotDef{Id=id,Label=label,Group=group,Parent=parent,Points=pts};
    public static readonly RigSlotDef[] All={
        S("torso","躯干","躯干与头",null,P("pivot","腰（根）"),P("tip","颈"),P("shoulderNear","近侧肩"),P("shoulderFar","远侧肩"),P("hipNear","近侧髋",true),P("hipFar","远侧髋",true)),
        new RigSlotDef{Id="neck",Label="颈（可选）",Group="躯干与头",Parent="torso.tip",Optional=true,Points=new[]{P("pivot","颈根"),P("tip","接头处")}},
        S("head","头","躯干与头","torso.tip",P("pivot","颈接点"),P("tip","头顶方向")),
        S("skirtBack","后摆","衣摆","torso.pivot",P("pivot","腰带"),P("tip","下摆")),
        S("skirtFront","前摆","衣摆","torso.pivot",P("pivot","腰带"),P("tip","下摆")),
        S("upperFar","远侧大臂","远侧手臂","torso.shoulderFar",P("pivot","肩"),P("tip","肘")),
        S("forearmFar","远侧小臂","远侧手臂","upperFar.tip",P("pivot","肘"),P("tip","腕")),
        S("handFar","远侧手","远侧手臂","forearmFar.tip",P("pivot","腕"),P("tip","指尖"),P("grip","握点",true)),
        S("upperNear","近侧大臂","近侧手臂","torso.shoulderNear",P("pivot","肩"),P("tip","肘")),
        S("forearmNear","近侧小臂","近侧手臂","upperNear.tip",P("pivot","肘"),P("tip","腕")),
        S("handNear","近侧手","近侧手臂","forearmNear.tip",P("pivot","腕"),P("tip","指尖"),P("grip","握点",true)),
        S("thighFar","远侧大腿","远侧腿","torso.hipFar",P("pivot","髋"),P("tip","膝")),
        S("shinFar","远侧小腿","远侧腿","thighFar.tip",P("pivot","膝"),P("tip","踝")),
        S("footFar","远侧脚","远侧腿","shinFar.tip",P("pivot","踝"),P("tip","脚尖")),
        S("thighNear","近侧大腿","近侧腿","torso.hipNear",P("pivot","髋"),P("tip","膝")),
        S("shinNear","近侧小腿","近侧腿","thighNear.tip",P("pivot","膝"),P("tip","踝")),
        S("footNear","近侧脚","近侧腿","shinNear.tip",P("pivot","踝"),P("tip","脚尖")),
    };
    public static readonly Dictionary<string,RigSlotDef> ById=All.ToDictionary(s=>s.Id);
    public static readonly string[] DefaultOrder={"handFar","forearmFar","upperFar","skirtBack","footFar","shinFar","thighFar","footNear","shinNear","thighNear","skirtFront","torso","neck","head","upperNear","forearmNear","handNear"};
    // 关节限位：相对上一级（或躯干）的转角，单位度，以原样拼合时的角度为零。面朝右时负值为向前、向上转。
    public sealed class Limit {public bool TorsoRelative;public float Min,Max;public Limit(bool torso,float min,float max){TorsoRelative=torso;Min=min;Max=max;}}
    public static Dictionary<string,Limit> DefaultLimits()=>new Dictionary<string,Limit>{
        {"neck",new Limit(true,-30,30)},{"head",new Limit(false,-45,45)},{"skirtFront",new Limit(true,-40,40)},{"skirtBack",new Limit(true,-40,40)},
        {"forearmNear",new Limit(false,-120,30)},{"forearmFar",new Limit(false,-120,30)},{"handNear",new Limit(false,-70,70)},{"handFar",new Limit(false,-70,70)},
        {"thighNear",new Limit(true,-110,40)},{"thighFar",new Limit(true,-110,40)},{"shinNear",new Limit(false,-5,150)},{"shinFar",new Limit(false,-5,150)},
        {"footNear",new Limit(false,-35,35)},{"footFar",new Limit(false,-35,35)},
    };
}

public sealed class RigPiece {
    public string Name,File;public Texture2D Tex;public RectInt Source=new RectInt(-1,-1,0,0);
    public int Width=>Tex!=null?Tex.width:0;public int Height=>Tex!=null?Tex.height:0;
}
public sealed class RigSlotBind {
    public string Piece;public readonly Dictionary<string,Vector2> Points=new Dictionary<string,Vector2>();
    public RigSlotBind Copy(){var c=new RigSlotBind{Piece=Piece};foreach(var p in Points)c.Points[p.Key]=p.Value;return c;}
}

public sealed class RigBinding {
    public const string Format="jianghu-joint-rig";
    public const int Version=2;
    public string Name="新角色",Folder="",SourceSheet="";
    public bool ArtFacesLeft;          // 原画人物面朝左
    public float FitTolerance;         // 接缝补偿：允许每块按骨长等比缩放的幅度（0 ~ 0.2），不拉伸
    public readonly List<RigPiece> Pieces=new List<RigPiece>();
    public Dictionary<string,RigSlotBind> Slots=new Dictionary<string,RigSlotBind>();
    public List<string> Order=RigSlots.DefaultOrder.ToList();
    public Dictionary<string,RigSlots.Limit> Limits=RigSlots.DefaultLimits();

    public RigBinding(){foreach(var s in RigSlots.All)Slots[s.Id]=new RigSlotBind();}
    public RigPiece Piece(string name)=>string.IsNullOrEmpty(name)?null:Pieces.FirstOrDefault(p=>p.Name==name);
    public RigPiece SlotPiece(string id)=>Slots.TryGetValue(id,out var s)?Piece(s.Piece):null;
    public bool HasPoint(string id,string key)=>Slots.TryGetValue(id,out var s)&&s.Points.ContainsKey(key);
    public Vector2 Point(string id,string key)=>Slots[id].Points[key];
    public bool Complete(string id){
        if(SlotPiece(id)==null)return false;
        foreach(var p in RigSlots.ById[id].Points)if(!p.Optional&&!HasPoint(id,p.Key))return false;
        return true;
    }
    public string ParentRef(string id)=>id=="head"&&Complete("neck")?"neck.tip":RigSlots.ById[id].Parent;
    // 髋位没标时退回腰位。
    public bool TrySocket(string id,string key,out Vector2 v){
        if(Slots.TryGetValue(id,out var s)){
            if(s.Points.TryGetValue(key,out v))return true;
            if(id=="torso"&&(key=="hipNear"||key=="hipFar")&&s.Points.TryGetValue("pivot",out v))return true;
        }
        v=Vector2.zero;return false;
    }
    // 本块和所有上级都完成，才能拼进木偶。
    public bool Usable(string id){
        string cur=id;int guard=0;
        while(cur!=null&&guard++<20){
            if(!Complete(cur))return false;
            string pr=ParentRef(cur);if(pr==null)return true;
            var parts=pr.Split('.');if(!TrySocket(parts[0],parts[1],out _))return false;cur=parts[0];
        }
        return false;
    }
    public int RequiredDone=>RigSlots.All.Count(s=>!s.Optional&&Usable(s.Id));
    public int RequiredTotal=>RigSlots.All.Count(s=>!s.Optional);
    // 起点到终点的原画角度（度，y 向下）；手若标了握点，以握点定方向。
    public float DrawnAngle(string id){var a=Point(id,"pivot");var b=AimPoint(id);return Mathf.Atan2(b.y-a.y,b.x-a.x)*Mathf.Rad2Deg;}
    public Vector2 AimPoint(string id)=>HasPoint(id,"grip")?Point(id,"grip"):Point(id,"tip");
    public float BoneLength(string id){var a=Point(id,"pivot");var b=Point(id,"tip");return Vector2.Distance(a,b);}

    // ---------------------------------------------------------------- JSON
    public J ToJson(){
        var slots=J.O();
        foreach(var def in RigSlots.All){
            var s=Slots[def.Id];if(string.IsNullOrEmpty(s.Piece))continue;
            var pts=J.O();foreach(var p in def.Points)if(s.Points.TryGetValue(p.Key,out var v))pts[p.Key]=Pair(v);
            var o=J.O("piece",s.Piece,"parent",ParentRef(def.Id)??"","points",pts,"complete",Usable(def.Id));
            if(s.Points.ContainsKey("pivot")&&s.Points.ContainsKey("tip"))o["length_px"]=new J(Math.Round(BoneLength(def.Id),1));
            slots[def.Id]=o;
        }
        var pieces=new List<J>();
        foreach(var p in Pieces)pieces.Add(J.O("name",p.Name,"file",p.File??"","size",new J(new List<J>{new J(p.Width),new J(p.Height)}),"source_rect",p.Source.x<0?new J():new J(new List<J>{new J(p.Source.x),new J(p.Source.y),new J(p.Source.width),new J(p.Source.height)})));
        var limits=J.O();foreach(var l in Limits)limits[l.Key]=J.O("relative_to",l.Value.TorsoRelative?"torso":"parent","min_deg",l.Value.Min,"max_deg",l.Value.Max);
        return J.O("format",Format,"version",Version,"name",Name,"facing",ArtFacesLeft?"left":"right","fit_tolerance",Math.Round(FitTolerance,2),"coordinates","每块拆件自身像素坐标，x 向右，y 向下",
            "source_sheet",SourceSheet,"pieces",new J(pieces),"slots",slots,"draw_order",new J(Order.Select(o=>new J(o)).ToList()),"limits",limits);
    }
    static J Pair(Vector2 v)=>new J(new List<J>{new J(Math.Round(v.x,1)),new J(Math.Round(v.y,1))});
    static Vector2 Vec(J a)=>new Vector2((float)a[0].N,(float)a[1].N);
    // 读入绑定。pieceLoader 根据拆件记录返回贴图（可为空）；网页版导出的 v1 没有 pieces 字段，按 slots 里的名称对应。
    public static RigBinding FromJson(J j,Func<J,Texture2D> pieceLoader){
        if(j["format"].S!=Format)throw new FormatException("不是关节绑定数据（format 应为 "+Format+"）");
        var r=new RigBinding{Name=j["name"].Has?j["name"].S:"导入的角色",SourceSheet=j["source_sheet"].S,ArtFacesLeft=j["facing"].S=="left",FitTolerance=Mathf.Clamp((float)j["fit_tolerance"].N,0,.2f)};
        foreach(var pj in j["pieces"].A){
            var p=new RigPiece{Name=pj["name"].S,File=pj["file"].S};var sr=pj["source_rect"];
            if(sr.A.Count==4)p.Source=new RectInt(sr[0].I,sr[1].I,sr[2].I,sr[3].I);
            p.Tex=pieceLoader?.Invoke(pj);r.Pieces.Add(p);
        }
        foreach(var pair in j["slots"].D){
            if(!RigSlots.ById.ContainsKey(pair.Key))continue;var b=new RigSlotBind{Piece=pair.Value["piece"].S};
            foreach(var pt in pair.Value["points"].D)if(pt.Value.A.Count==2)b.Points[pt.Key]=Vec(pt.Value);
            r.Slots[pair.Key]=b;
        }
        var order=j["draw_order"].A.Select(o=>o.S).Where(RigSlots.ById.ContainsKey).Distinct().ToList();
        if(order.Count>0)r.Order=order.Concat(RigSlots.DefaultOrder.Where(id=>!order.Contains(id))).ToList();
        foreach(var l in j["limits"].D)if(RigSlots.ById.ContainsKey(l.Key))r.Limits[l.Key]=new RigSlots.Limit(l.Value["relative_to"].S=="torso",(float)l.Value["min_deg"].N,(float)l.Value["max_deg"].N);
        return r;
    }
    public string SlotsSnapshot(){var o=J.O();foreach(var s in Slots){var pts=J.O();foreach(var p in s.Value.Points)pts[p.Key]=Pair(p.Value);o[s.Key]=J.O("piece",s.Value.Piece??"","points",pts);}
        var lim=J.O();foreach(var l in Limits)lim[l.Key]=J.O("t",l.Value.TorsoRelative,"a",l.Value.Min,"b",l.Value.Max);
        return J.O("slots",o,"order",new J(Order.Select(x=>new J(x)).ToList()),"limits",lim).ToString();}
    public void RestoreSlots(string snapshot){
        var j=J.Parse(snapshot);
        foreach(var pair in j["slots"].D){var b=new RigSlotBind{Piece=pair.Value["piece"].S};foreach(var pt in pair.Value["points"].D)b.Points[pt.Key]=Vec(pt.Value);Slots[pair.Key]=b;}
        Order=j["order"].A.Select(o=>o.S).ToList();
        foreach(var l in j["limits"].D)Limits[l.Key]=new RigSlots.Limit(l.Value["t"].B,(float)l.Value["a"].N,(float)l.Value["b"].N);
    }

    // ---------------------------------------------------------------- files
    public void Save(string dir){
        Directory.CreateDirectory(dir);string pieceDir=Path.Combine(dir,"pieces");Directory.CreateDirectory(pieceDir);
        var keep=new HashSet<string>();
        for(int i=0;i<Pieces.Count;i++){
            var p=Pieces[i];if(p.Tex==null)continue;
            p.File="pieces/piece-"+(i+1).ToString("00")+".png";keep.Add(Path.GetFileName(p.File));
            System.IO.File.WriteAllBytes(Path.Combine(dir,p.File),p.Tex.EncodeToPNG());
        }
        foreach(var f in Directory.GetFiles(pieceDir,"piece-*.png"))if(!keep.Contains(Path.GetFileName(f)))System.IO.File.Delete(f);
        System.IO.File.WriteAllText(Path.Combine(dir,"rig.json"),ToJson().ToString());Folder=dir;
    }
    public static RigBinding Load(string dir){
        var j=J.Parse(System.IO.File.ReadAllText(Path.Combine(dir,"rig.json")));
        var r=FromJson(j,pj=>{string f=pj["file"].S;if(string.IsNullOrEmpty(f))return null;string path=Path.Combine(dir,f);return System.IO.File.Exists(path)?RigTextures.LoadFile(path):null;});
        r.Folder=dir;return r;
    }
}

public static class RigTextures {
    public static Texture2D LoadFile(string path){var t=new Texture2D(2,2,TextureFormat.RGBA32,true){wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear,anisoLevel=2};
        if(!t.LoadImage(System.IO.File.ReadAllBytes(path),false)){UnityEngine.Object.Destroy(t);return null;}t.wrapMode=TextureWrapMode.Clamp;t.name=Path.GetFileName(path);return t;}
}

// 绑定存放处：编辑器里存进 StreamingAssets/Rigs（随项目提交、随构建发布），
// 成品游戏里存进玩家目录。读取时两处都看，玩家目录同名者优先。
public static class RigLibrary {
    public static string BuiltinRigs=>Path.Combine(Application.streamingAssetsPath,"Rigs");
    public static string PlayerRigs=>Path.Combine(Application.persistentDataPath,"Rigs");
    public static string BuiltinSheets=>Path.Combine(Application.streamingAssetsPath,"RigSources");
    public static string PlayerSheets=>Path.Combine(Application.persistentDataPath,"RigSources");
    public static string SaveRoot=>Application.isEditor?BuiltinRigs:PlayerRigs;
    const string ActiveKey="jianghu.rig.active",EnemyKey="jianghu.rig.opponent";
    static readonly Dictionary<string,RigBinding> cache=new Dictionary<string,RigBinding>();
    public static string Active{get=>PlayerPrefs.GetString(ActiveKey,"");set{PlayerPrefs.SetString(ActiveKey,value??"");PlayerPrefs.Save();}}
    // 对手外观：可与玩家不同；为空时对手保持原来的人物。
    public static string Opponent{get=>PlayerPrefs.GetString(EnemyKey,"");set{PlayerPrefs.SetString(EnemyKey,value??"");PlayerPrefs.Save();}}
    public static string SafeFolder(string name){
        var bad=Path.GetInvalidFileNameChars();var s=new string((name??"").Trim().Select(c=>bad.Contains(c)?'_':c).ToArray());
        return string.IsNullOrEmpty(s)?"角色":s;
    }
    // 所有绑定：文件夹名 -> 完整路径。界面每帧都会问，所以一秒内复用上次扫描的结果。
    static SortedDictionary<string,string> listed;static float listedAt=-10;
    public static SortedDictionary<string,string> List(){
        if(listed!=null&&Time.unscaledTime-listedAt<1)return listed;
        listed=Scan();listedAt=Time.unscaledTime;return listed;
    }
    static SortedDictionary<string,string> Scan(){
        var found=new SortedDictionary<string,string>();
        foreach(var root in new[]{BuiltinRigs,PlayerRigs}){
            if(!Directory.Exists(root))continue;
            foreach(var d in Directory.GetDirectories(root))if(System.IO.File.Exists(Path.Combine(d,"rig.json")))found[Path.GetFileName(d)]=d;
        }
        return found;
    }
    public static List<string> Sheets(){
        var list=new List<string>();
        foreach(var root in new[]{BuiltinSheets,PlayerSheets}){
            if(!Directory.Exists(root))continue;
            list.AddRange(Directory.GetFiles(root).Where(f=>{string e=Path.GetExtension(f).ToLowerInvariant();return e==".png"||e==".jpg"||e==".jpeg";}));
        }
        return list;
    }
    public static RigBinding Get(string folder){
        if(string.IsNullOrEmpty(folder))return null;
        if(cache.TryGetValue(folder,out var r))return r;
        var all=List();r=null;
        if(all.TryGetValue(folder,out var dir)){try{r=RigBinding.Load(dir);}catch(Exception e){Debug.LogWarning("关节绑定读取失败："+dir+"\n"+e.Message);}}
        cache[folder]=r;return r;
    }
    public static void Invalidate(string folder){if(folder!=null)cache.Remove(folder);listed=null;}
    // 切磋用的外观：玩家用「设为切磋外观」选中的绑定，对手用「设为对手外观」选中的绑定。
    // 其它验收脚本运行时不用玩家选的外观，保证它们看到的仍是原来的人物；本功能自己的验收除外。
    public static bool SuppressedForAcceptance=>Environment.GetCommandLineArgs().Any(a=>a.StartsWith("-")&&a.EndsWith("Smoke")&&a!="-rigBinderSmoke");
    public static RigBinding ForDuel(bool human){
        if(SuppressedForAcceptance)return null;
        var r=Get(human?Active:Opponent);
        return ForDuelCandidate(r)?r:null;
    }
    // 切磋至少需要躯干、头、近侧手臂和两条腿；远侧手臂和衣摆可缺。
    public static readonly string[] DuelRequired={"torso","head","upperNear","forearmNear","handNear","thighNear","shinNear","footNear","thighFar","shinFar","footFar"};
    public static bool ForDuelCandidate(RigBinding r)=>r!=null&&DuelRequired.All(id=>r.Usable(id)&&r.SlotPiece(id)?.Tex!=null);
}
}
