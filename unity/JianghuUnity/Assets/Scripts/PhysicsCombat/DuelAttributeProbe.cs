using System;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    public sealed partial class Fighter {
        public J ProbeAbilities() {
            if(GameClient.Arg("-physicalCombatSmoke").Length==0)throw new InvalidOperationException("Isolated acceptance only");
            stamina=maxStamina;balance=maxBalance;strength=1;
            swordControl=true;shieldControl=false;pointer=limbs["upperNear"].A+Vector2.up;
            ControlArms(Time.fixedDeltaTime,game.enemy);
            float handForce=handTargets["Near"].maxForce;
            swordControl=false;Swing();float attackSpent=maxStamina-stamina,attackDuration=attackUntil-game.Clock;
            InterruptAttack();regenAt=game.Clock;stamina=maxStamina*.4f;float before=stamina;
            Regenerate(.5f);float recovered=stamina-before;
            stamina=maxStamina;balance=maxBalance;guardBrokenUntil=-10;GuardImpact(5,false);
            float balanceLost=maxBalance-balance,wait=regenAt-game.Clock;
            stamina=0;GuardImpact(5,false);float broken=guardBrokenUntil-game.Clock;
            stamina=maxStamina;stunUntil=injuredUntil=-10;lastDash=-10;
            float oldVelocity=pelvis.rb.linearVelocity.x;bool dashed=Dash();
            float dashVelocity=Mathf.Abs(pelvis.rb.linearVelocity.x-oldVelocity);
            Hit(10,Vector2.zero,torso.rb.position);
            return J.O("max_health",maxHealth,"max_stamina",maxStamina,"hand_force",handForce,
                "arm_speed",ArmSpeed,"attack_spent",attackSpent,"attack_duration",attackDuration,
                "recovered",recovered,"balance_max",maxBalance,"balance_lost",balanceLost,
                "balance_regen",balanceRegen,"recovery_wait",wait,"guard_break",broken,
                "dash",dashed,"dash_velocity",dashVelocity,"stun",stunUntil-game.Clock,
                "injury_duration",injuredUntil-game.Clock,"injury_strength",injuryStrength,
                "impact_damage",DuelImpact.Damage(.7f,5,1,damageScale,damageReduction),
                "weapon_mass",sword.rb.mass,"height_scale",heightScale,"body_mass",torso.rb.mass);
        }
    }
}
