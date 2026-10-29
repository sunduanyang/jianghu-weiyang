using System;
using System.Linq;
using Jianghu;
using UnityEngine;
using Random=UnityEngine.Random;

// EnvironmentCustomizer 0x36c83c8 / due 0x36c937c / duf 0x36c9440.
// The host supplies game state; this renderer does not invent campaign globals.
public sealed partial class OriginalEnvironmentCustomizer : MonoBehaviour {
    public sealed class Context {
        public readonly int RegionId,BBLevelType,ArenaSeed,CampaignStage,Hour,Day;
        public readonly bool InArena,ChristmasMode,SpookyMode,IsThroneRoom;
        // Captured hq.IsThroneRoom before duf; read CurrentIsThroneRoom on the
        // customizer for the source-resolved output after its Castle branch.
        public bool PreviousIsThroneRoom=>IsThroneRoom;
        public readonly int? GameMode;
        public readonly bool? IsMainMenuState;
        public Context(int regionId,int bbLevelType,int arenaSeed,int campaignStage,bool inArena,int hour,int day,bool christmasMode,bool spookyMode,bool isThroneRoom) {
            RegionId=regionId;BBLevelType=bbLevelType;ArenaSeed=arenaSeed;CampaignStage=campaignStage;InArena=inArena;
            Hour=hour;Day=day;ChristmasMode=christmasMode;SpookyMode=spookyMode;IsThroneRoom=isThroneRoom;
        }
        public Context(int gameMode,bool isMainMenuState,int regionId,int bbLevelType,int arenaSeed,int campaignStage,bool inArena,int hour,int day,bool christmasMode,bool spookyMode,bool previousIsThroneRoom)
            :this(regionId,bbLevelType,arenaSeed,campaignStage,inArena,hour,day,christmasMode,spookyMode,previousIsThroneRoom) {
            GameMode=gameMode;IsMainMenuState=isMainMenuState;
        }
        public Context(int gameMode,bool isMainMenuState,int regionId,int bbLevelType,int arenaSeed,int campaignStage,bool inArena,int hour,int day,SeasonalOptions seasonal,bool previousIsThroneRoom)
            :this(gameMode,isMainMenuState,regionId,bbLevelType,arenaSeed,campaignStage,inArena,hour,day,seasonal.Christmas,seasonal.Spooky,previousIsThroneRoom){}
        public int EnvironmentSeed=>InArena?ArenaSeed:GetSeedForStage(RegionId,CampaignStage);
    }
    public event Action OnCustomizationFinished;
    public const string AmbientMixerParameter="AmbienceLowpassVolume"; // ccm(), native-strings index 1416
    // duf sets the original ambient AudioMixer parameter. The audio owner binds
    // its recovered parameter name; the renderer never guesses that string.
    public event Action<float> OnAmbientMixerValue;
    public bool IsFinished {get;private set;}
    public Context CurrentContext {get;private set;}
    public GameObject CurrentStage {get;private set;}
    public int ChosenSeed {get;private set;}
    OriginalVisualCatalog catalog;
    J source;
    OriginalTimeOfDay timeOfDay;
    OriginalWeather weather;
    Transform instanceRoot,stars,moon,graphicsParent;
    GameObject spookyMoon,bigBuildings;
    SpriteRenderer mountains,hills,center,left,right;
    OriginalEnviroProp centerProp,centerSnow,leftProp,leftSnow,rightProp,rightSnow;
    float spacingDivisor;
    bool initialized;
    public void Initialize(OriginalVisualCatalog visualCatalog,Transform environmentRoot,OriginalTimeOfDay timeController,OriginalWeather weatherController) {
        if(initialized)throw new InvalidOperationException("Original environment customizer is already initialized");
        catalog=visualCatalog??throw new ArgumentNullException(nameof(visualCatalog));
        instanceRoot=environmentRoot??throw new ArgumentNullException(nameof(environmentRoot));
        timeOfDay=timeController??throw new ArgumentNullException(nameof(timeController));
        weather=weatherController??throw new ArgumentNullException(nameof(weatherController));
        source=catalog.Data["scene"].A.First(r=>r["script"].S=="EnvironmentCustomizer");
        stars=Node("Stars");moon=Node("Moon");spookyMoon=Node("SpookyMoon").gameObject;
        mountains=Component<SpriteRenderer>("Mountains");hills=Component<SpriteRenderer>("Hills");
        bigBuildings=Node("BigBuildingsContainer").gameObject;graphicsParent=Node("GraphicsContainerParent");
        center=Component<SpriteRenderer>("SpriteRenderer_BigBuilding_Center");
        left=Component<SpriteRenderer>("SpriteRenderer_BigBuilding_Side_1");right=Component<SpriteRenderer>("SpriteRenderer_BigBuilding_Side_2");
        centerProp=Prop("Customize_BigBuildings_Center");centerSnow=Prop("Customize_BigBuildings_Center_Snow");
        leftProp=Prop("Customize_BigBuildings_Sides_1");leftSnow=Prop("Customize_BigBuildings_Sides_1_Snow");
        rightProp=Prop("Customize_BigBuildings_Sides_2");rightSnow=Prop("Customize_BigBuildings_Sides_2_Snow");
        spacingDivisor=(float)source["data"]["bigBuildingsSpacingDividingNumber"].N;
        initialized=true;
    }
    Transform Node(string key)=>OriginalEnvironmentSource.Node(catalog,source,key,instanceRoot,source["scene_path"].S);
    T Component<T>(string key) where T:UnityEngine.Component=>OriginalEnvironmentSource.Component<T>(catalog,source,key,instanceRoot,source["scene_path"].S);
    OriginalEnviroProp Prop(string key) {
        string identity=source["assets"][key].S;
        var record=catalog.Data["scene"].A.First(r=>OriginalEnvironmentSource.Identity(r)==identity);
        return OriginalEnvironmentSource.Attach(catalog,record,instanceRoot,source["scene_path"].S,0);
    }
    public static int GetSeedForStage(int region,int stage)=>unchecked(region*stage-1); // 0x36618a4, MUL/SUB
    public static int NormalizeHour(int hour) {byte b=unchecked((byte)hour);return b>24?0:b;}
    public static bool IsMoonVisible(int hour)=>unchecked((uint)(hour-20))<0xfffffff0u;
    // Native reads Bounds+12 (extents.x), not Bounds.size.x. Do not "correct" it.
    public static float SideBuildingX(float centerExtent,float sideExtent,float divisor,bool onRight) {
        float half=onRight?.5f:-.5f;
        return (centerExtent*half+sideExtent*half)/divisor;
    }
    public void Customize(Context context) {
        if(!initialized)throw new InvalidOperationException("Initialize the original environment before customization");
        if(context==null)throw new ArgumentNullException(nameof(context));
        if(context.BBLevelType<0||context.BBLevelType>4)throw new ArgumentOutOfRangeException(nameof(context.BBLevelType));
        if(context.Day<1||context.Day>31)throw new ArgumentOutOfRangeException(nameof(context.Day));
        ValidateSpecialContext(context);
        J region=catalog.Data["scene"].A.First(r=>r["script"].S=="RegionConfiguration"&&r["data"]["regionId"].I==context.RegionId);
        int stageIndex=region["data"]["stages"].A.FindIndex(s=>s["levelType"].I==context.BBLevelType);
        if(stageIndex<0)throw new InvalidOperationException("Original region has no matching BBLevelType");
        CurrentIsThroneRoom=ResolveSpecialThroneRoom(context);
        string stageKey="stages["+stageIndex+"]",graphicsKey=OriginalEnvironmentSpecials.GraphicsKey(region,context.BBLevelType,CurrentIsThroneRoom,out _),prefabId=region["assets"][graphicsKey].S;
        var prefab=catalog.Get<GameObject>(prefabId);
        if(prefab==null)throw new InvalidOperationException("Original stage prefab is not bound: "+prefabId);
        CurrentContext=context;IsFinished=false;weather.CanAlterWeatherType=false;
        ChosenSeed=context.EnvironmentSeed;Random.InitState(ChosenSeed);
        int hour=NormalizeHour(context.Hour);
        bool holidayLighting=OriginalEnvironmentSpecials.PermitHolidayLighting(context.IsMainMenuState??false,CurrentIsThroneRoom);
        timeOfDay.ApplyEnvironmentContext(context.RegionId,hour,context.ChristmasMode,context.SpookyMode,holidayLighting);
        weather.ApplyEnvironmentContext(context.RegionId,context.ArenaSeed,context.ChristmasMode,CurrentIsThroneRoom);
        mountains.sprite=catalog.Get<Sprite>(region["assets"][context.ChristmasMode?"Mountains_Snow":"Mountains"].S);
        hills.sprite=catalog.Get<Sprite>(region["assets"][context.ChristmasMode?"Hills_Near_Snow":"Hills_Near"].S);
        RandomizeHorizon(mountains);RandomizeHorizon(hills);
        float degrees=(hour/24f)*360f;
        stars.SetPositionAndRotation(stars.position,Quaternion.Euler(0,0,degrees));
        stars.gameObject.SetActive(IsMoonVisible(hour));
        var moonSprite=moon.GetChild(0).GetComponent<SpriteRenderer>();
        moon.SetPositionAndRotation(moon.position,Quaternion.Euler(0,0,-degrees));
        moonSprite.transform.SetPositionAndRotation(moonSprite.transform.position,Quaternion.Euler(0,0,degrees));
        moonSprite.sprite=catalog.Get<Sprite>(source["assets"]["MoonSprites["+(context.Day/2)+"]"].S);
        CustomizeSpecialMoon(context,hour,moonSprite);
        bigBuildings.SetActive(false);bigBuildings.SetActive(true);
        // Destroy is deferred just as in the original. New stage sibling index
        // must therefore see the old children until the end of this frame.
        foreach(Transform child in graphicsParent)Destroy(child.gameObject);
        OnAmbientMixerValue?.Invoke(-80f);
        OriginalStageActors.Prepare(catalog,context,()=>timeOfDay.TimeOfDay,CurrentIsThroneRoom);
        CurrentStage=Instantiate(prefab,graphicsParent,false);
        CurrentStage.name=catalog.Data["assets"][prefabId]["scene_path"].S;
        OriginalStageActors.SetInstance(CurrentStage.transform);
        // Source ground customizes in OnEnable during Instantiate, before the
        // global buildings. Props customize later in their individual Starts.
        var floor=CurrentStage.GetComponent<OriginalGroundSource>()??CurrentStage.AddComponent<OriginalGroundSource>();
        if(!floor.IsInitialized)floor.ApplyEnvironmentContext(catalog,context.ArenaSeed,context.ChristmasMode);
        if(graphicsKey=="throneRoomGraphics")OriginalInteriorBloodSurface.Attach(catalog,CurrentStage.transform,CurrentStage.name);
        if(context.BBLevelType==4)OnAmbientMixerValue?.Invoke(OriginalEnvironmentSpecials.CastleAmbienceValue(CurrentIsThroneRoom));
        OriginalEnvironmentSource.AttachAutoProps(catalog,CurrentStage.transform,CurrentStage.name,context.ArenaSeed);
        string bigKey=stageKey+".bigBuildings";
        centerProp.Customize(OriginalEnvironmentSource.Sprites(catalog,region,bigKey+".center"),(float)ChosenSeed);
        centerSnow.Customize(OriginalEnvironmentSource.Sprites(catalog,region,bigKey+".centerSnow"),(float)ChosenSeed);
        centerSnow.spriteRenderer.enabled=context.ChristmasMode;
        bool sides=context.BBLevelType!=0;
        leftProp.gameObject.SetActive(sides);rightProp.gameObject.SetActive(sides);
        leftSnow.gameObject.SetActive(sides);rightSnow.gameObject.SetActive(sides);
        if(sides) {
            var sideSprites=OriginalEnvironmentSource.Sprites(catalog,region,bigKey+".sides");
            var snowSprites=OriginalEnvironmentSource.Sprites(catalog,region,bigKey+".sidesSnow");
            leftProp.Customize(sideSprites,(float)ChosenSeed);leftSnow.Customize(snowSprites,(float)ChosenSeed);leftSnow.spriteRenderer.enabled=context.ChristmasMode;
            rightProp.Customize(sideSprites,(float)ChosenSeed);rightSnow.Customize(snowSprites,(float)ChosenSeed);rightSnow.spriteRenderer.enabled=context.ChristmasMode;
        }
        float middleExtent=center.bounds.extents.x;
        leftProp.transform.localPosition=new Vector3(SideBuildingX(middleExtent,left.bounds.extents.x,spacingDivisor,false),0,0);
        leftSnow.transform.localPosition=leftProp.transform.localPosition;
        rightProp.transform.localPosition=new Vector3(SideBuildingX(middleExtent,right.bounds.extents.x,spacingDivisor,true),0,0);
        rightSnow.transform.localPosition=rightProp.transform.localPosition;
        weather.CanAlterWeatherType=true;
        if(context.InArena&&weather.isActiveAndEnabled)weather.ActivateWeatherType();
        IsFinished=true;timeOfDay.OnCustomizationFinished();OnCustomizationFinished?.Invoke();
    }
    static void RandomizeHorizon(SpriteRenderer renderer) {
        var position=renderer.transform.localPosition;position.x=Random.Range(-5f,5f);renderer.transform.localPosition=position;
        if(Random.Range(0,100)>50)renderer.flipX=true;
    }
}
