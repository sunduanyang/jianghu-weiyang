using System;
using System.IO;
using UnityEngine;

// Local ownership adapter for the recovered campaign/practice services.
// The original save domain and APK installation keep separate storage.
public sealed partial class OriginalPreview {
    public GameObject dummyPrefab;
    public Transform environmentRoot;
    public OriginalPracticeDummy practiceDummy {get;private set;}
    public OriginalLocalProgress Progress {get;private set;}
    public OriginalCampaignOutcome CampaignOutcome {get;private set;}
    public int GameMode {get{EnsureOriginalContext();return Progress.GameMode;}}
    public int ModeNumber=>GameMode==0?Progress.Level:1;
    public OriginalTutorialContext TutorialContext {get;private set;}
    OriginalNewItemsAlerts newItemsAlerts;
    OriginalUIPopupResult campaignPopupResult;
    OriginalCampaignOutcome popupOutcome;
    public OriginalEnvironmentCustomizer EnvironmentCustomizer=>environmentCustomizer;
    public OriginalUIPopupResult CampaignPopupResult {
        get {
            if(CampaignOutcome==null)return null;
            if(popupOutcome!=CampaignOutcome) {
                popupOutcome=CampaignOutcome;
                campaignPopupResult=Progress.CreatePopupResult(CampaignOutcome,new OriginalCampaignPopupServices {
                    RefreshNewItemsPips=()=>newItemsAlerts.RefreshAllPips(),
                    GetNewItemsCount=()=>newItemsAlerts.GetNewItemsCount(Progress.Region,false)
                });
            }
            return campaignPopupResult;
        }
    }
    OriginalEnvironmentCustomizer environmentCustomizer;
    OriginalTimeOfDay originalTimeOfDay;
    OriginalArenaAmbience arenaAmbience;
    bool environmentInitialized;
    bool contextReady;
    int? explicitEnvironmentHour,explicitEnvironmentDay;
    void Awake()=>EnsureOriginalContext();
    public void EnsureOriginalContext() {
        if(contextReady)return;
        var args=Environment.GetCommandLineArgs();
        string Argument(string key){int at=Array.IndexOf(args,key);return at>=0&&at+1<args.Length?args[at+1]:null;}
        string save=Argument("-originalProgressPath")??Path.Combine(Application.persistentDataPath,"original-local-progress.json");
        Progress=OriginalLocalProgress.Open(Path.GetFullPath(save));
        Progress.GameMode=Progress.TutorialPassed?0:2;
        // The existing local multi-round validation continues to exercise its
        // Friendly rules. Production startup follows original onboarding.
        if(Array.IndexOf(args,"-validateOriginalMatch")>=0)Progress.GameMode=5;
        if(int.TryParse(Argument("-originalGameMode"),out var mode)) {
            if(mode!=0&&mode!=2&&mode!=5)throw new NotSupportedException("Recovered local modes: Campaign 0, Practice 2, Friendly 5");
            Progress.GameMode=mode;
        }
        if(int.TryParse(Argument("-originalCampaignRegion"),out var region))Progress.SetCurrentCampaignRegion(region);
        if(int.TryParse(Argument("-originalCampaignLevel"),out var level))Progress.SetCurrentCampaignLevel(level);
        if(int.TryParse(Argument("-originalCampaignStage"),out var stage))Progress.SetCurrentCampaignStage(stage);
        else if(Progress.GameMode==0)Progress.SetCurrentCampaignStage(Progress.Stage);
        if(int.TryParse(Argument("-originalEnvironmentHour"),out var hour)) {
            if(hour<0||hour>24)throw new ArgumentOutOfRangeException("-originalEnvironmentHour");
            explicitEnvironmentHour=hour;
        }
        if(int.TryParse(Argument("-originalEnvironmentDay"),out var day)) {
            if(day<1||day>31)throw new ArgumentOutOfRangeException("-originalEnvironmentDay");
            explicitEnvironmentDay=day;
        }
        InitializePlaytestSetupContext(Path.GetFullPath(save),args);
        newItemsAlerts=OriginalNewItemsAlerts.Open(Path.GetFullPath(save)+".items.json",Progress);
        TutorialContext=new OriginalTutorialContext {
            GameMode=()=>GameMode,HasProgress=()=>Progress!=null,IsTutorialPassed=()=>Progress.TutorialPassed,
            HasNetworkController=()=>player!=null,NetworkState=()=>OriginalState,
            MarkTutorialPassed=Progress.MarkTutorialPassed,HasTransition=()=>true,
            SetCampaignLevel=Progress.SetCurrentCampaignLevel,SetCampaignStage=Progress.SetCurrentCampaignStage,
            TransitionToArena=TransitionOriginalArena
        };
        contextReady=true;
    }
    void InitializeOriginalEnvironment() {
        if(environmentRoot==null)throw new InvalidOperationException("Original environment root missing");
        var catalog=Resources.Load<OriginalVisualCatalog>("OriginalVisualCatalog");
        environmentCustomizer=environmentRoot.GetComponent<OriginalEnvironmentCustomizer>()??environmentRoot.gameObject.AddComponent<OriginalEnvironmentCustomizer>();
        originalTimeOfDay=FindAnyObjectByType<OriginalTimeOfDay>();
        environmentCustomizer.Initialize(catalog,environmentRoot,originalTimeOfDay,FindAnyObjectByType<OriginalWeather>());
        OriginalLightActivator.Attach(catalog,environmentRoot,()=>originalTimeOfDay.TimeOfDay);
        arenaAmbience=FindAnyObjectByType<OriginalArenaAmbience>();
        if(arenaAmbience==null)throw new InvalidOperationException("Original arena ambience host missing");
        arenaAmbience.Initialize(environmentRoot);environmentCustomizer.OnAmbientMixerValue+=arenaAmbience.ApplyEnvironmentMixer;
        CustomizeOriginalEnvironment();
        InitializeOriginalArenaTransition();
    }
    void CustomizeOriginalEnvironment() {
        if(environmentCustomizer==null)return;
        DateTime now=DateTime.Now;
        int hour=environmentInitialized?originalTimeOfDay.TimeOfDay:explicitEnvironmentHour??now.Hour;
        environmentCustomizer.Customize(new OriginalEnvironmentCustomizer.Context(GameMode,false,Progress.Region,Progress.LevelType,Progress.Seed,Progress.Stage,true,hour,explicitEnvironmentDay??now.Day,false,false,false));
        environmentInitialized=true;
    }
    public void TransitionOriginalArena(int mode) {
        if(mode!=0&&mode!=2&&mode!=5)throw new NotSupportedException("Original local arena mode is not recovered: "+mode);
        StartOriginalArenaTransition(mode);
    }
    public void NextOriginalMatch() {
        if(IsTransitioning||GameMode!=0||OriginalState!=6||WinsPlayer<=WinsEnemy)return;
        if(Progress.Level<1000) {
            int next=Progress.Level+1;Progress.SetCurrentCampaignLevel(next);
            Progress.SetCurrentCampaignStage(Mathf.Max(0,(next-1)/10));
            RememberPlaytestLocation();
        }
        TransitionOriginalArena(0);
    }
}
