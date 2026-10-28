using System;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    public J Equipment=new J();
    string equipmentSlot="weapon",equipmentSelected="";
    Vector2 equipmentListScroll,equipmentDetailScroll;

    public void LoadEquipment(){Client.Fetch("/api/equipment",j=>Equipment=j);}
    void EquipmentAction(string operation,J item){
        Client.Act("/api/equipment",J.O("operation",operation,"item_id",item["id"]),j=>{
            Equipment=j;
            Client.Fetch("/api/inventory",pack=>Inventory=pack);
            Client.Fetch("/api/market",shop=>Market=shop);
            if(SelectedPerson>=0)SelectPerson(SelectedPerson);
        });
    }
    static string EquipmentStatus(string status){
        switch(status){case "active":return "生效中";case "broken":return "已损坏";case "suppressed":return "暂时停用";default:return "未装备";}
    }
    void ChooseEquipmentSlot(J slot){
        equipmentSlot=slot["slot"].S;equipmentSelected=slot["item"]["id"].S;
        equipmentListScroll=equipmentDetailScroll=Vector2.zero;
    }
    public void OpenEquipmentForSmoke(string slot="weapon",string item=""){
        ViewPerson(-1,PersonTab.Bag);inventoryMode="装备";equipmentSlot=slot;equipmentSelected=item;
        equipmentListScroll=equipmentDetailScroll=Vector2.zero;
    }
    void DrawEquipment(){
        if(!Equipment.Has){T("正在整理随身装备…");return;}
        T(Equipment["locked"].B?Equipment["lock_reason"].S:"选择主手、副手或护甲，查看并替换随身装备。",small);
        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(GUILayout.Width(260));
        foreach(var slot in Equipment["slots"].A){
            Rect r=GUILayoutUtility.GetRect(254,110,GUILayout.Width(254),GUILayout.Height(110));
            if(GUI.Button(r,GUIContent.none,slot["slot"].S==equipmentSlot?rowOnStyle:rowStyle))ChooseEquipmentSlot(slot);
            J worn=slot["item"];
            GUI.Label(new Rect(r.x+12,r.y+9,90,25),slot["name"].S,sub);
            var status=new GUIStyle(small){alignment=TextAnchor.MiddleRight};
            TextColor(status,slot["status"].S=="active"?jade:slot["status"].S=="empty"?muted:brass);
            GUI.Label(new Rect(r.x+113,r.y+9,128,25),EquipmentStatus(slot["status"].S),status);
            var name=new GUIStyle(text){fontSize=19};TextColor(name,worn.Has?Hex(worn["grade_color"].S):muted);
            GUI.Label(new Rect(r.x+12,r.y+38,r.width-24,30),worn.Has?worn["name"].S:"空位",name);
            GUI.Label(new Rect(r.x+12,r.y+73,r.width-24,27),worn.Has?worn["grade_name"].S+" · 耐久 "+Num(worn["durability"])+" / 100":"点击选择可用装备",small);
        }
        GUILayout.Space(12);H("当前生效");
        Metric("主手",Equipment["effects"]["weapon_name"].S);
        Metric("装备防护",Num(Equipment["effects"]["bonuses"]["defense"]));
        Metric("受伤减免",(Equipment["effects"]["combat_parameters"]["damage_reduction"].N*100).ToString("0.#")+"%");
        T("双手兵器占用副手；换回单手兵器后，完好的副手装备自动恢复生效。",small);
        GUILayout.EndVertical();GUILayout.Space(10);

        GUILayout.BeginVertical(GUILayout.Width(382));
        var candidates=Equipment["items"].A.Where(i=>i["slot"].S==equipmentSlot).OrderBy(i=>i["grade"].I).ThenBy(i=>i["name"].S).ToList();
        string label=Equipment["slots"].A.First(s=>s["slot"].S==equipmentSlot)["name"].S;
        H(label+" · 可选装备 "+candidates.Count);
        if(!candidates.Any(i=>i["id"].S==equipmentSelected))equipmentSelected=candidates.FirstOrDefault(i=>i["preview"]["equipped"].B)?["id"].S??candidates.FirstOrDefault()?["id"].S??"";
        equipmentListScroll=GUILayout.BeginScrollView(equipmentListScroll,GUILayout.Height(500));
        foreach(var item in candidates){
            Rect r=GUILayoutUtility.GetRect(358,88,GUILayout.ExpandWidth(true),GUILayout.Height(88));
            if(GUI.Button(r,GUIContent.none,item["id"].S==equipmentSelected?rowOnStyle:rowStyle)){equipmentSelected=item["id"].S;equipmentDetailScroll=Vector2.zero;}
            var name=new GUIStyle(sub);TextColor(name,Hex(item["grade_color"].S));
            GUI.Label(new Rect(r.x+12,r.y+8,r.width-24,28),item["name"].S,name);
            GUI.Label(new Rect(r.x+12,r.y+39,r.width-24,20),item["grade_name"].S+" · 耐久 "+Num(item["durability"])+" / 100",small);
            GUI.Label(new Rect(r.x+12,r.y+62,r.width-24,20),item["preview"]["equipped"].B?"当前装备":item["durability"].N<=0?"已损坏 · 需先修补":"可替换",small);
        }
        if(candidates.Count==0)T("行囊中暂无此类装备，可前往货栈购置。",small);
        GUILayout.EndScrollView();
        if(Btn("前往货栈")){string category=equipmentSlot=="weapon"?"武器":equipmentSlot=="shield"?"盾牌":"护甲";Later(()=>{OpenMarket("货栈");itemCategory=category;selectedItem="";itemQuery="";});}
        GUILayout.EndVertical();GUILayout.Space(10);

        GUILayout.BeginVertical(GUILayout.Width(468));
        equipmentDetailScroll=GUILayout.BeginScrollView(equipmentDetailScroll,GUILayout.Height(596));
        var selectedGear=candidates.FirstOrDefault(i=>i["id"].S==equipmentSelected);
        if(selectedGear!=null)DrawEquipmentDetail(selectedGear);
        else{H("装备详情");T("选择一件装备，查看属性与替换后的变化。",small);}
        GUILayout.EndScrollView();GUILayout.EndVertical();
        GUILayout.EndHorizontal();
    }
    void DrawEquipmentDetail(J item){
        var gradeStyle=new GUIStyle(sub);TextColor(gradeStyle,Hex(item["grade_color"].S));
        T(item["grade_name"].S+" · "+item["slot_name"].S,gradeStyle);H(item["name"].S);T(item["description"].S,small);
        Bar("耐久",item["durability"].N,100);
        if(item["slot"].S=="weapon")foreach(var attribute in Equipment["weapon_attribute_catalog"].A){
            string key=attribute["id"].S;if(key!="durability")Metric(attribute["name"].S,Num(item["weapon_attributes"][key])+attribute["unit"].S);
        }else if(item["defense"].Has)Metric("防护",Num(item["defense"]));
        foreach(var warning in item["preview"]["warnings"].A)T(warning.S,small);
        var worn=Equipment["slots"].A.First(s=>s["slot"].S==item["slot"].S);
        if(item["preview"]["equipped"].B)T(worn["reason"].S,small);
        bool usable=!Equipment["locked"].B&&Equipment["alive"].B;
        GUILayout.BeginHorizontal();
        if(item["preview"]["equipped"].B){if(Btn("卸下装备",usable))EquipmentAction("unequip",item);}
        else if(Btn(item["durability"].N<=0?"已损坏，需先修补":"装备到"+item["slot_name"].S,usable&&item["durability"].N>0))EquipmentAction("equip",item);
        if(item["durability"].N<100){
            string material=item["slot"].S=="shield"?"timber":"iron";
            bool supplied=Inventory["items"].A.Any(i=>i["template"].S==material);
            if(Btn("修补 · "+Num(item["repair_price"])+"银",usable&&supplied&&Equipment["wealth"].N>=item["repair_price"].N))EquipmentAction("repair",item);
        }
        GUILayout.EndHorizontal();
        if(item["durability"].N<100)T("修补需1份"+(item["slot"].S=="shield"?"硬木料":"生铁")+"及工费。",small);
        if(!item["preview"]["equipped"].B&&item["durability"].N>0){
            H("替换后变化");
            foreach(var change in item["preview"]["changes"].A){
                GUILayout.BeginHorizontal();T(change["name"].S,small);GUILayout.FlexibleSpace();
                double delta=change["delta"].N;string unit=change["unit"].S;
                var value=new GUIStyle(small){alignment=TextAnchor.MiddleRight};
                TextColor(value,delta==0||change["direction"].N==0?muted:delta*change["direction"].N>0?jade:brass);
                GUILayout.Label(Num(change["before"])+unit+" → "+Num(change["after"])+unit+(delta==0?"":"  ("+(delta>0?"+":"")+Num(change["delta"])+")"),value);
                GUILayout.EndHorizontal();
            }
            if(item["preview"]["changes"].A.Count==0)T("当前生效数值不变。",small);
        }
    }
    void DrawPersonEquipment(J possessions){
        H("随身装备");
        foreach(var slot in possessions["equipment_state"]["slots"].A){
            Metric(slot["name"].S,slot["item"].Has?slot["item"]["name"].S:"未装备");
            if(slot["item"].Has)T(EquipmentStatus(slot["status"].S)+" · 耐久 "+Num(slot["item"]["durability"])+" / 100",small);
        }
    }
}
}
