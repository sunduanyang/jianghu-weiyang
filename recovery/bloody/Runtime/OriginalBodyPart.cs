using System.Collections;
using Jianghu;
using UnityEngine;

public sealed class OriginalArmorPart : MonoBehaviour {
    public int sourceId,material;
    public float defense;
}

// BodyPart native methods 0x3419420..0x341b7dc.
public sealed class OriginalBodyPart : MonoBehaviour {
    public OriginalRig rig;
    public J config;
    public int sourceId,type;
    public float health,maxHealth,overallMultiplier;
    public bool severable,severed;
    public OriginalPartMode mode;
    public Rigidbody2D body;
    public OriginalArmorPart[] armor=new OriginalArmorPart[3];
    float normalMass,normalDrag,normalAngular,impactCooldown;
    Vector2 severVelocity;
    Vector3 restPosition;
    float severAngular;
    bool pendingMode;
    public void Initialize(OriginalRig owner,J record) {
        rig=owner;config=record["data"];sourceId=record["id"].I;
        type=config["bodyPartType"].I;maxHealth=health=(float)config["MaxHealth"].N;
        restPosition=transform.localPosition;
        overallMultiplier=(float)config["overallDamageMultiplier"].N;severable=config["severable"].B;
        body=rig.Resolve<Rigidbody2D>(config["thisRigidbody2D"]);
        if(body!=null){normalMass=body.mass;normalDrag=body.linearDamping;normalAngular=body.angularDamping;}
        for(int i=0;i<3;i++)armor[i]=rig.ArmorPart(config["armorPart_L"+(i+1)]["id"].I);
    }
    public void ApplyLocalDamage(float damage){if(damage>0&&mode==OriginalPartMode.None)health=Mathf.Max(0,health-damage);}
    void OnCollisionEnter2D(Collision2D collision) {
        if(rig==null||Time.time<impactCooldown||collision.contactCount==0)return;
        var item=OriginalItem.From(collision.collider);if(item==null||item.owner==rig)return;
        var contact=collision.GetContact(0);
        var surface=OriginalCombatRules.Surface(collision.collider.sharedMaterial);
        float threshold=rig.DamageNumber(item.blunt?"DamageSpeedThresholdBlunt":"DamageSpeedThresholdSharp");
        if(OriginalCombatRules.Handle(surface))threshold*=.5f;
        if(contact.relativeVelocity.magnitude>=threshold)ProcessImpact(item,contact.point,contact.relativeVelocity,contact.normal,surface,false);
    }
    public bool ProcessImpact(OriginalItem item,Vector2 point,Vector2 velocity,Vector2 normal,OriginalSurface surface,bool cannon=false) {
        if(item==null||rig==null)return false;
        if(!rig.RegisterOriginalImpact()&&!cannon)return false;
        // Original enemy reaction is rolled before material/category math.
        if(!rig.human&&Random.Range(0,100)>75)rig.StartOriginalPullBackward();
        float defense=0;int material=config["armorMaterialIndex"].I;
        foreach(var layer in armor)if(layer!=null){defense+=layer.defense;material=layer.material;}
        material=Mathf.Max(1,material);
        float raw=OriginalCombatRules.RawDamage(item.category,item.twoHanded,velocity,item.damage,defense,surface,item.isShield,material,rig.DamageNumber("damagePerMps"));
        rig.rootBody.AddForceX((rig.OriginalFlipped?raw:-raw)*rig.DamageNumber("knockbackPerDamage"));
        float local=Mathf.Clamp(raw,1,100),global=local*overallMultiplier;
        ApplyLocalDamage(local);rig.ApplyOriginalGlobalDamage(global);rig.AccumulateOriginalImpact(global);
        if(type==0)severable=rig.health<20;
        var deathMode=OriginalCombatRules.DeathMode(surface);
        if(!cannon)impactCooldown=Time.time+.15f;
        rig.PublishOriginalImpact(new OriginalImpact {attacker=item.owner,victim=rig,item=item,part=this,point=point,velocity=velocity,normal=normal,localDamage=local,globalDamage=global,surface=surface});
        if(cannon) {
            Vector2 direction=velocity.sqrMagnitude>1e-8f?velocity.normalized:-normal;
            body.linearVelocity=direction*30+Vector2.up;
            if(type==0){rig.ApplyOriginalGlobalDamage(100);severable=true;}
            else if(type==1){rig.ApplyOriginalGlobalDamage(50);rig.StartOriginalBleeding();}
            else rig.ApplyOriginalGlobalDamage(10);
            rig.ConsumeOriginalStamina(100);health=0;
        }
        if(health<=0&&severable&&mode==OriginalPartMode.None&&!pendingMode&&deathMode!=OriginalPartMode.None) {
            if(cannon){pendingMode=true;StartCoroutine(DelayedMode(deathMode));}else SetMode(deathMode);
        }
        return true;
    }
    IEnumerator DelayedMode(OriginalPartMode value){yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();if(mode==OriginalPartMode.None)SetMode(value);}
    public void SetMode(OriginalPartMode value) {
        if(mode!=OriginalPartMode.None||value==OriginalPartMode.None)return;
        if(body!=null){severVelocity=body.linearVelocity;severAngular=body.angularVelocity;}
        mode=value;
        if(type==0)rig.ApplyOriginalGlobalDamage(rig.health);
        if(value==OriginalPartMode.Pierced||value==OriginalPartMode.Severed)rig.StartOriginalBleeding();
        rig.DisableOriginalPart(this);
        if(value==OriginalPartMode.Pierced||value==OriginalPartMode.Severed) {
            foreach(var rb in GetComponentsInChildren<Rigidbody2D>(true))if(rb.gameObject.layer!=15&&rb.gameObject.layer!=16)rb.gameObject.layer=17;
            foreach(var r in config["relatedBodyParts"].A) {
                var p=rig.BodyPart(r["id"].I);if(p!=null&&p.transform.IsChildOf(transform)){p.SetPhysicsDead();if(value==OriginalPartMode.Severed)p.RebakeJointAtRest();}
            }
            if(value==OriginalPartMode.Severed) {
                RebakeJointAtRest();SetPhysicsDead();
                var joint=rig.Resolve<Joint2D>(config["joint2D"]);if(joint!=null)joint.enabled=false;
                var position=transform.localPosition;position.z=(float)config["severedNewZPosition"].N;transform.localPosition=position;
                transform.SetParent(null,true);
                if(body!=null){body.linearVelocity=severVelocity;body.angularVelocity=severAngular;}
            }
            foreach(var r in config["objectsToDestroy"].A) {var t=rig.ResolveTransform(r);if(t!=null)Destroy(t.gameObject);}
            foreach(var r in config["objectsToActivate"].A) {var t=rig.ResolveTransform(r);if(t!=null)t.gameObject.SetActive(true);}
            if(body!=null){body.linearDamping=0;body.angularDamping=5;body.mass=9;var material=rig.OriginalDeadMaterial(config["deadPhysicsMaterial"]);if(material!=null)body.sharedMaterial=material;}
            severed=true;
        }
        rig.PublishOriginalPartMode(this,value);
    }
    void RebakeJointAtRest() {
        var joint=rig.Resolve<AnchoredJoint2D>(config["joint2D"]);if(joint==null)return;
        joint.autoConfigureConnectedAnchor=false;transform.localPosition=restPosition;
        joint.autoConfigureConnectedAnchor=true;joint.autoConfigureConnectedAnchor=false;
    }
    public void SetPhysicsDead() {
        if(body==null)return;
        var dead=rig.Resolve<Collider2D>(config["deadCollider"]);
        if(dead!=null){foreach(var r in config["aliveColliders"].A){var c=rig.Resolve<Collider2D>(r);if(c!=null)c.enabled=false;}dead.enabled=true;}
        body.mass=(float)config["deadMass"].N;body.linearDamping=(float)config["deadDrag"].N;body.angularDamping=(float)config["deadAngularDrag"].N;body.gravityScale=1;
    }
    public void SetPhysicsPassedOut(bool value) {
        if(body==null)return;
        body.mass=value?(float)config["passedOutMass"].N:normalMass;
        body.linearDamping=value?(float)config["passedOutDrag"].N:normalDrag;
        body.angularDamping=value?(float)config["passedOutAngularDrag"].N:normalAngular;
    }
}
