using System;
using System.Collections;
using System.IO;
using System.Linq;
using Jianghu;
using UnityEngine;

public sealed class OriginalPreview : MonoBehaviour {
    public GameObject playerPrefab,enemyPrefab;
    public OriginalRig player,enemy;
    Vector2 leftStart,rightStart;
    bool heldLeft,heldRight,leftMouseSide;
    bool automated;
    string output;
    IEnumerator Start() {
        var args=Environment.GetCommandLineArgs();
        for(int i=0;i+1<args.Length;i++)if(args[i]=="-recoveryCapture"){automated=true;output=args[i+1];}
        Application.targetFrameRate=60;
        Physics2D.simulationMode=SimulationMode2D.FixedUpdate;
        Time.fixedDeltaTime=1f/60;
        player=Create(playerPrefab,true,new Vector3(-1.4f,.05f,0));
        enemy=Create(enemyPrefab,false,new Vector3(1.4f,.05f,0));
        player.opponent=enemy;enemy.opponent=player;
        player.BeginOriginalRound();enemy.BeginOriginalRound();
        yield return null;
        if(automated) {
            Directory.CreateDirectory(output);
            yield return new WaitForSeconds(3);
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
                "renderers",renderers,"complete_port",false,"runtime_parity_verified",false);
            File.WriteAllText(Path.Combine(output,"hierarchy-preview.json"),report.ToString());
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"original-hierarchy.png"));
            yield return new WaitForSeconds(.5f);
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
        if(player==null||automated)return;
        player.movement=(Input.GetKey(KeyCode.D)||Input.GetKey(KeyCode.RightArrow)?1:0)-(Input.GetKey(KeyCode.A)||Input.GetKey(KeyCode.LeftArrow)?1:0);
        Vector2 mouse=Input.mousePosition;
        if(Input.GetMouseButtonDown(0)) {
            leftMouseSide=mouse.x<Screen.width*.5f;
            if(leftMouseSide){leftStart=mouse;heldLeft=true;}else{rightStart=mouse;heldRight=true;}
            player.OnOriginalScreenTap(leftMouseSide);
        }
        if(Input.GetMouseButton(0)) {
            if(leftMouseSide)player.leftInput=Nub(mouse-leftStart);else player.rightInput=Nub(mouse-rightStart);
        }
        if(Input.GetMouseButtonUp(0)){if(leftMouseSide)heldLeft=false;else heldRight=false;}
        player.SetOriginalHandInputs(player.leftInput,player.rightInput,heldLeft,heldRight);
        if(Mathf.Abs(player.movement)>.01f)player.KeepOriginalMove(player.movement);
        if(Input.GetKeyDown(KeyCode.Space))player.StartOriginalKick();
        if(Input.GetKeyDown(KeyCode.Escape))Application.Quit();
    }
    Vector2 Nub(Vector2 delta)=>Vector2.ClampMagnitude(delta/(Screen.height/480f),155);
    void OnGUI() {
        GUI.color=Color.white;
        GUI.Label(new Rect(20,12,Screen.width-40,25),"Bloody Bastards — original hierarchy recovery / engineering preview");
        GUI.Label(new Rect(20,Screen.height-40,Screen.width-40,25),"A / D: move    Hold and drag either half of the screen: corresponding hand    Esc: quit");
    }
}
