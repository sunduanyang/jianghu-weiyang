using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
// Two separate player processes prove that a long route advances, persists and
// finishes after an explicitly requested monthly turn. Never uses a personal save.
public class CityJourneySmoke : MonoBehaviour {
    GameClient client;JianghuApp app;string output;
    readonly List<J> checks=new List<J>();readonly List<string> errors=new List<string>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Boot(){
        if(GameClient.Arg("-cityJourneySmokeDir").Length==0)return;
        if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("City journey acceptance requires an isolated -saveDir");Application.Quit(1);return;}
        new GameObject("City journey acceptance").AddComponent<CityJourneySmoke>();
    }
    void OnEnable(){Application.logMessageReceived+=Log;}
    void OnDisable(){Application.logMessageReceived-=Log;}
    void Log(string message,string stack,LogType type){if(type==LogType.Exception||type==LogType.Error)errors.Add(message);}
    void Check(string name,bool pass,J facts=null){checks.Add(J.O("check",name,"pass",pass,"facts",facts??new J()));}
    IEnumerator Capture(string name){
        yield return new WaitForSecondsRealtime(.4f);yield return new WaitForEndOfFrame();
        var t=ScreenCapture.CaptureScreenshotAsTexture();var colors=new HashSet<int>();
        if(t!=null){var pixels=t.GetPixels32();for(int i=0;i<pixels.Length;i+=101){var c=pixels[i];colors.Add((c.r<<16)|(c.g<<8)|c.b);}File.WriteAllBytes(Path.Combine(output,name+".png"),t.EncodeToPNG());Destroy(t);}
        Check("render "+name,colors.Count>64,J.O("sampled_colors",colors.Count));
    }
    IEnumerator Start(){
        output=GameClient.Arg("-cityJourneySmokeDir");Directory.CreateDirectory(output);
        float deadline=Time.realtimeSinceStartup+100;
        while(Time.realtimeSinceStartup<deadline){client=FindAnyObjectByType<GameClient>();app=FindAnyObjectByType<JianghuApp>();if(client!=null&&client.Ready&&client.World.Has)break;yield return null;}
        if(client==null||!client.Ready||!client.World.Has){Check("startup",false);Finish();yield break;}
        var run=Run();while(true){bool more=false;object current=null;try{more=run.MoveNext();if(more)current=run.Current;}catch(Exception e){Check("journey acceptance execution",false,J.O("error",e.ToString()));break;}if(!more)break;yield return current;}
        Check("no Unity errors or exceptions",errors.Count==0,new J(errors.Select(e=>new J(e)).ToList()));Finish();
    }
    IEnumerator Run(){
        bool resume=GameClient.Arg("-cityJourneyPhase")=="resume";
        int place=client.World["hero"]["place"].I,month=client.World["month"].I;double calls=client.World["ai"]["calls"].N;
        app.GoStage(Stage.City,false);yield return app.CityLoadForSmoke(place);
        Screen.SetResolution(1280,800,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.3f);Screen.SetResolution(1680,960,FullScreenMode.Windowed);
        string siteId=GameClient.Arg("-cityJourneySite","settlement_2");
        var site=app.City["settlements"].A.First(s=>s["id"].S==siteId);var target=new Vector2Int(site["row"].I,site["col"].I);
        app.CityPickSettlementForSmoke(siteId);
        if(resume){
            var proof=J.Parse(File.ReadAllText(GameClient.Arg("-cityJourneyProof")));var hero=client.World["hero"];
            Check("cold restart preserves intermediate position and budget",hero["row"].I==proof["row"].I&&hero["col"].I==proof["col"].I&&hero["points"].I==proof["points"].I&&month==proof["month"].I);
            bool advanced=false;yield return client.Action("/api/hero",client.HeroBody("end_turn"),j=>advanced=true);
            float turnDeadline=Time.realtimeSinceStartup+60;
            while(client.World["month"].I==month&&client.World["job"]["remaining"].I>0&&Time.realtimeSinceStartup<turnDeadline){yield return new WaitForSecondsRealtime(.2f);yield return client.Refresh();}
            Check("only an explicit month end restores the budget",advanced&&client.World["month"].I==month+1&&client.World["hero"]["points"].I==client.World["hero"]["max_points"].I);
            yield return app.CityLoadForSmoke(place);app.CityPickSettlementForSmoke(siteId);
        }
        var before=client.World["hero"];int startMonth=client.World["month"].I;
        var path=app.CityPath(before["row"].I,before["col"].I,target.x,target.y,out int cost);
        int expectedSpend=0,expectedIndex=0;
        for(int i=1;i<path.Count;i++){char t=app.City["rows"][path[i].x].S[path[i].y];int step=t=='+'||t=='='?1:t=='t'?3:2;if(expectedSpend+step>before["points"].I)break;expectedSpend+=step;expectedIndex=i;}
        Check(resume?"remaining route is affordable":"fixture exceeds the monthly budget",resume?cost>0&&cost<=before["points"].I:cost>before["points"].I&&before["points"].I==before["max_points"].I,J.O("cost",cost,"budget",before["points"]));
        yield return Capture(resume?"resume-ready":"long-route-ready");
        Check("destination accepts the affordable route prefix",app.CityWalkForSmoke(target.x,target.y));
        float deadline=Time.realtimeSinceStartup+60;while(app.CityWalkingForSmoke&&Time.realtimeSinceStartup<deadline)yield return null;
        yield return client.Refresh();var after=client.World["hero"];
        Check("walking stops at the last affordable cell",!app.CityWalkingForSmoke&&after["row"].I==path[expectedIndex].x&&after["col"].I==path[expectedIndex].y);
        Check("only the traversed terrain is charged",after["points"].I==before["points"].I-expectedSpend&&after["points"].I>=0,J.O("spent",expectedSpend,"points",after["points"]));
        Check("walking never advances the calendar",client.World["month"].I==startMonth);
        Check(resume?"second month reaches the selected settlement":"first month makes partial progress",resume?after["row"].I==target.x&&after["col"].I==target.y:expectedIndex>0&&expectedIndex<path.Count-1);
        if(!resume){
            Check("exhausted budget cannot start another step",!app.CityWalkForSmoke(target.x,target.y));
            File.WriteAllText(Path.Combine(output,"proof.json"),J.O("row",after["row"],"col",after["col"],"points",after["points"],"month",client.World["month"]).ToString());
        }
        Check("no model calls",client.World["ai"]["calls"].N==calls);
        yield return Capture(resume?"long-route-arrival":"long-route-month-end");
    }
    void Finish(){bool pass=checks.All(c=>c["pass"].B)&&errors.Count==0;File.WriteAllText(Path.Combine(output,"city-journey.json"),J.O("pass",pass,"checks",new J(checks),"errors",new J(errors.Select(e=>new J(e)).ToList())).ToString());Application.Quit(pass?0:1);}
}
}
