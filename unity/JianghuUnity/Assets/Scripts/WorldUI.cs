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
    Texture2D landMask,atlasBase,atlasInk,cityMark,capitalMark,ringMark,pennantMark,compassMark;
    GUIStyle slipStyle,slipCapStyle,slipText,slipTextBold,slipTextCap;
    float mapZoom=1;Vector2 mapPan;bool dragging;Vector2 dragMouse;
    int chosenRow=-1,chosenCol=-1,originRow=-1,originCol=-1;
    string cityKind="全部设施";
    void InitMap(){
        if(geography.Has)return;geography=J.Parse(Resources.Load<TextAsset>("geography").text);landMask=Resources.Load<Texture2D>("Map/land");
        foreach(var c in geography["cells"].A)mapMasks[c["place"].I]=Resources.Load<Texture2D>("Map/cell-"+c["place"].S);
        atlasBase=Resources.Load<Texture2D>("Map/atlas_base");atlasInk=Resources.Load<Texture2D>("Map/atlas_ink");
        cityMark=Resources.Load<Texture2D>("Map/city");capitalMark=Resources.Load<Texture2D>("Map/capital");ringMark=Resources.Load<Texture2D>("Map/ring");pennantMark=Resources.Load<Texture2D>("Map/pennant");compassMark=Resources.Load<Texture2D>("Map/compass");
        slipStyle=SlipStyle("Map/label");slipCapStyle=SlipStyle("Map/label_cap");
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
        Metric("居民",p["population"].S+"人");Metric("守备",Num(p["guard"]));Metric("安定",Num(p["stability"]));
        GUILayout.BeginHorizontal();if(Btn("进入城中"))Go(1);if(Btn("所属官府"))OpenInstitution(true,p["country"].I);GUILayout.EndHorizontal();
        if(GUILayout.Button("此地中人 ›",linkStyle)){rosterMode="此地";rosterOpen=true;}
        mainScroll=GUILayout.BeginScrollView(mainScroll);
        DrawTravel();
        if(Btn("相邻势力攻城推演",Paused))Client.Fetch("/api/campaign?place="+SelectedPlace,j=>campaignOptions=j);
        foreach(var o in campaignOptions["options"].A){GUILayout.Label(Country(o["attacker"].I)["name"].S+" · 军费"+Num(o["cost"])+"银 · "+o["reason"].S,small);if(Btn("推演攻城 · "+Country(o["attacker"].I)["name"].S,Paused&&o["ready"].B))Client.Act("/api/campaign",J.O("attacker",o["attacker"],"place",SelectedPlace),j=>campaignOptions=new J());}
        Details("最近战报",W["campaigns"]);
        GUILayout.EndScrollView();GUILayout.EndArea();
    }
    void DrawTravel(){
        foreach(var connection in Hero["connections"].A){if(connection["place"].I!=SelectedPlace&&Page==0)continue;
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
        GUI.color=Color.white;
        if(atlasBase!=null)GUI.DrawTexture(mapRect,atlasBase,ScaleMode.StretchToFill);else GUI.DrawTexture(mapRect,landMask);
        GUI.color=prev;
        if(mapLayer=="疆域"||mapLayer=="州路")foreach(var c in geography["cells"].A){if(!mapMasks.TryGetValue(c["place"].I,out var tex)||tex==null)continue;GUI.color=Hex(Country(Place(c["place"].I)["country"].I)["color"].S,mapLayer=="州路"?.20f:.30f);GUI.DrawTexture(mapRect,tex);GUI.color=prev;}
        GUI.color=Color.white;
        if(atlasInk!=null)GUI.DrawTexture(mapRect,atlasInk,ScaleMode.StretchToFill);
        GUI.color=prev;
        if(atlasInk==null)foreach(var river in geography["rivers"].A)DrawVectorPath(river["path"].S,pos,new Color(.35f,.57f,.61f,.8f),1.4f);
        bool roads=mapLayer=="行路";
        foreach(var route in W["routes"].A){
            var a=Place(route["a"].I);var b=Place(route["b"].I);Vector2 pa=pos((float)a["x"].N,(float)a["y"].N),pb=pos((float)b["x"].N,(float)b["y"].N);
            // 驿道 in fine vermilion dots, 航路 in long ink-teal dashes
            if(route["sea"].B)DashedLine(pa,pb,new Color(.23f,.38f,.43f,roads?.9f:.6f),roads?1.8f:1.2f,7,6);
            else DashedLine(pa,pb,new Color(.59f,.20f,.14f,roads?.95f:.67f),roads?2.2f:1.5f,3,3.5f);
        }
        if(mapLayer=="州路")foreach(var region in geography["regions"].A){Vector2 q=pos((float)region["x"].N,(float)region["y"].N);HaloLabel(new Rect(q.x-30,q.y-21,110,18),region["name"].S,caption,mapHalo);}
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
            Fill(new Rect(labelRect.x+4,labelRect.y-3,labelRect.width-8,3),Hex(Country(p["country"].I)["color"].S,.95f));   // owner colour as a tab on top
            if(chosen)Outline(new Rect(labelRect.x-2,labelRect.y-2,labelRect.width+4,labelRect.height+4),brass,2);
            GUI.Label(labelRect,vertical,textStyle);
            if(!overUI){bool hit=GUI.Button(labelRect,GUIContent.none,GUIStyle.none);hit|=GUI.Button(markRect,GUIContent.none,GUIStyle.none);if(hit){SelectedPlace=p["id"].I;campaignOptions=new J();}}
        }
        if(mapLayer=="门派")foreach(var f in W["factions"].A){var p=Place(f["place"].I);Vector2 q=pos((float)p["x"].N,(float)p["y"].N);Rect fr=new Rect(q.x-12,q.y+16+(f["id"].I%3)*18,110,18);Fill(new Rect(fr.x-7,fr.y+6,5,5),brass);HaloLabel(fr,f["name"].S,caption,mapHalo);if(!overUI&&GUI.Button(fr,GUIContent.none,GUIStyle.none))OpenInstitution(false,f["id"].I);}
        if(Hero["created"].B){
            var p=Place(Hero["place"].I);Vector2 q=pos((float)p["x"].N,(float)p["y"].N);
            if(pennantMark!=null)GUI.DrawTexture(new Rect(q.x-10,q.y-38,32,32),pennantMark);else Fill(new Rect(q.x-3,q.y-24,6,13),vermilion);
            HaloLabel(new Rect(q.x+14,q.y-44,150,20),Hero["name"].S,mapLabelOn,mapHaloBold);
        }
        if(compassMark!=null)GUI.DrawTexture(new Rect(1330,72,76,76),compassMark);
        Event ev=Event.current;if(local.Contains(ev.mousePosition)&&!overUI){
            if(ev.type==EventType.ScrollWheel){float old=mapZoom;mapZoom=Mathf.Clamp(mapZoom*(ev.delta.y>0?.85f:1.18f),1f,5);Vector2 mouse=ev.mousePosition-center;mapPan+=mouse*(1/(k*(mapZoom/old))-1/k);ev.Use();}
            if(ev.type==EventType.MouseDown&&ev.button==0){dragging=true;dragMouse=ev.mousePosition;}
        }
        if(ev.type==EventType.MouseDrag&&dragging){mapPan+=(ev.mousePosition-dragMouse)/k;dragMouse=ev.mousePosition;ev.Use();}
        if(ev.rawType==EventType.MouseUp)dragging=false;
        GUI.EndGroup();
    }
    void DashedLine(Vector2 a,Vector2 b,Color c,float width,float dash,float gap){if(Event.current.type!=EventType.Repaint)return;float length=Vector2.Distance(a,b);if(length<1)return;Vector2 dir=(b-a)/length;for(float s=0;s<length;s+=dash+gap)Line(a+dir*s,a+dir*Mathf.Min(length,s+dash),c,width);}
    void DrawVectorPath(string path,Func<float,float,Vector2> pos,Color color,float width){var tokens=System.Text.RegularExpressions.Regex.Matches(path,@"[MLZmlz]|-?\d+(?:\.\d+)?");Vector2 last=Vector2.zero,start=Vector2.zero;bool move=true;for(int i=0;i<tokens.Count;i++){string t=tokens[i].Value;if(t=="M"||t=="m"){move=true;continue;}if(t=="L"||t=="l")continue;if(t=="Z"||t=="z"){Line(last,start,color,width);continue;}if(i+1>=tokens.Count)break;float x=float.Parse(t,System.Globalization.CultureInfo.InvariantCulture),y=float.Parse(tokens[++i].Value,System.Globalization.CultureInfo.InvariantCulture);var next=pos(x,y);if(move){start=next;move=false;}else Line(last,next,color,width);last=next;}}
    public List<Vector2Int> CityPath(int sr,int sc,int tr,int tc,out int cost){
        int size=City["size"].I;cost=0;var result=new List<Vector2Int>();if(sr<0||sc<0||tr<0||tc<0||sr>=size||sc>=size||tr>=size||tc>=size)return result;
        var distance=new Dictionary<Vector2Int,int>();var previous=new Dictionary<Vector2Int,Vector2Int>();var open=new List<Vector2Int>();var start=new Vector2Int(sr,sc);var target=new Vector2Int(tr,tc);distance[start]=0;open.Add(start);
        while(open.Count>0){var p=open.OrderBy(x=>distance[x]).First();open.Remove(p);if(p==target)break;foreach(var delta in new[]{Vector2Int.up,Vector2Int.down,Vector2Int.left,Vector2Int.right}){var q=p+delta;if(q.x<0||q.y<0||q.x>=size||q.y>=size)continue;char t=City["rows"][q.x].S[q.y];if("~^#".Contains(t))continue;int d=distance[p]+("+=".Contains(t)?1:t=='t'?3:2);if(!distance.ContainsKey(q)||d<distance[q]){distance[q]=d;previous[q]=p;if(!open.Contains(q))open.Add(q);}}}
        if(!distance.ContainsKey(target))return result;cost=distance[target];var at=target;result.Add(at);while(at!=start){at=previous[at];result.Add(at);}result.Reverse();return result;
    }
}
}
