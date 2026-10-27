using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    J twoHandResults;
    IEnumerator GripSmoke() {
        Directory.CreateDirectory(smokePath);started=true;training=true;
        yield return CheckTwoHandWeapons();
        bool ok=twoHandResults["passed"].B;
        File.WriteAllText(Path.Combine(smokePath,"grip-smoke.json"),J.O("passed",ok,"version","0.9.0","two_hand",twoHandResults).ToString());
        Application.Quit(ok?0:2);
    }
    IEnumerator CheckTwoHandWeapons() {
        var cases=new List<J>();
        for(int i=0;i<DuelWeapon.All.Length;i++) {
            if(!DuelWeapon.All[i].TwoHanded)continue;
            if(Arg("-duelWeapon").Length>0&&Arg("-duelWeapon")!=DuelWeapon.All[i].id)continue;
            playerWeaponIndex=enemyWeaponIndex=i;ResetRound();player.testOverride=enemy.testOverride=true;
            // Isolate each rig's healthy range from the other actor's parries.
            // Weapon contacts are exercised separately with actual colliders.
            foreach(var a in player.colliders)foreach(var b in enemy.colliders)Physics2D.IgnoreCollision(a,b);
            Vector2 press=new Vector2(1235,655);controls.Begin(701,press);controls.Move(701,PadPoint(controls.right,Vector2.up));
            // Establish the measured starting pose before accumulating a full
            // turn. Heavy finite-force rigs have a longer settling transient;
            // a fixed wait incorrectly counts that transient as part of the
            // sweep. Failure to settle within three seconds still fails.
            float prepareStart=Time.time,prepareUntil=Time.time+3;
            while(Time.time<prepareUntil) {
                player.stamina=enemy.stamina=100;
                yield return new WaitForFixedUpdate();
                if(Time.time-prepareStart>1.6f&&Mathf.Abs(Mathf.DeltaAngle(player.sword.rb.rotation,0))<3&&Mathf.Abs(player.sword.rb.angularVelocity)<10)break;
            }
            float initialUpError=Mathf.Abs(Mathf.DeltaAngle(player.sword.rb.rotation,0));
            J initialPose=player.HandPoseState("Near");
            bool noShield=player.shield==null&&enemy.shield==null&&player.root.GetComponentsInChildren<DuelContact>().All(c=>c.kind!="shield")&&enemy.root.GetComponentsInChildren<DuelContact>().All(c=>c.kind!="shield");
            bool attached=player.supportJoint!=null&&player.supportJoint.enabled&&player.supportJoint.connectedBody==player.sword.rb&&enemy.supportJoint!=null&&enemy.supportJoint.enabled;
            float previous=player.sword.Rotation,turn=0,peakError=0,peakGap=0,peakLimit=0,peakForce=0,peakGripAngle=0,peakTurnTorque=0;
            float start=Time.time,span=8;
            string worst="";
            J worstPose=J.O();float recordedLimit=0;
            while(Time.time<start+span) {
                // Motion validation isolates the healthy mechanical rig;
                // spending/zero-budget rules are checked in their own suite.
                player.stamina=enemy.stamina=100;
                float a=(Time.time-start)/span*Mathf.PI*2;
                controls.Move(701,PadPoint(controls.right,new Vector2(Mathf.Sin(a),Mathf.Cos(a))));
                yield return new WaitForFixedUpdate();
                turn+=Mathf.DeltaAngle(previous,player.sword.Rotation);previous=player.sword.Rotation;
                peakError=Mathf.Max(peakError,player.SupportGripError,enemy.SupportGripError);
                peakGripAngle=Mathf.Max(peakGripAngle,player.PrimaryGripAngleError,enemy.PrimaryGripAngleError);
                peakTurnTorque=Mathf.Max(peakTurnTorque,Mathf.Abs(player.gripTurnTorque),Mathf.Abs(enemy.gripTurnTorque));
                peakGap=Mathf.Max(peakGap,player.MaxJointGap(),enemy.MaxJointGap());peakLimit=Mathf.Max(peakLimit,player.MaxLimitError(),enemy.MaxLimitError());
                worst=player.worstJoint+" | "+enemy.worstJoint;
                if(peakLimit>recordedLimit){recordedLimit=peakLimit;worstPose=J.O("primary",player.HandPoseState("Near"),"secondary",player.HandPoseState("Far"));}
                peakForce=Mathf.Max(peakForce,player.supportJoint.reactionForce.magnitude,enemy.supportJoint.reactionForce.magnitude);
            }
            controls.Move(701,PadPoint(controls.right,Vector2.up));float settle=Time.time+.9f;
            while(Time.time<settle){player.stamina=enemy.stamina=100;yield return new WaitForFixedUpdate();turn+=Mathf.DeltaAngle(previous,player.sword.Rotation);previous=player.sword.Rotation;}
            float initialSpacing=player.gripSpacing;
            File.WriteAllText(Path.Combine(smokePath,"two-hand-"+player.weapon.id+"-physics.json"),J.O("initial_pose",initialPose,"peak_pose",worstPose,"settled_pose",player.HandPoseState("Near")).ToString());
            float initialAngle=player.sword.Rotation;Vector2 initialGrip=player.SupportGripPoint;
            controls.Begin(702,new Vector2(365,655));controls.Move(702,PadPoint(controls.left,Vector2.right));
            yield return new WaitForSeconds(.7f);
            float adjustedSpacing=player.gripSpacing;
            bool sliding=(Mathf.Abs(Mathf.DeltaAngle(initialAngle,player.sword.Rotation))>8||Vector2.Distance(initialGrip,player.SupportGripPoint)>.08f)&&Mathf.Abs(Vector2.Distance(player.limbs["handNear"].B,player.limbs["handFar"].B)-initialSpacing)<.04f;
            bones=true;ScreenCapture.CaptureScreenshot(Path.Combine(smokePath,"two-hand-"+player.weapon.id+"-colliders.png"));yield return new WaitForSeconds(.08f);bones=false;
            controls.ReleaseAll();player.stamina=100;yield return new WaitForSeconds(2);
            float restAngle=Mathf.Abs(Mathf.DeltaAngle(player.torso.rb.rotation,0));
            ScreenCapture.CaptureScreenshot(Path.Combine(smokePath,"two-hand-"+player.weapon.id+"-ready.png"));yield return new WaitForSeconds(.08f);
            player.Hit(8,Vector2.zero,player.torso.rb.position);yield return new WaitForSeconds(.3f);
            bool hitAttached=player.supportJoint.enabled&&player.SupportGripError<.05f;
            player.Hit(500,Vector2.zero,player.torso.rb.position);yield return new WaitForSeconds(.25f);
            bool released=!player.primaryGrip.enabled&&!player.supportJoint.enabled&&player.handTargets.Values.All(t=>!t.enabled)&&player.limbs.Values.All(l=>l.joint==null||!l.joint.useMotor);
            bool turnOk=initialUpError<6&&turn< -340&&turn> -380;
            bool ok=noShield&&attached&&sliding&&hitAttached&&released&&turnOk&&peakError<.04f&&peakGap<.10f&&peakLimit<6&&peakForce>.5f&&peakGripAngle<4&&peakTurnTorque<=18.01f&&restAngle<15;
            cases.Add(J.O("id",player.weapon.id,"name",player.weapon.name,"passed",ok,"no_shield_model_or_collider",noShield,"real_secondary_grip_joint",attached,"secondary_input_moves_weapon",sliding,"initial_spacing",initialSpacing,"adjusted_spacing",adjustedSpacing,"full_weapon_rotation",turnOk,"actual_weapon_turn_degrees",turn,"peak_grip_error",peakError,"peak_grip_angle_error",peakGripAngle,"peak_turn_torque",peakTurnTorque,"peak_joint_gap",peakGap,"peak_limit_error",peakLimit,"worst_joint",worst,"peak_support_reaction_force",peakForce,"rest_body_angle",restAngle,"hit_keeps_secondary_grip",hitAttached,"knockout_releases_secondary_grip",released));
            File.WriteAllText(Path.Combine(smokePath,"two-hand-progress.json"),new J(cases).ToString());
        }
        playerWeaponIndex=enemyWeaponIndex=DuelWeapon.SwordIndex;ResetRound();yield return new WaitForSeconds(.5f);
        bool restored=player.shield!=null&&player.shield.shape.enabled&&player.supportJoint==null&&!player.weapon.TwoHanded;
        twoHandResults=J.O("passed",cases.Count==(Arg("-duelWeapon").Length>0?1:4)&&cases.All(c=>c["passed"].B)&&restored,"two_hand_weapon_count",cases.Count,"cases",new J(cases),"switch_to_one_hand_restores_shield",restored);
        File.WriteAllText(Path.Combine(smokePath,"two-hand.json"),twoHandResults.ToString());
        yield return new WaitForSeconds(1);
    }
}
