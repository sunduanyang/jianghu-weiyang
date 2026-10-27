using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public class RankProgressSmoke : MonoBehaviour {
    GameClient client;JianghuApp app;string output;
    readonly List<J> checks=new List<J>();readonly List<string> errors=new List<string>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Boot(){
        if(GameClient.Arg("-rankSmokeDir").Length==0)return;
        if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("Rank acceptance requires an isolated save");Application.Quit(1);return;}
        new GameObject("Rank progression acceptance").AddComponent<RankProgressSmoke>();
    }
    void OnEnable(){Application.logMessageReceived+=Log;}
    void OnDisable(){Application.logMessageReceived-=Log;}
    void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(message);}
    void Check(string name,bool pass){checks.Add(J.O("check",name,"pass",pass));}
    IEnumerator Capture(string name){yield return new WaitForSecondsRealtime(.6f);yield return new WaitForEndOfFrame();var t=ScreenCapture.CaptureScreenshotAsTexture();if(t!=null){File.WriteAllBytes(Path.Combine(output,name+".png"),t.EncodeToPNG());Destroy(t);}Check("render "+name,t!=null);}
    IEnumerator Select(int id){app.SelectPerson(id);float deadline=Time.realtimeSinceStartup+15;while((!app.Person.Has||app.Person["id"].I!=id)&&Time.realtimeSinceStartup<deadline)yield return null;}
    IEnumerator Start(){
        output=GameClient.Arg("-rankSmokeDir");Directory.CreateDirectory(output);float deadline=Time.realtimeSinceStartup+100;
        while(Time.realtimeSinceStartup<deadline){client=FindAnyObjectByType<GameClient>();app=FindAnyObjectByType<JianghuApp>();if(client!=null&&client.Ready&&client.World.Has)break;yield return null;}
        if(client==null||!client.Ready){Check("companion startup",false);Finish();yield break;}
        var run=Run();while(true){bool more;object current=null;try{more=run.MoveNext();if(more)current=run.Current;}catch(Exception e){errors.Add(e.ToString());break;}if(!more)break;yield return current;}
        Check("no Unity errors or exceptions",errors.Count==0);Finish();
    }
    IEnumerator Run(){
        int month=client.World["month"].I,count=client.World["events"].I;
        Check("actual isolated world populated",client.World["population"].I>=1700);
        Check("rule simulation needs no language model",!client.World["ai"]["config"]["enabled"].B);
        yield return Select(int.Parse(GameClient.Arg("-rankPending")));
        var view=app.Person["rank_progression"];Check("assessment data and six gates",view["enabled"].B&&view["sect"]["requirements"].A.Count==6);
        Check("pending months persist",view["sect"]["completed_months"].I>0);
        Screen.SetResolution(1280,800,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.3f);Screen.SetResolution(1680,960,FullScreenMode.Windowed);
        app.ShowRankProgressForSmoke();yield return Capture("pending-promotion");
        yield return Select(int.Parse(GameClient.Arg("-rankHistory")));Check("real change history",app.Person["rank_progression"]["history"].A.Count>0);
        app.ShowRankProgressForSmoke();yield return Capture("rank-change-history");
        app.ShowHeroRankForSmoke();yield return Capture("hero-social-mobility");
        bool accepted=false;yield return client.Request("/api/ranks",J.O("action","recognize_notable","revision",-1,"month",month),j=>accepted=true,false);
        Check("stale status transaction rejected",!accepted);
        yield return client.Refresh();Check("inspection and rejected transaction preserve world month and events",client.World["month"].I==month&&client.World["events"].I==count);
        Check("all treasury accounts balance",client.World["countries"].A.All(c=>Math.Abs(c["government"]["fund"].N-c["government"]["opening_fund"].N-c["government"]["revenue"].N+c["government"]["expenses"].N)<.001));
    }
    void Finish(){bool passed=errors.Count==0&&checks.All(c=>c["pass"].B);File.WriteAllText(Path.Combine(output,"acceptance.json"),J.O("unity",Application.unityVersion,"checks",new J(checks),"errors",new J(errors.Select(e=>new J(e)).ToList()),"passed",passed).ToString());Application.Quit(passed?0:1);}
}
}
