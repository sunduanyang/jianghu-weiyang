using System.IO;
using Jianghu;
using UnityEngine;

// One data file owns the live and offscreen damage law. No small damage floor
// or per-hit upper cap: mass, closing speed, armour and body zone remain visible.
public static class DuelImpact {
    static J settings;
    static J Settings=>settings??(settings=J.Parse(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"Backend/jianghu/physical_impact.json"))));
    public static float MinimumSpeed=>(float)Settings["minimum_closing_speed"].N;
    public static float Damage(float mass,float speed,float weaponDamage=1,float attackScale=1,float reduction=0,string zone="body"){
        if(mass<=0||speed<MinimumSpeed)return 0;
        float energy=.5f*mass*speed*speed;
        J multiplier=Settings["zone_multipliers"][zone];
        return (float)Settings["energy_scale"].N*Mathf.Pow(energy,(float)Settings["energy_exponent"].N)
            *weaponDamage*attackScale*(1-Mathf.Clamp01(reduction))*(multiplier.Has?(float)multiplier.N:1);
    }
}
