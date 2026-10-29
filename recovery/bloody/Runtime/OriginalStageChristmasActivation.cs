using UnityEngine;

// ActivateGameObjectChristmasMode.Start 0x34448f0. Multiple source components
// can live on the same GameObject; the Editor pass binds each source identity.
public sealed class OriginalStageChristmasActivation : MonoBehaviour {
    public string SourceIdentity,SourceRootName;
    public bool HasStarted {get;private set;}
    public static bool SourceActive(bool activeOnChristmas,bool christmas)=>activeOnChristmas?christmas:!christmas;
    void Start() {
        var row=OriginalEnvironmentSpecialSource.Record(SourceIdentity);
        var root=OriginalStageActors.InstanceRoot(transform,SourceRootName);
        bool active=SourceActive(row["data"]["activeOnChristmas"].B,OriginalStageActors.Context.ChristmasMode);
        for(int i=0;i<row["data"]["gameObjects"].A.Count;i++)
            OriginalEnvironmentSpecialSource.Node(row,"gameObjects["+i+"]",root,SourceRootName).gameObject.SetActive(active);
        HasStarted=true;
    }
}
