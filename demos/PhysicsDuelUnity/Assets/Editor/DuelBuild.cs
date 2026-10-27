using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DuelBuild {
    public static void CalibrateGrip() {
        Physics2D.simulationMode=SimulationMode2D.Script;
        var a=new GameObject("grip-parent");a.transform.SetPositionAndRotation(new Vector3(50,50),Quaternion.Euler(0,0,37));
        var ar=a.AddComponent<Rigidbody2D>();ar.bodyType=RigidbodyType2D.Kinematic;
        var b=new GameObject("grip-child");b.transform.SetPositionAndRotation(new Vector3(50,50),Quaternion.Euler(0,0,37));
        var br=b.AddComponent<Rigidbody2D>();br.gravityScale=0;b.AddComponent<BoxCollider2D>().size=new Vector2(.1f,1);
        var fixedGrip=b.AddComponent<FixedJoint2D>();fixedGrip.connectedBody=ar;fixedGrip.autoConfigureConnectedAnchor=false;fixedGrip.anchor=fixedGrip.connectedAnchor=Vector2.zero;
        Debug.Log("DUEL_FIXED_BEFORE reference="+fixedGrip.referenceAngle+" parent="+ar.rotation+" child="+br.rotation);
        for(int i=0;i<12;i++)Physics2D.Simulate(1f/120);
        Debug.Log("DUEL_FIXED_AFTER reference="+fixedGrip.referenceAngle+" parent="+ar.rotation+" child="+br.rotation);
        UnityEngine.Object.DestroyImmediate(b);UnityEngine.Object.DestroyImmediate(a);
        Physics2D.simulationMode=SimulationMode2D.FixedUpdate;
    }
    public static void Calibrate() {
        Physics2D.simulationMode=SimulationMode2D.Script;
        var a=new GameObject("motor-parent").AddComponent<Rigidbody2D>();a.bodyType=RigidbodyType2D.Kinematic;a.position=new Vector2(100,100);
        var b=new GameObject("motor-child").AddComponent<Rigidbody2D>();b.position=a.position;b.gravityScale=0;b.gameObject.AddComponent<BoxCollider2D>().size=new Vector2(.2f,1);
        var j=b.gameObject.AddComponent<HingeJoint2D>();j.connectedBody=a;j.autoConfigureConnectedAnchor=false;j.anchor=j.connectedAnchor=Vector2.zero;
        j.useMotor=true;j.motor=new JointMotor2D {motorSpeed=90,maxMotorTorque=10};
        for(int i=0;i<24;i++)Physics2D.Simulate(1f/120);
        Debug.Log("DUEL_MOTOR_CALIBRATION rotation="+b.rotation+" joint="+j.jointAngle+" speed="+j.jointSpeed+" reference="+j.referenceAngle);
        UnityEngine.Object.DestroyImmediate(b.gameObject);UnityEngine.Object.DestroyImmediate(a.gameObject);
        Physics2D.simulationMode=SimulationMode2D.FixedUpdate;
    }
    [MenuItem("纸上争锋/构建 Windows Demo")]
    public static void Build() {
        Physics2D.simulationMode=SimulationMode2D.FixedUpdate;
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.88f,.84f,.74f);
        camera.orthographic = true;
        camera.gameObject.AddComponent<AudioListener>();
        new GameObject("Physics Duel").AddComponent<PhysicsDuel>();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Duel.unity");
        EditorBuildSettings.scenes = new[]{new EditorBuildSettingsScene("Assets/Scenes/Duel.unity",true)};
        PlayerSettings.companyName = "JianghuStudio";
        PlayerSettings.productName = "纸上争锋 · 物理格斗 Demo";
        PlayerSettings.bundleVersion = "0.9.0";
        PlayerSettings.defaultScreenWidth = 1440;
        PlayerSettings.defaultScreenHeight = 900;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = false;
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.Standalone, ManagedStrippingLevel.Disabled);
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{UnityEngine.Rendering.GraphicsDeviceType.Direct3D11});
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/Resources/PaperCast"})) {
            var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = true;
            importer.alphaIsTransparency = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
        }
        string output = Path.GetFullPath("Build-v09/纸上争锋.exe");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[]{"Assets/Scenes/Duel.unity"}, locationPathName = output,
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded) throw new Exception("Duel build failed: "+report.summary.result);
        Debug.Log("PHYSICS_DUEL_BUILD_OK "+output);
    }
}
