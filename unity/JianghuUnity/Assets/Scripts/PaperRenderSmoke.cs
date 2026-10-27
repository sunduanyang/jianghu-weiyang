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
        app.PaperSmokeStage();app.OpenSystem(SystemTab.Puppets);
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
        Screen.SetResolution(1680,960,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.8f);
        Check("24 skins, source sockets and walk",app.ValidatePaperCast(out J rigFacts),rigFacts);
        foreach(var size in new[]{new Vector2Int(1280,800),new Vector2Int(1680,960)}){
            Screen.SetResolution(size.x,size.y,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.4f);
            foreach(string clip in new[]{"bind","walk"})foreach(bool left in new[]{false,true}){
                string name=size.x+"-"+clip+(left?"-left":"-right");
                yield return Case(name,"m-youth-farmer",clip,left,false,true);
                yield return Case(name+"-all-skins","",clip,left,true,true);
            }
        }
        Screen.SetResolution(1680,960,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.4f);
        foreach(string id in new[]{"m-youth-farmer","f-youth-farmer","m-middle-farmer","f-middle-farmer","m-elder","f-elder","m-child","f-child","m-scholar","f-scholar","m-merchant","f-merchant","m-escort","f-escort","m-fisher","m-hunter","m-artisan","f-healer","m-performer","m-daoist","m-monk","m-official","m-beggar","f-swordswoman"}){
            app.PaperSmokeStage();app.OpenSystem(SystemTab.Puppets);app.PaperSmokeComparison(id,false,false);yield return new WaitForEndOfFrame();yield return new WaitForEndOfFrame();
            var image=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(output,"comparison-"+id+".png"),image.EncodeToPNG());Destroy(image);
            File.WriteAllText(Path.Combine(output,"comparison-"+id+".json"),app.PaperReferenceViewports().ToString());
        }
        yield return LiveCase("child-walking","f-child","walk",false);
        yield return LiveCase("daoist-walking","m-daoist","walk",true);
        string hips=Path.Combine(output,"hip-phases");Directory.CreateDirectory(hips);
        foreach(string id in new[]{"m-youth-farmer","m-middle-farmer","m-child","m-escort","f-escort","m-fisher","m-hunter","m-artisan","m-beggar"})for(int i=0;i<8;i++){
            app.OpenSystem(SystemTab.Puppets);app.PaperSmokeWalkComparison(id,i/8f);app.ResetPaperGuiProbe();
            yield return new WaitForEndOfFrame();yield return new WaitForEndOfFrame();
            Check("hip-phase-"+id+"-"+i,app.PaperGuiProbe(out J hipFacts),hipFacts);
            var hipImage=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(hips,id+"-"+i+".png"),hipImage.EncodeToPNG());Destroy(hipImage);
            if(i==0)File.WriteAllText(Path.Combine(hips,id+".json"),app.PaperReferenceViewports().ToString());
        }
        string frames=Path.Combine(output,"frames");Directory.CreateDirectory(frames);
        for(int i=0;i<24;i++){
            app.OpenSystem(SystemTab.Puppets);app.PaperSmokeWalkComparison("m-youth-farmer",i/24f);
            yield return new WaitForEndOfFrame();yield return new WaitForEndOfFrame();
            var frame=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(frames,i.ToString("D4")+".png"),frame.EncodeToPNG());Destroy(frame);
            if(i==0)File.WriteAllText(Path.Combine(output,"frame-viewports.json"),app.PaperReferenceViewports().ToString());
        }
        yield return client.Refresh();Check("no simulation or model side effects",client.World["month"].I==month&&client.World["ai"]["calls"].N==calls,J.O("month",month,"new_model_calls",client.World["ai"]["calls"].N-calls));
    }
    IEnumerator LiveCase(string name,string skin,string clip,bool left){
        app.PaperSmokeStage();app.OpenSystem(SystemTab.Puppets);app.PaperSmokePose(skin,clip,left,true,.55f);app.PaperSmokePlay(true);app.ResetPaperGuiProbe();
        yield return new WaitForSecondsRealtime(.8f);for(int i=0;i<4;i++)yield return new WaitForEndOfFrame();
        bool stable=app.PaperGuiProbe(out J facts);facts["skin"]=new J(skin);facts["clip"]=new J(clip);facts["left"]=new J(left);facts["playing"]=new J(true);Check(name,stable,facts);
        var image=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());Destroy(image);
    }
    void Finish(){File.WriteAllText(Path.Combine(output,"render-regression.json"),J.O("unity",Application.unityVersion,"checks",new J(checks),"passed",checks.Count>0&&checks.All(c=>c["pass"].B)).ToString());Application.Quit(checks.All(c=>c["pass"].B)?0:1);}
}
}
