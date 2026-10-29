using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public class SettlementLayoutSmoke : MonoBehaviour {
    GameClient client;JianghuApp app;string output;
    readonly List<J> checks=new List<J>();readonly List<string> errors=new List<string>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Boot(){
        if(GameClient.Arg("-settlementSmokeDir").Length==0)return;
        if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("Settlement acceptance requires an isolated save");Application.Quit(1);return;}
        new GameObject("Settlement layout acceptance").AddComponent<SettlementLayoutSmoke>();
    }
    void OnEnable(){Application.logMessageReceived+=Log;}
    void OnDisable(){Application.logMessageReceived-=Log;}
    void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(message);}
    void Check(string name,bool pass,J facts=null){checks.Add(J.O("check",name,"pass",pass,"facts",facts??new J()));}
    IEnumerator Capture(string name){
        bool rendered=false;
        for(int attempt=0;attempt<4&&!rendered;attempt++){
            yield return new WaitForSecondsRealtime(.6f);yield return new WaitForEndOfFrame();
            var t=ScreenCapture.CaptureScreenshotAsTexture();if(t==null)continue;
            var colors=new HashSet<int>();var pixels=t.GetPixels32();for(int i=0;i<pixels.Length;i+=101){var c=pixels[i];colors.Add((c.r<<16)|(c.g<<8)|c.b);}
            rendered=colors.Count>64;if(rendered)File.WriteAllBytes(Path.Combine(output,name+".png"),t.EncodeToPNG());Destroy(t);
        }
        Check("render "+name,rendered);
    }
    IEnumerator Start(){
        output=GameClient.Arg("-settlementSmokeDir");Directory.CreateDirectory(output);float deadline=Time.realtimeSinceStartup+100;
        while(Time.realtimeSinceStartup<deadline){client=FindAnyObjectByType<GameClient>();app=FindAnyObjectByType<JianghuApp>();if(client!=null&&client.Ready&&client.World.Has)break;yield return null;}
        if(client==null||!client.Ready){Check("companion startup",false);Finish();yield break;}
        var run=Run();while(true){bool more;object current=null;try{more=run.MoveNext();if(more)current=run.Current;}catch(Exception e){errors.Add(e.ToString());break;}if(!more)break;yield return current;}
        Check("no Unity errors or exceptions",errors.Count==0);Finish();
    }
    IEnumerator Run(){
        int month=client.World["month"].I;string events=client.World["events"].S,hero=client.World["hero"].ToString();
        var people=new HashSet<int>();var examples=new Dictionary<string,int>();int ordinaryTotal=0,sectTotal=0;
        foreach(var place in client.World["locations"].A){
            J city=new J();yield return client.Request("/api/city?place="+place["id"].I,null,j=>city=j);
            var ordinary=city["settlements"].A.Where(t=>t["kind"].S!="sect").ToList();var plan=city["settlement_layout"];string tier=plan["tier"].S;
            var expected=tier=="prosperous"?new[]{1,4,9}:tier=="general"?new[]{4,9}:new[]{1,9};
            Check("approved footprint pattern "+city["name"].S,city["version"].I==8&&ordinary.Select(t=>t["footprint"]["size"].I*t["footprint"]["size"].I).OrderBy(n=>n).SequenceEqual(expected));
            var sects=city["settlements"].A.Where(t=>t["kind"].S=="sect").ToList();
            Check("independent nine-cell sects "+city["name"].S,sects.All(t=>t["footprint"]["size"].I==3));
            foreach(var p in city["nodes"].A.SelectMany(n=>n["residents"].A))Check("person counted once "+p["id"].S,people.Add(p["id"].I));
            ordinaryTotal+=ordinary.Count;sectTotal+=sects.Count;
            if(!examples.ContainsKey(tier)||tier=="prosperous"&&sects.Count>0)examples[tier]=place["id"].I;
        }
        Check("49 regions remain",client.World["locations"].A.Count==49);
        Check("ordinary settlements reduced to 111; 14 sects retained",ordinaryTotal==111&&sectTotal==14,J.O("ordinary",ordinaryTotal,"sects",sectTotal));
        Check("all living people visible once",people.Count==client.World["population"].I);
        Screen.SetResolution(1280,800,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.3f);Screen.SetResolution(1680,960,FullScreenMode.Windowed);app.GoStage(Stage.City,false);
        foreach(var tier in new[]{"prosperous","general","sparse"}){
            yield return app.CityLoadForSmoke(examples[tier]);
            Check("rendered city uses "+tier,app.City["settlement_layout"]["tier"].S==tier);
            Check("settlement rosters cover every present person "+tier,app.ValidateCitySettlementRostersForSmoke(out J facts),facts);
            yield return Capture("layout-"+tier);
        }
        yield return client.Refresh();
        Check("viewing preserves calendar journal hero and points",client.World["month"].I==month&&client.World["events"].S==events&&client.World["hero"].ToString()==hero);
        Check("language model stays disabled",!client.World["ai"]["config"]["enabled"].B);
    }
    void Finish(){bool pass=errors.Count==0&&checks.All(c=>c["pass"].B);File.WriteAllText(Path.Combine(output,"acceptance.json"),J.O("unity",Application.unityVersion,"checks",new J(checks),"errors",new J(errors.Select(e=>new J(e)).ToList()),"passed",pass).ToString());Application.Quit(pass?0:1);}
}
public partial class JianghuApp {
    // The 舆图 roster lists a settlement's residents; together they must cover everyone present at a facility.
    public bool ValidateCitySettlementRostersForSmoke(out J facts){
        var present=City["nodes"].A.SelectMany(n=>n["residents"].A).Select(p=>p["id"].I).Distinct().OrderBy(id=>id).ToArray();
        var listed=CitySettlementsList.SelectMany(s=>s["residents"].A).Select(p=>p["id"].I).Distinct().OrderBy(id=>id).ToArray();
        facts=J.O("present",present.Length,"listed",listed.Length,"settlements",CitySettlementsList.Count,"population",City["population"].I);
        return present.Length==City["population"].I&&present.SequenceEqual(listed);
    }
}
}
