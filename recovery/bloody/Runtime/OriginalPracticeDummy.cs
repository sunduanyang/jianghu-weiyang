using System;
using System.Collections.Generic;
using System.Linq;
using Jianghu;
using UnityEngine;

public struct OriginalPracticeImpact {
    public OriginalPracticeDummyBodyPart Part;
    public OriginalItem Item;
    public Vector2 Point,Velocity,Normal;
    public bool Cannon,DustEmitted;
    public int SoundIndex;
    public float SoundVolume,FeedbackForce,CameraImpulse,HapticA,HapticB,HapticC;
}

public struct OriginalPracticeContact {
    public OriginalPracticeDummyBodyPart BodyPart;
    public OriginalItem FixedItem,OtherItem;
    public OriginalRig OtherRig;
    public Collider2D OtherCollider;
    public Vector2 Point,Velocity;
}

// AIOpponentSpawner.cbg 0x341024c selects the separate Dummy prefab in
// Practice2. This actor has no CharacterVitality/CharacterEnemyAI/OriginalRig.
public sealed class OriginalPracticeDummy : MonoBehaviour {
    public const string SourceGuid="a87a17d047355e740a4963b8664f84ac";
    readonly Dictionary<string,Transform> paths=new Dictionary<string,Transform>();
    readonly List<OriginalPracticeDummyBodyPart> parts=new List<OriginalPracticeDummyBodyPart>();
    readonly List<OriginalItem> items=new List<OriginalItem>();
    J source,soundSettings;
    AudioSource impactAudio;
    OriginalVisualCatalog catalog;
    OriginalEffects effects;
    Transform shadow,shadowTarget;
    public bool Initialized {get;private set;}
    public bool SpookyImplemented=>false;
    public Transform AimTarget {get;private set;}
    public Rigidbody2D Body {get;private set;}
    public IReadOnlyList<OriginalPracticeDummyBodyPart> BodyParts=>parts;
    public IReadOnlyList<OriginalItem> FixedItems=>items;
    public event Action<OriginalPracticeImpact> ImpactOccurred;
    // Diagnostic contact observation does not drive damage or presentation.
    public event Action<OriginalPracticeContact> ContactOccurred;
    public event Action<float,float,float> HapticRequested;
    public event Action<float> CameraImpulseRequested;

