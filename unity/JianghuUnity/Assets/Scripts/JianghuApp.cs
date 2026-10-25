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
    Texture2D portraits, valley;
    readonly Dictionary<string,bool> folds=new Dictionary<string,bool>();
    readonly Color ink=new Color(.17f,.22f,.20f), paper=new Color(.94f,.92f,.85f), green=new Color(.25f,.36f,.29f), gold=new Color(.68f,.49f,.27f);
    public J W=>Client.World;
    public J Hero=>W["hero"];
    public bool Paused=>!W["autoplay"].B && W["job"]["remaining"].I==0 && !W["battle_active"].B;
    public static readonly string[] Pages={"山海舆图","城中行旅","行囊货栈","功法演武","天下势力","皮影换装","世界年谱","本地模型","存档设置"};
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot(){if(FindAnyObjectByType<JianghuApp>()!=null)return;var go=new GameObject("江湖未央 · Unity");DontDestroyOnLoad(go);go.AddComponent<JianghuApp>();}
    void Awake(){Client=GetComponent<GameClient>()??gameObject.AddComponent<GameClient>();portraits=Resources.Load<Texture2D>("Art/portraits");valley=Resources.Load<Texture2D>("Art/river-valley");}
    void Update(){
        if(!Client.Ready)return;
        if(seenRevision!=Client.Revision){seenRevision=Client.Revision;if(W["month"].I!=lastMonth){lastMonth=W["month"].I;ReloadPage();if(SelectedPerson>=0)SelectPerson(SelectedPerson);} }
        if(Page==1&&City.Has&&Hero["created"].B&&City["place"].I==Hero["place"].I&&!Client.Busy&&Paused&&GUIUtility.keyboardControl==0){
            int dr=Input.GetKeyDown(KeyCode.UpArrow)?-1:Input.GetKeyDown(KeyCode.DownArrow)?1:0;
            int dc=Input.GetKeyDown(KeyCode.LeftArrow)?-1:Input.GetKeyDown(KeyCode.RightArrow)?1:0;
            if(dr!=0||dc!=0)HeroAction("walk","place",Hero["place"],"row",Hero["row"].I+dr,"col",Hero["col"].I+dc);
        }
    }
    void InitSkin(){
        if(skin!=null)return;
        skin=Instantiate(GUI.skin);skin.font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","SimSun","Arial"},16);
        skin.label.fontSize=15;skin.label.normal.textColor=ink;skin.label.wordWrap=true;
        skin.button.fontSize=14;skin.button.normal.textColor=ink;skin.button.normal.background=Solid(new Color(.88f,.87f,.79f));skin.button.hover.background=Solid(new Color(.80f,.83f,.73f));skin.button.active.background=Solid(new Color(.72f,.78f,.65f));skin.button.padding=new RectOffset(11,11,8,8);skin.button.margin=new RectOffset(3,3,3,3);skin.button.wordWrap=true;
        skin.textField.fontSize=15;skin.textField.normal.textColor=ink;skin.textField.focused.textColor=ink;skin.textField.normal.background=Solid(new Color(.99f,.98f,.94f));skin.textField.focused.background=skin.textField.normal.background;skin.textField.padding=new RectOffset(9,9,8,8);
        skin.textArea=new GUIStyle(skin.textField){wordWrap=true};
        skin.toggle.fontSize=14;skin.toggle.normal.textColor=ink;skin.toggle.onNormal.textColor=green;
        skin.box.normal.background=Solid(new Color(.90f,.90f,.82f));
        text=new GUIStyle(skin.label){wordWrap=true,fontSize=15};small=new GUIStyle(text){fontSize=12};small.normal.textColor=new Color(.43f,.47f,.40f);
        title=new GUIStyle(text){fontSize=30,fontStyle=FontStyle.Bold};heading=new GUIStyle(text){fontSize=20,fontStyle=FontStyle.Bold};sub=new GUIStyle(text){fontSize=16,fontStyle=FontStyle.Bold};
        chip=new GUIStyle(skin.button){fontSize=13,padding=new RectOffset(8,8,6,6)};
        selected=new GUIStyle(skin.button);selected.normal.background=Solid(green);selected.normal.textColor=Color.white;
        card=new GUIStyle(skin.box){padding=new RectOffset(14,14,12,12),margin=new RectOffset(3,3,5,5)};
        field=new GUIStyle(skin.textField);
        seal=new GUIStyle(title){alignment=TextAnchor.MiddleCenter,fontSize=28};seal.normal.textColor=paper;
    }
    static Texture2D Solid(Color c){var t=new Texture2D(1,1);t.SetPixel(0,0,c);t.Apply();return t;}
    public void OnGUI(){
        InitSkin();GUI.skin=skin;
        float scale=Mathf.Min(Screen.width/1680f,Screen.height/960f);GUI.matrix=Matrix4x4.TRS(new Vector3((Screen.width-1680*scale)/2,(Screen.height-960*scale)/2,0),Quaternion.identity,new Vector3(scale,scale,1));
        Fill(new Rect(0,0,1680,960),paper);Fill(new Rect(0,0,1680,97),ink);
        if(valley!=null){var old=GUI.color;GUI.color=new Color(1,1,1,.20f);GUI.DrawTexture(new Rect(650,0,1030,97),valley,ScaleMode.ScaleAndCrop);GUI.color=old;}
        Fill(new Rect(22,21,49,55),new Color(.59f,.24f,.18f));GUI.Label(new Rect(22,21,49,55),"江",seal);
        var light=new GUIStyle(title);light.normal.textColor=paper;GUI.Label(new Rect(87,16,520,44),"江湖未央",light);
        light=new GUIStyle(small);light.normal.textColor=new Color(.80f,.82f,.70f);GUI.Label(new Rect(90,62,600,25),"UNITY  /  山河一卷 · 众生百相",light);
        GUI.Label(new Rect(1170,30,470,35),Client.Ready?W["date"].S+"  ·  "+W["population"].S+"人在世":"正在开启江湖",light);
        GUILayout.BeginArea(new Rect(18,105,1644,49));GUILayout.BeginHorizontal();
        for(int i=0;i<Pages.Length;i++){int page=i;if(GUILayout.Button(Pages[i],Page==i?selected:skin.button,GUILayout.Height(39)))Go(page);}
        GUILayout.EndHorizontal();GUILayout.EndArea();
        if(!Client.Ready){GUILayout.BeginArea(new Rect(420,300,840,300));H("山河正在展开");T(string.IsNullOrEmpty(Client.Error)?Client.Status:Client.Error);T("首次启动会将原世界副本放入 Unity 的独立存档目录。",small);if(!string.IsNullOrEmpty(Client.Error)&&GUILayout.Button("打开日志目录"))Application.OpenURL(Client.SaveDir);GUILayout.EndArea();return;}
        Fill(new Rect(18,163,1644,52),new Color(.87f,.88f,.79f));GUILayout.BeginArea(new Rect(28,170,1620,40));GUILayout.BeginHorizontal();
        T(W["date"].S+"  /  "+(W["battle_active"].B?"切磋进行中":W["autoplay"].B?"自行演化":W["job"]["remaining"].I>0?"推演中 · 余"+W["job"]["remaining"].S+"月":"江湖已暂停"),sub);
        GUILayout.FlexibleSpace();GUI.enabled=!Client.Busy;
        if(Btn("结束本月",Paused&&Hero["created"].B))HeroAction("end_turn");
        if(Btn("一月",!W["battle_active"].B))Control("advance",1);if(Btn("一年",!W["battle_active"].B))Control("advance",12);if(Btn("十年",!W["battle_active"].B))Control("advance",120);if(Btn("五十年",!W["battle_active"].B))Control("advance",600);
        if(Btn("自行演化",!W["battle_active"].B))Control("auto");if(Btn("暂停"))Control("pause");if(Btn("保存"))Client.Act("/api/save",J.O());GUI.enabled=true;
        GUILayout.EndHorizontal();GUILayout.EndArea();
        Fill(new Rect(18,225,246,693),new Color(.90f,.90f,.83f));Fill(new Rect(276,225,1010,693),new Color(.97f,.95f,.89f));Fill(new Rect(1298,225,364,693),new Color(.90f,.90f,.83f));
        DrawRoster();DrawPerson();
        GUILayout.BeginArea(new Rect(289,237,984,669));
        switch(Page){case 0:DrawAtlas();break;case 1:DrawCity();break;case 2:DrawInventory();break;case 3:DrawMartial();break;case 4:DrawInstitutions();break;case 5:DrawPuppets();break;case 6:DrawEvents();break;case 7:DrawAI();break;case 8:DrawSettings();break;}
        GUILayout.EndArea();
        GUI.Label(new Rect(23,929,1630,26),!string.IsNullOrEmpty(Client.Error)?"提示："+Client.Error:Client.Busy?"正在结算，请稍候…":Client.Status+"   /   每月自动存档 · "+(W["ai"]["config"]["enabled"].B?"本地模型已启用":"规则世界"),small);
    }
    public void Go(int page){Page=page;mainScroll=Vector2.zero;ReloadPage();}
    public void ReloadPage(){if(!Client.Ready)return;switch(Page){case 1:LoadCity(SelectedPlace);break;case 2:LoadInventory();break;case 3:Client.Fetch("/api/martial",j=>Martial=j);break;case 6:Client.Fetch("/api/events",j=>Events=j);break;case 7:Client.Fetch("/api/ai/logs",j=>Logs=j);break;}}
    public void SelectPerson(int id){SelectedPerson=id;Client.Fetch("/api/npc/"+id,j=>Person=j);}
    public void HeroAction(string action,params object[] extra){Client.Act("/api/hero",Client.HeroBody(action,extra),j=>{Client.StartCoroutine(AfterHero());});}
    System.Collections.IEnumerator AfterHero(){yield return null;yield return Client.Refresh();if(Page==1)LoadCity(Hero["place"].I);}
    void Control(string action,int months=1){Client.Act("/api/control",J.O("action",action,"months",months,"fast",fastMode));}
    void DrawRoster(){
        GUILayout.BeginArea(new Rect(28,236,226,672));H("江湖中人");query=GUILayout.TextField(query,field);GUILayout.BeginHorizontal();showFilters=GUILayout.Toggle(showFilters,"筛选");includeDead=GUILayout.Toggle(includeDead,"故人");GUILayout.EndHorizontal();
        if(showFilters){countryFilter=Cycle(countryFilter,new[]{"全部势力"}.Concat(W["countries"].A.Select(x=>x["name"].S)).ToArray());placeFilter=Cycle(placeFilter,new[]{"全部城镇"}.Concat(W["locations"].A.Select(x=>x["name"].S)).ToArray());factionFilter=Cycle(factionFilter,new[]{"全部门派"}.Concat(W["factions"].A.Select(x=>x["name"].S)).ToArray());circuitFilter=Cycle(circuitFilter,new[]{"全部州路"}.Concat(W["locations"].A.Select(x=>x["region"].S).Distinct()).ToArray());jobFilter=Cycle(jobFilter,new[]{"百业"}.Concat(W["professions"].D.Keys).ToArray());gradeFilter=Cycle(gradeFilter,new[]{"全部品级"}.Concat(W["identity_grades"].D.Keys).ToArray());rosterSort=Cycle(rosterSort,new[]{"乡籍","财富","武学","年岁","身份"});}
        var people=W["npcs"].A.Where(n=>(includeDead||n["alive"].B)&&(string.IsNullOrEmpty(query)||n["name"].S.Contains(query)||n["job"].S.Contains(query))&&(placeFilter=="全部城镇"||Place(n["place"].I)["name"].S==placeFilter)&&(countryFilter=="全部势力"||Country(Place(n["place"].I)["country"].I)["name"].S==countryFilter)&&(factionFilter=="全部门派"||W["factions"].A.Any(f=>f["id"].I==n["faction"].I&&n["faction"].Has&&f["name"].S==factionFilter))&&(jobFilter=="百业"||n["job"].S==jobFilter)&&(gradeFilter=="全部品级"||n["identity"]["grade_label"].S==gradeFilter)&&(circuitFilter=="全部州路"||Place(n["place"].I)["region"].S==circuitFilter));
        if(rosterSort!="乡籍")people=people.OrderByDescending(n=>rosterSort=="财富"?n["wealth"].N:rosterSort=="武学"?n["skill"].N:rosterSort=="年岁"?n["age_months"].N:-n["identity"]["grade"].N);
        var rows=people.ToList();T(rows.Count+"人  ·  点击查阅一生",small);
        rosterScroll=GUILayout.BeginScrollView(rosterScroll);foreach(var n in rows){if(GUILayout.Button(n["name"].S+"   "+(n["age_months"].I/12)+"岁\n"+n["identity"]["title"].S+" · "+Place(n["place"].I)["name"].S,n["id"].I==SelectedPerson?selected:skin.button,GUILayout.MinHeight(54)))SelectPerson(n["id"].I);}GUILayout.EndScrollView();GUILayout.EndArea();
    }
    void DrawPerson(){
        GUILayout.BeginArea(new Rect(1310,237,340,669));
        if(!Person.Has){H("择一人，观其一生");T("看他为何远行，为何结怨，\n又为何在多年之后改变心意。",small);GUILayout.EndArea();return;}
        GUILayout.BeginHorizontal();if(portraits!=null){Rect pr=GUILayoutUtility.GetRect(60,76,GUILayout.Width(60));bool female=Person["sex"].S=="女";int years=Person["age_months"].I/12;int idx=years<16?(female?5:4):years>=60?(female?3:2):Person["id"].I%3==0?(female?7:6):(female?1:0);GUI.DrawTextureWithTexCoords(pr,portraits,new Rect((idx%4)/4f,(1-idx/4)/2f,.25f,.5f));}GUILayout.BeginVertical();H(Person["name"].S);T(Person["sex"].S+" · "+Person["age_months"].I/12+"岁 · "+(Person["alive"].B?"在世":"已故"));T(Place(Person["place"].I)["name"].S,small);GUILayout.EndVertical();GUILayout.EndHorizontal();
        personTab=GUILayout.SelectionGrid(personTab,new[]{"此刻","心性","身份","物品","关系","门中","武学","年谱"},4,chip);
        personScroll=GUILayout.BeginScrollView(personScroll);
        switch(personTab){
            case 0:Metric("家资",Num(Person["wealth"])+"银");Bar("体力",Person["health"].N,100);Bar("武学",Person["skill"].N,100);Metric("声望",Num(Person["reputation"]));T(Person["goal"]["title"].S,sub);T(Person["last_action"]["label"].S);Details("本月四周行程",Person["monthly_actions"]);Details("生活近况",J.O("illness",Person["illness"],"wound",Person["wound"],"pregnancy",Person["pregnancy"],"plan",Person["plan"]));break;
            case 1:Details("稳定心性",Person["traits"],true);Details("当前情绪",Person["mood"],true);Details("世界信念",Person["beliefs"],true);Details("心中所欲",Person["desires"],true);Metric("压力",Num(Person["pressure"]));Metric("孤独",Num(Person["loneliness"]));Details("长远心愿",Person["goal"],true);Details("心性变化",Person["personality_changes"]);Details("记忆与理解",Person["memories"]);break;
            case 2:Details("身份与营生",Person["identity"],true);Details("各业本领",Person["career_view"],true);Details("可学营生",Person["career_options"]);Details("旧业履历",Person["career"]);break;
            case 3:T(Person["possessions"]["items"].A.Count+"件物品 · "+Num(Person["possessions"]["weight"])+"斤");foreach(var item in Person["possessions"]["items"].A){T(item["name"].S+(item["equipped"].B?" · 已装备":""),sub);T(item["description"].S,small);T("耐久 "+item["durability"].S+" · "+item["origin"].S,small);}if(Btn("从我的行囊赠物")){giftTarget=SelectedPerson;Go(2);}break;
            case 4:relationQuery=GUILayout.TextField(relationQuery);relationFilter=Cycle(relationFilter,new[]{"全部","朋友","义亲","仇怨","故人"});foreach(var r in Person["relations_view"].A.Where(r=>r["name"].S.Contains(relationQuery)&&(relationFilter=="全部"||(relationFilter=="朋友"&&r["affinity"].N>20)||(relationFilter=="义亲"&&r["sworn"].B)||(relationFilter=="仇怨"&&r["affinity"].N<0)||(relationFilter=="故人"&&!r["alive"].B)))){if(GUILayout.Button(r["name"].S+" · "+r["label"].S+"\n情分 "+Num(r["affinity"])+" / 信任 "+Num(r["trust"])))SelectPerson(r["other"].I);}Details("与过客交情",Person["player_relation"],true);break;
            case 5:Details("门中身份",Person["membership_view"],true);if(Person["faction"].Has&&Btn("查看所属门派"))OpenInstitution(false,Person["faction"].I);Details("离合履历",Person["membership_history"]);break;
            case 6:DrawArtList(Person["martial_view"]["arts"],false);break;
            case 7:DrawEventList(Person["history"]);if(Btn("更早年谱")&&Person["history"].A.Count>0)Client.Fetch("/api/events?npc="+SelectedPerson+"&before="+Person["history"].A.Last()["id"].S,j=>Person["history"]=new J(Person["history"].A.Concat(j.A).ToList()));break;
        }
        GUILayout.Space(10);T("以过客之身介入",sub);GUILayout.BeginHorizontal();if(Btn("拜访"))Intervene("visit");if(Btn("赠银10"))Intervene("gift");if(Btn("切磋",Paused&&Person["alive"].B))Intervene("challenge");GUILayout.EndHorizontal();
        talk=GUILayout.TextField(talk);if(Btn("交谈",talk.Length>0))Intervene("talk");if(Btn("请其反思",W["ai"]["config"]["enabled"].B))Client.Act("/api/ai/review",J.O("npc_id",SelectedPerson));
        GUILayout.EndScrollView();GUILayout.EndArea();
    }
    void Intervene(string action){Client.Act("/api/intervene",J.O("action",action,"npc_id",SelectedPerson,"text",talk),j=>{if(action=="challenge"){Martial=j;Page=3;battleView=true;}else{Client.Status=j["reply"].S;SelectPerson(SelectedPerson);}});}
    public void OpenEvent(int id){Page=6;Client.Fetch("/api/event/"+id,j=>EventDetail=j);}
    void DrawEventList(J list){foreach(var e in list.A){if(GUILayout.Button(e["date"].S+"  "+e["summary"].S,skin.button))OpenEvent(e["id"].I);}}
    public J Place(int id)=>W["places"].A.FirstOrDefault(p=>p["id"].I==id)??new J();
    public J Country(int id)=>W["countries"].A.FirstOrDefault(p=>p["id"].I==id)??new J();
    public static string Num(J n)=>n.N.ToString("0.##");
    void H(string s){GUILayout.Label(s,heading);}
    void T(string s,GUIStyle style=null){GUILayout.Label(s,style??text);}
    bool Btn(string s,bool enabled=true){bool previous=GUI.enabled;GUI.enabled=previous&&enabled&&!Client.Busy;bool b=GUILayout.Button(s);GUI.enabled=previous;return b;}
    void Metric(string label,string value){GUILayout.BeginHorizontal();T(label,small);GUILayout.FlexibleSpace();T(value,sub);GUILayout.EndHorizontal();}
    void Bar(string label,double value,double max){Metric(label,value.ToString("0.#")+" / "+max.ToString("0.#"));var r=GUILayoutUtility.GetRect(10,6,GUILayout.ExpandWidth(true));Fill(r,new Color(.78f,.80f,.71f));Fill(new Rect(r.x,r.y,r.width*Mathf.Clamp01((float)(value/Math.Max(1,max))),r.height),green);GUILayout.Space(5);}
    string Cycle(string current,string[] options){if(options.Length==0)return current;int index=Array.IndexOf(options,current);if(index<0)index=0;if(GUILayout.Button(options[index]+"  ›",chip))index=(index+1)%options.Length;return options[index];}
    void Fill(Rect r,Color c){var old=GUI.color;GUI.color=c;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=old;}
    void Line(Vector2 a,Vector2 b,Color c,float width=1){if(Event.current.type!=EventType.Repaint)return;float length=Vector2.Distance(a,b);int steps=Mathf.Max(1,Mathf.CeilToInt(length/1.2f));float thickness=Mathf.Max(1.3f,width);for(int i=0;i<=steps;i++){Vector2 p=Vector2.Lerp(a,b,i/(float)steps);Fill(new Rect(p.x-thickness/2,p.y-thickness/2,thickness,thickness),c);}}
    static Color Hex(string s,float alpha=1){if(!ColorUtility.TryParseHtmlString(s,out var c))c=new Color(.52f,.58f,.44f);c.a=alpha;return c;}
    void Details(string label,J j,bool open=false){if(!j.Has||j.A.Count==0&&j.D.Count==0&&j.S=="")return;string key=SelectedPerson+":"+label;bool expanded=folds.TryGetValue(key,out var v)?v:open;if(GUILayout.Button((expanded?"▾ ":"▸ ")+label,chip))expanded=!expanded;folds[key]=expanded;if(expanded)Tree(j,0);}
    void Tree(J j,int depth){if(depth>5)return;if(j.value is List<J>){foreach(var item in j.A){if(item["name"].Has)T(item["name"].S,sub);else if(item["summary"].Has)T(item["summary"].S);Tree(item,depth+1);GUILayout.Space(5);}return;}if(j.value is Dictionary<string,J>){foreach(var p in j.D){if(!p.Value.Has||Hidden.Contains(p.Key))continue;string label=Labels.TryGetValue(p.Key,out var translated)?translated:p.Key;if(p.Value.value is Dictionary<string,J>||p.Value.value is List<J>){if(p.Value.A.Count==0&&p.Value.D.Count==0)continue;T(label,sub);Tree(p.Value,depth+1);}else Metric(label,p.Value.value is bool?(p.Value.B?"是":"否"):p.Value.S);}return;}T(j.S);}
    static readonly HashSet<string> Hidden=new HashSet<string>{"rng","rng_state","revision","version","template","owner","layout_id","event_id","last_event","trigger_events","choice_source","origin_event","last_contact","last_review_attempt","cooldowns","source_digest","id","native_place","opening_fund","contact_since"};
    static readonly Dictionary<string,string> Labels=new Dictionary<string,string>{
        {"name","名姓"},{"title","名目"},{"kind","类别"},{"description","说明"},{"summary","经历"},{"date","年月"},{"month","世界月数"},{"week","周次"},{"label","名称"},{"value","数值"},{"amount","金额"},{"reason","缘由"},{"text","内容"},{"status","近况"},{"progress","进展"},{"since","始于月数"},{"target","所向"},{"affinity","情分"},{"trust","信任"},{"romance","倾慕"},{"sworn","结义"},{"alive","在世"},{"other","对方"},{"honesty","诚实"},{"greed","贪欲"},{"aggression","暴烈"},{"empathy","体恤"},{"ambition","进取"},{"curiosity","好奇"},{"loyalty","忠义"},{"prudence","审慎"},{"courage","勇气"},{"diligence","勤勉"},{"risk","冒险"},{"sociability","合群"},{"joy","喜悦"},{"anger","愤怒"},{"fear","忧惧"},{"hope","期盼"},{"stress","压力"},{"loneliness","孤独"},{"fairness","公平"},{"world_fair","世道公正"},{"people_good","人心向善"},{"self_efficacy","自身有为"},{"security","安稳"},{"wealth","银两"},{"power","权势"},{"belonging","归属"},{"freedom","自由"},{"meaning","意义"},{"affection","情感"},{"mastery","精进"},{"health","体力"},{"skill","武学"},{"reputation","声望"},{"grade","品阶"},{"grade_label","品阶"},{"roles","兼有身份"},{"profession","营生"},{"proficiency","熟练度"},{"professions","各业本领"},{"skills","本领"},{"current","当前"},{"level","境界"},{"level_name","境界"},{"income","收入"},{"expense","支出"},{"faction","所属组织"},{"contribution","贡献"},{"rank","门中品级"},{"fund","库银"},{"revenue","入库"},{"expenses","支出"},{"tasks_completed","完成差事"},{"policy","施政"},{"treasury","国库"},{"tax_rate","税率"},{"public_health","公医"},{"security_bonus","治安增益"},{"order","治安"},{"taxes","税收"},{"relief","救济"},{"medical","医疗"},{"officials","官员"},{"cases","公议记录"},{"action","行止"},{"source","来源"},{"interpretation","个人理解"},{"emotion","感受"},{"belief","信念"},{"salience","印象深度"},{"confidence","确信"},{"illness","疾病"},{"wound","伤势"},{"pregnancy","妊娠"},{"plan","打算"},{"goal","心愿"},{"steps","步骤"},{"intent","意图"},{"job","职业"},{"facts","共同事实"},{"perspectives","各人理解"},{"causes","前因"},{"cause_events","前因事件"},{"stamina","精力"},{"practice","修习"},{"realm","重数"},{"qi","内力"},{"forms","式"},{"outer_guard","外伤护体"},{"inner_guard","内伤护体"},{"requirement","所需武学"},{"importance","影响"},{"population","人口"},{"settlements","城镇数"},{"poor","贫困居民"},{"patients","病弱居民"},{"members","成员"},{"local_members","本地成员"},{"leader","主持"},{"ethos","旨趣"},{"art","传习"},{"focus","专长"},{"ranks","成员品级"},{"horizon_years","所期年数"},{"minimum_months","践行月数"},{"born","出生"},{"deceased","亡者"},{"duration","时长"},{"old","从前"},{"new","如今"},{"delta","变化"},{"career","营生"},{"history","履历"},{"records","记录"},{"current_job","当前营生"},{"aptitude","禀赋"},{"demand","本地需求"},{"practice_gain","修习所得"},{"income_bonus","营生增益"},{"wealth_spent","支出银两"},{"week_cost","占用周次"},{"cooldown","间隔"},{"theme","风貌"},{"label_x","图中横位"},{"label_y","图中纵位"}};
}
}
