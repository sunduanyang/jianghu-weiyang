using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jianghu;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Opt-in regression through the same host gate, hit test and desktop capture
// used by hardware input. This does not replace Windows mouse acceptance.
public sealed partial class OriginalPreview {
    string originalMouseTrace;
    bool mouseTraceConfigured;
    static J MouseV(Vector2 v)=>J.O("x",float.IsFinite(v.x)?(object)v.x:null,"y",float.IsFinite(v.y)?(object)v.y:null);
    static J MouseWorldV(Vector3 v)=>J.O("x",float.IsFinite(v.x)?(object)v.x:null,"y",float.IsFinite(v.y)?(object)v.y:null,"z",float.IsFinite(v.z)?(object)v.z:null);
    static string MousePath(Transform t) {
        if(t==null)return "";string p=t.name;while(t.parent!=null){t=t.parent;p=t.name+"/"+p;}return p;
    }
    J MouseHits(Vector2 position) {
        var hits=new List<RaycastResult>();
        if(EventSystem.current!=null)EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=position,pointerId=-1},hits);
        return new J(hits.Where(h=>h.gameObject!=null).Select(h=>J.O("path",MousePath(h.gameObject.transform),"selectable",h.gameObject.GetComponentInParent<Selectable>()!=null,"module",h.module==null?"":h.module.GetType().Name)).ToList());
    }
    Transform MouseHandPoint(bool left) => player.ResolveTransform(player.source["controllers"]["CharacterMover"][0]["data"][left?"handLeftPoint":"handRightPoint"]);
    Rigidbody2D MousePhysicalHand(bool left)=>player.Resolve<Rigidbody2D>(player.source["controllers"]["CharacterMover"][0]["data"][left?"handLeftRigidbody2D":"handRightRigidbody2D"]);
    float MouseHeldTargetRadiusError(bool screenLeft,Vector2 nub,bool retained) {
        var point=MouseHandPoint(screenLeft);
        if(!retained)return ((Vector2)point.localPosition).magnitude;
        var mover=player.source["controllers"]["CharacterMover"][0]["data"];
        var wanted=new Vector2((nub.x/100+.15f)*(mover["IsFlipped"].B?-1:1),nub.y/100+.25f);
        // The parent bones interpolate after physics. That rotates the saved
        // local target at render time; compare its radius to distinguish a
        // genuine neutral reset from this normal physics interpolation.
        return Mathf.Abs(((Vector2)point.localPosition).magnitude-wanted.magnitude);
    }
    static J MouseBodyState(Rigidbody2D body)=>body==null?J.O("missing",true):J.O("path",MousePath(body.transform),"body_type",body.bodyType.ToString(),"simulated",body.simulated,
        "constraints",body.constraints.ToString(),"position",MouseV(body.position),"transform_world",MouseWorldV(body.transform.position),"velocity",MouseV(body.linearVelocity),
        "rotation",float.IsFinite(body.rotation)?(object)body.rotation:null,"angular_velocity",float.IsFinite(body.angularVelocity)?(object)body.angularVelocity:null,
        "awake",body.IsAwake(),"active_hierarchy",body.gameObject.activeInHierarchy);
    J MouseSnapshot() {
        if(player==null)return J.O("player_missing",true,"captures",pointers.Count);
        originalInput.ReadOriginalFields(out var l,out var r,out var ld,out var rd);
        var handL=MousePhysicalHand(true);var handR=MousePhysicalHand(false);
        return J.O("captures",pointers.Count,"screen_left_down",originalInput.IsDown(true),"screen_right_down",originalInput.IsDown(false),
            "screen_left_center",MouseV(originalInput.GetCenterScreen(true)),"screen_right_center",MouseV(originalInput.GetCenterScreen(false)),
            "native_left_nub",MouseV(l),"native_right_nub",MouseV(r),"native_left_down",ld,"native_right_down",rd,
            "rig_left_nub",MouseV(player.leftInput),"rig_right_nub",MouseV(player.rightInput),"rig_left_active",player.leftActive,"rig_right_active",player.rightActive,
            "left_target_local",MouseV(MouseHandPoint(true).localPosition),"right_target_local",MouseV(MouseHandPoint(false).localPosition),
            "left_target_world",MouseWorldV(MouseHandPoint(true).position),"right_target_world",MouseWorldV(MouseHandPoint(false).position),
            "left_hand",MouseV(player.leftHandCOM.position),"right_hand",MouseV(player.rightHandCOM.position),
            "left_driver",MouseBodyState(player.leftHandCOM),"right_driver",MouseBodyState(player.rightHandCOM),
            "left_hand_body",handL==null?J.O("missing",true):MouseV(handL.position),"right_hand_body",handR==null?J.O("missing",true):MouseV(handR.position),
            "left_hand_body_physics",MouseBodyState(handL),"right_hand_body_physics",MouseBodyState(handR),
            "motion_tick",player.OriginalMotionTick,"time_scale",Time.timeScale,"fixed_dt",Time.fixedDeltaTime,"dead",player.dead,"passed_out",player.passedOut,
            "left_arm_disabled",player.leftArmDisabled,"right_arm_disabled",player.rightArmDisabled);
    }
    void TraceOriginalMouseFrame(Vector2 position,int down,int held,int up) {
        if(!mouseTraceConfigured) {
            mouseTraceConfigured=true;var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-originalMouseTrace");
            if(at>=0&&at+1<args.Length){originalMouseTrace=Path.GetFullPath(args[at+1]);Directory.CreateDirectory(Path.GetDirectoryName(originalMouseTrace));}
        }
        if(originalMouseTrace==null||(down|held|up)==0)return;
        var sample=J.O("frame",Time.frameCount,"time",Time.realtimeSinceStartupAsDouble,"position",MouseV(position),"down",down,"held",held,"up",up,"state",OriginalState,"paused",Paused,
            "transitioning",IsTransitioning,"external_input_replay",ExternalInputReplay,"automated",automated,"application_focused",Application.isFocused,
            "transition_blocks_pointer_input",hud!=null&&hud.OriginalTransitionBlocksPointerInput,"snapshot",MouseSnapshot());
        if(down!=0)sample["hits"]=MouseHits(position);
        File.AppendAllText(originalMouseTrace,J.Write(sample)+Environment.NewLine);
    }
    void TryStartOriginalMouseValidation() {
        var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-validateOriginalMouse");
        if(at<0)return;
        string directory=Path.GetFullPath(at+1<args.Length?args[at+1]:Path.Combine(Application.persistentDataPath,"OriginalMouseValidation"));
        Directory.CreateDirectory(directory);ExternalInputReplay=true;AudioListener.volume=0;
        StartCoroutine(ValidateOriginalMouse(directory));
    }
    static bool MouseFinite(Vector2 value)=>float.IsFinite(value.x)&&float.IsFinite(value.y);
    static bool MouseFinite(Vector3 value)=>float.IsFinite(value.x)&&float.IsFinite(value.y)&&float.IsFinite(value.z);
    static bool MouseZero(Vector2 value)=>MouseFinite(value)&&value.sqrMagnitude<.00000001f;
    static bool MouseEqual(Vector2 a,Vector2 b)=>MouseFinite(a)&&MouseFinite(b)&&(a-b).sqrMagnitude<.00000001f;
    bool MouseRigFinite(OriginalRig rig) {
        if(rig==null||rig!=player||rig.chest==null||rig.leftHandCOM==null||rig.rightHandCOM==null)return false;
        if(MousePhysicalHand(true)==null||MousePhysicalHand(false)==null)return false;
        if(!float.IsFinite(rig.health)||!float.IsFinite(rig.stamina)||!MouseFinite(rig.leftInput)||!MouseFinite(rig.rightInput))return false;
        if(!MouseFinite(MouseHandPoint(true).position)||!MouseFinite(MouseHandPoint(false).position)||!MouseFinite(MouseHandPoint(true).localPosition)||!MouseFinite(MouseHandPoint(false).localPosition))return false;
        return rig.GetComponentsInChildren<Rigidbody2D>(true).All(body=>MouseFinite(body.position)&&MouseFinite(body.linearVelocity)&&float.IsFinite(body.rotation)&&float.IsFinite(body.angularVelocity));
    }
    bool MouseCaseReady(OriginalRig rig)=>rig!=null&&player==rig&&OriginalState==4&&!Paused&&!IsTransitioning&&hud!=null&&!hud.OriginalTransitionBlocksPointerInput
        &&!rig.dead&&!rig.passedOut&&!rig.leftArmDisabled&&!rig.rightArmDisabled;
    bool MouseCapturesCleared() {
        if(player==null)return false;
        originalInput.ReadOriginalFields(out var l,out var r,out var ld,out var rd);
        return pointers.Count==0&&!originalInput.IsDown(true)&&!originalInput.IsDown(false)&&!ld&&!rd&&MouseZero(l)&&MouseZero(r);
    }
    bool MouseInputsCleared()=>MouseCapturesCleared()&&!player.leftActive&&!player.rightActive&&MouseZero(player.leftInput)&&MouseZero(player.rightInput);
    bool MouseHeldApplied(Vector2 screenLeftAim,Vector2 screenRightAim,bool screenLeft,bool screenRight)=>player.leftActive==screenLeft&&player.rightActive==screenRight
        &&Vector2.Distance(player.rightInput,screenLeftAim)<.003f&&Vector2.Distance(player.leftInput,screenRightAim)<.003f;
    bool MouseCaptureMapped(int mask,bool screenLeft,bool screenRight) {
        originalInput.ReadOriginalFields(out var l,out var r,out var ld,out var rd);
        int count=((mask&1)!=0?1:0)+((mask&2)!=0?1:0);
        if(pointers.Count!=count||pointers.ContainsKey(-1)!=((mask&1)!=0)||pointers.ContainsKey(-2)!=((mask&2)!=0))return false;
        if(pointers.Values.Count(p=>p.left)!=(screenLeft?1:0)||pointers.Values.Count(p=>!p.left)!=(screenRight?1:0))return false;
        // The physical button owns the hand on both halves of the screen.
        if(((mask&1)!=0&&pointers[-1].left)||((mask&2)!=0&&!pointers[-2].left))return false;
        return originalInput.IsDown(true)==screenLeft&&originalInput.IsDown(false)==screenRight&&ld==screenRight&&rd==screenLeft
            &&(!screenLeft||(player.leftActive&&MouseEqual(player.rightInput,r)))&&(!screenRight||(player.rightActive&&MouseEqual(player.leftInput,l)))
            &&(screenRight||MouseZero(l))&&(screenLeft||MouseZero(r));
    }
    GameObject MouseTopHit(Vector2 position) {
        if(EventSystem.current==null)return null;
        var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=position,pointerId=-1},hits);
        return hits.FirstOrDefault(hit=>hit.gameObject!=null).gameObject;
    }
    IEnumerator MouseRestartFixture() {
        var prior=player;RestartOriginalMatch();double limit=Time.realtimeSinceStartupAsDouble+25;
        while((player==prior||!MouseCaseReady(player))&&Time.realtimeSinceStartupAsDouble<limit)yield return null;
    }
    IEnumerator ValidateOriginalMouse(string directory) {
        var cases=new List<J>();var errors=new List<J>();bool passed=true;
        double deadline=Time.realtimeSinceStartupAsDouble+90;
        while((player==null||OriginalState!=4||IsTransitioning||hud==null||hud.OriginalTransitionBlocksPointerInput)&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
        if(!MouseCaseReady(player)||!MouseRigFinite(player)) {
            passed=false;errors.Add(J.O("reason","host_not_ready","state",OriginalState,"transitioning",IsTransitioning,"hud_missing",hud==null,"pointer_blocked",hud!=null&&hud.OriginalTransitionBlocksPointerInput));
        } else {
            for(int i=0;i<5;i++)yield return null;
            foreach(var spec in new[]{
                ("left_mouse_main_on_left",1,.25f,1,false),
                ("left_mouse_main_on_right",1,.75f,1,false),
                ("right_mouse_off_on_left",2,.25f,1,false),
                ("right_mouse_off_on_right",2,.75f,1,false),
                ("left_mouse_main_crosses_midline",1,.48f,1,false),
                ("right_mouse_off_crosses_midline",2,.52f,1,false),
                ("both_buttons_on_left",3,.25f,2,false),
                ("both_buttons_on_right",3,.75f,2,false),
                ("both_buttons_right_pressed_first",3,.25f,2,true)}) {
                // A sustained virtual press uses the source stamina rules.
                // Start each independent gesture in a real fresh match rather
                // than changing HP/stamina or accumulating unrelated fatigue.
                if(cases.Count>0)yield return MouseRestartFixture();
                var rig=player;
                if(!MouseCaseReady(rig)||!MouseRigFinite(rig)){passed=false;cases.Add(J.O("case",spec.Item1,"passed",false,"reason","active_finite_player_missing_before_case","snapshot",MouseSnapshot()));break;}
                ResetPointers(true);int resetTick=rig.OriginalMotionTick;double resetDeadline=Time.realtimeSinceStartupAsDouble+5;
                while(MouseCaseReady(rig)&&MouseRigFinite(rig)&&rig.OriginalMotionTick-resetTick<2&&Time.realtimeSinceStartupAsDouble<resetDeadline)yield return null;
                bool resetReady=MouseCaseReady(rig)&&MouseRigFinite(rig)&&rig.OriginalMotionTick-resetTick>=2&&MouseInputsCleared()
                    &&MouseZero(MouseHandPoint(true).localPosition)&&MouseZero(MouseHandPoint(false).localPosition);
                if(!resetReady){passed=false;cases.Add(J.O("case",spec.Item1,"passed",false,"reason","reset_did_not_reach_clean_finite_active_physics","snapshot",MouseSnapshot()));break;}
                bool screenLeft=(spec.Item2&2)!=0,screenRight=(spec.Item2&1)!=0;
                var center=new Vector2(Screen.width*spec.Item3,Screen.height*.3f);
                var item=J.O("case",spec.Item1,"hits",MouseHits(center),"over_button",OverButton(-1,center));
                bool gateOpen=BeginOriginalInputFrame();
                if(gateOpen){
                    if(spec.Item5){ReadOriginalDesktopPointers(center,2,2,0);ReadOriginalDesktopPointers(center,1,3,0);}
                    else ReadOriginalDesktopPointers(center,spec.Item2,spec.Item2,0);
                    originalInput.ApplyToRig(player);
                }
                bool captureMapped=gateOpen&&MouseCaptureMapped(spec.Item2,screenLeft,screenRight);
                item["pressed"]=MouseSnapshot();
                Vector2 baseL=originalInput.GetCenterScreen(true),baseR=originalInput.GetCenterScreen(false);
                bool centeredAtPress=(!screenLeft||Vector2.Distance(baseL,center)<.25f)&&(!screenRight||Vector2.Distance(baseR,center)<.25f);
                int tick=player.OriginalMotionTick;double end=Time.realtimeSinceStartupAsDouble+20;
                float maxNubL=0,maxNubR=0,maxTargetL=0,maxTargetR=0,maxHandL=0,maxHandR=0,maxRelativeL=0,maxRelativeR=0;
                float maxBaseDriftL=0,maxBaseDriftR=0;
                var handBodyL=MousePhysicalHand(true);var handBodyR=MousePhysicalHand(false);
                Vector2 firstL=handBodyL.position,firstR=handBodyR.position,firstRelativeL=firstL-rig.chest.position,firstRelativeR=firstR-rig.chest.position;
                Vector2 position=center;bool finite=true,stateReady=true,mapping=captureMapped;
                void ObserveResponse() {
                    if(screenLeft)maxBaseDriftL=Mathf.Max(maxBaseDriftL,Vector2.Distance(baseL,originalInput.GetCenterScreen(true)));
                    if(screenRight)maxBaseDriftR=Mathf.Max(maxBaseDriftR,Vector2.Distance(baseR,originalInput.GetCenterScreen(false)));
                    // Screen-left is native/rightInput and drives handLeftPoint;
                    // screen-right is native/leftInput and drives handRightPoint.
                    maxNubL=Mathf.Max(maxNubL,rig.rightInput.magnitude);maxNubR=Mathf.Max(maxNubR,rig.leftInput.magnitude);
                    maxTargetL=Mathf.Max(maxTargetL,((Vector2)MouseHandPoint(true).localPosition).magnitude);maxTargetR=Mathf.Max(maxTargetR,((Vector2)MouseHandPoint(false).localPosition).magnitude);
                    // Measure the visible Bone_Hand rigidbodies, not only their
                    // child COM drivers; whole-body translation cannot satisfy
                    // the separate displacement relative to the chest.
                    maxHandL=Mathf.Max(maxHandL,Vector2.Distance(firstL,handBodyL.position));maxHandR=Mathf.Max(maxHandR,Vector2.Distance(firstR,handBodyR.position));
                    maxRelativeL=Mathf.Max(maxRelativeL,Vector2.Distance(firstRelativeL,handBodyL.position-rig.chest.position));maxRelativeR=Mathf.Max(maxRelativeR,Vector2.Distance(firstRelativeR,handBodyR.position-rig.chest.position));
                }
                while(rig.OriginalMotionTick-tick<16&&Time.realtimeSinceStartupAsDouble<end) {
                    stateReady&=MouseCaseReady(rig);finite&=MouseRigFinite(rig);if(!stateReady||!finite)break;
                    // Exceed the 100-screen-pixel joystick radius: the former
                    // floating center would drift, then fail return-to-origin.
                    float angle=(rig.OriginalMotionTick-tick)*.22f;position=center+new Vector2(Mathf.Cos(angle)*240,Mathf.Sin(angle)*140);
                    bool open=BeginOriginalInputFrame();gateOpen&=open;
                    if(open){ReadOriginalDesktopPointers(position,0,spec.Item2,0);originalInput.ApplyToRig(player);mapping&=MouseCaptureMapped(spec.Item2,screenLeft,screenRight);}
                    else mapping=false;
                    ObserveResponse();
                    yield return null;
                }
                stateReady&=MouseCaseReady(rig);finite&=MouseRigFinite(rig);if(stateReady&&finite)ObserveResponse();
                int advanced=rig.OriginalMotionTick-tick;
                bool captured=captureMapped&&pointers.Count==spec.Item4;
                bool nubMapped=(screenLeft?maxNubL>10:maxNubL<.0001f)&&(screenRight?maxNubR>10:maxNubR<.0001f);
                bool targetMapped=(screenLeft?maxTargetL>.2f:maxTargetL<.0001f)&&(screenRight?maxTargetR>.2f:maxTargetR<.0001f);
                bool handMoved=(!screenLeft||(maxHandL>.02f&&maxRelativeL>.02f))&&(!screenRight||(maxHandR>.02f&&maxRelativeR>.02f));
                item["dragged"]=MouseSnapshot();item["motion_ticks_advanced"]=new J(advanced);
                item["maximum_nub"]=new J(Mathf.Max(maxNubL,maxNubR));item["maximum_target_offset"]=new J(Mathf.Max(maxTargetL,maxTargetR));
                item["screen_maximum_nub"]=MouseV(new Vector2(maxNubL,maxNubR));item["side_maximum_target_offset"]=MouseV(new Vector2(maxTargetL,maxTargetR));
                item["hand_displacement"]=MouseV(new Vector2(maxHandL,maxHandR));item["hand_displacement_relative_to_chest"]=MouseV(new Vector2(maxRelativeL,maxRelativeR));
                item["hand_displacement_body_paths"]=J.O("left",MousePath(handBodyL.transform),"right",MousePath(handBodyR.transform));
                bool baseFixed=centeredAtPress&&maxBaseDriftL<.25f&&maxBaseDriftR<.25f;
                item["maximum_base_drift_pixels"]=MouseV(new Vector2(maxBaseDriftL,maxBaseDriftR));
                ReadOriginalDesktopPointers(center,0,spec.Item2,0);originalInput.ApplyToRig(player);
                bool returnedToOrigin=MouseCaptureMapped(spec.Item2,screenLeft,screenRight)&&MouseZero(player.leftInput)&&MouseZero(player.rightInput);
                item["returned_to_origin"]=MouseSnapshot();
                ReadOriginalDesktopPointers(position,0,spec.Item2,0);originalInput.ApplyToRig(player);
                Vector2 retainedL=player.rightInput,retainedR=player.leftInput;
                bool partialRelease=true,releaseGate=MouseCaseReady(rig)&&BeginOriginalInputFrame();
                if(releaseGate&&spec.Item2==3) {
                    ReadOriginalDesktopPointers(position,0,2,1);originalInput.ApplyToRig(player);partialRelease=MouseCaptureMapped(2,true,false)&&player.rightInput.magnitude>10;
                    item["left_released_right_held"]=MouseSnapshot();
                }
                if(releaseGate){ReadOriginalDesktopPointers(position,0,0,spec.Item2==3?2:spec.Item2);originalInput.ApplyToRig(player);}
                bool released=releaseGate&&MouseCapturesCleared()&&MouseHeldApplied(retainedL,retainedR,screenLeft,screenRight);
                // A held button without a new Down must not recapture after Up.
                if(releaseGate){ReadOriginalDesktopPointers(position,0,spec.Item2,0);originalInput.ApplyToRig(player);}
                bool noRecapture=releaseGate&&MouseCapturesCleared();int releaseTick=rig.OriginalMotionTick;double releaseEnd=Time.realtimeSinceStartupAsDouble+5;
                bool idleReleased=true;float releaseTargetError=0;
                while(MouseCaseReady(rig)&&MouseRigFinite(rig)&&rig.OriginalMotionTick-releaseTick<60&&Time.realtimeSinceStartupAsDouble<releaseEnd) {
                    // Exercise the normal no-buttons path for a full second.
                    BeginOriginalInputFrame();ReadOriginalDesktopPointers(position+Vector2.one*20,0,0,0);originalInput.ApplyToRig(player);
                    idleReleased&=MouseCapturesCleared()&&MouseHeldApplied(retainedL,retainedR,screenLeft,screenRight);
                    if(rig.OriginalMotionTick-releaseTick>=2)releaseTargetError=Mathf.Max(releaseTargetError,MouseHeldTargetRadiusError(true,retainedL,screenLeft),MouseHeldTargetRadiusError(false,retainedR,screenRight));
                    yield return null;
                }
                bool releaseTargets=MouseCaseReady(rig)&&MouseRigFinite(rig)&&rig.OriginalMotionTick-releaseTick>=60&&idleReleased&&releaseTargetError<.005f;
                item["idle_target_error"]=new J(releaseTargetError);item["idle_motion_ticks"]=new J(rig.OriginalMotionTick-releaseTick);item["idle_pose"]=MouseSnapshot();
                // New clicks at a new screen location must not reset aim.
                var nextCenter=new Vector2(Screen.width*.62f,Screen.height*.38f);
                ReadOriginalDesktopPointers(nextCenter,spec.Item2,spec.Item2,0);originalInput.ApplyToRig(player);
                bool repressed=MouseCaptureMapped(spec.Item2,screenLeft,screenRight)&&Vector2.Distance(player.rightInput,retainedL)<.003f&&Vector2.Distance(player.leftInput,retainedR)<.003f;
                var delta=new Vector2(12,-8);float scale=originalInput.CanvasScale;
                ReadOriginalDesktopPointers(nextCenter+delta,0,spec.Item2,0);originalInput.ApplyToRig(player);
                bool continued=(!screenLeft||Vector2.Distance(player.rightInput,OriginalInputAdapter.QuantizeNub(Vector2.ClampMagnitude(retainedL+delta/scale,150)))<.003f)
                    &&(!screenRight||Vector2.Distance(player.leftInput,OriginalInputAdapter.QuantizeNub(Vector2.ClampMagnitude(retainedR+delta/scale,150)))<.003f);
                item["repressed"]=MouseSnapshot();ReadOriginalDesktopPointers(nextCenter+delta,0,0,spec.Item2);originalInput.ApplyToRig(player);
                bool casePassed=captured&&gateOpen&&mapping&&nubMapped&&targetMapped&&handMoved&&advanced>=16&&finite&&stateReady&&baseFixed&&returnedToOrigin&&partialRelease&&released&&noRecapture&&releaseTargets&&repressed&&continued;
                item["checks"]=J.O("capture_mapping",captured,"gate_open",gateOpen,"input_mapping",mapping,"nub_mapping",nubMapped,"target_mapping",targetMapped,"physical_hand_response",handMoved,
                    "sixteen_ticks",advanced>=16,"finite",finite,"active_round",stateReady,"independent_release",partialRelease,"released",released,"held_without_down_not_recaptured",noRecapture,"released_targets_retained_for_one_second",releaseTargets,
                    "new_press_preserves_aim",repressed,"new_drag_continues_from_saved_aim",continued);
                item["checks"]["base_stays_at_press_position"]=new J(baseFixed);item["checks"]["return_to_origin_zeroes_input"]=new J(returnedToOrigin);
                item["checks"]["released_logical_hand_stays_pressed"]=new J(idleReleased);
                item["released"]=MouseSnapshot();item["passed"]=new J(casePassed);passed&=casePassed;cases.Add(item);
            }
            yield return MouseRestartFixture();ResetPointers(true);
            var button=hud.GetComponentsInChildren<Button>().FirstOrDefault(b=>b.name=="Button_Pause"||b.name=="PauseButton");
            if(button!=null) {
                var rect=button.GetComponent<RectTransform>();var canvas=button.GetComponentInParent<Canvas>();
                Vector2 position=RectTransformUtility.WorldToScreenPoint(canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,rect.TransformPoint(rect.rect.center));
                // The source's decorative metal panels finish after its root
                // blocking Image is disabled. Wait for the actual button to
                // become the foremost hit rather than testing through a panel.
                double buttonDeadline=Time.realtimeSinceStartupAsDouble+5;
                while(MouseCaseReady(player)&&Time.realtimeSinceStartupAsDouble<buttonDeadline) {
                    var hit=MouseTopHit(position);if(hit!=null&&hit.GetComponentInParent<Button>()==button)break;yield return null;
                }
                var top=MouseTopHit(position);bool gateOpen=MouseCaseReady(player)&&MouseRigFinite(player)&&BeginOriginalInputFrame();
                bool topIsButton=button.isActiveAndEnabled&&button.IsInteractable()&&top!=null&&top.GetComponentInParent<Button>()==button;
                if(gateOpen){ReadOriginalDesktopPointers(position,1,1,0);originalInput.ApplyToRig(player);}
                bool blocked=gateOpen&&topIsButton&&MouseInputsCleared();
                cases.Add(J.O("case","pause_button_does_not_capture","gate_open",gateOpen,"top_hit_is_pause_button",topIsButton,"top_hit",top==null?"":MousePath(top.transform),
                    "button_onclick_exercised",false,"hits",MouseHits(position),"passed",blocked));passed&=blocked;ResetPointers(true);
            } else {passed=false;errors.Add(J.O("reason","pause_button_missing"));}
            // Establish a real production capture and nonzero drag before
            // checking SetPaused/BeginOriginalInputFrame release semantics.
            var pauseRig=player;Vector2 pauseCenter=new Vector2(Screen.width*.25f,Screen.height*.3f),pauseDrag=pauseCenter+new Vector2(65,35);
            bool pauseGate=MouseCaseReady(pauseRig)&&MouseRigFinite(pauseRig)&&BeginOriginalInputFrame();
            if(pauseGate){ReadOriginalDesktopPointers(pauseCenter,1,1,0);ReadOriginalDesktopPointers(pauseDrag,0,1,0);originalInput.ApplyToRig(player);}
            int pauseTick=pauseRig==null?0:pauseRig.OriginalMotionTick;double pauseDeadline=Time.realtimeSinceStartupAsDouble+5;
            while(MouseCaseReady(pauseRig)&&MouseRigFinite(pauseRig)&&pauseRig.OriginalMotionTick-pauseTick<3&&Time.realtimeSinceStartupAsDouble<pauseDeadline) {
                bool open=BeginOriginalInputFrame();pauseGate&=open;if(open){ReadOriginalDesktopPointers(pauseDrag,0,1,0);originalInput.ApplyToRig(player);}yield return null;
            }
            bool prepared=pauseGate&&MouseCaseReady(pauseRig)&&MouseRigFinite(pauseRig)&&pauseRig.OriginalMotionTick-pauseTick>=3&&MouseCaptureMapped(1,false,true)
                &&pauseRig.leftInput.magnitude>10&&((Vector2)MouseHandPoint(false).localPosition).magnitude>.2f;
            var beforePause=MouseSnapshot();Vector2 pauseAim=pauseRig.leftInput;SetPaused(true);
            bool clearedByPause=MouseInputsCleared(),pauseBlocked=!BeginOriginalInputFrame()&&MouseInputsCleared();var afterPause=MouseSnapshot();SetPaused(false);
            bool resumed=MouseCaseReady(pauseRig)&&MouseRigFinite(pauseRig)&&BeginOriginalInputFrame();
            if(resumed){ReadOriginalDesktopPointers(pauseDrag,0,1,0);originalInput.ApplyToRig(player);}
            bool resumeClear=resumed&&MouseCapturesCleared()&&MouseHeldApplied(Vector2.zero,pauseAim,false,true);int resumeTick=pauseRig.OriginalMotionTick;
            double resumeEnd=Time.realtimeSinceStartupAsDouble+5;
            while(MouseCaseReady(pauseRig)&&MouseRigFinite(pauseRig)&&pauseRig.OriginalMotionTick-resumeTick<12&&Time.realtimeSinceStartupAsDouble<resumeEnd){originalInput.ApplyToRig(player);yield return null;}
            bool resumePose=MouseCaseReady(pauseRig)&&MouseRigFinite(pauseRig)&&pauseRig.OriginalMotionTick-resumeTick>=12&&MouseHeldTargetRadiusError(false,pauseAim,true)<.005f;
            bool pausePassed=prepared&&clearedByPause&&pauseBlocked&&resumeClear&&resumePose;
            cases.Add(J.O("case","pause_clears_capture","capture_prepared",prepared,"before_pause",beforePause,"cleared_by_set_paused",clearedByPause,"paused_gate_cleared",pauseBlocked,
                "after_pause",afterPause,"resumed_held_not_recaptured",resumeClear,"resumed_pose_retained",resumePose,"passed",pausePassed));passed&=pausePassed;
            // Touch release keeps the source neutral-return behavior.
            ResetPointers(true);var touchCenter=new Vector2(Screen.width*.25f,Screen.height*.3f);
            PointerDown(101,touchCenter,true);originalInput.PointerDrag(101,touchCenter+new Vector2(55,45));originalInput.ApplyToRig(player);
            int touchTick=player.OriginalMotionTick;double touchEnd=Time.realtimeSinceStartupAsDouble+5;
            while(MouseCaseReady(player)&&player.OriginalMotionTick-touchTick<3&&Time.realtimeSinceStartupAsDouble<touchEnd){originalInput.ApplyToRig(player);yield return null;}
            bool touchPrepared=originalInput.IsDown(true)&&originalInput.GetNetworkNub(true).magnitude>10&&((Vector2)MouseHandPoint(true).localPosition).magnitude>.2f;
            PointerUp(101);originalInput.ApplyToRig(player);touchTick=player.OriginalMotionTick;
            while(MouseCaseReady(player)&&player.OriginalMotionTick-touchTick<3&&Time.realtimeSinceStartupAsDouble<touchEnd)yield return null;
            bool touchReturned=touchPrepared&&MouseInputsCleared()&&MouseZero(MouseHandPoint(true).localPosition)&&MouseZero(MouseHandPoint(false).localPosition);
            cases.Add(J.O("case","touch_release_still_returns_to_neutral","passed",touchReturned,"snapshot",MouseSnapshot()));passed&=touchReturned;
            yield return MouseRestartFixture();ResetPointers(true);
            var shieldDelta=new Vector2(65,10);var shieldAim=OriginalInputAdapter.QuantizeNub(shieldDelta/originalInput.CanvasScale);
            ReadOriginalDesktopPointers(touchCenter,2,2,0);ReadOriginalDesktopPointers(touchCenter+shieldDelta,0,2,0);originalInput.ApplyToRig(player);
            int shieldTick=player.OriginalMotionTick;double shieldEnd=Time.realtimeSinceStartupAsDouble+8;
            while(MouseCaseReady(player)&&player.OriginalMotionTick-shieldTick<12&&Time.realtimeSinceStartupAsDouble<shieldEnd){originalInput.ApplyToRig(player);yield return null;}
            var shieldPressed=MouseSnapshot();ReadOriginalDesktopPointers(touchCenter+shieldDelta,0,0,2);originalInput.ApplyToRig(player);shieldTick=player.OriginalMotionTick;
            bool shieldHeld=MouseCapturesCleared()&&MouseHeldApplied(shieldAim,Vector2.zero,true,false);
            while(MouseCaseReady(player)&&player.OriginalMotionTick-shieldTick<60&&Time.realtimeSinceStartupAsDouble<shieldEnd) {
                originalInput.ApplyToRig(player);shieldHeld&=MouseCapturesCleared()&&MouseHeldApplied(shieldAim,Vector2.zero,true,false);yield return null;
            }
            shieldHeld&=MouseCaseReady(player)&&MouseRigFinite(player)&&player.OriginalMotionTick-shieldTick>=60&&MouseHeldTargetRadiusError(true,shieldAim,true)<.005f;
            cases.Add(J.O("case","off_hand_forward_hold_matches_pressed_control","pressed",shieldPressed,"released",MouseSnapshot(),"off_item_id",player.leftItem==null?0:player.leftItem.itemId,"passed",shieldHeld));passed&=shieldHeld;
            ResetPointers(true);var finalDelta=new Vector2(80,40);
            ReadOriginalDesktopPointers(touchCenter,1,1,0);ReadOriginalDesktopPointers(touchCenter+finalDelta,0,0,1);originalInput.ApplyToRig(player);
            var finalAim=OriginalInputAdapter.QuantizeNub(Vector2.ClampMagnitude(finalDelta/originalInput.CanvasScale,150));
            bool finalSample=MouseCapturesCleared()&&MouseHeldApplied(Vector2.zero,finalAim,false,true);
            cases.Add(J.O("case","fast_release_keeps_final_position_and_pressed_state","passed",finalSample,"snapshot",MouseSnapshot()));passed&=finalSample;
            ResetPointers(true);ReadOriginalDesktopPointers(touchCenter,1,1,0);ReadOriginalDesktopPointers(touchCenter+new Vector2(75,55),0,1,0);originalInput.ApplyToRig(player);
            int restartTick=player.OriginalMotionTick;double restartEnd=Time.realtimeSinceStartupAsDouble+5;
            while(MouseCaseReady(player)&&player.OriginalMotionTick-restartTick<3&&Time.realtimeSinceStartupAsDouble<restartEnd)yield return null;
            ReadOriginalDesktopPointers(touchCenter,0,0,1);originalInput.ApplyToRig(player);var oldPlayer=player;
            var restartBefore=MouseSnapshot();RestartOriginalMatch();restartEnd=Time.realtimeSinceStartupAsDouble+25;
            while((player==oldPlayer||!MouseCaseReady(player))&&Time.realtimeSinceStartupAsDouble<restartEnd)yield return null;
            bool fresh=player!=null&&player!=oldPlayer&&MouseCaseReady(player);restartTick=player==null?0:player.OriginalMotionTick;
            while(fresh&&player.OriginalMotionTick-restartTick<3&&Time.realtimeSinceStartupAsDouble<restartEnd)yield return null;
            bool restartCleared=fresh&&MouseRigFinite(player)&&MouseInputsCleared()&&MouseZero(MouseHandPoint(true).localPosition)&&MouseZero(MouseHandPoint(false).localPosition);
            cases.Add(J.O("case","restart_starts_with_fresh_hand_pose","before",restartBefore,"after",MouseSnapshot(),"fresh_ready",fresh,"finite",player!=null&&MouseRigFinite(player),"inputs_clear",MouseInputsCleared(),"passed",restartCleared));passed&=restartCleared;
        }
        ResetPointers(true);
        File.WriteAllText(Path.Combine(directory,"mouse-validation.json"),J.Write(J.O("passed",passed,"unity",Application.unityVersion,"scope","Production desktop pointer gate/capture/drag/release and rig response; synthetic mouse frames, not OS input","cases",new J(cases),"errors",new J(errors))));
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(directory,"mouse-validation.png"));
        yield return new WaitForSecondsRealtime(.5f);Application.Quit(passed?0:2);
    }
}
