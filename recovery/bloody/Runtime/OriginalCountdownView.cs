using System;
using System.Collections.Generic;
using System.Linq;
using Jianghu;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// CountdownView Awake/OnEnable/Update, 0x368c2bc..0x368c770. The host
// provides the original CountdownTimeRemaining; the parent's source Animator
// controls screen Enter/Exit, including Fight's short visible exit window.
public sealed class OriginalCountdownView : MonoBehaviour {
    TMP_Text countdownText,fightText;
    Func<float> remaining;
    OriginalCountdownTweens tweens;
    AudioSource audioSourceDefault;
    J data,library;
    Vector2 initialPosition;
    int lastSecond=-1;
    bool initialized;
    public int LastSecond=>lastSecond;
    public static OriginalCountdownView Attach(Transform hudRoot,J uiSource,Func<float> countdownRemaining) {
        if(hudRoot==null)throw new ArgumentNullException(nameof(hudRoot));
        if(countdownRemaining==null)throw new ArgumentNullException(nameof(countdownRemaining));
        var resource=Resources.Load<TextAsset>("OriginalCountdownSource");
        if(resource==null)throw new InvalidOperationException("OriginalCountdownSource missing");
        J library=J.Parse(resource.text),record=uiSource["records"].A.First(r=>r["script"].S=="CountdownView");
        var nodes=new Dictionary<string,Transform>();
        void Walk(Transform n,string path){nodes[path]=n;foreach(Transform c in n)Walk(c,path+"/"+c.name);}Walk(hudRoot,"CanvasAndCamera");
        Transform Node(J reference)=>nodes.TryGetValue(reference["path"].S,out var n)?n:null;
        var countdown=Node(record["data"]["countdownText"]);var fight=Node(record["data"]["fightText"]);
        if(countdown==null||fight==null||!nodes.TryGetValue(record["path"].S,out var host))throw new InvalidOperationException("Original CountdownView scene references missing");
        J sourceRef=library["records"]["UISFX"]["data"]["audioSourceDefault"];
        var audioNode=Node(sourceRef);var sources=audioNode==null?Array.Empty<AudioSource>():audioNode.GetComponents<AudioSource>();
        if(sourceRef["ordinal"].I>=sources.Length)throw new InvalidOperationException("Original UISFX default AudioSource missing");
        var scheduler=hudRoot.gameObject.GetComponent<OriginalCountdownTweens>();if(scheduler==null)scheduler=hudRoot.gameObject.AddComponent<OriginalCountdownTweens>();
        var driver=host.gameObject.AddComponent<OriginalCountdownView>();
        driver.countdownText=countdown.GetComponent<TMP_Text>();driver.fightText=fight.GetComponent<TMP_Text>();
        if(driver.countdownText==null||driver.fightText==null)throw new InvalidOperationException("Original CountdownView TMP components missing");
        driver.library=library;driver.data=library["records"]["CountdownView"]["data"];driver.remaining=countdownRemaining;
        driver.tweens=scheduler;driver.audioSourceDefault=sources[sourceRef["ordinal"].I];
        // Awake caches countdown local XY once; source code restores Z to zero.
        driver.initialPosition=driver.countdownText.transform.localPosition;driver.initialized=true;
        if(driver.isActiveAndEnabled)driver.OnEnable();return driver;
    }
    void OnEnable() {
        if(!initialized)return;
        countdownText.gameObject.SetActive(false);fightText.gameObject.SetActive(false);lastSecond=-1;
    }
    void Play(string field) {
        // UISFX.dov always stops Default before selecting the requested
        // source. Countdown raises UIAudioSourceType.Default (0).
        audioSourceDefault.Stop();OriginalUIAudio.Play(audioSourceDefault,data[field],library);
    }
    void Update() {
        if(!initialized)return;
        int second=NativeSecond(remaining());if(second==lastSecond)return;lastSecond=second;
        if(second==0) {
            countdownText.gameObject.SetActive(false);fightText.gameObject.SetActive(true);fightText.alpha=1;
            fightText.transform.localPosition=new Vector3(initialPosition.x,initialPosition.y,0);
            tweens.MoveY(fightText.transform,50,1,true);Play("countdownFightSFX");
        } else {
            countdownText.gameObject.SetActive(true);fightText.gameObject.SetActive(false);countdownText.text=second.ToString();countdownText.alpha=1;
            tweens.Fade(countdownText,0,.5f);
            countdownText.transform.localPosition=new Vector3(initialPosition.x,initialPosition.y,0);
            tweens.MoveY(countdownText.transform,-25,.5f,false);Play("countdownSecondSFX");
        }
    }
    public static int NativeSecond(float value) {
        // ARM64 frintp/fcvtps, followed by negative-bit clearing. Mathf's
        // native ceil conversion saturates positive overflow; +Infinity is
        // explicitly selected to zero by the original instruction sequence.
        if(float.IsPositiveInfinity(value)||float.IsNaN(value)||value<=0)return 0;
        return value>=int.MaxValue?int.MaxValue:Mathf.CeilToInt(value);
    }
}

// Original DOTween uses independent updates and no link/kill on Countdown
// disable. Its tweens therefore continue even after the parent has exited.
public sealed class OriginalCountdownTweens : MonoBehaviour {
    sealed class Tween {
        public Graphic graphic;public Transform transform;public float age,duration,start,end;public bool outCirc;
    }
    readonly List<Tween> active=new List<Tween>();
    public void Fade(Graphic graphic,float end,float duration)=>active.Add(new Tween{graphic=graphic,start=graphic.color.a,end=end,duration=duration});
    public void MoveY(Transform transform,float relative,float duration,bool outCirc)=>active.Add(new Tween{transform=transform,start=transform.localPosition.y,end=transform.localPosition.y+relative,duration=duration,outCirc=outCirc});
    public static float Ease(float t,bool outCirc)=>outCirc?Mathf.Sqrt(1-(t-1)*(t-1)):1-Mathf.Sqrt(1-t*t);
    void Update() {
        for(int i=0;i<active.Count;) {
            var tween=active[i];tween.age+=Time.unscaledDeltaTime;float t=Mathf.Clamp01(tween.age/tween.duration);
            float value=Mathf.LerpUnclamped(tween.start,tween.end,Ease(t,tween.outCirc));
            if(tween.graphic!=null){var color=tween.graphic.color;color.a=value;tween.graphic.color=color;}
            if(tween.transform!=null){var position=tween.transform.localPosition;position.y=value;tween.transform.localPosition=position;}
            if(t>=1)active.RemoveAt(i);else i++;
        }
    }
}
