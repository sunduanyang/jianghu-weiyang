using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jianghu {
// Full-screen scenes: 人物 (character sheet), 对话 (talking to an NPC) and 演武 (a duel).
public partial class JianghuApp {
    readonly List<int> recentPeople=new List<int>();
    string dialogueText="";
    Vector2 traitScroll;
    static readonly string[] TraitOrder={"compassion","honesty","loyalty","caution","sociability","ambition","greed","aggression"};
    static readonly Dictionary<string,string> TraitNames=new Dictionary<string,string>{{"compassion","仁厚"},{"honesty","诚实"},{"loyalty","忠义"},{"caution","审慎"},{"sociability","合群"},{"ambition","进取"},{"greed","贪欲"},{"aggression","暴烈"}};
    static readonly Dictionary<string,string> MoodNames=new Dictionary<string,string>{{"joy","喜"},{"anger","怒"},{"fear","忧"}};

    void RememberPerson(int id){recentPeople.Remove(id);recentPeople.Insert(0,id);if(recentPeople.Count>6)recentPeople.RemoveAt(6);}
    J Npc(int id)=>W["npcs"].A.FirstOrDefault(n=>n["id"].I==id)??new J();
    string MoodWord(J p){var m=p["mood"];string best="平";double top=12;foreach(var k in MoodNames.Keys){if(m[k].N>top){top=m[k].N;best=MoodNames[k];}}return best;}

    // ---------------------------------------------------------------- 人物
    void DrawPersonScreen(){
        BookScreen("人物");
        if(!Person.Has){GUI.Label(new Rect(600,400,480,40),"择一人，观其一生",centerTitle);return;}
        var p=Person;
        // recent people (left column)
        GUI.Label(new Rect(60,114,180,22),"近日查阅 "+recentPeople.Count,caption);
        for(int i=0;i<recentPeople.Count;i++){
            var n=Npc(recentPeople[i]);if(!n.Has)continue;Rect c=new Rect(60,140+i*104,180,96);
            bool on=recentPeople[i]==SelectedPerson;Frame(c,on?"btn_on":"card",6);
            Portrait(new Rect(c.x+6,c.y+6,66,84),n["id"].I,n["sex"].S,n["age_months"].I);
            GUI.Label(new Rect(c.x+80,c.y+12,96,24),n["name"].S,sub);GUI.Label(new Rect(c.x+80,c.y+40,96,40),n["identity"]["title"].S,small);
            if(GUI.Button(c,GUIContent.none,GUIStyle.none))SelectPerson(n["id"].I);
        }
        // name block and portrait ring
        GUI.Label(new Rect(270,122,250,34),p["name"].S,new GUIStyle(heading){fontSize=26,alignment=TextAnchor.MiddleRight});
        GUI.Label(new Rect(270,160,250,22),p["identity"]["title"].S,labelRight);
        GUI.Label(new Rect(270,186,250,22),p["age_months"].I/12+"岁 · "+(p["alive"].B?"在世":"已故"),labelRight);
        Fill(new Rect(300,214,220,1),hairline);
        Rect ring=new Rect(540,120,300,300);
        Portrait(new Rect(ring.x+21,ring.y+21,258,258),p["id"].I,p["sex"].S,p["age_months"].I,!p["alive"].B);Tex(ring,"ring_portrait");
        // interaction
        GUILayout.BeginArea(new Rect(470,432,440,90));GUILayout.BeginHorizontal();GUILayout.FlexibleSpace();
        if(Btn("拜访",p["alive"].B))OpenDialogue("visit");if(Btn("赠银10",p["alive"].B))OpenDialogue("gift");if(Btn("切磋",Paused&&p["alive"].B))Intervene("challenge");if(Btn("交谈",p["alive"].B))OpenDialogue(null);
        GUILayout.FlexibleSpace();GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();GUILayout.FlexibleSpace();if(Btn("请其反思",W["ai"]["config"]["enabled"].B))Client.Act("/api/ai/review",J.O("npc_id",SelectedPerson));if(Btn("从行囊赠物")){giftTarget=SelectedPerson;personScreen=false;Go(2);}GUILayout.FlexibleSpace();GUILayout.EndHorizontal();
        GUILayout.EndArea();
        // fact table
        var place=Place(p["place"].I);string faction=p["faction"].Has?W["factions"].A.FirstOrDefault(f=>f["id"].I==p["faction"].I)?["name"].S??"—":"—";
        var facts=new List<KeyValuePair<string,string>>{
            new KeyValuePair<string,string>("性别",p["sex"].S),new KeyValuePair<string,string>("居所",place["name"].S),new KeyValuePair<string,string>("所属",Country(place["country"].I)["name"].S),
            new KeyValuePair<string,string>("营生",p["job"].S),new KeyValuePair<string,string>("门派",faction),new KeyValuePair<string,string>("家资",Num(p["wealth"])+"银"),
            new KeyValuePair<string,string>("声望",Num(p["reputation"])),new KeyValuePair<string,string>("心情",MoodWord(p)),new KeyValuePair<string,string>("心愿",p["goal"]["title"].S)};
        for(int i=0;i<facts.Count;i++){Rect row=new Rect(880,124+i*36,330,34);GUI.Label(new Rect(row.x,row.y,90,row.height),facts[i].Key,new GUIStyle(text){alignment=TextAnchor.MiddleLeft});GUI.Label(new Rect(row.x+90,row.y,row.width-90,row.height),facts[i].Value,valueStyle);Fill(new Rect(row.x,row.yMax,row.width,1),hairline);}
        // traits (lower centre)
        Rect tr=new Rect(270,540,940,330);Frame(tr,"card",6);
        GUI.Label(new Rect(tr.x,tr.y+6,tr.width,26),"心性",new GUIStyle(sub){alignment=TextAnchor.MiddleCenter});
        for(int i=0;i<TraitOrder.Length;i++){
            string key=TraitOrder[i];if(!p["traits"][key].Has)continue;float v=(float)p["traits"][key].N;
            Rect c=new Rect(tr.x+20+(i%4)*228,tr.y+44+(i/4)*92,214,80);Frame(c,"btn",6);
            GUI.Label(new Rect(c.x,c.y+8,c.width,26),TraitNames[key],new GUIStyle(sub){alignment=TextAnchor.MiddleCenter});
            Rect bar=new Rect(c.x+18,c.y+46,c.width-36,6);Fill(bar,new Color(.10f,.12f,.12f));Fill(new Rect(bar.x,bar.y,bar.width*Mathf.Clamp01(v/100f),bar.height),v>=60?brass:v<=30?jade:new Color(.62f,.60f,.55f));
            GUI.Label(new Rect(c.x,c.y+54,c.width,22),Mathf.RoundToInt(v).ToString(),centerSmall);
        }
        GUILayout.BeginArea(new Rect(tr.x+20,tr.y+236,tr.width-40,86));GUILayout.Label(p["last_action"]["label"].Has?"近来："+p["last_action"]["label"].S:"尚未开始行动。",text);GUILayout.EndArea();
        // detail tabs (right)
        Rect side=new Rect(1240,112,380,770);Panel(side);
        GUILayout.BeginArea(new Rect(side.x+12,side.y+12,side.width-24,side.height-24));
        personTab=GUILayout.SelectionGrid(personTab,new[]{"此刻","心性","身份","物品","关系","门中","武学","年谱"},4,chip);
        personScroll=GUILayout.BeginScrollView(personScroll);DrawPersonTab();GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    // ---------------------------------------------------------------- 对话
    void OpenDialogue(string action){
        dialogueOpen=true;var p=Person;
        dialogueText="（"+p["name"].S+"抬眼看了看你。）";
        if(action!=null)Intervene(action);
    }
    void DrawDialogue(){
        Tile(new Rect(0,0,1680,960),"bg");
        if(valley!=null){var old=GUI.color;GUI.color=new Color(.30f,.32f,.30f,1);GUI.DrawTexture(new Rect(0,0,1680,960),valley,ScaleMode.ScaleAndCrop);GUI.color=old;}
        Tex(new Rect(0,0,1680,960),"vignette");
        var p=Person;
        // hero (left) and NPC (right)
        Rect hl=new Rect(70,250,360,456),hr=new Rect(1250,250,360,456);
        Portrait(hl,-1,Hero["created"].B?Hero["sex"].S:"男",Hero["created"].B?Hero["age_months"].I:216);Outline(hl,new Color(.79f,.64f,.35f,.6f));
        Portrait(hr,p["id"].I,p["sex"].S,p["age_months"].I);Outline(hr,new Color(.79f,.64f,.35f,.6f));
        Rect plateL=new Rect(40,716,420,150),plateR=new Rect(1220,716,420,150);Panel(plateL);Panel(plateR);
        GUI.Label(new Rect(plateL.x,plateL.y+10,plateL.width,28),Hero["created"].B?Hero["name"].S:"过客",new GUIStyle(sub){alignment=TextAnchor.MiddleCenter});
        GUI.Label(new Rect(plateL.x+20,plateL.y+46,plateL.width-40,22),"银两 "+Num(W["player"]["wealth"])+" · 声望 "+Num(W["player"]["reputation"]),centerSmall);
        GUI.Label(new Rect(plateR.x,plateR.y+10,plateR.width,28),p["name"].S,new GUIStyle(sub){alignment=TextAnchor.MiddleCenter});
        GUI.Label(new Rect(plateR.x+20,plateR.y+46,plateR.width-40,22),p["identity"]["title"].S+" · 心情 "+MoodWord(p),centerSmall);
        var rel=p["player_relation"];
        if(rel.Has){float aff=(float)rel["affinity"].N;GUI.Label(new Rect(plateR.x+20,plateR.y+80,plateR.width-40,20),"交情 "+Num(rel["affinity"]),centerSmall);Rect bar=new Rect(plateR.x+40,plateR.y+108,plateR.width-80,6);Fill(bar,new Color(.10f,.12f,.12f));Fill(new Rect(bar.x,bar.y,bar.width*Mathf.Clamp01((aff+100)/200f),bar.height),aff>=0?brass:vermilion);}
        // centre: scene banner, speech, numbered options
        Rect box=new Rect(510,150,660,760);Frame(box,"panel_solid",10);
        Rect banner=new Rect(box.x+14,box.y+14,box.width-28,210);
        if(valley!=null){var old=GUI.color;GUI.color=new Color(.78f,.74f,.66f,1);GUI.DrawTexture(banner,valley,ScaleMode.ScaleAndCrop);GUI.color=old;}Outline(banner,hairline);
        Rect speech=new Rect(box.x+30,banner.yMax+16,box.width-60,170);Frame(speech,"card",6);
        GUI.Label(new Rect(speech.x+20,speech.y+10,speech.width-40,speech.height-20),"“"+dialogueText+"”",dialogueStyle);
        GUILayout.BeginArea(new Rect(box.x+20,speech.yMax+16,box.width-40,box.yMax-speech.yMax-30));
        if(DialogueOption("1","（拜访叙旧……）",p["alive"].B))Intervene("visit");
        if(DialogueOption("2","（赠银十两……）",p["alive"].B))Intervene("gift");
        if(DialogueOption("3","（切磋武艺……）",Paused&&p["alive"].B)){dialogueOpen=false;personScreen=false;Intervene("challenge");}
        GUILayout.BeginHorizontal();GUILayout.Label("[4]",new GUIStyle(text){alignment=TextAnchor.MiddleCenter},GUILayout.Width(56),GUILayout.Height(40));talk=GUILayout.TextField(talk,GUILayout.Height(36));if(Btn("说",talk.Length>0))Intervene("talk");GUILayout.EndHorizontal();
        if(DialogueOption("ESC","（暂无要事……）",true))dialogueOpen=false;
        GUILayout.EndArea();
        var e=Event.current;if(e.type==EventType.KeyDown&&GUIUtility.keyboardControl==0){
            if(e.keyCode==KeyCode.Alpha1){Intervene("visit");e.Use();}else if(e.keyCode==KeyCode.Alpha2){Intervene("gift");e.Use();}
            else if(e.keyCode==KeyCode.Escape){dialogueOpen=false;e.Use();}
        }
    }
    bool DialogueOption(string key,string label,bool enabled){
        GUILayout.BeginHorizontal();bool prev=GUI.enabled;GUI.enabled=prev&&enabled&&!Client.Busy;
        GUILayout.Label("["+key+"]",new GUIStyle(text){alignment=TextAnchor.MiddleCenter},GUILayout.Width(56),GUILayout.Height(40));
        bool hit=GUILayout.Button(label,rowStyle,GUILayout.Height(40));GUI.enabled=prev;GUILayout.EndHorizontal();return hit;
    }

    // ---------------------------------------------------------------- 演武
    void DrawBattleScreen(){
        Tile(new Rect(0,0,1680,960),"bg");Tex(new Rect(0,0,1680,960),"vignette");
        GUI.Label(new Rect(40,10,400,90),"演武",titleBrush);
        Rect close=new Rect(1606,22,50,50);Tex(close,"close");if(GUI.Button(close,GUIContent.none,GUIStyle.none))battleView=false;
        var b=Martial["battle"];
        if(!b.Has){GUI.Label(new Rect(440,420,800,40),"尚未与人切磋。在人物页选一位成年、体力充足的江湖人，点「切磋」。",centerTitle);return;}
        var fighters=b["fighters"].A;
        // fighter cards
        for(int i=0;i<fighters.Count&&i<2;i++){
            var f=fighters[i];bool left=i==0;Rect c=left?new Rect(60,110,470,170):new Rect(1150,110,470,170);Panel(c);
            Rect pr=left?new Rect(c.x+14,c.y+14,112,142):new Rect(c.xMax-126,c.y+14,112,142);
            int id=f["id"].Has?f["id"].I:(left?-1:SelectedPerson);J n=left?new J():Npc(id);
            Portrait(pr,id,left?(Hero["created"].B?Hero["sex"].S:"男"):n["sex"].S,left?(Hero["created"].B?Hero["age_months"].I:216):n["age_months"].I);Outline(pr,new Color(.79f,.64f,.35f,.6f));
            float tx=left?c.x+140:c.x+14;
            GUI.Label(new Rect(tx,c.y+12,316,28),f["name"].S,new GUIStyle(heading){alignment=left?TextAnchor.MiddleLeft:TextAnchor.MiddleRight});
            GUI.Label(new Rect(tx,c.y+42,316,20),f["inner"].S,new GUIStyle(small){alignment=left?TextAnchor.MiddleLeft:TextAnchor.MiddleRight});
            BattleBar(new Rect(tx,c.y+74,316,10),"气血",f["vigor"].N,f["vigor_max"].N,vermilion,left);
            BattleBar(new Rect(tx,c.y+104,316,10),"内力",f["qi"].N,f["qi_max"].N,jade,left);
            GUI.Label(new Rect(tx,c.y+130,316,22),"式 "+f["forms"].S+" · 外伤 "+Num(f["outer_wounds"])+" · 内伤 "+Num(f["inner_wounds"]),new GUIStyle(small){alignment=left?TextAnchor.MiddleLeft:TextAnchor.MiddleRight});
        }
        // round dial
        Rect dial=new Rect(770,108,140,140);Tex(dial,"moon");
        GUI.Label(new Rect(dial.x,dial.y+26,dial.width,60),b["round"].S,bigNumber);GUI.Label(new Rect(dial.x,dial.y+84,dial.width,24),"合",moonLabel);
        // stage: lit screen with the two fighters as shadow puppets, spaced by distance
        Rect stage=new Rect(160,300,1360,330);StagePanel(stage);
        int dist=Mathf.Clamp(b["distance"].I,0,12);float mid=stage.center.x,gap=60+dist*42;
        if(fighters.Count>=2){
            var f0=fighters[0];var f1=fighters[1];var n1=Npc(f1["id"].Has?f1["id"].I:SelectedPerson);
            bool live=b["status"].S!="finished";
            DrawPuppetFigure(new Rect(mid-gap/2-300,stage.y+14,320,240),PuppetTypeFor(Hero["created"].B?Hero["sex"].S:"男",Hero["created"].B?Hero["age_months"].I:216),"right",live?"angry":"calm",live?"lunge":"idle",live?55:100,shadowInk);
            DrawPuppetFigure(new Rect(mid+gap/2-20,stage.y+14,320,240),PuppetTypeFor(n1["sex"].S,n1["age_months"].I),"left",live?"angry":"sad",live?"lunge":"idle",live?40:100,shadowInk);
        }
        Fill(new Rect(stage.x+40,stage.yMax-46,stage.width-80,1),new Color(.30f,.18f,.08f,.6f));
        for(int d=0;d<=12;d++){float x=mid+(d-6)*((stage.width-120)/12f);Fill(new Rect(x,stage.yMax-50,1,9),new Color(.30f,.18f,.08f,.6f));}
        GUI.Label(new Rect(stage.x,stage.yMax-40,stage.width,24),"身位距离 "+b["distance"].S+" 格",new GUIStyle(centerSmall){normal={textColor=new Color(.30f,.18f,.08f)}});
        if(b["status"].S!="finished")GUI.Label(new Rect(stage.x,stage.y+16,stage.width,26),"对手意图："+b["opponent_intent"].S,new GUIStyle(centerSmall){fontSize=15,normal={textColor=new Color(.30f,.18f,.08f)}});
        // log (left) and actions (right)
        Rect logBox=new Rect(60,650,520,226);Panel(logBox);
        GUILayout.BeginArea(new Rect(logBox.x+12,logBox.y+8,logBox.width-24,logBox.height-16));GUILayout.Label("拆招记录",sub);
        mainScroll=GUILayout.BeginScrollView(mainScroll);foreach(var row in b["log"].A.AsEnumerable().Reverse())GUILayout.Label(row["round"].S+"合 · "+(row["side"].I==0?"我":"客")+" · "+row["text"].S,small);GUILayout.EndScrollView();GUILayout.EndArea();
        Rect act=new Rect(600,650,1020,226);Panel(act);
        GUILayout.BeginArea(new Rect(act.x+14,act.y+10,act.width-28,act.height-20));
        if(b["status"].S=="finished"){
            GUILayout.Label("已收招 · "+b["reason"].S,new GUIStyle(heading){alignment=TextAnchor.MiddleCenter});
            GUILayout.BeginHorizontal();GUILayout.FlexibleSpace();if(b["event_id"].Has&&Btn("查看此战年谱"))OpenEvent(b["event_id"].I);if(Btn("返回功法"))battleView=false;GUILayout.FlexibleSpace();GUILayout.EndHorizontal();
        } else {
            GUILayout.Label("出招",sub);var actions=b["actions"].A;
            for(int i=0;i<actions.Count;i+=3){GUILayout.BeginHorizontal();for(int j=i;j<Mathf.Min(i+3,actions.Count);j++){var a=actions[j];if(Btn(a["label"].S+(a["reason"].S.Length>0?"\n"+a["reason"].S:""),a["enabled"].B))Client.Act("/api/battle",J.O("battle_id",b["id"],"round",b["round"],"action",a["id"]),j2=>Martial=j2);}GUILayout.EndHorizontal();}
        }
        GUILayout.EndArea();
    }
    void BattleBar(Rect r,string label,double value,double max,Color c,bool left){
        GUI.Label(new Rect(r.x,r.y-20,r.width,18),label+" "+value.ToString("0")+" / "+max.ToString("0"),new GUIStyle(caption){alignment=left?TextAnchor.MiddleLeft:TextAnchor.MiddleRight});
        Fill(r,new Color(.08f,.10f,.10f));float w=r.width*Mathf.Clamp01((float)(value/Math.Max(1,max)));Fill(left?new Rect(r.x,r.y,w,r.height):new Rect(r.xMax-w,r.y,w,r.height),c);Outline(r,hairline);
    }
}
}
