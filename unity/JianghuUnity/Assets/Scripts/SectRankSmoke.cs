using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
// Explicitly requested acceptance run; always uses a separately prepared save.
public class SectRankSmoke : MonoBehaviour {
    string output;GameClient client;JianghuApp app;
    readonly List<J> checks=new List<J>();readonly List<string> errors=new List<string>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Boot(){
        if(GameClient.Arg("-sectRankSmokeDir").Length==0)return;
        if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("Sect rank acceptance requires an isolated -saveDir");Application.Quit(1);return;}
        new GameObject("Sect rank acceptance").AddComponent<SectRankSmoke>();
    }
    void OnEnable(){Application.logMessageReceived+=Log;}
    void OnDisable(){Application.logMessageReceived-=Log;}
    void Log(string message,string stack,LogType type){if(type==LogType.Exception||type==LogType.Error)errors.Add(message);}
    void Check(string label,bool pass){checks.Add(J.O("check",label,"pass",pass));}
    IEnumerator Capture(string name){
        yield return new WaitForSecondsRealtime(.5f);yield return new WaitForEndOfFrame();
        var texture=ScreenCapture.CaptureScreenshotAsTexture();var colors=new HashSet<int>();
        if(texture!=null){File.WriteAllBytes(Path.Combine(output,name+".png"),texture.EncodeToPNG());var pixels=texture.GetPixels32();for(int i=0;i<pixels.Length;i+=97){var c=pixels[i];colors.Add((c.r<<16)|(c.g<<8)|c.b);}Destroy(texture);}
        Check("render "+name,texture!=null&&colors.Count>64);
    }
    IEnumerator Start(){
        output=GameClient.Arg("-sectRankSmokeDir");Directory.CreateDirectory(output);float deadline=Time.realtimeSinceStartup+100;
        while(Time.realtimeSinceStartup<deadline){client=FindAnyObjectByType<GameClient>();app=FindAnyObjectByType<JianghuApp>();if(client!=null&&client.Ready&&client.World.Has)break;yield return null;}
        if(client==null||!client.Ready||!client.World.Has){Check("startup",false);Finish();yield break;}
        var run=Run();while(true){bool more;object current=null;try{more=run.MoveNext();if(more)current=run.Current;}catch(Exception e){errors.Add(e.ToString());break;}if(!more)break;yield return current;}
        Check("no Unity errors or exceptions",errors.Count==0);Finish();
    }
    IEnumerator Run(){
        int month=client.World["month"].I,events=client.World["events"].I;double calls=client.World["ai"]["calls"].N;
        Check("fourteen sects",client.World["factions"].A.Count==14);
        foreach(var f in client.World["factions"].A){
            J detail=null;yield return client.Request("/api/faction/"+f["id"].S,null,j=>detail=j);
            Check("detail "+f["name"].S,detail!=null);if(detail==null)continue;
            var grades=detail["rank_system"]["grades"].A;var roster=detail["roster"].A;
            Check("nine ordered grades "+f["name"].S,grades.Select(r=>r["grade"].I).SequenceEqual(Enumerable.Range(1,9)));
            Check("nine distinct titles "+f["name"].S,grades.Select(r=>r["title"].S).Distinct().Count()==9);
            Check("one census entry per member "+f["name"].S,grades.Sum(r=>r["population"].I)==detail["members"].I);
            Check("all nine grades represented in fixture "+f["name"].S,grades.All(r=>r["population"].I>0));
            Check("roster identity matches sect grade "+f["name"].S,roster.All(n=>n["rank"]["grade"].I>=1&&n["rank"]["grade"].I<=9&&n["identity"]["grade"].I==n["rank"]["grade"].I));
            Check("leader first "+f["name"].S,roster.Count>0&&roster[0]["id"].I==f["leader"].I&&roster[0]["rank"]["grade"].I==1);
        }
        // Resizing initializes a hidden acceptance window's swapchain.
        Screen.SetResolution(1280,800,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.5f);
        Screen.SetResolution(1680,960,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.5f);
        foreach(string key in new[]{"wudang","beggars","shaolin","haidong","heishui"}){
            var f=client.World["factions"].A.First(s=>s["key"].S==key);app.Institution=new J();app.OpenInstitution(false,f["id"].I);
            float deadline=Time.realtimeSinceStartup+15;while((!app.Institution.Has||app.Institution["key"].S!=key)&&Time.realtimeSinceStartup<deadline)yield return null;
            Check("native rank panel "+key,app.Institution["rank_system"]["grades"].A.Count==9);app.ShowSectRanksForSmoke();yield return Capture("nine-grades-"+key);
        }
        if(client.World["hero"]["membership"].Has)
            Check("player grade agrees with membership",client.World["hero"]["identity"]["grade"].I==client.World["hero"]["membership"]["grade"].I);
        else Check("civil protagonist uses social grades",client.World["hero"]["identity"]["system"].S=="nation");
        if(int.TryParse(GameClient.Arg("-populationExpected"),out int expected)){
            Check("requested population loaded",client.World["population"].I==expected);
            foreach(var c in client.World["countries"].A){
                J detail=null;yield return client.Request("/api/country/"+c["id"].S,null,j=>detail=j);
                Check("all social grades populated "+c["name"].S,detail!=null&&detail["rank_system"]["grades"].A.Count==9&&detail["rank_system"]["grades"].A.All(r=>r["population"].I>0));
                if(detail!=null)Check("civil and sect census balance "+c["name"].S,detail["rank_system"]["population"].I+detail["rank_system"]["sect_population"].I==detail["population"].I);
            }
            app.Institution=new J();app.OpenInstitution(true,0);
            float deadline=Time.realtimeSinceStartup+15;while(!app.Institution.Has&&Time.realtimeSinceStartup<deadline)yield return null;
            yield return Capture("population-social-grades");
        }
        yield return client.Refresh();
        Check("read-only inspection preserves month and events",client.World["month"].I==month&&client.World["events"].I==events);
        Check("no model calls",client.World["ai"]["calls"].N==calls);
    }
    void Finish(){File.WriteAllText(Path.Combine(output,"acceptance.json"),J.O("unity",Application.unityVersion,"checks",new J(checks),"errors",new J(errors.Select(x=>new J(x)).ToList()),"passed",errors.Count==0&&checks.All(c=>c["pass"].B)).ToString());Application.Quit(errors.Count==0&&checks.All(c=>c["pass"].B)?0:1);}
}
}
