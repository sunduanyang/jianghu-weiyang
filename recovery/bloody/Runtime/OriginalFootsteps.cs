using System;
using System.Linq;
using Jianghu;
using UnityEngine;
using Random=UnityEngine.Random;

// FootstepsSFX 0x3412124..0x3412368 and CharacterConfigApplier.cfc.
// Source armor families select looping recordings, with movement controlling
// volume continuously. Native startup also reseeds the shared Unity RNG.
public sealed class OriginalFootsteps : MonoBehaviour {
    OriginalRig rig;
    OriginalVisualCatalog catalog;
    J source;
    AudioSource audioSource;
    int armor;
    static readonly string[] Fields={"footstepsBarefoot","footstepsClothLeather","footstepsMail","footstepsPlate"};
    public void Initialize(OriginalRig owner,OriginalVisualCatalog data,J record) {
        rig=owner;catalog=data;source=record;
        var binding=catalog.Data["assets"][record["assets"]["audioSource"].S];
        audioSource=GetComponents<AudioSource>().ElementAtOrDefault(binding["component_index"].I);
        if(audioSource==null)throw new InvalidOperationException("Original FootstepsSFX audioSource is missing: "+record["path"].S);
    }
    public void SetArmorMaterial(int material) {
        // Source cfc leaves Flesh (1) unchanged; None (0) is barefoot.
        if(material==1)return;
        SetArmor(material==0?0:material<4?1:material==4?2:3);
    }
    void SetArmor(int value) {
        if(audioSource==null)return;
        if(value==armor&&audioSource.isPlaying)return;armor=value;
        string identity=source["assets"][Fields[value]].S;
        var row=catalog.Data["scene"].A.FirstOrDefault(r=>r["script"].S=="AudioObject"&&r["source"]["file"].S+":"+r["source"]["id"].S==identity);
        if(row==null)throw new InvalidOperationException("Original footstep AudioObject is missing: "+identity);
        int count=row["data"]["audioClip"].A.Count;if(count==0)return;
        int index=Random.Range(0,count);string clipIdentity=row["assets"]["audioClip["+index+"]"].S;
        audioSource.clip=catalog.Get<AudioClip>(clipIdentity);
        if(audioSource.clip==null)throw new InvalidOperationException("Original footstep AudioClip is missing: "+clipIdentity);
        audioSource.loop=true;Random.InitState((int)rig.transform.position.x);audioSource.PlayDelayed(Random.Range(0f,2f));
    }
    void Update(){if(audioSource!=null)audioSource.volume=Mathf.Clamp01(Mathf.Abs(rig.OriginalFootstepVelocity)/(float)source["data"]["speedAtFullVolume"].N);}
}
