using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Jianghu {
public sealed partial class PhysicsBattleSmoke {
    IEnumerator AuditSourceProgram(bool resume){
        if(!resume){
            Check("all eighteen source bundles and seven clips load",DuelSourceMotion.BundleCount==18&&DuelSourceMotion.ClipCount==7);
            Check("native direction chooses chest-relative opening and reverses on failed preference",
                !DuelReferenceRules.AttackUp(1,true,1.5f,false,0,1,0)&&DuelReferenceRules.AttackUp(1,true,.6f,false,0,1,0)
                &&DuelReferenceRules.AttackUp(1,false,0,false,0,1,0)&&DuelReferenceRules.AttackUp(1,true,1.5f,false,0,0,.9f));
            var swing=DuelSourceMotion.Named("1Hand_SwingDown");
            Vector2 p=swing.Position("",.21f);
            Check("source Hermite tangents retained between keys",Vector2.Distance(p,new Vector2(.162599885f,.906808816f))<.00001f,J.O("x",p.x,"y",p.y));
            foreach(string kind in new[]{"Dagger","Sword","Axe","Mace","Hammer","Flail","Stick","Spear","Shield"})
                foreach(bool two in new[]{false,true}){
                    var rng=new System.Random(42);bool valid=true;
                    for(int i=0;i<10;i++)foreach(bool up in new[]{false,true})valid&=DuelSourceMotion.Choose(kind,two,up,rng)!=null;
                    Check("source bundle "+kind+(two?" 2H":" 1H"),valid);
                }
            var scene=SceneManager.CreateScene("Recovered source program acceptance",new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            var root=new GameObject("Recovered source program acceptance");root.SetActive(false);SceneManager.MoveGameObjectToScene(root,scene);
            var arena=root.AddComponent<PhysicsDuel>();var sword=DuelWeapon.All[2];
            arena.campaign=new PhysicsDuel.CampaignOptions{playerSkin="m-youth-farmer",enemySkin="f-youth-farmer",battle=J.O("seed",42,"fighters",new J(new List<J>{ReferenceFighter(sword,true),ReferenceFighter(sword,true)}))};
            app.enabled=false;root.SetActive(true);
            var result=arena.ProbeSourceProgram();File.WriteAllText(Path.Combine(output,"source-program.json"),result.ToString());
            foreach(string side in new[]{"player","enemy"}){
                var f=result[side];float facing=(float)f["facing"].N;
                Check("source hand target with facing "+side,Mathf.Abs((float)f["x"].N-facing*(-.4569215f))<.0001f&&Mathf.Abs((float)f["y"].N-2.7428551f)<.0001f,f);
                Check("missing original animation bindings preserve target "+side,f["unbound_safe"].B);
                Check("original kick foot and pelvis curves "+side,Mathf.Abs((float)f["kick_x"].N-facing*.602101994f)<.0001f&&Mathf.Abs((float)f["kick_y"].N-.350116563f)<.0001f&&Mathf.Abs((float)f["pelvis_x"].N-facing*(-.207771331f))<.0001f&&Mathf.Abs((float)f["pelvis_y"].N-.069311917f)<.0001f,f);
            }
            var d=result["death"];var stages=d["stages"];
            Check("death starts before body properties change",stages[0]["stage"].I==1&&stages[0]["arm_mass"].N==3&&stages[0]["grip"].B);
            Check("death stage two applies original dead body parameters",stages[1]["stage"].I==2&&stages[1]["arm_mass"].N==10);
            Check("death stage three softens joints before dropping",stages[2]["stage"].I==3&&stages[2]["frequency"].N==10&&stages[2]["grip"].B);
            Check("death stage four activates corpse collider and drops gear",stages[3]["stage"].I==4&&stages[3]["corpse"].B&&!stages[3]["grip"].B&&stages[3]["head_joint"].B);
            Check("death stage five releases head COM joint",stages[4]["stage"].I==5&&!stages[4]["head_joint"].B);
            Check("death finishes all six waits",stages[6]["stage"].I==7);
            Check("mid death state round trips",SameSaveValue(d["saved"],d["restored"]));
            yield return SceneManager.UnloadSceneAsync(scene);app.enabled=true;Time.timeScale=1;
        }
        if(resume)yield return Call("/api/martial",null,j=>app.Martial=j);
        else yield return Call("/api/intervene",J.O("action","challenge","npc_id",1),j=>app.Martial=j);
        app.ShowBattleForSmoke();yield return WaitArena();if(app.PhysicsArena==null)yield break;
        var real=app.PhysicsArena;real.player.testOverride=real.enemy.testOverride=true;
        string path=Path.Combine(client.SaveDir,"source-expected-frame.json");
        if(resume){
            var expected=J.Parse(File.ReadAllText(path));var actual=real.CaptureCampaign();
            Check("cold restart preserves source animation playback",SameSaveValue(expected["fighters"][1]["reference"]["source_motion"],actual["fighters"][1]["reference"]["source_motion"]));
            Check("cold restart preserves death flow body",SameSaveValue(expected["fighters"][1]["reference"]["death_state"],actual["fighters"][1]["reference"]["death_state"]));
            yield return app.SavePhysicsForSmoke("surrender");Check("source state formal battle settles",app.Martial["battle"]["settled"].B);
        }else{
            real.PrepareSourceCheckpoint();yield return app.SavePhysicsForSmoke("checkpoint");var saved=app.Martial["battle"]["frame"];
            Check("formal backend persists actual source motion",saved["fighters"][1]["reference"]["source_motion"]["clip"].S.Length>0&&saved["fighters"][1]["reference"]["death_state"]["version"].I==1,J.O("error",client.Error));
            File.WriteAllText(path,saved.ToString());
        }
    }
}
}
