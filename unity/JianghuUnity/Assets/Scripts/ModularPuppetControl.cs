using System.Collections;
using UnityEngine;

namespace Jianghu {
[DefaultExecutionOrder(1100)]
public sealed class ModularPuppetControl:MonoBehaviour {
    public static ModularPuppetControl Instance;
    public bool Bones,Battle,PreviewPlaying=true;
    public int Selected;
    public int MotionMode;
    public float PreviewTime,WalkingBlend,RaisingBlend;
    GUIStyle text,button;
    Font font;
    PhysicsDuel arena;
    readonly Color ink=new Color(.12f,.21f,.20f),jade=new Color(.15f,.36f,.31f),paper=new Color(.96f,.94f,.87f),red=new Color(.65f,.23f,.18f);
    public PhysicsDuel.Fighter SelectedFighter=>Selected==0?arena.player:arena.enemy;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot(){if(PhysicsDuel.ModularTrial)new GameObject("Modular carved puppet workshop").AddComponent<ModularPuppetControl>();}
    IEnumerator Start(){
        Instance=this;font=Resources.Load<Font>("Fonts/NotoSerifSC-Regular");
        while(PhysicsDuel.Instance==null||PhysicsDuel.Instance.player==null)yield return null;
        arena=PhysicsDuel.Instance;arena.BeginModularAcceptance();arena.PauseCampaign();
        arena.player.PuppetOutfit=0;arena.enemy.PuppetOutfit=1;arena.enemy.PuppetEyes=1;arena.enemy.PuppetBrows=1;arena.enemy.PuppetLips=2;
        arena.ModularInput(true);PreparePreview();
    }
    void Update(){
        if(arena==null||arena.player==null)return;
        if(Input.GetKeyDown(KeyCode.Space)||Input.GetKeyDown(KeyCode.Escape)){if(Battle)arena.RunCampaign(arena.paused);else if(MotionMode!=0)PreviewPlaying=!PreviewPlaying;}
        if(Input.GetKeyDown(KeyCode.P))SetBattle(!Battle);
        if(Input.GetKeyDown(KeyCode.V))Bones=!Bones;
        if(Input.GetKeyDown(KeyCode.R))Reset();
        if(Input.GetKeyDown(KeyCode.Alpha1))ChooseOutfit(0);
        if(Input.GetKeyDown(KeyCode.Alpha2))ChooseOutfit(1);
        if(Input.GetKeyDown(KeyCode.E))SelectedFighter.PuppetEyes=(SelectedFighter.PuppetEyes+1)%3;
        if(Input.GetKeyDown(KeyCode.B))SelectedFighter.PuppetBrows=(SelectedFighter.PuppetBrows+1)%3;
        if(Input.GetKeyDown(KeyCode.L))SelectedFighter.PuppetLips=(SelectedFighter.PuppetLips+1)%3;
    }
    void LateUpdate(){if(arena!=null&&!Battle&&PreviewPlaying)StepArtPreview(Time.unscaledDeltaTime);}
    void PreparePreview(){
        MotionMode=0;PreviewTime=WalkingBlend=RaisingBlend=0;PreviewPlaying=true;
        arena.PauseCampaign();arena.player.PrepareArtPreview();arena.enemy.PrepareArtPreview();StepArtPreview(0);
    }
    public void SetMotion(int mode){MotionMode=Mathf.Clamp(mode,0,2);PreviewPlaying=true;}
    public void StepArtPreview(float dt){
        if(arena==null||Battle)return;
        dt=Mathf.Clamp(dt,0,.05f);
        if(MotionMode!=0||WalkingBlend>0||RaisingBlend>0)PreviewTime+=dt;
        WalkingBlend=Mathf.MoveTowards(WalkingBlend,MotionMode==1?1:0,dt*1.6f);
        RaisingBlend=Mathf.MoveTowards(RaisingBlend,MotionMode==2?1:0,dt*1.6f);
        arena.player.PoseArtPreview(PreviewTime,WalkingBlend,RaisingBlend);
        arena.enemy.PoseArtPreview(PreviewTime,WalkingBlend,RaisingBlend);
        Physics2D.SyncTransforms();
    }
    public void SetBattle(bool value){if(Battle==value)return;Battle=value;Reset();}
    void Reset(){
        int[] left={arena.player.PuppetEyes,arena.player.PuppetBrows,arena.player.PuppetLips,arena.player.PuppetOutfit};
        int[] right={arena.enemy.PuppetEyes,arena.enemy.PuppetBrows,arena.enemy.PuppetLips,arena.enemy.PuppetOutfit};
        arena.ResetRound();arena.BeginModularAcceptance();arena.PauseCampaign();
        int i=0;foreach(var actor in new[]{arena.player,arena.enemy}){var v=i++==0?left:right;actor.PuppetEyes=v[0];actor.PuppetBrows=v[1];actor.PuppetLips=v[2];actor.PuppetOutfit=v[3];}
        arena.ModularInput(!Battle);if(!Battle)PreparePreview();
    }
    void ChooseOutfit(int value){SelectedFighter.PuppetOutfit=value;}
    void Box(Rect r,Color c){var prior=GUI.color;GUI.color=c;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=prior;}
    void Label(Rect r,string value,int size,Color? color=null,TextAnchor align=TextAnchor.MiddleLeft){text.fontSize=size;text.normal.textColor=color??ink;text.alignment=align;GUI.Label(r,value,text);}
    bool Button(Rect r,string value,bool active=false){
        bool hover=r.Contains(Event.current.mousePosition);Box(r,active?jade:new Color(.17f,.34f,.29f,hover?.16f:.07f));
        button.normal.textColor=active?paper:ink;return GUI.Button(r,value,button);
    }
    void ChoiceRow(float y,string title,string[] names,int value,System.Action<int> choose){
        Label(new Rect(1150,y,370,28),title,18);
        for(int i=0;i<3;i++)if(Button(new Rect(1150+i*127,y+35,118,38),names[i],value==i))choose(i);
    }
    public void DrawWorkshop(){
        if(arena==null||arena.player==null)return;
        if(text==null){text=new GUIStyle(GUI.skin.label){font=font,wordWrap=true};button=new GUIStyle(GUI.skin.button){font=font,fontSize=18,normal={background=null},hover={background=null},active={background=null},focused={background=null}};}
        float scale=Mathf.Min(Screen.width/1600f,Screen.height/950f);
        Matrix4x4 root=Matrix4x4.TRS(new Vector3((Screen.width-1600*scale)/2,(Screen.height-950*scale)/2),Quaternion.identity,new Vector3(scale,scale,1));GUI.matrix=root;
        Box(new Rect(0,0,1600,950),paper);
        Box(new Rect(35,32,6,68),red);Label(new Rect(58,29,650,49),"皮影工坊",37);
        Label(new Rect(60,79,900,30),"同骨 · 换面 · 易衣     /     江湖未央",17,new Color(.27f,.40f,.35f));
        Label(new Rect(1120,40,430,35),Battle?(arena.paused?"切磋待命":"手动切磋"):MotionMode==0?"站姿展示":!PreviewPlaying?"预览已暂停":MotionMode==1?"原地步行预览":"抬臂预览",20,jade,TextAnchor.MiddleRight);
        Label(new Rect(1020,81,530,28),"空格 暂停 / 继续   V 骨架与网格   P 工坊 / 擂台",15,null,TextAnchor.MiddleRight);
        Box(new Rect(35,128,Battle?1530:1060,754),new Color(.89f,.90f,.82f));
        for(int x=55;x<(Battle?1550:1080);x+=32)for(int y=148;y<860;y+=32)if((x/32+y/32)%2==0)Box(new Rect(x,y,32,32),new Color(.3f,.45f,.38f,.045f));
        if(Battle){
            Label(new Rect(75,143,900,33),"动作擂台 · 实际物理骨架与兵器",23);
            GUI.matrix=root*Matrix4x4.Translate(new Vector3(800,820))*Matrix4x4.Scale(new Vector3(1.65f,1.65f,1))*Matrix4x4.Translate(new Vector3(-800,-PhysicsDuel.Ground));
            arena.player.DrawModularPuppet(Bones,true);arena.enemy.DrawModularPuppet(Bones,true);GUI.matrix=root;
            if(Button(new Rect(1180,146,170,40),"返回工坊"))SetBattle(false);
            if(Button(new Rect(1365,146,170,40),arena.paused?"开始切磋":"暂停切磋"))arena.RunCampaign(arena.paused);
            Label(new Rect(75,828,1400,35),"手动切磋：空格开始 / 暂停，A / D 行走，鼠标左 / 右键驱动双手。R 复位。",18);
        }else{
            Label(new Rect(75,144,950,34),"同一骨架，两种服装，五官自由组合",23);
            Preview(arena.player,new Vector2(325,815),2.65f,root);
            Preview(arena.enemy,new Vector2(815,815),2.65f,root);
            GUI.matrix=root;
            if(Button(new Rect(180,822,290,40),"左侧 · "+(arena.player.PuppetOutfit==0?"丹青云纹":"素锦青竹"),Selected==0))Selected=0;
            if(Button(new Rect(665,822,290,40),"右侧 · "+(arena.enemy.PuppetOutfit==0?"丹青云纹":"素锦青竹"),Selected==1))Selected=1;
            Box(new Rect(1120,128,445,754),new Color(.985f,.972f,.92f));
            Label(new Rect(1150,143,370,35),Selected==0?"左侧人物 · 面容与衣装":"右侧人物 · 面容与衣装",23);
            var actor=SelectedFighter;
            if(actor.puppet!=null){
                // Enlarged head from the same live mesh render, with its own
                // orientation preserved rather than a separately drawn portrait.
                GUI.DrawTextureWithTexCoords(new Rect(1230,185,220,220),actor.puppet.Texture,actor.puppet.PortraitUV,true);
            }
            ChoiceRow(402,"眉形 · B",new[]{"清秀","英气","舒展"},actor.PuppetBrows,v=>actor.PuppetBrows=v);
            ChoiceRow(490,"眼形 · E",new[]{"丹凤","冷眸","含笑"},actor.PuppetEyes,v=>actor.PuppetEyes=v);
            ChoiceRow(578,"唇形 · L",new[]{"朱唇","微扬","淡绛"},actor.PuppetLips,v=>actor.PuppetLips=v);
            Label(new Rect(1150,668,370,27),"衣装 · 1 / 2",18);
            if(Button(new Rect(1150,705,180,42),"丹青云纹",actor.PuppetOutfit==0))ChooseOutfit(0);
            if(Button(new Rect(1340,705,180,42),"素锦青竹",actor.PuppetOutfit==1))ChooseOutfit(1);
            if(Button(new Rect(1150,766,180,40),MotionMode==1?"停止步行":"步行预览",MotionMode==1))SetMotion(MotionMode==1?0:1);
            if(Button(new Rect(1340,766,180,40),MotionMode==2?"停止抬臂":"抬臂预览",MotionMode==2))SetMotion(MotionMode==2?0:2);
            if(Button(new Rect(1150,822,180,40),Bones?"隐藏骨架":"骨架 / 网格",Bones))Bones=!Bones;
            if(Button(new Rect(1340,822,180,40),"手动切磋"))SetBattle(true);
        }
        Box(new Rect(35,903,1530,1),new Color(.3f,.4f,.33f,.3f));
        Label(new Rect(45,912,1100,26),"3 眉 × 3 眼 × 3 唇 × 2 衣 = 54 种组合     ·     腰腹与衣摆局部蒙皮     ·     背景与镂空透明",16);
        Label(new Rect(1230,912,330,26),Bones?"网格与真实骨段可见":"可交互制作样例",16,jade,TextAnchor.MiddleRight);
        GUI.matrix=Matrix4x4.identity;
    }
    void Preview(PhysicsDuel.Fighter actor,Vector2 foot,float zoom,Matrix4x4 root){
        var origin=PhysicsDuel.ScreenPoint(new Vector2(actor.Hip.x,0));
        GUI.matrix=root*Matrix4x4.Translate(foot)*Matrix4x4.Scale(new Vector3(zoom,zoom,1))*Matrix4x4.Translate(-origin);
        actor.DrawModularPuppet(Bones,false);
    }
}
}
