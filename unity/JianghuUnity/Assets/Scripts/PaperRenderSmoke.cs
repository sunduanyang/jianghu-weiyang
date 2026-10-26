using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
// Explicitly opt-in rendered attachment regression. Always use a disposable save.
public sealed class PaperRenderSmoke : MonoBehaviour {
    string output;JianghuApp app;GameClient client;
    readonly List<J> checks=new List<J>();
    readonly List<string> errors=new List<string>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot(){if(GameClient.Arg("-paperRegressionDir").Length==0)return;if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("Paper regression requires an isolated -saveDir");Application.Quit(1);return;}new GameObject("Paper rendered attachment regression").AddComponent<PaperRenderSmoke>();}
    void OnEnable(){Application.logMessageReceived+=Log;}
    void OnDisable(){Application.logMessageReceived-=Log;}
    void Log(string text,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(text);}
    void Check(string name,bool pass,J facts){checks.Add(J.O("check",name,"pass",pass,"facts",facts));}
    IEnumerator Start(){
        output=GameClient.Arg("-paperRegressionDir");Directory.CreateDirectory(output);
        float deadline=Time.realtimeSinceStartup+100;
        while(Time.realtimeSinceStartup<deadline){app=FindAnyObjectByType<JianghuApp>();client=FindAnyObjectByType<GameClient>();if(app!=null&&client!=null&&client.Ready&&client.World.Has)break;yield return null;}
        if(app==null||client==null||!client.Ready){Check("startup",false,J.O());Finish();yield break;}
        var iterator=Run();while(true){object current=null;bool more=false;try{more=iterator.MoveNext();if(more)current=iterator.Current;}catch(Exception error){errors.Add(error.ToString());break;}if(!more)break;yield return current;}
        Check("no runtime exceptions",errors.Count==0,new J(errors.Select(x=>new J(x)).ToList()));Finish();
    }
    IEnumerator Case(string name,string skin,string clip,bool left,bool gallery,bool capture){
        app.PaperSmokeStage();app.Go(5);
        if(gallery)app.PaperSmokeGallery(clip,left);else app.PaperSmokePose(skin,clip,left,true,.55f);
        app.ResetPaperGuiProbe();
        if(capture)yield return new WaitForSecondsRealtime(.65f);
        for(int i=0;i<4;i++)yield return new WaitForEndOfFrame();
        bool stable=app.PaperGuiProbe(out J facts);facts["clip"]=new J(clip);facts["left"]=new J(left);facts["gallery"]=new J(gallery);
        Check(name,stable,facts);
        if(capture){var image=ScreenCapture.CaptureScreenshotAsTexture();var pixels=image.GetPixels32();int lit=0;for(int i=0;i<pixels.Length;i+=997)if(pixels[i].r+pixels[i].g+pixels[i].b>60)lit++;Check(name+" visible screenshot",lit>100,J.O("lit_samples",lit));File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());Destroy(image);}
    }
    IEnumerator Run(){
        double calls=client.World["ai"]["calls"].N;int month=client.World["month"].I;
        bool quick=GameClient.Arg("-paperRegressionQuick")=="true";
        if(quick){yield return Case("large-idle-bones","m-youth-farmer","idle",false,false,true);yield return LiveCase("large-idle-playing","m-youth-farmer","idle",false);yield break;}
        // A hidden Windows player can initialize without a swapchain. Establish
        // a rendered viewport before inspecting any actual GUI matrices.
        Screen.SetResolution(1680,960,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.8f);
        Check("all skins and shared rig",app.ValidatePaperCast(out J rigFacts),rigFacts);
        string trial=GameClient.Arg("-paperTrialSkin");
        if(trial.Length>0){
            string stem=trial.Replace("reference-","");
            foreach(var size in new[]{new Vector2Int(1680,960),new Vector2Int(1280,800)}){
                Screen.SetResolution(size.x,size.y,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.4f);
                foreach(string clip in new[]{"idle","walk","talk","attack","guard","hurt","defeated"})foreach(bool left in new[]{false,true})
                    yield return Case(size.x+"-"+stem+"-"+clip+(left?"-left":"-right"),trial,clip,left,false,true);
            }
            Screen.SetResolution(1680,960,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.4f);
            bool facing=trial=="reference-bamboo";
            app.Go(5);app.PaperReferenceSkin(trial);app.PaperSmokeReference("idle",0,facing);yield return new WaitForSecondsRealtime(.4f);
            var still=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(output,stem+"-studio.png"),still.EncodeToPNG());Destroy(still);
            if(trial=="reference-plain"){app.PaperReferenceOverlay(true);yield return new WaitForEndOfFrame();yield return new WaitForEndOfFrame();var over=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(output,stem+"-overlay.png"),over.EncodeToPNG());Destroy(over);app.PaperReferenceOverlay(false);}
            if(GameClient.Arg("-paperCapture")=="true"){
                string frames=Path.Combine(output,"frames");Directory.CreateDirectory(frames);int frame=0;
                foreach(string clip in new[]{"walk","attack","guard"})for(int i=0;i<24;i++){
                    app.PaperSmokeReference(clip,i/23f,facing);yield return new WaitForEndOfFrame();yield return new WaitForEndOfFrame();
                    var image=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(frames,(frame++).ToString("D4")+".png"),image.EncodeToPNG());Destroy(image);
                }
            }
            yield return client.Refresh();Check("no simulation or model side effects",client.World["month"].I==month&&client.World["ai"]["calls"].N==calls,J.O("month",month,"new_model_calls",client.World["ai"]["calls"].N-calls));yield break;
        }
        foreach(string clip in new[]{"idle","walk","talk","attack","guard","hurt","defeated"})foreach(bool left in new[]{false,true})
            yield return Case("reference-"+clip+(left?"-left":"-right"),"reference-jade",clip,left,false,true);
        app.Go(5);app.PaperSmokeReference("idle",0);app.PaperReferenceOverlay(true);yield return new WaitForSecondsRealtime(.5f);
        var overlay=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(output,"reference-proportion-overlay.png"),overlay.EncodeToPNG());Destroy(overlay);app.PaperReferenceOverlay(false);
        app.Go(5);app.PaperSmokeReference("walk",.15f);yield return new WaitForSecondsRealtime(.5f);
        var reference=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(output,"reference-studio.png"),reference.EncodeToPNG());Destroy(reference);
        if(GameClient.Arg("-paperCapture")=="true"){
            string frames=Path.Combine(output,"frames");Directory.CreateDirectory(frames);int frame=0;
            foreach(string clip in new[]{"walk","attack","guard"})for(int i=0;i<24;i++){
                app.PaperSmokeReference(clip,i/23f);yield return new WaitForEndOfFrame();yield return new WaitForEndOfFrame();
                var image=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(frames,(frame++).ToString("D4")+".png"),image.EncodeToPNG());Destroy(image);
            }
        }
        foreach(var size in new[]{new Vector2Int(1280,800),new Vector2Int(1680,960),new Vector2Int(1920,1080),new Vector2Int(2560,1480)}){
            Screen.SetResolution(size.x,size.y,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.5f);
            string prefix=size.x+"x"+size.y;
            foreach(string clip in new[]{"idle","walk","talk","attack","guard","hurt","defeated"})foreach(bool left in new[]{false,true}){
                string name=prefix+"-"+clip+(left?"-left":"-right");
                yield return Case(name,"m-youth-farmer",clip,left,false,clip=="idle"&&!left||clip=="walk"&&!left||clip=="attack"&&left);
                yield return Case(name+"-all-skins","",clip,left,true,clip=="idle"&&!left||clip=="attack"&&left);
            }
        }
        yield return LiveCase("large-child-walk-playing","f-child","walk",false);
        yield return LiveCase("large-daoist-talk-playing","m-daoist","talk",true);
        yield return LiveCase("large-escort-attack-playing","m-escort","attack",false);
        yield return client.Refresh();Check("no simulation or model side effects",client.World["month"].I==month&&client.World["ai"]["calls"].N==calls,J.O("month",month,"new_model_calls",client.World["ai"]["calls"].N-calls));
    }
    IEnumerator LiveCase(string name,string skin,string clip,bool left){
        app.PaperSmokeStage();app.Go(5);app.PaperSmokePose(skin,clip,left,true,.55f);app.PaperSmokePlay(true);app.ResetPaperGuiProbe();
        yield return new WaitForSecondsRealtime(.8f);for(int i=0;i<4;i++)yield return new WaitForEndOfFrame();
        bool stable=app.PaperGuiProbe(out J facts);facts["skin"]=new J(skin);facts["clip"]=new J(clip);facts["left"]=new J(left);facts["playing"]=new J(true);Check(name,stable,facts);
        var image=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());Destroy(image);
    }
    void Finish(){File.WriteAllText(Path.Combine(output,"render-regression.json"),J.O("unity",Application.unityVersion,"checks",new J(checks),"passed",checks.Count>0&&checks.All(c=>c["pass"].B)).ToString());Application.Quit(checks.All(c=>c["pass"].B)?0:1);}
}
}
