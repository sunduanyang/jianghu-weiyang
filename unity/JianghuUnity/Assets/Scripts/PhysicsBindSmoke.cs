using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Jianghu {
public sealed partial class PhysicsBattleSmoke {
    IEnumerator AuditBinds(){
        var scene=SceneManager.CreateScene("Contact release acceptance",new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        var root=new GameObject("Contact release acceptance");root.SetActive(false);SceneManager.MoveGameObjectToScene(root,scene);
        var arena=root.AddComponent<PhysicsDuel>();var sword=DuelWeapon.All[2];
        arena.campaign=new PhysicsDuel.CampaignOptions{playerSkin="m-youth-farmer",enemySkin="f-youth-farmer",battle=J.O("seed",42,"fighters",new J(new List<J>{ReferenceFighter(sword),ReferenceFighter(sword)}))};
        app.enabled=false;root.SetActive(true);
        var cases=new List<J>();
        foreach(var w in new[]{sword,DuelWeapon.All[5],DuelWeapon.All[6],DuelWeapon.All[7]})foreach(bool body in new[]{false,true})foreach(float arc in new[]{6f,12f}){
            arena.campaign.battle["fighters"]=new J(new List<J>{ReferenceFighter(w),ReferenceFighter(w)});
            J result=arena.ProbeReferenceBind(body,arc);cases.Add(result);
            if(BindExercised(result))Check("weapon contact releases "+w.id+" "+(body?"body":"weapon")+" "+arc,BindReleased(result),result);
            yield return null;
        }
        foreach(var w in new[]{sword,DuelWeapon.All[6]})foreach(bool body in new[]{false,true}){
            arena.campaign.battle["fighters"]=new J(new List<J>{ReferenceFighter(w),ReferenceFighter(w)});
            J result=arena.ProbeReferenceBind(body,12,false);cases.Add(result);
            if(BindExercised(result))Check("hand-only withdrawal releases "+w.id+" "+(body?"body":"weapon"),BindReleased(result),result);
            yield return null;
        }
        Check("contact probes exercise all weapon target categories",cases.Where(BindExercised).Select(c=>c["weapon"].S+":"+c["target"].S).Distinct().Count()==8&&cases.Count(BindExercised)>=16,J.O("attempted",cases.Count,"exercised",cases.Count(BindExercised)));
        File.WriteAllText(Path.Combine(output,"contact-release-cases.json"),new J(cases).ToString());
        yield return SceneManager.UnloadSceneAsync(scene);app.enabled=true;Time.timeScale=1;
    }
    static bool BindExercised(J result)=>result["contact_frames"].I>0||result["actual_contact_events"].I>0;
    static bool BindReleased(J r)=>r["maximum_joint_gap"].N<.2&&((r["straight_release_seconds"].N>=0&&r["straight_release_seconds"].N<.65)||(r["straight_release_seconds"].N<0&&r["twist_release_seconds"].N>=0&&r["twist_release_seconds"].N<.65));
}
}
