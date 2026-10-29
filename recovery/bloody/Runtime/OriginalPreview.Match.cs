using System.Collections.Generic;
using Jianghu;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum OriginalMatchEndReason : byte { Normal=0,Forfeit=1,Disconnect=2,TimeOver=3 }

public sealed partial class OriginalPreview {
    // Source NetworkedGameState values and the original mounted rules asset.
    public int OriginalState {get;private set;}=0;
    public bool Paused {get;private set;}
    public int WinsPlayer {get;private set;}
    public int WinsEnemy {get;private set;}
    J rules;
    public int RoundsToWin=>rules==null?3:rules["roundsToWin"].I;
    public float RoundTimeRemaining {get;private set;}
    public float CountdownRemaining=>OriginalState==3?phaseTicks*Time.fixedDeltaTime:0;
    public int CurrentRound {get;private set;}
    public bool LastWinnerIsPlayer {get;private set;}
    public OriginalMatchEndReason EndReason {get;private set;}
    public event System.Action OriginalRoundTimeOver;
    public event System.Action OriginalHpResetRequested;
    public string PlayerDisplayName=>player==null?"":player.OriginalCharacterName;
    public string EnemyDisplayName=>enemy==null?"":enemy.OriginalCharacterName;
    readonly Dictionary<int,Pointer> pointers=new Dictionary<int,Pointer>();
    readonly OriginalInputAdapter originalInput=new OriginalInputAdapter();
    int roundTicks,phaseTicks;
    sealed class Pointer {public bool left;}
    void SpawnOriginalRound() {
        if(rules==null)rules=J.Parse(Resources.Load<TextAsset>("OriginalMatchRules").text)[GameMode==0?"MatchRules_SP_1_Campaign":GameMode==2?"MatchRules_SP_3_Practice":"MatchRules_MP_3_Friendly"]["data"];
        // Original Runner/MainNetworkRunner SpawnPoint1/2 world positions.
        player=Create(playerPrefab,true,new Vector3(-1.5f,.5f,0));
        OriginalStageActors.NotifyLocalPlayerSpawned(player.transform);
        ApplyPlaytestPlayerConfig();
        if(GameMode==2) {
            if(dummyPrefab==null)throw new System.InvalidOperationException("Original Practice Dummy prefab missing");
            var dummy=Instantiate(dummyPrefab,new Vector3(1.5f,.5f,0),dummyPrefab.transform.rotation);
            dummy.SetActive(true);practiceDummy=dummy.AddComponent<OriginalPracticeDummy>();
            OriginalDummyColorizeSprite.Attach(dummy,()=>Progress.Seed,()=>environmentCustomizer.IsFinished);
            practiceDummy.Initialize();
            practiceDummy.ImpactOccurred+=_=>impacts++;
        } else {
            enemy=Create(enemyPrefab,false,new Vector3(1.5f,.5f,0));
            if(GameMode==0)enemy.ApplyOriginalCampaignResult(OriginalCampaignGenerator.Load().GenerateCampaign(Progress.Region,Progress.Level));
            else enemy.ApplyOriginalNamedConfig("Enemy");
            player.opponent=enemy;enemy.opponent=player;
            enemy.Died+=OnOriginalDeath;enemy.ImpactOccurred+=_=>impacts++;
        }
        player.Died+=OnOriginalDeath;player.ImpactOccurred+=_=>impacts++;
        CurrentRound++;
        // csu 0x3649444: first-round cinematic, then the mounted rules'
        // countdown; subsequent rounds respawn before entering countdown.
        SetOriginalState(CurrentRound==1&&rules["cinematicIntroMode"].I!=0?2:rules["UsesCountdown"].B?3:4);
        phaseTicks=Mathf.RoundToInt(3/Time.fixedDeltaTime);RoundTimeRemaining=0;
        if(OriginalState==4)BeginActiveRound();
        // Loading the original metadata/textures may span several seconds.
        // Fusion starts this phase's timer after spawning; scene-load time
        // must not be replayed as already elapsed match ticks on first Update.
        lastHostTime=Time.realtimeSinceStartupAsDouble;hostAccumulator=0;
        ResetPointers(true);
    }
    void BeginActiveRound() {
        SetOriginalState(4);roundTicks=Mathf.RoundToInt((float)rules["maxRoundDurationSeconds"].N/Time.fixedDeltaTime);
        RoundTimeRemaining=roundTicks*Time.fixedDeltaTime;
        player.BeginOriginalRound();if(enemy!=null)enemy.BeginOriginalRound();
    }
    void TickOriginalMatch() {
        if(player==null||Paused)return;
        if(OriginalState==2||OriginalState==3) {
            phaseTicks=Mathf.Max(0,phaseTicks-1);
            if(phaseTicks==0) {
                if(OriginalState==2&&rules["UsesCountdown"].B){SetOriginalState(3);phaseTicks=Mathf.RoundToInt(3/Time.fixedDeltaTime);}
                else BeginActiveRound();
            }
            return;
        }
        if(OriginalState!=4)return;
        if(GameMode==2)return;
        roundTicks=Mathf.Max(0,roundTicks-1);RoundTimeRemaining=roundTicks*Time.fixedDeltaTime;
        if(player.dead||enemy.dead){FinishOriginalRound(!player.dead);return;}
        if((float)rules["maxRoundDurationSeconds"].N<=0||RoundTimeRemaining>0)return;
        // ctf publishes while state/reason still retain their prior values,
        // then writes TimeOver=3. It never kills the designated losing slot.
        OriginalRoundTimeOver?.Invoke();EndReason=OriginalMatchEndReason.TimeOver;
        FinishOriginalRound(Mathf.Approximately(player.health,enemy.health)?Random.value>=.5f:player.health>enemy.health,false);
    }
    void OnOriginalDeath(OriginalRig rig){
        if(OriginalState!=4)return;
        // MatchRulesPractice.OnPlayerDeath publishes the original request
        // and returns DeathVerdict.None. It does not manufacture a verdict
        // or reset HP/body flags in the rules implementation.
        if(GameMode==2){OriginalHpResetRequested?.Invoke();return;}
        FinishOriginalRound(rig==enemy);
    }
    void FinishOriginalRound(bool playerWon,bool death=true) {
        // Friendly death verdict tests the survivor's wins + 1 before the
        // original controller increments the score (crw/ctc).
        LastWinnerIsPlayer=playerWon;
        RoundTimeRemaining=0;
        if(LastWinnerIsPlayer)WinsPlayer++;else WinsEnemy++;
        bool matchEnd=WinsPlayer>=RoundsToWin||WinsEnemy>=RoundsToWin;
        // HandlePlayerDeath resets the reason only for a normal match end;
        // an earlier TimeOver survives subsequent non-final round verdicts.
        if(death&&matchEnd)EndReason=OriginalMatchEndReason.Normal;
        if(matchEnd&&GameMode==0&&playerWon)CampaignOutcome=Progress.RecordVictory(player.health);
        SetOriginalState(matchEnd?6:5);
        // The survivor's source AI enters EnjoyingTheVictory on seeing the
        // opponent's death; stopping it here would suppress that coroutine.
        ResetPointers();
    }
    // Original local-state handler resets to normal speed, then applies pause.
    public void SetPaused(bool paused) {Paused=paused;SetSourceTimeScale(1);ResetPointers();hud?.ApplyOriginalState(OriginalState,paused);}
    void DisposeRound() {
        if(player!=null)OriginalStageActors.NotifyLocalPlayerDespawned(player.transform);
        if(practiceDummy!=null){Destroy(practiceDummy.gameObject);practiceDummy=null;}
        foreach(var rig in new[]{player,enemy}) {
            if(rig==null)continue;
            rig.StopOriginalAI();
            foreach(var item in FindObjectsByType<OriginalItem>(FindObjectsSortMode.None))if(item.owner==rig)Destroy(item.gameObject);
            foreach(var part in FindObjectsByType<OriginalBodyPart>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                if(part.rig==rig&&!part.transform.IsChildOf(rig.transform))Destroy(part.gameObject);
            Destroy(rig.gameObject);
        }
        player=null;enemy=null;
    }
    public void AdvanceOriginalRound() {if(OriginalState!=5)return;SetPaused(false);DisposeRound();SpawnOriginalRound();}
    public void RestartOriginalMatch() {TransitionOriginalArena(GameMode);}
    void ResetPointers(bool clearHandPose=false) {if(TutorialContext!=null)foreach(var pointer in pointers.Values)TutorialContext.RaiseHandReleased();pointers.Clear();originalInput.Reset(clearHandPose);if(player!=null){player.movement=0;originalInput.ApplyToRig(player,false);}}
    bool OverButton(int id,Vector2 position) {
        if(EventSystem.current==null)return false;
        var eventData=new PointerEventData(EventSystem.current){position=position,pointerId=id};var results=new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData,results);
        // UGUI dispatches to its first valid hit. A covered button further
        // down the raycast list cannot intercept the foreground joystick.
        foreach(var hit in results)if(hit.gameObject!=null)return hit.gameObject.GetComponentInParent<Selectable>()!=null;
        return false;
    }
    void PointerDown(int id,Vector2 position,bool left,bool lockCenter=false) {
        if(OverButton(id,position))return;
        foreach(var pointer in pointers.Values)if(pointer.left==left)return;
        if(!originalInput.PointerDown(id,position,left,lockCenter))return;
        pointers[id]=new Pointer{left=left};player.OnOriginalScreenTap(left);
        TutorialContext?.RaiseHandPressed();
    }
    void PointerUp(int id) {if(pointers.Remove(id))TutorialContext?.RaiseHandReleased();originalInput.PointerUp(id);}
    bool BeginOriginalInputFrame() {
        originalInput.UpdateViewport();
        if(Paused||OriginalState!=4||(hud!=null&&hud.OriginalTransitionBlocksPointerInput)){ResetPointers();return false;}
        return true;
    }
    void ReadOriginalDesktopPointers(Vector2 position,int down,int held,int up) {
        for(int button=0;button<2;button++) {
            int id=-1-button,mask=1<<button;
            // Desktop ownership follows the button, independently of position:
            // left button -> original right joystick/main hand, right -> off hand.
            if((down&mask)!=0)PointerDown(id,position,button==1,true);
            // Sample the release position too: a fast final motion can arrive
            // in the same frame as Up and must become the retained hand aim.
            if(((held|up)&mask)!=0&&pointers.ContainsKey(id))originalInput.PointerDrag(id,position);
            if((up&mask)!=0)PointerUp(id);
        }
    }
    void ReadOriginalInput() {
        Vector2 pos=Input.mousePosition;
        int down=0,held=0,up=0;
        for(int button=0;button<2;button++) {
            int mask=1<<button;
            if(Input.GetMouseButtonDown(button))down|=mask;
            if(Input.GetMouseButton(button))held|=mask;
            if(Input.GetMouseButtonUp(button))up|=mask;
        }
        if(!BeginOriginalInputFrame()){TraceOriginalMouseFrame(pos,down,held,up);return;}
        if(Input.touchCount>0) {
            foreach(var t in Input.touches) {
                if(t.phase==TouchPhase.Began)PointerDown(t.fingerId,t.position,t.position.x<Screen.width*.5f);
                else if(t.phase==TouchPhase.Ended||t.phase==TouchPhase.Canceled)PointerUp(t.fingerId);
                else if(pointers.ContainsKey(t.fingerId))originalInput.PointerDrag(t.fingerId,t.position);
            }
        } else {
            ReadOriginalDesktopPointers(pos,down,held,up);
        }
        originalInput.ApplyToRig(player);
        TraceOriginalMouseFrame(pos,down,held,up);
        player.movement=(Input.GetKey(KeyCode.D)||Input.GetKey(KeyCode.RightArrow)?1:0)-(Input.GetKey(KeyCode.A)||Input.GetKey(KeyCode.LeftArrow)?1:0);
        if(Mathf.Abs(player.movement)>.01f)player.KeepOriginalMove(player.movement);
        if(Input.GetKeyDown(KeyCode.Space))player.StartOriginalKick();
    }
    void OnDestroy(){Time.timeScale=1;}
    void OnApplicationFocus(bool focused){if(!focused)ResetPointers();}
    void OnApplicationPause(bool paused){if(paused)ResetPointers();}
}
