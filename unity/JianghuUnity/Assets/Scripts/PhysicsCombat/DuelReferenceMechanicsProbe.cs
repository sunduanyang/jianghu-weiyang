using System;
using System.Collections.Generic;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    public J ProbeReferenceStress(int steps,float duration=180){
        if(!IsSmoke||steps<0||steps>16)throw new InvalidOperationException("Stress probe requires isolated acceptance mode");
        Clock=elapsed=0;ResetRound();RunCampaign(true);fixtureMode=true;if(steps>0)referenceSteps=steps;steps=referenceSteps;
        player.testOverride=enemy.testOverride=true;
        for(int i=0;i<60;i++)SimulateFighters();
        player.testOverride=enemy.testOverride=false;
        float maxGap=0,maxLimit=0,at=0,start=Time.realtimeSinceStartup;int ticks=0;string worst="";
        float minDistance=float.PositiveInfinity,maxDistance=0,maxPlayerWeaponSpeed=0,maxEnemyWeaponSpeed=0,nextAISample=1,maxRootX=0;
        int attackBandTicks=0,minimumSpacingTicks=0;var aiSamples=new List<J>();
        float maxHeldGrip=0,maxVisibleWeaponGrip=0;int heldGripSamples=0;
        J firstLimitViolation=new J(),firstGapViolation=new J();
        player.Dash();
        while(Clock<duration&&player.health>0&&enemy.health>0){
            player.ReadAI(enemy,false);enemy.ReadAI(player,false);SimulateFighters();elapsed=Clock;ticks++;
            float distance=player.SourceAIDistanceForProbe;
            maxRootX=Mathf.Max(maxRootX,Mathf.Abs(player.ReferenceRootPositionForProbe.x),Mathf.Abs(enemy.ReferenceRootPositionForProbe.x));
            minDistance=Mathf.Min(minDistance,distance);maxDistance=Mathf.Max(maxDistance,distance);
            maxPlayerWeaponSpeed=Mathf.Max(maxPlayerWeaponSpeed,player.SourceAIWeaponSpeedForProbe);
            maxEnemyWeaponSpeed=Mathf.Max(maxEnemyWeaponSpeed,enemy.SourceAIWeaponSpeedForProbe);
            if(player.SourceAIInBandForProbe||enemy.SourceAIInBandForProbe)attackBandTicks++;
            if(player.SourceAIBelowMinimumForProbe||enemy.SourceAIBelowMinimumForProbe)minimumSpacingTicks++;
            if(Clock>=nextAISample){nextAISample=Clock+1;aiSamples.Add(J.O("time",Clock,"distance",distance,"hits",hits,"clashes",blocks,"rejected_contacts",rejectedRestContacts,"player",player.SourceAICombatDiagnostics(),"enemy",enemy.SourceAICombatDiagnostics()));}
            maxGap=Mathf.Max(maxGap,player.MaxJointGap(),enemy.MaxJointGap());
            foreach(var fighter in new[]{player,enemy})if(fighter.weapon.id!="unarmed"&&fighter.primaryGrip!=null&&fighter.primaryGrip.enabled&&fighter.health>0){
                heldGripSamples++;maxHeldGrip=Mathf.Max(maxHeldGrip,fighter.PrimaryGripError,fighter.SupportGripError);
                maxVisibleWeaponGrip=Mathf.Max(maxVisibleWeaponGrip,Vector2.Distance(fighter.limbs["handNear"].B,fighter.sword.WorldPoint(fighter.weapon.Local(Vector2.zero,fighter.facing))));
            }
            float p=player.MaxLimitError(),e=enemy.MaxLimitError();
            if(!firstLimitViolation.Has&&Mathf.Max(p,e)>=8)firstLimitViolation=ProbeSourceJointMetrics();
            if(!firstGapViolation.Has&&Mathf.Max(player.MaxJointGap(),enemy.MaxJointGap())>=.15f)firstGapViolation=ProbeSourceJointMetrics();
            if(Mathf.Max(p,e)>maxLimit){maxLimit=Mathf.Max(p,e);at=Clock;worst=p>e?player.worstLimitJoint:enemy.worstLimitJoint;}
        }
        var result=J.O("substeps",steps,"velocity_iterations",Physics2D.velocityIterations,"position_iterations",Physics2D.positionIterations,"baumgarte",Physics2D.baumgarteScale,"ordered_limits_study",Arg("-referenceOrderLimits")=="1","source_mounts_study",Arg("-referenceSourceMounts")=="1","weapon",player.weapon.id,"ticks",ticks,"simulation_seconds",Clock,"wall_seconds",Time.realtimeSinceStartup-start,
            "max_gap",maxGap,"max_limit",maxLimit,"worst_time",at,"worst_joint",worst,"hits",hits,"player_health",player.health,"enemy_health",enemy.health,"limit_bound_passed",maxGap<.15f&&maxLimit<8,"max_root_x",maxRootX,"arena_bound_passed",maxRootX<6.1f,
            "ai_min_distance",minDistance,"ai_max_distance",maxDistance,"ai_attack_band_ticks",attackBandTicks,"ai_below_minimum_ticks",minimumSpacingTicks,
            "first_limit_violation",firstLimitViolation,"first_gap_violation",firstGapViolation,
            "player_max_weapon_point_speed",maxPlayerWeaponSpeed,"enemy_max_weapon_point_speed",maxEnemyWeaponSpeed,
            "strongest_non_kick_impact",strongestImpact,"non_kick_impact_observed",strongestImpact>0,"rejected_rest_contacts",rejectedRestContacts,"weapon_clashes",blocks,
            "held_grip_samples",heldGripSamples,"maximum_held_grip_gap",maxHeldGrip,"maximum_visible_weapon_handle_gap",maxVisibleWeaponGrip,
            "player_ai",player.SourceAICombatDiagnostics(),"enemy_ai",enemy.SourceAICombatDiagnostics(),"ai_samples",new J(aiSamples));
        fixtureMode=false;PauseCampaign();return result;
    }
    // Only isolated native acceptance may command or reposition these bodies.
    public J ProbeReferenceKick(float spacing,bool mirrored=false,bool restoreWindup=false,bool restoreImpact=false,bool downed=false){
        if(!IsSmoke)throw new InvalidOperationException("Kick probe requires isolated acceptance mode");
        Clock=elapsed=0;ResetRound();RunCampaign(true);fixtureMode=true;
        foreach(var f in new[]{player,enemy}){
            f.testOverride=true;
            f.PositionReferenceProbe((f==player?-1:1)*spacing*.5f);
            f.sword.shape.enabled=false;if(f.shield!=null)f.shield.shape.enabled=false;
        }
        Fighter attacker=mirrored?enemy:player,victim=mirrored?player:enemy;
        if(downed)victim.stamina=0;
        bool accepted=attacker.Dash(),repeat=attacker.Dash();
        float maxPush=0,minStamina=attacker.stamina;J windup=null,impact=null;bool restored=false;
        var samples=new List<J>();
        for(int tick=0;tick<65;tick++){
            SimulateFighters();elapsed=Clock;
            maxPush=Mathf.Max(maxPush,Mathf.Abs(victim.ReferenceRootVelocity.x));minStamina=Mathf.Min(minStamina,attacker.stamina);
            if(tick==8){windup=CaptureCampaign();if(restoreWindup){ResetRound();RestoreCampaign(windup);RunCampaign(true);attacker=mirrored?enemy:player;victim=mirrored?player:enemy;PrepareReferenceProbeAfterRestore();restored=true;}}
            if(impact==null&&victim.health<victim.maxHealth){impact=CaptureCampaign();if(restoreImpact){ResetRound();RestoreCampaign(impact);RunCampaign(true);attacker=mirrored?enemy:player;victim=mirrored?player:enemy;PrepareReferenceProbeAfterRestore();restored=true;}}
            samples.Add(J.O("time",Clock,"health",victim.health,"stamina",attacker.stamina,"kicking",attacker.ReferenceKicking,"active",attacker.ReferenceKickActive,"pushed",victim.ReferenceBeingKicked,"vx",victim.ReferenceRootVelocity.x,"hits",hits));
        }
        var result=J.O("spacing",spacing,"mirrored",mirrored,"accepted",accepted,"repeat_accepted",repeat,"restored",restored,"windup",windup,"impact",impact,
            "damage",victim.maxHealth-victim.health,"hits",hits,"min_stamina",minStamina,"max_push_speed",maxPush,"ended",!attacker.ReferenceKicking,"recovered",!victim.ReferenceBeingKicked,"samples",new J(samples));
        fixtureMode=false;PauseCampaign();return result;
    }
    void PrepareReferenceProbeAfterRestore(){foreach(var f in new[]{player,enemy}){f.testOverride=true;f.sword.shape.enabled=false;if(f.shield!=null)f.shield.shape.enabled=false;}}
    public sealed partial class Fighter {
        internal Vector2 ReferenceRootPositionForProbe=>mover.position;
        internal float SourceAIDistanceForProbe=>sourceOpponent==null?0:AIDistance;
        internal bool SourceAIInBandForProbe=>sourceOpponent!=null&&AIInBand;
        internal bool SourceAIBelowMinimumForProbe=>sourceOpponent!=null&&AIDistance<aiMinDistance;
        internal float SourceAIWeaponSpeedForProbe=>Mathf.Max(sword.rb.linearVelocity.magnitude,sword.rb.GetPointVelocity(sword.A).magnitude,sword.rb.GetPointVelocity(sword.B).magnitude);
        internal J SourceAICombatDiagnostics()=>J.O("root_x",mover.position.x,"root_y",mover.position.y,"state",SourceAIState.ToString(),"attacking",aiAttacking,"aggression",aiAggression,
            "attack_range",aiAttackRange,"minimum_distance",aiMinDistance,"preferred_distance",aiPreferredDistance,"item_length",SourceAIWeaponLength,
            "length_override",SourceAIWeaponLengthMetres,"strategy_distance",weapon.distance,"geometry_length",weapon.Length,"reach",weapon.reach,"tail",weapon.tail,
            "weapon_left",aiWeaponLeft,"weapon_right",aiWeaponRight,"two_handed",aiTwoHanded,"shield_left",aiShieldLeft,"shield_right",aiShieldRight,
            "decisions",aiDecisions,"relay_attacks",aiAttacks,"active_transitions",attackId,"walk_taps",SourceAIWalkTaps,"kick_schedules",SourceAIKickSchedules,
            "clip_left",SourceAILeftClip,"clip_right",SourceAIRightClip,"main_active",referenceHands["Near"].active,"off_active",referenceHands["Far"].active,
            "weapon_speed",sword.rb.linearVelocity.magnitude,"weapon_point_speed",SourceAIWeaponSpeedForProbe,"tip_root_x",Mathf.Abs(sword.B.x-mover.position.x),
            "main_grip",primaryGrip!=null&&primaryGrip.enabled,"stamina",stamina,"passed_out",PassedOut);
        public Vector2 ReferenceRootVelocity=>mover.linearVelocity;
        public void PositionReferenceProbe(float x){
            if(!game.IsSmoke)throw new InvalidOperationException("Position probe requires isolated acceptance mode");
            Vector2 delta=new Vector2(x-mover.position.x,0);
            foreach(var rb in root.GetComponentsInChildren<Rigidbody2D>())rb.position+=delta;
            foreach(var h in referenceHands.Values)h.target+=delta;
            referenceDriverStarts.Clear();referenceDriverGoals.Clear();Physics2D.SyncTransforms();
        }
    }
}
