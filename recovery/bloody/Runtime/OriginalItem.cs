using System;
using System.Collections.Generic;
using Jianghu;
using UnityEngine;

// SourcePrefab_HandItem 0x363d318..0x363fbc8. The exported prefab supplies
// the original shapes, material assignments, links, anchors and renderers.
public sealed class OriginalItem : MonoBehaviour {
    public OriginalRig owner;
    public int category,itemId,equippedHand;
    public bool twoHanded,isShield,blunt,dropped,isImpaling;
    public float length,damage,defense,weight;
    public Rigidbody2D itemBody,counterweightBody;
    public Transform handle1,handle2;
    public FixedJoint2D impaleJoint;
    public J config;
    J bindings;
    public event Action<float> ShieldWasHit,WeaponHitShield;
    static readonly Dictionary<Collider2D,OriginalItem> colliderOwners=new Dictionary<Collider2D,OriginalItem>();
    Collider2D[] ownColliders;
    float dropLayerAt=-1;
    public static OriginalItem From(Collider2D collider) {
        if(collider==null)return null;
        if(colliderOwners.TryGetValue(collider,out var item)&&item!=null)return item;
        return collider.GetComponentInParent<OriginalItem>();
    }
    public Transform RefTransform(J r)=>r["path"].Has?(r["path"].S.Length==0?transform:transform.Find(r["path"].S)):null;
    public T Ref<T>(J r) where T:Component {
        var t=RefTransform(r);if(t==null)return null;
        var binding=bindings==null?new J():bindings[r["id"].S];
        if(binding.Has) {
            var type=Type.GetType("UnityEngine."+binding["kind"].S+", UnityEngine.Physics2DModule");
            if(type!=null){var components=t.GetComponents(type);int index=binding["index"].I;if(index<components.Length)return components[index] as T;}
        }
        return t.GetComponent<T>();
    }
    public void Initialize(OriginalRig rig,J record,int hand) {
        owner=rig;config=record["fields"];bindings=record["component_bindings"];equippedHand=hand;
        category=record["category"].I;itemId=record["item_id"].I;twoHanded=record["two_handed"].B;
        blunt=record["blunt"].B;isShield=category==8;
        length=(float)config["Length"].N;
        damage=(float)record["stats"]["obs_Damage"].N;defense=(float)record["stats"]["obs_Defense"].N;weight=(float)record["stats"]["obs_Weight"].N;
        itemBody=Ref<Rigidbody2D>(config["ItemBaseRigidbody2D"]);counterweightBody=Ref<Rigidbody2D>(config["CounterweightRigidbody2D"]);
        handle1=RefTransform(config["Handle_1"]);handle2=RefTransform(config["Handle_2"]);
        impaleJoint=Ref<FixedJoint2D>(config["fixedJoint2DForImpaling"]);
        if(impaleJoint!=null)impaleJoint.enabled=false;
        var masses=OriginalCombatRules.RuntimeMass(category,twoHanded,config["IsFlailHead"].B);
        if(itemBody!=null) {itemBody.mass=masses.x;itemBody.gravityScale=-1;itemBody.linearDamping=50;itemBody.angularDamping=100;}
        if(counterweightBody!=null) {counterweightBody.mass=masses.y;counterweightBody.gravityScale=-5;}
        ownColliders=GetComponentsInChildren<Collider2D>(true);
        foreach(var c in ownColliders)colliderOwners[c]=this;
        // cqf sets the item root/head layer; chain children use the original
        // DummyChainLayerChar{1,2} layer rather than the root layer.
        int itemLayer=rig.human?15:16;
        gameObject.layer=itemLayer;
        foreach(var head in config["FlailHeads"].A) {
            var t=RefTransform(head);if(t==null)continue;t.gameObject.layer=itemLayer;
            var body=t.GetComponent<Rigidbody2D>();if(body!=null)body.mass=3;
        }
        var dummy=RefTransform(config[rig.human?"DummyChainLayerChar1":"DummyChainLayerChar2"]);
        foreach(var link in config["FlailLinks"].A) {
            var t=RefTransform(link);if(t==null)continue;
            foreach(var child in t.GetComponentsInChildren<Transform>(true))child.gameObject.layer=dummy==null?itemLayer:dummy.gameObject.layer;
        }
        foreach(var body in GetComponentsInChildren<Rigidbody2D>(true)) {
            if(body.GetComponent<OriginalItemCollisionRelay>()==null)body.gameObject.AddComponent<OriginalItemCollisionRelay>().item=this;
        }
        if(record["cannon"].Has)gameObject.AddComponent<OriginalHandCannon>().Initialize(this,record["cannon"]);
    }
    public void OnOriginalCollision(Collision2D collision) {
        if(collision.contactCount==0)return;
        var other=From(collision.collider);if(other==null||other==this||other.owner==owner)return;
        if(collision.collider.gameObject.layer!=(owner.human?16:15))return;
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
        if(dropped)return;dropped=true;
        transform.SetParent(null,true);
        // SetDropped 0x363e3d4: counterweight disabled, visible item returns
        // to gravity 1/mass 1/damping .05; noncollidable layer after 2s.
        if(counterweightBody!=null){counterweightBody.gravityScale=1;counterweightBody.gameObject.SetActive(false);}
        if(itemBody!=null){itemBody.gravityScale=1;itemBody.mass=1;itemBody.linearDamping=.05f;itemBody.angularDamping=.05f;}
        dropLayerAt=Time.time+2;
    }
    void FixedUpdate() {
        if(dropLayerAt>=0&&Time.time>=dropLayerAt) {
            dropLayerAt=-1;
            // SwapToNonCollidableLayer 0x363e4e4.
            foreach(var t in GetComponentsInChildren<Transform>(true))t.gameObject.layer=17;
        }
    }
    void OnDestroy(){if(ownColliders!=null)foreach(var c in ownColliders)if(c!=null)colliderOwners.Remove(c);}
}

public sealed class OriginalItemCollisionRelay : MonoBehaviour {
    public OriginalItem item;
    void OnCollisionEnter2D(Collision2D c){if(item!=null)item.OnOriginalCollision(c);}
}
