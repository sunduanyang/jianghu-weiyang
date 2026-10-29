using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jianghu;
using UnityEditor;
using UnityEngine;

public static partial class OriginalRecoveryBuild {
    // Install only the first-bout prefab callbacks. Source hierarchy and
    // sibling indices are preserved; ordinary non-BK prefabs are not changed.
    public static void RestoreOriginalFirstStageActors() {
        const string path="Assets/GameObject/2_GraphicsContainer_BK_Monastery.prefab";
        var catalog=AssetDatabase.LoadAssetAtPath<OriginalVisualCatalog>("Assets/Recovery/Resources/OriginalVisualCatalog.asset");
        if(catalog==null)throw new InvalidOperationException("Bind the original visual catalog before first-stage actors");
        var records=catalog.Data["scene"].A.Where(row=> {
            string script=row["script"].S;
            return (script=="BystanderCustomization"||script=="LightActivator"||script=="CustomizeGroundSpriteShapes")&&
                catalog.Data["hierarchy"][OriginalEnvironmentSource.Identity(row)]["root"].S==OriginalStageActors.FirstStageRoot;
        }).ToArray();
        if(records.Count(row=>row["script"].S=="BystanderCustomization")!=7||records.Count(row=>row["script"].S=="LightActivator")!=7||records.Count(row=>row["script"].S=="CustomizeGroundSpriteShapes")!=1)
            throw new InvalidOperationException("Original first-stage actor scope changed");
        var root=PrefabUtility.LoadPrefabContents(path);var installed=new List<J>();
        try {
            foreach(var row in records) {
                string identity=OriginalEnvironmentSource.Identity(row);var node=OriginalEnvironmentSource.IdentityNode(catalog,identity,root.transform,OriginalStageActors.FirstStageRoot);
                UnityEngine.Object component;
                switch(row["script"].S) {
                    case "BystanderCustomization":
                        var crowd=node.GetComponent<OriginalBystanderCustomization>()??node.gameObject.AddComponent<OriginalBystanderCustomization>();
                        crowd.SourceIdentity=identity;crowd.SourceRootName=OriginalStageActors.FirstStageRoot;crowd.enabled=row["data"]["m_Enabled"].B;component=crowd;break;
                    case "LightActivator":
                        var fire=node.GetComponent<OriginalStageLightEnable>()??node.gameObject.AddComponent<OriginalStageLightEnable>();
                        fire.SourceIdentity=identity;fire.enabled=row["data"]["m_Enabled"].B;component=fire;break;
                    default:
                        var floor=node.GetComponent<OriginalStageGroundEnable>()??node.gameObject.AddComponent<OriginalStageGroundEnable>();
                        floor.SourceRootName=OriginalStageActors.FirstStageRoot;floor.enabled=row["data"]["m_Enabled"].B;component=floor;break;
                }
                EditorUtility.SetDirty(component);
                installed.Add(J.O("source",identity,"script",row["script"],"hierarchy",catalog.Data["hierarchy"][identity]));
            }
            if(PrefabUtility.SaveAsPrefabAsset(root,path)==null)throw new InvalidOperationException("Original first-stage actor prefab could not be saved");
        }
        finally {PrefabUtility.UnloadPrefabContents(root);}
        string report=Path.GetFullPath("../../output/bloody-full-port-20261028/environment-check/first-stage-actor-installation.json");Directory.CreateDirectory(Path.GetDirectoryName(report));
        File.WriteAllText(report,J.O("prefab",path,"bystanders",7,"light_activators",7,"ground_enable_proxies",1,"installed",new J(installed),
            "lifecycle","Unity prefab Awake/OnEnable, then source Start and .025s realtime coroutine; no object reorder or fixed crowd seed").ToString());
        Debug.Log("ORIGINAL_FIRST_STAGE_ACTORS_INSTALLED bystanders=7 fire_activators=7 ground=1 report="+report);
    }
}
