using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Jianghu {
public sealed partial class PhysicsBattleSmoke {
    IEnumerator AuditKinetic(){
        var vectors=new List<J>();
        foreach(float mass in new[]{.12f,.28f,.7f,1.45f,2.35f})foreach(float speed in new[]{1.49f,1.5f,2f,5f,8f,12f})foreach(string zone in new[]{"body","head","leg"})
            vectors.Add(J.O("mass",mass,"speed",speed,"zone",zone,"damage",DuelImpact.Damage(mass,speed,1.2f,1.15f,.3f,zone)));
        File.WriteAllText(Path.Combine(output,"impact-vectors.json"),new J(vectors).ToString());
        Check("heavy kinetic impacts exceed old cap and glances have no six point floor",DuelImpact.Damage(2.35f,8,1.42f)>75&&DuelImpact.Damage(.28f,2,.72f)<6);
        var scene=SceneManager.CreateScene("Kinetic acceptance",new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        var root=new GameObject("Kinetic acceptance");root.SetActive(false);SceneManager.MoveGameObjectToScene(root,scene);
        var arena=root.AddComponent<PhysicsDuel>();arena.campaign=new PhysicsDuel.CampaignOptions{playerSkin="m-youth-farmer",enemySkin="f-youth-farmer",battle=FeelBattle(DuelWeapon.All[2])};
        app.enabled=false;root.SetActive(true);
        var probes=new List<J>();
        foreach(var profile in new[]{DuelWeapon.All[0],DuelWeapon.All[8]}){
            var result=arena.ProbeImpact(profile,6,"body");probes.Add(result);
            Check("zero stamina inactive "+profile.id+" still damages through actual contact",result["damage"].N>0&&result["stamina"].N==0&&!result["attack_active"].B,result);
            Check("actual collision damage equals uncapped kinetic law",System.Math.Abs(result["damage"].N-result["expected"].N)<.002,result);
            Check("persistent pressure hits once and separation rearms without a gesture",result["first_hits"].I==1&&result["second_hits"].I==1,result);
        }
        File.WriteAllText(Path.Combine(output,"contact-probes.json"),new J(probes).ToString());
        arena.ResetRound();arena.player.testOverride=arena.enemy.testOverride=true;
        foreach(var a in arena.player.colliders)foreach(var b in arena.enemy.colliders)Physics2D.IgnoreCollision(a,b);
        arena.RunCampaign(true);yield return new WaitForSecondsRealtime(.5f);
        arena.player.stamina=100;arena.CampaignTestPad(true,Vector2.up);yield return new WaitForFixedUpdate();
        Vector2 healthy=arena.player.handTargets["Near"].target-arena.player.limbs["upperNear"].A;
        arena.PauseCampaign();arena.ResetRound();arena.player.testOverride=arena.enemy.testOverride=true;
        foreach(var a in arena.player.colliders)foreach(var b in arena.enemy.colliders)Physics2D.IgnoreCollision(a,b);
        arena.RunCampaign(true);yield return new WaitForSecondsRealtime(.5f);arena.player.stamina=0;
        arena.CampaignTestPad(true,Vector2.up);yield return new WaitForFixedUpdate();
        Vector2 tired=arena.player.handTargets["Near"].target-arena.player.limbs["upperNear"].A;
        Check("input target follows equally when tired; only force weakens",Vector2.Distance(healthy,tired)<.035f&&arena.player.ArmCapacity<.5f,J.O("healthy_x",healthy.x,"healthy_y",healthy.y,"tired_x",tired.x,"tired_y",tired.y));
        arena.PauseCampaign();arena.campaign.battle=J.O("fighters",new J(new List<J>{AuditFighter(DuelWeapon.All[2]),AuditFighter(DuelWeapon.All[2],true)}));
        arena.ResetRound();arena.player.testOverride=true;arena.enemy.testOverride=false;arena.training=true;
        foreach(var a in arena.player.colliders)foreach(var b in arena.enemy.colliders)Physics2D.IgnoreCollision(a,b);
        arena.RunCampaign(true);yield return new WaitForSecondsRealtime(.9f);
        float shieldFront=(arena.enemy.shield.WorldPoint(Vector2.zero).x-arena.enemy.Hip.x)*arena.enemy.facing;
        Check("AI holds an equipped shield in front of the body",shieldFront>.12f,J.O("forward_distance",shieldFront));
        arena.PauseCampaign();arena.training=false;arena.campaign.battle=FeelBattle(DuelWeapon.All[2]);arena.ResetRound();arena.player.testOverride=true;arena.enemy.testOverride=false;
        arena.RunCampaign(true);float until=Time.realtimeSinceStartup+14;float gap=0,limit=0;
        while(Time.realtimeSinceStartup<until&&!arena.CampaignFinished){
            arena.player.move=0;arena.player.shieldControl=true;
            gap=Mathf.Max(gap,arena.player.MaxJointGap(),arena.enemy.MaxJointGap());limit=Mathf.Max(limit,arena.player.MaxLimitError(),arena.enemy.MaxLimitError());yield return null;
        }
        Check("AI drives manual trajectories and observes openings without preset Swing",arena.enemy.aiAttacks>1&&arena.enemy.aiDecisions>5&&arena.enemy.aiRetargets>1&&arena.enemy.slashAt<0,J.O("attacks",arena.enemy.aiAttacks,"decisions",arena.enemy.aiDecisions,"retargets",arena.enemy.aiRetargets,"target_zone",arena.enemy.aiTargetZone,"hits",arena.enemy.landedHits,"gap",gap,"limit",limit));
        Check("reactive AI constraints remain bounded",gap<.15f&&limit<8);
        yield return Picture("reactive-ai");arena.PauseCampaign();yield return SceneManager.UnloadSceneAsync(scene);app.enabled=true;Time.timeScale=1;
    }
}
}
