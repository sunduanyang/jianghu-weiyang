using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public class LocalLifeSmoke : MonoBehaviour {
    GameClient client;JianghuApp app;string output;
    readonly List<J> checks=new List<J>();readonly List<string> errors=new List<string>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Boot(){
        if(GameClient.Arg("-localLifeSmokeDir").Length==0)return;
        if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("Local life acceptance requires an isolated save");Application.Quit(1);return;}
        new GameObject("Settlement life acceptance").AddComponent<LocalLifeSmoke>();
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
        output=GameClient.Arg("-localLifeSmokeDir");Directory.CreateDirectory(output);float deadline=Time.realtimeSinceStartup+100;
        while(Time.realtimeSinceStartup<deadline){client=FindAnyObjectByType<GameClient>();app=FindAnyObjectByType<JianghuApp>();if(client!=null&&client.Ready&&client.World.Has)break;yield return null;}
        if(client==null||!client.Ready){Check("companion startup",false);Finish();yield break;}
        var run=Run();while(true){bool more;object current=null;try{more=run.MoveNext();if(more)current=run.Current;}catch(Exception e){errors.Add(e.ToString());break;}if(!more)break;yield return current;}
        Check("no Unity errors or exceptions",errors.Count==0);Finish();
    }
    IEnumerator Run(){
        int month=client.World["month"].I;string events=client.World["events"].S,hero=client.World["hero"].ToString();
        int homes=0,present=0,ordinary=0,sects=0,prosperous=0;var people=new HashSet<int>();
        foreach(var place in client.World["locations"].A){
            J city=new J();yield return client.Request("/api/city?place="+place["id"].I,null,j=>city=j);
            var towns=city["settlements"].A;var residents=towns.SelectMany(t=>t["home_residents"].A).ToList();
            var visitors=towns.SelectMany(t=>t["residents"].A).ToList();
            Check("census and presence cover region "+place["name"].S,residents.Count==city["population"].I&&visitors.Count==city["population"].I);
            Check("home census unique "+place["name"].S,residents.All(p=>people.Add(p["id"].I)));
            homes+=residents.Count;present+=visitors.Count;ordinary+=towns.Count(t=>t["kind"].S!="sect");sects+=towns.Count(t=>t["kind"].S=="sect");
            if(city["settlement_layout"]["tier"].S=="prosperous")prosperous++;
        }
        Check("all living people have one home and one current presence",homes==client.World["population"].I&&present==homes);
        Check("approved layout retained",ordinary==client.World["locations"].A.Count*2+prosperous&&sects==8&&client.World["locations"].A.Count==8);
        yield return client.Refresh();Check("browsing preserves saved progress",client.World["month"].I==month&&client.World["events"].S==events&&client.World["hero"].ToString()==hero);
        Screen.SetResolution(1280,800,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.3f);Screen.SetResolution(1680,960,FullScreenMode.Windowed);
        int placeId=client.World["hero"]["place"].I;
        yield return app.CityLoadForSmoke(placeId);
        Check("settlement rosters show real positions before simulation",app.ValidateCitySettlementRostersForSmoke(out J before),before);
        bool advanced=false;yield return client.Request("/api/hero",client.HeroBody("end_turn"),j=>advanced=true);yield return client.Refresh();
        Check("isolated world advances one month",advanced&&client.World["month"].I==month+1);
        yield return app.CityLoadForSmoke(placeId);
        Check("settlement rosters show real weekly positions",app.ValidateCitySettlementRostersForSmoke(out J after),after);
        var site=app.City["settlements"].A.First(t=>t["kind"].S=="village"||t["kind"].S=="town");
        app.CityPickSettlementForSmoke(site["id"].S);app.GoStage(Stage.City,false);
        yield return Capture("settlement-homes-and-presence");
        int person=site["home_residents"].A.First()["id"].I;app.SelectPerson(person);
        float deadline=Time.realtimeSinceStartup+15;while((!app.Person.Has||app.Person["id"].I!=person)&&Time.realtimeSinceStartup<deadline)yield return null;
        var local=app.Person["local_life_view"];
        Check("person exposes fixed home work and current cell",local["enabled"].B&&local["home"]["name"].Has&&local["work"]["name"].Has&&local["current"]["row"].Has);
        app.ShowSocialPersonForSmoke();yield return Capture("person-home-work-current");
        Check("language model stays disabled",!client.World["ai"]["config"]["enabled"].B);
    }
    void Finish(){bool pass=errors.Count==0&&checks.All(c=>c["pass"].B);File.WriteAllText(Path.Combine(output,"acceptance.json"),J.O("unity",Application.unityVersion,"checks",new J(checks),"errors",new J(errors.Select(e=>new J(e)).ToList()),"passed",pass).ToString());Application.Quit(pass?0:1);}
}
}
