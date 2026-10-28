using System;
using System.Collections.Generic;
using System.Linq;
using Jianghu;
using UnityEngine;

// Restores the original physical hierarchy. This is an engineering preview,
// not a completed port. Native addresses below identify reviewed translations.
public sealed partial class OriginalRig : MonoBehaviour {
    public const string PlayerSource="00af04d95140a4b44ac8453d91c032ea";
    public const string EnemySource="c0b154b0ebaf72f4fa139b4e3cf363e9";
    public bool human;
    public OriginalRig opponent;
    public float health=100,stamina=100;
    public float movement;
    public Vector2 leftInput,rightInput;
    public bool leftActive,rightActive;
    public Rigidbody2D rootBody,headCOM,chest,pelvisCOM,leftHandCOM,rightHandCOM;
    Rigidbody2D leftHand,rightHand,leftFootCOM,rightFootCOM;
    Transform feet,leftPoint,rightPoint,leftParent,rightParent,pelvisPoint,downedPoint,leftFootPoint,rightFootPoint;
    Collider2D pushingCollider;
    AreaEffector2D pushingEffector;
    J mover;
    bool flipped;
    float lean=1;
    readonly Dictionary<string,Transform> paths=new Dictionary<string,Transform>();
    public J source;
    public T Resolve<T>(J reference) where T:Component {
        if(reference["id"].S=="0"||!reference["path"].Has)return null;
        var target=paths[reference["path"].S];
        var binding=source["component_bindings"][reference["id"].S];
        if(binding.Has) {
            var components=target.GetComponents<Component>().Where(c=>c!=null&&c.GetType().Name==binding["kind"].S).ToArray();
            int index=binding["index"].I;
            if(index<components.Length&&components[index] is T exact)return exact;
        }
        return target.GetComponent<T>();
    }
    public Transform ResolveTransform(J reference) {
        if(reference["id"].S=="0"||!reference["path"].Has)return null;
        return paths[reference["path"].S];
    }
    public float Number(string key)=>(float)mover[key].N;
    public void Initialize(bool player) {
        human=player;
        void Walk(Transform t,string p) {paths[p]=t;for(int i=0;i<t.childCount;i++){var c=t.GetChild(i);Walk(c,p.Length==0?c.name:p+"/"+c.name);}}
        Walk(transform,"");
        source=J.Parse(Resources.Load<TextAsset>("OriginalRigReferences").text)[player?PlayerSource:EnemySource];
        mover=source["controllers"]["CharacterMover"][0]["data"];
        flipped=mover["IsFlipped"].B;
        rootBody=Resolve<Rigidbody2D>(mover["baseRigidbody2D"]);
        headCOM=Resolve<Rigidbody2D>(mover["headCOMRigidbody2D"]);
        chest=Resolve<Rigidbody2D>(mover["chestRigidbody2D"]);
        pelvisCOM=Resolve<Rigidbody2D>(mover["pelvisCOMRigidbody2D"]);
        leftHandCOM=Resolve<Rigidbody2D>(mover["handLeftCOMRigidbody2D"]);
        rightHandCOM=Resolve<Rigidbody2D>(mover["handRightCOMRigidbody2D"]);
        leftHand=Resolve<Rigidbody2D>(mover["handLeftRigidbody2D"]);
        rightHand=Resolve<Rigidbody2D>(mover["handRightRigidbody2D"]);
        leftFootCOM=Resolve<Rigidbody2D>(mover["footLeftCOMRigidbody2D"]);
        rightFootCOM=Resolve<Rigidbody2D>(mover["footRightCOMRigidbody2D"]);
        feet=ResolveTransform(mover["feetTransform"]);
        leftPoint=ResolveTransform(mover["handLeftPoint"]);rightPoint=ResolveTransform(mover["handRightPoint"]);
        leftParent=ResolveTransform(mover["handLeftPointParent"]);rightParent=ResolveTransform(mover["handRightPointParent"]);
        pelvisPoint=ResolveTransform(mover["pelvisPoint"]);downedPoint=ResolveTransform(mover["pelvisWhenAnyLegSeveredPoint"]);
        leftFootPoint=ResolveTransform(mover["footLeftPoint"]);rightFootPoint=ResolveTransform(mover["footRightPoint"]);
        pushingCollider=Resolve<Collider2D>(mover["pushingCollider"]);
        pushingEffector=Resolve<AreaEffector2D>(mover["pushingAreaEffector"]);
        // KienematicDynamicEnabler is a separate original behaviour. The
        // prefab already contains dynamic bodies; retained source flags apply.
        InitializeCombat();
        InitializeAppearance();
        InitializeMotion();
        InitializeAI();
    }
    partial void InitializeAppearance();
    partial void InitializeCombat();
    partial void StepCombat(float dt);
    partial void InitializeAI();
    partial void UpdateAI();
    partial void FixedAI();
    void Update(){if(mover!=null)UpdateAI();}
    void FixedUpdate() {
        if(mover==null)return;
        motionTick++;
        StepCombat(Time.fixedDeltaTime);
        FixedAI();
        StepOriginalMotionNetwork();
        if(dead)return;
        // CharacterMover.clq 0x34353f0: arena-edge push magnitude.
        if(passedOut){if(pushingCollider!=null)pushingCollider.enabled=false;pushDisabledForVitality=true;}
        else if(pushDisabledForVitality){if(pushingCollider!=null)pushingCollider.enabled=true;pushDisabledForVitality=false;}
        if(pushingEffector!=null)pushingEffector.forceMagnitude=Mathf.Clamp(rootBody.position.x*(flipped?5:-5),5,50);
        // CharacterMover.clr 0x34354d8: clamp the full vector before integration.
        gaitVelocityX=passedOut?0:Mathf.Abs(rootBody.linearVelocity.x);
        rootBody.linearVelocity=Vector2.ClampMagnitude(rootBody.linearVelocity,leftLegDisabled||rightLegDisabled?1.75f:Number("maxVelocityX"));
        float upward=(health+100)*Number("HeadForceUpwardsMultiplier");
        // CharacterMover.clm 0x34350d4: support depends on actual COM geometry.
        if(!passedOut) {
            if(!headDisabled) {
                lean=Mathf.Abs(headCOM.position.x-pelvisCOM.position.x)*2+1;
                headCOM.AddForceY((lean*250+upward)*3);
                chest.AddForce(new Vector2(rootBody.linearVelocity.x*Number("HeadForceSidewaysMultiplier"),upward)*3);
                pelvisCOM.MovePosition((leftLegDisabled||rightLegDisabled?downedPoint:pelvisPoint).position);
                if(OriginalKicking)pelvisCOM.MoveRotation(flipped?120:-120);
                // cll/cln retain the original cross-named hand wiring and the
                // prior-target MovePosition before the new target is written.
                if(!leftArmDisabled)DriveHand(rightHandCOM,rightPoint,rightParent,leftHand,rightActive,leftInput);
                if(!rightArmDisabled)DriveHand(leftHandCOM,leftPoint,leftParent,rightHand,leftActive,rightInput);
            }
            if(!leftLegDisabled)leftFootCOM.MovePosition(leftFootPoint.position);
            if(!rightLegDisabled)rightFootCOM.MovePosition(rightFootPoint.position);
        }
        if(passedOut||rightArmDisabled)leftHand.linearDamping=leftHand.angularDamping=Number("handsDragWhenActive");
        if(passedOut||leftArmDisabled)rightHand.linearDamping=rightHand.angularDamping=Number("handsDragWhenActive");
        // Feet are driven by the original rotating transform and target points,
        // rather than a newly authored gait on a different skeleton.
        float speed=gaitVelocityX;
        if(OriginalKicking) {
            feet.localScale=Vector3.one;feet.SetPositionAndRotation(feet.position,Quaternion.identity);
        } else if(speed>Number("VelocityXFeetMoverThreshold")) {
            // Native cls reads Transform.rotation.z, not eulerAngles.z. Keep
            // this source quirk; substituting Euler angles changes the gait.
            feet.Rotate(0,0,feet.rotation.z+lean*rootBody.linearVelocity.x*(flipped?4:-4));
            feet.localScale=Vector3.one*((1+speed/Number("feetPositionTransformGrowScaleDivider"))/lean);
        } else {
            feet.localScale=Vector3.one;
            feet.rotation=Quaternion.Euler(0,0,feet.rotation.z>=90?180:0);
        }
        // FixedUpdate/clt preserves the root's current y while following the
        // pelvis COM horizontally during a passed-out state.
        if(passedOut)rootBody.MovePosition(new Vector2(pelvisCOM.position.x,rootBody.position.y));
    }
    void DriveHand(Rigidbody2D driver,Transform target,Transform parent,Rigidbody2D hand,bool active,Vector2 nub) {
        driver.MovePosition(target.position);
        float drag=(hand.linearVelocity.x+hand.linearVelocity.y)*.5f>Number("handsDragThreshold")?Number("handsDragWhenActive"):Number("handsDragWhenPassive");
        hand.linearDamping=hand.angularDamping=drag;
        if(active)target.position=parent.position+new Vector3((nub.x/100+.15f)*(flipped?-1:1),nub.y/100+.25f,0);
        else target.localPosition=Vector3.zero;
    }
}
