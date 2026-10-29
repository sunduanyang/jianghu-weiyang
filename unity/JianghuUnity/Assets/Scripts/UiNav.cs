using System;
using System.Collections.Generic;
using UnityEngine;

namespace Jianghu {
// 界面导航，仿太吾绘卷：
//   底层场景  天下 / 城中，永远在最下面；
//   全屏视图  人物卷、江湖卷、系统、对话、切磋，从哪里打开就叠在哪里之上，关闭即回到原处。
// 所有跳转都经过 Show / Back / GoStage；Esc 与卷轴右上角的关闭都等于 Back。
public enum Stage { World, City }
public enum ViewKind { Person, Jianghu, System, Dialogue, Battle, Interaction }
public enum PersonTab { Overview, Character, Martial, Bag, Ties, Life }
public enum JianghuTab { Powers, Market, Chronicle }
public enum SystemTab { Saves, Model, Puppets, Rigging }

public sealed class UiView {
    public ViewKind Kind;
    public int Tab;        // PersonTab / JianghuTab / SystemTab / interaction category
    public int Person=-1;  // 人物卷所看之人或对话对象；-1 为主角
    internal JianghuApp.ViewState State;
}

public partial class JianghuApp {
    public static readonly string[] PersonTabNames={"概览","品性","武学","行囊","人情","生平"};
    public static readonly string[] JianghuTabNames={"势力","市井","年谱"};
    public static readonly string[] SystemTabNames={"存档","本地模型","皮影衣橱","关节绑定"};

    public Stage CurrentStage{get;private set;}=Stage.World;
    readonly List<UiView> views=new List<UiView>();
    Action deferred;
    bool drawingGui;   // true while OnGUI draws; view changes requested then wait until the pass ends

    public UiView TopView=>views.Count>0?views[views.Count-1]:null;
    public bool ViewOpen=>views.Count>0;
    public bool TopIs(ViewKind kind)=>views.Count>0&&views[views.Count-1].Kind==kind;
    public bool DialogueVisible=>TopIs(ViewKind.Dialogue);
    public bool BattleVisible=>TopIs(ViewKind.Battle);
    public PersonTab CurrentPersonTab=>TopIs(ViewKind.Person)?(PersonTab)TopView.Tab:PersonTab.Overview;
    public JianghuTab CurrentJianghuTab=>TopIs(ViewKind.Jianghu)?(JianghuTab)TopView.Tab:JianghuTab.Powers;
    public SystemTab CurrentSystemTab=>TopIs(ViewKind.System)?(SystemTab)TopView.Tab:SystemTab.Saves;
    // 正在看谁：人物卷的主人或对话对象；-1 为主角（或当前不在看人）。
    public int FocusPersonId=>views.Count>0&&(TopView.Kind==ViewKind.Person||TopView.Kind==ViewKind.Dialogue||TopView.Kind==ViewKind.Interaction)?TopView.Person:-1;

    // Run a state change after this OnGUI pass, so the layout pass and the event pass draw the same controls.
    void Later(Action action){deferred+=action;}
    void RunDeferred(){drawingGui=false;var run=deferred;deferred=null;run?.Invoke();}
    // Every view change goes through here: deferred while drawing, immediate from Update, callbacks and smokes.
    bool DeferWhileDrawing(Action action){if(!drawingGui)return false;Later(action);return true;}

    // ---------------------------------------------------------------- moving between views
    // Each view opens on top of the one it was opened from, so closing it returns there. A kind that is
    // already open further down is unwound to and replaced, so the stack never cycles (at most one per kind).
    void Show(UiView view){
        if(DeferWhileDrawing(()=>Show(view)))return;
        SaveViewState();
        CloseTransient();
        int at=views.FindIndex(v=>v.Kind==view.Kind);
        if(at>=0)views.RemoveRange(at,views.Count-at);
        views.Add(view);
        OnViewChanged();
    }
    public void Back(){
        if(DeferWhileDrawing(Back))return;
        if(views.Count==0)return;
        if(TopIs(ViewKind.Battle)){CloseBattle();return;}
        views.RemoveAt(views.Count-1);OnViewChanged(true);
    }
    public void GoStage(Stage stage,bool reload=true){
        if(DeferWhileDrawing(()=>GoStage(stage,reload)))return;
        views.Clear();CloseTransient();InvalidateViewRequests();CurrentStage=stage;mainScroll=Vector2.zero;
        if(reload)ReloadView();
    }
    void OnViewChanged(bool restore=false){
        InvalidateViewRequests();
        mainScroll=personScroll=itemScroll=detailScroll=eventScroll=Vector2.zero;
        var top=TopView;
        if(top!=null&&(top.Kind==ViewKind.Person||top.Kind==ViewKind.Dialogue||top.Kind==ViewKind.Interaction)&&top.Person>=0&&SelectedPerson!=top.Person)SelectPerson(top.Person);
        if(top!=null&&top.Kind==ViewKind.Dialogue){talk="";conversationScroll=new Vector2(0,float.MaxValue);nextConversationPoll=0;}
        if(restore&&top?.State!=null)RestoreViewState(top.State);
        ReloadView();
    }
    void SetTab(int tab){
        if(DeferWhileDrawing(()=>SetTab(tab)))return;
        if(views.Count==0)return;
        CloseChoices();InvalidateViewRequests();TopView.Tab=tab;mainScroll=personScroll=itemScroll=detailScroll=eventScroll=Vector2.zero;
        ReloadView();
    }
    void CloseTransient(){advanceOpen=false;CloseChoices();CloseWorldTravel();}

