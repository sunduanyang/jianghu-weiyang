using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
// Opt-in native city checks on an isolated save: routes and areas against the backend,
// walking by destination on the 舆图, and sect seats. Run with -saveDir <copy> -citySmokeDir <output>.
public class CityMapSmoke : MonoBehaviour {
    string output;GameClient client;JianghuApp app;
    readonly List<J> checks=new List<J>();readonly List<string> errors=new List<string>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Boot(){
        if(GameClient.Arg("-citySmokeDir").Length==0)return;
        if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("City acceptance requires an isolated -saveDir");Application.Quit(1);return;}
        new GameObject("City map acceptance").AddComponent<CityMapSmoke>();
    }
    void OnEnable(){Application.logMessageReceived+=Log;}
    void OnDisable(){Application.logMessageReceived-=Log;}
    void Log(string message,string stack,LogType type){if(type==LogType.Exception||type==LogType.Error)errors.Add(message);}
    void Check(string name,bool pass,J facts=null){checks.Add(J.O("check",name,"pass",pass,"facts",facts??new J()));if(!pass)Debug.LogWarning("CITY CHECK FAILED: "+name);}
    IEnumerator Call(string path,J body,Action<J> done=null){bool ok=false;yield return client.Request(path,body,j=>{ok=true;done?.Invoke(j);});Check(path,ok,J.O("error",client.Error));}
    IEnumerator Capture(string name){
        yield return new WaitForSecondsRealtime(.5f);yield return new WaitForEndOfFrame();var t=ScreenCapture.CaptureScreenshotAsTexture();var colors=new HashSet<int>();
        if(t!=null){File.WriteAllBytes(Path.Combine(output,name+".png"),t.EncodeToPNG());var pixels=t.GetPixels32();for(int i=0;i<pixels.Length;i+=101){var c=pixels[i];colors.Add((c.r<<16)|(c.g<<8)|c.b);}Destroy(t);}
        Check("render "+name,t!=null&&colors.Count>64,J.O("sampled_colors",colors.Count));
    }
    IEnumerator Start(){
        output=GameClient.Arg("-citySmokeDir");Directory.CreateDirectory(output);float deadline=Time.realtimeSinceStartup+100;
        while(Time.realtimeSinceStartup<deadline){client=FindAnyObjectByType<GameClient>();app=FindAnyObjectByType<JianghuApp>();if(client!=null&&client.Ready&&client.World.Has)break;yield return null;}
        if(client==null||!client.Ready||!client.World.Has){Check("startup",false);Finish();yield break;}
        var run=Run();while(true){bool more=false;object current=null;try{more=run.MoveNext();if(more)current=run.Current;}catch(Exception e){Check("city smoke execution",false,J.O("error",e.ToString()));break;}if(!more)break;yield return current;}
        Check("no Unity errors or exceptions",errors.Count==0,new J(errors.Select(x=>new J(x)).ToList()));Finish();
    }
    IEnumerator Run(){
        if(GameClient.Arg("-cityWalkOnly")=="true"){yield return WalkFlow();yield break;}
        double calls=client.World["ai"]["calls"].N;int month=client.World["month"].I;
        var examples=new Dictionary<int,int>();
        if(client.World["battle_active"].B){
            var before=client.World["hero"];app.SelectedPlace=before["place"].I;app.GoStage(Stage.City);yield return new WaitForSecondsRealtime(1);
            yield return Capture("00-unfinished-battle-movement-notice");
            Check("unfinished battle explains the movement lock",app.CityMovementReasonForSmoke.Contains("切磋"));
            Check("blocked input preserves the hero",!app.CityWalkForSmoke(before["row"].I-1,before["col"].I)&&client.World["hero"]["revision"].I==before["revision"].I);
            yield return app.CityEndBattleForSmoke();
            Check("city surrender releases the movement lock",!client.World["battle_active"].B&&app.CityMovementReasonForSmoke.Length==0);
        }
        yield return app.CityLoadForSmoke(45);var firstEntry=app.CityLoadFactsForSmoke();
        yield return app.CityLoadForSmoke(45);var cachedEntry=app.CityLoadFactsForSmoke();
        Check("repeat entry reuses the view, grid index and routes",cachedEntry["view_cache_hit"].B&&cachedEntry["index_builds"].I==firstEntry["index_builds"].I&&cachedEntry["route_builds"].I==firstEntry["route_builds"].I,cachedEntry);
        Check("fast city lines clip before drawing",app.ValidateCityLineClipping());
        foreach(var place in client.World["locations"].A){
            yield return Call("/api/city?place="+place["id"].S,null,j=>app.City=j);
            if(!examples.ContainsKey(app.City["size"].I))examples[app.City["size"].I]=place["id"].I;
            Check("native routes and settlement lots match backend: "+place["name"].S,app.ValidateCityMap(out J facts),facts);
            Check("whole areas share cell properties: "+place["name"].S,app.ValidateCityAreas(out J areaFacts),areaFacts);
            Check("no bridges in the walking grid: "+place["name"].S,app.ValidateNoBridges(out J noBridgeFacts),noBridgeFacts);
        }
        Check("browsing preserves month",client.World["month"].I==month);
        if(!client.World["hero"]["created"].B){yield return Call("/api/hero",client.HeroBody("create","name","山河行客","sex","男","age",20,"place",45));yield return client.Refresh();}
        int home=client.World["hero"]["place"].I;app.SelectedPlace=home;app.GoStage(Stage.City);yield return new WaitForSecondsRealtime(1);
        yield return app.CityLoadForSmoke(home);
        foreach(var size in new[]{new Vector2Int(1280,800),new Vector2Int(1920,1080),new Vector2Int(1680,960)}){
            Screen.SetResolution(size.x,size.y,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.6f);
            app.CitySmokeView();yield return Capture("07-city-fullscreen-"+size.x+"x"+size.y);
        }
        // A hidden acceptance window needs its swapchain resized before capture.
        yield return Capture("00-city-default-view");
        app.CitySmokeView();yield return Capture("01-city-overview");
        var hero=client.World["hero"];
        var target=app.CitySmokeAffordableSite(hero["points"].I,out int cost);
        if(target.HasValue){
            Check("city input accepts an affordable settlement",app.CityWalkForSmoke(target.Value.x,target.Value.y));while(app.CityWalkingForSmoke)yield return null;
            Check("walk consumes displayed cost and preserves month",client.World["hero"]["points"].I==hero["points"].I-cost&&client.World["month"].I==month);
            app.LoadCity(home);yield return new WaitForSecondsRealtime(.5f);app.CitySmokeView();yield return Capture("03-city-arrival");
        }else Check("walk fixture has an affordable settlement",false);
        foreach(string theme in new[]{"capital","oasis","mountain","highland","forest","island"}){
            foreach(var place in client.World["locations"].A){
                J data=null;yield return Call("/api/city?place="+place["id"].S,null,j=>data=j);
                if(data["theme"].S!=theme)continue;
                app.LoadCity(place["id"].I);yield return new WaitForSecondsRealtime(.4f);app.CitySmokeView();yield return Capture("04-city-"+theme);break;
            }
        }
        foreach(int extent in new[]{20,30})if(examples.TryGetValue(extent,out int example)){
            app.LoadCity(example);yield return new WaitForSecondsRealtime(.5f);app.CitySmokeView();yield return Capture("06-city-size-"+extent);
        }
        Check("exactly fourteen approved sects",client.World["factions"].A.Count==14);
        foreach(string key in new[]{"shaolin","haidong","heishui"}){
            var sect=client.World["factions"].A.First(f=>f["key"].S==key);
            yield return app.CityLoadForSmoke(sect["place"].I);app.GoStage(Stage.City,false);app.CitySmokeView();
            Check("independent nine-cell seat: "+key,app.City["settlements"].A.Count(s=>s["kind"].S=="sect"&&s["faction"].I==sect["id"].I&&s["footprint"]["size"].I==3)==1);
            yield return Capture("12-sect-seat-"+key);
            app.Institution=new J();app.OpenInstitution(false,sect["id"].I);float deadline=Time.realtimeSinceStartup+15;
            while((!app.Institution.Has||app.Institution["key"].S!=key)&&Time.realtimeSinceStartup<deadline)yield return null;
            Check("sect detail and player options: "+key,app.Institution["key"].S==key&&app.Institution["player_options"].A.Count>0);
            yield return Capture("13-sect-detail-"+key);
        }
        app.GoStage(Stage.City,false);app.LoadCity(home);yield return new WaitForSecondsRealtime(.4f);app.CitySmokeView();
        Check("no model calls",client.World["ai"]["calls"].N==calls);
    }
    IEnumerator WalkFlow(){
        if(client.World["battle_active"].B)yield return app.CityEndBattleForSmoke();
        if(!client.World["hero"]["created"].B){yield return Call("/api/hero",client.HeroBody("create","name","行路试客","sex","男","age",20,"place",45));yield return client.Refresh();}
        if(client.World["hero"]["points"].I<25){yield return Call("/api/hero",client.HeroBody("end_turn"));yield return client.Refresh();}
        int place=client.World["hero"]["place"].I;app.GoStage(Stage.City);yield return app.CityLoadForSmoke(place);
        Screen.SetResolution(1680,960,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.6f);
        Screen.SetResolution(1280,800,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.6f);
        yield return Capture("walk-ready");
        var hero=client.World["hero"];int month=client.World["month"].I;double calls=client.World["ai"]["calls"].N;
        var origin=new Vector2Int(hero["row"].I,hero["col"].I);
        List<Vector2Int> route=null;int cost=0;
        for(int row=0;row<app.City["size"].I&&route==null;row++)for(int col=0;col<app.City["size"].I;col++){
            var path=app.CityPath(origin.x,origin.y,row,col,out int amount);
            if(path.Count>=5&&path.Count<=7&&amount<=Math.Min(12,hero["points"].I-8)){route=path;cost=amount;break;}
        }
        Check("fixture provides a multi-cell affordable route",route!=null);
        if(route==null)yield break;
        var target=route[route.Count-1];app.CityFocusForWalkSmoke();
        int refreshes=client.StateRefreshRequests;float maxLanding=0,walkingStarted=Time.realtimeSinceStartup;
        Check("destination starts a walk",app.CityWalkForSmoke(target.x,target.y));
        Check("accepting the destination preserves the starting cell",client.World["hero"]["row"].I==origin.x&&client.World["hero"]["col"].I==origin.y);
        int seen=0,spent=0,interiorFrames=0;bool sequential=true,charged=true,visual=true,captured=false;
        float deadline=Time.realtimeSinceStartup+45;
        while(app.CityWalkingForSmoke&&Time.realtimeSinceStartup<deadline){
            var current=client.World["hero"];var at=new Vector2Int(current["row"].I,current["col"].I);
            if(at!=route[seen]){
                if(seen+1>=route.Count||at!=route[seen+1]){sequential=false;break;}
                seen++;char terrain=app.City["rows"][at.x].S[at.y];spent+=terrain=='+'||terrain=='='?1:terrain=='t'?3:2;
                charged&=current["points"].I==hero["points"].I-spent;
            }
            var facts=app.CityWalkVisualForSmoke();
            maxLanding=Mathf.Max(maxLanding,(float)facts["landing_seconds"].N);
            if(facts["interior"].B){interiorFrames++;visual&=facts["on_segment"].B;
                if(!captured){yield return new WaitForEndOfFrame();var frame=ScreenCapture.CaptureScreenshotAsTexture();if(frame!=null){File.WriteAllBytes(Path.Combine(output,"walking-between-cells.png"),frame.EncodeToPNG());Destroy(frame);captured=true;}}
            }
            yield return null;
        }
        Check("walk visits every planned cell in order",sequential&&seen==route.Count-1&&!app.CityWalkingForSmoke,J.O("visited_steps",seen,"expected_steps",route.Count-1));
        Check("each step charges its terrain cost",charged&&spent==cost&&client.World["hero"]["points"].I==hero["points"].I-cost,J.O("expected_cost",cost,"spent",spent));
        Check("hero marker interpolates between adjacent cells",visual&&interiorFrames>0&&captured,J.O("interior_frames",interiorFrames));
        Check("walking has only a short landing pause",maxLanding<.15f,J.O("longest_landing_seconds",maxLanding,"elapsed_seconds",Time.realtimeSinceStartup-walkingStarted,"steps",seen));
        Check("walking does not refresh the whole world per cell",client.StateRefreshRequests-refreshes<=1,J.O("world_refreshes",client.StateRefreshRequests-refreshes));
        yield return Capture("walk-arrival");
        if(app.CityWalkingForSmoke){app.CityStopForSmoke();yield break;}

        int before=client.World["hero"]["points"].I;
        var returnPath=app.CityPath(target.x,target.y,origin.x,origin.y,out int returnCost);
        Check("return route starts",app.CityWalkForSmoke(origin.x,origin.y));
        deadline=Time.realtimeSinceStartup+15;
        while(app.CityWalkingForSmoke&&client.World["hero"]["row"].I==target.x&&client.World["hero"]["col"].I==target.y&&Time.realtimeSinceStartup<deadline)yield return null;
        app.CityStopForSmoke();
        while(app.CityWalkingForSmoke&&Time.realtimeSinceStartup<deadline)yield return null;
        var stopped=new Vector2Int(client.World["hero"]["row"].I,client.World["hero"]["col"].I);
        int after=client.World["hero"]["points"].I,revision=client.World["hero"]["revision"].I;
        yield return new WaitForSecondsRealtime(.5f);
        var expectedStop=returnPath[1];char stopTerrain=app.City["rows"][expectedStop.x].S[expectedStop.y];int stopCost=stopTerrain=='+'||stopTerrain=='='?1:stopTerrain=='t'?3:2;
        Check("stop finishes only the current adjacent step",!app.CityWalkingForSmoke&&stopped==expectedStop&&before-after==stopCost&&client.World["hero"]["revision"].I==revision,J.O("stopped_at",stopped.ToString(),"spent",before-after));

        Check("reroute fixture starts",app.CityWalkForSmoke(origin.x,origin.y));
        Check("destination can change during an in-flight step",app.CityWalkForSmoke(target.x,target.y));
        deadline=Time.realtimeSinceStartup+20;
        while(app.CityWalkingForSmoke&&Time.realtimeSinceStartup<deadline)yield return null;
        Check("changed route reaches the new destination",!app.CityWalkingForSmoke&&client.World["hero"]["row"].I==target.x&&client.World["hero"]["col"].I==target.y);

        var blocked=new Vector2Int(-1,-1);
        for(int row=0;row<app.City["size"].I&&blocked.x<0;row++)for(int col=0;col<app.City["size"].I;col++)if("~^#".Contains(app.City["rows"][row].S[col])){blocked=new Vector2Int(row,col);break;}
        revision=client.World["hero"]["revision"].I;
        Check("blocked destination does not start movement",blocked.x>=0&&!app.CityWalkForSmoke(blocked.x,blocked.y)&&client.World["hero"]["revision"].I==revision);
        var tooFar=new Vector2Int(-1,-1);
        for(int row=0;row<app.City["size"].I&&tooFar.x<0;row++)for(int col=0;col<app.City["size"].I;col++){
            var path=app.CityPath(target.x,target.y,row,col,out int amount);
            if(path.Count>1&&amount>client.World["hero"]["points"].I){tooFar=new Vector2Int(row,col);break;}
        }
        Check("unaffordable destination leaves the hero in place",tooFar.x>=0&&!app.CityWalkForSmoke(tooFar.x,tooFar.y)&&client.World["hero"]["revision"].I==revision);
        var back=app.CityPath(target.x,target.y,origin.x,origin.y,out int backCost);
        var shortTarget=back[2];var shortPath=app.CityPath(target.x,target.y,shortTarget.x,shortTarget.y,out int shortCost);
        Check("page-exit fixture starts",app.CityWalkForSmoke(shortTarget.x,shortTarget.y));app.GoStage(Stage.World);
        deadline=Time.realtimeSinceStartup+15;
        while(app.CityWalkingForSmoke&&Time.realtimeSinceStartup<deadline)yield return null;
        var exited=new Vector2Int(client.World["hero"]["row"].I,client.World["hero"]["col"].I);
        yield return new WaitForSecondsRealtime(.3f);
        Check("leaving the city cancels all remaining steps",!app.CityWalkingForSmoke&&exited==shortPath[1]&&client.World["hero"]["revision"].I==revision+1);
        Check("city walking preserves month and makes no model calls",client.World["month"].I==month&&client.World["ai"]["calls"].N==calls);
    }
    void Finish(){File.WriteAllText(Path.Combine(output,"acceptance.json"),J.O("unity",Application.unityVersion,"checks",new J(checks),"passed",checks.All(c=>c["pass"].B)).ToString());Application.Quit(checks.All(c=>c["pass"].B)?0:1);}
}
public partial class JianghuApp {
    public IEnumerator CityLoadForSmoke(int place){yield return LoadCityRoutine(place);}
    public bool ValidateCityLineClipping(){
        var bounds=new Rect(0,0,10,10);var a=new Vector2(-5,4);var b=new Vector2(15,4);
        if(!CityClipLine(ref a,ref b,bounds)||a!=new Vector2(0,4)||b!=new Vector2(10,4))return false;
        a=new Vector2(5,-2);b=new Vector2(5,20);if(!CityClipLine(ref a,ref b,bounds)||a!=new Vector2(5,0)||b!=new Vector2(5,10))return false;
        a=new Vector2(-5,-5);b=new Vector2(-1,-1);if(CityClipLine(ref a,ref b,bounds))return false;
        a=new Vector2(-1,-1);b=new Vector2(2,2);return CityClipLine(ref a,ref b,bounds)&&a==Vector2.zero&&b==new Vector2(2,2);
    }
    public J CityLoadFactsForSmoke()=>J.O("load_ms",cityLastLoadMs,"view_cache_hit",cityLastViewCacheHit,"index_builds",cityIndexBuilds,"route_builds",cityRouteBuilds);
    public string CityMovementReasonForSmoke=>CityMovementBlockReason;
    public bool CityWalkForSmoke(int row,int col)=>TryCityWalk(new Vector2Int(row,col));
    public bool CityWalkingForSmoke=>cityWalking;
    public void CityStopForSmoke()=>StopCityWalk();
    public IEnumerator CityEndBattleForSmoke()=>EndCityBattle();
    // The 舆图 at full view, picking the settlement where the hero stands.
    public void CitySmokeView(){SetCityMode(CityMode.Atlas);rosterOpen=false;cityPathKey="";EnsureCityRoutes();CityAtlasReset();}
    public void CityFocusForWalkSmoke(){CitySmokeView();CityAtlasFocusHero();}
    // The nearest settlement the hero can afford to walk to, by the same routes the 舆图 offers.
    public Vector2Int? CitySmokeAffordableSite(int points,out int cost){
        cost=0;EnsureCityRoutes();Vector2Int? best=null;
        foreach(var site in CitySettlementsList){
            var at=SiteTarget(site);if(!cityCosts.TryGetValue(at,out int amount)||amount<=0||amount>points)continue;
            if(!best.HasValue||amount<cost){best=at;cost=amount;}
        }
        return best;
    }
    public J CityWalkVisualForSmoke(){
        var a=new Vector2(cityStepFrom.x,cityStepFrom.y);var b=new Vector2(cityStepTo.x,cityStepTo.y);var p=HeroTileFloat;
        bool interior=cityStepAnimating&&cityStepProgress>.05f&&cityStepProgress<.95f;
        bool followsRoute=cityWalkIndex>0&&cityWalkIndex<cityWalkRoute.Count&&cityWalkRoute[cityWalkIndex-1]==cityStepFrom&&cityWalkRoute[cityWalkIndex]==cityStepTo;
        return J.O("interior",interior,"landing_seconds",cityStepAnimating?Mathf.Max(0,Time.realtimeSinceStartup-cityStepStartedAt-CityWalkStepSeconds):0,"on_segment",followsRoute&&Vector2.Distance(p,Vector2.Lerp(a,b,cityStepProgress))<.001f&&Vector2.Distance(p,a)>.001f&&Vector2.Distance(p,b)>.001f);
    }
    public bool ValidateCityMap(out J facts){
        cityPathKey="";EnsureCityRoutes();int checkedNodes=0;var failures=new List<J>();
        var gate=CityGateNode;
        foreach(var node in City["nodes"].A){
            var native=CityPath(gate["row"].I,gate["col"].I,node["row"].I,node["col"].I,out int cost);checkedNodes++;
            var q=new Vector2Int(node["row"].I,node["col"].I);
            var origin=CityOrigin();CityPath(origin.x,origin.y,q.x,q.y,out int originCost);
            if(native.Count==0||cost!=node["walk_cost"].I||!cityCosts.TryGetValue(q,out int overlay)||overlay!=originCost)failures.Add(J.O("node",node["name"],"native",cost,"server",node["walk_cost"]));
        }
        int one=0,four=0,nine=0;var used=new HashSet<Vector2Int>();
        foreach(var site in CitySettlementsList){
            int size=site["footprint"]["size"].I;var corner=SiteCorner(site);var entrance=SiteTarget(site);
            if(size<1||size>3){failures.Add(J.O("invalid_footprint",site["id"]));continue;}
            if(size==1)one++;else if(size==2)four++;else nine++;
            bool includesEntrance=false;
            for(int row=0;row<size;row++)for(int col=0;col<size;col++){
                var q=corner+new Vector2Int(row,col);includesEntrance|=q==entrance;
                if(!CityInside(q)||!used.Add(q))failures.Add(J.O("invalid_lot_cell",site["id"],"row",q.x,"col",q.y));
                else if(size>1&&(CityBlocked(CityTile(q.x,q.y))||CityTile(q.x,q.y)=='='))failures.Add(J.O("lot_covers_barrier",site["id"]));
            }
            if(!includesEntrance)failures.Add(J.O("entrance_outside_lot",site["id"]));
        }
        int sects=CitySettlementsList.Count(n=>n["kind"].S=="sect");
        if(City["version"].I>=4&&(nine!=1+sects||four!=1||one!=2||CitySettlementsList.Count!=4+sects))failures.Add(J.O("wrong_settlement_count",CitySettlementsList.Count));
        foreach(var seat in CitySettlementsList.Where(n=>n["kind"].S=="sect"))if(seat["footprint"]["size"].I!=3||!seat["faction"].Has)failures.Add(J.O("invalid_sect_seat",seat["id"]));
        int blocked=0;for(int row=0;row<City["size"].I;row++)for(int col=0;col<City["size"].I;col++)if(CityBlocked(CityTile(row,col))){blocked++;if(cityCosts.ContainsKey(new Vector2Int(row,col)))failures.Add(J.O("blocked_row",row,"col",col));}
        facts=J.O("facilities",checkedNodes,"one_cell_settlements",one,"four_cell_settlements",four,"nine_cell_settlements",nine,"blocked_tiles",blocked,"failures",new J(failures));return failures.Count==0;
    }
    public bool ValidateCityAreas(out J facts){
        EnsureCityIndex();int checkedCells=0;var failures=new List<J>();var areas=City["areas"].A;var cells=City["cell_areas"];
        for(int index=0;index<areas.Count;index++){var area=areas[index];int row=area["row"].I,col=area["col"].I,extent=area["size"].I;
            for(int r=0;r<extent;r++)for(int c=0;c<extent;c++){
                var q=new Vector2Int(row+r,col+c);checkedCells++;
                if(!CityInside(q)){failures.Add(J.O("area_outside_map",area["id"]));continue;}
                char t=CityTile(q.x,q.y);
                if(cells[q.x][q.y].I!=index||t.ToString()!=area["terrain"].S||CityBlocked(t)==area["passable"].B||CityStepCost(t)!=area["move_cost"].I)failures.Add(J.O("inconsistent_area",area["id"],"cell",q.ToString()));
                if(area["kind"].S!="landscape"&&!CitySettlementsList.Any(s=>s["id"].S==area["id"].S&&SiteContains(s,q)))failures.Add(J.O("split_settlement",area["id"]));
            }
        }
        facts=J.O("areas",areas.Count,"checked_cells",checkedCells,"settlements",CitySettlementsList.Count,"failures",new J(failures));
        return checkedCells==citySize*citySize&&failures.Count==0;
    }
    public bool ValidateNoBridges(out J facts){
        EnsureCityIndex();int bridges=0,fords=0;
        for(int row=0;row<citySize;row++)for(int col=0;col<citySize;col++){char t=CityTile(row,col);if(t=='=')bridges++;if(t=='s')fords++;}
        facts=J.O("bridges",bridges,"fords",fords);return bridges==0;
    }
}
}
