using System.Linq;
using UnityEngine;

namespace Jianghu {
// 人物卷与对话所看之人。主人记在导航栈顶的视图上（UiView.Person），选赠物对象不会改变它。
public partial class JianghuApp {
    bool InspectingNpc=>FocusPersonId>=0;
    bool CharacterReady=>!InspectingNpc||(Person.Has&&Person["id"].I==FocusPersonId);
    J CharacterInventory=>InspectingNpc?Person["possessions"]:Inventory;
    J CharacterMartial=>InspectingNpc?Person["martial_view"]:Martial["player"]["martial"];
    string HeroName=>Hero["created"].B?Hero["name"].S:"过客";
    string CharacterName=>InspectingNpc?(CharacterReady?Person["name"].S:Npc(FocusPersonId)["name"].S):HeroName;

    void ResetItemFilters(int owner){
        selectedItem="";itemQuery="";itemCategory="全部";equipmentSelected="";
        mainScroll=personScroll=itemScroll=detailScroll=Vector2.zero;
        if(owner>=0&&inventoryMode!="装备")inventoryMode="行囊";
    }
    // 赠物：打开主角自己的行囊，并记下受赠之人。
    void OpenOwnInventory(int target=-1){
        giftTarget=target;inventoryMode="行囊";
        if(TopIs(ViewKind.Person)){SwitchSubject(-1);SetTab((int)PersonTab.Bag);}
        else ViewPerson(-1,PersonTab.Bag);
    }
    // 人物卷左侧：主角与最近看过的人，点一下即切换卷主，标签不变。
    void DrawCharacterRail(){
        GUI.Label(new Rect(60,112,180,24),"查看对象",sub);
        DrawCharacterChoice(new Rect(60,144,180,84),-1,Hero,HeroName,"我的人物");
        var ids=recentPeople.ToArray();
        GUI.Label(new Rect(60,232,180,20),"近期查看的人物",caption);
        for(int i=0;i<ids.Length;i++){
            var n=Npc(ids[i]);if(!n.Has)continue;
            DrawCharacterChoice(new Rect(60,258+i*100,180,92),ids[i],n,n["name"].S,n["identity"]["title"].S);
        }
    }
    void DrawCharacterChoice(Rect r,int id,J actor,string name,string detail){
        Frame(r,FocusPersonId==id?"btn_on":"card",8);
        Portrait(new Rect(r.x+6,r.y+6,62,r.height-12),id,actor["sex"].S,actor["age_months"].I);
        GUI.Label(new Rect(r.x+76,r.y+10,100,26),name,sub);
        GUI.Label(new Rect(r.x+76,r.y+38,100,44),detail,small);
        if(GUI.Button(r,GUIContent.none,GUIStyle.none)&&FocusPersonId!=id)Later(()=>SwitchSubject(id));
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
}
}
