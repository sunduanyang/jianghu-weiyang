using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BootRepeatInteractionSmoke(){
        if(GameClient.Arg("-repeatInteractionSmoke").Length==0)return;
        if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("Repeated interaction acceptance requires an isolated save");Application.Quit(2);return;}
        new GameObject("Repeated interaction acceptance").AddComponent<RepeatInteractionSmoke>();
    }
    internal IEnumerator VerifyRepeatInteractions(){
        string output=GameClient.Arg("-repeatInteractionSmoke");Directory.CreateDirectory(output);
        var checks=new List<J>();var errors=new List<string>();
        Application.LogCallback capture=(s,t,k)=>{if(k==LogType.Error||k==LogType.Exception)errors.Add(s+"\n"+t);};Application.logMessageReceived+=capture;
        Action<string,bool> check=(label,pass)=>{checks.Add(J.O("check",label,"passed",pass));File.WriteAllText(Path.Combine(output,"progress.json"),new J(checks).ToString());};
        float until=Time.realtimeSinceStartup+100;while((!Client.Ready||W["npcs"].A.Count<3)&&Time.realtimeSinceStartup<until)yield return null;
        check("isolated world is ready",Client.Ready&&W["npcs"].A.Count>=3);
        if(Client.Ready&&W["npcs"].A.Count>=3){
            var fixture=J.Parse(File.ReadAllText(Path.Combine(Client.SaveDir,"fixture.json")));int id=fixture["people"][0].I,other=fixture["people"][2].I,month=W["month"].I;
            J released=new J();yield return Client.Request("/api/npc/"+other,null,j=>released=j);
            check("legacy player appointment is released without charging player AP",released["time_left"].I==4&&released["monthly_actions"].A.Count==0&&Hero["points"].I==30);
            yield return InteractionSubjectForSmoke(id);
            string schedule=Person["monthly_actions"].ToString();
            check("NPC remains busy with its independent schedule",Person["time_left"].I==0);
            check("legacy cooldown and busy NPC do not block praise",interactionData["actions"].A.First(a=>a["id"].S=="praise")["enabled"].B);
            yield return CharacterPicture(output,"01-busy-npc-available");
            for(int i=0;i<5;i++)yield return InteractionActForSmoke(id,"praise");
            check("five praises use five player AP in the same month",Hero["points"].I==25&&W["month"].I==month);
            for(int i=0;i<5;i++)yield return InteractionActForSmoke(id,"insult");
            check("five insults are allowed and charge each time",Hero["points"].I==20&&W["player"]["reputation"].N==-5);
            string art=interactionData["manual_offers"].A.First(a=>a["reason"].S.Length==0)["art"].S;
            int before=interactionData["gifts"].A.Count(i=>i["art"].S==art);
            for(int i=0;i<5;i++)yield return InteractionActForSmoke(id,"teach",art:art);
            check("five same-art requests award five real manuals for ten AP",Hero["points"].I==10&&interactionData["gifts"].A.Count(i=>i["art"].S==art)==before+5);
            ChooseInteraction("teach");interactionArt=art;yield return CharacterPicture(output,"02-repeat-teaching");
            for(int bout=0;bout<2;bout++){
                yield return InteractionActForSmoke(id,"spar");
                until=Time.realtimeSinceStartup+12;while(PhysicsArena==null&&Time.realtimeSinceStartup<until)yield return null;
                check("paid spar opens the actual arena again: "+bout,PhysicsArena!=null&&BattleVisible);
                if(PhysicsArena!=null)yield return SavePhysicsForSmoke("surrender");
                yield return InteractionSubjectForSmoke(id);
            }
            check("two same-month spars cost four AP",Hero["points"].I==6);
            for(int i=0;i<6;i++)yield return InteractionActForSmoke(id,"praise");
            check("actions can continue until player AP reaches zero",Hero["points"].I==0&&W["month"].I==month);
            yield return Client.Request("/api/npc/"+id,null,j=>Person=j);
            check("all interactions leave NPC time and schedule unchanged",Person["time_left"].I==0&&Person["monthly_actions"].ToString()==schedule);
            check("menu blocks on player AP rather than monthly count",interactionData["actions"].A.All(a=>!a["enabled"].B&&a["reason"].S.Contains("行动力")));
            bool accepted=false;yield return Client.Request("/api/interaction",J.O("npc_id",id,"action","praise","month",month,"revision",Hero["revision"]),j=>accepted=true,false);
            yield return Client.Refresh();check("insufficient AP request is rejected without negative AP",!accepted&&Hero["points"].I==0);
            interactionChoice="";yield return CharacterPicture(output,"03-only-player-ap-exhausted");
        }
        check("no native exceptions",errors.Count==0);Application.logMessageReceived-=capture;
        bool passed=checks.All(c=>c["passed"].B);File.WriteAllText(Path.Combine(output,"acceptance.json"),J.O("passed",passed,"checks",new J(checks),"errors",new J(errors.Select(s=>new J(s)).ToList())).ToString());
        if(GameClient.Arg("-smokeHold")!="true")Application.Quit(passed?0:2);
    }
}
public sealed class RepeatInteractionSmoke:MonoBehaviour {
    IEnumerator Start(){yield return null;var app=FindAnyObjectByType<JianghuApp>();if(app!=null)yield return app.VerifyRepeatInteractions();else Application.Quit(2);}
}
}
