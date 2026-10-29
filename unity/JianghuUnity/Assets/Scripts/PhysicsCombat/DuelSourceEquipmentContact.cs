using System;
using System.Collections.Generic;
using System.Linq;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    // ReferenceContact delegates here. This is the shared formal entry for
    // sparring, robbery and assault; the separate preview is not involved.
    void SourceEquipmentContact(DuelContact weapon,DuelContact other,Collision2D collision){
        if(referenceBindProbe){if(referenceProbeCollecting&&weapon.owner==player&&weapon.limb==player.sword&&other.owner==enemy&&(referenceProbeBody?other.kind!="sword"&&other.kind!="shield":other.limb==enemy.sword))referenceProbeContactEvents++;return;}
        if(!Live||weapon.owner==other.owner||collision.contactCount==0||weapon.owner.health<=0)return;
        if(weapon.kind!="sword"&&weapon.kind!="shield")return;
        var c=collision.GetContact(0);Vector2 velocity=c.relativeVelocity;float speed=velocity.magnitude;
        if(other.kind=="sword"||other.kind=="shield"){if(speed>15&&Clock-weapon.owner.lastClash>.1f){weapon.owner.lastClash=Clock;blocks++;Impact(c.point,true,speed);}return;}
        var spec=weapon.limb.strikeProfile??weapon.owner.weapon;
        bool shield=weapon.kind=="shield"&&weapon.owner.HasShield;
        bool handle=collision.otherCollider!=weapon.limb.shape;
        var material=DuelSourceEquipment.Surface(spec,handle,shield);
        if(spec.id=="unarmed"&&!weapon.owner.SourceNothingActive)material=DuelReferenceRules.Surface.MetalBlunt;
        if(speed<DuelSourceEquipment.SpeedThreshold(spec,handle,shield,weapon.owner.SourceNothingActive)){rejectedRestContacts++;return;}
        if(!other.owner.AcceptReferenceImpact(other.limb.key))return;
        var armor=DuelSourceEquipment.Armor(other.owner.SourceEquipmentPacket,other.limb.key);
        var damage=DuelSourceEquipment.CalculateDamage(spec,velocity,armor,material,shield,weapon.owner.damageScale,weapon.owner.SourceNothingActive);
        float dealt=other.owner.ApplyReferenceDamage(other.limb.key,damage.local,c.point,material,damage.knockback);
        if(dealt<=0)return;hits++;weapon.owner.landedHits++;weapon.owner.lastHit=Clock;strongestImpact=Mathf.Max(strongestImpact,speed);if(other.kind=="head")headHits++;
        Impact(c.point,false,dealt);floating.Add(new FloatingText{p=c.point,value=Mathf.CeilToInt(dealt).ToString(),life=.85f,color=vermilion});
    }
    public sealed partial class Fighter {
        internal J SourceEquipmentPacket=>game.Campaign?game.campaign.battle["fighters"][human?0:1]:new J();
        public void BindEquipmentImpaleMaterials(){
            void Bind(Limb item,string slot,bool isShield){
                if(item==null)return;var spec=item.strikeProfile??weapon;
                foreach(var shape in item.Frame.GetComponents<Collider2D>())BindSourceImpaleWeapon(shape,item,DuelSourceEquipment.Surface(spec,shape!=item.shape,isShield),slot);
            }
            Bind(sword,"Near",false);if(!weapon.TwoHanded)Bind(shield,"Far",HasShield);
        }
        // CharacterConfigApplier.ApplyHandItem{Left,Right}/ApplyDoubleHandedItem
        // and cez 0x3421ed8. Operates on exact source refs, never guesses limbs.
        public void ConfigureSourceEquipmentRig(){
            if(sourceRig==null)return;
            J config=sourceRig.Source("CharacterConfigApplier");
            foreach(string side in new[]{"L","R"}){
                // Source Nothing has Type=7 even though AI filters its name.
                // An empty off hand still follows the SHIELD slot branch.
                bool shieldSide=!weapon.TwoHanded&&(SourceNothingActive?(side=="R"||weapon.id=="unarmed"):side=="R"&&HasShield);
                string driver=side=="L"?"handLeftCOMRigidbody2D":"handRightCOMRigidbody2D";
                var rb=sourceRig.Resolve<Rigidbody2D>(sourceMover[driver]);
                if(rb!=null)rb.mass=(float)config[shieldSide?"handCOMShieldMass":"handCOMWeaponMass"].N;
                foreach(var reference in config[side=="L"?"armLeftRidibodies":"armRightRidibodies"].A){var arm=sourceRig.Resolve<Rigidbody2D>(reference);if(arm!=null)arm.linearDamping=arm.angularDamping=shieldSide?3:5;}
                var hinge=sourceRig.Resolve<HingeJoint2D>(config["handHingeJoint2D"+side]);
                int dummy=DuelSourceRig.Id(config["handHingeJoint2D"+side+(weapon.TwoHanded?"_Dummy_TwoHanded":"_Dummy_OneHanded")]);
                var raw=sourceRig.Data["physics"].A.FirstOrDefault(p=>p["id"].I==dummy);
                if(hinge!=null&&raw!=null){J limits=raw["data"]["m_AngleLimits"];hinge.limits=new JointAngleLimits2D{min=(float)limits["m_LowerAngle"].N,max=(float)limits["m_UpperAngle"].N};}
            }
        }
        // Native build-2 evidence: source Qy180 / signed scale means neither
        // full 3D TransformPoint nor unscaled 2D rotation maps joint anchors.
        // GetRelativePoint consumes the signed lossyScale-adjusted 2D anchor.
        // This changes measurement only; the actual joints remain untouched.
        static Vector2 SourceAnchorPoint(Rigidbody2D body,Vector2 anchor)=>body==null?anchor:body.GetRelativePoint(Vector2.Scale(anchor,(Vector2)body.transform.lossyScale));
        float MeasuredJointGap(AnchoredJoint2D joint){
            if(joint==null||!joint.enabled)return 0;
            Vector2 a=SourceRigActive?SourceAnchorPoint(joint.attachedRigidbody,joint.anchor):Limb.BodyPoint(joint.attachedRigidbody,joint.anchor);
            Vector2 b=SourceRigActive?SourceAnchorPoint(joint.connectedBody,joint.connectedAnchor):joint.connectedBody!=null?Limb.BodyPoint(joint.connectedBody,joint.connectedAnchor):joint.connectedAnchor;
            return Vector2.Distance(a,b);
        }
        HashSet<Rigidbody2D> SourceLiveBodies(){
            var connected=new HashSet<Rigidbody2D>();if(health<=0)return connected;connected.Add(pelvis.rb);
            var joints=root.GetComponentsInChildren<Joint2D>().Where(j=>j!=null&&j.enabled&&j.attachedRigidbody!=null&&j.attachedRigidbody.simulated&&j.connectedBody!=null&&j.connectedBody.simulated&&j.connectedBody.gameObject.activeInHierarchy).ToArray();
            bool changed;do{changed=false;foreach(var joint in joints){if(connected.Contains(joint.attachedRigidbody))changed|=connected.Add(joint.connectedBody);if(connected.Contains(joint.connectedBody))changed|=connected.Add(joint.attachedRigidbody);}}while(changed);
            return connected;
        }
        static bool InSourceLiveComponent(Joint2D joint,HashSet<Rigidbody2D> connected)=>joint!=null&&connected.Contains(joint.attachedRigidbody)&&(joint.connectedBody==null||connected.Contains(joint.connectedBody));
        public float SourceSkeletonGap(){var connected=SourceLiveBodies();float gap=0;foreach(var limb in referenceBones.Values)if(InSourceLiveComponent(limb.joint,connected))gap=Mathf.Max(gap,MeasuredJointGap(limb.joint));return gap;}
        public float SourceCounterweightGap(){float gap=0;foreach(var rb in referenceWeights.Values)if(rb.simulated)gap=Mathf.Max(gap,MeasuredJointGap(rb.GetComponent<FixedJoint2D>()));return gap;}
        // Source mirrored hinges retain serialized endpoint order (for example
        // feet 5,-55) while the native jointAngle lives between the endpoints.
        // Sort ONLY the measured interval; never rewrite the actual limits.
        internal static float LimitExcess(float angle,float first,float second,bool sourceRig){
            float low=sourceRig?Mathf.Min(first,second):first,high=sourceRig?Mathf.Max(first,second):second;
            return Mathf.Max(0,low-angle,angle-high);
        }
        float MeasuredLimitError(HingeJoint2D joint)=>LimitExcess(joint.jointAngle,joint.limits.min,joint.limits.max,SourceRigActive);
        public void DrawSourceRig(bool debug){
            foreach(var pair in referenceBones){
                var l=pair.Value;Color color=human?new Color(.35f,.53f,.51f):new Color(.64f,.38f,.30f);if(severed.Contains(pair.Key))color*=.65f;if(game.Clock<hitTint)color=new Color(.9f,.36f,.24f);
                float width=l.shape==null?.1f:l.shape is CapsuleCollider2D capsule?Mathf.Min(capsule.size.x,capsule.size.y)*Mathf.Abs(capsule.transform.lossyScale.x):l.shape is CircleCollider2D circle?circle.radius*2*Mathf.Abs(circle.transform.lossyScale.x):l.shape is BoxCollider2D box?Mathf.Min(box.size.x,box.size.y)*Mathf.Abs(box.transform.lossyScale.x):Mathf.Min(l.shape.bounds.size.x,l.shape.bounds.size.y);
                width=Mathf.Max(.025f,width)*Unit;game.Line(ScreenPoint(l.A),ScreenPoint(l.B),width,color);game.Circle(ScreenPoint(l.A),Vector2.one*width,color);game.Circle(ScreenPoint(l.B),Vector2.one*width,color);if(debug&&l.shape!=null)DrawCollider(l.shape);
            }
            if(shield!=null&&HasShield)DrawWeapon(shield,true);if(weapon.id!="unarmed")DrawWeapon(sword,false);
            if(debug)foreach(var hand in referenceHands.Values)game.Circle(ScreenPoint(hand.target),Vector2.one*9,new Color(.9f,.7f,.1f,.8f));
        }
    }
}
