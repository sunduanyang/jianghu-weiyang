using System;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    public J ProbeSourceConstraintComponents(){
        if(!IsSmoke)throw new InvalidOperationException("Constraint topology probe requires isolated acceptance");
        Clock=elapsed=0;ResetRound();RunCampaign(true);fixtureMode=true;
        var result=player.ProbeSourceConstraintTopology();
        fixtureMode=false;PauseCampaign();return result;
    }
    public sealed partial class Fighter {
        internal J ProbeSourceConstraintTopology(){
            var hand=referenceBones["handNear"];hand.rb.position+=Vector2.right;
            float liveBefore=MaxJointGap();
            ApplyReferenceDamage("forearmNear",31,hand.rb.position,DuelReferenceRules.Surface.MetalBlade,0);
            hand.rb.position+=Vector2.right;
            float looseGap=MeasuredJointGap(hand.joint),liveAfter=MaxJointGap();
            referenceBones["handFar"].rb.position+=Vector2.right;
            return J.O("live_before",liveBefore,"detached_actual_gap",looseGap,"live_after_cut",liveAfter,"other_live_gap",MaxJointGap(),
                "forearm_cut",!sourceCutJoints["forearmNear"].enabled,"child_hinge_retained",hand.joint.enabled,"child_health",partHealth["handNear"]);
        }
    }
}
