using System.Collections.Generic;
using Jianghu;
using UnityEngine;

public sealed partial class OriginalPreview {
    J timeSource;
    float sourceTimeScale=1;
    public bool NativeUiTimeEvents {get;set;}
    public event System.Action<float> OriginalTimeScaleChanged;
    public void RelayOriginalTimeScale(float value){SetSourceTimeScale(value);}
    double hostAccumulator;
    double lastHostTime;
    readonly List<J> originalPhaseTrace=new List<J>();
    readonly List<TimeSequence> sourceTimeSequences=new List<TimeSequence>();
    sealed class TimeSequence {public J events;public string view;public int index;public float elapsed;public bool unscaled;}
    string OriginalView(int state)=>state==2?"CinematicIntro":state==3?"Countdown":state==4?"Round":state==5?"RoundEnd":state==6?"MatchEnd":"";
    void SetOriginalState(int state) {
        int previous=OriginalState;OriginalState=state;
        if(state==previous)return;
        originalPhaseTrace.Add(J.O("state",state,"real_time",Time.realtimeSinceStartupAsDouble,"scaled_time",Time.timeAsDouble,"frame",Time.frameCount));
        if(timeSource==null)timeSource=J.Parse(Resources.Load<TextAsset>("OriginalTimeScaleSource").text);
        // hy.dxl 0x34434f4 resets only Countdown/Round. Other changes come
        // from the original UI animation RelayTimeScale events via hz.
        if(state==3||state==4)SetSourceTimeScale(1);
        if(!NativeUiTimeEvents){sourceTimeSequences.RemoveAll(s=>s.view==OriginalView(previous));QueueSourceTimeEvents(OriginalView(previous),"Exit");QueueSourceTimeEvents(OriginalView(state),"Enter");}
        OriginalStageActors.ApplyGameState(previous,state);
        foreach(var item in FindObjectsByType<OriginalItem>(FindObjectsSortMode.None))
            if(item.isActiveAndEnabled)item.OnOriginalStateChanged(previous,state);
        // CanvasAndCamera subscribes synchronously to the source state event.
        // Submit Exit before Animator evaluation, so the intro's final slow
        // event cannot run after the new round reset while Exit is still queued.
        hud?.ApplyOriginalState(state,Paused);
    }
    void QueueSourceTimeEvents(string view,string suffix) {
        if(view.Length==0)return;
        var clip=timeSource["clips"][view+"_"+suffix];if(!clip.Has)return;
        var sequence=new TimeSequence{events=clip["events"],view=view,unscaled=timeSource["views"][view]["update_mode"].I==2};
        sourceTimeSequences.Add(sequence);ApplySourceTimeEvents(sequence);
    }
    void SetSourceTimeScale(float value){sourceTimeScale=value;Time.timeScale=Paused?0:value;PublishOriginalTimeScale();}
    void PublishOriginalTimeScale(){arenaAmbience?.ApplySourceTimeScale();OriginalTimeScaleChanged?.Invoke(Time.timeScale);}
    void ApplySourceTimeEvents(TimeSequence sequence) {
        while(sequence.index<sequence.events.A.Count&&(float)sequence.events[sequence.index]["time"].N<=sequence.elapsed+1e-6f)
            SetSourceTimeScale((float)sequence.events[sequence.index++]["value"].N);
    }
    void LateUpdate() {
        if(NativeUiTimeEvents)return;
        foreach(var sequence in sourceTimeSequences) {sequence.elapsed+=sequence.unscaled?Time.unscaledDeltaTime:Time.deltaTime;ApplySourceTimeEvents(sequence);}
        sourceTimeSequences.RemoveAll(s=>s.index>=s.events.A.Count);
    }
    void AdvanceOriginalHostClock() {
        double now=Time.realtimeSinceStartupAsDouble,elapsed=lastHostTime==0?0:now-lastHostTime;lastHostTime=now;
        if(Paused||player==null)return;
        // Local adapter for Fusion's independent 60 Hz match timers. The
        // character's Unity physics and Invoke/coroutines retain scaled time.
        hostAccumulator+=elapsed;double step=Time.fixedDeltaTime;
        for(int catchup=0;hostAccumulator>=step&&catchup<240;catchup++){hostAccumulator-=step;TickOriginalMatch();}
    }
}
