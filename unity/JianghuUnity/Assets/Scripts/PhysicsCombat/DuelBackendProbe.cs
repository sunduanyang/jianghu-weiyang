using System;
using System.Collections.Generic;
using System.Diagnostics;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    public J BackendInputSchedule(int frames){
        var rows=new List<J>();for(int i=0;i<frames;i++){BackendCommand(i,out var a,out var b,out float move);rows.Add(J.O("tick",i,"main_x",a.x,"main_y",a.y,"guard_x",b.x,"guard_y",b.y,"move",move,"kick",i%360==180));}return new J(rows);
    }
    static void BackendCommand(int frame,out Vector2 attack,out Vector2 guard,out float move){
        float t=frame/60f;int cycle=frame%480;
        attack=frame<60?Vector2.zero:new Vector2(Mathf.Cos(t*3.4f)*.85f,Mathf.Sin(t*3.4f)*.65f);
        guard=new Vector2(-.15f+Mathf.Sin(t*1.7f)*.25f,.25f+Mathf.Cos(t*1.7f)*.3f);
        move=cycle>=60&&cycle<150?1:cycle>=300&&cycle<390?-1:0;
    }
    public J ProbeBackendSequence(int frames){
        if(!IsSmoke||frames<120||frames>10800)throw new InvalidOperationException("Backend sequence requires isolated acceptance mode");
        Clock=elapsed=0;ResetRound();RunCampaign(true);fixtureMode=referenceBindProbe=true;referenceProbeBody=referenceProbeCollecting=true;referenceProbeContactEvents=0;muted=true;
        player.testOverride=enemy.testOverride=true;player.maxStamina=enemy.maxStamina=100000;player.stamina=enemy.stamina=100000;
        var times=new List<double>();var samples=new List<J>();double total=0;float maxGap=0,maxLimit=0,maxGrip=0,maxHandle=0,maxRoot=0,maxSpeed=0;int held=0;J worst=new J();
        for(int i=0;i<frames;i++){
            BackendCommand(i,out var a,out var b,out float move);
            player.ReferenceProbeInput(a,a);enemy.ReferenceProbeInput(b,b);player.move=move;enemy.move=-move*.35f;
            if(i%360==180)player.Dash();
            long start=Stopwatch.GetTimestamp();SimulateFighters();double ms=(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
            if(i>=60){times.Add(ms);total+=ms;}
            maxGap=Mathf.Max(maxGap,player.MaxJointGap(),enemy.MaxJointGap());float limit=Mathf.Max(player.MaxLimitError(),enemy.MaxLimitError());if(limit>maxLimit){maxLimit=limit;worst=GameClient.Arg("-comparisonDiagnostics")=="1"?ProbeSourceJointMetrics():J.O("time",Clock,"player_joint",player.worstLimitJoint,"enemy_joint",enemy.worstLimitJoint);}
            maxRoot=Mathf.Max(maxRoot,Mathf.Abs(player.ReferenceRootPositionForProbe.x),Mathf.Abs(enemy.ReferenceRootPositionForProbe.x));
            maxSpeed=Mathf.Max(maxSpeed,player.SourceAIWeaponSpeedForProbe,enemy.SourceAIWeaponSpeedForProbe);
            foreach(var fighter in new[]{player,enemy})if(fighter.weapon.id!="unarmed"&&fighter.primaryGrip!=null&&fighter.primaryGrip.enabled&&fighter.health>0){
                held++;maxGrip=Mathf.Max(maxGrip,fighter.PrimaryGripError,fighter.SupportGripError);
                maxHandle=Mathf.Max(maxHandle,Vector2.Distance(fighter.limbs["handNear"].B,fighter.sword.WorldPoint(fighter.weapon.Local(Vector2.zero,fighter.facing))));
            }
            if(i%60==0)samples.Add(J.O("tick",i,"gap",player.MaxJointGap(),"limit",player.MaxLimitError(),"main_tip_x",player.sword.B.x,"main_tip_y",player.sword.B.y,"root_x",player.Hip.x));
        }
        times.Sort();double p95=times[Math.Min(times.Count-1,(int)(times.Count*.95))];
        var result=J.O("backend",UseCorePhysics?"physics-core-2d":"legacy-physics2d","input_version",1,"weapon",player.weapon.id,"frames",frames,"substeps",referenceSteps,
            "simulation_seconds",Clock,"measured_frames",times.Count,"controller_and_physics_ms",total,"mean_tick_ms",total/times.Count,"p95_tick_ms",p95,
            "max_gap",maxGap,"max_limit_degrees",maxLimit,"max_held_grip_gap",maxGrip,"max_visible_handle_gap",maxHandle,"held_samples",held,
            "max_root_x",maxRoot,"max_weapon_speed",maxSpeed,"actual_body_contact_entries",referenceProbeContactEvents,
            "constraints_passed",maxGap<.15f&&maxLimit<8,"held_grip_passed",player.weapon.id=="unarmed"||held>100&&maxGrip<.03f&&maxHandle<.03f,
            "physics",PhysicsBackendMetrics(),"samples",new J(samples),"worst_limit",worst);
        elapsed=Clock;fixtureMode=referenceBindProbe=referenceProbeCollecting=false;PauseCampaign();return result;
    }
}
