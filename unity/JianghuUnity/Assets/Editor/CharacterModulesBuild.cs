using UnityEditor;
using UnityEngine;
namespace Jianghu.Editor {
public static class CharacterModulesBuild {
    public static void Build(){
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach(string guid in AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/Resources/Characters"})){
            var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
            bool dataMap=importer.assetPath.EndsWith("/part-map.png");
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.npotScale=TextureImporterNPOTScale.None;importer.alphaIsTransparency=!dataMap;importer.sRGBTexture=!dataMap;importer.mipmapEnabled=false;importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=dataMap?FilterMode.Point:FilterMode.Bilinear;importer.maxTextureSize=2048;importer.SaveAndReimport();
        }
        BuildTools.BuildWindows();
    }
}
}
