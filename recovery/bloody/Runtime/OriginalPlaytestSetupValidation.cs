using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jianghu;
using UnityEngine;
using UnityEngine.UI;

// Explicit, isolated integration fixture. All normal selections use the live
// UGUI callbacks; no HP, body position, physics, inventory or gold is changed.
public sealed class OriginalPlaytestSetupValidation : MonoBehaviour {
    static readonly string[] Fields={"HandItemRightId","HandItemLeftId","DoubleHandedItemId","HatId","TopL1Id","TopL2Id","TopL3Id","TopL4Id","GlovesId","BottomL1Id","BottomL2Id","BottomL3Id","ShoesId"};
    OriginalPreview host;
    string directory,expectedPath;
    bool reload,finished,observing;
    int expectedRejection,walletEvents,winEvents;
    double began;
    J baseline,expectedDocument;
    OriginalPlaytestSetup expected;
    readonly List<J> steps=new List<J>(),logs=new List<J>();
    readonly List<string> errors=new List<string>();
    OriginalPlaytestSetupMenu Menu=>host.GetComponent<OriginalPlaytestSetupMenu>()??FindAnyObjectByType<OriginalPlaytestSetupMenu>();
    GameObject Overlay=>Menu==null?null:Menu.MainOverlayRoot;
    public static bool TryAttach(OriginalPreview preview) {
        var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-validateOriginalSetup");
        if(at<0||at+1>=args.Length)return false;
        var check=preview.gameObject.AddComponent<OriginalPlaytestSetupValidation>();
        check.host=preview;check.directory=Path.GetFullPath(args[at+1]);Directory.CreateDirectory(check.directory);
        int flag=Array.IndexOf(args,"-validateOriginalSetupReload");
        check.reload=flag>=0&&(flag+1>=args.Length||!(args[flag+1]=="false"||args[flag+1]=="0"));return true;
    }
    void Start() {
        began=Time.realtimeSinceStartupAsDouble;observing=true;host.ExternalInputReplay=true;AudioListener.volume=0;
        Application.logMessageReceived+=OnLog;StartCoroutine(Guard(Run()));
    }
    void Update(){if(observing&&!finished&&Time.realtimeSinceStartupAsDouble-began>(reload?60:90)){errors.Add("Setup validation real-time watchdog expired");Finish();}}
    void OnLog(string message,string trace,LogType type) {
        if(logs.Count<160&&(type==LogType.Exception||type==LogType.Error||type==LogType.Assert||type==LogType.Warning))logs.Add(J.O("type",type.ToString(),"message",message,"expected_rejection",expectedRejection>0));
        if(expectedRejection==0&&(type==LogType.Exception||type==LogType.Error||type==LogType.Assert))errors.Add(message);
    }
    IEnumerator Guard(IEnumerator routine) {
        var stack=new Stack<IEnumerator>();stack.Push(routine);
        while(stack.Count>0&&!finished) {
            object current=null;bool next=false;Exception failure=null;
            try{next=stack.Peek().MoveNext();if(next)current=stack.Peek().Current;}catch(Exception error){failure=error;}
            if(failure!=null){errors.Add(failure.ToString());Finish();yield break;}
            if(!next){(stack.Pop() as IDisposable)?.Dispose();continue;}
            if(current is IEnumerator nested)stack.Push(nested);else yield return current;
        }
        if(!finished)Finish();
    }
    IEnumerator Run() {
        yield return Wait(()=>host.Progress!=null&&host.SetupCatalog!=null&&host.player!=null&&!host.IsTransitioning,"initial arena/setup bootstrap",22);
        var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-originalProgressPath");
        Require(at>=0&&at+1<args.Length&&Path.IsPathRooted(args[at+1]),"An explicit isolated -originalProgressPath is required");
        Require(string.Equals(Path.GetFullPath(args[at+1]),Path.GetFullPath(host.Progress.SavePath),StringComparison.OrdinalIgnoreCase),"Host progress path differs from the supplied fixture path");
        expectedPath=host.Progress.SavePath+".setup-validation-expected.json";
        baseline=host.Progress.Snapshot();host.Progress.OnWalletChanged+=OnWallet;host.Progress.OnCampaignMatchWon+=OnWin;
        Snapshot("bootstrap");
        if(reload){yield return Reload();yield break;}
        yield return Initial();
    }
    IEnumerator Initial() {
        yield return OpenMenu();expected=host.CurrentSetup.Clone();
        var catalog=host.SetupCatalog;
        var sword=catalog.Choices("MainHand").First(c=>c.Id!=0&&c.Id!=197&&!c.TwoHanded&&c.Category==1);
        var shield=catalog.Choices("OffHand").First(c=>c.Id!=0&&c.Id!=235&&!c.TwoHanded&&c.Category==8);
        var hat=catalog.Choices("HatId").First(c=>c.Id>0&&c.Defense>0);
        var body=catalog.Choices("TopL2Id").First(c=>c.Id>0&&c.Defense>0);
        var two=catalog.Choices("MainHand").Where(c=>c.Id>0&&c.TwoHanded).OrderBy(c=>c.Category==1?0:1).First();
        yield return Select("MainHand",sword.Id);
        Click("SetupDefaults");expected=catalog.Defaults();yield return null;
        Check(SelectedId(catalog.CurrentId(expected,"MainHand")),"Defaults callback did not restore the displayed original main-hand id");
        Check(expected.Character["BottomL1Id"].I==121,"Fixture expected original BottomL1 121 before removal");
        yield return Select("MainHand",sword.Id);yield return Select("OffHand",shield.Id);
        yield return Select("HatId",hat.Id);yield return Select("TopL2Id",body.Id);
        SetLocation(1,2);expected.Region=1;expected.Level=2;
        Snapshot("onehand_menu_selected");yield return Capture("onehand-menu");
        int old=host.player.GetInstanceID();Click("SetupStart");
        yield return NewActiveRig(old,"single-hand setup transition");
        CheckApplied(expected,false);Check(host.player.rightItem!=null&&host.player.rightItem.itemId==sword.Id&&host.player.rightItem.owner==host.player,"MainRight sword is not the actual player-owned right item");
        Check(host.player.leftItem!=null&&host.player.leftItem.itemId==shield.Id&&host.player.leftItem.owner==host.player,"OffLeft shield is not the actual player-owned left item");
        CheckStore(expected);var initialPaint=InspectArmorPaint("BottomL1",false);var initialArmor=InspectArmorDefense("BottomL1",false);
        steps.Add(J.O("name","bottom121_before_removal","paint",initialPaint,"armor",initialArmor));
        Snapshot("onehand_active");yield return Capture("onehand-active");
        old=host.player.GetInstanceID();
        var pause=host.hud.GetComponentsInChildren<Button>(true).FirstOrDefault(b=>b.name=="Button_Pause"&&b.gameObject.activeInHierarchy&&b.IsInteractable());
        Require(pause!=null,"Source Pause button missing before retry");pause.onClick.Invoke();yield return null;
        var retry=host.hud.GetComponentsInChildren<Button>(true).FirstOrDefault(b=>b.name=="Button_Restart"&&b.gameObject.activeInHierarchy&&b.IsInteractable());
        Require(retry!=null,"Source Restart button missing in pause menu");retry.onClick.Invoke();
        yield return NewActiveRig(old,"source Retry callback");CheckApplied(expected,false);CheckStore(expected);Snapshot("retry_preserved_setup");CheckEconomy();
        yield return OpenMenu();CheckRejections();
        yield return Select("MainHand",two.Id);
        Check(!catalog.IsSlotEnabled(expected,"OffHand")&&!Button("Slot_OffHand").interactable,"Two-handed selection did not disable the actual OffHand slot");
        var disabled=expected.Clone();string before=Equipment(disabled.Character).ToString();bool refused=false;
        try{catalog.Select(disabled,"OffHand",shield.Id);}catch(ArgumentException){refused=true;}catch(InvalidOperationException){refused=true;}
        Check(refused&&before==Equipment(disabled.Character).ToString(),"Catalog changed disabled OffHand state");
        yield return Select("HatId",0);yield return Select("BottomL1Id",0);
        SetLocation(4,3);expected.Region=4;expected.Level=3;
        Snapshot("twohand_menu_selected");yield return Capture("twohand-menu");
        old=host.player.GetInstanceID();Click("SetupStart");yield return NewActiveRig(old,"two-handed setup transition");
        CheckApplied(expected,true);CheckStore(expected);
        steps.Add(J.O("name","removed_armor_actual","hat_paint",InspectArmorPaint("Hat",true),"hat_armor",InspectArmorDefense("Hat",true),"bottomL1_paint",InspectArmorPaint("BottomL1",true),"bottomL1_armor",InspectArmorDefense("BottomL1",true)));
        CheckEconomy();Snapshot("twohand_active_removed_armor");yield return Capture("twohand-active");
        expectedDocument=J.O("Region",expected.Region,"Level",expected.Level,"Character",Equipment(expected.Character),"Wallet",baseline["Wallet"],"Campaign",baseline["Progress"]["Campaign"]);
        File.WriteAllText(expectedPath,expectedDocument.ToString());
        steps.Add(J.O("name","cold_start_fixture_retained","path",expectedPath,"expected",expectedDocument));
    }
    IEnumerator Reload() {
        Require(Array.IndexOf(Environment.GetCommandLineArgs(),"-playtestSetup")<0,"Cold-reload fixture must omit -playtestSetup");
        Require(File.Exists(expectedPath),"Initial phase expectation document missing");
        expectedDocument=J.Parse(File.ReadAllText(expectedPath));expected=host.SetupCatalog.Defaults();
        expected.Region=expectedDocument["Region"].I;expected.Level=expectedDocument["Level"].I;
        foreach(string field in Fields)expected.Character[field]=new J(expectedDocument["Character"][field].I);
        host.SetupCatalog.Validate(expected);
        if(host.SetupMenuOpen){Click("SetupBack");yield return null;}
        yield return Wait(()=>host.player!=null&&!host.IsTransitioning&&host.OriginalState==4,"cold-start active arena",22);
        CheckApplied(expected,true);CheckStore(expected);
        Check(host.GameMode==0&&host.Progress.Region==expected.Region&&host.Progress.Level==expected.Level,"Cold start did not apply the saved campaign location/mode");
        Check(host.Progress.Snapshot()["Wallet"].ToString()==expectedDocument["Wallet"].ToString()&&host.Progress.Snapshot()["Progress"]["Campaign"].ToString()==expectedDocument["Campaign"].ToString(),"Cold startup changed original Wallet/completed campaign data");
        steps.Add(J.O("name","reload_none_armor","bottomL1_paint",InspectArmorPaint("BottomL1",true),"bottomL1_armor",InspectArmorDefense("BottomL1",true)));
        Snapshot("cold_start_applied");yield return Capture("reload-active");yield return OpenMenu();
        Check(Input("SetupLevel").text==expected.Level.ToString(),"Reload menu level differs from saved level");
        Check(Dropdown("SetupRegion").value==host.SetupCatalog.Regions.OrderBy(r=>r.Id).ToList().FindIndex(r=>r.Id==expected.Region),"Reload menu region differs from saved region");
        Click("Slot_MainHand");yield return null;Input("SetupSearch").text=host.SetupCatalog.CurrentId(expected,"MainHand").ToString();yield return null;
        CheckChoiceSelected(host.SetupCatalog.CurrentId(expected,"MainHand"));Check(!Button("Slot_OffHand").interactable,"Reload menu lost two-handed OffHand disabling");
        Click("Slot_BottomL1Id");yield return null;Input("SetupSearch").text="0";yield return null;CheckChoiceSelected(0);
        Snapshot("reload_menu_matches_saved");yield return Capture("reload-menu");Click("SetupBack");yield return null;
        yield return Wait(()=>!host.SetupMenuOpen&&!host.Paused&&host.OriginalState==4&&Mathf.Abs(Time.timeScale-1)<.001f,"menu close restores active scene time scale",8);
        CheckEconomy();Snapshot("reload_menu_closed");
    }
    IEnumerator OpenMenu() {
        if(!host.SetupMenuOpen)host.OpenPlaytestSetup();yield return null;
        Require(host.SetupMenuOpen&&Menu!=null&&Menu.IsOpen&&Overlay!=null&&Overlay.activeInHierarchy,"Live setup overlay did not open");
    }
    IEnumerator Select(string key,int id) {
        Click("Slot_"+key);yield return null;
        var marker=Node("Slot_"+key).Find("SelectedMarker");Check(marker!=null&&marker.gameObject.activeSelf,"Slot selected marker missing: "+key);
        Input("SetupSearch").text=id.ToString();yield return null;
        var choices=Overlay.GetComponentsInChildren<Button>(false).Where(b=>b.name.StartsWith("Choice_",StringComparison.Ordinal)).ToArray();
        Check(choices.Length==1&&choices[0].name=="Choice_"+id,"Numeric search did not filter to the exact source id "+id);
        Click("Choice_"+id);host.SetupCatalog.Select(expected,key,id);yield return null;CheckChoiceSelected(id);
        steps.Add(J.O("name","ui_choice","slot",key,"id",id,"expected_equipment",Equipment(expected.Character)));
    }
    void SetLocation(int region,int level) {
        int index=host.SetupCatalog.Regions.OrderBy(r=>r.Id).ToList().FindIndex(r=>r.Id==region);Require(index>=0,"Target original region missing");
        var dropdown=Dropdown("SetupRegion");dropdown.value=index;dropdown.onValueChanged.Invoke(index);Input("SetupLevel").text=level.ToString();
    }
    void CheckChoiceSelected(int id) {
        var button=Button("Choice_"+id);var status=button.GetComponentsInChildren<Text>(true).SingleOrDefault(t=>t.name=="ChoiceStatus");
        Check(status!=null&&status.text.Contains("已装备"),"Choice UI does not mark selected item "+id);Check(SelectedId(id),"Selected detail id differs from chosen item "+id);
    }
    bool SelectedId(int id) {var label=Node("SetupSelectedId").GetComponent<Text>();return label!=null&&Regex.IsMatch(label.text,@"(^|\D)"+id+@"(\D|$)");}
    IEnumerator NewActiveRig(int previous,string label) {
        yield return Wait(()=>host.player!=null&&host.player.GetInstanceID()!=previous&&!host.IsTransitioning&&!host.SetupMenuOpen&&host.OriginalState==4,label,22);
        Check(!host.Paused&&Mathf.Abs(Time.timeScale-1)<.001f,"New active arena has wrong pause/time scale: "+label);
    }
    IEnumerator Wait(Func<bool> predicate,string label,double seconds) {
        double at=Time.realtimeSinceStartupAsDouble;
        while(!predicate()){if(Time.realtimeSinceStartupAsDouble-at>seconds)throw new TimeoutException(label);yield return null;}
    }
    void CheckApplied(OriginalPlaytestSetup selected,bool twoHanded) {
        Require(host.player!=null,"Actual player missing");Check(host.Progress.Region==selected.Region&&host.Progress.Level==selected.Level&&host.GameMode==0,"Applied campaign location differs");
        CheckSetup(host.CurrentSetup,selected,"host setup");
        foreach(string field in Fields)Check(host.player.AppearanceConfig[field].I==selected.Character[field].I,"Actual AppearanceConfig mismatch: "+field);
        if(twoHanded) {
            int id=selected.Character["DoubleHandedItemId"].I;
            var items=FindObjectsByType<OriginalItem>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(i=>i.owner==host.player&&i.itemId==id&&i.twoHanded&&!i.dropped&&i.gameObject.activeInHierarchy).ToArray();
            Check(id!=0&&items.Length>0&&host.player.doubleHanded,"Selected double-handed item is not an actual player-owned equipped source instance");
        }
        else Check(!host.player.doubleHanded&&selected.Character["DoubleHandedItemId"].I==0,"Single-hand setup left double-handed mode active");
    }
    void CheckSetup(OriginalPlaytestSetup actual,OriginalPlaytestSetup wanted,string label) {
        Check(actual.Region==wanted.Region&&actual.Level==wanted.Level,label+" location mismatch");
        foreach(string field in Fields)Check(actual.Character[field].I==wanted.Character[field].I,label+" equipment mismatch: "+field);
    }
    void CheckStore(OriginalPlaytestSetup wanted) {
        string path=host.Progress.SavePath+".setup.json";Check(File.Exists(path),"Setup JSON was not written");
        var store=OriginalPlaytestStore.Open(path,host.SetupCatalog);Check(string.IsNullOrEmpty(store.LoadError),"Independent settings reload failed: "+store.LoadError);
        CheckSetup(store.Value,wanted,"independent JSON reload");steps.Add(J.O("name","store_reload","path",store.SavePath,"equipment",Equipment(store.Value.Character)));
    }
    void CheckRejections() {
        foreach(int level in new[]{0,1001}) {var invalid=host.CurrentSetup.Clone();invalid.Level=level;Reject(invalid,"level_"+level);}
        var unknown=host.CurrentSetup.Clone();unknown.Character["HandItemRightId"]=new J(int.MaxValue);unknown.Character["DoubleHandedItemId"]=new J(0);Reject(unknown,"unknown_item");
        var clone=host.CurrentSetup.Clone();string before=Equipment(clone.Character).ToString();bool rejected=false;
        try{host.SetupCatalog.Select(clone,"MainHand",int.MaxValue);}catch(ArgumentException){rejected=true;}
        Check(rejected&&Equipment(clone.Character).ToString()==before,"Catalog.Select accepted/mutated unknown source id");CheckEconomy();
    }
    void Reject(OriginalPlaytestSetup invalid,string name) {
        bool rejected=false;try{host.SetupCatalog.Validate(invalid);}catch(ArgumentException){rejected=true;}Check(rejected,"Catalog.Validate accepted "+name);
        var previous=host.CurrentSetup.Clone();int rig=host.player.GetInstanceID();string path=host.Progress.SavePath+".setup.json",file=File.Exists(path)?File.ReadAllText(path):null;
        bool accepted;string error;expectedRejection++;try{accepted=host.StartPlaytestSetup(invalid,out error);}finally{expectedRejection--;}
        Check(!accepted&&!string.IsNullOrWhiteSpace(error),"Host accepted/reported no rejection for "+name);Check(host.player!=null&&host.player.GetInstanceID()==rig&&!host.IsTransitioning,"Rejected setup respawned or started a transition");
        CheckSetup(host.CurrentSetup,previous,"rejected setup preserved");Check(file==(File.Exists(path)?File.ReadAllText(path):null),"Rejected setup modified committed JSON");
        steps.Add(J.O("name","rejected_input","case",name,"error",error));
    }
    J InspectArmorPaint(string slot,bool removed) {
        var catalog=Resources.Load<OriginalVisualCatalog>("OriginalVisualCatalog");var records=catalog.Data["rigs"][OriginalRig.PlayerSource].A;
        var applier=records.Single(r=>r["script"].S=="CharacterConfigApplier");string field=char.ToLowerInvariant(slot[0])+slot.Substring(1)+"PaintableTexture";
        int id=applier["data"][field]["m_PathID"].I;var record=records.Single(r=>r["id"].I==id);string path=record["path"].S;
        var node=path.Length==0?host.player.transform:host.player.transform.Find(path);Require(node!=null,"Original armor paint node missing: "+path);
        var paint=node.GetComponent<OriginalPaintTexture>();Require(paint!=null&&paint.target!=null&&paint.current!=null,"Original armor paint instance/RT missing: "+field);
        var row=J.O("source_component_id",id,"source_path",path,"field",field,"source_texture",paint.sourceTexture==null?"null":paint.sourceTexture.name,"source_alpha",paint.sourceColor.a,"rt",paint.current.name,"width",paint.current.width,"height",paint.current.height);
        if(removed) {
            var prior=RenderTexture.active;var sample=new Texture2D(1,1,TextureFormat.RGBA32,false);var alpha=new List<J>();
            try{RenderTexture.active=paint.current;foreach(float x in new[]{.2f,.5f,.8f})foreach(float y in new[]{.2f,.5f,.8f}){sample.ReadPixels(new Rect(Mathf.Clamp((int)(x*paint.current.width),0,paint.current.width-1),Mathf.Clamp((int)(y*paint.current.height),0,paint.current.height-1),1,1),0,0,false);sample.Apply(false,false);alpha.Add(new J(sample.GetPixel(0,0).a));}}
            finally{RenderTexture.active=prior;Destroy(sample);}
            bool transparent=alpha.All(a=>a.N<=.005);row["alpha_samples"]=new J(alpha);row["samples_transparent"]=new J(transparent);
            Check(paint.sourceColor.a<=.001f||transparent,"Removed "+slot+" retains opaque original paint");Check(host.player.AppearanceConfig[slot+"Id"].I==0,"Removed armor config is not zero: "+slot);
        }
        else Check(paint.sourceTexture!=null&&paint.sourceColor.a>0,"Original BottomL1 121 was not painted before removal");
        return row;
    }
    J InspectArmorDefense(string slot,bool removed) {
        var data=host.player.source["controllers"]["CharacterConfigApplier"][0]["data"];string suffix=slot.StartsWith("BottomL",StringComparison.Ordinal)?slot.Replace("BottomL","Bottom"):slot;
        var rows=new List<J>();foreach(var pair in data.D)if(pair.Key.StartsWith("ArmorPart_",StringComparison.Ordinal)&&pair.Key.EndsWith("_"+suffix,StringComparison.Ordinal)) {
            var part=host.player.ArmorPart(pair.Value["id"].I);Check(part!=null,"Original armor defense instance missing: "+pair.Key);
            if(part!=null){rows.Add(J.O("field",pair.Key,"source_id",part.sourceId,"defense",part.defense,"material",part.material));if(removed)Check(Mathf.Abs(part.defense)<.00001f,"Removed "+slot+" retains source armor defense");}
        }
        Check(rows.Count>0,"No original armor domain could be inspected for "+slot);return new J(rows);
    }
    void CheckEconomy() {
        var now=host.Progress.Snapshot();Check(now["Wallet"].ToString()==baseline["Wallet"].ToString(),"Setup flow changed Wallet");Check(now["Progress"]["Campaign"].ToString()==baseline["Progress"]["Campaign"].ToString(),"Setup flow changed completed campaign levels/stars");Check(walletEvents==0&&winEvents==0,"Setup selection emitted wallet/victory reward events");
    }
    void OnWallet()=>walletEvents++;
    void OnWin(int region,byte stars)=>winEvents++;
    Transform Node(string name) {Require(Overlay!=null,"Setup overlay missing");var values=Overlay.GetComponentsInChildren<Transform>(true).Where(t=>t.name==name&&t.gameObject.activeInHierarchy).ToArray();Require(values.Length==1,"Expected one active setup node: "+name+"; found "+values.Length);return values[0];}
    Button Button(string name){var button=Node(name).GetComponent<Button>();Require(button!=null,"Setup Button component missing: "+name);return button;}
    InputField Input(string name){var input=Node(name).GetComponent<InputField>();Require(input!=null,"Setup InputField missing: "+name);return input;}
    Dropdown Dropdown(string name){var dropdown=Node(name).GetComponent<Dropdown>();Require(dropdown!=null,"Setup Dropdown missing: "+name);return dropdown;}
    void Click(string name){var button=Button(name);Require(button.IsInteractable(),"Setup button is disabled: "+name);button.onClick.Invoke();}
    static J Equipment(J character){var result=J.O();foreach(string field in Fields)result[field]=new J(character[field].I);return result;}
    void Snapshot(string name) {
        var p=host.player;steps.Add(J.O("name",name,"frame",Time.frameCount,"state",host.OriginalState,"transition",host.IsTransitioning,"menu_open",host.SetupMenuOpen,"paused",host.Paused,"time_scale",Time.timeScale,"region",host.Progress.Region,"level",host.Progress.Level,"player_instance",p==null?0:p.GetInstanceID(),"left",Item(p==null?null:p.leftItem),"right",Item(p==null?null:p.rightItem),"actual_equipment",p==null?new J():Equipment(p.AppearanceConfig),"stored_equipment",Equipment(host.CurrentSetup.Character)));Write(false);
    }
    J Item(OriginalItem item)=>item==null?J.O("missing",true):J.O("instance",item.GetInstanceID(),"id",item.itemId,"two_handed",item.twoHanded,"owner_is_player",item.owner==host.player,"active",item.gameObject.activeInHierarchy,"dropped",item.dropped);
    IEnumerator Capture(string name){yield return new WaitForEndOfFrame();OriginalRenderingDiagnostics.Capture(Camera.main,Path.Combine(directory,name));Write(false);}
    void Check(bool condition,string error){if(!condition)errors.Add(error);}
    static void Require(bool condition,string error){if(!condition)throw new InvalidOperationException(error);}
    void Write(bool complete) {
        File.WriteAllText(Path.Combine(directory,reload?"setup-reload-validation.json":"setup-validation.json"),J.O("complete",complete,"passed",complete&&errors.Count==0,"phase",reload?"cold_reload":"initial","unity",Application.unityVersion,"elapsed_real_seconds",Time.realtimeSinceStartupAsDouble-began,"progress_path",host.Progress==null?"missing":host.Progress.SavePath,"expected_path",expectedPath,"baseline",baseline,"wallet_events",walletEvents,"campaign_win_events",winEvents,"steps",new J(steps),"errors",new J(errors.Select(e=>new J(e)).ToList()),"logs",new J(logs),"limits","UGUI callbacks and source runtime objects; desktop hardware gestures and original-APK frame parity are not asserted; no HP/physics/economy mutation", "runtime_parity_verified",false).ToString());
    }
    void Finish(){if(finished)return;finished=true;Write(true);Application.Quit(errors.Count==0?0:1);}
    void OnDestroy(){Application.logMessageReceived-=OnLog;if(host!=null&&host.Progress!=null){host.Progress.OnWalletChanged-=OnWallet;host.Progress.OnCampaignMatchWon-=OnWin;}}
}
