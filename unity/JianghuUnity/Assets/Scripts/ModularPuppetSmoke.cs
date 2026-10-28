using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;

namespace Jianghu {
public sealed class ModularPuppetSmoke:MonoBehaviour {
    string output;PhysicsDuel arena;ModularPuppetControl ui;
    readonly List<J> checks=new List<J>();readonly List<string> errors=new List<string>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot(){if(GameClient.Arg("-modularSmoke").Length>0)new GameObject("Modular puppet native acceptance").AddComponent<ModularPuppetSmoke>();}
    void OnEnable(){Application.logMessageReceived+=Log;}void OnDisable(){Application.logMessageReceived-=Log;}
    void Log(string value,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(value);}
    void Check(string name,bool pass,J facts=null){checks.Add(J.O("check",name,"pass",pass,"facts",facts??J.O()));}
    IEnumerator Start(){
        output=GameClient.Arg("-modularSmoke");Directory.CreateDirectory(output);Directory.CreateDirectory(Path.Combine(output,"frames"));
        float until=Time.realtimeSinceStartup+30;while(Time.realtimeSinceStartup<until){arena=PhysicsDuel.Instance;ui=ModularPuppetControl.Instance;if(arena!=null&&arena.player!=null&&ui!=null)break;yield return null;}
        if(arena==null||ui==null){Check("workshop created",false);Finish();yield break;}
        var run=Run();while(true){object current=null;bool more=false;try{more=run.MoveNext();if(more)current=run.Current;}catch(Exception error){errors.Add(error.ToString());break;}if(!more)break;yield return current;}
        Check("no runtime errors",errors.Count==0,new J(errors.Select(e=>new J(e)).ToList()));Finish();
    }
    IEnumerator Picture(string name){for(int i=0;i<3;i++)yield return new WaitForEndOfFrame();var t=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(output,name+".png"),t.EncodeToPNG());Destroy(t);}
    string VisualHash(){
        var small=RenderTexture.GetTemporary(256,256,0,RenderTextureFormat.ARGB32);Graphics.Blit(arena.player.puppet.Texture,small);
        var prior=RenderTexture.active;RenderTexture.active=small;var t=new Texture2D(256,256,TextureFormat.RGBA32,false);t.ReadPixels(new Rect(0,0,256,256),0,0);t.Apply();RenderTexture.active=prior;
        string result;using(var sha=SHA256.Create())result=Convert.ToBase64String(sha.ComputeHash(t.GetRawTextureData()));Destroy(t);RenderTexture.ReleaseTemporary(small);return result;
    }
    J AuditMotion(int mode){
        ui.SetMotion(mode);ui.PreviewPlaying=false;
        float dt=1f/120,maxSpeed=0,maxGap=0,maxLean=0;int unsupported=0;
        var actors=new[]{arena.player,arena.enemy};var previous=actors.Select(a=>a.ArtAngles()).ToArray();
        var originalRoot=arena.player.root;
        for(int frame=0;frame<7200;frame++){
            ui.StepArtPreview(dt);
            for(int i=0;i<actors.Length;i++){
                var current=actors[i].ArtAngles();
                foreach(var pair in current)maxSpeed=Mathf.Max(maxSpeed,Mathf.Abs(Mathf.DeltaAngle(previous[i][pair.Key],pair.Value))/dt);
                previous[i]=current;maxGap=Mathf.Max(maxGap,actors[i].ReferenceSkeletonGap());maxLean=Mathf.Max(maxLean,actors[i].ArtTorsoLean);
                if(actors[i].ArtLowestAnkle>.131f)unsupported++;
            }
        }
        return J.O("preview_seconds",60,"sample_rate",120,"max_bone_degrees_per_second",maxSpeed,"max_joint_gap",maxGap,"max_torso_lean_degrees",maxLean,"both_feet_airborne_samples",unsupported,"same_character_root",ReferenceEquals(originalRoot,arena.player.root));
    }
    IEnumerator Run(){
        Screen.SetResolution(1600,950,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.3f);
        arena.BeginModularAcceptance();arena.PauseCampaign();
        yield return Picture("01-workshop");
        var bodyCount=arena.player.ReferenceBodyCount;string physical=arena.player.CapturePhysicalState().ToString();
        yield return new WaitForSecondsRealtime(1);
        Check("workshop starts in a stable standing pose",arena.paused&&!ui.Battle&&ui.MotionMode==0&&physical==arena.player.CapturePhysicalState().ToString());
        Check("real physics rig without backend or save",arena.player.Reference&&arena.enemy.Reference&&FindAnyObjectByType<GameClient>()==null&&!arena.Campaign,J.O("bodies",bodyCount));
        Check("actual weighted meshes",arena.player.puppet.MixedVertices>140&&arena.player.puppet.VertexCount==419&&arena.player.puppet.MaxWeightError<.00001f,arena.player.puppet.Probe());
        var looks=new HashSet<string>();
        for(int eye=0;eye<3;eye++)for(int brow=0;brow<3;brow++)for(int lip=0;lip<3;lip++){
            arena.player.PuppetEyes=eye;arena.player.PuppetBrows=brow;arena.player.PuppetLips=lip;
            yield return new WaitForEndOfFrame();yield return new WaitForEndOfFrame();looks.Add(VisualHash());
        }
        Check("27 facial combinations produce distinct live renders",looks.Count==27,J.O("distinct",looks.Count));
        Check("appearance changes preserve frozen physical state",physical==arena.player.CapturePhysicalState().ToString()&&bodyCount==arena.player.ReferenceBodyCount);
        arena.player.PuppetEyes=2;arena.player.PuppetBrows=2;arena.player.PuppetLips=2;arena.player.PuppetOutfit=1;arena.enemy.PuppetOutfit=0;
        yield return Picture("02-outfits-swapped");
        string jade=VisualHash();arena.player.PuppetOutfit=0;yield return new WaitForEndOfFrame();yield return new WaitForEndOfFrame();
        Check("outfit changes keep UV and topology",jade!=VisualHash()&&arena.player.puppet.VertexCount==419&&physical==arena.player.CapturePhysicalState().ToString());
        var prior=RenderTexture.active;RenderTexture.active=arena.player.puppet.Texture;
        var sample=new Texture2D(1024,1024,TextureFormat.RGBA32,false);sample.ReadPixels(new Rect(0,0,1024,1024),0,0);sample.Apply();RenderTexture.active=prior;
        var pixels=sample.GetPixels32();int clear=pixels.Count(p=>p.a==0),solid=pixels.Count(p=>p.a>240);Destroy(sample);
        Check("live character render has transparent background",clear>500000&&solid>5000,J.O("transparent_pixels",clear,"opaque_pixels",solid));
        ui.Bones=true;yield return Picture("03-mesh-and-bones");ui.Bones=false;
        arena.player.PuppetEyes=0;arena.player.PuppetBrows=0;arena.player.PuppetLips=0;arena.player.PuppetOutfit=0;arena.enemy.PuppetOutfit=1;
        var walk=AuditMotion(1);
        Check("60-second walking preview has bounded motion and a planted foot",walk["max_bone_degrees_per_second"].N<180&&walk["max_joint_gap"].N<.001&&walk["max_torso_lean_degrees"].N<3&&walk["both_feet_airborne_samples"].I==0&&walk["same_character_root"].B,walk);
        ui.PreviewPlaying=true;
        for(int i=0;i<12;i++){yield return new WaitForSecondsRealtime(.1f);yield return Picture("frames/walk-"+i.ToString("D2"));}
        Check("walking preview deforms weighted garment meshes",arena.paused&&arena.player.puppet.MaxHemDeformation>.005f&&arena.player.puppet.Finite,arena.player.puppet.Probe());
        ui.PreviewPlaying=false;string frozen=arena.player.CapturePhysicalState().ToString();yield return new WaitForSecondsRealtime(.5f);
        Check("pausing a preview freezes the pose",frozen==arena.player.CapturePhysicalState().ToString());
        var raise=AuditMotion(2);
        Check("60-second arm preview stays upright without spins",raise["max_bone_degrees_per_second"].N<180&&raise["max_joint_gap"].N<.001&&raise["max_torso_lean_degrees"].N<3&&raise["both_feet_airborne_samples"].I==0&&raise["same_character_root"].B,raise);
        ui.PreviewPlaying=true;
        for(int i=0;i<18;i++){
            if(i==8){arena.player.PuppetOutfit=1;arena.player.PuppetEyes=1;arena.player.PuppetBrows=1;arena.player.PuppetLips=1;}
            yield return new WaitForSecondsRealtime(.1f);yield return Picture("frames/gesture-"+i.ToString("D2"));
        }
        ui.SetBattle(true);yield return Picture("04-physical-arena");
        bool blocked=false;try{arena.player.PoseArtPreview(0,0,0);}catch(InvalidOperationException){blocked=true;}
        Check("physical arena starts paused and rejects preview posing",arena.paused&&blocked);
        arena.RunCampaign(true);yield return new WaitForSecondsRealtime(.3f);arena.PauseCampaign();
        ui.SetBattle(false);yield return Picture("05-return-to-standing");
        Check("returning from combat rebuilds a clean standing preview",arena.paused&&ui.MotionMode==0&&arena.player.PuppetOutfit==1&&arena.enemy.PuppetOutfit==1&&arena.player.ReferenceSkeletonGap()<.001);
        Check("both facing directions remain valid",arena.player.facing>0&&arena.enemy.facing<0&&arena.player.puppet.Finite&&arena.enemy.puppet.Finite,J.O("left",arena.player.puppet.Probe(),"right",arena.enemy.puppet.Probe()));
        Screen.SetResolution(1280,800,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.3f);yield return Picture("06-compact");
        arena.PauseCampaign();File.WriteAllText(Path.Combine(output,"physical-frame.json"),arena.CaptureCampaign().ToString());
    }
    void Finish(){bool pass=checks.All(c=>c["pass"].B);File.WriteAllText(Path.Combine(output,"modular-puppet-native.json"),J.O("pass",pass,"checks",new J(checks),"errors",new J(errors.Select(e=>new J(e)).ToList())).ToString());Application.Quit(pass?0:1);}
}
}
