using System.Collections;
using UnityEngine;

namespace Jianghu {
[DefaultExecutionOrder(1200)]
public sealed class RedLotusTrialControl:MonoBehaviour {
    public bool Ready,Workshop=true,Walking=true,Left,Bones;
    public string AppearanceId {get;private set;}
    public GameClient Client {get;private set;}
    public JianghuApp App {get;private set;}
    public int NpcId {get;private set;}
    Font font;GUIStyle title,text,button;string error="";
    readonly Color ink=new Color(.15f,.21f,.19f),paper=new Color(.96f,.94f,.87f),red=new Color(.56f,.19f,.14f);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot(){if(GameClient.Arg("-redLotusTrial")=="1")new GameObject("Original red lotus atlas trial").AddComponent<RedLotusTrialControl>();}
    IEnumerator Start(){
        if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("Red lotus trial requires a separate saveDir");Application.Quit(2);yield break;}
        font=Resources.Load<Font>("Fonts/NotoSerifSC-Regular");
        do{yield return null;Client=FindAnyObjectByType<GameClient>();App=FindAnyObjectByType<JianghuApp>();}while(Client==null||App==null||!Client.Ready||!Client.World.Has);
        NpcId=Client.World["npcs"][0]["id"].I;AppearanceId=App.PaperSkinFor(Client.World["hero"],true);
        ShowWorkshop();Ready=true;Debug.Log("RED_LOTUS_TRIAL_READY "+AppearanceId);
    }
    public void ShowWorkshop(){if(App==null)return;if(App.PhysicsArena!=null)App.PhysicsArena.PauseCampaign();Workshop=true;App.enabled=false;Time.timeScale=1;}
    public void ShowInteraction(){Workshop=false;App.enabled=true;App.CharacterInteractionForSmoke(NpcId);}
    public IEnumerator ShowBattle(){
        if(App.PhysicsArena==null){
            bool accepted=false;yield return Client.Request("/api/intervene",J.O("action","challenge","npc_id",NpcId),j=>{App.Martial=j;accepted=true;});
            if(!accepted){error=Client.Error;yield break;}
        }
        Workshop=false;App.enabled=true;App.ShowBattleForSmoke();
        float until=Time.realtimeSinceStartup+15;while(App.PhysicsArena==null&&Time.realtimeSinceStartup<until)yield return null;
    }
    void Fill(Rect r,Color colour){var prior=GUI.color;GUI.color=colour;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=prior;}
    void Line(Vector2 a,Vector2 b){var prior=GUI.matrix;var c=GUI.color;GUI.color=new Color(.08f,.48f,.43f,.9f);GUIUtility.RotateAroundPivot(Mathf.Atan2(b.y-a.y,b.x-a.x)*Mathf.Rad2Deg,a);GUI.DrawTexture(new Rect(a.x,a.y-1,(b-a).magnitude,2),Texture2D.whiteTexture);GUI.matrix=prior;GUI.color=c;}
    void Figure(Rect cell,bool walk){
        Fill(cell,new Color(.88f,.88f,.79f));
        for(int y=0;y<17;y++)for(int x=0;x<19;x++)if((x+y)%2==0)Fill(new Rect(cell.x+x*40,cell.y+y*40,Mathf.Min(40,cell.xMax-cell.x-x*40),Mathf.Min(40,cell.yMax-cell.y-y*40)),new Color(.91f,.91f,.83f));
        GUI.Label(new Rect(cell.x+20,cell.y+12,cell.width-40,34),walk?"同一组分件 · 行走":"原素材绑定 · 静止",text);
        Rect r=new Rect(cell.x+32,cell.y+45,cell.width-64,cell.height-56);float phase=Mathf.Repeat(Time.unscaledTime/1.9f,1);
        if(Event.current.type==EventType.Repaint)CharacterArt.DrawFigure(r,AppearanceId,walk?"walk":"bind",phase,Left,false);
        if(Bones&&Event.current.type==EventType.Repaint){
            var pose=walk?CharacterPose.Sample("walk",phase,Left):CharacterPose.Bind(Left);float height=Mathf.Min(r.height,r.width/(.5f/.84f)),width=height*.5f/.84f;
            Rect target=new Rect(r.center.x-width*.5f,r.yMax-height,width,height);
            System.Func<Vector2,Vector2> map=p=>new Vector2(target.x+(p.x+.65f)/1.3f*target.width,target.yMax-(p.y+.096f)/2.184f*target.height);
            foreach(var b in pose.Bones.Values)Line(map(b.A),map(b.B));
        }
    }
    void OnGUI(){
        if(!Ready)return;
        if(title==null){title=new GUIStyle(GUI.skin.label){font=font,fontSize=32};title.normal.textColor=ink;text=new GUIStyle(title){fontSize=19};button=new GUIStyle(GUI.skin.button){font=font,fontSize=18};}
        var matrix=GUI.matrix;GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1680f,Screen.height/1000f,1));
        try{
            if(!Workshop){if(GUI.Button(new Rect(1460,950,200,36),"返回素材试装",button))ShowWorkshop();return;}
            Fill(new Rect(0,0,1680,1000),paper);Fill(new Rect(36,35,5,90),red);
            GUI.Label(new Rect(56,34,1500,50),"红莲皮影 · 原素材试装",title);
            GUI.Label(new Rect(58,91,1550,32),"右侧编号分件绑定现有骨架 · 原脸与原色 · 透明背景 · 独立存档",text);
            if(GUI.Button(new Rect(40,145,160,42),Walking?"暂停行走":"播放行走",button))Walking=!Walking;
            if(GUI.Button(new Rect(212,145,160,42),Left?"面朝右":"面朝左",button))Left=!Left;
            if(GUI.Button(new Rect(384,145,160,42),Bones?"隐藏骨架":"显示骨架",button))Bones=!Bones;
            if(GUI.Button(new Rect(1090,145,250,42),"查看正式人物交互",button))ShowInteraction();
            if(GUI.Button(new Rect(1352,145,288,42),"进入正式切磋试装",button))StartCoroutine(ShowBattle());
            Figure(new Rect(40,208,780,716),false);Figure(new Rect(860,208,780,716),Walking);
            GUI.Label(new Rect(40,943,1600,38),error.Length>0?error:"16 个实际显示分件 · 02 号保留作备用过渡片 · 大腿 / 小腿 / 独立靴分别校准 · 数字标签不进入人物",text);
        }finally{GUI.matrix=matrix;}
    }
}
}
