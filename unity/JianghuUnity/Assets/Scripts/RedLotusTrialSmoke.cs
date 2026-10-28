using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public sealed class RedLotusTrialSmoke:MonoBehaviour {
    readonly List<J> checks=new List<J>();readonly List<string> errors=new List<string>();string output;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot(){if(GameClient.Arg("-redLotusSmoke").Length>0)new GameObject("Red lotus trial acceptance").AddComponent<RedLotusTrialSmoke>();}
    void OnEnable(){Application.logMessageReceived+=Log;}void OnDisable(){Application.logMessageReceived-=Log;}
    void Log(string s,string trace,LogType kind){if(kind==LogType.Error||kind==LogType.Exception)errors.Add(s+"\n"+trace);}
    void Check(string label,bool pass,J facts=null){checks.Add(J.O("check",label,"pass",pass,"facts",facts??J.O()));File.WriteAllText(Path.Combine(output,"progress.json"),new J(checks).ToString());}
    IEnumerator Picture(string name){for(int i=0;i<3;i++)yield return new WaitForEndOfFrame();var t=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(output,name+".png"),t.EncodeToPNG());Destroy(t);}
    IEnumerator Start(){
        output=GameClient.Arg("-redLotusSmoke");Directory.CreateDirectory(output);
        var run=Run();while(true){object next=null;bool more=false;try{more=run.MoveNext();if(more)next=run.Current;}catch(Exception e){errors.Add(e.ToString());break;}if(!more)break;yield return next;}
        Check("no runtime errors",errors.Count==0,new J(errors.Select(s=>new J(s)).ToList()));
        File.WriteAllText(Path.Combine(output,"red-lotus-native.json"),J.O("pass",checks.All(c=>c["pass"].B),"checks",new J(checks),"errors",new J(errors.Select(s=>new J(s)).ToList())).ToString());Application.Quit(checks.All(c=>c["pass"].B)?0:1);
    }
    IEnumerator Run(){
        RedLotusTrialControl control=null;float until=Time.realtimeSinceStartup+120;
        while(Time.realtimeSinceStartup<until){control=FindAnyObjectByType<RedLotusTrialControl>();if(control!=null&&control.Ready)break;yield return null;}
        Check("private trial is ready",control!=null&&control.Ready);if(control==null||!control.Ready)yield break;
        int month=control.Client.World["month"].I;var source=Resources.Load<Texture2D>("Characters/RedLotus/sheet");var spec=J.Parse(Resources.Load<TextAsset>("Characters/RedLotus/index").text);
        Check("original atlas imported without resampling",source.width==1536&&source.height==1024,J.O("width",source.width,"height",source.height,"source_sha256",spec["source_sha256"]));
        Check("original art and face selected",CharacterArt.Look(control.AppearanceId).Art=="red-lotus"&&spec["heads"][0]["baked_features"].B);
        using(var renderer=new CharacterRenderer(1024,asset:"red-lotus")){
            var look=CharacterArt.Look(control.AppearanceId);bool finite=true;
            foreach(bool left in new[]{false,true})for(int i=0;i<24;i++){renderer.Render(look,CharacterPose.Sample("walk",i/24f,left));finite&=renderer.Probe()["finite"].B;}
            var facts=renderer.Probe();Check("48 walk poses stay finite in both directions",finite,facts);
            Check("16 original parts with face overlays disabled",facts["mesh_renderers"].I==16&&spec["spare_source_numbers"][0].I==2,facts);
            Check("head root stays behind collar",facts["neck_layer"].I<facts["collar_layer"].I);
        }
        Screen.SetResolution(1680,1000,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.3f);yield return Picture("workshop");
        control.Left=true;control.Bones=true;yield return Picture("workshop-bones");control.Left=false;control.Bones=false;
        control.ShowInteraction();until=Time.realtimeSinceStartup+15;while(!control.App.CharacterInteractionReadyForSmoke&&Time.realtimeSinceStartup<until)yield return null;
        Screen.SetResolution(2560,1440,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.3f);yield return Picture("interaction");
        Check("formal interaction uses source art at screen density",control.App.CharacterInteractionReadyForSmoke&&CharacterArt.LastFigureSampling["raster_height"].N>=CharacterArt.LastFigureSampling["draw_height"].N,CharacterArt.LastFigureSampling);
        Check("preview does not advance the world",control.Client.World["month"].I==month);
        yield return control.ShowBattle();Check("formal battle opens",control.App.PhysicsArena!=null);
        if(control.App.PhysicsArena!=null){
            var arena=control.App.PhysicsArena;yield return Picture("battle");
            Check("both real combat rigs render original source parts",arena.player.CharacterRenderFacts()["art"].S=="red-lotus"&&arena.enemy.CharacterRenderFacts()["art"].S=="red-lotus",J.O("hero",arena.player.CharacterRenderFacts(),"npc",arena.enemy.CharacterRenderFacts()));
            arena.RunCampaign(true);yield return new WaitForSecondsRealtime(.65f);yield return Picture("battle-motion");arena.PauseCampaign();
        }
        Check("UI character cache remains bounded",CharacterArt.Facts()["bytes"].N<=CharacterArt.Facts()["budget"].N,CharacterArt.Facts());
    }
}
}
