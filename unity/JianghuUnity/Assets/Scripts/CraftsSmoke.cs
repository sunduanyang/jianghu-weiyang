using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
namespace Jianghu {
public partial class JianghuApp {
    readonly List<J> crChecks=new List<J>(),crErrors=new List<J>();string crOutput;J crExpected;
    void CrCheck(string name,bool pass,J facts=null){crChecks.Add(J.O("check",name,"passed",pass,"facts",facts??J.O()));File.WriteAllText(Path.Combine(crOutput,"progress.json"),new J(crChecks).ToString());}
    void CrLog(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)crErrors.Add(new J(message));}
    IEnumerator CrRefresh(){yield return Client.Request("/api/crafts",null,j=>Crafts=j);yield return Client.Request("/api/inventory",null,j=>Inventory=j);yield return Client.Refresh();}
    IEnumerator CrCall(string path,J body,Action<J> done=null){J answer=null;yield return Client.Action(path,body,j=>{answer=j;done?.Invoke(j);});CrCheck(path,answer!=null,J.O("error",Client.Error));}
    IEnumerator CrPage(string section,string picture){craftSection=section;OpenMarket("技艺");yield return CrRefresh();yield return CharacterPicture(crOutput,picture);}
    J CrItem(string id)=>Inventory["items"].A.FirstOrDefault(i=>i["id"].S==id)??new J();
    double CrSkill(string id)=>Crafts["crafts"].A.FirstOrDefault(c=>c["id"].S==id)?["value"].N??0;
    // The isolated injury fixture injects damage and records matching contact counters; it is not a balance measurement.
    IEnumerator CrArena(int npc,string action,string item){
        if(W["player"]["equipment"]["weapon"].S!=item)yield return CrCall("/api/items",J.O("operation","equip","item_id",item));
        yield return InteractionActForSmoke(npc,action);
        float until=Time.realtimeSinceStartup+15;while(PhysicsArena==null&&Time.realtimeSinceStartup<until)yield return null;
        var a=PhysicsArena;CrCheck(action+" opens the production source rig",a!=null&&a.Campaign&&a.player.SourceRigActive&&Martial["battle"]["encounter"].S==action);
        if(a==null)yield break;
        J packet=Martial["battle"]["fighters"][0]["weapon"];var weapon=a.player.weapon;var physics=DuelSourceEquipment.Physics(weapon);
        CrCheck(action+" forged attributes reach the real rigidbody and drivers",Math.Abs(weapon.mass-packet["mass"].N)<.0001&&Math.Abs(weapon.handling-packet["handling"].N)<.0001&&Math.Abs(weapon.damage-packet["damage"].N)<.0001&&Math.Abs(weapon.pressure-packet["pressure"].N)<.0001&&Math.Abs(weapon.cost-packet["cost"].N)<.0001&&Math.Abs(a.player.sword.rb.mass-physics.bodyMass)<.0001,packet);
        CrCheck(action+" does not import old world part damage into this bout",a.CaptureCampaign()["fighters"][0]["reference"]["parts"]["forearmNear"]["health"].N==30);
        a.player.testOverride=a.enemy.testOverride=true;a.RunCampaign(true);a.CampaignTestPad(true,Vector2.up);a.StepCampaignForAcceptance(.28f);a.PauseCampaign();a.CampaignReleasePads();
        CrCheck(action+" forged weapon is driven with a retained grip",a.player.attackId>0&&a.player.PrimaryGripError<.15f,J.O("grip_error",a.player.PrimaryGripError,"mass",a.player.sword.rb.mass));
    }
    internal IEnumerator VerifyCrafts(){
        crOutput=GameClient.Arg("-craftsSmoke");Directory.CreateDirectory(crOutput);Application.logMessageReceived+=CrLog;Screen.SetResolution(1680,1000,FullScreenMode.Windowed);
        float until=Time.realtimeSinceStartup+100;while((!Client.Ready||!W.Has)&&Time.realtimeSinceStartup<until)yield return null;
        CrCheck("isolated backend ready",Client.Ready);if(!Client.Ready)yield break;
        yield return CrCall("/api/ai/config",J.O("enabled",false));yield return CrRefresh();
        string phase=GameClient.Arg("-craftsPhase","main"),expectedPath=Path.Combine(Client.SaveDir,"crafts-expected.json");
        J fixture=J.Parse(File.ReadAllText(Path.Combine(Client.SaveDir,"crafts-fixture.json")));int smith=fixture["people"][0].I,rob=fixture["people"][1].I,spar=fixture["people"][2].I,assault=fixture["people"][3].I;
        if(phase=="main"){
            crExpected=J.O("month",W["month"],"repair_id",fixture["repair_id"]);
            CrCheck("three crafts are served by the live API",Crafts["crafts"].A.Count==3&&Crafts["repair"]["cap"].I==70);
            yield return CrPage("造诣","01-levels");
            yield return CrCall("/api/items",J.O("operation","repair","item_id",fixture["repair_id"]));yield return CrRefresh();
            CrCheck("unskilled self repair stops at seventy",CrItem(fixture["repair_id"].S)["durability"].N==70);
            yield return InteractionActForSmoke(smith,"mend");yield return CrRefresh();CrCheck("paid smith repairs the actual item to full",CrItem(fixture["repair_id"].S)["durability"].N==100);
            double before=CrSkill("forging");yield return InteractionActForSmoke(smith,"learn_craft");yield return CrRefresh();CrCheck("learning transfers the smith craft experience",Math.Abs(CrSkill("forging")-before-4.1)<.011);
            foreach(string temper in new[]{"heavy","light"}){
                int points=Hero["points"].I;J made=null;yield return CrCall("/api/crafts",J.O("operation","forge","template","iron_dagger","temper",temper),j=>made=j);
                if(made==null)yield break;string id=made["event"]["facts"]["item_id"].S;crExpected[temper]=new J(id);yield return CrRefresh();J item=CrItem(id);
                CrCheck(temper+" forging creates an attributed physical item",item["craft"]["temper"].S==temper&&item["craft_label"].S.Contains("所铸")&&Hero["points"].I==points-3,item["weapon_attributes"]);
                CrCheck(temper+" changes mass in the intended direction",temper=="heavy"?item["craft"]["mods"]["mass"].N>1:item["craft"]["mods"]["mass"].N<1);
            }
            yield return CrPage("锻造","02-forging");
            yield return CrCall("/api/crafts",J.O("operation","compose","title","松风"),j=>crExpected["writing"]=j["event"]["facts"]["item_id"]);yield return CrRefresh();
            CrCheck("writing creates a named work with its real maker",CrItem(crExpected["writing"].S)["name"].S.Contains("松风")&&CrItem(crExpected["writing"].S)["craft"]["kind"].S=="writing");yield return CrPage("题字","03-writing");
            yield return CrCall("/api/crafts",J.O("operation","treat_self"));yield return CrRefresh();CrCheck("self treatment clears the seeded part injury",Crafts["wound"]["parts"].A.Count==0);yield return CrPage("制药","04-brewing");
            yield return CrCall("/api/items",J.O("operation","craft","recipe","pill"));yield return CrRefresh();CrCheck("trained medicine creates a potent pill",Inventory["items"].A.Any(i=>i["template"].S=="pill"&&i["craft"]["potency"].N>1));
            yield return CrArena(spar,"spar",crExpected["heavy"].S);if(PhysicsArena==null)yield break;
            PhysicsArena.enemy.ApplyReferenceDamage("forearmFar",23,PhysicsArena.enemy.pelvis.B,Vector2.zero,false);PhysicsArena.player.landedHits++;PhysicsArena.hits++;yield return SavePhysicsForSmoke("surrender");yield return Client.Request("/api/npc/"+spar,null,j=>CrCheck("sparring leaves no world part injury",!j["wound"]["parts"].Has));
            yield return CrArena(rob,"rob",crExpected["heavy"].S);if(PhysicsArena==null)yield break;
            PhysicsArena.player.ApplyReferenceDamage("forearmNear",23,PhysicsArena.player.pelvis.B,Vector2.zero,false);PhysicsArena.enemy.ApplyReferenceDamage("forearmFar",23,PhysicsArena.enemy.pelvis.B,Vector2.zero,false);PhysicsArena.player.landedHits++;PhysicsArena.enemy.landedHits++;PhysicsArena.hits+=2;
            yield return SavePhysicsForSmoke("checkpoint");J frame=Martial["battle"]["frame"];
            CrCheck("hostile checkpoint records both actual part health values",frame["fighters"][0]["reference"]["parts"]["forearmNear"]["health"].N<12&&frame["fighters"][1]["reference"]["parts"]["forearmFar"]["health"].N<12);
            crExpected["battle_id"]=Martial["battle"]["id"];crExpected["frame"]=frame;File.WriteAllText(expectedPath,crExpected.ToString());yield return CharacterPicture(crOutput,"05-hostile-checkpoint");
        }else{
            crExpected=J.Parse(File.ReadAllText(expectedPath));
            CrCheck("forged weapons and writing survive cold restart",CrItem(crExpected["heavy"].S)["craft"]["temper"].S=="heavy"&&CrItem(crExpected["light"].S)["craft"]["temper"].S=="light"&&CrItem(crExpected["writing"].S)["craft"]["kind"].S=="writing");
            if(phase=="restore"){
                yield return Client.Request("/api/martial",null,j=>Martial=j);ShowBattleForSmoke();until=Time.realtimeSinceStartup+15;while(PhysicsArena==null&&Time.realtimeSinceStartup<until)yield return null;
                CrCheck("cold restart reopens the same forged hostile battle",PhysicsArena!=null&&Martial["battle"]["id"].S==crExpected["battle_id"].S);
                if(PhysicsArena==null)yield break;
                CrCheck("cold restore retains physical part damage",PhysicsArena.CaptureCampaign()["fighters"][1]["reference"]["parts"]["forearmFar"]["health"].N<12);
                yield return SavePhysicsForSmoke("surrender");J injured=null;yield return Client.Request("/api/npc/"+rob,null,j=>injured=j);
                CrCheck("robbery settlement stores a world bone injury",injured!=null&&injured["wound"]["parts"]["forearmFar"]["kind"].S=="fracture",injured?["wound"]);
                yield return InteractionActForSmoke(rob,"treat");yield return Client.Request("/api/npc/"+rob,null,j=>CrCheck("real diagnosis sets the bone and reduces severity",j["wound"]["parts"]["forearmFar"]["kind"].S!="fracture"&&j["wound"]["parts"]["forearmFar"]["severity"].N<injured["wound"]["parts"]["forearmFar"]["severity"].N,j["wound"]));
                yield return CrRefresh();CrCheck("hostile player part injury also persists",Crafts["wound"]["parts"].A.Any(p=>p["key"].S=="forearmNear"));yield return CrPage("调治","06-part-care");
                yield return CrCall("/api/crafts",J.O("operation","treat_self"));yield return CrRefresh();CrCheck("self care can set the player's bone",Crafts["wound"]["parts"].A.All(p=>p["kind"].S!="fracture"));
                yield return CrArena(assault,"assault",crExpected["light"].S);if(PhysicsArena==null)yield break;
                PhysicsArena.enemy.ApplyReferenceDamage("thighNear",29,PhysicsArena.enemy.pelvis.B,Vector2.zero,false);PhysicsArena.player.landedHits++;PhysicsArena.hits++;yield return SavePhysicsForSmoke("surrender");
                yield return Client.Request("/api/npc/"+assault,null,j=>{crExpected["final_npc_wound"]=j["wound"];CrCheck("assault also records the actual leg injury",j["wound"]["parts"]["thighNear"]["kind"].S=="fracture",j["wound"]);});
                yield return CrRefresh();crExpected["crafts"]=Crafts;crExpected["final_player_wound"]=W["player"]["wound"];File.WriteAllText(expectedPath,crExpected.ToString());yield return CrCall("/api/save",J.O());yield return CrPage("造诣","07-practised-crafts");
            }else{
                CrCheck("craft proficiency survives the second cold restart",Crafts["crafts"].ToString()==crExpected["crafts"]["crafts"].ToString());
                CrCheck("player treatment survives the second cold restart",W["player"]["wound"].ToString()==crExpected["final_player_wound"].ToString());
                yield return Client.Request("/api/npc/"+assault,null,j=>CrCheck("NPC part injuries survive the second cold restart",j["wound"].ToString()==crExpected["final_npc_wound"].ToString()));yield return CrPage("调治","08-restored-care");
            }
        }
        CrCheck("crafts and bouts do not advance the world month",W["month"].I==crExpected["month"].I);
    }
    internal void FinishCrafts(string exception=null){if(exception!=null)crErrors.Add(new J(exception));CrCheck("no native errors",crErrors.Count==0,new J(crErrors));Application.logMessageReceived-=CrLog;bool passed=crChecks.All(c=>c["passed"].B);File.WriteAllText(Path.Combine(crOutput,"report.json"),J.O("passed",passed,"checks",new J(crChecks)).ToString());Application.Quit(passed?0:2);}
}
public sealed class CraftsSmoke:MonoBehaviour {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Boot(){if(GameClient.Arg("-craftsSmoke").Length==0)return;if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("Crafts acceptance requires an isolated saveDir");Application.Quit(2);return;}new GameObject("Crafts acceptance").AddComponent<CraftsSmoke>();}
    IEnumerator Start(){yield return null;var app=FindAnyObjectByType<JianghuApp>();if(app==null){Application.Quit(2);yield break;}var run=app.VerifyCrafts();string error=null;while(true){object value=null;bool more;try{more=run.MoveNext();if(more)value=run.Current;}catch(Exception e){error=e.ToString();break;}if(!more)break;yield return value;}app.FinishCrafts(error);}
}
}
