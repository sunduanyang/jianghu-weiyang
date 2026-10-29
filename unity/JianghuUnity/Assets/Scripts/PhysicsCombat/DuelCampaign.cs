using System;
using System.Collections.Generic;
using System.Linq;
using Jianghu;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed partial class PhysicsDuel {
    public sealed class CampaignOptions {
        public J battle;
        public string playerSkin,enemySkin;
        public Action<string> command;
    }
    public CampaignOptions campaign;
    public bool Campaign=>campaign!=null;
    public bool CampaignFinished=>finished;
    public bool CampaignAutoplay;
    public bool CampaignHidden {get;private set;}
    public float Clock {get;private set;}
    PhysicsScene2D combatPhysics;
    public float CampaignElapsed=>elapsed;
    public string CampaignMessage="";
    float priorFixedStep,priorTimeScale;
    Vector2 priorGravity;
    int priorVelocityIterations,priorPositionIterations,priorTargetFps;
    float priorContactOffset;
    float priorLinearCorrection,priorAngularCorrection,priorTranslationSpeed,priorRotationSpeed,priorBaumgarte,priorTOI;
    readonly List<GameObject> fixtures=new List<GameObject>();

    void RememberPhysicsSettings(){priorFixedStep=Time.fixedDeltaTime;priorTimeScale=Time.timeScale;priorTargetFps=Application.targetFrameRate;priorGravity=Physics2D.gravity;priorVelocityIterations=Physics2D.velocityIterations;priorPositionIterations=Physics2D.positionIterations;priorContactOffset=Physics2D.defaultContactOffset;priorLinearCorrection=Physics2D.maxLinearCorrection;priorAngularCorrection=Physics2D.maxAngularCorrection;priorTranslationSpeed=Physics2D.maxTranslationSpeed;priorRotationSpeed=Physics2D.maxRotationSpeed;priorBaumgarte=Physics2D.baumgarteScale;priorTOI=Physics2D.baumgarteTOIScale;}
    public void PauseCampaign(){player?.SourceAIClockPaused(true);enemy?.SourceAIClockPaused(true);paused=true;Time.timeScale=0;controls.ReleaseAll(true);player.ClearMouseCapture();enemy.ClearMouseCapture();}
    public void ActivateCampaign(){CampaignHidden=false;Application.targetFrameRate=90;PauseCampaign();}
    public void DeactivateCampaign(){PauseCampaign();CampaignHidden=true;Application.targetFrameRate=priorTargetFps;Time.timeScale=1;}
    void CampaignCommand(string command){PauseCampaign();campaign.command?.Invoke(command);}
    public void RunCampaign(bool run){player?.SourceAIClockPaused(!run);enemy?.SourceAIClockPaused(!run);paused=!run;help=false;Time.timeScale=run?1:0;}
    public void CampaignTestPad(bool primary,Vector2 direction){
        if(GameClient.Arg("-physicalCombatSmoke").Length==0&&GameClient.Arg("-craftsSmoke").Length==0)throw new InvalidOperationException("Test input requires isolated acceptance mode");
        int id=primary?7001:7002;var pad=primary?controls.right:controls.left;
        if(!pad.captured)controls.Begin(id,primary?new Vector2(1235,655):new Vector2(365,655));
        controls.Move(id,pad.center+new Vector2(direction.x,-direction.y)*DuelControls.Pad.Radius);
    }
    public void CampaignReleasePads(){controls.ReleaseAll();}
    public void StepCampaignForAcceptance(float seconds){
        if((GameClient.Arg("-interactionGameplaySmoke").Length==0&&GameClient.Arg("-craftsSmoke").Length==0)||seconds<=0||seconds>1)throw new InvalidOperationException("Controlled campaign steps require isolated interaction acceptance");
        bool previousFixture=fixtureMode;fixtureMode=true;
        try{int ticks=Mathf.RoundToInt(seconds/Time.fixedDeltaTime);for(int i=0;i<ticks;i++)SimulateFighters();elapsed=Clock;}
        finally{fixtureMode=previousFixture;}
    }
    public J CaptureCampaign()=>J.O("physics_backend",UseCorePhysics?"physics-core-2d":"legacy-physics2d","engine","unity-physics2d-v09","version",1,"elapsed",ReferenceCombat?(double)Clock:elapsed,"hits",hits,"clashes",blocks,
        "fighters",new J(new List<J>{player.CapturePhysicalState(),enemy.CapturePhysicalState()}));
    void RestoreCampaign(J frame){
        if(!frame.Has)return;
        elapsed=(float)frame["elapsed"].N;hits=frame["hits"].I;blocks=frame["clashes"].I;
        Clock=elapsed;
        player.RestorePhysicalState(frame["fighters"][0]);enemy.RestorePhysicalState(frame["fighters"][1]);
        player.ResolveSourceImpalement();enemy.ResolveSourceImpalement();
        Physics2D.SyncTransforms();
        player.FinishSourceRigRestore();enemy.FinishSourceRigRestore();
        if(UseCorePhysics)CoreWorld.Synchronize();
    }
    public void RestoreCampaignForSmoke(J frame){if(!IsSmoke)throw new InvalidOperationException("Restore probe requires acceptance mode");RestoreCampaign(frame);}
    void DrawCampaignPanel(){
        if(IsSmoke&&GameClient.Arg("-physicalCombatPhase")=="backend-comparison")return;
        if(help){DrawHelp();return;}
        if(!paused&&!finished)return;
        if(finished&&!paused&&Clock-resultAt<1.3f)return;
        Panel(new Rect(500,265,600,395));
        bool spar=campaign.battle["spar"].B;
        Label(new Rect(535,291,530,64),finished?result:spar?"切 磋 暂 歇":"交 手 暂 歇",38,ink,TextAnchor.MiddleCenter);
        Label(new Rect(540,361,520,60),CampaignMessage.Length>0?CampaignMessage:finished?(spar?"交手结果正在结算，下一场恢复完整状态。":"正在结算本次交手的体力、伤势与战利品。"):(spar?"进退、角度与挥动决定接触。兵器来自当前装备，不能在交手中换装。":"本次是敌对交手，损伤带回江湖。抢夺须胜出才有战利品。"),19,jade,TextAnchor.MiddleCenter);
        if(!finished&&Button(new Rect(560,435,480,55),"继续交手",true))paused=false;
        if(finished&&Button(new Rect(560,435,480,55),"结算并返回",true))CampaignCommand("complete");
        if(!finished&&Button(new Rect(560,506,230,48),"保存并暂离"))CampaignCommand("leave");
        if(!finished&&Button(new Rect(810,506,230,48),"收招认输"))CampaignCommand("surrender");
        if(Button(new Rect(560,575,480,43),finished?"重试结算":"保存战局"))CampaignCommand(finished?"complete":"checkpoint");
    }
    void OnDestroy(){
        CoreWorld?.Dispose();
        if(player!=null){player.ReleaseSourceRig();Destroy(player.root);}if(enemy!=null){enemy.ReleaseSourceRig();Destroy(enemy.root);}
        Time.fixedDeltaTime=priorFixedStep;Time.timeScale=priorTimeScale;Application.targetFrameRate=priorTargetFps;Physics2D.gravity=priorGravity;
        Physics2D.velocityIterations=priorVelocityIterations;Physics2D.positionIterations=priorPositionIterations;Physics2D.defaultContactOffset=priorContactOffset;
        Physics2D.maxLinearCorrection=priorLinearCorrection;Physics2D.maxAngularCorrection=priorAngularCorrection;Physics2D.maxTranslationSpeed=priorTranslationSpeed;Physics2D.maxRotationSpeed=priorRotationSpeed;Physics2D.baumgarteScale=priorBaumgarte;Physics2D.baumgarteTOIScale=priorTOI;
        foreach(var material in new[]{bodyMaterial,footMaterial,bladeMaterial,shieldMaterial,referenceRootMaterial,referencePelvisMaterial,referenceGroundMaterial,referenceHandleMaterial})if(material!=null)Destroy(material);
        if(background!=null)Destroy(background);if(disk!=null)Destroy(disk);if(strikeSound!=null)Destroy(strikeSound);if(blockSound!=null)Destroy(blockSound);
        if(Instance==this)Instance=null;
    }

    public sealed partial class Fighter {
        public string displayName="";
        public float maxHealth=120,damageScale=1,damageReduction,handlingScale=1;
        public int landedHits;
        public bool HasShield=true;
        public FixedJoint2D offhandGrip;
        public J CapturePhysicalState(){
            var bodies=limbs.Select(p=>J.O("key",p.Key,"x",p.Value.rb.position.x,"y",p.Value.rb.position.y,"angle",p.Value.rb.rotation,
                "vx",p.Value.rb.linearVelocity.x,"vy",p.Value.rb.linearVelocity.y,"spin",p.Value.rb.angularVelocity)).ToList();
            var plants=J.O();foreach(var p in feet){var f=p.Value;plants[p.Key]=J.O("state",(int)f.state,"phase",f.phase,"anchor_x",f.anchor.x,"anchor_y",f.anchor.y,"from_x",f.from.x,"from_y",f.from.y,"target_x",f.target.x,"target_y",f.target.y);}
            var state=J.O("health",health,"stamina",stamina,"balance",balance,"strength",strength,"heat",swingHeat,"motion_spent",motionStaminaSpent,
                "attack_count",attackId,"landed_hits",landedHits,"bodies",new J(bodies),"feet",plants,
                "stun_left",Mathf.Max(0,stunUntil-game.Clock),"injury_left",Mathf.Max(0,injuredUntil-game.Clock),
                "guard_break_left",Mathf.Max(0,guardBrokenUntil-game.Clock),"regen_left",Mathf.Max(0,regenAt-game.Clock),
                "motion_exhausted",motionExhausted?1:0,"swing_seconds",movingSeconds);
            if(Reference)state["reference"]=CaptureReference();return state;
        }
        public void RestorePhysicalState(J state){
            foreach(var p in state["bodies"].A){var rb=limbs[p["key"].S].rb;rb.position=new Vector2((float)p["x"].N,(float)p["y"].N);rb.rotation=(float)p["angle"].N;rb.linearVelocity=new Vector2((float)p["vx"].N,(float)p["vy"].N);rb.angularVelocity=(float)p["spin"].N;}
            health=(float)state["health"].N;stamina=(float)state["stamina"].N;balance=(float)state["balance"].N;strength=(float)state["strength"].N;
            swingHeat=(float)state["heat"].N;motionStaminaSpent=(float)state["motion_spent"].N;attackId=state["attack_count"].I;landedHits=state["landed_hits"].I;
            if(Reference){RestoreReference(state["reference"]);return;}
            foreach(var p in feet){var f=p.Value;var s=state["feet"][p.Key];f.state=(FootPlant.Phase)s["state"].I;f.phase=(float)s["phase"].N;f.anchor=new Vector2((float)s["anchor_x"].N,(float)s["anchor_y"].N);f.from=new Vector2((float)s["from_x"].N,(float)s["from_y"].N);f.target=new Vector2((float)s["target_x"].N,(float)s["target_y"].N);f.goal=f.state==FootPlant.Phase.Planted?f.anchor:Joint("ankle"+p.Key);f.support.target=f.anchor;}
            hasCarryPose=false;hasTwoHandPose=false;InterruptAttack();
            stunUntil=game.Clock+Ability(state,"stun_left",0);injuredUntil=game.Clock+Ability(state,"injury_left",0);
            guardBrokenUntil=game.Clock+Ability(state,"guard_break_left",0);regenAt=game.Clock+Ability(state,"regen_left",.35f*recoveryDelayScale);
            motionExhausted=state["motion_exhausted"].Has?state["motion_exhausted"].I==1:StaminaRatio<=.005f;
            movingSeconds=Ability(state,"swing_seconds",0);
            foreach(var side in new[]{"Near","Far"})handTargets[side].target=limbs["hand"+side].A;
            RestoreMousePose();
            if(health<=0)RelaxHandDrives();foreach(var limb in limbs.Values)limb.SampleVelocity();
        }
    }
}
