using System;
using System.Collections.Generic;
using System.Linq;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    // Old checkpoints retain their old graph. New bouts use the recovered
    // graph; the two graphs are never restored on top of one another.
    bool UseSourceRig=>!(IsSmoke&&Arg("-referenceLegacyRig")=="1")&&(!Campaign||!campaign.battle["frame"].Has||campaign.battle["frame"]["fighters"][0]["reference"]["source_rig"].Has);
    public sealed partial class Fighter {
        DuelSourceRig sourceRig;
        J sourceMover;
        readonly Dictionary<string,J> sourceParts=new Dictionary<string,J>();
        readonly Dictionary<int,string> sourcePartIds=new Dictionary<int,string>();
        readonly Dictionary<string,HingeJoint2D> sourceCutJoints=new Dictionary<string,HingeJoint2D>();
        readonly Dictionary<string,Transform> sourcePoints=new Dictionary<string,Transform>();
        readonly Dictionary<string,Vector3> sourceRestPoints=new Dictionary<string,Vector3>();
        readonly HashSet<int> sourceSnapshotPoints=new HashSet<int>();
        readonly Dictionary<Rigidbody2D,float> sourceRotationStarts=new Dictionary<Rigidbody2D,float>();
        public bool SourceRigActive=>sourceRig!=null;
        public bool SourceNothingActive {get;private set;}
        public string SourceRigTemplate=>sourceRig?.SourceId??"";
        J sourceRigValidation;
        J sourceRigRestorePending;
        bool SourceMountStudy=>game.IsSmoke&&Arg("-referenceSourceMounts")=="1";
        static J sourceMountStudy;
        Vector2 SourceMountDelta(DuelWeapon spec,bool isShield,string field){
            if(sourceMountStudy==null)sourceMountStudy=J.Parse(Resources.Load<TextAsset>("CombatSource/mount-study").text);
            J p=sourceMountStudy["samples"][isShield?"shield":spec.id][field];return new Vector2(facing*(float)p["x"].N,(float)p["y"].N);
        }
        public void ReleaseSourceRig(){ClearSourceImpalementLifecycle();sourceRig?.DestroyMaterials();}
        static string SourcePartKey(string name){
            if(name=="_Bone_Pelvis")return "pelvis";if(name=="Bone_Belly")return "belly";if(name=="Bone_Chest")return "torso";
            if(name=="Bone_Head")return "head";if(name=="Bone_Neck")return "neck";
            string side=name.EndsWith("L")?"Near":"Far";
            return name.StartsWith("Bone_Arm")?"upper"+side:name.StartsWith("Bone_Forearm")?"forearm"+side:name.StartsWith("Bone_Hand")?"hand"+side:name.StartsWith("Bone_Thigh")?"thigh"+side:name.StartsWith("Bone_Calf")?"shin"+side:name.StartsWith("Bone_Foot")?"foot"+side:"";
        }
        void BuildSourceRig(float x){
            Reference=true;heightScale=bodyMassScale=armSpanScale=1;
            if(!game.Campaign){health=maxHealth=100;stamina=maxStamina=100;}
            sourceRig=DuelSourceRig.Create(root.transform,human,x);sourceMover=sourceRig.Source("CharacterMover");
            SourceNothingActive=!game.Campaign||!game.campaign.battle["frame"].Has||game.campaign.battle["frame"]["fighters"][human?0:1]["reference"]["source_equipment"]["version"].I==1;
            if(game.IsSmoke)sourceRigValidation=sourceRig.ValidateSource();
            foreach(var row in sourceRig.Controllers["BodyPart"].A){
                J data=row["data"];var rb=sourceRig.Resolve<Rigidbody2D>(data["thisRigidbody2D"]);string key=SourcePartKey(rb.name);
                if(key.Length==0)throw new InvalidOperationException("Unknown recovered body "+rb.name);
                var shapes=sourceRig.Colliders.Values.Where(c=>c.attachedRigidbody==rb&&!c.isTrigger).ToArray();
                var shape=shapes.FirstOrDefault(c=>c.enabled&&c.gameObject.activeInHierarchy)??shapes.FirstOrDefault();
                var hinge=rb.GetComponents<HingeJoint2D>().FirstOrDefault(j=>j.enabled)??rb.GetComponent<HingeJoint2D>();
                var limb=new Limb{key=key,rb=rb,shape=shape,joint=hinge,sourceEndpoints=true};
                referenceBones[key]=limb;sourceParts[key]=data;sourcePartIds[row["id"].I]=key;
                sourceCutJoints[key]=sourceRig.Resolve<HingeJoint2D>(data["joint2D"]);
                partHealth[key]=(float)data["MaxHealth"].N;InitializeReferencePart(key,new Vector3(rb.mass,rb.linearDamping,rb.angularDamping));
                if(key=="belly"||key=="neck")referenceBodies[key]=rb;else limbs[key]=limb;
            }
            pelvis=referenceBones["pelvis"];torso=referenceBones["torso"];head=referenceBones["head"];
            var refs=new Dictionary<string,string>{{"mover","baseRigidbody2D"},{"headCOM","headCOMRigidbody2D"},{"chestCOM","chestRigidbody2D"},{"pelvisCOM","pelvisCOMRigidbody2D"},{"handCOMNear","handLeftCOMRigidbody2D"},{"handCOMFar","handRightCOMRigidbody2D"},{"footCOMNear","footLeftCOMRigidbody2D"},{"footCOMFar","footRightCOMRigidbody2D"}};
            foreach(var p in refs)referenceBodies[p.Key]=sourceRig.Resolve<Rigidbody2D>(sourceMover[p.Value]);mover=referenceBodies["mover"];
            ConfigureSourceBodyShape();Physics2D.SyncTransforms();
            foreach(string name in new[]{"feetTransform","handLeftPoint","handRightPoint","handLeftPointParent","handRightPointParent","pelvisPoint","pelvisWhenAnyLegSeveredPoint","footLeftPoint","footRightPoint"}){
                var t=sourceRig.ResolveTransform(sourceMover[name]);sourcePoints[name]=t;sourceRestPoints[name]=t.localPosition;
                for(var node=t;node!=null&&node!=sourceRig.Root.transform;node=node.parent){
                    if(node.GetComponent<Rigidbody2D>()!=null)break;
                    int id=sourceRig.Transforms.First(p=>p.Value==node).Key;sourceSnapshotPoints.Add(id);
                    if(node.name=="ControlPoints")break;
                }
            }
            foreach(var p in referenceBones){
                string key=p.Key;Limb l=p.Value;Vector2 a=l.rb.position,b=a+Vector2.up*.1f;
                string next=key=="pelvis"?"belly":key=="belly"?"torso":key=="torso"?"neck":key=="neck"?"head":key.StartsWith("upper")?"forearm"+key.Substring(5):key.StartsWith("forearm")?"hand"+key.Substring(7):key.StartsWith("thigh")?"shin"+key.Substring(5):key.StartsWith("shin")?"foot"+key.Substring(4):"";
                if(next.Length>0)b=referenceBones[next].rb.position;
                else if(key.StartsWith("hand"))b=referenceBodies["handCOM"+(key.EndsWith("Near")?"Near":"Far")].position;
                else if(key.StartsWith("foot")){a=l.rb.position+new Vector2(-facing*.05f,0);b=l.rb.position+new Vector2(facing*.18f,0);}
                // The recovered head origin is its neck hinge, not a head
                // centre. CharacterRenderer expects the same base-to-crown
                // bone used by CharacterPose.Bind in portraits and outfits.
                else if(key=="head"){a=l.rb.position;b=a+Vector2.up*.30f;}
                l.sourceA=Limb.BodyLocalPoint(l.rb,a);l.sourceB=Limb.BodyLocalPoint(l.rb,b);l.length=Vector2.Distance(a,b);
            }
            foreach(var shape in sourceRig.Colliders.Values){
                colliders.Add(shape);var rb=shape.attachedRigidbody??shape.GetComponentInParent<Rigidbody2D>();
                var limb=referenceBones.Values.FirstOrDefault(l=>l.rb==rb);if(limb==null)continue;
                var contact=shape.GetComponent<DuelContact>()??shape.gameObject.AddComponent<DuelContact>();contact.owner=this;contact.limb=limb;contact.kind=limb.key=="head"||limb.key=="neck"?"head":limb.key.StartsWith("foot")?"foot":"body";
            }
            foreach(string side in new[]{"Near","Far"}){
                var driver=referenceBodies["handCOM"+side];driver.gameObject.SetActive(true);
                referenceHands[side]=new HandEffort{target=sourcePoints[side=="Near"?"handLeftPoint":"handRightPoint"].position,anchor=driver.position-mover.position,drain=weapon.TwoHanded?3:4.5f};
                var foot=referenceBones["foot"+side];var support=foot.rb.gameObject.AddComponent<TargetJoint2D>();support.enabled=false;feet[side]=new FootPlant(foot.B,.13f,support);
            }
            var config=sourceRig.Source("CharacterConfigApplier");
            if(SourceNothingActive&&weapon.id=="unarmed")sword=EquipSourceNothing("sword",sourceRig.Resolve<FixedJoint2D>(config["handFixedJoint2DL"]));
            else {EquipWeapon();if(primaryGrip!=null){primaryGrip.enabled=false;UnityEngine.Object.Destroy(primaryGrip);}}
            primaryGrip=sourceRig.Resolve<FixedJoint2D>(config["handFixedJoint2DL"]);
            BindSourceGrip(primaryGrip,sword);
            if(weapon.TwoHanded){
                supportJoint=sourceRig.Resolve<FixedJoint2D>(config["handFixedJoint2DR"]);gripSpacing=-weapon.supportGrip;
                BindSourceGrip(supportJoint,sword);
                if(SourceMountStudy)supportJoint.connectedAnchor=primaryGrip.connectedAnchor+SourceMountDelta(weapon,false,"second_from_primary");
            }else if(HasShield){
                var hand=limbs["handFar"];shield=Make("shield",hand.B+Vector2.down*.30f,hand.B+Vector2.up*.30f,.16f,1.05f,"shield");shield.shape.sharedMaterial=game.shieldMaterial;
                offhandGrip=sourceRig.Resolve<FixedJoint2D>(config["handFixedJoint2DR"]);BindSourceGrip(offhandGrip,shield);
            }else if(SourceNothingActive){
                offhandGrip=sourceRig.Resolve<FixedJoint2D>(config["handFixedJoint2DR"]);
                shield=EquipSourceNothing("shield",offhandGrip);BindSourceGrip(offhandGrip,shield);
            }else{
                shield=EquipFist("shield",limbs["handFar"],out offhandGrip);
                sourceRig.Resolve<FixedJoint2D>(config["handFixedJoint2DR"]).enabled=false;
            }
            foreach(var item in new[]{sword,shield})if(item!=null){
                // The selected source item prefabs are Discrete, like their
                // hands. Make() defaults to Continuous for the old arena;
                // per-item TOI steps otherwise advance the weapon ahead of
                // the hand's fixed joint and visibly pull it out of the grip.
                item.rb.collisionDetectionMode=CollisionDetectionMode2D.Discrete;
                foreach(var shape in item.Frame.GetComponents<Collider2D>())DuelSourceRig.ApplyItemLayer(shape,human);
            }
            ConfigureSourceEquipmentRig();
            if(game.IsSmoke&&Arg("-referenceOrderLimits")=="1")foreach(var hinge in sourceRig.Components.Values.OfType<HingeJoint2D>()){
                var limits=hinge.limits;hinge.limits=new JointAngleLimits2D{min=Mathf.Min(limits.min,limits.max),max=Mathf.Max(limits.min,limits.max)};
            }
            BuildReferenceWeight("Near",sword,weapon,false);
            if(!weapon.TwoHanded&&(HasShield||SourceNothingActive))BuildReferenceWeight("Far",shield,HasShield?weapon:DuelWeapon.Fist,HasShield);
            foreach(var p in referenceHands)p.Value.anchor=ReferenceEffortBody(p.Key).position-mover.position;
            BuildSourceRigKickAndCorpse();
            BuildSourceImpalement(sourceRig.SourceId,sourceRig.Component,sourceRig.Transform);
            BindEquipmentImpaleMaterials();
            SourceAIWeaponLengthMetres=weapon.id=="unarmed"?0:weapon.Length;
            SourceAIWeaponMount=sword.Frame;SourceAIShieldCOM=HasShield?shield?.Frame:null;
            BindSourceAITargets(sourceRig.ResolveTransform(sourceRig.Source("CharacterEnemyAI")["aimOrigin"]),sourcePoints["handLeftPoint"],sourcePoints["handRightPoint"],sourcePoints["handLeftPointParent"],sourcePoints["handRightPointParent"]);
            foreach(var a in colliders)foreach(var b in colliders)if(a!=b&&a!=null&&b!=null)DuelPhysicsAPI.IgnoreCollision(a,b);
            referenceSimulatedBodies=root.GetComponentsInChildren<Rigidbody2D>(true);
            SampleReference();
            CaptureCharacterBindingPose();
        }
        void BindSourceGrip(FixedJoint2D joint,Limb item){
            if(joint==null)throw new InvalidOperationException("Missing recovered hand grip");
            Vector2 world=joint.transform.TransformPoint(joint.anchor);
            joint.connectedBody=item.rb;joint.autoConfigureConnectedAnchor=false;joint.connectedAnchor=Limb.BodyLocalPoint(item.rb,world);joint.enabled=true;
        }
        Limb EquipSourceNothing(string key,FixedJoint2D sourceGrip){
            // sharedassets0.assets:273502, not the separate EmptyShopSlot.
            // Handle_1 and the counterweight are both at local (0,0).
            var data=DuelSourceEquipment.Nothing;float radius=(float)data["radius"].N;
            Vector2 point=SourceAnchorPoint(sourceGrip.attachedRigidbody,sourceGrip.anchor);
            var item=Make(key,point-Vector2.up*radius,point+Vector2.up*radius,radius*2,1,"sword");
            RemoveShape(item);var circle=item.Frame.gameObject.AddComponent<CircleCollider2D>();circle.radius=radius;
            circle.sharedMaterial=sourceRig.Material(J.O("material_key","sharedassets0.assets:1228"));item.shape=circle;colliders.Add(circle);
            var physics=DuelSourceEquipment.SourcePhysics(DuelWeapon.Fist);item.strikeProfile=DuelWeapon.Fist;
            item.rb.mass=physics.bodyMass;item.rb.linearDamping=physics.linearDamping;item.rb.angularDamping=physics.angularDamping;item.rb.gravityScale=physics.bodyGravity;
            item.rb.collisionDetectionMode=CollisionDetectionMode2D.Discrete;item.rb.sleepMode=(RigidbodySleepMode2D)data["body_sleep_mode"].I;item.rb.interpolation=(RigidbodyInterpolation2D)data["body_interpolation"].I;
            return item;
        }
        void BuildSourceRigKickAndCorpse(){
            var receiver=sourceRig.Source("KickReceiver");referenceKickReceiver=sourceRig.Resolve<Collider2D>(receiver["Collider2D"]);
            if(referenceKickReceiver==null){var go=sourceRig.ResolveTransform(receiver["m_GameObject"]);referenceKickReceiver=go?.GetComponent<Collider2D>();}
            if(referenceKickReceiver==null)throw new InvalidOperationException("Missing original kick receiver");
            (referenceKickReceiver.GetComponent<DuelKickReceiver>()??referenceKickReceiver.gameObject.AddComponent<DuelKickReceiver>()).owner=this;
            var effector=sourceRig.ResolveTransform(sourceMover["kickEffector"]);kickShape=effector.GetComponent<CapsuleCollider2D>();
            (kickShape.GetComponent<DuelKickTrigger>()??kickShape.gameObject.AddComponent<DuelKickTrigger>()).owner=this;kickShape.enabled=false;
            var vitality=sourceRig.Source("CharacterVitality");var corpse=sourceRig.ResolveTransform(vitality["weaponSelfColliderWhenDead"]);
            referenceCorpseBody=corpse.GetComponent<Rigidbody2D>();referenceCorpseShape=corpse.GetComponent<CircleCollider2D>();corpse.gameObject.SetActive(false);
        }
        void StepSourceRig(bool active,Fighter target,float dt){
            if(game.referenceSubstep==0){
                referenceDriverStarts.Clear();referenceDriverGoals.Clear();
                sourceRotationStarts.Clear();
                bool kicking=ReferenceKicking;
                if(kicking){float t=.5f-(kickUntil-game.Clock);var motion=DuelSourceMotion.Kick;var top=sourcePoints["pelvisPoint"].parent.parent;Vector2 p=motion.Position("Top",t);top.localPosition=new Vector3(p.x,p.y,top.localPosition.z);
                    foreach(string side in new[]{"Near","Far"}){var point=sourcePoints[side=="Near"?"footLeftPoint":"footRightPoint"];Vector2 v=motion.Position(side=="Near"?"FeetContainer/Feet/FootLeftPoint":"FeetContainer/Feet/FootRightPoint",t);point.localPosition=new Vector3(v.x,v.y,point.localPosition.z);}
                }else{
                    var top=sourcePoints["pelvisPoint"].parent.parent;top.localPosition=Vector3.zero;
                    sourcePoints["footLeftPoint"].localPosition=sourceRestPoints["footLeftPoint"];sourcePoints["footRightPoint"].localPosition=sourceRestPoints["footRightPoint"];
                }
                if(!PassedOut){
                    var pelvisPoint=sourcePoints[ReferenceDowned?"pelvisWhenAnyLegSeveredPoint":"pelvisPoint"];SetSourceDriverGoal(referenceBodies["pelvisCOM"],pelvisPoint.position);
                    foreach(string side in new[]{"Near","Far"}){
                        if(!ReferenceLegDisabled(side)){var p=sourcePoints[side=="Near"?"footLeftPoint":"footRightPoint"];SetSourceDriverGoal(referenceBodies["footCOM"+side],p.position);feet[side].goal=p.position;}
                        bool held;Vector2 input;
                        if(aiDriving&&SourceAIHandCommand(side,out var parent,out var nub,out held)){
                            sourcePoints[side=="Near"?"handLeftPointParent":"handRightPointParent"].localPosition=new Vector3(parent.x,parent.y,sourcePoints[side=="Near"?"handLeftPointParent":"handRightPointParent"].localPosition.z);input=nub;
                        }else{input=ReferenceStick(side,out held)*155;}
                        held&=active&&!ReferenceKicking;
                        var point=sourcePoints[side=="Near"?"handLeftPoint":"handRightPoint"];var parentPoint=sourcePoints[side=="Near"?"handLeftPointParent":"handRightPointParent"];
                        if(!ReferenceArmDisabled(side)){
                            SetSourceDriverGoal(referenceBodies["handCOM"+side],point.position);
                            // Original cln drives the prior point, then updates
                            // it. Cross-named input/damping wiring is retained.
                            var dampingHand=limbs[side=="Near"?"handFar":"handNear"].rb;
                            float damping=(dampingHand.linearVelocity.x+dampingHand.linearVelocity.y)*.5f>(float)sourceMover["handsDragThreshold"].N?(float)sourceMover["handsDragWhenActive"].N:(float)sourceMover["handsDragWhenPassive"].N;
                            dampingHand.linearDamping=dampingHand.angularDamping=damping;
                            if(held)point.position=parentPoint.position+new Vector3(facing*(input.x/100+.15f),input.y/100+.25f,0);else point.localPosition=Vector3.zero;
                            var h=referenceHands[side];if(held&&!h.active)attackId++;h.active=held;h.target=point.position;
                        }else referenceHands[side].active=false;
                    }
                }
                var feetTransform=sourcePoints["feetTransform"];float vx=mover.linearVelocity.x,speed=PassedOut?0:Mathf.Abs(vx);
                if(kicking){feetTransform.localScale=Vector3.one;feetTransform.rotation=Quaternion.identity;}
                else if(speed>(float)sourceMover["VelocityXFeetMoverThreshold"].N){float lean=1+2*Mathf.Abs(referenceBodies["headCOM"].position.x-referenceBodies["pelvisCOM"].position.x);feetTransform.Rotate(0,0,feetTransform.rotation.z+lean*vx*(-facing)*4);feetTransform.localScale=Vector3.one*((1+speed/(float)sourceMover["feetPositionTransformGrowScaleDivider"].N)/lean);}
                else{feetTransform.localScale=Vector3.one;feetTransform.rotation=Quaternion.Euler(0,0,feetTransform.rotation.z>=90?180:0);}
            }
            if(!PassedOut){
                float command=active&&!ReferenceKicking&&!ReferenceBeingKicked?move:0;
                if(mover.position.x*command>5.1f)command=0;
                if(ReferenceKicking)mover.linearVelocity=Vector2.zero;else mover.CombatAddForce(new Vector2(command*(ReferenceDowned?10:19),0));
                float lean=1+2*Mathf.Abs(referenceBodies["headCOM"].position.x-referenceBodies["pelvisCOM"].position.x),up=(health+100)*(float)sourceMover["HeadForceUpwardsMultiplier"].N;
                if(!severed.Contains("head")&&!severed.Contains("neck")){
                    referenceBodies["headCOM"].CombatAddForce(Vector2.up*((lean*250+up)*3));
                    referenceBodies["chestCOM"].CombatAddForce(new Vector2(mover.linearVelocity.x*(float)sourceMover["HeadForceSidewaysMultiplier"].N,up)*3);
                }
                foreach(var p in referenceDriverGoals.ToArray())if(p.Key!=null&&p.Key.simulated&&p.Key.gameObject.activeInHierarchy)MoveReferenceDriver(p.Key,p.Value);
                if(ReferenceKicking){var rb=referenceBodies["pelvisCOM"];if(game.referenceSubstep==0)sourceRotationStarts[rb]=rb.rotation;rb.CombatMoveRotation(Mathf.LerpAngle(sourceRotationStarts[rb],facing>0?-120:120,(game.referenceSubstep+1)/(float)game.referenceSteps));}
            }else mover.CombatMovePosition(new Vector2(referenceBodies["pelvisCOM"].position.x,mover.position.y));
            foreach(string side in new[]{"Near","Far"})if(PassedOut||ReferenceArmDisabled(side))limbs["hand"+side].rb.linearDamping=limbs["hand"+side].rb.angularDamping=(float)sourceMover["handsDragWhenActive"].N;
            var push=sourceRig.Resolve<AreaEffector2D>(sourceMover["pushingAreaEffector"]);if(push!=null)push.forceMagnitude=Mathf.Clamp(mover.position.x*(-facing)*5,5,50);
            var pushShape=sourceRig.Resolve<Collider2D>(sourceMover["pushingCollider"]);if(pushShape!=null)pushShape.enabled=!PassedOut;
            effortTick+=dt;while(effortTick>=.1666f){effortTick-=.1666f;TickReferenceEffort();}
            action=PassedOut?"力竭倒地":ReferenceKicking?(kickPaid?"踢击":"起腿"):ReferenceDowned?"腿伤倒地":Mathf.Abs(move)>.1f?"移步":referenceHands.Values.Any(h=>h.active)?"挥动":"守势";
            if(game.referenceSubstep==game.referenceSteps-1){trail.Enqueue(sword.B);while(trail.Count>8)trail.Dequeue();}SampleReference();
        }
        void SetSourceDriverGoal(Rigidbody2D rb,Vector2 goal){referenceDriverStarts[rb]=rb.position;referenceDriverGoals[rb]=goal;}
        J CaptureSourceRig(){
            if(sourceRig==null)return new J();
            return J.O("version",1,"template",sourceRig.SourceId,
                // Mono's default Single formatting emits seven digits. Store
                // Singles as exact Doubles so a save/reload cannot cross that
                // rounding boundary before Unity normalizes the transform.
                "bodies",new J(sourceRig.Bodies.Select(p=>{var q=p.Value.transform.rotation;return J.O("id",p.Key,"x",(double)p.Value.position.x,"y",(double)p.Value.position.y,"angle",(double)p.Value.rotation,"vx",(double)p.Value.linearVelocity.x,"vy",(double)p.Value.linearVelocity.y,"spin",(double)p.Value.angularVelocity,"simulated",p.Value.simulated,"active",p.Value.gameObject.activeSelf,"mass",(double)p.Value.mass,"drag",(double)p.Value.linearDamping,"angular_drag",(double)p.Value.angularDamping,"gravity",(double)p.Value.gravityScale,"body_type",(int)p.Value.bodyType,"qx",(double)q.x,"qy",(double)q.y,"qz",(double)q.z,"qw",(double)q.w);}).ToList()),
                "joints",new J(sourceRig.Components.Where(p=>p.Value is AnchoredJoint2D).Select(p=>{var joint=(AnchoredJoint2D)p.Value;return J.O("id",p.Key,"enabled",joint.enabled,"anchor_x",(double)joint.anchor.x,"anchor_y",(double)joint.anchor.y,"connected_x",(double)joint.connectedAnchor.x,"connected_y",(double)joint.connectedAnchor.y);}).ToList()),
                "points",new J(sourceSnapshotPoints.Select(id=>{var t=sourceRig.Transform(id);return J.O("id",id,"x",(double)t.localPosition.x,"y",(double)t.localPosition.y,"z",(double)t.localPosition.z,"angle",(double)t.localEulerAngles.z,"sx",(double)t.localScale.x,"sy",(double)t.localScale.y);}).ToList()));
        }
        void RestoreSourceRig(J state){
            if(sourceRig==null||!state.Has)return;
            int Depth(Transform t){int depth=0;for(;t!=null;t=t.parent)depth++;return depth;}
            foreach(var p in state["points"].A){var t=sourceRig.Transform(p["id"].I);t.localPosition=new Vector3((float)p["x"].N,(float)p["y"].N,(float)p["z"].N);t.localRotation=Quaternion.Euler(t.localEulerAngles.x,t.localEulerAngles.y,(float)p["angle"].N);t.localScale=new Vector3((float)p["sx"].N,(float)p["sy"].N,t.localScale.z);}
            foreach(var p in state["joints"].A){var joint=(AnchoredJoint2D)sourceRig.Component(p["id"].I);joint.autoConfigureConnectedAnchor=false;joint.anchor=new Vector2((float)p["anchor_x"].N,(float)p["anchor_y"].N);joint.connectedAnchor=new Vector2((float)p["connected_x"].N,(float)p["connected_y"].N);joint.enabled=p["enabled"].B;}
            foreach(var p in state["bodies"].A.OrderBy(p=>Depth(sourceRig.Bodies[p["id"].I].transform))){var rb=sourceRig.Bodies[p["id"].I];rb.gameObject.SetActive(p["active"].B);rb.bodyType=(RigidbodyType2D)p["body_type"].I;rb.mass=(float)p["mass"].N;rb.linearDamping=(float)p["drag"].N;rb.angularDamping=(float)p["angular_drag"].N;rb.gravityScale=(float)p["gravity"].N;rb.simulated=p["simulated"].B;
                rb.transform.SetPositionAndRotation(new Vector3((float)p["x"].N,(float)p["y"].N,rb.transform.position.z),new Quaternion((float)p["qx"].N,(float)p["qy"].N,(float)p["qz"].N,(float)p["qw"].N));}
            // The caller synchronizes BOTH hierarchies before assigning native
            // 2D velocities. A Transform push can otherwise change a child's
            // angle/velocity after that child's state has already been set.
            sourceRigRestorePending=state;
        }
        internal void FinishSourceRigRestore(){
            if(sourceRigRestorePending==null)return;var state=sourceRigRestorePending;sourceRigRestorePending=null;
            foreach(var p in state["bodies"].A){var rb=sourceRig.Bodies[p["id"].I];rb.position=new Vector2((float)p["x"].N,(float)p["y"].N);rb.rotation=(float)p["angle"].N;
                if(rb.bodyType!=RigidbodyType2D.Static){rb.linearVelocity=new Vector2((float)p["vx"].N,(float)p["vy"].N);rb.angularVelocity=(float)p["spin"].N;}}
        }
    }
}
