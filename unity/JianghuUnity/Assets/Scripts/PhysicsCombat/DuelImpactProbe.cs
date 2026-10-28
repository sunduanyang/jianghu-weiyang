using System;
using System.Collections.Generic;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    // Controlled contact fixture, available only to an isolated native test.
    // Actual contacts use the shipping collector, resolver and duplicate cache.
    public J ProbeImpact(DuelWeapon profile,float speed,string zone,bool shieldStrike=false){
        if(!IsSmoke)throw new InvalidOperationException("Impact probes require acceptance mode");
        ResetRound();RunCampaign(true);fixtureMode=true;elapsed=1;
        foreach(var fighter in new[]{player,enemy}){
            fighter.testOverride=true;
            foreach(var c in fighter.colliders)c.enabled=false;
            foreach(var limb in fighter.limbs.Values){limb.rb.gravityScale=0;limb.rb.linearVelocity=Vector2.zero;limb.rb.angularVelocity=0;}
        }
        player.InterruptAttack();player.stamina=0;enemy.health=enemy.maxHealth=1000;
        int before=hits;
        var moving=new GameObject("kinetic probe");SceneParent(moving);
        moving.transform.SetPositionAndRotation(new Vector3(-.35f,4),Quaternion.Euler(0,0,-90));
        var rb=moving.AddComponent<Rigidbody2D>();rb.gravityScale=0;rb.mass=profile.mass;rb.collisionDetectionMode=CollisionDetectionMode2D.Continuous;
        var shape=moving.AddComponent<CircleCollider2D>();shape.radius=.05f;
        var weapon=new Limb {key="probe",rb=rb,shape=shape,length=.1f,strikeProfile=profile};
        var delivery=moving.AddComponent<DuelContact>();delivery.owner=player;delivery.limb=weapon;delivery.kind=shieldStrike?"shield":"sword";
        var wall=new GameObject("kinetic receiver");SceneParent(wall);wall.transform.position=new Vector3(0,4);
        var receiver=wall.AddComponent<Rigidbody2D>();receiver.bodyType=RigidbodyType2D.Kinematic;
        var box=wall.AddComponent<BoxCollider2D>();box.size=new Vector2(.1f,.8f);
        var body=new Limb {key=ReferenceCombat?(zone=="head"?"head":"torso"):"receiver",rb=receiver,shape=box,length=.8f};
        var target=wall.AddComponent<DuelContact>();target.owner=enemy;target.limb=body;target.kind=zone;
        Physics2D.SyncTransforms();
        for(int step=0;step<40;step++){
            rb.linearVelocity=Vector2.right*speed;weapon.SampleVelocity();body.SampleVelocity();
            Clock+=Time.fixedDeltaTime;physicsStep++;combatPhysics.Simulate(Time.fixedDeltaTime);ResolveContacts();
        }
        float first=1000-enemy.health;int firstHits=hits-before;
        // Leave contact, then hit again without paying / generating a gesture.
        rb.position=new Vector2(-.5f,4);rb.linearVelocity=Vector2.zero;
        for(int step=0;step<15;step++){Clock+=Time.fixedDeltaTime;physicsStep++;weapon.SampleVelocity();combatPhysics.Simulate(Time.fixedDeltaTime);ResolveContacts();}
        for(int step=0;step<40;step++){
            rb.linearVelocity=Vector2.right*speed;weapon.SampleVelocity();body.SampleVelocity();
            Clock+=Time.fixedDeltaTime;physicsStep++;combatPhysics.Simulate(Time.fixedDeltaTime);ResolveContacts();
        }
        float expected=ReferenceCombat?(speed<=(shieldStrike?20:15)?0:Fighter.ReferenceDamage(profile.id,profile.TwoHanded,Vector2.right*speed,shieldStrike?1:profile.damage,0,false,shieldStrike)*(zone=="head"?.75f:.35f)):DuelImpact.Damage(profile.mass,speed,profile.damage);
        J result=J.O("weapon",profile.id,"speed",speed,"zone",zone,"damage",first,"expected",expected,
            "first_hits",firstHits,"second_hits",hits-before-firstHits,"stamina",player.stamina,"attack_active",player.IsAttacking);
        moving.SetActive(false);wall.SetActive(false);Destroy(moving);Destroy(wall);fixtureMode=false;
        return result;
    }
    void SceneParent(GameObject go){go.transform.SetParent(transform,false);}
}
