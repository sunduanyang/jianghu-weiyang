using System;
using System.Collections.Generic;
using Jianghu;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Required external ownership for the original Practice tutorial. The host
// must provide its actual progress/state/transition services. No campaign
// progression or character/equipment data is created by the tutorial itself.
public sealed class OriginalTutorialContext {
    public Func<int> GameMode;
    public Func<bool> HasProgress;
    public Func<bool> IsTutorialPassed;
    public Func<bool> HasNetworkController;
    public Func<int> NetworkState;
    public Action MarkTutorialPassed;
    public Func<bool> HasTransition;
    public Action<int> SetCampaignLevel,SetCampaignStage,TransitionToArena;
    public event Action HandInputPressed,HandInputReleased;
    // PlayerTouchControls emits each hand's callback independently. These
    // must not be coalesced into only the first press / final finger release.
    public void RaiseHandPressed()=>HandInputPressed?.Invoke();
    public void RaiseHandReleased()=>HandInputReleased?.Invoke();
}

// TutorialController's 16 original methods, VA 0x3443c98..0x344451c.
// Its mounted four screens and existing UITransitionAnimator controllers are
// retained. This component never changes rig input, physics or equipment.
public sealed class OriginalTutorialController : MonoBehaviour {
    OriginalTutorialContext context;
    OriginalTutorialAlphaTweens fades;
    GameObject tutorialContainer,topBar;
    OriginalUITransitionAnimator[] screens;
    CanvasGroup[] hands;
    readonly List<Tuple<Button,UnityAction>> buttonBindings=new List<Tuple<Button,UnityAction>>();
    int step;
    bool initialized,listening;
    public int CurrentStep=>step;
    public bool ListeningForHandInput=>listening;

    public static OriginalTutorialController Attach(Transform hudRoot,OriginalTutorialContext owner) {
        if(hudRoot==null||owner==null||owner.GameMode==null||owner.HasProgress==null||owner.IsTutorialPassed==null||owner.HasNetworkController==null||owner.NetworkState==null||owner.MarkTutorialPassed==null)throw new ArgumentNullException("Original tutorial owner context missing");
        var resource=Resources.Load<TextAsset>("OriginalTutorialSource");if(resource==null)throw new InvalidOperationException("OriginalTutorialSource missing");J library=J.Parse(resource.text),record=library["controller"],data=record["data"];
        var nodes=new Dictionary<string,Transform>();
        void Walk(Transform node,string path){nodes[path]=node;foreach(Transform child in node)Walk(child,path+"/"+child.name);}Walk(hudRoot,"CanvasAndCamera");
        Transform Required(string path)=>nodes.TryGetValue(path,out var node)?node:throw new InvalidOperationException("Original tutorial node missing: "+path);
        Transform Ref(J reference)=>Required(reference["path"].S);
        var host=Required(record["path"].S);if(host.GetComponent<OriginalTutorialController>()!=null)throw new InvalidOperationException("Original tutorial already attached");
        var scheduler=hudRoot.GetComponent<OriginalTutorialAlphaTweens>()??hudRoot.gameObject.AddComponent<OriginalTutorialAlphaTweens>();
        var driver=host.gameObject.AddComponent<OriginalTutorialController>();driver.context=owner;driver.fades=scheduler;driver.tutorialContainer=Ref(data["tutorialContainer"]).gameObject;driver.topBar=Ref(data["topBarInGame"]).gameObject;
        driver.screens=new OriginalUITransitionAnimator[data["tutorialScreens"].A.Count];
        for(int i=0;i<driver.screens.Length;i++) {
            driver.screens[i]=Ref(data["tutorialScreens"][i]).GetComponent<OriginalUITransitionAnimator>();
            if(driver.screens[i]==null)throw new InvalidOperationException("Original tutorial UITransitionAnimator must be restored by OriginalUIFlow before Attach");
        }
        driver.hands=new CanvasGroup[data["tutorialHandsCanvasGroups"].A.Count];
        for(int i=0;i<driver.hands.Length;i++){driver.hands[i]=Ref(data["tutorialHandsCanvasGroups"][i]).GetComponent<CanvasGroup>();if(driver.hands[i]==null)throw new InvalidOperationException("Original tutorial hands CanvasGroup missing");}
        foreach(var row in library["buttons"].A) {
            var button=Required(row["path"].S).GetComponent<Button>();if(button==null)throw new InvalidOperationException("Original tutorial Button missing");
            foreach(var call in row["calls"].A) {
                if(call["m_CallState"].I==0)continue;
                UnityAction action;
                switch(call["m_MethodName"].S) {
                    case "OnClickedNext":action=driver.OnClickedNext;break;
                    case "OnClickedNo":action=driver.OnClickedNo;break;
                    case "OnClickedFight":action=driver.OnClickedFight;break;
                    default:throw new InvalidOperationException("Unsupported original tutorial callback: "+call["m_MethodName"].S);
                }
                button.onClick.AddListener(action);driver.buttonBindings.Add(Tuple.Create(button,action));
            }
        }
        // The reference currently runs in English. Preserve exact original
        // table strings; imported original fonts/materials remain in place.
        int english=0;for(int i=0;i<library["languages"].A.Count;i++)if(library["languages"][i]["Code"].S=="en"){english=i;break;}
        foreach(var localizer in library["localizers"].A) {
            string term=localizer["term"].S;if(term.Length==0)continue;
            var text=Required(localizer["path"].S).GetComponent<TMP_Text>();if(text!=null)text.text=library["terms"][term][english].S;
        }
        var sourceRef=library["uisfx_default"];var sourceNode=Required(sourceRef["path"].S);var sources=sourceNode.GetComponents<AudioSource>();
        if(sourceRef["ordinal"].I>=sources.Length)throw new InvalidOperationException("Original tutorial UISFX AudioSource missing");
        var audioSource=sources[sourceRef["ordinal"].I];
        foreach(var popup in library["popup_sfx"].A) {
            var audioDriver=Required(popup["path"].S).gameObject.AddComponent<OriginalTutorialPopupSFX>();audioDriver.Initialize(audioSource,popup,library);
        }
        driver.initialized=true;if(driver.isActiveAndEnabled)driver.OnEnable();return driver;
    }
    void OnEnable() {
        if(!initialized||context.GameMode()!=2||!context.HasProgress()||context.IsTutorialPassed())return;
        context.HandInputPressed+=OnHandInputPressed;context.HandInputReleased+=OnHandInputReleased;listening=true;
        tutorialContainer.SetActive(false);foreach(var screen in screens)screen.gameObject.SetActive(false);
        step=0;ShowScreen(0);Invoke(nameof(HideInGameTopBar),.1f);
    }
    void OnDisable() {
        if(!initialized)return;
        context.HandInputPressed-=OnHandInputPressed;context.HandInputReleased-=OnHandInputReleased;listening=false;
        // The source does not cancel Invoke callbacks or kill alpha tweens.
    }
    public void OnClickedNext() {
        if(!context.HasNetworkController()||context.NetworkState()!=4)return;
        step=unchecked(step+1);if(step<screens.Length)ShowScreen(step);
    }
    // Original OnClickedNo is the four-byte branch at 0x3444134 directly to
    // OnClickedFight (0x3444138). Preserve this APK's identical button path.
    public void OnClickedNo()=>OnClickedFight();
    public void OnClickedFight() {
        FinishTutorial();
        if(!context.HasProgress()||context.HasTransition==null||!context.HasTransition())return;
        if(context.SetCampaignLevel==null||context.SetCampaignStage==null||context.TransitionToArena==null)throw new InvalidOperationException("Original tutorial Campaign transition context missing");
        context.SetCampaignLevel(1);context.SetCampaignStage(1);context.TransitionToArena(0);
    }
    void ShowScreen(int index) {
        tutorialContainer.SetActive(true);foreach(var screen in screens)screen.Exit(false);screens[index].Enter();
    }
    void FinishTutorial() {
        foreach(var screen in screens)screen.Exit(false);ShowInGameTopBar();
        if(context.HasProgress())context.MarkTutorialPassed();
    }
    void OnHandInputPressed()=>FadeHands(0);
    void OnHandInputReleased(){CancelInvoke(nameof(ShowAllTutorialHands));Invoke(nameof(ShowAllTutorialHands),1);}
    public void ShowAllTutorialHands()=>FadeHands(1);
    void FadeHands(float alpha){foreach(var group in hands)fades.To(group,alpha,.1f);}
    public void HideInGameTopBar()=>topBar.SetActive(false);
    void ShowInGameTopBar()=>topBar.SetActive(true);
    void OnDestroy(){foreach(var binding in buttonBindings)if(binding.Item1!=null)binding.Item1.onClick.RemoveListener(binding.Item2);}
}

