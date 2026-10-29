using System;
using System.Collections;
using System.Linq;
using Jianghu;
using UnityEngine;
using Random=UnityEngine.Random;

// BystanderCustomization: Awake 0x36c1114, Start 0x36c1174,
// Customize 0x36c1388, delayed ordinary body 0x36c17d0.
public sealed class OriginalBystanderCustomization : MonoBehaviour {
    public string SourceIdentity,SourceRootName;
    public int SourceSeed {get;private set;}
    public bool SourceAwakeDeleted {get;private set;}
    J record,data;
    Transform instanceRoot,rootTransform,torsoTransform;
    SpriteRenderer head,eyes,hat,top1,top2,bottom;
    SpriteRenderer[] skin;
    void Bind() {
        if(record!=null)return;
        record=OriginalStageActors.Record(SourceIdentity);data=record["data"];
        instanceRoot=OriginalStageActors.InstanceRoot(transform,SourceRootName);
        rootTransform=Node("rootTransform");torsoTransform=Node("torsoTransform");
        head=Renderer("headSpriteRenderer");eyes=Renderer("eyesSpriteRenderer");hat=Renderer("hatSpriteRenderer");
        top1=Renderer("topL1SpriteRenderer");top2=Renderer("topL2SpriteRenderer");bottom=Renderer("bottomSpriteRenderer");
        skin=data["skinSpriteRenderers"].A.Select((_,i)=>Renderer("skinSpriteRenderers["+i+"]")).ToArray();
    }
    Transform Node(string key)=>OriginalEnvironmentSource.Node(OriginalStageActors.Catalog,record,key,instanceRoot,SourceRootName);
    SpriteRenderer Renderer(string key)=>OriginalEnvironmentSource.Component<SpriteRenderer>(OriginalStageActors.Catalog,record,key,instanceRoot,SourceRootName);
    Sprite[] Sprites(string key)=>OriginalEnvironmentSource.Sprites(OriginalStageActors.Catalog,record,key);
    Gradient Gradient(string key)=>OriginalVisualCatalog.Gradient(data[key]);
    void Awake() {
        Bind();int chance=data["chanesToDeleteOutOf10"].I;
        // Intentionally no InitState: native Awake consumes the current shared
        // RNG at the actual prefab callback boundary.
        if(chance>0&&Random.Range(0,11)<=chance){SourceAwakeDeleted=true;rootTransform.gameObject.SetActive(false);}
    }
    void Start() {
        Bind();SourceSeed=unchecked(OriginalStageActors.Context.ArenaSeed+transform.GetSiblingIndex()+Time.frameCount);Random.InitState(SourceSeed);
        float x=rootTransform.localPosition.x+Random.Range((float)data["randomPosition"]["min"].N,(float)data["randomPosition"]["max"].N);
        rootTransform.localPosition=new Vector3(x,transform.localPosition.y,0);
        var torso=torsoTransform.localPosition;torso.y+=Random.Range((float)data["randomHeight"]["min"].N,(float)data["randomHeight"]["max"].N);torso.z=0;torsoTransform.localPosition=torso;
        StartCoroutine(CustomizeAfterDelay());
    }
    IEnumerator CustomizeAfterDelay() {
        yield return new WaitForSecondsRealtime(.025f);
        ApplyOrdinaryAppearance();
    }
    public void ApplyOrdinaryAppearance() {
        Bind();
        bool spooky=OriginalStageActors.Context.SpookyMode;
        Random.InitState(SourceSeed);
        Color color=Gradient(spooky?"boneColorGradient":"possibleSkinColors").Evaluate(Random.Range(0f,1f));
        var heads=Sprites("possibleHeadSprites");head.sprite=heads[Random.Range(0,heads.Length)];
        if(spooky) {
            eyes.enabled=false;var bones=Sprites("spookySprites");
            for(int i=0;i<5;i++)skin[i].sprite=bones[i];
        }
        Customize(eyes,Sprites("possibleEyesSprites"),Gradient("possibleColorsNull"),unchecked(SourceSeed+1));
        foreach(var renderer in skin)renderer.color=color;
        Customize(hat,Sprites("possibleHatSprites"),Gradient("possibleHatColors"),unchecked(SourceSeed+3));
        Customize(top1,Sprites("possibleTopL1Sprites"),Gradient("possibleTopL1Colors"),unchecked(SourceSeed+5));
        Customize(top2,Sprites("possibleTopL2Sprites"),Gradient("possibleTopL2Colors"),unchecked(SourceSeed+7));
        Customize(bottom,Sprites("possibleBottomSprites"),Gradient("possibleBottomColors"),unchecked(SourceSeed+11));
    }
    public void Customize(SpriteRenderer renderer,Sprite[] sprites,Gradient colors,int seed) {
        Random.InitState(unchecked(transform.GetSiblingIndex()+seed));
        renderer.sprite=sprites[Random.Range(0,sprites.Length)];renderer.color=colors.Evaluate(Random.Range(0f,1f));
    }
}
