using System;
using System.Linq;
using Jianghu;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// TimeOfDayController 0x3687d24/0x36883d0/0x3688d84/0x36891d8.
// The native hour changes at transitions, rather than on a per-frame timer.
public sealed class OriginalTimeOfDay : MonoBehaviour {
    public int regionId=1;
    OriginalVisualCatalog catalog;
    J source;
    Gradient globalGradient,sceneGradient;
    AnimationCurve windowPower,rimPower;
    Light2D globalLight;
    Light2D[] sceneLights;
    Material[] windowMaterials,rimMaterials;
    GameObject stars;
    OriginalTopRimRendererFeature rim;
    byte hour,lastHour=255;
    bool initialized,explicitContext;
    public int TimeOfDay {get=>hour;set{hour=(byte)(unchecked((byte)value)%24);Apply(false);}}
    public bool AreLightsOn {
        get {
            int on=source["data"]["lightsOnHour"].I,off=source["data"]["lightsOffHour"].I;
            return off<on?hour>=on||hour<off:hour>=on&&hour<off;
        }
    }
    static AnimationCurve Curve(J data)=>new AnimationCurve(data["m_Curve"].A.Select(k=>new Keyframe((float)k["time"].N,(float)k["value"].N,(float)k["inSlope"].N,(float)k["outSlope"].N){weightedMode=(WeightedMode)k["weightedMode"].I,inWeight=(float)k["inWeight"].N,outWeight=(float)k["outWeight"].N}).ToArray());
    static Transform SceneNode(Scene scene,string path) {
        int slash=path.IndexOf('/');string root=slash<0?path:path.Substring(0,slash);
        var go=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==root);
        return go==null?null:slash<0?go.transform:go.transform.Find(path.Substring(slash+1));
    }
    Transform Node(string field) {
        string identity=source["assets"][field].S;
        return SceneNode(gameObject.scene,catalog.Data["assets"][identity]["scene_path"].S);
    }
    Material Material(string field)=>catalog.Get<Material>(source["assets"][field].S);
    void Start() {Initialize();}
    void Initialize() {
        if(initialized)return;
        catalog=Resources.Load<OriginalVisualCatalog>("OriginalVisualCatalog");
        if(catalog==null)throw new InvalidOperationException("Original time-of-day visual catalog is missing");
        source=catalog.Data["scene"].A.First(x=>x["script"].S=="TimeOfDayController");
        globalLight=Node("globalLight").GetComponent<Light2D>();
        sceneLights=source["data"]["sceneLights"].A.Select((v,i)=>Node("sceneLights["+i+"]").GetComponent<Light2D>()).ToArray();
        stars=Node("Stars").gameObject;
        windowMaterials=source["data"]["windowMaterials"].A.Select((v,i)=>Material("windowMaterials["+i+"]")).ToArray();
        rimMaterials=source["data"]["topRimMaterials"].A.Select((v,i)=>Material("topRimMaterials["+i+"]")).ToArray();
        windowPower=Curve(source["data"]["windowLightsPowerCurve"]);rimPower=Curve(source["data"]["globalRimIntensityCurve"]);
        rim=Resources.FindObjectsOfTypeAll<OriginalTopRimRendererFeature>().FirstOrDefault(f=>f.isActive);
        initialized=true;
        // Native OnEnable reads the operating system's local hour.
        if(!explicitContext)hour=(byte)DateTime.Now.Hour;
        SetRegion(regionId);
    }
    void OnEnable(){if(initialized&&!explicitContext){hour=(byte)DateTime.Now.Hour;Apply(false);}}
    public void ApplyEnvironmentContext(int region,int explicitHour,bool christmas,bool spooky,bool permitHolidayLighting=true) {
        explicitContext=true;regionId=region;hour=(byte)OriginalEnvironmentCustomizer.NormalizeHour(explicitHour);
        Initialize();SetRegion(region,christmas,spooky,permitHolidayLighting);
    }
    public void SetRegion(int region,bool christmas=false,bool spooky=false,bool permitHolidayLighting=true) {
        regionId=region;if(!initialized)return;
        J data=source["data"];
        if(permitHolidayLighting&&christmas) {
            globalGradient=OriginalVisualCatalog.Gradient(data["globalLightGradientChristmas"]);
            sceneGradient=OriginalVisualCatalog.Gradient(data["sceneLightsGradientChristmas"]);
        }
        else if(permitHolidayLighting&&spooky) {
            globalGradient=OriginalVisualCatalog.Gradient(data["globalLightGradientSpooky"]);
            sceneGradient=OriginalVisualCatalog.Gradient(data["sceneLightsGradientSpooky"]);
        }
        else {
            J regionData=catalog.Data["scene"].A.First(x=>x["script"].S=="RegionConfiguration"&&x["data"]["regionId"].I==regionId)["data"];
            globalGradient=OriginalVisualCatalog.Gradient(regionData["GlobalLightGradient"]);
            sceneGradient=OriginalVisualCatalog.Gradient(regionData["SceneLightsGradient"]);
        }
        Apply(true);
    }
    // Native dbq only advances the field; customization completion applies it.
    public void OnTransitionSwitching(bool paused){if(!paused)hour=(byte)(hour<23?hour+1:0);}
    public void OnCustomizationFinished()=>Apply(true);
    public void SetWeather(int weather) {
        if(!initialized)return;
        foreach(var pair in source["data"]["weatherLightingPairs"].A)if(pair["WeatherType"].I==weather)globalLight.intensity=(float)pair["globalLightIntensity"].N;
    }
    void Apply(bool force) {
        if(!initialized||(!force&&hour==lastHour))return;
        lastHour=hour;float t=hour/23f;bool night=AreLightsOn;J d=source["data"];
        globalLight.color=globalGradient.Evaluate(t);
        foreach(var material in windowMaterials) {
            if(material==null)continue;
            material.SetColor("_CavityWindowsColor",OriginalVisualCatalog.Color(d[night?"windowColorByNight":"cavityWindowColorByDay"]));
            material.SetColor("_PaperWindowsColor",OriginalVisualCatalog.Color(d[night?"windowColorByNight":"paperWindowColorByDay"]));
            material.SetFloat("_LightPower",night?windowPower.Evaluate(t):0);
        }
        stars.SetActive(night);
        Color sceneColor=sceneGradient.Evaluate(t);foreach(var light in sceneLights)light.color=sceneColor;
        if(rim!=null)rim.settings.rimIntensity=rimPower.Evaluate(t);
        // Source deliberately evaluates windowLightsPowerCurve for this slot.
        foreach(var material in rimMaterials)if(material!=null)material.SetFloat("_RimIntensity",windowPower.Evaluate(t));
    }
}
