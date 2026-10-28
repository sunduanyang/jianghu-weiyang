using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public sealed partial class PhysicsBattleSmoke {
    static bool SameSaveValue(J a,J b){
        if(a.D.Count>0||b.D.Count>0)return a.D.Count==b.D.Count&&a.D.All(p=>b.D.ContainsKey(p.Key)&&SameSaveValue(p.Value,b[p.Key]));
        if(a.A.Count>0||b.A.Count>0)return a.A.Count==b.A.Count&&a.A.Select((p,i)=>SameSaveValue(p,b[i])).All(v=>v);
        if(a.value is string||b.value is string||a.value is bool||b.value is bool)return a.S==b.S;
        return a.Has==b.Has&&a.N==b.N;
    }
    // Isolated persistence fixture: damage releases the real grip, then an
    // imposed launch velocity lets the shipping physics carry the loose item
    // beyond the old save envelope. No collision shapes are disabled.
    IEnumerator AuditSaveBounds(bool resume){
        if(resume)yield return Call("/api/martial",null,j=>app.Martial=j);
        else yield return Call("/api/intervene",J.O("action","challenge","npc_id",1),j=>app.Martial=j);
        app.ShowBattleForSmoke();yield return WaitArena();if(app.PhysicsArena==null)yield break;
        var arena=app.PhysicsArena;
        arena.player.testOverride=arena.enemy.testOverride=true;
        Check("save bounds uses the shipping reference rig",arena.player.Reference&&arena.enemy.Reference);
        string expectedPath=Path.Combine(client.SaveDir,"bounds-expected-frame.json");
        if(!resume){
            arena.RunCampaign(true);yield return new WaitForSecondsRealtime(.2f);
            var enemy=arena.enemy;
            enemy.ApplyReferenceDamage("handNear",55,enemy.limbs["handNear"].B,Vector2.zero,true);
            if(enemy.weapon.TwoHanded)enemy.ApplyReferenceDamage("handFar",55,enemy.limbs["handFar"].B,Vector2.zero,true);
            // Account for the scripted injury; contact/damage fidelity is
            // covered separately. This fixture tests the save transaction.
            arena.player.landedHits++;arena.hits++;
            Check("physical grip releases before the throw",!enemy.primaryGrip.enabled&&(!enemy.weapon.TwoHanded||!enemy.supportJoint.enabled));
            enemy.sword.rb.linearVelocity=new Vector2(45,80);
            yield return new WaitForSecondsRealtime(.8f);arena.PauseCampaign();
            var actual=arena.CaptureCampaign();
            var loose=actual["fighters"][1]["bodies"].A.First(b=>b["key"].S=="sword");
            Check("real loose weapon exceeds the old coordinate bounds",loose["x"].N>20&&loose["y"].N>20,loose);
            File.WriteAllText(Path.Combine(output,"thrown-weapon-frame.json"),actual.ToString());
            yield return app.SavePhysicsForSmoke("checkpoint");
            var saved=app.Martial["battle"]["frame"];
            Check("checkpoint accepts the exact offstage pose",saved.Has&&SameSaveValue(saved,actual),J.O("error",client.Error));
            Check("checkpoint leaves the bout active",app.Martial["battle"]["status"].S=="active"&&!app.Martial["battle"]["settled"].B);
            File.WriteAllText(expectedPath,actual.ToString());
            yield break;
        }
        Check("restart fixture exists",File.Exists(expectedPath));if(!File.Exists(expectedPath))yield break;
        var expected=J.Parse(File.ReadAllText(expectedPath));var restored=arena.CaptureCampaign();
        float error=0;
        for(int i=0;i<2;i++){
            foreach(string group in new[]{"bodies","reference.bodies","reference.weights"}){
                var before=group=="bodies"?expected["fighters"][i]["bodies"]:expected["fighters"][i]["reference"][group.Split('.')[1]];
                var after=group=="bodies"?restored["fighters"][i]["bodies"]:restored["fighters"][i]["reference"][group.Split('.')[1]];
                foreach(var b in before.A){var a=after.A.First(p=>p["key"].S==b["key"].S);foreach(string key in new[]{"x","y","angle","vx","vy","spin"})error=Mathf.Max(error,Mathf.Abs((float)(a[key].N-b[key].N)));}
            }
        }
        Check("cold restart preserves offstage bodies and velocities",error<.0001f,J.O("maximum_error",error));
        Check("cold restart preserves the released grip",arena.enemy.ReferenceSevered("handNear")&&!arena.enemy.primaryGrip.enabled);
        int month=client.World["month"].I;
        arena.enemy.ApplyReferenceDamage("torso",arena.enemy.maxHealth*4,arena.enemy.torso.B,Vector2.zero,false);
        arena.player.landedHits++;arena.hits++;
        arena.RunCampaign(true);yield return null;yield return null;arena.PauseCampaign();
        Check("native victory is detected",arena.CampaignFinished&&arena.enemy.health<=0);
        yield return app.SavePhysicsForSmoke("complete");
        var battle=app.Martial["battle"];
        Check("offstage victory settles and records its winner",battle["settled"].B&&battle["winner"].I==0&&battle["event_id"].Has,J.O("error",client.Error));
        float returnUntil=Time.realtimeSinceStartup+8;
        while(Time.realtimeSinceStartup<returnUntil&&(app.PhysicsArena!=null||Time.timeScale!=1||app.CityMovementReasonForSmoke.Length>0))yield return null;
        Check("settlement returns to the world and restores controls",app.PhysicsArena==null&&Time.timeScale==1&&app.CityMovementReasonForSmoke.Length==0&&!client.World["battle_active"].B,
            J.O("arena_closed",app.PhysicsArena==null,"time_scale",Time.timeScale,"movement_reason",app.CityMovementReasonForSmoke,"battle_active",client.World["battle_active"]));
        Check("settlement preserves world month",client.World["month"].I==month);
        int events=client.World["events"].I;
        yield return Call("/api/battle",J.O("battle_id",battle["id"],"revision",battle["last_request_revision"],"action",battle["last_action"],"frame",battle["frame"]));
        yield return client.Refresh();
        Check("retry cannot settle the same victory twice",client.World["events"].I==events);
        File.WriteAllText(Path.Combine(output,"settled-battle.json"),battle.ToString());
    }
}
}
