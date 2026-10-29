using System;
using System.IO;
using System.Linq;
using Jianghu;
using UnityEditor;
using UnityEngine;

public static partial class OriginalRecoveryBuild {
    // Independent second group. Does not expand the stable 7/7/1 actor pass.
    public static void RestoreOriginalFirstStageMovement() {
        const string path="Assets/GameObject/2_GraphicsContainer_BK_Monastery.prefab";
        var catalog=AssetDatabase.LoadAssetAtPath<OriginalVisualCatalog>("Assets/Recovery/Resources/OriginalVisualCatalog.asset");
        if(catalog==null)throw new InvalidOperationException("Bind the original visual catalog before stage movement");
        var records=catalog.Data["scene"].A.Where(r=>(r["script"].S=="BystanderMovement"||r["script"].S=="BystandersController")&&
            catalog.Data["hierarchy"][OriginalEnvironmentSource.Identity(r)]["root"].S==OriginalStageActors.FirstStageRoot).ToArray();
        if(records.Count(r=>r["script"].S=="BystanderMovement")!=7||records.Count(r=>r["script"].S=="BystandersController")!=1)
            throw new InvalidOperationException("Original first-stage movement scope changed");
        var root=PrefabUtility.LoadPrefabContents(path);
        try {
            foreach(var row in records) {
                string identity=OriginalEnvironmentSource.Identity(row);
                var node=OriginalEnvironmentSource.IdentityNode(catalog,identity,root.transform,OriginalStageActors.FirstStageRoot);
                if(row["script"].S=="BystanderMovement") {
                    var actor=node.GetComponent<OriginalBystanderMovement>()??node.gameObject.AddComponent<OriginalBystanderMovement>();
                    actor.SourceIdentity=identity;actor.SourceRootName=OriginalStageActors.FirstStageRoot;actor.enabled=row["data"]["m_Enabled"].B;EditorUtility.SetDirty(actor);
                } else {
                    var controller=node.GetComponent<OriginalBystandersController>()??node.gameObject.AddComponent<OriginalBystandersController>();
                    controller.SourceIdentity=identity;controller.SourceRootName=OriginalStageActors.FirstStageRoot;controller.enabled=row["data"]["m_Enabled"].B;EditorUtility.SetDirty(controller);
                }
            }
            if(PrefabUtility.SaveAsPrefabAsset(root,path)==null)throw new InvalidOperationException("Original first-stage movement prefab save failed");
        }
        finally {PrefabUtility.UnloadPrefabContents(root);}
        string report=Path.GetFullPath("../../output/bloody-full-port-20261028/environment-check/first-stage-movement-installation.json");Directory.CreateDirectory(Path.GetDirectoryName(report));
        File.WriteAllText(report,J.O("prefab",path,"movements",7,"controllers",1,"dispatch","WaitForStart before Create; NotifyLocalPlayerSpawned before config; NotifyLocalPlayerDespawned before destroy; ApplyGameState on Networked-state publication").ToString());
    }
}
