using System;
using System.Collections;
using System.Collections.Generic;
using Jianghu;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Random=UnityEngine.Random;

// CharacterExpressions native 0x342e8ac,0x342951c,0x342fe90..0x34310f0.
// Fusion's pulse representation is retained locally: a byte sequence plus
// two six-bit parameters. Playback uses the supplied original clips.
public sealed class OriginalExpressions : MonoBehaviour {
    public OriginalVoice Voice {get;private set;}
    public uint ExpressionPulse {get;private set;}
    public string CurrentClip {get;private set;}
    public event Action<int> HitPublished;
    OriginalRig owner;
    J data,library;
    Animator animator;
    PlayableGraph graph;
    AnimationMixerPlayable mixer;
    readonly List<State> states=new List<State>();
    sealed class State {public AnimationClipPlayable playable;public float weight,from,target,age,duration;}
    byte sequence,lastSequence;
    bool hasRendered,chargeBlocked,batch;
    float fadeDuration;
    public void Initialize(OriginalRig rig,J settings,J source,Animator target,OriginalVoice voice) {
        owner=rig;data=settings;library=source;animator=target;Voice=voice;batch=Application.isBatchMode;
        fadeDuration=(float)data["fadeDuration"].N;
        graph=PlayableGraph.Create("Original expressions "+rig.name);graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        mixer=AnimationMixerPlayable.Create(graph,0);var output=AnimationPlayableOutput.Create(graph,"Original face",animator);output.SetSourcePlayable(mixer);graph.Play();
    }
    // Network authority is the local recovered fighter. Headless behaviour
    // retains the original Application.isBatchMode render suppression.
    void Pulse(int clip,float a=0,float b=0) {
        sequence++;
        ExpressionPulse=OriginalExpressionPulse.Pack(sequence,clip,a,b);
    }
    public void Idle() {
        chargeBlocked=false;if(owner.dead||owner.passedOut)return;
        Pulse(1,owner.health<25?1:0,Mathf.Clamp01((Random.Range(.85f,1.15f)-.85f)/.3f));
    }
    public void Charge(bool random=true) {
        if(random&&(chargeBlocked||Random.Range(0,10)<6))return;
        if(owner.dead)return;
        Pulse(2);Invoke(owner.stamina<=0?nameof(PantHeavy):nameof(Idle),Random.Range(.75f,1.25f));chargeBlocked=true;
    }
    public void Laugh(){if(owner.dead)return;Pulse(3);Invoke(nameof(Idle),Random.Range(.5f,1f));}
    public void PantHeavy(){if(!owner.dead)Pulse(4);}
    public void Died(){Pulse(5,Mathf.Clamp01(Random.Range(0,.25f)*4),Mathf.Clamp01((Random.Range(1f,7.5f)-1)/6.5f));}
    public int TryHit(int tier) {
        if(owner.dead||owner.passedOut)return 0;
        if(tier>=1&&tier<=3)Invoke(nameof(Idle),Random.Range(tier*.25f,(tier+1)*.25f));
        chargeBlocked=true;return tier;
    }
    public void Hit(int tier) {
        int actual=TryHit(tier);if(actual==0)return;
        if(HitPublished!=null)HitPublished(actual);else PlayHitLocal(actual);
    }
    public void PlayHitLocal(int tier) {
        if(batch)return;
        switch(tier) {
            case 1:Play("hitLightClip");Voice.Play_ScreamLight();break;
            case 2:Play("hitMediumClip");Voice.Play_ScreamMedium();break;
            case 3:Play("hitHeavyClip");Voice.Play_ScreamHeavy();break;
        }
    }
    public void StartScreamingAfterSeveringLimb(){StartCoroutine(SeveringScream());}
    public void StartScreamingAfterBashingLimb(){StartCoroutine(BashingScream());}
    IEnumerator SeveringScream(){yield return new WaitForSeconds(Random.Range(1,2));Hit(3);}
    IEnumerator BashingScream() {
        foreach(int tier in new[]{3,3,3,2,1}){yield return new WaitForSeconds(Random.Range(.5f,1.5f));Hit(tier);}
    }
    public void Play_PantingLight()=>Voice.Play_PantingLight();
    public void Play_PantingHeavyIn()=>Voice.Play_PantingHeavyIn();
    public void Play_PantingHeavyOut()=>Voice.Play_PantingHeavyOut();
    public void Play_DeathScreamLight()=>Voice.Play_ScreamLight();
    public void Play_DeathScreamHeavy()=>Voice.Play_ScreamHeavy();
    State Play(string field,bool fade=true) {
        string key=data[field]["key"].S;var record=library["clips"][key];var clip=Resources.Load<AnimationClip>(record["resource"].S);
        if(clip==null)throw new InvalidOperationException("Original expression clip missing: "+key);
        CurrentClip=clip.name;float duration=fade?fadeDuration:0;
        // Animancer FadeMode.FromStart allows a fresh copy while the prior
        // instance fades out. Preserve that overlap even for the same clip.
        int slot=-1;
        for(int i=0;i<states.Count;i++) {
            var old=states[i];if(old==null){if(slot<0)slot=i;continue;}
            old.from=old.weight;old.target=0;old.age=0;old.duration=duration;
        }
        if(slot<0){slot=states.Count;states.Add(null);mixer.SetInputCount(states.Count);}
        var state=new State{playable=AnimationClipPlayable.Create(graph,clip),target=1,duration=duration};
        state.playable.SetApplyFootIK(false);state.playable.SetApplyPlayableIK(false);state.playable.SetTime(0);state.playable.SetSpeed(1);
        graph.Connect(state.playable,0,mixer,slot);states[slot]=state;
        if(duration<=0){state.weight=1;mixer.SetInputWeight(slot,1);}
        return state;
    }
    void RenderPulse() {
        if(batch||hasRendered&&lastSequence==sequence)return;
        lastSequence=sequence;hasRendered=true;
        int clip=(int)(ExpressionPulse&15);float a=((ExpressionPulse>>4)&63)/63f,b=((ExpressionPulse>>10)&63)/63f;
        switch(clip) {
            case 1:Voice.StopAllVoices();var idle=Play(a>.5f?"pantingLightClip":"idleClip");idle.playable.SetSpeed(.85f+b*.3f);break;
            case 2:Play("chargeClip");Voice.Play_Charge();break;
            case 3:Play("laughClip");break;
            case 4:Play("pantingHeavyClip");break;
            case 5:
                var died=Play("diedClip",false);died.playable.SetTime((.25f+a*.25f)*died.playable.GetAnimationClip().length);died.playable.SetSpeed(owner.headDisabled?0:1+b*6.5f);break;
        }
    }
    void Update() {
        RenderPulse();
        for(int i=0;i<states.Count;i++) {
            var state=states[i];if(state==null)continue;
            state.age+=Time.deltaTime;float q=state.duration<=0?1:Mathf.Clamp01(state.age/state.duration);
            state.weight=Mathf.Lerp(state.from,state.target,q);mixer.SetInputWeight(i,state.weight);
            if(q>=1&&state.target==0){graph.Disconnect(mixer,i);state.playable.Destroy();states[i]=null;}
        }
    }
    void OnDestroy(){if(graph.IsValid())graph.Destroy();}
}

public static class OriginalExpressionPulse {
    // CharacterExpressions.ckf 0x3430310, retaining banker's rounding.
    public static uint Pack(byte sequence,int clip,float a,float b) =>
        (uint)(clip&15)|((uint)Mathf.Clamp(Mathf.RoundToInt(a*63),0,63)<<4)|((uint)Mathf.Clamp(Mathf.RoundToInt(b*63),0,63)<<10)|((uint)sequence<<16);
}
