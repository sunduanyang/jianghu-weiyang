using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    // Only presentation state is kept here. Following a record never advances the world.
    sealed class BookLocation {
        public int page,person,selected,tab,returnPerson,place,community,grade;
        public bool profile,dialogue;
        public string kind,bag,item,query,category,sort;
        public J institution,record;
        public Vector2 scroll,personScroll,eventScroll,itemScroll,detailScroll;
    }
    readonly Stack<BookLocation> bookTrail=new Stack<BookLocation>();
    int institutionRequest,eventRequest;
    string choiceKey="",choiceQuery="",choiceCurrent="",choiceCaption="",pendingChoiceKey="",pendingChoice="";
    string[] choiceOptions=new string[0];
    Vector2 choiceAnchor,choiceScroll;
    Rect choiceBounds;
    bool choiceOpen=>choiceKey.Length>0;
    string StageName=>basePage==1?"城中地图":"天下地图";
    string BookBackLabel=>bookTrail.Count>0?"返回上一页":dialogueReturnPerson>=0?"返回交谈":"返回"+StageName;
    string UiStatus=>!string.IsNullOrEmpty(Client.Error)?"操作未完成："+Client.Error:Client.Busy?"正在处理，请稍候…":Client.Status;

    void RememberBookLocation(){
        bookTrail.Push(new BookLocation {page=Page,person=bookPersonId,selected=SelectedPerson,tab=personTab,
            returnPerson=dialogueReturnPerson,place=SelectedPlace,community=communityPlace,
            profile=personScreen,dialogue=dialogueOpen,kind=institutionKind,bag=inventoryMode,
            institution=Institution,record=EventDetail,scroll=mainScroll,personScroll=personScroll,eventScroll=eventScroll,
            item=selectedItem,query=itemQuery,category=itemCategory,grade=itemGrade,sort=itemSort,itemScroll=itemScroll,detailScroll=detailScroll});
    }
    bool RestoreBookLocation(){
        if(bookTrail.Count==0)return false;
        var at=bookTrail.Pop();institutionRequest++;eventRequest++;
        Page=at.page;personScreen=at.profile;dialogueOpen=at.dialogue;bookPersonId=at.person;
        personTab=at.tab;dialogueReturnPerson=at.returnPerson;SelectedPlace=at.place;communityPlace=at.community;
        institutionKind=at.kind;inventoryMode=at.bag;Institution=at.institution;EventDetail=at.record;
        mainScroll=at.scroll;personScroll=at.personScroll;eventScroll=at.eventScroll;battleView=false;
        selectedItem=at.item;itemQuery=at.query;itemCategory=at.category;itemGrade=at.grade;itemSort=at.sort;itemScroll=at.itemScroll;detailScroll=at.detailScroll;
        if(at.selected>=0)SelectPerson(at.selected);
        if(Page==2)LoadInventory();
        if(Page==1&&City["place"].I!=SelectedPlace)LoadCity(SelectedPlace);
        return true;
    }
    void ReturnToStage(){
        bookTrail.Clear();dialogueReturnPerson=-1;personScreen=dialogueOpen=battleView=advanceOpen=false;
        CloseChoices();Go(basePage);
    }
    void NavigateBookTab(int target){
        bookTrail.Clear();
        if(target<0||target==2||target==3)OpenCharacterPage(target);
        else{personScreen=false;dialogueOpen=false;Go(target);}
    }
    void OpenLinkedPerson(int id){RememberBookLocation();SwitchBookPerson(id);OpenCharacterPage(-1);}
    void HandleUiInput(){
        var e=Event.current;
        if(choiceOpen){
            if(e.type==EventType.KeyDown&&e.keyCode==KeyCode.Escape){CloseChoices();e.Use();}
            else if(e.type==EventType.MouseDown&&!choiceBounds.Contains(e.mousePosition)){CloseChoices();e.Use();}
            return;
        }
        if(advanceOpen&&e.type==EventType.MouseDown&&!AdvancePopup.Contains(e.mousePosition)&&!Dock.Contains(e.mousePosition)){advanceOpen=false;e.Use();return;}
        if(e.type==EventType.KeyDown&&e.keyCode==KeyCode.Escape){
            if(advanceOpen){advanceOpen=false;e.Use();}
            else if(personScreen||!IsStagePage(Page)&&!battleView){CloseBook();e.Use();}
            else if(dialogueOpen){dialogueOpen=false;e.Use();}
            else if(creatingHero){creatingHero=false;e.Use();}
        }
    }
    void DrawUiOverlay(){
        if(choiceOpen){
            Vector2 anchor=GUIUtility.ScreenToGUIPoint(choiceAnchor);
            float height=Mathf.Min(444,116+choiceOptions.Length*36);
            choiceBounds=new Rect(Mathf.Clamp(anchor.x,16,1680-396),Mathf.Clamp(anchor.y,72,878-height),380,height);
            Fill(choiceBounds,new Color(.055f,.075f,.07f,1));Outline(choiceBounds,brass);
            GUI.ModalWindow(19274,choiceBounds,DrawChoiceWindow,"",card);
        }
        if(!string.IsNullOrEmpty(GUI.tooltip)){
            string hint=GUI.tooltip;float width=Mathf.Min(540,small.CalcSize(new GUIContent(hint)).x+24);
            Vector2 m=Event.current.mousePosition;
            Rect r=new Rect(Mathf.Clamp(m.x+14,8,1672-width),Mathf.Clamp(m.y+24,8,902),width,42);
            Frame(r,"panel_solid",12);GUI.Label(new Rect(r.x+10,r.y+5,r.width-20,r.height-10),hint,small);
        }
    }
    void DrawChoiceWindow(int id){
        GUILayout.BeginHorizontal();T("选择 · "+(choiceCaption.Length>0?choiceCaption:choiceCurrent),sub);GUILayout.FlexibleSpace();
        if(GUILayout.Button("关闭",chip,GUILayout.Width(62)))CloseChoices();GUILayout.EndHorizontal();
        if(choiceOptions.Length>8){
            GUILayout.BeginHorizontal();T("查找",small);GUI.SetNextControlName("ChoiceSearch");
            string next=GUILayout.TextField(choiceQuery);if(next!=choiceQuery){choiceQuery=next;choiceScroll=Vector2.zero;}
            GUILayout.EndHorizontal();
        }
        choiceScroll=GUILayout.BeginScrollView(choiceScroll);
        var rows=choiceOptions.Where(o=>o.IndexOf(choiceQuery,StringComparison.OrdinalIgnoreCase)>=0).ToArray();
        foreach(string option in rows)
            if(GUILayout.Button((option==choiceCurrent?"●  ":"    ")+option,option==choiceCurrent?rowOnStyle:rowStyle,GUILayout.Height(34))){
                pendingChoiceKey=choiceKey;pendingChoice=option;CloseChoices();
            }
        if(rows.Length==0)T("没有匹配项，请换个关键词。",small);
        GUILayout.EndScrollView();
    }
    void CloseChoices(){choiceKey="";choiceQuery="";if(Event.current!=null)GUI.FocusControl(null);}
    string Choose(string current,string[] options,string caption=""){
        if(options.Length==0)return current;
        string key=string.Join("\u001f",options);
        if(pendingChoiceKey==key){if(options.Contains(pendingChoice))current=pendingChoice;pendingChoiceKey="";}
        if(!options.Contains(current))current=options[0];
        Rect r=GUILayoutUtility.GetRect(new GUIContent(current+"  ▼"),chip,GUILayout.MinHeight(30),GUILayout.ExpandWidth(true));
        if(GUI.Button(r,new GUIContent(current+"  ▼","展开后直接选择"),chip)){
            choiceKey=key;choiceCurrent=current;choiceCaption=caption;choiceOptions=options;choiceQuery="";choiceScroll=Vector2.zero;
            choiceAnchor=GUIUtility.GUIToScreenPoint(new Vector2(r.x,r.yMax));
            choiceBounds=new Rect(-1,-1,0,0);GUI.FocusControl(null);
        }
        return current;
    }
    string FilterChoice(string label,string value,string[] options){
        GUILayout.BeginHorizontal();GUILayout.Label(label,small,GUILayout.Width(42));value=Choose(value,options,label);GUILayout.EndHorizontal();return value;
    }
    string SearchField(string value,string label){
        GUILayout.BeginHorizontal();GUILayout.Label(label,small,GUILayout.Width(42));
        value=GUILayout.TextField(value,field);
        if(GUILayout.Button(new GUIContent("清空","清除搜索关键词"),chip,GUILayout.Width(54)))value="";
        GUILayout.EndHorizontal();return value;
    }
    void ResetRosterFilters(){
        query="";countryFilter="全部势力";placeFilter="全部城镇";factionFilter="全部门派";
        circuitFilter="全部州路";jobFilter="百业";gradeFilter="全部品级";rosterSort="乡籍";includeDead=false;rosterScroll=Vector2.zero;
    }
    void DrawBookStatus(){
        string status=UiStatus;
        var style=new GUIStyle(small){wordWrap=false,clipping=TextClipping.Clip};
        if(Client.Error.Length>0)TextColor(style,new Color(1,.66f,.52f));
        GUI.Label(new Rect(790,69,670,23),new GUIContent(status,status),style);
    }
    void DrawSectionTabs(ref int current,string[] labels){
        GUILayout.BeginHorizontal();for(int i=0;i<labels.Length;i++)if(GUILayout.Button(labels[i],current==i?selected:chip)){current=i;mainScroll=Vector2.zero;}GUILayout.EndHorizontal();
    }
    void ResetWorldUi(){
        personFetchVersion++;institutionRequest++;eventRequest++;bookTrail.Clear();recentPeople.Clear();
        Person=new J();Institution=new J();EventDetail=new J();City=new J();Inventory=new J();Market=new J();Martial=new J();
        bookPersonId=SelectedPerson=dialogueReturnPerson=-1;personScreen=dialogueOpen=creatingHero=battleView=false;
        aiEditing=false;giftTarget=-1;lastMonth=-1;ResetRosterFilters();
    }
}
}
