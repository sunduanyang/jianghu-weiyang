using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public class PersuasionSmoke : MonoBehaviour {
    JianghuApp app;
    string output;
    readonly List<J> checks=new List<J>();
    readonly List<string> errors=new List<string>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot(){
        if(GameClient.Arg("-persuasionSmoke")!="true")return;
        if(GameClient.Arg("-saveDir")==""){Debug.LogError("Persuasion acceptance requires an isolated save");Application.Quit(1);return;}
        new GameObject("Persuasion acceptance").AddComponent<PersuasionSmoke>();
    }
    void Awake(){Application.logMessageReceived+=(c,s,t)=>{if(t==LogType.Error||t==LogType.Exception)errors.Add(c+"\n"+s);};}
    void Check(string name,bool pass){checks.Add(J.O("name",name,"pass",pass));Debug.Log("PERSUASION_CHECK "+name+" "+pass);}
    IEnumerator Call(string path,J body,Action<J> done){yield return app.Client.Request(path,body,done);}
    IEnumerator Picture(string name){yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));yield return new WaitForSecondsRealtime(.3f);}
    IEnumerator Start(){
        output=GameClient.Arg("-smokeOut");Directory.CreateDirectory(output);
        float deadline=Time.realtimeSinceStartup+100;
        while(Time.realtimeSinceStartup<deadline){app=FindAnyObjectByType<JianghuApp>();if(app!=null&&app.Client!=null&&app.Client.Ready&&app.W["npcs"].A.Count>0)break;yield return null;}
        if(app==null||!app.Client.Ready||app.W["npcs"].A.Count==0){File.WriteAllText(Path.Combine(output,"failed.txt"),"Client not ready");Application.Quit(1);yield break;}
        yield return app.Client.Refresh();app.SelectedPerson=4;
        yield return Call("/api/npc/4",null,j=>app.Person=j);
        var original=app.Person["player_social"]["requests"].A.Last();
        Check("real refusal fixture loaded",original["choice_id"].S=="refuse"&&original["can_persuade"].B);
        Check("exact five personality options",original["persuasion_options"].A.Select(o=>o["label"].S).SequenceEqual(new[]{"刚正","仁善","中庸","叛逆","唯我"}));
        Check("assessment stays private",!original["decision_context"].Has&&!original["decision"].Has);
        app.OpenSocialForSmoke();yield return Picture("01-five-personalities");
        int month=app.W["month"].I;double points=app.Hero["points"].N,money=app.W["player"]["wealth"].N,npcMoney=app.Person["wealth"].N;
        int npcTime=app.Person["time_left"].I;J submitted=new J();
        float begin=Time.realtimeSinceStartup;
        yield return Call("/api/social",J.O("npc_id",4,"mode","persuade","request_id",original["id"],"option_id","kind","revision",app.Person["player_social"]["revision"]),j=>{submitted=j;app.Person["player_social"]=j;});
        Check("selection queues asynchronously",Time.realtimeSinceStartup-begin<5&&submitted["pending"].B);
        int request=submitted["requests"].A.Last()["id"].I;
        Check("selected text comes from stored option",submitted["requests"].A.Last()["text"].S==original["persuasion_options"].A.First(o=>o["id"].S=="kind")["text"].S);
        yield return Picture("02-reconsidering");
        deadline=Time.realtimeSinceStartup+220;
        while(Time.realtimeSinceStartup<deadline){
            yield return Call("/api/npc/4",null,j=>app.Person=j);
            var latest=app.Person["player_social"]["requests"].A.Last();
            if(latest["id"].I==request&&latest["status"].S!="pending")break;
            yield return new WaitForSecondsRealtime(.8f);
        }
        var result=app.Person["player_social"]["requests"].A.Last();
        Check("NPC reconsideration completed",result["status"].S=="completed"&&result["reply"].S.Length>0);
        Check("appeal has correct parent and style",result["parent_request"].I==original["id"].I&&result["persuasion_style"].S=="kind");
        Check("one completed appeal cannot chain more options",!result["can_persuade"].B&&!app.Person["player_social"]["requests"].A.First(r=>r["id"].I==original["id"].I)["can_persuade"].B);
        yield return app.Client.Refresh();
        bool paid=result["choice_id"].S=="give_aid";
        Check("actual transfer follows NPC decision",Math.Abs(app.W["player"]["wealth"].N-money-(paid?4:0))<.001&&Math.Abs(app.Person["wealth"].N-npcMoney+(paid?4:0))<.001);
        Check("one player point and no duplicate NPC week",app.Hero["points"].N==points-1&&app.Person["time_left"].I==npcTime);
        Check("calendar stays paused",app.W["month"].I==month);
        yield return Picture("03-npc-final-decision");
        J logs=new J();yield return Call("/api/ai/logs",null,j=>logs=j);
        var log=logs.A.FirstOrDefault(j=>j["task"].S=="player_interaction"&&j["context"]["request"]["id"].I==request);
        Check("same NPC prior refusal restored",log!=null&&log["context"]["reconsideration"]["prior_decision"]["choice_id"].S=="refuse"&&log["context"]["npc_id"].I==4);
        Check("both original and selected utterance cited",log!=null&&log["response"]["memory_ids"].A.Any(m=>m.I==original["event_id"].I)&&log["response"]["memory_ids"].A.Any(m=>m.I==result["event_id"].I));
        Check("real reconsideration model calls recorded",log!=null&&log["request_count"].I>=2&&log["request_count"].I<=3&&log["applied"].B);
        yield return Call("/api/save",J.O(),j=>Check("save completed",j["saved"].S.Length>0));
        Check("no native errors",errors.Count==0);
        string backend=Path.Combine(app.Client.SaveDir,"backend-error.log");Check("no backend exceptions",!File.Exists(backend)||!File.ReadAllText(backend).Contains("Traceback"));
        File.WriteAllText(Path.Combine(output,"acceptance.json"),J.O("passed",checks.All(c=>c["pass"].B),"checks",new J(checks),"errors",new J(errors.Select(e=>new J(e)).ToList()),"result",result).ToString());
        if(GameClient.Arg("-smokeHold")!="true")Application.Quit(checks.All(c=>c["pass"].B)?0:1);
    }
}
}
