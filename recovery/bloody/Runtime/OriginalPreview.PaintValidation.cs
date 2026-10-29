using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jianghu;
using UnityEngine;

// Explicit GPU regression. Ordinary combat never selects the legacy shader
// keyword; this probe supplies both the source decals and a negative control.
public sealed partial class OriginalPreview {
    sealed class PaintValidationTexture {
        public OriginalPaintTexture paint;
        public Texture source;
        public Color color;
        public string path,file;
        public Color32[] baseline;
    }
    sealed class PaintValidationRun {
        public string directory;
        public OriginalVisualCatalog catalog;
        public OriginalRig rig;
        public readonly List<PaintValidationTexture> textures=new List<PaintValidationTexture>();
        public readonly List<J> modes=new List<J>(),errors=new List<J>(),points=new List<J>();
        public readonly List<Vector3> anchors=new List<Vector3>();
        public J[] decals;
        public bool passed=true,priorExternal,priorPaused;
        public float priorVolume,health;
        public double started,deadline;
    }
    sealed class PaintValidationStats {
        public long rgba,alpha,brighter,darkened;
        public int maxIncrease,maxDarkening;
        public readonly List<J> rows=new List<J>();
        public J Json()=>J.O("rgba_mismatch",rgba,"alpha_mismatch",alpha,"rgb_increase_over_2",brighter,"darkened_over_2",darkened,
            "maximum_rgb_increase",maxIncrease,"maximum_rgb_darkening",maxDarkening,"textures",new J(rows));
    }
    void TryStartOriginalPaintValidation() {
        var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-validateOriginalPaint");if(at<0)return;
        var run=new PaintValidationRun {
            directory=Path.GetFullPath(at+1<args.Length?args[at+1]:Path.Combine(Application.persistentDataPath,"OriginalPaintValidation")),
            priorExternal=ExternalInputReplay,priorPaused=Paused,priorVolume=AudioListener.volume,started=Time.realtimeSinceStartupAsDouble
        };
        Directory.CreateDirectory(run.directory);ExternalInputReplay=true;AudioListener.volume=0;
        StartCoroutine(ValidateOriginalPaint(run));
    }
    static string PaintValidationPath(Transform node) {
        string value=node.name;while(node.parent!=null){node=node.parent;value=node.name+"/"+value;}return value;
    }
    static string PaintValidationFile(string value)=>new string(value.Select(c=>char.IsLetterOrDigit(c)||c=='-'||c=='_'?c:'_').ToArray());
    static J PaintValidationVector(Vector3 p)=>J.O("x",p.x,"y",p.y,"z",p.z);
    static Color32[] ReadPaintValidationPixels(RenderTexture target,string png=null) {
        if(target==null||!target.IsCreated())throw new InvalidOperationException("Paint validation RenderTexture is not created");
        var prior=RenderTexture.active;Texture2D readable=null;
        try {
            // Linear readback avoids an extra sRGB conversion in the comparison.
            readable=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false,true);
            RenderTexture.active=target;readable.ReadPixels(new Rect(0,0,target.width,target.height),0,0,false);readable.Apply(false,false);
            var result=readable.GetPixels32();if(png!=null)File.WriteAllBytes(png,readable.EncodeToPNG());return result;
        } finally {RenderTexture.active=prior;if(readable!=null)Destroy(readable);}
    }
    static void CapturePaintValidationScreen(string filename) {
        Texture2D screenshot=null;
        try{screenshot=ScreenCapture.CaptureScreenshotAsTexture();if(screenshot==null)throw new InvalidOperationException("ScreenCapture returned no texture");File.WriteAllBytes(filename,screenshot.EncodeToPNG());}
        finally{if(screenshot!=null)Destroy(screenshot);}
    }
    void ResetPaintValidationTextures(PaintValidationRun run) {
        foreach(var state in run.textures)state.paint.Replace(state.source,state.color);
        foreach(var state in run.textures)state.baseline=ReadPaintValidationPixels(state.paint.current);
    }
    PaintValidationStats SamplePaintValidation(PaintValidationRun run,string dumpDirectory=null) {
        var total=new PaintValidationStats();if(dumpDirectory!=null)Directory.CreateDirectory(dumpDirectory);
        foreach(var state in run.textures) {
            var current=ReadPaintValidationPixels(state.paint.current,dumpDirectory==null?null:Path.Combine(dumpDirectory,state.file+".png"));
            if(current.Length!=state.baseline.Length)throw new InvalidOperationException("Paint texture dimensions changed: "+state.path);
            long rgba=0,alpha=0,brighter=0,darkened=0;int maxIncrease=0,maxDarkening=0;
            for(int i=0;i<current.Length;i++) {
                var before=state.baseline[i];var after=current[i];
                if(before.r!=after.r||before.g!=after.g||before.b!=after.b||before.a!=after.a)rgba++;
                if(before.a!=after.a)alpha++;
                int increase=Mathf.Max(after.r-before.r,Mathf.Max(after.g-before.g,after.b-before.b));
                int darkening=Mathf.Max(before.r-after.r,Mathf.Max(before.g-after.g,before.b-after.b));
                if(increase>2)brighter++;if(darkening>2)darkened++;
                maxIncrease=Mathf.Max(maxIncrease,increase);maxDarkening=Mathf.Max(maxDarkening,darkening);
            }
            total.rgba+=rgba;total.alpha+=alpha;total.brighter+=brighter;total.darkened+=darkened;
            total.maxIncrease=Mathf.Max(total.maxIncrease,maxIncrease);total.maxDarkening=Mathf.Max(total.maxDarkening,maxDarkening);
            total.rows.Add(J.O("path",state.path,"file",state.file+".png","width",state.paint.current.width,"height",state.paint.current.height,
                "rt_srgb",state.paint.current.sRGB,"active",state.paint.isActiveAndEnabled,"rgba_mismatch",rgba,"alpha_mismatch",alpha,
                "rgb_increase_over_2",brighter,"darkened_over_2",darkened,"maximum_rgb_increase",maxIncrease,"maximum_rgb_darkening",maxDarkening));
        }
        return total;
    }
    void CheckPaintValidationHost(PaintValidationRun run) {
        if(GameMode!=0||player==null||player!=run.rig||OriginalState!=4||!Paused||IsTransitioning||player.dead||player.health!=run.health)
            throw new InvalidOperationException("Paint fixture lost its paused, living Campaign actor or health changed");
        if(Time.realtimeSinceStartupAsDouble>run.deadline)throw new TimeoutException("Paint GPU validation exceeded 180 real seconds");
    }
    Vector3 PaintValidationPoint(PaintValidationRun run,int stamp) {
        var point=run.anchors[stamp%run.anchors.Count];float angle=stamp*2.39996323f,radius=.015f+stamp*.0001f;
        point.x+=Mathf.Cos(angle)*radius;point.y+=Mathf.Sin(angle)*radius;point.z=0;return point;
    }
    void ApplyPaintValidationStamp(PaintValidationRun run,J decal,Vector3 point,bool legacy) {
        CheckPaintValidationHost(run);
        foreach(var state in run.textures)state.paint.PaintPoint(run.catalog,decal,point,legacy);
    }
    IEnumerator RunOriginalPaintValidation(PaintValidationRun run) {
        double readyDeadline=Time.realtimeSinceStartupAsDouble+90;
        while((player==null||OriginalState!=4||IsTransitioning||hud==null||hud.OriginalTransitionBlocksPointerInput)&&Time.realtimeSinceStartupAsDouble<readyDeadline)yield return null;
        if(GameMode!=0||player==null||OriginalState!=4||IsTransitioning||hud==null||hud.OriginalTransitionBlocksPointerInput)
            throw new InvalidOperationException("Paint validation requires a ready normal Campaign (GameMode0) arena");
        if(!SystemInfo.graphicsUVStartsAtTop)throw new InvalidOperationException("Legacy UV negative control requires a top-origin graphics API");
        run.rig=player;run.health=player.health;SetPaused(true);ResetPointers();
        // Flush presentations already queued by the preceding physics frame.
        yield return null;yield return null;run.deadline=Time.realtimeSinceStartupAsDouble+180;
        run.catalog=Resources.Load<OriginalVisualCatalog>("OriginalVisualCatalog");if(run.catalog==null)throw new InvalidOperationException("OriginalVisualCatalog missing");
        int serial=0;
        foreach(var paint in player.GetComponentsInChildren<OriginalPaintTexture>(true).OrderBy(p=>PaintValidationPath(p.transform),StringComparer.Ordinal)) {
            if(paint.current==null||paint.target==null)continue;
            run.textures.Add(new PaintValidationTexture {paint=paint,source=paint.sourceTexture,color=paint.sourceColor,path=PaintValidationPath(paint.transform),file=(serial++).ToString("00")+"-"+PaintValidationFile(paint.name)});
        }
        if(run.textures.Count==0)throw new InvalidOperationException("Player has no original paint textures");
        var data=run.catalog.Data;var manager=data["scene"].A.First(row=>row["script"].S=="VFXAndDecalManager");
        var names=new[]{"DamageDecalBloodSmall","DamageDecalBloodBig","DamageDecalBruise"};
        run.decals=names.Select(name=> {
            string key=manager["assets"][name].S;var row=data["scene"].A.First(r=>r["source"]["file"].S+":"+r["source"]["id"].S==key);
            if(row["data"]["blendMode"]["Index"].I!=10||run.catalog.Get<Texture>(row["assets"]["texture"].S)==null)throw new InvalidOperationException("Source multiply decal/texture missing: "+name);
            return row;
        }).ToArray();
        // Use the live source skeleton/body positions, without moving any bone.
        foreach(var body in player.GetComponentsInChildren<Rigidbody2D>(true).Where(body=>body.gameObject.activeInHierarchy&&body.transform.name.StartsWith("Bone_",StringComparison.Ordinal)).OrderBy(body=>PaintValidationPath(body.transform),StringComparer.Ordinal)) {
            var p=(Vector3)body.position;if(float.IsFinite(p.x)&&float.IsFinite(p.y))run.anchors.Add(p);
        }
        run.anchors.Insert(0,player.pelvisCOM.position);run.anchors.Insert(0,player.chest.position);
        ResetPaintValidationTextures(run);SamplePaintValidation(run,Path.Combine(run.directory,"before"));
        yield return new WaitForEndOfFrame();CapturePaintValidationScreen(Path.Combine(run.directory,"before-screen.png"));
        foreach(bool legacy in new[]{true,false}) {
            string label=legacy?"legacy":"fixed";var identitySamples=new List<J>();var realSamples=new List<J>();
            ResetPaintValidationTextures(run);long identityAlpha=0,identityRgba=0;bool identityUnchanged=true;
            for(int stamp=1;stamp<=3;stamp++) {
                // Detached clone: only fixture opacity changes; source records
                // and the ordinary production paint entry point remain intact.
                var identity=J.Parse(J.Write(run.decals[(stamp-1)%run.decals.Length]));identity["data"]["opacity"]=new J(0);
                var point=PaintValidationPoint(run,stamp-1);ApplyPaintValidationStamp(run,identity,point,legacy);
                var sample=SamplePaintValidation(run,stamp==3?Path.Combine(run.directory,label+"-identity"):null);
                identityAlpha=Math.Max(identityAlpha,sample.alpha);identityRgba=Math.Max(identityRgba,sample.rgba);identityUnchanged&=sample.rgba==0;
                identitySamples.Add(J.O("stamp",stamp,"source_decal",names[(stamp-1)%names.Length],"point",PaintValidationVector(point),"pixels",sample.Json()));yield return null;
            }
            bool identityPassed=legacy?identityAlpha>0&&identityRgba>0:identityUnchanged;
            ResetPaintValidationTextures(run);long maxAlpha=0,maxBrighter=0,maxDarkened=0;int maxIncrease=0,maxDarkening=0;bool fixedContract=true;
            var checkpoints=new HashSet<int>(new[]{1,3,7,15,31,63,127,200});
            for(int stamp=1;stamp<=200;stamp++) {
                int index=(stamp-1)%run.decals.Length;var point=PaintValidationPoint(run,stamp-1);ApplyPaintValidationStamp(run,run.decals[index],point,legacy);
                if(legacy)run.points.Add(J.O("stamp",stamp,"source_decal",names[index],"point",PaintValidationVector(point)));
                if(checkpoints.Contains(stamp)) {
                    var sample=SamplePaintValidation(run,stamp==200?Path.Combine(run.directory,label+"-after-200"):null);
                    maxAlpha=Math.Max(maxAlpha,sample.alpha);maxBrighter=Math.Max(maxBrighter,sample.brighter);maxDarkened=Math.Max(maxDarkened,sample.darkened);
                    maxIncrease=Mathf.Max(maxIncrease,sample.maxIncrease);maxDarkening=Mathf.Max(maxDarkening,sample.maxDarkening);
                    fixedContract&=sample.alpha==0&&sample.brighter==0;
                    realSamples.Add(J.O("stamp",stamp,"pixels",sample.Json()));
                }
                if(stamp%4==0)yield return null;
            }
            bool realPassed=legacy?maxAlpha>0:fixedContract&&maxDarkened>0;
            run.passed&=identityPassed&&realPassed;
            run.modes.Add(J.O("mode",label,"legacy_keyword",legacy,"identity_passed",identityPassed,"identity_maximum_alpha_mismatch",identityAlpha,
                "identity_maximum_rgba_mismatch",identityRgba,"identity_samples",new J(identitySamples),"stamps",200,"samples",new J(realSamples),
                "maximum_sample_alpha_mismatch",maxAlpha,"maximum_sample_rgb_increase_over_2",maxBrighter,"maximum_sample_darkened_over_2",maxDarkened,
                "maximum_rgb_increase",maxIncrease,"maximum_rgb_darkening",maxDarkening,"real_decal_passed",realPassed,"passed",identityPassed&&realPassed));
            yield return new WaitForEndOfFrame();CapturePaintValidationScreen(Path.Combine(run.directory,label+"-after-200-screen.png"));
        }
        CheckPaintValidationHost(run);
    }
    IEnumerator ValidateOriginalPaint(PaintValidationRun run) {
        var routine=RunOriginalPaintValidation(run);
        // Pump this iterator so a GPU/readback exception becomes a failed JSON
        // result instead of silently terminating the regression coroutine.
        while(true) {
            bool more;object next=null;
            try{more=routine.MoveNext();if(more)next=routine.Current;}
            catch(Exception error){run.passed=false;run.errors.Add(J.O("error",error.ToString(),"frame",Time.frameCount));break;}
            if(!more)break;yield return next;
        }
        (routine as IDisposable)?.Dispose();ResetPointers();SetPaused(run.priorPaused);ExternalInputReplay=run.priorExternal;AudioListener.volume=run.priorVolume;
        if(run.modes.Count!=2)run.passed=false;
        var report=J.O("passed",run.passed,"unity",Application.unityVersion,"graphics_device",SystemInfo.graphicsDeviceType.ToString(),"uv_starts_at_top",SystemInfo.graphicsUVStartsAtTop,
            "color_space",QualitySettings.activeColorSpace.ToString(),"linear_readback",true,"duration",Time.realtimeSinceStartupAsDouble-run.started,"game_mode",GameMode,
            "scope","Paused living Campaign pose; real GPU opacity-zero identity and original blood/bruise multiply stamps. Legacy must fail alpha preservation; fixed must preserve alpha and never brighten RGB by more than two byte values. No continuous battle or APK frame parity assertion.",
            "modes",new J(run.modes),"stamp_points_shared_by_both_modes",new J(run.points),"errors",new J(run.errors),"runtime_parity_verified",false);
        File.WriteAllText(Path.Combine(run.directory,"paint-validation.json"),J.Write(report));
        Debug.Log("ORIGINAL_PAINT_VALIDATION_FINISHED passed="+run.passed+" "+run.directory);
        yield return new WaitForSecondsRealtime(.25f);Application.Quit(run.passed?0:2);
    }
}
