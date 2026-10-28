using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Jianghu {
public sealed partial class PhysicsBattleSmoke {
    IEnumerator AuditInjury(bool resume){
        if(!resume){
            var scene=SceneManager.CreateScene("Injury support acceptance",new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            var root=new GameObject("Injury support acceptance");root.SetActive(false);SceneManager.MoveGameObjectToScene(root,scene);
            var arena=root.AddComponent<PhysicsDuel>();var sword=DuelWeapon.All[2];
            arena.campaign=new PhysicsDuel.CampaignOptions{playerSkin="m-youth-farmer",enemySkin="f-youth-farmer",battle=J.O("seed",42,"fighters",new J(new List<J>{ReferenceFighter(sword),ReferenceFighter(sword)}))};
            app.enabled=false;root.SetActive(true);
            foreach(string mode in new[]{"thighNear","shinNear","footNear","thighFar","both","mirror","resume","exhaust","kick","blunt","blunt-resume"}){
                bool blunt=mode.StartsWith("blunt");
                var p=arena.ProbeReferenceInjury(mode.StartsWith("thigh")||mode=="shinNear"||mode=="footNear"?mode:"thighNear",mode=="both"||mode=="resume"||mode=="exhaust"||blunt,mode=="mirror",mode=="resume"||mode=="blunt-resume",mode=="exhaust",mode=="kick",blunt);
                File.WriteAllText(Path.Combine(output,"injury-"+mode+".json"),p.ToString());
                var facts=J.O("standing_y",p["standing_pelvis_y"],"settled_y",p["max_settled_pelvis_y"],"hand_drop",p["hand_drop"],"grounded_ticks",p["grounded_ticks"],"max_gap",p["max_gap"],"max_limit",p["max_limit"],"movement_speed",p["max_move_speed"],"detached_foot_y",p["max_detached_foot_y"]);
                Check("injury lowers pelvis and hands instead of hovering "+mode,p["max_settled_pelvis_y"].N<.5&&p["standing_pelvis_y"].N-p["max_settled_pelvis_y"].N>.4&&p["hand_drop"].N>.35,facts);
                Check("living attached body has sustained actual floor contacts "+mode,p["grounded_ticks"].I>=55,facts);
                // The source clamps before the next integration. Account for
                // one substep of its 10 N force on the 1 kg movement body.
                float postIntegrationBound=DuelReferenceRules.DownedSpeed+10*Time.fixedDeltaTime+.001f;
                Check("injury preserves living fighter and slower movement "+mode,p["health"].N>0&&p["stamina"].N>0&&p["downed"].B&&p["max_move_speed"].N<=postIntegrationBound&&p["driver_mass"].N==5,facts);
                if(blunt)Check("blunt leg injury collapses without severing "+mode,!p["part_severed"].B&&p["downed"].B);
                else Check("detached feet fall and their drivers stay inactive "+mode,p["detached_drivers_disabled"].B&&p["max_detached_foot_y"].N<.18,facts);
                Check("injury blocks or interrupts kicking "+mode,p["kick_rejected"].B&&p["kick_inactive"].B&&(mode!="kick"||p["kick_started"].B));
                Check("injured active joints stay connected "+mode,p["max_gap"].N<.15&&p["max_limit"].N<8,facts);
                if(mode=="both"||mode=="mirror"){
                    arena.RunCampaign(true);yield return Picture("injury-"+mode);arena.PauseCampaign();
                }
                yield return null;
            }
            yield return SceneManager.UnloadSceneAsync(scene);app.enabled=true;Time.timeScale=1;
        }
        if(resume)yield return Call("/api/martial",null,j=>app.Martial=j);
        else yield return Call("/api/intervene",J.O("action","challenge","npc_id",1),j=>app.Martial=j);
        app.ShowBattleForSmoke();yield return WaitArena();if(app.PhysicsArena==null)yield break;
        var real=app.PhysicsArena;real.player.testOverride=real.enemy.testOverride=true;
        string expectedPath=Path.Combine(client.SaveDir,"injury-expected-frame.json");
        if(resume){
            var expected=J.Parse(File.ReadAllText(expectedPath));var actual=real.CaptureCampaign();
            Check("cold restart preserves exact injured body pose",SameSaveValue(expected["fighters"][1]["bodies"],actual["fighters"][1]["bodies"]));
            Check("cold restart immediately restores downed state",real.enemy.ReferenceDowned&&!real.enemy.Dash());
        }else{
            real.RunCampaign(true);yield return new WaitForSecondsRealtime(.5f);real.PauseCampaign();
            foreach(string side in new[]{"Near","Far"})real.enemy.ApplyReferenceDamage("thigh"+side,41,real.enemy.pelvis.B,Vector2.zero,true);
            real.player.landedHits++;real.hits++;
        }
        real.RunCampaign(true);yield return new WaitForSecondsRealtime(1.5f);real.PauseCampaign();
        Check("shipping campaign injured opponent stays low",real.enemy.ReferenceDowned&&real.enemy.pelvis.B.y<.5,J.O("pelvis_y",real.enemy.pelvis.B.y));
        yield return Picture(resume?"injury-cold-resume":"injury-campaign");
        yield return app.SavePhysicsForSmoke("checkpoint");var frame=app.Martial["battle"]["frame"];
        Check("backend persists both severed legs without healing",frame["fighters"][1]["reference"]["parts"]["thighNear"]["severed"].B&&frame["fighters"][1]["reference"]["parts"]["thighFar"]["severed"].B, J.O("error",client.Error));
        if(!resume)File.WriteAllText(expectedPath,frame.ToString());
    }
}
}
