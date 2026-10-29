using System;
using System.Collections.Generic;
using System.Linq;
using Jianghu;
using TMPro;
using UnityEngine;

// MatchHUD OnEnable/dip/Update, 0x36a0cf0..0x36a1134. This component owns
// its original text/container; the host supplies game-mode/timer state.
public sealed class OriginalMatchHUD : MonoBehaviour {
    TMP_Text matchText;
    GameObject graphicsContainer;
    Func<float> remaining;
    Func<int> gameMode,modeNumber;
    string timerFormat;
    int lastSecond=-1;
    bool timed,initialized;
    public int LastSecond=>lastSecond;
    public static OriginalMatchHUD Attach(Transform hudRoot,J uiSource,Func<float> roundRemaining,Func<int> gameMode,Func<int> modeNumber=null) {
        if(hudRoot==null||roundRemaining==null||gameMode==null)throw new ArgumentNullException("Original MatchHUD host providers missing");
        var nodes=new Dictionary<string,Transform>();
        void Walk(Transform n,string path){nodes[path]=n;foreach(Transform c in n)Walk(c,path+"/"+c.name);}Walk(hudRoot,"CanvasAndCamera");
        var row=uiSource["records"].A.First(r=>r["script"].S=="MatchHUD");var data=row["data"];
        Transform Node(J reference)=>nodes.TryGetValue(reference["path"].S,out var n)?n:null;
        if(!nodes.TryGetValue(row["path"].S,out var host))throw new InvalidOperationException("Original MatchHUD host missing");
        var text=Node(data["matchText"]);var container=Node(data["graphicsContainer"]);
        if(text==null||container==null)throw new InvalidOperationException("Original MatchHUD bindings missing");
        var driver=host.gameObject.AddComponent<OriginalMatchHUD>();driver.matchText=text.GetComponent<TMP_Text>();
        if(driver.matchText==null)throw new InvalidOperationException("Original MatchHUD TMP text missing");
        driver.graphicsContainer=container.gameObject;driver.remaining=roundRemaining;driver.gameMode=gameMode;driver.modeNumber=modeNumber;
        driver.timerFormat=OriginalHUDTextLibrary.Load()["timer_format"]["text"].S;driver.initialized=true;
        if(driver.isActiveAndEnabled)driver.OnEnable();return driver;
    }
    public static bool TimedMode(int mode)=>mode==3||(mode&~1)==4;
    public static int NativeSecond(float value) {
        // Original frintp/fcvtps. Unlike CountdownView this cache does not
        // clamp negatives, and +Infinity is explicitly mapped to int.MinValue.
        if(float.IsNaN(value))return 0;
        if(float.IsPositiveInfinity(value)||value<=int.MinValue)return int.MinValue;
        return value>=int.MaxValue?int.MaxValue:Mathf.CeilToInt(value);
    }
    void OnEnable() {
        if(!initialized)return;
        int mode=gameMode();string text="";
        if(mode==0||mode==1){if(modeNumber==null)throw new InvalidOperationException("Original campaign/survival MatchHUD number provider missing");text=modeNumber().ToString();}
        graphicsContainer.SetActive(text!="");matchText.text=text;lastSecond=-1;timed=TimedMode(gameMode());
    }
    void Update() {
        if(!initialized||!timed)return;
        int second=NativeSecond(remaining());if(second==lastSecond)return;lastSecond=second;
        if(second>0) {
            matchText.text=string.Format(timerFormat,second/60,second%60);
            if(!graphicsContainer.activeSelf)graphicsContainer.SetActive(true);
        } else if(graphicsContainer.activeSelf)graphicsContainer.SetActive(false);
    }
}

internal static class OriginalHUDTextLibrary {
    static J source;
    public static J Load() {
        if(source!=null)return source;var file=Resources.Load<TextAsset>("OriginalHUDTextSource");
        if(file==null)throw new InvalidOperationException("OriginalHUDTextSource missing");return source=J.Parse(file.text);
    }
}
