using System.Collections;
using System.IO;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    J armResults;
    static Vector2 PadPoint(DuelControls.Pad pad,Vector2 direction)=>pad.center+new Vector2(direction.x,-direction.y)*DuelControls.Pad.Radius;
    float ArmExtension(string side){var a=player.limbs["upper"+side];var b=player.limbs["forearm"+side];return Vector2.Distance(a.A,b.B)/(a.length+b.length);}
    float ArmUpError(string side)=>Vector2.Angle(Vector2.up,player.limbs["forearm"+side].B-player.limbs["upper"+side].A);
    float ElbowBend(string side)=>Mathf.Abs(Mathf.DeltaAngle(player.limbs["upper"+side].rb.rotation,player.limbs["forearm"+side].rb.rotation));
    float ShoulderAngle(string side)=>player.limbs["upper"+side].rb.rotation-player.torso.rb.rotation;
    IEnumerator HealthyArmPose(float duration) {
        float until=Time.time+duration;
        while(Time.time<until){player.stamina=100;yield return new WaitForFixedUpdate();}
    }
    IEnumerator ArmSmoke() {
        Directory.CreateDirectory(smokePath);started=true;training=true;
        yield return CheckArms();
        File.WriteAllText(Path.Combine(smokePath,"arm-smoke.json"),armResults.ToString());Application.Quit(armResults["passed"].B?0:2);
    }
    IEnumerator CheckArms() {
        player.testOverride=enemy.testOverride=true;player.brace=enemy.brace=false;enemy.shieldControl=true;player.shieldControl=false;
        controls.ReleaseAll();
        bool hiddenIdle=!controls.left.captured&&!controls.right.captured;
        yield return new WaitForSeconds(.8f);
        ScreenCapture.CaptureScreenshot(Path.Combine(smokePath,"08-neutral-hidden-controls.png"));yield return new WaitForSeconds(.08f);
        Vector2 swordPress=new Vector2(1235,655),shieldPress=new Vector2(365,655);
        controls.Begin(-1,new Vector2(350,540));controls.Begin(-2,new Vector2(1250,540));
        bool mouseHands=controls.right.captured&&controls.left.captured&&controls.right.center.x==350&&controls.left.center.x==1250;
        controls.ReleaseAll();
        controls.Begin(101,swordPress);controls.Begin(102,shieldPress);
        bool atPress=controls.right.center==swordPress&&controls.left.center==shieldPress;
        bool centeredSafe=!controls.right.HasDirection&&!controls.left.HasDirection;
        controls.Move(101,PadPoint(controls.right,Vector2.up));controls.Move(102,PadPoint(controls.left,Vector2.up));
        yield return HealthyArmPose(1.6f);
        float swordReach=ArmExtension("Near"),shieldReach=ArmExtension("Far"),swordUp=ArmUpError("Near"),shieldUp=ArmUpError("Far");
        float swordElbow=ElbowBend("Near"),shieldElbow=ElbowBend("Far");
        bones=true;ScreenCapture.CaptureScreenshot(Path.Combine(smokePath,"06-arms-up-colliders.png"));yield return new WaitForSeconds(.08f);
        bones=false;ScreenCapture.CaptureScreenshot(Path.Combine(smokePath,"07-arms-up.png"));
        float previousSword=ShoulderAngle("Near"),previousShield=ShoulderAngle("Far"),swordTravel=0,shieldTravel=0,maxGap=0,maxLimit=0;
        float start=Time.time,span=6;
        while(Time.time<start+span) {
            player.stamina=100;
            float angle=(Time.time-start)/span*Mathf.PI*2;
            controls.Move(101,PadPoint(controls.right,new Vector2(Mathf.Sin(angle),Mathf.Cos(angle))));
            controls.Move(102,PadPoint(controls.left,new Vector2(-Mathf.Sin(angle),Mathf.Cos(angle))));
            yield return new WaitForFixedUpdate();
            float actualSword=ShoulderAngle("Near"),actualShield=ShoulderAngle("Far");
            swordTravel+=Mathf.DeltaAngle(previousSword,actualSword);shieldTravel+=Mathf.DeltaAngle(previousShield,actualShield);
            previousSword=actualSword;previousShield=actualShield;
            maxGap=Mathf.Max(maxGap,player.MaxJointGap());maxLimit=Mathf.Max(maxLimit,player.MaxLimitError());
        }
        controls.Move(101,PadPoint(controls.right,Vector2.up));controls.Move(102,PadPoint(controls.left,Vector2.up));
        float settle=Time.time+.9f;
        while(Time.time<settle){player.stamina=100;yield return new WaitForFixedUpdate();float a=ShoulderAngle("Near"),b=ShoulderAngle("Far");swordTravel+=Mathf.DeltaAngle(previousSword,a);shieldTravel+=Mathf.DeltaAngle(previousShield,b);previousSword=a;previousShield=b;}
        controls.End(101);
        bool independentRelease=!controls.right.captured&&controls.left.HasDirection&&Vector2.Distance(controls.left.value,Vector2.up)<.001f;
        controls.Move(102,PadPoint(controls.left,Vector2.left));
        bool independentMove=!controls.right.HasDirection&&controls.left.value.x<-.99f;
        controls.End(102);
        bool released=!controls.left.captured&&!controls.right.captured;
        controls.Begin(103,swordPress);Vector2 farPointer=new Vector2(1510,655);controls.Move(103,farPointer);
        bool clamped=controls.right.value==Vector2.right;
        bool fixedBase=controls.right.center==swordPress;
        Vector2 thumb=controls.right.center+new Vector2(controls.right.value.x,-controls.right.value.y)*DuelControls.Pad.Radius;
        bool rimClamped=Vector2.Distance(thumb,swordPress+Vector2.right*DuelControls.Pad.Radius)<.001f&&Vector2.Distance(thumb,farPointer)>100;
        controls.Move(103,swordPress+new Vector2(0,-DuelControls.Pad.Radius*.25f));
        bool originalOrigin=controls.right.center==swordPress&&Vector2.Distance(controls.right.value,new Vector2(0,.25f))<.001f;
        controls.End(103);Vector2 nextPress=new Vector2(1000,500);controls.Begin(103,nextPress);
        bool nextOrigin=controls.right.center==nextPress&&!controls.right.HasDirection;
        controls.ReleaseAll();
        bool uiExcluded=!controls.Begin(-1,new Vector2(800,90))&&!controls.Begin(-2,new Vector2(1450,190));
        controls.Begin(104,controls.forward.center);bool moveButton=controls.MoveAxis==1&&!controls.left.captured&&!controls.right.captured;controls.End(104);
        controls.Begin(105,swordPress);controls.Begin(106,shieldPress);
        controls.Move(105,swordPress+new Vector2(0,-DuelControls.Pad.Radius*.18f));controls.Move(106,shieldPress+new Vector2(DuelControls.Pad.Radius*.18f,0));
        yield return HealthyArmPose(1.3f);
        float foldedSword=ElbowBend("Near"),foldedShield=ElbowBend("Far");
        float signedSword=Mathf.DeltaAngle(player.limbs["upperNear"].rb.rotation,player.limbs["forearmNear"].rb.rotation)*player.facing;
        float signedShield=Mathf.DeltaAngle(player.limbs["upperFar"].rb.rotation,player.limbs["forearmFar"].rb.rotation)*player.facing;
        float bladeError=Mathf.Abs(Mathf.DeltaAngle(player.sword.Rotation,0)),guardError=Mathf.Abs(Mathf.DeltaAngle(player.shield.Rotation,0));
        File.WriteAllText(Path.Combine(smokePath,"folded-physics.json"),J.O("primary",player.HandPoseState("Near"),"secondary",player.HandPoseState("Far")).ToString());
        bool bendSafe=foldedSword<138&&foldedShield<138&&signedSword>=-2&&signedShield>=-2&&bladeError<12&&guardError<12;
        ScreenCapture.CaptureScreenshot(Path.Combine(smokePath,"09-bent-arms.png"));yield return new WaitForSeconds(.08f);
        controls.ReleaseAll();
        controls.Begin(107,swordPress);paused=true;yield return null;yield return null;
        bool pauseRelease=!controls.right.captured&&!controls.left.captured;paused=false;yield return null;
        controls.ReleaseAll();bool cleared=!controls.right.captured&&!controls.left.captured&&controls.MoveAxis==0;
        bool upward=swordReach>.97f&&shieldReach>.97f&&swordUp<12&&shieldUp<12&&swordElbow<16&&shieldElbow<16;
        bool circle=swordTravel< -340&&shieldTravel>340;
        bool safe=maxGap<.10f&&maxLimit<6;
        armResults=J.O("passed",upward&&circle&&safe&&bendSafe&&hiddenIdle&&mouseHands&&atPress&&fixedBase&&rimClamped&&originalOrigin&&nextOrigin&&uiExcluded&&moveButton&&pauseRelease&&centeredSafe&&independentRelease&&independentMove&&released&&clamped&&cleared,"hidden_when_idle",hiddenIdle,"appears_at_press",atPress,"mouse_buttons_select_hands_anywhere",mouseHands,"base_fixed_during_drag",fixedBase,"thumb_clamped_to_rim",rimClamped,"return_uses_original_center",originalOrigin,"new_press_repositions_base",nextOrigin,"ui_regions_excluded",uiExcluded,"movement_buttons_independent",moveButton,"pause_releases_sticks",pauseRelease,"folded_pose_passed",bendSafe,"folded_sword_elbow_degrees",foldedSword,"folded_shield_elbow_degrees",foldedShield,"folded_sword_blade_error_degrees",bladeError,"folded_shield_angle_error_degrees",guardError,"both_arms_straight_up",upward,"sword_extension_ratio",swordReach,"shield_extension_ratio",shieldReach,"sword_up_error_degrees",swordUp,"shield_up_error_degrees",shieldUp,"sword_elbow_bend_degrees",swordElbow,"shield_elbow_bend_degrees",shieldElbow,"full_circle_passed",circle,"sword_actual_shoulder_rotation_degrees",swordTravel,"shield_actual_shoulder_rotation_degrees",shieldTravel,"sweep_maximum_joint_gap",maxGap,"sweep_maximum_limit_error_degrees",maxLimit,"neutral_deadzone",centeredSafe,"independent_pointer_release",independentRelease,"independent_pointer_move",independentMove,"release_returns_neutral",released,"outside_ring_clamped",clamped,"reset_clears_controls",cleared);
        File.WriteAllText(Path.Combine(smokePath,"arms.json"),armResults.ToString());
        ResetRound();yield return new WaitForSeconds(1.5f);
    }
}
