using System;
using System.Collections.Generic;
using System.Linq;
using Jianghu;
using UnityEngine;

// Owns original verdict hosts. Selection is prepared before UIFlow activates
// their parent screen, preserving one OnEnable/audio/RNG sequence.
public sealed class OriginalUIPopups : IDisposable {
    readonly Dictionary<string,Transform> nodes=new Dictionary<string,Transform>();
    readonly Dictionary<string,OriginalUIPopupDriver> drivers=new Dictionary<string,OriginalUIPopupDriver>();
    readonly J ui,library;
    public event Action<float,float,float> HapticRequested;
    public OriginalUIPopups(Transform root,J uiSource) {
        ui=uiSource;var file=Resources.Load<TextAsset>("OriginalUIPopupSource");
        if(file==null)throw new InvalidOperationException("OriginalUIPopupSource manifest missing");library=J.Parse(file.text);
        void Walk(Transform t,string p){nodes[p]=t;foreach(Transform c in t)Walk(c,p+"/"+c.name);}Walk(root,"CanvasAndCamera");
        foreach(var record in library["records"].A) {
            var host=Node(record["path"].S);if(host==null)throw new InvalidOperationException("Original verdict host missing: "+record["path"].S);
            var driver=host.gameObject.AddComponent<OriginalUIPopupDriver>();driver.Initialize(this,record,library);drivers.Add(record["path"].S,driver);
        }
    }
    internal Transform Node(string path)=>nodes.TryGetValue(path,out var n)?n:null;
    internal Transform Resolve(J reference)=>reference["kind"].S=="scene"?Node(reference["path"].S):null;
    internal T Resolve<T>(J reference)where T:Component {
        var node=Resolve(reference);if(node==null)return null;
        var components=node.GetComponents<T>();int ordinal=reference["ordinal"].I;
        if(components.Length==1)return components[0];
        return ordinal>=0&&ordinal<components.Length?components[ordinal]:null;
    }
    internal void Haptic(float low,float high,float duration)=>HapticRequested?.Invoke(low,high,duration);
    public IReadOnlyList<GameObject> ActivateVerdict(bool match,bool won,int gameMode=5,OriginalUIPopupResult result=null,int matchEndReason=0,Action prepareText=null) {
        J view=ui["records"].A.First(r=>r["script"].S==(match?"MatchEndView":"RoundEndView"))["data"];
        var container=Node(view["popupsContainer"]["path"].S);
        if(container==null)throw new InvalidOperationException("Original verdict container missing");
        container.gameObject.SetActive(false);
        foreach(Transform child in container)child.gameObject.SetActive(false);
        prepareText?.Invoke();
        var selected=new List<GameObject>();
        bool forfeited=match&&(matchEndReason==1||matchEndReason==2)&&gameMode!=4;
        var references=forfeited?new[]{view["opponentForfeitedPopup"]}:view["popups"].A.Where(p=>p["mode"].I==gameMode&&p["isWin"].B==won).Select(p=>p["popup"]);
        foreach(var reference in references) {
            string path=reference["path"].S;var n=Node(path);
            if(n==null)throw new InvalidOperationException("Original verdict binding missing: "+path);
            if(drivers.TryGetValue(path,out var driver))driver.Result=result;
            n.gameObject.SetActive(true);selected.Add(n.gameObject);
            var soundView=library["views"][match?"MatchEndView":"RoundEndView"];
            string soundField=match?(forfeited||won?"deathWon":"deathLost"):(won?"roundWon":"roundLost");
            OriginalUIAudio.Play(Resolve<AudioSource>(soundView["audioSource"]),soundView[soundField],library);
        }
        // The original RoundOrMatchEnd_Enter curve enables Popups at 2.5 s.
        // Enabling it here would start its child clip/audio/RNG too early.
        return selected;
    }
    public OriginalUIPopupDriver Find(string sourcePath)=>drivers.TryGetValue(sourcePath,out var driver)?driver:null;
    public void Dispose(){foreach(var driver in drivers.Values)if(driver!=null)UnityEngine.Object.Destroy(driver);drivers.Clear();}
}
