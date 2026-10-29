using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BootClaudeSocietySmoke(){
        if(GameClient.Arg("-claudeSocietySmoke").Length==0)return;
        if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("Society acceptance requires an isolated save");Application.Quit(2);return;}
        new GameObject("Claude society acceptance").AddComponent<ClaudeSocietySmoke>();
    }
    internal IEnumerator VerifyClaudeSociety(Action<string,bool> check,string output){
        float deadline=Time.realtimeSinceStartup+100;
        while((!Client.Ready||!Hero["created"].B)&&Time.realtimeSinceStartup<deadline)yield return null;
        check("isolated native world ready",Client.Ready&&Hero["created"].B);
        if(!Client.Ready||!Hero["created"].B)yield break;
        var fixture=J.Parse(File.ReadAllText(Path.Combine(Client.SaveDir,"society-fixture.json")));
        int person=fixture["person"].I,target=fixture["target"].I;
        bool restore=GameClient.Arg("-societyRestore")=="1";
        int month=W["month"].I,events=W["events"].I;
        check("new world contains households and two acceptance actors",W["population"].I>=1800&&fixture["households"]["couples"].I>0&&fixture["households"]["acquaintances"].I>0);
        check("local model disabled",!W["ai"]["config"]["enabled"].B&&W["ai"]["calls"].N==0);
        ViewPerson(fixture["family"].I,PersonTab.Ties);yield return WaitForCharacter(fixture["family"].I);
        check("registered parentage reaches the native person data",Person["parents"].A.Count==2);
        ViewPerson(person,PersonTab.Overview);yield return WaitForCharacter(person);
        var plan=Person["planning_view"]["current"];
        if(!restore){
            check("long undertaking has its name next step and real target",plan["intent"].S=="courtship"&&plan["undertaking"].S=="求偶成家"&&plan["target"].I==target&&plan["next_description"].S.Length>0);
            personScroll=new Vector2(0,5000);
            if(GameClient.Arg("-societyCapture")=="1")yield return CharacterPicture(output,"01-long-undertaking");
            // Follow the same ViewPerson route used by the new target button.
            ViewPerson(plan["target"].I,PersonTab.Overview);yield return WaitForCharacter(target);
            check("undertaking target opens the correct person",FocusPersonId==target&&CurrentPersonTab==PersonTab.Overview);
            SwitchSubject(person);yield return WaitForCharacter(person);
            check("recent-person switch restores the undertaking owner",FocusPersonId==person&&Person["id"].I==person&&CurrentPersonTab==PersonTab.Overview);
        }
        var fv=Person["fame_view"];
        check("town fame reaches the bundled backend and native client",fv["version"].I==1&&fv["towns"].A.Count>1&&fv["towns"].A.All(t=>t["name"].S.Length>0&&t["known"].N>=0&&t["known"].N<=1));
        SetTab((int)PersonTab.Character);personScroll=new Vector2(0,5000);
        if(GameClient.Arg("-societyCapture")=="1")yield return CharacterPicture(output,"02-town-fame");
        J market=new J();yield return Client.Request("/api/economy",null,j=>market=j);
        check("market quotes use the new bid ask and list prices",market["enabled"].B&&market["goods"].A.Count==13&&market["goods"].A.All(g=>g["list_price"].N>0&&g["sell_price"].N<g["price"].N));
        yield return Client.Refresh();
        check("inspecting plans fame and households preserves calendar and events",W["month"].I==month&&W["events"].I==events);
        if(restore){
            var expected=J.Parse(File.ReadAllText(Path.Combine(Client.SaveDir,"society-expected.json")));
            check("cold restart preserves the progressed month",month==expected["month"].I);
            check("cold restart preserves named plan and completed actions",plan.ToString()==expected["plan"].ToString());
            check("cold restart preserves all town recognition shares",fv.ToString()==expected["fame"].ToString());
        }else{
            bool advanced=false;
            yield return Client.Request("/api/hero",Client.HeroBody("end_turn"),j=>advanced=true);
            deadline=Time.realtimeSinceStartup+90;
            do{yield return new WaitForSecondsRealtime(.3f);yield return Client.Refresh();}while(W["month"].I==month&&Time.realtimeSinceStartup<deadline);
            check("actual native end turn advances one month",advanced&&W["month"].I==month+1);
            ViewPerson(person,PersonTab.Overview);yield return WaitForCharacter(person);
            // ViewPerson can reuse a cached response, so explicitly fetch current data.
            yield return Client.Request("/api/npc/"+person,null,j=>Person=j);
            plan=Person["planning_view"]["current"];fv=Person["fame_view"];
            check("the named undertaking advances once through real engine actions",plan["intent"].S=="courtship"&&plan["completed"].A.Count==1&&plan["completed"].A[0]["event_id"].I>0);
            check("monthly fame propagation changes the town recognition data",fv.ToString()!=fixture["fame"].ToString());
            check("government treasuries balance after the new public works rules",W["countries"].A.All(c=>Math.Abs(c["government"]["fund"].N-c["government"]["opening_fund"].N-c["government"]["revenue"].N+c["government"]["expenses"].N)<.001));
            File.WriteAllText(Path.Combine(Client.SaveDir,"society-expected.json"),J.O("month",W["month"],"plan",plan,"fame",fv).ToString());
            yield return Client.Request("/api/save",J.O(),j=>{});
        }
        check("acceptance never invokes a model",W["ai"]["calls"].N==0);
    }
}
public sealed class ClaudeSocietySmoke:MonoBehaviour {
    readonly List<J> checks=new List<J>();readonly List<string> errors=new List<string>();string output;
    void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(message+"\n"+stack);}
    void Check(string name,bool passed){checks.Add(J.O("check",name,"passed",passed));File.WriteAllText(Path.Combine(output,"progress.json"),new J(checks).ToString());}
    IEnumerator Start(){
        output=GameClient.Arg("-claudeSocietySmoke");Directory.CreateDirectory(output);Application.logMessageReceived+=Log;
        yield return null;var app=FindAnyObjectByType<JianghuApp>();
        if(app==null)Check("native application exists",false);
        else{
            var run=app.VerifyClaudeSociety(Check,output);
            while(true){bool more=false;object current=null;try{more=run.MoveNext();if(more)current=run.Current;}catch(Exception e){errors.Add(e.ToString());break;}if(!more)break;yield return current;}
        }
        Check("no native errors or exceptions",errors.Count==0);Application.logMessageReceived-=Log;
        bool passed=checks.All(c=>c["passed"].B)&&errors.Count==0;
        File.WriteAllText(Path.Combine(output,"acceptance.json"),J.O("passed",passed,"checks",new J(checks),"errors",new J(errors.Select(e=>new J(e)).ToList()),"capture_requested",GameClient.Arg("-societyCapture")=="1","visual_verified",false).ToString());
        Application.Quit(passed?0:2);
    }
}
}
