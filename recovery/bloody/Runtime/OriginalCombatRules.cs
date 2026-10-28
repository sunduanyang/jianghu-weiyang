using UnityEngine;

public enum OriginalPartMode { None=0,Bashed=1,Pierced=2,Severed=3 }
public enum OriginalSurface { Unknown,WoodHandle,WoodBlunt,MetalHandle,MetalBlade,MetalSpike,MetalBlunt,SpikePommel }
public struct OriginalImpact {
    public OriginalRig attacker,victim;
    public OriginalItem item;
    public OriginalBodyPart part;
    public Vector2 point,velocity,normal;
    public float localDamage,globalDamage;
    public OriginalSurface surface;
}

// Literal translations of 5.0.3 ARM64 control flow, independent of RPG stats.
public static class OriginalCombatRules {
    public static bool RegisterImpact(float now,ref int count,ref float expires) {
        if(now>expires)count=0;
        if(count>2)return false;
        count++;expires=now+.1f;return true;
    }
    // CharacterDamageCalculator.OnHandItemImpact_Authoritative 0x3428224.
    public static float RawDamage(int category,bool two,Vector2 velocity,float stat,float defense,OriginalSurface surface,bool shield,int armorMaterial,float damagePerMps=.25f) {
        Vector2 factor=Vector2.one;
        switch(category) {
            case 0:case 1:factor=new Vector2(two?1:.75f,.75f);break;
            case 2:case 3:case 6:factor=two?new Vector2(1.25f,.75f):new Vector2(1,.85f);break;
            case 4:factor=new Vector2(two?1:.75f,1);break;
            case 5:factor=two?Vector2.one:new Vector2(1.15f,.85f);break;
            case 7:factor=new Vector2(two?1.25f:1.15f,.75f);break;
            case 8:factor=new Vector2(1.25f,.75f);break;
        }
        float speed=Vector2.Scale(velocity,factor).magnitude;
        float damage=stat==.1f?speed*damagePerMps/2.5f:damagePerMps*speed*(stat/15+1)*2.15f;
        if(shield)damage*=.5f;
        damage-=defense;
        if(category==1&&surface==OriginalSurface.SpikePommel)damage*=1.5f;
        if(armorMaterial>4) {
            if(surface==OriginalSurface.MetalBlade||surface==OriginalSurface.MetalSpike) {
                if(category==2)damage*=.85f;
                else if(category!=3&&category!=4)damage*=.7f;
            }
            if(surface==OriginalSurface.WoodBlunt)damage*=.65f;
        }
        if(surface==OriginalSurface.WoodHandle)damage*=.2f;
        if(surface==OriginalSurface.MetalHandle)damage*=.4f;
        return damage;
    }
    public static Vector2 RuntimeMass(int category,bool two,bool flailHead=false) {
        float body=1,weight=.5f;
        switch(category) {
            case 0:case 1:body=two?2:1;weight=two?.25f:1;break;
            case 2:body=two?2.25f:1;weight=two?1.25f:1;break;
            case 3:body=two?7:3;weight=two?4:2.5f;break;
            case 4:body=two?3:2;weight=two?1.5f:1.25f;break;
            case 5:case 6:body=two?3:1;break;
            case 7:body=two?3:1.5f;break;
            case 8:body=two?12:2;weight=two?1:.05f;break;
        }
        if(flailHead)body=3;
        return new Vector2(body,weight);
    }
    public static OriginalPartMode DeathMode(OriginalSurface surface) {
        if(surface==OriginalSurface.MetalBlunt||surface==OriginalSurface.WoodBlunt)return OriginalPartMode.Bashed;
        if(surface==OriginalSurface.MetalSpike||surface==OriginalSurface.SpikePommel)return OriginalPartMode.Pierced;
        return surface==OriginalSurface.MetalBlade?OriginalPartMode.Severed:OriginalPartMode.None;
    }
    public static OriginalSurface Surface(PhysicsMaterial2D material) {
        if(material==null)return OriginalSurface.Unknown;
        string name=material.name.Replace(" (Instance)","").Replace("_","").Replace(" ","").ToLowerInvariant();
        if(name.Contains("spikepommel"))return OriginalSurface.SpikePommel;
        if(name.Contains("woodhandle"))return OriginalSurface.WoodHandle;
        if(name.Contains("woodblunt"))return OriginalSurface.WoodBlunt;
        if(name.Contains("metalhandle"))return OriginalSurface.MetalHandle;
        if(name.Contains("metalblade"))return OriginalSurface.MetalBlade;
        if(name.Contains("metalspike"))return OriginalSurface.MetalSpike;
        if(name.Contains("metalblunt"))return OriginalSurface.MetalBlunt;
        return OriginalSurface.Unknown;
    }
    public static bool Handle(OriginalSurface s)=>s==OriginalSurface.WoodHandle||s==OriginalSurface.MetalHandle||s==OriginalSurface.SpikePommel;
}
