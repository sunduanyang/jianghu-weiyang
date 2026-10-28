using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Jianghu {
public sealed partial class PhysicsBattleSmoke {
    IEnumerator AuditReferenceStress(){
        var scene=SceneManager.CreateScene("Reference solver measurement",new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        var root=new GameObject("Reference solver measurement");root.SetActive(false);SceneManager.MoveGameObjectToScene(root,scene);
        var arena=root.AddComponent<PhysicsDuel>();arena.campaign=new PhysicsDuel.CampaignOptions{playerSkin="m-youth-farmer",enemySkin="f-youth-farmer",battle=J.O("seed",42,"fighters",new J(new List<J>{ReferenceFighter(DuelWeapon.Fist),ReferenceFighter(DuelWeapon.Fist)}))};
        app.enabled=false;root.SetActive(true);var rows=new List<J>();
        bool comparison=GameClient.Arg("-referenceStudyAll")=="1";
        int[] studySteps=comparison?new[]{4,8,12,16}:new[]{0};
        var weapons=comparison?new[]{DuelWeapon.Fist,DuelWeapon.All.First(w=>w.id=="sword"),DuelWeapon.All.First(w=>w.id=="greatsword"),DuelWeapon.All.First(w=>w.id=="spear")}:new[]{DuelWeapon.Fist}.Concat(DuelWeapon.All).ToArray();
        foreach(var weapon in weapons)foreach(int steps in studySteps){
            arena.campaign.battle["fighters"]=new J(new List<J>{ReferenceFighter(weapon,weapon.id=="sword"),ReferenceFighter(weapon,weapon.id=="sword")});
            var row=arena.ProbeReferenceStress(steps);rows.Add(row);File.WriteAllText(Path.Combine(output,"solver-measurements.json"),new J(rows).ToString());
            Check("solver measurement completed "+weapon.id+" / "+steps,row["ticks"].I>0&&row["max_gap"].N<1,row);yield return null;
            if(!comparison)Check("production solver keeps active joint limits "+weapon.id,row["limit_bound_passed"].B,row);
        }
        yield return SceneManager.UnloadSceneAsync(scene);app.enabled=true;Time.timeScale=1;
    }
    IEnumerator AuditReferenceMechanics(){
        var scene=SceneManager.CreateScene("Reference mechanics acceptance",new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        var root=new GameObject("Reference mechanics acceptance");root.SetActive(false);SceneManager.MoveGameObjectToScene(root,scene);
        var arena=root.AddComponent<PhysicsDuel>();var sword=DuelWeapon.All[2];
        arena.campaign=new PhysicsDuel.CampaignOptions{playerSkin="m-youth-farmer",enemySkin="f-youth-farmer",battle=J.O("seed",42,"fighters",new J(new List<J>{ReferenceFighter(sword,true),ReferenceFighter(sword,true)}))};
        app.enabled=false;root.SetActive(true);arena.bones=true;
        Check("small physical driver shapes have nonzero measured inertia",arena.player.ReferenceDriverInertia("handCOMNear")>0&&arena.player.ReferenceDriverInertia("handCOMNear")<.01f&&arena.player.ReferenceDriverInertia("footCOMNear")>0&&arena.player.ReferenceDriverInertia("footCOMNear")<.01f,
            J.O("hand_inertia",arena.player.ReferenceDriverInertia("handCOMNear"),"foot_inertia",arena.player.ReferenceDriverInertia("footCOMNear")));
        foreach(float speed in new[]{19.9f,20f,21f,30f}){
            var p=arena.ProbeImpact(sword,speed,"body",true);
            Check("shield actual collider damage threshold "+speed,Mathf.Abs((float)(p["damage"].N-p["expected"].N))<.001f,p);
            Check("shield sustained contact and separated recontact "+speed,p["first_hits"].I==(speed>20?1:0)&&p["second_hits"].I==(speed>20?1:0),p);
        }
        arena.campaign.battle["fighters"]=new J(new List<J>{ReferenceFighter(DuelWeapon.Fist),ReferenceFighter(DuelWeapon.Fist)});
        arena.ResetRound();arena.player.testOverride=arena.enemy.testOverride=true;arena.RunCampaign(true);
        yield return new WaitForSecondsRealtime(.6f);arena.PauseCampaign();
        Check("active constraints settle before injury audit",arena.player.MaxLimitError()<8,J.O("limit_degrees",arena.player.MaxLimitError(),"joint",arena.player.worstLimitJoint));
        arena.player.ApplyReferenceDamage("footNear",41,arena.player.limbs["footNear"].rb.position,Vector2.right,true);
        Check("severed leg blocks kicking",arena.player.ReferenceSevered("footNear")&&!arena.player.Dash());
        Check("detached hinges are excluded from active constraint audit",arena.player.root.GetComponentsInChildren<HingeJoint2D>().Any(j=>!j.enabled&&j.useLimits)&&arena.player.MaxLimitError()<8);
        foreach(string mode in new[]{"normal","mirror","windup-resume","impact-resume","out-of-range","downed"}){
            var p=arena.ProbeReferenceKick(mode=="out-of-range"?2.8f:.85f,mode=="mirror",mode=="windup-resume",mode=="impact-resume",mode=="downed");
            File.WriteAllText(Path.Combine(output,"kick-"+mode+".json"),p.ToString());
            Check("kick prevents repeat during active action "+mode,p["accepted"].B&&!p["repeat_accepted"].B);
            int attacker=mode=="mirror"?1:0,victim=1-attacker;
            var start=p["windup"]["fighters"];
            Check("kick windup has no damage or early stamina cost "+mode,start[victim]["health"].N==arena.enemy.maxHealth&&start[attacker]["stamina"].N>99);
            Check("kick charges once even when missed "+mode,p["min_stamina"].N>89&&p["min_stamina"].N<92,p);
            Check("kick actual trigger hit and recovery "+mode,p["damage"].N==(mode=="out-of-range"?0:1)&&p["hits"].I==(mode=="out-of-range"?0:1)&&p["ended"].B&&p["recovered"].B,p);
            if(mode!="out-of-range")Check("kick applies a real push before next component speed cap "+mode,p["max_push_speed"].N>0,p);
            if(mode.EndsWith("resume"))Check("kick state resumes without duplicate hit or cost "+mode,p["restored"].B);
        }
        yield return Picture("reference-mechanics");yield return SceneManager.UnloadSceneAsync(scene);app.enabled=true;Time.timeScale=1;
        yield return Call("/api/intervene",J.O("action","challenge","npc_id",1),j=>app.Martial=j);
        app.ShowBattleForSmoke();yield return WaitArena();if(app.PhysicsArena==null)yield break;
        arena=app.PhysicsArena;arena.player.testOverride=arena.enemy.testOverride=true;arena.RunCampaign(true);arena.player.Dash();
        yield return new WaitForSecondsRealtime(.28f);arena.PauseCampaign();
        yield return app.SavePhysicsForSmoke("checkpoint");var battle=app.Martial["battle"];var frame=battle["frame"];
        Check("real backend persists versioned paid kick",frame["fighters"][0]["reference"]["kick_state"]["version"].I==1&&frame["fighters"][0]["reference"]["kick_state"]["paid"].B&&battle["revision"].I>1);
        File.WriteAllText(Path.Combine(client.SaveDir,"expected-physical-frame.json"),frame.ToString());
        File.WriteAllText(Path.Combine(client.SaveDir,"expected-physical-session.json"),J.O("id",battle["id"],"month",client.World["month"].I).ToString());
    }
}
}
