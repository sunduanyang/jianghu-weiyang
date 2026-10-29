using System;
using System.Collections.Generic;
using System.Linq;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    // Isolated native acceptance exercises the real scene trigger callback;
    // ReceiveSourceOrganOverlap is never invoked directly by this probe.
    public J ProbeSourceImpalement(){
        if(!IsSmoke)throw new InvalidOperationException("Impalement probes require an isolated acceptance save");
        var cases=new List<J>();
        try{
            foreach(string name in new[]{"threshold","spike","too_little_damage","healthy","expired","blunt","handle","pommel","detached","overkill_accumulates"}){
                Clock=elapsed=1;ResetRound();started=true;paused=help=finished=armory=false;fixtureMode=true;
                var organ=enemy.PrepareSourceImpalementProbe(player,name);
                // Keep only the tested source organ and the real main weapon's
                // damaging shape. Bodies and the weapon remain real Physics2D
                // bodies; FreezeAll prevents the fixture changing their poses.
                player.FreezeSourceImpalementProbe(organ.shape,player.sword.shape,true);
                enemy.FreezeSourceImpalementProbe(organ.shape,player.sword.shape,false);
                Physics2D.SyncTransforms();
                player.sword.rb.position+=(Vector2)organ.shape.bounds.center-(Vector2)player.sword.shape.bounds.center;
                Physics2D.SyncTransforms();combatPhysics.Simulate(1f/60);
                cases.Add(J.O("case",name,"trigger_events",organ.TriggerEnterCount,"health",enemy.health,"accumulated",enemy.SourceAccumulatedDamage,
                    "stage",enemy.SourceImpalementStage,"joint",player.SourceWeaponImpaling("Near"),"masks",organ.MasksVisible));
                if(name=="threshold")cases[cases.Count-1]["phases"]=enemy.ProbeSourceImpalementPhases(player);
                if(name=="overkill_accumulates"){
                    var state=enemy.CaptureSourceImpalement();enemy.RestoreSourceImpalement(state);enemy.ResolveSourceImpalement();
                    cases[cases.Count-1]["restored"]=enemy.CaptureSourceImpalement();
                    player.root.SetActive(false);
                    cases[cases.Count-1]["disabled_cleanup"]=new J(enemy.SourceImpalementStage==0&&!player.SourceWeaponImpaling("Near")&&!organ.MasksVisible);
                }
            }
        }finally{fixtureMode=false;PauseCampaign();}
        return J.O("cases",new J(cases),"entry","Physics2D.Simulate -> DuelSourceImpaleOrgan.OnTriggerEnter2D");
    }
    public sealed partial class Fighter {
        internal void FreezeSourceImpalementProbe(Collider2D candidate,Collider2D attackerShape,bool attacking){
            foreach(var rb in root.GetComponentsInChildren<Rigidbody2D>(true)){rb.constraints=RigidbodyConstraints2D.FreezeAll;rb.linearVelocity=Vector2.zero;rb.angularVelocity=0;}
            foreach(var collider in colliders)collider.enabled=collider==candidate||collider==attackerShape;
            if(!attacking)return;
            if(primaryGrip!=null)primaryGrip.enabled=false;if(supportJoint!=null)supportJoint.enabled=false;
            foreach(var body in referenceWeights.Values){body.simulated=false;var joint=body.GetComponent<FixedJoint2D>();if(joint!=null)joint.enabled=false;}
        }
        internal DuelSourceImpaleOrgan PrepareSourceImpalementProbe(Fighter attacker,string name){
            var organ=sourceOrgans["Ribcage"];health=100;
            AcceptReferenceImpact("torso");
            // This production damage function, rather than a private test
            // assignment, must feed the accumulated effective damage window.
            ApplyReferenceDamage("torso",(name=="too_little_damage"?1.99f:2)/.35f,organ.transform.position,DuelReferenceRules.Surface.MetalBlade,0);
            health=name=="healthy"?10.01f:10;
            if(name=="expired")game.Clock+=DuelReferenceRules.ImpactWindow+.0001f;
            if(name=="detached")severed.Add(organ.linked);
            if(name=="overkill_accumulates"){
                sourceAccumulatedDamage=0;health=1;
                ApplyReferenceDamage("torso",10,organ.transform.position,DuelReferenceRules.Surface.MetalBlade,0);
            }
            var material=name=="spike"?DuelReferenceRules.Surface.MetalSpike:name=="blunt"?DuelReferenceRules.Surface.MetalBlunt:name=="handle"?DuelReferenceRules.Surface.MetalHandle:name=="pommel"?DuelReferenceRules.Surface.SpikePommel:DuelReferenceRules.Surface.MetalBlade;
            attacker.BindSourceImpaleWeapon(attacker.sword.shape,attacker.sword,material);
            return organ;
        }
        internal J ProbeSourceImpalementPhases(Fighter attacker){
            var phases=new List<J>();
            void Record(){phases.Add(J.O("stage",sourceImpaleStage,"joint",attacker.SourceWeaponImpaling("Near"),"masks",sourceOrgans["Ribcage"].MasksVisible));}
            Record();
            var saved=CaptureSourceImpalement();RestoreSourceImpalement(saved);ResolveSourceImpalement();
            bool anchorsRestored=CaptureSourceImpalement().ToString()==saved.ToString();
            // The source drop/death operation changes item mass and grip, but
            // does not call EndImpalement. The constraint stays until its wait.
            attacker.DropReferenceWeight("Near");bool droppedRetains=attacker.SourceWeaponImpaling("Near");
            game.referenceSubstep=0;game.Clock=sourceImpaleAt+.0001f;TickSourceImpalement();Record();
            game.Clock=sourceImpaleAt+.0001f;TickSourceImpalement();Record();
            game.Clock=sourceImpaleAt+.0001f;TickSourceImpalement();Record();
            return J.O("states",new J(phases),"restored",anchorsRestored,"drop_retains",droppedRetains);
        }
    }
}
