using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    public sealed partial class Fighter {
        public J ProbePace(){
            if(!game.IsSmoke)throw new System.InvalidOperationException("Isolated acceptance only");
            game.controls.mouse.Clear();ResetGesture();attackUntil=-10;stunUntil=-10;shieldControl=brace=false;
            stamina=maxStamina*.4f;regenAt=game.Clock-1;
            float start=stamina;Regenerate(.25f);float idle=stamina-start;
            RestoreMousePose();start=stamina;Regenerate(.25f);float held=stamina-start;
            start=stamina;float paid=SpendEffort(10,1);
            return J.O("max_health",maxHealth,"max_stamina",maxStamina,"force_scale",forceScale,"movement_scale",movementScale,
                "effort_scale",effortScale,"regen_scale",regenScale,"idle_regen_quarter_second",idle,"static_target_regen_quarter_second",held,
                "ten_point_effort_paid",paid,"actual_stamina_delta",start-stamina,"recovery_delay",regenAt-game.Clock);
        }
    }
}
