using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Jianghu.Editor {
public static class BuildTools {
    // Import a complete prepared pack while this editor owns the project lock.
    // This keeps parallel game builds from seeing half-updated atlases/indexes.
    public static void BuildPreparedArt(){
        string source=Path.GetFullPath(GameClient.Arg("-artInput")),root=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.."));
        if(!source.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!File.Exists(Path.Combine(source,"PaperCast/index.json")))throw new InvalidOperationException("A complete prepared art pack inside the workspace is required");
        AssetDatabase.StartAssetEditing();
        try{
            foreach(string folder in new[]{"PaperCast","PaperReference","PaperBust"}){
                string destination=Path.Combine(Application.dataPath,"Resources",folder);Directory.CreateDirectory(destination);
                foreach(string file in Directory.GetFiles(Path.Combine(source,folder))){
                    string target=Path.Combine(destination,Path.GetFileName(file));
                    for(int attempt=0;;attempt++)try{File.Copy(file,target,true);break;}catch(IOException){if(attempt>=8)throw;System.Threading.Thread.Sleep(200);}catch(UnauthorizedAccessException){if(attempt>=8)throw;System.Threading.Thread.Sleep(200);}
                }
            }
        }finally{AssetDatabase.StopAssetEditing();}
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);BuildWindows();
    }
    [MenuItem("江湖/打开主场景")]
    public static void CreateScene(){
        Directory.CreateDirectory("Assets/Scenes");
        if(!Application.isBatchMode&&!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
        if(!File.Exists("Assets/Scenes/Jianghu.unity")){
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var camera=new GameObject("山河摄像机").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.94f,.92f,.85f);camera.orthographic=true;camera.tag="MainCamera";
            EditorSceneManager.SaveScene(scene,"Assets/Scenes/Jianghu.unity");
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/Scenes/Jianghu.unity",true)};
            PlayerSettings.companyName="JianghuStudio";PlayerSettings.productName="江湖未央 Unity";PlayerSettings.bundleVersion="0.3.0";
            PlayerSettings.defaultScreenWidth=1680;PlayerSettings.defaultScreenHeight=960;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.runInBackground=true;PlayerSettings.resizableWindow=true;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);
            PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.Standalone,ManagedStrippingLevel.Disabled);
            PlayerSettings.allowUnsafeCode=false;
        }else EditorSceneManager.OpenScene("Assets/Scenes/Jianghu.unity",OpenSceneMode.Single);
        AssetDatabase.SaveAssets();
        Debug.Log("JIANGHU_SCENE_READY");
    }
    [MenuItem("江湖/构建 Windows 游戏")]
    public static void BuildWindows(){
        if(!Application.isBatchMode&&!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
        CreateScene();
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{UnityEngine.Rendering.GraphicsDeviceType.Direct3D11});
        foreach(string guid in AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/Resources/PaperCast"})){
            var importer=AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as TextureImporter;
            if(importer!=null&&(importer.isReadable||importer.textureCompression!=TextureImporterCompression.Uncompressed||!importer.mipmapEnabled)){
                importer.isReadable=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.mipmapEnabled=true;importer.alphaIsTransparency=true;importer.SaveAndReimport();
            }
        }
        string output=GameClient.Arg("-buildOutput",Path.GetFullPath(Path.Combine(Application.dataPath,"../../Build/Current/江湖未央.exe")));Directory.CreateDirectory(Path.GetDirectoryName(output));
        foreach(string guid in AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/Resources/CityBiomes"})){
            var importer=AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as TextureImporter;
            if(importer!=null){importer.textureCompression=TextureImporterCompression.Uncompressed;importer.mipmapEnabled=true;importer.alphaIsTransparency=true;importer.wrapMode=TextureWrapMode.Clamp;importer.maxTextureSize=512;importer.SaveAndReimport();}
        }
        foreach(string guid in AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/Resources/CitySettlements"})){
            var importer=AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as TextureImporter;
            if(importer!=null){importer.textureCompression=TextureImporterCompression.Uncompressed;importer.mipmapEnabled=true;importer.alphaIsTransparency=true;importer.wrapMode=TextureWrapMode.Clamp;importer.maxTextureSize=1024;importer.SaveAndReimport();}
        }
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Scenes/Jianghu.unity"},locationPathName=output,target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
        if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Windows build failed: "+report.summary.result);
        Debug.Log("JIANGHU_BUILD_OK "+output+" bytes="+report.summary.totalSize);
    }
}
}
