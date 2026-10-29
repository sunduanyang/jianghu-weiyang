using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Jianghu {
public sealed partial class PhysicsBattleSmoke {
    IEnumerator AuditCoreQueries(){
        var scene=SceneManager.CreateScene("Core signed distance",new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        var root=new GameObject("Core signed distance");root.SetActive(false);SceneManager.MoveGameObjectToScene(root,scene);
        var arena=root.AddComponent<PhysicsDuel>();arena.campaign=new PhysicsDuel.CampaignOptions{playerSkin="m-youth-farmer",enemySkin="f-youth-farmer",battle=J.O("seed",42,"fighters",new J(new List<J>{ReferenceFighter(DuelWeapon.Fist),ReferenceFighter(DuelWeapon.Fist)}))};
        app.enabled=false;root.SetActive(true);Check("signed query fixture uses Core",arena.UseCorePhysics);
        var a=new GameObject("distance A");a.transform.SetParent(root.transform,false);a.transform.position=new Vector3(0,4);a.AddComponent<Rigidbody2D>().bodyType=RigidbodyType2D.Kinematic;var ca=a.AddComponent<BoxCollider2D>();ca.size=Vector2.one;
        var b=new GameObject("distance B");b.transform.SetParent(root.transform,false);b.transform.position=new Vector3(1.5f,4);b.AddComponent<Rigidbody2D>().bodyType=RigidbodyType2D.Kinematic;var cb=b.AddComponent<BoxCollider2D>();cb.size=Vector2.one;
        long before=arena.CoreWorld.Steps;arena.CoreWorld.Synchronize();float separated=arena.CoreWorld.Distance(ca,cb),legacySeparated=ca.Distance(cb).distance;
        b.transform.position=new Vector3(.5f,4);arena.CoreWorld.Synchronize();float overlap=arena.CoreWorld.Distance(ca,cb),legacyOverlap=ca.Distance(cb).distance;
        var facts=J.O("core_separated",separated,"legacy_separated",legacySeparated,"core_overlap",overlap,"legacy_overlap",legacyOverlap);
        // Core uses the authored box edges; the legacy query includes its
        // implicit contact skin. Test geometry and the known skin separately.
        Check("Core preserves authored positive separation",Mathf.Abs(separated-.5f)<.001f,facts);
        Check("Core preserves authored negative penetration depth",Mathf.Abs(overlap+.5f)<.001f,facts);
        float skin=2*Physics2D.defaultContactOffset;facts["legacy_contact_skin"]=new J(skin);
        Check("legacy query difference is its contact skin",Mathf.Abs(separated-legacySeparated-skin)<.001f&&Mathf.Abs(overlap-legacyOverlap-skin)<.001f,facts);
        Check("queries do not advance combat",arena.CoreWorld.Steps==before);
        File.WriteAllText(Path.Combine(output,"signed-distances.json"),facts.ToString());
        yield return SceneManager.UnloadSceneAsync(scene);app.enabled=true;Time.timeScale=1;
    }
    IEnumerator AuditBackendComparison(){
        var scene=SceneManager.CreateScene("Backend comparison",new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        var root=new GameObject("Backend comparison");root.SetActive(false);SceneManager.MoveGameObjectToScene(root,scene);
        var arena=root.AddComponent<PhysicsDuel>();arena.campaign=new PhysicsDuel.CampaignOptions{playerSkin="m-youth-farmer",enemySkin="f-youth-farmer",battle=J.O("seed",42,"fighters",new J(new List<J>{ReferenceFighter(DuelWeapon.Fist),ReferenceFighter(DuelWeapon.Fist)}))};
        app.enabled=false;root.SetActive(true);var rows=new List<J>();
        int frames=int.TryParse(GameClient.Arg("-comparisonFrames"),out var value)&&value>=120&&value<=10800?value:1200;
        File.WriteAllText(Path.Combine(output,"input-sequence.json"),arena.BackendInputSchedule(frames).ToString());
        foreach(var weapon in new[]{DuelWeapon.Fist}.Concat(DuelWeapon.All)){
            string only=GameClient.Arg("-comparisonWeapon");if(only.Length>0&&weapon.id!=only)continue;
            arena.campaign.battle["fighters"]=new J(new List<J>{ReferenceFighter(weapon,weapon.id=="sword"),ReferenceFighter(weapon,weapon.id=="sword")});
            var row=arena.ProbeBackendSequence(frames);rows.Add(row);File.WriteAllText(Path.Combine(output,"comparison.json"),new J(rows).ToString());
            Check("finite physics and bounded active joints "+weapon.id,row["constraints_passed"].B,row);
            Check("held weapon remains attached "+weapon.id,row["held_grip_passed"].B,row);
            Check("roots stay within arena "+weapon.id,row["max_root_x"].N<6.1,row);
            if(arena.UseCorePhysics)Check("Core is authoritative with zero legacy steps "+weapon.id,row["physics"]["native_steps"].N>0&&row["physics"]["legacy_simulation_steps"].N==0,row["physics"]);
            yield return Picture("comparison-"+weapon.id);
        }
        yield return SceneManager.UnloadSceneAsync(scene);app.enabled=true;Time.timeScale=1;
    }
}
}
