using System;
using System.Collections.Generic;
using System.Linq;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    // Read-only runtime evidence for 2D native joint anchors versus a source
    // hierarchy containing Y-axis rotations and signed three-dimensional scale.
    // This does not change anchors, solver limits, transforms or acceptance bounds.
    public J ProbeSourceJointMetrics(){
        if(!IsSmoke)throw new InvalidOperationException("Source joint diagnostics require isolated acceptance");
        return J.O("time",Clock,"player",player.SourceJointMetrics(),"enemy",enemy.SourceJointMetrics());
    }
    public sealed partial class Fighter {
        internal J SourceJointMetrics(){
            var rows=new List<J>();if(sourceRig==null)return J.O("active",false,"joints",rows);
            var connectedToBody=SourceLiveBodies();
            float maxTransform=0,maxPhysics=0,maxScaled=0;
            J Vec(Vector2 v)=>J.O("x",v.x,"y",v.y);
            J Vec3(Vector3 v)=>J.O("x",v.x,"y",v.y,"z",v.z);
            J Body(Rigidbody2D rb){
                if(rb==null)return new J();var t=rb.transform;Quaternion q=t.rotation;
                return J.O("name",rb.name,"position",Vec(rb.position),"angle",rb.rotation,"world_position",Vec3(t.position),"world_quaternion",J.O("x",q.x,"y",q.y,"z",q.z,"w",q.w),"local_scale",Vec3(t.localScale),"lossy_scale",Vec3(t.lossyScale),"transform_x",Vec3(t.TransformVector(Vector3.right)),"transform_y",Vec3(t.TransformVector(Vector3.up)),"physics_x",Vec(rb.GetRelativeVector(Vector2.right)),"physics_y",Vec(rb.GetRelativeVector(Vector2.up)),"active",rb.gameObject.activeInHierarchy,"simulated",rb.simulated);
            }
            foreach(var pair in sourceRig.Components){
                if(!(pair.Value is AnchoredJoint2D joint)||!joint.enabled)continue;
                var own=joint.attachedRigidbody;var connected=joint.connectedBody;
                if(own==null)continue;
                Vector2 anchor=joint.anchor,remote=joint.connectedAnchor;
                Vector2 ownTransform=joint.transform.TransformPoint(anchor),otherTransform=connected!=null?(Vector2)connected.transform.TransformPoint(remote):remote;
                Vector2 ownPhysics=own.GetRelativePoint(anchor),otherPhysics=connected!=null?connected.GetRelativePoint(remote):remote;
                Vector2 ownScale=own.transform.lossyScale,otherScale=connected!=null?(Vector2)connected.transform.lossyScale:Vector2.one;
                Vector2 ownScaled=own.GetRelativePoint(Vector2.Scale(anchor,ownScale)),otherScaled=connected!=null?connected.GetRelativePoint(Vector2.Scale(remote,otherScale)):remote;
                maxTransform=Mathf.Max(maxTransform,Vector2.Distance(ownTransform,otherTransform));maxPhysics=Mathf.Max(maxPhysics,Vector2.Distance(ownPhysics,otherPhysics));maxScaled=Mathf.Max(maxScaled,Vector2.Distance(ownScaled,otherScaled));
                J raw=new J();foreach(var r in sourceRig.Data["physics"].A)if(r["id"].I==pair.Key){raw=r["data"];break;}
                var row=J.O("id",pair.Key,"name",joint.name,"own",Body(own),"connected",Body(connected),"anchor",Vec(anchor),"connected_anchor",Vec(remote),
                    "in_live_component",InSourceLiveComponent(joint,connectedToBody),
                    "transform_own",Vec(ownTransform),"transform_connected",Vec(otherTransform),"transform_gap",Vector2.Distance(ownTransform,otherTransform),
                    "physics_own",Vec(ownPhysics),"physics_connected",Vec(otherPhysics),"physics_gap",Vector2.Distance(ownPhysics,otherPhysics),
                    "scaled_physics_own",Vec(ownScaled),"scaled_physics_connected",Vec(otherScaled),"scaled_physics_gap",Vector2.Distance(ownScaled,otherScaled),
                    "reaction_force",Vec(joint.CombatReactionForce()),"reaction_torque",joint.CombatReactionTorque(),"raw",raw);
                if(joint is HingeJoint2D hinge){row["joint_angle"]=new J(hinge.CombatJointAngle());row["reference_angle"]=new J(hinge.CombatReferenceAngle());row["limits"]=J.O("min",hinge.limits.min,"max",hinge.limits.max);row["use_limits"]=new J(hinge.useLimits);row["native_limit_state"]=new J(hinge.limitState.ToString());row["measured_interval"]=J.O("low",Mathf.Min(hinge.limits.min,hinge.limits.max),"high",Mathf.Max(hinge.limits.min,hinge.limits.max));row["legacy_limit_error"]=new J(LimitExcess(hinge.CombatJointAngle(),hinge.limits.min,hinge.limits.max,false));row["source_limit_error"]=new J(MeasuredLimitError(hinge));}
                if(joint is FixedJoint2D fixedJoint){row["frequency"]=new J(fixedJoint.frequency);row["damping_ratio"]=new J(fixedJoint.dampingRatio);}
                rows.Add(row);
            }
            return J.O("active",true,"source",sourceRig.SourceId,"health",health,"stamina",stamina,"passed_out",PassedOut,"kicking",ReferenceKicking,"severed",new J(severed.Select(k=>new J(k)).ToList()),"joints",rows,"maximum_transform_gap",maxTransform,"maximum_physics_gap",maxPhysics,"maximum_scaled_physics_gap",maxScaled,"source_skeleton_gap",SourceSkeletonGap(),"maximum_joint_gap",MaxJointGap(),"primary_grip_error",PrimaryGripError,"support_grip_error",SupportGripError);
        }
    }
}
