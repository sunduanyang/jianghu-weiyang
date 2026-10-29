using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Jianghu;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Local playtest configuration UI. Only the detached draft is edited here;
// persistence, pausing, input ownership and arena transitions belong to host.
public sealed class OriginalPlaytestSetupMenu : MonoBehaviour {
    const int PageSize=10;
    static readonly Color Ink=new Color(.095f,.064f,.039f,1),Panel=new Color(.145f,.100f,.061f,.97f),Raised=new Color(.205f,.146f,.086f,1);
    static readonly Color Gold=new Color(.89f,.72f,.36f,1),Ivory=new Color(.96f,.91f,.79f,1),Muted=new Color(.66f,.59f,.46f,1),Selected=new Color(.32f,.235f,.115f,1);
    OriginalPreview host;
    OriginalPlaytestSetup draft;
    Font font;
    RectTransform shell;
    Button entry,start,previous,next;
    Dropdown region;
    InputField level,search;
    Text limits,error,currentSlot,count,pageLabel,selectedName,selectedId,selectedKind,damage,defense,weight,summary,emptyList,emptyPreview;
    Image spritePreview;
    RawImage texturePreview;
    AspectRatioFitter textureAspect;
    readonly List<SlotView> slots=new List<SlotView>();
    readonly List<ChoiceView> rows=new List<ChoiceView>();
    readonly List<OriginalPlaytestRegion> regions=new List<OriginalPlaytestRegion>();
    List<OriginalPlaytestChoice> filtered=new List<OriginalPlaytestChoice>();
    string slotKey="MainHand";
    int page;
    sealed class SlotView {public string key,label;public Button button;public Text text;public GameObject marker;}
    sealed class ChoiceView {public Button button;public Text id,label,stats,status;public OriginalPlaytestChoice choice;}
    public GameObject MainOverlayRoot {get;private set;}
    public bool IsOpen=>MainOverlayRoot!=null&&MainOverlayRoot.activeSelf;

