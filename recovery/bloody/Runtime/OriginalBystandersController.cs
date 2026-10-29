using System;
using System.Linq;
using Jianghu;
using UnityEngine;
using Random=UnityEngine.Random;

// Source BystandersController Start 0x36c22f0, dsh 0x36c2378,
// Networked-state callback dsi 0x36c2694, cheer dsj 0x36c26d0.
public sealed class OriginalBystandersController : MonoBehaviour {
    public string SourceIdentity,SourceRootName;
    public bool HasStarted {get;private set;}
    public bool Muted {get;private set;}
    OriginalBystanderMovement[] actors;
    AudioSource source;
    J record;
    sealed class VolumeTween {public float from,to,duration,elapsed;public bool started;}
    void Bind() {
        if(record!=null)return;
        record=OriginalStageActors.Record(SourceIdentity);
        var root=OriginalStageActors.InstanceRoot(transform,SourceRootName);
        actors=record["data"]["BystandersMovements"].A.Select((_,i)=>OriginalEnvironmentSource.Component<OriginalBystanderMovement>(OriginalStageActors.Catalog,record,"BystandersMovements["+i+"]",root,SourceRootName)).ToArray();
        source=OriginalEnvironmentSource.Component<AudioSource>(OriginalStageActors.Catalog,record,"BystandersAudioSource",root,SourceRootName);
        if(actors.Any(a=>a==null)||source==null)throw new InvalidOperationException("Original bystander controller bindings missing: "+SourceIdentity);
    }
    void Start() {
        Bind();ApplyInitialContext();
        OriginalStageActors.NetworkedStateChanged+=ApplyGameState;HasStarted=true;
    }
    void OnDestroy(){OriginalStageActors.NetworkedStateChanged-=ApplyGameState;}
    public void ApplyInitialContext() {
        Bind();
        if(transform.childCount>0)foreach(var actor in actors) {
            bool menu=OriginalStageActors.IsMainMenuState;
            actor.SafelyDisable();actor.gameObject.SetActive(!menu);source.mute=menu;
        }
        if(OriginalStageActors.IsThroneRoom||OriginalStageActors.GameMode==2){Muted=true;source.mute=true;}
        if(!Muted&&!source.isPlaying)PlaySourceLoop();
        if(OriginalStageActors.Context.SpookyMode){Muted=true;source.mute=true;}
        source.enabled=true;source.volume=0;Fade(.115f,1f);
    }
    void PlaySourceLoop() {
        // bg.PlaySound checks enabled, then consumes clip, volume, pitch in
        // that order, including singleton arrays and fixed MinMax values.
        if(!source.enabled)return;
        var audio=OriginalStageActors.Record(record["assets"]["BystandersCheerAudioObject"].S);var d=audio["data"];
        int count=d["audioClip"].A.Count;if(count==0)return;
        int index=Random.Range(0,count);var clip=OriginalStageActors.Catalog.Get<AudioClip>(audio["assets"]["audioClip["+index+"]"].S);
        if(clip==null)throw new InvalidOperationException("Original bystander cheer AudioClip missing");
        source.volume=Random.Range((float)d["volume"]["min"].N,(float)d["volume"]["max"].N);
        source.pitch=Random.Range((float)d["pitch"]["min"].N,(float)d["pitch"]["max"].N);source.loop=d["loop"].B;
        if(source.loop){source.clip=clip;source.Play();}else source.PlayOneShot(clip);
    }
    public void ApplyGameState(int previous,int current) {
        if(current==4)Fade(.115f,.15f);
        else if(current==5||current==6)StartCheer(5f);
    }
    public void StartCheer(float duration) {
        if(Muted)return;
        foreach(var actor in actors)actor.SetCheer();
        Fade(.755f,1.5f);Invoke(nameof(BystandersEndCheer),duration);
    }
    public void BystandersEndCheer()=>Fade(.115f,2f);
    void Fade(float value,float duration) {
        // Native DOFade default Ease.OutQuad and scaled Update; no kill.
        var fade=new VolumeTween{to=Mathf.Clamp01(value),duration=duration};
        OriginalStageTweenHost.Add(delta=> {
            if(source==null)return false;
            if(!fade.started){fade.from=source.volume;fade.started=true;}
            fade.elapsed+=delta;float t=Mathf.Clamp01(fade.elapsed/fade.duration);
            source.volume=Mathf.LerpUnclamped(fade.from,fade.to,1-(1-t)*(1-t));
            return t<1;
        });
    }
}
