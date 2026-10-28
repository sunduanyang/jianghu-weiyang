using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public sealed class PuppetTrialSmoke:MonoBehaviour {
    string output;
    PhysicsDuel arena;
    readonly List<J> checks=new List<J>();readonly List<string> errors=new List<string>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot(){if(GameClient.Arg("-puppetTrialSmoke").Length==0)return;if(!PhysicsDuel.LaceTrial||GameClient.Arg("-referenceCombat").Length==0){Debug.LogError("The puppet trial requires offline reference-combat flags");Application.Quit(2);return;}new GameObject("Lace puppet actual-rig acceptance").AddComponent<PuppetTrialSmoke>();}
    void OnEnable(){Application.logMessageReceived+=Log;}void OnDisable(){Application.logMessageReceived-=Log;}
    void Log(string value,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(value);}
    void Check(string name,bool pass,J facts=null){checks.Add(J.O("check",name,"pass",pass,"facts",facts??J.O()));}
    IEnumerator Start(){
        output=GameClient.Arg("-puppetTrialSmoke");Directory.CreateDirectory(output);Directory.CreateDirectory(Path.Combine(output,"frames"));
        float until=Time.realtimeSinceStartup+30;while(Time.realtimeSinceStartup<until){arena=PhysicsDuel.Instance;if(arena!=null&&arena.player!=null&&PuppetTrialControl.Instance!=null)break;yield return null;}
        if(arena==null||arena.player==null){Check("arena created",false);Finish();yield break;}
        var run=Run();while(true){object current=null;bool more=false;try{more=run.MoveNext();if(more)current=run.Current;}catch(Exception error){errors.Add(error.ToString());break;}if(!more)break;yield return current;}
        Check("no runtime exceptions",errors.Count==0,new J(errors.Select(e=>new J(e)).ToList()));Finish();
    }
    IEnumerator Picture(string name){for(int i=0;i<3;i++)yield return new WaitForEndOfFrame();var shot=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(output,name+".png"),shot.EncodeToPNG());Destroy(shot);}
    IEnumerator Run(){
        Screen.SetResolution(1680,960,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.3f);
        arena.BeginLaceAcceptance();arena.PauseCampaign();PuppetTrialControl.Instance.Inspect=true;
        Check("real current reference bodies",arena.player.Reference&&arena.enemy.Reference,J.O("player_bodies",arena.player.ReferenceBodyCount,"enemy_bodies",arena.enemy.ReferenceBodyCount));
        Check("trial never creates a backend or opens a save",FindAnyObjectByType<GameClient>()==null&&!arena.Campaign);
        var index=J.Parse(Resources.Load<TextAsset>("PuppetTrial/index").text);
        Check("15 original detached components",index["parts"].A.Count==15&&index["binding_basis"].S=="right-hand detached component artwork only");
        PhysicsDuel.Fighter.ResetLaceProbe();yield return Picture("01-bind-closeup");
        Check("bind pose anchors coincide in both directions",PhysicsDuel.Fighter.LaceAnchorSamples>0&&PhysicsDuel.Fighter.LaceAnchorError<.5,PhysicsDuel.Fighter.LaceProbe());
        arena.bones=true;yield return Picture("02-skeleton-overlay");arena.bones=false;
        arena.RunCampaign(true);yield return new WaitForSecondsRealtime(1.2f);yield return Picture("03-standing");
        PhysicsDuel.Fighter.ResetLaceProbe();float startX=arena.player.Hip.x;arena.player.move=.65f;
        for(int i=0;i<8;i++){yield return new WaitForSecondsRealtime(.12f);yield return Picture("frames/walk-"+i.ToString("D2"));}
        arena.player.move=0;yield return Picture("04-walking");
        Check("walking keeps artwork attached",PhysicsDuel.Fighter.LaceAnchorError<.5&&PhysicsDuel.Fighter.LaceAnchorSamples>0,J.O("travel",arena.player.Hip.x-startX,"probe",PhysicsDuel.Fighter.LaceProbe()));
        PhysicsDuel.Fighter.ResetLaceProbe();
        for(int i=0;i<14;i++){float phase=i*.4f;arena.LaceAcceptanceHands(new Vector2(Mathf.Cos(phase),Mathf.Sin(phase)),new Vector2(Mathf.Cos(phase+.9f),Mathf.Sin(phase+.9f)));yield return new WaitForSecondsRealtime(.12f);yield return Picture("frames/wave-"+i.ToString("D2"));}
        arena.CampaignReleasePads();yield return Picture("05-hand-driving");
        Check("hand driving keeps artwork attached",PhysicsDuel.Fighter.LaceAnchorError<.5&&PhysicsDuel.Fighter.LaceAnchorSamples>0,PhysicsDuel.Fighter.LaceProbe());
        PuppetTrialControl.Instance.Inspect=false;yield return Picture("06-arena");PuppetTrialControl.Instance.Inspect=true;
        Screen.SetResolution(1280,800,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.3f);PhysicsDuel.Fighter.ResetLaceProbe();yield return Picture("07-compact");
        Check("1280x800 projection matches actual physical anchors",PhysicsDuel.Fighter.LaceAnchorError<.5&&PhysicsDuel.Fighter.LaceAnchorSamples>0,PhysicsDuel.Fighter.LaceProbe());
        File.WriteAllText(Path.Combine(output,"physical-frame.json"),arena.CaptureCampaign().ToString());
        arena.PauseCampaign();
    }
    void Finish(){bool pass=checks.All(c=>c["pass"].B);File.WriteAllText(Path.Combine(output,"puppet-rig-native.json"),J.O("pass",pass,"checks",new J(checks),"errors",new J(errors.Select(e=>new J(e)).ToList())).ToString());Application.Quit(pass?0:1);}
}
}
