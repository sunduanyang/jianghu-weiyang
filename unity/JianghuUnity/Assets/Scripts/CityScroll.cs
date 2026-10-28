using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jianghu {
// 长卷：一处聚落（城、镇、村或门派驻地）的营生设施沿一条街横向展开。
// 设施的顺序、名称与在场之人都来自 /api/city；同一聚落内的设施共用一块地格，
// 在设施之间走动不耗行动力，前往城门或别处聚落仍沿原寻路扣行动力。
public partial class JianghuApp {
    string cityScrollSite="",cityScrollPick="",cityScrollHover="",cityScrollHeroAt="";
    float cityScrollX,cityScrollHeroX=float.NaN;int cityScrollFocus=-1;
    bool cityScrollDragging,cityScrollMoved;Vector2 cityScrollPress;float cityScrollPressX;
    const float ScrollStep=420,ScrollMargin=260,ScrollSpriteW=442,ScrollSpriteH=338;
    readonly Dictionary<string,Rect> cityScrollHits=new Dictionary<string,Rect>();

    static string FacilityArt(string kind)=>kind=="faction"?"sect":Array.IndexOf(FacilityOrder,kind)>=0?kind:"generic";
    public void OpenCityScroll(string site,string facility=null){
        if(DeferWhileDrawing(()=>OpenCityScroll(site,facility)))return;
        if(site!=cityScrollSite){cityScrollX=0;cityScrollHeroX=float.NaN;cityScrollHeroAt="";}
        cityScrollSite=site;cityScrollPick=facility??"";cityAtlasPick="s:"+site;cityMode=CityMode.Scroll;
        cityScrollDragging=false;rosterMode="此地";rosterScroll=Vector2.zero;mainScroll=Vector2.zero;
        int index=CityFacilities(site).FindIndex(n=>n["id"].S==cityScrollPick);cityScrollFocus=index;
        if(index>=0)CityScrollPick(cityScrollPick);
    }
    void CityScrollPick(string facility){
        cityScrollPick=facility;rosterMode="此地";rosterScroll=Vector2.zero;mainScroll=Vector2.zero;
        var node=City["nodes"].A.FirstOrDefault(n=>n["id"].S==facility);
        // Inside one settlement the hero steps between its facilities freely; the gate is a separate tile.
        if(node!=null&&node["kind"].S!="gate"&&HeroAtlasKey=="s:"+cityScrollSite)cityScrollHeroAt=facility;
    }
    // The facility the hero stands at in the open scroll, or "" when elsewhere.
    string CityScrollHeroFacility(){
        if(!CityLocal)return "";
        var list=CityFacilities(cityScrollSite);string key=HeroAtlasKey;
        if(key=="gate"){var gate=list.FirstOrDefault(n=>n["kind"].S=="gate");return gate!=null?gate["id"].S:"";}
        if(key!="s:"+cityScrollSite)return "";
        if(list.Any(n=>n["id"].S==cityScrollHeroAt&&n["kind"].S!="gate"))return cityScrollHeroAt;
        var first=list.FirstOrDefault(n=>n["kind"].S!="gate")??list.FirstOrDefault();
        return first!=null?first["id"].S:"";
    }

    void DrawCityScroll(Rect sheet){
        var site=CitySettlement(cityScrollSite);
        if(!site.Has){CityAtlasReset();site=CitySettlement(cityScrollSite);if(!site.Has){cityMode=CityMode.Atlas;return;}}
        var facilities=CityFacilities(cityScrollSite);EnsureCityRoutes();
        SilkSheet(sheet);
        // heading, and the other settlements of this region as tabs along the top
        GUI.Label(new Rect(sheet.x+22,sheet.y+12,420,32),site["name"].S+" · "+site["label"].S,atlasTitle);
        GUI.Label(new Rect(sheet.x+24,sheet.y+42,560,18),site["role"].S+" · 常住"+site["home_residents"].A.Count+"人 · 在场"+site["residents"].A.Count+"人 · 设施"+facilities.Count+"处",atlasLegend);
        float tx=sheet.xMax-16;
        foreach(var other in CitySettlementsList.AsEnumerable().Reverse()){
            string label=other["glyph"].S+" "+other["name"].S;bool on=other["id"].S==cityScrollSite;
            float tabW=tagStyle.CalcSize(new GUIContent(label)).x+18;tx-=tabW+6;
            if(GUI.Button(new Rect(tx,sheet.y+16,tabW,26),label,on?tagOnStyle:tagStyle)&&!on){string id=other["id"].S;Later(()=>OpenCityScroll(id));}
        }
        Rect strip=new Rect(sheet.x+16,sheet.y+66,sheet.width-32,sheet.height-66-46);
        float content=ScrollMargin*2+Mathf.Max(0,facilities.Count-1)*ScrollStep,maxX=Mathf.Max(0,content-strip.width);
        float offset=content<strip.width?(strip.width-content)/2:0;
        if(cityScrollFocus>=0){cityScrollX=ScrollMargin+cityScrollFocus*ScrollStep-strip.width/2;cityScrollFocus=-1;}
        cityScrollX=Mathf.Clamp(cityScrollX,0,maxX);
        string heroAt=CityScrollHeroFacility();int heroIndex=facilities.FindIndex(n=>n["id"].S==heroAt);
        // rods at both ends, like the 轴 of a handscroll
        Fill(new Rect(strip.x-10,strip.y-8,9,strip.height+16),new Color(.30f,.19f,.11f));Fill(new Rect(strip.xMax+1,strip.y-8,9,strip.height+16),new Color(.30f,.19f,.11f));
        GUI.BeginGroup(strip);
        float w=strip.width,h=strip.height,ground=h*.78f;var e=Event.current;
        var silk=ScrollArt("silk");if(silk!=null)GUI.DrawTextureWithTexCoords(new Rect(0,0,w,h),silk,new Rect(cityScrollX/silk.width,0,w/silk.width,h/silk.height));
        var hills=ScrollArt("hills");
        if(hills!=null){float hh=h*.40f,tileW=hills.width*hh/hills.height;GUI.DrawTextureWithTexCoords(new Rect(0,ground-hh-38,w,hh),hills,new Rect(cityScrollX*.45f/tileW,0,w/tileW,1));}
        Fill(new Rect(0,ground,w,50),new Color(.78f,.69f,.50f,.55f));
        Fill(new Rect(0,ground,w,1),new Color(.17f,.13f,.10f,.45f));Fill(new Rect(0,ground+50,w,1),new Color(.17f,.13f,.10f,.25f));
        // the near bank below the street: a faint wash and a pine between each pair of buildings
        Fill(new Rect(0,ground+51,w,h-ground-51),new Color(.45f,.55f,.40f,.10f));
        float pine=Mathf.Clamp(h-ground-56,0,56);
        if(pine>24)for(int i=0;i+1<facilities.Count;i++){float px=offset+ScrollMargin+(i+.5f)*ScrollStep-cityScrollX;if(px>-60&&px<w+60)AtlasTexture(new Rect(px-pine/2+(i%3)*14,h-pine-4,pine,pine),"pine",new Color(1,1,1,.85f));}
        cityScrollHits.Clear();
        var mist=ScrollArt("mist");
        for(int i=0;i<facilities.Count;i++){
            var node=facilities[i];string id=node["id"].S;float fx=offset+ScrollMargin+i*ScrollStep-cityScrollX;
            if(fx<-ScrollStep||fx>w+ScrollStep)continue;
            bool picked=cityScrollPick==id,hovered=cityScrollHover==id;
            Rect b=new Rect(fx-ScrollSpriteW/2,ground+8-ScrollSpriteH*.915f,ScrollSpriteW,ScrollSpriteH);   // the sprite's street line sits at 238/260
            if(picked||hovered)Fill(new Rect(b.x+40,ground+2,b.width-80,4),picked?new Color(.56f,.17f,.13f,.85f):new Color(.79f,.64f,.35f,.6f));
            AtlasTexture(b,"building_"+FacilityArt(node["kind"].S));
            // name slip and head count, hung at the top left of each building
            string name=node["name"].S,vertical=Vertical(name);var textStyle=picked?slipTextCap:slipTextBold;var fit=textStyle.CalcSize(new GUIContent(vertical));
            Rect slip=new Rect(b.x+6,Mathf.Max(10,b.y+6),Mathf.Max(26,fit.x+8),fit.y+6);
            var slipBack=picked?slipCapStyle:slipStyle;if(e.type==EventType.Repaint&&slipBack!=null)slipBack.Draw(slip,false,false,false,false);
            GUI.Label(slip,vertical,textStyle);
            GUI.Label(new Rect(slip.xMax+4,slip.y+2,140,18),node["label"].S,atlasLegend);
            GUI.Label(new Rect(slip.xMax+4,slip.y+20,140,18),"在场 "+node["residents"].A.Count,atlasLegend);
            cityScrollHits[id]=Rect.MinMaxRect(Mathf.Min(slip.x,b.x+50),slip.y,b.xMax-50,ground+6);
            DrawScrollPeople(node["residents"].A,fx,ground+8,heroIndex==i);
            if(mist!=null&&i%2==1){var old=GUI.color;GUI.color=new Color(1,1,1,.55f);GUI.DrawTexture(new Rect(fx+ScrollStep/2-160,ground-300,320,96),mist);GUI.color=old;}
        }
        if(facilities.Count==0)GUI.Label(new Rect(0,h/2-20,w,40),"此处尚无设施记录",inkBold);
        if(heroIndex>=0){
            float target=offset+ScrollMargin+heroIndex*ScrollStep-cityScrollX-128;
            if(float.IsNaN(cityScrollHeroX)||Mathf.Abs(cityScrollHeroX-target)>ScrollStep*3)cityScrollHeroX=target;
            if(e.type==EventType.Repaint)cityScrollHeroX=Mathf.MoveTowards(cityScrollHeroX,target,Mathf.Max(6,Mathf.Abs(target-cityScrollHeroX)*.18f));
            DrawScrollHero(new Vector2(cityScrollHeroX,ground+4));
        }else cityScrollHeroX=float.NaN;
        HandleScrollInput(strip,maxX);
        GUI.EndGroup();
        DrawScrollOverview(new Rect(strip.x,strip.yMax+12,strip.width,24),facilities,content,offset,strip.width,maxX,heroIndex);
    }
    void DrawScrollPeople(List<J> people,float fx,float y,bool heroHere){
        const float pw=30,ph=38,gap=4;int max=7,shown=Mathf.Min(max,people.Count);
        float total=shown*pw+Mathf.Max(0,shown-1)*gap+(people.Count>max?34:0);float x=fx-total/2+(heroHere?24:0);
        for(int k=0;k<shown;k++){
            var resident=people[k];int id=resident["id"].I;Rect r=new Rect(x+k*(pw+gap),y,pw,ph);
            bool hasPerson=cityPersonViews.TryGetValue(id,out var person);
            if(hasPerson)Portrait(r,id,person["sex"].S,person["age_months"].I);else Fill(r,new Color(.15f,.17f,.16f,.72f));
            Outline(r,id==SelectedPerson?brass:new Color(.17f,.13f,.10f,.8f),id==SelectedPerson?2:1);
            if(GUI.Button(r,new GUIContent("",resident["name"].S+" · "+resident["job"].S),GUIStyle.none))OpenInteraction(id,true);
            if(r.Contains(Event.current.mousePosition))HaloLabel(new Rect(r.center.x-60,r.yMax+2,120,18),resident["name"].S,inkBold,atlasHalo);
        }
        if(people.Count>max)GUI.Label(new Rect(x+shown*(pw+gap),y+8,34,22),"+"+(people.Count-max),inkBold);
    }
    void DrawScrollHero(Vector2 p){
        float s=1.1f;
        Tex(new Rect(p.x-23*s,p.y-50*s,46*s,46*s),"medallion");
        DrawPaperPortrait(new Rect(p.x-14*s,p.y-46*s,28*s,36*s),Hero,true,false,false);
        if(pennantMark!=null)GUI.DrawTexture(new Rect(p.x+8,p.y-86,32,32),pennantMark);
        HaloLabel(new Rect(p.x-64,p.y-74,128,22),Hero["name"].S+" · 主角",mapLabelOn,mapHaloBold);
    }
    void HandleScrollInput(Rect strip,float maxX){
        var e=Event.current;var m=e.mousePosition;
        bool over=new Rect(0,0,strip.width,strip.height).Contains(m)&&CityStagePointer(m+strip.position);
        string hit="";if(over)foreach(var pair in cityScrollHits)if(pair.Value.Contains(m)){hit=pair.Key;break;}
        if(e.type==EventType.Repaint||e.type==EventType.MouseMove)cityScrollHover=hit;
        if(over&&e.type==EventType.MouseDown&&e.button==0){cityScrollDragging=true;cityScrollMoved=false;cityScrollPress=m;cityScrollPressX=cityScrollX;GUI.FocusControl(null);e.Use();}
        if(cityScrollDragging&&e.type==EventType.MouseDrag){if(Mathf.Abs(m.x-cityScrollPress.x)>5)cityScrollMoved=true;if(cityScrollMoved)cityScrollX=Mathf.Clamp(cityScrollPressX-(m.x-cityScrollPress.x),0,maxX);e.Use();}
        if(cityScrollDragging&&e.rawType==EventType.MouseUp){
            if(!cityScrollMoved&&hit.Length>0)CityScrollPick(hit);
            cityScrollDragging=false;if(e.type==EventType.MouseUp)e.Use();
        }
        if(over&&e.type==EventType.ScrollWheel){cityScrollX=Mathf.Clamp(cityScrollX+(Mathf.Abs(e.delta.x)>Mathf.Abs(e.delta.y)?e.delta.x:e.delta.y)*26,0,maxX);e.Use();}
    }
    // A thin strip under the scroll: every facility as a tick, the visible part framed, click to jump.
    void DrawScrollOverview(Rect r,List<J> facilities,float content,float offset,float view,float maxX,int heroIndex){
        Fill(r,new Color(.90f,.84f,.68f,.65f));Outline(r,new Color(.17f,.13f,.10f,.45f));
        float span=Mathf.Max(content,view);
        for(int i=0;i<facilities.Count;i++){
            float x=r.x+(offset+ScrollMargin+i*ScrollStep)/span*r.width;
            Fill(new Rect(x,r.y+4,1,r.height-8),new Color(.17f,.13f,.10f,.6f));
            bool on=facilities[i]["id"].S==cityScrollPick;
            GUI.Label(new Rect(x+3,r.y+3,90,18),facilities[i]["name"].S,on?inkBold:inkSmall);
            if(i==heroIndex)Fill(new Rect(x-3,r.y+r.height/2-3,6,6),vermilion);
        }
        if(content>view){Rect win=new Rect(r.x+cityScrollX/span*r.width,r.y,view/span*r.width,r.height);Outline(win,vermilion,2);}
        if(GUI.Button(r,GUIContent.none,GUIStyle.none)&&content>view)cityScrollX=Mathf.Clamp((Event.current.mousePosition.x-r.x)/r.width*span-view/2,0,maxX);
    }

    // ---------------------------------------------------------------- 长卷 card
    void DrawCityScrollDetail(){
        var site=CitySettlement(cityScrollSite);if(!site.Has)return;
        var node=City["nodes"].A.FirstOrDefault(n=>n["id"].S==cityScrollPick&&n["settlement_id"].S==cityScrollSite);
        bool inSite=HeroAtlasKey=="s:"+cityScrollSite;
        GUILayout.Space(6);
        if(node==null){
            T(site["name"].S,sub);T(site["label"].S+" · "+site["role"].S,small);
            DrawAtlasWalk(SiteTarget(site),inSite);
            T("点长卷上的设施，查看在场之人与可办之事。",small);
            foreach(var f in CityFacilities(cityScrollSite))if(GUILayout.Button(f["glyph"].S+"  "+f["name"].S+" · 在场"+f["residents"].A.Count,chip)){string id=f["id"].S;cityScrollFocus=CityFacilities(cityScrollSite).FindIndex(n=>n["id"].S==id);CityScrollPick(id);}
            if(Btn("‹ 回到舆图"))SetCityMode(CityMode.Atlas);
            return;
        }
        T(node["name"].S,sub);T(node["label"].S+(node["description"].Has?" · "+node["description"].S:""),small);
        string kind=node["kind"].S;
        if(kind=="gate"){
            DrawAtlasWalk(new Vector2Int(node["row"].I,node["col"].I),HeroAtlasKey=="gate");
            if(CityLocal)DrawTravel();
        }else if(inSite){
            T(CityScrollHeroFacility()==node["id"].S?"你在此处":"同在"+site["name"].S+"，走过去不耗行动力",small);
            if(kind=="dock")DrawTravel();
        }else DrawAtlasWalk(SiteTarget(site),false);
        if((CityHasService(node,"market")||CityHasService(node,"workshop"))&&Btn("打开过客货栈 ›"))Later(()=>OpenMarket("货栈"));
        if((CityHasService(node,"training")||CityHasService(node,"temple")||CityHasService(node,"faction"))&&Btn("武学与调养 ›"))Later(()=>ViewPerson(-1,PersonTab.Martial));
        if(CityHasService(node,"government")&&Btn("查看所属官府 ›"))OpenInstitution(true,City["country"].I);
        if(node["faction"].Has&&Btn("查看门派 · 入门与传艺 ›"))OpenInstitution(false,node["faction"].I);
        T("在场 · "+node["residents"].A.Count+"人",sub);
        foreach(var resident in node["residents"].A)if(GUILayout.Button(resident["name"].S+" · "+resident["job"].S,chip))OpenInteraction(resident["id"].I,true);
        if(node["residents"].A.Count==0)T("此处眼下无人。",small);
        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        if(Btn("‹ 全部设施")){cityScrollPick="";mainScroll=Vector2.zero;}
        if(Btn("回到舆图"))SetCityMode(CityMode.Atlas);
        GUILayout.EndHorizontal();
    }
}
}
