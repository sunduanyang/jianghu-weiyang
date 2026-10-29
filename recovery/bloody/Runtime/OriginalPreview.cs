using System;
using System.Collections;
using System.IO;
using System.Linq;
using Jianghu;
using UnityEngine;

public sealed partial class OriginalPreview : MonoBehaviour {
    public GameObject playerPrefab,enemyPrefab;
    public OriginalRig player,enemy;
    public OriginalBattleHud hud;
    public bool ExternalInputReplay {get;set;}
    bool automated;
    float captureSeconds=3;
    int impacts;
    string output;
    IEnumerator Start() {
        var args=Environment.GetCommandLineArgs();
        InitializeDesktopPresentation(args);
        for(int i=0;i+1<args.Length;i++)if(args[i]=="-recoveryCapture"){automated=true;output=args[i+1];}
        for(int i=0;i+1<args.Length;i++)if(args[i]=="-captureSeconds"&&float.TryParse(args[i+1],out var seconds))captureSeconds=Mathf.Clamp(seconds,1,60);
        if(automated||Array.IndexOf(args,"-validateOriginalEquipment")>=0||Array.IndexOf(args,"-validateOriginalLifecycle")>=0)AudioListener.volume=0;
        Application.targetFrameRate=60;
        Physics2D.simulationMode=SimulationMode2D.FixedUpdate;
        Time.fixedDeltaTime=1f/60;
        originalInput.Bind(hud==null?null:hud.transform);
        InitializeOriginalEnvironment();
        if(OriginalRecoveryValidation.TryStartPractice(this))yield break;
        if(OriginalRecoveryValidation.TryStart(this))yield break;
        // Even the direct local arena bootstrap uses the original cover,
        // context switch and spawn sequence; it does not leave its blocking
        // Image in the imported initial state.
        yield return arenaTransition.CreateToArena(GameMode);
        OriginalOnboardingReplay.TryAttach(this);
        OriginalMatchReplay.TryAttach(this);
        TryStartOriginalMouseValidation();
        TryStartOriginalPaintValidation();
        TryStartOriginalTimeValidation();
        AttachPlaytestSetupMenu();
        OriginalPlaytestSetupValidation.TryAttach(this);
        if(Array.IndexOf(args,"-recoveryHideHud")>=0&&hud!=null)foreach(var canvas in hud.GetComponentsInChildren<Canvas>(true))canvas.enabled=false;
        yield return null;
        if(automated) {
            Directory.CreateDirectory(output);
            yield return new WaitForSecondsRealtime(captureSeconds);
            var bodies=player.GetComponentsInChildren<Rigidbody2D>(true);
            var active=bodies.Where(b=>b.gameObject.activeInHierarchy&&b.simulated).ToArray();
            var renderers=new J(player.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r=> {
                var mesh=r.sharedMesh;
                var colors=mesh==null?new Color[0]:mesh.colors;
                var materials=new J(r.sharedMaterials.Select(m=>m==null?J.O("missing",true):J.O(
                    "name",m.name,"shader",m.shader.name,"texture",m.mainTexture==null?"null":m.mainTexture.name,
                    "white",m.HasProperty("White")?m.GetColor("White").ToString():"absent",
                    "tint",m.HasProperty("Color_4F6CF892")?m.GetColor("Color_4F6CF892").ToString():"absent")).ToList());
                return J.O("name",r.name,"enabled",r.enabled,"active",r.gameObject.activeInHierarchy,
                    "mesh",mesh==null?"null":mesh.name,"vertices",mesh==null?0:mesh.vertexCount,
                    "color_count",colors.Length,"first_color",colors.Length==0?"absent":colors[0].ToString(),"materials",materials);
            }).ToList());
            var report=J.O("scope","Original resource/physics hierarchy preview; full gameplay is incomplete",
                "unity",Application.unityVersion,"gravity",Physics2D.gravity.y,
                "fixed_dt",Time.fixedDeltaTime,"velocity_iterations",Physics2D.velocityIterations,
                "position_iterations",Physics2D.positionIterations,"bodies",bodies.Length,
                "active_bodies",active.Length,"finite",active.All(b=>float.IsFinite(b.position.x)&&float.IsFinite(b.position.y)),
                "player_root_x",player.rootBody.position.x,"player_root_y",player.rootBody.position.y,
                "head_x",player.headCOM.position.x,"head_y",player.headCOM.position.y,
                "game_mode",GameMode,"campaign_level",Progress.Level,"campaign_stage",Progress.Stage,"level_type",Progress.LevelType,
                "environment_seed",environmentCustomizer.ChosenSeed,"environment_stage",environmentCustomizer.CurrentStage.name,
                "environment_hour",environmentCustomizer.CurrentContext.Hour,"environment_day",environmentCustomizer.CurrentContext.Day,
                "enemy_name",EnemyDisplayName,"player_health",player.health,"enemy_health",enemy==null?null:(object)enemy.health,"player_stamina",player.stamina,
                "player_equipment",J.O("left",player.leftItem==null?0:player.leftItem.itemId,"right",player.rightItem==null?0:player.rightItem.itemId),
                "enemy_equipment",J.O("left",enemy==null||enemy.leftItem==null?0:enemy.leftItem.itemId,"right",enemy==null||enemy.rightItem==null?0:enemy.rightItem.itemId),
                "enemy_ai",enemy==null?"none":enemy.OriginalAIState.ToString(),"practice_dummy",practiceDummy!=null,"impacts",impacts,"state",OriginalState,"end_reason",(int)EndReason,
                "phase_trace",new J(originalPhaseTrace),"round_time_remaining",RoundTimeRemaining,"time_scale",Time.timeScale,
                "camera",Camera.main==null?"missing":Camera.main.transform.position.ToString(),
                "renderers",renderers,"complete_port",false,"runtime_parity_verified",false);
            File.WriteAllText(Path.Combine(output,"hierarchy-preview.json"),report.ToString());
            yield return new WaitForEndOfFrame();
            OriginalRenderingDiagnostics.Capture(Camera.main,output);
            RenderTexture.active=null;
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"original-hierarchy.png"));
            yield return new WaitForSecondsRealtime(.5f);
            Application.Quit();
        }
    }
    OriginalRig Create(GameObject prefab,bool human,Vector3 position) {
        var go=Instantiate(prefab,position,prefab.transform.rotation);go.name=human?"Original player":"Original opponent";
        go.SetActive(true);
        var rig=go.AddComponent<OriginalRig>();rig.Initialize(human);
        return rig;
    }
    void Update() {
        HandleDesktopWindowKeys();
        AdvanceOriginalHostClock();
        if(player==null||automated||ExternalInputReplay||IsTransitioning)return;
        if(HandlePlaytestSetupKeys())return;
        if(Input.GetKeyDown(KeyCode.Escape))SetPaused(!Paused);
        if((OriginalState==5||OriginalState==6)&&(Input.GetKeyDown(KeyCode.Return)||Input.GetKeyDown(KeyCode.R))) {
            if(OriginalState==6)RestartOriginalMatch();else AdvanceOriginalRound();
        }
        ReadOriginalInput();
    }
    void OnGUI() {
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"-recoveryControlsOverlay")<0)return;
        GUI.color=Color.white;
        if(hud==null)GUI.Label(new Rect(20,12,Screen.width-40,25),"Bloody Bastards — recovery validation");
        GUI.Label(new Rect(20,Screen.height-26,Screen.width-40,25),"A / D: move   Left mouse: main hand   Right mouse: off hand   Space: kick   F2: loadout   F11: fullscreen   Esc: pause");
    }
}
