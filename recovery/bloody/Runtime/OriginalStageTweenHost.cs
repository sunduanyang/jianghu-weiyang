using System;
using System.Collections.Generic;
using UnityEngine;

// Small execution backend for the recovered stage's DOMove/DOFade calls.
// Source DOTweenSettings: UpdateType.Normal(0), independentUpdate=false,
// timeScale=1, defaultEase=OutQuad(6), safeMode=true. Disabled targets keep
// running; callers retain all concurrent tweens because native has no DOKill.
public sealed class OriginalStageTweenHost : MonoBehaviour {
    static OriginalStageTweenHost instance;
    readonly List<Func<float,bool>> active=new List<Func<float,bool>>();
    public static void Add(Func<float,bool> step) {
        if(step==null)throw new ArgumentNullException(nameof(step));
        if(instance==null) {
            var host=new GameObject("Original stage tween execution");
            DontDestroyOnLoad(host);instance=host.AddComponent<OriginalStageTweenHost>();
        }
        instance.active.Add(step);
    }
    void Update() {
        for(int i=0;i<active.Count;) {
            if(active[i](Time.deltaTime))i++;else active.RemoveAt(i);
        }
    }
    void OnDestroy(){if(instance==this)instance=null;}
}
