using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
// Opt-in native rendering and cross-implementation checks on an isolated save.
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
        if(t!=null){File.WriteAllBytes(Path.Combine(output,name+".png"),t.EncodeToPNG());var pixels=t.GetPixels32();for(int i=0;i<pixels.Length;i+=101){var c=pixels[i];colors.Add((c.r<<16)|(c.g<<8)|c.b);}if(name=="08-cliff-pixel-regression")Check("near ground remains visible beside a tall multi-cell cliff",app.ValidateCityCliffPixels(t,out J cliffFacts),cliffFacts);if(name=="10-river-pixel-regression")Check("natural fords have complete ground without bridge geometry",app.ValidateCityRiverPixels(t,out J riverFacts),riverFacts);Destroy(t);}
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
        Check("city entry prepares art without synchronous texture loads",firstEntry["sync_texture_loads"].I==0,firstEntry);
        while(!app.CityWarmupCompleteForSmoke)yield return null;firstEntry=app.CityLoadFactsForSmoke();
        yield return app.CityLoadForSmoke(45);var cachedEntry=app.CityLoadFactsForSmoke();
        Check("repeat entry reuses view terrain routes and prepared textures",cachedEntry["view_cache_hit"].B&&cachedEntry["index_builds"].I==firstEntry["index_builds"].I&&cachedEntry["route_builds"].I==firstEntry["route_builds"].I&&cachedEntry["sync_texture_loads"].I==firstEntry["sync_texture_loads"].I&&cachedEntry["face_builds"].I==firstEntry["face_builds"].I,cachedEntry);
        Check("streets use natural ground without gravel strips",app.ValidateCityStreetGround(out J streetFacts),streetFacts);
        Check("fast city lines clip before GUI rotation",app.ValidateCityLineClipping());
        Check("painted map assets are bundled",app.ValidateCityArt(out J artFacts),artFacts);
        if(GameClient.Arg("-riverSmokeOnly")=="true"){
            app.GoStage(Stage.City,false);app.CitySmokeView(false);app.CityBeginRiverPixelFixture();yield return Capture("10-river-pixel-regression");app.CityEndRiverPixelFixture();
            int riverPlace=client.World["locations"].A.First(p=>p["name"].S=="克鲁伦河营地")["id"].I;
            yield return app.CityLoadForSmoke(riverPlace);app.CitySmokeRiverFocus();yield return Capture("11-kherlen-river-bank");yield break;
        }
        foreach(var place in client.World["locations"].A){
            yield return Call("/api/city?place="+place["id"].S,null,j=>app.City=j);
            if(!examples.ContainsKey(app.City["size"].I))examples[app.City["size"].I]=place["id"].I;
            Check("native routes match backend: "+place["name"].S,app.ValidateCityMap(out J facts),facts);
            Check("biome footprints and ground plane: "+place["name"].S,app.ValidateCityTerrain(out J terrainFacts),terrainFacts);
            Check("whole areas share cell properties: "+place["name"].S,app.ValidateCityAreas(out J areaFacts),areaFacts);
            Check("no bridges and fords stay at water level: "+place["name"].S,app.ValidateNoBridges(out J noBridgeFacts),noBridgeFacts);
            Check("curated palette and readable large scenery: "+place["name"].S,app.ValidateCityPalette(out J paletteFacts),paletteFacts);
            Check("raised map picking: "+place["name"].S,app.ValidateCityCamera(out J pickFacts),pickFacts);
            Check("each grid coordinate selects its own cell: "+place["name"].S,app.ValidateCityCoordinates(out J coordinateFacts),coordinateFacts);
        }
        Check("browsing preserves month",client.World["month"].I==month);
        if(!client.World["hero"]["created"].B){yield return Call("/api/hero",client.HeroBody("create","name","山河行客","sex","男","age",20,"place",45));yield return client.Refresh();}
        int home=client.World["hero"]["place"].I;app.SelectedPlace=home;app.GoStage(Stage.City);yield return new WaitForSecondsRealtime(1);
        yield return app.CityLoadForSmoke(home);
        foreach(var size in new[]{new Vector2Int(1280,800),new Vector2Int(1920,1080),new Vector2Int(1680,960)}){
            Screen.SetResolution(size.x,size.y,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.6f);
            Check("full client map, stable camera and floating UI: "+size,app.ValidateCityFullscreen(out J fullscreenFacts),fullscreenFacts);
            yield return Capture("07-city-fullscreen-"+size.x+"x"+size.y);
        }
        // A hidden acceptance window needs its swapchain resized before capture.
        yield return Capture("00-city-default-view");
        app.CitySmokeView(false);yield return Capture("01-city-overview");
        app.CityBeginCliffPixelFixture();yield return Capture("08-cliff-pixel-regression");app.CityEndCliffPixelFixture();
        app.CityBeginRiverPixelFixture();yield return Capture("10-river-pixel-regression");app.CityEndRiverPixelFixture();
        Check("real multi-cell cliff fixture",app.CitySmokeCliffFocus());yield return Capture("09-city-cliff-ground");
        app.CitySmokeView(true);yield return Capture("02-city-route");
        app.CitySmokeLots();yield return Capture("05-city-building-footprints");app.CitySmokeView(true);
        Check("camera inverse at every zoom",app.ValidateCityCamera(out J cameraFacts),cameraFacts);
        var hero=client.World["hero"];var gate=app.City["nodes"].A.First(n=>n["id"].S==app.City["entrance"].S);
        var target=app.City["nodes"].A.FirstOrDefault(n=>{var path=app.CityPath(hero["row"].I,hero["col"].I,n["row"].I,n["col"].I,out int cost);return path.Count>1&&cost<=hero["points"].I;});
        if(target!=null){
            var path=app.CityPath(hero["row"].I,hero["col"].I,target["row"].I,target["col"].I,out int cost);
            if(path.Count>1&&cost<=hero["points"].I){
                Check("city input accepts an affordable destination",app.CityWalkForSmoke(target["row"].I,target["col"].I));while(app.CityWalkingForSmoke)yield return null;
                Check("walk consumes displayed cost and preserves month",client.World["hero"]["points"].I==hero["points"].I-cost&&client.World["month"].I==month);
                app.LoadCity(home);yield return new WaitForSecondsRealtime(.5f);app.CitySmokeView(true);yield return Capture("03-city-arrival");
            }else Check("walk fixture has affordable path",false);
        }
        foreach(string theme in new[]{"capital","oasis","mountain","highland","forest","island"}){
            foreach(var place in client.World["locations"].A){
                J data=null;yield return Call("/api/city?place="+place["id"].S,null,j=>data=j);
                if(data["theme"].S!=theme)continue;
                app.LoadCity(place["id"].I);yield return new WaitForSecondsRealtime(.4f);app.CitySmokeView(false);yield return Capture("04-city-"+theme);break;
            }
        }
        foreach(int extent in new[]{20,30})if(examples.TryGetValue(extent,out int example)){
            app.LoadCity(example);yield return new WaitForSecondsRealtime(.5f);app.CitySmokeView(false);yield return Capture("06-city-size-"+extent);
        }
        int kherlen=client.World["locations"].A.First(p=>p["name"].S=="克鲁伦河营地")["id"].I;
        yield return app.CityLoadForSmoke(kherlen);app.CitySmokeRiverFocus();yield return Capture("11-kherlen-river-bank");
        Check("exactly fourteen approved sects",client.World["factions"].A.Count==14);
        foreach(string key in new[]{"shaolin","haidong","heishui"}){
            var sect=client.World["factions"].A.First(f=>f["key"].S==key);
            yield return app.CityLoadForSmoke(sect["place"].I);app.GoStage(Stage.City,false);app.CitySmokeView(false);
            Check("independent nine-cell seat: "+key,app.City["settlements"].A.Count(s=>s["kind"].S=="sect"&&s["faction"].I==sect["id"].I&&s["footprint"]["size"].I==3)==1);
            yield return Capture("12-sect-seat-"+key);
            app.Institution=new J();app.OpenInstitution(false,sect["id"].I);float deadline=Time.realtimeSinceStartup+15;
            while((!app.Institution.Has||app.Institution["key"].S!=key)&&Time.realtimeSinceStartup<deadline)yield return null;
            Check("sect detail and player options: "+key,app.Institution["key"].S==key&&app.Institution["player_options"].A.Count>0);
            yield return Capture("13-sect-detail-"+key);
        }
        app.GoStage(Stage.City,false);app.LoadCity(home);yield return new WaitForSecondsRealtime(.4f);app.CitySmokeView(false);
        Check("no model calls",client.World["ai"]["calls"].N==calls);
    }
    IEnumerator WalkFlow(){
        if(client.World["battle_active"].B)yield return app.CityEndBattleForSmoke();
        if(!client.World["hero"]["created"].B){yield return Call("/api/hero",client.HeroBody("create","name","行路试客","sex","男","age",20,"place",45));yield return client.Refresh();}
        if(client.World["hero"]["points"].I<25){yield return Call("/api/hero",client.HeroBody("end_turn"));yield return client.Refresh();}
        int place=client.World["hero"]["place"].I;app.GoStage(Stage.City);yield return app.CityLoadForSmoke(place);
        while(!app.CityWarmupCompleteForSmoke)yield return null;
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
        var target=route[route.Count-1];app.CityFocusForWalkSmoke(origin,target);
        int refreshes=client.StateRefreshRequests;float maxLanding=0,walkingStarted=Time.realtimeSinceStartup;
        Check("destination starts a walk",app.CityWalkForSmoke(target.x,target.y));
        Check("accepting the destination preserves the starting tile",client.World["hero"]["row"].I==origin.x&&client.World["hero"]["col"].I==origin.y);
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
        Check("hero and minimap interpolate between adjacent cells",visual&&interiorFrames>0&&captured,J.O("interior_frames",interiorFrames));
        Check("walking has only a short landing pause",maxLanding<.15f,J.O("longest_landing_seconds",maxLanding,"elapsed_seconds",Time.realtimeSinceStartup-walkingStarted,"steps",seen));
        Check("walking does not refresh the whole world per tile",client.StateRefreshRequests-refreshes<=1,J.O("world_refreshes",client.StateRefreshRequests-refreshes));
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
        app.GoStage(Stage.City);yield return app.CityLoadForSmoke(place);yield return new WaitForSecondsRealtime(.5f);
        var keyboardStart=client.World["hero"];var keyboardFrom=new Vector2Int(keyboardStart["row"].I,keyboardStart["col"].I);
        Vector2Int keyboardDirection=Vector2Int.zero;
        foreach(var direction in new[]{Vector2Int.up,Vector2Int.down,Vector2Int.left,Vector2Int.right}){
            var next=keyboardFrom+direction;var third=keyboardFrom+direction*3;
            var path=app.CityPath(keyboardFrom.x,keyboardFrom.y,third.x,third.y,out int amount);
            if(path.Count==4&&path[1]==next&&path[2]==keyboardFrom+direction*2&&amount<=keyboardStart["points"].I){keyboardDirection=direction;break;}
        }
        Check("keyboard fixture has three affordable straight cells",keyboardDirection!=Vector2Int.zero);
        if(keyboardDirection!=Vector2Int.zero){
            refreshes=client.StateRefreshRequests;revision=keyboardStart["revision"].I;
            app.CityHoldDirectionForSmoke(keyboardDirection);deadline=Time.realtimeSinceStartup+10;
            while(app.CityWalkingForSmoke&&client.World["hero"]["revision"].I<revision+3&&Time.realtimeSinceStartup<deadline)yield return null;
            app.CityHoldDirectionForSmoke(Vector2Int.zero);
            while(app.CityWalkingForSmoke&&Time.realtimeSinceStartup<deadline)yield return null;
            var keyboardEnd=new Vector2Int(client.World["hero"]["row"].I,client.World["hero"]["col"].I);
            Check("held keyboard direction walks continuously and stops on release",keyboardEnd==keyboardFrom+keyboardDirection*3&&client.World["hero"]["revision"].I==revision+3&&client.StateRefreshRequests-refreshes<=1);
        }
        Check("city walking preserves month and makes no model calls",client.World["month"].I==month&&client.World["ai"]["calls"].N==calls);
    }
    void Finish(){File.WriteAllText(Path.Combine(output,"acceptance.json"),J.O("unity",Application.unityVersion,"checks",new J(checks),"passed",checks.All(c=>c["pass"].B)).ToString());Application.Quit(checks.All(c=>c["pass"].B)?0:1);}
}
public partial class JianghuApp {
    bool cityWalkSmokeInput;
    public bool ValidateCityFullscreen(out J facts){
        Rect viewport=CityViewport;float scale=Mathf.Min(Screen.width/1680f,Screen.height/960f);
        var offset=new Vector2((Screen.width-1680*scale)/2,(Screen.height-960*scale)/2);
        var min=viewport.min*scale+offset;var max=viewport.max*scale+offset;
        bool covers=Vector2.Distance(min,Vector2.zero)<.01f&&Vector2.Distance(max,new Vector2(Screen.width,Screen.height))<.01f;
        bool wasOpen=rosterOpen;rosterOpen=true;CityGeometry(CityViewport,out float openWidth,out float openHeight,out var openCenter);
        var overlays=new[]{TopBar,LeftPanel,PlaceCard,CityHeaderRect,CityToolbarRect,CityOverviewRect,new Rect(740,812,200,150),new Rect(1520,780,160,180)};
        int blocked=overlays.Count(rect=>!CityMapPointerAllowed(rect.center));
        bool margins=CityMapPointerAllowed(new Vector2(277,400))&&CityMapPointerAllowed(new Vector2(1410,400))&&CityMapPointerAllowed(new Vector2(1100,820))
            &&CityMapPointerAllowed(new Vector2(1100,764))&&CityMapPointerAllowed(new Vector2(393,675));
        rosterOpen=false;CityGeometry(CityViewport,out float closedWidth,out float closedHeight,out var closedCenter);
        bool stable=openWidth==closedWidth&&openHeight==closedHeight&&openCenter==closedCenter;rosterOpen=wasOpen;
        float oldZoom=cityZoom;var oldPan=cityPan;var anchor=new Vector2(viewport.width*.67f,viewport.height*.42f);
        var point=new Vector2Int(City["size"].I/2,City["size"].I/2);CityGeometry(viewport,out float tw,out float th,out var center);
        var before=CitySurface(point,tw,th,center);CityZoom(cityZoom*1.1f,anchor);float ratio=cityZoom/oldZoom;
        CityGeometry(viewport,out tw,out th,out center);float zoomError=Vector2.Distance(CitySurface(point,tw,th,center),anchor+(before-anchor)*ratio);
        cityZoom=oldZoom;cityPan=oldPan;
        facts=J.O("screen_width",Screen.width,"screen_height",Screen.height,"map_pixel_min",min.ToString(),"map_pixel_max",max.ToString(),"covers_client",covers,"roster_camera_stable",stable,"overlays_blocked",blocked,"overlays_checked",overlays.Length,"expanded_map_margins_interactive",margins,"zoom_anchor_error",zoomError);
        return covers&&stable&&blocked==overlays.Length&&margins&&zoomError<.01f;
    }
    public IEnumerator CityLoadForSmoke(int place){yield return LoadCityRoutine(place);}
    public bool CityWarmupCompleteForSmoke=>cityWarmupComplete;
    public bool ValidateCityLineClipping(){
        var bounds=new Rect(0,0,10,10);var a=new Vector2(-5,4);var b=new Vector2(15,4);
        if(!CityClipLine(ref a,ref b,bounds)||a!=new Vector2(0,4)||b!=new Vector2(10,4))return false;
        a=new Vector2(5,-2);b=new Vector2(5,20);if(!CityClipLine(ref a,ref b,bounds)||a!=new Vector2(5,0)||b!=new Vector2(5,10))return false;
        a=new Vector2(-5,-5);b=new Vector2(-1,-1);if(CityClipLine(ref a,ref b,bounds))return false;
        a=new Vector2(-1,-1);b=new Vector2(2,2);return CityClipLine(ref a,ref b,bounds)&&a==Vector2.zero&&b==new Vector2(2,2);
    }
    public J CityLoadFactsForSmoke()=>J.O("load_ms",cityLastLoadMs,"prepare_ms",cityLastPrepareMs,"view_cache_hit",cityLastViewCacheHit,"index_builds",cityIndexBuilds,"route_builds",cityRouteBuilds,"sync_texture_loads",citySyncLoads,"face_builds",cityFaceBuilds);
    public bool ValidateCityStreetGround(out J facts){int streets=0,gravel=0;foreach(var tile in City["terrain_tiles"].A)if(tile["size"].I==1&&City["rows"][tile["row"].I].S[tile["col"].I]=='+'){streets++;if(CityGroundAsset(City,tile)=="11-gravel-land")gravel++;}facts=J.O("street_cells",streets,"gravel_street_cells",gravel);return streets>0&&gravel==0;}
    public string CityMovementReasonForSmoke=>CityMovementBlockReason;
    public bool CityWalkForSmoke(int row,int col)=>TryCityWalk(new Vector2Int(row,col));
    public bool CityWalkingForSmoke=>cityWalking;
    public void CityStopForSmoke()=>StopCityWalk();
    public void CityHoldDirectionForSmoke(Vector2Int direction){
        cityWalkSmokeInput=true;cityHeldDirection=direction;
        if(direction==Vector2Int.zero){StopCityWalk();return;}
        if(!cityWalking)TryCityWalk(new Vector2Int(Hero["row"].I,Hero["col"].I)+direction,true);
        else{cityWalkKeyboard=true;cityWalkCancelled=false;}
    }
    public void CityFocusForWalkSmoke(Vector2Int from,Vector2Int to){cityZoom=3;CityFocus(new Vector2Int((from.x+to.x)/2,(from.y+to.y)/2));}
    public J CityWalkVisualForSmoke(){
        CityGeometry(CityViewport,out float tw,out float th,out var center);
        var a=CitySurface(cityStepFrom,tw,th,center);var b=CitySurface(cityStepTo,tw,th,center);var p=CityHeroSurface(tw,th,center);
        var miniA=CityXY(cityStepFrom,4,2,Vector2.zero);var miniB=CityXY(cityStepTo,4,2,Vector2.zero);var mini=CityHeroOverview(4,Vector2.zero);
        bool interior=cityStepAnimating&&cityStepProgress>.05f&&cityStepProgress<.95f;
        bool followsRoute=cityWalkIndex>0&&cityWalkIndex<cityWalkRoute.Count&&cityWalkRoute[cityWalkIndex-1]==cityStepFrom&&cityWalkRoute[cityWalkIndex]==cityStepTo;
        return J.O("interior",interior,"landing_seconds",cityStepAnimating?Mathf.Max(0,Time.realtimeSinceStartup-cityStepStartedAt-CityWalkStepSeconds):0,"on_segment",followsRoute&&Vector2.Distance(p,Vector2.Lerp(a,b,cityStepProgress))<.01f&&Vector2.Distance(mini,Vector2.Lerp(miniA,miniB,cityStepProgress))<.01f&&Vector2.Distance(p,a)>.01f&&Vector2.Distance(p,b)>.01f);
    }
    public IEnumerator CityEndBattleForSmoke()=>EndCityBattle();
    public bool ValidateCityCoordinates(out J facts){
        var labels=new HashSet<string>();var failures=new List<J>();int count=0;float oldZoom=cityZoom;var oldPan=cityPan;
        foreach(float zoom in new[]{.75f,1,2f,4f}){
            cityZoom=zoom;cityPan=Vector2.zero;CityGeometry(CityViewport,out float tw,out float th,out var center);
            for(int row=0;row<City["size"].I;row++)for(int col=0;col<City["size"].I;col++){
                var at=new Vector2Int(row,col);string label=CityCoordinate(at);labels.Add(label);count++;
                var picked=tw>=24?CityCoordinatePick(CityCoordinateRect(at,tw,th,center).center,tw,th,center):CitySurfacePick(CitySurface(at,tw,th,center),tw,th,center);
                bool correct=tw>=24?picked==at:CityInside(picked)&&picked.x+picked.y>=at.x+at.y;
                if(!correct)failures.Add(J.O("coordinate",label,"picked",CityCoordinate(picked),"zoom",zoom));
            }
        }
        cityZoom=oldZoom;cityPan=oldPan;facts=J.O("unique_labels",labels.Count,"cells",City["size"].I*City["size"].I,"picks",count,"failures",new J(failures));
        return labels.Count==City["size"].I*City["size"].I&&failures.Count==0;
    }
    public bool ValidateCityArt(out J facts){
        var pack=Resources.Load<TextAsset>("CityBiomes/art-pack");J manifest=pack!=null?J.Parse(pack.text):new J();
        var names=manifest["assets"].A.Select(a=>a["id"].S).ToArray();
        var missing=names.Where(n=>CityArt(n)==null).Select(n=>new J(n)).ToList();
        bool current=manifest["pack"].S=="landscape-mix-20261027"&&manifest["source_count"].I==43&&manifest["placement_version"].I==7&&manifest["original_terrain_count"].I==32&&!manifest["legacy_assets"].B;
        bool calibrated=names.All(n=>CityArt(n)!=null&&CityArt(n).width==512&&CityArt(n).height==512);
        var settlements=Resources.Load<TextAsset>("CitySettlements/art-pack");J settlementPack=settlements!=null?J.Parse(settlements.text):new J();
        var settlementNames=new[]{"01-village-1cell","02-town-4cell","03-city-9cell"};
        bool settlementOk=settlementPack["pack"].S=="settlement-tiles-1-4-9-20261026"&&settlementPack["source_count"].I==3&&settlementNames.All(n=>CitySettlementArt(n)!=null&&CitySettlementArt(n).width==1024);
        facts=J.O("resources_checked",names.Length,"missing",new J(missing),"pack",manifest["pack"],"source_count",manifest["source_count"],"calibrated_512px_tiles",calibrated,"settlement_pack",settlementPack["pack"],"settlements_checked",3,"settlements_passed",settlementOk);return names.Length==43&&missing.Count==0&&current&&calibrated&&settlementOk;
    }
    public bool ValidateCityPalette(out J facts){
        var manifest=J.Parse(Resources.Load<TextAsset>("CityBiomes/art-pack").text);
        var sizes=manifest["assets"].A.ToDictionary(a=>a["id"].S,a=>a["size"].I);var failures=new List<J>();var names=new HashSet<string>();int complex=0;
        foreach(var tile in City["terrain_tiles"].A){string name=tile["asset"].S;int extent=tile["size"].I;names.Add(name);if(extent>1)complex+=extent*extent;if(!sizes.TryGetValue(name,out int expected)||extent!=expected)failures.Add(J.O("asset",name,"size",extent));}
        double fraction=(double)complex/(citySize*citySize);
        facts=J.O("distinct_assets",names.Count,"complex_cells",complex,"complex_fraction",fraction,"failures",new J(failures));
        return City["art_pack"].S=="landscape-mix-20261027"&&(citySize==20||citySize==30)&&fraction<=.35&&failures.Count==0;
    }
    public bool ValidateCityMap(out J facts){
        originRow=originCol=chosenRow=chosenCol=-1;cityPathKey="";EnsureCityRoutes();int checkedNodes=0;var failures=new List<J>();
        var gate=City["nodes"].A.First(n=>n["id"].S==City["entrance"].S);
        foreach(var node in City["nodes"].A){
            var native=CityPath(gate["row"].I,gate["col"].I,node["row"].I,node["col"].I,out int cost);checkedNodes++;
            var q=new Vector2Int(node["row"].I,node["col"].I);
            var origin=CityOrigin();CityPath(origin.x,origin.y,q.x,q.y,out int originCost);
            if(native.Count==0||cost!=node["walk_cost"].I||!cityCosts.TryGetValue(q,out int overlay)||overlay!=originCost)failures.Add(J.O("node",node["name"],"native",cost,"server",node["walk_cost"]));
        }
        int one=0,four=0,nine=0;var used=new HashSet<Vector2Int>();CityGeometry(CityViewport,out float tw,out float th,out var center);
        foreach(var node in CitySites){
            int size=node["footprint"]["size"].I;var corner=CityLotCorner(node);var entrance=new Vector2Int(node["row"].I,node["col"].I);
            if(size<1||size>3){failures.Add(J.O("invalid_footprint",node["id"]));continue;}
            if(size==1)one++;else if(size==2)four++;else nine++;
            string expected=size==3?"03-city-9cell":size==2?"02-town-4cell":"01-village-1cell";
            if(node["settlement_asset"].S!=expected||CitySettlementArt(expected)==null)failures.Add(J.O("wrong_settlement_art",node["id"]));
            var average=Vector2.zero;bool includesEntrance=false;
            for(int row=0;row<size;row++)for(int col=0;col<size;col++){
                var q=corner+new Vector2Int(row,col);average+=CitySurface(q,tw,th,center);includesEntrance|=q==entrance;
                if(!CityInside(q)||!used.Add(q))failures.Add(J.O("invalid_lot_cell",node["id"],"row",q.x,"col",q.y));
                else if(size>1&&(CityBlocked(CityTile(q.x,q.y))||CityTile(q.x,q.y)=='='))failures.Add(J.O("lot_covers_barrier",node["id"]));
            }
            if(!includesEntrance||Vector2.Distance(average/(size*size),CityBuildingPoint(node,tw,th,center))>.01f)failures.Add(J.O("uncentred_building",node["id"]));
        }
        int sects=CitySites.Count(n=>n["kind"].S=="sect");
        if(City["version"].I>=4&&(nine!=1+sects||four!=1||one!=2||CitySites.Count!=4+sects))failures.Add(J.O("wrong_settlement_count",CitySites.Count));
        foreach(var seat in CitySites.Where(n=>n["kind"].S=="sect"))if(seat["footprint"]["size"].I!=3||!seat["faction"].Has)failures.Add(J.O("invalid_sect_seat",seat["id"]));
        int blocked=0;for(int row=0;row<City["size"].I;row++)for(int col=0;col<City["size"].I;col++)if(CityBlocked(CityTile(row,col))){blocked++;if(cityCosts.ContainsKey(new Vector2Int(row,col)))failures.Add(J.O("blocked_row",row,"col",col));}
        facts=J.O("facilities",checkedNodes,"one_cell_buildings",one,"four_cell_buildings",four,"nine_cell_buildings",nine,"blocked_tiles",blocked,"failures",new J(failures));return failures.Count==0;
    }
    public bool ValidateCityAreas(out J facts){
        int checkedCells=0;var failures=new List<J>();
        foreach(var area in City["areas"].A){int row=area["row"].I,col=area["col"].I,extent=area["size"].I;
            var reference=CityAreaAt(new Vector2Int(row,col));
            for(int r=0;r<extent;r++)for(int c=0;c<extent;c++){
                var q=new Vector2Int(row+r,col+c);checkedCells++;
                if(!ReferenceEquals(CityAreaAt(q),reference)||CityLevel(q)!=area["elevation"].I||CityTile(q.x,q.y).ToString()!=area["terrain"].S||CityBlocked(CityTile(q.x,q.y))==area["passable"].B||CityStepCost(CityTile(q.x,q.y))!=area["move_cost"].I)failures.Add(J.O("inconsistent_area",area["id"],"cell",q.ToString()));
                if(area["kind"].S!="landscape"&&(!cityLots.TryGetValue(q,out var site)||site["id"].S!=area["id"].S))failures.Add(J.O("split_settlement",area["id"]));
            }
        }
        facts=J.O("areas",City["areas"].A.Count,"checked_cells",checkedCells,"visible_settlements",CitySites.Count,"failures",new J(failures));return checkedCells==citySize*citySize&&CitySites.Count==4+CitySites.Count(n=>n["kind"].S=="sect")&&failures.Count==0;
    }
    public bool ValidateCityTerrain(out J facts){
        int size=City["size"].I,min=99,max=0,steps=0,water=0;var failures=new List<J>();
        var snapshot=(int[,])cityLevels.Clone();BuildCityTerrain();
        for(int row=0;row<size;row++)for(int col=0;col<size;col++){
            var q=new Vector2Int(row,col);int level=CityLevel(q);char t=CityTile(row,col);min=Mathf.Min(min,level);max=Mathf.Max(max,level);
            if(level!=snapshot[row,col])failures.Add(J.O("unstable_height",q.ToString()));
            if(t=='~'){water++;if(level!=0)failures.Add(J.O("raised_water",q.ToString()));}
            if(CityBlocked(t))continue;
            foreach(var step in CitySteps){var next=q+step;if(!CityInside(next)||CityBlocked(CityTile(next.x,next.y)))continue;
                int difference=Mathf.Abs(level-CityLevel(next));if(difference==1)steps++;if(difference>1)failures.Add(J.O("unwalkable_step",q.ToString()));}
        }
        foreach(var node in CitySites){var corner=CityLotCorner(node);int extent=CityLotSize(node);
            for(int row=0;row<extent;row++)for(int col=0;col<extent;col++)if(CityLevel(corner+new Vector2Int(row,col))!=CityLevel(corner))failures.Add(J.O("sloping_foundation",node["id"]));}
        facts=J.O("min_level",min,"max_level",max,"walkable_stairs",steps/2,"water_cells",water,"failures",new J(failures));
        var occupied=new HashSet<Vector2Int>();int four=0;
        foreach(var tile in City["terrain_tiles"].A){int extent=tile["size"].I;if(extent==2)four++;
            if(extent!=1&&extent!=2)failures.Add(J.O("invalid_biome_size",tile["asset"]));
            for(int row=0;row<extent;row++)for(int col=0;col<extent;col++){
                var q=new Vector2Int(tile["row"].I+row,tile["col"].I+col);
                if(!CityInside(q)||!occupied.Add(q)||extent==2&&cityLots.ContainsKey(q))failures.Add(J.O("overlapping_biome",tile["asset"]));
                if(CityLevel(q)!=CityLevel(new Vector2Int(tile["row"].I,tile["col"].I)))failures.Add(J.O("sloping_biome",tile["asset"]));
            }
        }
        facts["covered_cells"]=new J(occupied.Count);facts["four_cell_biomes"]=new J(four);
        return occupied.Count==size*size&&(size==20||size==30)&&max>min&&steps>0&&failures.Count==0;
    }
    public bool ValidateCityCamera(out J facts){float oldZoom=cityZoom;var oldPan=cityPan;int count=0,raised=0,faces=0;bool ok=true;
        foreach(float zoom in new[]{.75f,1,1.65f,2.8f}){cityZoom=zoom;cityPan=new Vector2(79,-51);CityGeometry(CityViewport,out float tw,out float th,out var center);
            for(int row=0;row<City["size"].I;row++)for(int col=0;col<City["size"].I;col++){
                var p=new Vector2Int(row,col);count++;ok&=CityPick(CityXY(p,tw,th,center),tw,th,center)==p;
                var top=CitySurface(p,tw,th,center);var picked=CitySurfacePick(top,tw,th,center);
                // A nearer terrace may occlude a low tile. Whatever is visible
                // at that point must contain it, and must be in front of it.
                var d=top-CitySurface(picked,tw,th,center);raised++;
                ok&=CityInside(picked)&&picked.x+picked.y>=p.x+p.y;
                // A low top can also be hidden by the side of a nearer block.
                if(picked==p)ok&=Mathf.Abs(d.x)/tw+Mathf.Abs(d.y)/th<=1.002f;
                for(int side=0;side<2;side++){
                    var next=p+new Vector2Int(side==0?1:0,side==1?1:0);if(CityInside(next))continue;
                    int level=CityGroundLevel(p);var ground=CityXY(p,tw,th,center)-new Vector2(0,level*tw*CityRise);
                    var face=ground+new Vector2((side==0?-1:1)*tw*.5f,th*.5f+(level+2)*tw*CityRise*.5f);
                    var front=CitySurfacePick(face,tw,th,center);faces++;ok&=CityInside(front)&&front.x+front.y>=p.x+p.y;
                }
            }
        }
        cityZoom=oldZoom;cityPan=oldPan;facts=J.O("tile_centres_checked",count,"raised_surface_picks",raised,"block_side_picks",faces);return ok;
    }
    public void CitySmokeView(bool detail){
        rosterOpen=false;cityZoom=detail?4f:1;cityPan=Vector2.zero;cityReach=detail;cityLabels=true;cityGrid=detail;cityPathKey="";EnsureCityRoutes();
        chosenRow=chosenCol=-1;
        if(detail){var target=CitySites.FirstOrDefault(n=>CityHasService(n,"market"));if(target!=null){chosenRow=target["row"].I;chosenCol=target["col"].I;CityFocus(new Vector2Int(chosenRow,chosenCol));}}
    }
    public void CitySmokeLots(){
        CitySmokeView(false);cityGrid=false;cityLabels=false;cityZoom=2.2f;
        var node=CitySites.OrderByDescending(n=>CityLotSize(n)).FirstOrDefault();
        if(node!=null){chosenRow=node["row"].I;chosenCol=node["col"].I;CityGeometry(CityViewport,out float tw,out float th,out var center);cityPan+=new Vector2(CityViewport.width/2,CityViewport.height/2)-CityBuildingPoint(node,tw,th,center);}
    }
}
}
