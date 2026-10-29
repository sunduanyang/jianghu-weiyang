using System;
using System.Collections.Generic;
using System.Linq;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    public J ProbeSourceInjuryDeath(){
        if(!IsSmoke)throw new InvalidOperationException("Source injury acceptance requires isolated save");
        var injuries=new List<J>();
        try{
            foreach(string name in new[]{"cut_arm","pierced_arm","bashed_arm","cut_leg","cut_neck"}){
                Clock=elapsed=1;ResetRound();RunCampaign(true);fixtureMode=true;
                string part=name=="cut_leg"?"thighFar":name=="cut_neck"?"neck":name=="bashed_arm"?"upperNear":name=="pierced_arm"?"forearmFar":"forearmNear";
                var surface=name=="pierced_arm"?DuelReferenceRules.Surface.MetalSpike:name=="bashed_arm"?DuelReferenceRules.Surface.MetalBlunt:DuelReferenceRules.Surface.MetalBlade;
                enemy.DamageSourcePartForProbe(part,surface);
                var before=enemy.ProbeSourceInjuryInfo(part);
                var saved=J.Parse(CaptureCampaign().ToString());ResetRound();RestoreCampaign(saved);
                injuries.Add(J.O("case",name,"part",part,"before",before,"restored",enemy.ProbeSourceInjuryInfo(part),"saved",saved,"roundtrip",CaptureCampaign()));
            }
            Clock=elapsed=1;ResetRound();RunCampaign(true);fixtureMode=true;
            var death=enemy.ProbeSourceExactDeathStages();
            return J.O("injuries",new J(injuries),"death",death);
        }finally{fixtureMode=false;PauseCampaign();}
    }
    public sealed partial class Fighter {
        internal void DamageSourcePartForProbe(string key,DuelReferenceRules.Surface material){
            if(!SourceRigActive)throw new InvalidOperationException("Source injury probe requires the imported graph");
            health=key=="neck"?60:100;
            AcceptReferenceImpact(key);ApplyReferenceDamage(key,(float)sourceParts[key]["MaxHealth"].N+1,referenceBones[key].rb.position,material,0);
        }
        internal J ProbeSourceInjuryInfo(string key){
            var data=sourceParts[key];var body=referenceBones[key].rb;
            var cut=sourceRig.Resolve<Joint2D>(data["joint2D"]);
            var related=new List<J>();
            foreach(var reference in data["relatedBodyParts"].A)if(sourcePartIds.TryGetValue(DuelSourceRig.Id(reference),out var child)){
                var rb=referenceBones[child].rb;
                related.Add(J.O("key",child,"health",partHealth[child],"severed",severed.Contains(child),"mass",rb.mass,"drag",rb.linearDamping,"angular_drag",rb.angularDamping,"layer",rb.gameObject.layer));
            }
            var rawMaterial=sourceRig.Material(data["deadPhysicsMaterial"]);
            return J.O("mode",(int)referencePartModes[key],"health",health,"local_health",partHealth[key],"severed",severed.Contains(key),
                "mass",body.mass,"drag",body.linearDamping,"angular_drag",body.angularDamping,"layer",body.gameObject.layer,"gravity",body.gravityScale,
                "source_cut_joint_id",DuelSourceRig.Id(data["joint2D"]),"cut_joint_owner",cut==null?"":cut.gameObject.name,"cut_joint_enabled",cut!=null&&cut.enabled,
                "own_hinge_enabled",referenceBones[key].joint!=null&&referenceBones[key].joint.enabled,
                "parent",body.transform.parent==null?"":body.transform.parent.name,"retained_by_bout",body.transform.IsChildOf(root.transform),
                "dead_material",body.sharedMaterial==rawMaterial,"related",new J(related),
                "destroy_refs",SourceObjectFlagsForProbe(data["objectsToDestroy"]),"activate_refs",SourceObjectFlagsForProbe(data["objectsToActivate"]),
                "source_snapshot",CaptureSourceRig());
        }
        J SourceObjectFlagsForProbe(J refs)=>new J(refs.A.Select(reference=>{var t=sourceRig.ResolveTransform(reference);return J.O("id",DuelSourceRig.Id(reference),"exists",t!=null,"active",t!=null&&t.gameObject.activeSelf);}).ToList());
        internal J ProbeSourceExactDeathStages(){
            var records=new List<J>();game.referenceSubstep=0;health=30;
            AcceptReferenceImpact("torso");ApplyReferenceDamage("torso",100,torso.rb.position,DuelReferenceRules.Surface.MetalBlunt,0);TickReferenceDeath();
            void Record(){
                var vitality=sourceRig.Source("CharacterVitality");
                var joints=new List<J>();foreach(var reference in vitality["bodyFixedJoints2D"].A){var joint=sourceRig.Resolve<FixedJoint2D>(reference);joints.Add(J.O("id",DuelSourceRig.Id(reference),"enabled",joint!=null&&joint.enabled,"frequency",joint==null?-1:joint.frequency,"damping",joint==null?-1:joint.dampingRatio));}
                var killed=new List<J>();foreach(var reference in vitality["fixedJoints2DToKill"].A){var joint=sourceRig.Resolve<Joint2D>(reference);killed.Add(J.O("id",DuelSourceRig.Id(reference),"enabled",joint!=null&&joint.enabled));}
                var bodies=new List<J>();foreach(var reference in vitality["bodyParts"].A)if(sourcePartIds.TryGetValue(DuelSourceRig.Id(reference),out var key)){var rb=referenceBones[key].rb;var data=sourceParts[key];bodies.Add(J.O("key",key,"mass",rb.mass,"drag",rb.linearDamping,"angular_drag",rb.angularDamping,"gravity",rb.gravityScale,"dead_mass",(float)data["deadMass"].N,"dead_drag",(float)data["deadDrag"].N,"dead_angular_drag",(float)data["deadAngularDrag"].N));}
                var kick=sourceRig.Resolve<Collider2D>(vitality["extraColliderKick"]);var overlap=sourceRig.Resolve<Collider2D>(vitality["overlapCollider"]);
                records.Add(J.O("stage",referenceDeathStage,"body_type",(int)mover.bodyType,"root_layer",sourceRig.Root.layer,"kick",kick!=null&&kick.enabled,"overlap",overlap!=null&&overlap.enabled,
                    "extra",SourceObjectFlagsForProbe(vitality["ExtraColliders"]),"vapor",SourceObjectFlagsForProbe(new J(new List<J>{vitality["vaporContainer"]})),
                    "corpse",referenceCorpseBody.gameObject.activeSelf,"body_joints",new J(joints),"killed_joints",new J(killed),"bodies",new J(bodies),
                    "deactivate",SourceObjectFlagsForProbe(vitality["gameObjectsToDeactivateUponDeath"]),"activate",SourceObjectFlagsForProbe(vitality["gameObjectsToActivateUponDeath"])));
            }
            Record();for(int stage=2;stage<=7;stage++){game.Clock=referenceDeathAt+.0001f;TickReferenceDeath();Record();}
            return J.O("stages",new J(records),"snapshot",CaptureSourceRig());
        }
    }
}
