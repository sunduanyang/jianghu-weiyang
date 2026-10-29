using System.Collections;
using UnityEngine;

namespace Jianghu {
// A read-only gallery. It does not create people, alter saves or drive combat.
public sealed class CharacterCast24Showcase:MonoBehaviour {
    public bool Ready {get;private set;}
    public bool Open=true,Walking,Left;
    public int Selected;
    JianghuApp app;
    bool appWasEnabled;
    Font font;
    GUIStyle title,label,small,button;
    string error="";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot(){if(GameClient.Arg("-cast24Showcase")=="1")new GameObject("24 original shadow puppet gallery").AddComponent<CharacterCast24Showcase>();}
    IEnumerator Start(){
        font=Resources.Load<Font>("Fonts/NotoSerifSC-Regular");
        if(!CharacterCastCatalog.Available){error="24 套角色资源尚未加载，请检查 Cast24/catalog。";yield break;}
        GameClient client;
        do{yield return null;app=FindAnyObjectByType<JianghuApp>();client=FindAnyObjectByType<GameClient>();}while(app==null||client==null||!client.Ready||!client.World.Has);
        appWasEnabled=app.enabled;app.enabled=false;
        Ready=true;Debug.Log("CAST24_SHOWCASE_READY "+CharacterCastCatalog.Entries.Count);
    }
    public void SetOpen(bool value){
        if(Open==value)return;Open=value;
        if(app!=null){if(value){appWasEnabled=app.enabled;app.enabled=false;}else app.enabled=appWasEnabled;}
    }
    void Fill(Rect r,Color colour){var prior=GUI.color;GUI.color=colour;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=prior;}
    void Styles(){
        if(title!=null)return;
        title=new GUIStyle(GUI.skin.label){font=font,fontSize=30};title.normal.textColor=new Color(.94f,.88f,.71f);
        label=new GUIStyle(title){fontSize=19};small=new GUIStyle(label){fontSize=15};button=new GUIStyle(GUI.skin.button){font=font,fontSize=18};
    }
    void OnGUI(){
        Styles();var prior=GUI.matrix;GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1680f,Screen.height/1000f,1));
        try{
            if(!Open){if(GUI.Button(new Rect(1430,940,220,40),"24 角色图鉴",button))SetOpen(true);return;}
            Fill(new Rect(0,0,1680,1000),new Color(.08f,.11f,.10f));
            GUI.Label(new Rect(40,30,1220,48),"二十四角色 · 原画拆件绑定",title);
            GUI.Label(new Rect(42,82,1500,32),"选择角色查看全身与头像 · 原脸原色 · 17 张独立透明部件 · 同一套展示与战斗渲染器",label);
            if(GUI.Button(new Rect(40,122,160,36),Walking?"暂停步行":"播放步行",button))Walking=!Walking;
            if(GUI.Button(new Rect(215,122,160,36),Left?"面朝右":"面朝左",button))Left=!Left;
            if(GUI.Button(new Rect(1450,38,190,42),"返回游戏",button))SetOpen(false);
            if(!Ready){GUI.Label(new Rect(50,200,1450,60),error.Length>0?error:"正在载入角色资源…",label);return;}
            var entries=CharacterCastCatalog.Entries;Selected=Mathf.Clamp(Selected,0,entries.Count-1);
            float phase=Mathf.Repeat(Time.unscaledTime/1.9f,1);string clip=Walking?"walk":"bind";
            for(int i=0;i<entries.Count;i++){
                var entry=entries[i];var cell=new Rect(40+(i%6)*194,180+(i/6)*196,184,184);
                Fill(cell,i==Selected?new Color(.34f,.31f,.22f):new Color(.17f,.21f,.18f));
                if(GUI.Button(cell,GUIContent.none,GUIStyle.none))Selected=i;
                if(Event.current.type==EventType.Repaint)CharacterArt.DrawFigure(new Rect(cell.x+10,cell.y+7,cell.width-20,145),CharacterCastCatalog.AppearanceId(entry),clip,phase,Left,false);
                GUI.Label(new Rect(cell.x+8,cell.y+151,cell.width-16,27),(i+1).ToString("00")+" · "+entry.Label,small);
            }
            var selected=entries[Selected];string id=CharacterCastCatalog.AppearanceId(selected);
            Fill(new Rect(1220,180,420,772),new Color(.20f,.24f,.20f));
            GUI.Label(new Rect(1240,198,380,35),selected.Label,label);
            GUI.Label(new Rect(1240,236,380,27),selected.Id,small);
            if(Event.current.type==EventType.Repaint){
                CharacterArt.DrawFigure(new Rect(1235,285,390,635),id,clip,phase,Left,false);
                CharacterArt.DrawPortrait(new Rect(1510,193,105,105),id);
            }
        }finally{GUI.matrix=prior;}
    }
    void OnDestroy(){if(app!=null&&Open)app.enabled=appWasEnabled;}
}
}
