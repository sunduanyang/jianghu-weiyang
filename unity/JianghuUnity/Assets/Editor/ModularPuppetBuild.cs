using UnityEditor;
using UnityEngine;
namespace Jianghu.Editor {
public static class ModularPuppetBuild {
    public static void Build(){
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach(string guid in AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/Resources/ModularPuppet"})){
            var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.alphaIsTransparency=true;
            importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Bilinear;importer.maxTextureSize=2048;importer.SaveAndReimport();
        }
        BuildTools.BuildWindows();
    }
}
}