    public static OriginalPracticeDummy Create(GameObject prefab,Vector3 position) {
        if(prefab==null)throw new ArgumentNullException(nameof(prefab));
        var go=Instantiate(prefab,position,prefab.transform.rotation);go.SetActive(true);
        var dummy=go.AddComponent<OriginalPracticeDummy>();dummy.Initialize();return dummy;
    }
    public void Initialize() {
        if(Initialized)return;
        var text=Resources.Load<TextAsset>("OriginalPracticeSource");
        if(text==null)throw new InvalidOperationException("OriginalPracticeSource is missing");
        source=J.Parse(text.text);catalog=Resources.Load<OriginalVisualCatalog>("OriginalVisualCatalog");
        if(catalog==null)throw new InvalidOperationException("Original practice visual/audio catalog is missing");
        void Walk(Transform node,string path){paths[path]=node;for(int i=0;i<node.childCount;i++){var child=node.GetChild(i);Walk(child,path.Length==0?child.name:path+"/"+child.name);}}
        Walk(transform,"");effects=FindFirstObjectByType<OriginalEffects>();
        var sound=source["controllers"].A.First(row=>row["script"].S=="ImpactSFX");
        soundSettings=sound["sounds"];impactAudio=Resolve<AudioSource>(sound["data"]["audioSource"]);
        if(impactAudio==null)throw new InvalidOperationException("Original dummy ImpactSFX AudioSource missing");
        foreach(var row in source["items"].A) {
            var node=paths[row["path"].S];var item=node.gameObject.AddComponent<OriginalItem>();
            // DummyInitializer.Start sets its ImpactSFX, without equipping on a
            // character or installing a CharacterImpactFeed. Its fixed source
            // hierarchy supplies every joint, collider, anchor and material.
            item.Initialize(null,row["record"],row["record"]["fields"]["equippedInHand"].I,node,true);items.Add(item);
        }
        foreach(var row in source["controllers"].A.Where(row=>row["script"].S=="DummyBodyPart")) {
            var part=paths[row["path"].S].gameObject.AddComponent<OriginalPracticeDummyBodyPart>();
            part.Initialize(this,row,source["constants"]);parts.Add(part);
        }
        if(parts.Count!=5)throw new InvalidOperationException("Source dummy requires five body parts");
        AimTarget=paths["Dummy_Body"];Body=AimTarget.GetComponent<Rigidbody2D>();
        var shadowRow=source["controllers"].A.FirstOrDefault(row=>row["script"].S=="ShadowMover");
        if(shadowRow!=null){shadow=paths[shadowRow["path"].S];shadowTarget=paths[shadowRow["data"]["targetTransform"]["path"].S];}
        foreach(var body in GetComponentsInChildren<Rigidbody2D>(true)) {
            var relay=body.gameObject.AddComponent<OriginalPracticeContactRelay>();relay.Dummy=this;
            relay.FixedItem=body.GetComponentInParent<OriginalItem>();
            relay.BodyPart=relay.FixedItem==null?body.GetComponent<OriginalPracticeDummyBodyPart>():null;
        }
        Initialized=true;
        // DummyInitializer.Start 0x351796c returns in the ordinary seasonal
        // context. SpookyStab is a distinct source coroutine and is not enabled
        // by this normal-Practice implementation.
    }
    internal T Resolve<T>(J reference)where T:Component {
        if(reference["id"].S=="0"||!reference["path"].Has)return null;
        var node=paths[reference["path"].S];
        return node.GetComponents<T>().ElementAtOrDefault(reference["index"].I);
    }
    internal void Dust(Vector2 point){if(effects!=null)effects.DoVfx(4,point,1);}
    static string SoundField(int index) {
        switch(index){case 8:return "metalSharpHitFlesh";case 11:return "metalBashFlesh_Small";case 12:return "woodBashFlesh_Big";case 13:return "woodBashFlesh_Small";default:throw new ArgumentOutOfRangeException(nameof(index));}
    }
    internal void PlaySound(int index,float volume) {
        var audio=soundSettings[SoundField(index)];
        if(!audio.Has)throw new InvalidOperationException("Source dummy AudioObject missing for sound "+index);
        var data=audio["data"];impactAudio.pitch=UnityEngine.Random.Range((float)data["pitch"]["min"].N,(float)data["pitch"]["max"].N);
        int count=data["audioClip"].A.Count;int selected=UnityEngine.Random.Range(0,count);
        var clip=catalog.Get<AudioClip>(audio["assets"]["audioClip["+selected+"]"].S);
        if(clip==null)throw new InvalidOperationException("Source dummy AudioClip missing");
        impactAudio.PlayOneShot(clip,volume/5+(float)data["volume"]["max"].N);
    }
    internal void Publish(OriginalPracticeImpact impact) {
        HapticRequested?.Invoke(impact.HapticA,impact.HapticB,impact.HapticC);
        CameraImpulseRequested?.Invoke(impact.CameraImpulse);ImpactOccurred?.Invoke(impact);
    }
    internal void PublishContact(OriginalPracticeDummyBodyPart part,OriginalItem item,Collision2D collision) {
        if(ContactOccurred==null||collision.contactCount==0)return;
        var contact=collision.GetContact(0);var other=OriginalItem.From(collision.collider);
        ContactOccurred.Invoke(new OriginalPracticeContact {BodyPart=part,FixedItem=item,OtherItem=other,
            OtherRig=other!=null?other.owner:collision.collider.GetComponentInParent<OriginalRig>(),OtherCollider=collision.collider,Point=contact.point,Velocity=contact.relativeVelocity});
    }
    // ShadowMover.FixedUpdate 0x3516238 retains its y/z, follows target x,
    // and supplies Quaternion.identity to SetPositionAndRotation.
    void FixedUpdate(){if(shadow!=null&&shadowTarget!=null)shadow.SetPositionAndRotation(new Vector3(shadowTarget.position.x,shadow.position.y,shadow.position.z),Quaternion.identity);}
    void OnDestroy(){foreach(var item in items)if(item!=null&&!item.transform.IsChildOf(transform))Destroy(item.gameObject);}
}

public sealed class OriginalPracticeContactRelay : MonoBehaviour {
    internal OriginalPracticeDummy Dummy;
    internal OriginalPracticeDummyBodyPart BodyPart;
    internal OriginalItem FixedItem;
    void OnCollisionEnter2D(Collision2D collision){if(Dummy!=null)Dummy.PublishContact(BodyPart,FixedItem,collision);}
}

