using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Jianghu {
public sealed partial class PhysicsBattleSmoke {
    static J ReferenceFighter(DuelWeapon weapon,bool shield=false){var j=AuditFighter(weapon,shield);j["physics_profile"]=new J(PhysicsDuel.ReferenceProfile);return j;}
    IEnumerator AuditReference(){
        var scene=SceneManager.CreateScene("Reference combat acceptance",new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        var root=new GameObject("Reference combat acceptance");root.SetActive(false);SceneManager.MoveGameObjectToScene(root,scene);
        var arena=root.AddComponent<PhysicsDuel>();var sword=DuelWeapon.All[2];
        arena.campaign=new PhysicsDuel.CampaignOptions{playerSkin="m-youth-farmer",enemySkin="f-youth-farmer",battle=J.O("seed",42,"fighters",new J(new List<J>{ReferenceFighter(sword),ReferenceFighter(sword)}))};
        app.enabled=false;root.SetActive(true);arena.bones=true;
        Check("reference profile uses 60 Hz and APK gravity",arena.player.Reference&&Mathf.Abs(Time.fixedDeltaTime-1f/60)<.000001f&&Mathf.Abs(Physics2D.gravity.y+12.5f)<.001f);
        Check("body foot and weapon contact materials match reference",Mathf.Abs(arena.bodyMaterial.friction-.5f)<.001f&&Mathf.Abs(arena.bodyMaterial.bounciness-.25f)<.001f&&Mathf.Abs(arena.footMaterial.friction-1)<.001f&&(int)arena.bladeMaterial.frictionCombine==3&&(int)arena.bladeMaterial.bounceCombine==4);
        Check("dynamic COM rig includes counterweight and no active target joints or motors",arena.player.ReferenceBodyCount==28&&!arena.player.root.GetComponentsInChildren<TargetJoint2D>().Any(j=>j.enabled)&&!arena.player.root.GetComponentsInChildren<HingeJoint2D>().Any(j=>j.useMotor),J.O("body_count",arena.player.ReferenceBodyCount));
        foreach(var speed in new[]{10f,14.9f,16f,30f}){var p=arena.ProbeImpact(sword,speed,"body");float damage=(float)p["damage"].N;Check("real contact speed threshold "+speed,speed<=15?damage==0:damage>0,p);if(speed>15)Check("sustained contact does not repeat; recontact does",p["first_hits"].I==1&&p["second_hits"].I==1,p);}
        var cases=new List<J>();
        foreach(var w in new[]{DuelWeapon.Fist}.Concat(DuelWeapon.All)){
            arena.campaign.battle["fighters"]=new J(new List<J>{ReferenceFighter(w,w==sword),ReferenceFighter(w,w==sword)});arena.ResetRound();arena.player.testOverride=arena.enemy.testOverride=true;arena.RunCampaign(true);
            float maxGap=0,minHead=10,maxSpeed=0,skeletonGap=0,gripGap=0,weightGap=0;string worst="";float start=Time.realtimeSinceStartup;
            while(Time.realtimeSinceStartup-start<2){yield return new WaitForFixedUpdate();if(Time.realtimeSinceStartup-start>.6f){maxGap=Mathf.Max(maxGap,arena.player.MaxJointGap());minHead=Mathf.Min(minHead,arena.player.head.rb.position.y);}}
            if(w.TwoHanded)Check("two hand resting grip keeps point forward "+w.id,(arena.player.sword.B.x-arena.player.sword.WorldPoint(w.Local(Vector2.zero,1)).x)>.1f);
            float initial=arena.player.Hip.x;arena.player.move=1;
            float run=Time.realtimeSinceStartup;while(Time.realtimeSinceStartup-run<1){yield return new WaitForFixedUpdate();}
            float travel=arena.player.Hip.x-initial;arena.player.move=0;
            start=Time.realtimeSinceStartup;
            while(Time.realtimeSinceStartup-start<2){float a=(Time.realtimeSinceStartup-start)*12;arena.CampaignTestPad(true,new Vector2(Mathf.Cos(a),Mathf.Sin(a)));arena.CampaignTestPad(false,new Vector2(Mathf.Cos(a+.2f),Mathf.Sin(a+.2f)));yield return new WaitForFixedUpdate();float gap=arena.player.MaxJointGap();if(gap>maxGap){maxGap=gap;worst=arena.player.worstJoint;}skeletonGap=Mathf.Max(skeletonGap,arena.player.ReferenceSkeletonGap());gripGap=Mathf.Max(gripGap,arena.player.PrimaryGripError,arena.player.SupportGripError);weightGap=Mathf.Max(weightGap,arena.player.ReferenceWeightGap());maxSpeed=Mathf.Max(maxSpeed,arena.player.sword.rb.linearVelocity.magnitude);}
            arena.CampaignReleasePads();yield return Picture("reference-"+w.id);arena.PauseCampaign();
            var record=J.O("weapon",w.id,"max_gap",maxGap,"skeleton_gap",skeletonGap,"grip_gap",gripGap,"counterweight_gap",weightGap,"item_mass",arena.player.ReferenceItemMass,"counterweight_mass",arena.player.ReferenceWeightMass,"min_head_y",minHead,"travel",travel,"max_weapon_speed",maxSpeed,"motion_spent",arena.player.motionStaminaSpent,"health",arena.player.health,"worst",worst);cases.Add(record);
            Check("standing movement and independent hand driving "+w.id,maxGap<.20f&&weightGap<.1f&&minHead>1.1f&&travel>.3f&&maxSpeed>6,record);
            float expectedMass=w==DuelWeapon.Fist?.12f:w.id=="mace"||w.id=="spear"||w.id=="warhammer"?3:w.id=="greataxe"?2.25f:w.id=="greatsword"?2:1;
            float expectedWeight=w==DuelWeapon.Fist?0:w.id=="mace"?2.5f:w.id=="spear"?.5f:w.id=="warhammer"?1.5f:w.id=="greataxe"?1.25f:w.id=="greatsword"?.25f:1;
            Check("runtime item and counterweight masses "+w.id,Mathf.Abs(arena.player.ReferenceItemMass-expectedMass)<.001f&&Mathf.Abs(arena.player.ReferenceWeightMass-expectedWeight)<.001f,record);
            File.WriteAllText(Path.Combine(output,"reference-"+w.id+"-frame.json"),arena.CaptureCampaign().ToString());
        }
        arena.campaign.battle["fighters"]=new J(new List<J>{ReferenceFighter(sword),ReferenceFighter(sword)});arena.ResetRound();arena.player.testOverride=arena.enemy.testOverride=true;arena.RunCampaign(true);yield return new WaitForSecondsRealtime(.5f);
        arena.player.stamina=0;yield return new WaitForSecondsRealtime(.1f);Check("zero stamina causes three second collapse",arena.player.PassedOut&&arena.player.ExhaustedLeft>2.8f);
        var captured=arena.CaptureCampaign();arena.ResetRound();arena.player.testOverride=arena.enemy.testOverride=true;arena.RestoreCampaignForSmoke(captured);Check("exhaustion survives fresh rig restore",arena.player.PassedOut);arena.RunCampaign(true);
        yield return new WaitForSecondsRealtime(3.1f);Check("exhaustion wakes with stamina and recovery",!arena.player.PassedOut&&arena.player.stamina>=10);
        arena.player.ApplyReferenceDamage("forearmNear",40,arena.player.limbs["forearmNear"].rb.position,Vector2.right,true);
        Check("limb injury disconnects descendants and drops weapon",arena.player.ReferenceSevered("handNear")&&!arena.player.primaryGrip.enabled);
        var saved=arena.CaptureCampaign();arena.ResetRound();arena.RestoreCampaignForSmoke(saved);Check("severed limb survives fresh rig restore",arena.player.ReferenceSevered("handNear")&&!arena.player.primaryGrip.enabled);
        arena.ResetRound();arena.CampaignAutoplay=true;arena.RunCampaign(true);yield return new WaitForSecondsRealtime(18);arena.PauseCampaign();
        Check("AI reaches real damaging contacts",arena.hits>0,J.O("hits",arena.hits,"clashes",arena.blocks,"player_health",arena.player.health,"enemy_health",arena.enemy.health));
        File.WriteAllText(Path.Combine(output,"reference-ai-frame.json"),arena.CaptureCampaign().ToString());yield return Picture("reference-ai");
        File.WriteAllText(Path.Combine(output,"reference-cases.json"),new J(cases).ToString());
        yield return SceneManager.UnloadSceneAsync(scene);app.enabled=true;Time.timeScale=1;
        yield return Call("/api/intervene",J.O("action","challenge","npc_id",1),j=>app.Martial=j);
        app.ShowBattleForSmoke();yield return WaitArena();if(app.PhysicsArena==null)yield break;
        arena=app.PhysicsArena;Check("real campaign creates reference profile",arena.player.Reference);
        arena.enemy.testOverride=true;arena.RunCampaign(true);arena.CampaignTestPad(true,Vector2.up);yield return new WaitForSecondsRealtime(.6f);arena.CampaignReleasePads();arena.PauseCampaign();
        yield return app.SavePhysicsForSmoke("checkpoint");
        var battle=app.Martial["battle"];var frame=battle["frame"];
        Check("real backend accepts all reference bodies and timers",frame["fighters"][0]["reference"]["bodies"].A.Count==10&&battle["revision"].I>1);
        File.WriteAllText(Path.Combine(client.SaveDir,"expected-physical-frame.json"),frame.ToString());
        File.WriteAllText(Path.Combine(client.SaveDir,"expected-physical-session.json"),J.O("id",battle["id"],"month",client.World["month"].I).ToString());
        File.WriteAllText(Path.Combine(output,"campaign-frame.json"),frame.ToString());
    }
}
}
