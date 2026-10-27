using System.Collections;
using System.IO;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    J combatResults;
    IEnumerator CheckCombatRules() {
        training=true;ResetRound();player.testOverride=enemy.testOverride=true;enemy.shieldControl=true;
        yield return new WaitForSeconds(1);
        Vector2 press=new Vector2(1235,655);
        controls.Begin(201,press);controls.Move(201,PadPoint(controls.right,Vector2.up));
        yield return new WaitForSeconds(.25f);
        float healthyTorque=player.handTargets["Near"].maxForce;
        controls.ReleaseAll();player.InterruptAttack();yield return new WaitForSeconds(.32f);
        player.stamina=0;int emptyId=player.attackId;
        controls.Begin(202,press);controls.Move(202,PadPoint(controls.right,Vector2.up));
        yield return new WaitForSeconds(.25f);
        bool emptyManual=!player.IsAttacking&&player.attackId==emptyId&&!player.TryManualAttack();
        player.Swing();bool emptyButtons=player.attackId==emptyId&&!player.Dash();
        float tiredTorque=player.handTargets["Near"].maxForce;
        bool fatigue=tiredTorque<healthyTorque*.5f&&player.ArmSpeed<.55f;
        ScreenCapture.CaptureScreenshot(Path.Combine(smokePath,"10-exhausted-controls.png"));yield return new WaitForSeconds(.08f);
        controls.ReleaseAll();player.InterruptAttack();yield return new WaitForSeconds(.32f);
        player.stamina=100;int before=player.attackId;
        bool manualPaid=player.TryManualAttack()&&player.attackId==before+1&&player.IsAttacking;
        float manualCost=100-player.stamina,paidStamina=player.stamina;
        bool cooldown=!player.TryManualAttack()&&player.stamina==paidStamina;
        player.Hit(1,Vector2.zero,player.torso.rb.position);int interruptedId=player.attackId;
        bool interrupted=!player.IsAttacking&&!player.TryManualAttack()&&!player.Dash();player.Swing();interrupted&=player.attackId==interruptedId;
        controls.Begin(203,press);controls.Move(203,PadPoint(controls.right,Vector2.up));
        yield return new WaitForSeconds(.4f);
        bool noResume=!player.IsAttacking&&player.attackId==interruptedId;
        controls.ReleaseAll();player.InterruptAttack();yield return new WaitForSeconds(.3f);
        player.stamina=100;before=player.attackId;player.Swing(1);
        float buttonCost=100-player.stamina;
        bool sameCost=player.attackId==before+1&&Mathf.Abs(manualCost-buttonCost)<.001f&&manualCost==player.AttackCost;
        float spent=player.stamina;yield return new WaitForSeconds(.25f);
        bool delay=player.stamina<=spent;
        yield return new WaitForSeconds(1.35f);bool rested=player.stamina>spent;
        player.stamina=30;player.shieldControl=true;player.GuardImpact(8,false);float guardSpent=player.stamina;
        yield return new WaitForSeconds(.35f);bool guardDelay=player.stamina==guardSpent;
        yield return new WaitForSeconds(1);bool guardRecovery=player.stamina>guardSpent&&player.stamina<guardSpent+1.4f;
        bool ok=emptyManual&&emptyButtons&&fatigue&&manualPaid&&cooldown&&interrupted&&noResume&&sameCost&&delay&&rested&&guardDelay&&guardRecovery;
        combatResults=J.O("passed",ok,"empty_manual_no_attack",emptyManual,"empty_buttons_no_attack_or_dash",emptyButtons,"fatigue_reduces_hand_force_and_speed",fatigue,"healthy_hand_max_force",healthyTorque,"exhausted_hand_max_force",tiredTorque,"paid_manual_starts_attack",manualPaid,"commit_cooldown_no_double_cost",cooldown,"hit_interrupts_manual_and_button_attacks",interrupted,"interrupted_attack_does_not_resume",noResume,"manual_and_button_cost_match",sameCost,"manual_attack_cost",manualCost,"button_attack_cost",buttonCost,"attack_delays_regeneration",delay,"rest_recovers_stamina",rested,"guard_delays_regeneration",guardDelay,"held_guard_recovers_slowly",guardRecovery);
        File.WriteAllText(Path.Combine(smokePath,"combat-rules.json"),combatResults.ToString());
        ResetRound();yield return new WaitForSeconds(1.5f);
    }
}
