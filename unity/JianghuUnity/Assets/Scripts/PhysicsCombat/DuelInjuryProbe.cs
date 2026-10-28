using System;
using System.Collections.Generic;
using System.Linq;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    // Scripted injuries isolate support behavior. All following motion is
    // resolved by the shipping Physics2D rig, without replacing its colliders.
    public J ProbeReferenceInjury(string part,bool both=false,bool mirror=false,bool resume=false,bool exhaust=false,bool kicking=false,bool blunt=false){
        if(!IsSmoke)throw new InvalidOperationException("Injury probe requires an isolated acceptance save");
        ResetRound();Clock=elapsed=0;RunCampaign(true);fixtureMode=true;
        player.testOverride=enemy.testOverride=true;
        for(int tick=0;tick<60;tick++)SimulateFighters();
        Fighter victim=mirror?enemy:player;
        float standing=victim.pelvis.B.y,handY=victim.ReferenceHandTarget("Near").y;
        bool kickStarted=kicking&&victim.Dash();
        victim.ApplyReferenceDamage(part,41,victim.limbs[part].rb.position,Vector2.zero,!blunt);
        if(both)victim.ApplyReferenceDamage(part.EndsWith("Near")?part.Replace("Near","Far"):part.Replace("Far","Near"),41,victim.pelvis.B,Vector2.zero,!blunt);
        if(exhaust)victim.stamina=0;
        J atRestore=null;var samples=new List<J>();float maxGap=0,maxLimit=0,maxSettledPelvis=0;int groundTicks=0;
        for(int tick=0;tick<300;tick++){
            SimulateFighters();elapsed=Clock;
            if(tick==8&&resume){atRestore=CaptureCampaign();ResetRound();RestoreCampaign(atRestore);RunCampaign(true);player.testOverride=enemy.testOverride=true;victim=mirror?enemy:player;}
            if(tick>=240){maxSettledPelvis=Mathf.Max(maxSettledPelvis,victim.pelvis.B.y);if(victim.InjuryBodyGrounded)groundTicks++;}
            maxGap=Mathf.Max(maxGap,victim.ReferenceSkeletonGap());maxLimit=Mathf.Max(maxLimit,victim.MaxLimitError());
            if(tick%6==0)samples.Add(J.O("time",Clock,"pelvis_y",victim.pelvis.B.y,"foot_y",victim.limbs["footNear"].rb.position.y,"hand_target_y",victim.ReferenceHandTarget("Near").y,"stamina",victim.stamina));
        }
        float startX=victim.Hip.x,maxMove=0;victim.move=victim.facing;
        for(int tick=0;tick<30;tick++){SimulateFighters();elapsed=Clock;maxMove=Mathf.Max(maxMove,victim.ReferenceRootVelocity.magnitude);}
        victim.move=0;
        var result=J.O("part",part,"both",both,"mirror",mirror,"resumed",atRestore!=null,"exhausted",exhaust,"kick_started",kickStarted,"blunt",blunt,"part_severed",victim.ReferenceSevered(part),
            "standing_pelvis_y",standing,"max_settled_pelvis_y",maxSettledPelvis,"hand_drop",handY-victim.ReferenceHandTarget("Near").y,
            "downed",victim.ReferenceDowned,"grounded_ticks",groundTicks,"kick_rejected",!victim.Dash(),"kick_inactive",!victim.ReferenceKickActive,
            "max_move_speed",maxMove,"travel",Mathf.Abs(victim.Hip.x-startX),"max_gap",maxGap,"max_limit",maxLimit,
            "health",victim.health,"stamina",victim.stamina,"driver_mass",victim.InjuryPelvisMass,"raw_peak_solver_spin",victim.ReferencePeakSolverSpin,"velocity_corrections",victim.ReferenceVelocityCorrections,
            "detached_drivers_disabled",victim.InjuryFootDriversDisabled,
            "max_detached_foot_y",victim.limbs.Where(p=>p.Key.StartsWith("foot")&&victim.ReferenceSevered(p.Key)).Select(p=>p.Value.rb.position.y).DefaultIfEmpty(0).Max(),
            "samples",new J(samples),"restore_frame",atRestore,"final_frame",CaptureCampaign());
        fixtureMode=false;PauseCampaign();return result;
    }
    public sealed partial class Fighter {
        public float InjuryPelvisMass=>referenceBodies["pelvisCOM"].mass;
        public bool InjuryFootDriversDisabled=>referenceBodies.Where(p=>p.Key.StartsWith("footCOM")&&ReferenceSevered("foot"+p.Key.Substring(7))).All(p=>!p.Value.simulated);
        public bool InjuryBodyGrounded=>referenceBones.Any(p=>!severed.Contains(p.Key)&&p.Value.shape.IsTouchingLayers(1<<8));
    }
}
