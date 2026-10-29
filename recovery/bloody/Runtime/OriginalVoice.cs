using System;
using System.Collections.Generic;
using Jianghu;
using UnityEngine;
using Random=UnityEngine.Random;

// VoiceSFX, native 0x3517048..0x3517458. This deliberately shares Unity's
// global random state with the original AI, including InitState(frameCount).
public sealed class OriginalVoice : MonoBehaviour {
    public int VoiceTypeIndex {get;private set;}=1;
    public float VoicePitch {get;private set;}=1;
    public bool Muffled {get;private set;}
    public bool Suppressed {get;private set;}
    public AudioSource Source {get;private set;}
    J data,library;
    string charge,lightVoice,medium,heavy,pantLight,pantIn,pantOut;
    readonly List<Fade> fades=new List<Fade>();
    sealed class Fade {public float start,age;}
    public void Initialize(OriginalRig rig,J settings,J source,J config) {
        data=settings;library=source;Source=rig.Resolve<AudioSource>(settings["audioSource"]);Muffled=settings["Muffled"].B;
        if(Source==null)throw new InvalidOperationException("Original VoiceSFX AudioSource missing");
        SetVoice(config);
    }
    string Key(string name)=>data[name]["key"].S;
    // SetVoice: five human packs; non-male pitch multiplier is 1.35.
    // The original spooky branch sets a persistent suppression flag.
    public void SetVoice(J config,bool spooky=false) {
        VoiceTypeIndex=config["VoiceTypeIndex"].I;VoicePitch=(float)config["VoicePitch"].N*(config["Gender"].I==1?1:1.35f);
        int type=VoiceTypeIndex>=1&&VoiceTypeIndex<=5?VoiceTypeIndex:1;
        charge=Key("charge_"+type);lightVoice=Key("screamLight_"+type);medium=Key("screamMedium_"+type);heavy=Key("screamHeavy_"+type);
        pantLight=Key("pantingLight");pantIn=Key("pantingHeavyIn");pantOut=Key("pantingHeavyOut");
        if(spooky) {
            Suppressed=true;charge=Key("charge_spooky");lightVoice=Key("screamLight_spooky");medium=Key("screamMedium_spooky");heavy=Key("screamHeavy_spooky");
            pantLight=Key("pantingLight_spooky");pantIn=Key("pantingHeavyIn_spooky");pantOut=Key("pantingHeavyOut_spooky");
        }
    }
    public void Play_Charge()=>PlaySound(charge);
    public void Play_ScreamLight()=>PlaySound(lightVoice);
    public void Play_ScreamMedium()=>PlaySound(medium);
    public void Play_ScreamHeavy()=>PlaySound(heavy);
    public void Play_PantingLight()=>PlaySound(pantLight);
    public void Play_PantingHeavyIn()=>PlaySound(pantIn);
    public void Play_PantingHeavyOut()=>PlaySound(pantOut);
    public void PlaySound(string objectKey) {
        if(Suppressed)return;
        Random.InitState(Time.renderedFrameCount);
        Source.pitch=VoicePitch+Random.Range(-.075f,.075f);
        var audio=library["audio_objects"][objectKey];
        if(!audio.Has)throw new InvalidOperationException("Original voice AudioObject missing: "+objectKey);
        Source.volume=Random.Range((float)audio["volume"]["min"].N,(float)audio["volume"]["max"].N);
        var choices=audio["audioClip"].A;
        var selected=choices[Random.Range(0,choices.Count)];
        Source.clip=Resources.Load<AudioClip>(library["audio"][selected["key"].S]["resource"].S);
        if(Source.clip==null)throw new InvalidOperationException("Original voice clip missing: "+selected["name"].S);
        Invoke(nameof(PlaySoundDelayed),.1f);
    }
    public void PlaySoundDelayed(){Source.Play();}
    // StopAllVoices is DOVolume(0,.1), rather than AudioSource.Stop.
    public void StopAllVoices(){fades.Add(new Fade{start=Source.volume});}
    void Update() {
        for(int i=0;i<fades.Count;) {
            var fade=fades[i];fade.age+=Time.deltaTime;float t=Mathf.Clamp01(fade.age/.1f);
            Source.volume=fade.start*(1-t)*(1-t);
            if(t>=1)fades.RemoveAt(i);else i++;
        }
    }
}
