using System;
using System.Collections;
using System.Linq;
using Jianghu;
using UnityEngine;

// Explicit counterpart of the source arena globals for prefab lifecycle code.
// Prepare is called before Instantiate; no extra RNG or object reordering.
public static class OriginalStageActors {
    public const string FirstStageRoot="2_GraphicsContainer_BK_Monastery";
    public static OriginalVisualCatalog Catalog {get;private set;}
    public static OriginalEnvironmentCustomizer.Context Context {get;private set;}
    static Func<int> hour;
    static Transform instance;
    static int instanceFrame;
    public static bool IsThroneRoom {get;private set;}
    public static event Action<Transform> LocalPlayerSpawned,LocalPlayerDespawned;
    public static event Action<int,int> NetworkedStateChanged;
    public static void Prepare(OriginalVisualCatalog catalog,OriginalEnvironmentCustomizer.Context context,Func<int> currentHour,bool? resolvedIsThroneRoom=null) {
        Catalog=catalog??throw new ArgumentNullException(nameof(catalog));Context=context??throw new ArgumentNullException(nameof(context));hour=currentHour??throw new ArgumentNullException(nameof(currentHour));
        instance=null;IsThroneRoom=resolvedIsThroneRoom??context.IsThroneRoom;
        LocalPlayerSpawned=null;LocalPlayerDespawned=null;NetworkedStateChanged=null;
    }
    public static int GameMode=>Context?.GameMode??throw new InvalidOperationException("Original stage controller needs explicit GameMode");
    public static bool IsMainMenuState=>Context?.IsMainMenuState??throw new InvalidOperationException("Original stage controller needs explicit LocalGameController.IsMainMenuState");
    public static void SetInstance(Transform stageRoot) {instance=stageRoot??throw new ArgumentNullException(nameof(stageRoot));instanceFrame=Time.frameCount;}
    public static bool StartsReady {
        get {
            if(instance==null)throw new InvalidOperationException("Set the original stage instance before waiting for its Starts");
            if(Time.frameCount==instanceFrame)return false;
            return instance.GetComponentsInChildren<OriginalBystandersController>(true).All(p=>!p.isActiveAndEnabled||p.HasStarted)&&
                instance.GetComponentsInChildren<OriginalBystanderMovement>(true).All(p=>!p.isActiveAndEnabled||p.HasStarted)&&
                instance.GetComponentsInChildren<OriginalStageChristmasActivation>(true).All(p=>!p.isActiveAndEnabled||p.HasStarted)&&
                instance.GetComponentsInChildren<OriginalInteriorBloodSurface>(true).All(p=>!p.isActiveAndEnabled||p.HasStarted);
        }
    }
    // Source runner startup is asynchronous; NetworkPlayer.Spawned is later
    // than the stage Starts. Wait before creating the local player, never
    // cache an early event and replay it after subscribers appear.
    public static IEnumerator WaitForStart() {while(!StartsReady)yield return null;}
    public static void NotifyLocalPlayerSpawned(Transform player) {
        if(player==null)throw new ArgumentNullException(nameof(player));
        if(!StartsReady)throw new InvalidOperationException("Wait for original stage Starts before local player creation/Spawned dispatch");
        LocalPlayerSpawned?.Invoke(player);
    }
    public static void NotifyLocalPlayerDespawned(Transform player) {LocalPlayerDespawned?.Invoke(player);}
    // This is NetworkedGameController.OnStateChanged, not a menu-state event.
    public static void ApplyGameState(int previous,int current) {NetworkedStateChanged?.Invoke(previous,current);}
    public static int Hour=>hour==null?throw new InvalidOperationException("Prepare original stage actors before Instantiate"):hour();
    public static J Record(string identity) {
        if(Catalog==null||Context==null)throw new InvalidOperationException("Prepare original stage actors before Instantiate");
        return Catalog.Data["scene"].A.Single(row=>OriginalEnvironmentSource.Identity(row)==identity);
    }
    public static Transform InstanceRoot(Transform node,string sourceRootName) {
        for(var root=node;root!=null;root=root.parent)
            if(root.name==sourceRootName||root.name==sourceRootName+"(Clone)")return root;
        throw new InvalidOperationException("Original actor source instance root is missing: "+sourceRootName);
    }
}
