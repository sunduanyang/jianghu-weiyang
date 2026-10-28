using System;
using System.Collections.Generic;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    public J ProbeReferenceStress(int steps,float duration=180){
        if(!IsSmoke||steps<0||steps>16)throw new InvalidOperationException("Stress probe requires isolated acceptance mode");
        ResetRound();Clock=elapsed=0;RunCampaign(true);fixtureMode=true;if(steps>0)referenceSteps=steps;steps=referenceSteps;
        player.testOverride=enemy.testOverride=true;
        for(int i=0;i<60;i++)SimulateFighters();
        player.testOverride=enemy.testOverride=false;
        float maxGap=0,maxLimit=0,at=0,start=Time.realtimeSinceStartup;int ticks=0;string worst="";
        player.Dash();
        while(Clock<duration&&player.health>0&&enemy.health>0){
            player.ReadAI(enemy,false);enemy.ReadAI(player,false);SimulateFighters();elapsed=Clock;ticks++;
            maxGap=Mathf.Max(maxGap,player.MaxJointGap(),enemy.MaxJointGap());
            float p=player.MaxLimitError(),e=enemy.MaxLimitError();
            if(Mathf.Max(p,e)>maxLimit){maxLimit=Mathf.Max(p,e);at=Clock;worst=p>e?player.worstLimitJoint:enemy.worstLimitJoint;}
        }
        var result=J.O("substeps",steps,"weapon",player.weapon.id,"ticks",ticks,"simulation_seconds",Clock,"wall_seconds",Time.realtimeSinceStartup-start,
            "max_gap",maxGap,"max_limit",maxLimit,"worst_time",at,"worst_joint",worst,"hits",hits,"player_health",player.health,"enemy_health",enemy.health,"limit_bound_passed",maxGap<.15f&&maxLimit<8);
        fixtureMode=false;PauseCampaign();return result;
    }
    // Only isolated native acceptance may command or reposition these bodies.
    public J ProbeReferenceKick(float spacing,bool mirrored=false,bool restoreWindup=false,bool restoreImpact=false,bool downed=false){
        if(!IsSmoke)throw new InvalidOperationException("Kick probe requires isolated acceptance mode");
        ResetRound();Clock=elapsed=0;RunCampaign(true);fixtureMode=true;
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
