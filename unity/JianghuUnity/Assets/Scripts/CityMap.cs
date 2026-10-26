using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jianghu {
// The city renderer uses the existing settlement data and movement rules. Its
// camera, reachability overlay and preview routes never mutate the simulation.
public partial class JianghuApp {
    readonly Dictionary<string,Texture2D> cityArt=new Dictionary<string,Texture2D>();
    readonly Dictionary<Vector2Int,int> cityCosts=new Dictionary<Vector2Int,int>();
    readonly Dictionary<Vector2Int,Vector2Int> cityPrevious=new Dictionary<Vector2Int,Vector2Int>();
    readonly Dictionary<Vector2Int,J> cityNodes=new Dictionary<Vector2Int,J>();
    string cityPathKey="";
    float cityZoom=1;
    Vector2 cityPan,cityPress,cityLastMouse;
    bool cityPointerDown,cityPanned,cityLabels=true,cityReach,cityGrid=true,cityLoading;
    int cityRequest;
    Vector2Int cityHover=new Vector2Int(-1,-1);
    GUIStyle cityTiny,cityTip,cityTitle;
    static readonly Vector2Int[] CitySteps={new Vector2Int(-1,0),new Vector2Int(0,1),new Vector2Int(1,0),new Vector2Int(0,-1)};
    Rect CityViewport=>new Rect(rosterOpen?284:42,146,rosterOpen?1118:1360,604);
    bool CityLocal=>Hero["created"].B&&City["place"].I==Hero["place"].I;

    public void LoadCity(int place){
        int request=++cityRequest;SelectedPlace=place;cityLoading=true;
        Client.Fetch("/api/city?place="+place,j=>{
            if(request!=cityRequest)return;
            bool changed=!City.Has||City["place"].I!=place;
            City=j;cityLoading=false;cityPathKey="";cityNodes.Clear();
            foreach(var node in City["nodes"].A)cityNodes[new Vector2Int(node["row"].I,node["col"].I)]=node;
            if(changed){chosenRow=chosenCol=originRow=originCol=-1;cityZoom=1;cityPan=Vector2.zero;cityKind="全部设施";}
            cityPointerDown=false;cityHover=new Vector2Int(-1,-1);
        });
    }
    Texture2D CityArt(string key){if(!cityArt.TryGetValue(key,out var t)){t=Resources.Load<Texture2D>("City/"+key);cityArt[key]=t;}return t;}
    void InitCityStyles(){
        if(cityTiny!=null)return;
        cityTiny=new GUIStyle(centerSmall){fontSize=11};TextColor(cityTiny,new Color(.85f,.86f,.72f));
        cityTip=new GUIStyle(small){fontSize=12,wordWrap=false};TextColor(cityTip,paperText);
        cityTitle=new GUIStyle(centerTitle){fontSize=26};
    }
    static string CityTerrain(char t)=>t=='~'?"水域":t=='^'?"山岭":t=='#'?"城垣":t=='t'?"林地":t==','?"田圃":t=='+'?"街道":t=='='?"桥梁":"平地";
    static bool CityBlocked(char t)=>t=='~'||t=='^'||t=='#';
    static int CityStepCost(char t)=>t=='+'||t=='='?1:t=='t'?3:2;
    char CityTile(int row,int col)=>City["rows"][row].S[col];
    bool CityInside(Vector2Int p)=>p.x>=0&&p.y>=0&&p.x<City["size"].I&&p.y<City["size"].I;
    Vector2Int CityOrigin(){
        if(CityLocal)return new Vector2Int(Hero["row"].I,Hero["col"].I);
        if(originRow>=0)return new Vector2Int(originRow,originCol);
        var gate=City["nodes"].A.First(n=>n["id"].S==City["entrance"].S);
        return new Vector2Int(gate["row"].I,gate["col"].I);
    }
    void EnsureCityRoutes(){
        var start=CityOrigin();string key=City["place"].S+"/"+string.Join("/",City["rows"].A.Select(x=>x.S))+"/"+start;
        if(key==cityPathKey)return;
        cityPathKey=key;cityCosts.Clear();cityPrevious.Clear();
        cityNodes.Clear();foreach(var node in City["nodes"].A)cityNodes[new Vector2Int(node["row"].I,node["col"].I)]=node;
        var open=new List<Vector2Int>{start};cityCosts[start]=0;
        while(open.Count>0){
            var p=open.OrderBy(q=>cityCosts[q]).First();open.Remove(p);
            foreach(var step in CitySteps){var q=p+step;if(!CityInside(q)||CityBlocked(CityTile(q.x,q.y)))continue;
                int cost=cityCosts[p]+CityStepCost(CityTile(q.x,q.y));
                if(cityCosts.TryGetValue(q,out int old)&&old<=cost)continue;
                cityCosts[q]=cost;cityPrevious[q]=p;if(!open.Contains(q))open.Add(q);
            }
        }
    }
    List<Vector2Int> CityPreview(Vector2Int target){
        var path=new List<Vector2Int>();if(!cityCosts.ContainsKey(target))return path;
        var p=target;path.Add(p);while(cityPrevious.TryGetValue(p,out var previous)){p=previous;path.Add(p);}path.Reverse();return path;
    }
    void CityGeometry(Rect viewport,out float tw,out float th,out Vector2 center){
        int size=City["size"].I;tw=Mathf.Min((viewport.width-30)/(2*size),(viewport.height-75)/size)*cityZoom;th=tw*.5f;
        center=new Vector2(viewport.width*.5f,viewport.height*.5f-(size-1)*th)+cityPan;
    }
    static Vector2 CityXY(Vector2Int p,float tw,float th,Vector2 center)=>center+new Vector2((p.y-p.x)*tw,(p.x+p.y)*th);
    static Vector2Int CityPick(Vector2 mouse,float tw,float th,Vector2 center){var q=mouse-center;return new Vector2Int(Mathf.RoundToInt((q.y/th-q.x/tw)*.5f),Mathf.RoundToInt((q.y/th+q.x/tw)*.5f));}
    void CityClampPan(){Rect r=CityViewport;cityPan.x=Mathf.Clamp(cityPan.x,-r.width*cityZoom*.48f,r.width*cityZoom*.48f);cityPan.y=Mathf.Clamp(cityPan.y,-r.height*cityZoom*.48f,r.height*cityZoom*.48f);}
    void CityZoom(float zoom,Vector2 anchor){
        float next=Mathf.Clamp(zoom,.75f,2.8f),ratio=next/cityZoom;var middle=new Vector2(CityViewport.width/2,CityViewport.height/2);
        cityPan=(anchor-middle)*(1-ratio)+cityPan*ratio;cityZoom=next;CityClampPan();
    }
    void CityFocus(Vector2Int point){CityGeometry(CityViewport,out float tw,out float th,out var center);cityPan+=new Vector2(CityViewport.width/2,CityViewport.height/2)-CityXY(point,tw,th,center);CityClampPan();}
    void SelectCityNode(J node,bool focus=false){chosenRow=node["row"].I;chosenCol=node["col"].I;GUI.FocusControl(null);if(focus)CityFocus(new Vector2Int(chosenRow,chosenCol));}

    void DrawCityScreen(Rect screen){
        InitCityStyles();Tile(screen,"bg");var backdrop=CityArt("backdrop");if(backdrop!=null)GUI.DrawTexture(screen,backdrop,ScaleMode.StretchToFill);
        if(cityLoading&&!string.IsNullOrEmpty(Client.Error))cityLoading=false;
        if(!City.Has){GUI.Label(new Rect(480,400,720,50),"正在展开城中街巷…",centerTitle);return;}
        EnsureCityRoutes();
        GUI.Label(new Rect(400,64,880,36),City["name"].S+" · 城中行旅",cityTitle);
        GUI.Label(new Rect(570,96,540,17),City["theme_label"].S+"   /   "+City["road_name"].S,centerSmall);
        DrawCityBoard(CityViewport);
        DrawCityToolbar();DrawCityLegend();DrawCityOverview();
        if(cityLoading)GUI.Label(new Rect(620,370,430,44),"正在展开新图…",centerTitle);
    }
    void DrawCityToolbar(){
        float left=CityViewport.x;Rect r=new Rect(left,112,CityViewport.width,30);
        Fill(r,new Color(.07f,.12f,.10f,.76f));
        GUI.enabled=!cityLoading;
        if(GUI.Button(new Rect(left+5,114,65,25),"－ 缩小",chip))CityZoom(cityZoom/1.2f,new Vector2(CityViewport.width/2,CityViewport.height/2));
        GUI.Label(new Rect(left+72,117,56,22),Mathf.RoundToInt(cityZoom*100)+"%",centerSmall);
        if(GUI.Button(new Rect(left+129,114,65,25),"＋ 放大",chip))CityZoom(cityZoom*1.2f,new Vector2(CityViewport.width/2,CityViewport.height/2));
        if(GUI.Button(new Rect(left+198,114,64,25),"全图",chip)){cityZoom=1;cityPan=Vector2.zero;}
        GUI.enabled=!cityLoading&&CityLocal;
        if(GUI.Button(new Rect(left+267,114,80,25),"定位主角",chip))CityFocus(new Vector2Int(Hero["row"].I,Hero["col"].I));
        GUI.enabled=!cityLoading;
        cityLabels=GUI.Toggle(new Rect(left+366,118,65,23),cityLabels,"名牌");
        cityGrid=GUI.Toggle(new Rect(left+439,118,65,23),cityGrid,"地格");
        if(CityLocal)cityReach=GUI.Toggle(new Rect(left+512,118,94,23),cityReach,"可达范围");
        GUI.Label(new Rect(r.xMax-260,118,250,20),"滚轮缩放 · 拖动浏览 · 单击选地",cityTiny);
        GUI.enabled=true;
    }
    void CityTexture(string name,Vector2 p,float tw,Color tint){var t=CityArt(name);if(t==null)return;var old=GUI.color;GUI.color=tint;float s=tw/128;GUI.DrawTexture(new Rect(p.x-128*s,p.y-171*s,256*s,256*s),t);GUI.color=old;}
    void CityDiamond(Vector2 p,float tw,float th,Color c,float width=1.2f){Line(p+new Vector2(0,-th),p+new Vector2(tw,0),c,width);Line(p+new Vector2(tw,0),p+new Vector2(0,th),c,width);Line(p+new Vector2(0,th),p+new Vector2(-tw,0),c,width);Line(p+new Vector2(-tw,0),p+new Vector2(0,-th),c,width);}
    string CityGround(char t)=>t=='~'||t=='='?"water":t=='^'?"mountain":t=='#'?"wall":t=='t'?"forest":t==','?"field":t=='+'?"street":"grass";
    int CityRoadMask(Vector2Int p){int mask=0;for(int i=0;i<4;i++){var q=p+CitySteps[i];if(CityInside(q)&&"+=".Contains(CityTile(q.x,q.y)))mask|=1<<i;}return mask;}
    void DrawCityBoard(Rect r){
        CityGeometry(r,out float tw,out float th,out var center);int size=City["size"].I;
        GUI.BeginGroup(r);var e=Event.current;var localBounds=new Rect(0,0,r.width,r.height);
        bool over=localBounds.Contains(e.mousePosition)&&!new Rect(10,r.height-116,170,104).Contains(e.mousePosition)&&!OverUI(e.mousePosition+r.position)&&!cityLoading;
        cityHover=over?CityPick(e.mousePosition,tw,th,center):new Vector2Int(-1,-1);if(!CityInside(cityHover))cityHover=new Vector2Int(-1,-1);
        var target=chosenRow>=0?new Vector2Int(chosenRow,chosenCol):cityHover;
        var route=CityPreview(target);bool affordable=!CityLocal||!cityCosts.TryGetValue(target,out int total)||total<=Hero["points"].I;
        Color routeColor=affordable?new Color(.97f,.79f,.36f):new Color(.89f,.40f,.27f);
        var all=new List<Vector2Int>();for(int sum=0;sum<size*2;sum++)for(int row=0;row<size;row++){int col=sum-row;if(col>=0&&col<size)all.Add(new Vector2Int(row,col));}
        Color tint=City["theme"].S=="oasis"||City["theme"].S=="highland"?new Color(1,.94f,.78f):City["theme"].S=="forest"?new Color(.88f,1,.94f):Color.white;
        foreach(var q in all){
            Vector2 p=CityXY(q,tw,th,center);char t=CityTile(q.x,q.y);int variant=(q.x*17+q.y*7+City["place"].I)%3;
            CityTexture(CityGround(t)+"-"+variant,p,tw,tint);
            if(t=='+'||t=='=')CityTexture((t=='='?"bridge-":"road-")+CityRoadMask(q),p,tw,Color.white);
            if(t=='~'){
                for(int i=0;i<4;i++){var next=q+CitySteps[i];if(!CityInside(next)||"~=".Contains(CityTile(next.x,next.y)))continue;
                    Vector2 neighbor=CityXY(next,tw,th,center),mid=(p+neighbor)/2,edge=new Vector2((i==0||i==2?1:-1)*tw*.48f,th*.48f);
                    Line(mid-edge,mid+edge,new Color(.76f,.77f,.49f,.75f),2.3f);
                }
            }
            if(cityGrid)CityDiamond(p,tw*.985f,th*.985f,new Color(.13f,.22f,.17f,.40f),.7f);
            if(cityReach&&CityLocal&&cityCosts.TryGetValue(q,out int cost)&&cost<=Hero["points"].I)CityDiamond(p,tw*.88f,th*.88f,new Color(.64f,.87f,.57f,.65f));
        }
        for(int i=0;i<route.Count;i++){
            var p=CityXY(route[i],tw,th,center);CityDiamond(p,tw*.86f,th*.86f,new Color(routeColor.r,routeColor.g,routeColor.b,.65f));
            if(i>0){var prev=CityXY(route[i-1],tw,th,center);Line(prev,p,new Color(.12f,.18f,.12f,.8f),5);DashedLine(prev,p,routeColor,2.4f,6,4);}
        }
        if(CityInside(cityHover))CityDiamond(CityXY(cityHover,tw,th,center),tw,th,new Color(.96f,.91f,.69f,.95f),2);
        if(chosenRow>=0)CityDiamond(CityXY(target,tw,th,center),tw,th,route.Count>0?brass:new Color(.9f,.4f,.27f),2.5f);
        // Ground is completed first, then scenery back-to-front; tall props cannot hide the route underneath a later tile.
        foreach(var q in all){
            var p=CityXY(q,tw,th,center);char t=CityTile(q.x,q.y);int variant=(q.x*17+q.y*7+City["place"].I)%3;
            if(cityNodes.TryGetValue(q,out var node)){
                bool matched=cityKind=="全部设施"||node["label"].S==cityKind;
                CityTexture("building-"+node["kind"].S,p,tw,matched?Color.white:new Color(.65f,.69f,.64f,.6f));
            }else if(t=='t'||t=='^'||t=='#'||t=='.'&&(q.x*11+q.y*7)%5==0)CityTexture("prop-"+CityGround(t)+"-"+variant,p,tw,tint);
            if(CityLocal&&q.x==Hero["row"].I&&q.y==Hero["col"].I)DrawCityHero(p,tw);
        }
        foreach(var q in all){
            if(!cityNodes.TryGetValue(q,out var node))continue;
            bool picked=q.x==chosenRow&&q.y==chosenCol,hovered=q==cityHover,matched=cityKind=="全部设施"||node["label"].S==cityKind;
            if(!matched&&!picked&&!hovered||!cityLabels&&!picked&&!hovered)continue;
            var p=CityXY(q,tw,th,center);string label=(picked||hovered?node["name"].S:node["label"].S)+(node["residents"].A.Count>0?" · "+node["residents"].A.Count:"");
            Rect tag=TagRect(new Vector2(p.x,p.y+th*.46f),label,picked);tag.x-=tag.width/2;
            if(localBounds.Overlaps(tag))TagLabel(tag,label,picked);
            if(over&&tag.Contains(e.mousePosition))cityHover=q;
        }
        if(CityLocal){var p=CityXY(new Vector2Int(Hero["row"].I,Hero["col"].I),tw,th,center);HaloLabel(new Rect(p.x-64,p.y-75*Mathf.Clamp(tw/46,.75f,1.35f),128,22),Hero["name"].S+" · 主角",mapLabelOn,mapHaloBold);}
        if(CityInside(cityHover)&&!cityPointerDown){
            var q=cityHover;char t=CityTile(q.x,q.y);string label=cityNodes.TryGetValue(q,out var node)?node["name"].S:CityTerrain(t);
            string detail=CityBlocked(t)?"不可通行 · "+(t=='~'?"须从桥梁绕行":t=='^'?"山势险峻":"请经城门出入"):cityCosts.TryGetValue(q,out int cost)?(cost==0?"当前寻路起点":"路线消耗 "+cost+" 行动力"+(CityLocal&&cost>Hero["points"].I?" · 本月不足":"")):"没有可通行的路线";
            Rect tip=new Rect(Mathf.Clamp(e.mousePosition.x+18,8,r.width-256),Mathf.Clamp(e.mousePosition.y+22,8,r.height-65),248,58);
            Panel(tip);GUI.Label(new Rect(tip.x+12,tip.y+6,225,23),label+"  /  "+CityTerrain(t),cityTip);GUI.Label(new Rect(tip.x+12,tip.y+31,225,21),detail,cityTiny);
        }
        if(over&&e.type==EventType.ScrollWheel){CityZoom(cityZoom*(e.delta.y>0?1/1.15f:1.15f),e.mousePosition);e.Use();}
        if(over&&e.type==EventType.MouseDown&&(e.button==0||e.button==2)){cityPointerDown=true;cityPanned=e.button==2;cityPress=cityLastMouse=e.mousePosition;GUI.FocusControl(null);}
        if(cityPointerDown&&e.type==EventType.MouseDrag){if(Vector2.Distance(e.mousePosition,cityPress)>5)cityPanned=true;if(cityPanned){cityPan+=e.mousePosition-cityLastMouse;CityClampPan();}cityLastMouse=e.mousePosition;e.Use();}
        if(cityPointerDown&&e.rawType==EventType.MouseUp){
            if(!cityPanned&&over&&CityInside(cityHover)){chosenRow=cityHover.x;chosenCol=cityHover.y;GUI.FocusControl(null);}
            cityPointerDown=false;if(e.type==EventType.MouseUp)e.Use();
        }
        GUI.EndGroup();
    }
    void DrawCityHero(Vector2 p,float tw){
        float s=Mathf.Clamp(tw/46,.75f,1.35f);Color c=new Color(.97f,.83f,.46f);
        CityDiamond(p,tw*.44f,tw*.22f,c,2);Line(p+new Vector2(-6,-4)*s,p+new Vector2(-4,-31)*s,c,2);Line(p+new Vector2(6,-4)*s,p+new Vector2(3,-31)*s,c,2);
        // Shared paper-puppet art keeps the player consistent with portraits and dialogue.
        DrawPaperPortrait(new Rect(p.x-15*s,p.y-59*s,30*s,44*s),Hero,true,false);
        Line(p+new Vector2(0,-13)*s,p+new Vector2(0,-2)*s,c,2);
    }
    void DrawCityLegend(){
        Rect r=new Rect(CityViewport.x,758,CityViewport.width,42);Fill(r,new Color(.06f,.11f,.09f,.88f));
        string[] labels={"街桥 1","平田 2","林地 3","水山城垣 ×"};Color[] colors={new Color(.74f,.68f,.48f),new Color(.59f,.65f,.39f),new Color(.31f,.51f,.34f),new Color(.31f,.50f,.51f)};
        for(int i=0;i<labels.Length;i++){float x=r.x+13+i*110;Fill(new Rect(x,r.y+15,9,9),colors[i]);GUI.Label(new Rect(x+17,r.y+9,100,24),labels[i],small);}
        string status=CityLocal?"本月余 "+Hero["points"].S+" / "+Hero["max_points"].S+" 行动力":"游览地图 · 行旅不结算";
        GUI.Label(new Rect(r.xMax-320,r.y+9,300,24),status,centerSmall);
    }
    void DrawCityOverview(){
        Rect r=new Rect(CityViewport.x+10,CityViewport.yMax-116,170,104);Panel(r);int size=City["size"].I;float unit=4.4f;
        var center=new Vector2(r.center.x,r.y+12);foreach(var pair in cityNodes){var q=pair.Key;var p=center+new Vector2((q.y-q.x)*unit,(q.x+q.y)*unit*.5f);Fill(new Rect(p.x-1.5f,p.y-1.5f,3,3),brass);}
        for(int row=0;row<size;row++)for(int col=0;col<size;col++){char t=CityTile(row,col);var p=center+new Vector2((col-row)*unit,(col+row)*unit*.5f);if(t=='~')Fill(new Rect(p.x-2,p.y,4,2),new Color(.35f,.58f,.58f,.75f));else if(t=='+'||t=='=')Fill(new Rect(p.x-1,p.y,2,1),muted);}
        if(CityLocal){var p=center+new Vector2((Hero["col"].I-Hero["row"].I)*unit,(Hero["col"].I+Hero["row"].I)*unit*.5f);Outline(new Rect(p.x-3,p.y-3,6,6),paperText,2);}
        GUI.Label(new Rect(r.x+8,r.yMax-23,r.width-16,20),"北 ↑  /  点此收览全图",cityTiny);
        // Overview is chrome, never a destination tile.
        if(GUI.Button(r,GUIContent.none,GUIStyle.none)){cityZoom=1;cityPan=Vector2.zero;chosenRow=chosenCol=-1;}
    }
    void DrawNodeCard(){
        Rect c=PlaceCard;Panel(c);GUILayout.BeginArea(new Rect(c.x+12,c.y+10,c.width-24,c.height-18));
        if(!City.Has){GUILayout.Label("正在展开城中街巷…",small);GUILayout.EndArea();return;}
        GUILayout.Label(City["name"].S+" · 城中",heading);GUILayout.Label(City["country_name"].S+" · "+City["population"].S+"人在世",small);
        bool local=CityLocal;
        GUILayout.Label(local?Hero["name"].S+" · "+Hero["position_name"].S:"远方地图预览 · 浏览不消耗回合",small);
        GUI.enabled=!cityLoading;
        GUILayout.BeginHorizontal();if(Btn("主角所在",Hero["created"].B))LoadCity(Hero["place"].I);if(Btn("下一城镇")){var all=W["locations"].A;int idx=all.FindIndex(p=>p["id"].I==SelectedPlace);LoadCity(all[(idx+1)%all.Count]["id"].I);}GUILayout.EndHorizontal();
        cityKind=Cycle(cityKind,new[]{"全部设施"}.Concat(City["nodes"].A.Select(n=>n["label"].S).Distinct()).ToArray());
        mainScroll=GUILayout.BeginScrollView(mainScroll);
        var at=new Vector2Int(chosenRow,chosenCol);var node=cityNodes.TryGetValue(at,out var found)?found:new J();
        if(CityInside(at)){
            char t=CityTile(at.x,at.y);GUILayout.Label(node["name"].Has?node["name"].S:CityTerrain(t)+"（"+(chosenRow+1)+"，"+(chosenCol+1)+"）",sub);
            if(node["description"].Has)T(node["description"].S,small);
            var path=CityPreview(at);int cost=cityCosts.TryGetValue(at,out int amount)?amount:0;
            T(path.Count>0?Math.Max(0,path.Count-1)+"格 · "+cost+"行动力 · 道路优先":"不可抵达 · "+(CityBlocked(t)?CityTerrain(t)+"阻断通行":"没有连通步道"),small);
            string reason=path.Count==0?"此处不可通行":path.Count==1?"已在寻路起点":cost>Hero["points"].I&&local?"本月行动力不足":"行走至此 · "+cost+"力";
            if(local){if(Btn(reason,!cityLoading&&!Client.Busy&&Paused&&path.Count>1&&cost<=Hero["points"].I))HeroAction("walk","place",City["place"],"row",chosenRow,"col",chosenCol);if(cost>Hero["points"].I)T("可在下方月盘结束本月，恢复行动力。",small);}
            else if(Btn("以此为游览起点",path.Count>0&&!CityBlocked(t))){originRow=chosenRow;originCol=chosenCol;cityPathKey="";EnsureCityRoutes();}
            if(local&&!Paused)T("先暂停世界推演或收招，再安排行旅。",small);
            string kind=node["kind"].S;
            if((kind=="market"||kind=="workshop")&&Btn("打开过客货栈 ›")){inventoryMode="货栈";Go(2);}
            if((kind=="training"||kind=="temple"||kind=="faction")&&Btn("功法与闭关 ›"))Go(3);
            if(kind=="government"&&Btn("查看所属官府 ›"))OpenInstitution(true,City["country"].I);
            if((kind=="gate"||kind=="dock")&&Btn("查看城外行路 ›")){mapLayer="行路";Go(0);}
            if(node.Has){T("此处人物 · "+node["residents"].A.Count+"人",sub);foreach(var resident in node["residents"].A)if(GUILayout.Button(resident["name"].S+" · "+resident["job"].S,chip)){SelectPerson(resident["id"].I);OpenPersonScreen();}}
        }else{T("点选地格，查看山水与行旅路线。",sub);T("金线标示最低行动力路径；绿色边框显示本月可达范围。",small);}
        GUILayout.Space(10);T("城中设施  /  "+City["nodes"].A.Count+"处",sub);
        foreach(var n in City["nodes"].A.Where(n=>cityKind=="全部设施"||n["label"].S==cityKind))if(GUILayout.Button(n["glyph"].S+"  "+n["name"].S+" · "+n["residents"].A.Count+"人",n["row"].I==chosenRow&&n["col"].I==chosenCol?selected:chip))SelectCityNode(n,true);
        if(local)DrawTravel();GUILayout.EndScrollView();GUI.enabled=true;GUILayout.EndArea();
    }
}
}
