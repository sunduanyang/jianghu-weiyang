using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    string heroName="沈行舟",heroAge="18",heroSex="男";
    bool creatingHero;
    J geography=new J(),campaignOptions=new J();
    readonly Dictionary<int,Texture2D> mapMasks=new Dictionary<int,Texture2D>();
    Texture2D landMask,atlasBase,atlasInk,cityMark,capitalMark,ringMark,pennantMark,compassMark;
    GUIStyle slipStyle,slipCapStyle,slipText,slipTextBold,slipTextCap,siteLabel,siteHalo,countryTitle,countryHalo;
    // Winding post roads from geography.json, keyed "a-b" (a<b), in 1280x760 map units.
    readonly Dictionary<string,List<Vector2>> roadPaths=new Dictionary<string,List<Vector2>>();
    readonly Dictionary<string,Texture2D> siteMarks=new Dictionary<string,Texture2D>();
    // Land centroid (x, y) and land area (z) of each town region, from geography.json.
    readonly Dictionary<int,Vector3> cellAnchors=new Dictionary<int,Vector3>();
    // Picking uses the same cell ∩ opening territory ∩ land shapes as the painted masks.
    sealed class MapPolygon {
        public Vector2[] points;public Rect bounds;
        public bool Contains(Vector2 p){
            if(!bounds.Contains(p))return false;bool inside=false;
            for(int i=0,j=points.Length-1;i<points.Length;j=i++){
                Vector2 a=points[i],b=points[j];
                if((a.y>p.y)!=(b.y>p.y)&&p.x<(b.x-a.x)*(p.y-a.y)/(b.y-a.y)+a.x)inside=!inside;
            }
            return inside;
        }
    }
    readonly Dictionary<int,List<MapPolygon>> mapCells=new Dictionary<int,List<MapPolygon>>(),mapTerritories=new Dictionary<int,List<MapPolygon>>();
    List<MapPolygon> mapLand;
    float mapZoom=1;Vector2 mapPan;bool dragging;Vector2 dragMouse,mapPressMouse;
    int mapPressedPlace=-1;
    int chosenRow=-1,chosenCol=-1,originRow=-1,originCol=-1;
    string cityKind="全部聚落";
    void InitMap(){
        if(geography.Has)return;geography=J.Parse(Resources.Load<TextAsset>("geography").text);landMask=Resources.Load<Texture2D>("Map/land");
        foreach(var c in geography["cells"].A){mapMasks[c["place"].I]=Resources.Load<Texture2D>("Map/cell-"+c["place"].S);if(c["area"].Has)cellAnchors[c["place"].I]=new Vector3((float)c["cx"].N,(float)c["cy"].N,(float)c["area"].N);}
        mapLand=MapPolygons(geography["land"].S);
        foreach(var c in geography["cells"].A)mapCells[c["place"].I]=MapPolygons(c["path"].S);
        foreach(var t in geography["territories"].A)mapTerritories[t["country"].I]=MapPolygons(t["path"].S);
        atlasBase=Resources.Load<Texture2D>("Map/atlas_base");atlasInk=Resources.Load<Texture2D>("Map/atlas_ink");
        cityMark=Resources.Load<Texture2D>("Map/city");capitalMark=Resources.Load<Texture2D>("Map/capital");ringMark=Resources.Load<Texture2D>("Map/ring");pennantMark=Resources.Load<Texture2D>("Map/pennant");compassMark=Resources.Load<Texture2D>("Map/compass");
        slipStyle=SlipStyle("Map/label");slipCapStyle=SlipStyle("Map/label_cap");
        foreach(var road in geography["roads"].A)roadPaths[RoadKey(road["a"].I,road["b"].I)]=ParsePath(road["path"].S);
        foreach(var kind in new[]{"city","town","mountain","port"})siteMarks[kind]=Resources.Load<Texture2D>("Map/site_"+kind);
    }
    static string RoadKey(int a,int b)=>Mathf.Min(a,b)+"-"+Mathf.Max(a,b);
    static List<Vector2> ParsePath(string path){
        var nums=System.Text.RegularExpressions.Regex.Matches(path,@"-?\d+(?:\.\d+)?");var pts=new List<Vector2>();
        for(int i=0;i+1<nums.Count;i+=2)pts.Add(new Vector2(float.Parse(nums[i].Value,System.Globalization.CultureInfo.InvariantCulture),float.Parse(nums[i+1].Value,System.Globalization.CultureInfo.InvariantCulture)));
        return pts;
    }
    static List<MapPolygon> MapPolygons(string path){
        var result=new List<MapPolygon>();
        foreach(string section in System.Text.RegularExpressions.Regex.Split(path,"[Mm]")){
            var pts=ParsePath(section);if(pts.Count<3)continue;
            float x0=pts[0].x,y0=pts[0].y,x1=x0,y1=y0;
            foreach(var p in pts){x0=Mathf.Min(x0,p.x);y0=Mathf.Min(y0,p.y);x1=Mathf.Max(x1,p.x);y1=Mathf.Max(y1,p.y);}
            result.Add(new MapPolygon{points=pts.ToArray(),bounds=Rect.MinMaxRect(x0,y0,x1,y1)});
        }
        return result;
    }
    int MapRegionAt(Vector2 p){
        // Exclude the decorative frame, sea and the portions of Voronoi cells beyond their country.
        if(!new Rect(36,26,1208,708).Contains(p)||mapLand==null||!mapLand.Any(poly=>poly.Contains(p)))return -1;
        foreach(var cell in geography["cells"].A){
            int id=cell["place"].I;
            if(mapCells.TryGetValue(id,out var shapes)&&shapes.Any(poly=>poly.Contains(p))&&
                mapTerritories.TryGetValue(cell["native_country"].I,out var territory)&&territory.Any(poly=>poly.Contains(p)))return id;
        }
        return -1;
    }
    void SelectMapPlace(int id){SelectedPlace=id;campaignOptions=new J();}
    // Dashes continue across the bends of a polyline instead of restarting at every vertex.
    void DashedPolyline(List<Vector2> pts,Func<float,float,Vector2> pos,Color c,float width,float dash,float gap){
        if(Event.current.type!=EventType.Repaint||pts.Count<2)return;float phase=0;
        for(int i=0;i+1<pts.Count;i++){
            Vector2 a=pos(pts[i].x,pts[i].y),b=pos(pts[i+1].x,pts[i+1].y);float length=Vector2.Distance(a,b);if(length<.01f)continue;Vector2 dir=(b-a)/length;float t=0;
            while(t<length){float cycle=phase%(dash+gap);bool on=cycle<dash;float step=Mathf.Max(.5f,Mathf.Min(length-t,on?dash-cycle:dash+gap-cycle));if(on)Line(a+dir*t,a+dir*(t+step),c,width);t+=step;phase+=step;}
        }
    }
    // Country colours are muted for the silk; push saturation up so each power reads at a glance.
    Color OwnerColor(int country,float alpha){
        var c=Hex(Country(country)["color"].S);Color.RGBToHSV(c,out float h,out float sat,out float v);
        var o=Color.HSVToRGB(h,Mathf.Clamp01(sat*2.1f+.12f),Mathf.Clamp01(v*.9f));o.a=alpha;return o;
    }
    Texture2D SiteMark(string kind)=>siteMarks.TryGetValue(kind,out var t)&&t!=null?t:siteMarks.TryGetValue("city",out var c)?c:null;
    void InitMapStyles(){
        if(siteLabel!=null)return;
        siteLabel=new GUIStyle(mapLabel){fontSize=11,wordWrap=false,alignment=TextAnchor.MiddleLeft};TextColor(siteLabel,new Color(.20f,.15f,.10f));
        siteHalo=new GUIStyle(siteLabel);TextColor(siteHalo,new Color(.90f,.84f,.68f,.85f));
        countryTitle=new GUIStyle(mapLabel){font=fontBold,fontSize=26,wordWrap=false,alignment=TextAnchor.MiddleCenter};
        countryHalo=new GUIStyle(countryTitle);TextColor(countryHalo,new Color(.92f,.86f,.70f,.55f));
    }
    static GUIStyle SlipStyle(string path){var t=Resources.Load<Texture2D>(path);var s=new GUIStyle();s.normal.background=t;s.border=new RectOffset(6,6,6,6);return s;}
    // Place names are written top-to-bottom on small paper slips beside the town mark, the way names sit
    // in their ruled boxes on Song and Ming maps. Capitals get a vermilion slip.
    void InitSlipText(){
        if(slipText!=null)return;
        slipText=new GUIStyle(mapLabel){alignment=TextAnchor.UpperCenter,fontSize=13,wordWrap=false,padding=new RectOffset(0,0,6,6)};TextColor(slipText,new Color(.17f,.13f,.09f));
        slipTextBold=new GUIStyle(slipText){font=fontBold};TextColor(slipTextBold,new Color(.12f,.08f,.05f));
        slipTextCap=new GUIStyle(slipText){font=fontBold};TextColor(slipTextCap,paperText);
    }
    static string Vertical(string s)=>string.Join("\n",s.ToCharArray());
    Rect SlipRect(Vector2 q,float ms,Vector2 size,int attempt){
        bool left=attempt%2==1;int shift=attempt/2;float step=size.y*.5f+8;
        float y=q.y-size.y/2+(shift==0?0:(shift%2==1?1:-1)*((shift+1)/2)*step);
        float x=left?q.x-ms/2-3-size.x:q.x+ms/2+3;
        return new Rect(x,y,size.x,size.y);
    }
    void DrawWorld(Rect screen){InitMap();DrawMap(screen);}
    void DrawPlaceCard(){
        Rect c=PlaceCard;Panel(c);var p=Place(SelectedPlace);
        GUILayout.BeginArea(new Rect(c.x+12,c.y+10,c.width-24,c.height-18));
        GUILayout.Label(p["name"].S,heading);
        GUILayout.Label(Country(p["country"].I)["name"].S+" · "+p["region"].S,small);
        T(Hero["created"].B?(Hero["place"].I==SelectedPlace?"主角所在城镇":"远方城镇 · 查看地图不消耗行动力"):"查看城镇地图，或在此创建主角。",small);
        Metric("居民",p["population"].S+"人");Metric("守备",Num(p["guard"]));Metric("安定",Num(p["stability"]));
        GUILayout.BeginHorizontal();if(Btn("查看城中"))Later(()=>GoStage(Stage.City));if(Btn("所属官府"))OpenInstitution(true,p["country"].I);GUILayout.EndHorizontal();
        if(GUILayout.Button("此地中人 ›",linkStyle)){rosterMode="此地";rosterOpen=true;}
        mainScroll=GUILayout.BeginScrollView(mainScroll);
        DrawTravel();
        if(Btn("相邻势力攻城推演",Paused))Client.Fetch("/api/campaign?place="+SelectedPlace,j=>campaignOptions=j);
        foreach(var o in campaignOptions["options"].A){GUILayout.Label(Country(o["attacker"].I)["name"].S+" · 军费"+Num(o["cost"])+"银 · "+o["reason"].S,small);if(Btn("推演攻城 · "+Country(o["attacker"].I)["name"].S,Paused&&o["ready"].B))Client.Act("/api/campaign",J.O("attacker",o["attacker"],"place",SelectedPlace),j=>campaignOptions=new J());}
        Details("最近战报",W["campaigns"]);
        GUILayout.EndScrollView();GUILayout.EndArea();
    }
    void DrawTravel(){
        foreach(var connection in Hero["connections"].A){if(connection["place"].I!=SelectedPlace&&CurrentStage==Stage.World)continue;
            T("前往"+connection["name"].S+" · "+connection["points"].S+"行动力 / "+Num(connection["silver"])+"银"+(connection["sea"].B?" · 航路":" · 驿道"),small);
            if(Btn(connection["enabled"].B?"沿路远行 →":connection["reason"].S,Paused&&connection["enabled"].B))HeroAction("travel","place",connection["place"]);
        }
    }
    Vector2 MapPoint(J p,Rect r)=>new Vector2((float)p["x"].N,(float)p["y"].N);
    // Full-screen silk-scroll atlas under lamplight: painted base, territory washes tinted by the current
    // owner, ink layer (coast, rivers, mountains), dashed roads, town seals and dark name plates.
    void DrawMap(Rect r){
        GUI.BeginGroup(r);Rect local=new Rect(0,0,r.width,r.height);Tile(local,"bg");
        float k=Mathf.Max(r.width/1280f,r.height/760f)*mapZoom;Vector2 center=new Vector2(r.width/2,r.height/2);Vector2 offset=center-new Vector2(640,380)*k+mapPan*k;
        Func<float,float,Vector2> pos=(x,y)=>new Vector2(x*k+offset.x,y*k+offset.y);
        var mapRect=new Rect(offset.x,offset.y,1280*k,760*k);var prev=GUI.color;
        Vector2 pointer=Event.current.mousePosition;
        int hoveredPlace=local.Contains(pointer)&&!OverUI(pointer)&&!creatingHero&&Application.isFocused?MapRegionAt((pointer-offset)/k):-1;
        GUI.color=Color.white;
        if(atlasBase!=null)GUI.DrawTexture(mapRect,atlasBase,ScaleMode.StretchToFill);else GUI.DrawTexture(mapRect,landMask);
        GUI.color=prev;
        foreach(var c in geography["cells"].A){if(!mapMasks.TryGetValue(c["place"].I,out var tex)||tex==null)continue;GUI.color=OwnerColor(Place(c["place"].I)["country"].I,.50f);GUI.DrawTexture(mapRect,tex);GUI.color=prev;}
        if(hoveredPlace>=0&&mapMasks.TryGetValue(hoveredPlace,out var hoverMask)&&hoverMask!=null){
            GUI.color=new Color(1f,.82f,.30f,.80f);GUI.DrawTexture(mapRect,hoverMask);GUI.color=prev;
        }
        GUI.color=Color.white;
        if(atlasInk!=null)GUI.DrawTexture(mapRect,atlasInk,ScaleMode.StretchToFill);
        GUI.color=prev;
        if(atlasInk==null)foreach(var river in geography["rivers"].A)DrawVectorPath(river["path"].S,pos,new Color(.35f,.57f,.61f,.8f),1.4f);
        // Roads are painted into the atlas; redraw the ones leaving the selected town.
        foreach(var route in W["routes"].A){
            int ra=route["a"].I,rb=route["b"].I;bool touches=ra==SelectedPlace||rb==SelectedPlace;
            if(!touches&&atlasInk!=null)continue;
            if(!roadPaths.TryGetValue(RoadKey(ra,rb),out var pts)){var a=Place(ra);var b=Place(rb);pts=new List<Vector2>{new Vector2((float)a["x"].N,(float)a["y"].N),new Vector2((float)b["x"].N,(float)b["y"].N)};}
            bool sea=route["sea"].B;float w=touches?2.6f:2f;
            DashedPolyline(pts,pos,sea?new Color(.16f,.36f,.42f,.95f):new Color(.66f,.15f,.09f,.95f),w,sea?9:5,sea?6:3.5f);
        }
        InitMapStyles();
        // 疆域: each power's name written large across the towns it holds
        foreach(var country in W["countries"].A){
            var held=W["locations"].A.Where(p=>p["country"].I==country["id"].I).ToList();if(held.Count==0)continue;
            // centre of the land it holds (area-weighted), falling back to the towns themselves
            Vector2 m=Vector2.zero;float weight=0;
            foreach(var p in held){if(cellAnchors.TryGetValue(p["id"].I,out var a)&&a.z>0){m+=new Vector2(a.x,a.y)*a.z;weight+=a.z;}}
            if(weight>0)m/=weight;else{foreach(var p in held)m+=new Vector2((float)p["x"].N,(float)p["y"].N);m/=held.Count;}
            Vector2 q=pos(m.x,m.y);string title=string.Join(" ",country["name"].S.ToCharArray());
            float baseSize=weight>0?Mathf.Clamp(12+Mathf.Sqrt(weight)/12f,18,34):24;   // wide powers get larger names
            countryTitle.fontSize=Mathf.RoundToInt(baseSize*Mathf.Clamp(mapZoom,1,1.8f));countryHalo.fontSize=countryTitle.fontSize;
            TextColor(countryTitle,OwnerColor(country["id"].I,.78f));
            HaloLabel(new Rect(q.x-160,q.y-24,320,48),title,countryTitle,countryHalo);
        }
        // minor places merged into a town region: small pictograms, names once zoomed in; clicking selects the region
        bool overSites=OverUI(Event.current.mousePosition);float siteSize=12*Mathf.Clamp(mapZoom,1,2f);
        foreach(var p in W["places"].A){
            if(p["active"].B)continue;Vector2 q=pos((float)p["x"].N,(float)p["y"].N);
            Rect sr=new Rect(q.x-siteSize/2,q.y-siteSize/2,siteSize,siteSize);var t=SiteMark(p["kind"].S);if(t!=null)GUI.DrawTexture(sr,t);
            if(mapZoom>=1.6f)HaloLabel(new Rect(sr.xMax+2,q.y-8,100,16),p["name"].S,siteLabel,siteHalo);
            if(!overSites&&GUI.Button(sr,GUIContent.none,GUIStyle.none))SelectMapPlace(p["hub"].I);
        }
        bool overUI=OverUI(Event.current.mousePosition);
        var placedLabels=new List<Rect>();
        foreach(var p in W["locations"].A){
            Vector2 q=pos((float)p["x"].N,(float)p["y"].N);bool chosen=p["id"].I==SelectedPlace;bool capital=p["kind"].S=="capital";
            float ms=(capital?20:15)*Mathf.Clamp(mapZoom,1,1.6f);Rect markRect=new Rect(q.x-ms/2,q.y-ms/2,ms,ms);
            Texture2D mark=capital?capitalMark:cityMark;
            if(mark!=null)GUI.DrawTexture(markRect,mark);else Fill(new Rect(q.x-3,q.y-3,6,6),Hex(Country(p["country"].I)["color"].S));
            if(chosen&&ringMark!=null)GUI.DrawTexture(new Rect(q.x-ms,q.y-ms,ms*2,ms*2),ringMark);
            InitSlipText();string vertical=Vertical(p["name"].S);var textStyle=capital?slipTextCap:chosen?slipTextBold:slipText;
            Vector2 slip=new Vector2(21,textStyle.CalcSize(new GUIContent(vertical)).y);
            Rect labelRect=SlipRect(q,ms,slip,0);
            for(int attempt=1;attempt<18&&placedLabels.Any(o=>o.Overlaps(labelRect));attempt++)labelRect=SlipRect(q,ms,slip,attempt);
            placedLabels.Add(labelRect);
            if(Vector2.Distance(labelRect.center,q)>60)Line(q,labelRect.center,new Color(.35f,.24f,.14f,.45f));
            if(Event.current.type==EventType.Repaint)(capital?slipCapStyle:slipStyle).Draw(labelRect,false,false,false,false);
            Fill(new Rect(labelRect.x+3,labelRect.y-4,labelRect.width-6,4),OwnerColor(p["country"].I,.95f));   // owner colour as a tab on top
            if(chosen||p["id"].I==hoveredPlace)Outline(new Rect(labelRect.x-2,labelRect.y-2,labelRect.width+4,labelRect.height+4),brass,2);
            GUI.Label(labelRect,vertical,textStyle);
            if(!overUI){bool hit=GUI.Button(labelRect,GUIContent.none,GUIStyle.none);hit|=GUI.Button(markRect,GUIContent.none,GUIStyle.none);if(hit)SelectMapPlace(p["id"].I);}
        }
        if(Hero["created"].B){
            var p=Place(Hero["place"].I);Vector2 q=pos((float)p["x"].N,(float)p["y"].N);
            if(pennantMark!=null)GUI.DrawTexture(new Rect(q.x-10,q.y-38,32,32),pennantMark);else Fill(new Rect(q.x-3,q.y-24,6,13),vermilion);
            HaloLabel(new Rect(q.x+14,q.y-44,150,20),Hero["name"].S,mapLabelOn,mapHaloBold);
        }
        if(compassMark!=null)GUI.DrawTexture(new Rect(1330,72,76,76),compassMark);
        Event ev=Event.current;
        if(!Application.isFocused){dragging=false;mapPressedPlace=-1;}
        if(local.Contains(ev.mousePosition)&&!overUI){
            if(ev.type==EventType.ScrollWheel){mapPressedPlace=-1;float old=mapZoom;mapZoom=Mathf.Clamp(mapZoom*(ev.delta.y>0?.85f:1.18f),1f,5);Vector2 mouse=ev.mousePosition-center;mapPan+=mouse*(1/(k*(mapZoom/old))-1/k);ev.Use();}
            if(ev.type==EventType.MouseDown&&ev.button==0&&Application.isFocused){dragging=true;dragMouse=mapPressMouse=ev.mousePosition;mapPressedPlace=MapRegionAt((ev.mousePosition-offset)/k);}
        }
        if(ev.type==EventType.MouseDrag&&dragging){
            // Once the pointer leaves the click tolerance, returning to the start is still a drag.
            if((ev.mousePosition-mapPressMouse).sqrMagnitude>36)mapPressedPlace=-1;
            mapPan+=(ev.mousePosition-dragMouse)/k;dragMouse=ev.mousePosition;ev.Use();
        }
        if(ev.rawType==EventType.MouseUp&&ev.button==0){
            if(ev.type==EventType.MouseUp&&dragging&&mapPressedPlace>=0&&local.Contains(ev.mousePosition)&&!overUI&&
                (ev.mousePosition-mapPressMouse).sqrMagnitude<=36&&MapRegionAt((ev.mousePosition-offset)/k)==mapPressedPlace){
                SelectMapPlace(mapPressedPlace);ev.Use();
            }
            dragging=false;mapPressedPlace=-1;
        }
        GUI.EndGroup();
    }
    void DashedLine(Vector2 a,Vector2 b,Color c,float width,float dash,float gap){if(Event.current.type!=EventType.Repaint)return;float length=Vector2.Distance(a,b);if(length<1)return;Vector2 dir=(b-a)/length;for(float s=0;s<length;s+=dash+gap)Line(a+dir*s,a+dir*Mathf.Min(length,s+dash),c,width);}
    void DrawVectorPath(string path,Func<float,float,Vector2> pos,Color color,float width){var tokens=System.Text.RegularExpressions.Regex.Matches(path,@"[MLZmlz]|-?\d+(?:\.\d+)?");Vector2 last=Vector2.zero,start=Vector2.zero;bool move=true;for(int i=0;i<tokens.Count;i++){string t=tokens[i].Value;if(t=="M"||t=="m"){move=true;continue;}if(t=="L"||t=="l")continue;if(t=="Z"||t=="z"){Line(last,start,color,width);continue;}if(i+1>=tokens.Count)break;float x=float.Parse(t,System.Globalization.CultureInfo.InvariantCulture),y=float.Parse(tokens[++i].Value,System.Globalization.CultureInfo.InvariantCulture);var next=pos(x,y);if(move){start=next;move=false;}else Line(last,next,color,width);last=next;}}
    public List<Vector2Int> CityPath(int sr,int sc,int tr,int tc,out int cost){
        EnsureCityIndex();cost=0;var result=new List<Vector2Int>();var start=new Vector2Int(sr,sc);var target=new Vector2Int(tr,tc);
        if(!CityInside(start)||!CityInside(target))return result;
        var distance=new Dictionary<Vector2Int,int>();var previous=new Dictionary<Vector2Int,Vector2Int>();BuildCityPaths(start,distance,previous,target);
        if(!distance.ContainsKey(target))return result;cost=distance[target];var at=target;result.Add(at);while(at!=start){at=previous[at];result.Add(at);}result.Reverse();return result;
    }
}
}
