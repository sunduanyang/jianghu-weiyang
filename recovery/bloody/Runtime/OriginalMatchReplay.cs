using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Jianghu;
using TMPro;
using UnityEngine;

// Opt-in integration observation, never an original APK equivalence test.
// This supplies pointer/gamepad-adapter input to the ordinary local host. It
// does not set health, part modes, body poses, joint settings or damage values.
[DefaultExecutionOrder(9000)]
public sealed class OriginalMatchReplay : MonoBehaviour {
    const int LeftPointer=-101,RightPointer=-102;
    OriginalPreview host;
    OriginalInputAdapter input;
    string directory;
    double began,deadline,nextSample,nextProgress,verdictAt=-1;
    float fightTime,nextKick;
    bool initialized,finished,pointersDown,popupObserved,verdictAudioObserved,respawnRequested,respawnVerified,allFinite=true;
    bool priorExternal,campaignNext;
    int initialRound,observedState=-1,observedRound=-1,collisionCount,impactCount;
    int firstPlayerId,firstEnemyId;
    float maximumSeconds=120;
    float gestureRadiansPerSecond=9;
    Vector2 leftCenter,rightCenter;
    J source,initialPlayer,initialEnemy,initialContext,respawn;
    readonly List<J> samples=new List<J>(),inputs=new List<J>(),contacts=new List<J>(),impacts=new List<J>(),states=new List<J>(),deaths=new List<J>(),popups=new List<J>(),audio=new List<J>(),errors=new List<J>();
    readonly Dictionary<string,Transform> ui=new Dictionary<string,Transform>();
    readonly Dictionary<AudioSource,AudioState> audioState=new Dictionary<AudioSource,AudioState>();
    readonly HashSet<OriginalRig> observedRigs=new HashSet<OriginalRig>();
    readonly HashSet<string> popupClips=new HashSet<string>();
    sealed class AudioState {public bool playing;public AudioClip clip;public int? sample;}

