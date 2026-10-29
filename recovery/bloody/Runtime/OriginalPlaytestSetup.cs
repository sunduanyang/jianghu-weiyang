using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Jianghu;
using UnityEngine;

public sealed class OriginalPlaytestSetup {
    public int Region=1,Level=1;
    public J Character;
    public OriginalPlaytestSetup Clone()=>new OriginalPlaytestSetup {Region=Region,Level=Level,Character=Character==null?null:J.Parse(J.Write(Character))};
}
public sealed class OriginalPlaytestSlot {public string Key,Label;}
public sealed class OriginalPlaytestRegion {public int Id,LevelCount;public string Label;}
public sealed class OriginalPlaytestChoice {
    public int Id,Region,Category;
    public string Label;
    public bool TwoHanded;
    public float Damage,Defense,Weight;
    internal string resource,textureIdentity;
    internal OriginalVisualCatalog visuals;
    internal Color tint=Color.white;
    bool spriteRead,textureRead;
    Sprite sprite;
    Texture texture;
    // Reading labels/stats never loads an item prefab. The original asset is
    // inspected only when its preview is requested; it is never instantiated.
    public Sprite PreviewSprite {
        get {
            if(!spriteRead) {
                spriteRead=true;
                if(Id!=0&&!string.IsNullOrEmpty(resource)) {
                    var prefab=Resources.Load<GameObject>(resource);
                    var renderer=prefab==null?null:prefab.GetComponentInChildren<SpriteRenderer>(true);
                    if(renderer!=null){sprite=renderer.sprite;tint=renderer.color;}
                }
            }
            return sprite;
        }
    }
    public Texture PreviewTexture {
        get {
            if(!textureRead) {
                textureRead=true;
                if(!string.IsNullOrEmpty(resource)){var preview=PreviewSprite;texture=preview==null?null:preview.texture;}
                else if(Id!=0&&visuals!=null&&!string.IsNullOrEmpty(textureIdentity))texture=visuals.Get<Texture>(textureIdentity);
            }
            return texture;
        }
    }
    public Color PreviewTint {get {if(!string.IsNullOrEmpty(resource)&&Id!=0){var unused=PreviewSprite;}return tint;}}
}

