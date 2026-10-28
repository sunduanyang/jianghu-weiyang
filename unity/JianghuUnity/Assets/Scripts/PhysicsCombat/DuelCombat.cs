using UnityEngine;

public sealed partial class PhysicsDuel {
    public sealed partial class Fighter {
        public float AttackCost=>weapon.cost*effortScale;
        float attackUntil=-10,nextCommitAt=-10,regenAt=-10;
        int buttonStrokeId;
        bool recoveringBreath;
        bool ButtonAttacking=>game.Clock<attackUntil;
        public bool IsAttacking=>health>0&&(Reference?ReferenceArmsActive:ButtonAttacking||mainStroke.active||offStroke.active);
        public bool Recovering=>game.Clock<regenAt;
        public float ArmCapacity=>Mathf.Lerp(.25f,1,Mathf.Clamp01(StaminaRatio/.25f));
        public float ArmSpeed=>weapon.handling*handlingScale;
        public float ArmPower=>forceScale*strength*ArmCapacity*(game.Clock<stunUntil?.35f:1);
        public float relativeSwingSpeed,swingHeat,motionStaminaSpent;
        float movingSeconds;
        public float MeasureSwingSpeed() {
            float speed=StrikeVelocity(sword).magnitude;
            if(!weapon.TwoHanded&&!HasShield&&shield!=null){
                speed=Mathf.Max(speed,StrikeVelocity(shield).magnitude);
            }
            return speed;
        }
        public void TickWeaponEffort(float dt) {
            UpdateManualStrokes(dt);
            relativeSwingSpeed=MeasureSwingSpeed();
            float mainPressure=0,offPressure=0;
            float mainRate=WeaponEffort(sword,ButtonAttacking||MainManual||weapon.TwoHanded&&shieldControl,out mainPressure);
            float offRate=0;
            if(!weapon.TwoHanded&&shield!=null)offRate=HasShield?ShieldEffort():WeaponEffort(shield,OffManual,out offPressure);
            currentPressureRate=mainPressure+offPressure;
            bool moving=mainRate+offRate>.01f&&game.Clock>=stunUntil;
            movingSeconds=moving?movingSeconds+dt:Mathf.Max(0,movingSeconds-dt*2);
            swingHeat=Mathf.MoveTowards(swingHeat,moving&&movingSeconds>.4f?1:0,dt*(moving?.5f*fatigueGrowth:fatigueRecovery));
            if(!moving)return;
            float rate=(mainRate+offRate)*(1+swingHeat*.65f);
            float paid=SpendEffort(rate*dt,.35f);motionStaminaSpent+=paid;
            mainMotionSpent+=paid*mainRate/(mainRate+offRate);offMotionSpent+=paid*offRate/(mainRate+offRate);
            pressureStaminaSpent+=paid*currentPressureRate/(mainRate+offRate);
        }
        public string CombatState=>Reference?(PassedOut?"力竭倒地":ReferenceDowned?"腿伤倒地":game.Clock<recoveryUntil?"起身":StaminaRatio<.2f?"乏力":""):game.Clock<stunUntil?"卸力":game.Clock<guardBrokenUntil?"破防":motionExhausted?"气竭":StaminaRatio<.2f?"乏力":currentPressureRate>.1f?"顶压耗气":Recovering?"回气稍候":"";

        float SpendEffort(float cost,float recoveryDelay) {
            float paid=Mathf.Min(stamina,cost*effortScale);
            stamina=Mathf.Max(0,stamina-paid);
            regenAt=Mathf.Max(regenAt,game.Clock+recoveryDelay*recoveryDelayScale);
            return paid;
        }
        bool CommitAttack(float duration) {
            if(!game.Live||health<=0||game.Clock<stunUntil||game.Clock<nextCommitAt||stamina<AttackCost)return false;
            SpendEffort(weapon.cost,.8f/ArmSpeed);attackUntil=game.Clock+duration;nextCommitAt=game.Clock+.28f/ArmSpeed;
            buttonStrokeId=++attackId;return true;
        }
        public void InterruptAttack() {
            attackUntil=-10;slashAt=-10;ResetGesture();
        }
        void ResetGesture(){mainStroke.Clear();offStroke.Clear();currentPressureRate=0;}
        void Regenerate(float dt) {
            if(game.Clock<regenAt||game.Clock<stunUntil||IsAttacking)return;
            // Remembered static goals are rest. Real motion and pressure still
            // spend effort and reset the recovery delay in TickWeaponEffort.
            float rate=shieldControl||brace?8:14;
            stamina=Mathf.Min(maxStamina,stamina+dt*rate*regenScale);
        }
    }
}
