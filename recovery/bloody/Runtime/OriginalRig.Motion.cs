using System;
using Jianghu;
using UnityEngine;

public sealed partial class OriginalRig {
    int motionTick,lastLeftTap=-1000,lastRightTap=-1000,lastLeftDouble=-1000,lastRightDouble=-1000;
    int doubleWindowTicks,kickWindowTicks,moveUntilTick,forceLeftUntilTick,forceRightUntilTick;
    int kickUntilTick,kickStartTick,pushUntilTick;
    bool pendingLeftTap,pendingRightTap,kickPaid,pushDisabledForVitality;
    float moveDirection,gaitVelocityX;
    Animation sourceKickAnimation;
    GameObject sourceKickEffector;
    Vector3 initialPelvisPoint,initialLeftFootPoint,initialRightFootPoint;
    float beingKickedUntil;
    public bool OriginalKicking {get;private set;}
    public bool OriginalBeingKicked=>Time.time<beingKickedUntil;
    public bool originalGamepadControls;
    Vector2 desktopLeftAim,desktopRightAim;
    bool desktopLeftPose,desktopRightPose;
    public int OriginalMotionTick=>motionTick;
    public float OriginalMoveDirection=>moveDirection;
    public GameObject OriginalKickEffector=>sourceKickEffector;
    int Ticks(float seconds)=>Mathf.Max(1,Mathf.RoundToInt(seconds/Time.fixedDeltaTime));

