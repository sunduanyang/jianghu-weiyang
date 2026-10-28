using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public class LLMAgencySmoke : MonoBehaviour {
    GameClient client;JianghuApp app;string output;
    readonly List<J> checks=new List<J>();readonly List<string> errors=new List<string>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Boot(){
        if(GameClient.Arg("-llmAgencySmokeDir").Length==0)return;
        if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("LLM acceptance requires an isolated save");Application.Quit(1);return;}
        new GameObject("LLM agency acceptance").AddComponent<LLMAgencySmoke>();
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
        output=GameClient.Arg("-llmAgencySmokeDir");Directory.CreateDirectory(output);float deadline=Time.realtimeSinceStartup+100;
        while(Time.realtimeSinceStartup<deadline){client=FindAnyObjectByType<GameClient>();app=FindAnyObjectByType<JianghuApp>();if(client!=null&&client.Ready&&client.World.Has)break;yield return null;}
        if(client==null||!client.Ready){Check("companion startup",false);Finish();yield break;}
        var run=Run();while(true){bool more;object current=null;try{more=run.MoveNext();if(more)current=run.Current;}catch(Exception e){errors.Add(e.ToString());break;}if(!more)break;yield return current;}
        Check("no Unity errors or exceptions",errors.Count==0);Finish();
    }
    IEnumerator Run(){
        int month=client.World["month"].I;string hero=client.World["hero"].ToString();
        Check("saved LLM preference survives native startup",client.World["ai"]["config"]["enabled"].B);
        Check("world paused while background thoughts run",!client.World["autoplay"].B&&client.World["job"]["remaining"].I==0);
        float deadline=Time.realtimeSinceStartup+120;
        while(client.World["ai"]["coverage"].I<3&&Time.realtimeSinceStartup<deadline){yield return new WaitForSecondsRealtime(.5f);yield return client.Refresh();}
        Check("actual model produces at least three accepted personal decisions",client.World["ai"]["coverage"].I>=3,client.World["ai"]);
        Check("installed local model starts automatically",client.World["ai"]["managed_model"].B);
        Check("background thinking preserves calendar and protagonist",client.World["month"].I==month&&client.World["hero"].ToString()==hero);
        Check("model life policies are active",client.World["ai"]["policy_active"].I>=3);
        Screen.SetResolution(1280,800,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.3f);Screen.SetResolution(1680,960,FullScreenMode.Windowed);
        int person=client.World["ai"]["last_result"]["npc_id"].I;app.SelectPerson(person);
        deadline=Time.realtimeSinceStartup+15;while((!app.Person.Has||app.Person["id"].I!=person)&&Time.realtimeSinceStartup<deadline)yield return null;
        Check("person exposes model policy and own settlement context",app.Person["planning_view"]["life_policy"]["active"].B&&app.Person["local_life_view"]["enabled"].B);
        app.ShowThinkingForSmoke();yield return Capture("model-life-policy");
        double before=client.World["ai"]["plan_executions"].N;
        bool queued=false;yield return client.Request("/api/hero",client.HeroBody("end_turn"),j=>queued=true);
        Check("normal end-month accepts asynchronous LLM turn",queued);
        deadline=Time.realtimeSinceStartup+120;
        while(client.World["month"].I==month&&Time.realtimeSinceStartup<deadline){yield return new WaitForSecondsRealtime(.5f);yield return client.Refresh();}
        Check("normal end-month advances exactly once",client.World["month"].I==month+1);
        Check("model plans really execute during that month",client.World["ai"]["plan_executions"].N>before,client.World["ai"]);
        Check("inference queue is bounded",client.World["ai"]["pending"].I<=client.World["ai"]["config"]["queue_limit"].I);
        app.ShowLLMSettingsForSmoke();yield return Capture("llm-participation-settings");
        J result=new J();yield return client.Request("/api/ai/config",J.O("enabled",false),j=>result=j);
        Check("inference can be disabled",!result["config"]["enabled"].B);
        yield return client.Request("/api/save",J.O(),j=>{});
    }
    void Finish(){bool pass=errors.Count==0&&checks.All(c=>c["pass"].B);File.WriteAllText(Path.Combine(output,"acceptance.json"),J.O("unity",Application.unityVersion,"checks",new J(checks),"errors",new J(errors.Select(e=>new J(e)).ToList()),"passed",pass).ToString());Application.Quit(pass?0:1);}
}
public partial class JianghuApp {public void ShowLLMSettingsForSmoke(){OpenSystem(SystemTab.Model);aiEditing=false;}}
}
