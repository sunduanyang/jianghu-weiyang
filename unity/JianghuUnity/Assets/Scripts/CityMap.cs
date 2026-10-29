using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jianghu {
// Shared state for the city stage. The 舆图 and 长卷 views (CityAtlas.cs, CityScroll.cs)
// draw on top of the same /api/city grid; routes, walk costs and saves stay on that grid
// and are owned by the backend. Nothing here mutates the simulation.
public partial class JianghuApp {
    readonly Dictionary<Vector2Int,int> cityCosts=new Dictionary<Vector2Int,int>();
    readonly Dictionary<Vector2Int,Vector2Int> cityPrevious=new Dictionary<Vector2Int,Vector2Int>();
    readonly List<Vector2Int> cityPreviewPath=new List<Vector2Int>();
    // Portrait, age and rank for the people listed by the atlas and scroll rosters.
    readonly Dictionary<int,J> cityPersonViews=new Dictionary<int,J>();
    J cityIndexedView;
    string cityIndexedLayout="";
    string[] cityRows=new string[0];
    int citySize,cityPreviewRevision=-1;
    Vector2Int cityPreviewTarget;
    Rect cityLineBounds;
    string cityPathKey="";
    bool cityLoading,cityBattleEnding;
    int cityRequest;
    GUIStyle cityTiny,cityTitle;
    // Only the floating controls follow the roster. The camera always covers
    // the physical client, including space outside the 1680x960 UI safe area.
    Rect CityHudRect=>new Rect(rosterOpen?284:42,146,rosterOpen?1118:1360,604);
    Rect CityHeaderRect=>new Rect(CityHudRect.center.x-300,64,600,50);
    Rect CityToolbarRect=>new Rect(CityHudRect.x,114,CityHudRect.width,32);
    Rect CityNoticeRect=>new Rect(CityHudRect.x+12,CityHudRect.y+8,CityHudRect.width-24,46);
    static readonly Vector2Int[] CitySteps={new Vector2Int(-1,0),new Vector2Int(0,1),new Vector2Int(1,0),new Vector2Int(0,-1)};
    Rect CityViewport {
        get {
            float scale=Mathf.Min(Screen.width/1680f,Screen.height/960f);
            float width=Screen.width/scale,height=Screen.height/scale;
            return new Rect((1680-width)*.5f,(960-height)*.5f,width,height);
        }
    }
    bool CityLocal=>Hero["created"].B&&City["place"].I==Hero["place"].I;

    string CityMovementBlockReason=>!Hero["created"].B?"请先创建主角":!CityLocal?"远方地图预览，请先返回主角所在城镇":W["battle_active"].B?"切磋尚未结束，收招后才能在城中行走":W["autoplay"].B||W["job"]["remaining"].I>0?"世界正在推演，请先暂停再行走":Hero["points"].I<=0?"本月行动力已用尽，请结束本月":"";
    bool CityMovementAllowed=>CurrentStage==Stage.City&&!ViewOpen&&City.Has&&CityMovementBlockReason.Length==0&&!advanceOpen&&!choiceOpen;
    // The atlas and scroll walk by destination; Esc stops a walk in progress.
    void UpdateCityMovement(){
        if(!CityMovementAllowed||cityLoading){StopCityWalk();return;}
        if(cityWalking&&Application.isFocused&&Input.GetKeyDown(KeyCode.Escape))StopCityWalk();
    }
    bool TryCityWalk(Vector2Int target){
        if(!CityMovementAllowed){Client.Status=CityMovementBlockReason;return false;}
        if(cityLoading||Client.Busy&&!cityWalking||!CityInside(target))return false;
        EnsureCityRoutes();string name=CityPlaceLabel(target);
        if(!cityCosts.TryGetValue(target,out int cost)){Client.Status=name+" 不可通行，请沿街巷或桥梁绕行";return false;}
        if(cost<=0){Client.Status="已在 "+name;return false;}
        if(cost>Hero["points"].I){Client.Status="前往 "+name+" 需要 "+cost+" 行动力，本月不足";return false;}
        BeginCityWalk(target);return true;
    }
    internal void ResumeCityBattle(){OpenBattle();}
    System.Collections.IEnumerator EndCityBattle(){
        if(cityBattleEnding||Client.Busy)yield break;
        cityBattleEnding=true;
        try{
            J current=null;yield return Client.Request("/api/martial",null,j=>current=j);
            if(current==null)yield break;
            Martial=current;var battle=current["battle"];
            if(battle["status"].S=="active")yield return Client.Action("/api/battle",J.O("battle_id",battle["id"],"revision",battle["revision"],"action","surrender"),j=>Martial=j);
            else yield return Client.Refresh();
            if(!W["battle_active"].B){Client.Status="已收招，可在舆图上选择去处继续行旅";if(CurrentStage==Stage.City)LoadCity(Hero["place"].I);}
        }finally{cityBattleEnding=false;}
    }

    public void LoadCity(int place){
        StopCityWalk();
        StartCoroutine(LoadCityRoutine(place));
    }
    void InitCityStyles(){
        if(cityTiny!=null)return;
        cityTiny=new GUIStyle(centerSmall){fontSize=11};TextColor(cityTiny,new Color(.85f,.86f,.72f));
        cityTitle=new GUIStyle(centerTitle){fontSize=26};
    }
    static bool CityBlocked(char t)=>t=='~'||t=='^'||t=='#';
    static int CityStepCost(char t)=>t=='+'||t=='='?1:t=='t'?3:2;
    char CityTile(int row,int col)=>cityRows[row][col];
    bool CityInside(Vector2Int p)=>p.x>=0&&p.y>=0&&p.x<citySize&&p.y<citySize;
    void EnsureCityIndex(){if(!ReferenceEquals(cityIndexedView,City))IndexCity();}
    void IndexCity(){
        string key=City["layout_id"].S;
        if(key!=cityIndexedLayout||cityRows.Length!=City["size"].I){
            citySize=City["size"].I;cityRows=City["rows"].A.Select(row=>row.S).ToArray();
            cityPathKey="";cityIndexedLayout=key;cityIndexBuilds++;
        }
        cityPersonViews.Clear();foreach(var person in W["npcs"].A)cityPersonViews[person["id"].I]=person;
        cityIndexedView=City;
    }
    // Routes start where the hero stands; a remote preview measures from the gate.
    Vector2Int CityOrigin(){
        if(CityLocal)return HeroTile;
        var gate=CityGateNode;return new Vector2Int(gate["row"].I,gate["col"].I);
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

    void DrawCityScreen(Rect screen){
        InitCityStyles();Fill(screen,new Color(.075f,.105f,.085f));
        if(cityLoading&&!string.IsNullOrEmpty(Client.Error))cityLoading=false;
        if(!City.Has){GUI.Label(new Rect(480,400,720,50),"正在展开城中舆图…",centerTitle);return;}
        if(!CityAtlasReady){GUI.Label(new Rect(480,400,720,50),"此地图缺少聚落资料，无法展开舆图",centerTitle);return;}
        EnsureCityRoutes();
        DrawCityAtlasStage();
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
        DrawCityToolbar();DrawCityMovementNotice();
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
        left+=DrawCityModeTabs(r.x+14,y);
        DrawCityAtlasTools(left+14,y,r);GUI.enabled=true;
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
    // Clip ink strokes to the sheet before drawing so fast lines never escape it.
    static bool CityClipLine(ref Vector2 a,ref Vector2 b,Rect bounds){
        var delta=b-a;float near=0,far=1;
        if(!CityClipEdge(-delta.x,a.x-bounds.xMin,ref near,ref far)||!CityClipEdge(delta.x,bounds.xMax-a.x,ref near,ref far)||!CityClipEdge(-delta.y,a.y-bounds.yMin,ref near,ref far)||!CityClipEdge(delta.y,bounds.yMax-a.y,ref near,ref far))return false;
        b=a+delta*far;a+=delta*near;return true;
    }
    static bool CityClipEdge(float direction,float distance,ref float near,ref float far){
        if(Mathf.Abs(direction)<.00001f)return distance>=0;float value=distance/direction;
        if(direction<0){if(value>far)return false;near=Mathf.Max(near,value);}else{if(value<near)return false;far=Mathf.Min(far,value);}return true;
    }
}
}
