using System;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    public J ProbeReferenceCore(){
        if(!IsSmoke)throw new InvalidOperationException("Core probes require isolated acceptance");
        fixtureMode=true;var result=J.O();
        ResetRound();Clock=elapsed=1;player.testOverride=enemy.testOverride=true;
        result["transitions"]=player.ProbeReferenceCoreTransitions();
        ResetRound();Clock=elapsed=1;player.testOverride=enemy.testOverride=true;
        player.maxStamina=140;player.stamina=0;RunCampaign(true);SimulateFighters();
        result["passed_out"]=J.O("active",player.PassedOut,"arm_mass",player.limbs["upperNear"].rb.mass,"arm_drag",player.limbs["upperNear"].rb.linearDamping,"arm_spin",player.limbs["upperNear"].rb.angularDamping,"foot_spin",player.limbs["footNear"].rb.angularDamping);
        while(player.PassedOut)SimulateFighters();
        result["wake_stamina"]=new J(player.stamina);result["wake_max_stamina"]=new J(player.maxStamina);
        ResetRound();Clock=elapsed=1;player.testOverride=enemy.testOverride=true;
        player.health=0;player.SetReferenceLimpForProbe();
        result["dead"]=J.O("arm_spin",player.limbs["upperNear"].rb.angularDamping,"foot_spin",player.limbs["footNear"].rb.angularDamping,"trunk_spin",player.torso.rb.angularDamping);
        ResetRound();Clock=elapsed=1;player.testOverride=enemy.testOverride=true;
        result["limbs"]=player.ProbeReferenceLimbModes();
        fixtureMode=false;PauseCampaign();return result;
    }
    public sealed partial class Fighter {
        internal void SetReferenceLimpForProbe()=>SetReferenceLimp(true);
        internal J ProbeReferenceCoreTransitions(){
            var result=J.O();game.Clock=1;
            result["first"]=new J(AcceptReferenceImpact("torso"));game.Clock=1.08f;
            result["same_part"]=new J(AcceptReferenceImpact("torso"));
            result["second"]=new J(AcceptReferenceImpact("head"));game.Clock=1.12f;
            result["third"]=new J(AcceptReferenceImpact("neck"));game.Clock=1.16f;
            result["fourth"]=new J(AcceptReferenceImpact("upperFar"));game.Clock=1.23f;
            result["after_quiet"]=new J(AcceptReferenceImpact("torso"));
            kickedUntil=game.Clock+.5f;mover.linearVelocity=new Vector2(50,0);game.referenceSubstep=0;
            ClampReferenceRoot();result["kick_clamped_speed"]=new J(mover.linearVelocity.magnitude);
            game.Clock+=.4f;UpdateReferenceKick();result["kick_damping"]=new J(mover.linearDamping);
            exhaustedUntil=game.Clock+3;mover.linearVelocity=new Vector2(-50,0);ClampReferenceRoot();
            result["passed_out_clamped_speed"]=new J(mover.linearVelocity.magnitude);
            var saved=CaptureReference();RestoreReferenceCore(saved["core_state"],saved["parts"]);RestoreReferenceKick(saved);
            result["restored_kick_damping"]=new J(mover.linearDamping);return result;
        }
        internal J ProbeReferenceLimbModes(){
            var result=J.O();health=maxHealth=1000;
            ApplyReferenceDamage("forearmFar",31,pelvis.B,DuelReferenceRules.Surface.MetalBlunt,0);
            result["bashed"]=J.O("mode",(int)ReferencePartMode("forearmFar"),"detached",ReferenceSevered("forearmFar"),"bleeding",referenceBleeding,"grip_enabled",offhandGrip!=null&&offhandGrip.enabled);
            ApplyReferenceDamage("forearmNear",31,pelvis.B,DuelReferenceRules.Surface.MetalBlade,0);
            result["cut"]=J.O("mode",(int)ReferencePartMode("forearmNear"),"root_joint",limbs["forearmNear"].joint.enabled,"child_joint",limbs["handNear"].joint.enabled,
                "child_detached",ReferenceSevered("handNear"),"mass",limbs["forearmNear"].rb.mass,"spin",limbs["forearmNear"].rb.angularDamping,"bleeding",referenceBleeding);
            float initial=health,at=referenceBleedAt;game.Clock=at-.001f;TickReferenceBleeding();result["bleed_before"]=new J(initial-health);
            game.Clock=at+.001f;TickReferenceBleeding();result["bleed_first"]=new J(initial-health);
            game.Clock=referenceBleedAt+.001f;exhaustedUntil=game.Clock+3;TickReferenceBleeding();result["bleed_second_while_passed_out"]=new J(initial-health);
            var saved=CaptureReference();RestoreReferenceCore(saved["core_state"],saved["parts"]);
            result["saved_core"]=saved["core_state"];result["restored_core"]=CaptureReferenceCore();
            ApplyReferenceDamage("upperFar",31,pelvis.B,DuelReferenceRules.Surface.MetalSpike,0);
            result["pierced"]=J.O("mode",(int)ReferencePartMode("upperFar"),"joint",limbs["upperFar"].joint.enabled,"detached",ReferenceSevered("upperFar"),"bleeding",referenceBleeding);
            health=74;maxHealth=140;ApplyReferenceDamage("head",70,head.B,Vector2.zero,true);
            result["head_above_twenty"]=new J(health==21.5f&&ReferencePartMode("head")==DuelReferenceRules.PartMode.None);
            ApplyReferenceDamage("head",3,head.B,Vector2.zero,true);
            result["head_below_twenty"]=new J(health==0&&ReferencePartMode("head")==DuelReferenceRules.PartMode.Severed);
            return result;
        }
    }
}
