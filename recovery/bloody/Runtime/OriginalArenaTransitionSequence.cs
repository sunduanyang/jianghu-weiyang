using System;
using System.Collections;
using Jianghu;
using UnityEngine;

// Actual owners supply the runner operations. In the local port Teardown must
// emit its real OnRunnerDestroyed bridge, including CanvasAndCamera.ccu, before
// it finishes. This sequence never manually calls Tutorial.OnEnable/OnDisable.
public sealed class OriginalArenaTransitionContext {
    public Func<bool> RunnerActive,NetworkControllerExists,InterstitialPlaying;
    public Func<IEnumerator> TearDownRunner;
    public Action<int> SpawnRunner,RequestLocalState;
    public Action<bool> SetContextObjects;
    public Func<bool> BatchMode;
}

// TransitionController.dck/et and dcm/eu. Create* corresponds to the native
// IEnumerator factory, not its MonoBehaviour StartCoroutine wrapper. The host
// rejects repeated user requests while IsTransitioning, then runs one factory.
public sealed class OriginalArenaTransitionSequence {
    readonly OriginalArenaTransitionContext context;
    readonly float coverDelay,spawnTimeout;
    public bool IsTransitioning {get;private set;}
    public bool InArena {get;private set;}
    public event Action OnSwitching,OnSwitched,OnSwitchedToArena,OnSwitchedToMenu;
    public event Action<bool> OnContextChanged;
    public OriginalArenaTransitionSequence(J source,OriginalArenaTransitionContext owner,bool initialInArena) {
        context=owner??throw new ArgumentNullException(nameof(owner));
        if(source==null||!source["cover_delay"].Has||!source["network_controller_spawn_timeout"].Has)throw new ArgumentException("Original Arena transition source missing");
        if(context.RunnerActive==null||context.NetworkControllerExists==null||context.InterstitialPlaying==null||context.TearDownRunner==null||context.SpawnRunner==null||context.RequestLocalState==null||context.SetContextObjects==null)
            throw new ArgumentException("Original Arena transition owner callbacks missing");
        coverDelay=(float)source["cover_delay"].N;spawnTimeout=(float)source["network_controller_spawn_timeout"].N;InArena=initialInArena;
    }
    void SwitchContext(bool arena) {
        InArena=arena;
        if(!(context.BatchMode?.Invoke()??Application.isBatchMode))context.SetContextObjects(arena);
        if(arena)OnSwitchedToArena?.Invoke();else OnSwitchedToMenu?.Invoke();
        OnContextChanged?.Invoke(arena);
    }
    public IEnumerator CreateToArena(int mode) {
        IsTransitioning=true;OnSwitching?.Invoke();
        yield return new WaitForSecondsRealtime(coverDelay);
        if(context.RunnerActive()) {
            yield return context.TearDownRunner();
            yield return null;yield return null;
        } else SwitchContext(true);
        // Original dby is !hq.InterstitialPlaying, not a runner-ready check.
        yield return new WaitUntil(()=>!context.InterstitialPlaying());
        context.SpawnRunner(mode);
        float elapsed=0;
        while(!context.NetworkControllerExists()&&elapsed<spawnTimeout) {
            elapsed+=Time.unscaledDeltaTime;yield return null;
        }
        if(!context.NetworkControllerExists()) {
            yield return CreateToMenu(7); // Original MultiModePicker, even for SP timeout.
            yield break;
        }
        yield return null;yield return null;yield return null;yield return null;yield return null;
        OnSwitched?.Invoke();IsTransitioning=false;
    }
    public IEnumerator CreateToMenu(int localState) {
        IsTransitioning=true;OnSwitching?.Invoke();
        yield return new WaitForSecondsRealtime(coverDelay);
        // Source menu coroutine always asks for teardown, including an empty runner.
        yield return context.TearDownRunner();
        SwitchContext(false);context.RequestLocalState(localState);
        OnSwitched?.Invoke();IsTransitioning=false;
    }
}
