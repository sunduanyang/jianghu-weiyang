using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jianghu;
using UnityEngine;
using UnityEngine.SceneManagement;

// A separate, offline active-ragdoll arena. All contacts, joints and impulses
// are resolved by Unity Physics2D; the paper atlas only supplies the drawing.
public sealed partial class PhysicsDuel : MonoBehaviour {
    public static PhysicsDuel Instance;
    public const float Unit = 122, Ground = 728;
    [NonSerialized] public Fighter player, enemy;
    public bool started, training, paused, bones, muted, help;
    bool finished, smoke;
    bool fixtureMode;
    string result = "", notice = "拉开距离，观察对手的剑尖。", smokePath = "";
    float elapsed, resultAt, noticeUntil, shake, hurtFlash;
    int wins, losses, round = 1;
    public int hits, blocks, headHits, knockouts;
    public float strongestImpact;
    public PhysicsMaterial2D bodyMaterial,footMaterial,bladeMaterial,shieldMaterial;
    readonly List<HitPacket> contacts=new List<HitPacket>();
    public int rejectedHandleContacts,rejectedRestContacts,blockedBodyContacts,duplicateStrokeContacts;
    J catalog;
    Font font;
    Texture2D background, disk;
    AudioSource sound;
    AudioClip strikeSound, blockSound;
    readonly List<Spark> sparks = new List<Spark>();
    readonly List<FloatingText> floating = new List<FloatingText>();
    GUIStyle text, button;
    readonly Color ink = new Color(.13f,.19f,.18f), vermilion = new Color(.65f,.21f,.15f), jade = new Color(.22f,.39f,.34f);

    sealed class Spark { public Vector2 p,v; public float life,max; public Color color; }
    sealed class FloatingText { public Vector2 p; public string value; public float life; public Color color; }

