using System.Collections;
using UnityEngine;

namespace Jianghu {
[DefaultExecutionOrder(1000)]
public sealed class PuppetTrialControl:MonoBehaviour {
    public static PuppetTrialControl Instance;
    public bool Inspect=true;
    public bool Bones;
    GUIStyle label;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot(){if(PhysicsDuel.LaceTrial)new GameObject("Carved puppet binding trial").AddComponent<PuppetTrialControl>();}
    IEnumerator Start(){
        Instance=this;
        while(PhysicsDuel.Instance==null||PhysicsDuel.Instance.player==null)yield return null;
        var arena=PhysicsDuel.Instance;
        if(!arena.Campaign){arena.started=true;arena.training=true;arena.muted=true;if(GameClient.Arg("-puppetTrialSmoke").Length==0)arena.paused=true;}
    }
    void Update(){if(Input.GetKeyDown(KeyCode.P))Inspect=!Inspect;if(Input.GetKeyDown(KeyCode.W))Inspect=false;if(PhysicsDuel.Instance!=null)Bones=PhysicsDuel.Instance.bones;}
    void Box(Rect rect,Color color){Color prior=GUI.color;GUI.color=color;GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=prior;}
    public void DrawOverlay(){
        var arena=PhysicsDuel.Instance;if(arena==null||arena.player==null)return;
        float scale=Mathf.Min(Screen.width/1600f,Screen.height/950f);
        Matrix4x4 root=Matrix4x4.TRS(new Vector3((Screen.width-1600*scale)/2,(Screen.height-950*scale)/2),Quaternion.identity,new Vector3(scale,scale,1));GUI.matrix=root;
        if(label==null)label=new GUIStyle(GUI.skin.label){font=Resources.Load<Font>("Fonts/NotoSerifSC-Regular"),fontSize=22,alignment=TextAnchor.MiddleCenter,normal={textColor=new Color(.12f,.2f,.2f)}};
        if(Inspect){
            Box(new Rect(40,170,1520,715),new Color(.91f,.91f,.84f,.98f));
            for(int y=220;y<860;y+=40)for(int x=70;x<1530;x+=40)if((x/40+y/40)%2==0)Box(new Rect(x,y,40,40),new Color(.35f,.48f,.45f,.035f));
            GUI.Label(new Rect(80,178,1440,35),"新皮影 · 实际战斗骨架近景   /   Esc 运行或暂停   P 返回擂台   V 骨架   R 复位   W 兵器",label);
            DrawPreview(arena.player,new Vector2(465,848),2.6f,root);
            DrawPreview(arena.enemy,new Vector2(1120,848),2.6f,root);
            GUI.matrix=root;
            GUI.Label(new Rect(220,842,480,35),arena.player.facing>=0?"面朝右":"面朝左",label);GUI.Label(new Rect(900,842,480,35),arena.enemy.facing>=0?"面朝右":"面朝左",label);
        }else GUI.Label(new Rect(410,150,780,32),"新皮影试绑 · P 放大查看 / V 骨架对照",label);
        GUI.matrix=Matrix4x4.identity;
    }
    void DrawPreview(PhysicsDuel.Fighter fighter,Vector2 foot,float zoom,Matrix4x4 root){
        Vector2 origin=PhysicsDuel.ScreenPoint(new Vector2(fighter.Hip.x,0));
        GUI.matrix=root*Matrix4x4.Translate(foot)*Matrix4x4.Scale(new Vector3(zoom,zoom,1))*Matrix4x4.Translate(-origin);
        fighter.DrawLacePuppet(Bones,false);
    }
}
}
