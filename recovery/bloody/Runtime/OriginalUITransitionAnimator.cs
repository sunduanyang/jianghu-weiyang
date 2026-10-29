using System;
using System.Collections;
using UnityEngine;

// Literal UITransitionAnimator, 0x36b7448..0x36b8120. hz is the separate
// original event relay; its float event is forwarded by RelayTimeScale here.
public sealed class OriginalUITransitionAnimator : MonoBehaviour {
    public Animator Animator {get;private set;}
    public bool Exiting=>exitRoutine!=null;
    public event Action<OriginalUITransitionAnimator,float> TimeScaleRelayed;
    GameObject root;
    Coroutine exitRoutine;
    Action<float> timeScaleRelay;
    public void Initialize(GameObject sourceRoot,Animator sourceAnimator,Action<float> relay) {
        root=sourceRoot;Animator=sourceAnimator;timeScaleRelay=relay;
        if(root==null||Animator==null)throw new InvalidOperationException("Original UI transition references missing on "+name);
    }
    public void Enter(bool delayed=false) {
        StopExit();
        if(!root.activeSelf)root.SetActive(true);
        Animator.SetTrigger(delayed?"EnterDelayed":"Enter");
    }
    public void Exit(bool immediate=false) {
        if(immediate){root.SetActive(false);return;}
        if(Animator.isActiveAndEnabled)Animator.SetTrigger("Exit");
        if(gameObject.activeInHierarchy)exitRoutine=StartCoroutine(WaitForExit());
    }
    IEnumerator WaitForExit() {
        // UITransitionAnimator/ga.MoveNext 0x36b7ff8: always yield one
        // frame so the Exit trigger can change the current Animator state.
        yield return null;
        while(Animator.GetCurrentAnimatorStateInfo(0).normalizedTime<1)yield return null;
        root.SetActive(false);exitRoutine=null;
    }
    void StopExit(){if(exitRoutine!=null){StopCoroutine(exitRoutine);exitRoutine=null;}}
    void OnDisable(){StopExit();}
    public void ShowSub(GameObject target){if(target!=null)target.SetActive(true);}
    public void HideSub(GameObject target){if(target!=null)target.SetActive(false);}
    // AnimationClip event receiver. Do not replay these events in a host
    // clock as well: Animator advances them in its original update mode.
    public void RelayTimeScale(float value){timeScaleRelay?.Invoke(value);TimeScaleRelayed?.Invoke(this,value);}
}
