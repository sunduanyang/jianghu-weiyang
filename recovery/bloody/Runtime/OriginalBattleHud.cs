using System.Collections.Generic;
using Jianghu;
using UnityEngine;
using UnityEngine.UI;

// Original UGUI hierarchy, textures, fonts, gradients and native bindings.
// The local host supplies the original round-state domain without Fusion RPCs.
public sealed class OriginalBattleHud : MonoBehaviour {
    public OriginalPreview preview;
    J source;
    Transform main;
    readonly Dictionary<string,Transform> nodes=new Dictionary<string,Transform>();
    int phase=-1;
    bool paused;
    OriginalUIFlow flow;
    OriginalVerdictText verdictText;
    OriginalTransitionView transitionView;
    void Awake() {
        preview.EnsureOriginalContext();
        source=J.Parse(Resources.Load<TextAsset>("OriginalUISource").text);
        void Walk(Transform t,string path){nodes[path]=t;foreach(Transform c in t)Walk(c,path+"/"+c.name);}
        Walk(transform,"CanvasAndCamera");main=Node("CanvasAndCamera/Canvas/MainContainer");
        foreach(var button in GetComponentsInChildren<Button>(true)) {
            button.onClick=new Button.ButtonClickedEvent();
            switch(button.name) {
                case "Button_Pause":case "PauseButton":button.onClick.AddListener(()=>preview.SetPaused(true));break;
                case "Button_Unpause":button.onClick.AddListener(()=>preview.SetPaused(false));break;
                case "Button_NextRound":button.onClick.AddListener(preview.AdvanceOriginalRound);break;
                case "Button_Rematch":case "Button_Restart":button.onClick.AddListener(preview.RestartOriginalMatch);break;
                case "Button_Next":button.onClick.AddListener(preview.NextOriginalMatch);break;
                case "Button_Shop":
                    button.onClick.AddListener(preview.OpenPlaytestSetup);
                    var shopLabel=button.GetComponentInChildren<TMPro.TMP_Text>(true);
                    if(shopLabel!=null)shopLabel.text="Loadout";
                    break;
                case "Button_Quit":case "Button_Back":button.onClick.AddListener(Application.Quit);break;
            }
        }
        var canvas=Node("CanvasAndCamera/Canvas");
        if(canvas!=null)foreach(Transform child in canvas)if(child!=main)child.gameObject.SetActive(false);
        foreach(Transform child in main)child.gameObject.SetActive(false);
        main.gameObject.SetActive(true);
        flow=new OriginalUIFlow(transform,source,preview.RelayOriginalTimeScale);
        OriginalWalletView.Attach(transform,source,preview.Progress);
        OriginalCountdownView.Attach(transform,source,()=>preview.CountdownRemaining);
        OriginalMatchHUD.Attach(transform,source,()=>preview.RoundTimeRemaining,()=>preview.GameMode,()=>preview.ModeNumber);
        OriginalCharacterHUD.Attach(transform,source,slot=>slot==0?preview.player:preview.enemy,slot=>slot==0?preview.WinsPlayer:preview.WinsEnemy,()=>preview.RoundsToWin,gameMode:()=>preview.GameMode);
        verdictText=new OriginalVerdictText(transform,source,"en");
        preview.NativeUiTimeEvents=true;
        // Tutorial/AFK panels are independently enabled by their original
        // controllers. A regular local duel starts without those overlays.
        Active("CanvasAndCamera/Canvas/MainContainer/Round/PracticeController",preview.GameMode==2);
        Active("CanvasAndCamera/Canvas/MainContainer/Round/AfkOverlay",false);
        OriginalTutorialController.Attach(transform,preview.TutorialContext);
    }
    Transform Node(string path)=>nodes.TryGetValue(path,out var n)?n:null;
    void Active(string path,bool value){var n=Node(path);if(n!=null)n.gameObject.SetActive(value);}
    public void OnOriginalRunnerDestroyed(bool retrying) {
        if(retrying)return;
        flow.OnOriginalRunnerDestroyed(false);phase=0;
    }
    public void SetOriginalArenaControls(bool arena)=>Active("CanvasAndCamera/Canvas/MainContainer/PlayerControls",arena);
    public void AttachOriginalTransition(OriginalArenaTransitionSequence sequence) {
        if(transitionView!=null)return;
        transitionView=OriginalTransitionView.Attach(transform,sequence);
    }
    public bool OriginalTransitionBlocksPointerInput {
        get {var node=Node("CanvasAndCamera/Canvas/MainContainer/TransitionView");var image=node==null?null:node.GetComponent<Image>();return image!=null&&image.isActiveAndEnabled&&image.raycastTarget;}
    }
    void LateUpdate() {
        if(preview!=null)ApplyOriginalState(preview.OriginalState,preview.Paused);
    }
    public void ApplyOriginalState(int state,bool isPaused) {
        if(preview==null||preview.player==null||state<2||state>6)return;
        if(phase!=state||paused!=isPaused)SetScreen(state,isPaused);
    }
    void SetScreen(int value,bool isPaused) {
        bool enteringVerdict=phase!=value&&(value==5||value==6);
        phase=value;paused=isPaused;
        if(main==null)return;
        Active("CanvasAndCamera/Canvas/MainContainer/Round/PracticeController",preview.GameMode==2);
        if(enteringVerdict) {
            // Native view selects children while their container is inactive;
            // its original parent clip reveals them after 2.5 seconds.
            flow.ActivateVerdict(value==6,preview.LastWinnerIsPlayer,preview.GameMode,result:preview.CampaignPopupResult,matchEndReason:(int)preview.EndReason,prepareText:()=>verdictText.Apply(value==6,preview.LastWinnerIsPlayer?preview.PlayerDisplayName:preview.EnemyDisplayName,preview.GameMode,(int)preview.EndReason));
        }
        flow.SetState(value,isPaused);
        Active("CanvasAndCamera/Canvas/MainContainer/PlayerControls",value==4&&!isPaused);
    }
    void OnDestroy(){flow?.Dispose();}
}
