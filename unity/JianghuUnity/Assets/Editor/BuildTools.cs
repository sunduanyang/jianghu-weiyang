using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Jianghu.Editor {
public static class BuildTools {
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
        foreach(string guid in AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/Resources/Puppets"})){
            var importer=AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as TextureImporter;
            if(importer!=null&&(!importer.isReadable||importer.textureCompression!=TextureImporterCompression.Uncompressed||importer.mipmapEnabled)){
                importer.isReadable=true;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.mipmapEnabled=false;importer.alphaIsTransparency=true;importer.SaveAndReimport();
            }
        }
        string output=GameClient.Arg("-buildOutput",Path.GetFullPath(Path.Combine(Application.dataPath,"../../Build/Current/江湖未央.exe")));Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Scenes/Jianghu.unity"},locationPathName=output,target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
        if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Windows build failed: "+report.summary.result);
        Debug.Log("JIANGHU_BUILD_OK "+output+" bytes="+report.summary.totalSize);
    }
}
}