    public static OriginalPlaytestSetupMenu Attach(OriginalPreview owner) {
        if(owner==null)throw new ArgumentNullException(nameof(owner));
        var existing=owner.GetComponentInChildren<OriginalPlaytestSetupMenu>(true);if(existing!=null)return existing;
        var root=new GameObject("Original playtest setup",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
        root.layer=5;root.transform.SetParent(owner.transform,false);
        var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.overrideSorting=true;canvas.sortingOrder=31000;
        var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1600,900);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
        var menu=root.AddComponent<OriginalPlaytestSetupMenu>();menu.host=owner;
        menu.font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","SimHei","Arial"},18);
        if(menu.font==null)throw new InvalidOperationException("无法创建出战配置的中文字体。");
        menu.Build();menu.Hide();return menu;
    }
    void Update() {
        // F2/Escape are handled once by OriginalPreview, before battle input.
        bool visible=host!=null&&host.player!=null&&!host.IsTransitioning&&!IsOpen;
        if(entry!=null&&entry.gameObject.activeSelf!=visible)entry.gameObject.SetActive(visible);
    }
    GameObject Node(string name,Transform parent) {
        var go=new GameObject(name,typeof(RectTransform));go.layer=5;go.transform.SetParent(parent,false);return go;
    }
    static RectTransform Rect(GameObject go)=>(RectTransform)go.transform;
    static void Place(RectTransform rect,float x,float y,float w,float h) {
        rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(w,h);
    }
    static void Stretch(RectTransform rect,float inset=0) {
        rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.pivot=new Vector2(.5f,.5f);rect.offsetMin=new Vector2(inset,inset);rect.offsetMax=new Vector2(-inset,-inset);
    }
    Image Background(GameObject node,Color color,bool raycast=true) {
        var image=node.AddComponent<Image>();image.color=color;image.raycastTarget=raycast;return image;
    }
    void Border(GameObject go,Color? color=null) {
        var outline=go.AddComponent<Outline>();outline.effectColor=color??new Color(Gold.r,Gold.g,Gold.b,.38f);outline.effectDistance=new Vector2(1,-1);outline.useGraphicAlpha=false;
    }
    Text Label(string name,Transform parent,string text,float x,float y,float w,float h,int size=18,Color? color=null,TextAnchor anchor=TextAnchor.MiddleLeft,bool bold=false) {
        var go=Node(name,parent);Place(Rect(go),x,y,w,h);var label=go.AddComponent<Text>();label.font=font;label.text=text;label.fontSize=size;label.color=color??Ivory;
        label.fontStyle=bold?FontStyle.Bold:FontStyle.Normal;label.alignment=anchor;label.supportRichText=false;label.raycastTarget=false;label.lineSpacing=1.08f;
        label.horizontalOverflow=HorizontalWrapMode.Wrap;label.verticalOverflow=VerticalWrapMode.Truncate;return label;
    }
    Button Button(string name,Transform parent,string text,float x,float y,float w,float h,Action click,bool primary=false) {
        var go=Node(name,parent);Place(Rect(go),x,y,w,h);var image=Background(go,primary?Gold:Raised);Border(go);
        var button=go.AddComponent<Button>();button.targetGraphic=image;var colors=button.colors;
        colors.normalColor=Color.white;colors.highlightedColor=new Color(1,1,.90f);colors.pressedColor=new Color(.76f,.68f,.48f);colors.selectedColor=Color.white;colors.disabledColor=new Color(.55f,.55f,.55f,.65f);colors.fadeDuration=.08f;button.colors=colors;
        if(!string.IsNullOrEmpty(text))Label("Label",go.transform,text,5,0,w-10,h,primary?21:18,primary?Ink:Ivory,TextAnchor.MiddleCenter,primary);
        if(click!=null)button.onClick.AddListener(()=>click());return button;
    }
    RectTransform Card(string name,float x,float y,float w,float h) {
        var go=Node(name,shell);Place(Rect(go),x,y,w,h);Background(go,Panel);Border(go);
        var line=Node("GoldRule",go.transform);Place(Rect(line),0,0,w,2);Background(line,new Color(Gold.r,Gold.g,Gold.b,.8f),false);return Rect(go);
    }
    InputField Input(string name,Transform parent,string placeholder,float x,float y,float w,float h,bool integer=false) {
        var go=Node(name,parent);Place(Rect(go),x,y,w,h);var image=Background(go,Ink);Border(go);
        var field=go.AddComponent<InputField>();field.targetGraphic=image;field.contentType=integer?InputField.ContentType.IntegerNumber:InputField.ContentType.Standard;
        field.lineType=InputField.LineType.SingleLine;field.characterLimit=integer?12:100;field.customCaretColor=true;field.caretColor=Gold;field.selectionColor=new Color(Gold.r,Gold.g,Gold.b,.35f);
        var text=Label("Text",go.transform,"",12,0,w-24,h,19);text.horizontalOverflow=HorizontalWrapMode.Overflow;field.textComponent=text;
        field.placeholder=Label("Placeholder",go.transform,placeholder,12,0,w-24,h,17,Muted);return field;
    }
    Dropdown RegionDropdown(Transform parent,float x,float y,float w,float h) {
        var go=Node("SetupRegion",parent);Place(Rect(go),x,y,w,h);var image=Background(go,Ink);Border(go);
        var dropdown=go.AddComponent<Dropdown>();dropdown.targetGraphic=image;
        dropdown.captionText=Label("Label",go.transform,"",12,0,w-48,h,18);Label("Arrow",go.transform,"▼",w-36,0,24,h,13,Gold,TextAnchor.MiddleCenter);
        var template=Node("Template",go.transform);var tr=Rect(template);tr.anchorMin=new Vector2(0,0);tr.anchorMax=new Vector2(1,0);tr.pivot=new Vector2(.5f,1);tr.anchoredPosition=new Vector2(0,-4);tr.sizeDelta=new Vector2(0,204);Background(template,Raised);Border(template);
        // UGUI otherwise gives a newly created popup Canvas order30000, below
        // this menu's order31000 backdrop. Keep popup and its blocker above it.
        var popupCanvas=template.AddComponent<Canvas>();popupCanvas.overrideSorting=true;popupCanvas.sortingOrder=31002;template.AddComponent<GraphicRaycaster>();
        var viewport=Node("Viewport",template.transform);Stretch(Rect(viewport),5);viewport.AddComponent<RectMask2D>();
        var content=Node("Content",viewport.transform);var cr=Rect(content);cr.anchorMin=new Vector2(0,1);cr.anchorMax=Vector2.one;cr.pivot=new Vector2(.5f,1);cr.sizeDelta=new Vector2(0,44);
        var item=Node("Item",content.transform);var ir=Rect(item);ir.anchorMin=new Vector2(0,.5f);ir.anchorMax=new Vector2(1,.5f);ir.pivot=new Vector2(.5f,.5f);ir.sizeDelta=new Vector2(0,44);
        var itemImage=Background(item,Ink);var toggle=item.AddComponent<Toggle>();toggle.targetGraphic=itemImage;
        var check=Node("Checkmark",item.transform);Place(Rect(check),10,13,16,18);toggle.graphic=Background(check,Gold,false);
        dropdown.itemText=Label("ItemLabel",item.transform,"地区",38,0,w-55,44,18);
        var scroll=template.AddComponent<ScrollRect>();scroll.viewport=Rect(viewport);scroll.content=cr;scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;
        dropdown.template=tr;template.SetActive(false);return dropdown;
    }
    void Build() {
        MainOverlayRoot=Node("MainOverlayRoot",transform);Stretch(Rect(MainOverlayRoot));Background(MainOverlayRoot,new Color(.035f,.025f,.017f,.91f));
        var shellNode=Node("SetupFrame",MainOverlayRoot.transform);shell=Rect(shellNode);shell.anchorMin=shell.anchorMax=shell.pivot=new Vector2(.5f,.5f);shell.sizeDelta=new Vector2(1600,900);
        Label("SetupTitle",shell,"出战配置",38,24,600,55,36,Gold,bold:true);
        Label("SetupSubtitle",shell,"选择战场，搭配装备，然后开始新的战斗。",40,78,1000,27,18,Muted);
        Label("SetupTag",shell,"本地试玩",1360,33,200,32,18,Gold,TextAnchor.MiddleRight);
        Label("RegionLabel",shell,"地区",40,116,65,44,18,Muted);region=RegionDropdown(shell,106,116,310,44);
        Label("LevelLabel",shell,"关卡",442,116,65,44,18,Muted);
        Button("SetupLevelMinus",shell,"−",506,116,46,44,()=>StepLevel(-1));level=Input("SetupLevel",shell,"1",560,116,100,44,true);
        Button("SetupLevelPlus",shell,"+",668,116,46,44,()=>StepLevel(1));limits=Label("SetupLevelLimits",shell,"",736,116,680,44,17,Muted);
        var left=Card("SetupSlots",38,180,230,578);Label("SlotsHeading",left,"装备部位",18,14,194,32,22,Gold,bold:true);
        int slotIndex=0;
        foreach(var slot in host.SetupCatalog.Slots) {
            var view=new SlotView{key=slot.Key,label=slot.Label};int rowIndex=slotIndex++;
            view.button=Button("Slot_"+slot.Key,left,"",12,56+rowIndex*42,206,36,()=>ChooseSlot(view.key));
            view.text=Label("SlotLabel",view.button.transform,slot.Label,16,0,178,36,17);
            var marker=Node("SelectedMarker",view.button.transform);Place(Rect(marker),0,0,4,36);Background(marker,Gold,false);view.marker=marker;slots.Add(view);
        }
        var middle=Card("SetupEquipment",286,180,752,578);
        currentSlot=Label("SetupCurrentSlot",middle,"主手",18,12,475,36,23,Gold,bold:true);count=Label("SetupChoiceCount",middle,"",493,12,241,36,15,Muted,TextAnchor.MiddleRight);
        search=Input("SetupSearch",middle,"搜索英文名称 / 装备编号",18,57,638,42);Button("SetupClearSearch",middle,"清空",666,57,68,42,()=>search.text="");
        var list=Node("SetupChoiceList",middle);Place(Rect(list),18,113,716,408);
        for(int i=0;i<PageSize;i++) {
            var row=new ChoiceView();row.button=Button("ChoiceRow_"+i,list.transform,"",0,i*40,716,36,()=>SelectChoice(row.choice));
            row.id=Label("ChoiceId",row.button.transform,"",12,0,65,36,14,Muted);
            row.label=Label("ChoiceName",row.button.transform,"",78,0,326,36,17);row.label.resizeTextForBestFit=true;row.label.resizeTextMinSize=13;row.label.resizeTextMaxSize=17;
            row.stats=Label("ChoiceStats",row.button.transform,"",410,0,216,36,14,Muted,TextAnchor.MiddleRight);
            row.status=Label("ChoiceStatus",row.button.transform,"",635,0,72,36,14,Gold,TextAnchor.MiddleCenter);rows.Add(row);
        }
        emptyList=Label("SetupNoResults",list.transform,"没有匹配的装备\n试试英文名称或完整编号",0,110,716,100,20,Muted,TextAnchor.MiddleCenter);
        previous=Button("SetupPreviousPage",middle,"上一页",18,530,114,34,()=>{page--;RefreshRows();});
        pageLabel=Label("SetupPage",middle,"",142,530,450,34,16,Muted,TextAnchor.MiddleCenter);
        next=Button("SetupNextPage",middle,"下一页",620,530,114,34,()=>{page++;RefreshRows();});
        var right=Card("SetupDetails",1056,180,506,578);Label("DetailsHeading",right,"装备详情",20,12,466,36,23,Gold,bold:true);
        var preview=Node("SetupPreview",right);Place(Rect(preview),20,60,190,194);Background(preview,Ink);Border(preview);
        var sprite=Node("Sprite",preview.transform);Stretch(Rect(sprite),14);spritePreview=sprite.AddComponent<Image>();spritePreview.preserveAspect=true;spritePreview.raycastTarget=false;
        var raw=Node("Texture",preview.transform);Stretch(Rect(raw),14);texturePreview=raw.AddComponent<RawImage>();texturePreview.raycastTarget=false;textureAspect=raw.AddComponent<AspectRatioFitter>();textureAspect.aspectMode=AspectRatioFitter.AspectMode.FitInParent;
        emptyPreview=Label("SetupEmptyPreview",preview.transform,"未装备",8,60,174,70,19,Muted,TextAnchor.MiddleCenter);
        selectedName=Label("SetupSelectedName",right,"",230,62,254,85,23,Ivory,bold:true);selectedName.resizeTextForBestFit=true;selectedName.resizeTextMinSize=16;selectedName.resizeTextMaxSize=23;
        selectedId=Label("SetupSelectedId",right,"",230,151,254,30,16,Muted);selectedKind=Label("SetupSelectedKind",right,"",230,187,254,60,17,Gold);
        damage=Label("SetupDamage",right,"",20,271,146,42,19,Ivory,TextAnchor.MiddleCenter);
        defense=Label("SetupDefense",right,"",176,271,146,42,19,Ivory,TextAnchor.MiddleCenter);
        weight=Label("SetupWeight",right,"",332,271,154,42,19,Ivory,TextAnchor.MiddleCenter);
        var rule=Node("SummaryRule",right);Place(Rect(rule),20,325,466,1);Background(rule,new Color(Gold.r,Gold.g,Gold.b,.3f),false);
        Label("SummaryHeading",right,"当前配装",20,336,466,30,19,Gold,bold:true);
        summary=Label("SetupSummary",right,"",20,374,466,191,16,Muted,TextAnchor.UpperLeft);
        error=Label("SetupError",shell,"",40,768,1520,34,17,new Color(1,.64f,.48f));
        Button("SetupDefaults",shell,"初始装备",40,816,176,54,ResetCharacter);
        Label("SetupHelp",shell,"F11 全屏 / 大窗口   ·   F2 装备与选关",238,816,660,54,17,Muted);
        Button("SetupBack",shell,"返回",1110,816,152,54,()=>host.ClosePlaytestSetup());
        start=Button("SetupStart",shell,"保存并出战",1280,816,280,54,StartBattle,true);
        regions.AddRange(host.SetupCatalog.Regions.OrderBy(r=>r.Id));region.ClearOptions();region.AddOptions(regions.Select(r=>r.Label).ToList());
        region.onValueChanged.AddListener(index=>{if(draft==null)return;draft.Region=regions[index].Id;RefreshLimits();ValidateLevelEditor();});
        level.onEndEdit.AddListener(_=>{if(draft!=null)ValidateLevelEditor();});
        search.onValueChanged.AddListener(_=>{if(draft==null)return;page=0;FilterChoices(false);});
        entry=Button("SetupEntry",transform,"装备 / 选关 (F2)",0,0,248,46,()=>host.OpenPlaytestSetup());
        var er=Rect(entry.gameObject);er.anchorMin=er.anchorMax=er.pivot=new Vector2(1,0);er.anchoredPosition=new Vector2(-24,24);
    }
    public void Open(OriginalPlaytestSetup value,string loadError=null) {
        if(value==null)throw new ArgumentNullException(nameof(value));draft=value.Clone();
        int regionIndex=regions.FindIndex(r=>r.Id==draft.Region);region.SetValueWithoutNotify(Mathf.Max(0,regionIndex));
        if(regionIndex<0&&regions.Count>0)draft.Region=regions[0].Id;
        level.SetTextWithoutNotify(draft.Level.ToString(CultureInfo.InvariantCulture));search.SetTextWithoutNotify("");page=0;
        if(!slots.Any(s=>s.key==slotKey)||!host.SetupCatalog.IsSlotEnabled(draft,slotKey))slotKey="MainHand";
        MainOverlayRoot.SetActive(true);entry.gameObject.SetActive(false);RefreshLimits();RefreshEquipment(true);ShowError(loadError);
    }
    public void Hide() {
        if(MainOverlayRoot!=null)MainOverlayRoot.SetActive(false);
        if(EventSystem.current!=null&&EventSystem.current.currentSelectedGameObject!=null&&EventSystem.current.currentSelectedGameObject.transform.IsChildOf(transform))EventSystem.current.SetSelectedGameObject(null);
        draft=null;
        if(entry!=null)entry.gameObject.SetActive(host!=null&&host.player!=null&&!host.IsTransitioning);
    }
    void ShowError(string message){error.text=message??"";}
    int MaximumLevel=>regions.Count==0?1000:Mathf.Min(1000,regions[Mathf.Clamp(region.value,0,regions.Count-1)].LevelCount);
    void RefreshLimits(){limits.text="可选关卡  1.."+MaximumLevel+"   ·   战场随所选地区与关卡生成";}
    bool ReadLevel(out int value) {
        return int.TryParse(level.text.Trim(),NumberStyles.Integer,CultureInfo.InvariantCulture,out value)&&value>=1&&value<=MaximumLevel;
    }
    bool ValidateLevelEditor() {
        if(!ReadLevel(out int value)){ShowError("请输入 1.."+MaximumLevel+" 范围内的整数关卡。");return false;}
        draft.Level=value;ShowError(null);return true;
    }
    void StepLevel(int direction) {
        if(draft==null||!ValidateLevelEditor())return;
        draft.Level=Mathf.Clamp(draft.Level+direction,1,MaximumLevel);level.SetTextWithoutNotify(draft.Level.ToString(CultureInfo.InvariantCulture));
    }
    void ChooseSlot(string key) {
        if(draft==null||!host.SetupCatalog.IsSlotEnabled(draft,key))return;
        slotKey=key;search.SetTextWithoutNotify("");page=0;RefreshEquipment(true);
    }
    void RefreshEquipment(bool locateEquipped) {
        foreach(var slot in slots) {
            bool enabled=host.SetupCatalog.IsSlotEnabled(draft,slot.key),selected=slot.key==slotKey;
            slot.button.interactable=enabled;slot.button.GetComponent<Image>().color=selected?Selected:Raised;slot.marker.SetActive(selected);
            slot.text.text=enabled?slot.label:slot.label+" · 双手占用";slot.text.color=enabled?(selected?Gold:Ivory):Muted;
        }
        currentSlot.text=slots.First(s=>s.key==slotKey).label;FilterChoices(locateEquipped);RefreshDetails();RefreshSummary();
    }
    void FilterChoices(bool locateEquipped) {
        string query=search.text.Trim();string number=query.TrimStart('#');bool exactId=int.TryParse(number,NumberStyles.Integer,CultureInfo.InvariantCulture,out int id);
        filtered=host.SetupCatalog.Choices(slotKey).Where(choice=>query.Length==0||(exactId?choice.Id==id:(choice.Label??"").IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0)).ToList();
        if(locateEquipped){int index=filtered.FindIndex(choice=>choice.Id==host.SetupCatalog.CurrentId(draft,slotKey));page=index<0?0:index/PageSize;}
        RefreshRows();
    }
    static string Number(float value)=>value.ToString("0.##",CultureInfo.InvariantCulture);
    void RefreshRows() {
        int pages=Mathf.Max(1,(filtered.Count+PageSize-1)/PageSize);page=Mathf.Clamp(page,0,pages-1);int equipped=host.SetupCatalog.CurrentId(draft,slotKey);
        count.text=filtered.Count+" 件装备";pageLabel.text="第 "+(page+1)+" / "+pages+" 页";previous.interactable=page>0;next.interactable=page+1<pages;emptyList.gameObject.SetActive(filtered.Count==0);
        for(int i=0;i<rows.Count;i++) {
            var row=rows[i];int index=page*PageSize+i;row.button.gameObject.SetActive(index<filtered.Count);if(index>=filtered.Count){row.choice=null;continue;}
            var choice=filtered[index];row.choice=choice;row.button.name="Choice_"+choice.Id;
            row.id.text="#"+choice.Id;row.label.text=choice.Label;row.stats.text="伤 "+Number(choice.Damage)+"   防 "+Number(choice.Defense)+"   重 "+Number(choice.Weight);
            row.status.text=choice.Id==equipped?"已装备":"装备";row.button.GetComponent<Image>().color=choice.Id==equipped?Selected:Ink;
        }
    }
    void SelectChoice(OriginalPlaytestChoice choice) {
        if(draft==null||choice==null)return;
        try{host.SetupCatalog.Select(draft,slotKey,choice.Id);ShowError(null);RefreshEquipment(false);}
        catch(Exception failure){ShowError(failure.Message);}
    }
    void RefreshDetails() {
        int id=host.SetupCatalog.CurrentId(draft,slotKey);var choice=host.SetupCatalog.Choice(slotKey,id);
        spritePreview.gameObject.SetActive(false);texturePreview.gameObject.SetActive(false);spritePreview.sprite=null;texturePreview.texture=null;emptyPreview.gameObject.SetActive(true);
        if(choice==null){selectedName.text="装备编号不可用";selectedId.text="#"+id;selectedKind.text="请重新选择";damage.text=defense.text=weight.text="—";return;}
        selectedName.text=choice.Label;selectedId.text="#"+choice.Id+(choice.Region>0?"  ·  地区 "+choice.Region:"");
        selectedKind.text=choice.Id==0?"不装备此部位":slotKey=="MainHand"||slotKey=="OffHand"?(choice.TwoHanded?"双手武器 · 占用副手":"单手装备"):slots.First(s=>s.key==slotKey).label;
        damage.text="伤害  "+Number(choice.Damage);defense.text="防御  "+Number(choice.Defense);weight.text="重量  "+Number(choice.Weight);
        if(choice.Id==0){emptyPreview.text="未装备";return;}
        // These lazy getters are used only for the selected detail, never for
        // the catalog/list rows. No preview prefab is instantiated.
        var sprite=choice.PreviewSprite;
        if(sprite!=null){spritePreview.sprite=sprite;spritePreview.color=choice.PreviewTint;spritePreview.gameObject.SetActive(true);emptyPreview.gameObject.SetActive(false);return;}
        var texture=choice.PreviewTexture;
        if(texture!=null){texturePreview.texture=texture;texturePreview.color=choice.PreviewTint;textureAspect.aspectRatio=(float)texture.width/Mathf.Max(1,texture.height);texturePreview.gameObject.SetActive(true);emptyPreview.gameObject.SetActive(false);}
        else emptyPreview.text="暂无预览";
    }
    void RefreshSummary() {
        // Six compact pairs keep all twelve positions visible. Full original
        // English names remain in the list and selected-item detail above.
        var text=new StringBuilder();int index=0;
        foreach(var slot in slots) {
            if(index>0)text.Append(index%2==0?"\n":"     ·     ");index++;text.Append(slot.label).Append(" ");
            if(!host.SetupCatalog.IsSlotEnabled(draft,slot.key)){text.Append("双手占用");continue;}
            int id=host.SetupCatalog.CurrentId(draft,slot.key);text.Append(id==0?"未装备":"#"+id);
        }
        summary.text=text.ToString();
    }
    void ResetCharacter() {
        if(draft==null)return;
        try{draft.Character=J.Parse(J.Write(host.SetupCatalog.Defaults().Character));if(!host.SetupCatalog.IsSlotEnabled(draft,slotKey))slotKey="MainHand";ShowError(null);RefreshEquipment(true);}
        catch(Exception failure){ShowError(failure.Message);}
    }
    void StartBattle() {
        if(draft==null||!ValidateLevelEditor())return;
        if(regions.Count==0||region.value<0||region.value>=regions.Count){ShowError("请选择有效地区。");return;}
        draft.Region=regions[region.value].Id;
        try {
            host.SetupCatalog.Validate(draft);
            if(!host.StartPlaytestSetup(draft.Clone(),out string reason))ShowError(string.IsNullOrWhiteSpace(reason)?"无法开始战斗，请检查配置。":reason);
        } catch(Exception failure){ShowError(failure.Message);}
    }
    void OnDestroy(){if(font!=null)Destroy(font);}
}
