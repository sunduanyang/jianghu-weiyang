using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jianghu;
using UnityEngine;
using UnityEngine.UI;

// Opt-in integration check. Clicks the recovered Unity Button callbacks and
// observes real actors, UI activation, saves and arena transition. It does not
// simulate desktop hardware, change HP or skip the host's state machine.
public sealed class OriginalOnboardingReplay : MonoBehaviour {
    OriginalPreview host;
    string directory;
    readonly List<J> steps=new List<J>();
    readonly List<string> errors=new List<string>();
    readonly List<J> dummyImpacts=new List<J>();
    readonly List<J> dummyContacts=new List<J>();
    bool observing;
    bool finished;
    double startedAt;
    public static bool TryAttach(OriginalPreview preview) {
        var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-validateOriginalOnboarding");
        if(at<0||at+1>=args.Length)return false;
        var test=preview.gameObject.AddComponent<OriginalOnboardingReplay>();test.host=preview;test.directory=Path.GetFullPath(args[at+1]);
        Directory.CreateDirectory(test.directory);return true;
    }
    void OnLog(string message,string trace,LogType type) {
        if(type==LogType.Exception||type==LogType.Error||type==LogType.Assert)errors.Add(message);
    }
    IEnumerator Start() {
        host.ExternalInputReplay=true;AudioListener.volume=0;observing=true;startedAt=Time.realtimeSinceStartupAsDouble;Application.logMessageReceived+=OnLog;
        yield return new WaitForSecondsRealtime(2.75f);
        var tutorial=host.hud.GetComponentInChildren<OriginalTutorialController>(true);
        Check(host.GameMode==2&&host.OriginalState==4,"New player did not enter active source Practice");
        Check(!host.Progress.TutorialPassed,"Onboarding fixture requires a new isolated save");
        Check(host.enemy==null&&host.practiceDummy!=null,"Practice must contain its independent Dummy and no Enemy rig");
        if(host.practiceDummy!=null) {
            host.practiceDummy.ContactOccurred+=contact=> {
                if(dummyContacts.Count<512)dummyContacts.Add(J.O("player_owned_item",contact.OtherItem!=null&&contact.OtherItem.owner==host.player,"other_item",contact.OtherItem==null?0:contact.OtherItem.itemId,"fixed_item",contact.FixedItem==null?0:contact.FixedItem.itemId,"body_part",contact.BodyPart==null?"":contact.BodyPart.SourcePath));
            };
            host.practiceDummy.ImpactOccurred+=impact=>dummyImpacts.Add(J.O("part",impact.Part.SourcePath,"item",impact.Item==null?0:impact.Item.itemId,"player_owned_item",impact.Item!=null&&impact.Item.owner==host.player,"sound",impact.SoundIndex,"volume",impact.SoundVolume,"feedback",impact.FeedbackForce,"cannon",impact.Cannon));
            Check(host.practiceDummy.BodyParts.Count==5,"Dummy requires five original body parts");
            Check(host.practiceDummy.FixedItems.Select(item=>item.itemId).OrderBy(id=>id).SequenceEqual(new[]{202,229}),"Dummy fixed equipment differs from source");
        }
        Check(tutorial!=null&&tutorial.CurrentStep==0&&tutorial.ListeningForHandInput,"First recovered tutorial screen did not activate");
        Snapshot("practice_first_screen");
        OriginalRenderingDiagnostics.Capture(Camera.main,Path.Combine(directory,"practice"));
        yield return DrivePractice();
        var library=J.Parse(Resources.Load<TextAsset>("OriginalTutorialSource").text);
        for(int step=0;step<3;step++) {
            int expected=step;
            var row=library["buttons"].A.First(r=>r["path"].S.Contains("/Step "+(expected+1)+"/")&&r["calls"].A.Any(c=>c["m_MethodName"].S=="OnClickedNext"));
            Click(row["path"].S);yield return new WaitForSecondsRealtime(.7f);
            Check(tutorial!=null&&tutorial.CurrentStep==expected+1,"Recovered Next callback did not advance tutorial step");
            Snapshot("tutorial_step_"+(step+2));
        }
        var oldPlayer=host.player;var oldDummy=host.practiceDummy;
        var oldBodies=oldPlayer.GetComponentsInChildren<Rigidbody2D>(true).Concat(oldDummy==null?Array.Empty<Rigidbody2D>():oldDummy.GetComponentsInChildren<Rigidbody2D>(true)).ToArray();
        var oldItems=FindObjectsByType<OriginalItem>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(item=>item.owner==oldPlayer||(oldDummy!=null&&oldDummy.FixedItems.Contains(item))).ToArray();
        var fight=library["buttons"].A.First(r=>r["calls"].A.Any(c=>c["m_MethodName"].S=="OnClickedFight"));
        Click(fight["path"].S);
        double transitionAt=Time.realtimeSinceStartupAsDouble;
        while(Time.realtimeSinceStartupAsDouble-transitionAt<8&&(host.GameMode!=0||host.player==null||host.player==oldPlayer||host.enemy==null))yield return null;
        Check(host.Progress.TutorialPassed&&host.GameMode==0,"Fight did not persist tutorial and enter Campaign");
        Check(host.Progress.Level==1&&host.Progress.Stage==1&&host.Progress.Seed==0&&host.Progress.LevelType==1,"Fight selection differs from original Level1/Stage1/Monastery/seed0");
        Check(oldPlayer==null&&oldDummy==null&&host.practiceDummy==null,"Previous Practice actors survived arena transition");
        Check(oldBodies.All(body=>body==null)&&oldItems.All(item=>item==null),"Previous Practice rigid bodies or fixed/dropped items survived transition");
        Check(host.enemy!=null&&host.EnemyDisplayName=="Willow Murphy","Original first enemy was not generated");
        Check(host.enemy!=null&&host.enemy.leftItem!=null&&host.enemy.leftItem.itemId==199&&(host.enemy.rightItem==null||host.enemy.rightItem.itemId==0),"Original first enemy equipment differs");
        Check(host.EnvironmentCustomizer!=null&&host.EnvironmentCustomizer.CurrentStage.name.Contains("Monastery"),"Original first arena did not load Monastery");
        Snapshot("campaign_intro");
        yield return new WaitForSecondsRealtime(4);
        Check(host.OriginalState==4&&host.RoundTimeRemaining==0,"Campaign did not reach untimed active Round");
        CheckFinite();
        CheckCampaignHud();
        Snapshot("campaign_first_fight");
        Check(OriginalLocalProgress.Open(host.Progress.SavePath).TutorialPassed,"Tutorial completion was not saved to the isolated file");
        OriginalRenderingDiagnostics.Capture(Camera.main,Path.Combine(directory,"campaign"));
        Complete();
    }
    void Update(){if(observing&&!finished&&Time.realtimeSinceStartupAsDouble-startedAt>45){errors.Add("Onboarding real-time watchdog expired");Complete();}}
    void Complete() {
        if(finished)return;finished=true;
        File.WriteAllText(Path.Combine(directory,"original-onboarding-replay.json"),J.O("passed",errors.Count==0,"unity",Application.unityVersion,
            "scope","Original UI Button callbacks, real Practice Dummy, tutorial persistence and first Campaign arena; desktop hardware input and frame parity unverified",
            "steps",new J(steps),"dummy_contacts",new J(dummyContacts),"dummy_body_impacts",new J(dummyImpacts),"errors",new J(errors.Select(e=>new J(e)).ToList()),"runtime_parity_verified",false).ToString());
        Application.Quit(errors.Count==0?0:1);
    }
    IEnumerator DrivePractice() {
        if(host.practiceDummy==null)yield break;
        var input=new OriginalInputAdapter();input.Bind(host.hud.transform);input.UpdateViewport();
        Vector2 left=new Vector2(Screen.width*.25f,Screen.height*.3f),right=new Vector2(Screen.width*.75f,Screen.height*.3f);
        input.PointerDown(1101,left,true);host.player.OnOriginalScreenTap(true);host.TutorialContext.RaiseHandPressed();
        input.PointerDown(1102,right,false);host.player.OnOriginalScreenTap(false);host.TutorialContext.RaiseHandPressed();
        float started=Time.time;double realStarted=Time.realtimeSinceStartupAsDouble;
        while(Time.time-started<6&&Time.realtimeSinceStartupAsDouble-realStarted<15) {
            if(!CheckFinite())break;
            input.UpdateViewport();float angle=(Time.time-started)*9;
            input.PointerDrag(1101,left+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*140*input.CanvasScale);
            input.PointerDrag(1102,right+new Vector2(Mathf.Cos(-angle+1.4f),Mathf.Sin(-angle+1.4f))*140*input.CanvasScale);
            input.ApplyToRig(host.player);
            float distance=host.practiceDummy.Body.position.x-host.player.rootBody.position.x;
            host.player.movement=Mathf.Abs(distance)>.65f?Mathf.Sign(distance):0;
            if(host.player.movement!=0)host.player.KeepOriginalMove(host.player.movement);
            yield return null;
        }
        input.PointerUp(1101);host.TutorialContext.RaiseHandReleased();input.PointerUp(1102);host.TutorialContext.RaiseHandReleased();
        input.ApplyToRig(host.player);host.player.movement=0;
        Check(Time.time-started>=6,"Practice input gesture stalled before six scaled seconds");
        Check(dummyContacts.Any(contact=>contact["player_owned_item"].B),"Real player-owned weapon produced no actual Dummy/body/fixed-weapon contact");
        Check(!host.player.dead&&host.enemy==null,"Practice gesture lost the actual Practice actor domain");
        yield return new WaitForSecondsRealtime(1.2f);
    }
    void CheckCampaignHud() {
        var top=host.hud.transform.Find("Canvas/MainContainer/TopBar_InGame");
        Check(top!=null&&top.gameObject.activeInHierarchy,"Campaign top bar did not activate");
        if(top==null)return;
        var rect=top.GetComponent<RectTransform>();var corners=new Vector3[4];rect.GetWorldCorners(corners);
        var canvas=rect.GetComponentInParent<Canvas>();Camera camera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
        var points=corners.Select(point=>RectTransformUtility.WorldToScreenPoint(camera,point)).ToArray();
        Check(points.Max(point=>point.y)>0&&points.Min(point=>point.y)<Screen.height&&points.Max(point=>point.x)>0&&points.Min(point=>point.x)<Screen.width,"Campaign top bar remained outside the screen");
    }
    bool CheckFinite() {
        bool actors=new[]{host.player,host.enemy}.Where(rig=>rig!=null).All(rig=>float.IsFinite(rig.health)&&float.IsFinite(rig.stamina));
        bool bodies=FindObjectsByType<Rigidbody2D>(FindObjectsInactive.Include,FindObjectsSortMode.None).All(body=>float.IsFinite(body.position.x)&&float.IsFinite(body.position.y)&&float.IsFinite(body.rotation)&&float.IsFinite(body.linearVelocity.x)&&float.IsFinite(body.linearVelocity.y)&&float.IsFinite(body.angularVelocity));
        Check(actors&&bodies,"Actor vitality or actual physics state became non-finite");return actors&&bodies;
    }
    void Click(string sourcePath) {
        const string prefix="CanvasAndCamera/";
        var node=host.hud.transform.Find(sourcePath.Substring(prefix.Length));
        var button=node==null?null:node.GetComponent<Button>();
        Check(button!=null&&button.gameObject.activeInHierarchy&&button.interactable,"Recovered tutorial button unavailable: "+sourcePath);
        if(button!=null&&button.gameObject.activeInHierarchy&&button.interactable)button.onClick.Invoke();
    }
    void Check(bool value,string message){if(!value)errors.Add(message);}
    void Snapshot(string label)=>steps.Add(J.O("label",label,"frame",Time.frameCount,"mode",host.GameMode,"state",host.OriginalState,"level",host.Progress.Level,"stage",host.Progress.Stage,"seed",host.Progress.Seed,"level_type",host.Progress.LevelType,"tutorial_passed",host.Progress.TutorialPassed,"enemy_name",host.EnemyDisplayName));
    void OnDestroy(){if(observing)Application.logMessageReceived-=OnLog;}
}
