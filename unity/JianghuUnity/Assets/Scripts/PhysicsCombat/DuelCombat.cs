using UnityEngine;

public sealed partial class PhysicsDuel {
    public sealed partial class Fighter {
        public float AttackCost=>weapon.cost;
        float attackUntil=-10,nextCommitAt=-10,regenAt=-10;
        bool manualWasActive,recoveringBreath;
        float manualHeading,manualReach,stabReturn,stabAdvance;
        public bool IsAttacking=>health>0&&game.Clock>=stunUntil&&game.Clock<attackUntil;
        public bool Recovering=>game.Clock<regenAt;
        public float ArmCapacity=>Mathf.Lerp(.25f,1,Mathf.Clamp01(stamina/25));
        public float ArmSpeed=>weapon.handling*handlingScale*Mathf.Lerp(.38f,1,Mathf.Clamp01(stamina/25))*(game.Clock<stunUntil?.45f:1);
        public float ArmPower=>strength*ArmCapacity*(game.Clock<stunUntil?.35f:1);
        public float relativeSwingSpeed,swingHeat,motionStaminaSpent;
        float movingSeconds;
        float movingMass;
        public float MeasureSwingSpeed() {
            Vector2 point=sword.WorldPoint(weapon.Local(new Vector2(0,weapon.hitStart+(weapon.reach-weapon.hitStart)*.65f),facing));
            float speed=(sword.rb.GetPointVelocity(point)-pelvis.rb.GetPointVelocity(point)).magnitude;movingMass=weapon.mass;
            if(!weapon.TwoHanded&&!HasShield&&shield!=null){
                Vector2 fistPoint=shield.B;
                float offhand=(shield.rb.GetPointVelocity(fistPoint)-pelvis.rb.GetPointVelocity(fistPoint)).magnitude;
                if(offhand>speed){speed=offhand;movingMass=DuelWeapon.Fist.mass;}
            }
            return speed;
        }
        public void TickWeaponEffort(float dt) {
            relativeSwingSpeed=MeasureSwingSpeed();
            bool active=IsAttacking||SwordWheel||swordControl||(weapon.TwoHanded||!HasShield)&&ShieldWheel;
            bool moving=active&&relativeSwingSpeed>2.4f&&game.Clock>=stunUntil;
            movingSeconds=moving?movingSeconds+dt:Mathf.Max(0,movingSeconds-dt*2);
            swingHeat=Mathf.MoveTowards(swingHeat,moving&&movingSeconds>.4f?1:0,dt*(moving?.5f:1));
            if(!moving)return;
            float rate=Mathf.Min(6,(relativeSwingSpeed-2.4f)*.30f*Mathf.Sqrt(movingMass))*(1+swingHeat*2);
            float paid=Mathf.Min(stamina,rate*dt);SpendEffort(paid,.35f);motionStaminaSpent+=paid;
        }
        public string CombatState=>game.Clock<stunUntil?"卸力":game.Clock<guardBrokenUntil?"破防":stamina<AttackCost?"气竭":Recovering?"回气稍候":"";

        void SpendEffort(float cost,float recoveryDelay) {
            stamina=Mathf.Max(0,stamina-cost);
            regenAt=Mathf.Max(regenAt,game.Clock+recoveryDelay);
        }
        bool CommitAttack(float duration) {
            if(!game.Live||health<=0||game.Clock<stunUntil||game.Clock<nextCommitAt||stamina<AttackCost)return false;
            SpendEffort(AttackCost,.8f/weapon.handling);attackUntil=game.Clock+duration;nextCommitAt=game.Clock+.28f/weapon.handling;
            attackId++;strokeTravel=0;stabReturn=stabAdvance=0;return true;
        }
        public bool TryManualAttack() {
            if(!CommitAttack(.72f/weapon.handling))return false;
            slashAt=-10;return true;
        }
        public void InterruptAttack() {
            attackUntil=-10;slashAt=-10;ResetGesture();
        }
        void ResetGesture(){manualWasActive=false;strokeTravel=stabReturn=stabAdvance=0;}
        void TrackManualAttack(bool active,float angle,float reach) {
            if(!active){ResetGesture();return;}
            if(!manualWasActive)TryManualAttack();
            else {
                strokeTravel+=Mathf.Abs(Mathf.DeltaAngle(manualHeading,angle));
                float extension=reach-manualReach;
                if(extension<0){stabReturn=Mathf.Min(.35f,stabReturn-extension);stabAdvance=0;}
                else if(stabReturn>.12f)stabAdvance+=extension;
                if(strokeTravel>=75||stabAdvance>=.16f)TryManualAttack();
            }
            manualWasActive=true;manualHeading=angle;manualReach=reach;
        }
        void Regenerate(float dt) {
            if(game.Clock<regenAt||game.Clock<stunUntil||IsAttacking)return;
            float rate=shieldControl||ShieldWheel?2:SwordWheel?8:14;
            stamina=Mathf.Min(100,stamina+dt*rate);
        }
    }
}
