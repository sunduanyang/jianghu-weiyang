using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    internal int ClaudeIntegrationScreenshotCount {get;private set;}
    IEnumerator ClaudeIntegrationPicture(string output,string name){
        if(GameClient.Arg("-claudeIntegrationCapture")!="1")yield break;
        string path=Path.Combine(output,name+".png");
        bool existed=File.Exists(path);DateTime previous=existed?File.GetLastWriteTimeUtc(path):DateTime.MinValue;
        yield return CharacterPicture(output,name);
        if(File.Exists(path)&&(!existed||File.GetLastWriteTimeUtc(path)!=previous))ClaudeIntegrationScreenshotCount++;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BootClaudeIntegrationSmoke(){
        if(GameClient.Arg("-claudeIntegrationSmoke").Length==0)return;
        if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("Claude integration acceptance requires an isolated save");Application.Quit(2);return;}
        new GameObject("Claude integration acceptance").AddComponent<ClaudeIntegrationSmoke>();
    }
    internal IEnumerator VerifyClaudeIntegration(Action<string,bool> check,string output){
        ClaudeIntegrationScreenshotCount=0;
        float deadline=Time.realtimeSinceStartup+100;
        while((!Client.Ready||!Hero["created"].B)&&Time.realtimeSinceStartup<deadline)yield return null;
        check("isolated fixture ready",Client.Ready&&Hero["created"].B);
        if(!Client.Ready||!Hero["created"].B)yield break;
        var fixture=J.Parse(File.ReadAllText(Path.Combine(Client.SaveDir,"fixture.json")));
        int faction=fixture["faction"].I,elder=fixture["elder"].I,outlaw=fixture["outlaw"].I,month=W["month"].I;
        double calls=W["ai"]["calls"].N;
        yield return Client.Request("/api/ai/config",J.O("enabled",false),j=>{});
        yield return InteractionSubjectForSmoke(elder);
        var temperament=Person["temperament_view"];
        check("live personality has stance and five new axes",temperament["axes"].A.Count==5&&temperament["stance_label"].S.Length>0&&temperament["axes"].A.Select(a=>a["key"].S).Distinct().Count()==5);
        ViewPerson(elder,PersonTab.Overview);yield return WaitForCharacter(elder);
        yield return ClaudeIntegrationPicture(output,"01-temperament-overview");
        SetTab((int)PersonTab.Character);personScroll=Vector2.zero;
        yield return ClaudeIntegrationPicture(output,"02-temperament-detail");
        check("personality detail keeps the selected person",CurrentPersonTab==PersonTab.Character&&FocusPersonId==elder&&Person["goal_view"].Has);
        OpenInstitution(false,faction);deadline=Time.realtimeSinceStartup+15;
        while((Client.Busy||!Institution.Has)&&Time.realtimeSinceStartup<deadline)yield return null;
        var life=Institution["sect_life"];
        check("native sect page receives duties precepts and relations",life["member"].B&&life["player"]["board"].A.Any(c=>c["kind"].S=="hunt")&&life["precepts"].A.Count>0&&life["relations"].A.Count==13);
        mainScroll=Vector2.zero;yield return ClaudeIntegrationPicture(output,"03-sect-life");
        yield return InteractionSubjectForSmoke(elder);
        var trial=interactionData["actions"].A.FirstOrDefault(a=>a["id"].S=="sect_trial");
        check("promotion trial is enabled in the live combat category",trial!=null&&trial["enabled"].B&&trial["category"].I==1);
        if(trial==null||!trial["enabled"].B)yield break;
        ChooseInteraction("sect_trial");SetTab(1);yield return ClaudeIntegrationPicture(output,"04-trial-confirmation");
        yield return InteractionActForSmoke(elder,"sect_trial");
        yield return ClaudeIntegrationBattle("sect_trial",elder,check,output);
        check("winning trial promotes exactly one grade",Hero["membership"]["grade"].I==fixture["grade"].I-1);
        bool accepted=false;
        yield return Client.Request("/api/hero",Client.HeroBody("sect_accept","faction",faction,"choice",fixture["hunt"]),j=>accepted=true);
        yield return Client.Refresh();
        check("hunt is accepted from the current real commission board",accepted);
        yield return InteractionSubjectForSmoke(outlaw);
        var errand=interactionData["actions"].A.FirstOrDefault(a=>a["id"].S=="sect_errand");
        check("hunt target exposes enabled sect errand interaction",errand!=null&&errand["enabled"].B);
        if(errand==null||!errand["enabled"].B)yield break;
        double wealth=W["player"]["wealth"].N;
        yield return Client.Request("/api/faction/"+faction,null,j=>Institution=j);
        double fund=Institution["fund"].N;
        ChooseInteraction("sect_errand");SetTab(5);yield return ClaudeIntegrationPicture(output,"06-hunt-confirmation");
        yield return InteractionActForSmoke(outlaw,"sect_errand");
        yield return ClaudeIntegrationBattle("sect_hunt",outlaw,check,output);
        yield return Client.Request("/api/faction/"+faction,null,j=>Institution=j);
        check("successful hunt removes the active commission",!Institution["sect_life"]["player"]["active"].A.Any(c=>c["id"].S==fixture["hunt"].S)&&Institution["sect_life"]["player"]["completed"].I==1);
        check("hunt pays five real silver from sect treasury",Math.Abs(W["player"]["wealth"].N-wealth-5)<.001&&Math.Abs(fund-Institution["fund"].N-5)<.001);
        OpenInstitution(false,faction);deadline=Time.realtimeSinceStartup+15;
        while((Client.Busy||!Institution.Has)&&Time.realtimeSinceStartup<deadline)yield return null;
        mainScroll=Vector2.zero;yield return ClaudeIntegrationPicture(output,"08-sect-result");
        check("acceptance preserves month and never invokes a model",W["month"].I==month&&W["ai"]["calls"].N==calls);
        yield return Client.Request("/api/save",J.O(),j=>{});
    }
    IEnumerator ClaudeIntegrationBattle(string encounter,int target,Action<string,bool> check,string output){
        float deadline=Time.realtimeSinceStartup+15;
        while(PhysicsArena==null&&Time.realtimeSinceStartup<deadline)yield return null;
        yield return null;
        check(encounter+" enters the formal native physical arena",PhysicsArena!=null&&BattleVisible&&Martial["battle"]["encounter"].S==encounter&&Martial["battle"]["ruleset"].I==4&&GameClient.Arg("-referenceCombat").Length==0);
        if(PhysicsArena==null)yield break;
        var arena=PhysicsArena;string session=Martial["battle"]["id"].S;
        check(encounter+" uses both source rigs and correct spar policy",arena.Campaign&&arena.player.SourceRigActive&&arena.enemy.SourceRigActive&&Martial["battle"]["spar"].B==(encounter=="sect_trial"));
        arena.player.testOverride=arena.enemy.testOverride=true;
        arena.RunCampaign(true);yield return new WaitForSecondsRealtime(.12f);arena.PauseCampaign();
        yield return SavePhysicsForSmoke("checkpoint");
        check(encounter+" saves a physical checkpoint",Martial["battle"]["frame"].Has&&Martial["battle"]["revision"].I>1);
        yield return SavePhysicsForSmoke("leave");
        check(encounter+" returns to its original person while unresolved",TopIs(ViewKind.Interaction)&&FocusPersonId==target&&arena.CampaignHidden&&!Martial["battle"]["settled"].B);
        OpenBattle();deadline=Time.realtimeSinceStartup+15;
        while((PhysicsArena==null||PhysicsArena.CampaignHidden)&&Time.realtimeSinceStartup<deadline)yield return null;
        check(encounter+" resumes the same paused physical battle",PhysicsArena==arena&&!arena.CampaignHidden&&arena.paused&&Martial["battle"]["id"].S==session);
        yield return ClaudeIntegrationPicture(output,encounter=="sect_trial"?"05-trial-arena":"07-hunt-arena");
        // Inject native damage only in this explicit acceptance fixture. This
        // checks settlement wiring without claiming a combat balance result.
        arena.enemy.ApplyReferenceDamage("torso",arena.enemy.maxHealth*4,arena.enemy.torso.B,Vector2.zero,false);
        arena.player.landedHits++;arena.hits++;
        arena.RunCampaign(true);yield return null;yield return null;arena.PauseCampaign();
        check(encounter+" detects native victory after acceptance damage",arena.CampaignFinished&&arena.enemy.health<=0);
        yield return SavePhysicsForSmoke("complete");yield return Client.Refresh();yield return WaitForCharacter(target);
        check(encounter+" settles and returns to the same person",Martial["battle"]["settled"].B&&Martial["battle"]["event_id"].Has&&PhysicsArena==null&&!W["battle_active"].B&&TopIs(ViewKind.Interaction)&&FocusPersonId==target);
    }
}
public sealed class ClaudeIntegrationSmoke:MonoBehaviour {
    readonly List<J> checks=new List<J>();readonly List<string> errors=new List<string>();string output;
    void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(message+"\n"+stack);}
    void Check(string name,bool passed){checks.Add(J.O("check",name,"passed",passed));File.WriteAllText(Path.Combine(output,"progress.json"),new J(checks).ToString());}
    IEnumerator Start(){
        output=GameClient.Arg("-claudeIntegrationSmoke");Directory.CreateDirectory(output);Application.logMessageReceived+=Log;
        yield return null;var app=FindAnyObjectByType<JianghuApp>();
        if(app==null)Check("native application exists",false);
        else{
            var run=app.VerifyClaudeIntegration(Check,output);
            while(true){bool more=false;object current=null;try{more=run.MoveNext();if(more)current=run.Current;}catch(Exception e){errors.Add(e.ToString());break;}if(!more)break;yield return current;}
        }
        Check("no native errors or exceptions",errors.Count==0);Application.logMessageReceived-=Log;
        bool passed=checks.All(c=>c["passed"].B)&&errors.Count==0;
        File.WriteAllText(Path.Combine(output,"acceptance.json"),J.O("passed",passed,"checks",new J(checks),"errors",new J(errors.Select(e=>new J(e)).ToList()),"screenshot_requested",GameClient.Arg("-claudeIntegrationCapture")=="1","screenshot_count",app!=null?app.ClaudeIntegrationScreenshotCount:0,"visual_verified",false,"limitation","Functional acceptance only; screenshots are opt-in and require separate visual review. Victories use explicit acceptance-only native damage injection; no balance or model-quality claim.").ToString());
        Application.Quit(passed?0:2);
    }
}
}
