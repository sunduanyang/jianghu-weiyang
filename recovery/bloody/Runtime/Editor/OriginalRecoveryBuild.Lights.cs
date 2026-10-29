using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jianghu;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static partial class OriginalRecoveryBuild {
    // Only the fourteen audited prefab lights. The five scene lights remain
    // owned by RestoreLights, and no original prefab preparation is rerun.
    static readonly Dictionary<string,long[]> OriginalPrefabLightIds=new Dictionary<string,long[]> {
        {"1_GraphicsContainer_HRE_Forest",new long[]{227099,235458}},
        {"2_GraphicsContainer_HRE_Monastery",new long[]{260322,268739}},
        {"3_GraphicsContainer_HRE_Village",new long[]{257284,260239}},
        {"4_GraphicsContainer_HRE_Town",new long[]{234309,257432}},
        {"5_GraphicsContainer_HRE_Castle",new long[]{268719,268738}},
        {"6_GraphicsContainer_HRE_ThroneRoom",new long[]{273719,276061}},
        {"RI_HandItem_2HandedCannon",new long[]{229379,260193}}
    };
    public static void RestoreOriginalPrefabLights() {
        const string catalogPath="Assets/Recovery/Resources/OriginalVisualCatalog.asset";
        var savedCatalog=AssetDatabase.LoadAssetAtPath<OriginalVisualCatalog>(catalogPath);
        if(savedCatalog==null||savedCatalog.sourceJson==null)
            throw new InvalidOperationException("Bind the original visual catalog before restoring prefab lights: "+catalogPath);
        // A fresh data index also supports repeated Editor calls after a JSON
        // reimport, without changing the shared catalog's cached runtime data.
        var catalog=ScriptableObject.CreateInstance<OriginalVisualCatalog>();
        catalog.sourceJson=savedCatalog.sourceJson;catalog.entries=savedCatalog.entries;
        var prefabs=new List<J>();int total=0,added=0,reused=0;
        try {
            foreach(var group in OriginalPrefabLightIds) {
                string rootName=group.Key;
                string path=rootName=="RI_HandItem_2HandedCannon"
                    ?"Assets/Resources/sourceprefabs/handitem/RI_HandItem_2HandedCannon.prefab"
                    :"Assets/GameObject/"+rootName+".prefab";
                if(AssetDatabase.LoadAssetAtPath<GameObject>(path)==null)
                    throw new InvalidOperationException("Original light prefab is missing: "+path);
                var records=group.Value.Select(id=>catalog.Data["scene"].A.Single(r=>r["script"].S=="Light2D"&&OriginalEnvironmentSource.Identity(r)=="sharedassets0.assets:"+id)).ToArray();
                foreach(var record in records)
                    if(catalog.Data["hierarchy"][OriginalEnvironmentSource.Identity(record)]["root"].S!=rootName)
                        throw new InvalidOperationException("Original light identity does not belong to expected prefab: "+OriginalEnvironmentSource.Identity(record));
                var root=PrefabUtility.LoadPrefabContents(path);
                var lights=new List<J>();
                try {
                    if(root.name!=rootName)throw new InvalidOperationException("Original light prefab root was renamed: "+path+" / "+root.name);
                    foreach(var record in records) {
                        string identity=OriginalEnvironmentSource.Identity(record);
                        var node=OriginalEnvironmentSource.IdentityNode(catalog,identity,root.transform,rootName);
                        var present=node.GetComponents<Light2D>();
                        if(present.Length>1)throw new InvalidOperationException("Duplicate official Light2D components at original light: "+identity);
                        bool isNew=present.Length==0;
                        var light=isNew?node.gameObject.AddComponent<Light2D>():present[0];
                        CopyProperties(light,record["data"]);
                        var serialized=new SerializedObject(light);
                        var copiedReferences=new List<J>();
                        foreach(string field in new[]{"m_LightCookieSprite","m_DeprecatedPointLightCookieSprite"}) {
                            var property=serialized.FindProperty(field);if(property==null)continue;
                            string spriteIdentity=record["assets"][field].S;
                            var sprite=string.IsNullOrEmpty(spriteIdentity)?null:catalog.Get<Sprite>(spriteIdentity);
                            if(!string.IsNullOrEmpty(spriteIdentity)&&sprite==null)
                                throw new InvalidOperationException("Original light cookie is not bound: "+identity+" / "+spriteIdentity);
                            property.objectReferenceValue=sprite;
                            copiedReferences.Add(J.O("field",field,"identity",spriteIdentity));
                        }
                        // CopyProperties handles primitive/vector/array fields;
                        // Bounds has a distinct SerializedPropertyType.
                        var bounds=serialized.FindProperty("m_LocalBounds");
                        if(bounds!=null) {
                            J sourceBounds=record["data"]["m_LocalBounds"];
                            var value=new Bounds();value.center=OriginalLightVector(sourceBounds["m_Center"]);value.extents=OriginalLightVector(sourceBounds["m_Extent"]);
                            bounds.boundsValue=value;
                        }
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                        light.enabled=record["data"]["m_Enabled"].B;EditorUtility.SetDirty(light);
                        var fields=record["data"].D.Keys.Where(k=>k!="m_GameObject"&&k!="m_Script"&&k!="m_Name").ToArray();
                        var missing=fields.Where(k=>serialized.FindProperty(k)==null).Select(k=>new J(k)).ToList();
                        lights.Add(J.O("identity",identity,"source_path",record["scene_path"].S,"added",isNew,"reused",!isNew,
                            "source_serialized_fields",new J(fields.Select(k=>new J(k)).ToList()),"fields_absent_from_official_component",new J(missing),
                            "bound_cookie_references",new J(copiedReferences),"source_bounds_copied",bounds!=null));
                        if(isNew)added++;else reused++;total++;
                    }
                    if(PrefabUtility.SaveAsPrefabAsset(root,path)==null)
                        throw new InvalidOperationException("Original light prefab could not be saved: "+path);
                }
                finally {PrefabUtility.UnloadPrefabContents(root);}
                prefabs.Add(J.O("root",rootName,"path",path,"lights",new J(lights)));
            }
            if(total!=14||prefabs.Count!=7)throw new InvalidOperationException("Original prefab light scope changed");
            string reportPath=Path.GetFullPath("../../output/bloody-full-port-20261028/environment-check/prefab-light-restoration.json");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            File.WriteAllText(reportPath,J.O("prefab_count",prefabs.Count,"light_count",total,"added",added,"reused",reused,
                "scope","Only audited HRE stage and cannon prefab lights; scene lights are not touched", "prefabs",new J(prefabs)).ToString());
            Debug.Log("ORIGINAL_PREFAB_LIGHTS_RESTORED prefabs=7 lights=14 added="+added+" reused="+reused+" report="+reportPath);
        }
        finally {UnityEngine.Object.DestroyImmediate(catalog);}
    }
    static Vector3 OriginalLightVector(J value)=>new Vector3((float)value["x"].N,(float)value["y"].N,(float)value["z"].N);
}
