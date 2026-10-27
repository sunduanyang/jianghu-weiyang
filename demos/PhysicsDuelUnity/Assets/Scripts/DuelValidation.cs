using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    readonly List<GameObject> fixtures=new List<GameObject>();
    J fixtureResults;
    Limb Probe(string name,string kind,Fighter owner,Vector2 p,Vector2 size,bool moving) {
        var go=new GameObject(name);fixtures.Add(go);go.layer=kind=="sword"||kind=="shield"?10:9;
        var rb=go.AddComponent<Rigidbody2D>();rb.position=p;rb.mass=.7f;rb.gravityScale=0;rb.collisionDetectionMode=CollisionDetectionMode2D.Continuous;
        rb.sleepMode=RigidbodySleepMode2D.NeverSleep;
        if(!moving)rb.bodyType=RigidbodyType2D.Kinematic;
        var c=go.AddComponent<BoxCollider2D>();c.size=size;c.sharedMaterial=bladeMaterial;
        var l=new Limb {key=name,rb=rb,shape=c,length=size.y};l.SampleVelocity();
        if(owner!=null){var d=go.AddComponent<DuelContact>();d.kind=kind;d.owner=owner;d.limb=l;}
        return l;
    }
    static void LaunchProbe(Limb p,Vector2 origin,float speed){p.rb.position=origin;p.rb.linearVelocity=Vector2.right*speed;p.rb.angularVelocity=0;p.SampleVelocity();}
    void ClearFixtures(){foreach(var go in fixtures){go.SetActive(false);Destroy(go);}fixtures.Clear();contacts.Clear();}
    IEnumerator PrepareProbeAttack(){yield return new WaitForSeconds(.31f/player.weapon.handling);player.stamina=100;player.stunUntil=-10;player.InterruptAttack();if(!player.TryManualAttack())throw new Exception("Probe could not commit a paid attack");}
    IEnumerator CheckContacts() {
        fixtureMode=true;player.root.SetActive(false);enemy.root.SetActive(false);
        yield return PrepareProbeAttack();enemy.health=120;enemy.stamina=100;
        // These are real dynamic contacts using the same callback and damage
        // resolver as the arena. No test calls ResolveHit or subtracts damage.
        Vector2 origin=new Vector2(-1.2f,7);
        Limb body=Probe("静止身体探针","body",enemy,new Vector2(0,7),new Vector2(.18f,.8f),false);
        Limb blade=Probe("静止剑刃探针","sword",player,new Vector2(-.12f,7),new Vector2(.08f,1.1f),true);
        LaunchProbe(blade,new Vector2(-.12f,7),.1f);
        yield return new WaitForSeconds(.35f);
        bool restSafe=enemy.health==120;
        ClearFixtures();

        yield return PrepareProbeAttack();
        body=Probe("离开身体探针","body",enemy,new Vector2(0,7),new Vector2(.18f,.3f),false);
        blade=Probe("移出剑刃探针","sword",player,new Vector2(-.03f,7),new Vector2(.08f,1.1f),true);
        int separationBefore=separatingContacts;
        LaunchProbe(blade,new Vector2(-.03f,7),-6);
        yield return new WaitForSeconds(.2f);
        bool separatingSafe=enemy.health==120&&separatingContacts>separationBefore;
        ClearFixtures();

        body=Probe("护手身体探针","body",enemy,new Vector2(0,7),new Vector2(.18f,.8f),false);
        blade=Probe("无伤护手探针","sword",player,origin,new Vector2(.04f,.04f),true);
        ((BoxCollider2D)blade.shape).offset=new Vector2(0,2);
        blade.rb.gameObject.AddComponent<BoxCollider2D>().size=new Vector2(.08f,.8f);
        LaunchProbe(blade,origin,8);
        int hiltBefore=rejectedHandleContacts;
        yield return new WaitForSeconds(.25f);
        bool hiltSafe=enemy.health==120&&rejectedHandleContacts>hiltBefore;
        ClearFixtures();

        body=Probe("盾后身体探针","body",enemy,new Vector2(.2f,7),new Vector2(.18f,.8f),false);
        Limb guard=Probe("盾牌探针","shield",enemy,new Vector2(-.13f,7),new Vector2(.25f,1.35f),false);
        blade=Probe("格挡剑刃探针","sword",player,origin,new Vector2(.08f,1.1f),true);
        LaunchProbe(blade,origin,8);
        int blockBefore=blocks;
        yield return new WaitForSeconds(.25f);
        bool shieldSafe=enemy.health==120&&blocks>blockBefore;
        int guardEvents=blocks,guardContactBefore=blockContacts;
        float afterGuardStamina=enemy.stamina;
        float pressureUntil=Time.time+.35f;
        while(Time.time<pressureUntil){blade.rb.AddForce(Vector2.right*12);yield return new WaitForFixedUpdate();}
        bool stillTouching=blade.shape.IsTouching(guard.shape);
        bool pressureSafe=blocks==guardEvents&&enemy.stamina==afterGuardStamina&&stillTouching;
        File.WriteAllText(Path.Combine(smokePath,"pressure-debug.json"),J.O("events_before",guardEvents,"events_after",blocks,"stamina_before",afterGuardStamina,"stamina_after",enemy.stamina,"raw_contacts",blockContacts-guardContactBefore,"still_touching",stillTouching).ToString());
        enemy.stamina=5;yield return PrepareProbeAttack();int breakBefore=guardBreaks;LaunchProbe(blade,origin,8);
        yield return new WaitForSeconds(.25f);
        bool guardBreakSafe=guardBreaks>breakBefore&&enemy.guardBrokenUntil>Time.time&&enemy.health==120;
        ClearFixtures();

        yield return PrepareProbeAttack();enemy.health=120;
        body=Probe("重复命中身体探针","body",enemy,new Vector2(0,7),new Vector2(.18f,.8f),false);
        blade=Probe("同一挥砍探针","sword",player,origin,new Vector2(.08f,1.1f),true);
        LaunchProbe(blade,origin,8);
        yield return new WaitForSeconds(.25f);
        float afterFirst=enemy.health;
        int duplicateBefore=duplicateStrokeContacts;
        LaunchProbe(blade,origin,8);
        yield return new WaitForSeconds(.25f);
        bool singleHit=afterFirst<120&&enemy.health==afterFirst&&duplicateStrokeContacts>duplicateBefore;
        ClearFixtures();

        body=Probe("高速薄壁探针","body",null,new Vector2(0,8),new Vector2(.035f,1.2f),false);
        blade=Probe("高速剑刃探针","sword",null,new Vector2(-1.2f,8),new Vector2(.045f,.7f),true);
        var recorder=blade.rb.gameObject.AddComponent<DuelProbe>();
        LaunchProbe(blade,new Vector2(-1.2f,8),60);
        yield return new WaitForSeconds(.12f);
        bool ccd=recorder.contacts>0&&blade.rb.position.x<.12f;
        ClearFixtures();
        bool exposedHead=false,exposedLeg=false;
        for(int i=0;i<2;i++) {
            yield return PrepareProbeAttack();enemy.health=120;
            body=Probe(i==0?"盾上露头探针":"盾下露腿探针",i==0?"head":"leg",enemy,new Vector2(-.035f,i==0?7.35f:6.85f),new Vector2(.18f,.22f),false);
            guard=Probe("局部盾面探针","shield",enemy,new Vector2(0,i==0?6.8f:7.3f),new Vector2(.25f,.28f),false);
            blade=Probe("跨越盾边剑刃探针","sword",player,origin,new Vector2(.08f,1.1f),true);
            int previousExposed=exposedGuardHits,previousGuard=blockContacts;
            LaunchProbe(blade,origin,8);yield return new WaitForSeconds(.25f);
            bool exposed=enemy.health<120&&blockContacts>previousGuard&&exposedGuardHits>previousExposed;
            if(i==0)exposedHead=exposed;else exposedLeg=exposed;
            ClearFixtures();
        }
        body=Probe("遮挡查询身体探针","body",enemy,new Vector2(.2f,7),new Vector2(.18f,.8f),false);
        guard=Probe("遮挡查询盾牌探针","shield",enemy,new Vector2(0,7),new Vector2(.25f,.86f),false);
        blade=Probe("遮挡查询剑刃探针","sword",player,origin,new Vector2(.08f,1.1f),false);
        yield return new WaitForFixedUpdate();
        var query=new HitPacket {weapon=blade.Frame.GetComponent<DuelContact>(),other=body.Frame.GetComponent<DuelContact>(),relativeVelocity=Vector2.right*12,point=new Vector2(.15f,7)};
        bool coveredQuery=GuardOccludes(query);query.point=new Vector2(.15f,7.7f);bool headQuery=!GuardOccludes(query);
        query.point=new Vector2(.15f,6.3f);bool legQuery=!GuardOccludes(query);query.point=new Vector2(-.3f,7);bool backQuery=!GuardOccludes(query);
        ClearFixtures();
        player.InterruptAttack();player.stamina=0;enemy.health=120;
        body=Probe("零气力身体探针","body",enemy,new Vector2(0,7),new Vector2(.18f,.8f),false);
        blade=Probe("零气力剑刃探针","sword",player,origin,new Vector2(.08f,1.1f),true);
        bool emptyRejected=!player.TryManualAttack();LaunchProbe(blade,origin,8);yield return new WaitForSeconds(.25f);
        bool emptySafe=emptyRejected&&enemy.health==120;ClearFixtures();
        yield return PrepareProbeAttack();
        body=Probe("付出气力身体探针","body",enemy,new Vector2(0,7),new Vector2(.18f,.8f),false);
        blade=Probe("付出气力剑刃探针","sword",player,origin,new Vector2(.08f,1.1f),true);
        LaunchProbe(blade,origin,8);yield return new WaitForSeconds(.25f);bool paidDamage=enemy.health<120;ClearFixtures();
        fixtureResults=J.O("resting_blade_no_damage",restSafe,"separating_blade_no_damage",separatingSafe,"hilt_no_damage",hiltSafe,"shield_stops_damage",shieldSafe,"continuous_pressure_no_repeat_cost",pressureSafe,"exhausted_guard_gives_way",guardBreakSafe,"one_hit_per_stroke",singleHit,"ccd_60_units_per_second",ccd,"same_step_exposed_head_can_be_hit",exposedHead,"same_step_exposed_leg_can_be_hit",exposedLeg,"guard_geometry_covers_body",coveredQuery,"guard_geometry_leaves_head_exposed",headQuery,"guard_geometry_leaves_leg_exposed",legQuery,"guard_behind_target_does_not_block",backQuery,"zero_stamina_contact_no_damage",emptySafe,"paid_contact_damages",paidDamage);
        fixtureMode=false;training=true;ResetRound();
    }

    IEnumerator Smoke() {
        Directory.CreateDirectory(smokePath);started=true;training=true;
        float idlePeakAngle=0,idlePeakGap=0,idlePeakLimit=0,idleGrounded=0,idleSamples=0;
        yield return new WaitForSeconds(1.5f);
        float settledX=player.Hip.x,settledEnemyX=enemy.Hip.x;
        float until=Time.time+2;
        while(Time.time<until){idleGrounded+=player.feet.Values.Count(f=>f.grounded)+enemy.feet.Values.Count(f=>f.grounded);idleSamples+=4;idlePeakAngle=Mathf.Max(idlePeakAngle,Mathf.Abs(Mathf.DeltaAngle(player.torso.rb.rotation,0)),Mathf.Abs(Mathf.DeltaAngle(enemy.torso.rb.rotation,0)));idlePeakGap=Mathf.Max(idlePeakGap,player.MaxJointGap(),enemy.MaxJointGap());idlePeakLimit=Mathf.Max(idlePeakLimit,player.MaxLimitError(),enemy.MaxLimitError());yield return new WaitForFixedUpdate();}
        float idleHeight=player.torso.rb.position.y;
        float idleDrift=Mathf.Max(Mathf.Abs(player.Hip.x-settledX),Mathf.Abs(enemy.Hip.x-settledEnemyX));
        File.WriteAllText(Path.Combine(smokePath,"idle-feet.txt"),"player hip="+player.Hip+" enemy hip="+enemy.Hip+"\n"+string.Join("\n",player.feet.Select(p=>p.Key+" anchor="+p.Value.anchor+" goal="+p.Value.goal+" actual="+player.Joint("ankle"+p.Key)+" ground="+p.Value.grounded+" state="+p.Value.state+" target="+p.Value.support.target+" enabled="+p.Value.support.enabled)));
        bool idleSafe=player.health==120&&enemy.health==120&&hits==0;
        bones=true;ScreenCapture.CaptureScreenshot(Path.Combine(smokePath,"01-colliders.png"));
        yield return new WaitForSeconds(.08f);bones=false;ScreenCapture.CaptureScreenshot(Path.Combine(smokePath,"02-stance.png"));

        yield return CheckArms();
        yield return CheckCombatRules();
        yield return CheckWeapons();
        yield return CheckTwoHandWeapons();
        yield return CheckHandDrivePhysics();
        player.testOverride=true;enemy.testOverride=true;player.brace=false;enemy.brace=false;enemy.shieldControl=true;
        float startX=player.Hip.x;player.move=1;
        yield return new WaitForSeconds(1.25f);
        float forward=player.Hip.x-startX;player.move=-1;
        ScreenCapture.CaptureScreenshot(Path.Combine(smokePath,"03-step.png"));
        yield return new WaitForSeconds(1.25f);
        player.move=0;yield return new WaitForSeconds(.4f);
        float returnX=player.Hip.x,slip=player.maxFootSlip,walkLimit=Mathf.Max(player.MaxLimitError(),enemy.MaxLimitError());
        float plantedSeconds=player.feet.Values.Sum(f=>f.plantedSeconds);
        float holdRms=Mathf.Sqrt(player.feet.Values.Sum(f=>f.slipSquared)/Mathf.Max(.001f,plantedSeconds));
        float landingError=player.feet.Values.Max(f=>f.maxLandingError);
        int completedSteps=player.feet.Values.Sum(f=>f.completedSteps),airborneSteps=player.feet.Values.Sum(f=>f.airborneSteps);
        float maxStepSeconds=player.feet.Values.Max(f=>f.maxStepSeconds);
        bool walkSafe=forward>.75f&&returnX<startX+forward-.50f&&player.torso.rb.position.y>1.5f;
        float dashStart=player.Hip.x;bool dashStarted=player.Dash();
        yield return new WaitForSeconds(.9f);
        float dashDistance=(player.Hip.x-dashStart)*player.facing;
        bool dashSafe=dashStarted&&dashDistance>.15f&&player.health==120;
        yield return CheckContacts();
        bool fixturesSafe=fixtureResults.D.Values.All(v=>v.B);

        // Restart between fixtures and the arena so fixture damage/counters do
        // not inflate the genuine duel result.
        int beforeHits=hits,beforeBlocks=blocks,beforeKo=knockouts,beforeBlockContacts=blockContacts,beforeGuardBreaks=guardBreaks;
        strongestImpact=0;
        yield return new WaitForSeconds(1);training=false;
        float timeout=Time.time+90,maxGap=idlePeakGap,maxLimit=idlePeakLimit;
        while(Time.time<timeout&&!finished){maxGap=Mathf.Max(maxGap,player.MaxJointGap(),enemy.MaxJointGap());maxLimit=Mathf.Max(maxLimit,player.MaxLimitError(),enemy.MaxLimitError());File.WriteAllText(Path.Combine(smokePath,"live-joints.txt"),player.worstJoint+"\n"+enemy.worstJoint);yield return new WaitForSeconds(.05f);}
        int duelHits=hits-beforeHits,duelBlocks=blocks-beforeBlocks,duelKo=knockouts-beforeKo;
        float playerHealth=player.health,enemyHealth=enemy.health,seconds=elapsed;
        Fighter loser=player.health<=0?player:enemy;
        yield return new WaitForSeconds(1.1f);
        bool deadMotorsOff=loser.health<=0&&loser.limbs.Values.All(l=>l.joint==null||!l.joint.useMotor);
        bool deadSupportsOff=loser.health<=0&&loser.feet.Values.All(f=>!f.support.enabled);
        ScreenCapture.CaptureScreenshot(Path.Combine(smokePath,"04-duel.png"));yield return new WaitForSeconds(.1f);
        training=true;ResetRound();yield return new WaitForSeconds(.5f);
        bool resetSafe=player.health==120&&enemy.health==120;
        paused=true;yield return null;Vector2 before=player.pelvis.rb.position;
        yield return new WaitForSecondsRealtime(.5f);float pauseDrift=Vector2.Distance(before,player.pelvis.rb.position);
        paused=false;started=false;yield return null;ScreenCapture.CaptureScreenshot(Path.Combine(smokePath,"05-home.png"));yield return new WaitForSecondsRealtime(.2f);
        bool feetSafe=plantedSeconds>1&&holdRms<.12f&&slip<.32f&&landingError<.35f&&completedSteps>=4&&airborneSteps>=3&&maxStepSeconds<.7f;
        bool ok=handDriveResults["passed"].B&&twoHandResults["passed"].B&&weaponResults["passed"].B&&combatResults["passed"].B&&armResults["passed"].B&&idleSafe&&idleDrift<.2f&&idleGrounded/idleSamples>.6f&&fixturesSafe&&walkSafe&&dashSafe&&feetSafe&&resetSafe&&pauseDrift<.001f&&idleHeight>1.5f&&idlePeakAngle<15&&maxGap<.10f&&maxLimit<6&&duelHits>=3&&duelBlocks>0&&duelKo>0&&deadMotorsOff&&deadSupportsOff;
        var report=J.O("passed",ok,"version","0.9.0","hand_drive",handDriveResults,"two_hand",twoHandResults,"weapons",weaponResults,"combat_rules",combatResults,"arm_controls",armResults,"engine","Unity Physics2D","physics_hz",120,"idle_no_damage",idleSafe,"idle_body_height",idleHeight,"idle_position_drift",idleDrift,"idle_grounded_fraction",idleGrounded/idleSamples,"idle_peak_body_angle",idlePeakAngle,"maximum_joint_gap",maxGap,"maximum_limit_error_degrees",maxLimit,"walk_passed",walkSafe,"dash_passed",dashSafe,"dash_retained_distance",dashDistance,"walk_forward_distance",forward,"walk_return_x",returnX,"maximum_planted_foot_error",slip,"planted_foot_rms_error",holdRms,"planted_sample_seconds",plantedSeconds,"maximum_landing_error",landingError,"completed_steps",completedSteps,"airborne_steps",airborneSteps,"maximum_step_seconds",maxStepSeconds,"foot_support_passed",feetSafe,"walk_limit_error",walkLimit,"contact_regressions",fixtureResults,"arena_hits",duelHits,"arena_clash_events",duelBlocks,"arena_guard_contacts",blockContacts-beforeBlockContacts,"arena_guard_breaks",guardBreaks-beforeGuardBreaks,"arena_knockouts",duelKo,"dead_motors_off",deadMotorsOff,"dead_foot_supports_off",deadSupportsOff,"restart_restores_health",resetSafe,"pause_drift",pauseDrift,"arena_player_health",playerHealth,"arena_opponent_health",enemyHealth,"arena_seconds",seconds,"strongest_contact_normal_speed",strongestImpact);
        File.WriteAllText(Path.Combine(smokePath,"smoke.json"),report.ToString());Debug.Log("PHYSICS_DUEL_SMOKE "+report);Application.Quit(ok?0:2);
    }
}
