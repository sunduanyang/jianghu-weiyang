using UnityEngine;

// Serialized separately for the seven audited first-stage fire parents.
// Mirrors LightActivator.OnEnable; no Update or time-change subscription.
public sealed class OriginalStageLightEnable : MonoBehaviour {
    public string SourceIdentity;
    void OnEnable() {
        var data=OriginalStageActors.Record(SourceIdentity)["data"];
        gameObject.SetActive(OriginalLightActivator.SourceActive((float)data["HoursActive"]["min"].N,(float)data["HoursActive"]["max"].N,data["Inverted"].B,OriginalStageActors.Hour));
    }
}
