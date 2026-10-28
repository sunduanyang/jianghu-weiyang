using System;
using System.Collections.Generic;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    public J ProbeSourceProgram(){
        if(!IsSmoke)throw new InvalidOperationException("Source probes require an isolated acceptance save");
        ResetRound();Clock=elapsed=1;fixtureMode=true;
        player.testOverride=enemy.testOverride=true;
        var result=J.O("player",player.ProbeSourceTargets(),"enemy",enemy.ProbeSourceTargets());
        result["death"]=enemy.ProbeSourceDeath();fixtureMode=false;PauseCampaign();return result;
    }
    public void PrepareSourceCheckpoint(){
        if(!IsSmoke)throw new InvalidOperationException("Source checkpoint probe requires isolated acceptance");
        enemy.PrepareSourceCheckpointForProbe(player);enemy.testOverride=true;
        player.landedHits++;hits++;
    }
    public sealed partial class Fighter {
        internal void PrepareSourceCheckpointForProbe(Fighter target){InitializeAI();aiDriving=true;BeginSourceAttack(target);}
        internal J ProbeSourceTargets(){
            InitializeAI();aiDriving=true;aiOffAttack=false;aiSourceAt=game.Clock;aiSourceStart=0;aiPhase=AIPhase.Strike;
            aiSourceAttack=DuelSourceMotion.Named("1Hand_SwingDown");
            game.Clock=aiSourceAt+1f/3;
            SourceHandTarget("Near",out var target);Vector2 delta=target-mover.position;
            aiSourceParentNear=SourceParentFromHand("Near");
            aiSourceAttack=DuelSourceMotion.Named("2Hands_Stab");
            bool unbound=SourceHandTarget("Near",out var held);
            kickUntil=game.Clock+.25f;
            Vector2 foot=ReferenceKickFoot("Near")-mover.position,pelvisOffset=ReferenceKickPelvis();
            partHealth["thighNear"]=0;SetReferencePartMode("thighNear",DuelReferenceRules.PartMode.Bashed);
            aiSourceParentNear=SourceParentFromHand("Near");bool downedUnbound=SourceHandTarget("Near",out var downHeld);
            return J.O("facing",facing,"x",delta.x,"y",delta.y,"unbound_safe",unbound&&Vector2.Distance(held,referenceHands["Near"].target)<.00001f&&downedUnbound&&Vector2.Distance(downHeld,referenceHands["Near"].target)<.00001f,
                "kick_x",foot.x,"kick_y",foot.y,"pelvis_x",pelvisOffset.x,"pelvis_y",pelvisOffset.y);
        }
        internal J ProbeSourceDeath(){
            health=0;BeginReferenceDeath();game.referenceSubstep=0;
            var stages=new List<J>();
            void Record(){stages.Add(J.O("stage",referenceDeathStage,"arm_mass",limbs["upperNear"].rb.mass,
                "grip",primaryGrip.enabled,"frequency",referenceBodies["pelvisCOM"].GetComponent<FixedJoint2D>().frequency,
                "head_joint",referenceBodies["headCOM"].GetComponent<FixedJoint2D>().enabled,"corpse",referenceCorpseBody.gameObject.activeSelf));}
            Record();game.Clock+=.0251f;TickReferenceDeath();Record();
            var saved=CaptureReferenceDeath();RestoreReferenceDeath(saved);var restored=CaptureReferenceDeath();
            for(int i=0;i<5;i++){game.Clock+=.0251f;TickReferenceDeath();Record();}
            return J.O("stages",new J(stages),"saved",saved,"restored",restored);
        }
    }
}
