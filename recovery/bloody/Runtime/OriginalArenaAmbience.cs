using System;
using System.Linq;
using Jianghu;
using UnityEngine;

// Arena subset: StopAndPlayAmbienceLoop and EnvironmentCustomizer's original
// ambience mixer parameter. Menu music/settings transitions have their own
// AudioAndMusicManager lifecycle and are not manufactured here.
public sealed class OriginalArenaAmbience : MonoBehaviour {
    OriginalVisualCatalog catalog;
    public AudioSource AmbienceSource {get;private set;}
    bool initialized;
    public void Initialize(Transform environmentRoot) {
        if(initialized)return;
        catalog=Resources.Load<OriginalVisualCatalog>("OriginalVisualCatalog");
        var source=catalog.Data["scene"].A.Single(row=>row["script"].S=="AudioAndMusicManager");
        var identity=source["assets"]["AmbienceAudioSource"].S;
        int ordinal=catalog.Data["assets"][identity]["component_index"].I;
        AmbienceSource=GetComponents<AudioSource>().ElementAtOrDefault(ordinal);
        if(AmbienceSource==null||AmbienceSource.outputAudioMixerGroup==null)throw new InvalidOperationException("Original ambience AudioSource/mixer binding missing");
        foreach(var row in catalog.Data["scene"].A.Where(record=>record["script"].S=="StopAndPlayAmbienceLoop")) {
            string path=row["scene_path"].S;
            if(!path.StartsWith(environmentRoot.name+"/",StringComparison.Ordinal))continue;
            var node=OriginalEnvironmentSource.IdentityNode(catalog,OriginalEnvironmentSource.Identity(row),environmentRoot,environmentRoot.name);
            if(node==null)throw new InvalidOperationException("Original weather ambience node missing: "+path);
            var loop=node.gameObject.AddComponent<OriginalAmbienceLoop>();
            var clip=catalog.Get<AudioClip>(row["assets"]["audioClip"].S);
            loop.Initialize(AmbienceSource,clip,(float)row["data"]["volume"].N);
        }
        initialized=true;
    }
    public void ApplyEnvironmentMixer(float value) {
        if(!initialized)throw new InvalidOperationException("Initialize original ambience before environment customization");
        if(!AmbienceSource.outputAudioMixerGroup.audioMixer.SetFloat(OriginalEnvironmentCustomizer.AmbientMixerParameter,value))
            throw new InvalidOperationException("Original ambience mixer parameter missing: "+OriginalEnvironmentCustomizer.AmbientMixerParameter);
    }
    // hy.SetTimeScaleManually 0x3442fb4: reads Unity's accepted timeScale,
    // sets the original SFXPitch parameter, then publishes its change event.
    public void ApplySourceTimeScale() {
        if(!initialized)return;
        if(!AmbienceSource.outputAudioMixerGroup.audioMixer.SetFloat("SFXPitch",Time.timeScale))
            throw new InvalidOperationException("Original SFXPitch mixer parameter missing");
    }
}

public sealed class OriginalAmbienceLoop : MonoBehaviour {
    AudioSource source;
    AudioClip clip;
    float volume;
    bool initialized;
    public void Initialize(AudioSource originalSource,AudioClip originalClip,float originalVolume) {
        source=originalSource;clip=originalClip;volume=originalVolume;
        if(source==null||clip==null)throw new InvalidOperationException("Original ambience loop audio binding missing");
        initialized=true;if(isActiveAndEnabled)OnEnable();
    }
    void OnEnable() {
        if(!initialized)return;
        source.Stop();source.clip=clip;source.volume=volume;
        UnityEngine.Random.InitState(Time.frameCount);source.time=UnityEngine.Random.Range(0f,clip.length);source.Play();
    }
}