    // 人物卷：id 为 -1 时看主角。不给 tab 时沿用已打开人物卷的标签。
    public void ViewPerson(int id,PersonTab? tab=null){
        if(DeferWhileDrawing(()=>ViewPerson(id,tab)))return;
        var keep=TopIs(ViewKind.Person)?(PersonTab)TopView.Tab:PersonTab.Overview;
        Show(new UiView{Kind=ViewKind.Person,Person=id,Tab=(int)(tab??keep)});
        ResetItemFilters(id);
    }
    // Switch whose 人物卷 is open, keeping the tab.
    void SwitchSubject(int id){
        if(DeferWhileDrawing(()=>SwitchSubject(id)))return;
        if(!TopIs(ViewKind.Person)){ViewPerson(id);return;}
        ResetItemFilters(id);TopView.Person=id;OnViewChanged();
    }
    public void OpenJianghu(JianghuTab tab)=>Show(new UiView{Kind=ViewKind.Jianghu,Tab=(int)tab});
    public void OpenSystem(SystemTab tab)=>Show(new UiView{Kind=ViewKind.System,Tab=(int)tab});
    public void OpenMarket(string mode){marketMode=mode;OpenJianghu(JianghuTab.Market);}
    public void TalkTo(int id,string action=null){
        if(id<0)return;
        if(DeferWhileDrawing(()=>TalkTo(id,action)))return;
        OpenInteraction(id);ChooseInteraction(action=="gift"?"social:help":"social:talk");
    }
    public void OpenBattle()=>Show(new UiView{Kind=ViewKind.Battle});
    void CloseBattle(){
        if(DeferWhileDrawing(CloseBattle))return;
        int at=views.FindIndex(v=>v.Kind==ViewKind.Battle);
        if(at<0)return;
        views.RemoveRange(at,views.Count-at);OnViewChanged(true);
        if(SelectedPerson>=0)SelectPerson(SelectedPerson);
    }

    // Monthly refresh and the data each view needs when it opens.
    public void ReloadView(){
        if(!Client.Ready)return;
        var top=TopView;
        if(top==null){if(CurrentStage==Stage.City)LoadCity(SelectedPlace);return;}
        switch(top.Kind){
            case ViewKind.Interaction:
                LoadInteraction();
                break;
            case ViewKind.Person:
                var tab=(PersonTab)top.Tab;
                if(tab==PersonTab.Martial)LoadMartialView();
                if(top.Person>=0)break;
                if(tab==PersonTab.Bag)LoadInventory();
                else if(tab==PersonTab.Ties)LoadEconomy();
                break;
            case ViewKind.Battle:
                // A cold start has no cached Martial data. The city resume entry must load
                // the saved session itself instead of requiring a visit to the martial tab.
                if(PhysicsArena==null)LoadMartialView();
                break;
            case ViewKind.Jianghu:
                var j2=(JianghuTab)top.Tab;
                if(j2==JianghuTab.Market)LoadInventory();
                else if(j2==JianghuTab.Chronicle)Client.Fetch("/api/events",j=>Events=j);
                break;
            case ViewKind.System:
                if((SystemTab)top.Tab==SystemTab.Model)Client.Fetch("/api/ai/logs",j=>Logs=j);
                break;
        }
    }

    // Esc closes the innermost thing: a popup, then the top view; on the bare map it opens 系统.
    bool HandleEscape(){
        if(GUIUtility.hotControl!=0)return false;
        if(WorldTravelOpen){Later(CloseWorldTravel);return true;}
        if(choiceOpen){Later(CloseChoices);return true;}
        if(TopIs(ViewKind.Interaction)&&interactionChoice.Length>0){Later(()=>interactionChoice="");return true;}
        if(advanceOpen||creatingHero&&!Hero["created"].B){Later(()=>{advanceOpen=false;creatingHero=false;});return true;}
        if(ViewOpen){Later(Back);return true;}
        if(CurrentStage==Stage.City&&cityWalking)return false;   // UpdateCityMovement stops the walk
        Later(()=>OpenSystem(SystemTab.Saves));return true;
    }
}
}
