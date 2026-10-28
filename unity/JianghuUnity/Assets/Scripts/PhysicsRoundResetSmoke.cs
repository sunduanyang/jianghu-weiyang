using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Jianghu {
public sealed partial class PhysicsBattleSmoke {
    IEnumerator AuditRoundReset(){
        int npc=int.Parse(GameClient.Arg("-roundResetNpc","1"));
        yield return Call("/api/martial",null,j=>app.Martial=j);
        double health=app.Martial["player"]["health"].N,energy=app.Martial["player"]["martial"]["stamina"].N;
        int month=client.World["month"].I;
        Check("fixture begins with depleted world energy",energy<12,J.O("world_health",health,"world_energy",energy));
        var rounds=new List<J>();
        for(int round=0;round<3;round++){
            yield return Call("/api/intervene",J.O("action","challenge","npc_id",npc),j=>app.Martial=j);
            app.ShowBattleForSmoke();yield return WaitArena();if(app.PhysicsArena==null)yield break;
            var arena=app.PhysicsArena;
            arena.player.testOverride=arena.enemy.testOverride=true;
            foreach(var f in new[]{arena.player,arena.enemy}){
                string side=f==arena.player?"player":"enemy";
                Check("round "+round+" "+side+" starts at full capacity",Math.Abs(f.health-f.maxHealth)<.001&&Math.Abs(f.stamina-f.maxStamina)<.001,
                    J.O("health",f.health,"max_health",f.maxHealth,"stamina",f.stamina,"max_stamina",f.maxStamina));
                Check("round "+round+" "+side+" has fresh limbs and effort",!f.ReferenceSevered("handNear")&&!f.ReferenceSevered("handFar")&&f.ReferenceLocalHealth("handNear")==50&&f.ReferenceLocalHealth("head")==70&&f.ExhaustedLeft==0&&f.motionStaminaSpent==0&&f.primaryGrip.enabled);
            }
            Check("round "+round+" has no previous snapshot",!app.Martial["battle"]["frame"].Has&&app.Martial["battle"]["resource_policy"].S=="per-bout-v1");
            if(round==0){
                arena.player.ApplyReferenceDamage("handNear",55,arena.player.limbs["handNear"].B,Vector2.zero,true);
                arena.enemy.landedHits++;arena.hits++;
                arena.player.stamina=0;arena.RunCampaign(true);
                yield return new WaitForSecondsRealtime(.12f);arena.PauseCampaign();
                Check("injury and exhaustion still work inside a bout",arena.player.ReferenceSevered("handNear")&&arena.player.PassedOut);
                yield return app.SavePhysicsForSmoke("checkpoint");
                var saved=app.Martial["battle"]["frame"]["fighters"][0];
                Check("saving the same bout preserves its injury",saved["reference"]["parts"]["handNear"]["severed"].B&&saved["reference"]["exhausted_left"].N>0&&saved["stamina"].N==0);
            }
            if(round==1){
                arena.enemy.ApplyReferenceDamage("torso",arena.enemy.maxHealth*4,arena.enemy.torso.B,Vector2.zero,false);
                arena.player.landedHits++;arena.hits++;
                arena.RunCampaign(true);yield return null;yield return null;arena.PauseCampaign();
                Check("second bout detects victory",arena.CampaignFinished&&arena.enemy.health<=0);
            }
            yield return app.SavePhysicsForSmoke(round==1?"complete":"surrender");
            var battle=app.Martial["battle"];
            Check("round "+round+" settles once",battle["settled"].B&&battle["event_id"].Has);
            Check("round "+round+" leaves world resources unchanged",app.Martial["player"]["health"].N==health&&app.Martial["player"]["martial"]["stamina"].N==energy);
            Check("round "+round+" returns without a month of rest",app.PhysicsArena==null&&!client.World["battle_active"].B&&client.World["month"].I==month);
            rounds.Add(battle);
            yield return new WaitForSecondsRealtime(.25f);
        }
        File.WriteAllText(Path.Combine(output,"consecutive-bouts.json"),new J(rounds).ToString());
    }
}
}
