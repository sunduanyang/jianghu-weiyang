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
            if(!comparison)Check("formal arena contains active roots "+weapon.id,row["arena_bound_passed"].B,J.O("max_root_x",row["max_root_x"]));
            if(!comparison&&weapon.id!="unarmed")Check("held weapon and visible handle remain attached "+weapon.id,row["held_grip_samples"].I>100&&row["maximum_held_grip_gap"].N<.03&&row["maximum_visible_weapon_handle_gap"].N<.03,J.O("samples",row["held_grip_samples"],"joint_gap",row["maximum_held_grip_gap"],"visible_handle_gap",row["maximum_visible_weapon_handle_gap"]));
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
            Check("shield sustained contact and separated recontact "+speed,p["first_hits"].I==(speed>=20?1:0)&&p["second_hits"].I==(speed>=20?1:0),p);
            Check("shield fixture reports actual contacts even below damage threshold "+speed,p["first_contact_events"].I>0&&p["second_contact_events"].I>0,p);
        }
        // The fixture still delivers REAL Physics2D collider contacts to the
        // shipping collector. Neither the result nor its injury mode is set
        // by the probe; only the expected arithmetic uses the shared adapter.
        foreach(float speed in new[]{14.9f,15f,20f}){
            var p=arena.ProbeImpact(DuelWeapon.Fist,speed,"body");
            Check("Nothing actual source damage threshold "+speed,p["threshold"].N==15&&p["surface"].S=="WoodBlunt"&&System.Math.Abs(p["damage"].N-p["expected"].N)<.001,p);
            Check("Nothing real contact delivery "+speed,p["first_contact_events"].I>0&&p["second_contact_events"].I>0&&p["first_hits"].I==(speed>=15?1:0)&&p["second_hits"].I==(speed>=15?1:0),p);
        }
        var wood=DuelWeapon.CampaignWeapon(J.O("id","sword","name","枣木剑","template_id","wood_sword","mass",.45,"cost",7,"handling",1.08,"damage",.8,"pressure",.8,"distance",1.95));
        foreach(var spec in new[]{sword,wood})foreach(float speed in new[]{14.9f,15f,19.9f,20f,30f}){
            var p=arena.ProbeImpact(spec,speed,"body");
            Check("actual blade or wood threshold "+spec.name+" "+speed,Mathf.Abs((float)(p["damage"].N-p["expected"].N))<.001f,p);
            int accepted=speed>=p["threshold"].N?1:0;
            Check("actual blade or wood separated recontact "+spec.name+" "+speed,p["first_hits"].I==accepted&&p["second_hits"].I==accepted,p);
            Check("blade or wood fixture retains actual collision delivery "+spec.name+" "+speed,p["first_contact_events"].I>0&&p["second_contact_events"].I>0,p);
        }
        var woodInjury=arena.ProbeImpact(wood,90,"forearmNear",false,null,false);
        Check("actual wood sword collision bashes without severing",woodInjury["surface"].S=="WoodBlunt"&&woodInjury["first_hits"].I==1&&woodInjury["first_part_mode"].I==(int)DuelReferenceRules.PartMode.Bashed&&!woodInjury["first_detached"].B&&Mathf.Abs((float)(woodInjury["damage"].N-woodInjury["expected"].N))<.001f,woodInjury);
        var cloth=J.O("template_id","cloth","defense",1,"durability",100);
        var protectedBody=arena.ProbeImpact(sword,30,"body",false,cloth);
        var bareHead=arena.ProbeImpact(sword,30,"head",false,cloth);
        var brokenBody=arena.ProbeImpact(sword,30,"body",false,J.O("template_id","cloth","defense",1,"durability",0));
        Check("actual armor collision uses covered torso defense",protectedBody["armor_defense"].N==1&&Mathf.Abs((float)(protectedBody["damage"].N-protectedBody["expected"].N))<.001f,protectedBody);
        Check("actual armor collision gives no free helmet",bareHead["armor_defense"].N==0&&Mathf.Abs((float)(bareHead["damage"].N-bareHead["expected"].N))<.001f,bareHead);
        Check("actual broken armor collision loses protection",brokenBody["armor_defense"].N==0&&Mathf.Abs((float)(brokenBody["damage"].N-protectedBody["damage"].N)-.35f)<.001f&&Mathf.Abs((float)(brokenBody["damage"].N-brokenBody["expected"].N))<.001f,brokenBody);
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
