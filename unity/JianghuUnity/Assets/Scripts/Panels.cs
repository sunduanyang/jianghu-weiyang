using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    string inventoryMode="装备",itemQuery="",itemCategory="全部",itemSort="种类",selectedItem="",institutionKind="政权",institutionQuery="",importPath="",newPopulation="1800",newSeed="42",newYears="0";
    int giftTarget=-1;
    int itemGrade;
    bool battleView,fastMode,aiEditing,aiEnabled,aiOllama,aiAutoStart=true;
    string aiUrl="http://127.0.0.1:11435",aiModel="qwen3:4b",aiQueue="2",aiCooldown="1";
    Vector2 itemScroll,detailScroll;
    public void LoadInventory(){Client.Fetch("/api/inventory",j=>Inventory=j);Client.Fetch("/api/market",j=>Market=j);LoadEquipment();LoadEconomy();}
    void ItemAction(string operation,J item=null,string recipe=null){Client.Act("/api/items",J.O("operation",operation,"item_id",item==null?new J():item["id"],"target",giftTarget,"recipe",recipe),j=>{LoadInventory();if(SelectedPerson>=0)SelectPerson(SelectedPerson);});}
    void DrawInventory(){
        H("一器一物，皆有来处");if(!CharacterReady){T("正在整理人物行囊…");return;}
        var bag=CharacterInventory;
        if(!bag.Has){T("正在整理行囊…");return;}
        T(bag["name"].S+"的行囊  /  "+Num(bag["wealth"])+"银",sub);
        if(InspectingNpc&&Btn("从我的行囊赠物"))OpenOwnInventory(SelectedPerson);
        GUILayout.BeginHorizontal();foreach(string mode in InspectingNpc?new[]{"装备","行囊"}:new[]{"装备","行囊","货栈","制药","市井供需"})if(GUILayout.Button(mode,inventoryMode==mode?selected:skin.button)){inventoryMode=mode;selectedItem="";itemGrade=0;if(mode=="市井供需")LoadEconomy();}GUILayout.EndHorizontal();
        if(inventoryMode=="装备"){if(InspectingNpc)DrawInspectedEquipment(bag);else DrawEquipment();return;}
        if(inventoryMode=="市井供需"){DrawEconomy();return;}
        if(inventoryMode=="制药"){
            mainScroll=GUILayout.BeginScrollView(mainScroll);T("耗用行囊中的药草与2银工费，制得一份成药。",small);
            foreach(var r in Market["recipes"].A){GUILayout.BeginVertical(card);H(r["name"].S);T(r["description"].S);foreach(var ing in r["ingredients"].D)Metric(ing.Key,ing.Value.S+"份");int needed=r["ingredients"].D.Sum(k=>k.Value.I),owned=bag["items"].A.Count(i=>i["template"].S=="herb");T("行囊药草 "+owned+" / "+needed+"份",small);if(Btn("依方制成",owned>=needed&&bag["wealth"].N>=2&&!W["battle_active"].B))ItemAction("craft",null,r["template"].S);GUILayout.EndVertical();}GUILayout.EndScrollView();return;
        }
        GUILayout.BeginHorizontal();
        if(GUILayout.Button("全部品级",itemGrade==0?selected:chip,GUILayout.Width(100)))itemGrade=0;
        foreach(var grade in bag["grades"].A){var style=new GUIStyle(itemGrade==grade["grade"].I?selected:chip);TextColor(style,Hex(grade["grade_color"].S));if(GUILayout.Button(grade["grade_name"].S,style))itemGrade=itemGrade==grade["grade"].I?0:grade["grade"].I;}
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();foreach(var slot in bag["slots"].D){var gear=bag["items"].A.FirstOrDefault(x=>x["id"].S==bag["equipment"][slot.Key].S);if(GUILayout.Button(slot.Value.S+" · "+(gear==null?"未装备":gear["name"].S),chip)){if(gear!=null){inventoryMode="行囊";selectedItem=gear["id"].S;itemCategory="全部";itemQuery="";itemGrade=0;}}}GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();foreach(string category in new[]{"全部"}.Concat(bag["categories"].D.Values.Select(x=>x.S)))if(GUILayout.Button(category,itemCategory==category?selected:chip)){itemCategory=category;selectedItem="";}GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();GUILayout.Label("寻物",small,GUILayout.Width(38));itemQuery=GUILayout.TextField(itemQuery);itemSort=Cycle(itemSort,new[]{"种类","品阶","价值"});GUILayout.EndHorizontal();
        if(!InspectingNpc&&giftTarget>=0)T("赠物对象："+(W["npcs"].A.FirstOrDefault(n=>n["id"].I==giftTarget)?["name"].S??"请选择人物"),sub);
        var list=(inventoryMode=="货栈"?Market:bag)["items"].A.Where(i=>i["name"].S.Contains(itemQuery)&&(itemCategory=="全部"||i["category_label"].S==itemCategory)&&(itemGrade==0||i["grade"].I==itemGrade));
        var categoryOrder=bag["categories"].D.Keys.ToList();
        list=itemSort=="品阶"?list.OrderBy(i=>i["grade"].N):itemSort=="价值"?list.OrderByDescending(i=>i[inventoryMode=="货栈"?"buy_price":"sell_price"].N):list.OrderBy(i=>categoryOrder.IndexOf(i["category"].S)).ThenBy(i=>i["grade"].I);
        var rows=list.GroupBy(i=>i["template"].S+"/"+i["durability"].S+"/"+i["equipped"].B).Select(group=>{var representative=group.FirstOrDefault(x=>x["id"].S==selectedItem)??group.First();var tile=J.Parse(representative.ToString());tile["stack_count"]=new J(group.Count());return tile;}).ToList();
        if(!rows.Any(i=>i["id"].S==selectedItem))selectedItem=rows.FirstOrDefault()?["id"].S??"";
        GUILayout.BeginHorizontal();GUILayout.BeginVertical(GUILayout.Width(738));itemScroll=GUILayout.BeginScrollView(itemScroll,GUILayout.Height(408));
        if(itemSort=="种类"&&itemCategory=="全部"){foreach(var group in rows.GroupBy(x=>x["category_label"].S)){T(group.Key,sub);DrawItemGrid(group.ToList());}}else DrawItemGrid(rows);
        if(rows.Count==0)T("未寻到物品，试试其他分类或品级。",small);
        GUILayout.EndScrollView();GUILayout.EndVertical();
        GUILayout.BeginVertical(GUILayout.Width(402));detailScroll=GUILayout.BeginScrollView(detailScroll,GUILayout.Height(408));var i=rows.FirstOrDefault(x=>x["id"].S==selectedItem);if(i!=null){var qualityStyle=new GUIStyle(sub);TextColor(qualityStyle,Hex(i["grade_color"].S));T(i["grade_name"].S+" · "+i["grade_label"].S+" · "+i["category_label"].S,qualityStyle);H(i["name"].S);T(i["description"].S);if(new[]{"weapon","armor","accessory","tool","manual"}.Contains(i["category"].S))Bar("耐久",i["durability"].N,100);if(i["category"].S=="weapon")foreach(var attribute in bag["weapon_attribute_catalog"].A){string key=attribute["id"].S;if(key!="durability")Metric(attribute["name"].S,Num(i["weapon_attributes"][key])+attribute["unit"].S);}Metric(inventoryMode=="货栈"?"购入":"售出",Num(i[inventoryMode=="货栈"?"buy_price":"sell_price"])+"银");
            foreach(string key in new[]{"defense","income","training","recovery","heal","joy","wound","illness","learning"})if(i[key].Has)Metric(ItemLabels.ContainsKey(key)?ItemLabels[key]:key,Num(i[key]));
            if(i["category"].S=="manual")T("研读 "+i["read_progress"].S+" / "+i["pages"].S+"篇 · 所需武学 "+i["requirement"].S,small);
            T("此物来处",sub);T(i["origin"].S);if(i["origin_event"].Has&&Btn("查看初得记录"))OpenEvent(i["origin_event"].I);if(i["last_event"].Has&&Btn("查看最近流转"))OpenEvent(i["last_event"].I);
            if(i["stack_count"].I>1)T("同类 "+i["stack_count"].S+"件 · 当前物号 "+i["id"].S+" · 每次操作一件",small);
            if(!InspectingNpc&&SelectedPerson>=0&&Btn("赠物对象设为所选人物"))giftTarget=SelectedPerson;
            var ops=InspectingNpc?new J[0]:inventoryMode=="货栈"?new[]{new J("buy")}:i["operations"].A.ToArray();foreach(var op in ops){bool enabled=!W["battle_active"].B&&(op.S!="gift"||giftTarget>=0)&&(op.S!="buy"||i["buy_price"].N<=bag["wealth"].N);if(Btn(OperationLabels.TryGetValue(op.S,out var label)?label:op.S,enabled))ItemAction(op.S,i);}
        }else T("行囊空空，未寻到物品。");GUILayout.EndScrollView();GUILayout.EndVertical();GUILayout.EndHorizontal();T(InspectingNpc?CharacterName+"的随身物品 · 九品最低，一品最高":inventoryMode=="货栈"?"货栈持有有限实物 · 现银 "+Num(Market["wealth"])+"两":"九品最低，一品最高 · 同类合并显示，每件保留来源与耐久 · 每次操作自动存档",small);
    }
    void DrawItemGrid(List<J> rows){
        const int columns=5;
        for(int start=0;start<rows.Count;start+=columns){GUILayout.BeginHorizontal();for(int column=0;column<columns;column++){
            if(start+column>=rows.Count){GUILayout.Space(142);continue;}
            var item=rows[start+column];Rect r=GUILayoutUtility.GetRect(136,134,GUILayout.Width(136),GUILayout.Height(134));
            if(GUI.Button(r,GUIContent.none,selectedItem==item["id"].S?rowOnStyle:rowStyle))selectedItem=item["id"].S;
            Color color=Hex(item["grade_color"].S);Fill(new Rect(r.x+1,r.yMax-4,r.width-2,3),color);
            var badge=new GUIStyle(small){fontSize=12,alignment=TextAnchor.MiddleLeft,wordWrap=false};TextColor(badge,color);
            GUI.Label(new Rect(r.x+7,r.y+5,65,20),item["grade_name"].S,badge);
            GUI.Label(new Rect(r.x+76,r.y+5,54,20),item["equipped"].B?"已装备":"×"+item["stack_count"].S,small);
            var glyph=new GUIStyle(centerTitle){fontSize=36};TextColor(glyph,color);
            GUI.Label(new Rect(r.x+30,r.y+25,76,50),ItemGlyph(item["category"].S),glyph);
            GUI.Label(new Rect(r.x+3,r.y+78,r.width-6,25),item["name"].S,centerSmall);
            GUI.Label(new Rect(r.x+3,r.y+103,r.width-6,21),inventoryMode=="货栈"?Num(item["buy_price"])+"银":item["category"].S=="weapon"?"耐久 "+Num(item["durability"])+" / 100":"售出 "+Num(item["sell_price"])+"银",centerSmall);
        }GUILayout.EndHorizontal();}
    }
    static string ItemGlyph(string category){switch(category){case "manual":return "书";case "food":return "食";case "wine":return "酒";case "tea":return "茶";case "weapon":return "剑";case "shield":return "盾";case "armor":return "衣";case "accessory":return "玉";case "medicine":return "药";case "material":return "材";default:return "器";}}
    static readonly System.Collections.Generic.Dictionary<string,string> ItemLabels=new System.Collections.Generic.Dictionary<string,string>{{"defense","防护"},{"income","营生收益"},{"recovery","调养恢复"},{"heal","恢复体力"},{"joy","舒缓心绪"},{"wound","缓解外伤"},{"illness","缓解疾病"}};
    static readonly System.Collections.Generic.Dictionary<string,string> OperationLabels=new System.Collections.Generic.Dictionary<string,string>{{"equip","装备"},{"unequip","卸下"},{"read","研读一篇"},{"repair","修补"},{"sell","出售"},{"gift","赠予"},{"buy","购入"},{"use","使用"}};
    void DrawMartial(){
        if(InspectingNpc){DrawInspectedCombat();return;}
        GUILayout.BeginHorizontal();H("人物战斗能力");GUILayout.FlexibleSpace();if(Btn(battleView?"人物属性":"演武场 / 续战"))battleView=!battleView;GUILayout.EndHorizontal();
        if(!Martial.Has){T("正在载入人物属性…");return;}
        mainScroll=GUILayout.BeginScrollView(mainScroll);
        if(battleView){DrawBattleScreen();GUILayout.EndScrollView();return;}
        var p=Martial["player"];var m=p["martial"];
        T("精力 "+Num(m["stamina"])+" · 行囊 "+Num(p["wealth"])+"银",sub);
        DrawCombatAttributes(m["combat"],p["health"].N,m["stamina"].N);
        DrawKnownMartial(m);
        DrawMartialCatalog();
        if(Btn(showHeroRank?"收起主角品阶进退":"主角品阶进退"))showHeroRank=!showHeroRank;
        if(showHeroRank)DrawRankProgress(Hero["rank_progression"],true);
        T("调养会推进江湖一个月，恢复体力与精力。",small);
        if(Btn("调养一月",Paused))MartialAction("rest");
        GUILayout.EndScrollView();
    }
    void MartialAction(string action){Client.Act("/api/martial",J.O("action",action),j=>Martial=j);}
    public void OpenInstitution(bool country,int id){personScreen=false;dialogueOpen=false;Page=4;institutionKind=country?"政权":"门派";Client.Fetch("/api/"+(country?"country/":"faction/")+id,j=>Institution=j);}
    void DrawInstitutions(){
        GUILayout.BeginHorizontal();H("天下势力簿");institutionKind=Cycle(institutionKind,new[]{"政权","门派","乡里"});institutionQuery=GUILayout.TextField(institutionQuery);GUILayout.EndHorizontal();
        if(institutionKind=="乡里"){DrawCommunityDirectory();return;}
        GUILayout.BeginHorizontal();GUILayout.BeginVertical(GUILayout.Width(275));itemScroll=GUILayout.BeginScrollView(itemScroll);foreach(var f in W[institutionKind=="政权"?"countries":"factions"].A.Where(f=>f["name"].S.Contains(institutionQuery))){if(GUILayout.Button(f["name"].S+"\n"+(institutionKind=="政权"?f["population"].S+"人 · "+f["settlements"].S+"地区":"成员"+f["members"].S+"人 · 库银"+Num(f["fund"])),rowStyle))OpenInstitution(institutionKind=="政权",f["id"].I);}GUILayout.EndScrollView();GUILayout.EndVertical();
        GUILayout.BeginVertical(GUILayout.Width(665));mainScroll=GUILayout.BeginScrollView(mainScroll);if(Institution.Has){H(Institution["name"].S);T(Institution["ethos"].S);if(Institution["government"].Has){var g=Institution["government"];Metric("国库",Num(g["fund"])+"银");Metric("现行施政",g["policy"].S);Metric("税率",(g["tax_rate"].N*100).ToString("0.#")+"%");Metric("治安 / 公医",Num(g["security"])+" / "+Num(g["sanitation"]));DrawNationalRanks(Institution["rank_system"]);Details("国库收支与民政",g);Details("边境往来",Institution["border_activity"]);Details("外交关系",Institution["relations"]);foreach(var p in Institution["places"].A)if(Btn("城镇 · "+p["name"].S)){SelectedPlace=p["id"].I;Go(0);}}
            else{Metric("传承",Institution["tradition"].S+" · "+Institution["art"].S);Metric("驻地",Institution["place_name"].S+" · "+Institution["seat_name"].S+" · 独立9格");T(Institution["basis"].S,small);
                if(Institution["leader_name"].Has)Metric("现任主持",Institution["leader_name"].S);
                DrawSectRanks(Institution["rank_system"]);
                H("门派九品武学");DrawRankArts(Institution["martial_curriculum"]["arts"]);
                var membership=Institution["player_membership"];if(membership.Has)T("主角身份 · "+membership["name"].S+" "+membership["grade_label"].S+" "+membership["title"].S+" · 贡献"+Num(membership["contribution"]),sub);
                foreach(var option in Institution["player_options"].A){if(Btn(option["label"].S+" / "+option["points"].S+"行动力",Paused&&option["enabled"].B))HeroAction(option["action"].S,"faction",Institution["id"]);if(!option["enabled"].B)T(option["reason"].S,small);}
                if(Btn("查看门派驻地 ›")){SelectedPlace=Institution["place"].I;Go(1);}
                Metric("库银",Num(Institution["fund"])+"银");Metric("收入 / 支出",Num(Institution["revenue"])+" / "+Num(Institution["expenses"]));Metric("完成差事",Institution["tasks_completed"].S);foreach(var n in Institution["roster"].A)if(GUILayout.Button(n["name"].S+" · "+n["rank"]["grade_label"].S+" "+n["rank"]["title"].S+" · 贡献"+Num(n["contribution"]),chip))SelectPerson(n["id"].I);}
            DrawEventList(Institution["events"]);
        }else T("择一方势力，查看其成员、民政与收支。");GUILayout.EndScrollView();GUILayout.EndVertical();GUILayout.EndHorizontal();
    }
    void DrawEvents(){
        GUILayout.BeginHorizontal();H(EventDetail.Has?"循因溯果":"风起江湖");GUILayout.FlexibleSpace();if(EventDetail.Has&&Btn("返回世界年谱"))EventDetail=new J();if(Btn("刷新记录"))ReloadPage();GUILayout.EndHorizontal();
        eventScroll=GUILayout.BeginScrollView(eventScroll);if(EventDetail.Has){H(EventDetail["summary"].S);T(EventDetail["date"].S,small);foreach(var id in EventDetail["people"].A){var n=W["npcs"].A.FirstOrDefault(x=>x["id"].I==id.I);if(n!=null&&Btn(n["name"].S))SelectPerson(id.I);}Details("共同事实",EventDetail["facts"],true);foreach(var e in EventDetail["cause_events"].A)if(Btn("前因 · "+e["summary"].S))OpenEvent(e["id"].I);Details("各人理解",EventDetail["perspectives"],true);}else{DrawEventList(Events);if(Events.A.Count>0&&Btn("更早记录"))Client.Fetch("/api/events?before="+Events.A.Last()["id"].S,j=>Events=new J(Events.A.Concat(j.A).ToList()));}GUILayout.EndScrollView();
    }
    void DrawAI(){
        H("共享一盏心灯");mainScroll=GUILayout.BeginScrollView(mainScroll);var config=W["ai"]["config"];T("游玩期间，人物持续在后台轮换思考，没有每月人数上限。这个月停留越久，能完成的思考就越多。",small);
        if(!aiEditing){aiEnabled=config["enabled"].B;aiOllama=config["backend"].S=="ollama";aiUrl=config["url"].S;aiModel=config["model"].S;aiQueue=config["queue_limit"].S;aiCooldown=config["cooldown_months"].S;aiAutoStart=config["auto_start"].B;aiEditing=true;}
        aiEnabled=GUILayout.Toggle(aiEnabled,"启用真实 AI 决策");bool prior=aiOllama;aiOllama=GUILayout.Toggle(aiOllama,"使用 Ollama（不勾选则使用 llama.cpp）");if(prior!=aiOllama)aiUrl=aiOllama?"http://127.0.0.1:11434":"http://127.0.0.1:11435";
        T("本机模型地址",sub);aiUrl=GUILayout.TextField(aiUrl);T("模型名称",sub);aiModel=GUILayout.TextField(aiModel);T("后台待办数量 / 同一人物重复思考间隔（月）",sub);GUILayout.BeginHorizontal();aiQueue=GUILayout.TextField(aiQueue);aiCooldown=GUILayout.TextField(aiCooldown);GUILayout.EndHorizontal();
        aiAutoStart=GUILayout.Toggle(aiAutoStart,"需要时自动启动已安装的本机模型");
        if(Btn("保存模型设置")&&int.TryParse(aiQueue,out int slots)&&int.TryParse(aiCooldown,out int cooldown))Client.Act("/api/ai/config",J.O("enabled",aiEnabled,"backend",aiOllama?"ollama":"llamacpp","url",aiUrl,"model",aiModel,"queue_limit",slots,"cooldown_months",cooldown,"auto_start",aiAutoStart));
        fastMode=GUILayout.Toggle(fastMode,"世界推演使用规则快进（跳过新推理，保留已有方针）");
        T("过月不等待模型。已完成的计划和方针继续生效，尚未开始的旧排程会按当前处境更新。小队列用于控制本机负载，不限制本月思考人数。",small);
        Metric("模型请求",W["ai"]["calls"].S);Metric("完成思考 / 失败思考",W["ai"]["successes"].S+" / "+W["ai"]["failed"].S);Metric("已覆盖人物",W["ai"]["coverage"].S+" / "+W["ai"]["eligible"].S);Metric("排队 / 推理中",W["ai"]["queued"].S+" / "+W["ai"]["pending"].S);
        Metric("本月已思考",W["ai"]["month_completed"].S+"人 · 无人数上限");
        Metric("本次后台完成",W["ai"]["session_applied"].S+"次");
        Metric("完成深入思考",W["ai"]["deep_completed"].S+"次");
        Metric("近期玩家往来 / 高位人物",W["ai"]["recent_player_contacts"].S+" / "+W["ai"]["high_status_attention"].S+"人");
        if(W["ai"]["busy"].B)T("正在思考 · "+W["ai"]["current_name"].S+" · "+W["ai"]["current_depth"].S+" · "+W["ai"]["current_stage"].S,small);
        Metric("有效生活方针",W["ai"]["policy_active"].S+"人");
        Metric("模型计划实际执行",W["ai"]["plan_executions"].S+"步");
        Metric("模型方针参与行动",W["ai"]["policy_actions"].S+"次");
        Metric("方针改变候选排序",W["ai"]["policy_ranking_changes"].S+"次");
        if(W["ai"]["last_error"].S.Length>0)T("最近模型状态："+W["ai"]["last_error"].S,small);
        if(Btn("刷新推理账本"))Client.Fetch("/api/ai/logs",j=>Logs=j);foreach(var log in Logs.A)Details("推理记录 "+log["npc_id"].S+" / "+log["month"].S,log);
        GUILayout.EndScrollView();
    }
    void DrawSettings(){
        H("存档与世界设置");mainScroll=GUILayout.BeginScrollView(mainScroll);T("当前独立存档",sub);T(W["database"].S,small);GUILayout.BeginHorizontal();if(Btn("立即保存"))Client.Act("/api/save",J.O());if(Btn("打开存档目录"))Application.OpenURL(Client.SaveDir);GUILayout.EndHorizontal();
        T("导入现有江湖",heading);T("输入原版 .sqlite 的完整路径。导入会创建新副本，原档和当前 Unity 世界均保留。",small);importPath=GUILayout.TextField(importPath);
        #if UNITY_EDITOR
        if(Btn("选择存档文件")){string path=UnityEditor.EditorUtility.OpenFilePanel("选择江湖存档","","sqlite");if(path.Length>0)importPath=path;}
        #endif
        if(Btn("复制并载入存档",Paused&&File.Exists(importPath)))Client.Act("/api/unity/import",J.O("path",importPath),j=>{Person=new J();Institution=new J();EventDetail=new J();SelectedPerson=-1;});
        GUILayout.Space(18);H("另起一方江湖");T("初始人口（30–3000，默认1800）",sub);newPopulation=GUILayout.TextField(newPopulation);T("固定种子",sub);newSeed=GUILayout.TextField(newSeed);T("历史预演年数（0–50）",sub);newYears=GUILayout.TextField(newYears);
        if(Btn("建立新世界，保留旧档",Paused)&&int.TryParse(newPopulation,out int population)&&int.TryParse(newSeed,out int seed)&&int.TryParse(newYears,out int years))Client.Act("/api/new",J.O("population",population,"seed",seed,"prehistory",years),j=>{Person=new J();SelectedPerson=-1;Institution=new J();City=new J();creatingHero=false;});
        GUILayout.Space(12);T("Unity 客户端负责原生界面、地图与皮影动画；随游戏启动的本机模拟引擎负责社会、战斗、存档与本地 AI。关闭 Unity 时会保存并退出本次模拟进程。",small);GUILayout.EndScrollView();
    }
}
}
