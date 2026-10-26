using System;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    bool battlePaused=true,battleHelp;
    int battleSpeed=1;
    float nextBattleBeat;
    float battleVisualDistance=3;
    string battleSession="";
    string battleLastError="";
    Vector2 battleLogScroll;
    readonly Color combatDark=new Color(.055f,.095f,.095f,.96f),combatLight=new Color(.95f,.90f,.76f),combatRed=new Color(.73f,.30f,.23f);
    static readonly string[] BasicKeys={"basic_thrust","basic_slash","basic_crush"};
    static readonly string[] ArtHotkeys={"Q","W","E","Z","X","C"};

    void OnApplicationFocus(bool focus){if(!focus)battlePaused=true;}
    void UpdateBattle(){
        if(Page!=3||!battleView||personScreen||dialogueOpen){battlePaused=true;return;}
        var b=Martial["battle"];
        if(!b.Has||b["status"].S!="active"){battlePaused=true;return;}
        if(battleSession!=b["id"].S){battleSession=b["id"].S;battleVisualDistance=(float)b["distance"].N;battlePaused=true;nextBattleBeat=Time.realtimeSinceStartup+1.8f;}
        battleVisualDistance=Mathf.MoveTowards(battleVisualDistance,(float)b["distance"].N,Time.unscaledDeltaTime*3.5f);
        if(Input.GetKeyDown(KeyCode.Space)&&!battleHelp)ToggleBattleClock();
        if(Input.GetKeyDown(KeyCode.Escape)){battlePaused=true;battleHelp=false;battleView=false;return;}
        if(battleHelp||Client.Busy)return;
        if(Client.Error.Length>0){
            battlePaused=true;
            if(battleLastError!=Client.Error){battleLastError=Client.Error;Client.Fetch("/api/martial",j=>Martial=j);}
            return;
        }
        battleLastError="";
        string command=Input.GetKeyDown(KeyCode.A)?"advance":Input.GetKeyDown(KeyCode.D)?"retreat":Input.GetKeyDown(KeyCode.G)?"guard":Input.GetKeyDown(KeyCode.F)?"parry":Input.GetKeyDown(KeyCode.R)?"breathe":null;
        for(int i=0;i<3;i++)if(Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1+i)))command=BasicKeys[i];
        KeyCode[] keys={KeyCode.Q,KeyCode.W,KeyCode.E,KeyCode.Z,KeyCode.X,KeyCode.C};
        var arts=b["actions"].A.Where(a=>a["kind"].S=="art"||(!a["kind"].Has&&a["damage_type"].Has)).ToArray();
        for(int i=0;i<arts.Length&&i<keys.Length;i++)if(Input.GetKeyDown(keys[i]))command=arts[i]["id"].S;
        if(command!=null){SendBattleAction(command);return;}
        if(!battlePaused&&Time.realtimeSinceStartup>=nextBattleBeat)SendBattleAction(b["ruleset"].I==2?"auto":"guard");
    }
    void ToggleBattleClock(){battlePaused=!battlePaused;nextBattleBeat=Time.realtimeSinceStartup+1.8f/battleSpeed;}
    void SendBattleAction(string command){
        var b=Martial["battle"];
        if(Client.Busy||b["status"].S!="active")return;
        var action=b["actions"].A.FirstOrDefault(a=>a["id"].S==command);
        if(command!="auto"&&(action==null||!action["enabled"].B))return;
        nextBattleBeat=Time.realtimeSinceStartup+1.8f/battleSpeed;
        Client.Act("/api/battle",J.O("battle_id",b["id"],"round",b["round"],"action",command),j=>{
            Martial=j;nextBattleBeat=Time.realtimeSinceStartup+1.8f/battleSpeed;
            if(j["battle"]["status"].S!="active")battlePaused=true;
        });
    }
    GUIStyle CombatText(int size,Color color,TextAnchor align=TextAnchor.MiddleLeft){return new GUIStyle(text){fontSize=size,alignment=align,wordWrap=true,normal={textColor=color}};}
    bool CombatButton(Rect r,string label,bool active=false,bool enabled=true,string hint=""){
        Fill(r,active?new Color(.16f,.29f,.26f):combatDark);Outline(r,active?brass:hairline);
        GUI.Label(r,label,CombatText(17,enabled?(active?brass:ink):muted,TextAnchor.MiddleCenter));
        bool was=GUI.enabled;GUI.enabled=was&&enabled&&!Client.Busy;
        bool clicked=GUI.Button(r,new GUIContent("",hint),GUIStyle.none);GUI.enabled=was;return clicked;
    }
    void DrawBattleScreen(){
        Tile(new Rect(0,0,1680,960),"bg");
        var b=Martial["battle"];
        if(!b.Has){GUI.Label(new Rect(280,350,1120,80),"尚未约战\n在人物页选择一位成年、体力充足的江湖人，点击「切磋」。",centerTitle);if(CombatButton(new Rect(735,500,210,46),"返回功法"))battleView=false;return;}
        bool finished=b["status"].S=="finished",modern=b["ruleset"].I==2;
        var fighters=b["fighters"].A;
        if(fighters.Count<2)return;
        string key=b["id"].S+":"+b["round"].S+":"+b["status"].S;
        if(paperBattleKey!=key){paperBattleKey=key;paperBattleStart=Time.realtimeSinceStartup;battleLogScroll=Vector2.zero;}
        float elapsed=(Time.realtimeSinceStartup-paperBattleStart)*battleSpeed;
        // Landscape and actors occupy the full middle field, framed by combat data.
        Rect stage=new Rect(24,188,1632,460);StagePanel(stage);
        if(valley!=null){Color prev=GUI.color;GUI.color=new Color(.80f,.80f,.73f,.78f);GUI.DrawTexture(new Rect(stage.x+8,stage.y+8,stage.width-16,stage.height-16),valley,ScaleMode.ScaleAndCrop);GUI.color=prev;}
        Fill(new Rect(stage.x+8,stage.y+8,stage.width-16,stage.height-16),new Color(.75f,.70f,.56f,.12f));
        float gap=150+battleVisualDistance*105,mid=840;
        float px=mid-gap/2,ex=mid+gap/2;
        var enemy=Npc(fighters[1]["id"].I);
        string pose0=PaperBattleAnimation(b,0,elapsed,out float phase0),pose1=PaperBattleAnimation(b,1,elapsed,out float phase1);
        DrawPaperPerson(new Rect(px-155,306,310,296),PaperPerson(-1,"男",216),true,pose0,false,phase0);
        DrawPaperPerson(new Rect(ex-155,306,310,296),enemy,false,pose1,true,phase1);
        DrawCastLabel(new Rect(px-170,265,340,38),fighters[0],false);
        DrawCastLabel(new Rect(ex-170,265,340,38),fighters[1],true);
        DrawBattleImpacts(b,px,ex,elapsed);
        DrawCombatFighter(new Rect(24,20,650,153),fighters[0],true);
        DrawCombatFighter(new Rect(1006,20,650,153),fighters[1],false);
        // Centre clock is deliberately independent from the world's monthly calendar.
        Tex(new Rect(754,13,172,172),"moon");
        GUI.Label(new Rect(758,30,164,54),finished?"终":b["round"].S,CombatText(38,shadowInk,TextAnchor.MiddleCenter));
        GUI.Label(new Rect(748,86,184,24),finished?"已收招":battlePaused?"凝神 · 暂停":"行招 · "+battleSpeed+"倍",CombatText(16,ink,TextAnchor.MiddleCenter));
        if(CombatButton(new Rect(692,133,144,38),finished?"战斗结束":battlePaused?"开始 / 空格":"暂停 / 空格",!battlePaused,!finished))ToggleBattleClock();
        if(CombatButton(new Rect(844,133,144,38),battleSpeed+"倍速",false,!finished)){battleSpeed=battleSpeed%3+1;nextBattleBeat=Time.realtimeSinceStartup+1.8f/battleSpeed;}
        DrawFormStrip(new Rect(34,178,616,40),fighters[0],false);
        DrawFormStrip(new Rect(1030,178,616,40),fighters[1],true);
        Rect intent=new Rect(575,224,530,36);Fill(intent,combatDark);Outline(intent,hairline);
        GUI.Label(intent,finished?(b["winner"].Has?fighters[b["winner"].I]["name"].S+"胜出 · "+b["reason"].S:"平手 · "+b["reason"].S):"对手将要："+b["opponent_intent"].S+(b["opponent_action"]["reach"].Has?" · 距离 "+b["opponent_action"]["reach"][0].S+"–"+b["opponent_action"]["reach"][1].S:""),CombatText(17,finished?brass:combatLight,TextAnchor.MiddleCenter));
        DrawBattleDistance(b,new Rect(555,594,570,46));
        if(finished)DrawBattleResult(b);else DrawBattleActions(b);
        DrawBattleLog(b,new Rect(1240,664,416,254));
        GUI.Label(new Rect(24,926,1265,23),Client.Error.Length>0?Client.Error:GUI.tooltip.Length>0?GUI.tooltip:"空格 启停 · A / D 进退 · 1 / 2 / 3 普攻 · Q W E Z X C 功法 · G 架招 · F 拆招 · R 调息 · 关闭或失去焦点时暂停",CombatText(14,Client.Error.Length>0?combatRed:muted));
        if(CombatButton(new Rect(1304,925,104,28),"战斗说明")){battleHelp=!battleHelp;battlePaused=true;}
        if(CombatButton(new Rect(1416,925,112,28),finished?"返回功法":"暂离战场")){battlePaused=true;battleView=false;}
        if(!finished&&CombatButton(new Rect(1536,925,120,28),"收招认输"))SendBattleAction("surrender");
        if(battleHelp){FlushPaperFigures();DrawBattleHelp(modern);}
    }
    void DrawCombatFighter(Rect r,J f,bool left){
        Fill(r,combatDark);Outline(r,hairline);
        Rect portrait=new Rect(left?r.x+10:r.xMax-110,r.y+10,100,130);
        var person=left?PaperPerson(-1,"男",216):Npc(f["id"].I);
        DrawPaperPortrait(portrait,person,left);Outline(portrait,brass);
        float x=left?r.x+126:r.x+16;float width=r.width-142;
        GUI.Label(new Rect(x,r.y+8,width,32),f["name"].S,CombatText(26,combatLight,left?TextAnchor.MiddleLeft:TextAnchor.MiddleRight));
        GUI.Label(new Rect(x,r.y+40,width,21),f["inner"].S+" · "+(f["mode_name"].Has?f["mode_name"].S:"旧制切磋"),CombatText(14,muted,left?TextAnchor.MiddleLeft:TextAnchor.MiddleRight));
        BattleMeter(new Rect(x,r.y+68,width,15),"气血",f["vigor"].N,f["vigor_max"].N,combatRed,left);
        BattleMeter(new Rect(x,r.y+94,width,12),"内力",f["qi"].N,f["qi_max"].N,jade,left);
        if(f["posture"].Has){BattleMeter(new Rect(x,r.y+120,width/2-8,11),"架势",f["posture"].N,f["posture_max"].N,brass,left);BattleMeter(new Rect(x+width/2+8,r.y+120,width/2-8,11),"脚力",f["mobility"].N,100,new Color(.40f,.55f,.62f),left);}
    }
    void BattleMeter(Rect r,string name,double value,double max,Color color,bool left){
        Fill(r,new Color(.02f,.04f,.04f));float w=r.width*Mathf.Clamp01((float)(value/Math.Max(1,max)));Fill(new Rect(left?r.x:r.xMax-w,r.y,w,r.height),color);Outline(r,hairline);
        GUI.Label(new Rect(r.x+4,r.y-3,r.width-8,r.height+6),name+" "+value.ToString("0")+" / "+max.ToString("0"),CombatText(12,Color.white,left?TextAnchor.MiddleLeft:TextAnchor.MiddleRight));
    }
    string FormWord(string key){return key=="thrust"?"刺":key=="slash"?"劈":key=="crush"?"崩":"凝";}
    void DrawFormStrip(Rect r,J f,bool right){
        Fill(r,combatDark);Outline(r,hairline);
        for(int i=0;i<4;i++){
            Rect token=new Rect(right?r.xMax-38-i*35:r.x+8+i*35,r.y+5,29,29);bool on=i<f["forms"].I;
            Fill(token,on?new Color(.35f,.29f,.16f):new Color(.11f,.16f,.16f));Outline(token,on?brass:hairline);
            GUI.Label(token,on?FormWord(f["tokens"][i].S):"·",CombatText(20,on?combatLight:muted,TextAnchor.MiddleCenter));
        }
        string state=f["casting"].Has?"运功中":f["parrying"].B?"拆招待发":f["guarding"].B?"架招护体":f["evading"].B?"身法避实":"静候时机";
        GUI.Label(new Rect(right?r.x+8:r.x+158,r.y+4,r.width-168,31),state+"   外伤 "+Num(f["outer_wounds"])+" · 内伤 "+Num(f["inner_wounds"]),CombatText(14,muted,right?TextAnchor.MiddleLeft:TextAnchor.MiddleRight));
    }
    void DrawCastLabel(Rect r,J f,bool enemy){
        if(!f["casting"].Has)return;
        Fill(r,combatDark);Outline(r,enemy?combatRed:brass);
        var c=f["casting"];float p=(c["total"].I-c["remaining"].I+1f)/(c["total"].I+1f);
        Fill(new Rect(r.x,r.yMax-4,r.width*p,4),enemy?combatRed:brass);
        GUI.Label(r,c["name"].S+" · 还需 "+c["remaining"].S+" 拍",CombatText(19,combatLight,TextAnchor.MiddleCenter));
    }
    void DrawBattleImpacts(J b,float px,float ex,float elapsed){
        if(elapsed>2.2f)return;
        int round=b["log"].A.LastOrDefault()?["round"].I??0;
        int first=b["log"].A.FirstOrDefault(r=>r["round"].I==round)?["side"].I??0;
        foreach(var row in b["log"].A.Where(r=>r["round"].I==round&&r["outcome"].Has)){
            float t=elapsed-(row["side"].I==first?0:.9f);if(t<0||t>1.4f)continue;
            string word=row["outcome"].S=="windup"?"运功":row["guard_break"].B?"破势！":row["damage"].N>0?"−"+row["damage"].N.ToString("0.0"):row["outcome"].S=="parried"?"拆招":row["outcome"].S=="interrupted"?"中断":"落空";
            float x=row["side"].I==0?ex:px;
            GUI.Label(new Rect(x-90,356-t*26,180,46),word,CombatText(29,row["damage"].N>0||row["guard_break"].B?new Color(.55f,.14f,.08f):new Color(.17f,.28f,.24f),TextAnchor.MiddleCenter));
        }
    }
    void DrawBattleDistance(J b,Rect r){
        Fill(r,combatDark);Outline(r,hairline);
        GUI.Label(new Rect(r.x,r.y,132,r.height),"距离 "+b["distance"].N.ToString("0.0"),CombatText(23,brass,TextAnchor.MiddleCenter));
        var reach=b["fighters"][0]["basic_reach"];if(!reach.Has)reach=J.Parse("[0,2]");
        for(int d=0;d<=5;d++){
            Rect tick=new Rect(r.x+148+d*66,r.y+5,58,34);bool here=d==b["distance"].I,range=d>=reach[0].I&&d<=reach[1].I;
            Fill(tick,here?new Color(.29f,.34f,.23f):range?new Color(.13f,.25f,.22f):new Color(.08f,.13f,.13f));Outline(tick,here?brass:hairline);
            GUI.Label(tick,d.ToString(),CombatText(17,here?brass:range?combatLight:muted,TextAnchor.MiddleCenter));
        }
    }
    J BattleChoice(J b,string id){return b["actions"].A.FirstOrDefault(a=>a["id"].S==id)??new J();}
    void DrawActionChoice(Rect r,J action,string hotkey=""){
        if(!action.Has)return;
        bool enabled=action["enabled"].B&&!Client.Busy;
        Fill(r,enabled?new Color(.09f,.17f,.16f):new Color(.065f,.10f,.10f));Outline(r,enabled?new Color(.38f,.46f,.37f):hairline);
        GUI.Label(new Rect(r.x+9,r.y+7,r.width-18,27),action["label"].S,CombatText(21,enabled?combatLight:muted));
        if(hotkey.Length>0)GUI.Label(new Rect(r.xMax-32,r.y+5,24,20),hotkey,CombatText(12,brass,TextAnchor.MiddleRight));
        string cost=action["damage_type"].Has?action["reach"][0].S+"–"+action["reach"][1].S+"距 · "+action["qi"].S+"气 · "+action["forms"].S+"式"+(action["cast"].I>0?" · 运功"+action["cast"].S:" · 瞬发"):action["description"].S;
        string note=action["reason"].S.Length>0?action["reason"].S:cost;
        if(r.width<150&&action["reason"].S.Length==0){
            string id=action["id"].S;
            note=action["kind"].S=="basic"?action["reach"][0].S+"–"+action["reach"][1].S+"距 · +"+FormWord(action["token"].S)+"式":id=="advance"||id=="retreat"?"22脚力":id=="guard"?"减伤55% · +凝":id=="parry"?"6气 · 反震":id=="breathe"?"回气12 · 易伤":id=="mode_fist"?"普攻0–2距":id=="mode_blade"?"普攻2–4距":note;
        }
        GUI.Label(new Rect(r.x+9,r.y+37,r.width-18,r.height-39),note,CombatText(r.width<150?11:14,enabled?muted:new Color(.68f,.48f,.36f)));
        bool prev=GUI.enabled;GUI.enabled=prev&&enabled;
        if(GUI.Button(r,new GUIContent("",action["description"].S+" · "+cost),GUIStyle.none))SendBattleAction(action["id"].S);
        GUI.enabled=prev;
    }
    void DrawBattleActions(J b){
        Fill(new Rect(24,664,350,254),combatDark);Outline(new Rect(24,664,350,254),hairline);
        GUI.Label(new Rect(36,670,320,28),"行 招  /  普攻积式",CombatText(17,brass));
        for(int i=0;i<3;i++)DrawActionChoice(new Rect(34+i*111,706,106,62),BattleChoice(b,BasicKeys[i]),(i+1).ToString());
        string mode=b["fighters"][0]["mode"].S=="blade"?"mode_fist":"mode_blade";
        string[] ids={"advance","retreat",mode,"guard","parry","breathe"};string[] keys={"A","D","","G","F","R"};
        for(int i=0;i<ids.Length;i++)DrawActionChoice(new Rect(34+i%3*111,775+i/3*69,106,62),BattleChoice(b,ids[i]),keys[i]);
        var arts=b["actions"].A.Where(a=>a["kind"].S=="art"||(!a["kind"].Has&&a["damage_type"].Has)).ToArray();
        Fill(new Rect(390,664,834,254),combatDark);Outline(new Rect(390,664,834,254),hairline);
        GUI.Label(new Rect(404,670,550,28),"摧 破 功 法  /  "+arts.Length+" 门装配",CombatText(17,brass));
        var cont=BattleChoice(b,"continue");
        if(cont.Has&&CombatButton(new Rect(991,669,220,31),"继续运功 →",true,true,cont["description"].S))SendBattleAction("continue");
        for(int i=0;i<6;i++){
            Rect r=new Rect(402+i%3*271,706+i/3*99,262,90);
            if(i<arts.Length)DrawActionChoice(r,arts[i],ArtHotkeys[i]);
            else{Outline(r,new Color(.20f,.26f,.24f));GUI.Label(r,"未装配",CombatText(16,new Color(.24f,.31f,.28f),TextAnchor.MiddleCenter));}
        }
    }
    void DrawBattleLog(J b,Rect r){
        Fill(r,combatDark);Outline(r,hairline);
        GUI.Label(new Rect(r.x+14,r.y+6,r.width-28,30),"拆 招 录",CombatText(17,brass));
        GUILayout.BeginArea(new Rect(r.x+14,r.y+44,r.width-28,r.height-54));
        battleLogScroll=GUILayout.BeginScrollView(battleLogScroll);
        foreach(var row in b["log"].A.AsEnumerable().Reverse())GUILayout.Label(row["round"].S+" · "+(row["side"].I==0?"我":"客")+"  "+row["text"].S,CombatText(13,row["side"].I==0?new Color(.68f,.78f,.69f):new Color(.80f,.68f,.56f)));
        if(b["log"].A.Count==0)GUILayout.Label("双方站定。先看距离与对手意图，择招先行。",CombatText(14,muted));
        GUILayout.EndScrollView();GUILayout.EndArea();
    }
    void DrawBattleResult(J b){
        Rect r=new Rect(24,664,1200,254);Fill(r,combatDark);Outline(r,brass);
        GUI.Label(new Rect(50,680,1148,48),b["winner"].Has?b["fighters"][b["winner"].I]["name"].S+" 胜出":"此番平手",CombatText(29,brass,TextAnchor.MiddleCenter));
        for(int i=0;i<2;i++){var f=b["fighters"][i];var sum=b["summary"][i];GUI.Label(new Rect(70+i*575,738,540,67),f["name"].S+"   命中 "+sum["hits"].S+"   伤害 "+sum["damage"].S+"   破势 "+sum["breaks"].S+"\n剩余气血 "+Num(f["vigor"])+" · 外伤 "+Num(f["outer_wounds"])+" · 内伤 "+Num(f["inner_wounds"]),CombatText(17,ink,TextAnchor.MiddleCenter));}
        GUI.Label(new Rect(50,817,1148,25),"点到为止。体力、精力与装备损耗已结算，过程已记入年谱。",CombatText(15,muted,TextAnchor.MiddleCenter));
        if(b["event_id"].Has&&CombatButton(new Rect(425,859,210,39),"查看此战年谱")){battleView=false;OpenEvent(b["event_id"].I);}
        if(CombatButton(new Rect(665,859,210,39),"收剑归鞘"))battleView=false;
    }
    void DrawBattleHelp(bool modern){
        Fill(new Rect(0,0,1680,960),new Color(0,0,0,.75f));Rect r=new Rect(410,230,860,500);Fill(r,combatDark);Outline(r,brass);
        GUI.Label(new Rect(450,250,780,45),"进退之间，招式相生",CombatText(29,brass));
        string help=modern?"一、距离  兵刃普攻在2–4格，拳脚在0–2格；功法按各自范围判定。\n\n二、积式  三种普攻积攒刺、劈、崩；架招积攒凝，可替代任意式。最多保存四式。\n\n三、运功  强招先支付内力与式，再运功出手。进退、架招或切换路数会主动中止，消耗不返还；破势也会中断。\n\n四、拆解  架招减轻55%伤害。拆招可化解普攻并反震架势，挡不住摧破。调息时受击加重25%。\n\n五、节奏  空格切换暂停与连续行招。运行时自动普攻并继续运功；暂停时可手动行动。每拍按双方指令结算，进退与防守先于攻击，同类行动由你先行。":"这是升级前保存的旧战局，仍按原规则续战。结束后新约战启用积式、运功和破势规则。";
        GUI.Label(new Rect(450,310,780,340),help,CombatText(17,ink));
        if(CombatButton(new Rect(1000,667,220,40),"明白了 · 返回战场"))battleHelp=false;
    }
}
}