public sealed class OriginalPlaytestCatalog {
    internal static readonly string[] EquipmentFields={"HandItemRightId","HandItemLeftId","DoubleHandedItemId","HatId","TopL1Id","TopL2Id","TopL3Id","TopL4Id","GlovesId","BottomL1Id","BottomL2Id","BottomL3Id","ShoesId"};
    static readonly string[] SlotKeys={"MainHand","OffHand","HatId","TopL1Id","TopL2Id","TopL3Id","TopL4Id","GlovesId","BottomL1Id","BottomL2Id","BottomL3Id","ShoesId"};
    static readonly string[] SlotLabels={"主手","副手","头部","上衣第一层","上衣第二层","上衣第三层","上衣第四层","手套","下装第一层","下装第二层","下装第三层","鞋子"};
    readonly Dictionary<string,IReadOnlyList<OriginalPlaytestChoice>> choices=new Dictionary<string,IReadOnlyList<OriginalPlaytestChoice>>(StringComparer.Ordinal);
    readonly Dictionary<string,Dictionary<int,OriginalPlaytestChoice>> byId=new Dictionary<string,Dictionary<int,OriginalPlaytestChoice>>(StringComparer.Ordinal);
    readonly J baseCharacter;
    public IReadOnlyList<OriginalPlaytestSlot> Slots {get;}
    public IReadOnlyList<OriginalPlaytestRegion> Regions {get;}
    public OriginalPlaytestCatalog():this(LoadVisuals()){}
    OriginalPlaytestCatalog(OriginalVisualCatalog visuals):this(LoadJson("OriginalItemCatalog"),LoadJson("OriginalMatchConfigs"),visuals.Data["armor"],visuals,LoadJson("OriginalProgressSource")){}
    // Data injection also permits finite storage/catalog checks without a
    // Unity player or any access to preview assets.
    internal OriginalPlaytestCatalog(J items,J configs,J armor,OriginalVisualCatalog visuals,J progress) {
        var original=configs["profiles"]["CharacterConfigDataSO_Slot_0"]["data"];
        if(!(original.value is Dictionary<string,J>))throw new InvalidDataException("原始 Slot_0 人物配置缺失。");
        baseCharacter=J.Parse(J.Write(original));
        Slots=Array.AsReadOnly(SlotKeys.Select((key,index)=>new OriginalPlaytestSlot{Key=key,Label=SlotLabels[index]}).ToArray());
        var labels=new[]{"英伦诸岛","神圣罗马帝国","俄罗斯沙皇国","日本幕府"};
        Regions=Array.AsReadOnly(Enumerable.Range(1,4).Select(id=>new OriginalPlaytestRegion {
            Id=id,Label=labels[id-1],LevelCount=ReadInteger(progress["regions"][id.ToString()]["number_of_levels"],"地区关卡数量")
        }).ToArray());
        var hands=new Dictionary<int,OriginalPlaytestChoice>();
        foreach(var row in items["items"].A) {
            int id=row["item_id"].I;
            if(id<0||row["fields"]["EmptyShopSlot"].B||string.IsNullOrWhiteSpace(row["resource"].S)||hands.ContainsKey(id))continue;
            hands.Add(id,new OriginalPlaytestChoice {
                Id=id,Region=row["fields"]["region"].I,Category=row["category"].I,Label=id==0?"未装备（"+row["label"].S+"）":row["label"].S,
                TwoHanded=row["two_handed"].B,Damage=(float)row["stats"]["obs_Damage"].N,Defense=(float)row["stats"]["obs_Defense"].N,Weight=(float)row["stats"]["obs_Weight"].N,resource=row["resource"].S
            });
        }
        if(!hands.ContainsKey(0))hands.Add(0,new OriginalPlaytestChoice{Id=0,Label="未装备"});
        AddChoices("MainHand",hands.Values);AddChoices("OffHand",hands.Values.Where(item=>!item.TwoHanded));
        for(int index=2;index<SlotKeys.Length;index++) {
            int type=index+6;var rows=new Dictionary<int,OriginalPlaytestChoice>();
            foreach(var row in armor.A) {
                int id=row["item_id"].I;
                if(row["type"].I!=type||id<0||row["empty"].B||rows.ContainsKey(id)||string.IsNullOrEmpty(row["texture"].S))continue;
                rows.Add(id,new OriginalPlaytestChoice {
                    Id=id,Category=type,Region=row["region"].I,Label=id==0?"未装备":row["label"].S,
                    Damage=(float)row["damage"].N,Defense=(float)row["defense"].N,Weight=(float)row["weight"].N,
                    textureIdentity=row["texture"].S,visuals=visuals,tint=OriginalVisualCatalog.Color(row["tint"])
                });
            }
            if(!rows.ContainsKey(0))rows.Add(0,new OriginalPlaytestChoice{Id=0,Category=type,Label="未装备",tint=Color.clear});
            AddChoices(SlotKeys[index],rows.Values);
        }
        Validate(Defaults());
    }
    static OriginalVisualCatalog LoadVisuals()=>Resources.Load<OriginalVisualCatalog>("OriginalVisualCatalog")??throw new InvalidOperationException("原始外观资源目录缺失。");
    static J LoadJson(string name) {
        var asset=Resources.Load<TextAsset>(name);if(asset==null)throw new InvalidOperationException("原始资源目录缺失："+name);
        return J.Parse(asset.text);
    }
    void AddChoices(string key,IEnumerable<OriginalPlaytestChoice> values) {
        var array=values.OrderBy(item=>item.Id==0?0:1).ThenBy(item=>item.Region).ThenBy(item=>item.Category).ThenBy(item=>item.Id).ToArray();
        choices.Add(key,Array.AsReadOnly(array));byId.Add(key,array.ToDictionary(item=>item.Id));
    }
    void CheckKey(string key) {if(key==null||!choices.ContainsKey(key))throw new ArgumentException("未知装备位置："+(key??"空"));}
    static void CheckCharacter(OriginalPlaytestSetup state) {
        if(state==null||state.Character==null||!(state.Character.value is Dictionary<string,J>))throw new ArgumentException("人物装备配置缺失。");
    }
    internal static int ReadInteger(J value,string label) {
        object raw=value?.value;
        if(!(raw is byte||raw is sbyte||raw is short||raw is ushort||raw is int||raw is uint||raw is long||raw is ulong||raw is float||raw is double||raw is decimal))throw new ArgumentException(label+"必须是整数。");
        double number=value.N;
        if(double.IsNaN(number)||double.IsInfinity(number)||Math.Truncate(number)!=number||number<int.MinValue||number>int.MaxValue)throw new ArgumentException(label+"必须是有效整数。");
        return (int)number;
    }
    public IReadOnlyList<OriginalPlaytestChoice> Choices(string key){CheckKey(key);return choices[key];}
    public OriginalPlaytestChoice Choice(string key,int id){CheckKey(key);return byId[key].TryGetValue(id,out var value)?value:null;}
    public int CurrentId(OriginalPlaytestSetup state,string key) {
        CheckKey(key);CheckCharacter(state);
        if(key=="MainHand") {
            int two=ReadInteger(state.Character["DoubleHandedItemId"],"双手武器编号");
            return two!=0?two:ReadInteger(state.Character["HandItemRightId"],"主手武器编号");
        }
        return ReadInteger(state.Character[key=="OffHand"?"HandItemLeftId":key],key);
    }
    public bool IsSlotEnabled(OriginalPlaytestSetup state,string key) {
        CheckKey(key);CheckCharacter(state);return key!="OffHand"||ReadInteger(state.Character["DoubleHandedItemId"],"双手武器编号")==0;
    }
    public void Select(OriginalPlaytestSetup state,string key,int id) {
        CheckKey(key);CheckCharacter(state);var choice=Choice(key,id);
        if(choice==null)throw new ArgumentException("该位置没有编号为 "+id+" 的有效装备。");
        if(!IsSlotEnabled(state,key))throw new InvalidOperationException("使用双手武器时无法更换副手；请先选择单手武器。");
        if(key=="MainHand") {
            state.Character["DoubleHandedItemId"]=new J(choice.TwoHanded?id:0);
            if(!choice.TwoHanded)state.Character["HandItemRightId"]=new J(id);
        } else state.Character[key=="OffHand"?"HandItemLeftId":key]=new J(id);
    }
    public OriginalPlaytestSetup Defaults()=>new OriginalPlaytestSetup{Character=J.Parse(J.Write(baseCharacter))};
    public void Validate(OriginalPlaytestSetup state) {
        CheckCharacter(state);var region=Regions.FirstOrDefault(value=>value.Id==state.Region);
        if(region==null)throw new ArgumentException("请选择四个原始地区之一。");
        if(state.Level<1||state.Level>1000||state.Level>region.LevelCount)throw new ArgumentException("关卡必须在 1 至 "+Math.Min(1000,region.LevelCount)+" 之间。");
        foreach(string field in EquipmentFields)ReadInteger(state.Character[field],field);
        int left=state.Character["HandItemLeftId"].I,right=state.Character["HandItemRightId"].I,two=state.Character["DoubleHandedItemId"].I;
        if(Choice("OffHand",left)==null)throw new ArgumentException("副手武器编号无效，副手只接受单手装备。");
        var rightChoice=Choice("MainHand",right);
        if(rightChoice==null||rightChoice.TwoHanded)throw new ArgumentException("单手主手编号无效；双手装备必须使用双手武器字段。");
        if(two!=0&&(Choice("MainHand",two)==null||!Choice("MainHand",two).TwoHanded))throw new ArgumentException("双手武器编号无效。");
        foreach(var slot in Slots.Skip(2))if(Choice(slot.Key,state.Character[slot.Key].I)==null)throw new ArgumentException(slot.Label+"装备编号无效。");
    }
    internal OriginalPlaytestSetup MergeEquipment(OriginalPlaytestSetup source) {
        Validate(source);var result=Defaults();result.Region=source.Region;result.Level=source.Level;
        foreach(string field in EquipmentFields)result.Character[field]=new J(source.Character[field].I);
        return result;
    }
}

