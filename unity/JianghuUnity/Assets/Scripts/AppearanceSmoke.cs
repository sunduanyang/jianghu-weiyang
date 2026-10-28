using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public sealed class AppearanceSmoke:MonoBehaviour {
    string output;JianghuApp app;GameClient client;
    readonly List<J> checks=new List<J>();readonly List<string> errors=new List<string>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot(){/* The compatible -appearanceSmoke entry now runs CharacterModulesSmoke. */}
    void OnEnable(){Application.logMessageReceived+=Log;}void OnDisable(){Application.logMessageReceived-=Log;}
    void Log(string text,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(text);}
    void Check(string name,bool pass,J facts=null){checks.Add(J.O("check",name,"pass",pass,"facts",facts??J.O()));}
    IEnumerator Start(){
        output=GameClient.Arg("-appearanceSmoke");Directory.CreateDirectory(output);
        float until=Time.realtimeSinceStartup+120;
        while(Time.realtimeSinceStartup<until){app=FindAnyObjectByType<JianghuApp>();client=FindAnyObjectByType<GameClient>();if(client!=null&&client.Ready&&client.World.Has)break;yield return null;}
        if(client==null||!client.Ready){Check("backend ready",false);Finish();yield break;}
        var run=Run();while(true){object current=null;bool more=false;try{more=run.MoveNext();if(more)current=run.Current;}catch(Exception error){errors.Add(error.ToString());break;}if(!more)break;yield return current;}
        Check("no runtime errors",errors.Count==0,new J(errors.Select(e=>new J(e)).ToList()));Finish();
    }
    Texture2D Read(Texture texture){var rt=RenderTexture.GetTemporary(texture.width,texture.height,0,RenderTextureFormat.ARGB32);var prior=RenderTexture.active;try{Graphics.Blit(texture,rt);RenderTexture.active=rt;var copy=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false);copy.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);copy.Apply();return copy;}finally{RenderTexture.active=prior;RenderTexture.ReleaseTemporary(rt);}}
    IEnumerator Picture(string name){for(int i=0;i<4;i++)yield return new WaitForEndOfFrame();var shot=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(output,name+".png"),shot.EncodeToPNG());Destroy(shot);}
    IEnumerator Run(){
        int month=client.World["month"].I;double calls=client.World["ai"]["calls"].N;
        var people=client.World["npcs"].A;
        if(!client.World["hero"]["created"].B){yield return client.Request("/api/hero",client.HeroBody("create","name","外貌验收","sex","男","age",28,"place",client.World["hero"]["place"]),j=>{});yield return client.Refresh();}
        Check("every NPC has persistent appearance",people.Count>=24&&people.All(n=>n["appearance"]["version"].I==1),J.O("people",people.Count));
        J detail=null;yield return client.Request("/api/npc/1",null,j=>detail=j);
        Check("roster and detail share appearance",detail!=null&&detail["appearance"].ToString()==people.First(n=>n["id"].I==1)["appearance"].ToString());
        var catalog=J.Parse(Resources.Load<TextAsset>("PaperCast/index").text);
        app.AppearancePreview();
        var sample=app.AppearancePreviewPerson(people[0]);string first=app.PaperSkinFor(sample);
        sample["job"]=new J("书生");string changed=app.PaperSkinFor(sample);
        J original=PaperAppearanceArt.Skin(catalog,first),outfit=PaperAppearanceArt.Skin(catalog,changed);
        Check("changing occupation preserves head",original["head_id"].S==outfit["head_id"].S&&original["body_id"].S!=outfit["body_id"].S);
        sample["age_months"]=new J(70*12);sample["appearance_view"]=J.O("grey",4);
        Check("ageing preserves adult head",PaperAppearanceArt.Skin(catalog,app.PaperSkinFor(sample))["head_id"].S==original["head_id"].S);
        var baseSkin=catalog["skins"][original["body_id"].S];
        Check("appearance preserves calibrated skeleton",baseSkin["bind_joints"].ToString()==original["bind_joints"].ToString()&&baseSkin["lengths"].ToString()==original["lengths"].ToString());
        Texture2D body=Read(PaperAppearanceArt.Atlas(original)),source=Read(Resources.Load<Texture2D>("PaperCast/"+original["body_id"].S));
        var a=body.GetPixels32();var b=source.GetPixels32();int alphaError=0,colorChanges=0;
        for(int i=0;i<a.Length;i++){alphaError=Math.Max(alphaError,Math.Abs(a[i].a-b[i].a));if(a[i].a>128&&Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b)>20)colorChanges++;}
        Check("GPU recolour preserves alpha and changes clothing",alphaError<=1&&colorChanges>1000,J.O("max_alpha_error",alphaError,"recoloured_pixels",colorChanges));Destroy(body);Destroy(source);
        foreach(int sex in new[]{0,1})foreach(bool left in new[]{false,true}){
            app.AppearancePreview(sex,28,left,true);app.ResetPaperGuiProbe();yield return Picture((sex==0?"men":"women")+(left?"-left":"-right"));
            Check("rendered joints "+sex+" "+left,app.PaperGuiProbe(out J facts),facts);
        }
        app.AppearancePreview(0,70,false,false);yield return Picture("older-adults");
        app.AppearancePreview(1,12,false,true);yield return Picture("children");
        app.AppearancePreview(0,28,false,false,"书生");yield return Picture("scholars");
        Screen.SetResolution(1280,800,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.3f);app.AppearancePreview(0,28,true,true);app.ResetPaperGuiProbe();yield return Picture("compact-left-walk");Check("rendered joints at 1280x800",app.PaperGuiProbe(out J compact),compact);Screen.SetResolution(1680,960,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.3f);
        app.ViewPerson(1,PersonTab.Overview);float detailUntil=Time.realtimeSinceStartup+10;while(app.Person["id"].I!=1&&Time.realtimeSinceStartup<detailUntil)yield return null;yield return Picture("person-overview");
        app.PaperSmokeDialogue();yield return Picture("dialogue");
        // Sample all existing costumes and both head pools, including mirrored walking.
        foreach(var pair in catalog["skins"].D){
            J person=J.Parse(people[0].ToString());person["sex"]=new J(pair.Key.StartsWith("f-")?"女":"男");person["appearance"]["head"]=new J(pair.Key.StartsWith("f-")?"f-swordswoman":"m-artisan");
            string id=PaperAppearanceArt.Id(pair.Key,person);J skin=PaperAppearanceArt.Skin(catalog,id);PaperAppearanceArt.Atlas(skin);PaperAppearanceArt.Atlas(skin,true);
            Check("compatible head on "+pair.Key,skin["parts"]["head-neck"]["height"].N==pair.Value["parts"]["head-neck"]["height"].N);
        }
        Check("bounded texture cache",PaperAppearanceArt.CacheFacts()["bytes"].N<=PaperAppearanceArt.CacheFacts()["budget"].N,PaperAppearanceArt.CacheFacts());
        Check("preview leaves world time and model calls unchanged",client.World["month"].I==month&&client.World["ai"]["calls"].N==calls);
        yield return client.Request("/api/intervene",J.O("action","challenge","npc_id",1),j=>app.Martial=j);
        app.ShowBattleForSmoke();float deadline=Time.realtimeSinceStartup+12;while(app.PhysicsArena==null&&Time.realtimeSinceStartup<deadline)yield return null;
        Check("physical battle instantiated",app.PhysicsArena!=null);
        if(app.PhysicsArena!=null){var arena=app.PhysicsArena;arena.RunCampaign(true);yield return Picture("physical-battle");arena.PauseCampaign();Check("battle uses same hero and NPC appearance",arena.player.skin["appearance_id"].S==app.PaperSkinFor(client.World["hero"],true)&&arena.enemy.skin["appearance_id"].S==app.PaperSkinFor(people.First(n=>n["id"].I==1)));}
    }
    void Finish(){bool pass=checks.All(c=>c["pass"].B);File.WriteAllText(Path.Combine(output,"appearance-native.json"),J.O("pass",pass,"checks",new J(checks),"errors",new J(errors.Select(e=>new J(e)).ToList())).ToString());Application.Quit(pass?0:1);}
}
}