// DummyBodyPart.OnCollisionEnter2D/ProcessImpact/dtw/dtv 0x36c5c90..0x36c6628.
// FeedbackForce is a presentation value. No HP, severing or death is created.
public sealed class OriginalPracticeDummyBodyPart : MonoBehaviour {
    OriginalPracticeDummy dummy;
    J config,constants;
    float impactUntil,dustUntil;
    public Rigidbody2D Body {get;private set;}
    public string SourcePath {get;private set;}
    public OriginalPracticeImpact LastImpact {get;private set;}
    public int ImpactCount {get;private set;}
    public float ImpactCooldownUntil=>impactUntil;
    public float DustCooldownUntil=>dustUntil;
    float N(string key)=>(float)config[key].N;
    public void Initialize(OriginalPracticeDummy owner,J row,J sourceConstants) {
        dummy=owner;config=row["data"];constants=sourceConstants;SourcePath=row["path"].S;
        Body=dummy.Resolve<Rigidbody2D>(config["thisRigidbody2D"]);
        if(Body==null)throw new InvalidOperationException("Source dummy body Rigidbody2D missing: "+SourcePath);
    }
    public static int SoundIndex(OriginalSurface surface) {
        switch(surface) {
            case OriginalSurface.MetalBlade:case OriginalSurface.MetalSpike:case OriginalSurface.MetalBlunt:return 8;
            case OriginalSurface.WoodBlunt:return 12;
            case OriginalSurface.WoodHandle:return 13;
            case OriginalSurface.MetalHandle:return 11;
            default:return -1;
        }
    }
    public static bool Qualifies(float now,float cooldown,float speed,float threshold,OriginalSurface surface) {
        if(now<cooldown)return false;
        if(OriginalCombatRules.Handle(surface))threshold*=.5f;
        return speed>=threshold;
    }
    public static float FeedbackForce(float damagePerMps,float speed,float itemDamage,bool shield,OriginalSurface surface) {
        float force=damagePerMps*speed*(itemDamage/6+1);
        if(shield)force*=.5f;
        if(surface==OriginalSurface.WoodHandle||surface==OriginalSurface.MetalHandle)force/=10;
        return Mathf.Clamp(force,1,100);
    }
    void OnCollisionEnter2D(Collision2D collision) {
        if(dummy==null||collision.contactCount==0||!collision.collider.CompareTag("HandItem"))return;
        var item=OriginalItem.From(collision.collider);if(item==null)return;
        var contact=collision.GetContact(0);var surface=OriginalCombatRules.Surface(collision.collider.sharedMaterial);
        if(Qualifies(Time.time,impactUntil,contact.relativeVelocity.magnitude,N("impactSpeedThreshold"),surface))ProcessImpact(item,contact.point,contact.relativeVelocity,contact.normal,surface);
    }
    public bool ProcessImpact(OriginalItem item,Vector2 point,Vector2 velocity,Vector2 normal,OriginalSurface surface,bool cannon=false) {
        if(dummy==null||item==null)return false;
        float speed=velocity.magnitude;
        var impact=new OriginalPracticeImpact {Part=this,Item=item,Point=point,Velocity=velocity,Normal=normal,Cannon=cannon,
            FeedbackForce=FeedbackForce(N("damagePerMps"),speed,item.damage,item.isShield,surface),SoundIndex=SoundIndex(surface),SoundVolume=speed*N("sfxVolumePerMps")+.25f};
        if(Time.time>=dustUntil){dummy.Dust(point);dustUntil=Time.time+.5f;impact.DustEmitted=true;}
        if(impact.SoundIndex>=0)dummy.PlaySound(impact.SoundIndex,impact.SoundVolume);
        if(cannon) {
            for(int i=0;i<config["cannonballDustBursts"].I;i++)dummy.Dust(point+UnityEngine.Random.insideUnitCircle*(float)constants["cannon_dust_radius"].N);
            Vector2 direction=velocity.sqrMagnitude>(float)constants["direction_epsilon"].N?velocity.normalized:-normal;
            Body.linearVelocity+=direction*N("cannonballKnockback");Body.angularVelocity+=UnityEngine.Random.Range(-1f,1f)*N("cannonballSpin");
            dummy.PlaySound(8,1);impact.HapticA=.5f;impact.HapticB=impact.HapticC=1;impact.CameraImpulse=N("cannonballShakeForce");
        } else {
            float feedback=impact.FeedbackForce/40+(float)constants["feedback_base"].N;
            impact.HapticA=.5f;impact.HapticB=feedback+.75f;impact.HapticC=feedback;
            impactUntil=Time.time+.25f;impact.CameraImpulse=impact.FeedbackForce/500+(float)constants["shake_base"].N;
        }
        LastImpact=impact;ImpactCount++;dummy.Publish(impact);return true;
    }
}
