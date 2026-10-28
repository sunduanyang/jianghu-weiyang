using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
// Opt-in acceptance for the 舆图 / 长卷 views on a copied save:
//   Unity -batchmode is not enough (IMGUI needs a frame), so run the built game or the editor with
//   -saveDir <copy> -cityAtlasSmokeDir <output>. Browsing must never advance the world.
public class CityAtlasSmoke : MonoBehaviour {
    string output;GameClient client;JianghuApp app;
    readonly List<J> checks=new List<J>();readonly List<string> errors=new List<string>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Boot(){
        if(GameClient.Arg("-cityAtlasSmokeDir").Length==0)return;
        if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("City atlas acceptance requires an isolated -saveDir");Application.Quit(1);return;}
        new GameObject("City atlas and scroll acceptance").AddComponent<CityAtlasSmoke>();
    }
    void OnEnable(){Application.logMessageReceived+=Log;}
    void OnDisable(){Application.logMessageReceived-=Log;}
    void Log(string message,string stack,LogType type){if(type==LogType.Exception||type==LogType.Error)errors.Add(message);}
    void Check(string name,bool pass,J facts=null){checks.Add(J.O("check",name,"pass",pass,"facts",facts??new J()));}
    IEnumerator Capture(string name){
        bool rendered=false;int colors=0;
        for(int attempt=0;attempt<4&&!rendered;attempt++){
            yield return new WaitForSecondsRealtime(.6f);yield return new WaitForEndOfFrame();
            var texture=ScreenCapture.CaptureScreenshotAsTexture();if(texture==null)continue;
            var sampled=new HashSet<int>();var pixels=texture.GetPixels32();
            for(int i=0;i<pixels.Length;i+=101){var c=pixels[i];sampled.Add((c.r<<16)|(c.g<<8)|c.b);}
            colors=sampled.Count;rendered=colors>64;
            if(rendered)File.WriteAllBytes(Path.Combine(output,name+".png"),texture.EncodeToPNG());Destroy(texture);
        }
        Check("capture "+name,rendered,J.O("sampled_colors",colors));
    }
    IEnumerator Start(){
        output=GameClient.Arg("-cityAtlasSmokeDir");Directory.CreateDirectory(output);
        float deadline=Time.realtimeSinceStartup+100;
        while(Time.realtimeSinceStartup<deadline){client=FindAnyObjectByType<GameClient>();app=FindAnyObjectByType<JianghuApp>();if(client!=null&&client.Ready&&client.World.Has)break;yield return null;}
        if(client==null||!client.Ready||!client.World.Has){Check("startup",false);Finish();yield break;}
        var run=Run();while(true){bool more=false;object current=null;try{more=run.MoveNext();if(more)current=run.Current;}catch(Exception e){Check("acceptance execution",false,J.O("error",e.ToString()));break;}if(!more)break;yield return current;}
        Check("no Unity errors or exceptions",errors.Count==0,new J(errors.Select(e=>new J(e)).ToList()));Finish();
    }
    IEnumerator Run(){
        int month=client.World["month"].I;double calls=client.World["ai"]["calls"].N;string events=client.World["events"].S,hero=client.World["hero"].ToString();
        Screen.SetResolution(1680,960,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.3f);
        app.GoStage(Stage.City,false);
        var places=client.World["locations"].A.OrderByDescending(p=>p["population"].I).Take(3).Select(p=>p["id"].I).ToList();
        if(client.World["hero"]["created"].B&&!places.Contains(client.World["hero"]["place"].I))places.Insert(0,client.World["hero"]["place"].I);
        foreach(int place in places){
            yield return app.CityLoadForSmoke(place);
            // The unloaded map deliberately falls back to tiles; check the default after its data arrives.
            if(place==places[0])Check("舆图 is the default city view",app.CityModeForSmoke==CityMode.Atlas);
            app.SetCityModeForSmoke(CityMode.Atlas);
            yield return new WaitForSecondsRealtime(.3f);
            Check("every facility belongs to a settlement: "+place,app.ValidateCityAtlasData(out J data),data);
            yield return Capture("atlas-"+place);
            Check("every settlement, the gate and each road out are drawn inside the sheet: "+place,app.ValidateCityAtlasHits(out J hits),hits);
            foreach(var site in app.City["settlements"].A.Select(s=>s["id"].S).ToList()){
                app.OpenCityScroll(site);yield return new WaitForSecondsRealtime(.3f);
                Check("scroll lists the facilities of "+site+" at "+place,app.ValidateCityScroll(out J scroll),scroll);
                yield return Capture("scroll-"+place+"-"+site);
            }
            app.SetCityModeForSmoke(CityMode.Tiles);yield return new WaitForSecondsRealtime(.3f);
            Check("地格 remains available: "+place,app.CityModeForSmoke==CityMode.Tiles);
            yield return Capture("tiles-"+place);
            app.SetCityModeForSmoke(CityMode.Atlas);
        }
        yield return client.Refresh();
        Check("browsing preserves month, events, hero and model calls",client.World["month"].I==month&&client.World["events"].S==events&&client.World["hero"].ToString()==hero&&client.World["ai"]["calls"].N==calls);
    }
    void Finish(){bool pass=checks.All(c=>c["pass"].B);File.WriteAllText(Path.Combine(output,"city-atlas.json"),J.O("pass",pass,"checks",new J(checks),"save_dir",client==null?"":client.SaveDir).ToString());Application.Quit(pass?0:1);}
}
public partial class JianghuApp {
    public CityMode CityModeForSmoke=>CityModeShown;
    public void SetCityModeForSmoke(CityMode mode)=>SetCityMode(mode);
    public bool ValidateCityAtlasData(out J facts){
        var sites=new HashSet<string>(CitySettlementsList.Select(s=>s["id"].S));
        var orphans=City["nodes"].A.Where(n=>!sites.Contains(n["settlement_id"].S)).Select(n=>new J(n["id"].S)).ToList();
        var empty=CitySettlementsList.Where(s=>CityFacilities(s["id"].S).Count==0).Select(s=>new J(s["id"].S)).ToList();
        int people=CitySettlementsList.Sum(s=>CityFacilities(s["id"].S).Sum(n=>n["residents"].A.Count));
        facts=J.O("settlements",CitySettlementsList.Count,"facilities",City["nodes"].A.Count,"orphans",new J(orphans),"empty_settlements",new J(empty),"people_at_facilities",people,"population",City["population"].I,"exits",City["connections"].A.Count);
        return CityAtlasReady&&orphans.Count==0&&empty.Count==0;
    }
    public bool ValidateCityAtlasHits(out J facts){
        Rect sheet=CitySheetRect;var expected=CitySettlementsList.Select(s=>"s:"+s["id"].S).Concat(new[]{"gate"}).Concat(City["connections"].A.Select(c=>"x:"+c["place"].S)).ToList();
        var missing=expected.Where(k=>!cityAtlasHits.ContainsKey(k)).Select(k=>new J(k)).ToList();
        var outside=cityAtlasHits.Where(p=>!sheet.Overlaps(p.Value)||p.Value.x<sheet.x-2||p.Value.xMax>sheet.xMax+2).Select(p=>new J(p.Key)).ToList();
        var overlapping=new List<J>();var keys=cityAtlasHits.Keys.Where(k=>k.StartsWith("x:")).ToList();
        for(int i=0;i<keys.Count;i++)for(int j=i+1;j<keys.Count;j++)if(cityAtlasHits[keys[i]].Overlaps(cityAtlasHits[keys[j]]))overlapping.Add(new J(keys[i]+"/"+keys[j]));
        facts=J.O("hits",cityAtlasHits.Count,"missing",new J(missing),"outside",new J(outside),"overlapping_exits",new J(overlapping));
        return missing.Count==0&&outside.Count==0&&overlapping.Count==0;
    }
    public bool ValidateCityScroll(out J facts){
        var facilities=CityFacilities(cityScrollSite);var site=CitySettlement(cityScrollSite);
        // Facilities beyond the visible width are culled; scrolling through must reach every one.
        var seen=new HashSet<string>(cityScrollHits.Keys);
        facts=J.O("site",cityScrollSite,"facilities",facilities.Count,"visible",seen.Count,"residents",facilities.Sum(n=>n["residents"].A.Count),"settlement_residents",site["residents"].A.Count);
        return CityModeShown==CityMode.Scroll&&site.Has&&facilities.Count>0&&seen.Count>0&&seen.All(k=>facilities.Any(n=>n["id"].S==k));
    }
}
}
