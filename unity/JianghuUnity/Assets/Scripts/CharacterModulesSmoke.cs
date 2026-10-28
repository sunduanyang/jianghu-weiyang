using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;

namespace Jianghu {
public sealed class CharacterModulesSmoke:MonoBehaviour {
    GameClient client;JianghuApp app;string output;
    readonly List<J> checks=new List<J>();readonly List<string> errors=new List<string>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot(){
        if(GameClient.Arg("-characterModulesSmoke").Length==0&&GameClient.Arg("-appearanceSmoke").Length==0)return;
        if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("Character acceptance requires an isolated saveDir");Application.Quit(2);return;}
        new GameObject("Production character module acceptance").AddComponent<CharacterModulesSmoke>();
    }
    void OnEnable(){Application.logMessageReceived+=Log;}void OnDisable(){Application.logMessageReceived-=Log;}
    void Log(string s,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(s);}
    void Check(string name,bool pass,J facts=null){checks.Add(J.O("check",name,"pass",pass,"facts",facts??J.O()));File.WriteAllText(Path.Combine(output,"progress.json"),new J(checks).ToString());}
    IEnumerator Picture(string name){for(int i=0;i<3;i++)yield return new WaitForEndOfFrame();var t=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(output,name+".png"),t.EncodeToPNG());Destroy(t);}
    Texture2D Read(Texture source){var t=new Texture2D(source.width,source.height,TextureFormat.RGBA32,false);var rt=RenderTexture.GetTemporary(source.width,source.height);var prior=RenderTexture.active;Graphics.Blit(source,rt);RenderTexture.active=rt;t.ReadPixels(new Rect(0,0,t.width,t.height),0,0);t.Apply();RenderTexture.active=prior;RenderTexture.ReleaseTemporary(rt);return t;}
    string Hash(Texture source){var t=Read(source);string result;using(var sha=SHA256.Create())result=Convert.ToBase64String(sha.ComputeHash(t.GetRawTextureData()));Destroy(t);return result;}
    IEnumerator Start(){
        output=GameClient.Arg("-characterModulesSmoke",GameClient.Arg("-appearanceSmoke"));Directory.CreateDirectory(output);
        float until=Time.realtimeSinceStartup+120;while(Time.realtimeSinceStartup<until){client=FindAnyObjectByType<GameClient>();app=FindAnyObjectByType<JianghuApp>();if(client!=null&&client.Ready&&client.World.Has)break;yield return null;}
        if(client==null||!client.Ready||!client.World.Has){Check("backend ready",false);Finish();yield break;}
        var run=Run();while(true){object value=null;bool more=false;try{more=run.MoveNext();if(more)value=run.Current;}catch(Exception e){errors.Add(e.ToString());break;}if(!more)break;yield return value;}
        Check("no runtime errors",errors.Count==0,new J(errors.Select(e=>new J(e)).ToList()));Finish();
    }
    IEnumerator Run(){
        Screen.SetResolution(1680,1000,FullScreenMode.Windowed);yield return client.Request("/api/ai/config",J.O("enabled",false),j=>{});
        var people=client.World["npcs"].A;int month=client.World["month"].I;double calls=client.World["ai"]["calls"].N;
        Check("all NPC profiles migrated to saved modular features",people.Count>=24&&people.All(n=>n["appearance"]["version"].I==2&&n["appearance"]["eyes"].Has&&n["appearance"]["brows"].Has&&n["appearance"]["lips"].Has),J.O("people",people.Count));
        J detail=null;yield return client.Request("/api/npc/1",null,j=>detail=j);
        Check("roster and details share one appearance",detail!=null&&detail["appearance"].ToString()==people.First(n=>n["id"].I==1)["appearance"].ToString());
        app.AppearancePreview();var sample=app.AppearancePreviewPerson(people[0]);var first=CharacterArt.Look(app.PaperSkinFor(sample));
        sample["job"]=new J("书生");var other=CharacterArt.Look(app.PaperSkinFor(sample));Check("occupation changes clothes while preserving facial identity",first.FaceKey==other.FaceKey&&first.Role!=other.Role);
        var faces=new HashSet<string>();
        for(int e=0;e<3;e++)for(int b=0;b<3;b++)for(int l=0;l<3;l++){
            sample["appearance"]["eyes"]=new J(e);sample["appearance"]["brows"]=new J(b);sample["appearance"]["lips"]=new J(l);
            faces.Add(Hash(CharacterArt.Canvas(app.PaperSkinFor(sample),portrait:true)));
        }
        Check("27 independently rendered facial combinations",faces.Count==27,J.O("distinct",faces.Count));
        sample["age_months"]=new J(12*12);sample["sex"]=new J("男");int boy=CharacterArt.Look(app.PaperSkinFor(sample)).Head;
        sample["sex"]=new J("女");int girl=CharacterArt.Look(app.PaperSkinFor(sample)).Head;sample["age_months"]=new J(70*12);int elder=CharacterArt.Look(app.PaperSkinFor(sample)).Head;
        Check("child elder and monk silhouettes are distinct",boy==4&&girl==5&&elder==3&&CharacterLook.Parse(CharacterLook.ForPerson("m-monk",sample)).Head==6);
        sample=app.AppearancePreviewPerson(people[0]);Texture2D before=Read(CharacterArt.Canvas(app.PaperSkinFor(sample)));sample["appearance"]["palette"]=new J((sample["appearance"]["palette"].I+3)%8);Texture2D after=Read(CharacterArt.Canvas(app.PaperSkinFor(sample)));
        var a=before.GetPixels32();var z=after.GetPixels32();int alphaError=0,colours=0;
        for(int i=0;i<a.Length;i++){alphaError=Math.Max(alphaError,Math.Abs(a[i].a-z[i].a));if(a[i].a>128&&Math.Abs(a[i].r-z[i].r)+Math.Abs(a[i].g-z[i].g)+Math.Abs(a[i].b-z[i].b)>20)colours++;}
        Check("palette preserves transparent cutouts",alphaError<=1&&colours>100,J.O("alpha_error",alphaError,"changed_pixels",colours));Destroy(before);Destroy(after);
        Check("neck is behind the collar",CharacterRenderer.Catalog["head_layer"].I<CharacterRenderer.Catalog["collar_layer"].I);
        foreach(int sex in new[]{0,1})foreach(bool left in new[]{false,true}){app.AppearancePreview(sex,28,left,true);yield return Picture((sex==0?"men":"women")+(left?"-left":"-right"));}
        app.AppearancePreview(0,70);yield return Picture("elder-men");app.AppearancePreview(1,70);yield return Picture("elder-women");app.AppearancePreview(0,12);yield return Picture("boys");app.AppearancePreview(1,12);yield return Picture("girls");
        app.ViewPerson(1,PersonTab.Overview);float deadline=Time.realtimeSinceStartup+10;while(app.Person["id"].I!=1&&Time.realtimeSinceStartup<deadline)yield return null;yield return Picture("profile");app.PaperSmokeDialogue();yield return Picture("dialogue");
        app.CharacterInteractionForSmoke(1);deadline=Time.realtimeSinceStartup+15;
        while(!app.CharacterInteractionReadyForSmoke&&Time.realtimeSinceStartup<deadline)yield return null;
        foreach(int width in new[]{2560,3840}){
            Screen.SetResolution(width,width*9/16,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.4f);yield return Picture("interaction-"+width);
            var sampling=CharacterArt.LastFigureSampling;
            Check("interaction bust retains screen density at actual "+Screen.width+"x"+Screen.height,app.CharacterInteractionReadyForSmoke&&sampling["bust"].B&&sampling["crop_before_raster"].B&&sampling["raster_width"].N>=sampling["draw_width"].N&&sampling["raster_height"].N>=sampling["draw_height"].N,J.O("requested_width",width,"screen_width",Screen.width,"screen_height",Screen.height,"sampling",sampling));
        }
        Screen.SetResolution(1680,1000,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.3f);
        for(int i=0;i<260;i++){
            var n=app.AppearancePreviewPerson(people[0]);var p=n["appearance"];p["palette"]=new J(i%8);p["eyes"]=new J(i/8%3);p["brows"]=new J(i/24%3);p["lips"]=new J(i/72%3);p["face_width"]=new J(i%5);
            CharacterArt.Canvas(app.PaperSkinFor(n),size:384);if(i%8==0)yield return null;
        }
        Check("NPC images use one shared studio and bounded cache",CharacterArt.Facts()["bytes"].N<=CharacterArt.Facts()["budget"].N&&CharacterArt.Facts()["shared_studio_renderers"].I==1,CharacterArt.Facts());
        Check("viewing characters leaves simulation time and model calls unchanged",client.World["month"].I==month&&client.World["ai"]["calls"].N==calls);
        yield return client.Request("/api/intervene",J.O("action","challenge","npc_id",1),j=>app.Martial=j);app.ShowBattleForSmoke();deadline=Time.realtimeSinceStartup+15;while(app.PhysicsArena==null&&Time.realtimeSinceStartup<deadline)yield return null;
        Check("formal physical battle opens",app.PhysicsArena!=null);
        if(app.PhysicsArena!=null){var arena=app.PhysicsArena;yield return Picture("battle");Check("battle uses the same hero and NPC appearance",arena.player.CharacterAppearanceId==app.PaperSkinFor(client.World["hero"],true)&&arena.enemy.CharacterAppearanceId==app.PaperSkinFor(people.First(n=>n["id"].I==1)),J.O("hero",arena.player.CharacterRenderFacts(),"npc",arena.enemy.CharacterRenderFacts()));arena.RunCampaign(true);yield return new WaitForSecondsRealtime(.6f);yield return Picture("battle-motion");arena.PauseCampaign();}
        Screen.SetResolution(1280,800,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.3f);app.AppearancePreview(1,28,true,true);yield return Picture("compact");
    }
    void Finish(){bool pass=checks.All(c=>c["pass"].B);File.WriteAllText(Path.Combine(output,"characters-native.json"),J.O("pass",pass,"checks",new J(checks),"errors",new J(errors.Select(e=>new J(e)).ToList())).ToString());Application.Quit(pass?0:1);}
}
public partial class JianghuApp {
    public void CharacterInteractionForSmoke(int id)=>OpenInteraction(id);
    public bool CharacterInteractionReadyForSmoke=>TopIs(ViewKind.Interaction)&&InteractionDataReady;
}
}