    void Awake() {
        RememberPhysicsSettings();
        combatPhysics=gameObject.scene.GetPhysicsScene2D();
        Instance = this;
        InitializePhysicsBackend();
        Application.targetFrameRate = 90;
        Time.fixedDeltaTime = 1f/120;
        Physics2D.simulationMode=SimulationMode2D.FixedUpdate;
        Physics2D.gravity = new Vector2(0,-18);
        Physics2D.velocityIterations = 50;
        Physics2D.positionIterations = 60;
        Physics2D.defaultContactOffset=.005f;
        Physics2D.IgnoreLayerCollision(11,9,true);
        Physics2D.IgnoreLayerCollision(11,10,false);
        Physics2D.IgnoreLayerCollision(11,11,true);
        bodyMaterial=Material("布衣",.22f,0);footMaterial=Material("鞋底",.9f,0);
        bladeMaterial=Material("剑刃",.7f,.10f);shieldMaterial=Material("木盾",.7f,.10f);
        font = Resources.Load<Font>("Fonts/NotoSerifSC-Regular");
        catalog = J.Parse(Resources.Load<TextAsset>("PaperCast/index").text);
        background = PaperBackground();
        disk = new Texture2D(96,96,TextureFormat.RGBA32,false);
        var pixels = new Color[96*96];
        for(int y=0;y<96;y++) for(int x=0;x<96;x++) pixels[y*96+x] = new Color(1,1,1,Mathf.Clamp01((47.5f-Vector2.Distance(new Vector2(x,y),new Vector2(47.5f,47.5f)))*1.4f));
        disk.SetPixels(pixels); disk.Apply();
        sound = gameObject.AddComponent<AudioSource>();
        if(FindAnyObjectByType<AudioListener>()==null)gameObject.AddComponent<AudioListener>();
        strikeSound = Tone(false); blockSound = Tone(true);
        StaticBox("地面",new Vector2(0,-.3f),new Vector2(22,.6f));
        // Keep the same inner faces at +/-6. The source passed-out follow
        // motor can move up to 100 m/s; a thin discrete wall lets it tunnel.
        StaticBox("左界",new Vector2(-7.5f,3),new Vector2(3,8));
        StaticBox("右界",new Vector2(7.5f,3),new Vector2(3,8));
        smoke=GameClient.Arg("-physicalCombatSmoke").Length>0||GameClient.Arg("-interactionGameplaySmoke").Length>0||GameClient.Arg("-craftsSmoke").Length>0;
        ResetRound();
        if(Campaign){
            started=true;paused=true;
            // Bind the native joint reference angles at the authored pose
            // before restoring body transforms. Unity creates these native
            // constraints lazily on the first simulation step.
            var previousMode=Physics2D.simulationMode;
            Physics2D.simulationMode=SimulationMode2D.Script;
            Physics2D.SyncTransforms();SimulateCombat(.00001f);
            Physics2D.simulationMode=previousMode;
            contacts.Clear();queuedContacts.Clear();
            RestoreCampaign(campaign.battle["frame"]);PauseCampaign();
        }

    }
    static string Arg(string key) { var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,key);return i>=0&&i+1<a.Length?a[i+1]:""; }
    static PhysicsMaterial2D Material(string name,float friction,float bounce)=>new PhysicsMaterial2D(name){friction=friction,bounciness=bounce};
    void StaticBox(string name,Vector2 p,Vector2 size) { var go=new GameObject(name);go.transform.SetParent(transform);go.layer=8;go.transform.position=p;go.AddComponent<BoxCollider2D>().size=size; }
    internal void ResetRound() {
        ResetPhysicsBackend();
        ConfigureReferencePhysics();
        hits=blocks=headHits=0;strongestImpact=0;
        controls.ReleaseAll();
        if(player!=null){player.ReleaseSourceRig();player.root.SetActive(false);Destroy(player.root);}
        if(enemy!=null){enemy.ReleaseSourceRig();enemy.root.SetActive(false);Destroy(enemy.root);}
        contacts.Clear();
        queuedContacts.Clear();clashes.Clear();colliderIds.Clear();nextColliderId=0;
        DuelWeapon playerSpec=Campaign?DuelWeapon.CampaignWeapon(campaign.battle["fighters"][0]["weapon"]):DuelWeapon.All[playerWeaponIndex];
        DuelWeapon enemySpec=Campaign?DuelWeapon.CampaignWeapon(campaign.battle["fighters"][1]["weapon"]):DuelWeapon.All[enemyWeaponIndex];
        // Long points must not begin interpenetrating the opposing shaft.
        // Restoring an existing battle still uses its authoritative body pose.
        float playerExtent=.9f+playerSpec.reach*Mathf.Abs(Mathf.Cos(playerSpec.readyAngle*Mathf.Deg2Rad));
        float enemyExtent=.9f+enemySpec.reach*Mathf.Abs(Mathf.Cos(enemySpec.readyAngle*Mathf.Deg2Rad));
        float opening=Mathf.Max(2.05f,(playerExtent+enemyExtent+.4f)*.5f);
        player = new Fighter(this,catalog,Campaign?campaign.playerSkin:"m-youth-farmer",-opening,1,true,playerSpec,Campaign?campaign.battle["fighters"][0]:null);
        enemy = new Fighter(this,catalog,Campaign?campaign.enemySkin:"f-youth-farmer",opening,-1,false,enemySpec,Campaign?campaign.battle["fighters"][1]:null);
        finished=false; result=""; elapsed=0; sparks.Clear();floating.Clear();
        Time.timeScale=1; paused=false; help=false; hurtFlash=0;
        notice=ReferenceCombat?"按住鼠标拖动双手；挥动速度决定命中，松开回收手臂。":"左键主手，右键副手；两键同按同时控制，松开保持 / 续势，中键收住。"; noticeUntil=7;
        if(UseCorePhysics)CoreWorld.Synchronize();
    }
    void Begin(bool practice) { training=practice; started=true; help=false; round=1;wins=losses=0;ResetRound(); }
    void Update() {
        bool freezeAI=!Live||CampaignHidden;player?.SourceAIClockPaused(freezeAI);enemy?.SourceAIClockPaused(freezeAI);
        if(CampaignHidden)return;
        if(!smoke&&!ModularTrial) {
            if(!Campaign&&Input.GetKeyDown(KeyCode.Return)){if(armory)ApplyArmory();else if(!started)Begin(false);}
            if(Input.GetKeyDown(KeyCode.Escape)){if(armory)CloseArmory();else if(started)paused=!paused;}
            if(!Campaign&&Input.GetKeyDown(KeyCode.W)){if(armory)CloseArmory();else OpenArmory();}
            if(!Campaign&&Input.GetKeyDown(KeyCode.R)&&started) { if(finished)round++;ResetRound(); }
            if(Input.GetKeyDown(KeyCode.H)) help=!help;
            if(Input.GetKeyDown(KeyCode.V)) bones=!bones;
            if(Input.GetKeyDown(KeyCode.M)) muted=!muted;
        }
        Time.timeScale=(paused||help||armory)&&started?0:1;
        if(!Live){controls.ReleaseAll(true);player.ClearMouseCapture();}
        else if(!smoke)controls.Poll(ToCanvas,player.MouseSeed(true),player.MouseSeed(false),player.MousePixels(true),player.MousePixels(false),player.MouseAxis(true),player.MouseAxis(false));
        if(!started||paused||help||armory) return;
        if(!finished) elapsed+=Time.deltaTime;
        for(int i=sparks.Count-1;i>=0;i--) { var p=sparks[i];p.life-=Time.deltaTime;p.v+=Vector2.down*8*Time.deltaTime;p.p+=p.v*Time.deltaTime;if(p.life<=0)sparks.RemoveAt(i); }
        for(int i=floating.Count-1;i>=0;i--) { floating[i].life-=Time.deltaTime;floating[i].p+=Vector2.up*.5f*Time.deltaTime;if(floating[i].life<=0)floating.RemoveAt(i); }
        shake=Mathf.MoveTowards(shake,0,Time.unscaledDeltaTime*15);hurtFlash=Mathf.MoveTowards(hurtFlash,0,Time.unscaledDeltaTime*2);
        if(!fixtureMode){player.ReadInput(enemy,CampaignAutoplay);enemy.ReadAI(player,training);}
        if(!finished&&(player.health<=0||enemy.health<=0)) {
            finished=true;resultAt=Clock;knockouts++;
            if(player.health<=0&&enemy.health<=0)result="两败俱伤";
            else if(player.health>0) { result=training?"练习完成":"你赢了这一场";wins++; }
            else { result="胜败乃兵家常事";losses++; }
            notice=Campaign?"切磋结束，正在保存交手结果。":player.health>0?"漂亮的一击。按 R 再来一场。":"调整距离与盾牌位置，再试一次。";noticeUntil=9999;
        }
    }
    void FinishCampaignTimeout(){if(Campaign&&!finished&&elapsed>=180){finished=true;resultAt=Clock;result="三分钟收招";}}
    void LateUpdate(){FinishCampaignTimeout();}
    void FixedUpdate() {
        if(player==null||CampaignHidden||paused||help||armory) return;
        if(fixtureMode)return;
        SimulateFighters();
    }
    void SimulateFighters(){
        int steps=ReferenceCombat?referenceSteps:1;
        float dt=Time.fixedDeltaTime/steps;
        for(referenceSubstep=0;referenceSubstep<steps;referenceSubstep++){
            Clock+=dt;ResolveContacts();physicsStep++;
            player.Step(started&&!finished,enemy);
            enemy.Step(started&&!finished,player);
            SimulateCombat(dt);
            if(ReferenceCombat){player.FinishReferencePhysics();enemy.FinishReferencePhysics();}
        }
    }
    void OnApplicationFocus(bool focus) {if(CampaignHidden)return;if(!smoke&&!focus&&player!=null){controls.ReleaseAll(true);player.ClearMouseCapture();if(started)paused=true;if(Campaign)campaign.command?.Invoke("checkpoint");} }
    public bool Live => started&&!paused&&!help&&!finished&&!armory;
    public bool IsSmoke => smoke;

    public void Contact(DuelContact weapon,DuelContact other,DuelCollision collision) {
        if(ReferenceCombat){ReferenceContact(weapon,other,collision);return;}
        if(!Live||weapon.owner==other.owner||weapon.kind!="sword"||collision.contactCount==0)return;
        weapon.owner.RecordWeaponResistance(weapon.limb);
        bool guardContact=other.kind=="shield"||other.kind=="sword"||other.kind=="hilt";
        // A harmless shaft / guard still blocks another weapon. Suppressing
        // these contacts removes both feedback and guard effort from a bind.
        if(collision.otherCollider!=weapon.limb.shape&&!guardContact){rejectedHandleContacts++;return;}
        var contact=collision.GetContact(0);
        Vector2 relative=weapon.limb.VelocityAt(contact.point)-other.limb.VelocityAt(contact.point);
        float speed=Mathf.Max(0,-Vector2.Dot(relative,contact.normal));
        if(speed<.01f)separatingContacts++;
        uint ownId=ColliderId(collision.otherCollider),otherId=ColliderId(collision.collider);
        ulong directed=Pair(ownId,otherId);if(!queuedContacts.Add(directed))return;
        uint guardOwn=ColliderId(weapon.limb.shape),guardOther=ColliderId(other.limb.shape);
        bool ownFirst=guardOwn<guardOther;
        ulong guardPair=ownFirst?Pair(guardOwn,guardOther):Pair(guardOther,guardOwn);
        float fraction=Vector2.Dot(contact.point-weapon.limb.A,(weapon.limb.B-weapon.limb.A).normalized)/weapon.limb.length;
        int ownStroke=weapon.owner.StrikeId(weapon.limb),otherStroke=other.owner.StrikeId(other.limb);
        if(other.kind!="shield"&&other.kind!="sword"&&other.kind!="hilt")weapon.owner.TrackStrikeContact(other.owner,weapon.limb);
        contacts.Add(new HitPacket {weapon=weapon,other=other,point=contact.point,normal=contact.normal,relativeVelocity=relative,speed=speed,bladeFraction=fraction,step=physicsStep,strokeId=ownStroke,directedPair=directed,guardPair=guardPair,strokeLow=ownFirst?ownStroke:otherStroke,strokeHigh=ownFirst?otherStroke:ownStroke});
    }
    void ResolveContacts() {
        if(contacts.Count==0){queuedContacts.Clear();return;}
        // Resolve guard impacts first, then check each body contact's own
        // incoming path. Exposed targets remain hittable in the same step.
        foreach(var hit in contacts.OrderByDescending(h=>h.blocked))ResolveHit(hit);
        contacts.Clear();
        queuedContacts.Clear();
    }
    void ResolveHit(HitPacket hit) {
        var weapon=hit.weapon;var other=hit.other;
        if(!Live||weapon==null||other==null)return;
        float speed=hit.speed;Vector2 point=hit.point;
        if(hit.blocked) {
            ResolveClash(hit);
            return;
        }
        Fighter target=other.owner;
        if(target.health<=0)return;
        DuelWeapon strike=weapon.limb.strikeProfile??weapon.owner.weapon;
        if(hit.bladeFraction<strike.HitFraction){rejectedHandleContacts++;return;}
        if(speed<DuelImpact.MinimumSpeed){rejectedRestContacts++;return;}
        if(GuardOccludes(hit)){blockedBodyContacts++;return;}
        if(!weapon.owner.RegisterHit(target,hit.strokeId,weapon.limb)){duplicateStrokeContacts++;return;}
        if(SameStepGuardContact(hit))exposedGuardHits++;
        weapon.owner.lastHit=Clock;
        bool head=other.kind=="head";
        float damage=Mathf.Min(target.health,DuelImpact.Damage(strike.mass,speed,strike.damage,weapon.owner.damageScale,target.damageReduction,other.kind));
        // Follow the actual incoming cut, including reverse cuts and thrusts.
        Vector2 direction=hit.relativeVelocity.normalized;
        direction=new Vector2(direction.x,Mathf.Clamp(direction.y,-.55f,.55f)).normalized;
        Vector2 impulse=direction*Mathf.Min(3.5f,damage*.11f);
        target.Hit(damage,impulse,point);hits++;weapon.owner.landedHits++;if(head)headHits++;
        strongestImpact=Mathf.Max(strongestImpact,speed);
        Impact(point,false,damage);
        floating.Add(new FloatingText {p=point+Vector2.up*.2f,value=(head?"要害 ":"")+Mathf.RoundToInt(damage),life=.85f,color=vermilion});
        notice=head?"命中要害 · 注意保护头部":"命中 · 趁对手失衡调整站位";noticeUntil=elapsed+1.7f;
        if(target==player)hurtFlash=.12f;
    }
    void Impact(Vector2 p,bool block,float damage) {
        shake=Mathf.Min(4,block?1.5f:damage*.13f);
        for(int i=0;i<(block?12:8);i++)sparks.Add(new Spark {p=p,v=UnityEngine.Random.insideUnitCircle*(block?3.3f:1.6f)+Vector2.up,life=.25f+i*.018f,max=.5f,color=block?new Color(.78f,.52f,.19f):vermilion});
        if(!muted){sound.pitch=block?1.08f:Mathf.Lerp(1.1f,.8f,Mathf.Clamp01(damage/30));sound.PlayOneShot(block?blockSound:strikeSound,block?.26f:Mathf.Lerp(.12f,.30f,Mathf.Clamp01(damage/30)));}
    }
    static AudioClip Tone(bool metal) {
        int n=6615;var a=new float[n];var random=new System.Random(metal?17:42);
        for(int i=0;i<n;i++) {float t=i/44100f,env=Mathf.Exp(-t*(metal?35:48));a[i]=((float)random.NextDouble()-.5f)*env*.55f+Mathf.Sin(2*Mathf.PI*(metal?1350:110)*t)*env*.23f;}
        var clip=AudioClip.Create(metal?"剑盾相击":"受击",n,1,44100,false);clip.SetData(a,0);return clip;
    }

    public static Vector2 Point(J p)=>new Vector2((float)p[0].N,(float)p[1].N);
    public static Vector2 ScreenPoint(Vector2 p)=>new Vector2(800+p.x*Unit,Ground-p.y*Unit);
    static Vector2 ToCanvas(Vector2 screen) {float s=Mathf.Min(Screen.width/1600f,Screen.height/950f);return new Vector2((screen.x-(Screen.width-1600*s)/2)/s,((Screen.height-screen.y)-(Screen.height-950*s)/2)/s);}
    void Fill(Rect r,Color color) {Color c=GUI.color;GUI.color=color;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=c;}
    public void Circle(Vector2 center,Vector2 size,Color color) {Color c=GUI.color;GUI.color=color;GUI.DrawTexture(new Rect(center-size/2,size),disk);GUI.color=c;}
    public void Line(Vector2 a,Vector2 b,float width,Color color) {Matrix4x4 m=GUI.matrix;GUI.matrix=m*Matrix4x4.Translate(a)*Matrix4x4.Rotate(Quaternion.Euler(0,0,Mathf.Atan2(b.y-a.y,b.x-a.x)*Mathf.Rad2Deg))*Matrix4x4.Translate(-a);Fill(new Rect(a.x,a.y-width/2,(b-a).magnitude,width),color);GUI.matrix=m;}
    void Label(Rect r,string value,int size,Color color,TextAnchor align=TextAnchor.MiddleLeft) {text.fontSize=size;text.normal.textColor=color;text.alignment=align;GUI.Label(r,value,text);}
    bool Button(Rect r,string value,bool primary=false) {bool hover=r.Contains(Event.current.mousePosition);Fill(r,primary?(hover?new Color(.28f,.43f,.37f):jade):new Color(.19f,.27f,.24f,hover?.16f:.08f));button.normal.textColor=primary?new Color(.97f,.93f,.82f):ink;return GUI.Button(r,value,button);}
    void OnGUI() {
        if(CampaignHidden)return;
        if(ModularTrial&&ModularPuppetControl.Instance!=null){ModularPuppetControl.Instance.DrawWorkshop();return;}
        if(text==null){text=new GUIStyle(GUI.skin.label){font=font,wordWrap=true};button=new GUIStyle(GUI.skin.button){font=font,fontSize=23,normal={background=null},hover={background=null},active={background=null},focused={background=null}};}
        float s=Mathf.Min(Screen.width/1600f,Screen.height/950f);GUI.matrix=Matrix4x4.TRS(new Vector3((Screen.width-1600*s)/2,(Screen.height-950*s)/2),Quaternion.identity,new Vector3(s,s,1));
        GUI.DrawTexture(new Rect(0,0,1600,950),background);
        Circle(new Vector2(1220,270),new Vector2(210,210),new Color(.92f,.79f,.55f,.36f));
        DrawScenery();
        Matrix4x4 steady=GUI.matrix;
        if(shake>.1f)GUI.matrix*=Matrix4x4.Translate(new Vector3(Mathf.Sin(Time.unscaledTime*122)*shake,Mathf.Cos(Time.unscaledTime*97)*shake*.5f));
        player.Draw(bones);enemy.Draw(bones);
        foreach(var p in sparks)Circle(ScreenPoint(p.p),Vector2.one*(p.life*6+1),new Color(p.color.r,p.color.g,p.color.b,p.life/p.max));
        foreach(var f in floating)Label(new Rect(ScreenPoint(f.p)-new Vector2(70,25),new Vector2(140,50)),f.value,29,new Color(f.color.r,f.color.g,f.color.b,f.life),TextAnchor.MiddleCenter);
        GUI.matrix=steady;
        if(hurtFlash>0)Fill(new Rect(0,145,1600,620),new Color(.7f,.1f,.05f,hurtFlash));
        DrawHud();
        if(Campaign)DrawCampaignPanel();
        else if(armory)DrawArmory();
        else if(!started)DrawWelcome();
        else if(paused)DrawPause();
        else if(help)DrawHelp();
        else if(finished&&Clock-resultAt>1.3f)DrawResult();
        if(LaceTrial&&PuppetTrialControl.Instance!=null)PuppetTrialControl.Instance.DrawOverlay();
        GUI.matrix=Matrix4x4.identity;
    }
    void DrawScenery() {
        var faint=new Color(.21f,.29f,.25f,.15f);
        for(int i=0;i<8;i++) {
            float x=65+i*15;Line(new Vector2(x,718),new Vector2(x-42,270+i*12),3,faint);
            for(int j=0;j<5;j++){Vector2 p=new Vector2(x-26,355+j*62);Line(p,p+new Vector2(40,20),2,faint);Line(p,p+new Vector2(-37,-20),2,faint);}
        }
        Line(new Vector2(70,736),new Vector2(1530,736),5,new Color(.24f,.28f,.23f,.55f));
        Line(new Vector2(70,744),new Vector2(1530,744),1,new Color(.31f,.3f,.22f,.3f));
        for(int x=160;x<1500;x+=180) {Line(new Vector2(x,724),new Vector2(x,630),7,new Color(.25f,.28f,.24f,.25f));Circle(new Vector2(x,628),Vector2.one*13,new Color(.25f,.28f,.24f,.25f));}
        Line(new Vector2(160,650),new Vector2(1420,650),2,new Color(.28f,.3f,.24f,.19f));
        Label(new Rect(1320,387,70,220),"点到\n为止",35,new Color(.23f,.32f,.27f,.24f),TextAnchor.MiddleCenter);
        Label(new Rect(75,912,300,30),"江 湖 未 央  /  点 到 为 止",14,new Color(.24f,.32f,.28f,.65f));
        Label(new Rect(1250,912,275,30),"江 湖 切 磋",14,new Color(.24f,.32f,.28f,.65f),TextAnchor.MiddleRight);
    }
    void DrawHud() {
        string nearLabel=(Campaign?player.displayName+" / 你 · ":"布衣客 / 你 · ")+player.weapon.name;
        string farLabel=(Campaign?enemy.displayName:"黄裳客")+" / "+(training?"陪练":"对手")+" · "+enemy.weapon.name;
        Label(new Rect(72,33,480,37),nearLabel,Mathf.Clamp(470/Mathf.Max(1,nearLabel.Length),16,25),ink);
        Label(new Rect(1048,33,480,37),farLabel,Mathf.Clamp(470/Mathf.Max(1,farLabel.Length),16,25),ink,TextAnchor.MiddleRight);
        Bar(new Rect(72,83,480,12),player.health/player.maxHealth,jade,false);Bar(new Rect(1048,83,480,12),enemy.health/enemy.maxHealth,vermilion,true);
        Bar(new Rect(72,105,290,4),player.StaminaRatio,player.StaminaRatio<.2f?vermilion:new Color(.61f,.49f,.27f),false);Bar(new Rect(1238,105,290,4),enemy.StaminaRatio,enemy.StaminaRatio<.2f?vermilion:new Color(.61f,.49f,.27f),true);
        Label(new Rect(72,114,480,24),"气血 "+Mathf.CeilToInt(player.health)+"/"+Mathf.CeilToInt(player.maxHealth)+"    气力 "+Mathf.CeilToInt(player.stamina)+"/"+Mathf.CeilToInt(player.maxStamina)+"  "+player.CombatState,14,ink);
        Label(new Rect(1048,114,480,24),"气血 "+Mathf.CeilToInt(enemy.health)+"/"+Mathf.CeilToInt(enemy.maxHealth)+"    气力 "+Mathf.CeilToInt(enemy.stamina)+"/"+Mathf.CeilToInt(enemy.maxStamina)+"  "+enemy.CombatState,14,ink,TextAnchor.MiddleRight);
        Bar(new Rect(72,144,180,3),player.BalanceRatio,jade,false);Bar(new Rect(1348,144,180,3),enemy.BalanceRatio,jade,true);
        Label(new Rect(260,136,292,18),"架势 "+Mathf.CeilToInt(player.balance)+"  疲劳 "+Mathf.RoundToInt(player.swingHeat*100)+"%",12,ink);
        Label(new Rect(1048,136,292,18),"架势 "+Mathf.CeilToInt(enemy.balance)+"  疲劳 "+Mathf.RoundToInt(enemy.swingHeat*100)+"%",12,ink,TextAnchor.MiddleRight);
        Label(new Rect(600,30,400,40),(Campaign?"江 湖 切 磋":"纸 上 争 锋"),31,ink,TextAnchor.MiddleCenter);
        Label(new Rect(600,78,400,30),training?"自 由 练 习":"第 "+round+" 场   ·   "+((int)elapsed/60).ToString("00")+":"+((int)elapsed%60).ToString("00"),17,ink,TextAnchor.MiddleCenter);
        Line(new Vector2(72,156),new Vector2(1528,156),1,new Color(.2f,.28f,.23f,.2f));
        if(started&&!finished)Label(new Rect(450,184,700,35),elapsed<noticeUntil?notice:"距离、角度与挥动速度，决定这一剑。",18,new Color(.32f,.38f,.30f),TextAnchor.MiddleCenter);
        DrawControls();
        if(Button(new Rect(72,178,128,34),Campaign?"暂歇 Esc":"兵器 W")){if(Campaign)paused=true;else OpenArmory();}
        if(Button(new Rect(1400,178,128,34),"操作 H"))help=!help;
    }
    void Bar(Rect r,float ratio,Color c,bool reverse) {Fill(r,new Color(.15f,.22f,.19f,.12f));float w=r.width*Mathf.Clamp01(ratio);Fill(new Rect(reverse?r.xMax-w:r.x,r.y,w,r.height),c);}
    void Panel(Rect r) {Fill(new Rect(0,158,1600,640),new Color(.9f,.87f,.77f,.66f));Fill(r,new Color(.94f,.91f,.82f,.98f));Line(r.min,new Vector2(r.xMax,r.y),2,new Color(.24f,.36f,.30f,.6f));}
    void DrawWelcome() {
        Rect r=new Rect(478,252,644,408);Panel(r);
        Label(new Rect(520,283,560,30),"江湖未央 · 物理格斗试作",18,jade,TextAnchor.MiddleCenter);
        Label(new Rect(520,331,560,78),"纸上争锋",68,ink,TextAnchor.MiddleCenter);
        Label(new Rect(540,424,520,58),"分清握持，顺势发力。\n试试用一记有分量的挥砍，打破对手的平衡。",20,ink,TextAnchor.MiddleCenter);
        if(Button(new Rect(537,520,246,63),"进入擂台  ↵",true))Begin(false);
        if(Button(new Rect(809,520,246,63),"自由练习"))Begin(true);
        if(Button(new Rect(650,602,300,38),"选择双方兵器  W"))OpenArmory();
    }
    void DrawPause() {
        Panel(new Rect(520,290,560,332));Label(new Rect(550,322,500,60),"歇 一 歇",42,ink,TextAnchor.MiddleCenter);
        if(Button(new Rect(580,410,440,55),"继续交手",true))paused=false;
        if(Button(new Rect(580,480,210,52),"重新开局"))ResetRound();
        if(Button(new Rect(810,480,210,52),training?"切换对战":"切换练习")){training=!training;ResetRound();}
        if(Button(new Rect(580,548,210,43),"兵器选择 W"))OpenArmory();
        if(Button(new Rect(810,548,210,43),"返回首页")){started=false;paused=false;ResetRound();}
    }
    void DrawHelp() {
        Panel(new Rect(400,230,800,480));Label(new Rect(445,258,710,55),"交 手 要 诀",38,ink,TextAnchor.MiddleCenter);
        if(ReferenceCombat){
            Label(new Rect(450,335,700,245),"按住左键拖动主手，按住右键拖动副手；松开手臂回收。\n触屏左右区域各控制一只手，双击该侧并按住移步。\nA / D 移步 · S 沉身 · Space 踢击 · Esc 暂歇\n挥动兵器形成速度，再利用真实碰撞击中对手。\n盾牌和兵器靠接触挡击；身体暴露的部位仍可被击中。\n连续小圈急挥更耗气，力竭会倒地三秒再尝试起身。\n四肢分别受伤，断手会失去握持；V 查看碰撞框。",20,ink);
            if(Button(new Rect(570,620,460,52),"明白了 · 返回擂台",true))help=false;return;
        }
        Label(new Rect(450,335,700,245),"左键主手、右键副手；两键同按，鼠标同时驱动两手。\n切换不跳姿，松开的一手保留目标；无需拖两个摇杆。\n松开后直刺向外续伸、到最远挺住；转圈劈砍带阻力续转。\n中键收住双手续势；静态姿态保持，挥动与顶压仍耗气。\n双手兵器受固定握距约束；无盾副手可独立出拳。\nA/D 移步 · Space 进身 · J 单次变招 · L 刺 · K 守\n触屏保留双手轮盘 · S 沉身 · Esc 暂歇、保存或收招。",20,ink);
        if(Button(new Rect(570,620,460,52),"明白了 · 返回擂台",true))help=false;
    }
    void DrawResult() {
        Panel(new Rect(500,260,600,360));Label(new Rect(530,292,540,65),result,40,ink,TextAnchor.MiddleCenter);
        Label(new Rect(540,376,520,63),"本局用时 "+((int)elapsed)+" 秒\n累计战绩 "+wins+" 胜 · "+losses+" 负",21,jade,TextAnchor.MiddleCenter);
        if(Button(new Rect(560,476,480,59),"再来一场  R",true)){round++;ResetRound();}
        if(Button(new Rect(560,552,480,38),"返回首页")){started=false;ResetRound();}
    }
    static Texture2D PaperBackground() {
        const int w=800,h=475;var t=new Texture2D(w,h,TextureFormat.RGB24,false);var p=new Color[w*h];var random=new System.Random(119);
        for(int y=0;y<h;y++)for(int x=0;x<w;x++) {
            float grain=((float)random.NextDouble()-.5f)*.028f;
            float vignette=Mathf.Pow(Mathf.Abs((float)x/w-.5f)*1.25f,2)*.045f;
            Color c=new Color(.915f+grain-vignette,.891f+grain-vignette,.813f+grain-vignette);
            float screenY=h-y;
            for(int layer=0;layer<3;layer++) {float ridge=225+layer*47+Mathf.Sin(x*.007f+layer*3)*36+Mathf.Sin(x*.019f+layer*2)*18+Mathf.Sin(x*.036f+layer)*9;if(screenY>ridge&&screenY<365)c=Color.Lerp(c,new Color(.34f,.45f,.40f),.035f+layer*.012f);}
            if(screenY>369)c=Color.Lerp(c,new Color(.65f,.61f,.48f),.13f);
            p[y*w+x]=c;
        }
        t.SetPixels(p);t.Apply();return t;
    }


}

public sealed class DuelContact : MonoBehaviour {
    [NonSerialized] public PhysicsDuel.Fighter owner;
    [NonSerialized] public PhysicsDuel.Limb limb;
    public string kind;
    void OnCollisionEnter2D(Collision2D collision) {DeliverCore(new DuelCollision(collision),true);}
    void OnCollisionStay2D(Collision2D collision) {DeliverCore(new DuelCollision(collision),false);}
    public void DeliverCore(DuelCollision collision,bool entering) {
        if(!entering&&owner!=null&&owner.Reference)return;
        var own=collision.otherCollider.GetComponent<DuelContact>();if(own==null)return;
        if(own.kind=="foot"&&collision.collider.gameObject.layer==8) {
            for(int i=0;i<collision.contactCount;i++)if(collision.GetContact(i).normal.y>.5f)own.limb.lastGroundContact=PhysicsDuel.Instance.Clock;
        }
        if(own.kind!="sword"&&!(own.owner!=null&&own.owner.Reference&&own.kind=="shield"))return;
        var other=collision.collider.GetComponent<DuelContact>();if(other!=null)PhysicsDuel.Instance.Contact(own,other,collision);
    }
}
