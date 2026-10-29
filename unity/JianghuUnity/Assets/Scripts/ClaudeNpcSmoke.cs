using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BootClaudeNpcSmoke(){
        if(GameClient.Arg("-claudeNpcSmoke").Length==0)return;
        if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("NPC acceptance requires an isolated save");Application.Quit(2);return;}
        new GameObject("Claude NPC acceptance").AddComponent<ClaudeNpcSmoke>();
    }
    internal IEnumerator VerifyClaudeNpc(Action<string,bool> check,string output){
        float deadline=Time.realtimeSinceStartup+100;
        while((!Client.Ready||!Hero["created"].B)&&Time.realtimeSinceStartup<deadline)yield return null;
        check("isolated native world ready",Client.Ready&&Hero["created"].B);
        if(!Client.Ready||!Hero["created"].B)yield break;
        var fixture=J.Parse(File.ReadAllText(Path.Combine(Client.SaveDir,"npc-fixture.json")));
        int id=fixture["person"].I;bool restore=GameClient.Arg("-npcRestore")=="1";
        int month=W["month"].I,events=W["events"].I;
        check("model disabled for deterministic native acceptance",!W["ai"]["config"]["enabled"].B&&W["ai"]["calls"].N==0);
        ViewPerson(id,PersonTab.Overview);yield return WaitForCharacter(id);
        var plan=Person["planning_view"]["current"];
        check("model selected undertaking reaches native client with its named target",plan["chosen_by"].S=="本地模型选定"&&plan["target"].I==fixture["target"].I&&plan["ai_reason"].S.Length>0);
        check("turning point reaches native thinking view",Person["turning_view"]["pending"].A.Count>0);
        check("model creed and voice reach native character view",Person["creed_view"]["written"].B&&Person["creed_view"]["motto"].S=="待人以诚，遇事不屈"&&Person["voice_view"]["source"].S=="LLM");
        check("close tie reveals the persisted impression",Person["impression_view"]["visible"].B&&Person["impression_view"]["text"].S.Contains("可以信赖"));
        check("validated character response reaches native client",Person["player_reactions"].A.Any(r=>r["status"].S=="completed"&&r["reply"].S.Contains("好意")));
        check("model participation counters reach system page",W["ai"]["creeds"].I>=1&&W["ai"]["voices"].I>=1&&W["ai"]["reactions_completed"].I>=1);
        personScroll=new Vector2(0,900);
        if(GameClient.Arg("-npcCapture")=="1")yield return CharacterPicture(output,"01-model-undertaking-turning");
        SetTab((int)PersonTab.Character);personScroll=new Vector2(0,900);
        if(GameClient.Arg("-npcCapture")=="1")yield return CharacterPicture(output,"02-creed-voice-impression");
        yield return Client.Refresh();
        check("inspecting new character data preserves time and event count",W["month"].I==month&&W["events"].I==events);
        var visit=Hero["visits"].A.FirstOrDefault(v=>v["id"].I==fixture["visit_id"].I);
        ViewPerson(-1,PersonTab.Overview);yield return null;
        if(GameClient.Arg("-npcCapture")=="1")yield return CharacterPicture(output,"03-protagonist-visits");
        if(restore){
            check("cold restart preserves answered visit",visit==null);
            check("cold restart preserves settled player silver",Math.Abs(W["player"]["wealth"].N-fixture["wealth"].N+4)<.001);
        }else{
            check("real visitor and choices reach protagonist page",visit!=null&&visit["npc_id"].I==fixture["visitor"].I&&visit["kind"].S=="ask_help"&&visit["here"].B&&visit["choices"].A.Any(c=>c["id"].S=="give"&&c["enabled"].B));
            bool rejectedCallback=false;Client.Error="";
            yield return Client.Request("/api/visit",J.O("visit_id",fixture["visit_id"],"choice","give","revision",-1,"month",month),j=>rejectedCallback=true);
            check("stale visit response is rejected",!rejectedCallback&&Client.Error.Length>0);Client.Error="";
            yield return Client.Refresh();
            check("rejected answer changes neither silver nor calendar",Math.Abs(W["player"]["wealth"].N-fixture["wealth"].N)<.001&&W["month"].I==month);
            J answer=new J();
            yield return Client.Request("/api/visit",J.O("visit_id",fixture["visit_id"],"choice","give","revision",Hero["revision"],"month",month),j=>answer=j);
            yield return Client.Refresh();
            check("native visit response settles once through the real endpoint",answer["event"]["kind"].S=="visit_answer"&&Math.Abs(W["player"]["wealth"].N-fixture["wealth"].N+4)<.001&&!Hero["visits"].A.Any(v=>v["id"].I==fixture["visit_id"].I));
            J visitor=new J();yield return Client.Request("/api/npc/"+fixture["visitor"].I,null,j=>visitor=j);
            check("visit silver reaches the correct NPC",Math.Abs(visitor["wealth"].N-fixture["visitor_wealth"].N-4)<.001);
            bool againCallback=false;yield return Client.Request("/api/visit",J.O("visit_id",fixture["visit_id"],"choice","give","revision",Hero["revision"],"month",month),j=>againCallback=true);
            check("duplicate visit answer is rejected",!againCallback&&Client.Error.Length>0);Client.Error="";
            yield return Client.Request("/api/save",J.O(),j=>{});
        }
        check("acceptance never invokes a model",W["ai"]["calls"].N==0);
    }
}
public sealed class ClaudeNpcSmoke:MonoBehaviour {
    readonly List<J> checks=new List<J>();readonly List<string> errors=new List<string>();string output;
    void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(message+"\n"+stack);}
    void Check(string name,bool passed){checks.Add(J.O("check",name,"passed",passed));File.WriteAllText(Path.Combine(output,"progress.json"),new J(checks).ToString());}
    IEnumerator Start(){
        output=GameClient.Arg("-claudeNpcSmoke");Directory.CreateDirectory(output);Application.logMessageReceived+=Log;
        yield return null;var app=FindAnyObjectByType<JianghuApp>();
        if(app==null)Check("native application exists",false);
        else{
            var run=app.VerifyClaudeNpc(Check,output);
            while(true){bool more=false;object current=null;try{more=run.MoveNext();if(more)current=run.Current;}catch(Exception e){errors.Add(e.ToString());break;}if(!more)break;yield return current;}
        }
        Check("no native errors or exceptions",errors.Count==0);Application.logMessageReceived-=Log;
        bool passed=checks.All(c=>c["passed"].B)&&errors.Count==0;
        File.WriteAllText(Path.Combine(output,"acceptance.json"),J.O("passed",passed,"checks",new J(checks),"errors",new J(errors.Select(e=>new J(e)).ToList())).ToString());
        Application.Quit(passed?0:2);
    }
}
}
