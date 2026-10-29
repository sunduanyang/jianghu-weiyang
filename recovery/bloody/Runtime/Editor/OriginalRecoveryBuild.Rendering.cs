using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static partial class OriginalRecoveryBuild {
    // Kept separate from RestorePipeline while camera-output diagnosis runs.
    // Call after copying the Renderer2DData source settings.
    static void RestoreOriginalRendererFeatures(Renderer2DData renderer,OriginalVisualCatalog catalog) {
        var record=catalog.Data["scene"].A.First(x=>x["script"].S=="TopRimRendererFeature");
        var feature=AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(renderer)).OfType<OriginalTopRimRendererFeature>().FirstOrDefault();
        if(feature==null){feature=ScriptableObject.CreateInstance<OriginalTopRimRendererFeature>();feature.name=record["data"]["m_Name"].S;AssetDatabase.AddObjectToAsset(feature,renderer);}
        CopyProperties(feature,record["data"]);feature.SetActive(record["data"]["m_Active"].B);
        renderer.rendererFeatures.Clear();renderer.rendererFeatures.Add(feature);
        feature.Create();EditorUtility.SetDirty(feature);EditorUtility.SetDirty(renderer);
        var flipRecord=catalog.Data["scene"].A.First(x=>x["script"].S=="FlipViewRendererFeature");
        var flip=AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(renderer)).OfType<OriginalFlipViewRendererFeature>().FirstOrDefault();
        if(flip==null){flip=ScriptableObject.CreateInstance<OriginalFlipViewRendererFeature>();flip.name=flipRecord["data"]["m_Name"].S;AssetDatabase.AddObjectToAsset(flip,renderer);}
        CopyProperties(flip,flipRecord["data"]);flip.flipShader=catalog.RecoveredShader("Hidden/FlipView");flip.SetActive(flipRecord["data"]["m_Active"].B);
        renderer.rendererFeatures.Add(flip);flip.Create();EditorUtility.SetDirty(flip);
        AssetDatabase.SaveAssets();
    }
}
