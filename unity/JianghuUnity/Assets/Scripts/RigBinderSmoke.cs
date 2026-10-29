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
    static void Boot(){if(GameClient.Arg("-rigBinderSmoke").Length==0)return;new GameObject("Rig binder acceptance").AddComponent<RigBinderSmoke>();}
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
        string priorActive=RigLibrary.Active;bool priorEnemy=RigLibrary.EnemyToo;
        try{
            RigLibrary.Active="示例少年";RigLibrary.EnemyToo=true;
            PhysicsDuel.OpenReferenceSandbox();for(int i=0;i<20;i++)yield return null;
            var duel=PhysicsDuel.Instance;
            yield return Picture("duel");
            var mismatch=duel!=null&&duel.player!=null?duel.player.BoundRigMismatch():new Dictionary<string,float>();
            Check("sparring draws both fighters with the bound rig",duel!=null&&duel.player!=null&&duel.player.UsesBoundRig&&duel.enemy.UsesBoundRig,J.O("meters_per_pixel",duel?.player?.BoundRigMetersPerPixel??0,"mismatch",J.O(mismatch.SelectMany(p=>new object[]{p.Key,Math.Round(p.Value,3)}).ToArray())));
        }finally{RigLibrary.Active=priorActive;RigLibrary.EnemyToo=priorEnemy;}
    }
}
}
