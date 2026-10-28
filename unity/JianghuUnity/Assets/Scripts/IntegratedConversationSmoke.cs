using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BootIntegratedConversationSmoke(){
        if(GameClient.Arg("-integratedConversationSmoke").Length==0)return;
        if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("Integrated conversation acceptance requires an isolated save");Application.Quit(2);return;}
        new GameObject("Integrated conversation acceptance").AddComponent<IntegratedConversationSmoke>();
    }
    IEnumerator WaitIntegratedReply(int id){
        float until=Time.realtimeSinceStartup+40;
        while(Client.Busy&&Time.realtimeSinceStartup<until)yield return null;
        while(Time.realtimeSinceStartup<until){
            yield return PollSocialPerson(id);
            if(!InteractionSocial["pending"].B)break;
            yield return new WaitForSecondsRealtime(.15f);
        }
        yield return Client.Refresh();LoadInteraction();
        until=Time.realtimeSinceStartup+10;while(!InteractionDataReady&&Time.realtimeSinceStartup<until)yield return null;
        yield return new WaitForSecondsRealtime(.3f);
    }
    internal IEnumerator VerifyIntegratedConversation(){
        string output=GameClient.Arg("-integratedConversationSmoke");Directory.CreateDirectory(output);
        var checks=new List<J>();var errors=new List<string>();
        Application.LogCallback capture=(s,t,k)=>{if(k==LogType.Error||k==LogType.Exception)errors.Add(s+"\n"+t);};Application.logMessageReceived+=capture;
        Action<string,bool> check=(label,pass)=>{checks.Add(J.O("check",label,"passed",pass));File.WriteAllText(Path.Combine(output,"progress.json"),new J(checks).ToString());};
        float until=Time.realtimeSinceStartup+100;while((!Client.Ready||W["npcs"].A.Count<2)&&Time.realtimeSinceStartup<until)yield return null;
        check("isolated world ready",Client.Ready&&W["npcs"].A.Count>=2);
        if(Client.Ready&&W["npcs"].A.Count>=2){
            int a=1,b=2,month=W["month"].I;double points=Hero["points"].N,money=W["player"]["wealth"].N;
            TalkTo(a);yield return WaitForCharacter(a);
            until=Time.realtimeSinceStartup+15;while(!InteractionDataReady&&Time.realtimeSinceStartup<until)yield return null;
            check("legacy talk entry opens the same new six-category screen",TopIs(ViewKind.Interaction)&&InteractionConversationOpen&&views.Count==1&&InteractionCategories.Length==6);
            check("all six old conversation topics and twelve direct actions remain",interactionData["conversations"].A.Count==6&&interactionData["actions"].A.Count==12);
            check("conversation preserves actual favors and introductions",InteractionSocial["favors"].A.Count>0&&InteractionSocial["introductions"].A.Count>0);
            interactionChoice="";SetTab(0);yield return CharacterPicture(output,"01-talk-categories");
            SetTab(5);yield return CharacterPicture(output,"02-services");ChooseInteraction("social:ask_aid");
            check("refusal opens five personality choices within the new screen",conversationPane==1&&InteractionAction["action"].S=="ask_aid");
            var root=InteractionSocial["requests"].A.Last();int requestId=root["id"].I;
            check("five original lines and labels are available",string.Join("/",root["persuasion_options"].A.Select(o=>o["label"].S))=="刚正/仁善/中庸/叛逆/唯我"&&root["can_persuade"].B);
            yield return CharacterPicture(output,"03-five-personalities");
            Screen.SetResolution(1280,800,FullScreenMode.Windowed);yield return CharacterPicture(output,"04-five-small");Screen.SetResolution(1680,960,FullScreenMode.Windowed);
            ViewPerson(b,PersonTab.Bag);yield return WaitForCharacter(b);Back();yield return WaitForCharacter(a);
            check("profile return keeps original person topic refusal and pane",TopIs(ViewKind.Interaction)&&FocusPersonId==a&&interactionChoice=="social:ask_aid"&&persuasionRequestId==requestId&&conversationPane==1&&TopView.Tab==5);
            J social=InteractionSocial;ApplySocialSnapshot(b,J.O("revision",999,"requests",new J()));
            check("a late response for another person cannot replace conversation",InteractionSocial==social&&FocusPersonId==a);
            string schedule=Person["monthly_actions"].ToString();double npcMoney=Person["wealth"].N;
            Persuade(requestId,"kind");yield return WaitIntegratedReply(a);
            var answer=InteractionSocial["requests"].A.Last();
            check("selected original personality line is submitted and answered",answer["status"].S=="completed"&&answer["persuasion_label"].S=="仁善"&&answer["choice_id"].S=="give_aid");
            check("persuasion outcome applies real silver exactly once",W["player"]["wealth"].N==money+4&&Person["wealth"].N==npcMoney-4&&Hero["points"].N==points-1);
            check("persuasion does not change month or NPC time",W["month"].I==month&&Person["time_left"].I==0&&Person["monthly_actions"].ToString()==schedule);
            check("answer appears in the same conversation history",conversationPane==0&&answer["reply"].S.Length>0&&InteractionSocial["requests"].A.Any(r=>r["id"].I==requestId&&!r["can_persuade"].B));
            yield return CharacterPicture(output,"05-persuaded-reply");
            conversationPane=2;conversationScroll=Vector2.zero;yield return CharacterPicture(output,"06-favors-introductions");
            double beforeRequest=Hero["points"].N;
            SocialAction("ask_aid");yield return WaitIntegratedReply(a);
            var newRefusal=InteractionSocial["requests"].A.Last();
            check("a newly submitted request receives its live refusal and five generated lines",newRefusal["choice_id"].S=="refuse"&&newRefusal["can_persuade"].B&&newRefusal["persuasion_options"].A.Count==5&&newRefusal["id"].I>requestId);
            check("new refusal automatically opens its own persuasion pane for one AP",conversationPane==1&&persuasionRequestId==newRefusal["id"].I&&Hero["points"].N==beforeRequest-1);
            yield return CharacterPicture(output,"07-live-refusal");
            TalkTo(b);yield return WaitForCharacter(b);
            until=Time.realtimeSinceStartup+15;while(!InteractionDataReady&&Time.realtimeSinceStartup<until)yield return null;
            ChooseInteraction("social:ask_aid");
            var retry=InteractionSocial["requests"].A.Last();int retryId=retry["id"].I;double beforeRetry=Hero["points"].N;
            check("failed options remain recoverable in the unified screen",retry["can_retry_options"].B&&conversationPane==1);
            Persuade(retryId);yield return WaitIntegratedReply(b);
            retry=InteractionSocial["requests"].A.First(r=>r["id"].I==retryId);
            check("retry regenerates five choices without charging AP",retry["can_persuade"].B&&retry["persuasion_options"].A.Count==5&&Hero["points"].N==beforeRetry);
            SocialAction("talk");yield return WaitIntegratedReply(b);
            check("old preset request is sent through the new conversation pane",InteractionSocial["requests"].A.Last()["action"].S=="talk"&&InteractionSocial["requests"].A.Last()["status"].S=="completed"&&Hero["points"].N==beforeRetry-1);
            ChooseInteraction("social:swear");check("old sworn invitation remains in the conversation topic selector",InteractionAction["action"].S=="swear");
            GoStage(Stage.World,false);TalkTo(a);yield return WaitForCharacter(a);yield return new WaitForSecondsRealtime(.5f);
            check("returning to a person restores its actual saved conversation",InteractionSocial["requests"].A.Any(r=>r["persuasion_label"].S=="仁善")&&!InteractionSocial["requests"].A.Any(r=>r["npc_id"].I==b));
            yield return Client.Request("/api/save",J.O(),j=>check("integrated conversation saves through the normal game",j["saved"].S.Length>0));
        }
        check("no native exceptions",errors.Count==0);Application.logMessageReceived-=capture;
        bool passed=checks.All(c=>c["passed"].B);File.WriteAllText(Path.Combine(output,"acceptance.json"),J.O("passed",passed,"checks",new J(checks),"errors",new J(errors.Select(s=>new J(s)).ToList())).ToString());
        if(GameClient.Arg("-smokeHold")!="true")Application.Quit(passed?0:2);
    }
}
public sealed class IntegratedConversationSmoke:MonoBehaviour {
    IEnumerator Start(){yield return null;var app=FindAnyObjectByType<JianghuApp>();if(app!=null)yield return app.VerifyIntegratedConversation();else Application.Quit(2);}
}
}
