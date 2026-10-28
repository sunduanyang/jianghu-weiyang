using System;
using System.Collections;
using Jianghu;
using UnityEngine;

// HandCannon / HandCannonSync: one scheduled shot per original round state 4.
// ResolveShot 0x350ed58, authoritative hit 0x351023c, recoil 0x350f538.
public sealed class OriginalHandCannon : MonoBehaviour {
    public OriginalItem item;
    public bool fired,awaitingShot;
    J config;
    Transform muzzle,target,hinge;
    Rigidbody2D thrower;
    LineRenderer tracer;
    ParticleSystem muzzleBlast,impact;
    float fireAt,hingeAt,hingeDuration,hingeFrom,hingeTo,hideTracerAt=-1;
    readonly RaycastHit2D[] hits=new RaycastHit2D[16];
    float N(string key)=>(float)config[key].N;
    public void Initialize(OriginalItem source,J data) {
        item=source;config=data;
        muzzle=item.RefTransform(config["muzzle"]);target=item.RefTransform(config["projectileTargetPoint"]);hinge=item.RefTransform(config["hinge"]);
        thrower=item.Ref<Rigidbody2D>(config["throwerRigidbody2D"]);tracer=item.Ref<LineRenderer>(config["tracer"]);
        muzzleBlast=item.Ref<ParticleSystem>(config["muzzleBlastVFX"]);impact=item.Ref<ParticleSystem>(config["impactVFX"]);
        if(impact!=null)impact.transform.SetParent(null,true);
        if(tracer!=null){tracer.useWorldSpace=true;tracer.positionCount=2;tracer.enabled=false;}
        if(hinge!=null)hinge.localRotation=Quaternion.Euler(0,0,N("hingeAngleMenu"));
        SetSourceActive("sparksVFX",false);
    }
    public void BeginRound(float fuseDelay) {
        fired=false;awaitingShot=true;fireAt=Time.time+fuseDelay;
        hingeAt=Time.time;hingeDuration=N("hingeTime");hingeTo=N("hingeAngleArena");
        hingeFrom=hinge==null?0:hinge.localEulerAngles.z;
        SetSourceActive("sparksVFX",false);
    }
    void FixedUpdate() {
        if(awaitingShot&&Time.time>=fireAt){awaitingShot=false;Fire();}
    }
    void Update() {
        if(awaitingShot&&hinge!=null) {
            float t=hingeDuration<=0?1:Mathf.Clamp01((Time.time-hingeAt)/hingeDuration);
            // PlayAnticipation uses Ease.OutExpo (17).
            float eased=t==1?1:1-Mathf.Pow(2,-10*t);
            hinge.localRotation=Quaternion.Euler(0,0,Mathf.LerpAngle(hingeFrom,hingeTo,eased));
            if(t==1)SetSourceActive("fuseVFX",true);
        }
        if(hideTracerAt>=0&&Time.time>=hideTracerAt){hideTracerAt=-1;if(tracer!=null)tracer.enabled=false;}
    }
    public void Fire() {
        if(fired||muzzle==null||target==null)return;fired=true;
        Vector2 direction=(Vector2)(target.position-muzzle.position);
        if(direction.sqrMagnitude<=1e-6f)direction=muzzle.right;else direction.Normalize();
        Vector2 origin=(Vector2)muzzle.position+direction*N("muzzleOffset");
        Vector2 end=origin+direction*N("shotRange"),normal=-direction;
        int mask=config[item.owner.OriginalFlipped?"targetMask_ShooterIsCharacter2":"targetMask_ShooterIsCharacter1"]["m_Bits"].I;
        int blockers=config["blockerLayers"]["m_Bits"].I;
        var filter=new ContactFilter2D {useTriggers=false,useLayerMask=true,layerMask=mask};
        int count=Physics2D.CircleCast(origin,N("shotRadius"),direction,filter,hits,N("shotRange"));
        Array.Sort(hits,0,count,OriginalShotComparer.Instance);
        OriginalBodyPart part=null;
        for(int i=0;i<count;i++) {
            var hit=hits[i];if(hit.collider==null)continue;
            if((blockers&(1<<hit.collider.gameObject.layer))!=0){end=hit.point;normal=hit.normal;break;}
            var p=hit.collider.GetComponentInParent<OriginalBodyPart>();
            if(p==null)continue;
            part=p;end=hit.point;normal=hit.normal;break;
        }
        if(thrower!=null)thrower.AddForce(-direction*N("recoilImpulse"),ForceMode2D.Impulse);
        if(hinge!=null)hinge.localRotation=Quaternion.Euler(0,0,N("hingeAngleArena"));
        SetSourceActive("sparksVFX",false);
        if(muzzleBlast!=null)muzzleBlast.Play(true);
        var animation=item.Ref<Animation>(config["fireAnimation"]);if(animation!=null)animation.Play();
        var audio=item.Ref<AudioSource>(config["fireSFX"]);if(audio!=null&&audio.clip!=null)audio.Play();
        if(tracer!=null){tracer.SetPosition(0,origin);tracer.SetPosition(1,end);tracer.enabled=true;hideTracerAt=Time.time+N("tracerTime");}
        if(impact!=null){impact.transform.position=end;impact.Play(true);}
        if(part!=null)part.ProcessImpact(item,end,direction*N("muzzleSpeed"),normal,OriginalSurface.MetalSpike,true);
    }
    void SetSourceActive(string key,bool active){var t=item.RefTransform(config[key]);if(t!=null)t.gameObject.SetActive(active);}
    void OnDestroy(){if(impact!=null)Destroy(impact.gameObject);}
    sealed class OriginalShotComparer : System.Collections.Generic.IComparer<RaycastHit2D> {
        public static readonly OriginalShotComparer Instance=new OriginalShotComparer();
        public int Compare(RaycastHit2D a,RaycastHit2D b)=>a.distance.CompareTo(b.distance);
    }
}
