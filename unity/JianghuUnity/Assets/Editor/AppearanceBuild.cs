using UnityEditor;
using UnityEngine;

namespace Jianghu.Editor {
public static class AppearanceBuild {
    public static void Build(){
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach(string guid in AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/Resources/Appearance/Masks"})){
            var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
            importer.sRGBTexture=false;importer.alphaIsTransparency=false;
            importer.alphaSource=TextureImporterAlphaSource.FromInput;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
        }
        BuildTools.BuildWindows();
    }
}
}
