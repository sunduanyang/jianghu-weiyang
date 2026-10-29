using System;
using System.Collections.Generic;
using System.Linq;
using Jianghu;
using UnityEngine;

// CanvasAndCamera.ccs/ccx/ccy and paused cct/ccz/cda, 0x3415ac8..0x3416da8.
// The host supplies state changes; the original controllers, curves and
// AnimationEvents remain responsible for their own animation and timing.
public sealed class OriginalUIFlow : IDisposable {
    sealed class Pair {public GameObject[] screens;public OriginalUITransitionAnimator transition;}
    readonly Dictionary<string,Transform> nodes=new Dictionary<string,Transform>();
    readonly Dictionary<string,OriginalUITransitionAnimator> transitions=new Dictionary<string,OriginalUITransitionAnimator>();
    readonly Dictionary<int,Pair> network=new Dictionary<int,Pair>();
    readonly List<OriginalUITransitionAnimator> owned=new List<OriginalUITransitionAnimator>();
    readonly List<GameObject> screens=new List<GameObject>();
    Pair pausedPair;
    bool disposed;
    public int State {get;private set;}=-1;
    public bool Paused {get;private set;}
    public OriginalUIPopups Popups {get;private set;}
    public IReadOnlyList<string> ActiveScreenPaths=>nodes.Where(p=>screens.Contains(p.Value.gameObject)&&p.Value.gameObject.activeInHierarchy).Select(p=>p.Key).ToList();
    public OriginalUIFlow(Transform root,J uiSource,Action<float> relayTimeScale) {
        if(root==null)throw new ArgumentNullException(nameof(root));
        if(relayTimeScale==null)throw new ArgumentNullException(nameof(relayTimeScale));
        var asset=Resources.Load<TextAsset>("OriginalUIFlowSource");
        if(asset==null)throw new InvalidOperationException("OriginalUIFlowSource manifest missing");
        J flow=J.Parse(asset.text),canvas=uiSource["records"].A.First(r=>r["script"].S=="CanvasAndCamera")["data"];
        void Walk(Transform n,string path){nodes[path]=n;foreach(Transform c in n)Walk(c,path+"/"+c.name);}
        Walk(root,"CanvasAndCamera");
        // The standalone host enters directly into a match. Remove source
        // menu screens from this bootstrap without disabling exiting battle
        // screens on each later state change.
        foreach(var pairs in new[]{canvas["localPairs"].A,canvas["networkedPairs"].A})foreach(var pair in pairs)foreach(var reference in pair["uiScreens"].A) {
            var n=Node(reference["path"].S);if(n!=null)n.gameObject.SetActive(false);
        }
        foreach(var row in flow["animators"].A) {
            var n=Required(row["path"].S);var animator=n.GetComponent<Animator>();
            if(animator==null)throw new InvalidOperationException("Original UI Animator missing: "+row["path"].S);
            if(row["controller"].Has) {
                string controller=flow["assets"][row["controller"].S]["name"].S;
                if(animator.runtimeAnimatorController==null||animator.runtimeAnimatorController.name!=controller)
                    throw new InvalidOperationException("Original UI controller missing or mismatched: "+row["path"].S+" expected "+controller);
            }
            animator.updateMode=(AnimatorUpdateMode)row["update_mode"].I;
            animator.cullingMode=(AnimatorCullingMode)row["culling_mode"].I;
            animator.enabled=row["enabled"].B;
        }
        foreach(var row in flow["legacy_animations"].A) {
            var animation=Required(row["path"].S).GetComponent<Animation>();
            if(animation==null)throw new InvalidOperationException("Original legacy UI Animation missing: "+row["path"].S);
            animation.playAutomatically=row["play_automatically"].B;animation.animatePhysics=row["animate_physics"].B;
            animation.enabled=row["enabled"].B;
        }
        var relayPaths=new HashSet<string>(flow["time_relays"].A.Where(r=>r["enabled"].B).Select(r=>r["path"].S));
        foreach(var row in flow["transitions"].A) {
            var host=Required(row["path"].S);var driver=host.gameObject.AddComponent<OriginalUITransitionAnimator>();
            driver.Initialize(Required(row["root_path"].S).gameObject,Required(row["animator_path"].S).GetComponent<Animator>(),
                relayPaths.Contains(row["path"].S)?relayTimeScale:null);
            driver.enabled=row["enabled"].B;transitions.Add(row["path"].S,driver);owned.Add(driver);
        }
        foreach(string relay in relayPaths)if(!transitions.ContainsKey(relay))throw new InvalidOperationException("Original UI time-event host lacks recovered transition: "+relay);
        foreach(var pair in canvas["networkedPairs"].A.Where(p=>p["state"].I>=2&&p["state"].I<=6))network.Add(pair["state"].I,ReadPair(pair));
        pausedPair=ReadPair(canvas["localPairs"].A.First(p=>p["state"].I==10));
        Popups=new OriginalUIPopups(root,uiSource);
    }
    Transform Node(string path)=>path!=null&&nodes.TryGetValue(path,out var n)?n:null;
    Transform Required(string path)=>Node(path)??throw new InvalidOperationException("Original UI source node missing: "+path);
    Pair ReadPair(J data) {
        var result=new Pair{screens=data["uiScreens"].A.Select(r=>Required(r["path"].S).gameObject).ToArray()};
        foreach(var screen in result.screens)if(!screens.Contains(screen))screens.Add(screen);
        string path=data["uiTransitionAnimator"]["path"].S;
        if(path.Length>0&&!transitions.TryGetValue(path,out result.transition))throw new InvalidOperationException("Original UI transition binding missing: "+path);
        return result;
    }
    static void Open(Pair pair,bool delayed) {
        if(pair==null)return;foreach(var screen in pair.screens)if(screen!=null)screen.SetActive(true);
        if(pair.transition!=null)pair.transition.Enter(delayed);
    }
    static void Close(Pair pair,bool networkPair) {
        if(pair==null)return;
        if(pair.transition!=null) {
            // Network close checks the transition host's active hierarchy;
            // local close calls Exit even if that host is already inactive.
            if(!networkPair||pair.transition.gameObject.activeInHierarchy)pair.transition.Exit(false);
        } else foreach(var screen in pair.screens)if(screen!=null)screen.SetActive(false);
    }
    public void SetState(int state,bool paused) {
        if(disposed)throw new ObjectDisposedException(nameof(OriginalUIFlow));
        if(!network.TryGetValue(state,out var next))throw new ArgumentOutOfRangeException(nameof(state),"Recovered battle UI states are 2 through 6");
        if(State!=state) {
            int old=State;if(network.TryGetValue(old,out var previous))Close(previous,true);
            State=state;Open(next,old==2&&state==4);
        }
        if(Paused!=paused) {
            if(Paused)Close(pausedPair,false);
            if(paused)Open(pausedPair,false);else Open(next,false);
            Paused=paused;
        }
    }
    public bool IsTransitionExiting(string sourcePath)=>transitions.TryGetValue(sourcePath,out var driver)&&driver.Exiting;
    // CanvasAndCamera.ccu 0x3416da8. A real destroyed runner closes every
    // mounted network screen immediately, including Round/Tutorial. A new
    // runner must then reopen its current pair, even for a Practice 4 -> 4.
    public void OnOriginalRunnerDestroyed(bool retrying) {
        if(retrying)return;
        foreach(var screen in screens)if(screen!=null)screen.SetActive(false);
        State=0;
    }
    public IReadOnlyList<GameObject> ActivateVerdict(bool match,bool won,int gameMode=5,OriginalUIPopupResult result=null,int matchEndReason=0,Action prepareText=null) {
        if(disposed)throw new ObjectDisposedException(nameof(OriginalUIFlow));
        return Popups.ActivateVerdict(match,won,gameMode,result,matchEndReason,prepareText);
    }
    public void Dispose() {
        if(disposed)return;disposed=true;
        Popups.Dispose();
        foreach(var driver in owned)if(driver!=null)UnityEngine.Object.Destroy(driver);
        owned.Clear();transitions.Clear();
    }
}
