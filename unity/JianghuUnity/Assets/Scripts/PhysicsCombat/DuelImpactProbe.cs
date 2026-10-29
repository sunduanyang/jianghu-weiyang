using System;
using System.Collections.Generic;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    // Controlled contact fixture, available only to an isolated native test.
    // Actual contacts use the shipping collector, resolver and duplicate cache.
    public J ProbeImpact(DuelWeapon profile,float speed,string zone,bool shieldStrike=false,J armorOverride=null,bool testRecontact=true){
        if(!IsSmoke)throw new InvalidOperationException("Impact probes require acceptance mode");
        ResetRound();RunCampaign(true);fixtureMode=true;elapsed=1;
        foreach(var fighter in new[]{player,enemy}){
            fighter.testOverride=true;
            foreach(var c in fighter.colliders)c.enabled=false;
            foreach(var limb in fighter.limbs.Values){limb.rb.gravityScale=0;limb.rb.linearVelocity=Vector2.zero;limb.rb.angularVelocity=0;}
        }
        player.InterruptAttack();player.stamina=0;enemy.health=enemy.maxHealth=1000;
        J previousArmor=enemy.SourceEquipmentPacket["armor"];
        if(armorOverride!=null)enemy.SourceEquipmentPacket["armor"]=armorOverride;
        int before=hits;
        var moving=new GameObject("kinetic probe");SceneParent(moving);
        moving.transform.SetPositionAndRotation(new Vector3(-.35f,4),Quaternion.Euler(0,0,-90));
        var rb=moving.AddComponent<Rigidbody2D>();rb.gravityScale=0;rb.mass=profile.mass;rb.collisionDetectionMode=CollisionDetectionMode2D.Continuous;
        var shape=moving.AddComponent<CircleCollider2D>();shape.radius=.05f;
        if(ReferenceCombat)DuelSourceRig.ApplyItemLayer(shape,true);
        var weapon=new Limb {key="probe",rb=rb,shape=shape,length=.1f,strikeProfile=profile};
        var delivery=moving.AddComponent<DuelContact>();delivery.owner=player;delivery.limb=weapon;delivery.kind=shieldStrike?"shield":"sword";
        var observed=moving.AddComponent<DuelImpactContactTrace>();
        var wall=new GameObject("kinetic receiver");SceneParent(wall);wall.transform.position=new Vector3(0,4);
        var receiver=wall.AddComponent<Rigidbody2D>();receiver.bodyType=RigidbodyType2D.Kinematic;
        var box=wall.AddComponent<BoxCollider2D>();box.size=new Vector2(.1f,.8f);
        if(ReferenceCombat){wall.layer=11;DuelSourceRig.ApplyLayerCollision(box);}
        string partKey=zone=="head"?"head":zone=="body"?"torso":zone;
        var body=new Limb {key=ReferenceCombat?partKey:"receiver",rb=receiver,shape=box,length=.8f};
        var target=wall.AddComponent<DuelContact>();target.owner=enemy;target.limb=body;target.kind=zone;
        Physics2D.SyncTransforms();
        for(int step=0;step<40;step++){
            rb.linearVelocity=Vector2.right*speed;weapon.SampleVelocity();body.SampleVelocity();
            Clock+=Time.fixedDeltaTime;physicsStep++;SimulateCombat(Time.fixedDeltaTime);ResolveContacts();
        }
        float first=1000-enemy.health;int firstHits=hits-before;
        int firstContactEvents=observed.events;
        var firstMode=ReferenceCombat?enemy.ReferencePartMode(partKey):DuelReferenceRules.PartMode.None;
        bool firstDetached=ReferenceCombat&&enemy.ReferenceSevered(partKey);
        // Leave contact, then hit again without paying / generating a gesture.
        rb.position=new Vector2(-.5f,4);rb.linearVelocity=Vector2.zero;
        for(int step=0;testRecontact&&step<15;step++){Clock+=Time.fixedDeltaTime;physicsStep++;weapon.SampleVelocity();SimulateCombat(Time.fixedDeltaTime);ResolveContacts();}
        for(int step=0;testRecontact&&step<40;step++){
            rb.linearVelocity=Vector2.right*speed;weapon.SampleVelocity();body.SampleVelocity();
            Clock+=Time.fixedDeltaTime;physicsStep++;SimulateCombat(Time.fixedDeltaTime);ResolveContacts();
        }
        var armor=DuelSourceEquipment.Armor(enemy.SourceEquipmentPacket,partKey);
        var material=DuelSourceEquipment.Surface(profile,false,shieldStrike);
        float threshold=DuelSourceEquipment.SpeedThreshold(profile,false,shieldStrike);
        float expected=ReferenceCombat?(speed<threshold?0:DuelSourceEquipment.CalculateDamage(profile,Vector2.right*speed,armor,material,shieldStrike,player.damageScale).local*enemy.ProbePartOverallMultiplier(partKey)):DuelImpact.Damage(profile.mass,speed,profile.damage);
        J result=J.O("weapon",profile.id,"speed",speed,"zone",zone,"damage",first,"expected",expected,
            "first_hits",firstHits,"second_hits",hits-before-firstHits,"stamina",player.stamina,"attack_active",player.IsAttacking,
            "threshold",threshold,"surface",material.ToString(),"armor_defense",armor.defense,"armor_material",armor.material,
            "first_part_mode",(int)firstMode,"first_detached",firstDetached,"part_key",partKey,"source_rig",enemy.SourceRigActive,
            "first_contact_events",firstContactEvents,"second_contact_events",observed.events-firstContactEvents,"observed_collision_speed",observed.firstSpeed);
        if(armorOverride!=null)enemy.SourceEquipmentPacket["armor"]=previousArmor;
        moving.SetActive(false);wall.SetActive(false);Destroy(moving);Destroy(wall);fixtureMode=false;
        return result;
    }
    public sealed partial class Fighter {
        internal float ProbePartOverallMultiplier(string key)=>SourceRigActive?(float)sourceParts[key]["overallDamageMultiplier"].N:key=="head"||key=="neck"?.75f:.35f;
    }
    void SceneParent(GameObject go){go.transform.SetParent(transform,false);}
}

// Acceptance-only observer; it does not calculate damage or alter contacts.
public sealed class DuelImpactContactTrace : MonoBehaviour {
    public int events;public float firstSpeed;
    void OnCollisionEnter2D(Collision2D collision){RecordCore(new DuelCollision(collision));}
    public void RecordCore(DuelCollision collision){if(collision.contactCount==0)return;if(events++==0)firstSpeed=collision.GetContact(0).relativeVelocity.magnitude;}
}
