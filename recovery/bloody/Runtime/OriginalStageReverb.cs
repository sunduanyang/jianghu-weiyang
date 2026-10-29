using System;
using UnityEngine;
using UnityEngine.Audio;

// ReverbSetter.Awake 0x343d570, getter crk 0x35d8e64 => "SFXReverb".
// Mixer group is a serialized, original GUID/local-file-ID binding.
public sealed class OriginalStageReverb : MonoBehaviour {
    public string SourceIdentity;
    public AudioMixerGroup SFXMixerGroup;
    public float ReverbValue;
    public bool ParameterAccepted {get;private set;}
    void Awake() {
        if(SFXMixerGroup==null)throw new InvalidOperationException("Original ReverbSetter mixer group missing: "+SourceIdentity);
        ParameterAccepted=SFXMixerGroup.audioMixer.SetFloat("SFXReverb",ReverbValue);
        // Native discards SetFloat's return; retain it only as a diagnostic.
    }
}
