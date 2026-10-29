using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Jianghu;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// External ownership domain. Friendly supplies its actual two local rigs and
// score/rules. Network registration, ranks and profile URLs require their own
// authoritative context; this class does not manufacture that data.
public sealed class OriginalCharacterHUDContext {
    public Func<int,OriginalRig> Character;
    public Func<int,int> Wins;
    public Func<int> RoundsToWin;
    public Func<int> LocalPlayerSlot=()=>0;
    public Func<int> GameMode=()=>5;
    public Func<bool> RoundControllerReady=()=>true;
    public Func<OriginalRig,string> ProfileUrl;
    public Func<OriginalRig,Sprite> RankedBadge;
    // Local equivalent of CharacterVitality.Render's per-render change
    // detector. A restored network runner can instead dispatch Notify*.
    public bool PollLocalRenderChanges=true;
}

// CharacterHUD 0x3430f94..0x3432898. Original mounted components, gradients,
// cached targets, activation, Bind/Reset and the one-shot pip coroutine.
public sealed class OriginalCharacterHUD : MonoBehaviour {
    OriginalCharacterHUDContext context;
    OriginalHUDAnchorTweens tweens;
    RectTransform healthBar,staminaBar;
    Image healthFill,healthEmpty,rankedImage;
    Gradient fillGradient,emptyGradient;
    GameObject staminaFlash,rankedBadge;
    Slider maximum;
    TMP_Text nameText;
    Button nameButton;
    Animation nameUrlAnimation;
    CanvasGroup canvasGroup;
    GameObject[] pips;
    OriginalRig bound;
    Coroutine pipRoutine;
    string profileUrl,seenName,seenUrl;
    float cachedHealth=-1,cachedStamina=-1,seenHealth,seenStamina,seenMaximum;
    int screenSlot,boundSlot;
    bool initialized;
    public int ScreenSlot=>screenSlot;
    public int BoundPlayerSlot=>boundSlot;
    public OriginalRig BoundCharacter=>bound;
    public float CachedHealth=>cachedHealth;
    public float CachedStamina=>cachedStamina;

