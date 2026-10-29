using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jianghu;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

public static partial class OriginalRecoveryBuild {
    public static void RestoreOriginalThroneLightActivators() {
        var catalog=AssetDatabase.LoadAssetAtPath<OriginalVisualCatalog>("Assets/Recovery/Resources/OriginalVisualCatalog.asset");
        if(catalog==null)throw new InvalidOperationException("Bind the original catalog before throne light activators");
        var roots=catalog.Data["scene"].A.Where(r=>r["script"].S=="RegionConfiguration").Select(r=>catalog.Data["assets"][r["assets"]["throneRoomGraphics"].S]["scene_path"].S).ToArray();
        var records=catalog.Data["scene"].A.Where(r=>r["script"].S=="LightActivator"&&roots.Contains(catalog.Data["hierarchy"][OriginalEnvironmentSource.Identity(r)]["root"].S)).ToArray();
        if(roots.Length!=4||records.Length!=68)throw new InvalidOperationException("Original throne light-activator scope changed");
        foreach(string sourceRoot in roots) {
            string path="Assets/GameObject/"+sourceRoot+".prefab";var root=PrefabUtility.LoadPrefabContents(path);
            try {
                foreach(var row in records.Where(r=>catalog.Data["hierarchy"][OriginalEnvironmentSource.Identity(r)]["root"].S==sourceRoot)) {
                    string identity=OriginalEnvironmentSource.Identity(row);var node=OriginalEnvironmentSource.IdentityNode(catalog,identity,root.transform,sourceRoot);
                    var component=node.GetComponents<OriginalStageLightEnable>().SingleOrDefault(c=>c.SourceIdentity==identity)??node.gameObject.AddComponent<OriginalStageLightEnable>();
                    component.SourceIdentity=identity;component.enabled=row["data"]["m_Enabled"].B;EditorUtility.SetDirty(component);
                }
                if(PrefabUtility.SaveAsPrefabAsset(root,path)==null)throw new InvalidOperationException("Original throne light prefab save failed");
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
        }
        string report=Path.GetFullPath("../../output/bloody-full-port-20261028/environment-check/throne-light-activator-installation.json");Directory.CreateDirectory(Path.GetDirectoryName(report));
        File.WriteAllText(report,J.O("prefabs",4,"light_activators",68,"ordinary_stage_prefabs_modified",0).ToString());
    }
    // Only the two now-verified script types. Crowd, houses and lantern
    // randomization in other stage scopes are not installed by this pass.
    public static void RestoreOriginalSpecialStageCallbacks() {
        var data=OriginalEnvironmentSpecialSource.Data;
        var records=data["records"].A.Where(r=>r["script"].S=="ActivateGameObjectChristmasMode"||r["script"].S=="ReverbSetter").ToArray();
        if(records.Count(r=>r["script"].S=="ActivateGameObjectChristmasMode")!=99||records.Count(r=>r["script"].S=="ReverbSetter")!=24)
            throw new InvalidOperationException("Original seasonal/reverb callback scope changed");
        var groups=records.GroupBy(r=>r["hierarchy"]["root"].S).ToArray();if(groups.Length!=24)throw new InvalidOperationException("Original special stage root count changed");
        foreach(var group in groups) {
            string path="Assets/GameObject/"+group.Key+".prefab";var root=PrefabUtility.LoadPrefabContents(path);
            try {
                foreach(var row in group.OrderBy(r=>r["component_order"].I)) {
                    string identity=row["identity"].S;var node=OriginalEnvironmentSpecialSource.Resolve(row["hierarchy"],root.transform,group.Key,identity);
                    if(row["script"].S=="ActivateGameObjectChristmasMode") {
                        var component=node.GetComponents<OriginalStageChristmasActivation>().SingleOrDefault(c=>c.SourceIdentity==identity)??node.gameObject.AddComponent<OriginalStageChristmasActivation>();
                        component.SourceIdentity=identity;component.SourceRootName=group.Key;component.enabled=row["data"]["m_Enabled"].B;EditorUtility.SetDirty(component);
                    } else {
                        var component=node.GetComponents<OriginalStageReverb>().SingleOrDefault(c=>c.SourceIdentity==identity)??node.gameObject.AddComponent<OriginalStageReverb>();
                        var binding=data["references"][row["assets"]["SFXMixerGroup"].S]["binding"];
                        string mixerPath=binding["asset_path"].S;
                        string block=Regex.Match(File.ReadAllText(mixerPath),@"(?ms)^--- !u!243 &"+binding["local_file_id"].S+@"\r?\n.*?(?=^---|\z)").Value;
                        if(!block.Contains("m_GroupID: "+binding["source_group_guid"].S))throw new InvalidOperationException("Original SFX mixer group GUID/local-file-ID binding changed");
                        var mixer=AssetDatabase.LoadAllAssetsAtPath(mixerPath).OfType<AudioMixerGroup>().Single(g=> {
                            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(g,out string guid,out long id)&&id==(long)binding["local_file_id"].N;
                        });
                        component.SourceIdentity=identity;component.SFXMixerGroup=mixer;component.ReverbValue=(float)row["data"]["ReverbValue"].N;component.enabled=row["data"]["m_Enabled"].B;EditorUtility.SetDirty(component);
                    }
                }
                if(PrefabUtility.SaveAsPrefabAsset(root,path)==null)throw new InvalidOperationException("Original special callback prefab save failed: "+path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
        }
        string report=Path.GetFullPath("../../output/bloody-full-port-20261028/environment-check/special-callback-installation.json");Directory.CreateDirectory(Path.GetDirectoryName(report));
        File.WriteAllText(report,J.O("prefabs",24,"christmas_start_components",99,"reverb_awake_components",24,"calendar_override",false,"source_guid_mixer_binding",true).ToString());
    }
}
