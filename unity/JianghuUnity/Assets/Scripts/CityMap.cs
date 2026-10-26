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
    readonly Dictionary<Vector2Int,J> cityLots=new Dictionary<Vector2Int,J>();
    readonly Dictionary<Vector2Int,J> cityDrawNodes=new Dictionary<Vector2Int,J>();
    string cityPathKey="";
    float cityZoom=1;
    Vector2 cityPan,cityPress,cityLastMouse;
    bool cityPointerDown,cityPanned,cityLabels=true,cityReach,cityGrid,cityLoading;
    int cityRequest;
    Vector2Int cityHover=new Vector2Int(-1,-1);
    Vector2Int cityTipTile=new Vector2Int(-1,-1);
    float cityHoverSince;
    GUIStyle cityTiny,cityTip,cityTitle,cityName,cityCount;
    Rect CityOverviewRect=>new Rect(CityViewport.x+12,CityViewport.yMax-138,194,126);
    Rect CityDestinationRect=>new Rect(CityViewport.center.x-196,CityViewport.yMax-76,392,64);
    static readonly Vector2Int[] CitySteps={new Vector2Int(-1,0),new Vector2Int(0,1),new Vector2Int(1,0),new Vector2Int(0,-1)};
    Rect CityViewport=>new Rect(rosterOpen?284:42,146,rosterOpen?1118:1360,604);
    bool CityLocal=>Hero["created"].B&&City["place"].I==Hero["place"].I;

    public void LoadCity(int place){
        int request=++cityRequest;SelectedPlace=place;cityLoading=true;
        Client.Fetch("/api/city?place="+place,j=>{
            if(request!=cityRequest)return;
            bool changed=!City.Has||City["place"].I!=place;
            City=j;cityLoading=false;cityPathKey="";IndexCityBuildings();
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
        cityName=new GUIStyle(centerSmall){fontSize=13,wordWrap=false};TextColor(cityName,new Color(.94f,.92f,.81f));
        cityCount=new GUIStyle(cityTiny){fontSize=10};
    }
    static string CityTerrain(char t)=>t=='~'?"水域":t=='^'?"山岭":t=='#'?"城垣":t=='t'?"林地":t==','?"田圃":t=='+'?"街道":t=='='?"桥梁":"平地";
    static bool CityBlocked(char t)=>t=='~'||t=='^'||t=='#';
    static int CityStepCost(char t)=>t=='+'||t=='='?1:t=='t'?3:2;
    char CityTile(int row,int col)=>City["rows"][row].S[col];
    bool CityInside(Vector2Int p)=>p.x>=0&&p.y>=0&&p.x<City["size"].I&&p.y<City["size"].I;
    static int CityLotSize(J node)=>node["footprint"]["size"].I==2?2:1;
    static Vector2Int CityLotCorner(J node)=>node["footprint"].Has?new Vector2Int(node["footprint"]["row"].I,node["footprint"]["col"].I):new Vector2Int(node["row"].I,node["col"].I);
    Vector2 CityBuildingPoint(J node,float tw,float th,Vector2 center){int size=CityLotSize(node);return CitySurface(CityLotCorner(node),tw,th,center)+new Vector2(0,(size-1)*th);}
    static string CityLotLabel(J node){int size=CityLotSize(node);return size+"×"+size+" · "+(size*size)+"格占地";}
    Vector2Int CityPickDestination(Vector2Int tile)=>cityLots.TryGetValue(tile,out var node)?new Vector2Int(node["row"].I,node["col"].I):tile;
    void IndexCityBuildings(){
        cityNodes.Clear();cityLots.Clear();cityDrawNodes.Clear();
        foreach(var node in City["nodes"].A){
            cityNodes[new Vector2Int(node["row"].I,node["col"].I)]=node;
            int size=CityLotSize(node);var start=CityLotCorner(node);
            for(int row=0;row<size;row++)for(int col=0;col<size;col++)cityLots[start+new Vector2Int(row,col)]=node;
            cityDrawNodes[start+new Vector2Int(size-1,size-1)]=node;
        }
        BuildCityTerrain();
    }
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
        IndexCityBuildings();
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
        int size=City["size"].I;tw=Mathf.Min((viewport.width-30)/(2*size),(viewport.height-120)/size)*cityZoom;th=tw*.5f;
        center=new Vector2(viewport.width*.5f,viewport.height*.5f-(size-1)*th+tw*.6f)+cityPan;
    }
    static Vector2 CityXY(Vector2Int p,float tw,float th,Vector2 center)=>center+new Vector2((p.y-p.x)*tw,(p.x+p.y)*th);
    static Vector2Int CityPick(Vector2 mouse,float tw,float th,Vector2 center){var q=mouse-center;return new Vector2Int(Mathf.RoundToInt((q.y/th-q.x/tw)*.5f),Mathf.RoundToInt((q.y/th+q.x/tw)*.5f));}
    void CityClampPan(){Rect r=CityViewport;cityPan.x=Mathf.Clamp(cityPan.x,-r.width*cityZoom*.48f,r.width*cityZoom*.48f);cityPan.y=Mathf.Clamp(cityPan.y,-r.height*cityZoom*.48f,r.height*cityZoom*.48f);}
    void CityZoom(float zoom,Vector2 anchor){
        float next=Mathf.Clamp(zoom,.75f,2.8f),ratio=next/cityZoom;var middle=new Vector2(CityViewport.width/2,CityViewport.height/2);
        cityPan=(anchor-middle)*(1-ratio)+cityPan*ratio;cityZoom=next;CityClampPan();
    }
    void CityFocus(Vector2Int point){CityGeometry(CityViewport,out float tw,out float th,out var center);cityPan+=new Vector2(CityViewport.width/2,CityViewport.height/2)-CitySurface(point,tw,th,center);CityClampPan();}
    void SelectCityNode(J node,bool focus=false){chosenRow=node["row"].I;chosenCol=node["col"].I;GUI.FocusControl(null);if(focus)CityFocus(new Vector2Int(chosenRow,chosenCol));}

    void DrawCityScreen(Rect screen){
        InitCityStyles();Tile(screen,"bg");var backdrop=CityArt("backdrop");if(backdrop!=null)GUI.DrawTexture(screen,backdrop,ScaleMode.StretchToFill);
        if(cityLoading&&!string.IsNullOrEmpty(Client.Error))cityLoading=false;
        if(!City.Has){GUI.Label(new Rect(480,400,720,50),"正在展开城中街巷…",centerTitle);return;}
        EnsureCityRoutes();
        GUI.Label(new Rect(CityViewport.center.x-230,64,460,36),City["name"].S+" · 城中行旅",cityTitle);
        Line(new Vector2(CityViewport.center.x-280,83),new Vector2(CityViewport.center.x-170,83),new Color(.7f,.6f,.36f,.4f));
        Line(new Vector2(CityViewport.center.x+170,83),new Vector2(CityViewport.center.x+280,83),new Color(.7f,.6f,.36f,.4f));
        GUI.Label(new Rect(CityViewport.center.x-270,97,540,17),City["country_name"].S+"  /  "+City["theme_label"].S+"  /  "+City["population"].S+"人在世",cityTiny);
        DrawCityBoard(CityViewport);
        DrawCityToolbar();DrawCityLegend();DrawCityOverview();DrawCityDestination();
        if(cityLoading)GUI.Label(new Rect(620,370,430,44),"正在展开新图…",centerTitle);
    }
    // Toolbar as a single brushed ink stroke with plain text commands, not a row of editor buttons.
    // Active toggles get a short vermilion brush mark under the word.
    bool CityTool(Rect r,string label,bool on=false){
        if(on){var brush=UI("tab_brush");if(brush!=null)GUI.DrawTexture(new Rect(r.x+4,r.yMax-9,r.width-8,9),brush,ScaleMode.StretchToFill);}
        return GUI.Button(r,label,on?linkOnStyle:linkStyle);
    }
    void DrawCityToolbar(){
        float left=CityViewport.x;Rect r=new Rect(left,110,CityViewport.width,34);
        var strip=UI("strip");if(strip!=null)GUI.DrawTexture(r,strip,ScaleMode.StretchToFill);else Fill(r,new Color(.055f,.095f,.087f,.88f));
        GUI.enabled=!cityLoading;float y=112;
        if(CityTool(new Rect(left+14,y,30,28),"－"))CityZoom(cityZoom/1.2f,new Vector2(CityViewport.width/2,CityViewport.height/2));
        GUI.Label(new Rect(left+44,y+5,50,20),Mathf.RoundToInt(cityZoom*100)+"%",centerSmall);
        if(CityTool(new Rect(left+94,y,30,28),"＋"))CityZoom(cityZoom*1.2f,new Vector2(CityViewport.width/2,CityViewport.height/2));
        if(CityTool(new Rect(left+132,y,52,28),"全图")){cityZoom=1;cityPan=Vector2.zero;}
        GUI.enabled=!cityLoading&&CityLocal;
        if(CityTool(new Rect(left+188,y,84,28),"定位主角"))CityFocus(new Vector2Int(Hero["row"].I,Hero["col"].I));
        GUI.enabled=!cityLoading;
        GUI.Label(new Rect(left+276,y+4,14,20),"·",centerSmall);
        if(CityTool(new Rect(left+292,y,52,28),"名牌",cityLabels))cityLabels=!cityLabels;
        if(CityTool(new Rect(left+348,y,52,28),"地格",cityGrid))cityGrid=!cityGrid;
        if(CityLocal&&CityTool(new Rect(left+404,y,84,28),"可达范围",cityReach))cityReach=!cityReach;
        GUI.Label(new Rect(r.xMax-290,y+6,270,20),"滚轮缩放 · 拖动浏览 · 点选目的地",cityTiny);
        GUI.enabled=true;
    }
    void CityTexture(string name,Vector2 p,float tw,Color tint){var t=CityArt(name);if(t==null)return;var old=GUI.color;GUI.color=tint;float s=tw/128;GUI.DrawTexture(new Rect(p.x-128*s,p.y-171*s,256*s,256*s),t);GUI.color=old;}
    void CityDiamond(Vector2 p,float tw,float th,Color c,float width=1.2f){Line(p+new Vector2(0,-th),p+new Vector2(tw,0),c,width);Line(p+new Vector2(tw,0),p+new Vector2(0,th),c,width);Line(p+new Vector2(0,th),p+new Vector2(-tw,0),c,width);Line(p+new Vector2(-tw,0),p+new Vector2(0,-th),c,width);}
    string CityGround(char t)=>t=='~'||t=='='?"water":t=='^'?"mountain":t=='#'?"wall":t=='t'?"forest":t==','?"field":t=='+'?"street":"grass";
    int CityRoadMask(Vector2Int p){int mask=0;for(int i=0;i<4;i++){var q=p+CitySteps[i];if(CityInside(q)&&"+=".Contains(CityTile(q.x,q.y)))mask|=1<<i;}return mask;}
    int CitySceneryAxis(Vector2Int p,bool bridge){
        int row=0,col=0;for(int i=0;i<4;i++){var q=p+CitySteps[i];if(!CityInside(q))continue;char t=CityTile(q.x,q.y);
            if(bridge?"+=".Contains(t):t=='#'){if(i%2==0)row++;else col++;}}
        // Bridge artwork rises to the right; wall artwork falls to the right.
        return bridge?(col>row?1:0):(row>col?1:0);
    }
    void DrawCityBoard(Rect r){
        CityGeometry(r,out float tw,out float th,out var center);int size=City["size"].I;
        GUI.BeginGroup(r);var e=Event.current;var localBounds=new Rect(0,0,r.width,r.height);
        bool over=localBounds.Contains(e.mousePosition)&&!CityOverviewRect.Contains(e.mousePosition+r.position)&&!(chosenRow>=0&&CityDestinationRect.Contains(e.mousePosition+r.position))&&!OverUI(e.mousePosition+r.position)&&!cityLoading;
        cityHover=over?CitySurfacePick(e.mousePosition,tw,th,center):new Vector2Int(-1,-1);if(!CityInside(cityHover))cityHover=new Vector2Int(-1,-1);
        cityHover=CityPickDestination(cityHover);
        var target=chosenRow>=0?new Vector2Int(chosenRow,chosenCol):cityHover;
        var route=CityPreview(target);bool affordable=!CityLocal||!cityCosts.TryGetValue(target,out int total)||total<=Hero["points"].I;
        Color routeColor=affordable?new Color(.97f,.79f,.36f):new Color(.89f,.40f,.27f);
        var all=new List<Vector2Int>();for(int sum=0;sum<size*2;sum++)for(int row=0;row<size;row++){int col=sum-row;if(col>=0&&col<size)all.Add(new Vector2Int(row,col));}
        // The soft silhouette sits beneath a solid, terraced block landscape.
        var island=CityArt("island-shadow");var islandCenter=CityXY(new Vector2Int(0,0),tw,th,center)+new Vector2(0,(size-1)*th);
        if(island!=null)GUI.DrawTexture(new Rect(islandCenter.x-size*tw-30,islandCenter.y-size*th-14,size*tw*2+60,size*th*2+50),island);
        Color tint=City["theme"].S=="oasis"||City["theme"].S=="highland"?new Color(1,.94f,.78f):City["theme"].S=="forest"?new Color(.88f,1,.94f):Color.white;
        foreach(var q in all){
            Vector2 p=CitySurface(q,tw,th,center);char t=CityTile(q.x,q.y);int variant=(q.x*17+q.y*7+City["place"].I)%3;
            var ground=t=='='?CityXY(q,tw,th,center):p;
            DrawCityBlock(q,ground,tw,th,tint);
            CityTexture(CityGround(t)+"-"+variant,ground,tw,tint);
            if(t=='+'||t=='=')CityTexture((t=='='?"bridge-":"road-")+CityRoadMask(q),p,tw,Color.white);
            DrawCityStairs(q,p,tw,th);
            if(cityGrid)CityDiamond(p,tw*.985f,th*.985f,new Color(.24f,.30f,.23f,.22f),.6f);
            if(cityReach&&CityLocal&&cityCosts.TryGetValue(q,out int cost)&&cost<=Hero["points"].I){CityTexture("reachable",p,tw,new Color(1,1,1,.65f));CityDiamond(p,tw*.94f,th*.94f,new Color(.69f,.84f,.53f,.38f));}
        }
        // Every facility has one whole square lot. A 2x2 lot covers its four
        // former ground cells and their internal grid lines with a single base.
        foreach(var node in City["nodes"].A){
            var p=CityBuildingPoint(node,tw,th,center);int extent=CityLotSize(node);
            CityTexture("building-parcel",p,tw*extent,Color.white);
            CityDiamond(p,tw*extent*.985f,th*extent*.985f,new Color(.68f,.60f,.40f,.70f),cityGrid?1.5f:.9f);
        }
        for(int i=0;i<route.Count;i++){
            var p=CitySurface(route[i],tw,th,center);
            if(i>0){var prev=CitySurface(route[i-1],tw,th,center);Line(prev,p,new Color(.10f,.15f,.11f,.8f),5);DashedLine(prev,p,routeColor,2.6f,5,4);}
            CityTexture("route-dot",p,tw*.14f,routeColor);
        }
        if(CityInside(cityHover)){bool facility=cityNodes.TryGetValue(cityHover,out var hoverNode);float extent=facility?CityLotSize(hoverNode):1;CityDiamond(facility?CityBuildingPoint(hoverNode,tw,th,center):CitySurface(cityHover,tw,th,center),tw*extent,th*extent,new Color(.96f,.91f,.69f,.95f),2);}
        if(chosenRow>=0){bool facility=cityNodes.TryGetValue(target,out var pickedNode);float extent=facility?CityLotSize(pickedNode):1;var p=facility?CityBuildingPoint(pickedNode,tw,th,center):CitySurface(target,tw,th,center);CityDiamond(p,tw*extent*.97f,th*extent*.97f,route.Count>0?brass:new Color(.9f,.4f,.27f),2);}
        // Ground is completed first, then scenery back-to-front; tall props cannot hide the route underneath a later tile.
        foreach(var q in all){
            var p=CitySurface(q,tw,th,center);char t=CityTile(q.x,q.y);int variant=(q.x*17+q.y*7+City["place"].I)%3;
            if(t=='=')CityTexture("bridge-axis-"+CitySceneryAxis(q,true),p,tw,Color.white);
            if(cityDrawNodes.TryGetValue(q,out var node)){
                bool matched=cityKind=="全部设施"||node["label"].S==cityKind;
                CityTexture("building-"+node["kind"].S,CityBuildingPoint(node,tw,th,center),tw*CityLotSize(node),matched?Color.white:new Color(.65f,.69f,.64f,.45f));
            }else if(!cityLots.ContainsKey(q)){
                if(t=='#')CityTexture("wall-axis-"+CitySceneryAxis(q,false),p,tw,tint);
                else if(t=='t'||t=='^'||t=='.'&&(q.x*11+q.y*7)%5==0)CityTexture("prop-"+CityGround(t)+"-"+variant,p,tw,tint);
            }
            if(CityLocal&&q.x==Hero["row"].I&&q.y==Hero["col"].I)DrawCityHero(p,tw);
        }
        var tags=new List<Rect>();
        foreach(var q in all.OrderBy(q=>q.x==chosenRow&&q.y==chosenCol?1:0)){
            if(!cityNodes.TryGetValue(q,out var node))continue;
            bool picked=q.x==chosenRow&&q.y==chosenCol,hovered=q==cityHover,matched=cityKind=="全部设施"||node["label"].S==cityKind;
            if(!matched&&!picked&&!hovered||!cityLabels&&!picked&&!hovered)continue;
            var p=CityBuildingPoint(node,tw,th,center);int extent=CityLotSize(node);string label=picked||hovered?node["name"].S:node["label"].S;
            float width=cityName.CalcSize(new GUIContent(label)).x+50;
            Rect tag=new Rect(p.x-width/2,p.y+th*extent*.80f,width,24);
            for(int attempt=0;attempt<7&&tags.Any(t=>t.Overlaps(tag));attempt++)tag.y+=25;
            tags.Add(tag);
            if(tag.y>p.y+th*extent*.80f+2)Line(p+new Vector2(0,th*extent*.80f),new Vector2(tag.center.x,tag.y),new Color(.84f,.76f,.5f,.45f));
            if(localBounds.Overlaps(tag)){
                // a small lacquered name board hung under the building: kind dot, name, a hairline, residents
                Frame(tag,picked?"plaque_on":"plaque",8);
                Fill(new Rect(tag.x+8,tag.center.y-3,6,6),CityNodeColor(node["kind"].S));
                GUI.Label(new Rect(tag.x+16,tag.y+1,tag.width-42,22),label,cityName);
                Fill(new Rect(tag.xMax-27,tag.y+6,1,tag.height-12),new Color(.79f,.64f,.35f,.4f));
                GUI.Label(new Rect(tag.xMax-26,tag.y+2,21,20),node["residents"].A.Count.ToString(),cityCount);
            }
            if(over&&tag.Contains(e.mousePosition))cityHover=q;
        }
        if(CityLocal){var p=CitySurface(new Vector2Int(Hero["row"].I,Hero["col"].I),tw,th,center);HaloLabel(new Rect(p.x-64,p.y-88*Mathf.Clamp(tw/46,.75f,1.35f),128,22),Hero["name"].S+" · 主角",mapLabelOn,mapHaloBold);}
        if(cityTipTile!=cityHover){cityTipTile=cityHover;cityHoverSince=Time.realtimeSinceStartup;}
        if(CityInside(cityHover)&&!cityPointerDown&&Time.realtimeSinceStartup-cityHoverSince>.3f){
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
        CityTexture("hero-ring",p,tw*.58f,Color.white);
        // Shared paper-puppet art keeps the player consistent with portraits and dialogue.
        Tex(new Rect(p.x-23*s,p.y-69*s,46*s,46*s),"medallion");
        DrawPaperPortrait(new Rect(p.x-14*s,p.y-65*s,28*s,36*s),Hero,true,false);
        Line(p+new Vector2(0,-16)*s,p+new Vector2(0,-3)*s,c,2);
    }
    Color CityNodeColor(string kind)=>kind=="market"||kind=="workshop"||kind=="granary"?new Color(.86f,.63f,.31f):kind=="government"||kind=="palace"||kind=="gate"?new Color(.76f,.38f,.29f):kind=="clinic"||kind=="garden"?new Color(.49f,.71f,.48f):new Color(.48f,.69f,.68f);
    void DrawCityLegend(){
        Rect r=new Rect(CityViewport.x,758,CityViewport.width,42);var strip=UI("strip");if(strip!=null)GUI.DrawTexture(r,strip,ScaleMode.StretchToFill);else Fill(r,new Color(.06f,.11f,.09f,.88f));
        string[] labels={"街桥 1","平田 2","林地 3","水山城垣 ×"};Color[] colors={new Color(.74f,.68f,.48f),new Color(.59f,.65f,.39f),new Color(.31f,.51f,.34f),new Color(.31f,.50f,.51f)};
        for(int i=0;i<labels.Length;i++){float x=r.x+24+i*110;Fill(new Rect(x,r.y+15,9,9),colors[i]);GUI.Label(new Rect(x+17,r.y+9,100,24),labels[i],small);}
        string status=CityLocal?"本月余 "+Hero["points"].S+" / "+Hero["max_points"].S+" 行动力":"游览地图 · 行旅不结算";
        GUI.Label(new Rect(r.xMax-330,r.y+9,300,24),status,centerSmall);
    }
    void DrawCityOverview(){
        Rect r=CityOverviewRect;Panel(r);int size=City["size"].I;float unit=Mathf.Min(78f/size,68f/size);
        GUI.Label(new Rect(r.x+12,r.y+4,170,19),"街巷总览  /  点击定位",cityTiny);
        var center=new Vector2(r.center.x,r.y+29);
        for(int row=0;row<size;row++)for(int col=0;col<size;col++){
            char t=CityTile(row,col);var p=CityXY(new Vector2Int(row,col),unit,unit*.5f,center);
            Color c=t=='~'?new Color(.27f,.50f,.51f):t=='^'||t=='t'?new Color(.28f,.40f,.30f):t=='+'||t=='='?new Color(.76f,.71f,.53f):new Color(.47f,.54f,.38f);
            CityDiamond(p,unit*.85f,unit*.42f,c,2.8f);
        }
        foreach(var pair in cityNodes){var node=pair.Value;var p=CityXY(CityLotCorner(node),unit,unit*.5f,center)+new Vector2(0,(CityLotSize(node)-1)*unit*.5f);int extent=CityLotSize(node);CityDiamond(p,unit*extent*.85f,unit*extent*.42f,CityNodeColor(node["kind"].S),1.5f);}
        if(chosenRow>=0){var p=CityXY(new Vector2Int(chosenRow,chosenCol),unit,unit*.5f,center);Outline(new Rect(p.x-4,p.y-3,8,6),brass,1);}
        if(CityLocal){var p=CityXY(new Vector2Int(Hero["row"].I,Hero["col"].I),unit,unit*.5f,center);Fill(new Rect(p.x-2.5f,p.y-2.5f,5,5),paperText);}
        GUI.Label(new Rect(r.x+8,r.yMax-23,80,18),"北 ↗",cityTiny);
        if(GUI.Button(new Rect(r.xMax-84,r.yMax-25,74,21),"收览全图",chip)){cityZoom=1;cityPan=Vector2.zero;}
        var hit=new Rect(r.x+8,r.y+25,r.width-16,r.height-52);
        if(GUI.Button(hit,GUIContent.none,GUIStyle.none)&&!cityLoading){var point=CityPick(Event.current.mousePosition,unit,unit*.5f,center);if(CityInside(point))CityFocus(point);}
    }
    void DrawCityDestination(){
        var at=new Vector2Int(chosenRow,chosenCol);if(!CityInside(at))return;
        Rect r=CityDestinationRect;Panel(r);Fill(new Rect(r.x,r.y,3,r.height),brass);
        var node=cityNodes.TryGetValue(at,out var n)?n:new J();string name=node.Has?node["name"].S:CityTerrain(CityTile(at.x,at.y));
        bool reachable=cityCosts.TryGetValue(at,out int cost),affordable=reachable&&cost<=Hero["points"].I;
        GUI.Label(new Rect(r.x+16,r.y+7,224,24),name,sub);
        string info=reachable?(cost==0?"已在此处":cost+" 行动力  /  "+(CityPreview(at).Count-1)+" 格"):("无法抵达 · "+CityTerrain(CityTile(at.x,at.y)));
        GUI.Label(new Rect(r.x+16,r.y+32,224,21),info,small);
        GUI.enabled=CityLocal&&affordable&&cost>0&&!cityLoading&&!Client.Busy&&Paused;
        if(GUI.Button(new Rect(r.xMax-137,r.y+14,124,36),!CityLocal?"远方预览":reachable&&!affordable?"行动力不足":"行走至此 →",selected))HeroAction("walk","place",City["place"],"row",chosenRow,"col",chosenCol);
        GUI.enabled=true;
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
            if(node.Has)T(CityLotLabel(node),small);
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
            if(node.Has){
                // The same anchored texture doubles as a legible facility portrait.
                Rect art=GUILayoutUtility.GetRect(100,112,GUILayout.ExpandWidth(true));
                Fill(art,new Color(.08f,.14f,.12f,.8f));Outline(art,new Color(.55f,.57f,.40f,.28f));
                CityTexture("building-"+node["kind"].S,art.center,106,Color.white);
                T("此处人物 · "+node["residents"].A.Count+"人",sub);foreach(var resident in node["residents"].A)if(GUILayout.Button(resident["name"].S+" · "+resident["job"].S,chip)){SelectPerson(resident["id"].I);OpenPersonScreen();}
            }
        }else{T("点选地格，查看山水与行旅路线。",sub);T("金线标示最低行动力路径；绿色边框显示本月可达范围。",small);}
        GUILayout.Space(10);T("城中设施  /  "+City["nodes"].A.Count+"处",sub);
        foreach(var n in City["nodes"].A.Where(n=>cityKind=="全部设施"||n["label"].S==cityKind))if(GUILayout.Button(n["glyph"].S+"  "+n["name"].S+" · "+n["residents"].A.Count+"人",n["row"].I==chosenRow&&n["col"].I==chosenCol?selected:chip))SelectCityNode(n,true);
        if(local)DrawTravel();GUILayout.EndScrollView();GUI.enabled=true;GUILayout.EndArea();
    }
}
}
