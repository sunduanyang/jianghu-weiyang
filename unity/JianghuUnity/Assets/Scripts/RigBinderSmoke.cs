using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
// 关节绑定验收：切图、示例绑定完整、木偶不散架、存取一致、切磋里确实用上了绑定。
// 用法：江湖未央.exe -rigBinderSmoke <输出目录> -saveDir <独立存档目录>
public sealed class RigBinderSmoke:MonoBehaviour {
    string output;readonly List<J> checks=new List<J>();readonly List<string> errors=new List<string>();bool failed;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot(){if(GameClient.Arg("-rigBinderSmoke").Length==0)return;if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("Rig binder acceptance requires an isolated saveDir");Application.Quit(2);return;}new GameObject("Rig binder acceptance").AddComponent<RigBinderSmoke>();}
    void OnEnable(){Application.logMessageReceived+=Log;}void OnDisable(){Application.logMessageReceived-=Log;}
    void Log(string s,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(s);}
    void Check(string name,bool pass,J facts=null){if(!pass)failed=true;checks.Add(J.O("check",name,"pass",pass,"facts",facts??J.O()));File.WriteAllText(Path.Combine(output,"progress.json"),new J(checks).ToString());}
    IEnumerator Picture(string name){for(int i=0;i<3;i++)yield return new WaitForEndOfFrame();var t=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(output,name+".png"),t.EncodeToPNG());Destroy(t);}
    IEnumerator Start(){
        output=GameClient.Arg("-rigBinderSmoke");Directory.CreateDirectory(output);
        var run=Run();
        while(true){object value=null;bool more=false;try{more=run.MoveNext();if(more)value=run.Current;}catch(Exception e){errors.Add(e.ToString());break;}if(!more)break;yield return value;}
        Check("no runtime errors",errors.Count==0,new J(errors.Select(e=>new J(e)).ToList()));
        File.WriteAllText(Path.Combine(output,"report.json"),J.O("passed",!failed,"checks",new J(checks)).ToString());
        Application.Quit(failed?1:0);
    }
    IEnumerator Run(){
        Screen.SetResolution(1680,1000,FullScreenMode.Windowed);
        // 1. 自动切图
        string sheetPath=RigLibrary.Sheets().FirstOrDefault(p=>Path.GetFileName(p).StartsWith("示例"));
        Check("example sheet ships with the game",sheetPath!=null,J.O("sheets",new J(RigLibrary.Sheets().Select(s=>new J(s)).ToList())));
        if(sheetPath!=null){
            var sheet=RigTextures.LoadFile(sheetPath);var cuts=RigSheetCutter.Run(sheet);
            Check("sheet is cut into separate pieces, number labels dropped",cuts.Count>=17&&cuts.Count<=20&&cuts.All(c=>c.Tex.width>20&&c.Tex.height>20),J.O("pieces",cuts.Count,"sizes",new J(cuts.Select(c=>new J(c.Tex.width+"x"+c.Tex.height)).ToList())));
            foreach(var c in cuts)Destroy(c.Tex);Destroy(sheet);
        }
        // 2. 示例绑定
        var rig=RigLibrary.Get("示例少年");
        Check("example rig loads with every required part bound",rig!=null&&rig.RequiredDone==rig.RequiredTotal&&RigLibrary.ForDuelCandidate(rig),J.O("done",rig?.RequiredDone??-1,"total",rig?.RequiredTotal??-1));
        if(rig==null)yield break;
        float shoulderGap=Vector2.Distance(rig.Point("torso","shoulderNear"),rig.Point("torso","shoulderFar"));
        Check("far shoulder sits beside the near shoulder, not on the back",shoulderGap<24,J.O("gap_px",shoulderGap));
        // 3. 木偶
        var puppet=new RigPuppet(rig);
        foreach(var mode in new[]{RigPuppet.Mode.Hang,RigPuppet.Mode.Fall}){
            puppet.Reset(mode);string grab=mode==RigPuppet.Mode.Hang?"handNear.tip":null;
            for(int i=0;i<600;i++)puppet.Step(mode,i<240?grab:null,puppet.BindJoints.TryGetValue("handNear.tip",out var h)?h+new Vector2(300*Mathf.Sin(i*.05f),-200):Vector2.zero);
            bool grounded=mode!=RigPuppet.Mode.Fall||puppet.JointIds.All(j=>puppet.Joint(j).y<=puppet.Ground+1);
            Check("puppet stays together while "+(mode==RigPuppet.Mode.Hang?"dragged on its string":"falling"),puppet.Finite&&puppet.MaxStretch()<.03f&&grounded,J.O("max_stretch",puppet.MaxStretch(),"grounded",grounded));
        }
        // 3b. 自动猜关节：与示例的手工标注相比，中位误差不超过拆件尺寸的 8%；面朝左的原画猜出的点左右对称。
        var guessErrors=new List<float>();
        foreach(var def in RigSlots.All){
            if(!rig.Usable(def.Id))continue;var piece=rig.SlotPiece(def.Id);var guess=RigAutoJoints.Guess(def.Id,piece.Tex);
            foreach(var p in rig.Slots[def.Id].Points)if(guess.TryGetValue(p.Key,out var g))guessErrors.Add(Vector2.Distance(g,p.Value)/Mathf.Max(piece.Width,piece.Height));
        }
        guessErrors.Sort();float median=guessErrors.Count>0?guessErrors[guessErrors.Count/2]:1;
        Check("auto-guessed joints land near the hand-placed ones",median<.08f,J.O("median_error_of_piece_size",Math.Round(median,3),"points",guessErrors.Count));
        var arm=rig.SlotPiece("upperNear").Tex;var flipped=new Texture2D(arm.width,arm.height,TextureFormat.RGBA32,false);var src=arm.GetPixels32();var dst=new Color32[src.Length];
        for(int y=0;y<arm.height;y++)for(int x=0;x<arm.width;x++)dst[y*arm.width+x]=src[y*arm.width+arm.width-1-x];flipped.SetPixels32(dst);flipped.Apply();
        var right=RigAutoJoints.Guess("upperNear",arm);var leftGuess=RigAutoJoints.Guess("upperNear",flipped,true);Destroy(flipped);
        float mirrorError=right.Keys.Max(key=>Vector2.Distance(right[key],new Vector2(arm.width-1-leftGuess[key].x,leftGuess[key].y)));
        Check("left-facing art is guessed as the mirror image",mirrorError<=2,J.O("max_px",mirrorError));
        // 3c. 接缝：出场姿势下测量；开启接缝补偿后最大接缝不增大。
        var pose=RigDuelPreview.Provider(RigDuelPreview.SpawnPose(0,1),rig);float k=RigPoseRenderer.FitScale(rig,pose);
        float savedTol=rig.FitTolerance;rig.FitTolerance=0;var raw=RigPoseRenderer.MeasureSeams(rig,pose,k,false);
        rig.FitTolerance=.15f;var fitted=RigPoseRenderer.MeasureSeams(rig,pose,k,false);rig.FitTolerance=savedTol;
        var mirrored=RigPoseRenderer.MeasureSeams(rig,RigDuelPreview.Provider(RigDuelPreview.SpawnPose(0,-1),rig),k,true);
        Check("seams are measured for every joint and compensation closes them overall",raw.Count>=11&&fitted.Values.Sum()<raw.Values.Sum(),
            J.O("meters_per_pixel",k,"total_raw",raw.Values.Sum(),"total_fitted",fitted.Values.Sum(),"seams",J.O(raw.SelectMany(p=>new object[]{p.Key,Math.Round(p.Value,1)}).ToArray())));
        Check("facing left gives the same seams as facing right",raw.All(p=>mirrored.TryGetValue(p.Key,out var m)&&Mathf.Abs(m-p.Value)<.5f));
        // 4. 存取一致
        string copy=Path.Combine(output,"saved-copy");rig.Save(copy);var again=RigBinding.Load(copy);
        Check("saved rig reloads identically",again.ToJson().ToString()==rig.ToJson().ToString()&&again.Pieces.Count==rig.Pieces.Count&&again.Pieces.All(p=>p.Tex!=null&&p.Tex.width==rig.Piece(p.Name).Width));
        // 5. 绑定台界面
        JianghuApp app=null;GameClient client=null;float until=Time.realtimeSinceStartup+90;
        while(Time.realtimeSinceStartup<until){client=FindAnyObjectByType<GameClient>();app=FindAnyObjectByType<JianghuApp>();if(client!=null&&client.Ready&&client.World.Has)break;yield return null;}
        if(app!=null&&client!=null&&client.Ready){
            app.OpenSystem(SystemTab.Rigging);for(int i=0;i<10;i++)yield return null;
            yield return Picture("binder");
            Check("binder tab opens on a saved rig",app.RigBinderRig!=null,J.O("status",app.RigBinderStatus));
        }else Check("backend ready for binder screen",false);
        // 6. 切磋里使用绑定
        string priorActive=RigLibrary.Active,priorOpponent=RigLibrary.Opponent;
        try{
            RigLibrary.Active="示例少年";RigLibrary.Opponent="示例少年";
            yield return client.Request("/api/ai/config",J.O("enabled",false),j=>{});
            bool opened=false;int month=client.World["month"].I;
            yield return client.Request("/api/intervene",J.O("action","challenge","npc_id",1),j=>{app.Martial=j;opened=j["battle"].Has;});
            Check("formal challenge opens through the real backend",opened);
            if(!opened)yield break;
            app.ShowBattleForSmoke();until=Time.realtimeSinceStartup+15;
            while(app.PhysicsArena==null&&Time.realtimeSinceStartup<until)yield return null;
            var duel=app.PhysicsArena;
            for(int i=0;i<20;i++)yield return null;
            if(duel!=null){duel.player.testOverride=duel.enemy.testOverride=true;duel.RunCampaign(true);}
            yield return Picture("duel");
            if(duel!=null)duel.PauseCampaign();
            Check("bound appearance uses the formal source graph and solver",duel!=null&&duel.Campaign&&duel.player.SourceRigActive&&duel.enemy.SourceRigActive&&duel.CombatSolverSubsteps==8);
            yield return app.SavePhysicsForSmoke("checkpoint");
            Check("formal bound sparring saves without advancing the world",app.Martial["battle"]["frame"].Has&&app.Martial["battle"]["revision"].I>1&&client.World["month"].I==month);
            var mismatch=duel!=null&&duel.player!=null?duel.player.BoundRigMismatch():new Dictionary<string,float>();
            var liveSeams=duel!=null&&duel.player!=null?duel.player.BoundRigSeams():new Dictionary<string,float>();
            Check("sparring draws both fighters with the bound rig",duel!=null&&duel.player!=null&&duel.player.UsesBoundRig&&duel.enemy.UsesBoundRig,J.O("meters_per_pixel",duel?.player?.BoundRigMetersPerPixel??0,"mismatch",J.O(mismatch.SelectMany(p=>new object[]{p.Key,Math.Round(p.Value,3)}).ToArray()),"seams",J.O(liveSeams.SelectMany(p=>new object[]{p.Key,Math.Round(p.Value,1)}).ToArray())));
        }finally{RigLibrary.Active=priorActive;RigLibrary.Opponent=priorOpponent;}
    }
}
}
