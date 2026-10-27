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
    readonly List<GameObject> fixtures=new List<GameObject>();

    void RememberPhysicsSettings(){priorFixedStep=Time.fixedDeltaTime;priorTimeScale=Time.timeScale;priorTargetFps=Application.targetFrameRate;priorGravity=Physics2D.gravity;priorVelocityIterations=Physics2D.velocityIterations;priorPositionIterations=Physics2D.positionIterations;priorContactOffset=Physics2D.defaultContactOffset;}
    public void PauseCampaign(){paused=true;Time.timeScale=0;controls.ReleaseAll(true);player.ClearMouseCapture();enemy.ClearMouseCapture();}
    public void ActivateCampaign(){CampaignHidden=false;Application.targetFrameRate=90;PauseCampaign();}
    public void DeactivateCampaign(){PauseCampaign();CampaignHidden=true;Application.targetFrameRate=priorTargetFps;Time.timeScale=1;}
    void CampaignCommand(string command){PauseCampaign();campaign.command?.Invoke(command);}
    public void RunCampaign(bool run){paused=!run;help=false;Time.timeScale=run?1:0;}
    public void CampaignTestPad(bool primary,Vector2 direction){
        if(GameClient.Arg("-physicalCombatSmoke").Length==0)throw new InvalidOperationException("Test input requires isolated acceptance mode");
        int id=primary?7001:7002;var pad=primary?controls.right:controls.left;
        if(!pad.captured)controls.Begin(id,primary?new Vector2(1235,655):new Vector2(365,655));
        controls.Move(id,pad.center+new Vector2(direction.x,-direction.y)*DuelControls.Pad.Radius);
    }
    public void CampaignReleasePads(){controls.ReleaseAll();}
    public J CaptureCampaign()=>J.O("engine","unity-physics2d-v09","version",1,"elapsed",elapsed,"hits",hits,"clashes",blocks,
        "fighters",new J(new List<J>{player.CapturePhysicalState(),enemy.CapturePhysicalState()}));
    void RestoreCampaign(J frame){
        if(!frame.Has)return;
        elapsed=(float)frame["elapsed"].N;hits=frame["hits"].I;blocks=frame["clashes"].I;
        Clock=elapsed;
        player.RestorePhysicalState(frame["fighters"][0]);enemy.RestorePhysicalState(frame["fighters"][1]);
        Physics2D.SyncTransforms();
    }
    void DrawCampaignPanel(){
        if(help){DrawHelp();return;}
        if(!paused&&!finished)return;
        if(finished&&!paused&&Clock-resultAt<1.3f)return;
        Panel(new Rect(500,265,600,395));
        Label(new Rect(535,291,530,64),finished?result:"切 磋 暂 歇",38,ink,TextAnchor.MiddleCenter);
        Label(new Rect(540,361,520,60),CampaignMessage.Length>0?CampaignMessage:finished?"交手结果正在结算，伤势、装备损耗与经历会写入江湖。":"进退、角度与挥动决定接触。兵器来自当前装备，不能在交手中换装。",19,jade,TextAnchor.MiddleCenter);
        if(!finished&&Button(new Rect(560,435,480,55),"继续交手",true))paused=false;
        if(finished&&Button(new Rect(560,435,480,55),"结算并返回",true))CampaignCommand("complete");
        if(!finished&&Button(new Rect(560,506,230,48),"保存并暂离"))CampaignCommand("leave");
        if(!finished&&Button(new Rect(810,506,230,48),"收招认输"))CampaignCommand("surrender");
        if(Button(new Rect(560,575,480,43),finished?"重试结算":"保存战局"))CampaignCommand(finished?"complete":"checkpoint");
    }
    void OnDestroy(){
        if(player!=null)Destroy(player.root);if(enemy!=null)Destroy(enemy.root);
        Time.fixedDeltaTime=priorFixedStep;Time.timeScale=priorTimeScale;Application.targetFrameRate=priorTargetFps;Physics2D.gravity=priorGravity;
        Physics2D.velocityIterations=priorVelocityIterations;Physics2D.positionIterations=priorPositionIterations;Physics2D.defaultContactOffset=priorContactOffset;
        foreach(var material in new[]{bodyMaterial,footMaterial,bladeMaterial,shieldMaterial})if(material!=null)Destroy(material);
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
            return J.O("health",health,"stamina",stamina,"balance",balance,"strength",strength,"heat",swingHeat,"motion_spent",motionStaminaSpent,
                "attack_count",attackId,"landed_hits",landedHits,"bodies",new J(bodies),"feet",plants,
                "stun_left",Mathf.Max(0,stunUntil-game.Clock),"injury_left",Mathf.Max(0,injuredUntil-game.Clock),
                "guard_break_left",Mathf.Max(0,guardBrokenUntil-game.Clock),"regen_left",Mathf.Max(0,regenAt-game.Clock),
                "motion_exhausted",motionExhausted?1:0,"swing_seconds",movingSeconds);
        }
        public void RestorePhysicalState(J state){
            foreach(var p in state["bodies"].A){var rb=limbs[p["key"].S].rb;rb.position=new Vector2((float)p["x"].N,(float)p["y"].N);rb.rotation=(float)p["angle"].N;rb.linearVelocity=new Vector2((float)p["vx"].N,(float)p["vy"].N);rb.angularVelocity=(float)p["spin"].N;}
            health=(float)state["health"].N;stamina=(float)state["stamina"].N;balance=(float)state["balance"].N;strength=(float)state["strength"].N;
            swingHeat=(float)state["heat"].N;motionStaminaSpent=(float)state["motion_spent"].N;attackId=state["attack_count"].I;landedHits=state["landed_hits"].I;
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
