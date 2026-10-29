using System;
using System.Collections;
using Jianghu;
using UnityEngine;

public sealed partial class OriginalPreview {
    OriginalArenaTransitionSequence arenaTransition;
    public bool IsTransitioning=>arenaTransition!=null&&arenaTransition.IsTransitioning;
    void InitializeOriginalArenaTransition() {
        if(arenaTransition!=null)return;
        var resource=Resources.Load<TextAsset>("OriginalArenaTransitionSource");
        if(resource==null)throw new InvalidOperationException("Original Arena transition source missing");
        arenaTransition=new OriginalArenaTransitionSequence(J.Parse(resource.text),new OriginalArenaTransitionContext {
            RunnerActive=()=>player!=null,NetworkControllerExists=()=>player!=null&&OriginalState>=2&&OriginalState<=6,
            InterstitialPlaying=()=>false,TearDownRunner=TearDownOriginalRunner,SpawnRunner=SpawnOriginalRunner,
            RequestLocalState=state=>Debug.LogError("Original menu local state is not yet integrated: "+state),
            SetContextObjects=arena=>hud?.SetOriginalArenaControls(arena)
        },false);
        arenaTransition.OnSwitching+=()=> {
            foreach(var item in FindObjectsByType<OriginalItem>(FindObjectsSortMode.None))item.OnOriginalTransitionSwitching();
            originalTimeOfDay?.OnTransitionSwitching(Paused);
        };
        hud?.AttachOriginalTransition(arenaTransition);
    }
    IEnumerator TearDownOriginalRunner() {
        ResetPointers();DisposeRound();hud?.OnOriginalRunnerDestroyed(false);SetOriginalState(0);
        // RunnerSpawner's local teardown bridge publishes RunnerDestroyed
        // synchronously, then yields a frame; the outer source adds two more.
        yield return null;
    }
    void SpawnOriginalRunner(int mode)=>StartCoroutine(SpawnOriginalRunnerAfterStageStart(mode));
    IEnumerator SpawnOriginalRunnerAfterStageStart(int mode) {
        // Native SpawnRunner, rather than the click handler, owns this write.
        Progress.GameMode=mode;SetPaused(false);rules=null;
        WinsPlayer=WinsEnemy=0;CurrentRound=0;EndReason=OriginalMatchEndReason.Normal;CampaignOutcome=null;
        CustomizeOriginalEnvironment();
        yield return OriginalStageActors.WaitForStart();
        SpawnOriginalRound();
    }
    void StartOriginalArenaTransition(int mode) {
        InitializeOriginalArenaTransition();
        if(arenaTransition.IsTransitioning)return;
        StartCoroutine(arenaTransition.CreateToArena(mode));
    }
}
