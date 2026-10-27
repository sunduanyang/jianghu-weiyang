using System.Collections;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public sealed partial class PhysicsBattleSmoke {
    IEnumerator AuditMartial(){
        yield return Call("/api/martial",null,j=>app.Martial=j);
        var m=app.Martial["player"]["martial"];
        Check("catalog contains fourteen complete nine-grade traditions",app.Martial["catalog"].A.Count==14&&app.Martial["catalog"].A.All(s=>s["arts"].A.Count==9));
        Check("fifth-grade player knows grades five through nine",m["curriculum"]["grade"].I==5&&m["arts"].A.Select(a=>a["grade"].I).OrderBy(x=>x).SequenceEqual(new[]{5,6,7,8,9}));
        bool bonuses=m["combat"]["attributes"].A.All(a=>System.Math.Abs(a["value"].N-System.Math.Min(100,a["base"].N+a["bonus"].N))<.001);
        Check("selected passive bonuses reach displayed abilities",bonuses&&m["combat"]["attributes"].A.Any(a=>a["bonus"].N>0));
        app.Go(3);yield return Picture("01-martial-character");
        yield return Call("/api/faction/0",null,j=>Check("sect page exposes all nine arts",j["martial_curriculum"]["arts"].A.Count==9));
        yield return Call("/api/npc/1",null,j=>Check("nonmember knows exactly one native-country art",j["martial_view"]["curriculum"]["system"].S=="civil"&&j["martial_view"]["arts"].A.Count==1));
        var scores=m["combat"]["ratings"];
        yield return Call("/api/intervene",J.O("action","challenge","npc_id",1),j=>app.Martial=j);
        app.ShowBattleForSmoke();yield return WaitArena();if(app.PhysicsArena==null)yield break;
        var f=app.Martial["battle"]["fighters"][0];var p=f["combat_parameters"];var arena=app.PhysicsArena;
        Check("effective arts-backed ratings enter the authoritative bout",scores.D.All(kv=>System.Math.Abs(kv.Value.N-f["combat_ratings"][kv.Key].N)<.001));
        Check("art-backed handling damage and stamina reach real fighter",System.Math.Abs(arena.player.handlingScale-p["handling_scale"].N)<.001&&System.Math.Abs(arena.player.damageScale-p["damage_scale"].N)<.001&&System.Math.Abs(arena.player.maxStamina-p["stamina_max"].N)<.001);
        arena.enemy.testOverride=true;arena.RunCampaign(true);arena.CampaignTestPad(true,Vector2.up);
        yield return new WaitForSecondsRealtime(2);arena.PauseCampaign();arena.CampaignReleasePads();
        yield return app.SavePhysicsForSmoke("checkpoint");
        Check("bout with selective bonuses checkpoints",app.Martial["battle"]["frame"].Has&&app.Martial["player"]["martial"]["arts"].A.Count==5);
        yield return Picture("02-martial-bout");
        yield return app.SavePhysicsForSmoke("surrender");
        Check("settlement retains entitlement and never folds bonuses into base",app.Martial["battle"]["settled"].B&&app.Martial["player"]["martial"]["arts"].A.Count==5&&app.Martial["player"]["martial"]["combat"]["attributes"].A.All(a=>System.Math.Abs(a["base"].N-50)<.001));
    }
}
}
