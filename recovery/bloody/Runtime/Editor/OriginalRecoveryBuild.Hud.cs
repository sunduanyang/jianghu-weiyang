using System;
using System.IO;
using System.Linq;
using Jianghu;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

public static partial class OriginalRecoveryBuild {
    static partial void AttachOriginalHud(Scene sourceScene,Scene targetScene,OriginalPreview preview) {
        string source=Path.GetFullPath("../../output/bloody-full-port-20261028/original-ui-source.json");
        string target="Assets/Recovery/Resources/OriginalUISource.json";
        var sourceData=J.Parse(File.ReadAllText(source));File.WriteAllText(target,J.O("records",sourceData["records"]).ToString());AssetDatabase.ImportAsset(target,ImportAssetOptions.ForceSynchronousImport);
        var root=sourceScene.GetRootGameObjects().First(g=>g.name=="CanvasAndCamera");
        var sourceCanvas=root.transform.Find("Canvas");
        var uiRoot=new GameObject("CanvasAndCamera");SceneManager.MoveGameObjectToScene(uiRoot,targetScene);
        var canvas=UnityEngine.Object.Instantiate(sourceCanvas,uiRoot.transform);canvas.name="Canvas";
        canvas.gameObject.SetActive(true);
        foreach(var c in canvas.GetComponentsInChildren<Canvas>(true)) {c.renderMode=RenderMode.ScreenSpaceOverlay;c.worldCamera=null;}
        foreach(var a in canvas.GetComponentsInChildren<Animator>(true))a.enabled=false;
        foreach(var a in canvas.GetComponentsInChildren<Animation>(true))a.enabled=false;
        var hud=uiRoot.AddComponent<OriginalBattleHud>();hud.preview=preview;preview.hud=hud;
        var events=new GameObject("Original UI events");SceneManager.MoveGameObjectToScene(events,targetScene);
        events.AddComponent<EventSystem>();events.AddComponent<StandaloneInputModule>();
    }
}
