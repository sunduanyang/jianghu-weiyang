using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public class PriorityThinkingSmoke : MonoBehaviour {
    GameClient client; JianghuApp app; string output;
    readonly List<J> checks=new List<J>(); readonly List<string> errors=new List<string>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] static void Boot(){
        if(GameClient.Arg("-priorityThinkingSmokeDir").Length==0)return;
        if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("Priority acceptance requires an isolated save");Application.Quit(1);return;}
        new GameObject("Priority thinking acceptance").AddComponent<PriorityThinkingSmoke>();
    }
    void OnEnable(){Application.logMessageReceived+=Log;} void OnDisable(){Application.logMessageReceived-=Log;}
    void Log(string m,string s,LogType t){if(t==LogType.Error||t==LogType.Exception)errors.Add(m);}
    void Check(string name,bool pass,J facts=null){checks.Add(J.O("check",name,"pass",pass,"facts",facts??new J()));}
    IEnumerator Capture(string name){
        bool rendered=false;
        for(int attempt=0;attempt<4&&!rendered;attempt++){
            yield return new WaitForSecondsRealtime(.6f);yield return new WaitForEndOfFrame();
            var t=ScreenCapture.CaptureScreenshotAsTexture();if(t==null)continue;
            var colors=new HashSet<int>();var p=t.GetPixels32();for(int i=0;i<p.Length;i+=101)colors.Add((p[i].r<<16)|(p[i].g<<8)|p[i].b);
            rendered=colors.Count>64;if(rendered)File.WriteAllBytes(Path.Combine(output,name+".png"),t.EncodeToPNG());Destroy(t);
        }
        Check("render "+name,rendered);
    }
    IEnumerator Start(){
        output=GameClient.Arg("-priorityThinkingSmokeDir");Directory.CreateDirectory(output);float deadline=Time.realtimeSinceStartup+100;
        while(Time.realtimeSinceStartup<deadline){client=FindAnyObjectByType<GameClient>();app=FindAnyObjectByType<JianghuApp>();if(client!=null&&client.Ready&&client.World.Has)break;yield return null;}
        if(client==null||!client.Ready){Check("companion startup",false);Finish();yield break;}
        var run=Run();while(true){bool more;object current=null;try{more=run.MoveNext();if(more)current=run.Current;}catch(Exception e){errors.Add(e.ToString());break;}if(!more)break;yield return current;}
        Check("no Unity errors or exceptions",errors.Count==0);Finish();
    }
    IEnumerator Run(){
        int month=client.World["month"].I;string hero=client.World["hero"].ToString();var cfg=client.World["ai"]["config"];
        Check("saved enabled preference and v6 continuous mode",cfg["enabled"].B&&cfg["config_version"].I==6);
        Check("no monthly quota or end-month wait",!cfg["reviews_per_month"].Has&&!cfg["turn_wait_seconds"].Has&&!cfg["turn_min_decisions"].Has);
        J ordinary=new J(),high=new J();
        foreach(var n in client.World["npcs"].A.Where(n=>n["alive"].B&&n["age_months"].I>=192)){
            if(ordinary.Has&&high.Has)break;
            bool candidate=n["identity"]["grade"].I>=7&&!ordinary.Has||n["identity"]["grade"].I<=3&&!high.Has;
            if(!candidate)continue;
            J detail=new J();yield return client.Request("/api/npc/"+n["id"].I,null,j=>detail=j);
            var attention=detail["planning_view"]["attention"];
            if(attention["contact_event"].I!=0)continue;
            if(attention["level"].S=="standard"&&!ordinary.Has)ordinary=detail;
            if(attention["grade"].I<=3&&!high.Has)high=detail;
        }
        Check("ordinary and high-standing candidates found",ordinary.Has&&high.Has);
        if(!ordinary.Has||!high.Has)yield break;
        int person=ordinary["id"].I;J first=new J(),second=new J();
        yield return client.Request("/api/intervene",J.O("npc_id",person,"action","visit"),j=>first=j);
        float deadline=Time.realtimeSinceStartup+100;
        while(Time.realtimeSinceStartup<deadline&&!(client.World["ai"]["current_npc"].I==person&&client.World["ai"]["busy"].B)){yield return new WaitForSecondsRealtime(.3f);yield return client.Refresh();}
        Check("recent player contact takes an active model turn",client.World["ai"]["current_npc"].I==person&&client.World["ai"]["busy"].B);
        yield return client.Request("/api/intervene",J.O("npc_id",person,"action","gift"),j=>second=j);
        J recent=new J();deadline=Time.realtimeSinceStartup+180;
        while(Time.realtimeSinceStartup<deadline){
            yield return new WaitForSecondsRealtime(.6f);yield return client.Refresh();
            yield return client.Request("/api/npc/"+person,null,j=>recent=j);
            if(recent["deep_thought"]["signature"]["contact_event"].I==second["id"].I)break;
        }
        var thought=recent["deep_thought"];
        Check("latest interaction replaces an in-flight obsolete snapshot",second["id"].I>first["id"].I&&thought["signature"]["contact_event"].I==second["id"].I);
        Check("recent NPC receives two actual stages and cites newest own contact",thought["rounds"].I==2&&thought["level"].S=="intensive"&&thought["assessment"]["player_memory_id"].I==second["id"].I&&thought["assessment"]["player_stance"].S.Length>0&&thought["assessment"]["player_stance"].S!="暂无直接往来"&&!thought["assessment"]["player_consideration"].S.Contains("暂无直接往来"),thought);
        Check("deep review compares alternatives and has a fallback",thought["assessment"]["alternatives"].A.Count>=2&&thought["assessment"]["fallback_action_id"].S.Length>0);
        Screen.SetResolution(1280,800,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.3f);Screen.SetResolution(1680,960,FullScreenMode.Windowed);
        app.SelectPerson(person);deadline=Time.realtimeSinceStartup+20;while(Time.realtimeSinceStartup<deadline&&app.Person["id"].I!=person)yield return null;
        Check("recent person's view has the requested identity",app.Person["id"].I==person);app.ShowThinkingForSmoke();yield return Capture("recent-player-deep-thought");
        int highId=high["id"].I;yield return client.Request("/api/ai/review",J.O("npc_id",highId),j=>{});
        deadline=Time.realtimeSinceStartup+140;
        while(Time.realtimeSinceStartup<deadline){
            yield return new WaitForSecondsRealtime(.6f);yield return client.Refresh();yield return client.Request("/api/npc/"+highId,null,j=>high=j);
            if(high["deep_thought"]["rounds"].I==2)break;
        }
        Check("actual high standing receives deep review without inventing player contact",high["planning_view"]["attention"]["grade"].I<=3&&high["deep_thought"]["rounds"].I==2&&high["deep_thought"]["assessment"]["player_consideration"].S=="暂无直接往来",high["deep_thought"]);
        var beforeHero=J.Parse(hero);
        Check("thinking preserves paused calendar and protagonist",client.World["month"].I==month&&new[]{"name","created","age_months","place","row","col","points","revision"}.All(key=>client.World["hero"][key].ToString()==beforeHero[key].ToString()));
        Check("small queue bounds GPU work",client.World["ai"]["pending"].I<=cfg["queue_limit"].I);
        Check("accepted deep reviews counted",client.World["ai"]["deep_completed"].I>=2,client.World["ai"]);
        app.SelectPerson(highId);deadline=Time.realtimeSinceStartup+20;while(Time.realtimeSinceStartup<deadline&&app.Person["id"].I!=highId)yield return null;
        Check("high person's view has the requested identity",app.Person["id"].I==highId);app.ShowThinkingForSmoke();yield return Capture("high-standing-deep-thought");
        app.ShowLLMSettingsForSmoke();yield return Capture("priority-model-settings");
        bool queued=false;yield return client.Request("/api/hero",client.HeroBody("end_turn"),j=>queued=true);deadline=Time.realtimeSinceStartup+100;
        while(client.World["month"].I==month&&Time.realtimeSinceStartup<deadline){yield return new WaitForSecondsRealtime(.5f);yield return client.Refresh();}
        Check("normal month advances once without waiting for model",queued&&client.World["month"].I==month+1&&!client.World["ai_budget_wait"].B&&!client.World["job"]["thinking"].B);
        yield return client.Request("/api/ai/config",J.O("enabled",false),j=>{});yield return client.Request("/api/save",J.O(),j=>{});
    }
    void Finish(){bool pass=errors.Count==0&&checks.All(c=>c["pass"].B);File.WriteAllText(Path.Combine(output,"acceptance.json"),J.O("checks",new J(checks),"errors",new J(errors.Select(e=>new J(e)).ToList()),"passed",pass).ToString());Application.Quit(pass?0:1);}
}
}
