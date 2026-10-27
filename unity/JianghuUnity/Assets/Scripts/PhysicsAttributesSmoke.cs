using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Jianghu {
public sealed partial class PhysicsBattleSmoke {
    IEnumerator AuditAttributes(){
        var fixture=J.Parse(File.ReadAllText(GameClient.Arg("-attributeFixture")));
        yield return Call("/api/martial",null,j=>app.Martial=j);
        var m=app.Martial["player"]["martial"];
        Check("ten abilities receive new grade arts without legacy skill",m["combat"]["attributes"].A.Count==10&&m["arts"].A.Count>0&&!app.Martial["player"]["skill"].Has&&app.Martial["catalog"].A.Count==14);
        app.Go(3);yield return Picture("01-character-attributes");
        var scene=SceneManager.CreateScene("Character ability acceptance",new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        var root=new GameObject("Character ability acceptance");root.SetActive(false);SceneManager.MoveGameObjectToScene(root,scene);
        var arena=root.AddComponent<PhysicsDuel>();
        J neutral=fixture["neutral"];
        arena.campaign=new PhysicsDuel.CampaignOptions{playerSkin="m-youth-farmer",enemySkin="f-youth-farmer",battle=J.O("fighters",new J(new List<J>{neutral,neutral}))};
        app.enabled=false;root.SetActive(true);
        var probes=new List<J>();
        foreach(var item in fixture["cases"].A){
            var pair=new List<J>();
            foreach(string side in new[]{"low","high"}){
                arena.campaign.battle=J.O("fighters",new J(new List<J>{item[side],neutral}));arena.ResetRound();
                arena.player.testOverride=arena.enemy.testOverride=true;arena.RunCampaign(true);
                var probe=arena.player.ProbeAbilities();pair.Add(probe);
                Check("weapon mass remains equipment data",System.Math.Abs(probe["weapon_mass"].N-neutral["weapon"]["mass"].N)<.001);
            }
            J low=pair[0],high=pair[1];string key=item["id"].S;
            bool pass=key=="constitution"?high["max_health"].N>low["max_health"].N:
                key=="strength"?high["hand_force"].N>low["hand_force"].N:
                key=="power"?high["impact_damage"].N>low["impact_damage"].N:
                key=="dexterity"?high["arm_speed"].N>low["arm_speed"].N&&high["attack_duration"].N<low["attack_duration"].N:
                key=="footwork"?high["dash"].B&&high["dash_velocity"].N>low["dash_velocity"].N:
                key=="endurance"?high["max_stamina"].N>low["max_stamina"].N&&high["attack_spent"].N<low["attack_spent"].N:
                key=="breathing"?high["recovered"].N>low["recovered"].N&&high["recovery_wait"].N<low["recovery_wait"].N:
                key=="rooting"?high["balance_max"].N>low["balance_max"].N&&high["balance_lost"].N<low["balance_lost"].N:
                key=="protection"?high["impact_damage"].N<low["impact_damage"].N:
                key=="resilience"&&high["stun"].N<low["stun"].N&&high["guard_break"].N<low["guard_break"].N&&high["injury_strength"].N>low["injury_strength"].N;
            Check(key+" changes real native combat behavior",pass,J.O("low",low,"high",high));
            probes.Add(J.O("id",key,"low",low,"high",high));
        }
        File.WriteAllText(Path.Combine(output,"attribute-probes.json"),new J(probes).ToString());
        var stress=new List<J>();
        foreach(string level in new[]{"low_all","high_all"})foreach(int index in new[]{0,2,5,8}){
            var spec=J.Parse(fixture[level].ToString());var weapon=DuelWeapon.All[index];
            spec["weapon"]=AuditFighter(weapon)["weapon"];spec["shield"]=new J(false);
            arena.campaign.battle=J.O("fighters",new J(new List<J>{spec,spec}));arena.ResetRound();
            arena.player.testOverride=arena.enemy.testOverride=true;
            foreach(var a in arena.player.colliders)foreach(var b in arena.enemy.colliders)Physics2D.IgnoreCollision(a,b);
            arena.RunCampaign(true);arena.CampaignTestPad(true,Vector2.up);
            float until=Time.realtimeSinceStartup+1.8f,gap=0,limit=0;
            while(Time.realtimeSinceStartup<until){
                arena.player.stamina=arena.player.maxStamina;arena.enemy.stamina=arena.enemy.maxStamina;
                arena.player.move=Mathf.Sin(arena.Clock*3)*.6f;yield return new WaitForFixedUpdate();
                gap=Mathf.Max(gap,arena.player.MaxJointGap(),arena.enemy.MaxJointGap());
                limit=Mathf.Max(limit,arena.player.MaxLimitError(),arena.enemy.MaxLimitError());
            }
            var facts=J.O("level",level,"weapon",weapon.id,"gap",gap,"limit",limit);
            Check("ability extremes preserve physical constraints",gap<.15f&&limit<8,facts);stress.Add(facts);
        }
        File.WriteAllText(Path.Combine(output,"attribute-stress.json"),new J(stress).ToString());
        arena.PauseCampaign();yield return SceneManager.UnloadSceneAsync(scene);app.enabled=true;Time.timeScale=1;
        yield return Call("/api/intervene",J.O("action","challenge","npc_id",1),j=>app.Martial=j);
        app.ShowBattleForSmoke();yield return WaitArena();if(app.PhysicsArena==null)yield break;
        arena=app.PhysicsArena;Check("authoritative capacities reach live fighter",System.Math.Abs(arena.player.maxStamina-app.Martial["battle"]["fighters"][0]["stamina_max"].N)<.001);
        yield return Picture("02-combat-resources");
        arena.CampaignAutoplay=true;arena.RunCampaign(true);yield return new WaitForSecondsRealtime(6);arena.PauseCampaign();
        yield return app.SavePhysicsForSmoke("checkpoint");
        Check("native snapshot saves new states",app.Martial["battle"]["frame"]["fighters"][0]["regen_left"].Has);
        yield return app.SavePhysicsForSmoke("surrender");
        Check("battle settles without recreating old skill",app.Martial["battle"]["settled"].B&&!app.Martial["player"]["skill"].Has);
    }
}
}
