using System;
using Jianghu;
using UnityEngine;
using Random=UnityEngine.Random;

// bg.PlaySound, 0x3411e0c. UI sound shares the original global RNG without
// VoiceSFX's separate frame-seeded random reset.
public static class OriginalUIAudio {
    public static void Play(AudioSource source,J reference,J library,float volumeOffset=0) {
        if(source==null||!source.enabled||reference["kind"].S!="audio_object")return;
        J settings=library["audio_objects"][reference["key"].S];if(!settings.Has)return;
        var clips=settings["audioClip"].A;if(clips.Count==0)return;
        J selected=clips[Random.Range(0,clips.Count)];
        var clip=Resources.Load<AudioClip>(library["audio"][selected["key"].S]["resource"].S);
        if(clip==null){Debug.LogWarning("AudioHelper: AudioClip is null");return;}
        source.volume=Random.Range((float)settings["volume"]["min"].N,(float)settings["volume"]["max"].N)+volumeOffset;
        source.pitch=Random.Range((float)settings["pitch"]["min"].N,(float)settings["pitch"]["max"].N);
        source.loop=settings["loop"].B;
        if(source.loop){source.clip=clip;source.Play();}else source.PlayOneShot(clip);
    }
}
