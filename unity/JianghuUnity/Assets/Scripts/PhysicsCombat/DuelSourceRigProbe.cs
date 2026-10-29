using System;
using System.Collections.Generic;
using System.Linq;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    public J ProbeSourceNothing(J fighters){
        if(!IsSmoke)throw new InvalidOperationException("Nothing equipment acceptance requires isolated save");
        var original=campaign.battle["fighters"];campaign.battle["fighters"]=fighters;
        Clock=elapsed=0;ResetRound();RunCampaign(true);fixtureMode=true;player.testOverride=enemy.testOverride=true;
        var evidence=J.O("player",player.SourceNothingEquipment(),"enemy",enemy.SourceNothingEquipment());
        for(int tick=0;tick<60;tick++)SimulateFighters();
        evidence["settled_player"]=player.SourceNothingEquipment();evidence["settled_enemy"]=enemy.SourceNothingEquipment();
        campaign.battle["fighters"]=original;fixtureMode=false;PauseCampaign();return evidence;
    }
    public J ProbeSourceRig(){
        if(!IsSmoke)throw new InvalidOperationException("Source rig acceptance requires isolated save");
        Clock=elapsed=0;ResetRound();RunCampaign(true);fixtureMode=true;
        player.testOverride=enemy.testOverride=true;
        var initial=J.O("player",player.ProbeSourceRigData(),"enemy",enemy.ProbeSourceRigData());
        initial["joint_initial"]=ProbeSourceJointMetrics();
        float maxGap=0,maxSpeed=0,maxHeight=0;var samples=new List<J>();
        for(int tick=0;tick<360;tick++){
            if(tick==180)player.testOverride=enemy.testOverride=false;
            if(tick>=60&&tick<120)player.move=1;else if(tick>=120&&tick<180)player.move=-1;else player.move=0;
            if(tick>=180){player.ReadAI(enemy,false);enemy.ReadAI(player,false);}
            SimulateFighters();elapsed=Clock;
            if(tick==0)initial["joint_first_frame"]=ProbeSourceJointMetrics();
            if(tick==59)initial["joint_one_second"]=ProbeSourceJointMetrics();
            maxGap=Mathf.Max(maxGap,player.ReferenceSkeletonGap(),enemy.ReferenceSkeletonGap());
            foreach(var f in new[]{player,enemy})foreach(var rb in f.SourceRigBodiesForProbe()){maxSpeed=Mathf.Max(maxSpeed,rb.linearVelocity.magnitude);maxHeight=Mathf.Max(maxHeight,rb.position.y);}
            if(tick%60==59)samples.Add(J.O("time",Clock,"player",player.ProbeSourceRigPose(),"enemy",enemy.ProbeSourceRigPose()));
        }
        var saved=J.Parse(CaptureCampaign().ToString());ResetRound();RestoreCampaign(saved);
        var restored=CaptureCampaign();
        initial["joint_restored"]=ProbeSourceJointMetrics();
        initial["samples"]=new J(samples);initial["maximum_gap"]=new J(maxGap);initial["maximum_speed"]=new J(maxSpeed);initial["maximum_height"]=new J(maxHeight);
        initial["saved"]=saved;initial["restored"]=restored;
        float resumedGap=0;RunCampaign(true);
        for(int tick=0;tick<60;tick++){player.ReadAI(enemy,false);enemy.ReadAI(player,false);SimulateFighters();elapsed=Clock;resumedGap=Mathf.Max(resumedGap,player.ReferenceSkeletonGap(),enemy.ReferenceSkeletonGap());}
        initial["resumed_gap"]=new J(resumedGap);initial["resumed_frame"]=CaptureCampaign();
        fixtureMode=false;player.RebaseSourceAIClock();enemy.RebaseSourceAIClock();PauseCampaign();return initial;
    }
    public sealed partial class Fighter {
        internal J SourceNothingEquipment(){
            var rows=new List<J>();
            foreach(string side in new[]{"Near","Far"}){
                if(weapon.TwoHanded||side=="Near"&&weapon.id!="unarmed"||side=="Far"&&HasShield)continue;
                var item=side=="Near"?sword:shield;var grip=side=="Near"?primaryGrip:offhandGrip;var counter=referenceWeights[side];
                var circle=item.shape as CircleCollider2D;
                rows.Add(J.O("side",side,"circle",circle!=null,"radius",circle==null?0:circle.radius,"surface",DuelSourceEquipment.Surface(DuelWeapon.Fist).ToString(),
                    "mass",item.rb.mass,"drag",item.rb.linearDamping,"angular_drag",item.rb.angularDamping,"counter_mass",counter.mass,"counter_radius",counter.GetComponent<CircleCollider2D>().radius,
                    "com_mass",referenceBodies["handCOM"+side].mass,"source_grip",sourceRig.Components.Values.Contains(grip),"connected",grip.connectedBody==item.rb,"grip_gap",MeasuredJointGap(grip)));
            }
            return new J(rows);
        }
        internal IEnumerable<Rigidbody2D> SourceRigBodiesForProbe()=>sourceRig.Bodies.Values;
        internal J ProbeSourceRigData()=>J.O("active",SourceRigActive,"template",SourceRigTemplate,"validation",sourceRigValidation,"shape",SourceBodyShapeEvidence,"snapshot",CaptureSourceRig(),"parts",referenceBones.Count,
            "organs",SourceOrganTriggerCount,"main_mass",sword.rb.mass,"main_damping",sword.rb.linearDamping,"main_gravity",sword.rb.gravityScale,"main_layer",sword.shape.gameObject.layer,
            "main_collision_mode",sword.rb.collisionDetectionMode.ToString(),"off_collision_mode",shield==null?"":shield.rb.collisionDetectionMode.ToString(),
            "head_base_to_hinge",Vector2.Distance(head.A,head.rb.position),"head_base_to_neck_tip",Vector2.Distance(head.A,referenceBones["neck"].B),"head_bind_length",head.length,"head_bind_up",head.B.y-head.A.y);
        internal J ProbeSourceRigPose()=>J.O("root_x",mover.position.x,"root_y",mover.position.y,"head_x",head.rb.position.x,"head_y",head.rb.position.y,"gap",ReferenceSkeletonGap(),"health",health,"stamina",stamina,"ai",AIState,"taps",SourceAIWalkTaps,"attacks",attackId,"grip_error",PrimaryGripError);
    }
}