    public static IReadOnlyList<OriginalCharacterHUD> Attach(Transform root,J uiSource,Func<int,OriginalRig> characters,Func<int,int> wins,Func<int> roundsToWin,Func<int> localPlayerSlot=null,Func<int> gameMode=null) {
        var domain=new OriginalCharacterHUDContext{Character=characters,Wins=wins,RoundsToWin=roundsToWin};
        if(localPlayerSlot!=null)domain.LocalPlayerSlot=localPlayerSlot;if(gameMode!=null)domain.GameMode=gameMode;
        return Attach(root,uiSource,domain);
    }
    public static IReadOnlyList<OriginalCharacterHUD> Attach(Transform root,J uiSource,OriginalCharacterHUDContext domain) {
        if(root==null||uiSource==null||domain==null||domain.Character==null||domain.Wins==null||domain.RoundsToWin==null)throw new ArgumentNullException("Original CharacterHUD context missing");
        var nodes=new Dictionary<string,Transform>();
        void Walk(Transform node,string path){nodes[path]=node;foreach(Transform child in node)Walk(child,path+"/"+child.name);}Walk(root,"CanvasAndCamera");
        Transform Node(J reference)=>nodes.TryGetValue(reference["path"].S,out var node)?node:null;
        T Ref<T>(J reference)where T:Component {var node=Node(reference);return node==null?null:node.GetComponent<T>();}
        T Required<T>(J reference)where T:Component {var component=Ref<T>(reference);if(component==null)throw new InvalidOperationException("Original CharacterHUD binding missing: "+reference["path"].S);return component;}
        GameObject Object(J reference){var node=Node(reference);return node==null?null:node.gameObject;}
        var scheduler=root.GetComponent<OriginalHUDAnchorTweens>()??root.gameObject.AddComponent<OriginalHUDAnchorTweens>();
        var result=new List<OriginalCharacterHUD>();
        foreach(var row in uiSource["records"].A.Where(r=>r["script"].S=="CharacterHUD")) {
            if(!nodes.TryGetValue(row["path"].S,out var node))throw new InvalidOperationException("Original CharacterHUD host missing");
            if(node.GetComponent<OriginalCharacterHUD>()!=null)throw new InvalidOperationException("Original CharacterHUD already attached");
            J d=row["data"];var driver=node.gameObject.AddComponent<OriginalCharacterHUD>();driver.context=domain;driver.tweens=scheduler;driver.screenSlot=d["screenSlot"].I;
            driver.healthBar=Required<RectTransform>(d["healthBarFillTransform"]);driver.staminaBar=Required<RectTransform>(d["staminaBarFillTransform"]);
            driver.healthFill=Required<Image>(d["healthBarFillRenderer"]);driver.healthEmpty=Required<Image>(d["healthBarEmptyRenderer"]);
            driver.fillGradient=OriginalVisualCatalog.Gradient(d["healthBarFillGradient"]);driver.emptyGradient=OriginalVisualCatalog.Gradient(d["healthBarEmptyGradient"]);
            driver.staminaFlash=Object(d["staminaEmptyFlashTransform"]);driver.maximum=Ref<Slider>(d["maxStaminaSlider"]);driver.nameText=Ref<TMP_Text>(d["nameText"]);
            driver.nameButton=Ref<Button>(d["nameButton"]);driver.nameUrlAnimation=Ref<Animation>(d["nameUrlAnimation"]);
            driver.rankedBadge=Object(d["rankedBadge"]);driver.rankedImage=Ref<Image>(d["rankedBadgeImage"]);driver.canvasGroup=Required<CanvasGroup>(d["canvasGroup"]);
            driver.pips=d["roundPips"].A.Select(Object).ToArray();
            if(driver.staminaFlash==null||driver.rankedBadge==null||driver.pips.Any(p=>p==null))throw new InvalidOperationException("Original CharacterHUD required object missing");
            driver.initialized=true;result.Add(driver);if(driver.isActiveAndEnabled)driver.OnEnable();
        }
        return result;
    }
    public static int NativePlayerSlot(int screen,int localPlayerSlot)=>localPlayerSlot==1?1-screen:screen;
    public static float NativeAlpha(int screen,int mode)=>screen==1&&mode==2?0:1;
    public static bool NativePipVisible(int index,int roundsToWin)=>index<roundsToWin;
    public static bool NativePipWon(int index,int wins)=>index<unchecked((sbyte)wins);
    public static bool HasOriginalProfileUrl(string url)=>!string.IsNullOrWhiteSpace(url)&&(url.StartsWith("https://",StringComparison.OrdinalIgnoreCase)||url.StartsWith("http://",StringComparison.OrdinalIgnoreCase));
    void OnEnable() {
        if(!initialized)return;
        canvasGroup.alpha=NativeAlpha(screenSlot,context.GameMode());canvasGroup.blocksRaycasts=canvasGroup.alpha>0;
        if(nameButton!=null)nameButton.onClick.AddListener(OnNameClicked);
        RefreshBinding();
    }
    void OnDisable() {
        if(!initialized)return;
        if(nameButton!=null)nameButton.onClick.RemoveListener(OnNameClicked);
        ResetBinding();
    }
    public void RefreshBinding() {
        if(!initialized||!isActiveAndEnabled)return;
        int slot=NativePlayerSlot(screenSlot,context.LocalPlayerSlot());var character=context.Character(slot);
        if(ReferenceEquals(bound,character)&&boundSlot==slot)return;
        ResetBinding();boundSlot=slot;if(character==null)return;
        bound=character;
        if(maximum!=null)maximum.value=bound.staminaMax/100;
        cachedHealth=cachedStamina=-1;
        seenHealth=bound.health;seenStamina=bound.stamina;seenMaximum=bound.staminaMax;
        RefreshHealth();RefreshStamina();RefreshName();RestartPips();RefreshRank();
    }
    void ResetBinding() {
        if(pipRoutine!=null){StopCoroutine(pipRoutine);pipRoutine=null;}
        profileUrl=null;if(nameButton!=null)nameButton.gameObject.SetActive(false);bound=null;
        // cks does not reset the cached ratios or kill target tweens. Bind
        // resets both to -1, and each first refresh performs DOKill + snap.
    }
    void Update() {
        if(!initialized)return;
        RefreshBinding();if(bound==null||!context.PollLocalRenderChanges)return;
        // CharacterVitality.Render compares the final rendered network state,
        // then invokes only fields that changed. Never retry an old health
        // event every frame merely because slow motion deferred its tween.
        if(BitConverter.SingleToInt32Bits(bound.health)!=BitConverter.SingleToInt32Bits(seenHealth)){seenHealth=bound.health;RefreshHealth();}
        if(BitConverter.SingleToInt32Bits(bound.stamina)!=BitConverter.SingleToInt32Bits(seenStamina)){seenStamina=bound.stamina;RefreshStamina();}
        string url=context.ProfileUrl==null?null:context.ProfileUrl(bound);
        if(seenName!=bound.OriginalCharacterName||seenUrl!=url||BitConverter.SingleToInt32Bits(seenMaximum)!=BitConverter.SingleToInt32Bits(bound.staminaMax))NotifyConfigApplied(bound);
    }
    public void NotifyHealthChanged(){if(initialized&&isActiveAndEnabled&&bound!=null){seenHealth=bound.health;RefreshHealth();}}
    public void NotifyStaminaChanged(){if(initialized&&isActiveAndEnabled&&bound!=null){seenStamina=bound.stamina;RefreshStamina();}}
    public void NotifyConfigApplied(OriginalRig character) {
        if(!initialized||!isActiveAndEnabled||bound==null||character!=bound)return;
        RefreshName();RefreshRank();seenMaximum=bound.staminaMax;if(maximum!=null)maximum.value=bound.staminaMax/100;
    }
    void RefreshHealth() {
        if(bound==null)return;
        var update=OriginalHUDBarUpdate.Decide(bound.health,bound.healthMax,cachedHealth,Time.timeScale);
        Apply(healthBar,update);cachedHealth=update.cached;
        healthFill.color=fillGradient.Evaluate(update.normalized);healthEmpty.color=emptyGradient.Evaluate(update.normalized);
    }
    void RefreshStamina() {
        if(bound==null)return;
        var update=OriginalHUDBarUpdate.Decide(bound.stamina,100,cachedStamina,Time.timeScale);
        Apply(staminaBar,update);cachedStamina=update.cached;staminaFlash.SetActive(bound.stamina<=0);
    }
    void Apply(RectTransform rect,OriginalHUDBarUpdate update) {
        if(update.action==OriginalHUDBarAction.Snap){tweens.Kill(rect);rect.anchorMax=new Vector2(update.normalized,1);}
        else if(update.action==OriginalHUDBarAction.Tween)tweens.To(rect,new Vector2(update.normalized,1),.35f);
    }
    void RefreshName() {
        seenName=bound.OriginalCharacterName;seenUrl=context.ProfileUrl==null?null:context.ProfileUrl(bound);
        if(nameText==null)return;
        nameText.text=seenName;profileUrl=seenUrl;bool valid=HasOriginalProfileUrl(profileUrl);
        if(nameButton!=null)nameButton.gameObject.SetActive(valid);
        if(valid){nameText.fontStyle|=FontStyles.Underline;if(nameUrlAnimation!=null)nameUrlAnimation.Play();}
        else nameText.fontStyle&=~FontStyles.Underline;
    }
    void RefreshRank() {
        int mode=context.GameMode();
        if(mode==4&&context.RankedBadge==null)throw new InvalidOperationException("Original ranked CharacterHUD needs authoritative rank context");
        rankedBadge.SetActive(mode==4);if(mode!=4||bound==null)return;
        var sprite=context.RankedBadge(bound);if(sprite!=null&&rankedImage!=null)rankedImage.sprite=sprite;
    }
    void OnNameClicked(){if(HasOriginalProfileUrl(profileUrl))Application.OpenURL(profileUrl);}
    void RestartPips(){if(pipRoutine!=null)StopCoroutine(pipRoutine);pipRoutine=StartCoroutine(SetPipsWhenReady());}
    IEnumerator SetPipsWhenReady() {
        yield return new WaitUntil(()=>context.RoundControllerReady());
        for(int index=0;index<pips.Length;index++) {
            pips[index].SetActive(NativePipVisible(index,context.RoundsToWin()));
            pips[index].transform.GetChild(0).gameObject.SetActive(NativePipWon(index,context.Wins(NativePlayerSlot(screenSlot,context.LocalPlayerSlot()))));
        }
        pipRoutine=null;
    }
}

