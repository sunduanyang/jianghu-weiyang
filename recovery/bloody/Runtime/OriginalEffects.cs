using System;
using System.Collections.Generic;
using System.Linq;
using Jianghu;
using UnityEngine;
using Random=UnityEngine.Random;

public struct OriginalPresentation {
    public int vfx,sound,burst,bloodyIndex,hitTier;
    public float volume,feedback;
}

// CharacterDamageCalculator.BuildAndPublishPresentation 0x3428860 and
// VFXAndDecalManager.DoVFX_Local 0x36416ac. Uses the original particle systems,
// decal textures, projection matrices and embedded decal fragment program.
public sealed class OriginalEffects : MonoBehaviour {
    static OriginalEffects instance;
    OriginalVisualCatalog catalog;
    J manager;
    readonly Dictionary<string,ParticleSystem> particles=new Dictionary<string,ParticleSystem>();
    readonly Dictionary<string,J> decals=new Dictionary<string,J>();
    readonly Dictionary<OriginalRig,J> soundSettings=new Dictionary<OriginalRig,J>();
    readonly Dictionary<OriginalRig,AudioSource> soundSources=new Dictionary<OriginalRig,AudioSource>();
    readonly Dictionary<OriginalRig,AudioSource> secondarySoundSources=new Dictionary<OriginalRig,AudioSource>();
    readonly Dictionary<string,J> audioObjects=new Dictionary<string,J>();
    sealed class Pending {public OriginalImpact impact;public OriginalPresentation presentation;public bool onlySound,discarded;public OriginalRig soundRig;public int soundIndex;public float soundVolume;}
    sealed class Feed {public readonly Queue<Pending> pending=new Queue<Pending>();public byte advance;}
    readonly Queue<Pending> impacts=new Queue<Pending>();
    readonly Dictionary<OriginalRig,Feed> rigImpacts=new Dictionary<OriginalRig,Feed>();
    readonly HashSet<OriginalRig> registered=new HashSet<OriginalRig>();
    public event Action<OriginalImpact,OriginalPresentation> Presented;
    static readonly string[] SoundFields={"fleshHitFlesh","fleshHitWood","fleshHitMetal","woodHitFlesh","woodBashFlesh","woodHitWood","woodHitMetal","metalSliceFlesh","metalSharpHitFlesh","metalBluntHitFlesh","metalBashFlesh_Big","metalBashFlesh_Small","woodBashFlesh_Big","woodBashFlesh_Small","metalHitWood","metalHitMail","metalSharpHitMetal_Small","metalSharpHitMetal_Big","metalImpaleFlesh","metalBluntHitMetal_Small","metalBluntHitMetal_Big","woodHitMetal_Big","severing","bashing","bodyfallNothing","kick"};
    void Awake() {
        instance=this;catalog=Resources.Load<OriginalVisualCatalog>("OriginalVisualCatalog");
        manager=catalog.Data["scene"].A.First(x=>x["script"].S=="VFXAndDecalManager");
        foreach(string name in new[]{"bloodSmall","bloodBig","dustSmall","dustBig","sparksSmall","sparksBig"}) {
            string path=catalog.Data["assets"][manager["assets"][name].S]["scene_path"].S;
            int start=path.IndexOf("VFXManager",StringComparison.Ordinal);if(start<0)continue;path=path.Substring(start+"VFXManager".Length).TrimStart('/');
            var node=transform.Find(path);if(node!=null){var ps=node.GetComponent<ParticleSystem>();if(ps!=null){ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);particles[name]=ps;}}
        }
        foreach(string name in new[]{"DamageDecalBloodSmall","DamageDecalBloodBig","DamageDecalBruise","DamageDecalBloodOnTheGround","DamageDecalMetalCut","DamageDecalBoneCrack"}) {
            string key=manager["assets"][name].S;var row=catalog.Data["scene"].A.FirstOrDefault(x=>x["source"]["file"].S+":"+x["source"]["id"].S==key);if(row!=null)decals[name]=row;
        }
        foreach(var row in catalog.Data["scene"].A.Where(x=>x["script"].S=="AudioObject"))audioObjects[row["source"]["file"].S+":"+row["source"]["id"].S]=row;
        foreach(var root in FindObjectsByType<Transform>(FindObjectsInactive.Exclude,FindObjectsSortMode.None))
            if(root.name.Contains("GraphicsContainer_")&&root.GetComponent<OriginalGroundSource>()==null)root.gameObject.AddComponent<OriginalGroundSource>();
    }
    public static void Register(OriginalRig rig) {
        if(instance==null)return;instance.RegisterRig(rig);
    }
    void RegisterRig(OriginalRig rig) {
        if(!registered.Add(rig))return;
        rig.ImpactOccurred+=OnImpact;
        rig.PartModeChanged+=OnPartMode;
        foreach(var row in catalog.Data["rigs"][rig.human?OriginalRig.PlayerSource:OriginalRig.EnemySource].A.Where(x=>x["script"].S=="DamageDecalPainter")) {
            var node=rig.transform.Find(row["path"].S);if(node!=null)node.gameObject.AddComponent<OriginalDamageDecalPainter>().Initialize(this,row["data"]);
        }
        var settings=catalog.Data["rigs"][rig.human?OriginalRig.PlayerSource:OriginalRig.EnemySource].A.FirstOrDefault(x=>x["script"].S=="ImpactSFX");
        if(settings==null)return;soundSettings[rig]=settings;
        var source=catalog.Data["assets"][settings["assets"]["audioSource"].S];string path=source["scene_path"].S;int slash=path.IndexOf('/');
        var target=slash<0?rig.transform:rig.transform.Find(path.Substring(slash+1));if(target!=null)soundSources[rig]=target.GetComponents<AudioSource>().ElementAtOrDefault(source["component_index"].I);
        source=catalog.Data["assets"][settings["assets"]["secondaryAudioSource"].S];path=source["scene_path"].S;slash=path.IndexOf('/');target=slash<0?rig.transform:rig.transform.Find(path.Substring(slash+1));
        if(target!=null)secondarySoundSources[rig]=target.GetComponents<AudioSource>().ElementAtOrDefault(source["component_index"].I);
    }
    void OnPartMode(OriginalBodyPart part,OriginalPartMode mode){if(mode==OriginalPartMode.Bashed)PublishSound(part.rig,23,2);else if(mode==OriginalPartMode.Pierced||mode==OriginalPartMode.Severed)PublishSound(part.rig,22,2);}
    public static void PublishSound(OriginalRig rig,int index,float volume) {
        if(instance==null)return;
        instance.Enqueue(rig,new Pending{onlySound=true,soundRig=rig,soundIndex=index,soundVolume=Mathf.Clamp(Mathf.RoundToInt(volume*64),0,1023)/64f});
    }
    void Enqueue(OriginalRig rig,Pending item) {
        if(!rigImpacts.TryGetValue(rig,out var feed)){feed=new Feed();rigImpacts.Add(rig,feed);}
        var local=feed.pending;feed.advance=unchecked((byte)(feed.advance+1));
        // CharacterImpactFeed has eight networked presentation slots. Its Render
        // consumes only the latest eight when multiple physics ticks outrun it.
        if(local.Count==8)local.Dequeue().discarded=true;
        local.Enqueue(item);impacts.Enqueue(item);
    }
    void OnImpact(OriginalImpact impact) {
        // The source publishes a packed event during physics, then consumes it
        // in Render. Keep shader draws out of the collision callback itself.
        var presentation=Classify(impact);
        if(impact.victim.OriginalExpression!=null)presentation.hitTier=impact.victim.OriginalExpression.TryHit(presentation.hitTier);
        // CharacterImpactFeed's local Render also consumes its packed event.
        // Preserve the source round-to-even point/volume quantization.
        impact.point=new Vector2(QuantizePoint(impact.point.x),QuantizePoint(impact.point.y));
        presentation.volume=Mathf.Clamp(Mathf.RoundToInt(presentation.volume*64),0,1023)/64f;
        Enqueue(impact.victim,new Pending{impact=impact,presentation=presentation});
    }
    static float QuantizePoint(float value)=>Mathf.Clamp(Mathf.RoundToInt((value+32)*1023.984375f),0,65535)/1023.984375f-32;
    void LateUpdate(){foreach(var feed in rigImpacts.Values)while(feed.pending.Count>Mathf.Min(feed.advance,8))feed.pending.Dequeue().discarded=true;while(impacts.Count>0){var item=impacts.Dequeue();if(item.discarded)continue;if(item.onlySound)PlaySound(item.soundRig,item.soundIndex,item.soundVolume);else Present(item.impact,item.presentation);}foreach(var feed in rigImpacts.Values){feed.pending.Clear();feed.advance=0;}}
    public static OriginalPresentation Classify(OriginalImpact impact) {
        bool sharp=impact.surface==OriginalSurface.MetalBlade||impact.surface==OriginalSurface.MetalSpike||impact.surface==OriginalSurface.SpikePommel;
        bool blunt=impact.surface==OriginalSurface.MetalBlunt,wood=impact.surface==OriginalSurface.WoodBlunt,woodHandle=impact.surface==OriginalSurface.WoodHandle;
        var p=new OriginalPresentation();
        switch(impact.armorMaterial) {
            case 2:p.vfx=0;p.sound=sharp?8:blunt?9:wood?12:woodHandle?13:11;p.burst=sharp||blunt||wood?3:0;break;
            case 3:p.vfx=sharp?0:7;p.sound=sharp?14:blunt||wood?6:woodHandle?13:14;p.burst=sharp||blunt||wood?2:0;break;
            case 4:p.vfx=sharp||blunt||wood?0:3;p.sound=sharp?15:blunt||wood?6:woodHandle?13:15;p.burst=sharp||blunt||wood?2:0;break;
            case 5:case 6:case 7:
                p.vfx=sharp||blunt?6:wood?4:woodHandle?3:5;p.sound=sharp?17:blunt?20:wood?(impact.item.isShield?6:21):woodHandle?13:19;p.burst=sharp||blunt?1:wood?5:0;break;
            default:p.vfx=sharp?0:7;p.sound=sharp?8:blunt?9:wood?12:woodHandle?13:11;p.burst=sharp||blunt||wood?3:0;break;
        }
        float speed=impact.velocity.magnitude;
        float force=impact.victim.DamageNumber("damagePerMps")*speed*(impact.item.damage/10+1)*2;
        if(impact.item.isShield)force*=.5f;if(woodHandle)force*=.2f;if(impact.surface==OriginalSurface.MetalHandle)force*=.4f;
        force=Mathf.Clamp(force,1,100);p.hitTier=force>=10?3:force>=2?2:Random.Range(0,2);
        // Tuple item3 is BloodyIndex. Burst is a separate caller argument:
        // normal impacts use 1; cannonball impacts use 3 and force BloodBig.
        p.bloodyIndex=p.burst;p.burst=impact.cannon?3:1;if(impact.cannon)p.vfx=1;
        p.volume=speed*impact.victim.DamageNumber("sfxVolumePerMps")+.25f;p.feedback=(int)impact.localDamage;return p;
    }
    void Present(OriginalImpact impact,OriginalPresentation presentation) {
        DoVfx(presentation.vfx,impact.point,presentation.burst);
        PlaySound(impact.victim,presentation.sound,presentation.volume);
        if(impact.victim.OriginalExpression!=null)impact.victim.OriginalExpression.PlayHitLocal(presentation.hitTier);
        if(impact.item!=null&&presentation.bloodyIndex>0)impact.item.GetComponent<OriginalWeaponAppearance>()?.BloodyUp(presentation.bloodyIndex);
        Presented?.Invoke(impact,presentation);
    }
    void Emit(string name,Vector2 point,int count) {
        if(count<=0||!particles.TryGetValue(name,out var system))return;
        var emit=new ParticleSystem.EmitParams{position=new Vector3(point.x,point.y,system.transform.position.z)};system.Emit(emit,count);
    }
    void Paint(string name,Vector3 point) {
        if(!decals.TryGetValue(name,out var decal))return;
        foreach(var paint in FindObjectsByType<OriginalPaintTexture>(FindObjectsInactive.Include,FindObjectsSortMode.None))paint.PaintPoint(catalog,decal,point);
    }
    public void DoVfx(int type,Vector2 point,int burst=1) {
        if(Application.isBatchMode)return;
        switch(type) {
            case 0:Emit("bloodSmall",point,burst*7);Paint("DamageDecalBloodSmall",point);GroundBlood(point);break;
            case 1:Emit("bloodBig",point,burst*7);Paint("DamageDecalBloodBig",point);GroundBlood(point);break;
            case 2:GroundBlood(point);break;
            case 3:Emit("dustSmall",point,burst*2);break;
            case 4:Emit("dustBig",point,burst*5);Paint("DamageDecalMetalCut",point);break;
            case 5:Emit("sparksSmall",point,burst*3);break;
            case 6:Emit("sparksBig",point,burst*7);Emit("dustBig",point,burst*3);Paint("DamageDecalMetalCut",point);break;
            case 7:Emit("bloodSmall",point,burst*5);Paint("DamageDecalBruise",point);Paint("DamageDecalBloodSmall",point);GroundBlood(point);break;
        }
    }
    void GroundBlood(Vector2 point){Paint("DamageDecalBloodOnTheGround",new Vector3(point.x+.25f+Random.Range(-.1f,.1f),0,0));}
    void PlaySound(OriginalRig rig,int index,float volume) {
        if(Application.isBatchMode)return;
        if(index<0||index>=SoundFields.Length||!soundSettings.TryGetValue(rig,out var settings)||!soundSources.TryGetValue(rig,out var source)||source==null)return;
        if(index==25&&secondarySoundSources.TryGetValue(rig,out var secondary)&&secondary!=null){PlayAudioObject(settings,"kick",secondary,volume);volume/=5;}
        string field=index==24||index==25?(rig.OriginalBodyfallType==2?"bodyfallPlate":rig.OriginalBodyfallType==1?"bodyfallLeather":"bodyfallNothing"):SoundFields[index];
        PlayAudioObject(settings,field,source,volume);
    }
    void PlayAudioObject(J settings,string field,AudioSource source,float volume) {
        string key=settings["assets"][field].S;
        if(!audioObjects.TryGetValue(key,out var audio))throw new InvalidOperationException("Original ImpactSFX AudioObject missing: "+field+" / "+key);
        var d=audio["data"];source.pitch=Random.Range((float)d["pitch"]["min"].N,(float)d["pitch"]["max"].N);
        int count=d["audioClip"].A.Count;if(count==0)return;int selected=Random.Range(0,count);
        var clip=catalog.Get<AudioClip>(audio["assets"]["audioClip["+selected+"]"].S);
        if(clip==null)throw new InvalidOperationException("Original ImpactSFX AudioClip missing: "+audio["assets"]["audioClip["+selected+"]"].S);
        source.PlayOneShot(clip,volume/5+(float)d["volume"]["max"].N);
    }
    void OnDestroy(){foreach(var rig in registered)if(rig!=null){rig.ImpactOccurred-=OnImpact;rig.PartModeChanged-=OnPartMode;}if(instance==this)instance=null;}
}