// CanvasGroup DOFade(.1).SetEase(OutCubic=9), without DOKill/SetUpdate/Link.
public sealed class OriginalTutorialAlphaTweens : MonoBehaviour {
    sealed class Tween {public CanvasGroup target;public float from,to,age,duration;public bool started;}
    readonly List<Tween> active=new List<Tween>();
    public int ActiveCount=>active.Count;
    public void To(CanvasGroup group,float alpha,float duration){active.Add(new Tween{target=group,to=alpha,duration=duration});}
    public static float OutCubic(float progress){float t=progress-1;return t*t*t+1;}
    void Update() {
        float delta=Time.deltaTime;if(delta<.000001f&&delta>-.000001f)return;
        for(int i=0;i<active.Count;) {
            var tween=active[i];if(tween.target==null){active.RemoveAt(i);continue;}
            if(!tween.started){tween.from=tween.target.alpha;tween.started=true;}
            tween.age+=delta;float progress=Mathf.Clamp01(tween.age/tween.duration);tween.target.alpha=tween.from+(tween.to-tween.from)*OutCubic(progress);
            if(progress>=1)active.RemoveAt(i);else i++;
        }
    }
}

// The two mounted PopupSFXRelayer components are ten-instruction wrappers:
// OnEnable/Disable raise their original AudioObject on UISFX Default (0).
public sealed class OriginalTutorialPopupSFX : MonoBehaviour {
    AudioSource source;
    J record,library;
    bool initialized;
    public void Initialize(AudioSource originalDefault,J originalRecord,J originalLibrary){source=originalDefault;record=originalRecord;library=originalLibrary;initialized=true;if(isActiveAndEnabled)OnEnable();}
    void Play(string field){if(source==null)return;source.Stop();OriginalUIAudio.Play(source,record[field],library);}
    void OnEnable(){if(initialized)Play("open");}
    void OnDisable(){if(initialized)Play("close");}
}
