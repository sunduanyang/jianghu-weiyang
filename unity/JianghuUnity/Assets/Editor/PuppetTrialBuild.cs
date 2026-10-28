using UnityEditor;
using UnityEngine;

namespace Jianghu.Editor {
public static class PuppetTrialBuild {
    public static void Build(){
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach(string guid in AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/Resources/PuppetTrial"})){
            var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.alphaIsTransparency=true;
            importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Bilinear;
            importer.SaveAndReimport();
        }
        AppearanceBuild.Build();
    }
}
}
