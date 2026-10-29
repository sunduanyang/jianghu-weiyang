using System;
using System.Collections.Generic;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    public J ProbeSourceKickReceiver(){
        if(!IsSmoke)throw new InvalidOperationException("Kick receiver acceptance requires isolated save");
        var cases=new List<J>();
        try{
            foreach(bool victimIsPlayer in new[]{true,false})foreach(bool passedOut in new[]{false,true}){
                Clock=elapsed=1;ResetRound();RunCampaign(true);fixtureMode=true;
                var victim=victimIsPlayer?player:enemy;var attacker=victimIsPlayer?enemy:player;
                foreach(var fighter in new[]{player,enemy})fighter.FreezeSourceKickReceiverProbe();
                victim.PrepareSourceKickVictimProbe(passedOut);attacker.PrepareSourceKickAttackerProbe(victim);
                Physics2D.SyncTransforms();SimulateCombat(1f/6000);
                var result=J.O("victim_player",victimIsPlayer,"passed_out",passedOut,"after_trigger",victim.ReadSourceKickReceiverProbe(),"trigger_active",attacker.SourceKickTriggerEnabledForProbe());
                var phases=new List<J>();
                for(int phase=1;phase<=2;phase++){
                    var before=victim.ReadSourceKickReceiverProbe();var saved=J.Parse(CaptureCampaign().ToString());
                    ResetRound();RestoreCampaign(saved);victim=victimIsPlayer?player:enemy;
                    phases.Add(J.O("before",before,"restored",victim.ReadSourceKickReceiverProbe()));
                    victim.AdvanceSourceKickReceiverForProbe();
                }
                result["phases"]=new J(phases);result["complete"]=victim.ReadSourceKickReceiverProbe();cases.Add(result);
            }
        }finally{fixtureMode=false;PauseCampaign();}
        return J.O("cases",new J(cases),"entry","Physics2D.Simulate -> DuelKickTrigger.OnTriggerEnter2D", "native_damping_writes",false);
    }
    public sealed partial class Fighter {
        internal void FreezeSourceKickReceiverProbe(){
            if(!SourceRigActive)throw new InvalidOperationException("Source kick probe requires the imported graph");
            foreach(var body in root.GetComponentsInChildren<Rigidbody2D>(true)){body.constraints=RigidbodyConstraints2D.FreezeAll;body.linearVelocity=Vector2.zero;body.angularVelocity=0;}
            foreach(var joint in root.GetComponentsInChildren<Joint2D>(true))joint.enabled=false;
            foreach(var collider in colliders)collider.enabled=false;
        }
        internal void PrepareSourceKickVictimProbe(bool passedOut){
            health=100;exhaustedUntil=passedOut?game.Clock+3:-10;stamina=passedOut?0:100;
            SetReferenceLimp(passedOut);
            mover.constraints=pelvis.rb.constraints=RigidbodyConstraints2D.FreezeRotation;
            mover.gravityScale=pelvis.rb.gravityScale=0;mover.linearDamping=pelvis.rb.linearDamping=2.25f;
            mover.linearVelocity=new Vector2(3,2);pelvis.rb.linearVelocity=new Vector2(-4,-3);
            referenceKickReceiver.gameObject.SetActive(true);referenceKickReceiver.enabled=true;
        }
        internal void PrepareSourceKickAttackerProbe(Fighter victim){
            kickUntil=game.Clock+.5f;kickPaid=true;UpdateReferenceKick();
            Physics2D.SyncTransforms();
            kickShape.transform.position+=(Vector3)(victim.referenceKickReceiver.bounds.center-kickShape.bounds.center);
        }
        internal bool SourceKickTriggerEnabledForProbe()=>kickShape.enabled&&kickShape.gameObject.activeInHierarchy;
        internal J ReadSourceKickReceiverProbe(){
            var config=sourceRig.Source("KickReceiver");
            return J.O("state",CaptureReferenceKick(),"being_kicked",ReferenceBeingKicked,"health",health,"stamina",stamina,
                "root_drag",mover.linearDamping,"pelvis_drag",pelvis.rb.linearDamping,"root_vx",mover.linearVelocity.x,"root_vy",mover.linearVelocity.y,
                "pelvis_vx",pelvis.rb.linearVelocity.x,"pelvis_vy",pelvis.rb.linearVelocity.y,"root_mass",mover.mass,"pelvis_mass",pelvis.rb.mass,
                "expected_force_x",(config["flipped"].B?1:-1)*(float)config["kickForce"].N,"serialized_damping_unused",(float)config["dampingWhenKicked"].N);
        }
        internal void AdvanceSourceKickReceiverForProbe(){game.referenceSubstep=0;game.Clock=sourceKickReceiverAt+.00001f;TickSourceKickReceiver();}
    }
}
