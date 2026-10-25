using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    string inventoryMode="行囊",itemQuery="",itemCategory="全部",itemSort="种类",selectedItem="",institutionKind="政权",institutionQuery="",importPath="",newPopulation="800",newSeed="42",newYears="0";
    int giftTarget=-1;
    bool battleView,fastMode=true,aiEditing,aiEnabled,aiOllama;
    string aiUrl="http://127.0.0.1:11435",aiModel="qwen3:4b",aiBudget="24",aiCooldown="3";
    Vector2 itemScroll,detailScroll;
    public void LoadInventory(){Client.Fetch("/api/inventory",j=>Inventory=j);Client.Fetch("/api/market",j=>Market=j);}
    void ItemAction(string operation,J item=null,string recipe=null){Client.Act("/api/items",J.O("operation",operation,"item_id",item==null?new J():item["id"],"target",giftTarget,"recipe",recipe),j=>{LoadInventory();if(SelectedPerson>=0)SelectPerson(SelectedPerson);});}
    void DrawInventory(){
        H("一器一物，皆有来处");if(!Inventory.Has){T("正在整理行囊…");return;}
        T(Inventory["name"].S+"  /  "+Num(Inventory["wealth"])+"银 · 负重 "+Num(Inventory["weight"])+" / "+Num(Inventory["capacity"])+"斤",sub);
        GUILayout.BeginHorizontal();foreach(string mode in new[]{"行囊","货栈","制药"})if(GUILayout.Button(mode,inventoryMode==mode?selected:skin.button)){inventoryMode=mode;selectedItem="";}GUILayout.EndHorizontal();
        if(inventoryMode=="制药"){
            mainScroll=GUILayout.BeginScrollView(mainScroll);T("耗用行囊中的药草与2银工费，制得一份成药。",small);
            foreach(var r in Market["recipes"].A){GUILayout.BeginVertical(card);H(r["name"].S);T(r["description"].S);foreach(var ing in r["ingredients"].D)Metric(ing.Key,ing.Value.S+"份");int needed=r["ingredients"].D.Sum(k=>k.Value.I),owned=Inventory["items"].A.Count(i=>i["template"].S=="herb");T("行囊药草 "+owned+" / "+needed+"份",small);if(Btn("依方制成",owned>=needed&&Inventory["wealth"].N>=2&&!W["battle_active"].B))ItemAction("craft",null,r["template"].S);GUILayout.EndVertical();}GUILayout.EndScrollView();return;
        }
        GUILayout.BeginHorizontal();itemQuery=GUILayout.TextField(itemQuery);itemCategory=Cycle(itemCategory,new[]{"全部","兵刃","衣甲","佩饰","器具","丹药","饮食","典籍","材料"});itemSort=Cycle(itemSort,new[]{"种类","品阶","价值","重量"});GUILayout.EndHorizontal();
        if(giftTarget>=0)T("赠物对象："+(W["npcs"].A.FirstOrDefault(n=>n["id"].I==giftTarget)?["name"].S??"请选择人物"),sub);
        var list=(inventoryMode=="货栈"?Market:Inventory)["items"].A.Where(i=>i["name"].S.Contains(itemQuery)&&(itemCategory=="全部"||i["category_label"].S==itemCategory));
        list=itemSort=="品阶"?list.OrderBy(i=>i["grade"].N):itemSort=="价值"?list.OrderByDescending(i=>i["price"].N):itemSort=="重量"?list.OrderByDescending(i=>i["weight"].N):list.OrderBy(i=>i["category"].S);var rows=list.ToList();
        if(!rows.Any(i=>i["id"].S==selectedItem))selectedItem=rows.FirstOrDefault()?["id"].S??"";
        GUILayout.BeginHorizontal();GUILayout.BeginVertical(GUILayout.Width(445));itemScroll=GUILayout.BeginScrollView(itemScroll);
        foreach(var item in rows){if(GUILayout.Button(item["name"].S+"  /  "+item["grade"].S+"品 · "+item["category_label"].S+"\n"+(inventoryMode=="货栈"?Num(item["buy_price"])+"银":Num(item["weight"])+"斤")+(item["equipped"].B?" · 已装备":""),selectedItem==item["id"].S?rowOnStyle:rowStyle,GUILayout.MinHeight(57)))selectedItem=item["id"].S;}GUILayout.EndScrollView();GUILayout.EndVertical();
        GUILayout.BeginVertical(GUILayout.Width(485));detailScroll=GUILayout.BeginScrollView(detailScroll);var i=rows.FirstOrDefault(x=>x["id"].S==selectedItem);if(i!=null){H(i["name"].S);T(i["description"].S);T(i["grade"].S+"品 · "+i["grade_label"].S+" · "+i["category_label"].S,small);Bar("耐久",i["durability"].N,100);Metric("重量",Num(i["weight"])+"斤");Metric(inventoryMode=="货栈"?"购入":"售出",Num(i[inventoryMode=="货栈"?"buy_price":"sell_price"])+"银");
            foreach(string key in new[]{"attack","defense","income","training","recovery","heal","wound","illness","learning"})if(i[key].Has)Metric(ItemLabels.ContainsKey(key)?ItemLabels[key]:key,Num(i[key]));
            if(i["category"].S=="manual")T("研读 "+i["read_progress"].S+" / "+i["pages"].S+"篇 · 所需武学 "+i["requirement"].S,small);
            T("此物来处",sub);T(i["origin"].S);if(i["origin_event"].Has&&Btn("查看初得记录"))OpenEvent(i["origin_event"].I);if(i["last_event"].Has&&Btn("查看最近流转"))OpenEvent(i["last_event"].I);
            if(SelectedPerson>=0&&Btn("赠物对象设为所选人物"))giftTarget=SelectedPerson;
            var ops=inventoryMode=="货栈"?new[]{new J("buy")}:i["operations"].A.ToArray();foreach(var op in ops){bool enabled=!W["battle_active"].B&&(op.S!="gift"||giftTarget>=0)&&(op.S!="buy"||i["buy_price"].N<=Inventory["wealth"].N&&i["weight"].N+Inventory["weight"].N<=Inventory["capacity"].N);if(Btn(OperationLabels.TryGetValue(op.S,out var label)?label:op.S,enabled))ItemAction(op.S,i);}
        }else T("行囊空空，未寻到物品。");GUILayout.EndScrollView();GUILayout.EndVertical();GUILayout.EndHorizontal();T(inventoryMode=="货栈"?"货栈持有有限实物 · 现银 "+Num(Market["wealth"])+"两":"四个独立装备部位 · 每次操作自动存档",small);
    }
    static readonly System.Collections.Generic.Dictionary<string,string> ItemLabels=new System.Collections.Generic.Dictionary<string,string>{{"attack","攻势"},{"defense","防护"},{"income","营生收益"},{"training","习武增益"},{"recovery","调养恢复"},{"heal","恢复体力"},{"wound","缓解外伤"},{"illness","缓解疾病"},{"learning","研读进益"}};
    static readonly System.Collections.Generic.Dictionary<string,string> OperationLabels=new System.Collections.Generic.Dictionary<string,string>{{"equip","装备"},{"unequip","卸下"},{"read","研读一篇"},{"repair","修补"},{"sell","出售"},{"gift","赠予"},{"buy","购入"},{"use","使用"}};
    void DrawMartial(){
        GUILayout.BeginHorizontal();H("练一身本领，赴一场江湖");GUILayout.FlexibleSpace();if(Btn(battleView?"功法与闭关":"演武场 / 续战"))battleView=!battleView;GUILayout.EndHorizontal();if(!Martial.Has){T("正在展开武谱…");return;}
        mainScroll=GUILayout.BeginScrollView(mainScroll);
        if(battleView){DrawBattle();GUILayout.EndScrollView();return;}
        var p=Martial["player"];var m=p["martial"];T("武学 "+Num(p["skill"])+" · 精力 "+Num(m["stamina"])+" · 内力 "+Num(m["stats"]["qi"])+" · 行囊 "+Num(p["wealth"])+"银",sub);
        T("闭关、突破、调养都会推进整个江湖一个月。修习与突破需3银、12精力；每月恢复8精力。",small);if(Btn("调养一月",Paused))MartialAction("rest");DrawArtList(m["arts"],true);
        H("手抄武谱");foreach(var art in Martial["catalog"].A){GUILayout.BeginVertical(card);T(art["name"].S+" · "+art["affinity"].S,sub);T(art["description"].S);if(Btn("研读 · 12银 / 武学需"+art["requirement"].S,Paused&&p["skill"].N>=art["requirement"].N))MartialAction("learn",art["id"].S);GUILayout.EndVertical();}GUILayout.EndScrollView();
    }
    void DrawArtList(J arts,bool interactive){foreach(var art in arts.A){GUILayout.BeginVertical(card);T(art["name"].S+" · "+art["realm_name"].S+(art["equipped"].B?" · 运用中":""),sub);T(art["description"].S,small);Bar("修习",art["practice"].N,art["gate"].N);if(art["reach"].Has)T("距离 "+art["reach"][0].S+"–"+art["reach"][1].S+" · "+art["qi"].S+"内力 · "+art["forms"].S+"式",small);if(interactive){GUILayout.BeginHorizontal();if(Btn(art["ready"].B?"突破关隘":"闭关修习",Paused&&art["realm"].I<3))MartialAction(art["ready"].B?"breakthrough":"practice",art["id"].S);if(Btn(art["equipped"].B?"已装配 / 卸下":"装配",Paused))MartialAction("equip",art["id"].S);GUILayout.EndHorizontal();}GUILayout.EndVertical();}}
    void MartialAction(string action,string art=null){Client.Act("/api/martial",J.O("action",action,"art",art),j=>Martial=j);}
    void DrawBattle(){
        var b=Martial["battle"];if(!b.Has){H("尚未与人切磋");T("在右侧人物档案选择一位成年、体力充足的江湖人，点击「切磋」进入演武场。");return;}
        H(b["status"].S=="finished"?"已收招 · "+b["reason"].S:"第 "+b["round"].S+" 合 · 对手意图："+b["opponent_intent"].S);
        GUILayout.BeginHorizontal();foreach(var f in b["fighters"].A){GUILayout.BeginVertical(card,GUILayout.Width(455));H(f["name"].S);T(f["inner"].S,small);Bar("气血",f["vigor"].N,f["vigor_max"].N);Bar("内力",f["qi"].N,f["qi_max"].N);T("式 "+f["forms"].S+" · 外伤 "+Num(f["outer_wounds"])+" / 内伤 "+Num(f["inner_wounds"]),small);GUILayout.EndVertical();}GUILayout.EndHorizontal();
        Metric("身位距离",b["distance"].S+"格");
        if(b["status"].S!="finished")foreach(var a in b["actions"].A){if(Btn(a["label"].S+(a["reason"].S.Length>0?" · "+a["reason"].S:""),a["enabled"].B))Client.Act("/api/battle",J.O("battle_id",b["id"],"round",b["round"],"action",a["id"]),j=>Martial=j);}
        else if(b["event_id"].Has&&Btn("查看此战年谱"))OpenEvent(b["event_id"].I);
        T("拆招记录",sub);foreach(var row in b["log"].A.AsEnumerable().Reverse())T(row["round"].S+"合 · "+(row["side"].I==0?"我":"客")+" · "+row["text"].S,small);
    }
    public void OpenInstitution(bool country,int id){Page=4;institutionKind=country?"政权":"门派";Client.Fetch("/api/"+(country?"country/":"faction/")+id,j=>Institution=j);}
    void DrawInstitutions(){
        GUILayout.BeginHorizontal();H("天下势力簿");institutionKind=Cycle(institutionKind,new[]{"政权","门派"});institutionQuery=GUILayout.TextField(institutionQuery);GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();GUILayout.BeginVertical(GUILayout.Width(275));itemScroll=GUILayout.BeginScrollView(itemScroll);foreach(var f in W[institutionKind=="政权"?"countries":"factions"].A.Where(f=>f["name"].S.Contains(institutionQuery))){if(GUILayout.Button(f["name"].S+"\n"+(institutionKind=="政权"?f["population"].S+"人 · "+f["settlements"].S+"城":"成员"+f["members"].S+"人 · 库银"+Num(f["fund"])),rowStyle))OpenInstitution(institutionKind=="政权",f["id"].I);}GUILayout.EndScrollView();GUILayout.EndVertical();
        GUILayout.BeginVertical(GUILayout.Width(665));mainScroll=GUILayout.BeginScrollView(mainScroll);if(Institution.Has){H(Institution["name"].S);T(Institution["ethos"].S);if(Institution["government"].Has){var g=Institution["government"];Metric("国库",Num(g["fund"])+"银");Metric("现行施政",g["policy"].S);Metric("税率",(g["tax_rate"].N*100).ToString("0.#")+"%");Metric("治安 / 公医",Num(g["security"])+" / "+Num(g["sanitation"]));foreach(var office in g["offices"].D){int id=office.Value.I;var n=W["npcs"].A.FirstOrDefault(x=>x["id"].I==id);if(GUILayout.Button(office.Key+" · "+(n==null?"虚位":n["name"].S),chip)&&n!=null)SelectPerson(id);}Details("国库收支与民政",g);Details("边境往来",Institution["border_activity"]);Details("外交关系",Institution["relations"]);foreach(var p in Institution["places"].A)if(Btn("城镇 · "+p["name"].S)){SelectedPlace=p["id"].I;Go(0);}}
            else{Metric("库银",Num(Institution["fund"])+"银");Metric("收入 / 支出",Num(Institution["revenue"])+" / "+Num(Institution["expenses"]));Metric("完成差事",Institution["tasks_completed"].S);foreach(var n in Institution["roster"].A)if(GUILayout.Button(n["name"].S+" · "+n["rank"]["title"].S+" · 贡献"+Num(n["contribution"]),chip))SelectPerson(n["id"].I);}
            DrawEventList(Institution["events"]);
        }else T("择一方势力，查看其成员、民政与收支。");GUILayout.EndScrollView();GUILayout.EndVertical();GUILayout.EndHorizontal();
    }
    void DrawEvents(){
        GUILayout.BeginHorizontal();H(EventDetail.Has?"循因溯果":"风起江湖");GUILayout.FlexibleSpace();if(EventDetail.Has&&Btn("返回世界年谱"))EventDetail=new J();if(Btn("刷新记录"))ReloadPage();GUILayout.EndHorizontal();
        eventScroll=GUILayout.BeginScrollView(eventScroll);if(EventDetail.Has){H(EventDetail["summary"].S);T(EventDetail["date"].S,small);foreach(var id in EventDetail["people"].A){var n=W["npcs"].A.FirstOrDefault(x=>x["id"].I==id.I);if(n!=null&&Btn(n["name"].S))SelectPerson(id.I);}Details("共同事实",EventDetail["facts"],true);foreach(var e in EventDetail["cause_events"].A)if(Btn("前因 · "+e["summary"].S))OpenEvent(e["id"].I);Details("各人理解",EventDetail["perspectives"],true);}else{DrawEventList(Events);if(Events.A.Count>0&&Btn("更早记录"))Client.Fetch("/api/events?before="+Events.A.Last()["id"].S,j=>Events=new J(Events.A.Concat(j.A).ToList()));}GUILayout.EndScrollView();
    }
    void DrawAI(){
        H("共享一盏心灯");mainScroll=GUILayout.BeginScrollView(mainScroll);var config=W["ai"]["config"];T("人物共享一个本地模型。关闭模型后，完整规则世界继续运行。",small);
        if(!aiEditing){aiEnabled=config["enabled"].B;aiOllama=config["backend"].S=="ollama";aiUrl=config["url"].S;aiModel=config["model"].S;aiBudget=config["reviews_per_month"].S;aiCooldown=config["cooldown_months"].S;aiEditing=true;}
        aiEnabled=GUILayout.Toggle(aiEnabled,"启用真实 AI 决策");bool prior=aiOllama;aiOllama=GUILayout.Toggle(aiOllama,"使用 Ollama（不勾选则使用 llama.cpp）");if(prior!=aiOllama)aiUrl=aiOllama?"http://127.0.0.1:11434":"http://127.0.0.1:11435";
        T("本机模型地址",sub);aiUrl=GUILayout.TextField(aiUrl);T("模型名称",sub);aiModel=GUILayout.TextField(aiModel);T("每月反思人数 / 冷却月数",sub);GUILayout.BeginHorizontal();aiBudget=GUILayout.TextField(aiBudget);aiCooldown=GUILayout.TextField(aiCooldown);GUILayout.EndHorizontal();
        if(Btn("保存模型设置")&&int.TryParse(aiBudget,out int budget)&&int.TryParse(aiCooldown,out int cooldown))Client.Act("/api/ai/config",J.O("enabled",aiEnabled,"backend",aiOllama?"ollama":"llamacpp","url",aiUrl,"model",aiModel,"reviews_per_month",budget,"cooldown_months",cooldown));
        fastMode=GUILayout.Toggle(fastMode,"世界推演使用规则快进（跳过模型调用）");
        T("先启动原项目的「启动本地模型.bat」。新 Unity 会话默认关闭 AI；需在此手动启用。",small);
        Metric("真实调用",W["ai"]["calls"].S);Metric("成功 / 失败",W["ai"]["successes"].S+" / "+W["ai"]["failed"].S);Metric("已覆盖人物",W["ai"]["coverage"].S+" / "+W["ai"]["eligible"].S);Metric("排队 / 推理中",W["ai"]["queued"].S+" / "+W["ai"]["pending"].S);
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
        GUILayout.Space(18);H("另起一方江湖");T("初始人口（30–1000）",sub);newPopulation=GUILayout.TextField(newPopulation);T("固定种子",sub);newSeed=GUILayout.TextField(newSeed);T("历史预演年数（0–50）",sub);newYears=GUILayout.TextField(newYears);
        if(Btn("建立新世界，保留旧档",Paused)&&int.TryParse(newPopulation,out int population)&&int.TryParse(newSeed,out int seed)&&int.TryParse(newYears,out int years))Client.Act("/api/new",J.O("population",population,"seed",seed,"prehistory",years),j=>{Person=new J();SelectedPerson=-1;Institution=new J();City=new J();creatingHero=false;});
        GUILayout.Space(12);T("Unity 客户端负责原生界面、地图与皮影动画；随游戏启动的本机模拟引擎负责社会、战斗、存档与本地 AI。关闭 Unity 时会保存并退出本次模拟进程。",small);GUILayout.EndScrollView();
    }
}
}
