using System;
using System.Collections;
using System.Collections.Generic;
using Jianghu;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Animations;
using UnityEngine.Playables;

// The six original popup controller types bound to their ten mounted hosts.
// External progression/reward results are supplied through Result; this class
// never fabricates a ranking, reward, new-item count or advertising outcome.
public sealed class OriginalUIPopupDriver : MonoBehaviour {
    public OriginalUIPopupResult Result;
    public string CurrentClip {get;private set;}
    OriginalUIPopups host;
    J data,library,enableEvents;
    string kind,currentKey;
    Animator animator;
    AudioSource audioSource;
    PlayableGraph graph;
    AnimationMixerPlayable mixer;
    readonly Dictionary<string,AnimationClipPlayable> states=new Dictionary<string,AnimationClipPlayable>();
    readonly Dictionary<string,int> slots=new Dictionary<string,int>();
    bool initialized;
    Coroutine sequence;
    readonly List<StarTween> stars=new List<StarTween>();
    sealed class StarTween {public Transform target;public float age;}
    bool rankTween;
    bool rankResultReady;
    OriginalUIPopupResult rankResult;
    float tweenAge,sliderFrom,sliderTarget,counterFrom,counterTarget,currentNormalized,targetNormalized;
    int rankStart,rankEnd,rankRange;
    bool WonRanked=>kind=="WonPopupRanked";
    T Ref<T>(string key)where T:Component=>host.Resolve<T>(data[key]);
    Transform Ref(string key)=>host.Resolve(data[key]);
    void Active(string key,bool value){var n=Ref(key);if(n!=null)n.gameObject.SetActive(value);}
    void Text(string key,string value){var t=Ref<TMP_Text>(key);if(t!=null)t.text=value;}
    void Sound(string key)=>OriginalUIAudio.Play(audioSource,data[key],library);
    void Haptic(float low,float high,float duration)=>host.Haptic(low,high,duration);
    public void Initialize(OriginalUIPopups collection,J record,J source) {
        host=collection;data=record["data"];enableEvents=record["enable_events"];kind=record["script"].S;library=source;
        animator=host.Resolve<Animator>(data["animancer"]["animator"]);audioSource=Ref<AudioSource>("audioSource");
        if(animator==null)throw new InvalidOperationException("Original popup Animator missing: "+record["path"].S);
        graph=PlayableGraph.Create("Original verdict "+name);
        graph.SetTimeUpdateMode(data["animancer"]["animator"]["update_mode"].I==2?DirectorUpdateMode.UnscaledGameTime:DirectorUpdateMode.GameTime);
        mixer=AnimationMixerPlayable.Create(graph,0);var output=AnimationPlayableOutput.Create(graph,"Original verdict",animator);output.SetSourcePlayable(mixer);
        var x2=Ref<Button>("x2Button");if(x2!=null)x2.onClick.AddListener(OnX2Clicked);
        initialized=true;if(isActiveAndEnabled)Begin();
    }
    void Play(string field) {
        string key=data[field]["key"].S;if(key.Length==0)throw new InvalidOperationException("Original popup clip field missing: "+kind+"."+field);
        if(currentKey==key&&graph.IsPlaying())return;
        if(!states.TryGetValue(key,out var state)) {
            var clip=Resources.Load<AnimationClip>(library["clips"][key]["resource"].S);
            if(clip==null)throw new InvalidOperationException("Original popup clip missing: "+key);
            state=AnimationClipPlayable.Create(graph,clip);state.SetApplyFootIK(false);state.SetApplyPlayableIK(false);
            int slot=slots.Count;slots.Add(key,slot);states.Add(key,state);mixer.SetInputCount(slot+1);graph.Connect(state,0,mixer,slot);
        }
        foreach(var slot in slots.Values)mixer.SetInputWeight(slot,0);
        mixer.SetInputWeight(slots[key],1);state.SetSpeed(1);currentKey=key;CurrentClip=state.GetAnimationClip().name;graph.Play();
    }
    void OnEnable(){if(initialized)Begin();}
    void Begin() {
        switch(kind) {
            case "WonPopupRandom":Play("open");Sound("openSFX");Haptic(1,.5f,.1f);break;
            case "LostPopup":Active("header",false);Play("open");Sound("openSFX");Haptic(1,.15f,.1f);break;
            case "WonPopupCampaign":
                RequireResult();foreach(var reference in data["stars"].A){var n=host.Resolve(reference);n.localScale=Vector3.zero;n.gameObject.SetActive(false);}
                Active("winningsContainer",false);Active("newItemsContainer",false);ResetBonusButton();SetNextButtons(true);sequence=StartCoroutine(Campaign());break;
            case "WonPopupSurvival":
                RequireResult();ResetBonusButton();Text("currentWaveText",Result.CurrentWave.ToString());Active("header",false);Active("winnersContainer",false);
                Play("open");Sound("openSFX");Haptic(1,.15f,.1f);ShowReward(false);if(Result.AdsRemoved)AwardBonus();break;
            case "WonPopupRanked":case "LostPopupRanked":
                Active("header",false);if(WonRanked)Active("rankUpPopup",false);Text(WonRanked?"rpGainText":"rpLostText","");
                rankResultReady=false;rankResult=null;if(Result!=null&&Result.HasRankedResult)ReceiveRankedResult(Result);
                BindRank(rankResultReady);sequence=StartCoroutine(Ranked());break;
        }
        // The mounted OpponentForfeited host's following FireEventOnEnable
        // component has these two SetActive calls, remapped to cloned nodes.
        foreach(var call in enableEvents.A){var n=host.Resolve(call["target"]);if(n!=null)n.gameObject.SetActive(call["active"].B);}
    }
    public void ReceiveRankedResult(OriginalUIPopupResult received) {
        if(rankResultReady||received==null)return;
        rankResult=new OriginalUIPopupResult{RPDelta=received.RPDelta,RPBefore=received.RPBefore,RPAfter=received.RPAfter,
            RankIndexBefore=received.RankIndexBefore,RankIndexAfter=received.RankIndexAfter,GlobalRankDelta=received.GlobalRankDelta};
        rankResultReady=true;
    }
    void RequireResult(){if(Result==null)throw new InvalidOperationException(kind+" requires the original progression/wallet result context");}
    void ResetBonusButton(){Active("x2Container",true);var b=Ref<Button>("x2Button");if(b!=null)b.interactable=true;}
    void SetNextButtons(bool active){foreach(var reference in data["nextGameObjects"].A){var n=host.Resolve(reference);if(n!=null)n.gameObject.SetActive(active);}Result?.SetGamepadNextEnabled?.Invoke(active);}
    void ShowReward(bool bonus) {
        if(Result.CalculateReward==null)throw new InvalidOperationException(kind+" requires its original wallet CalculateReward callback");
        Text("goldWonText",string.Format("<sprite index=0> {0:N0}",Result.CalculateReward(bonus)));
    }
    IEnumerator Campaign() {
        SetNextButtons(Result.CampaignProgressForNextButtons<1000);Play("open");Sound("openSFX");Haptic(1,.15f,.1f);
        yield return new WaitForSecondsRealtime(.5f);
        int count=Mathf.Clamp(Result.StarsEarned,0,data["stars"].A.Count);
        for(int i=0;i<count;i++) {
            var star=host.Resolve(data["stars"][i]);star.gameObject.SetActive(true);star.localScale=Vector3.zero;stars.Add(new StarTween{target=star});
            OriginalUIAudio.Play(audioSource,data["starSFX"][i],library);Haptic(.25f,.5f,.05f);yield return new WaitForSecondsRealtime(.15f);
        }
        bool winnings=Result.Coins>0;Active("winningsContainer",winnings);Play("jump");Sound(winnings?"winningsSFX":"noWinningsSFX");Haptic(1,.15f,.1f);
        if(winnings){ShowReward(false);if(Result.AdsRemoved)AwardBonus();}
        yield return new WaitForSecondsRealtime(.15f);
        if(Result.RefreshNewItemsPips==null||Result.GetNewItemsCount==null)throw new InvalidOperationException("Original campaign new-items service callbacks missing");
        Result.RefreshNewItemsPips();if(Result.GetNewItemsCount()>0){Active("newItemsContainer",true);Play("jump");Sound("winningsSFX");Haptic(1,1,.1f);}
    }
    public void OnX2Clicked() {
        if(kind!="WonPopupCampaign"&&kind!="WonPopupSurvival")return;RequireResult();
        var b=Ref<Button>("x2Button");if(b!=null)b.interactable=false;
        if(Result.RequestRewardedVideo==null)throw new InvalidOperationException("Original rewarded-video service callback missing");
        Result.RequestRewardedVideo(AwardBonus,()=>{var button=Ref<Button>("x2Button");if(button!=null)button.interactable=true;});
    }
    void AwardBonus(){if(Result.AwardRewardBonus==null)throw new InvalidOperationException("Original wallet AwardRewardBonus callback missing");Result.AwardRewardBonus();ShowReward(true);Active("x2Container",false);}
    void BindRank(bool received) {
        if(Result==null||Result.GetRankConfig==null)return;
        int index=received?rankResult.RankIndexBefore:Result.CurrentRankIndex;
        var rank=Result.GetRankConfig(index);if(rank==null)return;
        ConfigureRank(index,rank);
        int before=received?rankResult.RPBefore:Result.DisplayRankPoints??Result.RPBefore;
        currentNormalized=rankRange>0?Mathf.Max(0,before-rankStart)/(float)rankRange:1;
        targetNormalized=received&&rankRange>0?(WonRanked?Mathf.Min(rankResult.RPAfter-rankStart,rankRange):Mathf.Max(0,rankResult.RPAfter-rankStart))/(float)rankRange:currentNormalized;
        var slider=Ref<Slider>("rpSlider");if(slider!=null)slider.value=rankRange>0?OriginalUIPopupMath.NudgeFill(currentNormalized):1;
        Text("rankPointsText",rankRange>0?OriginalUIPopupMath.Line(before,rankEnd):OriginalUIPopupMath.RP(received?rankResult.RPAfter:before));
        if(received)Text(WonRanked?"rpGainText":"rpLostText",(WonRanked?"+ ":"- ")+OriginalUIPopupMath.RP(rankResult.RPDelta));
    }
    void ConfigureRank(int index,OriginalUIRankConfig rank) {
        var next=Result.GetRankConfig(index+1);rankStart=rank.RankStartsAt;rankRange=next==null?0:next.RankStartsAt-rankStart;rankEnd=next?.RankStartsAt??0;
        var current=Ref<Image>("currentRankBadge");if(current!=null)current.sprite=rank.SmallBadge;
        var badge=Ref<Image>("nextRankBadge");if(badge!=null){if(next!=null)badge.sprite=next.SmallBadge;badge.gameObject.SetActive(next!=null);}
        if(WonRanked){var full=Ref<Image>("nextRankBadgeFull");if(full!=null){if(next!=null)full.sprite=next.SmallBadgeFilled;full.gameObject.SetActive(false);}}
    }
    IEnumerator Ranked() {
        Play("open");Sound("openSFX");Haptic(1,.15f,.1f);yield return new WaitForSecondsRealtime(1);
        if(!rankResultReady) {
            float deadline=Time.unscaledTime+(float)data["resultTimeout"].N;
            while(!rankResultReady&&Time.unscaledTime<deadline)yield return null;
            if(!rankResultReady){Debug.LogWarning(kind+": no server result within "+data["resultTimeout"].S+"s; showing unmoved bar, reconcile at login.");yield break;}
            BindRank(true);
        }
        bool changed=WonRanked?rankResult.RankIndexAfter>rankResult.RankIndexBefore:rankResult.RankIndexAfter<rankResult.RankIndexBefore;
        if(WonRanked) {
            if(rankRange<=0)yield break;
            if(changed) {
                StartRankTween(1);while(rankTween)yield return null;Play("rankUp");Sound("badgeFillSFX");yield return new WaitForSecondsRealtime(.2f);
                var after=Result?.GetRankConfig?.Invoke(rankResult.RankIndexAfter);
                foreach(var reference in data["bigBadges"].A){var image=host.Resolve<Image>(reference);if(image!=null){image.gameObject.SetActive(false);if(after!=null)image.sprite=after.BigBadge;}}
            } else {StartRankTween(targetNormalized);Sound("sliderUpSFX");Haptic(.25f,.5f,.05f);}
        } else if(changed) {
            if(rankRange>0){StartRankTween(0);while(rankTween)yield return null;}
            Sound("derankSFX");Haptic(1,1,.1f);yield return new WaitForSecondsRealtime(.2f);
            var after=Result?.GetRankConfig?.Invoke(rankResult.RankIndexAfter);if(after==null)yield break;
            ConfigureRank(rankResult.RankIndexAfter,after);currentNormalized=rankRange>0?Mathf.Max(0,rankResult.RPAfter-rankStart)/(float)rankRange:1;
            var slider=Ref<Slider>("rpSlider");if(slider!=null)slider.value=rankRange>0?OriginalUIPopupMath.NudgeFill(currentNormalized):1;
            Text("rankPointsText",rankRange>0?OriginalUIPopupMath.Line(rankResult.RPAfter,rankEnd):OriginalUIPopupMath.RP(rankResult.RPAfter));
            // The original rankDown field is not played by this native path.
        } else if(rankRange>0){StartRankTween(targetNormalized);Haptic(.25f,.5f,.05f);}
    }
    void StartRankTween(float target) {
        var slider=Ref<Slider>("rpSlider");if(slider==null)return;
        if(!WonRanked)Sound(currentNormalized<target?"sliderUpSFX":"sliderDownSFX");
        sliderFrom=slider.value;sliderTarget=OriginalUIPopupMath.VisualTarget(currentNormalized,target,sliderFrom,WonRanked);
        counterFrom=currentNormalized;counterTarget=target;currentNormalized=target;tweenAge=0;rankTween=true;
    }
    void Update() {
        float dt=Time.unscaledDeltaTime;
        for(int i=0;i<stars.Count;) {
            var star=stars[i];star.age+=dt;float t=Mathf.Clamp01(star.age/.05f)-1;float value=1+2.70158f*t*t*t+1.70158f*t*t;
            if(star.target!=null)star.target.localScale=Vector3.one*value;if(t>=0)stars.RemoveAt(i);else i++;
        }
        if(rankTween) {
            tweenAge+=dt;float t=Mathf.Clamp01(tweenAge/.8f),ease=1-Mathf.Pow(1-t,3);
            var slider=Ref<Slider>("rpSlider");if(slider!=null)slider.value=Mathf.LerpUnclamped(sliderFrom,sliderTarget,ease);
            float counter=Mathf.LerpUnclamped(counterFrom,counterTarget,ease);
            Text("rankPointsText",OriginalUIPopupMath.Line(Mathf.RoundToInt(rankRange*counter)+rankStart,rankEnd));if(t>=1)rankTween=false;
        }
    }
    public void PlayWinningsSFX(){Sound("winningsSFX");if(kind=="LostPopup")Haptic(1,.15f,.1f);}
    public void PlayHeaderSFX()=>Sound("headerSFX");
    public void PlayBadgeFillHaptic()=>Haptic(1,.5f,.1f);
    public void PlayRankUpHaptic()=>Haptic(1,2,2.5f);
    public void PlayNewItemsInShopHaptic()=>Haptic(1,.5f,.1f);
    void OnDisable() {
        if(!initialized)return;if(sequence!=null)StopCoroutine(sequence);rankTween=false;stars.Clear();if(WonRanked)Active("rankUpPopup",false);
        // Mounted Animancer DisableAction is Stop (0), verified in its DLL.
        foreach(var pair in states){pair.Value.SetTime(0);pair.Value.SetSpeed(0);mixer.SetInputWeight(slots[pair.Key],0);}currentKey=null;
        if(graph.IsValid())graph.Stop();
    }
    void OnDestroy(){if(graph.IsValid())graph.Destroy();}
}
