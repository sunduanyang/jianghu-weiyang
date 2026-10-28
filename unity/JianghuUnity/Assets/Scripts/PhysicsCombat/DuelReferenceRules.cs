using UnityEngine;

// Recovered control flow is kept separate from this game's rigs and saves.
// Evidence addresses are ELF virtual addresses in the supplied 5.0.3 ARM64 lib.
public static class DuelReferenceRules {
    public const float DownedSpeed=1.75f,ImpactWindow=.1f,PartCooldown=.15f;
    public const float WakeStamina=10,BleedDelay=.65f,BleedPeriod=.25f,BleedDamage=.5f;
    public enum PartMode { None, Bashed, Pierced, Severed }
    public enum Surface { MetalBlade, MetalSpike, MetalBlunt, WoodBlunt, WoodHandle, MetalHandle, SpikePommel }
    public struct Damage {public float local,knockback;}
    // CharacterEnemyAI.cjm/cjn, VA 0x342ca4c / 0x342cc08. Heights
    // are relative to the opponent's CHEST COM, not its head driver.
    public static bool AttackUp(float chestY,bool hasThreat,float threatY,bool hasShield,float shieldY,float aggression,float roll){
        float Distance(float y){float d=hasThreat?Mathf.Abs(y-threatY):float.PositiveInfinity;if(hasShield)d=Mathf.Min(d,Mathf.Abs(y-shieldY));return float.IsPositiveInfinity(d)?1:d;}
        bool up=Distance(chestY+.5f)>=Distance(chestY-.4f);
        return roll<.5f+Mathf.Clamp01(aggression)*.45f?up:!up;
    }

    // CharacterDamageCalculator.RegisterImpact, VA 0x34281b8. The deadline
    // moves after each accepted impact; this is not a rolling timestamp queue.
    public static bool RegisterImpact(float now,ref int count,ref float deadline){
        if(now>deadline)count=0;
        if(count>2)return false;
        count++;deadline=now+ImpactWindow;return true;
    }
    public static bool IsHandle(Surface material)=>material==Surface.WoodHandle||material==Surface.MetalHandle||material==Surface.SpikePommel;
    public static PartMode ModeFor(Surface material)=>material==Surface.MetalBlade?PartMode.Severed:material==Surface.MetalSpike||material==Surface.SpikePommel?PartMode.Pierced:material==Surface.MetalBlunt||material==Surface.WoodBlunt?PartMode.Bashed:PartMode.None;

    // OnHandItemImpact_Authoritative, VA 0x3428224. Input D is the original
    // damage-stat scale. A caller adapting RPG equipment must do so explicitly.
    public static Damage CalculateDamage(string id,bool two,Vector2 velocity,float stat,float defense,Surface material,bool shield=false,int armorMaterial=0){
        Vector2 factor=shield?new Vector2(1.25f,.75f):id=="spear"?new Vector2(two?1.25f:1.15f,.75f):id=="sword"||id=="dagger"||id=="sabre"||id=="greatsword"?new Vector2(two?1:.75f,.75f):id=="warhammer"?new Vector2(two?1:.75f,1):id=="axe"||id=="greataxe"||id=="mace"?(two?new Vector2(1.25f,.75f):new Vector2(1,.85f)):Vector2.one;
        float speed=Vector2.Scale(velocity,factor).magnitude;
        float raw=stat==.1f?speed*.25f/2.5f:speed*(1+stat/15)*.25f*2.15f;
        if(shield)raw*=.5f;
        raw-=defense;
        bool sword=id=="sword"||id=="sabre"||id=="greatsword";
        if(sword&&material==Surface.SpikePommel)raw*=1.5f;
        if(armorMaterial>=5){
            if(material==Surface.MetalBlade||material==Surface.MetalSpike){
                if(id=="axe"||id=="greataxe")raw*=.85f;
                else if(id!="mace"&&id!="warhammer")raw*=.7f;
            }
            if(material==Surface.WoodBlunt)raw*=.65f;
        }
        if(material==Surface.WoodHandle)raw*=.2f;
        if(material==Surface.MetalHandle)raw*=.4f;
        return new Damage{local=Mathf.Clamp(raw,1,100),knockback=raw};
    }
    // BodyPart.SetPhysicsProps_PassedOut/Dead, VA 0x341b710 / 0x3419b0c.
    // Vector = mass, linear damping, angular damping.
    public static Vector3 RelaxedPhysics(string key,bool dead){
        bool trunk=key=="torso"||key=="belly"||key=="pelvis";
        bool arm=key.StartsWith("upper")||key.StartsWith("forearm")||key.StartsWith("hand");
        bool foot=key.StartsWith("foot"),leg=key.StartsWith("thigh")||key.StartsWith("shin");
        return new Vector3(trunk?20:10,dead?(arm||foot||leg?.05f:0):arm?5:0,foot?(dead?500:50):arm?25:.05f);
    }
}
