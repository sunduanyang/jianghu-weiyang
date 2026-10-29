using System.Linq;
using UnityEngine;
using Random=UnityEngine.Random;

// SourcePrefab_HandItem Awake/cqi/BloodyUpHandItem and CharacterImpactFeed's
// original FieldRVA BloodyTable (980AAA0F875821EE...7CF609).
public sealed class OriginalWeaponAppearance : MonoBehaviour {
    static readonly int Offset=Shader.PropertyToID("Vector1_2EC99CC9");
    static readonly int Amount=Shader.PropertyToID("Vector1_47037C59");
    static readonly float[] BloodyTable={0,.0002f,.003f,.007f,.1f,.0005f,0,0};
    MaterialPropertyBlock properties;
    Renderer[] renderers;
    float[] offsets;
    float bloodAmount;
    OriginalItem item;
    public void Initialize(OriginalItem value) {
        // Native-backed Unity objects must be created on the main thread,
        // after AddComponent has finished constructing this behaviour.
        properties=new MaterialPropertyBlock();
        item=value;
        renderers=item.config["renderers"].A.Select(r=>item.Ref<Renderer>(r)).ToArray();
        offsets=new float[renderers.Length];
        for(int i=0;i<offsets.Length;i++)offsets[i]=Random.Range(0f,1f);
        Apply(.35f);
    }
    void Apply(float amount) {
        for(int i=0;i<renderers.Length;i++)if(renderers[i]!=null) {
            renderers[i].GetPropertyBlock(properties);properties.SetFloat(Offset,offsets[i]);properties.SetFloat(Amount,amount);renderers[i].SetPropertyBlock(properties);
        }
    }
    public void BloodyUp(int index) {
        if(item==null||index<=0||index>=BloodyTable.Length)return;
        bloodAmount=Mathf.Max(.5f,bloodAmount/(item.isShield?1.05f:1));
        bloodAmount=bloodAmount>=.8f?.8f:bloodAmount+(BloodyTable[index]+.025f)/1.5f;
        Apply(bloodAmount);
    }
}
