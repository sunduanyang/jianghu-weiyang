using System;
using Jianghu;
using UnityEngine;

// This adapter preserves the campaign's coefficients/identities. Effective D
// is NOT a decoded obs_Damage from one of the APK's 342 original hand items.
public static class DuelSourceEquipment {
    static J mapping;
    static J Data {
        get {
            if(mapping==null){
                var asset=Resources.Load<TextAsset>("CombatSource/equipment");
                if(asset==null)throw new InvalidOperationException("CombatSource/equipment.json is missing");
                mapping=J.Parse(asset.text);
            }
            return mapping;
        }
    }
    public struct ArmorHit {public float defense; public int material;}
    public struct ItemPhysics {
        public float bodyMass,counterweightMass;
        public float bodyGravity,counterweightGravity,linearDamping,angularDamping;
    }
    // Native source D=1 baseline gives (1+1/15)=16/15. Do not clamp negative
    // effective D: doing so would erase the campaign's lower damage tiers.
    public static float EffectiveDamageStat(float coefficient)=>16*coefficient-15;
    public static float DamageStat(DuelWeapon spec,bool shield=false)=>shield?1:spec.id=="unarmed"?.1f:EffectiveDamageStat(spec.damage);
    public static DuelReferenceRules.Surface Surface(DuelWeapon spec,bool handle=false,bool shield=false){
        if(shield)return DuelReferenceRules.Surface.WoodBlunt;
        if(spec.id=="unarmed")return DuelReferenceRules.Surface.MetalBlunt;
        var entry=Data["weapons"][spec.sourceTemplate];
        string text=entry[handle?"handle":"head"].S;
        if(Enum.TryParse(text,out DuelReferenceRules.Surface found)&&text.Length>0)return found;
        if(handle)return spec.id=="axe"||spec.id=="greataxe"||spec.id=="mace"||spec.id=="warhammer"||spec.id=="spear"?DuelReferenceRules.Surface.WoodHandle:DuelReferenceRules.Surface.MetalHandle;
        return spec.id=="mace"||spec.id=="warhammer"?DuelReferenceRules.Surface.MetalBlunt:spec.id=="spear"?DuelReferenceRules.Surface.MetalSpike:DuelReferenceRules.Surface.MetalBlade;
    }
    public static float SpeedThreshold(DuelWeapon spec,bool handle=false,bool shield=false){
        var surface=Surface(spec,handle,shield);
        var head=Surface(spec,false,shield);
        bool blunt=head==DuelReferenceRules.Surface.MetalBlunt||head==DuelReferenceRules.Surface.WoodBlunt;
        return (blunt?20:15)*(DuelReferenceRules.IsHandle(surface)?.5f:1);
    }
    // SourcePrefab_HandItem.cqe, 0x363e0d8. The following returns SOURCE
    // category values. The OnEnable props apply only in its non-batch branch;
    // callers must not treat this as proof that the source global flag is off.
    public static ItemPhysics SourcePhysics(DuelWeapon spec,bool shield=false){
        bool two=spec.TwoHanded;
        float body=two?2:1,counter=two?.25f:1;
        switch(spec.id){
            case "axe":case "greataxe":body=two?2.25f:1;counter=two?1.25f:1;break;
            case "mace":body=two?7:3;counter=two?4:2.5f;break;
            case "warhammer":body=two?3:2;counter=two?1.5f:1.25f;break;
            case "spear":body=two?3:1.5f;counter=.5f;break;
        }
        if(shield){body=2;counter=.05f;}
        if(!shield&&spec.id=="unarmed"){body=spec.mass;counter=0;}
        return new ItemPhysics{bodyMass=body,counterweightMass=counter,bodyGravity=-1,counterweightGravity=-5,linearDamping=50,angularDamping=100};
    }
    public static ItemPhysics Physics(DuelWeapon spec,bool shield=false){
        var result=SourcePhysics(spec,shield);
        if(shield||spec.id=="unarmed")return result;
        float basis=(float)Data["profiles"][spec.id]["base_mass"].N;
        float ratio=basis>0?spec.mass/basis:1;
        result.bodyMass*=ratio;result.counterweightMass*=ratio;return result;
    }
    public static ItemPhysics DroppedPhysics()=>new ItemPhysics{bodyMass=1,counterweightMass=0,bodyGravity=1,counterweightGravity=1,linearDamping=.05f,angularDamping=.05f};
    public static string TemplateId(J packet){
        foreach(var key in new[]{"template_id","source_template","template"})if(packet[key].S.Length>0)return packet[key].S;
        // Legacy sessions did not send identity. Only these TWO exact names
        // disambiguate material; no grade, damage, or substring heuristic.
        if(packet["name"].S=="枣木剑")return "wood_sword";
        if(packet["name"].S=="铁胎练剑")return "iron_practice_sword";
        return "";
    }
    static bool Covered(J coverage,string part){
        foreach(var p in coverage.A){string key=p.S;if(part==key||part==key+"Near"||part==key+"Far")return true;}
        return false;
    }
    // Optional explicit armor_layers contains at most one entry per slot
    // L1..L3. enabled means a present source reference, NOT visible geometry.
    // cgg sums their defenses; cdv selects the LAST PRESENT layer's material.
    public static ArmorHit Armor(J fighter,string part){
        var result=new ArmorHit{material=1};
        if(fighter==null)return result;
        J layers=fighter["armor_layers"];
        if(layers.Has){
            for(int layer=1;layer<=3;layer++){
                foreach(var row in layers.A){
                    if(row["layer"].I!=layer||row["enabled"].Has&&!row["enabled"].B||!Covered(row["coverage"],part))continue;
                    result.defense+=(float)row["defense"].N;
                    result.material=Mathf.Max(1,row["material"].I);break;
                }
            }
            return result;
        }
        J armor=fighter["armor"],entry=Data["armor"][TemplateId(armor)];
        if(!entry.Has||armor["durability"].Has&&armor["durability"].N<=0||!Covered(entry["coverage"],part))return result;
        result.defense=armor["defense"].Has?(float)armor["defense"].N:(float)entry["defense"].N;
        result.material=Mathf.Max(1,entry["material"].I);return result;
    }
}
