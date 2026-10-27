using UnityEngine;

public sealed partial class PhysicsDuel {
    public sealed partial class Fighter {
        public FixedJoint2D supportJoint;
        public float gripSpacing;
        Vector2 lastTwoHandWrist;
        bool hasTwoHandPose;
        bool hasCarryPose;
        float carryAngle;
        Vector2 carryWrist;
        float poseFloor;
        Vector2 ReadyDirection=>new Vector2(facing*Mathf.Cos(weapon.readyAngle*Mathf.Deg2Rad),Mathf.Sin(weapon.readyAngle*Mathf.Deg2Rad));
        Vector2 InitialTwoHandWrist(string side,Vector2 waist) {
            Vector2 primary=waist+Local(skin["bind_joints"]["shoulderNear"])+new Vector2(facing*.62f,-.20f);
            return side=="Near"?primary:primary+ReadyDirection*weapon.supportGrip;
        }
        public Vector2 SupportGripPoint=>sword.WorldPoint(weapon.Local(new Vector2(0,gripSpacing),facing));
        public float SupportGripError=>supportJoint==null?0:Vector2.Distance(limbs["handFar"].B,SupportGripPoint);
        void AttachSupportHand() {
            gripSpacing=weapon.supportGrip;
            Limb hand=limbs["handFar"];
            supportJoint=hand.rb.gameObject.AddComponent<FixedJoint2D>();
            supportJoint.connectedBody=sword.rb;supportJoint.autoConfigureConnectedAnchor=false;
            supportJoint.anchor=Limb.BodyLocalPoint(hand.rb,hand.B);
            supportJoint.connectedAnchor=Limb.BodyLocalPoint(sword.rb,SupportGripPoint);
            supportJoint.frequency=0;supportJoint.enableCollision=false;
        }
        static float ArmMinimum(Limb upper,Limb lower)=>Mathf.Sqrt(upper.length*upper.length+lower.length*lower.length+2*upper.length*lower.length*Mathf.Cos(132*Mathf.Deg2Rad));
        static Vector2 ReachBand(Vector2 p,Vector2 origin,float minimum,float maximum,Vector2 fallback) {
            Vector2 delta=p-origin;float length=delta.magnitude;
            return origin+(length>.0001f?delta/length:fallback)*Mathf.Clamp(length,minimum,maximum);
        }
        bool TwoHandPoseFits(Vector2 wrist,Vector2 axis,float separation) {
            if(wrist.y<poseFloor-.004f)return false;
            return HandPoseFits(true,wrist,axis)&&HandPoseFits(false,wrist+axis*separation,axis);
        }
        bool HandPoseFits(bool near,Vector2 target,Vector2 axis) {
                Limb a=limbs[near?"upperNear":"upperFar"],b=limbs[near?"forearmNear":"forearmFar"];
                float length=Vector2.Distance(target,a.A);
                if(length<ArmMinimum(a,b)-.004f||length>a.length+b.length-.015f)return false;
                Solve(a.A,target,a.length,b.length,-facing,out Vector2 elbow,out Vector2 tip,.002f);
                float wristAngle=Mathf.DeltaAngle(Angle(tip-elbow),Angle(axis))*facing;
                if(wristAngle< -83||wristAngle>63)return false;
            return true;
        }
        Vector2 SharedWristPose(Vector2 desired,Vector2 axis,float separation) {
            Limb near=limbs["upperNear"],nearFore=limbs["forearmNear"],far=limbs["upperFar"],farFore=limbs["forearmFar"];
            Vector2 a=near.A,b=far.A-axis*separation;
            float minA=ArmMinimum(near,nearFore)+.005f,maxA=near.length+nearFore.length-.025f;
            float minB=ArmMinimum(far,farFore)+.005f,maxB=far.length+farFore.length-.025f;
            float hand=limbs["handNear"].length;
            float lowest=axis.y*(hand-weapon.tail)-Mathf.Abs(axis.x)*.04f;
            foreach(var point in weapon.head)lowest=Mathf.Min(lowest,axis.y*(hand+point.y)-axis.x*point.x*facing);
            poseFloor=.10f-lowest;
            Vector2 p=desired;
            for(int i=0;i<12;i++){p=ReachBand(p,a,minA,maxA,Vector2.right*facing);p=ReachBand(p,b,minB,maxB,Vector2.right*facing);p.y=Mathf.Max(p.y,poseFloor);}
            if(TwoHandPoseFits(p,axis,separation)){lastTwoHandWrist=p;hasTwoHandPose=true;return p;}
            // At the rear of a full rotation the reachable annuli can require
            // a lateral elbow offset. Search compatible poses, never move a body.
            Vector2 best=hasTwoHandPose?lastTwoHandWrist:p;float bestCost=float.PositiveInfinity;
            for(int ring=0;ring<7;ring++)for(int i=0;i<64;i++) {
                float angle=i*Mathf.PI/32,radius=Mathf.Lerp(minA,maxA,ring/6f);
                Vector2 candidate=a+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;
                if(!TwoHandPoseFits(candidate,axis,separation))continue;
                float cost=(candidate-desired).sqrMagnitude+(hasTwoHandPose?(candidate-lastTwoHandWrist).sqrMagnitude*.3f:0);
                if(cost<bestCost){best=candidate;bestCost=cost;}
            }
            if(float.IsPositiveInfinity(bestCost))best=p;
            lastTwoHandWrist=best;hasTwoHandPose=true;return best;
        }
        void ControlTwoHandArms(Vector2 desired,Vector2 axis,bool manual) {
            // Both pointers contribute a two-dimensional hand intention. The
            // authored grip spacing remains fixed on this weapon.
            if(ShieldWheel) {
                Vector2 support=MouseOff?limbs["upperFar"].A+OffStick*MouseReach(false):desired+axis*gripSpacing+OffStick*.42f;
                primaryIntent=desired;secondaryIntent=support;
                Vector2 lever=support-desired;
                axis=lever.sqrMagnitude>.0064f?lever.normalized:axis;
                desired=(desired+support)*.5f-axis*(gripSpacing*.5f);
            } else {primaryIntent=desired;secondaryIntent=desired+axis*gripSpacing;}
            if(!hasCarryPose){carryAngle=sword.rb.rotation+90;carryWrist=limbs["handNear"].A;hasCarryPose=true;}
            carryAngle=Mathf.LerpAngle(carryAngle,Mathf.Atan2(axis.y,axis.x)*Mathf.Rad2Deg,HandInputBlend);
            // A heavy or blocked weapon must catch up before its two wrist
            // targets advance around it. Otherwise the endpoint forces pull
            // through opposite sides of the wrist limits during a turn.
            float actualAxis=sword.rb.rotation+90;
            carryAngle=actualAxis+Mathf.Clamp(Mathf.DeltaAngle(actualAxis,carryAngle),-35,35);
            axis=new Vector2(Mathf.Cos(carryAngle*Mathf.Deg2Rad),Mathf.Sin(carryAngle*Mathf.Deg2Rad));
            float separation=gripSpacing+limbs["handNear"].length-limbs["handFar"].length;
            Vector2 wanted=SharedWristPose(desired,axis,separation);
            carryWrist=Vector2.Lerp(carryWrist,wanted,HandInputBlend);
            Vector2 primary=SharedWristPose(carryWrist,axis,separation),secondary=primary+axis*separation;
            float gripError=Mathf.Max(PrimaryGripError,SupportGripError);
            float loadYield=Mathf.Lerp(1,.15f,Mathf.InverseLerp(.025f,.07f,gripError));
            float power=ArmPower*loadYield,braced=shieldControl||ShieldWheel?1.12f:1;
            // Keep the two targets a rigid pair. Smoothing each arm's radius
            // independently would ask a fixed weapon to change its grip span.
            DriveHand("Near",primary,220*power,ArmSpeed,true);DriveHand("Far",secondary,220*power*braced,ArmSpeed,true);
            // Solve the wrist muscles with the grip / contact constraints.
            // Separate external torques fight the endpoint pull at a blocked
            // shaft and leave a long weapon pointing above the intended line.
            limbs["handNear"].Drive(Angle(axis),6,12,power,ArmSpeed);
            limbs["handFar"].Drive(Angle(axis),6,12,power,ArmSpeed);
            if(shieldControl||ShieldWheel)action="双手架杆";
            if(game.Clock<guardBrokenUntil)action="双手卸力";
        }
    }
}
