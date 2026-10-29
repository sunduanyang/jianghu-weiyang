using System;
using System.Collections;
using System.Linq;
using Jianghu;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Random=UnityEngine.Random;

// WeatherManager 0x3642008..0x3642de4. Original world objects and source
// RNGs are retained; this does not author replacement weather particles.
public sealed class OriginalWeather : MonoBehaviour {
    public int regionId=1,arenaSeed;
    public bool christmasMode,interior;
    public bool CanAlterWeatherType;
    public int CurrentWeatherType {get;private set;}
    public bool WeatherInitialized {get;private set;}
    public event Action<int> OnWeatherChanged;
    OriginalVisualCatalog catalog;
    J source;
    GameObject[] weatherContainers,precipitation;
    Transform[] clouds;
    Light2D[] lightningLights;
    int[] normalLightningLayers;
    GameObject lightningFar,lightningClose;
    OriginalTimeOfDay timeOfDay;
    System.Random lightningRandom;
    Coroutine lightning;
    static int previousWeather;
    static readonly float[] WindSpeed={.25f,.35f,.45f,.75f,.25f,.35f};
    static readonly int[] ChristmasWeather={4,4,4,5};
    static Transform SceneNode(Scene scene,string path) {
        int slash=path.IndexOf('/');string root=slash<0?path:path.Substring(0,slash);
        var go=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==root);
        return go==null?null:slash<0?go.transform:go.transform.Find(path.Substring(slash+1));
    }
    Transform Node(J record,string field) {
        string identity=record["assets"][field].S;
        var node=SceneNode(gameObject.scene,catalog.Data["assets"][identity]["scene_path"].S);
        if(node==null)throw new InvalidOperationException("Original weather object is missing: "+field+" / "+identity);
        return node;
    }
    void Awake() {
        catalog=Resources.Load<OriginalVisualCatalog>("OriginalVisualCatalog");
        if(catalog==null)throw new InvalidOperationException("Original weather visual catalog is missing");
        source=catalog.Data["scene"].A.First(r=>r["script"].S=="WeatherManager");J d=source["data"];
        CurrentWeatherType=d["CurrentWeatherType"].I;CanAlterWeatherType=false;WeatherInitialized=false;
        weatherContainers=d["weatherPairs"].A.Select((v,i)=>Node(source,"weatherPairs["+i+"].Container").gameObject).ToArray();
        clouds=d["clouds"].A.Select((v,i)=>Node(source,"clouds["+i+"]")).ToArray();
        precipitation=d["precipitationContainers"].A.Select((v,i)=>Node(source,"precipitationContainers["+i+"]").gameObject).ToArray();
        lightningFar=Node(source,"lightningFarContainer").gameObject;lightningClose=Node(source,"lightningCloseContainer").gameObject;
        lightningLights=d["lightningLights"].A.Select((v,i)=>Node(source,"lightningLights["+i+"]").GetComponent<Light2D>()).ToArray();
        // Native Awake repeatedly assigns this field; the last light wins.
        foreach(var light in lightningLights)normalLightningLayers=light.targetSortingLayers;
        foreach(var record in catalog.Data["scene"].A.Where(r=>r["script"].S=="LightningFlash")) {
            string path=record["scene_path"].S;var node=SceneNode(gameObject.scene,path);if(node==null)continue;
            var flash=node.GetComponent<OriginalLightningFlash>()??node.gameObject.AddComponent<OriginalLightningFlash>();flash.Initialize((float)record["data"]["disableAfterXSeconds"].N);
        }
        // Native Awake invokes once after .25 scaled seconds.
        Invoke(nameof(ChangeWeatherType),.25f);
    }
    public void ChangeWeatherType() {
        Random.InitState(Time.frameCount);int roll=Random.Range(0,100);
        int next=CurrentWeatherType;bool british=regionId==1;
        switch(previousWeather) {
            case 0:next=roll<(british?51:76)?1:0;break;
            case 1:next=roll<25?0:roll<50?1:2;break;
            case 2:next=roll<(british?25:75)?1:roll<(british?50:95)?3:2;break;
            case 3:next=roll<(british?25:5)?3:2;break;
            case 4:next=2;break;
            case 5:next=3;break;
        }
        previousWeather=CurrentWeatherType=next;ActivateWeatherType();
    }
    public void SetWeatherType(int weather){CurrentWeatherType=weather;ActivateWeatherType();}
    // Environment customization only refreshes these source globals. It does
    // not roll another weather type; the native .25s Awake invocation stays.
    public void ApplyEnvironmentContext(int region,int currentSeed,bool christmas,bool throneRoom) {
        regionId=region;arenaSeed=currentSeed;christmasMode=christmas;interior=throneRoom;
    }
    public void ActivateWeatherType() {
        int actual=christmasMode&&CurrentWeatherType>=0&&CurrentWeatherType<4?ChristmasWeather[CurrentWeatherType]:CurrentWeatherType;
        Shader.SetGlobalFloat("_WindSpeed",actual>=0&&actual<WindSpeed.Length?WindSpeed[actual]:.25f);
        Shader.SetGlobalFloat("_SnowAmount",christmasMode?.05f:3);
        var pairs=source["data"]["weatherPairs"].A;
        for(int i=0;i<weatherContainers.Length;i++)weatherContainers[i].SetActive(pairs[i]["WeatherType"].I==actual);
        var cloudRandom=new System.Random(arenaSeed);
        foreach(var cloud in clouds){var position=cloud.localPosition;position.x=(float)(cloudRandom.NextDouble()*2000-1000);position.z=0;cloud.localPosition=position;}
        lightningRandom=new System.Random(arenaSeed);
        if(lightning!=null)StopCoroutine(lightning);lightning=StartCoroutine(Lightning());
        foreach(var container in precipitation)container.SetActive(!interior);
        var layers=interior?source["data"]["lightningLightLayersInterior"].A.Select(v=>v.I).ToArray():normalLightningLayers;
        foreach(var light in lightningLights)light.targetSortingLayers=layers;
        if(timeOfDay==null)timeOfDay=FindAnyObjectByType<OriginalTimeOfDay>();if(timeOfDay!=null)timeOfDay.SetWeather(actual);
        WeatherInitialized=true;OnWeatherChanged?.Invoke(actual);
    }
    IEnumerator Lightning() {
        while(true) {
            yield return new WaitForSeconds((float)(lightningRandom.NextDouble()*10)+10);
            yield return new WaitForSeconds(1);
            (lightningRandom.NextDouble()>.5?lightningFar:lightningClose).SetActive(true);
        }
    }
}
