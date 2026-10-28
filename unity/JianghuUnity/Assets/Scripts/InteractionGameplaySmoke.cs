using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BootInteractionGameplaySmoke(){
        if(GameClient.Arg("-interactionGameplaySmoke").Length==0)return;
        if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("Interaction acceptance requires an isolated save");Application.Quit(2);return;}
        new GameObject("Interaction gameplay acceptance").AddComponent<InteractionGameplaySmoke>();
    }
    internal IEnumerator VerifyInteractionGameplay(){
        string output=GameClient.Arg("-interactionGameplaySmoke");Directory.CreateDirectory(output);
        var checks=new List<J>();var errors=new List<string>();
        Application.LogCallback capture=(s,t,k)=>{if(k==LogType.Error||k==LogType.Exception)errors.Add(s+"\n"+t);};Application.logMessageReceived+=capture;
        Action<string,bool> check=(name,pass)=>{checks.Add(J.O("check",name,"passed",pass));File.WriteAllText(Path.Combine(output,"progress.json"),new J(checks).ToString());};
        float until=Time.realtimeSinceStartup+100;while((!Client.Ready||W["npcs"].A.Count<3)&&Time.realtimeSinceStartup<until)yield return null;
        check("isolated game ready",Client.Ready);
        if(Client.Ready){
            var fixture=J.Parse(File.ReadAllText(Path.Combine(Client.SaveDir,"fixture.json")));
            int a=fixture["people"][0].I,b=fixture["people"][1].I,c=fixture["people"][2].I;
            int month=W["month"].I;double money=W["player"]["wealth"].N;
            yield return InteractionSubjectForSmoke(a);
            check("all twelve menu actions are loaded from the live engine",interactionData["actions"].A.Count==12);
            ChooseInteraction("praise");yield return CharacterPicture(output,"01-praise-confirm");
            yield return InteractionActForSmoke(a,"praise");
            check("praise updates real affinity",interactionData["relationship"]["affinity"].N==84);
            yield return CharacterPicture(output,"02-praise-result");
            check("praise remains available for another paid interaction",interactionData["actions"].A.First(x=>x["id"].S=="praise")["enabled"].B);
            yield return InteractionActForSmoke(a,"insult");
            check("insult changes reputation and trust",W["player"]["reputation"].N==-1&&interactionData["relationship"]["trust"].N==62);
            string gift=interactionData["gifts"][0]["id"].S;
            ChooseInteraction("gift");interactionItem=gift;yield return CharacterPicture(output,"03-gift-selection");
            yield return InteractionActForSmoke(a,"gift",gift);
            check("gift transfers the selected real item",interactionData["buy"].A.Any(i=>i["id"].S==gift)&&!interactionData["gifts"].A.Any(i=>i["id"].S==gift));
            string art=interactionData["manual_offers"].A.First(o=>o["reason"].S.Length==0)["art"].S;
            ChooseInteraction("teach");interactionArt=art;yield return CharacterPicture(output,"04-teaching-selection");
            yield return InteractionActForSmoke(a,"teach",art:art);
            check("teacher awards a real manual",interactionResult.Contains("秘籍")&&interactionData["gifts"].A.Any(i=>i["art"].S==art));
            yield return InteractionActForSmoke(a,"confide");check("confidant is registered",interactionData["relationship"]["confidant"].B);
            yield return InteractionActForSmoke(b,"marry");check("marriage belongs to the intended person",Hero["spouse"].I==b&&interactionData["relationship"]["married"].B);
            yield return InteractionActForSmoke(c,"swear");check("sworn bond belongs to the intended person",interactionData["relationship"]["sworn"].B&&Hero["spouse"].I==b);
            yield return InteractionSubjectForSmoke(a);ChooseInteraction("trade");
            string purchase=interactionData["buy"].A.First(i=>i["reason"].S.Length==0)["id"].S;
            double silver=interactionData["player_wealth"].N+interactionData["npc_wealth"].N;
            interactionItem=purchase;yield return CharacterPicture(output,"05-personal-trade");
            interactionDirection="buy";yield return InteractionActForSmoke(a,"trade",purchase);
            check("NPC purchase transfers ownership",interactionData["sell"].A.Any(i=>i["id"].S==purchase));
            interactionDirection="sell";yield return InteractionActForSmoke(a,"trade",purchase);
            check("sale transfers back and conserves silver",interactionData["buy"].A.Any(i=>i["id"].S==purchase)&&Math.Abs(interactionData["player_wealth"].N+interactionData["npc_wealth"].N-silver)<.001);
            yield return InteractionActForSmoke(a,"steal");
            check("theft produces a real settled outcome",interactionResult.Contains("偷取")||interactionResult.Contains("行窃"));
            foreach(string action in new[]{"spar","rob","assault"}){
                yield return InteractionActForSmoke(a,action);
                until=Time.realtimeSinceStartup+12;while(PhysicsArena==null&&Time.realtimeSinceStartup<until)yield return null;
                check(action+" opens the actual physical arena",BattleVisible&&PhysicsArena!=null&&Martial["battle"]["encounter"].S==action&&Martial["battle"]["spar"].B==(action=="spar"));
                if(PhysicsArena!=null){
                    var arena=PhysicsArena;
                    // The arena component may be assigned before Unity has run Start.
                    yield return null;
                    check(action+" runs the normal game campaign without demo entry",arena.Campaign&&enabled&&GameClient.Arg("-referenceCombat").Length==0);
                    check(action+" uses improved mechanics for both fighters",arena.ReferenceCombat&&arena.player.Reference&&arena.enemy.Reference&&Martial["battle"]["fighters"].A.All(f=>f["physics_profile"].S==PhysicsDuel.ReferenceProfile));
                    int expectedSteps=arena.player.weapon.id!="unarmed"||arena.enemy.weapon.id!="unarmed"||arena.player.HasShield||arena.enemy.HasShield?16:8;
                    check(action+" uses the calibrated solver in the game",arena.CombatSolverSubsteps==expectedSteps&&Mathf.Abs(Time.fixedDeltaTime-1f/60)<.000001f);
                    check(action+" loads physical hand and foot driver inertia",new[]{arena.player,arena.enemy}.All(f=>f.ReferenceDriverInertia("handCOMNear")>0&&f.ReferenceDriverInertia("footCOMNear")>0));
                    arena.player.testOverride=arena.enemy.testOverride=true;
                    RunBattleForSmoke(true);bool kicked=arena.player.Dash();
                    // Frame hitches can skip an entire .2-second windup when
                    // waiting on wall time. Advance the actual campaign's
                    // native physics clock deterministically for this check.
                    arena.StepCampaignForAcceptance(.12f);
                    var windup=arena.CaptureCampaign()["fighters"][0]["reference"];
                    check(action+" has a real unpaid kick windup",kicked&&arena.player.ReferenceKicking&&!windup["kick_state"]["paid"].B);
                    arena.StepCampaignForAcceptance(.16f);RunBattleForSmoke(false);
                    arena.enemy.ApplyReferenceDamage("forearmFar",31,arena.enemy.pelvis.B,Vector2.zero,false);
                    arena.player.landedHits++;arena.hits++;
                    yield return SavePhysicsForSmoke("checkpoint");
                    var saved=Martial["battle"]["frame"];string session=Martial["battle"]["id"].S;
                    check(action+" saves paid kick and physical drivers through the game backend",saved["fighters"][0]["reference"]["kick_state"]["version"].I==1&&saved["fighters"][0]["reference"]["kick_state"]["paid"].B&&saved["fighters"].A.All(f=>f["reference"]["bodies"].A.Count==10)&&Martial["battle"]["revision"].I>1);
                    check(action+" persists recovered core and bashed limb through formal mode",saved["fighters"].A.All(f=>f["reference"]["core_state"]["version"].I==1)&&saved["fighters"][1]["reference"]["core_state"]["part_modes"]["forearmFar"].I==1&&!saved["fighters"][1]["reference"]["parts"]["forearmFar"]["severed"].B);
                    check(action+" persists source motion and death state in formal mode",saved["fighters"].A.All(f=>f["reference"]["source_motion"]["version"].I==1&&f["reference"]["death_state"]["version"].I==1));
                    File.WriteAllText(Path.Combine(output,"combat-"+action+"-checkpoint.json"),saved.ToString());
                    yield return SavePhysicsForSmoke("leave");
                    check(action+" can leave an unresolved game battle",TopIs(ViewKind.Interaction)&&FocusPersonId==a&&arena.CampaignHidden&&!Martial["battle"]["settled"].B);
                    OpenBattle();until=Time.realtimeSinceStartup+12;
                    while((PhysicsArena==null||PhysicsArena.CampaignHidden)&&Time.realtimeSinceStartup<until)yield return null;
                    check(action+" returns to the same improved battle with its kick state",PhysicsArena==arena&&arena.paused&&Martial["battle"]["id"].S==session&&arena.CaptureCampaign()["fighters"][0]["reference"]["kick_state"]["paid"].B);
                    yield return CharacterPicture(output,"06-arena-"+action);
                    yield return SavePhysicsForSmoke("surrender");yield return WaitForCharacter(a);
                    check(action+" settles once and returns to the encounter",TopIs(ViewKind.Interaction)&&FocusPersonId==a&&Martial["battle"]["settled"].B);
                }
            }
            check("hostile battles persist world costs and reputation",W["player"]["health"].N<100&&W["player"]["reputation"].N<=-19);
            check("all actions preserve the current month",W["month"].I==month);
            check("all actions cost exactly the advertised action points",Hero["points"].I==7);
            J rejected=new J();bool accepted=false;int points=Hero["points"].I;double wealth=W["player"]["wealth"].N;
            yield return Client.Request("/api/interaction",J.O("npc_id",a,"action","praise","revision",-1,"month",month),j=>{accepted=true;rejected=j;},false);
            yield return Client.Refresh();check("stale submissions cannot charge or duplicate effects",!accepted&&Hero["points"].I==points&&W["player"]["wealth"].N==wealth);
            yield return InteractionSubjectForSmoke(b);yield return CharacterPicture(output,"07-marriage-profile");
        }
        check("no native exceptions",errors.Count==0);Application.logMessageReceived-=capture;
        bool passed=checks.All(c=>c["passed"].B);File.WriteAllText(Path.Combine(output,"acceptance.json"),J.O("passed",passed,"checks",new J(checks),"errors",new J(errors.Select(s=>new J(s)).ToList())).ToString());
        if(GameClient.Arg("-smokeHold")!="true")Application.Quit(passed?0:2);
    }
    IEnumerator InteractionSubjectForSmoke(int id){
        OpenInteraction(id);yield return WaitForCharacter(id);float until=Time.realtimeSinceStartup+15;
        while(!InteractionDataReady&&Time.realtimeSinceStartup<until)yield return null;
    }
    IEnumerator InteractionActForSmoke(int id,string action,string item=null,string art=null){
        if(!TopIs(ViewKind.Interaction)||FocusPersonId!=id)yield return InteractionSubjectForSmoke(id);
        ChooseInteraction(action);SetTab(interactionData["actions"].A.First(a=>a["id"].S==action)["category"].I);
        float until=Time.realtimeSinceStartup+15;while(!InteractionDataReady&&Time.realtimeSinceStartup<until)yield return null;
        BeginInteraction(item,art);while(Client.Busy)yield return null;yield return WaitForCharacter(id);yield return null;
    }
}
public sealed class InteractionGameplaySmoke:MonoBehaviour {
    IEnumerator Start(){yield return null;var app=FindAnyObjectByType<JianghuApp>();if(app!=null)yield return app.VerifyInteractionGameplay();else Application.Quit(2);}
}
}
