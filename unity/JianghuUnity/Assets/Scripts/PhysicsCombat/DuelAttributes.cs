using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    public sealed partial class Fighter {
        public float maxStamina=100,maxBalance=100;
        public float forceScale=1,movementScale=DuelTuning.Get("movement_multiplier"),effortScale=DuelTuning.Get("effort_multiplier"),regenScale=DuelTuning.Get("regen_multiplier"),recoveryDelayScale=DuelTuning.Get("recovery_delay_multiplier");
        public float fatigueGrowth=DuelTuning.Get("fatigue_growth_multiplier"),fatigueRecovery=DuelTuning.Get("fatigue_recovery_multiplier"),stabilityScale=1,balanceLossScale=1,balanceRegen=20;
        public float stunScale=1,injuryStrength=.48f,injuryDuration=.28f,strengthRecovery=1.8f;
        public float heightScale=1,bodyMassScale=1,armSpanScale=1;
        public float StaminaRatio=>Mathf.Clamp01(stamina/maxStamina);
        public float BalanceRatio=>Mathf.Clamp01(balance/maxBalance);
        static float Ability(J data,string key,float fallback)=>data[key].Has?(float)data[key].N:fallback;
        void LoadAttributes(J fighter) {
            if(fighter==null)return;
            J p=fighter["combat_parameters"];
            maxStamina=Ability(p,"stamina_max",100);maxBalance=Ability(p,"balance_max",100);
            forceScale=Ability(p,"force_scale",1);movementScale=Ability(p,"movement_scale",movementScale);
            effortScale=Ability(p,"effort_scale",effortScale);regenScale=Ability(p,"regen_scale",regenScale);
            recoveryDelayScale=Ability(p,"recovery_delay_scale",recoveryDelayScale);
            fatigueGrowth=Ability(p,"fatigue_growth",fatigueGrowth);fatigueRecovery=Ability(p,"fatigue_recovery",fatigueRecovery);
            stabilityScale=Ability(p,"stability_scale",1);balanceLossScale=Ability(p,"balance_loss_scale",1);
            balanceRegen=Ability(p,"balance_regen",20);stunScale=Ability(p,"stun_scale",1);
            injuryStrength=Ability(p,"injury_strength",.48f);injuryDuration=Ability(p,"injury_duration",.28f);
            strengthRecovery=Ability(p,"strength_recovery",1.8f);
            J body=fighter["physique"];
            heightScale=Ability(body,"height_cm",170)/170;bodyMassScale=Ability(body,"weight_kg",65)/65;
            armSpanScale=Ability(body,"arm_span_cm",170)/(170*heightScale);
            balance=maxBalance;
        }
    }
}
