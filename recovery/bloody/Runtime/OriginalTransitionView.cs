using System;
using System.Collections.Generic;
using Jianghu;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// TransitionView 0x36afb34..0x36afdb8, with its actual mounted Animancer
// playback domain. The original clips own their curves and animation events.
public sealed class OriginalTransitionView : MonoBehaviour {
    OriginalArenaTransitionSequence sequence;
    J library;
    Animator animator;
    AudioSource audioSource;
    PlayableGraph graph;
    AnimationMixerPlayable mixer;
    readonly Dictionary<string,AnimationClipPlayable> states=new Dictionary<string,AnimationClipPlayable>();
    readonly Dictionary<string,int> slots=new Dictionary<string,int>();
    bool initialized,listening;
    public string CurrentClip {get;private set;}
    public bool Listening=>listening;
    public static OriginalTransitionView Attach(Transform hudRoot,OriginalArenaTransitionSequence sourceSequence) {
        if(hudRoot==null||sourceSequence==null)throw new ArgumentNullException("Original TransitionView owner missing");
        var asset=Resources.Load<TextAsset>("OriginalTransitionViewSource");if(asset==null)throw new InvalidOperationException("OriginalTransitionViewSource missing");var source=J.Parse(asset.text);
        const string prefix="CanvasAndCamera/";
        Transform Node(string path)=>path=="CanvasAndCamera"?hudRoot:hudRoot.Find(path.Substring(prefix.Length));
        var root=Node(source["path"].S);if(root==null)throw new InvalidOperationException("Original TransitionView hierarchy missing");
        if(root.GetComponent<OriginalTransitionView>()!=null)throw new InvalidOperationException("Original TransitionView already attached");
        var driver=root.gameObject.AddComponent<OriginalTransitionView>();
        driver.sequence=sourceSequence;driver.library=source;driver.animator=Node(source["animancer"]["_Animator"]["path"].S)?.GetComponent<Animator>();driver.audioSource=Node(source["data"]["audioSource"]["path"].S)?.GetComponent<AudioSource>();
        if(driver.animator==null||driver.audioSource==null)throw new InvalidOperationException("Original TransitionView Animator/AudioSource missing");
        driver.animator.updateMode=(AnimatorUpdateMode)source["animator"]["m_UpdateMode"].I;driver.animator.cullingMode=(AnimatorCullingMode)source["animator"]["m_CullingMode"].I;driver.animator.enabled=source["animator"]["m_Enabled"].B;
        driver.graph=PlayableGraph.Create("Original TransitionView");driver.graph.SetTimeUpdateMode(DirectorUpdateMode.UnscaledGameTime);driver.mixer=AnimationMixerPlayable.Create(driver.graph,0);
        var output=AnimationPlayableOutput.Create(driver.graph,"Original transition cover",driver.animator);output.SetSourcePlayable(driver.mixer);
        driver.initialized=true;
        bool wasActive=driver.isActiveAndEnabled;root.gameObject.SetActive(source["source_active"].B);
        if(wasActive&&driver.isActiveAndEnabled)driver.OnEnable();
        return driver;
    }
    void OnEnable() {
        if(!initialized)return;
        sequence.OnSwitching+=OnSwitching;sequence.OnSwitched+=OnSwitched;listening=true;
    }
    void OnDisable() {
        if(!initialized)return;
        sequence.OnSwitching-=OnSwitching;sequence.OnSwitched-=OnSwitched;listening=false;
        // This is the separately mounted Animancer ActionOnDisable.Stop=0.
        // The source TransitionView callback itself only unsubscribes.
        if(graph.IsValid())graph.Stop();
    }
    void OnSwitching()=>Play("slideIn");
    void OnSwitched()=>Play("slideOut");
    void Play(string field) {
        if(!states.TryGetValue(field,out var state)) {
            var clip=Resources.Load<AnimationClip>(library["clips"][field]["resource"].S);if(clip==null)throw new InvalidOperationException("Original transition clip missing: "+field);
            state=AnimationClipPlayable.Create(graph,clip);state.SetApplyFootIK(false);state.SetApplyPlayableIK(false);
            int slot=slots.Count;slots.Add(field,slot);states.Add(field,state);mixer.SetInputCount(slot+1);graph.Connect(state,0,mixer,slot);
        }
        foreach(var slot in slots.Values)mixer.SetInputWeight(slot,0);
        mixer.SetInputWeight(slots[field],1);
        // Source Animancer.Play(clip,0,FadeMode.FromStart=2) restarts the clip.
        state.SetTime(0);state.SetSpeed(1);CurrentClip=state.GetAnimationClip().name;graph.Play();
    }
    public void PlaySlideIn()=>PlaySound("panelsSlideIn");
    public void PlayUnlock()=>PlaySound("panelsUnlock");
    public void PlaySlideOut()=>PlaySound("panelsSlideOut");
    public void PlayContact()=>PlaySound("panelsContact");
    void PlaySound(string field) {
        var settings=library["audio_objects"][field];
        audioSource.pitch=UnityEngine.Random.Range((float)settings["pitch"]["min"].N,(float)settings["pitch"]["max"].N);
        var clips=settings["clips"].A;int index=UnityEngine.Random.Range(0,clips.Count);
        var clip=Resources.Load<AudioClip>(library["audio"][clips[index].S]["resource"].S);
        if(clip==null)throw new InvalidOperationException("Original transition AudioClip missing: "+clips[index].S);
        float volume=UnityEngine.Random.Range((float)settings["volume"]["min"].N,(float)settings["volume"]["max"].N);
        audioSource.PlayOneShot(clip,volume);
    }
    void OnDestroy(){if(graph.IsValid())graph.Destroy();}
}
