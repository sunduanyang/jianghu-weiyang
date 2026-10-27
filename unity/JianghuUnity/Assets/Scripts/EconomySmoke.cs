using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public class EconomySmoke : MonoBehaviour {
    GameClient client;JianghuApp app;string output;
    readonly List<J> checks=new List<J>();readonly List<string> errors=new List<string>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Boot(){
        if(GameClient.Arg("-economySmokeDir").Length==0)return;
        if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("Economy acceptance requires an isolated save");Application.Quit(1);return;}
        new GameObject("Economy acceptance").AddComponent<EconomySmoke>();
    }
    void OnEnable(){Application.logMessageReceived+=Log;}
    void OnDisable(){Application.logMessageReceived-=Log;}
    void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(message);}
    void Check(string name,bool pass){checks.Add(J.O("check",name,"pass",pass));}
    IEnumerator Capture(string name){
        yield return new WaitForSecondsRealtime(.6f);yield return new WaitForEndOfFrame();
        var t=ScreenCapture.CaptureScreenshotAsTexture();var colors=new HashSet<int>();
        if(t!=null){File.WriteAllBytes(Path.Combine(output,name+".png"),t.EncodeToPNG());var pixels=t.GetPixels32();for(int i=0;i<pixels.Length;i+=97){var c=pixels[i];colors.Add((c.r<<16)|(c.g<<8)|c.b);}Destroy(t);}Check("render "+name,colors.Count>64);
    }
    IEnumerator Start(){
        output=GameClient.Arg("-economySmokeDir");Directory.CreateDirectory(output);
        float deadline=Time.realtimeSinceStartup+100;
        while(Time.realtimeSinceStartup<deadline){client=FindAnyObjectByType<GameClient>();app=FindAnyObjectByType<JianghuApp>();if(client!=null&&client.Ready&&client.World.Has)break;yield return null;}
        if(client==null||!client.Ready){Check("companion startup",false);Finish();yield break;}
        var run=Run();while(true){bool more;object current=null;try{more=run.MoveNext();if(more)current=run.Current;}catch(Exception e){errors.Add(e.ToString());break;}if(!more)break;yield return current;}
        Check("no Unity errors or exceptions",errors.Count==0);Finish();
    }
    IEnumerator Run(){
        Check("1800 NPC save",client.World["population"].I==1800);
        Check("model disabled",!client.World["ai"]["config"]["enabled"].B);
        int month=client.World["month"].I;J market=new J();yield return client.Request("/api/economy",null,j=>market=j);
        Check("13 goods and 49 finite town markets",market["enabled"].B&&market["goods"].A.Count==13&&market["towns"].A.Count==49);
        var buy=client.HeroBody("market","operation","buy","good","grain","amount",1,"place",client.World["hero"]["place"]);
        double pantry=market["household"]["stock"]["grain"].N;bool ok=false;
        yield return client.Request("/api/economy",buy,j=>{market=j["market"];ok=true;});Check("real player buy",ok&&market["household"]["stock"]["grain"].N>pantry);
        yield return client.Refresh();double wealth=client.World["player"]["wealth"].N;int points=client.World["hero"]["points"].I;
        bool staleAccepted=false;yield return client.Request("/api/economy",buy,j=>staleAccepted=true,false);yield return client.Refresh();
        Check("stale order rejected without another debit",!staleAccepted&&client.World["player"]["wealth"].N==wealth&&client.World["hero"]["points"].I==points);
        Screen.SetResolution(1280,800,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.3f);Screen.SetResolution(1680,960,FullScreenMode.Windowed);
        app.ShowEconomyForSmoke();yield return Capture("town-market");
        bool advanced=false;yield return client.Request("/api/hero",client.HeroBody("end_turn"),j=>advanced=true);yield return client.Refresh();Check("month advances once",advanced&&client.World["month"].I==month+1);
        app.ShowEconomyForSmoke();yield return Capture("household-month-end");
        int person=client.World["npcs"].A.First(n=>n["alive"].B&&n["age_months"].I>=192)["id"].I;app.SelectPerson(person);
        float deadline=Time.realtimeSinceStartup+15;while((!app.Person.Has||app.Person["id"].I!=person)&&Time.realtimeSinceStartup<deadline)yield return null;
        Check("NPC seven need categories",app.Person["economy_view"]["household"]["needs"].D.Count==7);
        app.ShowHouseholdForSmoke();yield return Capture("npc-household");
        Check("government accounts balance",client.World["countries"].A.All(c=>Math.Abs(c["government"]["fund"].N-c["government"]["opening_fund"].N-c["government"]["revenue"].N+c["government"]["expenses"].N)<.001));
    }
    void Finish(){bool passed=errors.Count==0&&checks.All(c=>c["pass"].B);File.WriteAllText(Path.Combine(output,"acceptance.json"),J.O("unity",Application.unityVersion,"checks",new J(checks),"errors",new J(errors.Select(e=>new J(e)).ToList()),"passed",passed).ToString());Application.Quit(passed?0:1);}
}
}
