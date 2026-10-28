using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BootPlayerTurnSmoke(){
        if(GameClient.Arg("-playerTurnSmoke").Length==0)return;
        if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("Player turn acceptance requires an isolated save");Application.Quit(2);return;}
        new GameObject("Player first turn acceptance").AddComponent<PlayerTurnSmoke>();
    }
    internal IEnumerator VerifyPlayerTurn(){
        string output=GameClient.Arg("-playerTurnSmoke");Directory.CreateDirectory(output);
        var checks=new List<J>();var errors=new List<string>();
        Application.LogCallback capture=(s,t,k)=>{if(k==LogType.Error||k==LogType.Exception)errors.Add(s+"\n"+t);};Application.logMessageReceived+=capture;
        Action<string,bool> check=(label,pass)=>{checks.Add(J.O("check",label,"passed",pass));File.WriteAllText(Path.Combine(output,"progress.json"),new J(checks).ToString());};
        float until=Time.realtimeSinceStartup+100;while((!Client.Ready||W["npcs"].A.Count<3)&&Time.realtimeSinceStartup<until)yield return null;
        check("isolated legacy fixture is ready",Client.Ready&&W["npcs"].A.Count>=3);
        if(Client.Ready&&W["npcs"].A.Count>=3){
            var fixture=J.Parse(File.ReadAllText(Path.Combine(Client.SaveDir,"fixture.json")));int id=fixture["people"][0].I,month=W["month"].I;
            yield return InteractionSubjectForSmoke(id);
            check("old exhausted NPC starts with four free weeks",Person["time_left"].I==4&&Person["monthly_actions"].A.Count==0);
            check("migration preserves hero actions age and wealth",Hero["points"].I==30&&Hero["age_months"].I==288&&W["player"]["wealth"].N==1000);
            check("praise is available before NPC acts",interactionData["actions"].A.First(a=>a["id"].S=="praise")["enabled"].B);
            yield return CharacterPicture(output,"01-player-first-menu");
            yield return InteractionActForSmoke(id,"praise");
            yield return Client.Request("/api/npc/"+id,null,j=>Person=j);
            check("player's first visit does not occupy NPC time",Person["time_left"].I==4);
            string art=interactionData["manual_offers"].A.First(a=>a["reason"].S.Length==0)["art"].S;
            yield return InteractionActForSmoke(id,"teach",art:art);
            yield return Client.Request("/api/npc/"+id,null,j=>Person=j);
            check("teaching only costs player AP before NPC actions",Person["time_left"].I==4&&Person["monthly_actions"].A.Count==0&&Hero["points"].I==27&&W["month"].I==month);
            interactionChoice="";yield return CharacterPicture(output,"02-player-booked-two-weeks");
            yield return Client.Action("/api/hero",Client.HeroBody("end_turn"));
            yield return InteractionSubjectForSmoke(id);
            yield return Client.Request("/api/npc/"+id,null,j=>Person=j);
            var completed=Person["previous_month_actions"].A;
            var booked=completed.Where(a=>a["kind"].S=="player_social"||a["id"].S=="manual_teaching:player").ToList();
            var npcActions=completed.Except(booked).ToList();
            check("NPC phase has no player time bookings",booked.Count==0);
            check("NPC keeps the full four weeks for its own actions",npcActions.All(a=>a["week"].I>=1)&&completed.Sum(a=>a["time_cost"].I)==4);
            check("next month opens with refreshed player and NPC budgets",W["month"].I==month+1&&Hero["points"].I==30&&Person["time_left"].I==4&&Person["monthly_actions"].A.Count==0);
            check("the old busy schedule no longer disables next-turn interaction",interactionData["actions"].A.All(a=>!a["reason"].S.Contains("既有邀约")&&!a["reason"].S.Contains("暂无空闲")));
            yield return CharacterPicture(output,"03-next-player-turn");
            yield return Client.Action("/api/hero",Client.HeroBody("end_turn"));
            yield return InteractionSubjectForSmoke(id);
            yield return Client.Request("/api/npc/"+id,null,j=>Person=j);
            check("the second month also returns control with free NPC time",W["month"].I==month+2&&Hero["points"].I==30&&Person["time_left"].I==4);
        }
        check("no native UI exceptions",errors.Count==0);Application.logMessageReceived-=capture;
        bool passed=checks.All(c=>c["passed"].B);
        File.WriteAllText(Path.Combine(output,"acceptance.json"),J.O("passed",passed,"checks",new J(checks),"errors",new J(errors.Select(s=>new J(s)).ToList())).ToString());
        if(GameClient.Arg("-smokeHold")!="true")Application.Quit(passed?0:2);
    }
}
public sealed class PlayerTurnSmoke:MonoBehaviour {
    IEnumerator Start(){yield return null;var app=FindAnyObjectByType<JianghuApp>();if(app!=null)yield return app.VerifyPlayerTurn();else Application.Quit(2);}
}
}
