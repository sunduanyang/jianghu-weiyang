using System;
using System.Collections.Generic;
using Jianghu;
using UnityEngine;

// SourcePrefab_HandItem 0x363d318..0x363fbc8. The exported prefab supplies
// the original shapes, material assignments, links, anchors and renderers.
public sealed class OriginalItem : MonoBehaviour {
    public OriginalRig owner;
    public int category,itemId,equippedHand;
    public bool twoHanded,isShield,blunt,dropped,isImpaling,flailHead;
    public bool OriginalInArena {get;private set;}
    public bool OriginalPlayerSide {get;private set;}
    // Equipment Type chooses the source mount/COM branch. HandItemType
    // (category) independently drives physical shield contact/mass behavior.
    // The real None prefab is Type7, category0, Name="Nothing".
    public int OriginalEquipmentType=>config==null?0:config["Type"].I;
    public bool OriginalUsesShieldMount=>OriginalEquipmentType==7;
    public float length,damage,defense,weight;
    public Rigidbody2D itemBody,counterweightBody;
    public Transform handle1,handle2;
    public FixedJoint2D impaleJoint;
    public J config;
    J bindings;
    Transform referenceRoot;
    readonly Dictionary<string,Transform> preservedReferences=new Dictionary<string,Transform>();
    public event Action<float> ShieldWasHit,WeaponHitShield;
    static readonly Dictionary<Collider2D,OriginalItem> colliderOwners=new Dictionary<Collider2D,OriginalItem>();
    Collider2D[] ownColliders;
    Transform[] flailHeadsForCleanup;
    bool initialized;
    AudioSource whoosh;
    Transform whooshPoint;
    Vector2 previousWhoosh;
    float dropLayerAt=-1;
    public static OriginalItem From(Collider2D collider) {
        if(collider==null)return null;
        if(colliderOwners.TryGetValue(collider,out var item)&&item!=null)return item;
        return collider.GetComponentInParent<OriginalItem>();
    }
    public void PreserveReference(J reference,Transform target){if(reference["id"].Has&&reference["id"].S!="0")preservedReferences[reference["file"].S+":"+reference["id"].S]=target;}
    public Transform RefTransform(J r) {
        if(preservedReferences.TryGetValue(r["file"].S+":"+r["id"].S,out var cached))return cached;
        return r["path"].Has?(r["path"].S.Length==0?referenceRoot:referenceRoot.Find(r["path"].S)):null;
    }
    public T Ref<T>(J r) where T:Component {
        var t=RefTransform(r);if(t==null)return null;
        var binding=bindings==null?new J():bindings[r["id"].S];
        if(binding.Has) {
            var type=Type.GetType("UnityEngine."+binding["kind"].S+", UnityEngine.Physics2DModule");
            if(type!=null){var components=t.GetComponents(type);int index=binding["index"].I;if(index<components.Length)return components[index] as T;}
        }
        return t.GetComponent<T>();
    }
    public void Initialize(OriginalRig rig,J record,int hand,Transform sourceRoot=null,bool inArena=true) {
        owner=rig;config=record["fields"];bindings=record["component_bindings"];equippedHand=hand;referenceRoot=sourceRoot==null?transform:sourceRoot;
        OriginalInArena=inArena;
        OriginalPlayerSide=rig!=null?rig.human:config["PlayerOrEnemy"].B;
        category=record["category"].I;itemId=record["item_id"].I;twoHanded=record["two_handed"].B;
        blunt=record["blunt"].B;isShield=category==8;flailHead=config["IsFlailHead"].B;
        length=(float)config["Length"].N;
        damage=(float)record["stats"]["obs_Damage"].N;defense=(float)record["stats"]["obs_Defense"].N;weight=(float)record["stats"]["obs_Weight"].N;
        itemBody=Ref<Rigidbody2D>(config["ItemBaseRigidbody2D"]);counterweightBody=Ref<Rigidbody2D>(config["CounterweightRigidbody2D"]);
        handle1=RefTransform(config["Handle_1"]);handle2=RefTransform(config["Handle_2"]);
        impaleJoint=Ref<FixedJoint2D>(config["fixedJoint2DForImpaling"]);
        if(impaleJoint!=null)impaleJoint.enabled=false;
        whoosh=flailHead?null:Ref<AudioSource>(config["whooshAudioSource"]);
        whooshPoint=config["FlailHeads"].A.Count>0?(RefTransform(config["FlailHeads"][0])??transform):counterweightBody==null?transform:counterweightBody.transform;
        if(whoosh!=null){float[] pitches={1.5f,1.25f,1,.75f,.75f,.75f,1.5f,1.5f,.65f};whoosh.pitch=pitches[Mathf.Clamp(category,0,8)]*(twoHanded?.65f:1);}
        ownColliders=GetComponentsInChildren<Collider2D>(true);
        var heads=new List<Transform>();
        foreach(var r in config["FlailHeads"].A){var head=RefTransform(r);if(head!=null)heads.Add(head);}
        flailHeadsForCleanup=heads.ToArray();initialized=true;
        if(isActiveAndEnabled)ApplyOriginalEnableState();
        // cqf sets the item root/head layer; chain children use the original
        // DummyChainLayerChar{1,2} layer rather than the root layer.
        int itemLayer=OriginalPlayerSide?15:16;
        gameObject.layer=itemLayer;
        foreach(var head in config["FlailHeads"].A) {
            var t=RefTransform(head);if(t==null)continue;t.gameObject.layer=itemLayer;
            var body=t.GetComponent<Rigidbody2D>();if(body!=null)body.mass=3;
        }
        var dummy=RefTransform(config[OriginalPlayerSide?"DummyChainLayerChar1":"DummyChainLayerChar2"]);
        foreach(var link in config["FlailLinks"].A) {
            var t=RefTransform(link);if(t==null)continue;
            foreach(var child in t.GetComponentsInChildren<Transform>(true))child.gameObject.layer=dummy==null?itemLayer:dummy.gameObject.layer;
        }
        foreach(var body in GetComponentsInChildren<Rigidbody2D>(true)) {
            var relay=body.GetComponent<OriginalItemCollisionRelay>();if(relay==null)relay=body.gameObject.AddComponent<OriginalItemCollisionRelay>();relay.item=this;
        }
        foreach(var headRecord in record["flail_heads"].A) {
            var node=RefTransform(headRecord["fields"]["m_GameObject"]);if(node==null)continue;
            node.gameObject.AddComponent<OriginalItem>().Initialize(rig,headRecord,hand,referenceRoot,inArena);
        }
        if(record["cannon"].Has)gameObject.AddComponent<OriginalHandCannon>().Initialize(this,record["cannon"]);
        gameObject.AddComponent<OriginalWeaponAppearance>().Initialize(this);
    }
    // OnEnable 0x363dd8c always applies cqe's category masses. Its floating,
    // heavily damped menu setup is guarded by !isBatchMode && !InArena;
    // arena items retain the gravity/damping serialized in their prefabs.
    void ApplyOriginalEnableState() {
        var masses=OriginalCombatRules.RuntimeMass(category,twoHanded,flailHead);
        if(itemBody!=null)itemBody.mass=masses.x;
        if(counterweightBody!=null)counterweightBody.mass=masses.y;
        foreach(var c in ownColliders)if(c!=null)colliderOwners[c]=this;
        if(flailHead)damage/=1.5f;
        if(!Application.isBatchMode&&!OriginalInArena&&!flailHead) {
            if(itemBody!=null){itemBody.gravityScale=-1;itemBody.linearDamping=50;itemBody.angularDamping=100;}
            if(counterweightBody!=null)counterweightBody.gravityScale=-5;
        }
    }
    void OnEnable(){if(initialized)ApplyOriginalEnableState();}
    void OnDisable(){UnregisterOriginalColliders();}
    void UnregisterOriginalColliders(){if(ownColliders!=null)foreach(var c in ownColliders)if(c!=null)colliderOwners.Remove(c);}
    // cqj/cqk 0x363f8cc..0x363f9b8 are cleanup callbacks, not physics resets.
    // The local host forwards these original state/transition events; newly
    // respawned parented equipment survives a new countdown as in the source.
    public void OnOriginalStateChanged(int previous,int next) {
        if(previous==5||previous==6||next==3)OnOriginalTransitionSwitching();
    }
    public void OnOriginalTransitionSwitching() {
        if(!initialized||!isActiveAndEnabled||(!dropped&&transform.parent!=null))return;
        if(flailHeadsForCleanup!=null)foreach(var head in flailHeadsForCleanup)if(head!=null)Destroy(head.gameObject);
        Destroy(gameObject);
    }
    public void OnOriginalCollision(Collision2D collision) {
        if(collision.contactCount==0)return;
        var other=From(collision.collider);if(other==null||other==this||other.owner==owner)return;
        if(collision.collider.gameObject.layer!=(OriginalPlayerSide?16:15))return;
        float speed=collision.GetContact(0).relativeVelocity.magnitude;
        if(speed<(float)config["impactSpeedThreshold"].N)return;
        if(isShield) {
            ShieldWasHit?.Invoke(Mathf.Min(2.5f,collision.relativeVelocity.magnitude*other.damage/50));
            if(speed>.5f&&itemBody!=null) {var body=other.GetComponent<Rigidbody2D>();if(body!=null)itemBody.linearVelocity=body.linearVelocity;}
        }
        if(other.isShield)WeaponHitShield?.Invoke(Mathf.Min(2.5f,collision.relativeVelocity.magnitude*other.damage/50));
    }
    public void EndImpalement(){if(impaleJoint!=null)impaleJoint.enabled=false;isImpaling=false;}
    public void SetDropped() {
        // SetDropped 0x363e3d4 has no already-dropped guard. Both hand drops
        // still apply physics; repeated native Invoke calls do not postpone
        // the earliest already-scheduled non-collidable layer swap.
        dropped=true;
        transform.SetParent(null,true);
        // SetDropped 0x363e3d4: counterweight disabled, visible item returns
        // to gravity 1/mass 1/damping .05; noncollidable layer after 2s.
        if(counterweightBody!=null){counterweightBody.gravityScale=1;counterweightBody.gameObject.SetActive(false);}
        if(itemBody!=null){itemBody.gravityScale=1;itemBody.mass=1;itemBody.linearDamping=.05f;itemBody.angularDamping=.05f;}
        dropLayerAt=dropLayerAt<0?Time.time+2:Mathf.Min(dropLayerAt,Time.time+2);
    }
    void FixedUpdate() {
        if(whoosh!=null&&whooshPoint!=null) {
            Vector2 position=whooshPoint.position;
            float speed=(previousWhoosh-position).magnitude/Time.fixedDeltaTime;previousWhoosh=position;
            float volume=Mathf.InverseLerp((float)config["minSpeedForWhoosh"].N,(float)config["speedAtFullVolume"].N,speed*2)*100;
            whoosh.volume=Mathf.MoveTowards(whoosh.volume,volume,(float)config["volumeChangePerSecond"].N*Time.fixedDeltaTime);
        }
        if(dropLayerAt>=0&&Time.time>=dropLayerAt) {
            dropLayerAt=-1;
            // SwapToNonCollidableLayer 0x363e4e4.
            foreach(var t in GetComponentsInChildren<Transform>(true))t.gameObject.layer=17;
        }
    }
    void OnDestroy(){UnregisterOriginalColliders();}
}

public sealed class OriginalItemCollisionRelay : MonoBehaviour {
    public OriginalItem item;
    void OnCollisionEnter2D(Collision2D c){if(item!=null)item.OnOriginalCollision(c);}
}
