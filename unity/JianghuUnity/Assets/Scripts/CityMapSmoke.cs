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
        foreach(var place in client.World["locations"].A){
            yield return Call("/api/city?place="+place["id"].S,null,j=>app.City=j);
            Check("native routes match backend: "+place["name"].S,app.ValidateCityMap(out J facts),facts);
        }
        Check("browsing preserves month",client.World["month"].I==month);
        if(!client.World["hero"]["created"].B){yield return Call("/api/hero",client.HeroBody("create","name","山河行客","sex","男","age",20,"place",45));yield return client.Refresh();}
        int home=client.World["hero"]["place"].I;app.SelectedPlace=home;app.Go(1);yield return new WaitForSecondsRealtime(1);
        app.CitySmokeView(false);yield return Capture("01-city-overview");
        app.CitySmokeView(true);yield return Capture("02-city-route");
        Check("camera inverse at every zoom",app.ValidateCityCamera(out J cameraFacts),cameraFacts);
        var hero=client.World["hero"];var gate=app.City["nodes"].A.First(n=>n["id"].S==app.City["entrance"].S);
        var target=app.City["nodes"].A.FirstOrDefault(n=>n["row"].I!=hero["row"].I||n["col"].I!=hero["col"].I);
        if(target!=null){
            var path=app.CityPath(hero["row"].I,hero["col"].I,target["row"].I,target["col"].I,out int cost);
            if(path.Count>1&&cost<=hero["points"].I){
                yield return Call("/api/hero",client.HeroBody("walk","place",home,"row",target["row"],"col",target["col"]));yield return client.Refresh();
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
    public bool ValidateCityMap(out J facts){
        originRow=originCol=chosenRow=chosenCol=-1;cityPathKey="";EnsureCityRoutes();int checkedNodes=0;var failures=new List<J>();
        var gate=City["nodes"].A.First(n=>n["id"].S==City["entrance"].S);
        foreach(var node in City["nodes"].A){
            var native=CityPath(gate["row"].I,gate["col"].I,node["row"].I,node["col"].I,out int cost);checkedNodes++;
            var q=new Vector2Int(node["row"].I,node["col"].I);
            if(native.Count==0||cost!=node["walk_cost"].I||!cityCosts.TryGetValue(q,out int overlay)||overlay!=cost)failures.Add(J.O("node",node["name"],"native",cost,"server",node["walk_cost"]));
        }
        int blocked=0;for(int row=0;row<City["size"].I;row++)for(int col=0;col<City["size"].I;col++)if(CityBlocked(CityTile(row,col))){blocked++;if(cityCosts.ContainsKey(new Vector2Int(row,col)))failures.Add(J.O("blocked_row",row,"col",col));}
        facts=J.O("facilities",checkedNodes,"blocked_tiles",blocked,"failures",new J(failures));return failures.Count==0;
    }
    public bool ValidateCityCamera(out J facts){float oldZoom=cityZoom;var oldPan=cityPan;int count=0;bool ok=true;
        foreach(float zoom in new[]{.75f,1,1.65f,2.8f}){cityZoom=zoom;cityPan=new Vector2(79,-51);CityGeometry(CityViewport,out float tw,out float th,out var center);
            for(int row=0;row<City["size"].I;row++)for(int col=0;col<City["size"].I;col++){var p=new Vector2Int(row,col);count++;ok&=CityPick(CityXY(p,tw,th,center),tw,th,center)==p;}
        }
        cityZoom=oldZoom;cityPan=oldPan;facts=J.O("tile_centres_checked",count);return ok;
    }
    public void CitySmokeView(bool detail){
        rosterOpen=false;cityZoom=detail?1.55f:1;cityPan=Vector2.zero;cityReach=detail;cityLabels=cityGrid=true;cityPathKey="";EnsureCityRoutes();
        chosenRow=chosenCol=-1;
        if(detail){var target=City["nodes"].A.FirstOrDefault(n=>n["kind"].S=="market");if(target!=null){chosenRow=target["row"].I;chosenCol=target["col"].I;CityFocus(new Vector2Int(chosenRow,chosenCol));}}
    }
}
}
