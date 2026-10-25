using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    string heroName="沈行舟",heroAge="18",heroSex="男",mapLayer="疆域";
    bool creatingHero;
    J geography=new J(),campaignOptions=new J();
    readonly Dictionary<int,Texture2D> mapMasks=new Dictionary<int,Texture2D>();
    Texture2D landMask,diamond,roof,atlasBase,atlasInk,cityMark,capitalMark,ringMark,pennantMark,compassMark;
    float mapZoom=1;Vector2 mapPan;bool dragging;Vector2 dragMouse;
    int chosenRow=-1,chosenCol=-1,originRow=-1,originCol=-1;
    string cityKind="全部设施";
    public void LoadCity(int place){SelectedPlace=place;Client.Fetch("/api/city?place="+place,j=>{City=j;chosenRow=-1;chosenCol=-1;});}
    void DrawHero(){
        GUILayout.BeginVertical(card);
        if(Hero["created"].B){
            GUILayout.BeginHorizontal();T(Hero["name"].S+"  /  "+Hero["place_name"].S+" · "+Hero["position_name"].S,sub);GUILayout.FlexibleSpace();T("行动力 "+Hero["points"].S+" / 30   ·   "+Num(W["player"]["wealth"])+"银",sub);GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();T("第"+Hero["turn"].S+"回合 · "+Hero["age_months"].I/12+"岁 · 一月一回合",small);GUILayout.FlexibleSpace();if(Btn("定位主角")){SelectedPlace=Hero["place"].I;mapZoom=1.5f;mapPan=new Vector2(640-(float)Place(SelectedPlace)["x"].N,380-(float)Place(SelectedPlace)["y"].N);}
            if(Btn("进入所在地")){SelectedPlace=Hero["place"].I;Go(1);}GUILayout.EndHorizontal();
        } else {
            H("立一名主角，走自己的路");T("创建后每月拥有30行动力。可在城中行走，沿相邻驿道或航路远行。",small);
            if(Btn("创建主角"))creatingHero=!creatingHero;
            if(creatingHero){GUILayout.BeginHorizontal();heroName=GUILayout.TextField(heroName);heroSex=Cycle(heroSex,new[]{"男","女"});heroAge=GUILayout.TextField(heroAge,GUILayout.Width(60));GUILayout.EndHorizontal();T("出身："+Place(SelectedPlace)["name"].S+"（在舆图上选择城镇）",small);if(Btn("此身入江湖",Paused)&&int.TryParse(heroAge,out int age))HeroAction("create","name",heroName,"sex",heroSex,"age",age,"place",SelectedPlace);}
        }
        GUILayout.EndVertical();
    }
    void InitMap(){
        if(geography.Has)return;geography=J.Parse(Resources.Load<TextAsset>("geography").text);landMask=Resources.Load<Texture2D>("Map/land");
        foreach(var c in geography["cells"].A)mapMasks[c["place"].I]=Resources.Load<Texture2D>("Map/cell-"+c["place"].S);
        atlasBase=Resources.Load<Texture2D>("Map/atlas_base");atlasInk=Resources.Load<Texture2D>("Map/atlas_ink");
        cityMark=Resources.Load<Texture2D>("Map/city");capitalMark=Resources.Load<Texture2D>("Map/capital");ringMark=Resources.Load<Texture2D>("Map/ring");pennantMark=Resources.Load<Texture2D>("Map/pennant");compassMark=Resources.Load<Texture2D>("Map/compass");
    }
    void DrawAtlas(){
        InitMap();mainScroll=GUILayout.BeginScrollView(mainScroll);DrawHero();
        GUILayout.BeginHorizontal();H("山海舆图");GUILayout.FlexibleSpace();foreach(string layer in new[]{"疆域","州路","门派","行路"})if(GUILayout.Button(layer,mapLayer==layer?selected:chip))mapLayer=layer;if(Btn("全图")){mapZoom=1;mapPan=Vector2.zero;}GUILayout.EndHorizontal();
        Rect r=GUILayoutUtility.GetRect(960,390,GUILayout.ExpandWidth(true));DrawMap(r);
        T("滚轮缩放 · 拖动浏览 · 点击城镇选址   /   约1200年开局 · "+W["locations"].A.Count+"处城镇区域",small);
        var p=Place(SelectedPlace);GUILayout.BeginVertical(card);GUILayout.BeginHorizontal();H(p["name"].S+" · "+Country(p["country"].I)["name"].S);GUILayout.FlexibleSpace();if(Btn("进入城中图"))Go(1);if(Btn("查看所属官府"))OpenInstitution(true,p["country"].I);GUILayout.EndHorizontal();
        T(p["region"].S+" · 居民"+p["population"].S+"人 · 守备"+Num(p["guard"])+" · 安定"+Num(p["stability"]),small);
        DrawTravel();
        if(Btn("查看相邻势力攻城选项",Paused))Client.Fetch("/api/campaign?place="+SelectedPlace,j=>campaignOptions=j);
        foreach(var o in campaignOptions["options"].A){T(Country(o["attacker"].I)["name"].S+" · 军费"+Num(o["cost"])+"银 · "+o["reason"].S,small);if(Btn("推演攻城 · "+Country(o["attacker"].I)["name"].S,Paused&&o["ready"].B))Client.Act("/api/campaign",J.O("attacker",o["attacker"],"place",SelectedPlace),j=>campaignOptions=new J());}
        GUILayout.EndVertical();
        GUILayout.BeginHorizontal();foreach(var c in W["countries"].A){var old=GUI.backgroundColor;GUI.backgroundColor=Hex(c["color"].S);if(GUILayout.Button(c["name"].S,chip))OpenInstitution(true,c["id"].I);GUI.backgroundColor=old;}GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();Metric("在世",W["population"].S);Metric("事件",W["events"].S);Metric("结义",W["sworn_bonds"].S);Metric("婚姻",W["marriages"].S);GUILayout.EndHorizontal();
        Details("世代与人间秩序",W["demography"]);Details("最近战报",W["campaigns"]);DrawEventList(W["world_events"]);GUILayout.EndScrollView();
    }
    void DrawTravel(){
        foreach(var connection in Hero["connections"].A){if(connection["place"].I!=SelectedPlace&&Page==0)continue;
            T("前往"+connection["name"].S+" · "+connection["points"].S+"行动力 / "+Num(connection["silver"])+"银"+(connection["sea"].B?" · 航路":" · 驿道"),small);
            if(Btn(connection["enabled"].B?"沿路远行 →":connection["reason"].S,Paused&&connection["enabled"].B))HeroAction("travel","place",connection["place"]);
        }
    }
    Vector2 MapPoint(J p,Rect r)=>new Vector2((float)p["x"].N,(float)p["y"].N);
    // Silk-scroll atlas: painted base (sea wash + paper land), territory washes tinted by current owner,
    // then the ink layer (coast, rivers, mountains), dashed roads, town seals and halo labels.
    void DrawMap(Rect r){
        GUI.BeginGroup(r);Rect local=new Rect(0,0,r.width,r.height);Paper(local,"paper");
        float k=Mathf.Min(r.width/1280f,r.height/760f)*mapZoom;Vector2 center=new Vector2(r.width/2,r.height/2);Vector2 offset=center-new Vector2(640,380)*k+mapPan*k;
        Func<float,float,Vector2> pos=(x,y)=>new Vector2(x*k+offset.x,y*k+offset.y);
        var mapRect=new Rect(offset.x,offset.y,1280*k,760*k);var prev=GUI.color;
        if(atlasBase!=null)GUI.DrawTexture(mapRect,atlasBase,ScaleMode.StretchToFill);
        else{GUI.color=new Color(.86f,.87f,.75f);GUI.DrawTexture(mapRect,landMask);GUI.color=prev;}
        if(mapLayer=="疆域"||mapLayer=="州路")foreach(var c in geography["cells"].A){if(!mapMasks.TryGetValue(c["place"].I,out var tex)||tex==null)continue;GUI.color=Hex(Country(Place(c["place"].I)["country"].I)["color"].S,mapLayer=="州路"?.34f:.52f);GUI.DrawTexture(mapRect,tex);GUI.color=prev;}
        GUI.color=prev;
        if(atlasInk!=null)GUI.DrawTexture(mapRect,atlasInk,ScaleMode.StretchToFill);
        else foreach(var river in geography["rivers"].A)DrawVectorPath(river["path"].S,pos,new Color(.35f,.57f,.61f,.8f),1.4f);
        foreach(var route in W["routes"].A){
            var a=Place(route["a"].I);var b=Place(route["b"].I);Vector2 pa=pos((float)a["x"].N,(float)a["y"].N),pb=pos((float)b["x"].N,(float)b["y"].N);
            bool roads=mapLayer=="行路";
            if(route["sea"].B)DashedLine(pa,pb,new Color(.20f,.38f,.44f,roads?.85f:.45f),roads?1.8f:1.2f,9,6);
            else DashedLine(pa,pb,new Color(.45f,.22f,.14f,roads?.85f:.35f),roads?2f:1.2f,5,4);
        }
        if(mapLayer=="州路")foreach(var region in geography["regions"].A){Vector2 q=pos((float)region["x"].N,(float)region["y"].N);HaloLabel(new Rect(q.x-30,q.y-21,110,18),region["name"].S,caption,mapHalo);}
        var placedLabels=new List<Rect>();
        foreach(var p in W["locations"].A){
            Vector2 q=pos((float)p["x"].N,(float)p["y"].N);bool chosen=p["id"].I==SelectedPlace;bool capital=p["kind"].S=="capital";
            float ms=capital?16:12;Rect markRect=new Rect(q.x-ms/2,q.y-ms/2,ms,ms);
            Texture2D mark=capital?capitalMark:cityMark;
            if(mark!=null)GUI.DrawTexture(markRect,mark);else Fill(new Rect(q.x-3,q.y-3,6,6),Hex(Country(p["country"].I)["color"].S));
            Fill(new Rect(q.x-ms/2,q.y+ms/2+1,ms,2),Hex(Country(p["country"].I)["color"].S,.9f));
            if(chosen&&ringMark!=null)GUI.DrawTexture(new Rect(q.x-15,q.y-15,30,30),ringMark);
            GUIStyle style=chosen?mapLabelOn:mapLabel;GUIStyle halo=chosen?mapHaloBold:mapHalo;
            float labelWidth=Mathf.Clamp(style.CalcSize(new GUIContent(p["name"].S)).x+6,30,120);
            Rect labelRect=new Rect(q.x+ms/2+3,q.y-10,labelWidth,20);
            for(int attempt=0;attempt<18&&placedLabels.Any(a=>a.Overlaps(labelRect));attempt++){int step=(attempt/2+1)*20;labelRect.y=q.y-10+(attempt%2==0?step:-step);labelRect.x=q.x+ms/2+3+(attempt>=10?-labelWidth-ms-6:0);}
            placedLabels.Add(labelRect);
            if(Vector2.Distance(labelRect.center,q)>60)Line(q,labelRect.center,new Color(.17f,.14f,.11f,.35f));
            HaloLabel(labelRect,p["name"].S,style,halo);
            bool hit=GUI.Button(labelRect,GUIContent.none,GUIStyle.none);hit|=GUI.Button(markRect,GUIContent.none,GUIStyle.none);if(hit){SelectedPlace=p["id"].I;campaignOptions=new J();}
        }
        if(mapLayer=="门派")foreach(var f in W["factions"].A){var p=Place(f["place"].I);Vector2 q=pos((float)p["x"].N,(float)p["y"].N);Rect fr=new Rect(q.x-12,q.y+14+(f["id"].I%3)*17,110,18);Fill(new Rect(fr.x-6,fr.y+6,4,4),vermilion);HaloLabel(fr,f["name"].S,caption,mapHalo);if(GUI.Button(fr,GUIContent.none,GUIStyle.none))OpenInstitution(false,f["id"].I);}
        if(Hero["created"].B){
            var p=Place(Hero["place"].I);Vector2 q=pos((float)p["x"].N,(float)p["y"].N);
            if(pennantMark!=null)GUI.DrawTexture(new Rect(q.x-9,q.y-34,28,28),pennantMark);else Fill(new Rect(q.x-3,q.y-24,6,13),vermilion);
            HaloLabel(new Rect(q.x+12,q.y-40,150,20),Hero["name"].S,mapLabelOn,mapHaloBold);
        }
        // fixed overlays: double rule, compass rose and title cartouche
        Frame(local,"frame_book",12);
        if(compassMark!=null)GUI.DrawTexture(new Rect(local.xMax-84,14,68,68),compassMark);
        Rect cart=new Rect(16,14,40,118);Paper(cart,"paper_light");Outline(cart,new Color(.17f,.14f,.11f,.8f));Outline(new Rect(cart.x+3,cart.y+3,cart.width-6,cart.height-6),new Color(.17f,.14f,.11f,.4f));
        GUI.Label(new Rect(cart.x,cart.y+8,cart.width,cart.height-16),"山\n海\n舆\n图",mapTitle);
        Event ev=Event.current;if(local.Contains(ev.mousePosition)){
            if(ev.type==EventType.ScrollWheel){float old=mapZoom;mapZoom=Mathf.Clamp(mapZoom*(ev.delta.y>0?.85f:1.18f),.8f,5);Vector2 mouse=ev.mousePosition-center;mapPan+=mouse*(1/(k*(mapZoom/old))-1/k);ev.Use();}
            if(ev.type==EventType.MouseDown&&ev.button==0){dragging=true;dragMouse=ev.mousePosition;}
            if(ev.type==EventType.MouseDrag&&dragging){mapPan+=(ev.mousePosition-dragMouse)/k;dragMouse=ev.mousePosition;ev.Use();}
        }if(ev.rawType==EventType.MouseUp)dragging=false;
        GUI.EndGroup();
    }
    void DashedLine(Vector2 a,Vector2 b,Color c,float width,float dash,float gap){if(Event.current.type!=EventType.Repaint)return;float length=Vector2.Distance(a,b);if(length<1)return;Vector2 dir=(b-a)/length;for(float s=0;s<length;s+=dash+gap)Line(a+dir*s,a+dir*Mathf.Min(length,s+dash),c,width);}
    void DrawVectorPath(string path,Func<float,float,Vector2> pos,Color color,float width){var tokens=System.Text.RegularExpressions.Regex.Matches(path,@"[MLZmlz]|-?\d+(?:\.\d+)?");Vector2 last=Vector2.zero,start=Vector2.zero;bool move=true;for(int i=0;i<tokens.Count;i++){string t=tokens[i].Value;if(t=="M"||t=="m"){move=true;continue;}if(t=="L"||t=="l")continue;if(t=="Z"||t=="z"){Line(last,start,color,width);continue;}if(i+1>=tokens.Count)break;float x=float.Parse(t,System.Globalization.CultureInfo.InvariantCulture),y=float.Parse(tokens[++i].Value,System.Globalization.CultureInfo.InvariantCulture);var next=pos(x,y);if(move){start=next;move=false;}else Line(last,next,color,width);last=next;}}
    void DrawCity(){
        if(!City.Has){T("正在展开城中街巷…");return;}
        GUILayout.BeginHorizontal();H(City["name"].S+" · 城中图");GUILayout.FlexibleSpace();if(Btn("返回主角所在地"))LoadCity(Hero["place"].I);if(Btn("下一城镇")){var a=W["locations"].A;int i=a.FindIndex(p=>p["id"].I==SelectedPlace);LoadCity(a[(i+1)%a.Count]["id"].I);}GUILayout.EndHorizontal();
        T(City["country_name"].S+" · "+City["theme_label"].S+" · "+City["population"].S+"人在世",small);
        bool local=Hero["created"].B&&City["place"].I==Hero["place"].I;
        T(local?Hero["name"].S+" · "+Hero["position_name"].S+" · 余"+Hero["points"].S+"行动力":"远方地图预览 · 浏览不消耗回合",sub);
        cityKind=Cycle(cityKind,new[]{"全部设施"}.Concat(City["nodes"].A.Select(n=>n["label"].S).Distinct()).ToArray());
        Rect board=GUILayoutUtility.GetRect(960,342,GUILayout.ExpandWidth(true));DrawCityBoard(board);
        mainScroll=GUILayout.BeginScrollView(mainScroll);
        var node=City["nodes"].A.FirstOrDefault(n=>n["row"].I==chosenRow&&n["col"].I==chosenCol)??new J();
        if(chosenRow>=0){H(node["name"].Has?node["name"].S:"所选地格（"+(chosenRow+1)+"，"+(chosenCol+1)+"）");T(node["description"].S,small);
            var gate=City["nodes"].A.First(n=>n["id"].S==City["entrance"].S);int sr=local?Hero["row"].I:originRow>=0?originRow:gate["row"].I,sc=local?Hero["col"].I:originCol>=0?originCol:gate["col"].I;
            var path=CityPath(sr,sc,chosenRow,chosenCol,out int cost);T(path.Count>0?"步行路线 "+Math.Max(0,path.Count-1)+"格 · 消耗"+cost+"行动力":"没有可通行的路线",small);
            GUILayout.BeginHorizontal();if(Btn("行走至此",local&&Paused&&path.Count>1&&cost<=Hero["points"].I))HeroAction("walk","place",City["place"],"row",chosenRow,"col",chosenCol);if(Btn("以此为游览起点",!local)){originRow=chosenRow;originCol=chosenCol;}
            string kind=node["kind"].S;if((kind=="market"||kind=="workshop")&&Btn("过客货栈")){inventoryMode="货栈";Go(2);}if((kind=="training"||kind=="temple"||kind=="faction")&&Btn("功法与闭关"))Go(3);if(kind=="government"&&Btn("查看官府"))OpenInstitution(true,City["country"].I);if((kind=="gate"||kind=="dock")&&Btn("城外行路")){mapLayer="行路";Go(0);}GUILayout.EndHorizontal();
            foreach(var resident in node["residents"].A)if(GUILayout.Button(resident["name"].S+" · "+resident["job"].S+" · "+resident["source"].S,chip))SelectPerson(resident["id"].I);
        }
        T("点击设施或可通行地格，再选择行走；方向键可逐格移动。道路/桥1点，平地/田圃2点，林地3点。",small);
        foreach(var n in City["nodes"].A.Where(n=>cityKind=="全部设施"||n["label"].S==cityKind))if(GUILayout.Button(n["glyph"].S+"  "+n["name"].S+"  /  "+n["residents"].A.Count+"人",chip)){chosenRow=n["row"].I;chosenCol=n["col"].I;}
        if(local)DrawTravel();GUILayout.EndScrollView();
    }
    void InitTiles(){if(diamond!=null)return;diamond=new Texture2D(96,48);roof=new Texture2D(96,48);for(int y=0;y<48;y++)for(int x=0;x<96;x++){bool inside=Mathf.Abs(x-47.5f)/48+Mathf.Abs(y-23.5f)/24<=1;diamond.SetPixel(x,y,inside?Color.white:Color.clear);roof.SetPixel(x,y,inside?Color.white:Color.clear);}diamond.Apply();roof.Apply();}
    void Tile(Vector2 q,float tw,float th,Color c){var old=GUI.color;GUI.color=c;GUI.DrawTexture(new Rect(q.x-tw,q.y-th,tw*2,th*2),diamond);GUI.color=old;}
    void DrawCityBoard(Rect r){
        InitTiles();GUI.BeginGroup(r);Paper(new Rect(0,0,r.width,r.height),"paper_light");int size=City["size"].I;float tw=Mathf.Min(31,430f/size),th=tw*.47f;Vector2 center=new Vector2(r.width*.48f,37);
        Func<int,int,Vector2> xy=(row,col)=>center+new Vector2((col-row)*tw,(col+row)*th);
        var gate=City["nodes"].A.First(n=>n["id"].S==City["entrance"].S);bool local=Hero["created"].B&&City["place"].I==Hero["place"].I;int sr=local?Hero["row"].I:originRow>=0?originRow:gate["row"].I,sc=local?Hero["col"].I:originCol>=0?originCol:gate["col"].I;
        var route=chosenRow>=0?CityPath(sr,sc,chosenRow,chosenCol,out _):new List<Vector2Int>();
        for(int sum=0;sum<size*2;sum++)for(int row=0;row<size;row++){int col=sum-row;if(col<0||col>=size)continue;char tile=City["rows"][row].S[col];Vector2 p=xy(row,col);Color c=tile=='~'?new Color(.66f,.74f,.70f):tile=='^'?new Color(.58f,.66f,.57f):tile=='#'?new Color(.60f,.54f,.45f):tile=='t'?new Color(.62f,.69f,.55f):tile==','?new Color(.85f,.80f,.62f):tile=='+'||tile=='='?new Color(.80f,.70f,.54f):new Color(.90f,.86f,.74f);Tile(p,tw-1,th-1,c);if(route.Contains(new Vector2Int(row,col)))Tile(p,tw*.65f,th*.65f,new Color(.56f,.19f,.13f,.45f));
            var n=City["nodes"].A.FirstOrDefault(n=>n["row"].I==row&&n["col"].I==col);if(n!=null){Color house=n["kind"].S=="government"?new Color(.56f,.19f,.13f):n["kind"].S=="temple"?new Color(.66f,.50f,.26f):new Color(.30f,.37f,.36f);Fill(new Rect(p.x-17,p.y-21,34,20),new Color(.93f,.88f,.76f));Outline(new Rect(p.x-17,p.y-21,34,20),new Color(.17f,.14f,.11f,.5f));Tile(p+new Vector2(0,-23),24,12,house);Fill(new Rect(p.x-3,p.y-10,7,9),new Color(.35f,.34f,.27f));GUI.Label(new Rect(p.x-31,p.y+8,75,22),n["label"].S,small);if(n["residents"].A.Count>0)GUI.Label(new Rect(p.x+13,p.y-30,30,19),n["residents"].A.Count.ToString(),small);}
            else if(tile=='t'){Line(p,p+new Vector2(0,-24),green,3);Tile(p+new Vector2(0,-21),11,14,new Color(.33f,.46f,.36f));}else if(tile=='^'){Tile(p+new Vector2(0,-12),tw*.8f,25,new Color(.40f,.55f,.52f));}
        }
        if(local){var p=xy(Hero["row"].I,Hero["col"].I);Fill(new Rect(p.x-5,p.y-24,10,20),vermilion);Fill(new Rect(p.x-4,p.y-32,8,8),new Color(.82f,.65f,.42f));GUI.Label(new Rect(p.x-32,p.y-55,120,24),Hero["name"].S,sub);}
        if(chosenRow>=0){Vector2 p=xy(chosenRow,chosenCol);Tile(p,tw,th,new Color(.56f,.19f,.13f,.28f));}
        Event e=Event.current;if(e.type==EventType.MouseDown&&e.button==0&&new Rect(0,0,r.width,r.height).Contains(e.mousePosition)){var p=e.mousePosition-center;int col=Mathf.RoundToInt((p.x/tw+p.y/th)*.5f),row=Mathf.RoundToInt((p.y/th-p.x/tw)*.5f);if(row>=0&&row<size&&col>=0&&col<size){chosenRow=row;chosenCol=col;GUI.FocusControl(null);e.Use();}}
        GUI.Label(new Rect(10,r.height-24,r.width-20,24),City["road_name"].S+"    /    "+City["note"].S,small);GUI.EndGroup();
    }
    public List<Vector2Int> CityPath(int sr,int sc,int tr,int tc,out int cost){
        int size=City["size"].I;cost=0;var result=new List<Vector2Int>();if(sr<0||sc<0||tr<0||tc<0||sr>=size||sc>=size||tr>=size||tc>=size)return result;
        var distance=new Dictionary<Vector2Int,int>();var previous=new Dictionary<Vector2Int,Vector2Int>();var open=new List<Vector2Int>();var start=new Vector2Int(sr,sc);var target=new Vector2Int(tr,tc);distance[start]=0;open.Add(start);
        while(open.Count>0){var p=open.OrderBy(x=>distance[x]).First();open.Remove(p);if(p==target)break;foreach(var delta in new[]{Vector2Int.up,Vector2Int.down,Vector2Int.left,Vector2Int.right}){var q=p+delta;if(q.x<0||q.y<0||q.x>=size||q.y>=size)continue;char t=City["rows"][q.x].S[q.y];if("~^#".Contains(t))continue;int d=distance[p]+("+=".Contains(t)?1:t=='t'?3:2);if(!distance.ContainsKey(q)||d<distance[q]){distance[q]=d;previous[q]=p;if(!open.Contains(q))open.Add(q);}}}
        if(!distance.ContainsKey(target))return result;cost=distance[target];var at=target;result.Add(at);while(at!=start){at=previous[at];result.Add(at);}result.Reverse();return result;
    }
}
}
