using System;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    bool realtimeLog;
    readonly Color breathBlue=new Color(.32f,.55f,.69f),injuryPurple=new Color(.60f,.35f,.65f);
    void UpdateRealtimeBattle(J b){
        if(battleHelp)return;
        string command=null;
        if(Input.GetKeyDown(KeyCode.A))command="advance";
        if(Input.GetKeyDown(KeyCode.D))command="retreat";
        if(Input.GetKeyUp(KeyCode.A)||Input.GetKeyUp(KeyCode.D))command=Input.GetKey(KeyCode.A)?"advance":Input.GetKey(KeyCode.D)?"retreat":"stop";
        if(Input.GetKeyDown(KeyCode.G))command="guard";
        if(Input.GetKeyDown(KeyCode.F))command="parry";
        if(Input.GetKeyDown(KeyCode.R))command="breathe";
        if(Input.GetKeyDown(KeyCode.T))command="toggle_auto";
        if(Input.GetKeyDown(KeyCode.V))command="movement_art";
        if(Input.GetKeyDown(KeyCode.H))command="heal";
        if(Input.GetKeyDown(KeyCode.Tab))command="cancel";
        for(int i=0;i<3;i++)if(Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1+i)))command="mode_"+new[]{"fist","blade","stone"}[i];
        KeyCode[] keys={KeyCode.Q,KeyCode.W,KeyCode.E,KeyCode.Z,KeyCode.X,KeyCode.C};
        var arts=b["actions"].A.Where(a=>a["kind"].S=="art").ToArray();
        for(int i=0;i<arts.Length&&i<6;i++)if(Input.GetKeyDown(keys[i]))command=arts[i]["id"].S;
        if(command!=null)pendingBattleCommand=command;
        if(Client.Busy)return;
        if(Client.Error.Length>0){battlePaused=true;if(battleLastError!=Client.Error){battleLastError=Client.Error;Client.Fetch("/api/martial",j=>Martial=j);}return;}
        battleLastError="";
        if(pendingBattleCommand!=null){string next=pendingBattleCommand;pendingBattleCommand=null;SendBattleAction(next);return;}
        if(!battlePaused&&Time.realtimeSinceStartup>=nextBattleBeat)SendBattleAction("tick");
    }
    void CombatLine(Vector2 a,Vector2 b,float width,Color color){
        var matrix=GUI.matrix;Vector2 d=b-a;
        GUI.matrix=matrix*Matrix4x4.TRS(new Vector3(a.x,a.y,0),Quaternion.Euler(0,0,Mathf.Atan2(d.y,d.x)*Mathf.Rad2Deg),Vector3.one);
        Fill(new Rect(0,-width/2,d.magnitude,width),color);GUI.matrix=matrix;
    }
    void CombatRing(Vector2 center,float radius,float value,Color color,float width=3){
        for(int i=0;i<64;i++){
            float a=(i*360f/64-90)*Mathf.Deg2Rad,c=((i+1)*360f/64-90)*Mathf.Deg2Rad;
            CombatLine(center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,center+new Vector2(Mathf.Cos(c),Mathf.Sin(c))*radius,width,i<value*64?color:new Color(.15f,.20f,.19f));
        }
    }
    void RealtimeBadge(Rect r,string word,Color color){
        CombatRing(r.center,Mathf.Min(r.width,r.height)/2-2,1,color,1.5f);
        GUI.Label(r,word,CombatText(18,color,TextAnchor.MiddleCenter));
    }
    void DrawRealtimeBattle(J b){
        bool done=b["status"].S=="finished";
        var fs=b["fighters"].A;
        if(fs.Count<2)return;
        Rect stage=new Rect(0,120,1680,570);
        if(valley!=null){var prev=GUI.color;GUI.color=new Color(.85f,.84f,.76f);GUI.DrawTexture(stage,valley,ScaleMode.ScaleAndCrop);GUI.color=prev;}
        Fill(stage,new Color(.22f,.24f,.22f,.12f));
        float gap=200+battleVisualDistance*71,px=840-gap/2,ex=840+gap/2;
        int serial=b["serial"].I;
        string key=b["id"].S+":"+serial;
        if(paperBattleKey!=key){paperBattleKey=key;paperBattleStart=Time.realtimeSinceStartup;}
        float elapsed=(Time.realtimeSinceStartup-paperBattleStart)*battleSpeed;
        string pose0=RealtimePose(b,0,elapsed,out float phase0),pose1=RealtimePose(b,1,elapsed,out float phase1);
        DrawPaperPerson(new Rect(px-140,403,280,265),PaperPerson(-1,"男",216),true,pose0,false,phase0);
        DrawPaperPerson(new Rect(ex-140,403,280,265),Npc(fs[1]["id"].I),false,pose1,true,phase1);
        DrawRealtimeCast(new Rect(px-155,360,310,44),fs[0],jade);
        DrawRealtimeCast(new Rect(ex-155,360,310,44),fs[1],combatRed);
        DrawBattleImpacts(b,px,ex,elapsed);
        // Injury capacity, followed by body locations and the three hit comparisons.
        DrawRealtimeHeader(fs[0],false);DrawRealtimeHeader(fs[1],true);
        Fill(new Rect(770,0,140,124),combatDark);
        CombatRing(new Vector2(840,53),43,1,brass,2);
        GUI.Label(new Rect(794,15,92,45),done?"终":battlePaused?"Ⅱ":"▶",CombatText(32,brass,TextAnchor.MiddleCenter));
        GUI.Label(new Rect(784,63,112,25),b["elapsed"].N.ToString("0.0")+" 息",CombatText(15,combatLight,TextAnchor.MiddleCenter));
        if(CombatButton(new Rect(777,92,79,27),battlePaused?"开始":"暂停",!battlePaused,!done))ToggleBattleClock();
        if(CombatButton(new Rect(859,92,43,27),"×"+battleSpeed,false,!done)){battleSpeed=battleSpeed%3+1;nextBattleBeat=Time.realtimeSinceStartup+BattleInterval();}
        GUI.Label(new Rect(200,164,400,27),fs[0]["name"].S+"  ·  "+fs[0]["inner"].S,CombatText(17,new Color(.17f,.22f,.19f)));
        GUI.Label(new Rect(1080,164,400,27),fs[1]["name"].S+"  ·  "+fs[1]["inner"].S,CombatText(17,new Color(.17f,.22f,.19f),TextAnchor.MiddleRight));
        DrawRealtimeRuler(b,new Rect(260,656,1160,32));
        Fill(new Rect(0,690,1680,270),combatDark);Outline(new Rect(0,690,1680,270),hairline);
        DrawRealtimeWheel(fs[0],new Vector2(178,827),false);
        DrawRealtimeWheel(fs[1],new Vector2(1502,827),true);
        if(done)DrawRealtimeResult(b);else DrawRealtimeDeck(b);
        string tip=Client.Error.Length>0?Client.Error:GUI.tooltip.Length>0?GUI.tooltip:"空格 启停   A / D 按住移动   1 / 2 / 3 切换兵器   Q W E Z X C 功法   T 自动普攻   V 身法   H 疗伤";
        GUI.Label(new Rect(20,931,1175,24),tip,CombatText(12,Client.Error.Length>0?combatRed:muted));
        if(CombatButton(new Rect(1200,931,90,25),"战斗说明")){battleHelp=true;battlePaused=true;}
        if(CombatButton(new Rect(1298,931,90,25),"拆招记录")){realtimeLog=!realtimeLog;battlePaused=true;}
        if(CombatButton(new Rect(1396,931,130,25),done?"返回功法":"暂离战场")){battlePaused=true;battleView=false;}
        if(!done&&CombatButton(new Rect(1534,931,126,25),"收招认输"))SendBattleAction("surrender");
        if(realtimeLog){FlushPaperFigures();DrawBattleLog(b,new Rect(400,175,880,460));if(CombatButton(new Rect(1115,180,155,30),"关闭记录"))realtimeLog=false;}
        if(battleHelp){FlushPaperFigures();DrawRealtimeHelp();}
    }
    string RealtimePose(J b,int side,float elapsed,out float phase){
        var f=b["fighters"][side];string clip="idle";
        if(b["status"].S=="finished"&&b["winner"].Has&&b["winner"].I!=side)clip="defeated";
        else if(f["casting"].Has||f["guarding"].B||f["parrying"].B)clip="guard";
        else if(f["move"].I!=0||f["target_distance"].Has&&Math.Abs(f["target_distance"].N-b["distance"].N)>.2)clip="walk";
        if(elapsed<.6f&&b["status"].S=="active"){
            var row=b["log"].A.LastOrDefault(r=>r["damage"].Has);
            if(row!=null){if(row["side"].I==side)clip="attack";else if(row["hit"].B)clip="hurt";}
        }
        phase=PaperRig.Phase(PaperCast["rig"],clip,elapsed);return clip;
    }
    void DrawRealtimeHeader(J f,bool right){
        float x=right?914:122,w=644;
        Fill(new Rect(right?1558:0,0,122,162),combatDark);
        DrawPaperPortrait(new Rect(right?1564:6,8,110,109),right?Npc(f["id"].I):PaperPerson(-1,"男",216),!right);
        GUI.Label(new Rect(right?1564:6,119,110,26),f["name"].S,CombatText(16,combatLight,TextAnchor.MiddleCenter));
        Fill(new Rect(x,0,w,120),combatDark);Outline(new Rect(x,0,w,120),hairline);
        float slotWidth=(w-16)/12;
        // Each capacity cell can contain several injury types; render fractional segments.
        float used=0;
        foreach(var injury in f["injury_slots"].A){
            float amount=(float)injury["slots"].N;
            Color color=injury["kind"].S=="internal"?injuryPurple:injury["kind"].S=="opening"?brass:injury["kind"].S=="seal"?breathBlue:combatRed;
            float start=used;used=Mathf.Min(12,used+amount);
            Fill(new Rect(right?x+w-8-used*slotWidth:x+8+start*slotWidth,10,(used-start)*slotWidth,40),color);
        }
        for(int i=0;i<12;i++)Outline(new Rect(x+8+i*slotWidth,10,slotWidth-2,40),hairline);
        GUI.Label(new Rect(x+10,52,w-20,20),"伤势 "+f["occupied_slots"].N.ToString("0.0")+" / 12    外伤 "+Num(f["outer_wounds"])+"    内伤 "+Num(f["inner_wounds"]),CombatText(12,muted,right?TextAnchor.MiddleRight:TextAnchor.MiddleLeft));
        string[] names={"力道","精妙","迅疾"},off={"force","precision","swiftness"},def={"unload","deflect","dodge"},defNames={"卸力","拆招","闪避"};
        for(int i=0;i<3;i++)GUI.Label(new Rect(x+12+i*205,78,203,31),names[i]+" "+f[off[i]].S+"  /  "+defNames[i]+" "+f[def[i]].S,CombatText(13,i==0?combatRed:i==1?jade:brass));
        for(int i=0;i<5;i++)RealtimeBadge(new Rect(right?x+w-38-i*37:x+2+i*37,125,32,32),new[]{"头","胸","腹","臂","腿"}[i],hairline);
        GUI.Label(new Rect(right?x:x+210,127,w-222,25),"破绽 "+f["openings"].A.Count+"  封穴 "+f["seals"].A.Count+"    内力 "+Num(f["qi"])+" / "+Num(f["qi_max"]),CombatText(13,jade,right?TextAnchor.MiddleLeft:TextAnchor.MiddleRight));
    }
    void DrawRealtimeCast(Rect r,J f,Color color){
        if(!f["casting"].Has)return;
        var c=f["casting"];Fill(r,combatDark);Outline(r,color);
        float fraction=1-(float)(c["remaining"].N/Math.Max(.01,c["total"].N));
        Fill(new Rect(r.x,r.yMax-4,r.width*fraction,4),color);
        GUI.Label(r,c["name"].S+"  "+c["remaining"].N.ToString("0.0")+"息",CombatText(20,combatLight,TextAnchor.MiddleCenter));
    }
    void DrawRealtimeRuler(J b,Rect r){
        Fill(r,combatDark);var f=b["fighters"][0];var enemy=b["fighters"][1];
        foreach(var side in new[]{0,1}){
            var range=(side==0?f:enemy)["basic_reach"];
            float min=(float)(range[0].N-1)/9,max=(float)(range[1].N-1)/9;
            Fill(new Rect(r.x+r.width*min,r.y+(side==0?0:17),r.width*(max-min),12),side==0?new Color(.20f,.47f,.39f,.8f):new Color(.50f,.25f,.21f,.8f));
        }
        for(int i=1;i<=10;i++)GUI.Label(new Rect(r.x+(i-1)/9f*r.width-12,r.y+3,24,22),i.ToString(),CombatText(12,combatLight,TextAnchor.MiddleCenter));
        float d=(float)(b["distance"].N-1)/9;
        Fill(new Rect(r.x+d*r.width-2,r.y-9,4,42),brass);
        Rect label=new Rect(780,r.y-31,120,28);Fill(label,combatDark);Outline(label,jade);GUI.Label(label,b["distance"].N.ToString("0.0"),CombatText(23,brass,TextAnchor.MiddleCenter));
    }
    void DrawRealtimeWheel(J f,Vector2 c,bool enemy){
        CombatRing(c,83,(float)f["posture"].N/100,jade,7);
        CombatRing(c,70,(float)f["breath"].N/100,breathBlue,5);
        CombatRing(c,55,1,hairline,1);
        var tokens=f["tokens"].A;
        for(int i=0;i<9;i++){
            float angle=(i*40-90)*Mathf.Deg2Rad;Vector2 p=c+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*102;
            RealtimeBadge(new Rect(p.x-16,p.y-16,32,32),i<tokens.Count?FormWord(tokens[i].S):"·",i<tokens.Count?brass:hairline);
        }
        GUI.Label(new Rect(c.x-65,c.y-35,130,32),f["mode_name"].S,CombatText(23,combatLight,TextAnchor.MiddleCenter));
        GUI.Label(new Rect(c.x-60,c.y,120,25),"架 "+f["posture"].N.ToString("0")+"  气 "+f["breath"].N.ToString("0"),CombatText(14,jade,TextAnchor.MiddleCenter));
        GUI.Label(new Rect(c.x-78,c.y+28,156,25),"内伤比例 "+f["inner_ratio"].S+"%",CombatText(13,injuryPurple,TextAnchor.MiddleCenter));
        GUI.Label(new Rect(c.x-136,699,272,26),"脚力 "+f["mobility"].N.ToString("0")+"    攻势 "+f["attack_charge"].N.ToString("0.0")+" / 3",CombatText(14,brass,TextAnchor.MiddleCenter));
        for(int i=0;i<3;i++){
            Rect r=new Rect(c.x-58+i*40,727,34,12);Fill(r,hairline);Fill(new Rect(r.x,r.y,r.width*Mathf.Clamp01((float)f["attack_charge"].N-i),r.height),brass);
        }
        if(!enemy){
            if(CombatButton(new Rect(39,900,66,24),"外伤＋"))SendBattleAction("ratio_down");
            if(CombatButton(new Rect(251,900,66,24),"内伤＋"))SendBattleAction("ratio_up");
        }
    }
    void RealtimeCard(Rect r,J action,string hotkey,string glyph){
        if(!action.Has){Outline(r,hairline);GUI.Label(r,"未装配",CombatText(13,hairline,TextAnchor.MiddleCenter));return;}
        bool enabled=action["enabled"].B;
        bool selectedAction=Martial["battle"]["fighters"][0]["casting"]["action"].S==action["id"].S;
        Fill(r,selectedAction?new Color(.12f,.27f,.23f):new Color(.07f,.12f,.12f));Outline(r,selectedAction?jade:hairline);
        GUI.Label(new Rect(r.x+5,r.y+4,r.width-10,17),hotkey,CombatText(11,brass,TextAnchor.UpperLeft));
        GUI.Label(new Rect(r.x+8,r.y+16,r.width-16,37),glyph,CombatText(29,enabled?jade:hairline,TextAnchor.MiddleCenter));
        GUI.Label(new Rect(r.x+4,r.y+55,r.width-8,25),action["label"].S,CombatText(16,enabled?combatLight:muted,TextAnchor.MiddleCenter));
        string note=action["reason"].S;
        if(note.Length==0&&action["kind"].S=="art")note=action["reach"][0].S+"–"+action["reach"][1].S+"距 · "+action["forms"].S+"式";
        GUI.Label(new Rect(r.x+3,r.y+82,r.width-6,24),note,CombatText(11,enabled?muted:combatRed,TextAnchor.MiddleCenter));
        string hint=action["description"].S;
        if(action["kind"].S=="art")hint="架势 "+action["posture_cost"].S+" / 提气 "+action["breath_cost"].S+" / 内力 "+action["qi"].S+" / 施展 "+action["cast"].S+"息；"+hint;
        bool old=GUI.enabled;GUI.enabled=old&&enabled&&!Client.Busy;
        if(GUI.Button(r,new GUIContent("",hint),GUIStyle.none))SendBattleAction(action["id"].S);GUI.enabled=old;
    }
    void DrawRealtimeDeck(J b){
        var f=b["fighters"][0];
        GUI.Label(new Rect(345,697,990,27),"战 斗 功 能     ·     兵器式轮积式 → 架势 / 提气 → 施展摧破",CombatText(15,jade,TextAnchor.MiddleCenter));
        var arts=b["actions"].A.Where(a=>a["kind"].S=="art").ToArray();
        for(int i=0;i<6;i++)RealtimeCard(new Rect(346+i*130,732,123,109),i<arts.Length?arts[i]:new J(),ArtHotkeys[i],new[]{"拳","剑","掌","锋","劲","破"}[i]);
        RealtimeCard(new Rect(1126,732,99,109),BattleChoice(b,"movement_art"),"V","步");
        RealtimeCard(new Rect(1232,732,99,109),BattleChoice(b,"heal"),"H","疗");
        string[] ids={"mode_fist","mode_blade","mode_stone","toggle_auto","guard","parry","breathe","cancel"};
        string[] labels={"1 拳脚","2 随身剑","3 飞石","T 自动普攻 "+(f["auto_attack"].B?"开":"关"),"G 守御","F 拆招","R 调息","中止施展"};
        for(int i=0;i<8;i++){
            var a=BattleChoice(b,ids[i]);if(CombatButton(new Rect(346+i*124,851,118,28),labels[i],i<3&&f["mode"].S==ids[i].Substring(5),a["enabled"].B,a["reason"].S+" "+a["description"].S))SendBattleAction(ids[i]);
        }
        GUI.Label(new Rect(346,889,92,28),"攻击部位",CombatText(13,muted));
        string[] parts={"head","chest","abdomen","arm","leg"},names={"头颈","胸背","腰腹","双臂","双腿"};
        for(int i=0;i<5;i++)if(CombatButton(new Rect(438+i*87,889,79,27),names[i],f["target"].S==parts[i]))SendBattleAction("target_"+parts[i]);
        if(CombatButton(new Rect(913,889,125,27),"前进 / A"))SendBattleAction("advance");
        if(CombatButton(new Rect(1046,889,125,27),"停步"))SendBattleAction("stop");
        if(CombatButton(new Rect(1179,889,151,27),battlePaused?"单步 0.2息":"后退 / D"))SendBattleAction(battlePaused?"tick":"retreat");
    }
    void DrawRealtimeResult(J b){
        Rect r=new Rect(345,728,990,187);Outline(r,brass);
        GUI.Label(new Rect(365,740,950,40),b["winner"].Has?b["fighters"][b["winner"].I]["name"].S+" 胜出":"此番平手",CombatText(28,brass,TextAnchor.MiddleCenter));
        GUI.Label(new Rect(365,782,950,28),b["reason"].S+" · "+b["elapsed"].N.ToString("0.0")+"息",CombatText(15,muted,TextAnchor.MiddleCenter));
        for(int i=0;i<2;i++)GUI.Label(new Rect(370+i*480,817,460,35),b["fighters"][i]["name"].S+"   命中 "+b["summary"][i]["hits"].S+"   伤害 "+b["summary"][i]["damage"].S,CombatText(17,combatLight,TextAnchor.MiddleCenter));
        GUI.Label(new Rect(370,863,940,30),"切磋结果已记入年谱，体力、精力及装备损耗已结算。",CombatText(14,muted,TextAnchor.MiddleCenter));
    }
    void DrawRealtimeHelp(){
        Fill(new Rect(0,0,1680,960),new Color(0,0,0,.8f));Rect r=new Rect(330,185,1020,590);Fill(r,combatDark);Outline(r,brass);
        GUI.Label(new Rect(370,207,940,48),"战斗说明 · 连续时间与距离",CombatText(29,brass));
        string help="空格暂停／继续，1、2、3倍速。暂停只停止时间，仍可切换兵器、选择部位或施展；单步推进0.2息。\n\n距离为1.0–10.0。按住A前进、D后退；绿色为己方兵器范围，红色为对方范围。移动消耗脚力，不打断施展；功法出手时重新判断距离。\n\n普攻消耗一格攻势，按兵器式轮获得式；最多存九式。摧破消耗对应式、内力，以及依内伤比例分配的架势和提气，施展后有回气。\n\n力道对卸力、精妙对拆招、迅疾对闪避，三项命中共同影响发挥。内外伤、暂时破绽与封穴共用12格承受上限；破绽和封穴随时间消退。\n\nG守御，F化解下一次普攻，V施展身法，H疗伤。重击可打断施展，疗伤受到伤害即中断；主动中止也不返还消耗。\n\n这是依参考机制编写的本作规则；完整原作的功法特效、毒、同道助战和细部数值尚未接入。";
        GUI.Label(new Rect(370,275,940,420),help,CombatText(17,ink));
        if(CombatButton(new Rect(1080,713,215,39),"返回战场"))battleHelp=false;
    }
}
}