    void InitializeMotion() {
        // Spawned 0x3434b64: banker's rounding, with a minimum of one tick.
        doubleWindowTicks=Ticks(Number("doubleTapWindow"));kickWindowTicks=Ticks(Number("kickWindow"));
        initialLeftFootPoint=leftFootPoint.localPosition;initialRightFootPoint=rightFootPoint.localPosition;
        sourceKickAnimation=Resolve<Animation>(mover["kickAnimation"]);
        sourceKickEffector=ResolveTransform(mover["kickEffector"]).gameObject;
        sourceKickEffector.SetActive(false);
        if(sourceKickAnimation!=null)sourceKickAnimation.enabled=false;
        foreach(var record in source["controllers"]["KickReceiver"].A) {
            var go=ResolveTransform(record["data"]["m_GameObject"]);
            var receiver=go.gameObject.AddComponent<OriginalKickReceiver>();
            receiver.Initialize(this,record["data"]);
        }
    }
    // Same raw button domain as CharacterMover.OnGoLeftInput/OnGoRightInput.
    public void OnOriginalMoveTap(bool left) {if(left)pendingLeftTap=true;else pendingRightTap=true;}
    public void KeepOriginalMove(float direction) {
        // clw 0x343617c. This method is also the PC/gamepad adapter boundary.
        if(OriginalKicking||passedOut||dead)return;
        moveDirection=Mathf.Clamp(direction,-1,1);moveUntilTick=motionTick+Ticks(Number("moveSustainSeconds"));
    }
    public void SetOriginalHandInputs(Vector2 nativeLeftNub,Vector2 nativeRightNub,bool nativeLeftDown,bool nativeRightDown) {
        // PlayerInputHandler.FixedUpdateNetwork 0x365ac48 crosses the active
        // flags while storing each nub in its original left/right field.
        leftInput=SanitizeNub(nativeLeftNub);rightInput=SanitizeNub(nativeRightNub);
        rightActive=nativeLeftDown;leftActive=nativeRightDown;
    }
    // PC-only posture targets, independent of the source pressed/attack flags.
    // AI and touch keep the original passive-hand behavior.
    public void SetDesktopHandPoseTargets(Vector2 nativeLeftAim,Vector2 nativeRightAim,bool retainLeft,bool retainRight) {
        desktopLeftAim=SanitizeNub(nativeLeftAim);desktopRightAim=SanitizeNub(nativeRightAim);
        desktopLeftPose=human&&retainLeft;desktopRightPose=human&&retainRight;
    }
    public void OnOriginalScreenTap(bool screenLeft) {
        // Input button 0/1 dispatch in PlayerInputHandler, preserving flip.
        OnOriginalMoveTap(flipped?!screenLeft:screenLeft);
    }
    static Vector2 SanitizeNub(Vector2 v) {
        if(!float.IsFinite(v.x)||!float.IsFinite(v.y))return Vector2.zero;
        return Vector2.ClampMagnitude(v,155);
    }
    void ProcessOriginalTap(bool left) {
        // clu 0x3435e18: last double times, not a sliding list of four taps.
        if(left){if(motionTick-lastLeftTap<=doubleWindowTicks)lastLeftDouble=motionTick;lastLeftTap=motionTick;}
        else{if(motionTick-lastRightTap<=doubleWindowTicks)lastRightDouble=motionTick;lastRightTap=motionTick;}
        if(motionTick-lastLeftDouble<=kickWindowTicks&&motionTick-lastRightDouble<=kickWindowTicks&&Math.Abs(lastLeftDouble-lastRightDouble)<=kickWindowTicks) {
            lastLeftTap=lastRightTap=lastLeftDouble=lastRightDouble=-1000;
            StartOriginalKick();return;
        }
        if(pushingCollider!=null) {
            if(!left&&Mathf.Max(0,lastRightDouble)==motionTick){pushingCollider.enabled=true;pushUntilTick=motionTick+Ticks(.5f);}
            else pushingCollider.enabled=false;
        }
        // clv 0x3436134: same-side active flag is forced off briefly.
        if(left){leftActive=false;forceLeftUntilTick=motionTick+Ticks(Number("handsForcedInactiveSeconds"));}
        else{rightActive=false;forceRightUntilTick=motionTick+Ticks(Number("handsForcedInactiveSeconds"));}
        if(Mathf.Max(0,left?lastLeftDouble:lastRightDouble)==motionTick)KeepOriginalMove(left?-1:1);
    }
    public bool StartOriginalKick() {
        // StartKick 0x3436098 deliberately has no up-front stamina>=10 gate.
        if(leftLegDisabled||rightLegDisabled||passedOut||dead||OriginalKicking)return false;
        OriginalKicking=true;kickPaid=false;kickStartTick=motionTick;kickUntilTick=motionTick+Ticks(Number("kickDurationSeconds"));
        initialPelvisPoint=pelvisPoint.localPosition;
        feet.SetPositionAndRotation(feet.position,Quaternion.identity);
        if(sourceKickAnimation!=null){sourceKickAnimation.enabled=true;sourceKickAnimation.Rewind();sourceKickAnimation.Stop();sourceKickAnimation.Play();}
        return true;
    }
    void StopOriginalKick() {
        OriginalKicking=false;kickPaid=false;kickUntilTick=0;sourceKickEffector.SetActive(false);
        if(sourceKickAnimation!=null){sourceKickAnimation.Rewind();sourceKickAnimation.Stop();sourceKickAnimation.enabled=false;}
        pelvisPoint.localPosition=initialPelvisPoint;
        leftFootPoint.localPosition=initialLeftFootPoint;rightFootPoint.localPosition=initialRightFootPoint;
    }
    void StepOriginalMotionNetwork() {
        // PlayerInputHandler samples button 0 before button 1. With an
        // unflipped actor these dispatch GoRight then GoLeft, respectively.
        if(human&&!flipped){ProcessPendingOriginalTap(false);ProcessPendingOriginalTap(true);}
        else{ProcessPendingOriginalTap(true);ProcessPendingOriginalTap(false);}
        if(dead){if(OriginalKicking)StopOriginalKick();return;}
        if(pushUntilTick>0&&motionTick>=pushUntilTick){pushUntilTick=0;if(pushingCollider!=null)pushingCollider.enabled=false;}
        // cll checks these timers for enemy/gamepad only. Touch input keeps its
        // own native active-flag update order rather than inheriting that gate.
        if(!human||originalGamepadControls) {
            if(motionTick<forceLeftUntilTick){leftActive=false;if(rightInput.magnitude>10)rightInput=Vector2.zero;}
            if(motionTick<forceRightUntilTick){rightActive=false;if(leftInput.magnitude>10)leftInput=Vector2.zero;}
        }
        if(!OriginalKicking&&!passedOut&&!OriginalBeingKicked&&motionTick<moveUntilTick) {
            float force=leftLegDisabled||rightLegDisabled?Number("movementSpeedWhenDowned"):Number("movementSpeed");
            rootBody.AddForce(new Vector2(force*moveDirection,0));
        }
        if(OriginalKicking) {
            rootBody.linearVelocity=Vector2.zero;
            if(!kickPaid&&motionTick-kickStartTick>=Ticks(Number("kickWindupSeconds"))) {
                sourceKickEffector.SetActive(true);ConsumeOriginalStamina(10);kickPaid=true;
            }
            if(motionTick>=kickUntilTick)StopOriginalKick();
        }
    }
    void ProcessPendingOriginalTap(bool left) {
        if(left){if(!pendingLeftTap)return;pendingLeftTap=false;}
        else{if(!pendingRightTap)return;pendingRightTap=false;}
        ProcessOriginalTap(left);
    }
    internal void ReceiveOriginalKick(J receiver) {
        // KickReceiver.OnTriggerEnter2D 0x3512380 and ct.MoveNext 0x3512670.
        var baseBody=Resolve<Rigidbody2D>(receiver["baseRigidbody2D"]);
        var secondary=Resolve<Rigidbody2D>(receiver["pelvisRigidbody2D"]);
        float oldVy=baseBody.linearVelocity.y;
        var force=new Vector2((float)receiver["kickForce"].N*(receiver["flipped"].B?1:-1),oldVy);
        baseBody.linearVelocity=Vector2.zero;secondary.linearVelocity=Vector2.zero;
        baseBody.AddForce(force);
        if(passedOut)secondary.AddForce(force*20);
        ApplyOriginalGlobalDamage(1);
        beingKickedUntil=Time.time+(float)receiver["timeUntilDampingStart"].N+(float)receiver["timeUntilDampingReset"].N;
        // ImpactSFX.Play_Kick 0x3412650 publishes sound25 at volume1.
        OriginalEffects.PublishSound(this,25,1);
    }
}

public sealed class OriginalKickReceiver : MonoBehaviour {
    OriginalRig owner;
    J data;
    float nextAccept;
    public void Initialize(OriginalRig rig,J source){owner=rig;data=source;}
    void OnTriggerEnter2D(Collider2D other) {
        if(owner==null||owner.opponent==null||Time.time<nextAccept)return;
        var effector=owner.opponent.OriginalKickEffector;
        if(effector==null||!other.gameObject.CompareTag(effector.tag))return;
        nextAccept=Time.time+.5f;owner.ReceiveOriginalKick(data);
    }
}
