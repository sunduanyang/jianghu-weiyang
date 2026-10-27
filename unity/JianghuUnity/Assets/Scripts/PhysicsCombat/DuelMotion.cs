using UnityEngine;

public sealed partial class PhysicsDuel {
    public sealed partial class Fighter {
        // A stroke belongs to a moving hand, not to a pointer angle or a timer.
        // Contacts retain this identity until a real reversal / new sweep.
        sealed class MotionStroke {
            public int id;
            public bool active;
            public float started,quiet,path;
            public Vector2 direction;
            public void Clear(){active=false;quiet=path=0;}
        }
        readonly MotionStroke mainStroke=new MotionStroke(),offStroke=new MotionStroke();
        bool motionExhausted;
        public int manualStrokeCount;
        public float mainMotionSpent,offMotionSpent;
        public float pressureStaminaSpent;
        float mainResistanceUntil=-10,offResistanceUntil=-10,currentPressureRate;
        bool MainManual=>SwordWheel||swordControl||weapon.TwoHanded&&ShieldWheel;
        bool OffManual=>!weapon.TwoHanded&&!HasShield&&ShieldWheel;

        Vector2 StrikeVelocity(Limb item){
            var profile=item.strikeProfile??weapon;
            Vector2 point=item.WorldPoint(profile.Local(new Vector2(0,Mathf.Lerp(profile.hitStart,profile.reach,.65f)),facing));
            return item.rb.GetPointVelocity(point)-pelvis.rb.GetPointVelocity(point);
        }
        public void RecordWeaponResistance(Limb item){if(item==shield&&!HasShield)offResistanceUntil=game.Clock+.12f;else mainResistanceUntil=game.Clock+.12f;}
        float WeaponEffort(Limb item,bool controlled,out float pressure){
            pressure=0;
            if(!controlled||motionExhausted)return 0;
            var profile=item.strikeProfile??weapon;
            float speed=StrikeVelocity(item).magnitude;
            bool off=item==shield&&!HasShield;
            if(game.Clock<(off?offResistanceUntil:mainResistanceUntil)){
                float error=weapon.TwoHanded?(HandTargetError("Near")+HandTargetError("Far"))*.5f:HandTargetError(off?"Far":"Near");
                // A bound weapon can have zero speed while the hands are still
                // pushing hard. Account for this load instead of free pressure.
                pressure=Mathf.Clamp01((error-.12f)/.45f)*Mathf.Sqrt(profile.mass)*2.8f;
            }
            return Mathf.Min(12,Mathf.Max(0,speed-1.25f)*.85f*Mathf.Sqrt(profile.mass)*Mathf.Sqrt(profile.cost/9))+pressure;
        }
        float ShieldEffort(){
            if(!ShieldWheel&&!shieldControl||motionExhausted)return 0;
            Vector2 point=shield.B;
            float speed=(shield.rb.GetPointVelocity(point)-pelvis.rb.GetPointVelocity(point)).magnitude;
            return Mathf.Min(3,Mathf.Max(0,speed-1.5f)*.32f);
        }

        void UpdateManualStrokes(float dt){
            if(StaminaRatio<=.005f)motionExhausted=true;
            else if(StaminaRatio>=.15f)motionExhausted=false;
            // These identities describe motion for logs / duplicate contacts.
            // They do not authorize damage and never require stamina payment.
            bool allowed=game.Live&&health>0;
            UpdateStroke(mainStroke,sword,allowed,dt);
            if(!weapon.TwoHanded&&!HasShield&&shield!=null)UpdateStroke(offStroke,shield,allowed,dt);
            else offStroke.Clear();
        }
        void UpdateStroke(MotionStroke stroke,Limb item,bool commanded,float dt){
            Vector2 velocity=StrikeVelocity(item);float speed=velocity.magnitude;
            if(!commanded){stroke.Clear();return;}
            // Hysteresis lets a real swing pass a low-speed contact without
            // repeatedly arming on contact solver noise. Holding still is free.
            if(speed<(stroke.active?.65f:1.05f)){
                stroke.quiet+=dt;if(stroke.quiet>.09f)stroke.Clear();return;
            }
            stroke.quiet=0;stroke.path+=speed*dt;
            Vector2 direction=velocity/speed;
            bool reversed=stroke.path>.18f&&Vector2.Dot(stroke.direction,direction)<-.25f;
            bool swept=stroke.path>Mathf.Max(.5f,(item.strikeProfile??weapon).reach*Mathf.PI);
            if(!stroke.active||(reversed||swept)&&game.Clock-stroke.started>.18f/weapon.handling){
                stroke.id=++attackId;manualStrokeCount++;stroke.started=game.Clock;
                stroke.path=0;stroke.direction=direction;stroke.active=true;
            }
        }
        public int StrikeId(Limb item)=>item==shield&&!HasShield?offStroke.active?offStroke.id:0:mainStroke.active?mainStroke.id:buttonStrokeId;
        public bool StrikeActive(Limb item)=>item==shield&&!HasShield?offStroke.active:mainStroke.active||ButtonAttacking;
    }
}