public enum OriginalHUDBarAction { None=0,Snap=1,Tween=2 }
public struct OriginalHUDBarUpdate {
    public OriginalHUDBarAction action;
    public float normalized,cached;
    public static OriginalHUDBarUpdate Decide(float value,float maximum,float previous,float timeScale) {
        float normalized=value/maximum;var update=new OriginalHUDBarUpdate{normalized=normalized,cached=previous};
        if(Mathf.Approximately(previous,normalized))return update;
        if(previous<0)update.action=OriginalHUDBarAction.Snap;
        else if(timeScale==1)update.action=OriginalHUDBarAction.Tween;
        else return update;
        update.cached=normalized;return update;
    }
}

// Source DOAnchorMax has no preceding DOKill, SetLink, SetUpdate or SetEase.
// Keep overlapping tweens, source default OutQuad and scaled time. The root
// scheduler remains active while a CharacterHUD target is disabled.
public sealed class OriginalHUDAnchorTweens : MonoBehaviour {
    sealed class Tween {public RectTransform target;public Vector2 start,end;public float age,duration;public bool started;}
    readonly List<Tween> active=new List<Tween>();
    public int ActiveCount=>active.Count;
    public void Kill(RectTransform target){active.RemoveAll(t=>t.target==target);}
    public void To(RectTransform target,Vector2 end,float duration){active.Add(new Tween{target=target,end=end,duration=duration});}
    public static float OutQuad(float progress)=>-progress*(progress-2);
    void Update() {
        // TweenManager.Update 0x3711b54 skips a scaled step strictly between
        // -1e-6 and +1e-6, including paused time, before tween startup.
        float delta=Time.deltaTime;if(delta<.000001f&&delta>-.000001f)return;
        for(int index=0;index<active.Count;) {
            var tween=active[index];if(tween.target==null){active.RemoveAt(index);continue;}
            // DOTween captures the getter at startup, not when To is called.
            if(!tween.started){tween.start=tween.target.anchorMax;tween.started=true;}
            tween.age+=delta;float progress=Mathf.Clamp01(tween.age/tween.duration);
            tween.target.anchorMax=Vector2.LerpUnclamped(tween.start,tween.end,OutQuad(progress));
            if(progress>=1)active.RemoveAt(index);else index++;
        }
    }
}
