using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
// Opt-in acceptance on a copied save; browsing never advances the world.
public class CityPeopleSmoke : MonoBehaviour {
    string output;GameClient client;JianghuApp app;
    readonly List<J> checks=new List<J>();readonly List<string> errors=new List<string>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Boot(){
        if(GameClient.Arg("-cityPeopleSmokeDir").Length==0)return;
        if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("City people acceptance requires an isolated -saveDir");Application.Quit(1);return;}
        new GameObject("Selected city cell acceptance").AddComponent<CityPeopleSmoke>();
    }
    void OnEnable(){Application.logMessageReceived+=Log;}
    void OnDisable(){Application.logMessageReceived-=Log;}
    void Log(string message,string stack,LogType type){if(type==LogType.Exception||type==LogType.Error)errors.Add(message);}
    void Check(string name,bool pass,J facts=null){checks.Add(J.O("check",name,"pass",pass,"facts",facts??new J()));}
    IEnumerator Capture(string name){
        // The first resized hidden swapchain can return a black frame.
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
        output=GameClient.Arg("-cityPeopleSmokeDir");Directory.CreateDirectory(output);
        float deadline=Time.realtimeSinceStartup+100;
        while(Time.realtimeSinceStartup<deadline){client=FindAnyObjectByType<GameClient>();app=FindAnyObjectByType<JianghuApp>();if(client!=null&&client.Ready&&client.World.Has)break;yield return null;}
        if(client==null||!client.Ready||!client.World.Has){Check("startup",false);Finish();yield break;}
        var run=Run();while(true){bool more=false;object current=null;try{more=run.MoveNext();if(more)current=run.Current;}catch(Exception e){Check("acceptance execution",false,J.O("error",e.ToString()));break;}if(!more)break;yield return current;}
        Check("no Unity errors or exceptions",errors.Count==0,new J(errors.Select(e=>new J(e)).ToList()));Finish();
    }
    IEnumerator Run(){
        int month=client.World["month"].I;double calls=client.World["ai"]["calls"].N;string events=client.World["events"].S,hero=client.World["hero"].ToString();
        Screen.SetResolution(1680,960,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.3f);
        app.Page=1;
        var places=client.World["locations"].A.OrderByDescending(p=>p["population"].I).Take(2).ToList();
        foreach(var place in places){
            yield return app.CityLoadForSmoke(place["id"].I);
            Check("each cell matches actual facility occupants: "+place["name"].S,app.ValidateCityPeopleForSmoke(out J facts),facts);
            Check("changing region clears selected occupants",app.CityPeopleSelectionForSmoke()["row"].I==-1);
            var home=app.City["nodes"].A.Where(n=>n["residents"].A.Count>0&&app.City["settlements"].A.Any(t=>t["footprint"]["size"].I>1&&t["row"].I==n["row"].I&&t["col"].I==n["col"].I)).OrderByDescending(n=>n["residents"].A.Count).First();
            int row=home["row"].I,col=home["col"].I;
            app.CityPeopleHideRosterForSmoke();
            app.CityPeopleSelectForSmoke(row,col);var populated=app.CityPeopleSelectionForSmoke();
            Check("selection displays populated cell",populated["people"].A.Count>0,populated);
            Check("selecting a cell opens left people tab and clears old search",populated["roster_open"].B&&populated["roster_mode"].S=="人物"&&populated["query"].S==""&&populated["scroll_y"].N==0,populated);
            foreach(var size in new[]{new Vector2Int(1280,800),new Vector2Int(1920,1080)}){
                Screen.SetResolution(size.x,size.y,FullScreenMode.Windowed);
                yield return Capture("populated-"+place["id"].S+"-"+size.x+"x"+size.y);
            }
            app.CityPeopleScrollForSmoke();yield return Capture("scrolled-people-"+place["id"].S);
            var town=app.City["settlements"].A.First(t=>t["footprint"]["size"].I>1&&t["row"].I==row&&t["col"].I==col);
            int emptyRow=town["footprint"]["row"].I,emptyCol=town["footprint"]["col"].I;
            if(emptyRow==row&&emptyCol==col)emptyCol++;
            app.CityPeopleSelectForSmoke(emptyRow,emptyCol);var empty=app.CityPeopleSelectionForSmoke();
            Check("another cell in same building does not inherit occupants",empty["people"].A.Count==0,empty);
            yield return Capture("empty-in-same-building-"+place["id"].S);
            app.CityPeopleSelectForSmoke(row,col);var restored=app.CityPeopleSelectionForSmoke();
            Check("switching back restores occupants and resets scroll",restored["people"].ToString()==populated["people"].ToString()&&restored["scroll_y"].N==0);
            yield return app.CityLoadForSmoke(place["id"].I);
            Check("refreshing same region keeps selected cell",app.CityPeopleSelectionForSmoke()["people"].ToString()==populated["people"].ToString());
        }
        yield return client.Refresh();
        Check("browsing preserves month, events, hero and model calls",client.World["month"].I==month&&client.World["events"].S==events&&client.World["hero"].ToString()==hero&&client.World["ai"]["calls"].N==calls);
    }
    void Finish(){bool pass=checks.All(c=>c["pass"].B);File.WriteAllText(Path.Combine(output,"city-people.json"),J.O("pass",pass,"checks",new J(checks),"save_dir",client==null?"":client.SaveDir).ToString());Application.Quit(pass?0:1);}
}
public partial class JianghuApp {
    public void CityPeopleHideRosterForSmoke(){rosterOpen=false;rosterMode="地区";query="previous search";rosterScroll.y=800;}
    public void CityPeopleScrollForSmoke(){rosterScroll.y=Mathf.Max(0,CityPeopleAt(new Vector2Int(chosenRow,chosenCol)).Count*60-400);}
    public void CityPeopleSelectForSmoke(int row,int col){SelectCityCell(new Vector2Int(row,col));CityFocus(new Vector2Int(row,col));}
    public J CityPeopleSelectionForSmoke()=>J.O("row",chosenRow,"col",chosenCol,"people",new J(CityPeopleAt(new Vector2Int(chosenRow,chosenCol)).Select(p=>p["id"]).ToList()),"scroll_y",rosterScroll.y,"roster_open",rosterOpen,"roster_mode",rosterMode,"query",query);
    public bool ValidateCityPeopleForSmoke(out J facts){
        var failures=new List<J>();int populated=0,emptyLotCells=0,checkedPeople=0,mergedCells=0;
        for(int row=0;row<citySize;row++)for(int col=0;col<citySize;col++){
            var at=new Vector2Int(row,col);var nodes=City["nodes"].A.Where(n=>n["row"].I==row&&n["col"].I==col).ToList();
            var expected=City["nodes"].A.SelectMany(n=>n["residents"].A.Where(p=>(p["row"].Has?p["row"].I:n["row"].I)==row&&(p["col"].Has?p["col"].I:n["col"].I)==col)).Select(p=>p["id"].I).Distinct().OrderBy(id=>id).ToArray();
            var actual=CityPeopleAt(at).Select(p=>p["id"].I).OrderBy(id=>id).ToArray();
            if(!expected.SequenceEqual(actual))failures.Add(J.O("row",row,"col",col));
            if(actual.Length>0){populated++;checkedPeople+=actual.Length;}
            if(nodes.Count(n=>n["residents"].A.Count>0)>1)mergedCells++;
            if(cityLots.ContainsKey(at)&&actual.Length==0)emptyLotCells++;
        }
        var gate=City["nodes"].A.First(n=>n["id"].S==City["entrance"].S);var gateAt=new Vector2Int(gate["row"].I,gate["col"].I);
        var original=City;var fixture=J.Parse(City.ToString());
        // Travellers at the gate must remain visible outside settlement lots.
        fixture["nodes"].A.First(n=>n["id"].S==fixture["entrance"].S)["residents"]=new J(new List<J>{J.O("id",-999,"name","城门行客","job","行旅")});
        City=fixture;IndexCityPeople();bool gateVisible=CityPeopleAt(gateAt).Any(p=>p["id"].I==-999);
        // A new view must remove people whose activity moved to another cell.
        fixture["nodes"].A.First(n=>n["id"].S==fixture["entrance"].S)["residents"]=new J(new List<J>());
        IndexCityPeople();bool refreshClears=!CityPeopleAt(gateAt).Any(p=>p["id"].I==-999);
        City=original;IndexCityPeople();
        facts=J.O("cells",citySize*citySize,"populated_cells",populated,"people",checkedPeople,"empty_cells_in_buildings",emptyLotCells,"merged_facility_cells",mergedCells,"gate_visible",gateVisible,"refresh_clears_old_people",refreshClears,"failures",new J(failures));
        return failures.Count==0&&checkedPeople==City["population"].I&&gateVisible&&refreshClears;
    }
}
}
