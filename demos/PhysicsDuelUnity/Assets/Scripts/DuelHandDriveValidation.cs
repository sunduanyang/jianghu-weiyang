using System.Collections;
using System.IO;
using System.Linq;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    J handDriveResults;
    IEnumerator DriveSmoke() {
        Directory.CreateDirectory(smokePath);started=true;training=true;
        yield return CheckHandDrivePhysics();
        yield return CheckArms();
        yield return CheckCombatRules();
        yield return CheckTwoHandWeapons();
        bool ok=handDriveResults["passed"].B&&armResults["passed"].B&&combatResults["passed"].B&&twoHandResults["passed"].B;
        var report=J.O("passed",ok,"version","0.9.0","hand_drive",handDriveResults,"arms",armResults,"combat",combatResults,"two_hand",twoHandResults);
        File.WriteAllText(Path.Combine(smokePath,"drive-smoke.json"),report.ToString());
        Debug.Log("DUEL_DRIVE_SMOKE "+report);Application.Quit(ok?0:2);
    }
    IEnumerator CheckHandDrivePhysics() {
        playerWeaponIndex=enemyWeaponIndex=DuelWeapon.SwordIndex;ResetRound();player.testOverride=enemy.testOverride=true;
        foreach(var a in player.colliders)foreach(var b in enemy.colliders)Physics2D.IgnoreCollision(a,b);
        yield return new WaitForSeconds(1.2f);
        bool independent=player.sword.rb!=player.limbs["handNear"].rb&&Mathf.Abs(player.sword.rb.mass-player.weapon.mass)<.001f;
        bool passive=player.limbs["upperNear"].joint.motor.maxMotorTorque<=8.01f&&player.limbs["forearmNear"].joint.motor.maxMotorTorque<=6.01f&&player.handTargets["Near"].enabled;
        Vector2 origin=new Vector2(1235,655);controls.Begin(811,origin);controls.Move(811,PadPoint(controls.right,Vector2.right));
        Limb wall=Probe("手部驱动实体阻挡","body",null,new Vector2(.05f,3.5f),new Vector2(.20f,7),false);
        float until=Time.time+2,peakGrip=0,peakForceRatio=0,peakTargetError=0;bool touching=false;
        while(Time.time<until) {
            player.stamina=100;
            yield return new WaitForFixedUpdate();
            peakGrip=Mathf.Max(peakGrip,player.PrimaryGripError);
            var target=player.handTargets["Near"];
            peakForceRatio=Mathf.Max(peakForceRatio,target.reactionForce.magnitude/Mathf.Max(1,target.maxForce));
            peakTargetError=Mathf.Max(peakTargetError,player.HandTargetError("Near"));
            touching|=player.sword.Frame.GetComponents<Collider2D>().Any(c=>c.enabled&&c.IsTouching(wall.shape));
        }
        bool blocked=touching&&player.sword.shape.bounds.max.x<wall.shape.bounds.min.x+.07f;
        bool bounded=peakForceRatio<=1.08f&&peakTargetError>.005f&&peakGrip<.04f;
        bones=true;ScreenCapture.CaptureScreenshot(Path.Combine(smokePath,"13-hand-drive-blocked.png"));yield return new WaitForSeconds(.08f);bones=false;
        ClearFixtures();controls.ReleaseAll();yield return new WaitForSeconds(1);
        Vector2 before=player.sword.B;player.sword.rb.AddForceAtPosition(new Vector2(-1.3f,.4f),player.sword.B,ForceMode2D.Impulse);
        yield return new WaitForSeconds(.06f);
        bool yields=Vector2.Distance(before,player.sword.B)>.008f&&player.PrimaryGripError<.04f;
        yield return new WaitForSeconds(1.5f);
        float restAngle=Mathf.Abs(Mathf.DeltaAngle(player.torso.rb.rotation,0));

        // Isolate the movement invariant using real Rigidbody2D point velocity.
        // These synchronous samples do not claim to be a combat simulation.
        fixtureMode=true;player.stamina=100;player.swordControl=true;player.stunUntil=-10;
        player.pelvis.rb.linearVelocity=player.sword.rb.linearVelocity=new Vector2(3,1);
        player.pelvis.rb.angularVelocity=player.sword.rb.angularVelocity=0;
        float paidBefore=player.motionStaminaSpent;player.TickWeaponEffort(.1f);
        float translationSpeed=player.relativeSwingSpeed;
        bool translationFree=translationSpeed<.001f&&player.motionStaminaSpent==paidBefore;
        player.sword.rb.angularVelocity=720;
        for(int i=0;i<30;i++)player.TickWeaponEffort(.05f);
        float swingSpeed=player.relativeSwingSpeed,extraPaid=player.motionStaminaSpent-paidBefore,hot=player.swingHeat;
        bool movingPaid=extraPaid>.1f&&hot>.1f;
        player.sword.rb.angularVelocity=0;paidBefore=player.motionStaminaSpent;player.TickWeaponEffort(.5f);
        bool stops=player.motionStaminaSpent==paidBefore&&player.swingHeat<hot;
        fixtureMode=false;
        bool ok=independent&&passive&&blocked&&bounded&&yields&&restAngle<15&&translationFree&&movingPaid&&stops;
        handDriveResults=J.O("passed",ok,"independent_weapon_rigidbody",independent,"posture_assist_torque_limited",passive,"physical_wall_blocks_weapon",blocked,"target_force_bounded",bounded,"peak_force_to_limit_ratio",peakForceRatio,"peak_primary_grip_error",peakGrip,"peak_target_error",peakTargetError,"external_impulse_deflects_weapon",yields,"rest_body_angle",restAngle,"shared_translation_free",translationFree,"shared_translation_relative_speed",translationSpeed,"relative_swing_charged",movingPaid,"relative_swing_speed",swingSpeed,"motion_stamina_spent",extraPaid,"swing_heat",hot,"stop_motion_stops_charge_and_cools",stops);
        File.WriteAllText(Path.Combine(smokePath,"hand-drive.json"),handDriveResults.ToString());
        ResetRound();yield return new WaitForSeconds(1.5f);
    }
}
