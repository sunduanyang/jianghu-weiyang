using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    public void OpenSocialForSmoke(){TalkTo(SelectedPerson);nextConversationPoll=0;}
}
public class PlayerSocialSmoke : MonoBehaviour {
    JianghuApp app;
    string output;
    readonly List<J> checks=new List<J>();
    readonly List<string> errors=new List<string>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot(){
        if(GameClient.Arg("-playerSocialSmoke")!="true")return;
        if(GameClient.Arg("-saveDir")==""){Debug.LogError("Player social acceptance requires an isolated save");Application.Quit(1);return;}
        new GameObject("Player social acceptance").AddComponent<PlayerSocialSmoke>();
    }
    void Awake(){Application.logMessageReceived+=(c,s,t)=>{if(t==LogType.Error||t==LogType.Exception)errors.Add(c+"\n"+s);};}
    void Check(string name,bool pass){checks.Add(J.O("name",name,"pass",pass));Debug.Log("PLAYER_SOCIAL_CHECK "+name+" "+pass);}
    IEnumerator Call(string path,J body,Action<J> done){yield return app.Client.Request(path,body,done);}
    IEnumerator Picture(string name){yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));yield return new WaitForSecondsRealtime(.3f);}
    IEnumerator WaitReply(int id){
        float deadline=Time.realtimeSinceStartup+180;
        while(Time.realtimeSinceStartup<deadline){
            yield return Call("/api/npc/"+id,null,j=>app.Person=j);
            var list=app.Person["player_social"]["requests"].A;
            if(list.Count>0&&list.Last()["status"].S!="pending")yield break;
            yield return new WaitForSecondsRealtime(1);
        }
        Check("reply arrived before timeout",false);
    }
    IEnumerator Start(){
        output=GameClient.Arg("-smokeOut");Directory.CreateDirectory(output);
        float deadline=Time.realtimeSinceStartup+100;
        while(Time.realtimeSinceStartup<deadline){app=FindAnyObjectByType<JianghuApp>();if(app!=null&&app.Client!=null&&app.Client.Ready&&app.W["npcs"].A.Count>0)break;yield return null;}
        if(app==null||!app.Client.Ready||app.W["npcs"].A.Count==0){File.WriteAllText(Path.Combine(output,"failed.txt"),"Client state not ready");Application.Quit(1);yield break;}
        yield return app.Client.Refresh();
        Check("isolated fixture with four people",app.W["npcs"].A.Count==4);
        int month=app.W["month"].I;double points=app.Hero["points"].N,wealth=app.W["player"]["wealth"].N;
        app.SelectedPerson=1;
        yield return Call("/api/npc/1",null,j=>app.Person=j);
        Check("social options rendered from real rules",app.Person["player_social"]["options"].A.Count==6);
        app.OpenSocialForSmoke();
        float begin=Time.realtimeSinceStartup;
        yield return Call("/api/social",J.O("npc_id",1,"action","talk","revision",app.Person["player_social"]["revision"]),j=>app.Person["player_social"]=j);
        Check("submit returns while model works",Time.realtimeSinceStartup-begin<5&&app.Person["player_social"]["pending"].B);
        yield return Picture("01-considering");
        yield return WaitReply(1);
        var first=app.Person["player_social"]["requests"].A.Last();
        Check("first real generated reply completed",first["status"].S=="completed"&&first["reply"].S.Length>0&&first["source"].S=="LLM");
        yield return Picture("02-generated-conversation");
        double npcWealth=app.Person["wealth"].N;
        yield return Call("/api/social",J.O("npc_id",1,"action","ask_aid","revision",app.Person["player_social"]["revision"]),j=>app.Person["player_social"]=j);
        yield return WaitReply(1);
        yield return app.Client.Refresh();
        var second=app.Person["player_social"]["requests"].A.Last();
        Check("second decision and reply completed",second["status"].S=="completed"&&second["reply"].S.Length>0);
        bool paid=second["choice_id"].S=="give_aid";
        Check("accepted or refused aid matches actual money",Math.Abs(app.W["player"]["wealth"].N-wealth-(paid?4:0))<.001&&Math.Abs(app.Person["wealth"].N-npcWealth+(paid?4:0))<.001);
        Check("conversation does not consume NPC time",app.Person["time_left"].I==4);
        Check("two completed conversations cost two player points",app.Hero["points"].N==points-2);
        Check("dialogue does not advance time",app.W["month"].I==month);
        Check("conversation retains both turns",app.Person["player_social"]["requests"].A.Count==2);
        yield return Picture("03-request-and-outcome");
        J logs=new J();yield return Call("/api/ai/logs",null,j=>logs=j);
        var conversations=logs.A.Where(j=>j["task"].S=="player_interaction").ToList();
        Check("decisions and optional speech recheck counted",conversations.Count==2&&conversations.All(j=>j["request_count"].I>=2&&j["request_count"].I<=3&&j["applied"].B));
        Check("current utterance is cited in both decisions",conversations.All(j=>j["response"]["memory_ids"].A.Any(m=>m.I==j["context"]["request"]["event_id"].I)));
        yield return Call("/api/save",J.O(),j=>Check("save completed",j["saved"].S.Length>0));
        Check("no native errors",errors.Count==0);
        string backendErrors=Path.Combine(app.Client.SaveDir,"backend-error.log");
        Check("no backend exceptions",!File.Exists(backendErrors)||!File.ReadAllText(backendErrors).Contains("Traceback"));
        File.WriteAllText(Path.Combine(output,"acceptance.json"),J.O("checks",new J(checks),"passed",checks.All(c=>c["pass"].B),"errors",new J(errors.Select(e=>new J(e)).ToList()),"conversation",app.Person["player_social"]).ToString());
        if(GameClient.Arg("-smokeHold")!="true")Application.Quit(checks.All(c=>c["pass"].B)?0:1);
    }
}
}
