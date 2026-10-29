using System;
using UnityEngine;
// Run outside Unity to check translated arithmetic against independently
// calculated ARM64 constants. This does not establish APK gameplay parity.
public class OriginalRig {} public class OriginalItem {} public class OriginalBodyPart {}
public static class BloodyCombatProbe {
    static int checks;
    static void Equal(float actual,float expected){checks++;if(Math.Abs(actual-expected)>.0001f)throw new Exception(actual+" != "+expected);}
    static void Assert(bool value){checks++;if(!value)throw new Exception("failed check "+checks);}
    public static void Main() {
        float[] expected={7.11436173f,8.01853708f,7.11436173f,8.01853708f,10.75973407f,11.01849525f,12.65851067f,12.96293559f,13.50361343f,14.55116520f,9.43378036f,10.18581564f,8.86095747f,9.07405491f,8.63640039f,9.07405491f,3.48702745f,3.48702745f};
        for(int category=0;category<9;category++)for(int hands=0;hands<2;hands++)
            Equal(OriginalCombatRules.RawDamage(category,hands==1,new Vector2(12,20),6,3,OriginalSurface.MetalBlade,category==8,6),expected[category*2+hands]);
        // Armor subtraction precedes surface scaling and preclamp knockback.
        Equal(OriginalCombatRules.RawDamage(1,false,Vector2.zero,6,50,OriginalSurface.WoodHandle,false,6),-10);
        Equal(OriginalCombatRules.RawDamage(1,false,Vector2.zero,6,50,OriginalSurface.MetalHandle,false,6),-20);
        Equal(OriginalCombatRules.RawDamage(1,false,Vector2.zero,6,50,OriginalSurface.SpikePommel,false,6),-75);
        Equal(OriginalCombatRules.RawDamage(0,false,new Vector2(100,0),.1f,0,OriginalSurface.Unknown,false,0),7.5f);
        Equal(OriginalCombatRules.RawDamage(0,false,new Vector2(40f/3,0),.1f,1e10f,OriginalSurface.Unknown,false,0,.25f,-1e10f,0),0);
        int count=0;float until=0;
        Assert(OriginalCombatRules.RegisterImpact(1,ref count,ref until));
        Assert(OriginalCombatRules.RegisterImpact(1.01f,ref count,ref until));
        Assert(OriginalCombatRules.RegisterImpact(1.02f,ref count,ref until));
        Assert(!OriginalCombatRules.RegisterImpact(1.03f,ref count,ref until));
        Assert(!OriginalCombatRules.RegisterImpact(until,ref count,ref until));
        Assert(OriginalCombatRules.RegisterImpact(until+.00001f,ref count,ref until));Assert(count==1);
        Equal(OriginalCombatRules.RuntimeMass(3,true).x,7);Equal(OriginalCombatRules.RuntimeMass(3,true).y,4);
        Equal(OriginalCombatRules.RuntimeMass(5,true).x,3);Equal(OriginalCombatRules.RuntimeMass(5,true).y,.5f);
        Equal(OriginalCombatRules.RuntimeMass(8,true).x,12);Equal(OriginalCombatRules.RuntimeMass(8,false).y,.05f);
        Assert(OriginalCombatRules.DeathMode(OriginalSurface.MetalBlade)==OriginalPartMode.Severed);
        Assert(OriginalCombatRules.DeathMode(OriginalSurface.MetalSpike)==OriginalPartMode.Pierced);
        Assert(OriginalCombatRules.DeathMode(OriginalSurface.WoodBlunt)==OriginalPartMode.Bashed);
        Assert(OriginalCombatRules.DeathMode(OriginalSurface.WoodHandle)==OriginalPartMode.None);
        Console.WriteLine("{\"checks\":"+checks+",\"passed\":true,\"source\":\"APK ARM64 translated arithmetic\",\"runtime_parity_verified\":false}");
    }
}
