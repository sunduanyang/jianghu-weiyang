using System;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    public bool UseCorePhysics {get;private set;}
    public DuelCoreWorld CoreWorld {get;private set;}
    long legacySimulationSteps;
    double legacySimulationMilliseconds;
    void InitializePhysicsBackend(){
        string requested=GameClient.Arg("-combatPhysics");
        if(requested.Length>0&&requested!="core"&&requested!="legacy")throw new ArgumentException("combatPhysics must be core or legacy");
        var saved=Campaign?campaign.battle["frame"]:new J();
        // Keep new shipping encounters on the established backend while the
        // compatibility Core bridge carries a higher CPU cost. An in-progress
        // frame keeps its backend even if the launcher requests another one;
        // frames written before this marker existed belong to legacy Physics2D.
        UseCorePhysics=saved.Has?saved["physics_backend"].S=="physics-core-2d":requested=="core";
        if(UseCorePhysics)CoreWorld=new DuelCoreWorld(this);
        Debug.Log("JIANGHU_COMBAT_BACKEND "+(UseCorePhysics?"physics-core-2d":"legacy-physics2d"));
    }
    void ResetPhysicsBackend(){CoreWorld?.Reset();legacySimulationSteps=0;legacySimulationMilliseconds=0;}
    void SimulateCombat(float dt){
        if(UseCorePhysics){CoreWorld.Step(dt);return;}
        long start=System.Diagnostics.Stopwatch.GetTimestamp();combatPhysics.Simulate(dt);legacySimulationSteps++;
        legacySimulationMilliseconds+=(System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000.0/System.Diagnostics.Stopwatch.Frequency;
    }
    public J PhysicsBackendMetrics()=>UseCorePhysics?CoreWorld.Metrics():J.O("backend","legacy-physics2d","native_steps",legacySimulationSteps,
        "simulation_ms",legacySimulationMilliseconds,"legacy_simulation_steps",legacySimulationSteps);
}
