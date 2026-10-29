using System;
using System.Linq;
using Jianghu;
using UnityEngine;
using Random=UnityEngine.Random;

// Source BystanderMovement Start 0x36c1994, dsg 0x36c1b70,
// FollowingTick 0x36c1d80, AnimationTick 0x36c1ee8. No synthetic seed.
public sealed class OriginalBystanderMovement : MonoBehaviour {
    public string SourceIdentity,SourceRootName;
    public bool HasStarted {get;private set;}
    public bool IsMoving {get;private set;}
    public bool CanCheer {get;private set;}
    public Transform FollowTarget {get;private set;}
    J record,data;
    Transform sourceRoot,baseTransform;
    Animator animator;
    Vector3 originalRotation,flippedRotation,originalScale,flippedScale;
    AnimationCurve movementCurve;
    int walkHash,idleHash,cheerHash;
    sealed class MoveTween {public Vector3 from,to;public float elapsed;public bool started;}
    static Vector3 Vector(J j)=>new Vector3((float)j["x"].N,(float)j["y"].N,(float)j["z"].N);
    void Bind() {
        if(record!=null)return;
        record=OriginalStageActors.Record(SourceIdentity);data=record["data"];
        sourceRoot=OriginalStageActors.InstanceRoot(transform,SourceRootName);
        baseTransform=OriginalEnvironmentSource.Node(OriginalStageActors.Catalog,record,"baseTransform",sourceRoot,SourceRootName);
        animator=OriginalEnvironmentSource.Component<Animator>(OriginalStageActors.Catalog,record,"animator",sourceRoot,SourceRootName);
        if(baseTransform==null||animator==null)throw new InvalidOperationException("Original bystander movement bindings missing: "+SourceIdentity);
        originalRotation=Vector(data["originalEulerRotation"]);flippedRotation=Vector(data["flippedEulerRotation"]);
        originalScale=Vector(data["originalScale"]);flippedScale=Vector(data["flippedScale"]);
        movementCurve=new AnimationCurve(data["MovementCurve"]["m_Curve"].A.Select(k=>new Keyframe((float)k["time"].N,(float)k["value"].N,(float)k["inSlope"].N,(float)k["outSlope"].N){weightedMode=(WeightedMode)k["weightedMode"].I,inWeight=(float)k["inWeight"].N,outWeight=(float)k["outWeight"].N}).ToArray());
        walkHash=Animator.StringToHash("OnWalk");idleHash=Animator.StringToHash("OnIdle");cheerHash=Animator.StringToHash("OnCheer");
    }
    void Start() {
        Bind();
        OriginalStageActors.LocalPlayerSpawned+=OnLocalPlayerSpawned;
        OriginalStageActors.LocalPlayerDespawned+=OnLocalPlayerDespawned;
        HasStarted=true;
    }
    void OnDestroy() {
        OriginalStageActors.LocalPlayerSpawned-=OnLocalPlayerSpawned;
        OriginalStageActors.LocalPlayerDespawned-=OnLocalPlayerDespawned;
    }
    void OnLocalPlayerSpawned(Transform player)=>SetFollowTarget(player);
    void OnLocalPlayerDespawned(Transform player)=>SafelyDisable(); // dsf tail-branches SafelyDisable.
    public void SetFollowTarget(Transform target) {
        Bind();FollowTarget=target;
        animator.speed=0;Invoke(nameof(SetIdle),Random.Range(0f,2f));
        originalRotation=baseTransform.localRotation.eulerAngles;
        InvokeRepeating(nameof(AnimationTick),Random.Range(.3f,.55f),.65f);
        int sit=data["chanceToSitAroundOutOf10"].I;
        if(sit>0&&sit<Random.Range(0,11)) {
            float delay=Random.Range((float)data["followingFrequencyRange"]["min"].N,(float)data["followingFrequencyRange"]["max"].N);
            InvokeRepeating(nameof(FollowingTick),delay,delay);
        }
        int cheer=data["chanceToCheerOutOf10"].I;
        if(cheer>0&&cheer<Random.Range(0,11))CanCheer=true;
    }
    public void SafelyDisable() {
        // Native changes the target and cancels Invoke calls only. Existing
        // DOMove tweens and the two boolean flags are intentionally retained.
        FollowTarget=transform;CancelInvoke();
    }
    public void FollowingTick() {
        if(IsMoving)return;
        if(FollowTarget==null)throw new InvalidOperationException("Original bystander follow target missing");
        Vector3 target=FollowTarget.position,position=baseTransform.position;
        float x=Random.Range(-1f,1f)+target.x+(position.x-target.x)/1.05f;
        float y=transform.position.y,z=transform.position.z;
        StartMove(new Vector3(x,y,z));
        Invoke(nameof(SetIdle),1.65f);animator.SetTrigger(walkHash);IsMoving=true;
    }
    public void AnimationTick() {
        if(FollowTarget==null)throw new InvalidOperationException("Original bystander follow target missing");
        bool flipped=FollowTarget.position.x<=baseTransform.position.x;
        transform.SetPositionAndRotation(transform.position,Quaternion.Euler(flipped?flippedRotation:originalRotation));
        transform.localScale=flipped?flippedScale:originalScale;
        animator.speed=Random.Range(.65f,.85f);
    }
    public void SetIdle() {animator.speed=1;animator.SetTrigger(idleHash);IsMoving=false;}
    public void SetCheer() {
        if(!CanCheer)return;
        CancelInvoke(nameof(FollowingTick));
        Invoke(nameof(StartCheeringAnimation),Random.Range(1f,2f));
        Invoke(nameof(SetIdle),Random.Range(2.5f,5.5f));IsMoving=false;
    }
    public void StartCheeringAnimation()=>animator.SetTrigger(cheerHash);
    void StartMove(Vector3 destination) {
        // Source DOMove(2,false).SetEase(MovementCurve), default scaled Update.
        // No DOKill/SetLink/SetUpdate occurs in the source method.
        var move=new MoveTween{to=destination};
        OriginalStageTweenHost.Add(delta=> {
            if(baseTransform==null)return false;
            if(!move.started){move.from=baseTransform.position;move.started=true;}
            move.elapsed+=delta;float t=Mathf.Clamp01(move.elapsed/2f);
            baseTransform.position=Vector3.LerpUnclamped(move.from,move.to,movementCurve.Evaluate(t));
            return t<1;
        });
    }
}
