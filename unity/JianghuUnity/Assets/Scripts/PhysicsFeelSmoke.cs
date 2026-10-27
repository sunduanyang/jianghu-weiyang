using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Jianghu {
public sealed partial class PhysicsBattleSmoke {
    IEnumerator AuditFeel(){
        var input=new DuelControls();
        Vector2 origin=new Vector2(1100,520);
        input.Begin(21,origin);input.Move(21,origin+new Vector2(500,-500));
        Check("pointer origin fixed and radial value clamped",input.right.center==origin&&Mathf.Abs(input.right.value.magnitude-1)<.001f);
        input.Begin(22,new Vector2(420,520));input.Move(22,new Vector2(420,430));
        Check("two touches own independent hand states",input.left.value.y>.99f&&input.right.value.x>.7f);
        input.End(21);Check("releasing primary touch preserves offhand",!input.right.captured&&input.left.captured);input.ReleaseAll();
        input.Begin(11,new Vector2(400,520),1);input.Move(11,new Vector2(1000,520));
        Check("touch ownership survives crossing screen center",input.left.captured&&!input.right.captured);
        input.ReleaseAll();input.Begin(12,new Vector2(400,520),2);input.End(12,2.08f);input.Begin(13,new Vector2(410,520),2.18f);
        Check("double tap hold moves without steering a hand",input.MoveAxis==-1&&!input.left.captured&&!input.right.captured);
        input.End(13,2.4f);Check("releasing double tap stops movement",input.MoveAxis==0);input.ReleaseAll();
        input.Begin(14,new Vector2(400,520),3);input.End(14,3.08f,false);input.Begin(15,new Vector2(400,520),3.18f);
        Check("cancelled touch cannot prime a movement double tap",input.MoveAxis==0&&input.left.captured);input.ReleaseAll();

        var scene=SceneManager.CreateScene("Combat feel acceptance",new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        var root=new GameObject("Combat feel acceptance");root.SetActive(false);SceneManager.MoveGameObjectToScene(root,scene);
        var arena=root.AddComponent<PhysicsDuel>();
        arena.campaign=new PhysicsDuel.CampaignOptions{playerSkin="m-youth-farmer",enemySkin="f-youth-farmer",battle=FeelBattle(DuelWeapon.All[2])};
        app.enabled=false;root.SetActive(true);
        int fps=int.TryParse(GameClient.Arg("-feelFps","90"),out int parsed)?parsed:90;
        int previousVSync=QualitySettings.vSyncCount;QualitySettings.vSyncCount=0;
        Application.targetFrameRate=fps;
        void Reset(DuelWeapon weapon,bool collide=false){
            arena.PauseCampaign();arena.campaign.battle=FeelBattle(weapon);arena.ResetRound();
            arena.player.testOverride=true;arena.enemy.testOverride=true;
            if(!collide)foreach(var a in arena.player.colliders)foreach(var b in arena.enemy.colliders)Physics2D.IgnoreCollision(a,b);
            arena.RunCampaign(true);
        }
        bool quickSpear=GameClient.Arg("-feelCheck")=="spear";
        if(!quickSpear){
        Reset(DuelWeapon.All[2]);yield return new WaitForSecondsRealtime(.8f);
        Vector2 before=arena.player.limbs["handNear"].A;
        float at=arena.Clock;arena.CampaignTestPad(true,Vector2.up);
        while(arena.Clock-at<.3f&&Vector2.Distance(before,arena.player.limbs["handNear"].A)<.08f)yield return new WaitForFixedUpdate();
        float response=arena.Clock-at;
        Check("physical hand responds within 250 ms",response<.25f,J.O("seconds",response,"distance",Vector2.Distance(before,arena.player.limbs["handNear"].A),"render_fps",fps));

        int lateActive=0;float start=arena.Clock;
        while(arena.Clock-start<3.4f){
            float angle=(arena.Clock-start)*3.2f;
            arena.CampaignTestPad(true,new Vector2(Mathf.Cos(angle),Mathf.Sin(angle)));
            arena.CampaignTestPad(false,new Vector2(Mathf.Cos(-angle+.7f),Mathf.Sin(-angle+.7f)));
            if(arena.Clock-start>1&&arena.player.StrikeActive(arena.player.sword)&&arena.player.relativeSwingSpeed>1.5f)lateActive++;
            yield return null;
        }
        var p=arena.player;
        Check("continuous physical swings stay armed beyond old attack window",lateActive>20&&p.manualStrokeCount>2,J.O("late_active_steps",lateActive,"manual_strokes",p.manualStrokeCount));
        Check("both hands spend effort independently",p.mainMotionSpent>0&&p.offMotionSpent>0,J.O("primary",p.mainMotionSpent,"offhand",p.offMotionSpent));
        p.TrackStrikeContact(arena.enemy,p.sword);p.TrackStrikeContact(arena.enemy,p.shield);
        Check("one contact per hand per sweep",p.RegisterHit(arena.enemy,9000,p.sword)&&!p.RegisterHit(arena.enemy,9000,p.sword)&&p.RegisterHit(arena.enemy,9000,p.shield)&&!p.RegisterHit(arena.enemy,9000,p.shield));
        p.TrackStrikeContact(arena.enemy,p.sword);
        Check("a fresh swing id cannot saw through sustained body contact",!p.RegisterHit(arena.enemy,9001,p.sword));
        yield return Picture("feel-independent-hands");

        arena.CampaignReleasePads();yield return new WaitForSecondsRealtime(1.2f);
        float spent=p.motionStaminaSpent;int strokes=p.manualStrokeCount;
        yield return new WaitForSecondsRealtime(.6f);
        Check("released hands do not spend active effort",p.motionStaminaSpent==spent);
        foreach(var body in p.limbs.Values.Select(l=>l.rb).Distinct()){body.linearVelocity=Vector2.right*2;body.angularVelocity=0;}
        Check("body translation is not counted as a swing",p.MeasureSwingSpeed()<.0001f);

        Reset(DuelWeapon.All[2]);yield return new WaitForSecondsRealtime(.5f);
        p=arena.player;p.stamina=0;int emptyCount=p.manualStrokeCount;
        arena.CampaignTestPad(true,Vector2.up);yield return new WaitForSecondsRealtime(.3f);
        Check("exhaustion reduces force rather than removing physical collisions",p.ArmCapacity<.5f&&p.ArmSpeed>.8f);
        arena.CampaignReleasePads();p.stamina=100;yield return new WaitForSecondsRealtime(.4f);
        arena.CampaignTestPad(true,Vector2.up);yield return new WaitForSecondsRealtime(.25f);
        p.Hit(1,Vector2.zero,p.torso.rb.position);int interrupted=p.manualStrokeCount;
        yield return new WaitForSecondsRealtime(.5f);
        Check("hit cancels preset input while residual motion remains physical",p.slashAt<0&&p.stunUntil<arena.Clock);
        arena.CampaignTestPad(true,Vector2.down);yield return new WaitForSecondsRealtime(.5f);
        Check("fresh direction after hit can start a new swing",p.manualStrokeCount>interrupted);

        var cases=new List<J>();
        foreach(var weapon in DuelWeapon.All.Concat(new[]{DuelWeapon.Fist})){
            Reset(weapon);yield return new WaitForSecondsRealtime(.5f);
            float maxGap=0,maxLimit=0;start=arena.Clock;
            while(arena.Clock-start<3.2f){
                float angle=(arena.Clock-start)*2.8f;
                arena.CampaignTestPad(true,new Vector2(Mathf.Cos(angle),Mathf.Sin(angle)));
                if(!weapon.TwoHanded)arena.CampaignTestPad(false,new Vector2(Mathf.Cos(-angle),Mathf.Sin(-angle))*.85f);
                maxGap=Mathf.Max(maxGap,arena.player.MaxJointGap());maxLimit=Mathf.Max(maxLimit,arena.player.MaxLimitError());
                yield return null;
            }
            bool pass=maxGap<.15f&&maxLimit<8&&arena.player.torso.rb.position.y>1.2f&&arena.player.mainMotionSpent>0;
            cases.Add(J.O("weapon",weapon.id,"passed",pass,"gap",maxGap,"limit",maxLimit,"effort",arena.player.motionStaminaSpent,"strokes",arena.player.manualStrokeCount,"worst",arena.player.worstJoint));
            File.WriteAllText(Path.Combine(output,"feel-weapons.json"),new J(cases).ToString());
            if(!pass)yield return Picture("feel-rig-"+weapon.id);
        }
        Check("nine weapon classes and fists retain stable physical constraints",cases.All(c=>c["passed"].B),new J(cases));

        Reset(DuelWeapon.All[2]);yield return new WaitForSecondsRealtime(.6f);
        float initial=arena.player.Hip.x;arena.player.move=1;yield return new WaitForSecondsRealtime(1.2f);float advanced=arena.player.Hip.x;
        arena.player.move=-1;yield return new WaitForSecondsRealtime(1.2f);float retreated=arena.player.Hip.x;arena.player.move=0;
        yield return new WaitForSecondsRealtime(.4f);
        Check("forward reverse and stop respond through physical feet",advanced-initial>.35f&&advanced-retreated>.25f&&Mathf.Abs(arena.player.pelvis.rb.linearVelocity.x)<.7f,J.O("advance",advanced-initial,"retreat",advanced-retreated,"stop_speed",arena.player.pelvis.rb.linearVelocity.x));
        }

        var duels=new List<J>();
        foreach(var weapon in quickSpear?new[]{DuelWeapon.All[5]}:new[]{DuelWeapon.All[2],DuelWeapon.All[5],DuelWeapon.Fist}){
            Reset(weapon,true);arena.enemy.testOverride=false;float start=arena.Clock;
            float maxGap=0,maxLimit=0,aimBias=0;J peak=new J();
            while(arena.Clock-start<18&&!arena.CampaignFinished){
                float dx=arena.enemy.Hip.x-arena.player.Hip.x;
                float desiredDistance=weapon.id=="spear"?2.25f:weapon.distance;
                arena.player.move=Mathf.Abs(dx)>desiredDistance+.05f?Mathf.Sign(dx):Mathf.Abs(dx)<desiredDistance-.2f?-Mathf.Sign(dx)*.4f:0;
                float angle=Mathf.Sin((arena.Clock-start)*4.2f)*1.25f;
                Vector2 aim=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
                if(weapon.id=="spear"){
                    // Thrust the point from the rig's real shared-grip reach;
                    // the harmless shaft is not a valid damaging contact.
                    Vector2 target=arena.enemy.torso.rb.position-arena.player.sword.WorldPoint(weapon.Local(Vector2.zero,1));
                    float wantedAxis=Mathf.Atan2(target.y,target.x)*Mathf.Rad2Deg;
                    // Like a player watching the point, correct the input when
                    // arm posture / inertia leave the point above the target.
                    float error=Mathf.DeltaAngle(wantedAxis,arena.player.sword.rb.rotation+90);
                    aimBias=Mathf.Clamp(aimBias-error*Time.deltaTime*1.4f,-65,65);
                    float inputAngle=(wantedAxis+aimBias)*Mathf.Deg2Rad;
                    Vector2 mainDirection=new Vector2(Mathf.Cos(.12f),Mathf.Sin(.12f));
                    Vector2 supportDirection=new Vector2(Mathf.Cos(inputAngle),Mathf.Sin(inputAngle));
                    // Keep the primary hand in front of the shoulder and steer
                    // the second grip independently, as a two-finger player can.
                    arena.CampaignTestPad(false,(supportDirection-mainDirection)*(weapon.supportGrip/.42f));
                    aim=mainDirection;
                    aim*=Mathf.Lerp(.25f,1,(Mathf.Sin((arena.Clock-start)*7)+1)*.5f);
                }
                arena.CampaignTestPad(true,aim);
                if(!weapon.TwoHanded)arena.CampaignTestPad(false,new Vector2(Mathf.Cos(-angle),Mathf.Sin(-angle)));
                float gap=Mathf.Max(arena.player.MaxJointGap(),arena.enemy.MaxJointGap());
                if(gap>maxGap){maxGap=gap;peak=J.O("clock",arena.Clock-start,"player_health",arena.player.health,"enemy_health",arena.enemy.health,"primary_grip",arena.player.PrimaryGripError,"support_grip",arena.player.SupportGripError,"player_pose",arena.player.CapturePhysicalState(),"enemy_pose",arena.enemy.CapturePhysicalState());}
                maxLimit=Mathf.Max(maxLimit,arena.player.MaxLimitError(),arena.enemy.MaxLimitError());
                yield return null;
            }
            duels.Add(J.O("weapon",weapon.id,"hits",arena.player.landedHits+arena.enemy.landedHits,"manual_hits",arena.player.landedHits,"clashes",arena.blocks,"block_contacts",arena.blockContacts,"pressure_effort",arena.player.pressureStaminaSpent,"passed",arena.player.landedHits+arena.enemy.landedHits>0&&maxGap<.15f&&maxLimit<8,"gap",maxGap,"limit",maxLimit,"elapsed",arena.Clock-start,"player_worst",arena.player.worstJoint,"enemy_worst",arena.enemy.worstJoint,"rest_contacts_rejected",arena.rejectedRestContacts,"handle_contacts_rejected",arena.rejectedHandleContacts,"occluded_contacts",arena.blockedBodyContacts));
            File.WriteAllText(Path.Combine(output,"feel-peak-"+weapon.id+".json"),peak.ToString());
            File.WriteAllText(Path.Combine(output,"feel-hands-"+weapon.id+".json"),J.O("near",arena.player.HandPoseState("Near"),"far",arena.player.HandPoseState("Far")).ToString());
            File.WriteAllText(Path.Combine(output,"feel-duels.json"),new J(duels).ToString());
            yield return Picture("feel-duel-"+weapon.id);
        }
        Check("sword spear and fists duels produce real contact damage",duels.All(c=>c["passed"].B),new J(duels));
        arena.PauseCampaign();yield return SceneManager.UnloadSceneAsync(scene);app.enabled=true;Time.timeScale=1;QualitySettings.vSyncCount=previousVSync;
    }
    static J FeelBattle(DuelWeapon weapon)=>J.O("fighters",new J(new List<J>{AuditFighter(weapon),AuditFighter(weapon)}));
}
}
