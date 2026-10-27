using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public class EquipmentSmoke : MonoBehaviour {
    JianghuApp app;
    string output;
    readonly List<J> checks=new List<J>();
    readonly List<string> errors=new List<string>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot(){
        if(GameClient.Arg("-equipmentSmoke").Length==0)return;
        if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("Equipment acceptance requires an isolated saveDir");Application.Quit(2);return;}
        new GameObject("Equipment acceptance").AddComponent<EquipmentSmoke>();
    }
    void OnEnable(){Application.logMessageReceived+=Log;}
    void OnDisable(){Application.logMessageReceived-=Log;}
    void Log(string text,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(text);}
    void Check(string name,bool pass,J facts=null){
        checks.Add(J.O("check",name,"passed",pass,"facts",facts??new J()));
        File.WriteAllText(Path.Combine(output,"progress.json"),new J(checks).ToString());
        if(!pass)Debug.LogWarning("EQUIPMENT_ACCEPTANCE_FAILED "+name);
    }
    IEnumerator Call(string path,J body,Action<J> done=null,bool expected=true){
        bool ok=false;yield return app.Client.Request(path,body,j=>{ok=true;done?.Invoke(j);});
        Check(path,ok==expected,J.O("expected",expected,"error",app.Client.Error));
    }
    IEnumerator Equip(string operation,J item){yield return Call("/api/equipment",J.O("operation",operation,"item_id",item["id"]),j=>app.Equipment=j);}
    IEnumerator Start(){
        output=GameClient.Arg("-equipmentSmoke");Directory.CreateDirectory(output);
        float deadline=Time.realtimeSinceStartup+100;
        while(Time.realtimeSinceStartup<deadline){app=FindAnyObjectByType<JianghuApp>();if(app!=null&&app.Client.Ready&&app.W.Has)break;yield return null;}
        if(app==null||!app.Client.Ready){Check("companion ready",false);Finish();yield break;}
        var work=Run();
        while(true){
            bool more;object next=null;
            try{more=work.MoveNext();if(more)next=work.Current;}
            catch(Exception error){Check("acceptance execution",false,J.O("error",error.ToString()));break;}
            if(!more)break;yield return next;
        }
        Check("no native runtime errors",errors.Count==0,new J(errors.Select(e=>new J(e)).ToList()));Finish();
    }
    IEnumerator Run(){
        yield return Call("/api/equipment",null,j=>app.Equipment=j);
        if(GameClient.Arg("-equipmentPhase")=="resume"){
            var expected=J.Parse(File.ReadAllText(Path.Combine(app.Client.SaveDir,"expected-equipment.json")));
            Check("restart preserves exact equipped instances",app.Equipment["slots"].ToString()==expected["slots"].ToString());
            Check("restart preserves effective stats",app.Equipment["effects"].ToString()==expected["effects"].ToString());
            yield break;
        }
        yield return Call("/api/ai/config",J.O("enabled",false));
        if(!app.Hero["created"].B){yield return Call("/api/hero",app.Client.HeroBody("create","name","装备验收","sex","男","age",24,"place",app.Hero["place"]));yield return app.Client.Refresh();}
        yield return Call("/api/inventory",null,j=>app.Inventory=j);
        yield return Call("/api/market",null,j=>app.Market=j);
        Check("only main hand offhand and armor slots",app.Equipment["slots"].A.Select(s=>s["name"].S).SequenceEqual(new[]{"主手","副手","护甲"}));
        Check("ordinary accessories and tools cannot equip",app.Equipment["items"].A.All(i=>new[]{"weapon","shield","armor"}.Contains(i["slot"].S)));
        Check("no carrying weight or capacity",!app.Inventory["weight"].Has&&!app.Inventory["capacity"].Has);
        foreach(string template in new[]{"wood_shield","leather"}){
            var item=app.Market["items"].A.First(i=>i["template"].S==template);
            yield return Call("/api/items",J.O("operation","buy","item_id",item["id"]));
        }
        yield return Call("/api/equipment",null,j=>app.Equipment=j);
        var sword=app.Equipment["items"].A.First(i=>i["template"].S=="iron_sword");
        var spear=app.Equipment["items"].A.First(i=>i["template"].S=="long_spear");
        var shield=app.Equipment["items"].A.First(i=>i["template"].S=="wood_shield");
        var armor=app.Equipment["items"].A.First(i=>i["template"].S=="leather");
        yield return Equip("equip",sword);yield return Equip("equip",shield);yield return Equip("equip",armor);
        Check("all three slots active",app.Equipment["active_count"].I==3);
        double protectedAmount=app.Equipment["effects"]["combat_parameters"]["damage_reduction"].N;
        var preview=app.Equipment["items"].A.First(i=>i["id"].S==spear["id"].S)["preview"];
        Check("two handed preview explains offhand conflict",preview["warnings"].A.Count>0&&preview["changes"].A.Any(c=>c["id"].S=="defense"&&c["delta"].N==-2));
        yield return Equip("equip",spear);
        Check("shield kept but temporarily suppressed",app.Equipment["slots"][1]["status"].S=="suppressed"&&app.Equipment["slots"][1]["item"]["id"].S==shield["id"].S);
        Check("suppressed shield adds no damage reduction",app.Equipment["effects"]["combat_parameters"]["damage_reduction"].N<protectedAmount);
        app.OpenEquipmentForSmoke("weapon",spear["id"].S);
        yield return Call("/api/intervene",J.O("action","challenge","npc_id",1),j=>app.Martial=j);
        app.ShowBattleForSmoke();float deadline=Time.realtimeSinceStartup+12;
        while(app.PhysicsArena==null&&Time.realtimeSinceStartup<deadline)yield return null;
        Check("real native battle opens",app.PhysicsArena!=null);
        if(app.PhysicsArena==null)yield break;
        var fighter=app.PhysicsArena.player;
        Check("equipped main hand and suppressed offhand reach physics",fighter.weapon.id=="spear"&&!fighter.HasShield&&Math.Abs(fighter.sword.rb.mass-spear["weapon_attributes"]["mass"].N)<.001);
        Check("equipment reduction reaches real native fighter",Math.Abs(fighter.damageReduction-app.Equipment["effects"]["combat_parameters"]["damage_reduction"].N)<.001);
        yield return Call("/api/equipment",J.O("operation","unequip","item_id",spear["id"]),null,false);
        yield return app.SavePhysicsForSmoke("surrender");
        yield return Call("/api/equipment",null,j=>app.Equipment=j);
        Check("only active gear wears in battle",app.Equipment["slots"][0]["item"]["durability"].N==98&&app.Equipment["slots"][2]["item"]["durability"].N==98&&app.Equipment["slots"][1]["item"]["durability"].N==100);
        yield return Equip("equip",sword);
        Check("single hand automatically restores offhand",app.Equipment["slots"][1]["status"].S=="active"&&app.Equipment["active_count"].I==3);
        double beforeWealth=app.Equipment["wealth"].N;
        var worn=app.Equipment["items"].A.First(i=>i["id"].S==armor["id"].S);
        yield return Equip("repair",worn);
        Check("repair restores actual instance and charges silver",app.Equipment["slots"][2]["item"]["durability"].N==100&&Math.Abs(app.Equipment["wealth"].N-(beforeWealth-worn["repair_price"].N))<.001);
        yield return Equip("unequip",armor);
        Check("armor can be removed without deleting possession",app.Equipment["slots"][2]["status"].S=="empty"&&app.Equipment["items"].A.Any(i=>i["id"].S==armor["id"].S));
        yield return Equip("equip",armor);
        yield return Call("/api/npc/1",null,j=>app.Person=j);
        Check("NPC possessions show identical three slots",app.Person["possessions"]["equipment_state"]["slots"].A.Count==3);
        File.WriteAllText(Path.Combine(app.Client.SaveDir,"expected-equipment.json"),J.O("slots",app.Equipment["slots"],"effects",app.Equipment["effects"]).ToString());
    }
    void Finish(){
        bool passed=checks.All(c=>c["passed"].B)&&errors.Count==0;
        File.WriteAllText(Path.Combine(output,"acceptance.json"),J.O("passed",passed,"checks",new J(checks),"errors",new J(errors.Select(e=>new J(e)).ToList())).ToString());
        Debug.Log("EQUIPMENT_ACCEPTANCE "+passed);Application.Quit(passed?0:2);
    }
}
}
