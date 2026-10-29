using System;
using System.Collections.Generic;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    bool guardProbe;
    int guardWeaponContacts,guardShieldContacts;
    int guardBladeHits,guardHandleHits,guardBladeHitsAfterContact;
    float guardFirstContact,guardPeakContactSpeed;
    void RecordGuardBodyHit(DuelContact own,DuelContact other,bool handle){
        if(!guardProbe||own.owner!=player||own.limb!=player.sword||other.owner!=enemy)return;
        if(handle)guardHandleHits++;else{guardBladeHits++;if(guardFirstContact>=0)guardBladeHitsAfterContact++;}
    }
    void RecordGuardContact(DuelContact own,DuelContact other,float speed){
        if(!guardProbe||own.owner!=player||own.limb!=player.sword||other.owner!=enemy)return;
        if(other.limb==enemy.sword)guardWeaponContacts++;
        else if(enemy.HasShield&&other.limb==enemy.shield)guardShieldContacts++;
        else return;
        if(guardFirstContact<0)guardFirstContact=Clock;
        guardPeakContactSpeed=Mathf.Max(guardPeakContactSpeed,speed);
    }
    public sealed partial class Fighter {
        public Vector2 GuardProbeStickForWrist(Vector2 world){
            if(!game.IsSmoke||!SourceRigActive)throw new InvalidOperationException("World aim is an isolated source-input fixture");
            Vector2 delta=world-(Vector2)sourcePoints["handLeftPointParent"].position;
            return Vector2.ClampMagnitude(new Vector2(delta.x/facing-.15f,delta.y-.25f)/1.55f,1);
        }
    }
    // Actual formal fighters, all colliders, live health damage and independent
    // hand controls. The attacker has a large stamina budget to measure motor
    // power separately from endurance; the defender keeps ordinary stamina.
    public J ProbeReferenceGuard(float arc){
        if(!IsSmoke||!ReferenceCombat)throw new InvalidOperationException("Guard probe requires an isolated formal encounter");
        Clock=elapsed=0;ResetRound();RunCampaign(true);fixtureMode=guardProbe=true;muted=true;
        guardWeaponContacts=guardShieldContacts=guardBladeHits=guardHandleHits=guardBladeHitsAfterContact=0;guardFirstContact=-1;guardPeakContactSpeed=0;
        player.testOverride=enemy.testOverride=true;player.stamina=player.maxStamina=1000;
        int startHits=player.landedHits,hitsAtBlock=-1;float firstBody=-1,maxSpeed=0,maxGap=0,maxTurn=0,guardAngle=0;
        var samples=new List<J>();
        for(int frame=0;frame<720&&player.health>0&&enemy.health>0;frame++){
            float t=frame/60f;
            // Aim the blade, not the wrist, through the torso. The earlier
            // generic stick circle brought the grip into the body and measured
            // hilt punches. These are real stick inputs; bodies are never moved
            // or contacts disabled. Alternating wrist goals model a mouse flick.
            float phase=(t-1)*arc;
            Vector2 bladeOffset=(Vector2)player.sword.shape.bounds.center-player.limbs["handNear"].B;
            Vector2 target=(Vector2)enemy.torso.shape.bounds.center+new Vector2(.1f*Mathf.Cos(phase),Mathf.Sin(phase)>=0?.5f:-.5f);
            Vector2 swing=frame<60?Vector2.zero:player.GuardProbeStickForWrist(target-bladeOffset);
            player.ReferenceProbeInput(swing,Vector2.zero);
            enemy.ReferenceProbeInput(new Vector2(.25f,.22f),enemy.HasShield?new Vector2(.45f,.12f):Vector2.zero);
            player.move=frame>=60&&frame<180?1:0;enemy.move=0;
            if(guardFirstContact>=0&&hitsAtBlock<0){hitsAtBlock=player.landedHits;guardAngle=enemy.sword.rb.rotation;}
            SimulateFighters();
            if(firstBody<0&&player.landedHits>startHits)firstBody=Clock;
            maxSpeed=Mathf.Max(maxSpeed,player.sword.rb.GetPointVelocity(player.sword.B).magnitude);
            maxGap=Mathf.Max(maxGap,player.PrimaryGripError,enemy.PrimaryGripError);
            if(hitsAtBlock>=0)maxTurn=Mathf.Max(maxTurn,Mathf.Abs(Mathf.DeltaAngle(guardAngle,enemy.sword.rb.rotation)));
            if(frame%12==0)samples.Add(J.O("time",Clock,"player_hits",player.landedHits-startHits,"enemy_health",enemy.health,"enemy_stamina",enemy.stamina,"point_speed",player.sword.rb.GetPointVelocity(player.sword.B).magnitude));
        }
        var result=J.O("physics_backend",PhysicsBackendMetrics(),"weapon",player.weapon.id,"template",player.weapon.sourceTemplate,"enemy_shield",enemy.HasShield,"arc",arc,
            "baseline_tuning",referenceContactBaseline,"aim","primary_blade_center_through_torso","body_threshold",DuelSourceEquipment.SpeedThreshold(player.weapon),"blade_friction",bladeMaterial.friction,"friction_combine",(int)bladeMaterial.frictionCombine,
            "weapon_contacts",guardWeaponContacts,"shield_contacts",guardShieldContacts,"first_guard_contact",guardFirstContact,"peak_guard_contact_speed",guardPeakContactSpeed,
            "body_hits",player.landedHits-startHits,"body_hits_after_guard",hitsAtBlock<0?0:player.landedHits-hitsAtBlock,"first_body_hit",firstBody,"max_point_speed",maxSpeed,"max_grip_gap",maxGap,"guard_deflection_degrees",maxTurn,
            "primary_blade_hits",guardBladeHits,"primary_handle_hits",guardHandleHits,"primary_blade_hits_after_guard",guardBladeHitsAfterContact,
            "attacker_clash_stamina",player.SourceClashStaminaSpent,"defender_clash_stamina",enemy.SourceClashStaminaSpent,"attacker_stamina_budget",1000,"defender_stamina",enemy.stamina,"duration",Clock,"samples",new J(samples));
        guardProbe=fixtureMode=false;PauseCampaign();return result;
    }
}
