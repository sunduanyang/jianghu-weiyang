using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public class NpcThinkingSmoke : MonoBehaviour {
    GameClient client;JianghuApp app;string output;
    readonly List<J> checks=new List<J>();readonly List<string> errors=new List<string>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Boot(){
        if(GameClient.Arg("-thinkingSmokeDir").Length==0)return;
        if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("Thinking acceptance requires an isolated save");Application.Quit(1);return;}
        new GameObject("NPC thinking acceptance").AddComponent<NpcThinkingSmoke>();
    }
    void OnEnable(){Application.logMessageReceived+=Log;}
    void OnDisable(){Application.logMessageReceived-=Log;}
    void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(message);}
    void Check(string name,bool pass){checks.Add(J.O("check",name,"pass",pass));}
    IEnumerator Call(string path,J body){bool ok=false;yield return client.Request(path,body,j=>ok=true);Check(path,ok);}
    IEnumerator Capture(string name){
        yield return new WaitForSecondsRealtime(.6f);yield return new WaitForEndOfFrame();
        var texture=ScreenCapture.CaptureScreenshotAsTexture();var colors=new HashSet<int>();
        if(texture!=null){File.WriteAllBytes(Path.Combine(output,name+".png"),texture.EncodeToPNG());var pixels=texture.GetPixels32();for(int i=0;i<pixels.Length;i+=97){var c=pixels[i];colors.Add((c.r<<16)|(c.g<<8)|c.b);}Destroy(texture);}
        Check("render "+name,colors.Count>64);
    }
    IEnumerator Start(){
        output=GameClient.Arg("-thinkingSmokeDir");Directory.CreateDirectory(output);
        float deadline=Time.realtimeSinceStartup+100;
        while(Time.realtimeSinceStartup<deadline){client=FindAnyObjectByType<GameClient>();app=FindAnyObjectByType<JianghuApp>();if(client!=null&&client.Ready&&client.World.Has)break;yield return null;}
        if(client==null||!client.Ready){Check("companion startup",false);Finish();yield break;}
        var work=Run();while(true){bool more;object current=null;try{more=work.MoveNext();if(more)current=work.Current;}catch(Exception e){errors.Add(e.ToString());break;}if(!more)break;yield return current;}
        Check("no Unity errors or exceptions",errors.Count==0);Finish();
    }
    IEnumerator Run(){
        Check("actual world has 1800 NPCs",client.World["population"].I==1800);
        Check("models disabled by default",!client.World["ai"]["config"]["enabled"].B);
        int month=client.World["month"].I,events=client.World["events"].I;
        int person=int.Parse(GameClient.Arg("-thinkingPerson"));app.SelectPerson(person);
        float deadline=Time.realtimeSinceStartup+15;while((!app.Person.Has||app.Person["id"].I!=person)&&Time.realtimeSinceStartup<deadline)yield return null;
        var view=app.Person["planning_view"];var plan=view["current"];
        Check("planning view provided",view["version"].I==1);
        Check("role responsibility is named",view["role"]["label"].S.Length>0);
        Check("committed multi-stage plan",plan["intent"].S=="career_change"||plan["intent"].S=="sect_training");
        Check("future steps and current phase shown",plan["steps"].A.Count>1&&plan["next_description"].S.Length>0);
        Check("remembered experiences bounded",app.Person["recalled_memories"].A.Count<=3);
        Screen.SetResolution(1280,800,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.4f);
        Screen.SetResolution(1680,960,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.4f);
        app.ShowThinkingForSmoke();yield return Capture("npc-committed-plan");
        yield return client.Refresh();Check("inspection preserves calendar and events",client.World["month"].I==month&&client.World["events"].I==events);
        yield return Call("/api/hero",client.HeroBody("end_turn"));yield return client.Refresh();
        Check("world turn advances once",client.World["month"].I==month+1);
        Check("all treasury accounts balance",client.World["countries"].A.All(c=>Math.Abs(c["government"]["fund"].N-c["government"]["opening_fund"].N-c["government"]["revenue"].N+c["government"]["expenses"].N)<.0001));
        app.SelectPerson(person);yield return new WaitForSecondsRealtime(.4f);app.ShowThinkingForSmoke();yield return Capture("npc-plan-next-month");
        yield return Call("/api/save",J.O());Check("no model calls in rule run",client.World["ai"]["calls"].N==0);
    }
    void Finish(){File.WriteAllText(Path.Combine(output,"acceptance.json"),J.O("unity",Application.unityVersion,"checks",new J(checks),"errors",new J(errors.Select(e=>new J(e)).ToList()),"passed",errors.Count==0&&checks.All(c=>c["pass"].B)).ToString());Application.Quit(errors.Count==0&&checks.All(c=>c["pass"].B)?0:1);}
}
}
