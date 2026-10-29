using System;
using System.Collections.Generic;
using Jianghu;
using UnityEngine;

// CharacterConfigApplier.RunStartLogic/PreCustomizeEnemyAndApplyAll. This
// helper generates original data; it does not own match/progression services.
public interface IOriginalCampaignRandom {
    void InitState(int seed);
    int Range(int minimumInclusive,int maximumExclusive);
    float Range(float minimumInclusive,float maximumInclusive);
}

public sealed class OriginalCampaignRandom : IOriginalCampaignRandom {
    public void InitState(int seed)=>UnityEngine.Random.InitState(seed);
    public int Range(int minimumInclusive,int maximumExclusive)=>UnityEngine.Random.Range(minimumInclusive,maximumExclusive);
    public float Range(float minimumInclusive,float maximumInclusive)=>UnityEngine.Random.Range(minimumInclusive,maximumInclusive);
}

public sealed class OriginalGeneratedEnemy {
    public int Region,Level,EffectiveLevel,GameMode,Seed;
    public bool IsSpecialBoss;
    public string BulkName,ProfileUrl;
    public J Data,Bulk,ClothingColors,Trace;
    public float AggressionMin,AggressionMax,ReactionTime;
}

public sealed class OriginalCampaignGenerator {
    readonly J configurations,source;
    readonly IOriginalCampaignRandom random;
    readonly Func<J,float,Color> evaluate;
    readonly List<J> draws=new List<J>();
    public OriginalCampaignGenerator(J configurations,J source,IOriginalCampaignRandom random=null,Func<J,float,Color> evaluate=null) {
        this.configurations=configurations??throw new ArgumentNullException(nameof(configurations));
        this.source=source??throw new ArgumentNullException(nameof(source));
        this.random=random??new OriginalCampaignRandom();
        this.evaluate=evaluate??((gradient,t)=>OriginalVisualCatalog.Gradient(gradient).Evaluate(t));
    }
    public static OriginalCampaignGenerator Load() {
        var config=Resources.Load<TextAsset>("OriginalMatchConfigs");var campaign=Resources.Load<TextAsset>("OriginalCampaignSource");
        if(config==null||campaign==null)throw new InvalidOperationException("Original campaign source resources missing");
        return new OriginalCampaignGenerator(J.Parse(config.text),J.Parse(campaign.text));
    }
    // cel 0x341caa8: serialized order and max-exclusive ranges are retained.
    public string SelectBulk(int region,int level) {
        var bands=source["regions"][region.ToString()]["bands"].A;
        if(bands.Count==0)throw new InvalidOperationException("Original campaign region has no enemy bands: "+region);
        foreach(var band in bands)if(band["min"].I<=level&&level<band["max"].I)return band["bulk"].S;
        return (level<bands[0]["min"].I?bands[0]:bands[bands.Count-1])["bulk"].S;
    }
    public OriginalGeneratedEnemy GenerateCampaign(int region,int level) {
        if(level<1)throw new ArgumentOutOfRangeException(nameof(level),"Campaign levels are one-based");
        string bulk=SelectBulk(region,level);
        // NPC level seed is independent of dy.GetSeedForStage's world seed.
        // Native mutates the shared Unity RNG; do not save/restore its state.
        random.InitState(level);
        var result=PreCustomize(region,level,0,bulk);result.Seed=level;return result;
    }
    float Range(J bulk,string field) {
        var range=bulk[field];return Range(field,(float)range["min"].N,(float)range["max"].N);
    }
    float Range(string field,float lo,float hi) {
        float value=random.Range(lo,hi);draws.Add(J.O("kind","float","field",field,"min",lo,"max",hi,"value",value));return value;
    }
    int Range(string field,int lo,int hi) {
        int value=random.Range(lo,hi);draws.Add(J.O("kind","int","field",field,"min",lo,"max",hi,"value",value));return value;
    }
    int Item(J bulk,string field) {
        var list=bulk[field].A;if(list.Count==0)return 0;
        return list[Range(field,0,list.Count)]["item_id"].I;
    }
    string Name(J table,string field) {
        var list=table[field].A;
        if(list.Count==0)throw new InvalidOperationException("Original enemy name table is empty: "+field);
        return list[Range(field,0,list.Count)].S;
    }
    static J ColorData(Color color)=>J.O("r",color.r,"g",color.g,"b",color.b,"a",color.a);
    J NewData() {
        // CharacterConfigData .ctor 0x3427288, with implicit zero fields.
        var data=J.O("Name",source["constants"]["constructor_name"].S,"Gender",1,"Height",0,"Weight",0,"ShouldersWidth",.125f,"WaistWidth",0,"HipsWidth",0,
            "EyesHeight",.5f,"EyesWidth",.5f,"NoseHeight",.5f,"NoseWidth",.5f,"MouthHeight",.5f,"MouthWidth",.5f,"VoiceTypeIndex",1,"VoicePitch",1,"SkinColorT",50,"HairColorT",50,"BeardColorT",50,"TuckTopL1",true,"RankedPoints",0);
        foreach(string field in new[]{"Head","Hair","Eyes","Nose","Mouth","Beard","Hat","TopL1","TopL2","TopL3","TopL4","Gloves","BottomL1","BottomL2","BottomL3","Shoes","HandItemLeft","HandItemRight","DoubleHandedItem"})data[field+"Id"]=new J(0);
        return data;
    }
    // This lower-level entry consumes the caller's current RNG, as the
    // native method does. GenerateCampaign performs its native level seeding.
    public OriginalGeneratedEnemy PreCustomize(int region,int level,int gameMode,string bulkName) {
        draws.Clear();J bulk=configurations["enemy_bulk"][bulkName]["data"],constants=source["constants"];
        if(!bulk.Has)throw new ArgumentException("Original bulk config missing: "+bulkName);
        var data=NewData();int effectiveLevel=level-(level%10==0&&gameMode==1?1:0);
        data["Gender"]=new J(Range(bulk,"Gender")>.5f?1:0);
        data["SkinColorT"]=new J(Range("SkinColorT",1,101));data["HairColorT"]=new J(Range("HairColorT",1,101));
        data["BeardColorT"]=new J(unchecked((byte)(int)Math.Round((float)data["HairColorT"].N/(float)constants["beard_color_divider"].N,MidpointRounding.ToEven)));
        data["VoiceTypeIndex"]=new J(unchecked((byte)(int)Range(bulk,"VoiceType")));
        foreach(string field in new[]{"VoicePitch","Height","Weight","ShouldersWidth","WaistWidth","HipsWidth","EyesHeight","EyesWidth","NoseHeight","NoseWidth","MouthWidth","MouthHeight"})data[field]=new J(Range(bulk,field));
        bool male=data["Gender"].I==1;
        data["HeadId"]=new J(Item(bulk,male?"HeadItems":"FemaleHeadItems"));data["NoseId"]=new J(Item(bulk,male?"NoseItems":"FemaleNoseItems"));
        data["HairId"]=new J(Item(bulk,male?"HairItems":"FemaleHairItems"));
        data["BeardId"]=new J(Item(bulk,"BeardItems"));data["EyesId"]=new J(Item(bulk,"EyesItems"));data["MouthId"]=new J(Item(bulk,"MouthItems"));
        // The source bulk's Right list is assigned to HandItemLeftId, and
        // its Left list to HandItemRightId. Preserve this native cross-wire.
        data["HandItemRightId"]=new J(Item(bulk,"SingleHandedItemsLeft"));data["HandItemLeftId"]=new J(Item(bulk,"SingleHandedItemsRight"));data["DoubleHandedItemId"]=new J(Item(bulk,"DoubleHandedItems"));
        foreach(string slot in new[]{"Hat","TopL1","TopL2","TopL3","TopL4","Gloves","BottomL1","BottomL2","BottomL3","Shoes"})data[slot+"Id"]=new J(Item(bulk,slot+"Items"));
        var ts=new float[8];for(int i=0;i<ts.Length;i++)ts[i]=Range("ClothesTints["+i+"]",(float)constants["clothes_random_ranges"][i][0].N,(float)constants["clothes_random_ranges"][i][1].N);
        var colors=J.O();for(int i=0;i<ts.Length;i++)colors["jl"+(char)('i'+i)]=ColorData(evaluate(bulk["ClothesTints"],ts[i]));
        if(effectiveLevel%10==0) {
            var offsets=constants["boss_offsets"];
            data["Height"]=new J((float)bulk["Height"]["min"].N+(float)offsets["Height"].N);
            data["Weight"]=new J((float)bulk["Weight"]["min"].N+(float)offsets["Weight"].N);
            data["ShouldersWidth"]=new J((float)bulk["ShouldersWidth"]["max"].N+(float)offsets["ShouldersWidth"].N);
            data["WaistWidth"]=new J((float)bulk["WaistWidth"]["max"].N+(float)offsets["WaistWidth"].N);
            data["VoicePitch"]=new J((float)bulk["VoicePitch"]["min"].N+(float)offsets["VoicePitch"].N);
        }
        string guid=bulk[male?"EnemyNames_Male":"EnemyNames_Female"]["guid"].S;var names=source["names"][guid];
        string first=Name(names,"FirstNames"),middle=Name(names,"MiddleNames"),family=Name(names,"FamilyNames");
        if(!male)family+=constants[region==3?"female_russian_family_suffix":"female_family_suffix"].S;
        data["Name"]=new J(first+constants["name_separator"].S+middle+family);
        bool special=false;string profileUrl=null;
        foreach(var boss in source["special_bosses"][bulkName].A)if(boss["level"].I==effectiveLevel&&boss["profile"].S.Length>0) {
            var profile=configurations["profiles"][boss["profile"].S]["data"];
            if(!profile.Has)throw new InvalidOperationException("Original special boss profile missing: "+boss["profile"].S);
            data=J.Parse(profile.ToString());profileUrl=boss["profile_url"].S;special=true;break;
        }
        float reaction=Range(bulk,"ReactionTime");
        return new OriginalGeneratedEnemy {Region=region,Level=level,EffectiveLevel=effectiveLevel,GameMode=gameMode,Seed=-1,BulkName=bulkName,Data=data,Bulk=bulk,ClothingColors=colors,IsSpecialBoss=special,ProfileUrl=profileUrl,
            AggressionMin=(float)bulk["Aggression"]["min"].N,AggressionMax=(float)bulk["Aggression"]["max"].N,ReactionTime=reaction,
            Trace=J.O("source","CharacterConfigApplier.PreCustomizeEnemyAndApplyAll","region",region,"level",level,"effective_level",effectiveLevel,"mode",gameMode,"bulk",bulkName,"special_boss",special,"draws",new J(new List<J>(draws)),"runtime_parity_verified",false)};
    }
}
