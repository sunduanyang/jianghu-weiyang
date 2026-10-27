using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public class ContinuousThinkingSmoke : MonoBehaviour {
    GameClient client;JianghuApp app;string output;
    readonly List<J> checks=new List<J>();readonly List<string> errors=new List<string>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Boot(){
        if(GameClient.Arg("-continuousSmokeDir").Length==0)return;
        if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("Continuous thinking acceptance requires an isolated save");Application.Quit(1);return;}
        new GameObject("Continuous thinking acceptance").AddComponent<ContinuousThinkingSmoke>();
    }
    void OnEnable(){Application.logMessageReceived+=Log;}
    void OnDisable(){Application.logMessageReceived-=Log;}
    void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(message);}
    void Check(string name,bool pass,J facts=null){checks.Add(J.O("check",name,"pass",pass,"facts",facts??new J()));}
    IEnumerator Capture(string name){
        bool rendered=false;
        for(int attempt=0;attempt<4&&!rendered;attempt++){
            yield return new WaitForSecondsRealtime(.6f);yield return new WaitForEndOfFrame();
            var t=ScreenCapture.CaptureScreenshotAsTexture();if(t==null)continue;
            var colors=new HashSet<int>();var pixels=t.GetPixels32();for(int i=0;i<pixels.Length;i+=101){var c=pixels[i];colors.Add((c.r<<16)|(c.g<<8)|c.b);}
            rendered=colors.Count>64;if(rendered)File.WriteAllBytes(Path.Combine(output,name+".png"),t.EncodeToPNG());Destroy(t);
        }
        Check("render "+name,rendered);
    }
    IEnumerator Start(){
        output=GameClient.Arg("-continuousSmokeDir");Directory.CreateDirectory(output);float deadline=Time.realtimeSinceStartup+100;
        while(Time.realtimeSinceStartup<deadline){client=FindAnyObjectByType<GameClient>();app=FindAnyObjectByType<JianghuApp>();if(client!=null&&client.Ready&&client.World.Has)break;yield return null;}
        if(client==null||!client.Ready){Check("companion startup",false);Finish();yield break;}
        var run=Run();while(true){bool more;object current=null;try{more=run.MoveNext();if(more)current=run.Current;}catch(Exception e){errors.Add(e.ToString());break;}if(!more)break;yield return current;}
        Check("no Unity errors or exceptions",errors.Count==0);Finish();
    }
    IEnumerator Run(){
        int month=client.World["month"].I;string hero=client.World["hero"].ToString();int initial=client.World["ai"]["month_completed"].I,initialApplied=client.World["ai"]["session_applied"].I;
        var config=client.World["ai"]["config"];
        Check("continuous mode keeps saved model enablement",config["enabled"].B&&config["config_version"].I>=5);
        Check("obsolete monthly quota and turn waits removed",!config["reviews_per_month"].Has&&!config["turn_wait_seconds"].Has&&!config["turn_min_decisions"].Has);
        float deadline=Time.realtimeSinceStartup+120;
        while(client.World["ai"]["session_applied"].I<initialApplied+3&&Time.realtimeSinceStartup<deadline){yield return new WaitForSecondsRealtime(.5f);yield return client.Refresh();}
        Check("at least three real thoughts complete while month stays open",client.World["ai"]["session_applied"].I>=initialApplied+3,client.World["ai"]);
        Check("monthly people count keeps growing beyond old quota",client.World["ai"]["month_completed"].I>=initial+3,J.O("before",initial,"after",client.World["ai"]["month_completed"]));
        Check("background does not advance calendar or spend protagonist points",client.World["month"].I==month&&client.World["hero"].ToString()==hero);
        Check("small queue controls load without monthly quota",client.World["ai"]["pending"].I<=config["queue_limit"].I);
        Screen.SetResolution(1280,800,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.3f);Screen.SetResolution(1680,960,FullScreenMode.Windowed);
        app.ShowLLMSettingsForSmoke();yield return Capture("continuous-month-open");
        double executions=client.World["ai"]["plan_executions"].N;bool queued=false;
        yield return client.Request("/api/hero",client.HeroBody("end_turn"),j=>queued=true);
        Check("ordinary end-month accepted",queued);
        deadline=Time.realtimeSinceStartup+100;
        while(client.World["month"].I==month&&Time.realtimeSinceStartup<deadline){yield return new WaitForSecondsRealtime(.5f);yield return client.Refresh();}
        Check("month advances exactly once without waiting for a thought quota",client.World["month"].I==month+1&&!client.World["ai_budget_wait"].B&&!client.World["job"]["thinking"].B);
        Check("completed model plans still execute",client.World["ai"]["plan_executions"].N>executions,client.World["ai"]);
        int applied=client.World["ai"]["session_applied"].I;deadline=Time.realtimeSinceStartup+60;
        while(client.World["ai"]["session_applied"].I<=applied&&Time.realtimeSinceStartup<deadline){yield return new WaitForSecondsRealtime(.5f);yield return client.Refresh();}
        Check("thinking continues after the new month starts",client.World["ai"]["session_applied"].I>applied);
        yield return Capture("continuous-after-turn");
        yield return client.Request("/api/ai/config",J.O("enabled",false),j=>{});
        yield return client.Request("/api/save",J.O(),j=>{});
    }
    void Finish(){bool pass=errors.Count==0&&checks.All(c=>c["pass"].B);File.WriteAllText(Path.Combine(output,"acceptance.json"),J.O("unity",Application.unityVersion,"checks",new J(checks),"errors",new J(errors.Select(e=>new J(e)).ToList()),"passed",pass).ToString());Application.Quit(pass?0:1);}
}
}
