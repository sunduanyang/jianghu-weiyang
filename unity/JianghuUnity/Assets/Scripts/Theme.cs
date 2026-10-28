using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jianghu {
// Dark lamplight skin with a layout modelled on 太吾绘卷 (navigation itself lives in UiNav.cs):
//   场景 — the map (天下 / 城中) fills the screen; panels float over it: top status bar, roster on the left,
//          city overview and place card on the right, and a bottom dock with the hero, the month dial and
//          function medallions. The globe at the bottom right switches 天下 ⇄ 城中.
//   卷轴 — 人物卷, 江湖卷 and 系统 open as full-screen scrolls with their own tab strip along the bottom.
// Textures come from unity/bake_ui.py; fonts are subsets of Noto Serif CJK SC (SIL OFL).
// Hand-painted replacements go in Resources/UI/Art/ under the same file name and win over the baked
// texture (bake_ui.py never writes there). Painted icons are optional: Resources/UI/Art/icon_<key>.png
// replaces the single glyph on a medallion, e.g. icon_bag.png for 囊. See unity/ART_PROMPTS.md.
public partial class JianghuApp {
    Font fontRegular, fontBold;
    readonly Dictionary<string,Texture2D> uiTextures=new Dictionary<string,Texture2D>();
    readonly Dictionary<string,GUIStyle> frameStyles=new Dictionary<string,GUIStyle>();
    GUIStyle tab, tabOn, rowStyle, rowOnStyle, linkStyle, linkOnStyle, sealSmall, mapLabel, mapLabelOn, mapHalo, mapHaloBold, caption, mapTitle,
        centerTitle, centerSmall, bigNumber, titleBrush, medalGlyph, medalLabel, tagStyle, tagOnStyle, dialogueStyle, valueStyle, labelRight, resGlyph, resValue, moonLabel, globeOn, globeOff;
    bool rosterOpen=true, advanceOpen;
    readonly Color vermilion=new Color(.56f,.17f,.13f), muted=new Color(.60f,.63f,.58f), paperText=new Color(.97f,.93f,.85f),
        hairline=new Color(.35f,.40f,.38f,.65f), brass=new Color(.79f,.64f,.35f,1f), jade=new Color(.24f,.56f,.48f), shadowInk=new Color(.08f,.06f,.04f,.92f);
    static readonly Vector2[] HaloOffsets={new Vector2(-1,0),new Vector2(1,0),new Vector2(0,-1),new Vector2(0,1),new Vector2(-1,-1),new Vector2(1,1),new Vector2(-1,1),new Vector2(1,-1)};

    // fixed layout of the 戏 screen (virtual 1680×960)
    static readonly Rect TopBar=new Rect(0,0,1680,58), Dock=new Rect(0,806,1680,154), PlaceCard=new Rect(1420,254,252,540), LayerRow=new Rect(680,66,320,34), AdvancePopup=new Rect(685,518,384,278);
    Rect LeftPanel=>rosterOpen?new Rect(8,70,262,640):new Rect(8,70,26,120);

    Texture2D UI(string name){if(!uiTextures.TryGetValue(name,out var t)){t=Resources.Load<Texture2D>("UI/Art/"+name);if(t==null)t=Resources.Load<Texture2D>("UI/"+name);uiTextures[name]=t;}return t;}
    static readonly Dictionary<string,string> IconKeys=new Dictionary<string,string>{
        {"人","person"},{"囊","bag"},{"武","martial"},{"势","faction"},{"市","market"},{"谱","chronicle"},{"影","puppet"},{"演","advance"},
        {"银","silver"},{"力","action"},{"望","fame"},{"体","health"},{"事","events"},{"义","bonds"}};
    // A painted icon for a medallion glyph, or null when only the baked skin is present.
    Texture2D Icon(string glyph,bool population=false){if(population)return UI("icon_population");return IconKeys.TryGetValue(glyph,out var key)?UI("icon_"+key):null;}
    void Glyph(Rect r,string glyph,GUIStyle style,bool population=false){var icon=Icon(glyph,population);if(icon!=null){float pad=r.width*.17f;GUI.DrawTexture(new Rect(r.x+pad,r.y+pad,r.width-pad*2,r.height-pad*2),icon,ScaleMode.ScaleToFit);}else GUI.Label(r,glyph,style);}
    Texture2D UIOr(string name,Color fallback){var t=UI(name);return t!=null?t:Solid(fallback);}
    static void Skin9(GUIStyle s,Texture2D normal,Texture2D hover,Texture2D active,int border){
        s.normal.background=normal;s.hover.background=hover??normal;s.active.background=active??hover??normal;s.focused.background=normal;
        s.onNormal.background=normal;s.onHover.background=hover??normal;s.onActive.background=active??normal;s.onFocused.background=normal;
        s.border=new RectOffset(border,border,border,border);
    }
    static void TextColor(GUIStyle s,Color c){s.normal.textColor=c;s.hover.textColor=c;s.active.textColor=c;s.focused.textColor=c;s.onNormal.textColor=c;s.onHover.textColor=c;s.onActive.textColor=c;s.onFocused.textColor=c;}
    void LoadFonts(){
        fontRegular=Resources.Load<Font>("Fonts/NotoSerifSC-Regular");fontBold=Resources.Load<Font>("Fonts/NotoSerifSC-Bold");
        if(fontRegular==null)fontRegular=Font.CreateDynamicFontFromOSFont(new[]{"Noto Serif CJK SC","SimSun","Microsoft YaHei","Arial"},16);
        if(fontBold==null)fontBold=fontRegular;
    }

    void InitSkin(){
        if(skin!=null)return;
        LoadFonts();
        skin=Instantiate(GUI.skin);skin.font=fontRegular;
        skin.label.font=fontRegular;skin.label.fontSize=15;TextColor(skin.label,ink);skin.label.wordWrap=true;

        var b=skin.button;
        Skin9(b,UIOr("btn",new Color(.17f,.20f,.19f)),UIOr("btn_hover",new Color(.22f,.26f,.25f)),UIOr("btn_active",new Color(.11f,.13f,.13f)),8);
        b.onNormal.background=UIOr("btn_on",vermilion);b.onHover.background=b.onNormal.background;b.onActive.background=b.onNormal.background;
        b.font=fontRegular;b.fontSize=14;TextColor(b,ink);b.onNormal.textColor=paperText;b.onHover.textColor=paperText;b.onActive.textColor=paperText;
        b.padding=new RectOffset(12,12,8,8);b.margin=new RectOffset(3,3,3,3);b.overflow=new RectOffset(0,0,0,0);b.wordWrap=true;

        var f=skin.textField;
        Skin9(f,UIOr("field",new Color(.07f,.09f,.09f)),null,null,7);
        f.font=fontRegular;f.fontSize=15;TextColor(f,ink);f.padding=new RectOffset(9,9,8,8);f.overflow=new RectOffset(0,0,0,0);
        skin.textArea=new GUIStyle(f){wordWrap=true};
        skin.settings.cursorColor=brass;skin.settings.selectionColor=new Color(.79f,.64f,.35f,.35f);

        var tg=skin.toggle;
        if(UI("toggle")!=null){
            tg.normal.background=UI("toggle");tg.hover.background=UI("toggle");tg.active.background=UI("toggle_on");tg.focused.background=UI("toggle");
            tg.onNormal.background=UI("toggle_on");tg.onHover.background=UI("toggle_on");tg.onActive.background=UI("toggle");tg.onFocused.background=UI("toggle_on");
            tg.border=new RectOffset(15,0,15,0);tg.overflow=new RectOffset(0,0,-4,0);tg.padding=new RectOffset(21,4,1,2);
        }
        tg.font=fontRegular;tg.fontSize=14;TextColor(tg,ink);tg.onNormal.textColor=brass;tg.onHover.textColor=brass;

        skin.box.normal.background=UIOr("card",new Color(.16f,.19f,.18f));skin.box.border=new RectOffset(8,8,8,8);

        if(UI("scroll_thumb")!=null){
            var vs=skin.verticalScrollbar;vs.normal.background=UI("scroll_track");vs.border=new RectOffset(0,0,6,6);vs.fixedWidth=12;vs.margin=new RectOffset(3,0,0,0);vs.padding=new RectOffset(0,0,0,0);
            var th=skin.verticalScrollbarThumb;th.normal.background=UI("scroll_thumb");th.hover.background=UI("scroll_thumb");th.active.background=UI("scroll_thumb");th.border=new RectOffset(0,0,6,6);th.fixedWidth=12;
        }
        if(UI("slider_track")!=null){
            var sl=skin.horizontalSlider;sl.normal.background=UI("slider_track");sl.border=new RectOffset(4,4,0,0);sl.fixedHeight=16;sl.overflow=new RectOffset(0,0,0,0);sl.padding=new RectOffset(0,0,0,0);
            var st=skin.horizontalSliderThumb;st.normal.background=UI("slider_thumb");st.hover.background=UI("slider_thumb");st.active.background=UI("slider_thumb");st.border=new RectOffset(0,0,0,0);st.overflow=new RectOffset(0,0,0,0);st.fixedWidth=16;st.fixedHeight=16;
        }

        text=new GUIStyle(skin.label){wordWrap=true,fontSize=15};
        small=new GUIStyle(text){fontSize=12};TextColor(small,muted);
        title=new GUIStyle(text){font=fontBold,fontSize=30};
        heading=new GUIStyle(text){font=fontBold,fontSize=20};heading.margin=new RectOffset(4,4,6,4);TextColor(heading,new Color(.93f,.86f,.70f));
        sub=new GUIStyle(text){font=fontBold,fontSize=16};
        chip=new GUIStyle(skin.button){fontSize=13,padding=new RectOffset(9,9,5,5)};
        selected=new GUIStyle(skin.button){font=fontBold};var onTex=UIOr("btn_on",vermilion);Skin9(selected,onTex,onTex,onTex,8);TextColor(selected,paperText);
        card=new GUIStyle(skin.box){padding=new RectOffset(14,14,12,12),margin=new RectOffset(3,3,5,5)};
        field=new GUIStyle(skin.textField);
        seal=new GUIStyle(title){alignment=TextAnchor.MiddleCenter,fontSize=22,wordWrap=false};TextColor(seal,paperText);
        sealSmall=new GUIStyle(seal){fontSize=15};

        tab=new GUIStyle(skin.button){font=fontRegular,fontSize=19,padding=new RectOffset(18,18,6,8),margin=new RectOffset(2,2,0,0),wordWrap=false,alignment=TextAnchor.MiddleCenter};
        Skin9(tab,null,UI("tab_hover"),UI("tab_hover"),6);TextColor(tab,ink);
        tabOn=new GUIStyle(tab){font=fontBold};var brush=UIOr("tab_brush",vermilion);Skin9(tabOn,brush,brush,brush,24);TextColor(tabOn,paperText);
        rowStyle=new GUIStyle(skin.button){alignment=TextAnchor.MiddleLeft,fontSize=14,padding=new RectOffset(12,10,8,8),margin=new RectOffset(0,0,0,0)};
        Skin9(rowStyle,UI("row"),UI("row_hover"),UI("row_on"),6);TextColor(rowStyle,ink);
        rowOnStyle=new GUIStyle(rowStyle);var rowSelected=UIOr("row_on",new Color(.40f,.14f,.10f));Skin9(rowOnStyle,rowSelected,rowSelected,rowSelected,6);
        linkStyle=new GUIStyle(skin.label){fontSize=14,alignment=TextAnchor.MiddleCenter,wordWrap=false,padding=new RectOffset(8,8,6,6)};TextColor(linkStyle,muted);linkStyle.hover.textColor=brass;
        linkOnStyle=new GUIStyle(linkStyle){font=fontBold};TextColor(linkOnStyle,brass);
        mapLabel=new GUIStyle(text){fontSize=13,wordWrap=false,alignment=TextAnchor.MiddleLeft,padding=new RectOffset(2,2,0,0),margin=new RectOffset(0,0,0,0)};TextColor(mapLabel,ink);
        mapLabelOn=new GUIStyle(mapLabel){font=fontBold};TextColor(mapLabelOn,brass);
        mapHalo=new GUIStyle(mapLabel);TextColor(mapHalo,new Color(.06f,.07f,.07f,.85f));
        mapHaloBold=new GUIStyle(mapLabelOn);TextColor(mapHaloBold,new Color(.06f,.07f,.07f,.85f));
        caption=new GUIStyle(small){alignment=TextAnchor.MiddleLeft,wordWrap=false};
        mapTitle=new GUIStyle(sub){alignment=TextAnchor.UpperCenter,fontSize=17,wordWrap=false};
        centerTitle=new GUIStyle(sub){fontSize=24,alignment=TextAnchor.MiddleCenter,wordWrap=false};TextColor(centerTitle,new Color(.95f,.88f,.72f));
        centerSmall=new GUIStyle(small){alignment=TextAnchor.MiddleCenter,wordWrap=false};
        bigNumber=new GUIStyle(sub){fontSize=44,alignment=TextAnchor.MiddleCenter,wordWrap=false};TextColor(bigNumber,new Color(.24f,.15f,.06f));
        titleBrush=new GUIStyle(sub){fontSize=64,alignment=TextAnchor.UpperLeft,wordWrap=false};TextColor(titleBrush,new Color(.79f,.64f,.35f,.55f));
        medalGlyph=new GUIStyle(sub){fontSize=24,alignment=TextAnchor.MiddleCenter,wordWrap=false};TextColor(medalGlyph,new Color(.93f,.83f,.60f));
        medalLabel=new GUIStyle(text){fontSize=14,alignment=TextAnchor.MiddleCenter,wordWrap=false};
        tagStyle=new GUIStyle(text){fontSize=13,alignment=TextAnchor.MiddleCenter,wordWrap=false,padding=new RectOffset(5,5,1,1),margin=new RectOffset(0,0,0,0)};
        Skin9(tagStyle,UIOr("tag",new Color(.07f,.09f,.09f,.8f)),null,null,4);TextColor(tagStyle,ink);
        tagOnStyle=new GUIStyle(tagStyle){font=fontBold};var tagOn=UIOr("tag_on",vermilion);Skin9(tagOnStyle,tagOn,tagOn,tagOn,4);TextColor(tagOnStyle,paperText);
        dialogueStyle=new GUIStyle(text){fontSize=21,alignment=TextAnchor.MiddleCenter,wordWrap=true};TextColor(dialogueStyle,new Color(.95f,.91f,.82f));
        valueStyle=new GUIStyle(sub){alignment=TextAnchor.MiddleRight,wordWrap=false,fontSize=15};
        labelRight=new GUIStyle(text){alignment=TextAnchor.MiddleRight,wordWrap=false};
        resGlyph=new GUIStyle(medalGlyph){fontSize=15};
        resValue=new GUIStyle(sub){wordWrap=false,fontSize=16};
        moonLabel=new GUIStyle(medalLabel);TextColor(moonLabel,new Color(.26f,.17f,.07f));
        globeOn=new GUIStyle(medalGlyph);TextColor(globeOn,new Color(.98f,.88f,.62f));
        globeOff=new GUIStyle(medalGlyph);TextColor(globeOff,muted);
    }

    // ---------------------------------------------------------------- drawing helpers
    void Tile(Rect r,string name){var t=UI(name);if(t==null){Fill(r,paper);return;}GUI.DrawTextureWithTexCoords(r,t,new Rect(r.x/t.width,r.y/t.height,r.width/t.width,r.height/t.height));}
    void Paper(Rect r,string name="paper"){Tile(r,name);}
    void Frame(Rect r,string name,int border){
        if(Event.current.type!=EventType.Repaint)return;var t=UI(name);if(t==null)return;
        if(!frameStyles.TryGetValue(name,out var s)){s=new GUIStyle();s.normal.background=t;s.border=new RectOffset(border,border,border,border);frameStyles[name]=s;}
        s.Draw(r,false,false,false,false);
    }
    void Panel(Rect r){Frame(r,"panel",24);}
    void BookPanel(Rect r){Frame(r,"panel",24);}
    void StagePanel(Rect r){var screen=UI("screen");if(screen!=null)GUI.DrawTexture(r,screen,ScaleMode.StretchToFill);else Fill(r,new Color(.96f,.89f,.74f));Frame(r,"frame_stage",14);}
    void Outline(Rect r,Color c,float w=1){Fill(new Rect(r.x,r.y,r.width,w),c);Fill(new Rect(r.x,r.yMax-w,r.width,w),c);Fill(new Rect(r.x,r.y,w,r.height),c);Fill(new Rect(r.xMax-w,r.y,w,r.height),c);}
    void Seal(Rect r,string s,GUIStyle style){var t=UI("seal");if(t!=null)GUI.DrawTexture(r,t,ScaleMode.StretchToFill);else Fill(r,vermilion);GUI.Label(r,s,style);}
    void Tex(Rect r,string name){var t=UI(name);if(t!=null)GUI.DrawTexture(r,t,ScaleMode.StretchToFill);}
    void HaloLabel(Rect r,string s,GUIStyle style,GUIStyle halo){
        if(Event.current.type==EventType.Repaint)foreach(var d in HaloOffsets)GUI.Label(new Rect(r.x+d.x,r.y+d.y,r.width,r.height),s,halo);
        GUI.Label(r,s,style);
    }
    Rect TagRect(Vector2 anchorLeft,string s,bool on){float w=(on?tagOnStyle:tagStyle).CalcSize(new GUIContent(s)).x+4;return new Rect(anchorLeft.x,anchorLeft.y-10,w,20);}
    void TagLabel(Rect r,string s,bool on){GUI.Label(r,s,on?tagOnStyle:tagStyle);}
    // A round medallion with a single glyph and a caption underneath; returns true when clicked.
    bool Medal(Vector2 c,float size,string glyph,string label,bool on=false,bool enabled=true){
        Rect r=new Rect(c.x-size/2,c.y-size/2,size,size);var old=GUI.color;if(!enabled)GUI.color=new Color(1,1,1,.45f);
        Tex(r,on?"medallion_on":"medallion");if(r.Contains(Event.current.mousePosition)&&enabled)Outline(new Rect(r.x+size*.12f,r.yMax-3,size*.76f,2),brass);
        Glyph(r,glyph,medalGlyph);if(!string.IsNullOrEmpty(label))GUI.Label(new Rect(c.x-50,r.yMax,100,22),label,medalLabel);
        GUI.color=old;bool hit=GUI.Button(new Rect(r.x,r.y,r.width,r.height+22),GUIContent.none,GUIStyle.none);return hit&&enabled&&!Client.Busy;
    }
    bool OverUI(Vector2 m){
        if(ViewOpen||choiceOpen||WorldTravelOpen)return true;
        if(TopBar.Contains(m)||LeftPanel.Contains(m)||PlaceCard.Contains(m))return true;
        if(CurrentStage==Stage.City&&CityOverviewRect.Contains(m))return true;
        if(m.y>Dock.y+40||new Rect(740,812,200,150).Contains(m)||new Rect(1520,780,160,180).Contains(m)||new Rect(0,712,300,248).Contains(m))return true;
        if(CurrentStage==Stage.World&&LayerRow.Contains(m))return true;
        if(advanceOpen&&AdvancePopup.Contains(m))return true;
        return false;
    }
    void Portrait(Rect r,int id,string sex,int ageMonths,bool silhouette=false){
        DrawPaperPortrait(r,PaperPerson(id,sex,ageMonths),id<0,silhouette);
    }

    // ---------------------------------------------------------------- 戏 screen chrome
    void Resource(ref float x,string glyph,string value,string label){
        Rect m=new Rect(x,12,32,32);Tex(m,"medallion");Glyph(m,glyph,resGlyph,label=="在世");
        GUI.Label(new Rect(x+36,8,110,22),value,resValue);GUI.Label(new Rect(x+36,29,110,18),label,caption);
        x+=Mathf.Max(96,resValue.CalcSize(new GUIContent(value)).x+50);
    }
    void DrawTopBar(){
        var t=UI("topbar");if(t!=null)GUI.DrawTextureWithTexCoords(TopBar,t,new Rect(0,0,TopBar.width/t.width,1));else Fill(TopBar,new Color(.07f,.09f,.09f,.95f));
        var p=W["player"];float x=14;
        Resource(ref x,"银",Num(p["wealth"]),"银两");
        Resource(ref x,"力",Hero["created"].B?Hero["points"].S+" / "+Hero["max_points"].S:"—","行动力");
        Resource(ref x,"望",Num(p["reputation"]),"声望");
        Resource(ref x,"体魄",Num(p["combat_attributes"]["base"]["constitution"]),"体魄");
        Resource(ref x,"体",Num(p["health"]),"体力");
        GUI.Label(new Rect(640,4,400,30),W["date"].S,centerTitle);
        GUI.Label(new Rect(640,32,400,20),W["battle_active"].B?"切磋进行中":W["autoplay"].B?"自行演化":W["job"]["remaining"].I>0?"推演中 · 余"+W["job"]["remaining"].S+"月":W["ai"]["busy"].B?"江湖已暂停 · 后台思考中":"江湖已暂停",centerSmall);
        Fill(new Rect(700,52,280,1),new Color(.79f,.64f,.35f,.6f));
        float rx=1110;
        Resource(ref rx,"人",W["population"].S,"在世");
        Resource(ref rx,"事",W["events"].S,"事件");
        Resource(ref rx,"义",W["sworn_bonds"].S,"结义");
        if(GUI.Button(new Rect(1576,12,96,34),"系统 · Esc",linkStyle))Later(()=>OpenSystem(SystemTab.Saves));
    }
    void DrawLayerRow(){
        GUILayout.BeginArea(LayerRow);GUILayout.BeginHorizontal();GUILayout.FlexibleSpace();
        GUILayout.Label("疆域",selected,GUILayout.Height(30));
        if(GUILayout.Button("全图",chip,GUILayout.Height(30))){mapZoom=1;mapPan=Vector2.zero;}
        GUILayout.FlexibleSpace();GUILayout.EndHorizontal();GUILayout.EndArea();
    }
    void DrawDock(){
        var bar=UI("bottombar");if(bar!=null)GUI.DrawTexture(Dock,bar,ScaleMode.StretchToFill);else Fill(new Rect(0,850,1680,110),new Color(.08f,.10f,.10f,.96f));
        // hero (left)
        Rect hp=new Rect(4,712,200,240);
        if(Hero["created"].B){DrawPaperBust(hp,Hero,true);GUI.Label(new Rect(208,880,200,24),Hero["name"].S,sub);GUI.Label(new Rect(208,904,260,20),Hero["place_name"].S+" · "+Hero["position_name"].S,caption);
            if(GUI.Button(new Rect(208,926,90,24),"定位主角",linkStyle)){SelectedPlace=Hero["place"].I;if(CurrentStage==Stage.City)LoadCity(SelectedPlace);else{mapZoom=1.6f;mapPan=new Vector2(640-(float)Place(SelectedPlace)["x"].N,380-(float)Place(SelectedPlace)["y"].N);}}
            if(GUI.Button(new Rect(308,926,110,24),"我的资料",linkStyle))Later(()=>ViewPerson(-1,PersonTab.Overview));}
        else{DrawPaperBust(hp,J.O("sex",heroSex,"age_months",int.TryParse(heroAge,out int ha)?ha*12:216),true);GUI.Label(new Rect(208,880,240,24),"尚未立主角",sub);if(GUI.Button(new Rect(208,906,110,30),"创建主角",selected))creatingHero=!creatingHero;}
        // month dial (centre)
        Rect moon=new Rect(765,812,150,150);Tex(moon,"moon");
        bool hero=Hero["created"].B;
        GUI.Label(new Rect(moon.x,moon.y+30,moon.width,60),hero?Hero["points"].S:"推",bigNumber);
        GUI.Label(new Rect(moon.x,moon.y+86,moon.width,24),hero?"结束本月":"推演一月",moonLabel);
        string monthHint=W["battle_active"].B?"请先结束切磋":!Paused?"请先暂停世界推演":hero?"你先行动；互动只耗你的行动力，结束本月后NPC独立行动":"推进世界一个月";
        bool previous=GUI.enabled;GUI.enabled=previous&&!Client.Busy&&!W["battle_active"].B&&(!hero||Paused);
        if(GUI.Button(moon,new GUIContent("",monthHint),GUIStyle.none)){if(hero)HeroAction("end_turn");else Control("advance",1);}GUI.enabled=previous;
        // 世界推演 folds into the month dial: a small toggle beside it opens the popup above.
        if(GUI.Button(new Rect(918,904,78,26),advanceOpen?"推演 ▼":"推演 ▲",advanceOpen?linkOnStyle:linkStyle))advanceOpen=!advanceOpen;
        // function medallions: 我的（人物卷各标签）left of the dial, 天下的（江湖卷各标签）right of it
        float y=900;
        GUI.Label(new Rect(450,845,270,24),"人物卷 · 我的",centerSmall);
        GUI.Label(new Rect(1010,845,270,24),"江湖卷",centerSmall);
        if(Medal(new Vector2(500,y),60,"人","人物"))Later(()=>ViewPerson(-1,PersonTab.Overview));
        if(Medal(new Vector2(585,y),60,"囊","行囊"))Later(()=>ViewPerson(-1,PersonTab.Bag));
        if(Medal(new Vector2(670,y),60,"武","武学"))Later(()=>ViewPerson(-1,PersonTab.Martial));
        if(Medal(new Vector2(1060,y),60,"势","势力"))Later(()=>OpenJianghu(JianghuTab.Powers));
        if(Medal(new Vector2(1145,y),60,"市","市井"))Later(()=>OpenJianghu(JianghuTab.Market));
        if(Medal(new Vector2(1230,y),60,"谱","年谱"))Later(()=>OpenJianghu(JianghuTab.Chronicle));
        // world / city globe (bottom right)
        Rect g=new Rect(1528,792,144,144);Tex(g,"globe");
        Fill(new Rect(g.x+30,g.center.y,g.width-60,1),new Color(.79f,.64f,.35f,.6f));
        GUI.Label(new Rect(g.x,g.y+26,g.width,40),"天下",CurrentStage==Stage.World?globeOn:globeOff);
        GUI.Label(new Rect(g.x,g.y+78,g.width,40),"城中",CurrentStage==Stage.City?globeOn:globeOff);
        if(GUI.Button(new Rect(g.x,g.y,g.width,g.height/2),GUIContent.none,GUIStyle.none)&&CurrentStage!=Stage.World)Later(()=>GoStage(Stage.World));
        if(GUI.Button(new Rect(g.x,g.center.y,g.width,g.height/2),GUIContent.none,GUIStyle.none)&&CurrentStage!=Stage.City)Later(()=>GoStage(Stage.City));
        // status toast above the dial
        string status=UiStatus;
        if(!string.IsNullOrEmpty(status)){float w=Mathf.Min(760,small.CalcSize(new GUIContent(status)).x+30);Rect sr=new Rect(840-w/2,776,w,24);GUI.Label(sr,new GUIContent(status,status),tagStyle);}
    }
    void DrawHeroCreation(){
        if(Hero["created"].B||!creatingHero)return;
        Rect r=new Rect(20,560,330,170);Panel(r);
        GUILayout.BeginArea(new Rect(r.x+14,r.y+10,r.width-28,r.height-18));
        GUILayout.Label("立一名主角，走自己的路",sub);
        GUILayout.BeginHorizontal();heroName=GUILayout.TextField(heroName,GUILayout.Width(120));heroSex=Cycle(heroSex,new[]{"男","女"});heroAge=GUILayout.TextField(heroAge,GUILayout.Width(50));GUILayout.Label("岁",small);GUILayout.EndHorizontal();
        GUILayout.Label("出身："+Place(SelectedPlace)["name"].S+"（在舆图上点选城镇）",small);
        if(Btn("此身入江湖",Paused)&&int.TryParse(heroAge,out int age)){HeroAction("create","name",heroName,"sex",heroSex,"age",age,"place",SelectedPlace);creatingHero=false;}
        GUILayout.EndArea();
    }
    void DrawAdvance(){
        if(!advanceOpen)return;Panel(AdvancePopup);
        GUILayout.BeginArea(new Rect(AdvancePopup.x+14,AdvancePopup.y+10,AdvancePopup.width-28,AdvancePopup.height-18));
        GUILayout.BeginHorizontal();GUILayout.Label("世界推演",sub);GUILayout.FlexibleSpace();if(GUILayout.Button("关闭",chip,GUILayout.Width(58)))advanceOpen=false;GUILayout.EndHorizontal();bool calm=!W["battle_active"].B;
        T("快进会推进整个世界；主角行动请用「结束本月」。",small);
        T("推进时长",small);
        GUILayout.BeginHorizontal();if(Btn("一月",calm))Control("advance",1);if(Btn("一年",calm))Control("advance",12);if(Btn("十年",calm))Control("advance",120);if(Btn("五十年",calm))Control("advance",600);GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();if(Btn("自行演化",calm))Control("auto");if(Btn("暂停"))Control("pause");if(Btn("保存"))Client.Act("/api/save",J.O());GUILayout.EndHorizontal();
        fastMode=GUILayout.Toggle(fastMode,"规则快进（跳过模型调用）");
        T(W["battle_active"].B?"切磋进行中，请先收招。":W["autoplay"].B?"正在自行演化，可随时暂停。":W["job"]["remaining"].I>0?"正在推演，剩余 "+W["job"]["remaining"].S+" 月。":"当前世界已暂停。",small);
        GUILayout.EndArea();
    }

    // ---------------------------------------------------------------- 卷轴 screens
    // Full-screen scroll chrome: big faint title and a close mark (same as Esc: back to where it was opened).
    void BookScreen(string heading){
        Tile(new Rect(0,0,1680,960),"bg");Tex(new Rect(0,0,1680,960),"vignette");
        GUI.Label(new Rect(60,18,710,42),heading,title);
        GUI.Label(new Rect(60,69,710,23),StageName+"  /  "+(TopIs(ViewKind.Person)?"人物卷":TopIs(ViewKind.Jianghu)?"江湖卷":"系统"),small);
        GUI.Label(new Rect(790,23,500,30),TopIs(ViewKind.Person)?(InspectingNpc?"正在查看他人 · ":"我的人物 · ")+CharacterName:"",sub);
        if(GUI.Button(new Rect(1310,20,166,36),new GUIContent("‹ "+BookBackLabel,"Esc 返回"),chip))Later(Back);
        if(GUI.Button(new Rect(1490,20,150,36),"返回地图",chip))Later(ReturnToStage);
        DrawBookStatus();Fill(new Rect(40,101,1600,1),hairline);
        Fill(new Rect(40,890,1600,1),hairline);
    }
    // The scroll's own tabs along the bottom. Returns the tab clicked this event, or -1.
    int BookTabStrip(string[] names,int current){
        int clicked=-1;
        GUILayout.BeginArea(new Rect(40,896,1600,52));GUILayout.BeginHorizontal();GUILayout.FlexibleSpace();
        for(int i=0;i<names.Length;i++)if(GUILayout.Button(names[i],i==current?tabOn:tab,GUILayout.Height(46),GUILayout.MinWidth(118)))clicked=i;
        GUILayout.FlexibleSpace();GUILayout.EndHorizontal();GUILayout.EndArea();
        return clicked;
    }
}
}
