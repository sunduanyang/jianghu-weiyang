using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
// Opt-in acceptance through the built Unity player and its actual local companion.
// No real model calls. Every run must be given an isolated -saveDir.
public class UnitySmoke : MonoBehaviour {
    string output;GameClient client;JianghuApp app;readonly List<J> checks=new List<J>();readonly List<string> exceptions=new List<string>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Boot(){if(GameClient.Arg("-smokeDir").Length>0){if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("Acceptance requires a separate -saveDir");Application.Quit(1);return;}new GameObject("Unity migration acceptance").AddComponent<UnitySmoke>();}}
    void OnEnable(){Application.logMessageReceived+=Log;}
    void OnDisable(){Application.logMessageReceived-=Log;}
    void Log(string message,string trace,LogType type){if(type==LogType.Exception||type==LogType.Error)exceptions.Add(message);}
    void Check(string name,bool pass,J facts=null){checks.Add(J.O("check",name,"pass",pass,"facts",facts??new J()));if(!pass)Debug.LogWarning("ACCEPTANCE FAILED: "+name);}
    IEnumerator Call(string path,J body,Action<J> done=null,bool expected=true){bool ok=false;yield return client.Request(path,body,j=>{ok=true;done?.Invoke(j);});Check(path,ok==expected,J.O("request",body??new J(),"expected_success",expected,"error",client.Error));}
    IEnumerator Snapshot(int page,string name){app.Go(page);yield return new WaitForSecondsRealtime(.8f);yield return new WaitForEndOfFrame();string path=Path.Combine(output,name+".png");var capture=ScreenCapture.CaptureScreenshotAsTexture();if(capture!=null){File.WriteAllBytes(path,capture.EncodeToPNG());Destroy(capture);}yield return new WaitForSecondsRealtime(.2f);Check("render "+name,File.Exists(path)&&new FileInfo(path).Length>10000);}
    IEnumerator Start(){
        output=GameClient.Arg("-smokeDir");Directory.CreateDirectory(output);float timeout=Time.realtimeSinceStartup+100;
        while(Time.realtimeSinceStartup<timeout){client=FindAnyObjectByType<GameClient>();app=FindAnyObjectByType<JianghuApp>();if(client!=null&&client.Ready&&client.World.Has)break;yield return null;}
        if(client==null||!client.Ready){Check("companion startup",false);Finish();yield break;}
        var work=Run();while(true){bool more=false;object next=null;try{more=work.MoveNext();if(more)next=work.Current;}catch(Exception e){Check("smoke execution",false,J.O("error",e.ToString()));break;}if(!more)break;yield return next;}
        yield return new WaitForSecondsRealtime(.4f);Check("no Unity runtime exceptions",exceptions.Count==0,new J(exceptions.Select(x=>new J(x)).ToList()));Finish();
    }
    IEnumerator Run(){
        Check("native client ready",client.Ready);Check("24 towns migrated",client.World["locations"].A.Count==24);Check("34 organisations migrated",client.World["factions"].A.Count==34);Check("AI defaults disabled",!client.World["ai"]["config"]["enabled"].B);
        string proof=Path.Combine(client.SaveDir,"acceptance-state.json");
        if(GameClient.Arg("-smokePhase")=="restart"){
            J old=J.Parse(File.ReadAllText(proof));Check("restart preserves month",old["month"].I==client.World["month"].I);Check("restart preserves hero",old["hero"].ToString()==client.World["hero"].ToString());Check("restart preserves inventory",old["inventory"].ToString()==client.World["player"]["inventory"].ToString());yield return Snapshot(0,"restart-world");yield break;
        }
        double initialCalls=client.World["ai"]["calls"].N;
        foreach(string endpoint in new[]{"/api/state","/api/hero","/api/inventory","/api/market","/api/martial","/api/country/0","/api/faction/0","/api/events","/api/ai/logs"})yield return Call(endpoint,null);
        var adult=client.World["npcs"].A.First(n=>n["alive"].B&&n["age_months"].I>=192&&n["health"].N>=60);app.SelectPerson(adult["id"].I);
        Check("24 skins share one rig and seven clips",app.ValidatePaperCast(out J paperFacts),paperFacts);
        yield return Snapshot(0,"01-atlas");app.SelectedPlace=client.World["hero"]["place"].I;yield return Snapshot(1,"02-city");yield return Snapshot(2,"03-inventory");yield return Snapshot(3,"04-martial");app.OpenInstitution(true,0);yield return Snapshot(4,"05-institutions");yield return Snapshot(5,"06-puppets");yield return Snapshot(6,"07-events");yield return Snapshot(7,"08-local-ai");yield return Snapshot(8,"09-saves");
        app.PaperSmokePerson();yield return Snapshot(0,"11-paper-character");Check("NPC portrait and figure resolve the same skin",app.PaperSkinFor(app.Person)==app.PaperSkinFor(client.World["npcs"].A.First(n=>n["id"].I==adult["id"].I)));app.PaperSmokeDialogue();yield return Snapshot(0,"12-paper-dialogue");app.PaperSmokeStage();
        app.PaperSmokeGallery("idle",false);yield return Snapshot(5,"13-paper-cast-idle");app.PaperSmokeGallery("attack",true);yield return Snapshot(5,"14-paper-cast-attack-left");app.PaperSmokeStudio();yield return Snapshot(5,"15-paper-walk-bones");
        Check("126 legacy vector rig poses preserved",app.ValidateSourceRig(out double maxRigError),J.O("max_anchor_error",maxRigError));
        if(GameClient.Arg("-smokePhase")=="visuals")yield break;
        yield return Call("/api/intervene",J.O("npc_id",adult["id"],"action","gift"));yield return Call("/api/intervene",J.O("npc_id",adult["id"],"action","talk","text","愿你在此地安顿生活。"));yield return Call("/api/intervene",J.O("npc_id",adult["id"],"action","visit"));
        J shop=new J(),pack=new J(),battle=new J();yield return Call("/api/market",null,j=>shop=j);var buy=shop["items"].A.First(i=>i["template"].S=="herb");yield return Call("/api/items",J.O("operation","buy","item_id",buy["id"]));yield return Call("/api/items",J.O("operation","sell","item_id",buy["id"]));
        yield return Call("/api/inventory",null,j=>pack=j);var equipped=pack["items"].A.First(i=>i["equipped"].B);yield return Call("/api/items",J.O("operation","unequip","item_id",equipped["id"]));yield return Call("/api/items",J.O("operation","equip","item_id",equipped["id"]));
        var manual=pack["items"].A.FirstOrDefault(i=>i["operations"].A.Any(o=>o.S=="read"));if(manual!=null)yield return Call("/api/items",J.O("operation","read","item_id",manual["id"]));
        yield return Call("/api/market",null,j=>shop=j);var herbs=shop["items"].A.Where(i=>i["template"].S=="herb").Take(2).ToArray();foreach(var herb in herbs)yield return Call("/api/items",J.O("operation","buy","item_id",herb["id"]));yield return Call("/api/items",J.O("operation","craft","recipe","salve"));yield return Call("/api/inventory",null,j=>pack=j);var medicine=pack["items"].A.First(i=>i["template"].S=="salve");yield return Call("/api/items",J.O("operation","use","item_id",medicine["id"]),null,medicine["operations"].A.Any(o=>o.S=="use"));var gift=pack["items"].A.First(i=>i["category"].S=="material");yield return Call("/api/items",J.O("operation","gift","item_id",gift["id"],"target",adult["id"]));
        yield return Call("/api/intervene",J.O("npc_id",adult["id"],"action","challenge"),j=>battle=j["battle"]);yield return Call("/api/battle",J.O("battle_id",battle["id"],"round",battle["round"],"action","guard"),j=>{app.Martial=j;battle=j["battle"];});app.ShowBattleForSmoke();yield return Snapshot(3,"10-battle");yield return Call("/api/battle",J.O("battle_id",battle["id"],"round",battle["round"],"action","surrender"));
        yield return client.Refresh();var h=client.World["hero"];
        if(!h["created"].B){yield return Call("/api/hero",client.HeroBody("create","name","Unity验收","sex","男","age",18,"place",h["place"]));yield return client.Refresh();h=client.World["hero"];}
        yield return Call("/api/city?place="+h["place"].S,null,j=>app.City=j);
        var gate=app.City["nodes"].A.First(n=>n["id"].S=="gate");var destination=app.City["nodes"].A.FirstOrDefault(n=>n["id"].S!="gate"&&n["walk_cost"].I>0&&n["walk_cost"].I<=h["points"].I);
        if(destination!=null){yield return Call("/api/hero",client.HeroBody("walk","place",h["place"],"row",destination["row"],"col",destination["col"]));yield return client.Refresh();Check("city move consumes AP",client.World["hero"]["points"].I<h["points"].I);yield return Call("/api/hero",client.HeroBody("end_turn"));yield return client.Refresh();yield return Call("/api/hero",client.HeroBody("walk","place",h["place"],"row",gate["row"],"col",gate["col"]));yield return client.Refresh();}
        var link=client.World["hero"]["connections"].A.FirstOrDefault(c=>c["enabled"].B);if(link!=null){yield return Call("/api/hero",client.HeroBody("travel","place",link["place"]));yield return client.Refresh();Check("route travel reaches target",client.World["hero"]["place"].I==link["place"].I);}
        J stale=client.HeroBody("end_turn");int before=client.World["month"].I;yield return Call("/api/hero",stale);yield return client.Refresh();Check("month advances exactly once",client.World["month"].I==before+1);yield return Call("/api/hero",stale,null,false);yield return client.Refresh();Check("stale month request rejected",client.World["month"].I==before+1);Check("AP resets to 30",client.World["hero"]["points"].I==30);
        yield return Call("/api/martial",J.O("action","practice","art","breath"));yield return client.Refresh();yield return Call("/api/campaign?place="+client.World["hero"]["place"].S,null);
        yield return Call("/api/save",J.O());Check("zero new model calls",client.World["ai"]["calls"].N==initialCalls);
        // Full import round-trip must load a copy and leave the source bytes intact.
        string source=Path.Combine(Application.streamingAssetsPath,"Backend","seed-world.sqlite");
        if(File.Exists(source)){string hash=Hash(source);yield return Call("/api/unity/import",J.O("path",source));yield return client.Refresh();Check("import preserves original bytes",hash==Hash(source));Check("import uses a new save",Path.GetFullPath(client.World["database"].S)!=Path.GetFullPath(source));}
        else Check("private migration fixture absent; import round-trip not run",true,J.O("skipped",true));
        yield return Call("/api/new",J.O("population",100,"seed",20261026,"prehistory",0));yield return client.Refresh();Check("new world population",client.World["population"].I==100);Check("new world month zero",client.World["month"].I==0);
        yield return Call("/api/hero",client.HeroBody("create","name","Unity验收","sex","女","age",20,"place",45));yield return client.Refresh();yield return Call("/api/hero",client.HeroBody("end_turn"));yield return client.Refresh();yield return Call("/api/save",J.O());
        File.WriteAllText(proof,J.O("month",client.World["month"],"hero",client.World["hero"],"inventory",client.World["player"]["inventory"]).ToString());
    }
    static string Hash(string p){using(var sha=System.Security.Cryptography.SHA256.Create())using(var f=File.OpenRead(p))return BitConverter.ToString(sha.ComputeHash(f));}
    void Finish(){File.WriteAllText(Path.Combine(output,GameClient.Arg("-smokePhase")=="restart"?"restart.json":"acceptance.json"),J.O("unity",Application.unityVersion,"checks",new J(checks),"passed",checks.All(c=>c["pass"].B)).ToString());Application.Quit(checks.All(c=>c["pass"].B)?0:1);}
}
public partial class JianghuApp {public void ShowBattleForSmoke(){battleView=true;}}
}
