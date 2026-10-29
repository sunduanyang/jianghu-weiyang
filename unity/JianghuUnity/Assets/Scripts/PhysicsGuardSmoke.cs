using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Jianghu {
public sealed partial class PhysicsBattleSmoke {
    IEnumerator AuditGuard(){
        var scene=SceneManager.CreateScene("Formal guard penetration acceptance",new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        var root=new GameObject("Formal guard penetration acceptance");root.SetActive(false);SceneManager.MoveGameObjectToScene(root,scene);
        var arena=root.AddComponent<PhysicsDuel>();var sword=DuelWeapon.All[2];
        var wood=DuelWeapon.CampaignWeapon(J.O("id","sword","template_id","wood_sword","name","枣木剑","mass",.45,"cost",7,"handling",1.08,"damage",.8,"pressure",.8,"distance",1.95));
        J Actor(DuelWeapon w,bool shield){var row=ReferenceFighter(w,shield);if(w.sourceTemplate.Length>0)row["weapon"]["template_id"]=new J(w.sourceTemplate);return row;}
        arena.campaign=new PhysicsDuel.CampaignOptions{playerSkin="m-youth-farmer",enemySkin="f-youth-farmer",battle=J.O("seed",42,"fighters",new J(new List<J>{Actor(wood,false),Actor(sword,false)}))};
        app.enabled=false;root.SetActive(true);var rows=new List<J>();
        foreach(var w in new[]{wood,sword})foreach(bool shield in new[]{false,true})foreach(float arc in new[]{6f,12f}){
            arena.campaign.battle["fighters"]=new J(new List<J>{Actor(w,false),Actor(sword,shield)});
            var row=arena.ProbeReferenceGuard(arc);rows.Add(row);File.WriteAllText(Path.Combine(output,"guard-cases.json"),new J(rows).ToString());
            // A shield remains a valid defense: an arbitrary scripted sweep
            // is not required to bypass it. Weapon-only guards must still
            // allow real PRIMARY BLADE hits, not offhand or hilt damage.
            // Fast flicks exercise the regression. Slower defended sweeps
            // remain observations: source damage is explicitly speed-gated.
            if(!shield&&arc>=12)Check("fast manual primary blade can hit beyond a weapon guard "+w.name+" / "+arc,row["primary_blade_hits"].I>0&&row["primary_blade_hits_after_guard"].I>0,row);
            yield return null;
        }
        Check("wood fixture retains the actual wood template and twenty mps threshold",rows.Where(r=>r["template"].S=="wood_sword").Count()==4&&rows.Where(r=>r["template"].S=="wood_sword").All(r=>r["body_threshold"].N==20));
        Check("actual blade and shield guards both encountered",rows.Any(r=>!r["enemy_shield"].B&&r["weapon_contacts"].I>0)&&rows.Any(r=>r["enemy_shield"].B&&r["shield_contacts"].I>0));
        Check("human primary blade can hit after an actual guard contact",rows.Any(r=>r["primary_blade_hits_after_guard"].I>0));
        Check("live guard collisions keep both physical grips attached",rows.All(r=>r["max_grip_gap"].N<.03));
        if(GameClient.Arg("-referenceContactBaseline")!="1")Check("shield guard contacts pay source attacker and defender stamina",rows.Any(r=>r["shield_contacts"].I>0&&r["attacker_clash_stamina"].N>0&&r["defender_clash_stamina"].N>0));
        yield return SceneManager.UnloadSceneAsync(scene);app.enabled=true;Time.timeScale=1;
    }
}
}