    public static bool TryAttach(OriginalPreview preview) {
        var arguments=Environment.GetCommandLineArgs();int index=Array.IndexOf(arguments,"-validateOriginalMatch");
        if(index<0)return false;
        if(preview==null)throw new ArgumentNullException(nameof(preview));
        if(preview.GetComponent<OriginalMatchReplay>()!=null)return true;
        var probe=preview.gameObject.AddComponent<OriginalMatchReplay>();probe.host=preview;
        probe.directory=Path.GetFullPath(index+1<arguments.Length&&!arguments[index+1].StartsWith("-",StringComparison.Ordinal)?arguments[index+1]:Path.Combine(Application.persistentDataPath,"OriginalMatchReplay"));
        int durationAt=Array.IndexOf(arguments,"-originalMatchReplaySeconds");
        // The source Friendly round may last its full 300 seconds. Allow
        // an explicit longer observation without changing the game's timer.
        if(durationAt>=0&&durationAt+1<arguments.Length&&float.TryParse(arguments[durationAt+1],NumberStyles.Float,CultureInfo.InvariantCulture,out var duration)&&float.IsFinite(duration))probe.maximumSeconds=Mathf.Clamp(duration,10,360);
        int speedAt=Array.IndexOf(arguments,"-originalMatchReplayAngularSpeed");
        if(speedAt>=0&&speedAt+1<arguments.Length&&float.TryParse(arguments[speedAt+1],NumberStyles.Float,CultureInfo.InvariantCulture,out var speed)&&float.IsFinite(speed))probe.gestureRadiansPerSecond=Mathf.Clamp(speed,1,40);
        probe.campaignNext=Array.IndexOf(arguments,"-originalMatchReplayCampaignNext")>=0;
        probe.Begin(Array.IndexOf(arguments,"-recoveryCapture")>=0);return true;
    }
    void Begin(bool conflictingCapture) {
        Directory.CreateDirectory(directory);began=Time.realtimeSinceStartupAsDouble;deadline=began+maximumSeconds;
        priorExternal=host.ExternalInputReplay;host.ExternalInputReplay=true;AudioListener.volume=0;
        source=J.Parse(Resources.Load<TextAsset>("OriginalUISource").text);
        if(host.hud!=null) {
            void Walk(Transform node,string path){ui[path]=node;foreach(Transform child in node)Walk(child,path+"/"+child.name);}
            Walk(host.hud.transform,"CanvasAndCamera");
            foreach(var a in host.hud.GetComponentsInChildren<AudioSource>(true))audioState[a]=new AudioState();
        }
        input=new OriginalInputAdapter();input.Bind(host.hud==null?null:host.hud.transform);
        initialRound=host.CurrentRound;firstPlayerId=host.player==null?0:host.player.GetInstanceID();firstEnemyId=host.enemy==null?0:host.enemy.GetInstanceID();
        initialPlayer=Fighter(host.player);initialEnemy=Fighter(host.enemy);initialContext=ContextSnapshot(true);
        ObserveRig(host.player);ObserveRig(host.enemy);Application.logMessageReceived+=ObserveLog;initialized=true;
        if(conflictingCapture){errors.Add(J.O("kind","configuration","message","Do not combine -recoveryCapture with -validateOriginalMatch; the former exits before a match can finish."));Finish("conflicting_capture_option");return;}
        if(host.player==null||host.enemy==null||host.hud==null){errors.Add(J.O("kind","configuration","message","A normal spawned local duel with its original HUD is required."));Finish("missing_original_match_or_hud");return;}
        WriteProgress();Debug.Log("ORIGINAL_MATCH_REPLAY_READY "+directory);
    }
    double Elapsed=>Time.realtimeSinceStartupAsDouble-began;
    static J Number(float n)=>float.IsFinite(n)?new J(n):new J(n.ToString(CultureInfo.InvariantCulture));
    static J V(Vector2 v)=>J.O("x",Number(v.x),"y",Number(v.y));
    static string PathOf(Transform node) {
        if(node==null)return "";string path=node.name;while(node.parent!=null){node=node.parent;path=node.name+"/"+path;}return path;
    }
    static J Fighter(OriginalRig rig) {
        if(rig==null)return J.O("missing",true);
        return J.O("instance",rig.GetInstanceID(),"name",rig.OriginalCharacterName,"health",Number(rig.health),"health_max",Number(rig.healthMax),"stamina",Number(rig.stamina),"stamina_max",Number(rig.staminaMax),"dead",rig.dead,"passed_out",rig.passedOut,
            "root_position",rig.rootBody==null?new J():V(rig.rootBody.position),"left_item",rig.leftItem==null?0:rig.leftItem.itemId,"right_item",rig.rightItem==null?0:rig.rightItem.itemId,
            "left_arm_disabled",rig.leftArmDisabled,"right_arm_disabled",rig.rightArmDisabled,"ai",rig.OriginalAIState.ToString(),"ai_left_clip",rig.OriginalAILeftClip,"ai_right_clip",rig.OriginalAIRightClip);
    }
    J ContextSnapshot(bool readSave=false) {
        var progress=host.Progress;var outcome=host.CampaignOutcome;
        var result=J.O("mode",host.GameMode,"region",progress.Region,"level",progress.Level,"stage",progress.Stage,"seed",progress.Seed,"level_type",progress.LevelType,
            "gold",progress.Gold,"tutorial_passed",progress.TutorialPassed,"completed_levels",progress.GetCompletedLevelsForRegion(progress.Region),"current_level_stars",progress.GetStarsForLevel(progress.Region,progress.Level-1),
            "campaign_outcome",outcome==null?new J():J.O("stars_earned",outcome.StarsEarned,"previous_stars",outcome.PreviousStars,"new_stars",outcome.NewStars,"coins",outcome.Coins,"gold_awarded",outcome.GoldAwarded,"region",outcome.Region,"level",outcome.Level,"ending_health",Number(outcome.EndingHealth)));
        if(readSave) {
            result["save_path"]=new J(progress.SavePath);
            if(progress.SavePath!=null&&File.Exists(progress.SavePath)) {
                // Read only the explicitly isolated port file; never touch the
                // Android installation or another game's persistence domain.
                var saved=J.Parse(File.ReadAllText(progress.SavePath));var current=progress.Snapshot();
                result["saved_wallet"]=saved["Wallet"];result["saved_campaign"]=saved["Progress"]["Campaign"];result["saved_prefs"]=saved["PlayerPrefs"];
                result["persisted_matches_memory"]=new J(J.Write(saved["Wallet"])==J.Write(current["Wallet"])&&J.Write(saved["Progress"]["Campaign"])==J.Write(current["Progress"]["Campaign"]));
            }
        }
        return result;
    }
    void ObserveRig(OriginalRig rig) {
        if(rig==null||!observedRigs.Add(rig))return;
        rig.ImpactOccurred+=OnImpact;
        rig.OriginalDeathStageCompleted+=stage=>deaths.Add(J.O("after",Elapsed,"frame",Time.frameCount,"fighter",rig.human?"player":"enemy","stage",stage,"state",host.OriginalState));
        foreach(var body in rig.GetComponentsInChildren<Rigidbody2D>(true)) {
            var observer=body.gameObject.AddComponent<OriginalMatchContactObserver>();observer.owner=rig;observer.probe=this;
        }
    }
    void OnImpact(OriginalImpact impact) {
        impactCount++;
        if(impacts.Count<2048)impacts.Add(J.O("after",Elapsed,"frame",Time.frameCount,"scaled_time",Time.timeAsDouble,"attacker",impact.attacker==host.player?"player":impact.attacker==host.enemy?"enemy":"other","victim",impact.victim==host.player?"player":impact.victim==host.enemy?"enemy":"other",
            "item",impact.item==null?0:impact.item.itemId,"part",impact.part==null?0:impact.part.sourceId,"point",V(impact.point),"velocity",V(impact.velocity),"local_damage",Number(impact.localDamage),"global_damage",Number(impact.globalDamage),"cannon",impact.cannon,"surface",impact.surface.ToString()));
    }
    internal void Contact(OriginalRig owner,Collision2D collision) {
        if(finished||collision==null)return;
        var other=collision.collider.GetComponentInParent<OriginalRig>();
        if(other==null){var item=collision.collider.GetComponentInParent<OriginalItem>();if(item!=null)other=item.owner;}
        if(other==null||other==owner||!((owner==host.player&&other==host.enemy)||(owner==host.enemy&&other==host.player)))return;
        // Both original bodies may report a contact. This is a callback count,
        // not a deduplicated number of strikes or successful damage events.
        collisionCount++;
        if(contacts.Count<128)contacts.Add(J.O("after",Elapsed,"frame",Time.frameCount,"owner",owner==host.player?"player":"enemy","collider",PathOf(collision.collider.transform),"other_collider",PathOf(collision.otherCollider.transform),"relative_velocity",V(collision.relativeVelocity),"points",collision.contactCount));
    }
    void ObserveLog(string message,string stack,LogType type) {
        if(finished||!(type==LogType.Error||type==LogType.Assert||type==LogType.Exception))return;
        if(errors.Count<64)errors.Add(J.O("kind",type.ToString(),"after",Elapsed,"message",message,"stack",stack));
    }
    void Update() {
        if(!initialized||finished)return;
        if(Time.realtimeSinceStartupAsDouble>=deadline){ReleasePointers();Finish(respawnVerified?"completed_round_and_respawn":"duration_limit_before_complete_round_and_respawn");return;}
        if(host.player==null||host.enemy==null) {
            ReleasePointers();
            // Source arena transitions tear the old runner down before the
            // new actors exist. The ordinary overall real-time watchdog also
            // bounds this pending transition; absence is not a successful spawn.
            if(respawnRequested&&!respawnVerified) {
                if(Elapsed>=nextSample){nextSample=Elapsed+.25;CheckFinite();if(!allFinite){Finish("non_finite_physics");return;}}
                if(Elapsed>=nextProgress){nextProgress=Elapsed+1;WriteProgress();}
                return;
            }
            errors.Add(J.O("kind","runtime","after",Elapsed,"message","Normal local host lost a fighter outside round respawn."));Finish("missing_fighter");return;
        }
        if(observedRound!=host.CurrentRound||observedState!=host.OriginalState) {
            ReleasePointers();observedRound=host.CurrentRound;observedState=host.OriginalState;
            states.Add(J.O("after",Elapsed,"scaled_time",Time.timeAsDouble,"frame",Time.frameCount,"round",host.CurrentRound,"state",host.OriginalState,"wins_player",host.WinsPlayer,"wins_enemy",host.WinsEnemy,"context",ContextSnapshot(host.OriginalState==6)));
            ObserveRig(host.player);ObserveRig(host.enemy);
            if(host.OriginalState==4){fightTime=Time.time;nextKick=Time.time+3;}
            if((host.OriginalState==5||host.OriginalState==6)&&verdictAt<0)verdictAt=Time.realtimeSinceStartupAsDouble;
        }
        if(host.OriginalState==4&&!respawnRequested)DrivePointers();
        if(Elapsed>=nextSample) {
            nextSample=Elapsed+.25;CheckFinite();samples.Add(J.O("after",Elapsed,"frame",Time.frameCount,"round",host.CurrentRound,"state",host.OriginalState,"remaining",Number(host.RoundTimeRemaining),"countdown",Number(host.CountdownRemaining),"time_scale",Number(Time.timeScale),"player",Fighter(host.player),"enemy",Fighter(host.enemy)));
            if(!allFinite){ReleasePointers();Finish("non_finite_physics");return;}
        }
        if(Elapsed>=nextProgress){nextProgress=Elapsed+1;WriteProgress();}
    }
    void DrivePointers() {
        input.UpdateViewport();
        if(!pointersDown) {
            leftCenter=new Vector2(Screen.width*.25f,Screen.height*.3f);rightCenter=new Vector2(Screen.width*.75f,Screen.height*.3f);
            bool left=input.PointerDown(LeftPointer,leftCenter,true),right=input.PointerDown(RightPointer,rightCenter,false);
            if(!left||!right){errors.Add(J.O("kind","input","after",Elapsed,"message","Original input adapter rejected replay pointer capture."));Finish("pointer_capture_rejected");return;}
            pointersDown=true;host.player.OnOriginalScreenTap(true);host.player.OnOriginalScreenTap(false);
        }
        // A declared engineering gesture, not a recovered player/AI policy.
        // Radius stays within the source 150-canvas-unit joystick radius.
        float t=Time.time-fightTime,angle=t*gestureRadiansPerSecond;
        Vector2 l=leftCenter+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*140*input.CanvasScale;
        Vector2 r=rightCenter+new Vector2(Mathf.Cos(-angle+1.4f),Mathf.Sin(-angle+1.4f))*140*input.CanvasScale;
        input.PointerDrag(LeftPointer,l);input.PointerDrag(RightPointer,r);input.ApplyToRig(host.player);
        float distance=host.enemy.rootBody.position.x-host.player.rootBody.position.x;
        float move=Mathf.Abs(distance)>.7f?Mathf.Sign(distance):0;host.player.movement=move;if(move!=0)host.player.KeepOriginalMove(move);
        bool kick=false;if(Time.time>=nextKick){nextKick=Time.time+5;if(Mathf.Abs(distance)<1.2f)kick=host.player.StartOriginalKick();}
        input.ReadOriginalFields(out var leftNub,out var rightNub,out var leftDown,out var rightDown);
        inputs.Add(J.O("after",Elapsed,"scaled_time",Time.timeAsDouble,"frame",Time.frameCount,"pointer_left",V(l),"pointer_right",V(r),"native_left_nub",V(leftNub),"native_right_nub",V(rightNub),"native_left_down",leftDown,"native_right_down",rightDown,"move",move,"kick_started",kick));
    }
    void ReleasePointers() {
        if(input!=null)input.Reset();pointersDown=false;
        if(host!=null&&host.player!=null){host.player.movement=0;host.player.SetOriginalHandInputs(Vector2.zero,Vector2.zero,false,false);}
    }
    void CheckFinite() {
        foreach(var rig in new[]{host.player,host.enemy})if(rig!=null&&(!float.IsFinite(rig.health)||!float.IsFinite(rig.stamina))) {
            allFinite=false;errors.Add(J.O("kind","vitality","after",Elapsed,"fighter",rig.human?"player":"enemy","message","Non-finite health or stamina."));return;
        }
        // Include dropped equipment and detached parts outside fighter roots.
        foreach(var body in FindObjectsByType<Rigidbody2D>(FindObjectsInactive.Include,FindObjectsSortMode.None)) {
            if(!float.IsFinite(body.position.x)||!float.IsFinite(body.position.y)||!float.IsFinite(body.rotation)||!float.IsFinite(body.linearVelocity.x)||!float.IsFinite(body.linearVelocity.y)||!float.IsFinite(body.angularVelocity)) {
                allFinite=false;errors.Add(J.O("kind","physics","after",Elapsed,"body",PathOf(body.transform),"message","Non-finite body pose or velocity."));return;
            }
        }
    }
    void LateUpdate() {
        if(!initialized||finished)return;ObserveAudio();
        if(verdictAt>=0&&!respawnRequested) {
            // Record actual original driver activation/clip rather than
            // inferring it from the requested match state or a fixed wait.
            foreach(var popup in host.hud.GetComponentsInChildren<OriginalUIPopupDriver>(true))if(popup.gameObject.activeInHierarchy&&popup.CurrentClip!=null&&popupClips.Add(PathOf(popup.transform)+"|"+popup.CurrentClip)) {
                popupObserved=true;popups.Add(UISnapshot("popup_driver_became_active"));
            }
            double since=Time.realtimeSinceStartupAsDouble-verdictAt;
            if(since>=3.25) {popups.Add(UISnapshot("after_verdict_delay"));respawnRequested=true;StartCoroutine(CaptureVerdictAndRespawn());}
        } else if(respawnRequested&&respawnVerified&&host.OriginalState==4)Finish("completed_round_and_respawn");
    }
    void ObserveAudio() {
        foreach(var pair in audioState) {
            var a=pair.Key;var last=pair.Value;if(a==null)continue;
            bool playing=a.isPlaying;var resource=a.resource;var clip=resource as AudioClip;
            // OneShot playback can be active with no AudioClip bound to the
            // source. Unity's sample cursor is valid only for an actual clip
            // resource; probing another/null resource emits a warning per frame.
            int? sample=playing&&clip!=null?a.timeSamples:(int?)null;
            bool cursorRestart=sample.HasValue&&last.sample.HasValue&&sample.Value<last.sample.Value;
            if(playing&&(!last.playing||last.clip!=clip||cursorRestart)) {
                if(!respawnRequested&&(host.OriginalState==5||host.OriginalState==6))verdictAudioObserved=true;
                audio.Add(J.O("after",Elapsed,"frame",Time.frameCount,"path",PathOf(a.transform),"is_playing",playing,"bound_resource_kind",resource==null?"none":resource.GetType().Name,"clip",clip==null?null:clip.name,"load_state",clip==null?null:clip.loadState.ToString(),"sample_position_available",sample.HasValue,"time_samples",sample,"state",host.OriginalState));
            }
            last.playing=playing;last.clip=clip;last.sample=sample;
        }
    }
    J UISnapshot(string reason) {
        bool match=host.OriginalState==6;var record=source["records"].A.First(r=>r["script"].S==(match?"MatchEndView":"RoundEndView"))["data"];
        string containerPath=record["popupsContainer"]["path"].S;ui.TryGetValue(containerPath,out var container);
        var drivers=host.hud.GetComponentsInChildren<OriginalUIPopupDriver>(true).Select(p=>J.O("path",PathOf(p.transform),"active_self",p.gameObject.activeSelf,"active_in_hierarchy",p.gameObject.activeInHierarchy,"clip",p.CurrentClip??"")).ToList();
        var text=new List<J>();if(!match)foreach(var reference in record["playerXWonTheRoundTexts"].A)if(ui.TryGetValue(reference["path"].S,out var node)){var target=node.GetComponent<TMP_Text>();text.Add(J.O("path",reference["path"].S,"text",target==null?"":target.text,"active_in_hierarchy",node.gameObject.activeInHierarchy));}
        return J.O("reason",reason,"after",Elapsed,"since_verdict",verdictAt<0?0:Time.realtimeSinceStartupAsDouble-verdictAt,"frame",Time.frameCount,"state",host.OriginalState,"container_path",containerPath,"container_active_self",container!=null&&container.gameObject.activeSelf,"container_active_in_hierarchy",container!=null&&container.gameObject.activeInHierarchy,"drivers",new J(drivers),"winner_texts",new J(text),"context",ContextSnapshot(true));
    }
    IEnumerator CaptureVerdictAndRespawn() {
        yield return new WaitForEndOfFrame();if(finished)yield break;
        string capture=Path.Combine(directory,"verdict");Directory.CreateDirectory(capture);OriginalRenderingDiagnostics.Capture(Camera.main,capture);
        var before=ContextSnapshot(true);int previousLevel=host.Progress.Level,previousStage=host.Progress.Stage,previousRegion=host.Progress.Region,previousGold=host.Progress.Gold;
        if(host.GameMode==0&&host.OriginalState==6) {
            if(!before["persisted_matches_memory"].B)errors.Add(J.O("kind","campaign_save","message","Campaign verdict progress/wallet differs from its isolated saved file."));
            if(host.LastWinnerIsPlayer&&host.CampaignOutcome==null)errors.Add(J.O("kind","campaign_result","message","Actual Campaign victory has no recovered settlement result."));
            if(!host.LastWinnerIsPlayer&&(previousGold!=initialContext["gold"].I||before["completed_levels"].I!=initialContext["completed_levels"].I||before["current_level_stars"].I!=initialContext["current_level_stars"].I))
                errors.Add(J.O("kind","campaign_loss","message","Campaign loss mutated wallet or current-level progress."));
        }
        string method;bool useNext=host.GameMode==0&&host.OriginalState==6&&host.LastWinnerIsPlayer&&campaignNext;
        if(host.OriginalState==5){method="AdvanceOriginalRound";host.AdvanceOriginalRound();}
        else if(host.OriginalState==6) {if(useNext){method="NextOriginalMatch";host.NextOriginalMatch();}else{method="RestartOriginalMatch";host.RestartOriginalMatch();}}
        else {errors.Add(J.O("kind","round","message","Verdict state changed before normal advance."));Finish("verdict_changed_before_advance");yield break;}
        bool ActorsReady()=>host.player!=null&&host.enemy!=null&&host.player.GetInstanceID()!=firstPlayerId&&host.enemy.GetInstanceID()!=firstEnemyId&&(host.OriginalState==2||host.OriginalState==3||host.OriginalState==4);
        while(!ActorsReady()) {
            if(finished||Time.realtimeSinceStartupAsDouble>=deadline)yield break;
            yield return null;
        }
        bool different=host.player!=null&&host.enemy!=null&&host.player.GetInstanceID()!=firstPlayerId&&host.enemy.GetInstanceID()!=firstEnemyId;
        bool healthy=host.player!=null&&host.enemy!=null&&!host.player.dead&&!host.enemy.dead&&host.player.health==host.player.healthMax&&host.enemy.health==host.enemy.healthMax;
        bool nativePhase=host.OriginalState==2||host.OriginalState==3||host.OriginalState==4;
        bool navigation=true;
        if(host.GameMode==0) {
            int expectedLevel=useNext&&previousLevel<1000?previousLevel+1:previousLevel;
            int expectedStage=useNext&&previousLevel<1000?OriginalLocalProgress.GetStageForLevel(expectedLevel):previousStage;
            navigation=host.Progress.Region==previousRegion&&host.Progress.Level==expectedLevel&&host.Progress.Stage==expectedStage&&host.Progress.Gold==previousGold;
        }
        respawnVerified=different&&healthy&&nativePhase&&navigation;
        respawn=J.O("after",Elapsed,"normal_host_method",method,"new_instances",different,"fresh_vitality",healthy,"native_start_phase",nativePhase,"navigation_verified",navigation,"context_before",before,"context_after",ContextSnapshot(true),"round",host.CurrentRound,"state",host.OriginalState,"player",Fighter(host.player),"enemy",Fighter(host.enemy),"verified",respawnVerified);
        if(!respawnVerified){errors.Add(J.O("kind","respawn","message","Normal host advance did not create healthy new fighter instances in a valid start phase."));Finish("respawn_check_failed");}
        WriteProgress();
    }
    void WriteProgress() {
        File.WriteAllText(Path.Combine(directory,"original-match-replay-progress.json"),J.O("complete",finished,"elapsed",Elapsed,"maximum_seconds",maximumSeconds,"round",host.CurrentRound,"state",host.OriginalState,"collision_callbacks",collisionCount,"damage_events",impactCount,"popup_observed",popupObserved,"verdict_audio_observed",verdictAudioObserved,"respawn_verified",respawnVerified,"finite",allFinite,"player",Fighter(host.player),"enemy",Fighter(host.enemy),"context",ContextSnapshot(),"errors",errors.Count).ToString());
    }
    void Finish(string outcome) {
        if(finished)return;finished=true;ReleasePointers();Application.logMessageReceived-=ObserveLog;
        bool success=outcome=="completed_round_and_respawn"&&collisionCount>0&&impactCount>0&&popupObserved&&verdictAudioObserved&&respawnVerified&&allFinite&&errors.Count==0;
        var report=J.O("scope","Normal local slot0 combat in source mode "+initialContext["mode"].I+" driven through recovered input; no APK trajectory comparison","source_apk_sha256","c9e4a52874f18c58363d976194227d68a699173d72925d22da34a75d0993f2ba","unity",Application.unityVersion,"complete",true,"passed",success,"outcome",outcome,"elapsed",Elapsed,"maximum_seconds",maximumSeconds,"human_input_slot",0,"player_configuration",initialContext["mode"].I==5?"Player_1":"Slot_0","initial_context",initialContext,"final_context",ContextSnapshot(true),"campaign_next_requested",campaignNext,
            "gesture_radians_per_scaled_second",gestureRadiansPerSecond,"initial_player",initialPlayer,"initial_enemy",initialEnemy,"collision_callback_count",collisionCount,"damage_event_count",impactCount,"finite",allFinite,"popup_observed",popupObserved,"verdict_audio_observed",verdictAudioObserved,"respawn_verified",respawnVerified,"respawn",respawn??new J(),"input_trace",new J(inputs),"samples",new J(samples),"states",new J(states),"contact_callbacks",new J(contacts),"impacts",new J(impacts),"death_stages",new J(deaths),"popup_snapshots",new J(popups),"audio_starts",new J(audio),"errors",new J(errors),"health_damage_mode_injected",false,"body_pose_or_joint_injected",false,"runtime_parity_verified",false,
            "limits",new J(new List<J>{new J("Scripted circular pointer gestures and PC-adapter movement/kicks are engineering inputs, not a recovered AI policy or an APK input trace."),new J("The host supplies actual local slot0 configuration: Slot_0 for Campaign/Practice or Player_1 for the explicit Friendly fixture, plus the mode-specific opponent; no fixture health/armor reductions."),new J("Pointer replay enters the original input adapter directly; OS input capture and UI button hit testing are outside this probe."),new J("Collision callback count may include both sides of one contact and is not a strike count."),new J("Only the first 128 contact callbacks and first 2048 impact events are retained; total counters remain separate."),new J("AudioSource activity is observed per rendered frame. OneShot playback may have no bound AudioClip, so its sample cursor is unavailable; overlapping or inter-frame OneShots may not be individually distinguishable."),new J("Popup and respawn evidence is only available if real combat ends within the maximum wall-clock duration."),new J("Campaign results and the isolated port save are observed. Navigation uses the normal host methods; this probe does not click their UI buttons or establish original Android save compatibility."),new J("No original APK replay, cross-platform trajectory parity or network multiplayer validation is performed.")}));
        File.WriteAllText(Path.Combine(directory,"original-match-replay.json"),report.ToString());WriteProgress();host.ExternalInputReplay=priorExternal;
        Debug.Log("ORIGINAL_MATCH_REPLAY_FINISHED "+outcome+" passed="+success);Application.Quit(success?0:errors.Count>0?1:2);
    }
    void OnDestroy(){Application.logMessageReceived-=ObserveLog;if(host!=null)host.ExternalInputReplay=priorExternal;}
}

public sealed class OriginalMatchContactObserver : MonoBehaviour {
    internal OriginalRig owner;
    internal OriginalMatchReplay probe;
    void OnCollisionEnter2D(Collision2D collision){if(probe!=null)probe.Contact(owner,collision);}
}
