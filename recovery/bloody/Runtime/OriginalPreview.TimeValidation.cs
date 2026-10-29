using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jianghu;
using UnityEngine;
using UnityEngine.UI;

// Opt-in timing regression. Victories below are UI progression fixtures,
// never a claim of natural combat, damage or APK trajectory equivalence.
public sealed partial class OriginalPreview {
    sealed class TimeValidationRun {
        public string directory,phase="starting";
        public double started,deadline;
        public bool passed=true,priorExternal;
        public float priorVolume,expectedScale=-1;
        public int badRelays;
        public readonly List<J> frames=new List<J>(),relays=new List<J>(),cases=new List<J>(),actions=new List<J>(),errors=new List<J>(),engineErrors=new List<J>();
        public readonly HashSet<OriginalUITransitionAnimator> drivers=new HashSet<OriginalUITransitionAnimator>();
        public Action<OriginalUITransitionAnimator,float> relay;
        public Application.LogCallback log;
    }
    void TryStartOriginalTimeValidation() {
        var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-validateOriginalTime");if(at<0)return;
        var run=new TimeValidationRun {
            directory=Path.GetFullPath(at+1<args.Length?args[at+1]:Path.Combine(Application.persistentDataPath,"OriginalTimeValidation")),
            started=Time.realtimeSinceStartupAsDouble,priorExternal=ExternalInputReplay,priorVolume=AudioListener.volume
        };
        run.deadline=run.started+60;Directory.CreateDirectory(run.directory);
        ExternalInputReplay=true;AudioListener.volume=0;ResetPointers();
        run.relay=(driver,value)=> {
            var row=TimeValidationState(run,"relay");row["value"]=new J(value);row["driver"]=TimeValidationDriver(driver);
            run.relays.Add(row);File.AppendAllText(Path.Combine(run.directory,"time-relays.jsonl"),J.Write(row)+Environment.NewLine);
            if(run.expectedScale>=0&&(!float.IsFinite(Time.timeScale)||Mathf.Abs(Time.timeScale-run.expectedScale)>.000001f))run.badRelays++;
        };
        run.log=(message,stack,type)=> {
            if(type!=LogType.Error&&type!=LogType.Exception&&type!=LogType.Assert)return;
            run.engineErrors.Add(J.O("type",type.ToString(),"message",message,"stack",stack,"phase",run.phase,"frame",Time.frameCount));
            if(type==LogType.Exception||type==LogType.Assert)run.passed=false;
        };
        Application.logMessageReceived+=run.log;TimeValidationSubscribe(run);
        StartCoroutine(ValidateOriginalTime(run));
    }
    static string TimeValidationPath(Transform node) {
        if(node==null)return "";string path=node.name;while(node.parent!=null){node=node.parent;path=node.name+"/"+path;}return path;
    }
    static J TimeValidationAnimator(Animator animator) {
        if(animator==null)return J.O("missing",true);
        var row=J.O("name",animator.name,"enabled",animator.enabled,"active",animator.gameObject.activeInHierarchy,"initialized",animator.isInitialized,
            "speed",animator.speed,"update_mode",animator.updateMode.ToString());
        if(!animator.isInitialized||animator.runtimeAnimatorController==null||animator.layerCount==0)return row;
        var state=animator.GetCurrentAnimatorStateInfo(0);bool transition=animator.IsInTransition(0);
        row["current"]=J.O("full_path_hash",state.fullPathHash,"short_name_hash",state.shortNameHash,"normalized_time",state.normalizedTime,"length",state.length,
            "clips",new J(animator.GetCurrentAnimatorClipInfo(0).Select(c=>J.O("name",c.clip==null?"":c.clip.name,"weight",c.weight)).ToList()));
        row["in_transition"]=new J(transition);
        if(transition) {
            var next=animator.GetNextAnimatorStateInfo(0);
            row["next"]=J.O("full_path_hash",next.fullPathHash,"short_name_hash",next.shortNameHash,"normalized_time",next.normalizedTime,"length",next.length,
                "clips",new J(animator.GetNextAnimatorClipInfo(0).Select(c=>J.O("name",c.clip==null?"":c.clip.name,"weight",c.weight)).ToList()));
        }
        return row;
    }
    static J TimeValidationDriver(OriginalUITransitionAnimator driver)=>driver==null?J.O("destroyed",true):J.O("name",driver.name,"path",TimeValidationPath(driver.transform),
        "active",driver.gameObject.activeInHierarchy,"exiting",driver.Exiting,"animator",TimeValidationAnimator(driver.Animator));
    void TimeValidationSubscribe(TimeValidationRun run) {
        if(hud==null)return;
        foreach(var driver in hud.GetComponentsInChildren<OriginalUITransitionAnimator>(true))if(run.drivers.Add(driver))driver.TimeScaleRelayed+=run.relay;
    }
    J TimeValidationState(TimeValidationRun run,string kind)=>J.O("kind",kind,"phase",run.phase,"frame",Time.frameCount,"real_time",Time.realtimeSinceStartupAsDouble,
        "scaled_time",Time.timeAsDouble,"delta_time",Time.deltaTime,"unscaled_delta_time",Time.unscaledDeltaTime,
        "delta_ratio",Time.unscaledDeltaTime>0?(object)(Time.deltaTime/Time.unscaledDeltaTime):null,"time_scale",Time.timeScale,"expected_scale",run.expectedScale,
        "source_time_scale",sourceTimeScale,"state",OriginalState,"level",Progress.Level,"paused",Paused,"transitioning",IsTransitioning,
        "player_id",player==null?0:player.GetInstanceID(),"motion_tick",player==null?-1:player.OriginalMotionTick,"health",player==null?null:(object)player.health,
        "enemy_health",enemy==null?null:(object)enemy.health,"fixed_dt",Time.fixedDeltaTime,"native_ui_time_events",NativeUiTimeEvents);
    void TimeValidationFrame(TimeValidationRun run) {
        TimeValidationSubscribe(run);var row=TimeValidationState(run,"frame");
        row["hud_drivers"]=new J(run.drivers.Where(d=>d!=null&&d.gameObject.activeInHierarchy).Select(TimeValidationDriver).ToList());
        run.frames.Add(row);File.AppendAllText(Path.Combine(run.directory,"time-frames.jsonl"),J.Write(row)+Environment.NewLine);
    }
    void TimeValidationAction(TimeValidationRun run,string action,J details=null) {
        var row=TimeValidationState(run,action);if(details!=null)row["details"]=details;run.actions.Add(row);
    }
    Button TimeValidationButton(params string[] names)=>hud==null?null:hud.GetComponentsInChildren<Button>(true)
        .FirstOrDefault(b=>names.Contains(b.name)&&b.gameObject.activeInHierarchy&&b.IsActive()&&b.IsInteractable());
    IEnumerator TimeValidationWaitActive(TimeValidationRun run,int level,int previousId=0) {
        run.phase="wait_level_"+level+"_active";run.expectedScale=-1;
        while(player==null||Progress.Level!=level||OriginalState!=4||Paused||IsTransitioning||hud==null||hud.OriginalTransitionBlocksPointerInput||player.GetInstanceID()==previousId) {
            if(Progress.Level>level)throw new InvalidOperationException("Campaign skipped expected level "+level);
            yield return null;
        }
        if(player.dead)throw new InvalidOperationException("Player already dead at level "+level+" activation");
        TimeValidationAction(run,"level_active",J.O("new_player",previousId==0||player.GetInstanceID()!=previousId));
    }
    IEnumerator TimeValidationObserve(TimeValidationRun run,string label,double seconds,float expected,bool strict) {
        run.phase=label;run.expectedScale=strict?expected:-1;
        var rig=player;int level=Progress.Level,firstTick=rig.OriginalMotionTick,badScale=0,badContext=0,nonFinite=0,frames=0,relayBefore=run.badRelays;
        double start=Time.realtimeSinceStartupAsDouble,scaled=0,unscaled=0;float minScale=float.PositiveInfinity,maxScale=float.NegativeInfinity;
        while(Time.realtimeSinceStartupAsDouble-start<seconds) {
            yield return null;frames++;scaled+=Time.deltaTime;unscaled+=Time.unscaledDeltaTime;
            minScale=Mathf.Min(minScale,Time.timeScale);maxScale=Mathf.Max(maxScale,Time.timeScale);
            if(!float.IsFinite(Time.timeScale)||!float.IsFinite(Time.deltaTime)||!float.IsFinite(Time.unscaledDeltaTime))nonFinite++;
            if(strict&&Mathf.Abs(Time.timeScale-expected)>.000001f)badScale++;
            if(player!=rig||Progress.Level!=level||OriginalState!=4||IsTransitioning||Paused!=(expected==0))badContext++;
        }
        double real=Time.realtimeSinceStartupAsDouble-start,ratio=scaled/real;
        int ticks=rig==null?-1:rig.OriginalMotionTick-firstTick;double tickRate=ticks/real,targetRate=expected/Time.fixedDeltaTime;
        bool tickPassed=expected==0?ticks==0:tickRate>=targetRate*.8&&tickRate<=targetRate*1.2;
        bool ratioPassed=expected==0?Math.Abs(scaled)<.00001:ratio>=.9&&ratio<=1.1;
        bool passed=nonFinite==0&&badContext==0&&(!strict||(badScale==0&&run.badRelays==relayBefore&&tickPassed&&ratioPassed));
        var result=J.O("case",label,"level",level,"strict",strict,"requested_seconds",seconds,"real_seconds",real,"frames",frames,"scaled_seconds",scaled,
            "unscaled_delta_sum",unscaled,"scaled_over_real",ratio,"ticks",ticks,"tick_hz",tickRate,"expected_tick_hz",targetRate,"tick_tolerance_fraction",.2,
            "time_scale_min",minScale,"time_scale_max",maxScale,"bad_scale_frames",badScale,"bad_context_frames",badContext,"non_finite_frames",nonFinite,
            "bad_scale_relays",run.badRelays-relayBefore,"passed",passed);
        run.cases.Add(result);run.passed&=passed;
        if(!passed)run.errors.Add(J.O("reason","observation_failed","case",label,"details",result));
        run.expectedScale=-1;
    }
    IEnumerator TimeValidationNext(TimeValidationRun run,int level) {
        run.phase="level_"+level+"_fixture_victory";run.expectedScale=-1;
        if(player==null||OriginalState!=4||Paused||IsTransitioning||Progress.Level!=level||player.dead)
            throw new InvalidOperationException("Active living Campaign required before explicit victory fixture at level "+level);
        int previous=player.GetInstanceID();float health=player.health;
        TimeValidationAction(run,"fixture_FinishOriginalRound_true",J.O("natural_victory",false,"changes_hp_or_physics",false,"player_health",health));
        FinishOriginalRound(true);
        if(OriginalState!=6)throw new InvalidOperationException("Campaign fixture did not reach match end; roundsToWin="+RoundsToWin);
        double verdictReady=Time.realtimeSinceStartupAsDouble+3.25;
        run.phase="level_"+level+"_wait_next_button";Button next;
        while((next=TimeValidationButton("Button_Next"))==null||IsTransitioning||Time.realtimeSinceStartupAsDouble<verdictReady)yield return null;
        TimeValidationAction(run,"invoke_original_next_onClick",J.O("path",TimeValidationPath(next.transform),"active",next.gameObject.activeInHierarchy,"interactable",next.IsInteractable()));
        next.onClick.Invoke();
        yield return TimeValidationWaitActive(run,level+1,previous);
    }
    IEnumerator RunOriginalTimeValidation(TimeValidationRun run) {
        string prefix=run.directory.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
        bool isolated=Progress.SavePath!=null&&Path.GetFullPath(Progress.SavePath).StartsWith(prefix,StringComparison.OrdinalIgnoreCase);
        TimeValidationAction(run,"startup",J.O("save_path",Progress.SavePath,"save_inside_validation_directory",isolated,"initial_snapshot",Progress.Snapshot(),
            "completed_levels",Progress.GetCompletedLevelsForRegion(Progress.Region),"initial_stage",Progress.Stage));
        if(!isolated||GameMode!=0||Progress.Level!=1||Progress.GetCompletedLevelsForRegion(Progress.Region)!=0)
            throw new InvalidOperationException("Requires isolated fresh progress inside validation directory, GameMode0, Campaign Level1; supply -originalProgressPath and -originalGameMode 0");
        yield return TimeValidationWaitActive(run,1);
        // Cinematic Exit legitimately ramps .2 -> 1 during its first frames.
        run.phase="level_1_opening_grace";double openingReady=Time.realtimeSinceStartupAsDouble+1;
        while(Time.realtimeSinceStartupAsDouble<openingReady)yield return null;
        yield return TimeValidationObserve(run,"level_1_active_before_fixture",3,1,true);
        yield return TimeValidationNext(run,1);
        yield return TimeValidationObserve(run,"level_2_opening_grace",1,1,false);
        yield return TimeValidationObserve(run,"level_2_active",5,1,true);
        run.phase="level_2_pause_button";Button pause=TimeValidationButton("Button_Pause","PauseButton");
        if(pause==null)throw new InvalidOperationException("Active pause button missing on level2");
        TimeValidationAction(run,"invoke_original_pause_onClick",J.O("path",TimeValidationPath(pause.transform)));pause.onClick.Invoke();
        yield return TimeValidationObserve(run,"level_2_paused",.5,0,true);
        run.phase="level_2_wait_unpause";Button unpause;while((unpause=TimeValidationButton("Button_Unpause"))==null)yield return null;
        TimeValidationAction(run,"invoke_original_unpause_onClick",J.O("path",TimeValidationPath(unpause.transform)));unpause.onClick.Invoke();
        yield return TimeValidationObserve(run,"level_2_resume_grace",1,1,false);
        yield return TimeValidationObserve(run,"level_2_resumed",2,1,true);
        yield return TimeValidationNext(run,2);
        yield return TimeValidationObserve(run,"level_3_opening_grace",1,1,false);
        yield return TimeValidationObserve(run,"level_3_active",5,1,true);
    }
    IEnumerator ValidateOriginalTime(TimeValidationRun run) {
        var stack=new Stack<IEnumerator>();stack.Push(RunOriginalTimeValidation(run));
        // Flatten child iterators so every wait is covered by the real-time
        // watchdog and exceptions produce a finite JSON failure and Quit.
        while(stack.Count>0) {
            bool more=false;object next=null;Exception failure=null;
            try {
                if(Time.realtimeSinceStartupAsDouble>run.deadline)throw new TimeoutException("60 second real-time watchdog in "+run.phase);
                more=stack.Peek().MoveNext();if(more)next=stack.Peek().Current;
                if(more&&next==null)TimeValidationFrame(run);
            } catch(Exception error){failure=error;}
            if(failure!=null){run.passed=false;run.errors.Add(J.O("reason","exception_or_watchdog","phase",run.phase,"error",failure.ToString(),"frame",Time.frameCount));break;}
            if(!more){(stack.Pop() as IDisposable)?.Dispose();continue;}
            if(next is IEnumerator child){stack.Push(child);continue;}
            if(next!=null){run.passed=false;run.errors.Add(J.O("reason","unexpected_yield","type",next.GetType().FullName));break;}
            yield return null;
        }
        foreach(var iterator in stack)(iterator as IDisposable)?.Dispose();
        run.phase="finished";run.expectedScale=-1;
        foreach(var driver in run.drivers)if(driver!=null)driver.TimeScaleRelayed-=run.relay;
        Application.logMessageReceived-=run.log;
        if(run.cases.Count!=8){run.passed=false;run.errors.Add(J.O("reason","incomplete_observations","actual",run.cases.Count,"expected",8));}
        var report=J.O("passed",run.passed,"unity",Application.unityVersion,"graphics_device",SystemInfo.graphicsDeviceType.ToString(),"duration",Time.realtimeSinceStartupAsDouble-run.started,
            "scope","Campaign levels1→2→3 through real host Update and original Next/Pause/Unpause onClick. FinishOriginalRound(true) is an explicit UI victory fixture; no HP, physics, slow multiplier or natural combat result is injected.",
            "watchdog_real_seconds",60,"fixed_dt",Time.fixedDeltaTime,"expected_normal_tick_hz",1/Time.fixedDeltaTime,"game_mode",GameMode,"final_level",Progress.Level,
            "save_path",Progress.SavePath,"cases",new J(run.cases),"actions",new J(run.actions),"frames",new J(run.frames),"relays",new J(run.relays),
            "errors",new J(run.errors),"engine_errors",new J(run.engineErrors),"runtime_parity_verified",false);
        File.WriteAllText(Path.Combine(run.directory,"time-validation.json"),J.Write(report));
        try{OriginalRenderingDiagnostics.Capture(Camera.main,Path.Combine(run.directory,"final-render"));}
        catch(Exception error){run.passed=false;run.errors.Add(J.O("reason","final_capture_failed","error",error.ToString()));report["passed"]=new J(false);File.WriteAllText(Path.Combine(run.directory,"time-validation.json"),J.Write(report));}
        ExternalInputReplay=run.priorExternal;AudioListener.volume=run.priorVolume;
        Debug.Log("ORIGINAL_TIME_VALIDATION_FINISHED passed="+run.passed+" "+run.directory);
        yield return new WaitForSecondsRealtime(.1f);Application.Quit(run.passed?0:2);
    }
}
