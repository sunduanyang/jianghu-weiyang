using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Jianghu {
public partial class JianghuApp {
    public PhysicsDuel PhysicsArena {get;private set;}
    public bool PhysicsBattleVisible=>PhysicsArena!=null&&!PhysicsArena.CampaignHidden;
    bool physicsSending;
    int battleReturnPage=1;
    float nextPhysicsSave,physicsFinishedAt=-1;
    string physicsSession="",queuedPhysicsCommand="";

    void UpdateBattle(){
        var b=Martial["battle"];
        bool visible=Page==3&&battleView&&!personScreen&&!dialogueOpen;
        if(!visible){
            if(PhysicsArena!=null){if(b["status"].S=="finished")ClosePhysicsArena();else if(!PhysicsArena.CampaignHidden)PhysicsArena.DeactivateCampaign();}
            return;
        }
        if(!b.Has||b["ruleset"].I!=4)return;
        if(PhysicsArena==null&&b["status"].S=="active"){
            var root=new GameObject("江湖切磋 · Physics2D");root.SetActive(false);
            var scene=SceneManager.CreateScene("江湖物理战场 "+b["id"].S,new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            SceneManager.MoveGameObjectToScene(root,scene);
            PhysicsArena=root.AddComponent<PhysicsDuel>();
            PhysicsArena.campaign=new PhysicsDuel.CampaignOptions {battle=b,playerSkin=PaperSkinFor(Hero,true),enemySkin=PaperSkinFor(Npc(b["fighters"][1]["id"].I)),command=QueuePhysicsCommand};
            physicsSession=b["id"].S;physicsFinishedAt=-1;nextPhysicsSave=Time.realtimeSinceStartup+5;root.SetActive(true);
            Client.PhysicsQuitFrame=PhysicsQuitBody;
        }
        if(PhysicsArena==null)return;
        if(physicsSession!=b["id"].S){ClosePhysicsArena();return;}
        if(PhysicsArena.CampaignHidden)PhysicsArena.ActivateCampaign();
        if(PhysicsArena.CampaignFinished&&b["status"].S=="active"){
            if(physicsFinishedAt<0){physicsFinishedAt=Time.realtimeSinceStartup;PhysicsArena.CampaignMessage="交手结束，正在保存结果。";}
            if(Time.realtimeSinceStartup-physicsFinishedAt>1.4f&&!physicsSending)QueuePhysicsCommand("settle");
        }else if(PhysicsArena.Live&&Time.realtimeSinceStartup>nextPhysicsSave&&!physicsSending)QueuePhysicsCommand("checkpoint");
    }
    J PhysicsQuitBody(){
        var b=Martial["battle"];
        if(PhysicsArena==null||b["status"].S!="active")return null;
        return J.O("battle_id",b["id"],"revision",b["revision"],"action",PhysicsArena.CampaignFinished?"complete":"checkpoint","frame",PhysicsArena.CaptureCampaign());
    }
    void QueuePhysicsCommand(string command){
        if(PhysicsArena==null)return;
        if(physicsSending){if(command!="checkpoint")queuedPhysicsCommand=command;return;}
        StartCoroutine(SendPhysicsCommand(command));
    }
    IEnumerator SendPhysicsCommand(string command){
        physicsSending=true;
        while(Client.Busy)yield return null;
        var arena=PhysicsArena;var b=Martial["battle"];
        if(arena==null){physicsSending=false;yield break;}
        bool leave=command=="leave"||command=="complete"||command=="surrender";
        if(b["status"].S=="finished"){
            physicsSending=false;if(leave)ReturnFromPhysics();yield break;
        }
        string action=command=="surrender"?"surrender":command=="settle"||command=="complete"||arena.CampaignFinished?"complete":"checkpoint";
        J body=J.O("battle_id",b["id"],"revision",b["revision"],"action",action,"frame",arena.CaptureCampaign());
        bool success=false;Client.Busy=true;
        yield return Client.Request("/api/battle",body,j=>{success=true;Martial=j;});
        if(success&&action!="checkpoint")yield return Client.Refresh();
        Client.Busy=false;physicsSending=false;nextPhysicsSave=Time.realtimeSinceStartup+5;
        if(success){
            arena.CampaignMessage=action=="checkpoint"?"战局已保存，可继续交手或暂离。":"切磋已结算，伤势、装备损耗与经历已保存。";
            if(leave)ReturnFromPhysics();
        }else{
            Debug.LogWarning("Physics battle save failed: "+Client.Error);
            arena.PauseCampaign();arena.CampaignMessage="保存未完成："+Client.Error+"。可重试保存或结算。";
            // A lost response may have committed. Refresh the revision before retry.
            yield return Client.Request("/api/martial",null,j=>Martial=j,false);
            physicsFinishedAt=Time.realtimeSinceStartup+4;
        }
        string pending=queuedPhysicsCommand;queuedPhysicsCommand="";
        if(pending.Length>0&&PhysicsArena!=null)QueuePhysicsCommand(pending);
    }
    void ReturnFromPhysics(){
        bool active=Martial["battle"]["status"].S=="active";
        if(active)PhysicsArena.DeactivateCampaign();else ClosePhysicsArena();
        battleView=false;Go(battleReturnPage);
    }
    void ClosePhysicsArena(){
        if(PhysicsArena!=null){var old=PhysicsArena;PhysicsArena=null;var scene=old.gameObject.scene;old.DeactivateCampaign();SceneManager.UnloadSceneAsync(scene);}
        Time.timeScale=1;Client.PhysicsQuitFrame=null;physicsFinishedAt=-1;
    }
    void DrawBattleScreen(){
        Rect box=new Rect(355,225,970,485);Panel(box);
        var b=Martial["battle"];
        GUI.Label(new Rect(410,267,860,65),b["status"].S=="finished"?"切磋已收招":"江湖切磋",centerTitle);
        string message=!b.Has?"在人物档案选择一位成年、体力充足的人，约定切磋。":b["status"].S=="finished"?"此次交手已归档，结果和装备损耗见世界年谱。":"正在展开物理战场…";
        GUI.Label(new Rect(430,365,820,100),message,text);
        if(GUI.Button(new Rect(595,520,490,55),"返回功法与闭关",selected))battleView=false;
        if(b["event_id"].Has&&GUI.Button(new Rect(595,595,490,45),"查看此战年谱",chip)){battleView=false;OpenEvent(b["event_id"].I);}
    }
    public void ShowBattleForSmoke(){Page=3;battleView=true;personScreen=dialogueOpen=false;}
    public bool BattlePausedForSmoke=>PhysicsArena==null||PhysicsArena.paused;
    public void RunBattleForSmoke(bool run){if(PhysicsArena!=null)PhysicsArena.RunCampaign(run);}
    public IEnumerator SavePhysicsForSmoke(string command){while(physicsSending)yield return null;yield return SendPhysicsCommand(command);}
}
}
