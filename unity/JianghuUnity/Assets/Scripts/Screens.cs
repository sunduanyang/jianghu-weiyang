using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jianghu {
// 人物卷：看自己与看任何人用同一张卷、同一套标签（概览 / 品性 / 武学 / 行囊 / 人情 / 生平）。
// 对话：与人当面交谈的全屏场景。
public partial class JianghuApp {
    readonly List<int> recentPeople=new List<int>();
    string dialogueText="";
    static readonly string[] TraitOrder={"compassion","honesty","loyalty","caution","sociability","ambition","greed","aggression"};
    static readonly Dictionary<string,string> TraitNames=new Dictionary<string,string>{{"compassion","仁厚"},{"honesty","诚实"},{"loyalty","忠义"},{"caution","审慎"},{"sociability","合群"},{"ambition","进取"},{"greed","贪欲"},{"aggression","暴烈"}};
    static readonly Dictionary<string,string> MoodNames=new Dictionary<string,string>{{"joy","喜"},{"anger","怒"},{"fear","忧"}};

    void RememberPerson(int id){recentPeople.Remove(id);recentPeople.Insert(0,id);if(recentPeople.Count>6)recentPeople.RemoveAt(6);}
    J Npc(int id)=>W["npcs"].A.FirstOrDefault(n=>n["id"].I==id)??new J();
    string MoodWord(J p){var m=p["mood"];string best="平";double top=12;foreach(var k in MoodNames.Keys){if(m[k].N>top){top=m[k].N;best=MoodNames[k];}}return best;}

    // ---------------------------------------------------------------- 人物卷
    void DrawPersonScroll(){
        var view=TopView;var tab=(PersonTab)view.Tab;
        BookScreen("人物卷 · "+CharacterName);
        DrawCharacterRail();
        int pick=BookTabStrip(PersonTabNames,view.Tab);
        if(pick>=0&&pick!=view.Tab)Later(()=>SetTab(pick));
        if(!CharacterReady){GUI.Label(new Rect(600,400,480,40),"正在载入人物…",centerTitle);return;}
        if(tab==PersonTab.Overview){if(InspectingNpc)DrawNpcOverview();else DrawHeroOverview();return;}
        DrawPersonBar(new Rect(260,112,1360,46));
        bool wide=tab==PersonTab.Bag||tab==PersonTab.Martial;
        GUILayout.BeginArea(new Rect(260,166,wide?1360:1120,716));
        if(tab==PersonTab.Bag)DrawBagTab();
        else{
            personScroll=GUILayout.BeginScrollView(personScroll);
            switch(tab){
                case PersonTab.Character:if(InspectingNpc)DrawNpcCharacter();else DrawHeroCharacter();break;
                case PersonTab.Martial:if(InspectingNpc)DrawNpcMartial();else DrawHeroMartial();break;
                case PersonTab.Ties:if(InspectingNpc)DrawNpcTies();else DrawHeroTies();break;
                case PersonTab.Life:if(InspectingNpc)DrawNpcLife();else DrawHeroLife();break;
            }
            GUILayout.EndScrollView();
        }
        GUILayout.EndArea();
    }
    // One line above every tab but 概览: whose scroll this is.
    void DrawPersonBar(Rect r){
        var name=new GUIStyle(heading){wordWrap=false};
        GUILayout.BeginArea(r);GUILayout.BeginHorizontal();
        if(InspectingNpc){
            var p=Person;GUILayout.Label(p["name"].S,name);
            GUILayout.Label(p["identity"]["grade_label"].S+" · "+p["identity"]["title"].S+" · "+p["age_months"].I/12+"岁"+(p["alive"].B?"":" · 已故"),caption,GUILayout.Height(36));
            GUILayout.FlexibleSpace();
        }else{
            GUILayout.Label(HeroName,name);
            GUILayout.Label("我的人物 · "+Hero["identity"]["grade_label"].S+" · "+Hero["identity"]["title"].S,caption,GUILayout.Height(36));
            GUILayout.FlexibleSpace();
        }
        GUILayout.EndHorizontal();GUILayout.EndArea();
    }
    // 概览 · 他人：头像、要目、心性，右栏为此刻近况与打算。
    void DrawNpcOverview(){
        var p=Person;
        GUI.Label(new Rect(260,116,620,40),p["name"].S,new GUIStyle(heading){fontSize=28,wordWrap=false});
        GUI.Label(new Rect(260,156,620,24),p["identity"]["grade_label"].S+" · "+p["identity"]["title"].S+" · "+p["age_months"].I/12+"岁 · "+(p["alive"].B?"在世":"已故"),caption);
        Rect ring=new Rect(260,190,280,280);
        Portrait(new Rect(ring.x+20,ring.y+20,240,240),p["id"].I,p["sex"].S,p["age_months"].I,!p["alive"].B);Tex(ring,"ring_portrait");
        var place=Place(p["place"].I);string faction=p["faction"].Has?W["factions"].A.FirstOrDefault(f=>f["id"].I==p["faction"].I)?["name"].S??"—":"—";
        var facts=new List<KeyValuePair<string,string>>{
            new KeyValuePair<string,string>("性别",p["sex"].S),new KeyValuePair<string,string>("居所",place["name"].S),new KeyValuePair<string,string>("所属",Country(place["country"].I)["name"].S),
            new KeyValuePair<string,string>("营生",p["job"].S),new KeyValuePair<string,string>("门派",faction),new KeyValuePair<string,string>("家资",Num(p["wealth"])+"银"),
            new KeyValuePair<string,string>("声望",Num(p["reputation"])),new KeyValuePair<string,string>("心情",MoodWord(p)),new KeyValuePair<string,string>("心愿",p["goal"]["title"].S)};
        for(int i=0;i<facts.Count;i++){Rect row=new Rect(564,194+i*31,316,30);GUI.Label(new Rect(row.x,row.y,80,row.height),facts[i].Key,new GUIStyle(text){alignment=TextAnchor.MiddleLeft});GUI.Label(new Rect(row.x+80,row.y,row.width-80,row.height),facts[i].Value,valueStyle);Fill(new Rect(row.x,row.yMax,row.width,1),hairline);}
        Rect tr=new Rect(260,536,620,346);Frame(tr,"card",8);
        GUI.Label(new Rect(tr.x,tr.y+6,tr.width,26),"心性",new GUIStyle(sub){alignment=TextAnchor.MiddleCenter});
        for(int i=0;i<TraitOrder.Length;i++){
            string key=TraitOrder[i];if(!p["traits"][key].Has)continue;float v=(float)p["traits"][key].N;
            Rect c=new Rect(tr.x+16+(i%4)*148,tr.y+42+(i/4)*90,140,80);Frame(c,"btn",8);
            GUI.Label(new Rect(c.x,c.y+8,c.width,26),TraitNames[key],new GUIStyle(sub){alignment=TextAnchor.MiddleCenter});
            Rect bar=new Rect(c.x+14,c.y+46,c.width-28,6);Fill(bar,new Color(.10f,.12f,.12f));Fill(new Rect(bar.x,bar.y,bar.width*Mathf.Clamp01(v/100f),bar.height),v>=60?brass:v<=30?jade:new Color(.62f,.60f,.55f));
            GUI.Label(new Rect(c.x,c.y+54,c.width,22),Mathf.RoundToInt(v).ToString(),centerSmall);
        }
        GUILayout.BeginArea(new Rect(tr.x+20,tr.y+228,tr.width-40,110));GUILayout.Label(p["last_action"]["label"].Has?"近来："+p["last_action"]["label"].S:"尚未开始行动。",text);GUILayout.EndArea();
        GUILayout.BeginArea(new Rect(910,116,710,766));personScroll=GUILayout.BeginScrollView(personScroll);
        H("此刻");
        if(p["appearance_view"]["description"].Has)T(p["appearance_view"]["description"].S,small);
        Metric("家资",Num(p["wealth"])+"银");Bar("体力",p["health"].N,100);Metric("声望",Num(p["reputation"]));T(p["goal"]["title"].S,sub);T(p["last_action"]["label"].S);
        Details("本月四周行程",p["monthly_actions"]);Details("生活近况",J.O("illness",p["illness"],"wound",p["wound"],"pregnancy",p["pregnancy"],"plan",p["plan"]));
        GUILayout.Space(10);DrawThinking();
        GUILayout.EndScrollView();GUILayout.EndArea();
    }
    // 概览 · 自己：皮影全身像与眼下光景。
    void DrawHeroOverview(){
        GUI.Label(new Rect(260,116,620,40),HeroName,new GUIStyle(heading){fontSize=28,wordWrap=false});
        GUI.Label(new Rect(260,156,620,24),"我的人物",caption);
        DrawPaperPerson(new Rect(300,196,360,490),Hero,true,"bind");
        GUILayout.BeginArea(new Rect(740,138,640,730));
        H("此刻");
        if(!Hero["created"].B)T("尚未立主角：回到舆图，在底栏左侧点「创建主角」。",small);
        Metric("年岁",Hero["age_months"].I/12+"岁");Metric("性别",Hero["sex"].S);
        Metric("所在",Hero["place_name"].S+" · "+Hero["position_name"].S);
        Metric("身份",Hero["identity"]["grade_label"].S+" · "+Hero["identity"]["title"].S);
        Metric("行动力",Hero["points"].S+" / "+Hero["max_points"].S);
        Metric("银两",Num(W["player"]["wealth"]));Metric("声望",Num(W["player"]["reputation"]));
        Bar("体力",W["player"]["health"].N,100);
        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        if(Btn("行囊与装备"))Later(()=>SetTab((int)PersonTab.Bag));
        if(Btn("武学与调养"))Later(()=>SetTab((int)PersonTab.Martial));
        if(Btn("品阶进退"))Later(()=>SetTab((int)PersonTab.Character));
        GUILayout.EndHorizontal();
        GUILayout.Space(8);T("近来经历",sub);
        mainScroll=GUILayout.BeginScrollView(mainScroll);
        DrawEventList(Hero["journal"]);
        if(Hero["journal"].A.Count==0)T("尚无记事。",small);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    // 品性：身份品阶、升降、本领与心性。
    void DrawNpcCharacter(){
        DrawPersonRank();
        DrawRankProgress(Person["rank_progression"],false);
        Details("各业本领",Person["career_view"],true);Details("可学营生",Person["career_options"]);Details("旧业履历",Person["career"]);
        H("心性");
        Details("稳定心性",Person["traits"],true);Details("当前情绪",Person["mood"],true);Details("世界信念",Person["beliefs"],true);Details("心中所欲",Person["desires"],true);
        Metric("压力",Num(Person["pressure"]));Metric("孤独",Num(Person["loneliness"]));
        Details("长远心愿",Person["goal"],true);Details("心性变化",Person["personality_changes"]);Details("记忆与理解",Person["memories"]);
    }
    void DrawHeroCharacter(){
        var identity=Hero["identity"];
        H((identity["system_label"].Has?identity["system_label"].S+" · ":"")+identity["grade_label"].S);
        T(identity["title"].S,sub);
        Details("兼有身份",identity["roles"]);
        DrawRankProgress(Hero["rank_progression"],true);
    }

    // 武学：战斗能力、所学与各派武学。自己的还可调养、进演武场。
    void DrawNpcMartial(){
        var p=Person;var m=CharacterMartial;
        T("银两 "+Num(p["wealth"]),sub);
        DrawCombatAttributes(m["combat"],true);
        DrawKnownMartial(m);DrawMartialCatalog();
    }
    void DrawHeroMartial(){
        GUILayout.BeginHorizontal();H("人物战斗能力");GUILayout.FlexibleSpace();if(Btn("演武场 / 续战"))Later(OpenBattle);GUILayout.EndHorizontal();
        if(!Martial.Has){T("正在载入人物属性…");return;}
        var p=Martial["player"];var m=p["martial"];
        T("行囊 "+Num(p["wealth"])+"银",sub);
        DrawCombatAttributes(m["combat"]);
        DrawKnownMartial(m);
        DrawMartialCatalog();
        T("切磋每场恢复完整气血、气力与肢体；伤势和疲劳不带入下一场。",small);
        T("调养会推进江湖一个月，恢复江湖中的体力与精力。",small);
        if(Btn("调养一月",Paused))MartialAction("rest");
    }

    // 人情：家计乡里、人际关系、门中身份。
    void DrawNpcTies(){
        DrawSocialPerson();
        H("人际关系");
        GUILayout.BeginHorizontal();relationQuery=GUILayout.TextField(relationQuery,GUILayout.Width(260));relationFilter=Cycle(relationFilter,new[]{"全部","朋友","义亲","仇怨","故人"});GUILayout.FlexibleSpace();GUILayout.EndHorizontal();
        foreach(var r in Person["relations_view"].A.Where(r=>r["name"].S.Contains(relationQuery)&&(relationFilter=="全部"||(relationFilter=="朋友"&&r["affinity"].N>20)||(relationFilter=="义亲"&&r["sworn"].B)||(relationFilter=="仇怨"&&r["affinity"].N<0)||(relationFilter=="故人"&&!r["alive"].B)))){
            int other=r["other"].I;
            if(GUILayout.Button(r["name"].S+" · "+r["label"].S+"\n情分 "+Num(r["affinity"])+" / 信任 "+Num(r["trust"]),rowStyle))Later(()=>SwitchSubject(other));
        }
        Details("与过客交情",Person["player_relation"],true);
        H("门中");
        if(!Person["faction"].Has)T("不在任何门派。",small);
        Details("门中身份",Person["membership_view"],true);if(Person["faction"].Has&&Btn("查看所属门派"))OpenInstitution(false,Person["faction"].I);Details("离合履历",Person["membership_history"]);
        GUILayout.Space(8);DrawHousehold(Person["economy_view"],false);
    }
    void DrawHeroTies(){
        H("门中");
        if(Hero["membership"].Has)Details("门中身份",Hero["membership"],true);else T("尚未投入任何门派。与该门派三品及以上人物对话，在「互动」栏选择「拜入门派」。",small);
        GUILayout.Space(8);DrawHousehold(Economy,true);
        T("与各人的交情记在对方人物卷的「人情」里。",small);
    }

    // 生平：此人经历过的事，点开即在江湖卷·年谱中循因溯果。
    void DrawNpcLife(){
        H("生平年谱");
        DrawEventList(Person["history"]);
        int id=Person["id"].I;
        if(Btn("更早年谱")&&Person["history"].A.Count>0)Client.Fetch("/api/events?npc="+id+"&before="+Person["history"].A.Last()["id"].S,j=>{if(Person["id"].I==id)Person["history"]=new J(Person["history"].A.Concat(j.A).ToList());});
    }
    void DrawHeroLife(){
        H("生平年谱");
        DrawEventList(Hero["journal"]);
        if(Hero["journal"].A.Count==0)T("尚无记事。",small);
        if(Btn("查看世界年谱 ›"))Later(()=>OpenJianghu(JianghuTab.Chronicle));
    }

    // ---------------------------------------------------------------- 对话
    void DrawDialogue(){
        Tile(new Rect(0,0,1680,960),"bg");
        if(valley!=null){var old=GUI.color;GUI.color=new Color(.30f,.32f,.30f,1);GUI.DrawTexture(new Rect(0,0,1680,960),valley,ScaleMode.ScaleAndCrop);GUI.color=old;}
        Tex(new Rect(0,0,1680,960),"vignette");
        if(!CharacterReady){
            GUI.Label(new Rect(470,360,740,70),"正在走近 "+CharacterName+"…",centerTitle);
            if(GUI.Button(new Rect(740,480,200,44),"返回",skin.button))Later(Back);
            return;
        }
        var p=Person;int id=p["id"].I;
        // hero (left) and NPC (right)
        Rect hl=new Rect(70,250,360,456),hr=new Rect(1250,250,360,456);
        DrawPaperPerson(hl,PaperPerson(-1,Hero["sex"].S,Hero["age_months"].I),true,"bind");
        DrawPaperPerson(hr,p,false,"bind",true);
        if(GUI.Button(hr,new GUIContent("","查看"+p["name"].S+"的人物卷"),GUIStyle.none))Later(()=>ViewPerson(id,PersonTab.Overview));
        Rect plateL=new Rect(40,716,420,150),plateR=new Rect(1220,716,420,150);Panel(plateL);Panel(plateR);
        GUI.Label(new Rect(plateL.x,plateL.y+10,plateL.width,28),HeroName,new GUIStyle(sub){alignment=TextAnchor.MiddleCenter});
        GUI.Label(new Rect(plateL.x+20,plateL.y+46,plateL.width-40,22),"银两 "+Num(W["player"]["wealth"])+" · 声望 "+Num(W["player"]["reputation"]),centerSmall);
        if(Hero["identity"].Has)GUI.Label(new Rect(plateL.x+20,plateL.y+80,plateL.width-40,22),Hero["identity"]["grade_label"].S+" · "+Hero["identity"]["title"].S,centerSmall);
        GUI.Label(new Rect(plateR.x,plateR.y+10,plateR.width,28),p["name"].S,new GUIStyle(sub){alignment=TextAnchor.MiddleCenter});
        GUI.Label(new Rect(plateR.x+20,plateR.y+46,plateR.width-40,22),p["identity"]["title"].S+" · 心情 "+MoodWord(p),centerSmall);
        var rel=p["player_relation"];
        if(rel.Has){float aff=(float)rel["affinity"].N;GUI.Label(new Rect(plateR.x+20,plateR.y+80,plateR.width-40,20),"交情 "+Num(rel["affinity"]),centerSmall);Rect bar=new Rect(plateR.x+40,plateR.y+108,plateR.width-80,6);Fill(bar,new Color(.10f,.12f,.12f));Fill(new Rect(bar.x,bar.y,bar.width*Mathf.Clamp01((aff+100)/200f),bar.height),aff>=0?brass:vermilion);}
        if(GUI.Button(new Rect(plateR.x+20,plateR.y+119,120,28),"人物卷",linkStyle))Later(()=>ViewPerson(id,PersonTab.Overview));
        if(GUI.Button(new Rect(plateR.x+150,plateR.y+119,120,28),"此人行囊",linkStyle))Later(()=>ViewPerson(id,PersonTab.Bag));
        if(GUI.Button(new Rect(plateR.x+280,plateR.y+119,120,28),"此人武学",linkStyle))Later(()=>ViewPerson(id,PersonTab.Martial));
        Rect box=new Rect(470,100,740,810);Frame(box,"panel_solid",24);
        if(GUI.Button(new Rect(52,36,192,38),"‹ "+BookBackLabel,chip))Later(Back);
        GUI.Label(new Rect(470,43,740,32),"人物交谈  /  "+CharacterName,centerTitle);
        GUI.Label(new Rect(54,884,382,38),new GUIContent(UiStatus,UiStatus),small);
        DrawSocialConversation(new Rect(box.x+28,box.y+20,box.width-56,box.height-40));
    }

    bool DialogueOption(string key,string label,bool enabled){
        GUILayout.BeginHorizontal();bool prev=GUI.enabled;GUI.enabled=prev&&enabled&&!Client.Busy;
        GUILayout.Label("["+key+"]",new GUIStyle(text){alignment=TextAnchor.MiddleCenter},GUILayout.Width(56),GUILayout.Height(40));
        bool hit=GUILayout.Button(label,rowStyle,GUILayout.Height(40));GUI.enabled=prev;GUILayout.EndHorizontal();return hit;
    }
}
}
