using UnityEngine;

// Kept on the original Ground GameObject so Unity schedules the recovered
// source OnEnable relative to crowd Awakes, rather than a guessed sorted pass.
public sealed class OriginalStageGroundEnable : MonoBehaviour {
    public string SourceRootName;
    void OnEnable() {
        var root=OriginalStageActors.InstanceRoot(transform,SourceRootName);
        var ground=root.GetComponent<OriginalGroundSource>()??root.gameObject.AddComponent<OriginalGroundSource>();
        if(!ground.IsInitialized)ground.ApplyEnvironmentContext(OriginalStageActors.Catalog,OriginalStageActors.Context.ArenaSeed,OriginalStageActors.Context.ChristmasMode,SourceRootName);
    }
}
