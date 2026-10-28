using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
// Opt-in native acceptance. Requires an isolated save; never runs in normal play.
public class WorldTravelSmoke : MonoBehaviour {
    GameClient client;JianghuApp app;string output;
    readonly List<J> checks=new List<J>();readonly List<string> errors=new List<string>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Boot(){
        if(GameClient.Arg("-worldTravelSmokeDir").Length==0)return;
        if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("World travel acceptance requires an isolated -saveDir");Application.Quit(1);return;}
        new GameObject("World map travel acceptance").AddComponent<WorldTravelSmoke>();
    }
    void OnEnable(){Application.logMessageReceived+=Log;}
    void OnDisable(){Application.logMessageReceived-=Log;}
    void Log(string message,string stack,LogType type){if(type==LogType.Exception||type==LogType.Error)errors.Add(message);}
    void Check(string name,bool pass,J facts=null){checks.Add(J.O("check",name,"pass",pass,"facts",facts??new J()));}
    IEnumerator Capture(string name){
        yield return new WaitForSecondsRealtime(.4f);yield return new WaitForEndOfFrame();
        var texture=ScreenCapture.CaptureScreenshotAsTexture();
        if(texture!=null){File.WriteAllBytes(Path.Combine(output,name+".png"),texture.EncodeToPNG());Destroy(texture);}else Check("capture "+name,false);
    }
    IEnumerator Start(){
        output=GameClient.Arg("-worldTravelSmokeDir");Directory.CreateDirectory(output);
        float deadline=Time.realtimeSinceStartup+100;
        while(Time.realtimeSinceStartup<deadline){client=FindAnyObjectByType<GameClient>();app=FindAnyObjectByType<JianghuApp>();if(client!=null&&client.Ready&&client.World.Has)break;yield return null;}
        if(client==null||!client.Ready||!client.World.Has){Check("startup",false);Finish();yield break;}
        var run=Run();while(true){bool more;object current=null;try{more=run.MoveNext();if(more)current=run.Current;}catch(Exception e){Check("acceptance execution",false,J.O("error",e.ToString()));break;}if(!more)break;yield return current;}
        Check("no Unity errors or exceptions",errors.Count==0,new J(errors.Select(e=>new J(e)).ToList()));Finish();
    }
    IEnumerator WaitPlan(){float end=Time.realtimeSinceStartup+10;while(!app.WorldTravelReadyForSmoke&&Time.realtimeSinceStartup<end)yield return null;}
    IEnumerator WaitJourney(){float end=Time.realtimeSinceStartup+45;while(app.WorldTravelMovingForSmoke&&Time.realtimeSinceStartup<end)yield return null;Check("journey settles before timeout",!app.WorldTravelMovingForSmoke);}
    IEnumerator Run(){
        Screen.SetResolution(1680,960,FullScreenMode.Windowed);
        if(!app.Hero["created"].B)yield return client.Action("/api/hero",client.HeroBody("create","name","行旅验收"));
        int home=app.Hero["place"].I;
        yield return app.CityLoadForSmoke(home);
        var start=new Vector2Int(app.Hero["row"].I,app.Hero["col"].I);
        var exits=app.City["nodes"].A.Where(n=>n["kind"].S=="gate"||n["kind"].S=="dock")
            .Select(n=>new Vector2Int(n["row"].I,n["col"].I)).ToList();
        var inside=new[]{Vector2Int.up,Vector2Int.down,Vector2Int.left,Vector2Int.right}.Select(d=>start+d)
            .First(p=>p.x>=0&&p.y>=0&&p.x<app.City["size"].I&&p.y<app.City["size"].I
                &&!"~^#".Contains(app.City["rows"][p.x].S[p.y])&&!exits.Contains(p));
        yield return client.Action("/api/hero",client.HeroBody("walk","place",home,"row",inside.x,"col",inside.y));
        Check("walked away from gates and docks; adjacent travel stays enabled",
            app.Hero["row"].I==inside.x&&app.Hero["col"].I==inside.y&&app.Hero["connections"].A.Any(c=>c["enabled"].B),
            J.O("row",inside.x,"col",inside.y,"exits",new J(exits.Select(p=>J.O("row",p.x,"col",p.y)).ToList())));
        int target=app.Hero["connections"].A.First(c=>c["enabled"].B)["place"].I;
        int month=client.World["month"].I,points=app.Hero["points"].I,revision=app.Hero["revision"].I;
        double wealth=client.World["player"]["wealth"].N;string events=client.World["events"].S;
        app.GoStage(Stage.World);
        float mapDeadline=Time.realtimeSinceStartup+15;
        while(!app.WorldMapReadyForSmoke&&Time.realtimeSinceStartup<mapDeadline)yield return null;
        if(!app.WorldMapReadyForSmoke){Check("world map has rendered before input acceptance",false);yield break;}
        Check("regions, name slips and town marks resolve to their region",app.ValidateWorldMapHitsForSmoke(out J hitFacts),hitFacts);
        Check("single click only selects; drag, right click, overlay and lost focus do not activate",app.ValidateWorldMapGesturesForSmoke(target,out J gestureFacts),gestureFacts);
        app.WorldRegionClickForSmoke(home);app.WorldRegionClickForSmoke(home);
        yield return new WaitForSecondsRealtime(.8f);
        Check("double click current region forces 舆图",app.CurrentStage==Stage.City&&app.CityModeForSmoke==CityMode.Atlas&&!app.WorldTravelOpenForSmoke);
        yield return Capture("current-region-atlas");
        app.GoStage(Stage.World);yield return new WaitForSecondsRealtime(.3f);
        app.WorldRegionClickForSmoke(target);app.WorldRegionClickForSmoke(target);yield return WaitPlan();
        J plan=app.WorldTravelPlanForSmoke;
        Check("double click other region from inside town shows enabled energy quote without a fare",app.WorldTravelOpenForSmoke&&plan["enabled"].B&&plan["points"].I>0&&plan["silver"].N==0,plan);
        yield return Capture("travel-confirmation");
        Check("modal blocks map picking",app.WorldTravelBlocksMapForSmoke);
        app.CancelWorldTravelForSmoke();yield return client.Refresh();
        Check("cancel preserves position, energy, silver, month, revision and events",!app.WorldTravelOpenForSmoke&&app.Hero["place"].I==home&&app.Hero["row"].I==inside.x&&app.Hero["col"].I==inside.y&&app.Hero["points"].I==points&&app.Hero["revision"].I==revision&&client.World["month"].I==month&&client.World["events"].S==events&&Math.Abs(client.World["player"]["wealth"].N-wealth)<.001);
        app.WorldRegionClickForSmoke(target);app.WorldRegionClickForSmoke(target);yield return WaitPlan();
        Screen.SetResolution(1280,720,FullScreenMode.Windowed);yield return Capture("travel-confirmation-small");
        app.EscapeWorldTravelForSmoke();
        Check("Esc dismisses the confirmation",!app.WorldTravelOpenForSmoke&&!app.ViewOpen);
        Screen.SetResolution(1680,960,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.3f);
        app.WorldRegionClickForSmoke(target);app.WorldRegionClickForSmoke(target);yield return WaitPlan();
        plan=app.WorldTravelPlanForSmoke;
        app.ConfirmWorldTravelForSmoke();
        yield return WaitJourney();
        yield return new WaitForSecondsRealtime(.8f);
        Check("confirm moves exactly once, spends energy without silver, opens destination atlas",app.Hero["place"].I==target&&app.Hero["points"].I==points-plan["points"].I&&app.Hero["revision"].I==revision+1&&Math.Abs(client.World["player"]["wealth"].N-wealth)<.001&&client.World["month"].I==month&&app.CurrentStage==Stage.City&&app.CityModeForSmoke==CityMode.Atlas);
        yield return Capture("destination-atlas");
        app.GoStage(Stage.World);yield return new WaitForSecondsRealtime(.3f);
        int far=-1;
        foreach(var place in client.World["locations"].A){
            if(place["id"].I==target)continue;
            J candidate=new J();yield return client.Request("/api/hero/travel?place="+place["id"].S,null,j=>candidate=j);
            if(candidate["enabled"].B&&!candidate["can_finish"].B){far=place["id"].I;break;}
        }
        Check("fixture provides a journey longer than the current energy budget",far>=0);
        if(far<0)yield break;
        app.WorldRegionClickForSmoke(far);app.WorldRegionClickForSmoke(far);yield return WaitPlan();
        plan=app.WorldTravelPlanForSmoke;
        points=app.Hero["points"].I;revision=app.Hero["revision"].I;wealth=client.World["player"]["wealth"].N;
        app.ConfirmWorldTravelForSmoke();yield return WaitJourney();
        Check("long journey moves complete affordable edges and keeps the original destination",
            app.WorldTravelOpenForSmoke&&app.WorldTravelTargetForSmoke==far&&app.Hero["place"].I==plan["travel_place"].I
            &&app.Hero["points"].I==points-plan["travel_points"].I&&app.Hero["revision"].I==revision+plan["travel_legs"].A.Count
            &&client.World["month"].I==month&&Math.Abs(client.World["player"]["wealth"].N-wealth)<.001,plan);
        Check("exhaustion prompts manual month advancement",app.WorldTravelPlanForSmoke["needs_month"].B
            &&!app.WorldTravelPlanForSmoke["enabled"].B&&app.WorldTravelPlanForSmoke["reason"].S=="精力不足，点击过月");
        yield return Capture("insufficient-energy");
        yield return new WaitForSecondsRealtime(.5f);yield return client.Refresh();
        Check("waiting for a month click never advances time automatically",client.World["month"].I==month);
        int clicks=0;
        while(app.Hero["place"].I!=far&&app.WorldTravelPlanForSmoke["needs_month"].B&&clicks++<8){
            int beforeMonth=client.World["month"].I;
            app.AdvanceWorldTravelMonthForSmoke();yield return WaitJourney();
            Check("each month click advances exactly one month and resumes the original journey",client.World["month"].I==beforeMonth+1);
        }
        Check("manual month continuation reaches the target and opens its atlas",app.Hero["place"].I==far
            &&!app.WorldTravelOpenForSmoke&&app.CurrentStage==Stage.City&&app.CityModeForSmoke==CityMode.Atlas);
        yield return Capture("continued-destination-atlas");
    }
    void Finish(){bool pass=checks.All(c=>c["pass"].B);File.WriteAllText(Path.Combine(output,"world-travel.json"),J.O("pass",pass,"checks",new J(checks),"save_dir",client==null?"":client.SaveDir).ToString());Application.Quit(pass?0:1);}
}
public partial class JianghuApp {
    public bool WorldMapReadyForSmoke=>geography.Has&&worldMapTargets.Count>0;
    public bool WorldTravelOpenForSmoke=>WorldTravelOpen;
    public bool WorldTravelReadyForSmoke=>WorldTravelOpen&&worldTravelPlan.Has;
    public bool WorldTravelMovingForSmoke=>worldTravelMoving||worldTravelWaitingMonth;
    public int WorldTravelTargetForSmoke=>worldTravelTarget;
    public J WorldTravelPlanForSmoke=>worldTravelPlan;
    public bool WorldTravelBlocksMapForSmoke=>OverUI(new Vector2(600,400));
    public void WorldRegionClickForSmoke(int place)=>WorldMapClick(place,new Vector2(600,400),Time.realtimeSinceStartup);
    public void CancelWorldTravelForSmoke()=>CloseWorldTravel();
    public void ConfirmWorldTravelForSmoke()=>ConfirmWorldTravel();
    public void AdvanceWorldTravelMonthForSmoke()=>AdvanceWorldTravelMonth();
    public void EscapeWorldTravelForSmoke(){HandleEscape();RunDeferred();}
    public bool ValidateWorldMapHitsForSmoke(out J facts){
        int regions=0,marks=0,slips=0;
        foreach(var place in W["locations"].A){
            int id=place["id"].I;var region=geography["cells"].A.First(c=>c["place"].I==id);
            Vector2 anchor=new Vector2((float)region["cx"].N,(float)region["cy"].N);
            if(MapRegionAt(anchor)==id)regions++;
            var hits=worldMapTargets.Where(t=>t.Value==id).ToList();
            if(hits.Count>=2){if(WorldMapPlaceAt(hits[hits.Count-1].Key.center)==id)marks++;if(WorldMapPlaceAt(hits[hits.Count-2].Key.center)==id)slips++;}
        }
        facts=J.O("regions",regions,"marks",marks,"slips",slips,"expected",W["locations"].A.Count);
        // Some clipped land centroids fall outside concave regions; marks/slips remain exact targets.
        return regions>0&&marks==W["locations"].A.Count&&slips==W["locations"].A.Count;
    }
    public bool ValidateWorldMapGesturesForSmoke(int place,out J facts){
        Rect local=new Rect(0,0,1680,960);Vector2 p=new Vector2(600,400),savedPan=mapPan;
        CloseWorldTravel();
        Action<EventType,Vector2,int,bool,bool> send=(type,point,button,overlay,focus)=>HandleWorldMapPointer(new Event{type=type,mousePosition=point,button=button},local,overlay,place,1,focus);
        send(EventType.MouseDown,p,0,false,true);send(EventType.MouseUp,p,0,false,true);
        bool single=SelectedPlace==place&&!WorldTravelOpen&&CurrentStage==Stage.World;
        send(EventType.MouseDown,p,0,false,true);send(EventType.MouseDrag,p+Vector2.right*20,0,false,true);send(EventType.MouseDrag,p,0,false,true);send(EventType.MouseUp,p,0,false,true);
        bool drag=!WorldTravelOpen;CloseWorldTravel();
        send(EventType.MouseDown,p,1,false,true);send(EventType.MouseUp,p,1,false,true);bool right=!WorldTravelOpen;
        send(EventType.MouseDown,p,0,true,true);send(EventType.MouseUp,p,0,true,true);bool overlay=!WorldTravelOpen;
        send(EventType.MouseDown,p,0,false,true);send(EventType.MouseUp,p,0,false,false);bool focus=!WorldTravelOpen;
        mapPan=savedPan;CloseWorldTravel();facts=J.O("single",single,"drag",drag,"right",right,"overlay",overlay,"focus",focus);
        return single&&drag&&right&&overlay&&focus;
    }
}
}
