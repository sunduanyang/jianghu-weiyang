using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class OriginalRecoveryBuild {
    public static void Build() {
        var original=EditorSceneManager.OpenScene("Assets/Game/Scenes/MainScene.unity",OpenSceneMode.Single);
        var environment=original.GetRootGameObjects().First(g=>g.name=="Environment");
        var preview=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        SceneManager.SetActiveScene(preview);
        var clone=UnityEngine.Object.Instantiate(environment);
        SceneManager.MoveGameObjectToScene(clone,preview);clone.SetActive(true);
        // The recovered scene is preserved separately; this preview exercises
        // original prefabs and geometry without invoking empty script bodies.
        foreach(var c in clone.GetComponentsInChildren<Camera>(true))UnityEngine.Object.DestroyImmediate(c.gameObject);
        var floor=clone.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Floor");
        if(floor!=null){floor.gameObject.SetActive(true);for(var t=floor.parent;t!=null;t=t.parent)t.gameObject.SetActive(true);}
        var sky=new GameObject("Recovery camera");
        var camera=sky.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=2.3f;
        camera.transform.position=new Vector3(0,1.05f,-10);camera.backgroundColor=new Color(.68f,.74f,.70f);
        camera.clearFlags=CameraClearFlags.SolidColor;sky.AddComponent<AudioListener>();
        var boot=new GameObject("Recovery preview").AddComponent<OriginalPreview>();
        boot.playerPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameObject/Character_Player_1.prefab");
        boot.enemyPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameObject/Character_Enemy.prefab");
        if(boot.playerPrefab==null||boot.enemyPrefab==null)throw new InvalidOperationException("Original character prefabs missing");
        EditorSceneManager.CloseScene(original,true);
        Directory.CreateDirectory("Assets/Recovery/Scenes");
        EditorSceneManager.SaveScene(preview,"Assets/Recovery/Scenes/OriginalHierarchy.unity");
        PlayerSettings.companyName="Local recovery";PlayerSettings.productName="Bloody Bastards Recovery";
        PlayerSettings.defaultScreenWidth=1280;PlayerSettings.defaultScreenHeight=720;
        PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
        PlayerSettings.runInBackground=true;
        PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone,ApiCompatibilityLevel.NET_Standard);
        string build=Path.GetFullPath("../../Build/BloodyBastardsRecovery/BloodyBastardsRecovery.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(build));
        var report=BuildPipeline.BuildPlayer(new[]{"Assets/Recovery/Scenes/OriginalHierarchy.unity"},build,BuildTarget.StandaloneWindows64,BuildOptions.None);
        if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("Recovery preview build failed: "+report.summary.result);
        Debug.Log("BLOODY_RECOVERY_PREVIEW_BUILD_OK "+build);
    }
}
