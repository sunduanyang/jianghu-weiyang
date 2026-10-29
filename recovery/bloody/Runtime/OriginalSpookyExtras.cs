using System;
using System.Collections;
using System.Linq;
using Jianghu;
using UnityEngine;
using Random=UnityEngine.Random;

// SpookyEnviroExtras.Refresh 0x3412adc and bh/bi.MoveNext. This is called by
// the source environment branch; no calendar, automatic OnEnable, or timer.
public sealed class OriginalSpookyExtras : MonoBehaviour {
    OriginalVisualCatalog catalog;
    J data,batSfx,howlSfx;
    AudioSource batAudio,howlAudio;
    ParticleSystem bats;
    bool initialized;
    public void Initialize(OriginalVisualCatalog visualCatalog,Transform environmentRoot) {
        if(initialized)return;
        catalog=visualCatalog;
        var row=OriginalEnvironmentSpecialSource.Data["records"].A.Single(r=>r["script"].S=="SpookyEnviroExtras");data=row["data"];
        var howl=OriginalEnvironmentSpecialSource.Record(row["assets"]["spookyHowlsSfx"].S);
        var bat=OriginalEnvironmentSpecialSource.Record(row["assets"]["spookyBatsSfx"].S);
        howlAudio=OriginalEnvironmentSpecialSource.Component<AudioSource>(howl,"audioSource",environmentRoot,environmentRoot.name);
        batAudio=OriginalEnvironmentSpecialSource.Component<AudioSource>(bat,"audioSource",environmentRoot,environmentRoot.name);
        bats=OriginalEnvironmentSpecialSource.Component<ParticleSystem>(row,"spookyBats",environmentRoot,environmentRoot.name);
        howlSfx=AudioObject(howl["assets"]["audioObject"].S);batSfx=AudioObject(bat["assets"]["audioObject"].S);
        initialized=true;
    }
    J AudioObject(string identity)=>catalog.Data["scene"].A.Single(r=>r["script"].S=="AudioObject"&&OriginalEnvironmentSource.Identity(r)==identity);
    public static bool SourceCanRun(int hour,bool spooky)=>unchecked((uint)(hour-20))<=0xffffffeeu&&spooky;
    public void RefreshSpookyCoroutines(int hour,bool spooky) {
        StopAllCoroutines();
        if(!SourceCanRun(hour,spooky))return;
        if(!initialized)throw new InvalidOperationException("Initialize original spooky extras before Refresh");
        // Source ccf/bh (bats) starts first and immediately draws its delay.
        StartCoroutine(Bats());StartCoroutine(Howls());
    }
    IEnumerator Bats() {
        while(true) {
            yield return new WaitForSeconds(Random.Range((float)data["DelayBetweenBats"]["min"].N,(float)data["DelayBetweenBats"]["max"].N));
            bats.Play(true);PlaySingleSfx(batAudio,batSfx);
        }
    }
    IEnumerator Howls() {
        while(true) {
            yield return new WaitForSeconds(Random.Range((float)data["DelayBetweenHowls"]["min"].N,(float)data["DelayBetweenHowls"]["max"].N));
            PlaySingleSfx(howlAudio,howlSfx);
        }
    }
    // SingleSFX.PlaySound is not bg.PlaySound: pitch -> Stop -> clip draw ->
    // volume draw -> PlayOneShot(volume), without changing AudioSource.volume.
    void PlaySingleSfx(AudioSource audio,J row) {
        var d=row["data"];audio.pitch=Random.Range((float)d["pitch"]["min"].N,(float)d["pitch"]["max"].N);audio.Stop();
        int index=Random.Range(0,d["audioClip"].A.Count);var clip=catalog.Get<AudioClip>(row["assets"]["audioClip["+index+"]"].S);
        if(clip==null)throw new InvalidOperationException("Original spooky SingleSFX clip missing");
        audio.PlayOneShot(clip,Random.Range((float)d["volume"]["min"].N,(float)d["volume"]["max"].N));
    }
}
