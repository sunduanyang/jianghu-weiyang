using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BootManualsSmoke(){
        if(GameClient.Arg("-manualsSmoke")=="")return;
        if(GameClient.Arg("-saveDir")==""){Debug.LogError("Manual acceptance requires an isolated save");Application.Quit(2);return;}
        new GameObject("Manual acceptance").AddComponent<ManualsSmokeRunner>();
    }
    internal IEnumerator VerifyManuals(){
        string output=GameClient.Arg("-manualsSmoke");Directory.CreateDirectory(output);
        var checks=new List<J>();var errors=new List<string>();
        Application.LogCallback capture=(s,t,k)=>{if(k==LogType.Error||k==LogType.Exception)errors.Add(s+"\n"+t);};
        Application.logMessageReceived+=capture;
        Action<string,bool> check=(name,ok)=>{checks.Add(J.O("check",name,"passed",ok));File.WriteAllText(Path.Combine(output,"progress.json"),new J(checks).ToString());};
        float deadline=Time.realtimeSinceStartup+100;
        while((!Client.Ready||!Hero["created"].B)&&Time.realtimeSinceStartup<deadline)yield return null;
        check("isolated fixture loaded",Client.Ready&&Hero["created"].B);
        if(Client.Ready&&Hero["created"].B){
            int teacher=1;string art="shaolin_g9";int month=W["month"].I;
            double points=Hero["points"].N;
            yield return Client.Request("/api/npc/"+teacher,null,j=>Person=j);
            check("NPC exposes selected art for seeking",Person["manual_offers"].A.Any(o=>o["art"].S==art&&o["reason"].S==""));
            ViewPerson(teacher,PersonTab.Martial);yield return WaitForCharacter(teacher);
            yield return CharacterPicture(output,"01-npc-martial-view");
            OpenInteraction(teacher);
            deadline=Time.realtimeSinceStartup+15;
            while((!InteractionDataReady||!CharacterReady)&&Time.realtimeSinceStartup<deadline)yield return null;
            SetTab(2);ChooseInteraction("teach");interactionArt=art;
            yield return CharacterPicture(output,"01-seek-teacher");
            BeginInteraction(art:art);
            deadline=Time.realtimeSinceStartup+15;
            while((Client.Busy||!Inventory["items"].A.Any(i=>i["art"].S==art))&&Time.realtimeSinceStartup<deadline)yield return null;
            LoadInventory();
            deadline=Time.realtimeSinceStartup+15;
            while((Client.Busy||!Inventory["items"].A.Any(i=>i["art"].S==art))&&Time.realtimeSinceStartup<deadline)yield return null;
            var book=Inventory["items"].A.FirstOrDefault(i=>i["art"].S==art);
            check("teacher actually hands over manual",book!=null);
            check("seeking consumes two AP",Hero["points"].N==points-2);
            yield return Client.Request("/api/martial",null,j=>Martial=j);
            check("receiving does not directly learn art",!Martial["player"]["martial"]["arts"].A.Any(a=>a["id"].S==art));
            OpenOwnInventory();itemCategory="武功秘籍";
            if(book!=null)selectedItem=book["id"].S;
            yield return CharacterPicture(output,"02-received-manual");
            if(book!=null){
                ItemAction("use",book);
                deadline=Time.realtimeSinceStartup+15;
                while((Client.Busy||!Martial["player"]["martial"]["arts"].A.Any(a=>a["id"].S==art))&&Time.realtimeSinceStartup<deadline)yield return null;
                check("use learns matching art in live martial view",Martial["player"]["martial"]["arts"].A.Any(a=>a["id"].S==art&&a["source"].S=="秘籍习得"));
                check("one manual consumed",!Inventory["items"].A.Any(i=>i["id"].S==book["id"].S));
                check("effective rooting bonus includes new art",Martial["player"]["martial"]["combat"]["attributes"].A.Any(a=>a["id"].S=="rooting"&&a["bonus"].N>=1));
            }
            ViewPerson(-1,PersonTab.Martial);yield return null;personScroll=new Vector2(0,10000);
            yield return CharacterPicture(output,"03-learned-art");
            yield return Client.Request("/api/market",null,j=>Market=j);
            check("market contains all 126 manuals",Market["items"].A.Where(i=>i["category"].S=="manual").Select(i=>i["art"].S).Distinct().Count()==126);
            OpenMarket("货栈");itemCategory="武功秘籍";yield return CharacterPicture(output,"04-manual-market");
            check("no month advanced",W["month"].I==month);
            yield return Client.Request("/api/save",J.O(),j=>{});
        }
        check("no runtime errors",errors.Count==0);
        Application.logMessageReceived-=capture;
        bool passed=checks.All(c=>c["passed"].B);
        File.WriteAllText(Path.Combine(output,"acceptance.json"),J.O("passed",passed,"checks",new J(checks),"errors",new J(errors.Select(e=>new J(e)).ToList())).ToString());
        Application.Quit(passed?0:2);
    }
}
public class ManualsSmokeRunner:MonoBehaviour {
    IEnumerator Start(){yield return null;var app=FindAnyObjectByType<JianghuApp>();if(app!=null)yield return app.VerifyManuals();else Application.Quit(2);}
}
}
