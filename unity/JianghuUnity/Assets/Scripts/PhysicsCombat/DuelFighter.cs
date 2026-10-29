using System;
using System.Collections.Generic;
using System.Linq;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    public sealed partial class Fighter {
        float StrokeDuration=>.94f/ArmSpeed;
        public readonly DuelWeapon weapon;
        readonly PhysicsDuel game;
        readonly bool human;
        readonly List<J> parts;
        readonly Color elbowTone;
        public GameObject root;
        public J skin,rig;
        public Texture atlas;
        public float scale,facing,health=120,stamina=100,stunUntil,hitTint,lastHit=-10,lastClash=-10;
        public Limb torso,pelvis,head,sword,shield;
        public readonly Dictionary<string,Limb> limbs=new Dictionary<string,Limb>();
        public readonly List<Collider2D> colliders=new List<Collider2D>();
        public readonly Queue<Vector2> trail=new Queue<Vector2>();
        public readonly Dictionary<string,FootPlant> feet=new Dictionary<string,FootPlant>();
        sealed class VictimContact {public int stroke=-1;public float touched=-10;public bool separated=true;}
        readonly Dictionary<Fighter,VictimContact> hitVictims=new Dictionary<Fighter,VictimContact>();
        readonly Dictionary<Fighter,VictimContact> offhandVictims=new Dictionary<Fighter,VictimContact>();
        public float move,slashAt=-10,strength=1,balance=100;
        public int attackId,attackType;
        public bool swordControl,shieldControl,brace,testOverride;
        public Vector2 pointer;
        public float maxFootSlip,peakAngularSpeed,peakSpeed;
        public string action="守势";
        public string worstJoint="";
        public float guardBrokenUntil;
        bool SwordWheel=>game.Live&&(aiDriving?aiMain.sqrMagnitude>.0064f:MouseMain||human&&game.controls.right.HasDirection);
        bool ShieldWheel=>game.Live&&(aiDriving?aiOff.sqrMagnitude>.0064f:MouseOff||human&&game.controls.left.HasDirection);
        Vector2 MainStick=>aiDriving?aiMain:MouseMain?game.controls.mouse.main.value:game.controls.right.value;
        Vector2 OffStick=>aiDriving?aiOff:MouseOff?game.controls.mouse.off.value:game.controls.left.value;
        float worstLimit;
        float weight,waistHeight,driveMove,heading=75,guardHeading,lastDash=-10,lastStep=-1;
        float injuredUntil;
        float supportBlend=.7f;
        float stanceOffset;
        bool lastSteppedNear;

        public Fighter(PhysicsDuel game,J catalog,string id,float x,float dir,bool human,DuelWeapon weaponSpec=null,J fighterSpec=null) {
            this.game=game;this.human=human;facing=dir;guardHeading=dir>0?0:180;
            weapon=weaponSpec??DuelWeapon.All[DuelWeapon.SwordIndex];
            if(fighterSpec!=null){displayName=fighterSpec["name"].S;maxHealth=(float)fighterSpec["vigor_max"].N;health=(float)fighterSpec["vigor"].N;stamina=(float)fighterSpec["stamina"].N;damageScale=(float)fighterSpec["damage_scale"].N;damageReduction=(float)fighterSpec["damage_reduction"].N;handlingScale=(float)fighterSpec["handling_scale"].N;HasShield=fighterSpec["shield"].B;}
            LoadAttributes(fighterSpec);
            if(weapon.TwoHanded)heading=weapon.readyAngle;
            if(!ModularTrial&&!LaceTrial&&!PaperAppearanceArt.IsLook(id))id=CharacterLook.ForPerson(id,J.O("sex",id.StartsWith("f-")?"女":"男","age_months",28*12));
            rig=catalog["rig"];skin=PaperAppearanceArt.Skin(catalog,id);atlas=Resources.Load<Texture2D>("PaperCast/"+(skin["body_id"].Has?skin["body_id"].S:id));
            elbowTone=PaperAppearanceArt.SkinTone(skin,id.StartsWith("m-")?new Color(.86f,.73f,.55f):new Color(.92f,.80f,.62f));
            scale=heightScale*3.5f/((float)skin["ground"].N+(float)skin["lengths"]["spine"].N+(float)skin["parts"]["head-neck"]["height"].N);
            parts=skin["parts"].D.Values.Where(p=>!p["hidden"].B).OrderBy(p=>p["layer"].N).ToList();
            root=new GameObject(human?"玩家 · physical skeleton":"对手 · physical skeleton");root.transform.SetParent(game.transform);
            if(game.ReferenceCombat){BuildReference(x);return;}
            waistHeight=(float)skin["ground"].N*scale;
            Vector2 waist=new Vector2(x,waistHeight);
            Vector2 neck=waist+Local(skin["bind_joints"]["neck"]);
            Vector2 hips=waist+(Local(skin["bind_joints"]["hipNear"])+Local(skin["bind_joints"]["hipFar"]))/2;
            pelvis=Make("pelvis",hips,waist,.43f,3.2f,"body");
            torso=Make("torso",waist,neck,.37f,4.4f,"body");
            Taper(torso,new[]{new Vector2(-.16f,-torso.length/2-.025f),new Vector2(.16f,-torso.length/2-.025f),new Vector2(.22f,torso.length*.24f),new Vector2(.16f,torso.length/2),new Vector2(-.16f,torso.length/2),new Vector2(-.22f,torso.length*.24f)});
            Join(torso,pelvis,waist,-24,24);
            head=Make("head",neck,neck+Vector2.up*(.54f*heightScale),.33f,1.0f,"head");
            Join(head,torso,neck,-32,32);
            foreach(string side in new[]{"Far","Near"}) {
                J bind=skin["bind_joints"];
                Vector2 shoulder=waist+Local(bind["shoulder"+side]);
                float upper=(float)skin["lengths"]["upper_arm"+side].N*scale*armSpanScale,lower=(float)skin["lengths"]["forearm"+side].N*scale*armSpanScale;
                Vector2 elbow=shoulder+new Vector2(facing*.47f,-.28f).normalized*upper;
                Vector2 wrist=elbow+new Vector2(facing*.62f,.38f).normalized*lower;
                if(weapon.TwoHanded)Solve(shoulder,InitialTwoHandWrist(side,waist),upper,lower,-facing,out elbow,out wrist,.002f);
                Limb arm=Make("upper"+side,shoulder,elbow,.18f,.58f,"arm");Join(arm,torso,shoulder,-180,180).useLimits=false;
                Limb fore=Make("forearm"+side,elbow,wrist,.145f,.45f,"arm");
                JoinAnatomical(fore,arm,elbow,facing>0?0:-150,facing>0?150:0);
                Vector2 grip=wrist+(weapon.TwoHanded?ReadyDirection*.14151f:new Vector2(facing*.075f,.12f));
                Limb hand=Make("hand"+side,wrist,grip,.13f,.45f,"hand");JoinAnatomical(hand,fore,wrist,facing>0?-85:-65,facing>0?65:85);
                Vector2 h=waist+Local(bind["hip"+side]),k=waist+Local(bind["knee"+side]),a=waist+Local(bind["ankle"+side]);
                Limb thigh=Make("thigh"+side,h,k,.23f,1.2f,"leg");Join(thigh,pelvis,h,-65,65);
                Limb shin=Make("shin"+side,k,a,.165f,.75f,"leg");JoinAnatomical(shin,thigh,k,facing>0?-145:-4,facing>0?4:145);
                float sole=(float)skin["sole"][side].N*scale;
                Limb foot=Make("foot"+side,a+new Vector2(-facing*.11f,-sole*.56f),a+new Vector2(facing*.20f,-sole*.56f),sole*.88f,.38f,"foot");
                var footShape=(CapsuleCollider2D)foot.shape;footShape.direction=CapsuleDirection2D.Vertical;
                foot.shape.sharedMaterial=game.footMaterial;
                Join(foot,shin,a,-32,32);
                var support=foot.rb.gameObject.AddComponent<TargetJoint2D>();support.autoConfigureTarget=false;
                support.anchor=Limb.BodyLocalPoint(foot.rb,a);support.frequency=6;support.dampingRatio=1;support.maxForce=0;
                feet[side]=new FootPlant(a,sole-.002f,support);
            }
            stanceOffset=waist.x-(feet["Near"].anchor.x+feet["Far"].anchor.x)/2;
            EquipWeapon();
            if(weapon.TwoHanded)AttachSupportHand();
            else if(HasShield) {
                Vector2 shieldGrip=limbs["handFar"].B;
                shield=MakeAttachment("shield",shieldGrip+Vector2.down*.43f,shieldGrip+Vector2.up*.43f,.25f,1.05f,"shield",limbs["handFar"]);
                var shieldBox=ReplaceBox(shield,new Vector2(.25f,.86f),Vector2.zero);shieldBox.edgeRadius=.025f;shieldBox.sharedMaterial=game.shieldMaterial;
            }
            if(!weapon.TwoHanded&&!HasShield)shield=EquipFist("shield",limbs["handFar"],out offhandGrip);
            CreateHandTargets();
            foreach(var a in colliders)foreach(var b in colliders)if(a!=b)DuelPhysicsAPI.IgnoreCollision(a,b);
            weight=limbs.Values.Select(l=>l.rb).Distinct().Sum(rb=>rb.mass);
            pelvis.driveInertia=weapon.TwoHanded?.60f:.30f;
            foreach(var limb in limbs.Values)limb.SampleVelocity();
            CaptureCharacterBindingPose();
        }
        Vector2 Local(J point)=>new Vector2((float)point[0].N*facing,-(float)point[1].N)*scale;
        Limb Make(string key,Vector2 a,Vector2 b,float width,float mass,string kind) {
            var go=new GameObject(key);go.transform.SetParent(root.transform);go.transform.position=(a+b)/2;go.transform.rotation=Quaternion.Euler(0,0,Angle(b-a));
            go.layer=kind=="foot"?11:kind=="sword"||kind=="shield"?10:9;
            bool bodyPart=kind!="sword"&&kind!="shield";
            var rb=go.AddComponent<Rigidbody2D>();rb.mass=mass*(bodyPart?bodyMassScale:1);rb.linearDamping=.12f;rb.angularDamping=.4f;rb.interpolation=RigidbodyInterpolation2D.Interpolate;rb.collisionDetectionMode=CollisionDetectionMode2D.Continuous;rb.sleepMode=RigidbodySleepMode2D.NeverSleep;
            if(bodyPart)width*=heightScale;
            var col=go.AddComponent<CapsuleCollider2D>();col.size=new Vector2(width,Mathf.Max(width,(a-b).magnitude));col.sharedMaterial=game.bodyMaterial;
            var contact=go.AddComponent<DuelContact>();contact.owner=this;contact.kind=kind;
            var limb=new Limb {key=key,rb=rb,length=(a-b).magnitude,shape=col};limbs[key]=limb;colliders.Add(col);contact.limb=limb;return limb;
        }
        void RemoveShape(Limb l) {colliders.Remove(l.shape);l.shape.enabled=false;UnityEngine.Object.Destroy(l.shape);}
        Limb MakeAttachment(string key,Vector2 a,Vector2 b,float width,float mass,string kind,Limb hand) {
            var go=new GameObject(key);go.transform.SetParent(hand.rb.transform);go.transform.position=(a+b)/2;go.transform.rotation=Quaternion.Euler(0,0,Angle(b-a));go.layer=10;
            var col=go.AddComponent<CapsuleCollider2D>();col.size=new Vector2(width,(a-b).magnitude);
            var limb=new Limb {key=key,rb=hand.rb,frame=go.transform,length=(a-b).magnitude,shape=col};
            var contact=go.AddComponent<DuelContact>();contact.owner=this;contact.kind=kind;contact.limb=limb;
            hand.rb.mass+=mass;limbs[key]=limb;colliders.Add(col);return limb;
        }
        BoxCollider2D ReplaceBox(Limb l,Vector2 size,Vector2 offset) {RemoveShape(l);var c=l.Frame.gameObject.AddComponent<BoxCollider2D>();c.size=size;c.offset=offset;l.shape=c;colliders.Add(c);return c;}
        void Taper(Limb l,Vector2[] points) {RemoveShape(l);var c=l.rb.gameObject.AddComponent<PolygonCollider2D>();c.points=points;c.sharedMaterial=game.bodyMaterial;l.shape=c;colliders.Add(c);}
        static HingeJoint2D Join(Limb child,Limb parent,Vector2 p,float min,float max) {
            var j=child.rb.gameObject.AddComponent<HingeJoint2D>();j.connectedBody=parent.rb;j.autoConfigureConnectedAnchor=false;
            j.anchor=Limb.BodyLocalPoint(child.rb,p);j.connectedAnchor=Limb.BodyLocalPoint(parent.rb,p);j.useLimits=true;j.limits=new JointAngleLimits2D {min=min,max=max};
            child.joint=j;return j;
        }
        static void JoinAnatomical(Limb child,Limb parent,Vector2 p,float min,float max) {
            var j=Join(child,parent,p,-180,180);
            float initial=Mathf.DeltaAngle(parent.rb.rotation,child.rb.rotation);
            // jointAngle starts at zero; convert anatomical relative angles to its reference frame.
            j.limits=new JointAngleLimits2D {min=initial-max,max=initial-min};
        }
        public static float Angle(Vector2 d)=>Mathf.Atan2(d.y,d.x)*Mathf.Rad2Deg-90;
        public Vector2 Hip=>pelvis.B;
        public Vector2 Joint(string key) {
            if(key=="hip")return Hip;if(key=="neck")return torso.B;
            if(key.StartsWith("shoulder"))return limbs["upper"+key.Substring(8)].A;
            if(key.StartsWith("elbow"))return limbs["upper"+key.Substring(5)].B;
            if(key.StartsWith("wrist"))return limbs["hand"+key.Substring(5)].A;
            if(key.StartsWith("hip"))return limbs["thigh"+key.Substring(3)].A;
            if(key.StartsWith("knee"))return limbs["thigh"+key.Substring(4)].B;
            if(key.StartsWith("ankle"))return limbs["shin"+key.Substring(5)].B;
            return Hip;
        }
        public void ReadInput(Fighter target,bool auto) {
            if(testOverride)return;
            if(auto){ReadAI(target,game.training);return;}
            aiDriving=false;
            move=Mathf.Clamp(game.controls.MoveAxis+(Input.GetKey(KeyCode.D)||Input.GetKey(KeyCode.RightArrow)?1:0)-(Input.GetKey(KeyCode.A)||Input.GetKey(KeyCode.LeftArrow)?1:0),-1,1);
            swordControl=false;shieldControl=Input.GetKey(KeyCode.K);brace=Input.GetKey(KeyCode.S)||Input.GetKey(KeyCode.DownArrow);
            if(!Reference&&Input.GetKeyDown(KeyCode.J)){game.controls.mouse.Forget(true);Swing();}
            if(!Reference&&Input.GetKeyDown(KeyCode.L)){game.controls.mouse.Forget(true);Swing(1);}
            if(Input.GetKeyDown(KeyCode.Space)&&!ModularTrial||game.controls.DashPressed)Dash();
        }
        VictimContact ContactHistory(Fighter victim,Limb item){var victims=item==shield&&!HasShield?offhandVictims:hitVictims;if(!victims.TryGetValue(victim,out var history)){history=new VictimContact();victims[victim]=history;}return history;}
        public void TrackStrikeContact(Fighter victim,Limb item){var history=ContactHistory(victim,item);if(game.Clock-history.touched>.08f)history.separated=true;history.touched=game.Clock;}
        public bool RegisterHit(Fighter victim,int stroke,Limb item=null){var history=ContactHistory(victim,item);if(!history.separated)return false;history.stroke=stroke;history.separated=false;return true;}
        public void Swing(int type=-1){if(game.Clock-slashAt<StrokeDuration||!CommitAttack(.86f/ArmSpeed))return;attackType=type<0?(attackType+1)%3:type;slashAt=game.Clock;}
        public bool GuardImpact(float speed,bool bladeClash) {
            float cost=Mathf.Clamp(2+speed*.8f,3,11)*(bladeClash?.45f:1);
            SpendEffort(cost,.9f);balance=Mathf.Max(0,balance-cost*.3f*balanceLossScale);
            if(!bladeClash&&StaminaRatio<.06f*balanceLossScale&&speed>3*stabilityScale&&game.Clock>guardBrokenUntil){guardBrokenUntil=game.Clock+.65f*stunScale;balance=Mathf.Max(0,balance-18*balanceLossScale);return true;}
            return false;
        }
        public void ClearMouseCapture(){ResetGesture();aiDriving=false;aiMain=aiOff=Vector2.zero;swordControl=shieldControl=false;}
        public bool Dash(){if(Reference)return KickReference();if(stamina<18*effortScale||health<=0||game.Clock<stunUntil||!game.Live||game.Clock-lastDash<.6f/movementScale)return false;SpendEffort(18,.8f);lastDash=game.Clock;pelvis.rb.CombatAddForce(new Vector2(facing*8*movementScale,0),ForceMode2D.Impulse);return true;}
        public void Hit(float damage,Vector2 impulse,Vector2 contact) {
            InterruptAttack();regenAt=Mathf.Max(regenAt,game.Clock+.85f*recoveryDelayScale);
            health=Mathf.Max(0,health-damage);balance=Mathf.Max(0,balance-damage*1.7f*balanceLossScale);
            stunUntil=game.Clock+(.12f+damage*.006f)*stunScale;injuredUntil=game.Clock+injuryDuration;hitTint=game.Clock+.15f;
            pelvis.rb.CombatAddForce(impulse*.40f,ForceMode2D.Impulse);torso.rb.CombatAddForceAtPosition(impulse*.60f,contact,ForceMode2D.Impulse);
        }
        public void Step(bool active,Fighter target) {
            if(Reference){StepReference(active,target);return;}
            if(health<=0){action="倒地";RelaxHandDrives();foreach(var l in limbs.Values)if(l.joint!=null)l.joint.useMotor=false;foreach(var foot in feet.Values)foot.Release();Sample();return;}
            if(!active){move=0;swordControl=shieldControl=false;}
            float dt=Time.fixedDeltaTime;
            AdvanceMouseTargets(dt);
            balance=Mathf.MoveTowards(balance,maxBalance,dt*balanceRegen);
            float wantedStrength=game.Clock<injuredUntil?injuryStrength:Mathf.Lerp(.65f,1,BalanceRatio);
            strength=Mathf.MoveTowards(strength,wantedStrength,dt*(wantedStrength<strength?7:strengthRecovery));
            Regenerate(dt);
            float command=game.Clock-lastDash<.35f?facing:move;
            // Brake before the shoulders reach the wall. Repeatedly driving
            // the hips into it can trap the outside elbow / stretch a grip.
            if(Mathf.Abs(command)>.001f)command*=Mathf.Clamp01((5.25f-Hip.x*Mathf.Sign(command))/.45f);
            if(Hip.x>5.35f)command=Mathf.Min(command,Mathf.Clamp((5.35f-Hip.x)*3,-1,0));
            else if(Hip.x< -5.35f)command=Mathf.Max(command,Mathf.Clamp((-5.35f-Hip.x)*3,0,1));
            float moveResponse=Mathf.Abs(command)<.01f||command*driveMove<0?14:9;
            driveMove=Mathf.MoveTowards(driveMove,command,dt*moveResponse*movementScale);
            UpdateGait(dt);
            bool supported=feet.Values.Any(f=>f.grounded);
            supportBlend=Mathf.MoveTowards(supportBlend,supported?1:0,dt*(supported?8:2.5f));
            float targetY=waistHeight-pelvis.length/2-(brace?.14f:0);
            // Leave weight on the soles instead of canceling all gravity at
            // the pelvis. Fade support rather than toggling it on contact jitter.
            float vertical=Mathf.Clamp(((targetY-pelvis.rb.position.y)*620-pelvis.rb.linearVelocity.y*95)*(.4f+.6f*supportBlend)+weight*18*.72f*supportBlend,-330,650);
            float desiredVelocity=driveMove*(brace?1.05f:1.8f)*movementScale;
            float center=0;int planted=0;
            foreach(var f in feet.Values)if(!f.stepping&&f.grounded){center+=f.anchor.x;planted++;}
            if(planted>0) {
                float supportCenter=center/planted+stanceOffset;
                if(Mathf.Abs(driveMove)<.08f)desiredVelocity+=Mathf.Clamp((supportCenter-Hip.x)*4,-1,1);
                else {
                    float ahead=(Hip.x-supportCenter)*Mathf.Sign(driveMove);
                    desiredVelocity*=Mathf.Clamp01((.55f-ahead)/.25f);
                }
            }
            pelvis.rb.CombatAddForce(new Vector2((desiredVelocity-pelvis.rb.linearVelocity.x)*60,vertical)*strength*stabilityScale);
            float lean=-driveMove*5+facing*(IsAttacking?-3:0);
            pelvis.Drive(lean*.45f,5.5f,weapon.TwoHanded?230:130,strength*stabilityScale);
            torso.Drive(lean,5.2f,weapon.TwoHanded?150:84,strength*stabilityScale);head.Drive(lean*.4f,5.0f,5,strength*stabilityScale);
            foreach(string side in new[]{"Far","Near"}) {
                Limb thigh=limbs["thigh"+side],shin=limbs["shin"+side],foot=limbs["foot"+side];
                // Solve the intended posture height, not only the current hip.
                // Otherwise a straight leg keeps requesting a straight leg and
                // prevents the pelvis from settling into a two-foot stance.
                float postureOffset=feet[side].stepping?0:Mathf.Clamp(targetY-pelvis.rb.position.y,-.16f,.12f);
                Vector2 poseHip=thigh.A+Vector2.up*postureOffset;
                Solve(poseHip,feet[side].goal,thigh.length,shin.length,facing,out Vector2 knee,out Vector2 ankle);
                thigh.Drive(Angle(knee-poseHip),7,40,strength*stabilityScale);shin.Drive(Angle(ankle-knee),7,26,strength*stabilityScale);foot.Drive(-facing*90,5,8,strength*stabilityScale);
            }
            ControlArms(dt,target);
            TickWeaponEffort(dt);
            trail.Enqueue(sword.B);while(trail.Count>8)trail.Dequeue();
            Sample();
        }
        void UpdateGait(float dt) {
            foreach(string side in new[]{"Far","Near"}) {
                var f=feet[side];Limb foot=limbs["foot"+side];
                f.grounded=game.Clock-foot.lastGroundContact<.025f;
                if(f.state==FootPlant.Phase.Landing) {
                    Limb thigh=limbs["thigh"+side],shin=limbs["shin"+side];
                    float reach=thigh.length+shin.length-.015f,dy=thigh.A.y-f.soleHeight;
                    float sideways=Mathf.Sqrt(Mathf.Max(.0025f,reach*reach-dy*dy))*.9f;
                    f.target.x=Mathf.Clamp(f.target.x,thigh.A.x-sideways,thigh.A.x+sideways);
                }
                f.Update(dt,Joint("ankle"+side),foot.rb.linearVelocity.y,weight*18*.35f*(brace?1.3f:1)*stabilityScale,strength,movementScale);
                maxFootSlip=Mathf.Max(maxFootSlip,f.maxSlip);
            }
            if(feet.Values.Any(f=>f.stepping))return;
            string next=lastSteppedNear?"Far":"Near";
            float velocity=Mathf.Lerp(pelvis.rb.linearVelocity.x,driveMove*1.8f*movementScale,.6f);
            float stride=Mathf.Abs(driveMove)>.1f?.31f:.42f;
            float ideal=Hip.x+(next=="Near"?-.20f:.20f)+velocity*.20f;
            string other=next=="Near"?"Far":"Near";
            float otherIdeal=Hip.x+(other=="Near"?-.20f:.20f)+velocity*.20f;
            if(Mathf.Abs(feet[next].anchor.x-ideal)<stride&&Mathf.Abs(feet[other].anchor.x-otherIdeal)>stride){next=other;ideal=otherIdeal;}
            if(Mathf.Abs(feet[next].anchor.x-ideal)>stride||game.Clock-lastStep>.8f/movementScale&&Mathf.Abs(driveMove)>.1f) {
                float stepX=Mathf.Abs(driveMove)>.1f?Hip.x+Mathf.Sign(driveMove)*.38f+velocity*.20f+(next=="Near"?-.12f:.12f):ideal;
                Vector2 physical=Joint("ankle"+next);
                stepX=Mathf.Clamp(stepX,physical.x-.55f,physical.x+.55f);
                feet[next].Start(new Vector2(Mathf.Clamp(stepX,-5.6f,5.6f),0),physical);lastSteppedNear=next=="Near";lastStep=game.Clock;
            }
        }
        void ControlArms(float dt,Fighter target) {
            Vector2 shoulder=limbs["upperNear"].A;
            bool mainIntent=SwordWheel||swordControl;
            bool manualSword=mainIntent||(weapon.TwoHanded||!HasShield)&&ShieldWheel;
            float t=(game.Clock-slashAt)*ArmSpeed,desired=weapon.readyAngle,reach=.66f;
            if(weapon.TwoHanded&&!manualSword&&t>=.94f&&(shieldControl||ShieldWheel))desired=weapon.id=="spear"?55:75;
            if(mainIntent) {
                Vector2 aim=SwordWheel?MainStick:pointer-shoulder;
                desired=Mathf.Atan2(aim.y,aim.x*facing)*Mathf.Rad2Deg;
                float full=limbs["upperNear"].length+limbs["forearmNear"].length-.002f;
                reach=MouseMain?full*Mathf.Clamp(aim.magnitude,DuelMouseHands.MinimumReach,1):SwordWheel?Mathf.Lerp(full*.44f,full,Mathf.InverseLerp(DuelControls.Pad.Deadzone,1,aim.magnitude)):Mathf.Clamp(aim.magnitude*.85f,full*.44f,full);
                action=MouseMain?game.controls.mouse.IsControlled(true)?"主手运械":game.controls.mouse.main.Coasting?"主手续势":"主手保持":SwordWheel?"轮盘运剑":"鼠标运剑";
            } else if(t<.94f) {
                if(attackType==1) {desired=10;reach=t<.18f?.43f:t<.46f?Mathf.Lerp(.43f,1.03f,(t-.18f)/.28f):Mathf.Lerp(1.03f,.75f,(t-.46f)/.48f);action=t<.18f?"蓄势":t<.46f?"直刺":"收剑";}
                else {float from=attackType==0?100:-60,to=attackType==0?-50:80;
                    desired=t<.18f?from:t<.70f?Mathf.Lerp(from,to,Mathf.SmoothStep(0,1,(t-.18f)/.52f)):Mathf.Lerp(to,75,(t-.70f)/.24f);
                    reach=t<.18f?.65f:.92f;action=t<.18f?"蓄势":t<.70f?attackType==0?"劈斩":"挑斩":"收剑";}
            } else action=shieldControl?"架盾":Mathf.Abs(driveMove)>.1f?"移步":"守势";
            heading=desired;
            Vector2 dir=new Vector2(facing*Mathf.Cos(heading*Mathf.Deg2Rad),Mathf.Sin(heading*Mathf.Deg2Rad));
            // Carry the cut with the whole arm. Holding the hand at a fixed
            // point while rotating the blade demands an impossible wrist bend.
            Vector2 goal=shoulder+dir*reach;
            if(!manualSword&&t>=.94f)goal=shoulder+new Vector2(facing*.80f,.06f);
            if(weapon.TwoHanded) {
                if(!manualSword&&t>=.94f)goal=shoulder+new Vector2(facing*.62f,-.20f);
                ControlTwoHandArms(goal,dir,manualSword);return;
            }
            Arm("Near",goal,ArmPower,7,manualSword?46:36,ArmSpeed);
            // Rigid grip: hand rotation steers the attached blade; no free sword hinge.
            float swordOffset=Mathf.DeltaAngle(limbs["handNear"].rb.rotation,sword.Rotation);
            float mouseOffset=MouseMain?game.controls.mouse.main.angleOffset:0;
            limbs["handNear"].Drive(Angle(dir)+mouseOffset-swordOffset,6,12,ArmPower,ArmSpeed);
            Vector2 far=limbs["upperFar"].A;
            bool broken=game.Clock<guardBrokenUntil;
            float guardStrength=broken?.24f:Mathf.Lerp(.5f,1,Mathf.Clamp01(StaminaRatio/.25f));
            Vector2 shieldGoal=far+new Vector2(facing*(shieldControl?.78f:.58f),broken?-.4f:shieldControl?-.20f:-.43f);
            float shieldAngle=HasShield?0:limbs["forearmFar"].rb.rotation;
            if(ShieldWheel) {
                Vector2 aim=OffStick;
                float full=limbs["upperFar"].length+limbs["forearmFar"].length-.002f;
                float extension=MouseOff?full*Mathf.Clamp(aim.magnitude,DuelMouseHands.MinimumReach,1):Mathf.Lerp(full*.44f,full,Mathf.InverseLerp(DuelControls.Pad.Deadzone,1,aim.magnitude));
                float desiredGuard=Mathf.Atan2(aim.y,aim.x)*Mathf.Rad2Deg;
                guardHeading=desiredGuard;
                Vector2 guardDir=new Vector2(Mathf.Cos(guardHeading*Mathf.Deg2Rad),Mathf.Sin(guardHeading*Mathf.Deg2Rad));
                shieldGoal=far+guardDir*extension;
                // A shield stands across the forearm; a fist follows the
                // punch direction. Sharing the shield's 90 degree offset
                // makes an unarmed wrist drive against its anatomical stop.
                shieldAngle=Angle(guardDir)+(HasShield?facing*90:0)+(MouseOff?game.controls.mouse.off.angleOffset:0);
            } else guardHeading=facing>0?0:180;
            Arm("Far",shieldGoal,ArmPower*guardStrength,7,32,ArmSpeed);
            float shieldOffset=Mathf.DeltaAngle(limbs["handFar"].rb.rotation,shield.Rotation);
            limbs["handFar"].Drive(shieldAngle-shieldOffset,6,16,ArmPower*guardStrength,ArmSpeed);
            if(broken)action="架势失衡";
        }
        void Arm(string side,Vector2 goal,float control,float frequency,float torque,float speedScale=1) {
            Limb a=limbs["upper"+side],b=limbs["forearm"+side];
            // Keep the hand outside the folded-arm singularity. Elbows retain
            // one bend direction, with reserve before the physical stop.
            Vector2 delta=goal-a.A;
            float minimum=Mathf.Sqrt(a.length*a.length+b.length*b.length+2*a.length*b.length*Mathf.Cos(132*Mathf.Deg2Rad));
            float requested=Mathf.Clamp(delta.magnitude,minimum,a.length+b.length-.002f);
            float extension=requested;
            Vector2 direction=delta.sqrMagnitude>.00001f?delta.normalized:new Vector2(facing,0);
            Solve(a.A,a.A+direction*extension,a.length,b.length,-facing,out Vector2 elbow,out Vector2 wrist,.002f);
            DriveHand(side,wrist,torque*(side=="Far"?4.5f:3)*control,speedScale);
        }
        void Sample(){foreach(var limb in limbs.Values){peakAngularSpeed=Mathf.Max(peakAngularSpeed,Mathf.Abs(limb.rb.angularVelocity));peakSpeed=Mathf.Max(peakSpeed,limb.rb.linearVelocity.magnitude);limb.rb.angularVelocity=Mathf.Clamp(limb.rb.angularVelocity,-720,720);limb.SampleVelocity();}}
        static void Solve(Vector2 a,Vector2 goal,float upper,float lower,float pole,out Vector2 knee,out Vector2 tip,float extensionMargin=.012f) {
            Vector2 d=goal-a;float len=Mathf.Clamp(d.magnitude,Mathf.Abs(upper-lower)+.006f,upper+lower-extensionMargin);Vector2 axis=d.sqrMagnitude>.00001f?d.normalized:Vector2.down;
            float along=(upper*upper-lower*lower+len*len)/(2*len),bend=Mathf.Sqrt(Mathf.Max(0,upper*upper-along*along));
            knee=a+axis*along+new Vector2(-axis.y,axis.x)*bend*pole;tip=a+axis*len;
        }
        public float MaxJointGap(){var connected=SourceRigActive?SourceLiveBodies():null;float gap=0;foreach(var j in root.GetComponentsInChildren<AnchoredJoint2D>())if(j.enabled&&(connected==null||InSourceLiveComponent(j,connected))){float value=MeasuredJointGap(j);if(value>gap){gap=value;worstJoint=j.name;}}return gap;}
        public string worstLimitJoint="";
        public float MaxLimitError(){var connected=SourceRigActive?SourceLiveBodies():null;float e=0;foreach(var j in root.GetComponentsInChildren<HingeJoint2D>())if(j.enabled&&j.useLimits&&(connected==null||InSourceLiveComponent(j,connected))){float v=MeasuredLimitError(j);e=Mathf.Max(e,v);if(v>worstLimit){worstLimit=v;worstLimitJoint=j.name+" angle="+j.CombatJointAngle()+" limits="+j.limits.min+","+j.limits.max;}}return e;}
    }
}