public sealed class OriginalPlaytestStore {
    const string Format="bloody-playtest-setup-v1";
    readonly OriginalPlaytestCatalog catalog;
    OriginalPlaytestSetup stored;
    // Callers edit their returned draft; failed saves cannot mutate the last
    // successfully committed in-memory settings through a shared JSON object.
    public OriginalPlaytestSetup Value=>stored.Clone();
    public string LoadError {get;private set;}
    public string SavePath {get;}
    OriginalPlaytestStore(string path,OriginalPlaytestCatalog source) {SavePath=path;catalog=source;stored=catalog.Defaults();}
    public static OriginalPlaytestStore Open(string absolutePath,OriginalPlaytestCatalog catalog) {
        if(catalog==null)throw new ArgumentNullException(nameof(catalog));
        if(string.IsNullOrWhiteSpace(absolutePath)||!Path.IsPathRooted(absolutePath))throw new ArgumentException("试玩设置需要独立的绝对文件路径。");
        var store=new OriginalPlaytestStore(Path.GetFullPath(absolutePath),catalog);
        if(!File.Exists(store.SavePath))return store;
        try {
            string text=File.ReadAllText(store.SavePath);CheckCompleteDocument(text);var root=J.Parse(text);
            if(root["format"].S!=Format||!(root["Character"].value is Dictionary<string,J>))throw new InvalidDataException("试玩设置格式不正确。");
            var draft=catalog.Defaults();draft.Region=OriginalPlaytestCatalog.ReadInteger(root["Region"],"地区");draft.Level=OriginalPlaytestCatalog.ReadInteger(root["Level"],"关卡");
            // Only equipment is accepted from disk. Names, dimensions, face,
            // voice, tints and every other body value come from original Slot0.
            foreach(string field in OriginalPlaytestCatalog.EquipmentFields)
                if(root["Character"][field].Has)draft.Character[field]=new J(OriginalPlaytestCatalog.ReadInteger(root["Character"][field],field));
            catalog.Validate(draft);store.stored=draft.Clone();
        } catch(Exception error) {store.LoadError="试玩设置读取失败，原文件已保留："+error.Message;}
        return store;
    }
    static void CheckCompleteDocument(string text) {
        int depth=0;bool quoted=false,escaped=false,started=false,ended=false;
        foreach(char c in text) {
            if(ended){if(!char.IsWhiteSpace(c))throw new FormatException("JSON 文档后有多余内容。");continue;}
            if(quoted){if(escaped)escaped=false;else if(c=='\\')escaped=true;else if(c=='"')quoted=false;continue;}
            if(c=='"'){quoted=true;continue;}
            if(c=='{'||c=='['){if(!started&&c!='{')throw new FormatException("试玩设置必须为 JSON 对象。");started=true;depth++;}
            else if(c=='}'||c==']'){if(--depth<0)throw new FormatException("JSON 括号不匹配。");if(depth==0)ended=true;}
            else if(!started&&!char.IsWhiteSpace(c))throw new FormatException("试玩设置必须为 JSON 对象。");
        }
        if(!started||!ended||depth!=0||quoted)throw new FormatException("JSON 文档不完整。");
    }
    public void Save(OriginalPlaytestSetup value) {
        catalog.Validate(value);var committed=catalog.MergeEquipment(value).Clone();
        var equipment=J.O();foreach(string field in OriginalPlaytestCatalog.EquipmentFields)equipment[field]=new J(committed.Character[field].I);
        string text=J.Write(J.O("format",Format,"Region",committed.Region,"Level",committed.Level,"Character",equipment));
        string directory=Path.GetDirectoryName(SavePath);Directory.CreateDirectory(directory);
        string temporary=SavePath+".writing-"+Guid.NewGuid().ToString("N");
        try {
            using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                byte[] data=new UTF8Encoding(false).GetBytes(text);file.Write(data,0,data.Length);file.Flush(true);
            }
            if(File.Exists(SavePath))File.Replace(temporary,SavePath,SavePath+".bak");else File.Move(temporary,SavePath);
            stored=committed;LoadError=null;
        } finally {if(File.Exists(temporary))File.Delete(temporary);}
    }
}
