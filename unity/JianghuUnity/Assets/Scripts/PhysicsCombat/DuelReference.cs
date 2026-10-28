using System;
using System.Collections.Generic;
using System.Linq;
using Jianghu;
using UnityEngine;

// Recovered rules and numeric motion dependencies with an adapted local rig.
// Original assemblies and native executable code are not used at runtime.
public sealed partial class PhysicsDuel {
    public static void OpenReferenceSandbox(){
        var scene=UnityEngine.SceneManagement.SceneManager.CreateScene("Combat sandbox",new UnityEngine.SceneManagement.CreateSceneParameters(UnityEngine.SceneManagement.LocalPhysicsMode.Physics2D));
        var root=new GameObject("Combat sandbox");root.SetActive(false);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);root.AddComponent<PhysicsDuel>();root.SetActive(true);Application.runInBackground=true;
    }
    public const string ReferenceProfile="bloody-reference-v1";
    public bool ReferenceCombat=>!Campaign||campaign.battle["fighters"][0]["physics_profile"].S==ReferenceProfile;
    bool referenceBindProbe;
    bool referenceProbeBody,referenceProbeCollecting;
    int referenceProbeContactEvents;
    int referenceSteps=4;
    int referenceSubstep;
    public int CombatSolverSubsteps=>ReferenceCombat?referenceSteps:1;
    PhysicsMaterial2D referenceRootMaterial,referencePelvisMaterial,referenceGroundMaterial,referenceHandleMaterial;
    static void ReferenceMaterial(PhysicsMaterial2D material,float friction,float bounce,int combine=1){material.friction=friction;material.bounciness=bounce;material.frictionCombine=(PhysicsMaterialCombine2D)combine;material.bounceCombine=(PhysicsMaterialCombine2D)4;}
    void ConfigureReferencePhysics(){
        var left=Campaign?DuelWeapon.CampaignWeapon(campaign.battle["fighters"][0]["weapon"]):DuelWeapon.All[playerWeaponIndex];
        var right=Campaign?DuelWeapon.CampaignWeapon(campaign.battle["fighters"][1]["weapon"]):DuelWeapon.All[enemyWeaponIndex];
        int needed=Mathf.Max(ReferenceSolverSteps(left),ReferenceSolverSteps(right));
        if(Campaign&&(campaign.battle["fighters"][0]["shield"].B||campaign.battle["fighters"][1]["shield"].B))needed=Mathf.Max(needed,16);
        referenceSteps=IsSmoke&&int.TryParse(Arg("-referenceSubsteps"),out var measuredSteps)&&measuredSteps>=1&&measuredSteps<=16?measuredSteps:needed;
        Time.fixedDeltaTime=ReferenceCombat?1f/60:1f/120;
        Physics2D.gravity=new Vector2(0,ReferenceCombat?-12.5f:-18);
        // Additional convergence for the independently authored capsules and
        // weapon inertias; the reference simulation/input clock stays 60 Hz.
        Physics2D.velocityIterations=50;
        Physics2D.positionIterations=60;
        Physics2D.maxLinearCorrection=ReferenceCombat?1000:priorLinearCorrection;
        Physics2D.maxAngularCorrection=ReferenceCombat?10:priorAngularCorrection;
        Physics2D.maxTranslationSpeed=ReferenceCombat?100:priorTranslationSpeed;
        Physics2D.maxRotationSpeed=ReferenceCombat?100:priorRotationSpeed;
        Physics2D.baumgarteScale=ReferenceCombat?.005f:priorBaumgarte;
        Physics2D.baumgarteTOIScale=ReferenceCombat?2:priorTOI;
        controls.reference=ReferenceCombat;
        if(ReferenceCombat){
            ReferenceMaterial(bodyMaterial,.5f,.25f);ReferenceMaterial(footMaterial,1,0);ReferenceMaterial(bladeMaterial,.7f,.1f,3);ReferenceMaterial(shieldMaterial,.7f,.1f,3);
            if(referenceRootMaterial==null){referenceRootMaterial=Material("Root",.35f,0);referencePelvisMaterial=Material("Pelvis",.95f,0);referenceGroundMaterial=Material("Ground",.25f,.15f);referenceHandleMaterial=Material("Sliding shaft and guard",.18f,.1f);}
            ReferenceMaterial(referenceRootMaterial,.35f,0);ReferenceMaterial(referencePelvisMaterial,.95f,0);ReferenceMaterial(referenceGroundMaterial,.25f,.15f);
            ReferenceMaterial(referenceHandleMaterial,.18f,.1f,3);
            foreach(var collider in GetComponentsInChildren<Collider2D>())if(collider.gameObject.layer==8)collider.sharedMaterial=referenceGroundMaterial;
        }
    }
    static int ReferenceSolverSteps(DuelWeapon weapon)=>weapon.id=="unarmed"?8:16;
    public sealed partial class Fighter {
        public bool Reference {get;private set;}
        public bool PassedOut=>Reference&&game.Clock<exhaustedUntil;
        bool ReferenceArmsActive=>!PassedOut&&referenceHands.Values.Any(h=>h.active);
        float exhaustedUntil=-10,recoveryUntil=-10,effortTick,walkPhase,kickUntil=-10;
        readonly Dictionary<string,Limb> referenceBones=new Dictionary<string,Limb>();
        readonly Dictionary<string,Rigidbody2D> referenceBodies=new Dictionary<string,Rigidbody2D>();
        readonly Dictionary<string,Rigidbody2D> referenceWeights=new Dictionary<string,Rigidbody2D>();
        readonly Dictionary<string,float> partHealth=new Dictionary<string,float>();
        readonly HashSet<string> severed=new HashSet<string>();
        sealed class HandEffort {public Vector2 anchor,target;public float heat,drain;public bool active;}
        readonly Dictionary<string,HandEffort> referenceHands=new Dictionary<string,HandEffort>();
        readonly Dictionary<Rigidbody2D,Vector2> referenceDriverStarts=new Dictionary<Rigidbody2D,Vector2>(),referenceDriverGoals=new Dictionary<Rigidbody2D,Vector2>();
        Rigidbody2D mover;
        Rigidbody2D[] referenceSimulatedBodies;
        public float ReferencePeakSolverSpin {get;private set;}
        public int ReferenceVelocityCorrections {get;private set;}
        public int ReferenceBodyCount=>limbs.Values.Select(l=>l.rb).Concat(referenceBodies.Values).Concat(referenceWeights.Values).Distinct().Count();
        public float ReferenceItemMass=>sword.rb.mass;
        public float ReferenceWeightMass=>referenceWeights.TryGetValue("Near",out var weight)?weight.mass:0;
        public float ReferenceDriverInertia(string key)=>referenceBodies[key].inertia;
        public float ReferenceWeightGap(){float gap=0;foreach(var rb in referenceWeights.Values){var j=rb.GetComponent<FixedJoint2D>();if(j.enabled&&rb.simulated)gap=Mathf.Max(gap,Vector2.Distance(rb.position,Limb.BodyPoint(j.connectedBody,j.connectedAnchor)));}return gap;}
        public float ReferenceLocalHealth(string key)=>partHealth.TryGetValue(key,out var hp)?hp:0;
        public Vector2 ReferenceHandTarget(string side)=>referenceHands[side].target;
        public bool ReferenceSevered(string key)=>severed.Contains(key);
        bool ReferenceLegDisabled(string side)=>new[]{"thigh","shin","foot"}.Any(p=>severed.Contains(p+side)||referencePartModes[p+side]!=DuelReferenceRules.PartMode.None);
        public bool ReferenceDowned=>Reference&&(ReferenceLegDisabled("Near")||ReferenceLegDisabled("Far"));
        public float ExhaustedLeft=>Mathf.Max(0,exhaustedUntil-game.Clock);
        public float ReferenceSkeletonGap(){float gap=0;foreach(var l in referenceBones.Values){var j=l.joint;if(j!=null&&j.enabled)gap=Mathf.Max(gap,Vector2.Distance(Limb.BodyPoint(l.rb,j.anchor),Limb.BodyPoint(j.connectedBody,j.connectedAnchor)));}return gap;}
        public bool ReferenceKicking=>Reference&&game.Clock<kickUntil;
        static float PartMaximum(string key)=>key=="head"||key=="neck"?70:key=="pelvis"||key=="belly"||key=="torso"?100:key.StartsWith("hand")?50:key.StartsWith("upper")||key.StartsWith("forearm")?30:40;
        Limb ReferenceBone(string key,Vector2 a,Vector2 b,float width,float mass,string kind,float drag=5,float spinDrag=5){
            var l=Make(key,a,b,width,mass,kind);l.rb.mass=mass;l.rb.linearDamping=drag;l.rb.angularDamping=spinDrag;
            referenceBones[key]=l;partHealth[key]=PartMaximum(key);InitializeReferencePart(key,new Vector3(mass,drag,spinDrag));return l;
        }
        Rigidbody2D ReferenceCOM(string key,Limb bone,Vector2 position,float mass,float drag,float angular,float gravity=1){
            var go=new GameObject(key);go.transform.SetParent(root.transform);go.transform.position=position;
            var rb=go.AddComponent<Rigidbody2D>();rb.mass=mass;rb.linearDamping=drag;rb.angularDamping=angular;rb.gravityScale=gravity;
            rb.interpolation=RigidbodyInterpolation2D.Interpolate;rb.sleepMode=RigidbodySleepMode2D.NeverSleep;
            // The source hands/feet COM bodies carry small real shapes. A
            // shapeless body keeps Unity's fallback inertia and loads the
            // wrist/ankle very differently despite having the correct mass.
            // Source layer 13 has an empty collision matrix row. Exclude
            // contacts per shape, retaining its contribution to mass/inertia.
            if(key.StartsWith("handCOM")){go.layer=13;var c=go.AddComponent<BoxCollider2D>();c.size=Vector2.one*.01f;c.excludeLayers=~0;colliders.Add(c);}
            else if(key.StartsWith("footCOM")){go.layer=13;var c=go.AddComponent<CircleCollider2D>();c.radius=.025f;c.excludeLayers=~0;colliders.Add(c);}
            if(bone!=null){var j=go.AddComponent<FixedJoint2D>();j.connectedBody=bone.rb;j.autoConfigureConnectedAnchor=false;j.anchor=Vector2.zero;j.connectedAnchor=Limb.BodyLocalPoint(bone.rb,position);j.frequency=0;j.dampingRatio=1;}
            referenceBodies[key]=rb;return rb;
        }
        void BuildReference(float x){
            Reference=true;heightScale=bodyMassScale=armSpanScale=1;
            if(!game.Campaign){health=maxHealth=100;stamina=maxStamina=100;}
            Vector2 p=new Vector2(x,0);
            pelvis=ReferenceBone("pelvis",p+new Vector2(0,.84f),p+new Vector2(0,.99f),.23f,3,"body",25,25);
            pelvis.shape.sharedMaterial=game.referencePelvisMaterial;
            var belly=ReferenceBone("belly",pelvis.B,p+new Vector2(0,1.14f),.25f,5,"body",5,100);Join(belly,pelvis,pelvis.B,-5,5);
            torso=ReferenceBone("torso",belly.B,p+new Vector2(0,1.34f),.28f,5,"body",5,100);Join(torso,belly,belly.B,-5,5);
            var neck=ReferenceBone("neck",torso.B,p+new Vector2(0,1.43f),.12f,3,"head",20,100);Join(neck,torso,torso.B,-2,2);
            head=ReferenceBone("head",neck.B,p+new Vector2(0,1.73f),.25f,3,"head",.05f,.5f);Join(head,neck,neck.B,-40,40);
            // Additional spine segments have their own versioned snapshot slots.
            limbs.Remove("belly");limbs.Remove("neck");referenceBodies["belly"]=belly.rb;referenceBodies["neck"]=neck.rb;
            foreach(string side in new[]{"Far","Near"}){
                float offset=side=="Near"?.06f:-.06f;
                Vector2 shoulder=p+new Vector2(offset*facing,1.30f),elbow=shoulder+new Vector2(facing*.20f,-.20f),wrist=elbow+new Vector2(facing*.25f,.06f);
                if(weapon.TwoHanded){Vector2 intended=p+new Vector2(facing*.10f,1.05f)+(side=="Far"?ReadyDirection*weapon.supportGrip:Vector2.zero);Solve(shoulder,intended,.283f,.257f,-facing,out elbow,out wrist,.001f);}
                var upper=ReferenceBone("upper"+side,shoulder,elbow,.12f,3,"arm");Join(upper,torso,shoulder,-180,180).useLimits=false;
                var fore=ReferenceBone("forearm"+side,elbow,wrist,.10f,3,"arm");JoinAnatomical(fore,upper,elbow,facing>0?15:-135,facing>0?135:-15);
                var hand=ReferenceBone("hand"+side,wrist,wrist+(weapon.TwoHanded?ReadyDirection*.098f:new Vector2(facing*.095f,.025f)),.09f,3,"hand");Join(hand,fore,wrist,weapon.TwoHanded?-40:20*facing,weapon.TwoHanded?40:20*facing);
                Vector2 hip=p+new Vector2(offset*facing,.87f),knee=p+new Vector2((offset+.10f)*facing,.49f),ankle=p+new Vector2((offset-.06f)*facing,.13f);
                var thigh=ReferenceBone("thigh"+side,hip,knee,.15f,3,"leg");Join(thigh,pelvis,hip,-45,45);
                var shin=ReferenceBone("shin"+side,knee,ankle,.12f,3,"leg");JoinAnatomical(shin,thigh,knee,facing>0?-150:20,facing>0?-20:150);
                var foot=ReferenceBone("foot"+side,ankle+new Vector2(-facing*.08f,-.055f),ankle+new Vector2(facing*.16f,-.055f),.12f,3,"foot",5,50);
                foot.shape.sharedMaterial=game.footMaterial;Join(foot,shin,ankle,-55,5);
                ReferenceCOM("footCOM"+side,foot,ankle,2,2,2);
                // Legacy foot serialization remains available; these disabled
                // joints never drive the new rig.
                var support=foot.rb.gameObject.AddComponent<TargetJoint2D>();support.enabled=false;feet[side]=new FootPlant(ankle,.13f,support);
                ReferenceCOM("handCOM"+side,hand,hand.B,side=="Far"&&HasShield&&!weapon.TwoHanded?.25f:.55f,0,0,0);
                referenceHands[side]=new HandEffort{target=hand.B,anchor=hand.B-p,drain=weapon.TwoHanded?3:4.5f};
            }
            ReferenceCOM("pelvisCOM",pelvis,pelvis.B,3,25,25);
            ReferenceCOM("headCOM",head,head.B,.1f,2.5f,25,-5);
            ReferenceCOM("chestCOM",torso,torso.rb.position,3,0,25,-5);
            mover=ReferenceCOM("mover",null,p+new Vector2(0,.055f),1,1,1);mover.constraints=RigidbodyConstraints2D.FreezeRotation;
            mover.gameObject.layer=11;var baseShape=mover.gameObject.AddComponent<CapsuleCollider2D>();baseShape.direction=CapsuleDirection2D.Horizontal;baseShape.size=new Vector2(.32f,.10f);baseShape.sharedMaterial=game.referenceRootMaterial;colliders.Add(baseShape);
            EquipWeapon();
            primaryGrip.enabled=false;UnityEngine.Object.Destroy(primaryGrip);
            primaryGrip=ReferenceGrip(limbs["handNear"],sword,limbs["handNear"].B);
            if(weapon.TwoHanded){
                // Two hands independently load the same physical weapon.
                // Initialize the second grip at its actual attachment before
                // enabling constraints; no runtime IK projects the inputs.
                var hand=limbs["handFar"];Vector2 delta=sword.WorldPoint(weapon.Local(new Vector2(0,weapon.supportGrip),facing))-hand.B;
                hand.rb.position+=delta;referenceBodies["handCOMFar"].position+=delta;referenceHands["Far"].target+=delta;
                AttachSupportHand();
            }else if(HasShield){
                var hand=limbs["handFar"];Vector2 grip=hand.B;
                shield=Make("shield",grip+Vector2.down*.30f,grip+Vector2.up*.30f,.16f,1.05f,"shield");
                offhandGrip=ReferenceGrip(hand,shield,grip);
                shield.shape.sharedMaterial=game.shieldMaterial;
            }else shield=EquipFist("shield",limbs["handFar"],out offhandGrip);
            BuildReferenceWeight("Near",sword,weapon,false);
            if(HasShield&&!weapon.TwoHanded)BuildReferenceWeight("Far",shield,weapon,true);
            foreach(var pair in referenceHands)pair.Value.anchor=ReferenceEffortBody(pair.Key).position-mover.position;
            BuildReferenceKick();
            BuildReferenceCorpse();
            foreach(var a in colliders)foreach(var b in colliders)if(a!=b)Physics2D.IgnoreCollision(a,b);
            // Match the reference graph: per-body CCD splits the solution at
            // individual impacts and lets a driven hand outrun its rigid grip.
            foreach(var rb in root.GetComponentsInChildren<Rigidbody2D>())rb.collisionDetectionMode=CollisionDetectionMode2D.Discrete;
            referenceSimulatedBodies=root.GetComponentsInChildren<Rigidbody2D>(true);
            foreach(var l in referenceBones.Values)l.SampleVelocity();foreach(var l in limbs.Values)l.SampleVelocity();
        }
        public void FinishReferencePhysics(){
            if(!Reference)return;
            // Physics2D limits the integrated angular speed, but its joint
            // solver can leave a larger velocity after a contact. Apply the
            // same 100 rad/s ceiling to the completed step, including loose
            // parts and drivers, so checkpoints remain valid and resumable.
            float limit=100*Mathf.Rad2Deg;
            foreach(var rb in referenceSimulatedBodies)if(rb.simulated){
                float speed=Mathf.Abs(rb.angularVelocity);ReferencePeakSolverSpin=Mathf.Max(ReferencePeakSolverSpin,speed);
                if(speed>limit){rb.angularVelocity=Mathf.Clamp(rb.angularVelocity,-limit,limit);ReferenceVelocityCorrections++;}
            }
        }
        static FixedJoint2D ReferenceGrip(Limb hand,Limb item,Vector2 point){var j=hand.rb.gameObject.AddComponent<FixedJoint2D>();j.connectedBody=item.rb;j.autoConfigureConnectedAnchor=false;j.anchor=Limb.BodyLocalPoint(hand.rb,point);j.connectedAnchor=Limb.BodyLocalPoint(item.rb,point);j.frequency=0;j.dampingRatio=0;return j;}
        // SourcePrefab_HandItem initializes masses by category at runtime;
        // neither prefab defaults nor an item's display weight are authoritative.
        public static Vector3 ReferenceItemPhysics(DuelWeapon spec,bool isShield=false){
            if(!isShield&&spec.id=="unarmed")return new Vector3(DuelWeapon.Fist.mass,0,0);
            bool two=spec.TwoHanded;float mass=two?2:1,weight=two?.25f:1,distance=two?.5073f:.184f;
            switch(spec.id){
                case "dagger":distance=.056f;break;
                case "axe":case "greataxe":mass=two?2.25f:1;weight=two?1.25f:1;distance=two?.514f:.396f;break;
                case "mace":mass=two?7:3;weight=two?4:2.5f;distance=two?.612f:.4f;break;
                case "warhammer":mass=two?3:2;weight=two?1.5f:1.25f;distance=two?.409f:.516f;break;
                case "spear":mass=two?3:1.5f;weight=.5f;distance=.327f;break;
            }
            if(isShield){mass=2;weight=.05f;}
            return new Vector3(mass,weight,distance);
        }
        void BuildReferenceWeight(string side,Limb item,DuelWeapon spec,bool isShield){
            if(!isShield&&spec.id=="unarmed")return;
            Vector3 physics=ReferenceItemPhysics(spec,isShield);float mass=physics.x,weight=physics.y,distance=physics.z;
            item.rb.mass=mass;item.rb.linearDamping=0;item.rb.angularDamping=.05f;
            Vector2 position=isShield?item.rb.position+new Vector2(facing*.221f,-.022f):item.WorldPoint(spec.Local(new Vector2(0,distance),facing));
            var go=new GameObject("counterweight"+side);go.transform.SetParent(root.transform);go.transform.position=position;go.layer=2;
            var rb=go.AddComponent<Rigidbody2D>();rb.mass=weight;rb.linearDamping=0;rb.angularDamping=.05f;rb.sleepMode=RigidbodySleepMode2D.NeverSleep;
            var shape=go.AddComponent<CircleCollider2D>();shape.radius=.05f;shape.isTrigger=true;
            var joint=go.AddComponent<FixedJoint2D>();joint.connectedBody=item.rb;joint.autoConfigureConnectedAnchor=false;joint.connectedAnchor=Limb.BodyLocalPoint(item.rb,position);joint.frequency=0;joint.dampingRatio=0;
            referenceWeights[side]=rb;
        }
        Rigidbody2D ReferenceEffortBody(string side){if(referenceWeights.TryGetValue(weapon.TwoHanded?"Near":side,out var weight))return weight;return (side=="Near"?sword:shield??sword).rb;}
        void MoveReferenceDriver(Rigidbody2D body,Vector2 goal){
            if(game.referenceSubstep==0||!referenceDriverStarts.ContainsKey(body)){referenceDriverStarts[body]=body.position;referenceDriverGoals[body]=goal;}
            Vector2 next=Vector2.Lerp(referenceDriverStarts[body],referenceDriverGoals[body],(game.referenceSubstep+1)/(float)game.referenceSteps);
            // A distant or blocked target must not turn finer contact steps
            // into a faster hand motor. Bound the auxiliary hand's sweep;
            // actual weapon angular velocity and contact impulses stay free.
            if(body==referenceBodies["handCOMNear"]||body==referenceBodies["handCOMFar"])next=Vector2.MoveTowards(body.position,next,75*Time.fixedDeltaTime/game.referenceSteps);
            body.MovePosition(next);
        }
        void DropReferenceWeight(string side){if(!referenceWeights.TryGetValue(side,out var rb))return;rb.simulated=false;rb.GetComponent<FixedJoint2D>().enabled=false;var item=side=="Near"?sword:shield;item.rb.mass=1;item.rb.gravityScale=1;item.rb.linearDamping=item.rb.angularDamping=.05f;}
        Vector2 ReferenceStick(string side,out bool held){
            if(aiDriving){Vector2 v=side=="Near"?aiMain:aiOff;held=v.sqrMagnitude>.00001f;return v;}
            var pad=side=="Near"?game.controls.right:game.controls.left;held=pad.captured;return pad.value;
        }
        void StepReference(bool active,Fighter target){
            float dt=Time.fixedDeltaTime/game.referenceSteps;
            UpdateReferenceKick();
            ClampReferenceRoot();TickReferenceVitality(dt);TickReferenceBleeding();
            if(health<=0){TickReferenceDeath();SampleReference();return;}
            if(PassedOut){SetReferenceLimp(true);stamina=0;action="力竭倒地";SampleReference();return;}
            balance=maxBalance;strength=1;
            float command=active&&!ReferenceKicking&&!ReferenceBeingKicked?move:0;
            if(mover.position.x*command>5.1f)command=0;
            if(ReferenceKicking)mover.linearVelocity=Vector2.zero;
            else mover.AddForce(new Vector2(command*(ReferenceDowned?10:19),0));
            float lean=1+2*Mathf.Abs(referenceBodies["headCOM"].position.x-referenceBodies["pelvisCOM"].position.x);
            float up=(health+100)*.25f;
            referenceBodies["headCOM"].gravityScale=referenceBodies["chestCOM"].gravityScale=ReferenceDowned?1:-5;
            if(!ReferenceDowned){
                referenceBodies["headCOM"].AddForce(Vector2.up*(lean*250+up)*3);
                referenceBodies["chestCOM"].AddForce(new Vector2(mover.linearVelocity.x*300,up*3));
            }
            // BodyPart's any-leg-severed branch selects a lower pelvis point,
            // mass 5 and downed locomotion. Keep the source target displacement
            // (-.30, -.65), adapted to this independent rig's standing height.
            Vector2 pelvisGoal=mover.position+(ReferenceDowned?new Vector2(-facing*.30f,.285f):new Vector2(0,brace?.68f:.935f))+ReferenceKickPelvis();
            if(ReferenceDowned){
                // The source's lowered target alone leaves this shorter rig
                // above the floor. Downward-only assistance lets real ground
                // contacts carry the weight; no motor can lift it back up.
                var body=referenceBodies["pelvisCOM"];
                body.AddForce(new Vector2(Mathf.Clamp((pelvisGoal.x-body.position.x)*40-body.linearVelocity.x*8,-50,50),Mathf.Min(0,(pelvisGoal.y-body.position.y)*80)));
            }else MoveReferenceDriver(referenceBodies["pelvisCOM"],pelvisGoal);
            float vx=mover.linearVelocity.x;
            if(Mathf.Abs(vx)>.75f)walkPhase+=vx*lean*4*Mathf.Deg2Rad/game.referenceSteps;
            else walkPhase=Mathf.MoveTowards(walkPhase,Mathf.Round(walkPhase/Mathf.PI)*Mathf.PI,dt*6);
            float stride=(1+Mathf.Abs(vx)/7)/lean;
            foreach(string side in new[]{"Near","Far"}){
                if(!ReferenceDowned&&!severed.Contains("foot"+side)){
                    float phase=walkPhase+(side=="Far"?Mathf.PI:0);
                    var goal=mover.position+new Vector2(Mathf.Cos(phase)*.21f*stride,Mathf.Max(0,Mathf.Sin(phase))*.18f*stride+.075f);
                    if(ReferenceKicking)goal=ReferenceKickFoot(side);
                    MoveReferenceDriver(referenceBodies["footCOM"+side],goal);feet[side].goal=goal;
                }
                var hand=limbs["hand"+side];var state=referenceHands[side];Vector2 stick=ReferenceStick(side,out bool held);held&=active&&game.Clock>=kickUntil-.3f;
                if(!ReferenceArmDisabled(side)){
                    MoveReferenceDriver(referenceBodies["handCOM"+side],state.target);
                    if(game.referenceSubstep==0){
                        float damping=(hand.rb.linearVelocity.x+hand.rb.linearVelocity.y)*.5f>1?0:75;
                        hand.rb.linearDamping=hand.rb.angularDamping=damping;
                        Vector2 origin=weapon.TwoHanded
                            ?mover.position+new Vector2(facing*.10f,1.05f)+ReadyDirection*(.098f+(side=="Far"?weapon.supportGrip:0))
                            :mover.position+new Vector2((side=="Near"?.07f:-.07f)*facing,1.16f);
                        // Arm targets must descend with the injured torso;
                        // targets fixed at standing height can suspend it too.
                        if(ReferenceDowned)origin+=pelvis.B-(mover.position+new Vector2(0,.935f));
                        state.target=held&&SourceHandTarget(side,out var sourceTarget)?sourceTarget:origin+(held?new Vector2(stick.x*1.55f+facing*.15f,stick.y*1.55f+.25f):Vector2.zero);
                        // A released supporting hand remains on its physical
                        // grip. Pulling it back to a separate idle target while
                        // the other hand is active creates an artificial clamp.
                        if(weapon.TwoHanded&&!held){ReferenceStick(side=="Near"?"Far":"Near",out bool otherHeld);if(otherHeld&&active)state.target=hand.B;}
                        if(held&&!state.active)attackId++;state.active=held;
                    }
                }else state.active=false;
            }
            effortTick+=dt;
            while(effortTick>=.1666f){effortTick-=.1666f;TickReferenceEffort();}
            action=PassedOut?"力竭倒地":ReferenceKicking?(kickPaid?"踢击":"起腿"):ReferenceBeingKicked?"受踢":ReferenceDowned?(Mathf.Abs(move)>.1f?"倒地挪移":"失足倒地"):Mathf.Abs(move)>.1f?"移步":referenceHands.Values.Any(h=>h.active)?"挥动":"守势";
            if(game.referenceSubstep==game.referenceSteps-1){trail.Enqueue(sword.B);while(trail.Count>8)trail.Dequeue();}SampleReference();
        }
        void TickReferenceEffort(){
            relativeSwingSpeed=0;
            foreach(var pair in referenceHands){
                var s=pair.Value;var item=ReferenceEffortBody(pair.Key);
                var hand=limbs["hand"+pair.Key];Vector2 relPos=item.position-mover.position;
                float speed=(item.linearVelocity-mover.linearVelocity).magnitude;relativeSwingSpeed=Mathf.Max(relativeSwingSpeed,speed);
                s.anchor=Vector2.MoveTowards(s.anchor,relPos,.5f*.1666f);
                bool fast=s.active&&speed>6&&!ReferenceArmDisabled(pair.Key);
                if(fast){float paid=ConsumeReferenceStamina(s.drain);motionStaminaSpent+=paid;if(pair.Key=="Near")mainMotionSpent+=paid;else offMotionSpent+=paid;}
                float baseline=weapon.TwoHanded?3:4.5f;
                if(fast&&Vector2.Distance(s.anchor,relPos)<.7f){s.heat+=.1666f;if(s.heat>.2f)s.drain=Mathf.Min(baseline*5,s.drain+2.5f*.1666f);}
                else {s.heat=Mathf.Max(0,s.heat-.5f*.1666f);if(s.heat==0)s.drain=baseline;}
            }
            swingHeat=Mathf.Clamp01(referenceHands.Values.Max(h=>h.heat)/5);
        }
        float ConsumeReferenceStamina(float amount){if(health<=0||PassedOut||game.Clock<recoveryUntil)return 0;float paid=Mathf.Min(stamina,amount);stamina-=paid;return paid;}
        void SampleReference(){foreach(var l in referenceBones.Values.Concat(limbs.Values).Distinct()){peakAngularSpeed=Mathf.Max(peakAngularSpeed,Mathf.Abs(l.rb.angularVelocity));peakSpeed=Mathf.Max(peakSpeed,l.rb.linearVelocity.magnitude);l.SampleVelocity();}}
        void SeverReference(string key){
            if(severed.Contains(key))return;var rootPart=referenceBones[key];
            if(rootPart.joint!=null)rootPart.joint.enabled=false;
            // Only the struck hinge is cut. Child hinges remain connected to
            // their severed parent; a forearm and its hand fall as one chain.
            var detached=new List<Limb>{rootPart};
            for(int i=0;i<detached.Count;i++)foreach(var child in referenceBones.Values.Where(c=>c.joint!=null&&c.joint.connectedBody==detached[i].rb).ToArray())if(!detached.Contains(child))detached.Add(child);
            foreach(var l in detached){
                severed.Add(l.key);partHealth[l.key]=0;l.rb.gravityScale=1;
                foreach(var pair in referenceBodies){var j=pair.Value.GetComponent<FixedJoint2D>();if(j!=null&&j.connectedBody==l.rb)DisableReferenceDriver(pair.Key);}
                if(l.key=="handNear")DisableReferenceArm("Near");if(l.key=="handFar")DisableReferenceArm("Far");
            }
            if(ReferenceDowned){referenceBodies["pelvisCOM"].mass=5;kickUntil=game.Clock;if(kickShape!=null)kickShape.enabled=false;}
            if(key=="handNear"&&primaryGrip!=null){primaryGrip.enabled=false;if(!weapon.TwoHanded||severed.Contains("handFar"))DropReferenceWeight("Near");}
            if(key=="handFar"){if(offhandGrip!=null)offhandGrip.enabled=false;if(supportJoint!=null)supportJoint.enabled=false;if(weapon.TwoHanded){if(severed.Contains("handNear"))DropReferenceWeight("Near");}else DropReferenceWeight("Far");}
            if(key=="head"||key=="neck")health=0;
        }
        public static float ReferenceDamage(string id,bool twoHanded,Vector2 velocity,float damageStat,float armor,bool handle,bool shield){
            return DuelReferenceRules.CalculateDamage(id,twoHanded,velocity,damageStat,armor,handle?DuelReferenceRules.Surface.WoodHandle:DuelReferenceRules.Surface.MetalBlade,shield).local;
        }
        void DrawReference(bool debug){
            foreach(var pair in referenceBones){var l=pair.Value;Color c=human?new Color(.35f,.53f,.51f):new Color(.64f,.38f,.30f);if(severed.Contains(pair.Key))c*=.65f;if(game.Clock<hitTint)c=new Color(.9f,.36f,.24f);float width=((CapsuleCollider2D)l.shape).size.x*Unit;game.Line(ScreenPoint(l.A),ScreenPoint(l.B),width,c);game.Circle(ScreenPoint(l.A),Vector2.one*width,c);game.Circle(ScreenPoint(l.B),Vector2.one*width,c);if(debug)DrawCollider(l.shape);}
            if(shield!=null&&HasShield)DrawWeapon(shield,true);if(weapon.id!="unarmed")DrawWeapon(sword,false);
            if(debug)foreach(var h in referenceHands.Values)game.Circle(ScreenPoint(h.target),Vector2.one*9,new Color(.9f,.7f,.1f,.8f));
        }
        J CaptureReference(){
            var parts=J.O();foreach(var p in partHealth)parts[p.Key]=J.O("health",p.Value,"severed",severed.Contains(p.Key));
            var hands=J.O();foreach(var p in referenceHands){var h=p.Value;hands[p.Key]=J.O("anchor_x",h.anchor.x,"anchor_y",h.anchor.y,"target_x",h.target.x,"target_y",h.target.y,"heat",h.heat,"drain",h.drain);}
            return J.O("profile",ReferenceProfile,"exhausted_left",ExhaustedLeft,"recovering_left",Mathf.Max(0,recoveryUntil-game.Clock),"wake_pending",exhaustedUntil>0,"effort_tick",effortTick,"walk_phase",walkPhase,"kick_left",Mathf.Max(0,kickUntil-game.Clock),"kick_state",CaptureReferenceKick(),"core_state",CaptureReferenceCore(),"source_motion",CaptureSourceMotion(),"death_state",CaptureReferenceDeath(),"parts",parts,"hands",hands,
                "weapon_rig",1,"weights",new J(referenceWeights.Select(p=>J.O("key",p.Key,"x",p.Value.position.x,"y",p.Value.position.y,"angle",p.Value.rotation,"vx",p.Value.linearVelocity.x,"vy",p.Value.linearVelocity.y,"spin",p.Value.angularVelocity)).ToList()),
                "bodies",new J(referenceBodies.Select(p=>J.O("key",p.Key,"x",p.Value.position.x,"y",p.Value.position.y,"angle",p.Value.rotation,"vx",p.Value.linearVelocity.x,"vy",p.Value.linearVelocity.y,"spin",p.Value.angularVelocity)).ToList()));
        }
        void RestoreReference(J s){
            foreach(var p in s["bodies"].A){var rb=referenceBodies[p["key"].S];rb.position=new Vector2((float)p["x"].N,(float)p["y"].N);rb.rotation=(float)p["angle"].N;rb.linearVelocity=new Vector2((float)p["vx"].N,(float)p["vy"].N);rb.angularVelocity=(float)p["spin"].N;}
            if(s["weapon_rig"].I==1){foreach(var p in s["weights"].A){var rb=referenceWeights[p["key"].S];rb.position=new Vector2((float)p["x"].N,(float)p["y"].N);rb.rotation=(float)p["angle"].N;rb.linearVelocity=new Vector2((float)p["vx"].N,(float)p["vy"].N);rb.angularVelocity=(float)p["spin"].N;}}
            else foreach(var rb in referenceWeights.Values){var j=rb.GetComponent<FixedJoint2D>();rb.position=Limb.BodyPoint(j.connectedBody,j.connectedAnchor);rb.rotation=j.connectedBody.rotation;rb.linearVelocity=j.connectedBody.GetPointVelocity(rb.position);rb.angularVelocity=j.connectedBody.angularVelocity;}
            RestoreReferenceCore(s["core_state"],s["parts"]);
            foreach(var p in s["hands"].D){var h=referenceHands[p.Key];h.anchor=new Vector2((float)p.Value["anchor_x"].N,(float)p.Value["anchor_y"].N);h.target=new Vector2((float)p.Value["target_x"].N,(float)p.Value["target_y"].N);h.heat=(float)p.Value["heat"].N;h.drain=(float)p.Value["drain"].N;h.active=false;}
            exhaustedUntil=s["wake_pending"].B?game.Clock+Mathf.Max(.00001f,(float)s["exhausted_left"].N):-10;recoveryUntil=game.Clock+(float)s["recovering_left"].N;effortTick=(float)s["effort_tick"].N;walkPhase=(float)s["walk_phase"].N;kickUntil=game.Clock+(float)s["kick_left"].N;
            RestoreReferenceKick(s);
            RestoreSourceMotion(s["source_motion"]);
            if(health>0)SetReferenceLimp(PassedOut);
            RestoreReferenceDeath(s["death_state"]);SampleReference();
        }
    }
    void ReferenceContact(DuelContact weapon,DuelContact other,Collision2D collision){
        if(referenceBindProbe){if(referenceProbeCollecting&&weapon.owner==player&&weapon.limb==player.sword&&other.owner==enemy&&(referenceProbeBody?other.kind!="sword"&&other.kind!="shield":other.limb==enemy.sword))referenceProbeContactEvents++;return;}
        if(!Live||weapon.owner==other.owner||collision.contactCount==0||weapon.owner.health<=0)return;
        if(weapon.kind!="sword"&&weapon.kind!="shield")return;
        var c=collision.GetContact(0);Vector2 velocity=c.relativeVelocity;float speed=velocity.magnitude;
        if(other.kind=="sword"||other.kind=="shield"){if(speed>15&&Clock-weapon.owner.lastClash>.1f){weapon.owner.lastClash=Clock;blocks++;Impact(c.point,true,speed);}return;}
        bool handle=collision.otherCollider!=weapon.limb.shape;
        var spec=weapon.limb.strikeProfile??weapon.owner.weapon;
        bool shield=weapon.kind=="shield"&&weapon.owner.HasShield;
        bool sharp=!shield&&spec.id!="unarmed"&&spec.id!="mace"&&spec.id!="warhammer";
        var material=handle?(spec.id=="axe"||spec.id=="greataxe"||spec.id=="spear"||spec.id=="mace"||spec.id=="warhammer"?DuelReferenceRules.Surface.WoodHandle:DuelReferenceRules.Surface.MetalHandle):sharp?DuelReferenceRules.Surface.MetalBlade:DuelReferenceRules.Surface.MetalBlunt;
        float threshold=(sharp?15:20)*(handle?.5f:1);
        if(speed<=threshold){rejectedRestContacts++;return;}
        if(!other.owner.AcceptReferenceImpact(other.limb.key))return;
        float stat=shield?1:spec.id=="unarmed"?.1f:spec.damage;
        var damage=DuelReferenceRules.CalculateDamage(spec.id,spec.TwoHanded,velocity,stat,other.owner.damageReduction*10,material,shield);
        float dealt=other.owner.ApplyReferenceDamage(other.limb.key,damage.local,c.point,material,damage.knockback);
        if(dealt<=0)return;hits++;weapon.owner.landedHits++;weapon.owner.lastHit=Clock;strongestImpact=Mathf.Max(strongestImpact,speed);if(other.kind=="head")headHits++;
        Impact(c.point,false,dealt);floating.Add(new FloatingText{p=c.point,value=Mathf.CeilToInt(dealt).ToString(),life=.85f,color=vermilion});
    }
}
