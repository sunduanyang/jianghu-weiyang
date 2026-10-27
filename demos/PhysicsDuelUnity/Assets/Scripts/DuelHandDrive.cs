using System.Collections.Generic;
using UnityEngine;
using Jianghu;

public sealed partial class PhysicsDuel {
    public sealed partial class Fighter {
        public FixedJoint2D primaryGrip;
        public readonly Dictionary<string,TargetJoint2D> handTargets=new Dictionary<string,TargetJoint2D>();
        public Vector2 primaryIntent,secondaryIntent;
        public float PrimaryGripError=>primaryGrip==null||!primaryGrip.enabled?0:Vector2.Distance(Limb.BodyPoint(sword.rb,primaryGrip.anchor),Limb.BodyPoint(primaryGrip.connectedBody,primaryGrip.connectedAnchor));
        public float PrimaryGripAngleError=>Mathf.Abs(Mathf.DeltaAngle(sword.rb.rotation-limbs["handNear"].rb.rotation,-primaryGrip.referenceAngle));
        public float HandTargetError(string side)=>Vector2.Distance(limbs["hand"+side].A,handTargets[side].target);
        public J HandPoseState(string side) {
            Limb a=limbs["upper"+side],f=limbs["forearm"+side],h=limbs["hand"+side];
            Vector2 t=handTargets[side].target;
            return J.O("upper_angle",a.rb.rotation,"forearm_angle",f.rb.rotation,"hand_angle",h.rb.rotation,"weapon_angle",sword.rb.rotation,"hand_motor_goal",h.targetAngle,"wrist_relative_angle",Mathf.DeltaAngle(f.rb.rotation,h.rb.rotation),"wrist_joint_angle",h.joint.jointAngle,"wrist_reference",h.joint.referenceAngle,"wrist_min",h.joint.limits.min,"wrist_max",h.joint.limits.max,"primary_fixed_reference",primaryGrip.referenceAngle,"primary_grip_angle_error",PrimaryGripAngleError,"hand_inertia",h.rb.inertia,"weapon_inertia",sword.rb.inertia,"wrist_x",h.A.x,"wrist_y",h.A.y,"target_x",t.x,"target_y",t.y,"target_error",HandTargetError(side),"carry_angle",carryAngle);
        }
        void CreateHandTargets() {
            // The small palm collider has hundreds of times less rotational
            // inertia than a long weapon. A rigid grip needs a conditioned
            // inertia ratio for the iterative angular constraints to converge.
            float load=Mathf.Max(sword.rb.inertia,weapon.mass*weapon.Length*weapon.Length/12);
            limbs["handNear"].rb.inertia=Mathf.Max(limbs["handNear"].rb.inertia,load*.35f);
            if(weapon.TwoHanded)limbs["handFar"].rb.inertia=Mathf.Max(limbs["handFar"].rb.inertia,load*.35f);
            foreach(string side in new[]{"Near","Far"}) {
                Limb hand=limbs["hand"+side],fore=limbs["forearm"+side];
                // Pull the wrist on the forearm side of its hinge. Putting a
                // mouse joint on the tiny palm also damps the grip's rotation.
                var target=fore.rb.gameObject.AddComponent<TargetJoint2D>();
                target.autoConfigureTarget=false;target.anchor=Limb.BodyLocalPoint(fore.rb,hand.A);
                target.target=hand.A;target.frequency=32;target.dampingRatio=1;target.maxForce=0;
                handTargets[side]=target;
            }
        }
        void DriveHand(string side,Vector2 wrist,float force,float speed,bool sharedPose=false) {
            Limb upper=limbs["upper"+side],fore=limbs["forearm"+side];
            // The endpoint supplies the main effort. A small angular posture
            // bias keeps the elbow on its anatomical branch under grip torque;
            // it is much weaker than the former 32-55 Nm arm servos.
            Solve(upper.A,wrist,upper.length,fore.length,-facing,out Vector2 elbow,out Vector2 tip,.002f);
            float power=Mathf.Min(1.12f,force/(side=="Near"?165:150));
            upper.Drive(Angle(elbow-upper.A),4,8,power,speed);
            fore.Drive(Angle(tip-elbow),4,6,power,speed);
            TargetJoint2D target=handTargets[side];target.enabled=true;
            target.target=sharedPose?wrist:Vector2.MoveTowards(target.target,wrist,Time.fixedDeltaTime*7*Mathf.Clamp(speed,.15f,1.2f));
            target.maxForce=force;
        }
        void RelaxHandDrives() {
            foreach(var target in handTargets.Values)target.enabled=false;
            if(primaryGrip!=null)primaryGrip.enabled=false;
            if(supportJoint!=null)supportJoint.enabled=false;
        }
    }
}
