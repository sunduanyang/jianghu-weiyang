using System;
using System.Collections;
using UnityEngine;

public sealed partial class OriginalPreview {
    void InitializeDesktopPresentation(string[] args) {
        if(Array.IndexOf(args,"-playtestFullscreen")>=0)SetDesktopFullscreen(true);
    }
    void HandleDesktopWindowKeys() {
        if(Input.GetKeyDown(KeyCode.F11))SetDesktopFullscreen(Screen.fullScreenMode==FullScreenMode.Windowed);
    }
    public void SetDesktopFullscreen(bool fullscreen) {
        int nativeWidth=Display.main.systemWidth,nativeHeight=Display.main.systemHeight;
        if(nativeWidth<=0||nativeHeight<=0){nativeWidth=Screen.currentResolution.width;nativeHeight=Screen.currentResolution.height;}
        if(nativeWidth<=0||nativeHeight<=0){nativeWidth=1920;nativeHeight=1080;}
        int width=fullscreen?nativeWidth:Mathf.RoundToInt(nativeWidth*.9f);
        int height=fullscreen?nativeHeight:Mathf.RoundToInt(nativeHeight*.9f);
        Screen.SetResolution(width,height,fullscreen?FullScreenMode.FullScreenWindow:FullScreenMode.Windowed);
        StartCoroutine(LogDesktopPresentation());
    }
    IEnumerator LogDesktopPresentation() {
        yield return null;yield return null;yield return null;
        Debug.Log("Playtest display: "+Screen.fullScreenMode+" "+Screen.width+"x"+Screen.height);
    }
}
