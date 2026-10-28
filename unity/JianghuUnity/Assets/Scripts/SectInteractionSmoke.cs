using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BootSectInteractionSmoke(){
        if(GameClient.Arg("-sectInteractionSmoke")=="")return;
        if(GameClient.Arg("-saveDir")==""){Debug.LogError("Sect acceptance requires an isolated save");Application.Quit(2);return;}
        new GameObject("Sect interaction acceptance").AddComponent<SectInteractionSmoke>();
    }
    internal IEnumerator VerifySectInteractions(){
        string output=GameClient.Arg("-sectInteractionSmoke");Directory.CreateDirectory(output);
        var checks=new List<J>();var errors=new List<string>();
        Application.LogCallback capture=(s,t,k)=>{if(k==LogType.Error||k==LogType.Exception)errors.Add(s+"\n"+t);};
        Application.logMessageReceived+=capture;
        Action<string,bool> check=(name,ok)=>{checks.Add(J.O("check",name,"passed",ok));File.WriteAllText(Path.Combine(output,"progress.json"),new J(checks).ToString());};
        float deadline=Time.realtimeSinceStartup+100;
        while((!Client.Ready||!Hero["created"].B)&&Time.realtimeSinceStartup<deadline)yield return null;
        check("isolated fixture ready",Client.Ready&&Hero["created"].B);
        if(Client.Ready&&Hero["created"].B){
            var fixture=J.Parse(File.ReadAllText(Path.Combine(Client.SaveDir,"fixture.json")));
            int faction=fixture["faction"].I;int points=Hero["points"].I;int month=W["month"].I;
            foreach(var id in fixture["people"].A){
                yield return InteractionSubjectForSmoke(id.I);SetTab(5);
                int grade=Person["membership_view"]["grade"].I;
                var join=interactionData["actions"].A.FirstOrDefault(a=>a["id"].S=="join_sect");
                check("recruitment matches grade "+grade,grade<=3?join!=null&&join["enabled"].B&&join["category"].I==5:join==null);
                if(grade==4)yield return CharacterPicture(output,"01-fourth-grade-no-entry");
            }
            OpenInstitution(false,faction);
            while(Client.Busy||!Institution.Has)yield return null;
            check("old institution join and duty options are absent",!Institution["player_options"].A.Any(a=>a["action"].S=="sect_join"||a["action"].S=="sect_task"));
            foreach(string action in new[]{"sect_join","sect_task","seek_teacher"}){
                bool accepted=false;
                yield return Client.Request("/api/hero",Client.HeroBody(action,"faction",faction),j=>accepted=true,false);
                yield return Client.Refresh();
                check("removed endpoint rejects "+action,!accepted&&Hero["points"].I==points&&!Hero["membership"].Has);
            }
            int elder=fixture["people"][2].I;
            yield return InteractionSubjectForSmoke(elder);SetTab(5);ChooseInteraction("join_sect");
            yield return CharacterPicture(output,"02-third-grade-recruitment");
            yield return InteractionActForSmoke(elder,"join_sect");
            check("third grade elder admits player as ninth grade",Hero["membership"]["faction"].I==faction&&Hero["membership"]["grade"].I==9);
            check("joining spends four AP without advancing month",Hero["points"].I==points-4&&W["month"].I==month);
            check("repeat recruitment is disabled",!interactionData["actions"].A.First(a=>a["id"].S=="join_sect")["enabled"].B);
            ViewPerson(-1,PersonTab.Ties);yield return CharacterPicture(output,"03-membership");
            ViewPerson(elder,PersonTab.Martial);yield return WaitForCharacter(elder);
            yield return CharacterPicture(output,"04-npc-martial-without-teacher-entry");
            yield return InteractionSubjectForSmoke(elder);SetTab(2);ChooseInteraction("teach");
            check("interactive teaching remains available",InteractionAction.Has&&InteractionAction["id"].S=="teach");
        }
        check("no native exceptions",errors.Count==0);Application.logMessageReceived-=capture;
        bool passed=checks.All(c=>c["passed"].B);
        File.WriteAllText(Path.Combine(output,"acceptance.json"),J.O("passed",passed,"checks",new J(checks),"errors",new J(errors.Select(e=>new J(e)).ToList())).ToString());
        if(GameClient.Arg("-smokeHold")!="true")Application.Quit(passed?0:2);
    }
}
public sealed class SectInteractionSmoke:MonoBehaviour {
    IEnumerator Start(){yield return null;var app=FindAnyObjectByType<JianghuApp>();if(app!=null)yield return app.VerifySectInteractions();else Application.Quit(2);}
}
}
