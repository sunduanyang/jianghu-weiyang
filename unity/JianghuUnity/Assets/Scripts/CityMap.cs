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
    readonly Dictionary<Vector2Int,Rect> cityCoordinateRects=new Dictionary<Vector2Int,Rect>();
    readonly List<Vector2Int> cityCells=new List<Vector2Int>();
    readonly List<J> cityLabelNodes=new List<J>(),cityBuildingNodes=new List<J>();
    readonly List<Vector2Int> cityPreviewPath=new List<Vector2Int>();
    J cityIndexedView;
    string cityIndexedLayout="";
    string[] cityRows=new string[0];
    int citySize,cityPreviewRevision=-1;
    Vector2Int cityPreviewTarget;
    bool cityClipLines;
    Rect cityLineBounds;
    int cityCoordinatePlace=-1;
    float cityCoordinateWidth;
    Vector2 cityCoordinateCenter;
    string cityPathKey="";
    float cityZoom=1;
    Vector2 cityPan,cityPress,cityLastMouse;
    bool cityPointerDown,cityPanned,cityLabels=true,cityReach,cityGrid,cityLoading,cityBattleEnding;
    Vector2Int cityHeldDirection;
    float cityNextStepAt;
    Vector2Int cityLastClickTile=new Vector2Int(-1,-1);
    Vector2 cityLastClickPosition;
    float cityLastClickAt=-10;
    int cityRequest;
    Vector2Int cityHover=new Vector2Int(-1,-1);
    Vector2Int cityTipTile=new Vector2Int(-1,-1);
    float cityHoverSince;
    GUIStyle cityTiny,cityTip,cityTitle,cityName,cityCount,cityCoordinateStyle;
    // Only the floating controls follow the roster. The camera always covers
    // the physical client, including space outside the 1680x960 UI safe area.
    Rect CityHudRect=>new Rect(rosterOpen?284:42,146,rosterOpen?1118:1360,604);
    Rect CityHeaderRect=>new Rect(CityHudRect.center.x-300,64,600,50);
    Rect CityToolbarRect=>new Rect(CityHudRect.x,114,CityHudRect.width,32);
    Rect CityOverviewRect=>new Rect(1420,70,252,176);
    Rect CityDestinationRect=>new Rect(CityHudRect.center.x-196,CityHudRect.yMax-76,392,64);
    Rect CityNoticeRect=>new Rect(CityHudRect.x+12,CityHudRect.y+8,CityHudRect.width-24,46);
    static readonly Vector2Int[] CitySteps={new Vector2Int(-1,0),new Vector2Int(0,1),new Vector2Int(1,0),new Vector2Int(0,-1)};
    Rect CityViewport {
        get {
            float scale=Mathf.Min(Screen.width/1680f,Screen.height/960f);
            float width=Screen.width/scale,height=Screen.height/scale;
            return new Rect((1680-width)*.5f,(960-height)*.5f,width,height);
        }
    }
    bool CityMapPointerAllowed(Vector2 mouse)=>!cityLoading&&CityViewport.Contains(mouse)
        &&!OverUI(mouse)&&!CityHeaderRect.Contains(mouse)&&!CityToolbarRect.Contains(mouse)
        &&!(creatingHero&&!Hero["created"].B&&new Rect(20,560,330,170).Contains(mouse))
        &&!CityOverviewRect.Contains(mouse)
        &&!(CityMovementBlockReason.Length>0&&CityNoticeRect.Contains(mouse))
        &&!(chosenRow>=0&&CityDestinationRect.Contains(mouse));
    bool CityLocal=>Hero["created"].B&&City["place"].I==Hero["place"].I;

    string CityMovementBlockReason=>!Hero["created"].B?"请先创建主角":!CityLocal?"远方地图预览，请先返回主角所在城镇":W["battle_active"].B?"切磋尚未结束，收招后才能在城中行走":W["autoplay"].B||W["job"]["remaining"].I>0?"世界正在推演，请先暂停再行走":Hero["points"].I<=0?"本月行动力已用尽，请结束本月":"";
    bool CityMovementAllowed=>Page==1&&City.Has&&CityMovementBlockReason.Length==0&&!personScreen&&!dialogueOpen&&!advanceOpen&&!choiceOpen;
    void UpdateCityMovement(){
        if(!CityMovementAllowed||cityLoading){StopCityWalk();cityHeldDirection=Vector2Int.zero;cityNextStepAt=0;return;}
        if(cityWalkSmokeInput)return;
        if(!Application.isFocused||GUI.GetNameOfFocusedControl()=="RosterSearch"){
            cityHeldDirection=Vector2Int.zero;cityNextStepAt=0;return;
        }
        if(Input.GetKeyDown(KeyCode.Escape)){StopCityWalk();return;}
        int row=(Input.GetKey(KeyCode.S)||Input.GetKey(KeyCode.DownArrow)?1:0)-(Input.GetKey(KeyCode.W)||Input.GetKey(KeyCode.UpArrow)?1:0);
        int col=(Input.GetKey(KeyCode.D)||Input.GetKey(KeyCode.RightArrow)?1:0)-(Input.GetKey(KeyCode.A)||Input.GetKey(KeyCode.LeftArrow)?1:0);
        // Take one cardinal step at a time, so two held keys cannot cut a corner.
        var direction=row!=0?new Vector2Int(row,0):new Vector2Int(0,col);
        bool changed=direction!=cityHeldDirection;cityHeldDirection=direction;
        if(direction==Vector2Int.zero){if(cityWalkKeyboard)StopCityWalk();cityNextStepAt=0;return;}
        if(!CityMovementAllowed){if(changed)Client.Status=CityMovementBlockReason;return;}
        if(cityWalking){cityWalkKeyboard=true;cityWalkCancelled=false;return;}
        if(changed)cityNextStepAt=0;
        if(cityLoading||Client.Busy||Time.realtimeSinceStartup<cityNextStepAt)return;
        TryCityWalk(new Vector2Int(Hero["row"].I,Hero["col"].I)+direction,true);
        cityNextStepAt=Time.realtimeSinceStartup+(changed?.18f:.09f);
    }
    bool TryCityWalk(Vector2Int target,bool keyboard=false){
        if(!CityMovementAllowed){Client.Status=CityMovementBlockReason;return false;}
        if(cityLoading||Client.Busy&&!cityWalking||!CityInside(target))return false;
        EnsureCityRoutes();
        if(!cityCosts.TryGetValue(target,out int cost)){Client.Status=CityCoordinate(target)+" 不可通行，请沿街巷或桥梁绕行";return false;}
        if(cost<=0){Client.Status="已在 "+CityCoordinate(target);return false;}
        if(cost>Hero["points"].I){Client.Status="前往 "+CityCoordinate(target)+" 需要 "+cost+" 行动力，本月不足";return false;}
        SelectCityCell(target);
        BeginCityWalk(target,keyboard);return true;
    }
    void ResumeCityBattle(){personScreen=dialogueOpen=false;Go(3);battleView=true;}
    System.Collections.IEnumerator EndCityBattle(){
        if(cityBattleEnding||Client.Busy)yield break;
        cityBattleEnding=true;
        try{
            J current=null;yield return Client.Request("/api/martial",null,j=>current=j);
            if(current==null)yield break;
            Martial=current;var battle=current["battle"];
            if(battle["status"].S=="active")yield return Client.Action("/api/battle",J.O("battle_id",battle["id"],"revision",battle["revision"],"action","surrender"),j=>Martial=j);
            else yield return Client.Refresh();
            if(!W["battle_active"].B){Client.Status="已收招，可用 WASD 或双击地格行走";if(Page==1)LoadCity(Hero["place"].I);}
        }finally{cityBattleEnding=false;}
    }
    static string CityColumn(int col){string label="";for(int value=col+1;value>0;value=(value-1)/26)label=(char)('A'+(value-1)%26)+label;return label;}
    static string CityCoordinate(Vector2Int at)=>CityColumn(at.y)+(at.x+1);
    void EnsureCityCoordinateLayout(float tw,float th,Vector2 center){
        int size=City["size"].I;
        if(cityCoordinateRects.Count==size*size&&cityCoordinatePlace==City["place"].I&&cityCoordinateWidth==tw&&cityCoordinateCenter==center)return;
        cityCoordinateRects.Clear();cityCoordinatePlace=City["place"].I;cityCoordinateWidth=tw;cityCoordinateCenter=center;
        // Tall terraces can project two different cells onto the same label.
        // Space labels along each isometric column and retain a leader to the tile.
        for(int diagonal=1-size;diagonal<size;diagonal++){
            var cells=new List<Vector2Int>();for(int row=0;row<size;row++){int col=row+diagonal;if(col>=0&&col<size)cells.Add(new Vector2Int(row,col));}
            float next=float.NegativeInfinity;
            foreach(var at in cells.OrderBy(q=>CitySurface(q,tw,th,center).y)){
                var p=CitySurface(at,tw,th,center);var rect=new Rect(p.x-13,Mathf.Max(p.y+th*.36f-7,next),26,14);
                cityCoordinateRects[at]=rect;next=rect.yMax+2;
            }
        }
    }
    Rect CityCoordinateRect(Vector2Int at,float tw,float th,Vector2 center){EnsureCityCoordinateLayout(tw,th,center);return cityCoordinateRects[at];}
    Vector2Int CityCoordinatePick(Vector2 mouse,float tw,float th,Vector2 center){
        EnsureCityCoordinateLayout(tw,th,center);
        foreach(var cell in cityCoordinateRects)if(cell.Value.Contains(mouse))return cell.Key;
        return new Vector2Int(-1,-1);
    }

    public void LoadCity(int place){
        StopCityWalk();
        StartCoroutine(LoadCityRoutine(place));
    }
    Texture2D CityArt(string key){if(!cityArt.TryGetValue(key,out var t)){citySyncLoads++;t=Resources.Load<Texture2D>("CityBiomes/"+key);cityArt[key]=t;}return t;}
    void InitCityStyles(){
        if(cityTiny!=null)return;
        cityTiny=new GUIStyle(centerSmall){fontSize=11};TextColor(cityTiny,new Color(.85f,.86f,.72f));
        cityTip=new GUIStyle(small){fontSize=12,wordWrap=false};TextColor(cityTip,paperText);
        cityTitle=new GUIStyle(centerTitle){fontSize=26};
        cityName=new GUIStyle(centerSmall){fontSize=13,wordWrap=false};TextColor(cityName,new Color(.94f,.92f,.81f));
        cityCount=new GUIStyle(cityTiny){fontSize=10};
        cityCoordinateStyle=new GUIStyle(cityTiny){fontSize=11,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter,wordWrap=false};
    }
    static string CityTerrain(char t)=>t=='s'?"浅滩":t=='~'?"水域":t=='^'?"山岭":t=='#'?"城垣":t=='t'?"林地":t==','?"田圃":t=='+'?"街道":t=='='?"桥梁":"平地";
    static bool CityBlocked(char t)=>t=='~'||t=='^'||t=='#';
    static int CityStepCost(char t)=>t=='+'||t=='='?1:t=='t'?3:2;
    char CityTile(int row,int col)=>cityRows[row][col];
    bool CityInside(Vector2Int p)=>p.x>=0&&p.y>=0&&p.x<citySize&&p.y<citySize;
    static int CityLotSize(J node)=>Mathf.Clamp(node["footprint"]["size"].I,1,3);
    static Vector2Int CityLotCorner(J node)=>node["footprint"].Has?new Vector2Int(node["footprint"]["row"].I,node["footprint"]["col"].I):new Vector2Int(node["row"].I,node["col"].I);
    Vector2 CityBuildingPoint(J node,float tw,float th,Vector2 center){int size=CityLotSize(node);return CitySurface(CityLotCorner(node),tw,th,center)+new Vector2(0,(size-1)*th);}
    static string CityLotLabel(J node){int size=CityLotSize(node);return size+"×"+size+" · "+(size*size)+"格占地";}
    void EnsureCityIndex(){if(!ReferenceEquals(cityIndexedView,City))IndexCityBuildings();}
    void IndexCityBuildings(){
        string key=City["layout_id"].S;bool changed=key!=cityIndexedLayout||cityRows.Length!=City["size"].I;
        if(changed){
            citySize=City["size"].I;cityRows=City["rows"].A.Select(row=>row.S).ToArray();
            cityCoordinateRects.Clear();cityPathKey="";cityIndexBuilds++;
            if(cityCells.Count!=citySize*citySize){cityCells.Clear();for(int sum=0;sum<citySize*2;sum++)for(int row=0;row<citySize;row++){int col=sum-row;if(col>=0&&col<citySize)cityCells.Add(new Vector2Int(row,col));}}
        }
        cityNodes.Clear();cityLots.Clear();cityDrawNodes.Clear();
        foreach(var node in CitySites){
            cityNodes[new Vector2Int(node["row"].I,node["col"].I)]=node;
            int size=CityLotSize(node);var start=CityLotCorner(node);
            for(int row=0;row<size;row++)for(int col=0;col<size;col++)cityLots[start+new Vector2Int(row,col)]=node;
            cityDrawNodes[start+new Vector2Int(size-1,size-1)]=node;
        }
        cityLabelNodes.Clear();cityLabelNodes.AddRange(CitySites.OrderBy(n=>n["row"].I+n["col"].I).ThenBy(n=>n["row"].I));
        cityBuildingNodes.Clear();cityBuildingNodes.AddRange(CitySites.OrderBy(n=>CityLotCorner(n).x+CityLotCorner(n).y+2*(CityLotSize(n)-1)).ThenBy(n=>CityLotCorner(n).x));
        if(changed){IndexCityTerrainTiles();BuildCityTerrain();cityIndexedLayout=key;}
        IndexCityAreas();
        IndexCityPeople();
        cityIndexedView=City;
    }
    Vector2Int CityOrigin(){
        if(CityLocal)return new Vector2Int(Hero["row"].I,Hero["col"].I);
        if(originRow>=0)return new Vector2Int(originRow,originCol);
        var gate=City["nodes"].A.First(n=>n["id"].S==City["entrance"].S);
        return new Vector2Int(gate["row"].I,gate["col"].I);
    }
    void EnsureCityRoutes(){
        EnsureCityIndex();var start=CityOrigin();string key=cityIndexedLayout+"/"+start;
        if(key==cityPathKey)return;
        cityPathKey=key;cityRouteBuilds++;BuildCityPaths(start,cityCosts,cityPrevious);
    }
    List<Vector2Int> CityPreview(Vector2Int target){
        if(cityPreviewRevision==cityRouteBuilds&&cityPreviewTarget==target)return cityPreviewPath;
        cityPreviewRevision=cityRouteBuilds;cityPreviewTarget=target;cityPreviewPath.Clear();
        if(!cityCosts.ContainsKey(target))return cityPreviewPath;
        var p=target;cityPreviewPath.Add(p);while(cityPrevious.TryGetValue(p,out var previous)){p=previous;cityPreviewPath.Add(p);}cityPreviewPath.Reverse();return cityPreviewPath;
    }
    void CityGeometry(Rect viewport,out float tw,out float th,out Vector2 center){
        int size=City["size"].I;tw=Mathf.Min((viewport.width-30)/(2*size),(viewport.height-120)/size)*cityZoom;th=tw*.5f;
        center=new Vector2(viewport.width*.5f,viewport.height*.5f-(size-1)*th+tw*.6f)+cityPan;
    }
    static Vector2 CityXY(Vector2Int p,float tw,float th,Vector2 center)=>center+new Vector2((p.y-p.x)*tw,(p.x+p.y)*th);
    static Vector2Int CityPick(Vector2 mouse,float tw,float th,Vector2 center){var q=mouse-center;return new Vector2Int(Mathf.RoundToInt((q.y/th-q.x/tw)*.5f),Mathf.RoundToInt((q.y/th+q.x/tw)*.5f));}
    void CityClampPan(){Rect r=CityViewport;cityPan.x=Mathf.Clamp(cityPan.x,-r.width*cityZoom*.48f,r.width*cityZoom*.48f);cityPan.y=Mathf.Clamp(cityPan.y,-r.height*cityZoom*.48f,r.height*cityZoom*.48f);}
    void CityZoom(float zoom,Vector2 anchor){
        float next=Mathf.Clamp(zoom,.75f,8f),ratio=next/cityZoom;var middle=new Vector2(CityViewport.width/2,CityViewport.height/2);
        cityPan=(anchor-middle)*(1-ratio)+cityPan*ratio;cityZoom=next;CityClampPan();
    }
    void CityFocus(Vector2Int point){CityGeometry(CityViewport,out float tw,out float th,out var center);cityPan+=new Vector2(CityViewport.width/2,CityViewport.height/2)-CitySurface(point,tw,th,center);CityClampPan();}
    void SelectCityNode(J node,bool focus=false){SelectCityCell(new Vector2Int(node["row"].I,node["col"].I));GUI.FocusControl(null);if(focus)CityFocus(new Vector2Int(chosenRow,chosenCol));}

    void DrawCityScreen(Rect screen){
        InitCityStyles();Fill(screen,new Color(.075f,.105f,.085f));
        if(cityLoading&&!string.IsNullOrEmpty(Client.Error))cityLoading=false;
        if(!City.Has){GUI.Label(new Rect(480,400,720,50),"正在展开城中街巷…",centerTitle);return;}
        EnsureCityRoutes();
        DrawCityBoard(CityViewport);
    }
    void DrawCityHud(){
        if(!City.Has)return;
        Rect header=CityHeaderRect;var strip=UI("strip");
        if(strip!=null)GUI.DrawTexture(header,strip,ScaleMode.StretchToFill);else Fill(header,new Color(.045f,.075f,.065f,.78f));
        GUI.Label(new Rect(header.center.x-230,64,460,36),City["name"].S+" · 城中行旅",cityTitle);
        Line(new Vector2(header.center.x-280,83),new Vector2(header.center.x-170,83),new Color(.7f,.6f,.36f,.4f));
        Line(new Vector2(header.center.x+170,83),new Vector2(header.center.x+280,83),new Color(.7f,.6f,.36f,.4f));
        var plan=City["settlement_layout"];
        GUI.Label(new Rect(header.center.x-365,97,730,17),City["country_name"].S+" / "+City["theme_label"].S+" / "+City["population"].S+"人在世"+(plan.Has?" / "+plan["label"].S+" · 普通聚落"+plan["ordinary_count"].S+"处 · 门派"+plan["sect_count"].S+"处":" / "+City["tier_label"].S),cityTiny);
        DrawCityToolbar();DrawCityOverview();DrawCityDestination();DrawCityMovementNotice();
        if(cityLoading)GUI.Label(new Rect(620,370,430,44),"正在展开新图…",centerTitle);
    }
    // Toolbar as a single brushed ink stroke with plain text commands, not a row of editor buttons.
    // Active toggles get a short vermilion brush mark under the word.
    bool CityTool(Rect r,string label,bool on=false){
        if(on){var brush=UI("tab_brush");if(brush!=null)GUI.DrawTexture(new Rect(r.x+4,r.yMax-9,r.width-8,9),brush,ScaleMode.StretchToFill);}
        return GUI.Button(r,label,on?linkOnStyle:linkStyle);
    }
    void DrawCityToolbar(){
        Rect r=CityToolbarRect;float left=r.x;
        var strip=UI("strip");if(strip!=null)GUI.DrawTexture(r,strip,ScaleMode.StretchToFill);else Fill(r,new Color(.055f,.095f,.087f,.88f));
        GUI.enabled=!cityLoading;float y=r.y+2;
        if(CityTool(new Rect(left+14,y,30,28),"－"))CityZoom(cityZoom/1.2f,new Vector2(CityViewport.width/2,CityViewport.height/2));
        GUI.Label(new Rect(left+44,y+5,50,20),Mathf.RoundToInt(cityZoom*100)+"%",centerSmall);
        if(CityTool(new Rect(left+94,y,30,28),"＋"))CityZoom(cityZoom*1.2f,new Vector2(CityViewport.width/2,CityViewport.height/2));
        if(CityTool(new Rect(left+132,y,52,28),"全图")){cityZoom=1;cityPan=Vector2.zero;}
        GUI.enabled=!cityLoading&&CityLocal;
        if(CityTool(new Rect(left+188,y,84,28),"定位主角"))CityFocus(new Vector2Int(Hero["row"].I,Hero["col"].I));
        GUI.enabled=!cityLoading;
        GUI.Label(new Rect(left+276,y+4,14,20),"·",centerSmall);
        if(CityTool(new Rect(left+292,y,52,28),"名牌",cityLabels))cityLabels=!cityLabels;
        if(CityTool(new Rect(left+348,y,68,28),"坐标格",cityGrid))cityGrid=!cityGrid;
        if(CityLocal&&CityTool(new Rect(left+420,y,84,28),"可达范围",cityReach))cityReach=!cityReach;
        GUI.Label(new Rect(r.xMax-365,y+6,355,20),"W ↗  A ↖  S ↙  D ↘ / 双击坐标格前往",cityTiny);
        GUI.enabled=true;
    }
    void DrawCityMovementNotice(){
        string reason=CityMovementBlockReason;if(reason.Length==0)return;
        Rect r=CityNoticeRect;Fill(r,new Color(.28f,.12f,.08f,.96f));Outline(r,brass);
        GUI.Label(new Rect(r.x+12,r.y+10,r.width-(W["battle_active"].B?310:24),27),reason,sub);
        if(W["battle_active"].B){
            GUI.enabled=!Client.Busy&&!cityBattleEnding;
            if(GUI.Button(new Rect(r.xMax-284,r.y+8,126,30),"返回切磋",chip))ResumeCityBattle();
            if(GUI.Button(new Rect(r.xMax-150,r.y+8,138,30),cityBattleEnding?"正在收招…":"收招认输，继续行旅",selected))Client.StartCoroutine(EndCityBattle());
            GUI.enabled=true;
        }
    }
    void CityTexture(string name,Vector2 p,float tw,Color tint){var t=CityArt(name);if(t==null)return;var old=GUI.color;GUI.color=tint;float s=tw/128;GUI.DrawTexture(new Rect(p.x-128*s,p.y-171*s,256*s,256*s),t);GUI.color=old;}
    void CityLine(Vector2 a,Vector2 b,Color c,float width=1){
        if(Event.current.type!=EventType.Repaint)return;
        if(cityClipLines&&!CityClipLine(ref a,ref b,new Rect(width,width,cityLineBounds.width-width*2,cityLineBounds.height-width*2)))return;
        var delta=b-a;float length=delta.magnitude;if(length<.01f)return;
        var matrix=GUI.matrix;var color=GUI.color;GUI.color=c;
        // Compose in local GUI space so scaled windows keep the same pivot.
        GUI.matrix=matrix*Matrix4x4.TRS(new Vector3(a.x,a.y,0),Quaternion.Euler(0,0,Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg),Vector3.one);
        GUI.DrawTexture(new Rect(0,-Mathf.Max(1,width)*.5f,length,Mathf.Max(1,width)),Texture2D.whiteTexture);GUI.matrix=matrix;GUI.color=color;
    }
    static bool CityClipLine(ref Vector2 a,ref Vector2 b,Rect bounds){
        var delta=b-a;float near=0,far=1;
        if(!CityClipEdge(-delta.x,a.x-bounds.xMin,ref near,ref far)||!CityClipEdge(delta.x,bounds.xMax-a.x,ref near,ref far)||!CityClipEdge(-delta.y,a.y-bounds.yMin,ref near,ref far)||!CityClipEdge(delta.y,bounds.yMax-a.y,ref near,ref far))return false;
        b=a+delta*far;a+=delta*near;return true;
    }
    static bool CityClipEdge(float direction,float distance,ref float near,ref float far){
        if(Mathf.Abs(direction)<.00001f)return distance>=0;float value=distance/direction;
        if(direction<0){if(value>far)return false;near=Mathf.Max(near,value);}else{if(value<near)return false;far=Mathf.Min(far,value);}return true;
    }
    void CityDashedLine(Vector2 a,Vector2 b,Color c,float width,float dash,float gap){float length=Vector2.Distance(a,b);if(length<1)return;var dir=(b-a)/length;for(float offset=0;offset<length;offset+=dash+gap)CityLine(a+dir*offset,a+dir*Mathf.Min(length,offset+dash),c,width);}
    void CityDiamond(Vector2 p,float tw,float th,Color c,float width=1.2f){CityLine(p+new Vector2(0,-th),p+new Vector2(tw,0),c,width);CityLine(p+new Vector2(tw,0),p+new Vector2(0,th),c,width);CityLine(p+new Vector2(0,th),p+new Vector2(-tw,0),c,width);CityLine(p+new Vector2(-tw,0),p+new Vector2(0,-th),c,width);}
    string CityGround(char t)=>t=='~'||t=='='||t=='s'?"water":t=='^'?"mountain":t=='#'?"wall":t=='t'?"forest":t==','?"field":t=='+'?"street":"grass";
    int CityRoadMask(Vector2Int p){int mask=0;for(int i=0;i<4;i++){var q=p+CitySteps[i];if(CityInside(q)&&"+=".Contains(CityTile(q.x,q.y)))mask|=1<<i;}return mask;}
    int CitySceneryAxis(Vector2Int p,bool bridge){
        int row=0,col=0;for(int i=0;i<4;i++){var q=p+CitySteps[i];if(!CityInside(q))continue;char t=CityTile(q.x,q.y);
            if(bridge?"+=".Contains(t):t=='#'){if(i%2==0)row++;else col++;}}
        // Bridge artwork rises to the right; wall artwork falls to the right.
        return bridge?(col>row?1:0):(row>col?1:0);
    }
    void DrawCityBoard(Rect r){
        CityGeometry(r,out float tw,out float th,out var center);int size=City["size"].I;
        GUI.BeginGroup(r);var e=Event.current;var localBounds=new Rect(0,0,r.width,r.height);cityClipLines=true;cityLineBounds=localBounds;
        bool over=CityMapPointerAllowed(e.mousePosition+r.position);
        cityHover=over?CitySurfacePick(e.mousePosition,tw,th,center):new Vector2Int(-1,-1);if(!CityInside(cityHover))cityHover=new Vector2Int(-1,-1);
        var target=chosenRow>=0?new Vector2Int(chosenRow,chosenCol):cityHover;
        var route=cityWalking?cityWalkRoute:CityPreview(target);int routeStart=cityWalking?Math.Max(0,cityWalkIndex-1):0;
        bool affordable=cityWalking||!CityLocal||!cityCosts.TryGetValue(target,out int total)||total<=Hero["points"].I;
        Color routeColor=affordable?new Color(.97f,.79f,.36f):new Color(.89f,.40f,.27f);
        var all=cityCells;
        if(e.type==EventType.Repaint){DrawCityLandscape(all,tw,th,center,r);DrawCityRoads(all,tw,th,center);}
        // Multi-cell settlements retain one continuous, horizontal foundation.
        foreach(var node in CitySites){
            var p=CityBuildingPoint(node,tw,th,center);int extent=CityLotSize(node);
            CityDiamond(p,tw*extent*.985f,th*extent*.985f,new Color(.20f,.22f,.13f,.75f),cityGrid?1.3f:.8f);
        }
        for(int i=routeStart;i<route.Count;i++){
            var p=CitySurface(route[i],tw,th,center);
            if(i>routeStart){var prev=CitySurface(route[i-1],tw,th,center);CityLine(prev,p,new Color(.10f,.15f,.11f,.8f),5);CityDashedLine(prev,p,routeColor,2.6f,5,4);}
            Fill(new Rect(p.x-2,p.y-2,4,4),routeColor);
        }
        if(CityLocal)DrawCityHero(CityHeroSurface(tw,th,center),tw);
        var tags=new List<Rect>();
        foreach(var node in cityLabelNodes.OrderBy(n=>n["row"].I==chosenRow&&n["col"].I==chosenCol?1:0)){
            var q=new Vector2Int(node["row"].I,node["col"].I);
            bool picked=q.x==chosenRow&&q.y==chosenCol,hovered=q==cityHover,matched=cityKind=="全部聚落"||node["label"].S==cityKind;
            if(!matched&&!picked&&!hovered||!cityLabels&&!picked&&!hovered)continue;
            var p=CityBuildingPoint(node,tw,th,center);int extent=CityLotSize(node);string label=picked||hovered||node["kind"].S=="sect"?node["name"].S:node["label"].S;
            float width=cityName.CalcSize(new GUIContent(label)).x+50;
            Rect tag=new Rect(p.x-width/2,p.y+th*extent*.80f,width,24);
            for(int attempt=0;attempt<7&&tags.Any(t=>t.Overlaps(tag));attempt++)tag.y+=25;
            tags.Add(tag);
            if(tag.y>p.y+th*extent*.80f+2)Line(p+new Vector2(0,th*extent*.80f),new Vector2(tag.center.x,tag.y),new Color(.84f,.76f,.5f,.45f));
            if(localBounds.Overlaps(tag)){
                // a small lacquered name board hung under the building: kind dot, name, a hairline, residents
                Fill(tag,new Color(.08f,.11f,.08f,.95f));Outline(tag,picked?brass:new Color(.47f,.48f,.32f));
                Fill(new Rect(tag.x+8,tag.center.y-3,6,6),CityNodeColor(node["kind"].S));
                GUI.Label(new Rect(tag.x+16,tag.y+1,tag.width-42,22),label,cityName);
                Fill(new Rect(tag.xMax-27,tag.y+6,1,tag.height-12),new Color(.79f,.64f,.35f,.4f));
                GUI.Label(new Rect(tag.xMax-26,tag.y+2,21,20),node["residents"].A.Count.ToString(),cityCount);
            }
            if(over&&tag.Contains(e.mousePosition))cityHover=q;
        }
        // Coordinates are a topmost hit layer: every cell, including a cell
        // beneath a multi-cell building, retains its own destination.
        if(cityGrid)foreach(var q in all){
            var p=CitySurface(q,tw,th,center);if(!cityLots.TryGetValue(q,out var gridLot)||CityLotSize(gridLot)==1)CityDiamond(p,tw*.985f,th*.985f,new Color(.81f,.79f,.61f,.48f),1);
            if(tw<24&&q!=cityHover&&!(q.x==chosenRow&&q.y==chosenCol))continue;
            Rect cell=tw>=24?CityCoordinateRect(q,tw,th,center):new Rect(p.x-13,p.y+th*.36f-7,26,14);
            var anchor=p+new Vector2(0,th*.36f);if(Vector2.Distance(cell.center,anchor)>2)Line(anchor,cell.center,new Color(.89f,.85f,.65f,.65f),1);
            bool heroHere=CityLocal&&q.x==Hero["row"].I&&q.y==Hero["col"].I;
            Fill(cell,heroHere?new Color(.38f,.27f,.10f,.97f):new Color(.035f,.08f,.075f,.88f));
            GUI.Label(cell,CityCoordinate(q),cityCoordinateStyle);
        }
        if(cityGrid&&tw>=24&&over){var coordinate=CityCoordinatePick(e.mousePosition,tw,th,center);if(CityInside(coordinate))cityHover=coordinate;}
        if(CityLocal)CityDiamond(CityHeroSurface(tw,th,center),tw*.88f,th*.88f,new Color(1,.82f,.35f),2.4f);
        if(CityInside(cityHover))CityDiamond(CitySurface(cityHover,tw,th,center),tw*.98f,th*.98f,new Color(.98f,.94f,.73f),2);
        if(chosenRow>=0)CityDiamond(CitySurface(target,tw,th,center),tw*.96f,th*.96f,route.Count>0?brass:new Color(.9f,.4f,.27f),2);
        if(CityLocal){var p=CityHeroSurface(tw,th,center);HaloLabel(new Rect(p.x-64,p.y-88*Mathf.Clamp(tw/46,.75f,1.35f),128,22),Hero["name"].S+" · 主角",mapLabelOn,mapHaloBold);}
        if(cityTipTile!=cityHover){cityTipTile=cityHover;cityHoverSince=Time.realtimeSinceStartup;}
        if(CityInside(cityHover)&&!cityPointerDown&&Time.realtimeSinceStartup-cityHoverSince>.3f){
            var q=cityHover;char t=CityTile(q.x,q.y);string label=cityLots.TryGetValue(q,out var node)?node["name"].S:CityAreaName(q);
            string detail=CityBlocked(t)?"不可通行 · "+(t=='~'?"请寻找浅滩涉水":t=='^'?"山势险峻":"请经城门出入"):cityCosts.TryGetValue(q,out int cost)?(cost==0?"当前寻路起点":"路线消耗 "+cost+" 行动力"+(CityLocal&&cost>Hero["points"].I?" · 本月不足":"")):"没有可通行的路线";
            Rect tip=new Rect(Mathf.Clamp(e.mousePosition.x+18,8,r.width-256),Mathf.Clamp(e.mousePosition.y+22,8,r.height-65),248,58);
            Panel(tip);GUI.Label(new Rect(tip.x+12,tip.y+6,225,23),CityCoordinate(q)+" · "+label+" / "+CityTerrain(t),cityTip);GUI.Label(new Rect(tip.x+12,tip.y+31,225,21),detail,cityTiny);
        }
        if(over&&e.type==EventType.ScrollWheel){CityZoom(cityZoom*(e.delta.y>0?1/1.15f:1.15f),e.mousePosition);e.Use();}
        if(over&&e.type==EventType.MouseDown&&(e.button==0||e.button==2)){cityPointerDown=true;cityPanned=e.button==2;cityPress=cityLastMouse=e.mousePosition;GUI.FocusControl(null);}
        if(cityPointerDown&&e.type==EventType.MouseDrag){if(Vector2.Distance(e.mousePosition,cityPress)>5)cityPanned=true;if(cityPanned){cityPan+=e.mousePosition-cityLastMouse;CityClampPan();}cityLastMouse=e.mousePosition;e.Use();}
        if(cityPointerDown&&e.rawType==EventType.MouseUp){
            if(!cityPanned&&e.button==0&&over&&CityInside(cityHover)){
                SelectCityCell(cityHover);GUI.FocusControl(null);
                bool twice=cityHover==cityLastClickTile&&Time.realtimeSinceStartup-cityLastClickAt<=.5f&&Vector2.Distance(e.mousePosition,cityLastClickPosition)<=6;
                cityLastClickTile=cityHover;cityLastClickPosition=e.mousePosition;cityLastClickAt=twice?-10:Time.realtimeSinceStartup;
                if(twice)TryCityWalk(cityHover);
            }else cityLastClickAt=-10;
            cityPointerDown=false;if(e.type==EventType.MouseUp)e.Use();
        }
        cityClipLines=false;GUI.EndGroup();
    }
    void DrawCityHero(Vector2 p,float tw){
        float s=Mathf.Clamp(tw/46,.75f,1.35f);Color c=new Color(.97f,.83f,.46f);
        CityDiamond(p,Mathf.Max(8,tw*.58f),Mathf.Max(4,tw*.29f),c,2);
        // Shared paper-puppet art keeps the player consistent with portraits and dialogue.
        Tex(new Rect(p.x-23*s,p.y-69*s,46*s,46*s),"medallion");
        DrawPaperPortrait(new Rect(p.x-14*s,p.y-65*s,28*s,36*s),Hero,true,false);
        Line(p+new Vector2(0,-16)*s,p+new Vector2(0,-3)*s,c,2);
    }
    Color CityNodeColor(string kind)=>kind=="town"||kind=="market"||kind=="workshop"||kind=="granary"?new Color(.86f,.63f,.31f):kind=="city"||kind=="government"||kind=="palace"||kind=="gate"?new Color(.76f,.38f,.29f):kind=="village"||kind=="clinic"||kind=="garden"?new Color(.49f,.71f,.48f):new Color(.48f,.69f,.68f);
    void DrawCityOverview(){
        Rect r=CityOverviewRect;Panel(r);int size=City["size"].I;float unit=Mathf.Min((r.width-28)/(2*size),(r.height-60)/size);
        GUI.Label(new Rect(r.x+12,r.y+4,r.width-24,19),"街巷总览  /  点击定位",cityTiny);
        var center=new Vector2(r.center.x,r.y+30+unit*.5f);
        for(int row=0;row<size;row++)for(int col=0;col<size;col++){
            char t=CityTile(row,col);var p=CityXY(new Vector2Int(row,col),unit,unit*.5f,center);
            Color c=t=='~'?new Color(.27f,.50f,.51f):t=='^'||t=='t'?new Color(.28f,.40f,.30f):t=='+'||t=='='?new Color(.76f,.71f,.53f):new Color(.47f,.54f,.38f);
            CityDiamond(p,unit*.85f,unit*.42f,c,2.8f);
        }
        foreach(var pair in cityNodes){var node=pair.Value;var p=CityXY(CityLotCorner(node),unit,unit*.5f,center)+new Vector2(0,(CityLotSize(node)-1)*unit*.5f);int extent=CityLotSize(node);CityDiamond(p,unit*extent*.85f,unit*extent*.42f,CityNodeColor(node["kind"].S),1.5f);}
        if(chosenRow>=0){var p=CityXY(new Vector2Int(chosenRow,chosenCol),unit,unit*.5f,center);Outline(new Rect(p.x-4,p.y-3,8,6),brass,1);}
        if(CityLocal){var p=CityHeroOverview(unit,center);Fill(new Rect(p.x-2.5f,p.y-2.5f,5,5),paperText);}
        GUI.Label(new Rect(r.x+8,r.yMax-23,80,18),"北 ↗",cityTiny);
        if(GUI.Button(new Rect(r.xMax-84,r.yMax-25,74,21),"收览全图",chip)){cityZoom=1;cityPan=Vector2.zero;}
        var hit=new Rect(r.x+8,r.y+25,r.width-16,r.height-52);
        if(GUI.Button(hit,GUIContent.none,GUIStyle.none)&&!cityLoading){var point=CityPick(Event.current.mousePosition,unit,unit*.5f,center);if(CityInside(point))CityFocus(point);}
    }
    void DrawCityDestination(){
        var at=new Vector2Int(chosenRow,chosenCol);if(!CityInside(at))return;
        Rect r=CityDestinationRect;Panel(r);Fill(new Rect(r.x,r.y,3,r.height),brass);
        var node=cityLots.TryGetValue(at,out var n)?n:new J();string name=CityCoordinate(at)+" · "+(node.Has?node["name"].S:CityAreaName(at));
        bool reachable=cityCosts.TryGetValue(at,out int cost),affordable=reachable&&cost<=Hero["points"].I;
        GUI.Label(new Rect(r.x+16,r.y+7,224,24),name,sub);
        string info=reachable?(cost==0?"已在此处":cost+" 行动力  /  "+(CityPreview(at).Count-1)+" 格"):("无法抵达 · "+CityTerrain(CityTile(at.x,at.y)));
        GUI.Label(new Rect(r.x+16,r.y+32,224,21),info,small);
        string hint=!CityLocal?"远方预览":W["battle_active"].B?"切磋中，先收招":!reachable?"不可通行":cost==0?"已在此处":!affordable?"行动力不足":!Paused?"请先暂停":"双击前往";
        if(cityWalking){if(GUI.Button(new Rect(r.xMax-128,r.y+20,116,25),"停止行走",chip))StopCityWalk();}
        else GUI.Label(new Rect(r.xMax-128,r.y+20,116,25),hint,centerSmall);
    }
    void DrawNodeCard(){
        Rect c=PlaceCard;Panel(c);GUILayout.BeginArea(new Rect(c.x+12,c.y+10,c.width-24,c.height-18));
        if(!City.Has||cityLoading||City["place"].I!=SelectedPlace){GUILayout.Label("正在展开城中街巷…",small);GUILayout.EndArea();return;}
        GUILayout.Label(City["name"].S+" · 城中",heading);GUILayout.Label(City["country_name"].S+" · "+City["population"].S+"人在世",small);
        bool local=CityLocal;
        GUILayout.Label(local?Hero["name"].S+" · "+CityCoordinate(new Vector2Int(Hero["row"].I,Hero["col"].I))+" · "+Hero["position_name"].S:"远方地图预览 · 浏览不消耗回合",small);
        GUI.enabled=!cityLoading;
        GUILayout.BeginHorizontal();if(Btn("主角所在",Hero["created"].B))LoadCity(Hero["place"].I);if(Btn("下一地区")){var all=W["locations"].A;int idx=all.FindIndex(p=>p["id"].I==SelectedPlace);LoadCity(all[(idx+1)%all.Count]["id"].I);}GUILayout.EndHorizontal();
        var kinds=new[]{"全部聚落"}.Concat(CitySites.Select(n=>n["label"].S).Distinct()).ToArray();
        if(!kinds.Contains(cityKind))cityKind="全部聚落";
        cityKind=Cycle(cityKind,kinds);
        mainScroll=GUILayout.BeginScrollView(mainScroll);
        var at=new Vector2Int(chosenRow,chosenCol);var node=cityLots.TryGetValue(at,out var found)?found:new J();
        if(CityInside(at)){
            char t=CityTile(at.x,at.y);GUILayout.Label(node["name"].Has?node["name"].S:CityAreaName(at)+"（"+(chosenRow+1)+"，"+(chosenCol+1)+"）",sub);
            T("选中 "+CityCoordinate(at)+" · 第"+(at.x+1)+"行，第"+(at.y+1)+"列",small);
            if(node["description"].Has)T(node["description"].S,small);
            if(node.Has)T(CityLotLabel(node),small);
            if(node["home_residents"].Has){
                T("常住 "+node["home_residents"].A.Count+"人 · 当前在场 "+node["residents"].A.Count+"人",sub);
                T(node["role"].S+" · 登记从业 "+node["workers"].A.Count+"人",small);
                if(node["production"]["actions"].I>0)T("本月谋生 "+node["production"]["actions"].S+"次 · 实付收运 "+Num(node["production"]["freight"])+"银",small);
            }
            var area=CityAreaAt(at);if(area.Has)T(CityTerrain(t)+" · 高程 "+area["elevation"].S+" · "+(area["passable"].B?"每格 "+area["move_cost"].S+" 行动力":"不可通行"),small);
            var path=CityPreview(at);int cost=cityCosts.TryGetValue(at,out int amount)?amount:0;
            T(path.Count>0?Math.Max(0,path.Count-1)+"格 · "+cost+"行动力 · 道路优先":"不可抵达 · "+(CityBlocked(t)?CityTerrain(t)+"阻断通行":"没有连通步道"),small);
            if(local){T("双击后沿路线逐格行走；WASD / 方向键可接管，Esc 停止。",small);if(cost>Hero["points"].I)T("本月行动力不足，可在下方月盘结束本月。",small);}
            else if(Btn("以此为游览起点",path.Count>0&&!CityBlocked(t))){originRow=chosenRow;originCol=chosenCol;cityPathKey="";EnsureCityRoutes();}
            if(local&&!Paused)T("先暂停世界推演或收招，再安排行旅。",small);
            string kind=node["kind"].S;
            if((CityHasService(node,"market")||CityHasService(node,"workshop"))&&Btn("交易货栈 ›")){SwitchBookPerson(-1);inventoryMode="货栈";OpenCharacterPage(2);}
            if((CityHasService(node,"training")||CityHasService(node,"temple")||CityHasService(node,"faction"))&&Btn("我的战斗与武学 ›")){SwitchBookPerson(-1);OpenCharacterPage(3);}
            if(CityHasService(node,"government")&&Btn("查看所属官府 ›"))OpenInstitution(true,City["country"].I);
            if(node["faction"].Has&&Btn("查看门派 · 入门与传艺 ›"))OpenInstitution(false,node["faction"].I);
            if((CityHasService(node,"gate")||CityHasService(node,"dock"))&&Btn("返回大地图 ›"))Go(0);
            if(node.Has){
                // The same anchored texture doubles as a legible facility portrait.
                Rect art=GUILayoutUtility.GetRect(100,112,GUILayout.ExpandWidth(true));
                Fill(art,new Color(.08f,.14f,.12f,.8f));Outline(art,new Color(.55f,.57f,.40f,.28f));
                var settlement=CitySettlementArt(node["settlement_asset"].S);if(settlement!=null)GUI.DrawTexture(art,settlement,ScaleMode.ScaleToFit);
                if(node["home_residents"].Has){
                    T("常住居民",sub);
                    foreach(var resident in node["home_residents"].A)if(GUILayout.Button(resident["name"].S+" · "+resident["job"].S+" · 常住",chip))TalkToPerson(resident["id"].I);
                }
                T("当前在场 · "+node["residents"].A.Count+"人",sub);foreach(var resident in node["residents"].A)if(GUILayout.Button(resident["name"].S+" · "+resident["job"].S,chip))TalkToPerson(resident["id"].I);
            }
        }else{T("单击查看，双击前往；WASD 移步。",sub);T("金线标示最低行动力路径；绿色边框显示本月可达范围。",small);}
        GUILayout.Space(10);T("普通聚落  /  "+CitySites.Count(n=>n["kind"].S!="sect")+"处",sub);
        foreach(var n in CitySites.Where(n=>n["kind"].S!="sect"&&(cityKind=="全部聚落"||n["label"].S==cityKind)))if(GUILayout.Button(n["glyph"].S+"  "+n["name"].S+" · 常住"+n["home_residents"].A.Count+" / 在场"+n["residents"].A.Count,n["row"].I==chosenRow&&n["col"].I==chosenCol?selected:chip))SelectCityNode(n,true);
        int sectCount=CitySites.Count(n=>n["kind"].S=="sect");
        if(sectCount>0){T("门派驻地  /  "+sectCount+"处",sub);foreach(var n in CitySites.Where(n=>n["kind"].S=="sect"&&(cityKind=="全部聚落"||n["label"].S==cityKind)))if(GUILayout.Button(n["glyph"].S+"  "+n["name"].S+" · "+n["residents"].A.Count+"人",n["row"].I==chosenRow&&n["col"].I==chosenCol?selected:chip))SelectCityNode(n,true);}
        if(local){if(Btn("定位地区出口 ›"))SelectCityNode(City["nodes"].A.First(n=>n["id"].S==City["entrance"].S),true);DrawTravel();}GUILayout.EndScrollView();GUI.enabled=true;GUILayout.EndArea();
    }
}
}
