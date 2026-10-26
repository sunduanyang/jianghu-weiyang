using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp : MonoBehaviour {
    public GameClient Client;
    public int Page;
    public J Person=new J(), City=new J(), Inventory=new J(), Market=new J(), Martial=new J(), Institution=new J(), Events=new J(), EventDetail=new J(), Logs=new J();
    public int SelectedPerson=-1, SelectedPlace=45;
    int seenRevision=-1, lastMonth=-1, personTab;
    string query="", talk="", relationQuery="", relationFilter="全部", rosterSort="乡籍", countryFilter="全部势力", factionFilter="全部门派", placeFilter="全部城镇", jobFilter="百业", gradeFilter="全部品级", circuitFilter="全部州路";
    bool includeDead, showFilters;
    Vector2 rosterScroll, personScroll, mainScroll, eventScroll;
    GUISkin skin;
    GUIStyle title, sub, small, text, heading, chip, selected, card, field, seal;
    Texture2D valley;
    readonly Dictionary<string,bool> folds=new Dictionary<string,bool>();
    readonly Color ink=new Color(.89f,.85f,.77f), paper=new Color(.094f,.118f,.118f), green=new Color(.25f,.36f,.29f), gold=new Color(.68f,.49f,.27f);
    public J W=>Client.World;
    public J Hero=>W["hero"];
    public bool Paused=>!W["autoplay"].B && W["job"]["remaining"].I==0 && !W["battle_active"].B;
    public static readonly string[] Pages={"山海舆图","城中行旅","行囊货栈","功法演武","天下势力","皮影衣橱","世界年谱","本地模型","存档设置"};
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot(){if(FindAnyObjectByType<JianghuApp>()!=null)return;var go=new GameObject("江湖未央 · Unity");DontDestroyOnLoad(go);go.AddComponent<JianghuApp>();}
    void Awake(){Client=GetComponent<GameClient>()??gameObject.AddComponent<GameClient>();valley=Resources.Load<Texture2D>("Art/river-valley");if(GameClient.Arg("-paperDemo")=="true"){Page=5;paperReferenceView=true;if(GameClient.Arg("-paperSkin")=="reference-bamboo")paperPreviewLeft=true;if(GameClient.Arg("-paperCalibration")=="true"){PaperSmokeReference("idle",0);paperDemoOverlay=true;}}}
    void Update(){
        if(!Client.Ready)return;
        UpdatePaperReference();
        UpdateBattle();
        if(seenRevision!=Client.Revision){seenRevision=Client.Revision;if(W["month"].I!=lastMonth){lastMonth=W["month"].I;ReloadPage();if(SelectedPerson>=0)SelectPerson(SelectedPerson);} }
        UpdateCityMovement();
    }
    static Texture2D Solid(Color c){var t=new Texture2D(1,1);t.SetPixel(0,0,c);t.Apply();return t;}
    public void OnGUI(){
        paperDraws.Clear();paperDrawOrigin=Vector2.zero;
        InitSkin();GUI.skin=skin;
        float scale=Mathf.Min(Screen.width/1680f,Screen.height/960f);GUI.matrix=Matrix4x4.TRS(new Vector3((Screen.width-1680*scale)/2,(Screen.height-960*scale)/2,0),Quaternion.identity,new Vector3(scale,scale,1));
        Rect screen=new Rect(0,0,1680,960);
        if(!Client.Ready){Tile(screen,"bg");Tex(screen,"vignette");Rect wait=new Rect(400,300,880,300);Panel(wait);GUILayout.BeginArea(new Rect(440,330,800,250));H("山河正在展开");T(string.IsNullOrEmpty(Client.Error)?Client.Status:Client.Error);T("首次启动会将原世界副本放入 Unity 的独立存档目录。",small);if(!string.IsNullOrEmpty(Client.Error)&&GUILayout.Button("打开日志目录"))Application.OpenURL(Client.SaveDir);GUILayout.EndArea();return;}
        if(dialogueOpen&&Person.Has){DrawDialogue();FlushPaperFigures();DrawTransition(screen);return;}
        if(personScreen){DrawPersonScreen();FlushPaperFigures();DrawTransition(screen);return;}
        if(Page==3&&battleView){DrawBattleScreen();FlushPaperFigures();DrawTransition(screen);return;}
        if(!IsStagePage(Page)){
            Rect body=BookScreen(Pages[Page]);
            Rect area=Page==5?new Rect(200,118,1280,760):new Rect(260,118,1160,760);
            paperDrawOrigin=area.position;GUILayout.BeginArea(area);
            switch(Page){case 2:DrawInventory();break;case 3:DrawMartial();break;case 4:DrawInstitutions();break;case 5:DrawPuppets();break;case 6:DrawEvents();break;case 7:DrawAI();break;case 8:DrawSettings();break;}
            GUILayout.EndArea();paperDrawOrigin=Vector2.zero;FlushPaperFigures();DrawTransition(screen);return;
        }
        // 戏: full-screen map or city, with floating panels
        basePage=Page;
        if(Page==0)DrawWorld(screen);else DrawCityScreen(screen);
        Tex(screen,"vignette");
        DrawTopBar();if(Page==0)DrawLayerRow();
        DrawRoster();DrawTracker();
        if(Page==0)DrawPlaceCard();else DrawNodeCard();
        DrawDock();DrawHeroCreation();DrawAdvance();
        FlushPaperFigures();DrawTransition(screen);
    }
    public void Go(int page){BeginTransition(page);Page=page;mainScroll=Vector2.zero;ReloadPage();}
    public void ReloadPage(){if(!Client.Ready)return;switch(Page){case 1:LoadCity(SelectedPlace);break;case 2:LoadInventory();break;case 3:Client.Fetch("/api/martial",j=>Martial=j);break;case 6:Client.Fetch("/api/events",j=>Events=j);break;case 7:Client.Fetch("/api/ai/logs",j=>Logs=j);break;}}
    public void SelectPerson(int id){SelectedPerson=id;RememberPerson(id);Client.Fetch("/api/npc/"+id,j=>Person=j);}
    public void HeroAction(string action,params object[] extra){Client.Act("/api/hero",Client.HeroBody(action,extra),j=>{Client.StartCoroutine(AfterHero());});}
    System.Collections.IEnumerator AfterHero(){yield return null;yield return Client.Refresh();if(Page==1)LoadCity(Hero["place"].I);}
    void Control(string action,int months=1){Client.Act("/api/control",J.O("action",action,"months",months,"fast",fastMode));}
    string rosterMode="人物";
    void DrawRoster(){
        Rect lp=LeftPanel;
        if(!rosterOpen){Panel(lp);if(GUI.Button(lp,"›",linkStyle))rosterOpen=true;return;}
        Panel(lp);
        GUILayout.BeginArea(new Rect(lp.x+10,lp.y+8,lp.width-20,lp.height-14));
        GUILayout.BeginHorizontal();foreach(string m in new[]{"人物","此地","地区"})if(GUILayout.Button(m,rosterMode==m?selected:chip))rosterMode=m;GUILayout.FlexibleSpace();if(GUILayout.Button("‹",linkStyle,GUILayout.Width(24)))rosterOpen=false;GUILayout.EndHorizontal();
        if(rosterMode=="地区"){
            rosterScroll=GUILayout.BeginScrollView(rosterScroll);
            foreach(var c in W["countries"].A){var towns=W["locations"].A.Where(x=>x["country"].I==c["id"].I).ToList();if(towns.Count==0)continue;
                GUILayout.BeginHorizontal();Rect sw=GUILayoutUtility.GetRect(10,22,GUILayout.Width(10));Fill(new Rect(sw.x,sw.y+6,8,10),Hex(c["color"].S));GUILayout.Label(c["name"].S+"  "+towns.Count+"城",sub);GUILayout.EndHorizontal();
                foreach(var t in towns)if(GUILayout.Button(t["name"].S+"   "+t["population"].S+"人",t["id"].I==SelectedPlace?rowOnStyle:rowStyle)){SelectedPlace=t["id"].I;campaignOptions=new J();mapPan=new Vector2(640-(float)t["x"].N,380-(float)t["y"].N);if(mapZoom<1.4f)mapZoom=1.4f;if(Page==1)LoadCity(SelectedPlace);}
            }
            GUILayout.EndScrollView();GUILayout.EndArea();return;
        }
        GUI.SetNextControlName("RosterSearch");query=GUILayout.TextField(query,field);
        IEnumerable<J> people;
        if(rosterMode=="此地"){people=W["npcs"].A.Where(n=>n["alive"].B&&n["place"].I==SelectedPlace&&(string.IsNullOrEmpty(query)||n["name"].S.Contains(query)||n["job"].S.Contains(query)));GUILayout.Label(Place(SelectedPlace)["name"].S+" · 此地中人",small);}
        else{
            GUILayout.BeginHorizontal();showFilters=GUILayout.Toggle(showFilters,"筛选");includeDead=GUILayout.Toggle(includeDead,"故人");GUILayout.EndHorizontal();
            if(showFilters){countryFilter=Cycle(countryFilter,new[]{"全部势力"}.Concat(W["countries"].A.Select(x=>x["name"].S)).ToArray());placeFilter=Cycle(placeFilter,new[]{"全部城镇"}.Concat(W["locations"].A.Select(x=>x["name"].S)).ToArray());factionFilter=Cycle(factionFilter,new[]{"全部门派"}.Concat(W["factions"].A.Select(x=>x["name"].S)).ToArray());circuitFilter=Cycle(circuitFilter,new[]{"全部州路"}.Concat(W["locations"].A.Select(x=>x["region"].S).Distinct()).ToArray());jobFilter=Cycle(jobFilter,new[]{"百业"}.Concat(W["professions"].D.Keys).ToArray());gradeFilter=Cycle(gradeFilter,new[]{"全部品级"}.Concat(W["identity_grades"].D.Keys).ToArray());rosterSort=Cycle(rosterSort,new[]{"乡籍","财富","武学","年岁","身份"});}
            people=W["npcs"].A.Where(n=>(includeDead||n["alive"].B)&&(string.IsNullOrEmpty(query)||n["name"].S.Contains(query)||n["job"].S.Contains(query))&&(placeFilter=="全部城镇"||Place(n["place"].I)["name"].S==placeFilter)&&(countryFilter=="全部势力"||Country(Place(n["place"].I)["country"].I)["name"].S==countryFilter)&&(factionFilter=="全部门派"||W["factions"].A.Any(f=>f["id"].I==n["faction"].I&&n["faction"].Has&&f["name"].S==factionFilter))&&(jobFilter=="百业"||n["job"].S==jobFilter)&&(gradeFilter=="全部品级"||n["identity"]["grade_label"].S==gradeFilter)&&(circuitFilter=="全部州路"||Place(n["place"].I)["region"].S==circuitFilter));
            if(rosterSort!="乡籍")people=people.OrderByDescending(n=>rosterSort=="财富"?n["wealth"].N:rosterSort=="武学"?n["skill"].N:rosterSort=="年岁"?n["age_months"].N:-n["identity"]["grade"].N);
        }
        var rows=people.ToList();GUILayout.Label(rows.Count+"人  ·  点击查阅一生",small);
        rosterScroll=GUILayout.BeginScrollView(rosterScroll);
        int first=Mathf.Max(0,(int)(rosterScroll.y/60)-2),last=Mathf.Min(rows.Count,first+16);
        GUILayout.Space(first*60);
        for(int k=first;k<last;k++){var n=rows[k];
            Rect rr=GUILayoutUtility.GetRect(200,60,GUILayout.ExpandWidth(true));
            if(GUI.Button(rr,GUIContent.none,n["id"].I==SelectedPerson?rowOnStyle:rowStyle)){SelectPerson(n["id"].I);OpenPersonScreen();}
            Portrait(new Rect(rr.x+6,rr.y+6,38,48),n["id"].I,n["sex"].S,n["age_months"].I,!n["alive"].B);
            GUI.Label(new Rect(rr.x+52,rr.y+6,rr.width-56,24),n["name"].S+"   "+(n["age_months"].I/12)+"岁",sub);
            GUI.Label(new Rect(rr.x+52,rr.y+31,rr.width-56,22),n["identity"]["title"].S+" · "+Place(n["place"].I)["name"].S,small);
        }
        GUILayout.Space(Mathf.Max(0,rows.Count-last)*60);
        GUILayout.EndScrollView();GUILayout.EndArea();
    }
    void DrawPersonTab(){
        switch(personTab){
            case 0:Metric("家资",Num(Person["wealth"])+"银");Bar("体力",Person["health"].N,100);Bar("武学",Person["skill"].N,100);Metric("声望",Num(Person["reputation"]));T(Person["goal"]["title"].S,sub);T(Person["last_action"]["label"].S);Details("本月四周行程",Person["monthly_actions"]);Details("生活近况",J.O("illness",Person["illness"],"wound",Person["wound"],"pregnancy",Person["pregnancy"],"plan",Person["plan"]));break;
            case 1:Details("稳定心性",Person["traits"],true);Details("当前情绪",Person["mood"],true);Details("世界信念",Person["beliefs"],true);Details("心中所欲",Person["desires"],true);Metric("压力",Num(Person["pressure"]));Metric("孤独",Num(Person["loneliness"]));Details("长远心愿",Person["goal"],true);Details("心性变化",Person["personality_changes"]);Details("记忆与理解",Person["memories"]);break;
            case 2:Details("身份与营生",Person["identity"],true);Details("各业本领",Person["career_view"],true);Details("可学营生",Person["career_options"]);Details("旧业履历",Person["career"]);break;
            case 3:T(Person["possessions"]["items"].A.Count+"件物品 · "+Num(Person["possessions"]["weight"])+"斤");foreach(var item in Person["possessions"]["items"].A){T(item["name"].S+(item["equipped"].B?" · 已装备":""),sub);T(item["description"].S,small);T("耐久 "+item["durability"].S+" · "+item["origin"].S,small);}if(Btn("从我的行囊赠物")){giftTarget=SelectedPerson;personScreen=false;Go(2);}break;
            case 4:relationQuery=GUILayout.TextField(relationQuery);relationFilter=Cycle(relationFilter,new[]{"全部","朋友","义亲","仇怨","故人"});foreach(var r in Person["relations_view"].A.Where(r=>r["name"].S.Contains(relationQuery)&&(relationFilter=="全部"||(relationFilter=="朋友"&&r["affinity"].N>20)||(relationFilter=="义亲"&&r["sworn"].B)||(relationFilter=="仇怨"&&r["affinity"].N<0)||(relationFilter=="故人"&&!r["alive"].B)))){if(GUILayout.Button(r["name"].S+" · "+r["label"].S+"\n情分 "+Num(r["affinity"])+" / 信任 "+Num(r["trust"]),rowStyle))SelectPerson(r["other"].I);}Details("与过客交情",Person["player_relation"],true);break;
            case 5:Details("门中身份",Person["membership_view"],true);if(Person["faction"].Has&&Btn("查看所属门派"))OpenInstitution(false,Person["faction"].I);Details("离合履历",Person["membership_history"]);break;
            case 6:DrawArtList(Person["martial_view"]["arts"],false);break;
            case 7:DrawEventList(Person["history"]);if(Btn("更早年谱")&&Person["history"].A.Count>0)Client.Fetch("/api/events?npc="+SelectedPerson+"&before="+Person["history"].A.Last()["id"].S,j=>Person["history"]=new J(Person["history"].A.Concat(j.A).ToList()));break;
        }
    }
    void Intervene(string action){Client.Act("/api/intervene",J.O("action",action,"npc_id",SelectedPerson,"text",talk),j=>{if(action=="challenge"){Martial=j;Page=3;battleView=true;personScreen=false;dialogueOpen=false;}else{Client.Status=j["reply"].S;dialogueText=j["reply"].S;if(action=="talk")talk="";SelectPerson(SelectedPerson);}});}
    public void OpenEvent(int id){personScreen=false;dialogueOpen=false;Page=6;Client.Fetch("/api/event/"+id,j=>EventDetail=j);}
    void DrawEventList(J list){foreach(var e in list.A){if(GUILayout.Button(e["date"].S+"  "+e["summary"].S,rowStyle))OpenEvent(e["id"].I);}}
    public J Place(int id)=>W["places"].A.FirstOrDefault(p=>p["id"].I==id)??new J();
    public J Country(int id)=>W["countries"].A.FirstOrDefault(p=>p["id"].I==id)??new J();
    public static string Num(J n)=>n.N.ToString("0.##");
    void H(string s){GUILayout.Label(s,heading);}
    void T(string s,GUIStyle style=null){GUILayout.Label(s,style??text);}
    bool Btn(string s,bool enabled=true){bool previous=GUI.enabled;GUI.enabled=previous&&enabled&&!Client.Busy;bool b=GUILayout.Button(s);GUI.enabled=previous;return b;}
    void Metric(string label,string value){GUILayout.BeginHorizontal();T(label,small);GUILayout.FlexibleSpace();T(value,sub);GUILayout.EndHorizontal();}
    void Bar(string label,double value,double max){Metric(label,value.ToString("0.#")+" / "+max.ToString("0.#"));var r=GUILayoutUtility.GetRect(10,6,GUILayout.ExpandWidth(true));Fill(r,new Color(.84f,.79f,.68f));Fill(new Rect(r.x,r.y,r.width*Mathf.Clamp01((float)(value/Math.Max(1,max))),r.height),vermilion);Fill(new Rect(r.x,r.yMax,r.width,1),new Color(.17f,.14f,.11f,.25f));GUILayout.Space(5);}
    string Cycle(string current,string[] options){if(options.Length==0)return current;int index=Array.IndexOf(options,current);if(index<0)index=0;if(GUILayout.Button(options[index]+"  ›",chip))index=(index+1)%options.Length;return options[index];}
    void Fill(Rect r,Color c){var old=GUI.color;GUI.color=c;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=old;}
    void Line(Vector2 a,Vector2 b,Color c,float width=1){if(Event.current.type!=EventType.Repaint)return;float length=Vector2.Distance(a,b);int steps=Mathf.Max(1,Mathf.CeilToInt(length/1.2f));float thickness=Mathf.Max(1.3f,width);for(int i=0;i<=steps;i++){Vector2 p=Vector2.Lerp(a,b,i/(float)steps);Fill(new Rect(p.x-thickness/2,p.y-thickness/2,thickness,thickness),c);}}
    static Color Hex(string s,float alpha=1){if(!ColorUtility.TryParseHtmlString(s,out var c))c=new Color(.52f,.58f,.44f);c.a=alpha;return c;}
    void Details(string label,J j,bool open=false){if(!j.Has||j.A.Count==0&&j.D.Count==0&&j.S=="")return;string key=SelectedPerson+":"+label;bool expanded=folds.TryGetValue(key,out var v)?v:open;if(GUILayout.Button((expanded?"▼ ":"▶ ")+label,chip))expanded=!expanded;folds[key]=expanded;if(expanded)Tree(j,0);}
    void Tree(J j,int depth){if(depth>5)return;if(j.value is List<J>){foreach(var item in j.A){if(item["name"].Has)T(item["name"].S,sub);else if(item["summary"].Has)T(item["summary"].S);Tree(item,depth+1);GUILayout.Space(5);}return;}if(j.value is Dictionary<string,J>){foreach(var p in j.D){if(!p.Value.Has||Hidden.Contains(p.Key))continue;string label=Labels.TryGetValue(p.Key,out var translated)?translated:p.Key;if(p.Value.value is Dictionary<string,J>||p.Value.value is List<J>){if(p.Value.A.Count==0&&p.Value.D.Count==0)continue;T(label,sub);Tree(p.Value,depth+1);}else Metric(label,p.Value.value is bool?(p.Value.B?"是":"否"):p.Value.S);}return;}T(j.S);}
    static readonly HashSet<string> Hidden=new HashSet<string>{"rng","rng_state","revision","version","template","owner","layout_id","event_id","last_event","trigger_events","choice_source","origin_event","last_contact","last_review_attempt","cooldowns","source_digest","id","native_place","opening_fund","contact_since"};
    static readonly Dictionary<string,string> Labels=new Dictionary<string,string>{
        {"name","名姓"},{"title","名目"},{"kind","类别"},{"description","说明"},{"summary","经历"},{"date","年月"},{"month","世界月数"},{"week","周次"},{"label","名称"},{"value","数值"},{"amount","金额"},{"reason","缘由"},{"text","内容"},{"status","近况"},{"progress","进展"},{"since","始于月数"},{"target","所向"},{"affinity","情分"},{"trust","信任"},{"romance","倾慕"},{"sworn","结义"},{"alive","在世"},{"other","对方"},{"honesty","诚实"},{"greed","贪欲"},{"aggression","暴烈"},{"empathy","体恤"},{"ambition","进取"},{"curiosity","好奇"},{"loyalty","忠义"},{"prudence","审慎"},{"courage","勇气"},{"diligence","勤勉"},{"risk","冒险"},{"sociability","合群"},{"joy","喜悦"},{"anger","愤怒"},{"fear","忧惧"},{"hope","期盼"},{"stress","压力"},{"loneliness","孤独"},{"fairness","公平"},{"world_fair","世道公正"},{"people_good","人心向善"},{"self_efficacy","自身有为"},{"security","安稳"},{"wealth","银两"},{"power","权势"},{"belonging","归属"},{"freedom","自由"},{"meaning","意义"},{"affection","情感"},{"mastery","精进"},{"health","体力"},{"skill","武学"},{"reputation","声望"},{"grade","品阶"},{"grade_label","品阶"},{"roles","兼有身份"},{"profession","营生"},{"proficiency","熟练度"},{"professions","各业本领"},{"skills","本领"},{"current","当前"},{"level","境界"},{"level_name","境界"},{"income","收入"},{"expense","支出"},{"faction","所属组织"},{"contribution","贡献"},{"rank","门中品级"},{"fund","库银"},{"revenue","入库"},{"expenses","支出"},{"tasks_completed","完成差事"},{"policy","施政"},{"treasury","国库"},{"tax_rate","税率"},{"public_health","公医"},{"security_bonus","治安增益"},{"order","治安"},{"taxes","税收"},{"relief","救济"},{"medical","医疗"},{"officials","官员"},{"cases","公议记录"},{"action","行止"},{"source","来源"},{"interpretation","个人理解"},{"emotion","感受"},{"belief","信念"},{"salience","印象深度"},{"confidence","确信"},{"illness","疾病"},{"wound","伤势"},{"pregnancy","妊娠"},{"plan","打算"},{"goal","心愿"},{"steps","步骤"},{"intent","意图"},{"job","职业"},{"facts","共同事实"},{"perspectives","各人理解"},{"causes","前因"},{"cause_events","前因事件"},{"stamina","精力"},{"practice","修习"},{"realm","重数"},{"qi","内力"},{"forms","式"},{"outer_guard","外伤护体"},{"inner_guard","内伤护体"},{"requirement","所需武学"},{"importance","影响"},{"population","人口"},{"settlements","城镇数"},{"poor","贫困居民"},{"patients","病弱居民"},{"members","成员"},{"local_members","本地成员"},{"leader","主持"},{"ethos","旨趣"},{"art","传习"},{"focus","专长"},{"ranks","成员品级"},{"horizon_years","所期年数"},{"minimum_months","践行月数"},{"born","出生"},{"deceased","亡者"},{"duration","时长"},{"old","从前"},{"new","如今"},{"delta","变化"},{"career","营生"},{"history","履历"},{"records","记录"},{"current_job","当前营生"},{"aptitude","禀赋"},{"demand","本地需求"},{"practice_gain","修习所得"},{"income_bonus","营生增益"},{"wealth_spent","支出银两"},{"week_cost","占用周次"},{"cooldown","间隔"},{"theme","风貌"},{"label_x","图中横位"},{"label_y","图中纵位"}};
}
}
