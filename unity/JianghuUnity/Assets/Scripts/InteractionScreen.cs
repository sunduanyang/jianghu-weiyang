using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    static readonly string[] InteractionCategories={"交谈","比武","修习","亲近","敌对","互动"};
    static readonly string[] InteractionMarks={"言","武","学","亲","争","往"};
    static readonly Rect InteractionPanel=new Rect(440,92,800,790);

    // All portrait and resident links share this view. Each opening starts on 交谈;
    // returning from a character scroll keeps this view's person and selected category.
    void OpenInteraction(int id,bool right=false){
        if(DeferWhileDrawing(()=>OpenInteraction(id,right)))return;
        if(id<0||!Npc(id).Has)return;
        ResetInteractionConversation();interactionOptionsScroll=Vector2.zero;
        interactionChoice="";interactionResult="";interactionResultEvent=0;interactionItem="";interactionArt="";interactionData=new J();
        Show(new UiView{Kind=ViewKind.Interaction,Person=id,Tab=0});
    }
    void HandleInteractionKeys(){
        var e=Event.current;if(e.type!=EventType.KeyDown)return;
        if(InteractionFormOpen)return;
        int next=-1;
        if(e.keyCode>=KeyCode.F1&&e.keyCode<=KeyCode.F6)next=(int)e.keyCode-(int)KeyCode.F1;
        if(e.keyCode==KeyCode.Q)next=(TopView.Tab+5)%6;
        if(e.keyCode==KeyCode.E)next=(TopView.Tab+1)%6;
        if(next<0)return;
        int category=next;Later(()=>{interactionChoice="";SetTab(category);});e.Use();
    }
    void DrawInteractionScreen(){
        HandleInteractionKeys();
        Rect screen=new Rect(0,0,1680,960);Tile(screen,"bg");
        if(valley!=null){var old=GUI.color;GUI.color=new Color(.23f,.27f,.26f,1);GUI.DrawTexture(screen,valley,ScaleMode.ScaleAndCrop);GUI.color=old;}
        Fill(screen,new Color(.015f,.025f,.025f,.42f));Tex(screen,"vignette");
        GUI.Label(new Rect(440,34,800,36),"人 物 交 互",centerTitle);
        if(GUI.Button(new Rect(34,34,188,34),"‹ "+BookBackLabel,chip))Later(Back);

        // Reuse the world's own cast and landscape rather than the reference game's art.
        J other=CharacterReady?Person:Npc(FocusPersonId);
        DrawInteractionFigure(new Rect(34,262,380,362),Hero,true);
        DrawInteractionFigure(new Rect(1266,262,380,362),other,false);

        Rect panel=InteractionPanel;Fill(panel,new Color(.12f,.18f,.18f));Outline(panel,hairline,2);
        Rect scene=new Rect(panel.x+16,panel.y+16,panel.width-32,InteractionConversationOpen?80:128);
        if(valley!=null)GUI.DrawTexture(scene,valley,ScaleMode.ScaleAndCrop);
        Outline(scene,new Color(.39f,.43f,.38f),2);
        string place=Place(other["place"].I)["name"].S;
        if(place.Length>0){Rect label=new Rect(scene.x+14,scene.yMax-36,200,26);Fill(label,new Color(.045f,.07f,.065f,.88f));GUI.Label(label,place,centerSmall);}

        Rect tabs=new Rect(scene.x,scene.yMax+12,scene.width,50);
        Rect content=new Rect(scene.x,tabs.yMax+12,scene.width,panel.yMax-76-tabs.yMax-12);
        Rect words=new Rect(content.x,content.y,content.width,142);
        string greeting=!CharacterReady?"正在走近 "+CharacterName+"…":!other["alive"].B?"故人已逝，往事仍在。":"你与"+CharacterName+"相逢，尚未开口。";
        Rect options=new Rect(content.x,words.yMax+12,content.width,content.yMax-words.yMax-12);
        DrawInteractionCategories(tabs);
        if(InteractionConversationOpen)DrawInteractionConversation(content);
        else if(InteractionFormOpen)DrawInteractionForm(content);
        else{
            DrawInteractionWords(words,greeting);
            DrawInteractionOptions(options);
        }
        GUI.Label(new Rect(scene.x,options.yMax+20,580,32),"行动力 "+Hero["points"].S+" / 30  ·  互动不占用对方时间",small);
        if(GUI.Button(new Rect(scene.xMax-132,options.yMax+14,132,38),"道别  Esc",chip))Later(Back);

        Fill(new Rect(0,914,1680,46),new Color(.035f,.055f,.055f,.96f));
        GUI.Label(new Rect(320,924,1040,26),"F1 交谈     F2 比武     F3 修习     F4 亲近     F5 敌对     F6 互动     Esc 道别",centerSmall);
    }
    void DrawInteractionOptions(Rect area){
        Fill(area,new Color(.065f,.095f,.095f));Outline(area,hairline);
        GUI.Label(new Rect(area.x+22,area.y+12,area.width-44,26),InteractionCategories[TopView.Tab],sub);
        Fill(new Rect(area.x+18,area.y+45,area.width-36,1),hairline);
        var optionStyle=new GUIStyle(text){fontSize=18,alignment=TextAnchor.MiddleLeft};
        var numberStyle=new GUIStyle(centerSmall){fontSize=14};
        var pendingStyle=new GUIStyle(small){fontSize=13,alignment=TextAnchor.MiddleRight};
        var actions=InteractionDataReady?interactionData["conversations"].A.Where(a=>a["listed"].B).Concat(interactionData["actions"].A).Where(a=>a["category"].I==TopView.Tab).ToList():new System.Collections.Generic.List<J>();
        Rect viewport=new Rect(area.x+10,area.y+53,area.width-20,area.height-61);
        interactionOptionsScroll=GUI.BeginScrollView(viewport,interactionOptionsScroll,new Rect(0,0,viewport.width-20,actions.Count*55));
        for(int i=0;i<actions.Count;i++){
            Rect row=new Rect(0,i*55,viewport.width-20,50);
            J action=actions[i];string key=action["id"].S;
            Fill(row,interactionChoice==key&&key.Length>0?new Color(.22f,.28f,.25f):new Color(.095f,.135f,.135f));
            Fill(new Rect(row.x,row.yMax-1,row.width,1),hairline);
            GUI.Label(new Rect(row.x+8,row.y+6,50,38),"["+(i+1)+"]",numberStyle);
            GUI.Label(new Rect(row.x+70,row.y+6,row.width-206,38),action["label"].S,optionStyle);
            bool model=action["mode"].S=="conversation";
            string status=!Paused?"推演中":model&&!W["ai"]["config"]["enabled"].B?"需启用交谈":action["enabled"].B?action["points"].S+"行动力":"条件不足";
            GUI.Label(new Rect(row.xMax-104,row.y+6,84,38),status,pendingStyle);
            bool enabled=GUI.enabled;GUI.enabled=enabled&&InteractionDataReady&&!Client.Busy;
            if(GUI.Button(row,new GUIContent("",action["reason"].S.Length>0?action["reason"].S:action["description"].S),GUIStyle.none))Later(()=>ChooseInteraction(key));
            GUI.enabled=enabled;
        }
        GUI.EndScrollView();
        if(!InteractionDataReady)GUI.Label(new Rect(area.x+22,area.y+60,area.width-44,30),"正在读取往来事项…",small);
    }
    void DrawInteractionCategories(Rect r){
        float width=r.width/InteractionCategories.Length;
        for(int i=0;i<InteractionCategories.Length;i++){
            int category=i;bool active=TopView.Tab==i;Rect cell=new Rect(r.x+i*width,r.y,width,r.height);
            bool hover=cell.Contains(Event.current.mousePosition);
            Fill(cell,active?vermilion:hover?new Color(.25f,.34f,.33f):new Color(.18f,.26f,.26f));
            if(i>0)Fill(new Rect(cell.x,cell.y+14,1,cell.height-28),hairline);
            if(active)Fill(new Rect(cell.x,cell.yMax-3,cell.width,3),brass);
            var mark=new GUIStyle(centerTitle){fontSize=22};TextColor(mark,active?paperText:brass);
            GUI.Label(new Rect(cell.x+9,cell.y+9,32,36),InteractionMarks[i],mark);
            GUI.Label(new Rect(cell.x+40,cell.y+9,cell.width-44,36),InteractionCategories[i],new GUIStyle(sub){alignment=TextAnchor.MiddleCenter,fontSize=19});
            if(GUI.Button(cell,new GUIContent("","F"+(i+1)+" · "+InteractionCategories[i]),GUIStyle.none))Later(()=>{interactionChoice="";interactionOptionsScroll=Vector2.zero;SetTab(category);});
        }
    }
    void DrawInteractionFigure(Rect figure,J actor,bool player){
        string name=player?HeroName:CharacterName;int id=player?-1:FocusPersonId;
        var person=player?PaperPerson(-1,actor["sex"].S,actor["age_months"].I):actor;
        DrawPaperFigure(figure,PaperSkinFor(person,player),"bind",0,left:!player,bust:true);
        if(GUI.Button(figure,new GUIContent("","查看"+name+"的人物卷"),GUIStyle.none))Later(()=>ViewPerson(id));
    }
    // Figure rendering is deferred by PaperCast. Draw the nameplates after that pass
    // so the lower body stays behind the plates at every window scale.
    void DrawInteractionForeground(){
        DrawInteractionPersonInfo(new Rect(30,624,388,198),Hero,true);
        DrawInteractionPersonInfo(new Rect(1262,624,388,198),CharacterReady?Person:Npc(FocusPersonId),false);
    }
    void DrawInteractionPersonInfo(Rect plate,J actor,bool player){
        string name=player?HeroName:CharacterName;int id=player?-1:FocusPersonId;
        Fill(plate,new Color(.045f,.075f,.075f,.97f));Outline(plate,hairline);
        Rect nameplate=new Rect(plate.x,plate.y,plate.width,44);Fill(nameplate,new Color(.13f,.17f,.16f));
        Fill(new Rect(nameplate.x,nameplate.yMax-2,nameplate.width,2),vermilion);
        GUI.Label(nameplate,name,new GUIStyle(centerTitle){fontSize=24});
        string identity=actor["identity"].Has?actor["identity"]["grade_label"].S+" · "+actor["identity"]["title"].S:"江湖过客";
        GUI.Label(new Rect(plate.x+18,plate.y+56,plate.width-36,26),identity,sub);
        string life=(!player&&!actor["alive"].B)?"已故":(actor["age_months"].I/12)+"岁";
        string job=actor["job"].S;
        GUI.Label(new Rect(plate.x+18,plate.y+86,plate.width-36,24),life+(job.Length>0?" · "+job:"")+" · 心情 "+MoodWord(actor),small);
        var relation=actor["player_relation"];
        string detail=player?"银两 "+Num(W["player"]["wealth"])+" · 声望 "+Num(W["player"]["reputation"]):relation.Has?"情分 "+Num(relation["affinity"])+" · 信任 "+Num(relation["trust"]):"";
        if(!player){string bond=relation["married"].B?"配偶":relation["sworn"].B?"义亲":relation["confidant"].B?"知交":"";if(bond.Length>0)detail=bond+" · "+detail;}
        GUI.Label(new Rect(plate.x+18,plate.y+116,plate.width-36,24),detail,small);
        if(!player&&relation.Has){Rect bar=new Rect(plate.x+18,plate.y+145,plate.width-36,3);Fill(bar,hairline);Fill(new Rect(bar.x,bar.y,bar.width*Mathf.Clamp01(((float)relation["affinity"].N+100)/200),bar.height),brass);}
        float width=(plate.width-40)/3;
        if(GUI.Button(new Rect(plate.x+16,plate.yMax-40,width,28),"人物卷",chip))Later(()=>ViewPerson(id));
        if(GUI.Button(new Rect(plate.x+20+width,plate.yMax-40,width,28),"行囊",chip))Later(()=>ViewPerson(id,PersonTab.Bag));
        if(GUI.Button(new Rect(plate.x+24+width*2,plate.yMax-40,width,28),"武学",chip))Later(()=>ViewPerson(id,PersonTab.Martial));
    }
}
}
