using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Jianghu {
// Acceptance through the shipping Unity client and its real local companion.
// Fixtures are separate saves; no production save or model endpoint is touched.
public sealed partial class PhysicsBattleSmoke:MonoBehaviour {
    GameClient client;JianghuApp app;string output,phase;
    readonly List<J> checks=new List<J>();readonly List<string> errors=new List<string>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot(){if(GameClient.Arg("-physicalCombatSmoke").Length==0)return;if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("Physical acceptance requires an isolated saveDir");Application.Quit(2);return;}new GameObject("Physical campaign acceptance").AddComponent<PhysicsBattleSmoke>();}
    void OnEnable(){Application.logMessageReceived+=Log;}
    void OnDisable(){Application.logMessageReceived-=Log;}
    void Log(string message,string stack,LogType type){if(type==LogType.Exception||type==LogType.Error)errors.Add(message);}
    void Check(string name,bool pass,J facts=null){checks.Add(J.O("check",name,"passed",pass,"facts",facts??new J()));File.WriteAllText(Path.Combine(output,"progress.json"),new J(checks).ToString());if(!pass)Debug.LogWarning("PHYSICAL ACCEPTANCE FAILED "+name);}
    IEnumerator Call(string path,J body,Action<J> received=null,bool expected=true){bool ok=false;yield return client.Request(path,body,j=>{ok=true;received?.Invoke(j);});Check(path,ok==expected,J.O("expected",expected,"error",client.Error));}
    IEnumerator Picture(string name){yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));yield return new WaitForSecondsRealtime(.12f);}
    IEnumerator WaitArena(){float until=Time.realtimeSinceStartup+12;while(app.PhysicsArena==null&&Time.realtimeSinceStartup<until)yield return null;Check("native arena instantiated",app.PhysicsArena!=null);}
    IEnumerator Start(){
        output=GameClient.Arg("-physicalCombatSmoke");phase=GameClient.Arg("-physicalCombatPhase","save");Directory.CreateDirectory(output);
        float until=Time.realtimeSinceStartup+100;
        while(Time.realtimeSinceStartup<until){client=FindAnyObjectByType<GameClient>();app=FindAnyObjectByType<JianghuApp>();if(client!=null&&client.Ready&&client.World.Has)break;yield return null;}
        Check("companion ready",client!=null&&client.Ready&&client.World.Has);if(client==null||!client.Ready){Finish();yield break;}
        yield return Call("/api/ai/config",J.O("enabled",false));
        if(!client.World["hero"]["created"].B){yield return Call("/api/hero",client.HeroBody("create","name","物理验收","sex","男","age",24,"place",client.World["hero"]["place"]));yield return client.Refresh();}
        if(phase=="weapons")yield return AuditWeapons();else if(phase=="pace")yield return AuditPace();else if(phase=="martial")yield return AuditMartial();else if(phase=="mouse")yield return AuditMouse();else if(phase=="attributes")yield return AuditAttributes();else if(phase=="kinetic")yield return AuditKinetic();else if(phase=="feel")yield return AuditFeel();else if(phase=="rigs")yield return AuditRigs();else if(phase=="resume")yield return Resume();else yield return Open(phase=="fists");
        Check("no runtime errors",errors.Count==0,new J(errors.Select(e=>new J(e)).ToList()));
        Finish();
    }
    IEnumerator Open(bool fists){
        int month=client.World["month"].I;
        yield return Call("/api/intervene",J.O("action","challenge","npc_id",1),j=>app.Martial=j);
        app.ShowBattleForSmoke();yield return WaitArena();if(app.PhysicsArena==null)yield break;
        var arena=app.PhysicsArena;
        Check("physical session is sole active ruleset",app.Martial["battle"]["ruleset"].I==4&&app.Martial["battle"]["engine"].S=="unity-physics2d-v09");
        Check("battle starts paused",arena.paused);
        Check("equipment reaches actual rigidbody",arena.player.weapon.id==(fists?"unarmed":"greatsword")&&Mathf.Abs(arena.player.sword.rb.mass-arena.player.weapon.mass)<.001f);
        Check("stats reach physical fighter",Mathf.Abs(arena.player.maxHealth-(float)app.Martial["battle"]["fighters"][0]["vigor_max"].N)<.001f);
        Check("no training shield conjured",arena.player.weapon.TwoHanded?arena.player.shield==null:!arena.player.HasShield);
        yield return Picture("01-opening");
        if(fists){
            arena.enemy.testOverride=true;arena.RunCampaign(true);arena.CampaignTestPad(false,Vector2.right);
            yield return new WaitForSecondsRealtime(1);arena.CampaignTestPad(false,Vector2.up);yield return new WaitForSecondsRealtime(1);
            Check("offhand fist has paid attack and motion effort",arena.player.attackId>0&&arena.player.motionStaminaSpent>0);
            arena.PauseCampaign();arena.CampaignReleasePads();arena.CampaignAutoplay=true;yield return CombatToFinish(35);yield break;
        }
        arena.enemy.testOverride=true;arena.RunCampaign(true);arena.CampaignTestPad(true,Vector2.up);
        yield return new WaitForSecondsRealtime(3.2f);arena.PauseCampaign();arena.CampaignReleasePads();
        Check("fixed origin controls and physical drive used",arena.player.attackId>0&&arena.player.stamina<arena.player.maxStamina&&arena.player.PrimaryGripError<.04f);
        yield return app.SavePhysicsForSmoke("checkpoint");
        var b=app.Martial["battle"];J saved=b["frame"];
        Check("actual body snapshot persisted",saved.Has&&saved["fighters"][0]["bodies"].A.Count==16&&b["revision"].I>1);
        Check("checkpoint preserves world time",client.World["month"].I==month&&!b["settled"].B);
        File.WriteAllText(Path.Combine(client.SaveDir,"expected-physical-frame.json"),saved.ToString());
        File.WriteAllText(Path.Combine(client.SaveDir,"expected-physical-session.json"),J.O("id",b["id"],"month",month).ToString());
        yield return Picture("02-saved-pose");
    }
    IEnumerator Resume(){
        J expected=J.Parse(File.ReadAllText(Path.Combine(client.SaveDir,"expected-physical-frame.json")));
        J session=J.Parse(File.ReadAllText(Path.Combine(client.SaveDir,"expected-physical-session.json")));
        yield return Call("/api/martial",null,j=>app.Martial=j);app.ShowBattleForSmoke();yield return WaitArena();if(app.PhysicsArena==null)yield break;
        expected=app.Martial["battle"]["frame"];
        File.WriteAllText(Path.Combine(output,"authoritative-restored-frame.json"),expected.ToString());
        var arena=app.PhysicsArena;var actual=arena.CaptureCampaign();File.WriteAllText(Path.Combine(output,"actual-restored-frame.json"),actual.ToString());float poseError=0;
        for(int side=0;side<2;side++)foreach(var body in expected["fighters"][side]["bodies"].A){var restored=actual["fighters"][side]["bodies"].A.First(x=>x["key"].S==body["key"].S);poseError=Mathf.Max(poseError,Mathf.Abs((float)(restored["x"].N-body["x"].N)),Mathf.Abs((float)(restored["y"].N-body["y"].N)),Mathf.Abs(Mathf.DeltaAngle((float)restored["angle"].N,(float)body["angle"].N)));}
        Check("restart restores physical pose and session",app.Martial["battle"]["id"].S==session["id"].S&&poseError<.003f,J.O("maximum_pose_error",poseError));
        Check("restart preserves health and stamina",Mathf.Abs(arena.player.health-(float)expected["fighters"][0]["health"].N)<.001f&&Mathf.Abs(arena.player.stamina-(float)expected["fighters"][0]["stamina"].N)<.001f);
        Check("restart clears pointer input",arena.paused);
        yield return Picture("03-resumed");
        yield return app.SavePhysicsForSmoke("leave");yield return new WaitForSecondsRealtime(.8f);
        Check("temporarily leaving keeps battle unresolved",client.World["battle_active"].B&&app.CurrentStage==Stage.City&&!app.ViewOpen&&arena.CampaignHidden);
        Check("unresolved battle blocks city movement",app.CityMovementReasonForSmoke.Contains("切磋")&&!app.CityWalkForSmoke(client.World["hero"]["row"].I,client.World["hero"]["col"].I));
        app.ShowBattleForSmoke();yield return WaitArena();yield return null;
        Check("returning reuses same physical rig",app.PhysicsArena==arena&&!arena.CampaignHidden&&arena.paused);
        yield return CombatToFinish(185);
    }
    IEnumerator CombatToFinish(float maxSeconds){
        var arena=app.PhysicsArena;int month=client.World["month"].I,points=client.World["hero"]["points"].I;
        arena.CampaignAutoplay=true;arena.enemy.testOverride=false;arena.RunCampaign(true);
        float until=Time.realtimeSinceStartup+maxSeconds,maxGap=0,maxLimit=0;
        while(Time.realtimeSinceStartup<until&&!arena.CampaignFinished){maxGap=Mathf.Max(maxGap,arena.player.MaxJointGap(),arena.enemy.MaxJointGap());maxLimit=Mathf.Max(maxLimit,arena.player.MaxLimitError(),arena.enemy.MaxLimitError());yield return new WaitForSecondsRealtime(.1f);}
        Check("real contacts produce damage",arena.hits>0&&(arena.player.health<arena.player.maxHealth||arena.enemy.health<arena.enemy.maxHealth),J.O("hits",arena.hits,"elapsed",arena.CampaignElapsed));
        Check("physical constraints remain bounded",maxGap<.15f&&maxLimit<8,J.O("gap",maxGap,"limit_degrees",maxLimit));
        yield return Picture("04-physical-combat");
        if(!arena.CampaignFinished){yield return app.SavePhysicsForSmoke("surrender");}else{
            until=Time.realtimeSinceStartup+15;while(!app.Martial["battle"]["settled"].B&&Time.realtimeSinceStartup<until)yield return null;
        }
        var b=app.Martial["battle"];yield return client.Refresh();
        Check("one world settlement completed",b["settled"].B&&b["event_id"].Has&&!client.World["battle_active"].B&&b["summary"].A.Count==2);
        Check("battle costs no month or map action points",client.World["month"].I==month&&client.World["hero"]["points"].I==points);
        int events=client.World["events"].I;var body=J.O("battle_id",b["id"],"revision",b["last_request_revision"],"action",b["last_action"],"frame",b["frame"]);
        yield return Call("/api/battle",body);yield return client.Refresh();Check("replayed result cannot charge twice",client.World["events"].I==events);
        if(app.PhysicsArena!=null)yield return app.SavePhysicsForSmoke("complete");
        yield return new WaitForSecondsRealtime(1);yield return client.Refresh();
        Check("back in world with controls restored",app.PhysicsArena==null&&Time.timeScale==1&&app.CityMovementReasonForSmoke.Length==0);
        float cityReady=Time.realtimeSinceStartup+10;while(!app.City.Has&&Time.realtimeSinceStartup<cityReady)yield return null;
        int row=client.World["hero"]["row"].I,col=client.World["hero"]["col"].I,oldPoints=client.World["hero"]["points"].I;
        bool walking=false;var destination=new Vector2Int(row,col);
        foreach(var delta in new[]{Vector2Int.right,Vector2Int.up,Vector2Int.left,Vector2Int.down}){
            var target=new Vector2Int(row,col)+delta;int size=app.City["size"].I;
            if(target.x<0||target.y<0||target.x>=size||target.y>=size)continue;
            char tile=app.City["rows"][target.x].S[target.y];if(tile=='~'||tile=='^'||tile=='#')continue;
            if(app.CityWalkForSmoke(target.x,target.y)){walking=true;destination=target;break;}
        }
        float walkUntil=Time.realtimeSinceStartup+12;
        while(walking&&Time.realtimeSinceStartup<walkUntil&&(app.CityWalkingForSmoke||client.World["hero"]["row"].I!=destination.x||client.World["hero"]["col"].I!=destination.y))yield return new WaitForSecondsRealtime(.1f);
        Check("actual city walking works after settlement",walking&&client.World["hero"]["row"].I==destination.x&&client.World["hero"]["col"].I==destination.y&&client.World["hero"]["points"].I<oldPoints);
        yield return Picture("05-returned-city");
    }
    static J AuditFighter(DuelWeapon weapon,bool shield=false){
        return J.O("name","骨架检查","vigor",120,"vigor_max",120,"stamina",100,"damage_scale",1,"damage_reduction",0,"handling_scale",1,"shield",shield,
            "weapon",J.O("id",weapon.id,"name",weapon.name,"mass",weapon.mass,"cost",weapon.cost,"handling",weapon.handling,"damage",weapon.damage,"pressure",weapon.pressure,"distance",weapon.distance));
    }
    IEnumerator AuditRigs(){
        var catalog=J.Parse(Resources.Load<TextAsset>("PaperCast/index").text);
        var skins=catalog["skins"].D.Keys.Where(k=>!k.EndsWith("child")).OrderBy(k=>k).ToArray();
        var scene=SceneManager.CreateScene("Physical binding acceptance",new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        var root=new GameObject("Physical binding acceptance");root.SetActive(false);SceneManager.MoveGameObjectToScene(root,scene);
        var arena=root.AddComponent<PhysicsDuel>();
        arena.campaign=new PhysicsDuel.CampaignOptions{playerSkin=skins[0],enemySkin=skins[0],battle=J.O("fighters",new J(new List<J>{AuditFighter(DuelWeapon.Fist),AuditFighter(DuelWeapon.Fist)}))};
        app.enabled=false;root.SetActive(true);
        var cases=new List<J>();
        var variants=new[]{DuelWeapon.All[DuelWeapon.SwordIndex],DuelWeapon.All[DuelWeapon.SwordIndex],DuelWeapon.All[6],DuelWeapon.Fist};
        foreach(var skin in skins)for(int variant=0;variant<variants.Length;variant++){
            var weapon=variants[variant];bool shield=variant==0;
            arena.campaign.playerSkin=arena.campaign.enemySkin=skin;
            arena.campaign.battle=J.O("fighters",new J(new List<J>{AuditFighter(weapon,shield),AuditFighter(weapon,shield)}));
            arena.ResetRound();arena.player.testOverride=arena.enemy.testOverride=true;
            foreach(var a in arena.player.colliders)foreach(var b in arena.enemy.colliders)Physics2D.IgnoreCollision(a,b);
            arena.RunCampaign(true);arena.CampaignTestPad(true,Vector2.up);
            float start=Time.realtimeSinceStartup,maxGap=0,maxLimit=0;
            while(Time.realtimeSinceStartup-start<1.4f){
                arena.player.stamina=arena.enemy.stamina=100;
                yield return new WaitForFixedUpdate();
                if(Time.realtimeSinceStartup-start>.3f){maxGap=Mathf.Max(maxGap,arena.player.MaxJointGap(),arena.enemy.MaxJointGap());maxLimit=Mathf.Max(maxLimit,arena.player.MaxLimitError(),arena.enemy.MaxLimitError());}
            }
            bool ok=maxGap<.1f&&maxLimit<6&&arena.player.torso.rb.position.y>1.5f&&arena.enemy.torso.rb.position.y>1.5f;
            cases.Add(J.O("skin",skin,"weapon",weapon.id,"shield",shield,"passed",ok,"max_gap",maxGap,"max_limit",maxLimit,"worst",arena.player.worstJoint+" | "+arena.enemy.worstJoint));
            File.WriteAllText(Path.Combine(output,"rig-cases.json"),new J(cases).ToString());
            if(!ok)yield return Picture("rig-"+skin+"-"+weapon.id);
        }
        Check("all adult skins support shield offhand-fist two-hand and fists",cases.Count==skins.Length*4&&cases.All(c=>c["passed"].B),J.O("skins",skins.Length,"cases",cases.Count,"failed",new J(cases.Where(c=>!c["passed"].B).ToList())));
        arena.PauseCampaign();yield return SceneManager.UnloadSceneAsync(scene);app.enabled=true;Time.timeScale=1;
    }
    void Finish(){bool passed=checks.All(c=>c["passed"].B)&&errors.Count==0;File.WriteAllText(Path.Combine(output,"acceptance.json"),J.O("passed",passed,"phase",phase,"checks",new J(checks),"errors",new J(errors.Select(e=>new J(e)).ToList())).ToString());Debug.Log("PHYSICAL_CAMPAIGN_ACCEPTANCE "+passed);Application.Quit(passed?0:2);}
}
}
