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
        double calls=client.World["ai"]["calls"].N;int month=client.World["month"].I;
        if(client.World["battle_active"].B){
            var before=client.World["hero"];app.SelectedPlace=before["place"].I;app.Go(1);yield return new WaitForSecondsRealtime(1);
            yield return Capture("00-unfinished-battle-movement-notice");
            Check("unfinished battle explains the movement lock",app.CityMovementReasonForSmoke.Contains("切磋"));
            Check("blocked input preserves the hero",!app.CityWalkForSmoke(before["row"].I-1,before["col"].I)&&client.World["hero"]["revision"].I==before["revision"].I);
            yield return app.CityEndBattleForSmoke();
            Check("city surrender releases the movement lock",!client.World["battle_active"].B&&app.CityMovementReasonForSmoke.Length==0);
        }
        Check("painted map assets are bundled",app.ValidateCityArt(out J artFacts),artFacts);
        foreach(var place in client.World["locations"].A){
            yield return Call("/api/city?place="+place["id"].S,null,j=>app.City=j);
            Check("native routes match backend: "+place["name"].S,app.ValidateCityMap(out J facts),facts);
            Check("terraces and foundations: "+place["name"].S,app.ValidateCityTerrain(out J terrainFacts),terrainFacts);
            Check("raised map picking: "+place["name"].S,app.ValidateCityCamera(out J pickFacts),pickFacts);
            Check("each grid coordinate selects its own cell: "+place["name"].S,app.ValidateCityCoordinates(out J coordinateFacts),coordinateFacts);
        }
        Check("browsing preserves month",client.World["month"].I==month);
        if(!client.World["hero"]["created"].B){yield return Call("/api/hero",client.HeroBody("create","name","山河行客","sex","男","age",20,"place",45));yield return client.Refresh();}
        int home=client.World["hero"]["place"].I;app.SelectedPlace=home;app.Go(1);yield return new WaitForSecondsRealtime(1);
        app.CitySmokeView(false);yield return Capture("01-city-overview");
        app.CitySmokeView(true);yield return Capture("02-city-route");
        app.CitySmokeLots();yield return Capture("05-city-building-footprints");app.CitySmokeView(true);
        Check("camera inverse at every zoom",app.ValidateCityCamera(out J cameraFacts),cameraFacts);
        var hero=client.World["hero"];var gate=app.City["nodes"].A.First(n=>n["id"].S==app.City["entrance"].S);
        var target=app.City["nodes"].A.FirstOrDefault(n=>n["row"].I!=hero["row"].I||n["col"].I!=hero["col"].I);
        if(target!=null){
            var path=app.CityPath(hero["row"].I,hero["col"].I,target["row"].I,target["col"].I,out int cost);
            if(path.Count>1&&cost<=hero["points"].I){
                Check("city input accepts an affordable destination",app.CityWalkForSmoke(target["row"].I,target["col"].I));while(client.Busy)yield return null;
                Check("walk consumes displayed cost and preserves month",client.World["hero"]["points"].I==hero["points"].I-cost&&client.World["month"].I==month);
                app.LoadCity(home);yield return new WaitForSecondsRealtime(.5f);app.CitySmokeView(true);yield return Capture("03-city-arrival");
            }else Check("walk fixture has affordable path",false);
        }
        foreach(string theme in new[]{"capital","oasis","mountain"}){
            foreach(var place in client.World["locations"].A){
                J data=null;yield return Call("/api/city?place="+place["id"].S,null,j=>data=j);
                if(data["theme"].S!=theme)continue;
                app.LoadCity(place["id"].I);yield return new WaitForSecondsRealtime(.4f);app.CitySmokeView(false);yield return Capture("04-city-"+theme);break;
            }
        }
        app.LoadCity(home);yield return new WaitForSecondsRealtime(.4f);app.CitySmokeView(false);
        Check("no model calls",client.World["ai"]["calls"].N==calls);
    }
    void Finish(){File.WriteAllText(Path.Combine(output,"acceptance.json"),J.O("unity",Application.unityVersion,"checks",new J(checks),"passed",checks.All(c=>c["pass"].B)).ToString());Application.Quit(checks.All(c=>c["pass"].B)?0:1);}
}
public partial class JianghuApp {
    public string CityMovementReasonForSmoke=>CityMovementBlockReason;
    public bool CityWalkForSmoke(int row,int col)=>TryCityWalk(new Vector2Int(row,col));
    public IEnumerator CityEndBattleForSmoke()=>EndCityBattle();
    public bool ValidateCityCoordinates(out J facts){
        var labels=new HashSet<string>();var failures=new List<J>();int count=0;float oldZoom=cityZoom;var oldPan=cityPan;
        foreach(float zoom in new[]{.75f,1,1.55f,2.8f}){
            cityZoom=zoom;cityPan=Vector2.zero;CityGeometry(CityViewport,out float tw,out float th,out var center);
            for(int row=0;row<City["size"].I;row++)for(int col=0;col<City["size"].I;col++){
                var at=new Vector2Int(row,col);string label=CityCoordinate(at);labels.Add(label);count++;
                var picked=CityCoordinatePick(CityCoordinateRect(at,tw,th,center).center,tw,th,center);
                if(picked!=at)failures.Add(J.O("coordinate",label,"picked",CityCoordinate(picked),"zoom",zoom));
            }
        }
        cityZoom=oldZoom;cityPan=oldPan;facts=J.O("unique_labels",labels.Count,"cells",City["size"].I*City["size"].I,"picks",count,"failures",new J(failures));
        return labels.Count==City["size"].I*City["size"].I&&failures.Count==0;
    }
    public bool ValidateCityArt(out J facts){
        var names=new[]{"gate","government","palace","market","inn","clinic","workshop","training","school","residence","granary","garden","temple","dock","faction"}.Select(k=>"building-"+k)
            .Concat(new[]{"selection","hero-ring","reachable","route-dot","island-shadow","bridge-axis-0","bridge-axis-1","wall-axis-0","wall-axis-1","building-parcel"})
            .Concat(Enumerable.Range(0,3).SelectMany(v=>new[]{"grass-","street-","water-","field-","forest-","prop-forest-","prop-grass-"}.Select(k=>k+v)))
            .Concat(Enumerable.Range(0,16).SelectMany(v=>new[]{"road-"+v,"bridge-"+v})).ToArray();
        var missing=names.Where(n=>CityArt(n)==null).Select(n=>new J(n)).ToList();
        var pack=Resources.Load<TextAsset>("City/art-pack");J manifest=pack!=null?J.Parse(pack.text):new J();
        bool current=manifest["pack"].S=="song-20261026"&&manifest["source_count"].I==21&&manifest["placement_version"].I==2&&manifest["building_alignment"].S=="visible_image_centre";
        bool calibrated=names.Where(n=>n.StartsWith("building-")).All(n=>CityArt(n)!=null&&CityArt(n).width==512&&CityArt(n).height==512);
        facts=J.O("resources_checked",names.Length,"missing",new J(missing),"pack",manifest["pack"],"source_count",manifest["source_count"],"placement_version",manifest["placement_version"],"building_alignment",manifest["building_alignment"],"calibrated_512px_buildings",calibrated);return missing.Count==0&&current&&calibrated;
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
        int one=0,four=0;var used=new HashSet<Vector2Int>();CityGeometry(CityViewport,out float tw,out float th,out var center);
        foreach(var node in City["nodes"].A){
            int size=node["footprint"]["size"].I;var corner=CityLotCorner(node);var entrance=new Vector2Int(node["row"].I,node["col"].I);
            if(size!=1&&size!=2){failures.Add(J.O("invalid_footprint",node["id"]));continue;}
            if(size==1)one++;else four++;
            var average=Vector2.zero;bool includesEntrance=false;
            for(int row=0;row<size;row++)for(int col=0;col<size;col++){
                var q=corner+new Vector2Int(row,col);average+=CitySurface(q,tw,th,center);includesEntrance|=q==entrance;
                if(!CityInside(q)||!used.Add(q))failures.Add(J.O("invalid_lot_cell",node["id"],"row",q.x,"col",q.y));
                else if(size==2&&(CityBlocked(CityTile(q.x,q.y))||CityTile(q.x,q.y)=='='))failures.Add(J.O("lot_covers_barrier",node["id"]));
            }
            if(!includesEntrance||Vector2.Distance(average/(size*size),CityBuildingPoint(node,tw,th,center))>.01f)failures.Add(J.O("uncentred_building",node["id"]));
        }
        int blocked=0;for(int row=0;row<City["size"].I;row++)for(int col=0;col<City["size"].I;col++)if(CityBlocked(CityTile(row,col))){blocked++;if(cityCosts.ContainsKey(new Vector2Int(row,col)))failures.Add(J.O("blocked_row",row,"col",col));}
        facts=J.O("facilities",checkedNodes,"one_cell_buildings",one,"four_cell_buildings",four,"blocked_tiles",blocked,"failures",new J(failures));return failures.Count==0;
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
        foreach(var node in City["nodes"].A){var corner=CityLotCorner(node);int extent=CityLotSize(node);
            for(int row=0;row<extent;row++)for(int col=0;col<extent;col++)if(CityLevel(corner+new Vector2Int(row,col))!=CityLevel(corner))failures.Add(J.O("sloping_foundation",node["id"]));}
        facts=J.O("min_level",min,"max_level",max,"walkable_stairs",steps/2,"water_cells",water,"failures",new J(failures));
        return max>min&&steps>0&&failures.Count==0;
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
                    faces++;ok&=CitySurfacePick(face,tw,th,center)==p;
                }
            }
        }
        cityZoom=oldZoom;cityPan=oldPan;facts=J.O("tile_centres_checked",count,"raised_surface_picks",raised,"block_side_picks",faces);return ok;
    }
    public void CitySmokeView(bool detail){
        rosterOpen=false;cityZoom=detail?1.55f:1;cityPan=Vector2.zero;cityReach=detail;cityLabels=true;cityGrid=true;cityPathKey="";EnsureCityRoutes();
        chosenRow=chosenCol=-1;
        if(detail){var target=City["nodes"].A.FirstOrDefault(n=>n["kind"].S=="market");if(target!=null){chosenRow=target["row"].I;chosenCol=target["col"].I;CityFocus(new Vector2Int(chosenRow,chosenCol));}}
    }
    public void CitySmokeLots(){
        CitySmokeView(false);cityGrid=true;cityZoom=1.35f;
        var node=City["nodes"].A.FirstOrDefault(n=>CityLotSize(n)==2);
        if(node!=null){chosenRow=node["row"].I;chosenCol=node["col"].I;CityGeometry(CityViewport,out float tw,out float th,out var center);cityPan+=new Vector2(CityViewport.width/2,CityViewport.height/2)-CityBuildingPoint(node,tw,th,center);}
    }
}
}
