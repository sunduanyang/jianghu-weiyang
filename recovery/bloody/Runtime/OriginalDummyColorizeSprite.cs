using System;
using System.Collections;
using System.Collections.Generic;
using Jianghu;
using UnityEngine;

// ColorizeSprite.OnEnable/gi.MoveNext 0x36c2fb4/0x36c319c. The source
// serializes LightTintType=Foreground but does not read it in this coroutine.
public sealed class OriginalDummyColorizeSprite : MonoBehaviour {
    J data;
    SpriteRenderer[] renderers;
    Gradient possibleColors;
    Func<int> currentSeed;
    Func<bool> environmentFinished;
    bool initialized;
    readonly List<float> lastDraws=new List<float>();
    public int OriginalLightTintType=>data==null?0:data["LightTintType"].I;
    public int OriginalSeedUsed {get;private set;}
    public float OriginalSeedInput {get;private set;}
    public int AppliedSpriteCount {get;private set;}
    public int SourceSpriteCount=>renderers==null?0:renderers.Length;
    public IReadOnlyList<float> LastDraws=>lastDraws;
    public event Action<OriginalDummyColorizeSprite> Applied;

    // Pass the host's original hq.CurrentSeed and the captured environment
    // customizer's IsFinished predicate. Seeding happens before WaitUntil;
    // do not substitute the completion flag with an immediate recoloring.
    public static OriginalDummyColorizeSprite Attach(GameObject dummyRoot,Func<int> currentSeed,Func<bool> environmentFinished) {
        if(dummyRoot==null)throw new ArgumentNullException(nameof(dummyRoot));
        var text=Resources.Load<TextAsset>("OriginalDummyColorSource");
        if(text==null)throw new InvalidOperationException("OriginalDummyColorSource is missing");
        var rows=J.Parse(text.text)["records"].A;
        if(rows.Count!=1)throw new InvalidOperationException("Original dummy requires its single source ColorizeSprite");
        var row=rows[0];Transform node=row["path"].S.Length==0?dummyRoot.transform:dummyRoot.transform.Find(row["path"].S);
        if(node==null)throw new InvalidOperationException("Original dummy ColorizeSprite path missing");
        var component=node.gameObject.AddComponent<OriginalDummyColorizeSprite>();
        component.Initialize(dummyRoot.transform,row["data"],currentSeed,environmentFinished);return component;
    }
    public void Initialize(Transform root,J source,Func<int> seed,Func<bool> finished) {
        if(initialized)throw new InvalidOperationException("Original dummy color component is already initialized");
        data=source??throw new ArgumentNullException(nameof(source));currentSeed=seed??throw new ArgumentNullException(nameof(seed));environmentFinished=finished??throw new ArgumentNullException(nameof(finished));
        var sourceRenderers=data["spriteRenderers"].A;renderers=new SpriteRenderer[sourceRenderers.Count];
        for(int i=0;i<renderers.Length;i++) {
            var reference=sourceRenderers[i];Transform node=reference["path"].S.Length==0?root:root.Find(reference["path"].S);
            if(node==null)throw new InvalidOperationException("Original dummy color renderer path missing: "+reference["path"].S);
            var components=node.GetComponents<SpriteRenderer>();int index=reference["index"].I;
            if(index>=components.Length)throw new InvalidOperationException("Original dummy color renderer component missing");
            renderers[i]=components[index];
        }
        possibleColors=OriginalVisualCatalog.Gradient(data["possibleColors"]);initialized=true;
        if(isActiveAndEnabled)ApplyOriginalEnable();
    }
    void OnEnable(){if(initialized)ApplyOriginalEnable();}
    void ApplyOriginalEnable(){if(data["customizeOnEnable"].B)Customize(EnableSeed(currentSeed(),transform.GetSiblingIndex()));}
    public static float EnableSeed(int seed,int siblingIndex)=>(float)unchecked(seed+siblingIndex);
    // ARM FCVTZS saturates finite overflow; the source IL2CPP path special
    // cases positive infinity to Int32.MinValue before calling InitState.
    public static int NativeSeed(float value) {
        if(float.IsPositiveInfinity(value))return int.MinValue;
        if(float.IsNaN(value))return 0;
        if(value>=2147483648f)return int.MaxValue;
        if(value<=-2147483648f)return int.MinValue;
        return (int)value;
    }
    public void Customize(float seed){StartCoroutine(CustomizeSource(seed));}
    IEnumerator CustomizeSource(float seed) {
        OriginalSeedInput=seed;OriginalSeedUsed=NativeSeed(seed);
        UnityEngine.Random.InitState(OriginalSeedUsed);
        yield return new WaitUntil(()=>environmentFinished());
        AppliedSpriteCount=0;lastDraws.Clear();
        foreach(var renderer in renderers) {
            // Native first overwrites any previous tint with White, then reads
            // it and multiplies by a separately drawn source gradient color.
            renderer.color=Color.white;Color prior=renderer.color;
            float t=UnityEngine.Random.Range(0f,1f);lastDraws.Add(t);
            renderer.color=prior*possibleColors.Evaluate(t);AppliedSpriteCount++;
        }
        Applied?.Invoke(this);
    }
}
