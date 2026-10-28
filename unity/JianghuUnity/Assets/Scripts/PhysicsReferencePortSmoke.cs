using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Jianghu {
public sealed partial class PhysicsBattleSmoke {
    IEnumerator AuditReferencePort(bool resume){
        if(!resume){
            // Literal expected outputs reconstructed from the native branches;
            // the expected values are not computed with the implementation.
            var damageCases=new[]{
                J.O("name","ordinary shield","id","sword","surface",2,"shield",true,"stat",1,"armor",0,"plate",0,"expected",8.6),
                J.O("name","metal sword handle","id","sword","surface",5,"shield",false,"stat",1,"armor",3,"plate",0,"expected",2.928),
                J.O("name","wood sword handle","id","sword","surface",4,"shield",false,"stat",1,"armor",3,"plate",0,"expected",1.464),
                J.O("name","blade against plate","id","sword","surface",0,"shield",false,"stat",1,"armor",0,"plate",5,"expected",7.224),
                J.O("name","exact point one branch","id","sword","surface",0,"shield",false,"stat",.1,"armor",0,"plate",0,"expected",1.8)};
            foreach(var c in damageCases){var d=DuelReferenceRules.CalculateDamage(c["id"].S,false,new Vector2(24,0),(float)c["stat"].N,(float)c["armor"].N,(DuelReferenceRules.Surface)c["surface"].I,c["shield"].B,c["plate"].I);Check("native damage branch "+c["name"].S,Math.Abs(d.local-c["expected"].N)<.001,J.O("actual",d.local,"expected",c["expected"]));}
            var scene=SceneManager.CreateScene("Recovered combat core acceptance",new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            var root=new GameObject("Recovered combat core acceptance");root.SetActive(false);SceneManager.MoveGameObjectToScene(root,scene);
            var arena=root.AddComponent<PhysicsDuel>();var sword=DuelWeapon.All[2];
            arena.campaign=new PhysicsDuel.CampaignOptions{playerSkin="m-youth-farmer",enemySkin="f-youth-farmer",battle=J.O("seed",42,"fighters",new J(new List<J>{ReferenceFighter(sword,true),ReferenceFighter(sword,true)}))};
            app.enabled=false;root.SetActive(true);
            var swordContact=arena.ProbeImpact(sword,24,"body");Check("native sword result reaches actual collision pipeline",Math.Abs(swordContact["damage"].N-3.612)<.001,swordContact);
            var shieldContact=arena.ProbeImpact(sword,24,"body",true);Check("native shield stat one reaches actual collision pipeline",Math.Abs(shieldContact["damage"].N-3.01)<.001,shieldContact);
            var p=arena.ProbeReferenceCore();
            File.WriteAllText(Path.Combine(output,"core-transitions.json"),p.ToString());var t=p["transitions"];
            Check("source counter extends after accepted impacts and has separate part cooldown",t["first"].B&&!t["same_part"].B&&t["second"].B&&t["third"].B&&!t["fourth"].B&&t["after_quiet"].B);
            Check("root cap applies while kicked and passed out",Math.Abs(t["kick_clamped_speed"].N-3.6)<.001&&Math.Abs(t["passed_out_clamped_speed"].N-3.6)<.001);
            Check("kick coroutine and restore do not introduce damping five",t["kick_damping"].N==1&&t["restored_kick_damping"].N==1);
            var o=p["passed_out"];Check("source passed out body parameters are used",o["active"].B&&o["arm_mass"].N==10&&o["arm_drag"].N==5&&o["arm_spin"].N==25&&o["foot_spin"].N==50);
            Check("wake uses absolute ten even with capacity 140",p["wake_max_stamina"].N==140&&p["wake_stamina"].N>=10&&p["wake_stamina"].N<10.5);
            var dead=p["dead"];Check("death and exhaustion have separate damping",dead["arm_spin"].N==25&&dead["foot_spin"].N==500&&Math.Abs(dead["trunk_spin"].N-.05)<.001);
            var l=p["limbs"];Check("bashed arm loses grip without cut or bleeding",l["bashed"]["mode"].I==1&&!l["bashed"]["detached"].B&&!l["bashed"]["bleeding"].B&&!l["bashed"]["grip_enabled"].B);
            var cut=l["cut"];Check("sever cuts struck hinge and retains child chain",cut["mode"].I==3&&!cut["root_joint"].B&&cut["child_joint"].B&&cut["child_detached"].B&&cut["mass"].N==9&&cut["spin"].N==5&&cut["bleeding"].B);
            Check("bleeding initial delay and repeated passed out damage",l["bleed_before"].N==0&&l["bleed_first"].N==.5&&l["bleed_second_while_passed_out"].N==1);
            Check("core timers modes and budget round trip",l["saved_core"].ToString()==l["restored_core"].ToString());
            Check("pierced injury retains hinge and starts bleeding",l["pierced"]["mode"].I==2&&l["pierced"]["joint"].B&&!l["pierced"]["detached"].B&&l["pierced"]["bleeding"].B);
            Check("head injury uses absolute twenty with health capacity 140",l["head_above_twenty"].B&&l["head_below_twenty"].B);
            yield return SceneManager.UnloadSceneAsync(scene);app.enabled=true;Time.timeScale=1;
        }
        if(resume)yield return Call("/api/martial",null,j=>app.Martial=j);
        else yield return Call("/api/intervene",J.O("action","challenge","npc_id",1),j=>app.Martial=j);
        app.ShowBattleForSmoke();yield return WaitArena();if(app.PhysicsArena==null)yield break;
        var real=app.PhysicsArena;real.player.testOverride=real.enemy.testOverride=true;
        string path=Path.Combine(client.SaveDir,"core-expected-frame.json");
        if(resume){
            var expected=J.Parse(File.ReadAllText(path));var actual=real.CaptureCampaign();
            Check("cold restart restores core state through actual game backend",SameSaveValue(actual["fighters"][1]["reference"]["core_state"],expected["fighters"][1]["reference"]["core_state"]));
            Check("cold restart keeps bashed and severed modes",real.enemy.ReferencePartMode("forearmFar")==DuelReferenceRules.PartMode.Bashed&&real.enemy.ReferencePartMode("forearmNear")==DuelReferenceRules.PartMode.Severed);
            yield return app.SavePhysicsForSmoke("surrender");Check("restored formal battle settles",app.Martial["battle"]["settled"].B);
        }else{
            real.enemy.ApplyReferenceDamage("forearmFar",31,real.enemy.pelvis.B,Vector2.zero,false);
            real.enemy.ApplyReferenceDamage("forearmNear",31,real.enemy.pelvis.B,Vector2.zero,true);
            real.player.landedHits+=2;real.hits+=2;
            yield return app.SavePhysicsForSmoke("checkpoint");var saved=app.Martial["battle"]["frame"];
            Check("formal campaign backend accepts new core state",saved["fighters"][1]["reference"]["core_state"]["version"].I==1&&saved["fighters"][1]["reference"]["core_state"]["bleeding"].B,J.O("error",client.Error));
            File.WriteAllText(path,saved.ToString());
        }
    }
}
}
