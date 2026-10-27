using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Jianghu {
public sealed partial class PhysicsBattleSmoke {
    IEnumerator AuditPace(){
        var fixture=J.Parse(File.ReadAllText(GameClient.Arg("-paceFixture")));
        var scene=SceneManager.CreateScene("Combat pace acceptance",new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        var root=new GameObject("Combat pace acceptance");root.SetActive(false);SceneManager.MoveGameObjectToScene(root,scene);
        var arena=root.AddComponent<PhysicsDuel>();arena.campaign=new PhysicsDuel.CampaignOptions{playerSkin="m-youth-farmer",enemySkin="f-youth-farmer",battle=J.O("fighters",new J(new List<J>{fixture["before"],fixture["before"]}))};
        app.enabled=false;root.SetActive(true);
        var results=new List<J>();
        foreach(string version in new[]{"before","after"}){
            arena.PauseCampaign();arena.campaign.battle=J.O("fighters",new J(new List<J>{fixture[version],fixture[version]}));arena.ResetRound();
            arena.player.testOverride=arena.enemy.testOverride=true;
            foreach(var a in arena.player.colliders)foreach(var b in arena.enemy.colliders)Physics2D.IgnoreCollision(a,b);
            arena.RunCampaign(true);yield return new WaitForSecondsRealtime(.5f);
            J probe=arena.player.ProbePace();probe["version"]=new J(version);arena.CampaignReleasePads();
            float startX=arena.player.Hip.x,start=arena.Clock,gap=0,limit=0;
            arena.player.move=1;
            while(arena.Clock-start<1.2f){gap=Mathf.Max(gap,arena.player.MaxJointGap());limit=Mathf.Max(limit,arena.player.MaxLimitError());yield return new WaitForFixedUpdate();}
            probe["walk_distance"]=new J(arena.player.Hip.x-startX);probe["gap"]=new J(gap);probe["limit"]=new J(limit);results.Add(probe);
            Check("faster footwork remains physically bounded "+version,gap<.15f&&limit<8,probe);
        }
        J old=results[0],current=results[1];
        Check("new baseline supplies stronger real fighter capacities",current["max_health"].N>=110&&current["max_stamina"].N>=120&&current["force_scale"].N>old["force_scale"].N,current);
        Check("real movement is visibly faster",current["walk_distance"].N>old["walk_distance"].N*1.2,J.O("before",old["walk_distance"],"after",current["walk_distance"]));
        Check("identical effort costs roughly half as much",current["ten_point_effort_paid"].N<old["ten_point_effort_paid"].N*.55,current);
        Check("idle recovery is at least twice as fast with a shorter delay",current["idle_regen_quarter_second"].N>old["idle_regen_quarter_second"].N*2&&current["recovery_delay"].N<old["recovery_delay"].N*.55,current);
        Check("remembered static goals recover as fast as idle",System.Math.Abs(current["static_target_regen_quarter_second"].N-current["idle_regen_quarter_second"].N)<.001,current);
        File.WriteAllText(Path.Combine(output,"pace-comparison.json"),new J(results).ToString());
        arena.PauseCampaign();arena.campaign.battle=J.O("fighters",new J(new List<J>{fixture["after"],fixture["after"]}));arena.ResetRound();arena.player.testOverride=arena.enemy.testOverride=true;
        foreach(var a in arena.player.colliders)foreach(var b in arena.enemy.colliders)Physics2D.IgnoreCollision(a,b);
        arena.RunCampaign(true);yield return new WaitForSecondsRealtime(.5f);
        var m=arena.MouseForSmoke;var fighter=arena.player;Vector2 pointer=new Vector2(700,460);
        m.Press(true,pointer,fighter.MouseSeed(true),fighter.MouseSeed(false),0,fighter.MouseAxis(true),fighter.MouseAxis(false));
        Vector2 previous=m.main.value;float at=arena.Clock;
        while(arena.Clock-at<1){
            float angle=(arena.Clock-at)*6;Vector2 target=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*.85f;
            Vector2 delta=(target-previous)*fighter.MousePixels(true);pointer+=new Vector2(delta.x,-delta.y);
            m.Move(pointer,fighter.MousePixels(true),fighter.MousePixels(false),arena.Clock-at+.01f);previous=target;yield return new WaitForFixedUpdate();
        }
        m.Release(true,1.01f);float released=arena.Clock,last=fighter.sword.Rotation,turn=0;
        while(arena.Clock-released<.7f){yield return new WaitForFixedUpdate();float angle=fighter.sword.Rotation;turn+=Mathf.Abs(Mathf.DeltaAngle(last,angle));last=angle;}
        float spin=Mathf.Abs(fighter.sword.rb.angularVelocity-fighter.torso.rb.angularVelocity);
        Check("released real weapon brakes without continuing full revolutions",turn<180&&spin<90&&!m.main.Coasting,J.O("actual_turn_degrees",turn,"residual_degrees_per_second",spin,"target_coast_degrees",m.main.travel));
        arena.PauseCampaign();yield return SceneManager.UnloadSceneAsync(scene);app.enabled=true;Time.timeScale=1;
    }
}
}
