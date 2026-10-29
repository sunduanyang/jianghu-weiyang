using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Jianghu;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static partial class OriginalRecoveryBuild {
    static partial void AttachOriginalHud(Scene sourceScene,Scene targetScene,OriginalPreview preview);
    public static void Build() {
        RestoreEngineMaterials();
        var catalog=BindVisualCatalog();
        RestoreOriginalPrefabLights();
        RestoreOriginalFirstStageActors();
        RestoreOriginalFirstStageMovement();
        RestorePipeline(catalog);
        var original=EditorSceneManager.OpenScene("Assets/Game/Scenes/MainScene.unity",OpenSceneMode.Single);
        var environment=original.GetRootGameObjects().First(g=>g.name=="Environment");
        var preview=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        SceneManager.SetActiveScene(preview);
        var clone=UnityEngine.Object.Instantiate(environment);
        SceneManager.MoveGameObjectToScene(clone,preview);clone.SetActive(true);
        clone.name=environment.name;
        // Original sibling indices participate in environment identity and
        // prop seeds. Preserve the original transforms when replacing view.
        foreach(var c in clone.GetComponentsInChildren<Camera>(true))c.enabled=false;
        foreach(var listener in clone.GetComponentsInChildren<AudioListener>(true))listener.enabled=false;
        var floor=clone.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Floor");
        if(floor!=null){floor.gameObject.SetActive(true);for(var t=floor.parent;t!=null;t=t.parent)t.gameObject.SetActive(true);}
        var lighting=original.GetRootGameObjects().First(g=>g.name=="TimeOfDayController");
        var lightingClone=UnityEngine.Object.Instantiate(lighting);lightingClone.name=lighting.name;
        SceneManager.MoveGameObjectToScene(lightingClone,preview);lightingClone.SetActive(true);
        RestoreLights(catalog,preview);
        lightingClone.AddComponent<OriginalTimeOfDay>();
        var weather=clone.transform.Find("MainContainer/WeatherManager");
        if(weather==null)throw new InvalidOperationException("Original WeatherManager hierarchy missing");
        weather.gameObject.AddComponent<OriginalWeather>();
        var originalCamera=original.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).First(c=>c.name=="MainCamera");
        var sky=new GameObject("Recovery camera");sky.tag="MainCamera";var camera=sky.AddComponent<Camera>();EditorUtility.CopySerialized(originalCamera,camera);
        // The source world uses perspective depth for all background planes.
        // Its Cinemachine player framing is restored independently at runtime.
        camera.enabled=true;camera.targetTexture=null;camera.transform.position=new Vector3(0,.875f,-2.35f);
        camera.transform.rotation=Quaternion.identity;sky.AddComponent<AudioListener>();
        var cameraData=sky.AddComponent<UniversalAdditionalCameraData>();cameraData.SetRenderer(0);
        sky.AddComponent<OriginalCameraFraming>().environmentTarget=clone.transform;
        var runner=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameObject/Runner.prefab");
        var originalVfx=runner==null?null:runner.transform.Find("VFXManager");
        if(originalVfx!=null){var effects=UnityEngine.Object.Instantiate(originalVfx.gameObject);effects.name="VFXManager";SceneManager.MoveGameObjectToScene(effects,preview);effects.SetActive(true);effects.AddComponent<OriginalEffects>();}
        var boot=new GameObject("Recovery preview").AddComponent<OriginalPreview>();
        boot.environmentRoot=clone.transform;
        var originalAudio=original.GetRootGameObjects().Single(g=>g.name=="AudioAndMusicManager");
        var arenaAudio=UnityEngine.Object.Instantiate(originalAudio);arenaAudio.name=originalAudio.name;
        SceneManager.MoveGameObjectToScene(arenaAudio,preview);arenaAudio.SetActive(true);arenaAudio.AddComponent<OriginalArenaAmbience>();
        boot.playerPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameObject/Character_Player_1.prefab");
        boot.enemyPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameObject/Character_Enemy.prefab");
        boot.dummyPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameObject/Dummy.prefab");
        if(boot.playerPrefab==null||boot.enemyPrefab==null)throw new InvalidOperationException("Original character prefabs missing");
        AttachOriginalHud(original,preview,boot);
        EditorSceneManager.CloseScene(original,true);
        Directory.CreateDirectory("Assets/Recovery/Scenes");
        EditorSceneManager.SaveScene(preview,"Assets/Recovery/Scenes/OriginalHierarchy.unity");
        PlayerSettings.companyName="Local recovery";PlayerSettings.productName="Bloody Bastards Recovery";
        PlayerSettings.defaultScreenWidth=1600;PlayerSettings.defaultScreenHeight=900;
        PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
        PlayerSettings.resizableWindow=true;
        PlayerSettings.runInBackground=true;
        // The recovered GLES programs target the tested desktop D3D11 path.
        // D3D12Core faulted during the Build17 player's native shutdown.
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{UnityEngine.Rendering.GraphicsDeviceType.Direct3D11});
        PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone,ApiCompatibilityLevel.NET_Standard);
        var buildArgs=Environment.GetCommandLineArgs();int outputAt=Array.IndexOf(buildArgs,"-recoveryBuildOutput");
        string build=Path.GetFullPath(outputAt>=0&&outputAt+1<buildArgs.Length?buildArgs[outputAt+1]:"../../Build/BloodyBastardsRecovery/BloodyBastardsRecovery.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(build));
        var report=BuildPipeline.BuildPlayer(new[]{"Assets/Recovery/Scenes/OriginalHierarchy.unity"},build,BuildTarget.StandaloneWindows64,BuildOptions.None);
        if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("Recovery preview build failed: "+report.summary.result);
        Debug.Log("BLOODY_RECOVERY_PREVIEW_BUILD_OK "+build);
    }
    static void RestoreEngineMaterials() {
        // AssetRipper exported engine URP shaders as dummy text too. Their
        // matching 6000.4 package supplies the actual maintained shader assets.
        // Change only the object reference, retaining all original material data.
        var shaders=new Dictionary<string,Shader>();
        foreach(string guid in AssetDatabase.FindAssets("t:Shader",new[]{"Packages/com.unity.render-pipelines.universal"})) {
            var shader=AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid));if(shader!=null)shaders[shader.name]=shader;
        }
        foreach(string guid in AssetDatabase.FindAssets("t:Material",new[]{"Assets/Material"})) {
            var material=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));if(material==null||material.shader==null)continue;
            if(!shaders.TryGetValue(material.shader.name,out var shader))continue;
            var so=new SerializedObject(material);so.FindProperty("m_Shader").objectReferenceValue=shader;so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(material);
        }
        AssetDatabase.SaveAssets();
    }
    static OriginalVisualCatalog BindVisualCatalog() {
        string resources="Assets/Recovery/Resources";Directory.CreateDirectory(resources);
        string source=Path.GetFullPath("../../output/bloody-full-port-20261028/original-visual-catalog.json");
        // Read the atomically published source once, so the JSON asset and
        // bound entries cannot come from two different exporter revisions.
        string visualJson=File.ReadAllText(source);
        File.WriteAllText(resources+"/OriginalVisualSource.json",visualJson);AssetDatabase.ImportAsset(resources+"/OriginalVisualSource.json",ImportAssetOptions.ForceSynchronousImport);
        var data=J.Parse(visualJson);string path=resources+"/OriginalVisualCatalog.asset";
        var catalog=AssetDatabase.LoadAssetAtPath<OriginalVisualCatalog>(path);
        if(catalog==null){catalog=ScriptableObject.CreateInstance<OriginalVisualCatalog>();AssetDatabase.CreateAsset(catalog,path);}
        var entries=new List<OriginalVisualCatalog.Entry>();
        foreach(var p in data["assets"].D) {
            var paths=p.Value["asset_paths"].A;if(paths.Count==0)continue;
            var asset=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(paths[0].S);if(asset==null)continue;
            entries.Add(new OriginalVisualCatalog.Entry{identity=p.Key,asset=asset});
        }
        catalog.entries=entries.ToArray();catalog.sourceJson=AssetDatabase.LoadAssetAtPath<TextAsset>(resources+"/OriginalVisualSource.json");
        catalog.recoveredShaders=AssetDatabase.FindAssets("t:Shader",new[]{"Assets/Shader"}).Select(g=>AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(g))).Where(s=>s!=null).ToArray();
        EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();Debug.Log("BOUND_ORIGINAL_VISUAL_ASSETS "+entries.Count);return catalog;
    }
    static void CopyProperties(UnityEngine.Object target,J data) {
        var serialized=new SerializedObject(target);
        foreach(var pair in data.D) {
            var prop=serialized.FindProperty(pair.Key);if(prop==null||pair.Key=="m_Script"||pair.Key=="m_Name")continue;
            ApplyProperty(prop,pair.Value);
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(target);
    }
    static void ApplyProperty(SerializedProperty prop,J value) {
        if(prop.propertyType==SerializedPropertyType.ObjectReference)return;
        if(prop.isArray&&prop.propertyType!=SerializedPropertyType.String) {
            // Object arrays contain source identities, and are bound explicitly.
            if(value.A.Any(v=>v["m_PathID"].Has))return;
            prop.arraySize=value.A.Count;for(int i=0;i<prop.arraySize;i++)ApplyProperty(prop.GetArrayElementAtIndex(i),value[i]);return;
        }
        switch(prop.propertyType) {
            case SerializedPropertyType.Integer:prop.longValue=(long)value.N;break;
            case SerializedPropertyType.Boolean:prop.boolValue=value.B;break;
            case SerializedPropertyType.Float:prop.floatValue=(float)value.N;break;
            case SerializedPropertyType.String:prop.stringValue=value.S;break;
            case SerializedPropertyType.Enum:prop.intValue=value.I;break;
            case SerializedPropertyType.Color:prop.colorValue=OriginalVisualCatalog.Color(value);break;
            case SerializedPropertyType.Vector2:prop.vector2Value=new Vector2((float)value["x"].N,(float)value["y"].N);break;
            case SerializedPropertyType.Vector3:prop.vector3Value=new Vector3((float)value["x"].N,(float)value["y"].N,(float)value["z"].N);break;
            case SerializedPropertyType.LayerMask:prop.intValue=unchecked((int)(long)value["m_Bits"].N);break;
            case SerializedPropertyType.Generic:foreach(var p in value.D){var child=prop.FindPropertyRelative(p.Key);if(child!=null)ApplyProperty(child,p.Value);}break;
        }
    }
    static void RestorePipeline(OriginalVisualCatalog catalog) {
        Directory.CreateDirectory("Assets/Recovery/Rendering");
        string rendererPath="Assets/Recovery/Rendering/OriginalRenderer2D.asset",pipelinePath="Assets/Recovery/Rendering/OriginalPipeline.asset";
        var renderer=AssetDatabase.LoadAssetAtPath<Renderer2DData>(rendererPath);
        if(renderer==null){renderer=ScriptableObject.CreateInstance<Renderer2DData>();AssetDatabase.CreateAsset(renderer,rendererPath);}
        var rendererSource=catalog.Data["scene"].A.First(x=>x["script"].S=="Renderer2DData");CopyProperties(renderer,rendererSource["data"]);
        ResourceReloader.ReloadAllNullIn(renderer,"Packages/com.unity.render-pipelines.universal");
        RestoreOriginalRendererFeatures(renderer,catalog);
        var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
        if(pipeline==null){pipeline=UniversalRenderPipelineAsset.Create(renderer);AssetDatabase.CreateAsset(pipeline,pipelinePath);}
        var pipelineSource=catalog.Data["scene"].A.First(x=>x["script"].S=="UniversalRenderPipelineAsset");CopyProperties(pipeline,pipelineSource["data"]);
        var so=new SerializedObject(pipeline);var list=so.FindProperty("m_RendererDataList");list.arraySize=1;list.GetArrayElementAtIndex(0).objectReferenceValue=renderer;so.ApplyModifiedPropertiesWithoutUndo();
        GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;AssetDatabase.SaveAssets();
    }
    static Transform SceneNode(Scene scene,string path) {
        int slash=path.IndexOf('/');string root=slash<0?path:path.Substring(0,slash);
        var go=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==root);if(go==null)return null;
        return slash<0?go.transform:go.transform.Find(path.Substring(slash+1));
    }
    static void RestoreLights(OriginalVisualCatalog catalog,Scene scene) {
        foreach(var record in catalog.Data["scene"].A.Where(x=>x["script"].S=="Light2D")) {
            var node=SceneNode(scene,record["scene_path"].S);if(node==null)continue;
            var light=node.gameObject.AddComponent<Light2D>();CopyProperties(light,record["data"]);
            light.enabled=record["data"]["m_Enabled"].B;
        }
    }
    static void RestoreEnvironment(OriginalVisualCatalog catalog,GameObject root) {
        var data=catalog.Data;var environment=data["scene"].A.First(x=>x["script"].S=="EnvironmentCustomizer");
        var region=data["scene"].A.First(x=>x["script"].S=="RegionConfiguration"&&x["data"]["regionId"].I==1);
        Transform RefNode(J record,string key){string identity=record["assets"][key].S;return SceneNode(root.scene,data["assets"][identity]["scene_path"].S);}
        Sprite SpriteRef(J record,string key)=>catalog.Get<Sprite>(record["assets"][key].S);
        void Assign(string key,string sprite){var node=RefNode(environment,key);if(node!=null){var sr=node.GetComponent<SpriteRenderer>();if(sr!=null)sr.sprite=SpriteRef(region,sprite);}}
        UnityEngine.Random.InitState(0);
        Assign("Mountains","Mountains");Assign("Hills","Hills_Far");
        var graphics=RefNode(environment,"GraphicsContainerParent");
        var stage=region["data"]["stages"][0];
        var prefab=catalog.Get<GameObject>(region["assets"]["stages[0].graphicsContainer"].S);
        GameObject stageRoot=null;
        if(graphics!=null&&prefab!=null){foreach(Transform child in graphics.Cast<Transform>().ToArray())UnityEngine.Object.DestroyImmediate(child.gameObject);stageRoot=UnityEngine.Object.Instantiate(prefab,graphics);stageRoot.name=prefab.name;stageRoot.SetActive(true);}
        // CustomizeEnviroProp.Start 0x36c3438 and Customize 0x36c356c.
        foreach(var record in data["scene"].A.Where(x=>x["script"].S=="CustomizeEnviroProp")) {
            string path=record["scene_path"].S;Transform node=SceneNode(root.scene,path);
            if(node==null&&stageRoot!=null&&path.StartsWith(stageRoot.name+"/",StringComparison.Ordinal))node=stageRoot.transform.Find(path.Substring(stageRoot.name.Length+1));
            if(node==null||!record["data"]["AutoCustomize"].B)continue;
            var renderer=node.GetComponent<SpriteRenderer>();if(renderer==null)continue;
            var d=record["data"];int count=d["possibleSprites"].A.Count;if(count==0)continue;
            int parentSeed=(d["SeedNotBasedOnSiblingIndex"].B||node.parent.childCount==1)?node.parent.GetSiblingIndex():node.GetSiblingIndex();
            UnityEngine.Random.InitState(node.GetSiblingIndex()+parentSeed);
            if(d["chanesToDeleteOutOf10"].I>0&&UnityEngine.Random.Range(0,11)<=d["chanesToDeleteOutOf10"].I)node.gameObject.SetActive(false);
            int selected=UnityEngine.Random.Range(0,count);renderer.sprite=SpriteRef(record,"possibleSprites["+selected+"]");
            renderer.color=OriginalVisualCatalog.Gradient(d["possibleColors"]).Evaluate(UnityEngine.Random.Range(0f,1f));
            renderer.sortingOrder+=node.GetSiblingIndex();
            renderer.size+=new Vector2(0,UnityEngine.Random.Range((float)d["randomHeight"]["x"].N,(float)d["randomHeight"]["y"].N));
            node.localPosition+=new Vector3(UnityEngine.Random.Range((float)d["randomPosition"]["x"].N,(float)d["randomPosition"]["y"].N),0,0);
            if(d["canBeFlipped"].B&&UnityEngine.Random.Range(0,100)>50)renderer.flipX=true;
        }
        // Forest stages have no side buildings. The source scene keeps editor
        // placeholders until EnvironmentCustomizer runs this exact branch.
        foreach(string key in new[]{"Customize_BigBuildings_Sides_1","Customize_BigBuildings_Sides_1_Snow","Customize_BigBuildings_Sides_2","Customize_BigBuildings_Sides_2_Snow"}){var n=RefNode(environment,key);if(n!=null)n.gameObject.SetActive(false);}
        foreach(string key in new[]{"Customize_BigBuildings_Center","Customize_BigBuildings_Center_Snow"}) {
            var n=RefNode(environment,key);if(n==null)continue;var renderer=n.GetComponent<SpriteRenderer>();if(renderer==null)continue;
            bool snow=key.EndsWith("_Snow",StringComparison.Ordinal);var sprites=stage["bigBuildings"][snow?"centerSnow":"center"].A;
            if(sprites.Count>0)renderer.sprite=SpriteRef(region,"stages[0].bigBuildings."+(snow?"centerSnow":"center")+"[0]");
            renderer.enabled=!snow;
        }
    }
}
