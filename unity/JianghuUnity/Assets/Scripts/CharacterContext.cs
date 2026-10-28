using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    // The open character book owns its tabs. Selecting a gift recipient never changes this owner.
    int bookPersonId=-1;
    int dialogueReturnPerson=-1;
    bool InspectingNpc=>bookPersonId>=0;
    bool CharacterReady=>!InspectingNpc||(Person.Has&&Person["id"].I==bookPersonId);
    J CharacterInventory=>InspectingNpc?Person["possessions"]:Inventory;
    J CharacterMartial=>InspectingNpc?Person["martial_view"]:Martial["player"]["martial"];
    string CharacterName=>InspectingNpc?(CharacterReady?Person["name"].S:Npc(bookPersonId)["name"].S):(Hero["created"].B?Hero["name"].S:"过客");

    public void TalkToPerson(int id){
        SwitchBookPerson(id);dialogueReturnPerson=-1;OpenDialogue(null);
    }
    void SwitchBookPerson(int id){
        bookPersonId=id;
        if(id>=0)SelectPerson(id);
        selectedItem="";itemQuery="";itemCategory="全部";itemGrade=0;
        equipmentSelected="";mainScroll=personScroll=itemScroll=detailScroll=Vector2.zero;
        if(id>=0&&inventoryMode!="装备")inventoryMode="行囊";
        battleView=false;
    }
    void OpenCharacterPage(int page){
        if(dialogueOpen)dialogueReturnPerson=SelectedPerson;
        dialogueOpen=false;
        if(InspectingNpc&&inventoryMode!="装备")inventoryMode="行囊";
        if(page<0){personScreen=true;return;}
        personScreen=false;battleView=false;Go(page);
    }
    void OpenOwnInventory(int target=-1){
        giftTarget=target;SwitchBookPerson(-1);inventoryMode="行囊";OpenCharacterPage(2);
    }
    void DrawCharacterRail(){
        GUI.Label(new Rect(60,112,180,24),"查看对象",sub);
        DrawCharacterChoice(new Rect(60,144,180,84),-1,Hero,Hero["created"].B?Hero["name"].S:"过客","我的人物");
        var ids=recentPeople.ToArray();
        GUI.Label(new Rect(60,232,180,20),"近期查看的人物",caption);
        for(int i=0;i<ids.Length;i++){
            var n=Npc(ids[i]);if(!n.Has)continue;
            DrawCharacterChoice(new Rect(60,258+i*100,180,92),ids[i],n,n["name"].S,n["identity"]["title"].S);
        }
    }
    void DrawCharacterChoice(Rect r,int id,J actor,string name,string detail){
        Frame(r,bookPersonId==id?"btn_on":"card",8);
        Portrait(new Rect(r.x+6,r.y+6,62,r.height-12),id,actor["sex"].S,actor["age_months"].I);
        GUI.Label(new Rect(r.x+76,r.y+10,100,26),name,sub);
        GUI.Label(new Rect(r.x+76,r.y+38,100,44),detail,small);
        if(GUI.Button(r,GUIContent.none,GUIStyle.none))SwitchBookPerson(id);
    }
    void DrawOwnPersonScreen(){
        BookScreen("人物 · "+CharacterName);DrawCharacterRail();
        DrawPaperPerson(new Rect(310,156,360,490),Hero,true,"bind");
        GUILayout.BeginArea(new Rect(740,138,650,710));
        H(CharacterName);T("我的人物",sub);
        Metric("年岁",Hero["age_months"].I/12+"岁");Metric("性别",Hero["sex"].S);
        Metric("所在",Hero["place_name"].S+" · "+Hero["position_name"].S);
        Metric("身份",Hero["identity"]["grade_label"].S+" · "+Hero["identity"]["title"].S);
        Metric("银两",Num(W["player"]["wealth"]));Metric("声望",Num(W["player"]["reputation"]));
        Bar("体力",W["player"]["health"].N,100);
        GUILayout.BeginHorizontal();if(Btn("查看我的行囊"))OpenCharacterPage(2);if(Btn("查看我的战斗能力"))OpenCharacterPage(3);GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }
    void DrawInspectedEquipment(J bag){
        mainScroll=GUILayout.BeginScrollView(mainScroll);
        DrawPersonEquipment(bag);
        foreach(var slot in bag["equipment_state"]["slots"].A){
            var item=slot["item"];if(!item.Has)continue;
            GUILayout.BeginVertical(card);H(slot["name"].S+" · "+item["name"].S);
            T(item["description"].S);T(slot["reason"].S,small);
            Bar("耐久",item["durability"].N,100);
            if(item["weapon_attributes"].Has)foreach(var a in bag["weapon_attribute_catalog"].A)
                if(a["id"].S!="durability")Metric(a["name"].S,Num(item["weapon_attributes"][a["id"].S])+a["unit"].S);
            if(item["defense"].Has)Metric("防护",Num(item["defense"]));
            T("此物来处 · "+item["origin"].S,small);GUILayout.EndVertical();
        }
        GUILayout.EndScrollView();
    }
    void DrawInspectedCombat(){
        if(!CharacterReady){T("正在载入人物属性…");return;}
        var p=Person;var m=CharacterMartial;
        H(CharacterName+" · 战斗能力");
        GUILayout.BeginHorizontal();
        if(Btn("与此人交谈",p["alive"].B))OpenDialogue(null);
        if(Btn("与此人切磋",Paused&&p["alive"].B))Intervene("challenge");
        GUILayout.EndHorizontal();
        mainScroll=GUILayout.BeginScrollView(mainScroll);
        T("精力 "+Num(m["stamina"])+" · 银两 "+Num(p["wealth"]),sub);
        DrawCombatAttributes(m["combat"],p["health"].N,m["stamina"].N);
        DrawKnownMartial(m);DrawMartialCatalog();GUILayout.EndScrollView();
    }
}
}
