using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jianghu {
// 城中的三种看法：
//   舆图 — 本地区的聚落、出入口与相邻城镇画成节点，驿道、步道与航路连线，地形退为底色；
//   长卷 — 一处聚落的营生设施沿街展开，在场之人站在各处门前（CityScroll.cs）；
//   地格 — 原 30×30 行走图，保留给需要逐格查看的人。
// 三者读同一份 /api/city 数据，行走与远行仍走原有的寻路和 /api/hero，不另立规则。
public enum CityMode { Atlas, Scroll, Tiles }

public partial class JianghuApp {
    // Smoke runs predate the atlas and photograph the tile board; they keep it as their default.
    CityMode cityMode=CityDefaultMode();
    static CityMode CityDefaultMode(){
        foreach(var arg in Environment.GetCommandLineArgs())
            if(arg.StartsWith("-")&&arg.IndexOf("smoke",StringComparison.OrdinalIgnoreCase)>=0&&arg!="-cityAtlasSmokeDir")return CityMode.Tiles;
        return CityMode.Atlas;
    }
    // Settlements carry the footprints the atlas needs; very old layouts without them stay on tiles.
    bool CityAtlasReady=>City.Has&&City["settlements"].A.Count>0&&City["rows"].A.Count==City["size"].I;
    CityMode CityModeShown=>cityMode!=CityMode.Tiles&&!CityAtlasReady?CityMode.Tiles:cityMode;
    Rect CitySheetRect=>new Rect(CityHudRect.x,CityHudRect.y+6,CityHudRect.width,CityHudRect.height-10);

    string cityAtlasPick="",cityAtlasHover="";
    float cityAtlasLastClickAt=-10;string cityAtlasLastClick="";
    Texture2D cityAtlasWash;string cityAtlasWashKey="";
    readonly Dictionary<string,Texture2D> scrollArt=new Dictionary<string,Texture2D>();
    readonly Dictionary<string,Rect> cityAtlasHits=new Dictionary<string,Rect>();
    GUIStyle inkLabel,inkBold,inkSmall,atlasTitle,atlasHalo,atlasNote,atlasLegend,inkBoldOn,inkSmallOn,atlasTitleVertical,atlasSeal;
    static readonly Color AtlasInkColor=new Color(.17f,.13f,.10f),AtlasRoad=new Color(.62f,.40f,.20f,.85f),AtlasPost=new Color(.60f,.16f,.10f,.92f),AtlasSea=new Color(.18f,.38f,.44f,.92f),AtlasGold=new Color(.86f,.60f,.18f);

    // Hand-painted replacements in Resources/Scroll/Art/ (same file name) win over the baked texture;
    // bake_scroll.py never writes there. See unity/ART_PROMPTS.md and unity/import_scroll_art.py.
    Texture2D ScrollArt(string name){if(!scrollArt.TryGetValue(name,out var t)){t=Resources.Load<Texture2D>("Scroll/Art/"+name);if(t==null)t=Resources.Load<Texture2D>("Scroll/"+name);scrollArt[name]=t;}return t;}
    void InitAtlasStyles(){
        if(inkLabel!=null)return;
        inkLabel=new GUIStyle(mapLabel){fontSize=13,alignment=TextAnchor.MiddleCenter,wordWrap=false};TextColor(inkLabel,AtlasInkColor);
        inkBold=new GUIStyle(inkLabel){font=fontBold,fontSize=15};TextColor(inkBold,AtlasInkColor);
        inkSmall=new GUIStyle(inkLabel){fontSize=11};TextColor(inkSmall,new Color(.33f,.26f,.19f));
        atlasTitle=new GUIStyle(inkBold){fontSize=24,alignment=TextAnchor.UpperLeft};TextColor(atlasTitle,AtlasInkColor);
        atlasHalo=new GUIStyle(inkLabel);TextColor(atlasHalo,new Color(.92f,.86f,.70f,.85f));
        atlasTitleVertical=new GUIStyle(atlasTitle){fontSize=22,alignment=TextAnchor.UpperCenter};
        atlasSeal=new GUIStyle(inkBold){fontSize=13};TextColor(atlasSeal,paperText);
        atlasNote=new GUIStyle(inkSmall){wordWrap=true,alignment=TextAnchor.UpperLeft,fontSize=12};
        atlasLegend=new GUIStyle(inkSmall){alignment=TextAnchor.MiddleLeft};
        inkBoldOn=new GUIStyle(inkBold);TextColor(inkBoldOn,paperText);
        inkSmallOn=new GUIStyle(inkSmall);TextColor(inkSmallOn,new Color(.95f,.88f,.72f));
    }

    // ---------------------------------------------------------------- data helpers
    List<J> CitySettlementsList=>City["settlements"].A;
    J CitySettlement(string id)=>CitySettlementsList.FirstOrDefault(s=>s["id"].S==id)??new J();
    J CityGateNode=>City["nodes"].A.FirstOrDefault(n=>n["id"].S==City["entrance"].S)??new J();
    J CityDockNode=>City["nodes"].A.FirstOrDefault(n=>n["kind"].S=="dock")??new J();
    static readonly string[] FacilityOrder={"gate","palace","government","market","inn","dock","clinic","workshop","training","school","temple","faction","residence","granary","garden"};
    static int FacilityRank(string kind){int i=Array.IndexOf(FacilityOrder,kind);return i<0?FacilityOrder.Length:i;}
    List<J> CityFacilities(string site)=>City["nodes"].A.Where(n=>n["settlement_id"].S==site).OrderBy(n=>FacilityRank(n["kind"].S)).ToList();
    static int SiteSize(J site)=>Mathf.Max(1,site["footprint"]["size"].Has?site["footprint"]["size"].I:1);
    static Vector2Int SiteCorner(J site)=>site["footprint"].Has?new Vector2Int(site["footprint"]["row"].I,site["footprint"]["col"].I):new Vector2Int(site["row"].I,site["col"].I);
    static Vector2 SiteCenter(J site){int size=SiteSize(site);var c=SiteCorner(site);return new Vector2(c.x+(size-1)*.5f,c.y+(size-1)*.5f);}
    static bool SiteContains(J site,Vector2Int p){int size=SiteSize(site);var c=SiteCorner(site);return p.x>=c.x&&p.y>=c.y&&p.x<c.x+size&&p.y<c.y+size;}
    static Vector2Int SiteTarget(J site)=>new Vector2Int(site["row"].I,site["col"].I);
    Vector2Int HeroTile=>new Vector2Int(Hero["row"].I,Hero["col"].I);
    // Where the protagonist stands, as an atlas key: "s:<settlement>", "gate", or "" when between places.
    string HeroAtlasKey{get{
        if(!CityLocal)return "";
        var gate=CityGateNode;if(gate.Has&&gate["row"].I==HeroTile.x&&gate["col"].I==HeroTile.y)return "gate";
        foreach(var site in CitySettlementsList)if(SiteContains(site,HeroTile))return "s:"+site["id"].S;
        return "";
    }}
    // Tile-space position of the hero (row, col), following the step animation.
    Vector2 HeroTileFloat=>cityStepAnimating?Vector2.Lerp(new Vector2(cityStepFrom.x,cityStepFrom.y),new Vector2(cityStepTo.x,cityStepTo.y),cityStepProgress):new Vector2(Hero["row"].I,Hero["col"].I);
    int CityWalkCost(Vector2Int target){EnsureCityRoutes();return cityCosts.TryGetValue(target,out int cost)?cost:-1;}
    J CityConnection(int place){
        foreach(var c in Hero["connections"].A)if(c["place"].I==place&&CityLocal)return c;
        foreach(var c in City["connections"].A)if(c["place"].I==place){
            int points=8+Mathf.CeilToInt((float)c["km"].N/120f)+(c["sea"].B?4:0);
            double silver=Math.Round(1+c["km"].N/250+(c["sea"].B?1.5:0),2);
            return J.O("place",place,"name",Place(place)["name"].S,"km",c["km"],"sea",c["sea"],"points",points,"silver",silver,"enabled",false,"reason","远方地图预览");
        }
        return new J();
    }
    List<J> CityExits=>City["connections"].A.Select(c=>CityConnection(c["place"].I)).Where(c=>c.Has).ToList();
    void CityAtlasReset(){
        string key=HeroAtlasKey;
        cityAtlasPick=key.Length>0?key:CitySettlementsList.Count>0?"s:"+CitySettlementsList[0]["id"].S:"";
        cityScrollSite=key.StartsWith("s:")?key.Substring(2):CitySettlementsList.Count>0?CitySettlementsList[0]["id"].S:"";
        cityScrollPick="";cityScrollX=0;cityAtlasHover="";
    }
    void SetCityMode(CityMode mode){
        if(mode==CityMode.Scroll&&!CitySettlement(cityScrollSite).Has)CityAtlasReset();
        if(mode==CityMode.Scroll&&cityAtlasPick.StartsWith("s:")&&cityAtlasPick.Substring(2)!=cityScrollSite)OpenCityScroll(cityAtlasPick.Substring(2));
        cityMode=mode;cityPointerDown=false;cityScrollDragging=false;
    }

    // ---------------------------------------------------------------- terrain wash
    // One soft texture per layout: water as a 石青 wash with scale waves and an ink shore,
    // fields, groves and rough ground as faint tints. Settlements and roads are drawn on top.
    static float AtlasSample(float[,] grid,int n,float r,float c){
        r=Mathf.Clamp(r,0,n-1);c=Mathf.Clamp(c,0,n-1);int r0=(int)r,c0=(int)c,r1=Mathf.Min(n-1,r0+1),c1=Mathf.Min(n-1,c0+1);float fr=r-r0,fc=c-c0;
        return Mathf.Lerp(Mathf.Lerp(grid[r0,c0],grid[r0,c1],fc),Mathf.Lerp(grid[r1,c0],grid[r1,c1],fc),fr);
    }
    static float[,] AtlasSoften(float[,] g,int n){
        var o=new float[n,n];
        for(int r=0;r<n;r++)for(int c=0;c<n;c++){float sum=0,w=0;for(int dr=-1;dr<=1;dr++)for(int dc=-1;dc<=1;dc++){int rr=r+dr,cc=c+dc;if(rr<0||cc<0||rr>=n||cc>=n)continue;float k=dr==0&&dc==0?4:dr==0||dc==0?2:1;sum+=g[rr,cc]*k;w+=k;}o[r,c]=sum/w;}
        return o;
    }
    void EnsureCityAtlasWash(){
        string key=City["layout_id"].S+"/"+City["size"].S+"/"+City["place"].S;
        if(cityAtlasWash!=null&&cityAtlasWashKey==key)return;
        int n=City["size"].I;var rows=City["rows"].A.Select(r=>r.S).ToArray();
        if(n<=0||rows.Length!=n||rows.Any(r=>r.Length<n))return;
        var water=new float[n,n];var field=new float[n,n];var rough=new float[n,n];
        for(int r=0;r<n;r++)for(int c=0;c<n;c++){char t=rows[r][c];water[r,c]=t=='~'||t=='s'||t=='='?1:0;field[r,c]=t==','?1:0;rough[r,c]=t=='#'?1:0;}
        field=AtlasSoften(field,n);rough=AtlasSoften(rough,n);
        const int px=16;int size=n*px;
        if(cityAtlasWash==null||cityAtlasWash.width!=size){if(cityAtlasWash!=null)Destroy(cityAtlasWash);cityAtlasWash=new Texture2D(size,size,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};}
        var pixels=new Color32[size*size];
        // Colours follow the world atlas: 石青 water, ochre ink, aged-silk pigment pooling at the shore.
        Color sea=new Color(.65f,.71f,.60f),wave=new Color(.20f,.32f,.31f),shore=new Color(.17f,.14f,.11f),pool=new Color(.67f,.59f,.44f),farm=new Color(.60f,.46f,.24f),stone=new Color(.50f,.38f,.22f);
        for(int y=0;y<size;y++)for(int x=0;x<size;x++){
            float r=(y+.5f)/px-.5f,c=(x+.5f)/px-.5f;
            float w=AtlasSample(water,n,r,c),f=AtlasSample(field,n,r,c),h=AtlasSample(rough,n,r,c);
            int hash=((x*73856093)^(y*19349663))&0x7fffffff;
            Color o=new Color(0,0,0,0);
            void Over(Color col,float a){if(a<=0)return;float na=a+o.a*(1-a);if(na<=0)return;o=new Color((col.r*a+o.r*o.a*(1-a))/na,(col.g*a+o.g*o.a*(1-a))/na,(col.b*a+o.b*o.a*(1-a))/na,na);}
            // 田: fields as a faint ruled hatch, the way plots are drawn on county maps
            float fm=Mathf.SmoothStep(0,1,(f-.4f)/.35f);
            if(fm>0){Over(farm,fm*.06f);if(x%6==0||y%6==0)Over(farm,fm*.22f);}
            // rough ground: a scatter of ink stipple, as deserts are on the atlas
            float hm=Mathf.SmoothStep(0,1,(h-.4f)/.35f);if(hm>0&&hash%29==0)Over(stone,hm*.55f);
            // pigment pools just inside the land edge
            if(w>.25f&&w<.5f)Over(pool,(w-.25f)/.25f*.22f);
            float inside=Mathf.SmoothStep(0,1,(w-.46f)/.08f);
            if(inside>0){
                Over(sea,inside*.92f);
                // 鱼鳞水纹: rows of upward arcs, every other row offset by half a scale; a quiet band hugs the shore
                int row=y/5;float lx=(x+(row%2)*6)%12-6,ly=y%5-6;float d=Mathf.Sqrt(lx*lx+ly*ly);
                float deep=Mathf.Clamp01((w-.68f)/.12f);
                if(deep>0&&Mathf.Abs(d-6)<.8f)Over(wave,.42f*deep);
            }
            float band=1-Mathf.Abs(w-.5f)/.045f;if(band>0)Over(shore,Mathf.Clamp01(band)*.92f);
            pixels[(size-1-y)*size+x]=o;
        }
        cityAtlasWash.SetPixels32(pixels);cityAtlasWash.Apply(false);cityAtlasWashKey=key;
    }

    // ---------------------------------------------------------------- geometry
    void CityAtlasGeometry(Rect sheet,out Rect map,out float unit){
        int n=Mathf.Max(1,City["size"].I);
        float side=Mathf.Min(sheet.width-300,sheet.height-88);
        unit=side/n;map=new Rect(sheet.center.x-side/2,sheet.y+40,side,side);
    }
    static Vector2 AtlasPoint(Rect map,float unit,float row,float col)=>new Vector2(map.x+(col+.5f)*unit,map.y+(row+.5f)*unit);
    // A road leaving the region ends in the side margin toward the town it reaches, at the height
    // its compass bearing crosses that margin, so north-east roads leave high on the right.
    Vector2 AtlasExitPoint(Rect sheet,Rect map,int place){
        var here=Place(City["place"].I);var there=Place(place);
        var d=new Vector2((float)(there["x"].N-here["x"].N),(float)(there["y"].N-here["y"].N));
        if(d.sqrMagnitude<.0001f)d=Vector2.right;d.Normalize();
        float side=d.x<0?-1:1,reach=map.width/2+120;
        float y=map.center.y+(Mathf.Abs(d.x)>.05f?d.y/Mathf.Abs(d.x)*reach:Mathf.Sign(d.y)*map.height);
        return new Vector2(map.center.x+side*reach,Mathf.Clamp(y,sheet.y+176,map.yMax-26));
    }
    // Stair-stepped tile paths become a brushed road by corner cutting (Chaikin).
    static List<Vector2> AtlasSmooth(List<Vector2> pts,int passes=3){
        for(int k=0;k<passes&&pts.Count>2;k++){
            var o=new List<Vector2>{pts[0]};
            for(int i=0;i+1<pts.Count;i++){o.Add(Vector2.Lerp(pts[i],pts[i+1],.25f));o.Add(Vector2.Lerp(pts[i],pts[i+1],.75f));}
            o.Add(pts[pts.Count-1]);pts=o;
        }
        return pts;
    }
    List<Vector2> AtlasPath(J path,Rect map,float unit){
        var pts=new List<Vector2>();foreach(var step in path.A)pts.Add(AtlasPoint(map,unit,step[0].I,step[1].I));
        return AtlasSmooth(pts);
    }
    List<Vector2> AtlasPath(List<Vector2Int> path,Rect map,float unit){
        var pts=new List<Vector2>();foreach(var step in path)pts.Add(AtlasPoint(map,unit,step.x,step.y));
        return AtlasSmooth(pts);
    }
    void AtlasPolyline(List<Vector2> pts,Color c,float width){for(int i=0;i+1<pts.Count;i++)CityLine(pts[i],pts[i+1],c,width);}
    void AtlasDashed(List<Vector2> pts,Color c,float width,float dash,float gap){
        if(Event.current.type!=EventType.Repaint)return;float phase=0;
        for(int i=0;i+1<pts.Count;i++){
            Vector2 a=pts[i],b=pts[i+1];float length=Vector2.Distance(a,b);if(length<.01f)continue;Vector2 dir=(b-a)/length;float t=0;
            while(t<length){float cycle=phase%(dash+gap);bool on=cycle<dash;float step=Mathf.Max(.5f,Mathf.Min(length-t,on?dash-cycle:dash+gap-cycle));if(on)CityLine(a+dir*t,a+dir*(t+step),c,width);t+=step;phase+=step;}
        }
    }
    static Vector2 AtlasMidpoint(List<Vector2> pts){
        float total=0;for(int i=0;i+1<pts.Count;i++)total+=Vector2.Distance(pts[i],pts[i+1]);
        float half=total/2;for(int i=0;i+1<pts.Count;i++){float l=Vector2.Distance(pts[i],pts[i+1]);if(half<=l)return Vector2.Lerp(pts[i],pts[i+1],l>0?half/l:0);half-=l;}
        return pts.Count>0?pts[pts.Count-1]:Vector2.zero;
    }
    void AtlasTexture(Rect r,string name,Color? tint=null){var t=ScrollArt(name);if(t==null)return;var old=GUI.color;if(tint.HasValue)GUI.color=tint.Value;GUI.DrawTexture(r,t,ScaleMode.ScaleToFit);GUI.color=old;}
    void SilkSheet(Rect r,float scrollX=0){
        var silk=ScrollArt("silk");
        if(silk!=null)GUI.DrawTextureWithTexCoords(r,silk,new Rect(scrollX/silk.width,0,r.width/silk.width,r.height/silk.height));else Fill(r,new Color(.84f,.76f,.58f));
        Outline(r,new Color(.17f,.13f,.10f,.85f),2);Outline(new Rect(r.x+5,r.y+5,r.width-10,r.height-10),new Color(.17f,.13f,.10f,.35f),1);
    }

    // ---------------------------------------------------------------- 舆图
    void DrawCityAtlasStage(){
        InitMap();InitSlipText();InitAtlasStyles();
        if(CityModeShown==CityMode.Scroll)DrawCityScroll(CitySheetRect);else DrawCityAtlas(CitySheetRect);
    }
    void DrawCityAtlas(Rect sheet){
        EnsureCityAtlasWash();EnsureCityRoutes();
        CityAtlasGeometry(sheet,out var map,out float unit);
        var e=Event.current;bool repaint=e.type==EventType.Repaint;
        SilkSheet(sheet);
        // title cartouche, compass and legend sit in the margins beside the square map
        DrawAtlasCartouche(new Rect(sheet.x+26,sheet.y+18,52,0),City["name"].S+"舆图");
        GUI.Label(new Rect(sheet.x+92,sheet.y+20,map.x-sheet.x-122,70),City["note"].S,atlasNote);
        if(compassMark!=null)GUI.DrawTexture(new Rect(sheet.xMax-92,sheet.y+14,72,72),compassMark);
        DrawAtlasLegend(new Rect(sheet.xMax-150,sheet.y+96,130,66));
        if(repaint){
            if(cityAtlasWash!=null)GUI.DrawTexture(map,cityAtlasWash,ScaleMode.StretchToFill);
            // 计里画方: a faint ruled grid, one square to five 格
            int n=Mathf.Max(1,City["size"].I);
            for(int k=5;k<n;k+=5){float v=k*unit;CityLine(new Vector2(map.x+v,map.y),new Vector2(map.x+v,map.yMax),new Color(.43f,.31f,.17f,.11f),1);CityLine(new Vector2(map.x,map.y+v),new Vector2(map.xMax,map.y+v),new Color(.43f,.31f,.17f,.11f),1);}
            DrawAtlasRelief(map,unit);
            var aging=ScrollArt("aging");if(aging!=null)GUI.DrawTexture(sheet,aging,ScaleMode.StretchToFill);
            // ruled frame: a heavy outer line and a thin inner one, as on the atlas
            Outline(new Rect(map.x-7,map.y-7,map.width+14,map.height+14),new Color(.17f,.13f,.10f,.86f),3);
            Outline(map,new Color(.17f,.13f,.10f,.66f),1);
        }
        cityAtlasHits.Clear();
        var gate=CityGateNode;Vector2 gatePoint=gate.Has?AtlasPoint(map,unit,gate["row"].I,gate["col"].I):map.center;
        // 步道: from the gate to every settlement, the walking route the simulation itself uses
        foreach(var site in CitySettlementsList){
            var pts=AtlasPath(site["path"],map,unit);if(pts.Count<2)continue;
            AtlasPolyline(pts,new Color(.95f,.89f,.74f,.75f),5);AtlasPolyline(pts,AtlasRoad,2.2f);
        }
        if(City["road_name"].S.Length>0){var main=CitySettlementsList.FirstOrDefault(s=>s["kind"].S=="city")??CitySettlementsList.FirstOrDefault();if(main!=null){var pts=AtlasPath(main["path"],map,unit);if(pts.Count>3){var m=AtlasMidpoint(pts);HaloLabel(new Rect(m.x+8,m.y-10,90,20),City["road_name"].S,inkSmall,atlasHalo);}}}
        // 驿道 / 航路: from the gate (or the landing) to the margin
        var dock=CityDockNode;Vector2 dockPoint=dock.Has?AtlasPoint(map,unit,dock["row"].I,dock["col"].I):gatePoint;
        var exits=CityExits;var exitPoints=new Dictionary<int,Vector2>();
        foreach(var exit in exits){
            var p=AtlasExitPoint(sheet,map,exit["place"].I);
            // keep exit slips apart along the margin
            float top=sheet.y+176,bottom=sheet.yMax-40;
            for(int pass=0;pass<4;pass++)foreach(var other in exitPoints.Values)if(Mathf.Abs(other.x-p.x)<10&&Mathf.Abs(other.y-p.y)<76){
                float up=other.y-76,down=other.y+76;p.y=(p.y<other.y||down>bottom)&&up>=top?up:down;
            }
            p.y=Mathf.Clamp(p.y,top,bottom);
            exitPoints[exit["place"].I]=p;
            bool sea=exit["sea"].B;var from=sea?dockPoint:gatePoint;var bend=new Vector2(Mathf.Lerp(from.x,p.x,.5f),Mathf.Lerp(from.y,p.y,.15f));
            var pts=AtlasSmooth(new List<Vector2>{from,bend,p},4);
            string key="x:"+exit["place"].S;bool lit=cityAtlasPick==key||cityAtlasHover==key;
            AtlasDashed(pts,sea?AtlasSea:AtlasPost,lit?3f:2.2f,sea?9:6,sea?6:4);
        }
        // the hero's next walk, when a reachable place is picked
        var target=AtlasTarget(cityAtlasHover.Length>0?cityAtlasHover:cityAtlasPick);
        if(CityLocal&&target.HasValue&&!cityWalking){
            var path=CityPreview(target.Value);
            if(path.Count>1){var pts=AtlasPath(path,map,unit);int cost=CityWalkCost(target.Value);Color c=cost<=Hero["points"].I?AtlasGold:new Color(.80f,.30f,.18f);AtlasPolyline(pts,new Color(.15f,.10f,.06f,.55f),5);AtlasDashed(pts,c,2.6f,6,4);}
        }else if(cityWalking&&cityWalkRoute.Count>1){var pts=AtlasPath(cityWalkRoute.Skip(Math.Max(0,cityWalkIndex-1)).ToList(),map,unit);AtlasDashed(pts,AtlasGold,2.8f,6,4);}
        // nodes
        if(gate.Has)DrawAtlasNode("gate",gatePoint,22,null,gate["name"].S,"出入口",false);
        if(dock.Has&&dock["row"].I==gate["row"].I&&dock["col"].I==gate["col"].I){}   // a landing at the gate shares its mark
        else if(dock.Has&&!CitySettlementsList.Any(s=>SiteContains(s,new Vector2Int(dock["row"].I,dock["col"].I))))DrawAtlasNode("dock",dockPoint,24,SiteMark("port"),dock["name"].S,"舟埠",false);
        foreach(var site in CitySettlementsList.OrderBy(s=>s["row"].I)){
            var c=SiteCenter(site);var p=AtlasPoint(map,unit,c.x,c.y);string kind=site["kind"].S;
            // 府城 a vermilion double square, 镇 a ruled square, 村 a ruled circle, 山门 a peak with a gate
            float size=kind=="city"?30:kind=="sect"?32:kind=="town"?22:18;
            Texture2D mark=kind=="city"?capitalMark:kind=="town"?cityMark:kind=="sect"?SiteMark("mountain"):SiteMark("city");
            DrawAtlasNode("s:"+site["id"].S,p,size,mark,site["name"].S,site["glyph"].S+" · 在场"+site["residents"].A.Count,true);
        }
        foreach(var exit in exits)DrawAtlasExit(exit,exitPoints[exit["place"].I]);
        if(CityLocal)DrawAtlasHero(AtlasPoint(map,unit,HeroTileFloat.x,HeroTileFloat.y));
        HandleAtlasInput(sheet);
    }
    Vector2Int? AtlasTarget(string key){
        if(key=="gate"){var g=CityGateNode;if(g.Has)return new Vector2Int(g["row"].I,g["col"].I);}
        if(key=="dock"){var d=CityDockNode;if(d.Has)return new Vector2Int(d["row"].I,d["col"].I);}
        if(key.StartsWith("s:")){var s=CitySettlement(key.Substring(2));if(s.Has)return SiteTarget(s);}
        return null;
    }
    static int AtlasCount(List<J> rows,int n,int r,int c,char t){int count=0;for(int dr=-1;dr<=1;dr++)for(int dc=-1;dc<=1;dc++){int rr=r+dr,cc=c+dc;if(rr>=0&&cc>=0&&rr<n&&cc<rows[rr].S.Length&&rows[rr].S[cc]==t)count++;}return count;}
    // The title written top to bottom in a ruled cartouche with a vermilion seal, as Song maps title themselves.
    void DrawAtlasCartouche(Rect at,string title){
        var fit=atlasTitle.CalcSize(new GUIContent(Vertical(title)));
        Rect box=new Rect(at.x,at.y,Mathf.Max(at.width,fit.x+18),fit.y+40);
        Fill(box,new Color(.93f,.87f,.71f,.92f));Outline(box,new Color(.17f,.13f,.10f,.86f),2);Outline(new Rect(box.x+4,box.y+4,box.width-8,box.height-8),new Color(.17f,.13f,.10f,.45f),1);
        GUI.Label(new Rect(box.x,box.y+8,box.width,fit.y),Vertical(title),atlasTitleVertical);
        Rect sealBox=new Rect(box.center.x-11,box.yMax-30,22,22);Fill(sealBox,new Color(.62f,.18f,.13f,.92f));GUI.Label(sealBox,"图",atlasSeal);
    }
    void DrawAtlasRelief(Rect map,float unit){
        int n=City["size"].I;var rows=City["rows"].A;
        // 青绿 peaks for mountains (small ranges where the ground is solid mountain), lower peaks on
        // rough ground, and Song-map groves of little ink trees; drawn row by row so nearer ones overlap.
        for(int r=0;r<n;r++){string row=rows[r].S;for(int c=0;c<n&&c<row.Length;c++){
            char t=row[c];int hash=((r*73856093)^(c*19349663))&0x7fffffff;
            var p=AtlasPoint(map,unit,r,c)+new Vector2(((hash>>8)%7-3)*unit*.10f,((hash>>12)%5-2)*unit*.08f);
            if(t=='^'||t=='#'){
                bool dense=t=='^'&&AtlasCount(rows,n,r,c,'^')>=7;
                if(dense&&(r%3==0)&&(c%4==(r/3)%4)){float s=unit*3.6f;AtlasTexture(new Rect(p.x-s*.78f,p.y-s*.72f,s*1.56f,s),"range_"+(hash%3));}
                else if(!dense&&(t=='^'?(r+c*2)%3==0:(hash>>3)%4==0)){float s=unit*(t=='^'?2.3f:1.7f)*(.86f+(hash&7)*.04f);AtlasTexture(new Rect(p.x-s*.6f,p.y-s*.75f,s*1.2f,s),"peak_"+(hash%6),t=='#'?new Color(1,1,1,.7f):(Color?)null);}
            }else if(t=='t'&&(hash>>4)%6==0){
                float s=unit*1.7f;AtlasTexture(new Rect(p.x-s/2,p.y-s*.7f,s,s*.78f),"grove_"+(hash%3),new Color(1,1,1,.9f));
            }
        }}
    }
    // Under the compass: what each kind of line means.
    void DrawAtlasLegend(Rect r){
        float x=r.x+10,y=r.y+11;
        CityLine(new Vector2(x,y),new Vector2(x+34,y),AtlasRoad,2.2f);GUI.Label(new Rect(x+42,y-9,90,18),"步道 · 城中",atlasLegend);y+=22;
        for(float d=0;d<34;d+=10)CityLine(new Vector2(x+d,y),new Vector2(x+Mathf.Min(34,d+6),y),AtlasPost,2.2f);GUI.Label(new Rect(x+42,y-9,90,18),"驿道 · 远行",atlasLegend);y+=22;
        for(float d=0;d<34;d+=15)CityLine(new Vector2(x+d,y),new Vector2(x+Mathf.Min(34,d+9),y),AtlasSea,2.2f);GUI.Label(new Rect(x+42,y-9,90,18),"航路 · 舟埠",atlasLegend);
    }
    // Mark (or, with no texture, a ruled 门 box for the gate), a vertical name slip and a caption.
    void DrawAtlasNode(string key,Vector2 p,float size,Texture2D art,string name,string caption,bool slip){
        bool picked=cityAtlasPick==key,hovered=cityAtlasHover==key;
        Rect mark=new Rect(p.x-size/2,p.y-size/2,size,size);
        if(picked||hovered){var ring=ringMark;if(ring!=null){var old=GUI.color;GUI.color=picked?Color.white:new Color(1,1,1,.55f);float s=size*1.35f;GUI.DrawTexture(new Rect(p.x-s/2,p.y-s/2,s,s),ring);GUI.color=old;}}
        if(art!=null)GUI.DrawTexture(mark,art,ScaleMode.ScaleToFit);
        else{Fill(mark,new Color(.91f,.85f,.69f,.96f));Outline(mark,new Color(.17f,.13f,.10f,.9f),2);GUI.Label(mark,"门",inkBold);}
        Rect hit=new Rect(mark.x-8,mark.y-8,mark.width+16,mark.height+16);
        if(slip){
            var style=picked?slipCapStyle:slipStyle;var textStyle=picked?slipTextCap:slipTextBold;
            string vertical=Vertical(name);var fit=textStyle.CalcSize(new GUIContent(vertical));float h=fit.y+6,w=Mathf.Max(24,fit.x+8);Rect s=new Rect(mark.xMax+2,p.y-h/2-4,w,h);
            if(Event.current.type==EventType.Repaint&&style!=null)style.Draw(s,false,false,false,false);
            GUI.Label(s,vertical,textStyle);hit=Rect.MinMaxRect(hit.x,Mathf.Min(hit.y,s.y),s.xMax,Mathf.Max(hit.yMax,s.yMax));
        }else HaloLabel(new Rect(p.x-60,mark.yMax-2,120,18),name,inkBold,atlasHalo);
        HaloLabel(new Rect(p.x-60,slip?mark.yMax+1:mark.yMax+15,120,16),caption,inkSmall,atlasHalo);
        cityAtlasHits[key]=hit;
    }
    void DrawAtlasExit(J exit,Vector2 p){
        string key="x:"+exit["place"].S;bool picked=cityAtlasPick==key,hovered=cityAtlasHover==key;
        string name="往"+exit["name"].S,cost=exit["points"].S+"力 · "+Num(exit["silver"])+"银 · "+(exit["sea"].B?"航路":"驿道");
        float w=Mathf.Max(inkBold.CalcSize(new GUIContent(name)).x,inkSmall.CalcSize(new GUIContent(cost)).x)+22;
        Rect r=new Rect(p.x-w/2,p.y-22,w,44);
        Fill(r,picked?new Color(.56f,.17f,.13f,.92f):new Color(.91f,.85f,.69f,.94f));Outline(r,picked||hovered?brass:new Color(.17f,.13f,.10f,.75f),picked||hovered?2:1);
        var nameStyle=picked?inkBoldOn:inkBold;var costStyle=picked?inkSmallOn:inkSmall;
        GUI.Label(new Rect(r.x,r.y+3,r.width,20),name,nameStyle);GUI.Label(new Rect(r.x,r.y+23,r.width,17),cost,costStyle);
        AtlasTexture(new Rect(p.x-14,r.y-26,28,28),exit["sea"].B?"node_boat":"node_exit");
        cityAtlasHits[key]=Rect.MinMaxRect(r.x,r.y-26,r.xMax,r.yMax);
    }
    void DrawAtlasHero(Vector2 p){
        if(pennantMark!=null)GUI.DrawTexture(new Rect(p.x-6,p.y-46,30,30),pennantMark);
        Tex(new Rect(p.x-17,p.y-20,34,34),"medallion");
        DrawPaperPortrait(new Rect(p.x-11,p.y-17,22,28),Hero,true,false,false);
        HaloLabel(new Rect(p.x+14,p.y-38,120,20),Hero["name"].S,mapLabelOn,mapHaloBold);
    }
    void HandleAtlasInput(Rect sheet){
        var e=Event.current;var m=e.mousePosition;
        bool over=sheet.Contains(m)&&CityStagePointer(m);
        string hit="";if(over)foreach(var pair in cityAtlasHits)if(pair.Value.Contains(m)){hit=pair.Key;break;}
        if(e.type==EventType.Repaint||e.type==EventType.MouseMove)cityAtlasHover=hit;
        if(over&&e.type==EventType.MouseDown&&e.button==0){
            if(hit.Length>0){
                bool twice=hit==cityAtlasLastClick&&Time.realtimeSinceStartup-cityAtlasLastClickAt<.45f;
                cityAtlasPick=hit;cityAtlasLastClick=hit;cityAtlasLastClickAt=twice?-10:Time.realtimeSinceStartup;
                rosterMode="此地";rosterScroll=Vector2.zero;mainScroll=Vector2.zero;
                // double-click unrolls a settlement as a scroll: browsing never spends points
                if(twice&&hit.StartsWith("s:"))Later(()=>OpenCityScroll(hit.Substring(2)));
            }
            GUI.FocusControl(null);e.Use();
        }
    }
    bool CityStagePointer(Vector2 m)=>!cityLoading&&!OverUI(m)&&!CityHeaderRect.Contains(m)&&!CityToolbarRect.Contains(m)
        &&!(CityMovementBlockReason.Length>0&&CityNoticeRect.Contains(m))&&!(creatingHero&&!Hero["created"].B&&new Rect(20,560,330,170).Contains(m));

    // ---------------------------------------------------------------- 舆图 card (right) and roster (left)
    void DrawCityAtlasCard(){
        Rect c=PlaceCard;Panel(c);GUILayout.BeginArea(new Rect(c.x+12,c.y+10,c.width-24,c.height-18));
        if(!City.Has||cityLoading||City["place"].I!=SelectedPlace){GUILayout.Label("正在展开舆图…",small);GUILayout.EndArea();return;}
        bool scroll=CityModeShown==CityMode.Scroll;
        GUILayout.Label(City["name"].S+(scroll?" · 长卷":" · 舆图"),heading);GUILayout.Label(City["country_name"].S+" · "+City["population"].S+"人在世",small);
        GUILayout.Label(CityLocal?Hero["name"].S+" · "+Hero["position_name"].S:"远方地图预览 · 浏览不消耗回合",small);
        GUI.enabled=!cityLoading;
        GUILayout.BeginHorizontal();if(Btn("主角所在",Hero["created"].B))LoadCity(Hero["place"].I);if(Btn("下一地区")){var all=W["locations"].A;int idx=all.FindIndex(p=>p["id"].I==SelectedPlace);LoadCity(all[(idx+1)%all.Count]["id"].I);}GUILayout.EndHorizontal();
        mainScroll=GUILayout.BeginScrollView(mainScroll);
        if(scroll)DrawCityScrollDetail();else DrawCityAtlasDetail();
        GUILayout.EndScrollView();GUI.enabled=true;GUILayout.EndArea();
    }
    void DrawCityAtlasDetail(){
        string key=cityAtlasPick;
        if(key.StartsWith("s:")){
            var site=CitySettlement(key.Substring(2));if(!site.Has){T("单击舆图上的聚落、城门或去路查看。",small);return;}
            GUILayout.Space(6);T(site["name"].S,sub);T(site["label"].S+" · "+site["role"].S,small);
            T("常住 "+site["home_residents"].A.Count+"人 · 当前在场 "+site["residents"].A.Count+"人",small);
            var jobs=site["professions"].D.OrderByDescending(p=>p.Value.N).Take(4).Select(p=>p.Key+p.Value.S).ToArray();if(jobs.Length>0)T("营生 · "+string.Join("、",jobs),small);
            DrawAtlasWalk(SiteTarget(site),HeroAtlasKey==key);
            if(Btn("展开长卷 ›"))Later(()=>OpenCityScroll(site["id"].S));
            T("设施",sub);
            foreach(var facility in CityFacilities(site["id"].S))if(GUILayout.Button(facility["glyph"].S+"  "+facility["name"].S+" · 在场"+facility["residents"].A.Count,chip)){string sid=site["id"].S,fid=facility["id"].S;Later(()=>OpenCityScroll(sid,fid));}
        }else if(key=="gate"||key=="dock"){
            var node=key=="gate"?CityGateNode:CityDockNode;if(!node.Has)return;
            GUILayout.Space(6);T(node["name"].S,sub);T(node["label"].S+" · "+node["description"].S,small);
            DrawAtlasWalk(new Vector2Int(node["row"].I,node["col"].I),HeroAtlasKey==key||CityLocal&&HeroTile==new Vector2Int(node["row"].I,node["col"].I));
            T("远行须由城门或舟埠出发。",small);
            if(CityLocal)DrawTravel();
        }else if(key.StartsWith("x:")){
            int place;if(!int.TryParse(key.Substring(2),out place))return;var exit=CityConnection(place);if(!exit.Has)return;
            GUILayout.Space(6);T("往"+exit["name"].S,sub);
            T((exit["sea"].B?"航路":"驿道")+" · "+exit["km"].S+"里程 · "+exit["points"].S+"行动力 / "+Num(exit["silver"])+"银",small);
            if(!CityLocal){T("远方地图预览，不能从这里出发。",small);return;}
            if(exit["enabled"].B){if(Btn("沿路远行 →",Paused))HeroAction("travel","place",exit["place"]);}
            else{
                T(exit["reason"].S,small);
                var gate=CityGateNode;
                if(gate.Has&&exit["reason"].S.Contains("城门")){var at=new Vector2Int(gate["row"].I,gate["col"].I);int cost=CityWalkCost(at);if(Btn(cost>=0?"先到"+gate["name"].S+" · "+cost+"行动力":"无路可到城门",cost>=0&&cost<=Hero["points"].I&&Paused))TryCityWalk(at);}
            }
            if(Btn("在天下舆图查看 ›")){SelectedPlace=place;Later(()=>GoStage(Stage.World));}
        }else{T("单击舆图上的聚落、城门或去路查看；双击聚落展开长卷。",small);}
        GUILayout.Space(8);T("单击查看 · 双击聚落展开长卷 · 浏览不耗行动力",small);
    }
    // "前往" for a settlement or the gate, with the real walking cost from where the hero stands.
    void DrawAtlasWalk(Vector2Int target,bool here){
        if(!CityLocal){T("远方地图预览",small);return;}
        if(here){T("你在此处",small);return;}
        int cost=CityWalkCost(target);
        if(cityWalking){if(Btn("停止行走"))StopCityWalk();return;}
        string reason=CityMovementBlockReason;
        if(cost<0)T("没有可通行的步道",small);
        else if(Btn("前往 · "+cost+" 行动力",reason.Length==0&&cost<=Hero["points"].I))TryCityWalk(target);
        if(reason.Length>0)T(reason,small);else if(cost>Hero["points"].I)T("本月行动力不足，可在下方月盘结束本月。",small);
    }
    // 此地: the people of the picked settlement (舆图) or facility (长卷), with portraits.
    void DrawCityPlaceRoster(){
        if(cityLoading||!City.Has||City["place"].I!=SelectedPlace){T("正在展开城中…",small);return;}
        string title;List<J> people;bool heroHere;
        if(CityModeShown==CityMode.Scroll){
            var site=CitySettlement(cityScrollSite);var facility=City["nodes"].A.FirstOrDefault(n=>n["id"].S==cityScrollPick);
            if(facility!=null&&facility["settlement_id"].S==cityScrollSite){title=facility["name"].S;people=facility["residents"].A;heroHere=CityLocal&&CityScrollHeroFacility()==facility["id"].S;}
            else{title=site["name"].S;people=site["residents"].A;heroHere=HeroAtlasKey=="s:"+cityScrollSite;}
        }else if(cityAtlasPick.StartsWith("s:")){var site=CitySettlement(cityAtlasPick.Substring(2));title=site["name"].S;people=site["residents"].A;heroHere=HeroAtlasKey==cityAtlasPick;}
        else if(cityAtlasPick=="gate"){var gate=CityGateNode;title=gate["name"].S;people=gate["residents"].A;heroHere=HeroAtlasKey=="gate";}
        else{T("选择聚落查看人物",sub);T("在舆图上单击一处聚落，即可查看此处之人。",small);return;}
        T(title+" · "+(people.Count+(heroHere?1:0))+"人",sub);
        if(heroHere)T(Hero["name"].S+" · 主角在此",small);
        GUI.SetNextControlName("RosterSearch");string nextQuery=GUILayout.TextField(query,field);
        if(nextQuery!=query){query=nextQuery;rosterScroll=Vector2.zero;}
        var rows=people.Where(p=>string.IsNullOrEmpty(query)||p["name"].S.Contains(query)||p["job"].S.Contains(query)).ToList();
        T(rows.Count+"人 · 点人物可交谈、赠礼或查看",small);
        if(rows.Count==0){T(people.Count==0?"此处眼下无人":"没有符合搜索的人物",small);return;}
        const float rowHeight=60;
        rosterScroll=GUILayout.BeginScrollView(rosterScroll);
        Rect content=GUILayoutUtility.GetRect(200,rows.Count*rowHeight,GUILayout.ExpandWidth(true));
        int first=Mathf.Clamp((int)(rosterScroll.y/rowHeight)-1,0,rows.Count-1),last=Mathf.Min(rows.Count,first+14);
        for(int i=first;i<last;i++){
            var resident=rows[i];int id=resident["id"].I;
            Rect row=new Rect(content.x,content.y+i*rowHeight,content.width,rowHeight);
            if(GUI.Button(row,GUIContent.none,id==SelectedPerson?rowOnStyle:rowStyle))OpenInteraction(id);
            float left=row.x+8;
            bool hasPerson=cityPersonViews.TryGetValue(id,out var person);
            if(hasPerson){Portrait(new Rect(row.x+6,row.y+6,38,48),id,person["sex"].S,person["age_months"].I);left=row.x+52;}
            GUI.Label(new Rect(left,row.y+6,row.xMax-left-4,24),resident["name"].S+(hasPerson?"  "+person["age_months"].I/12+"岁":""),sub);
            string where=CityModeShown==CityMode.Atlas?CityFacilityName(resident["node"].S):"";
            GUI.Label(new Rect(left,row.y+31,row.xMax-left-4,22),resident["job"].S+(hasPerson?" · "+person["identity"]["grade_label"].S:"")+(where.Length>0?" · "+where:""),small);
        }
        GUILayout.EndScrollView();
    }
    string CityFacilityName(string id){if(string.IsNullOrEmpty(id))return "";var n=City["nodes"].A.FirstOrDefault(x=>x["id"].S==id);return n!=null?n["name"].S:"";}

    // ---------------------------------------------------------------- toolbar
    // Returns the width used by the 舆图 / 长卷 / 地格 switch at the start of the toolbar.
    float DrawCityModeTabs(float x,float y){
        bool ready=CityAtlasReady;
        GUI.enabled=!cityLoading&&ready;
        if(CityTool(new Rect(x,y,52,28),"舆图",CityModeShown==CityMode.Atlas))SetCityMode(CityMode.Atlas);
        if(CityTool(new Rect(x+54,y,52,28),"长卷",CityModeShown==CityMode.Scroll))SetCityMode(CityMode.Scroll);
        GUI.enabled=!cityLoading;
        if(CityTool(new Rect(x+108,y,52,28),"地格",CityModeShown==CityMode.Tiles))SetCityMode(CityMode.Tiles);
        GUI.Label(new Rect(x+162,y+4,14,20),"|",centerSmall);
        return 176;
    }
    void DrawCityAtlasTools(float left,float y,Rect r){
        GUI.enabled=!cityLoading&&CityLocal;
        if(CityTool(new Rect(left,y,84,28),"定位主角")){string key=HeroAtlasKey;if(key.Length>0)cityAtlasPick=key;if(CityModeShown==CityMode.Scroll&&key.StartsWith("s:"))OpenCityScroll(key.Substring(2));}
        GUI.enabled=!cityLoading;
        if(CityModeShown==CityMode.Scroll){
            if(CityTool(new Rect(left+90,y,96,28),"‹ 回到舆图"))SetCityMode(CityMode.Atlas);
            GUI.Label(new Rect(r.xMax-365,y+6,355,20),"拖动或滚轮横移长卷 · 点设施看在场之人",cityTiny);
        }else GUI.Label(new Rect(r.xMax-365,y+6,355,20),"单击查看 · 双击聚落展开长卷 · 前往在右侧",cityTiny);
        GUI.enabled=true;
    }
}
}
