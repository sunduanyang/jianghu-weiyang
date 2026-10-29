using System;
using System.Linq;
using Jianghu;
using UnityEngine;

// LightActivator.OnEnable 0x3444ad4. This script filters its own activation;
// the original contains no time-change subscription or per-frame updater.
public sealed class OriginalLightActivator : MonoBehaviour {
    float minimum,maximum;
    bool inverted,initialized;
    Func<int> hour;
    public static OriginalLightActivator[] Attach(OriginalVisualCatalog catalog,Transform environmentRoot,Func<int> hour) {
        if(catalog==null||environmentRoot==null||hour==null)throw new ArgumentNullException("Original light-activator context missing");
        var asset=Resources.Load<TextAsset>("OriginalLightActivatorSource");
        if(asset==null)throw new InvalidOperationException("OriginalLightActivatorSource is missing");
        var rows=J.Parse(asset.text)["records"].A.Where(row=>row["scope"].S=="clear").ToArray();
        if(rows.Length!=2)throw new InvalidOperationException("Original Clear ambience activation scope changed");
        return rows.Select(row=> {
            var node=OriginalEnvironmentSource.IdentityNode(catalog,row["node_identity"].S,environmentRoot,environmentRoot.name);
            var component=node.GetComponent<OriginalLightActivator>()??node.gameObject.AddComponent<OriginalLightActivator>();
            if(!component.initialized)component.Initialize((float)row["data"]["HoursActive"]["min"].N,(float)row["data"]["HoursActive"]["max"].N,row["data"]["Inverted"].B,hour,row["data"]["enabled"].B);
            return component;
        }).ToArray();
    }
    public void Initialize(float originalMinimum,float originalMaximum,bool originalInverted,Func<int> currentHour,bool sourceEnabled=true) {
        if(initialized)throw new InvalidOperationException("Original light activator is already initialized");
        minimum=originalMinimum;maximum=originalMaximum;inverted=originalInverted;hour=currentHour??throw new ArgumentNullException(nameof(currentHour));initialized=true;
        enabled=sourceEnabled;if(isActiveAndEnabled)OnEnable();
    }
    public static bool SourceActive(float minimum,float maximum,bool inverted,int hour) {
        int start=OriginalEnviroProp.NativeSeedInteger(minimum),end=OriginalEnviroProp.NativeSeedInteger(maximum);
        bool contains=Enumerable.Range(start,unchecked(end-start)).Contains(unchecked((byte)hour));
        return inverted?contains:!contains;
    }
    void OnEnable(){if(initialized)gameObject.SetActive(SourceActive(minimum,maximum,inverted,hour()));}
}
