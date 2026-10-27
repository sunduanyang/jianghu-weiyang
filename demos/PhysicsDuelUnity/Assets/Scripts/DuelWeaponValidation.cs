using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    J weaponResults;
    Limb WeaponProbe(Fighter owner,Vector2 position) {
        var go=new GameObject(owner.weapon.name+" · 实际形状探针");go.layer=10;fixtures.Add(go);
        var rb=go.AddComponent<Rigidbody2D>();rb.position=position;rb.mass=owner.sword.rb.mass;rb.gravityScale=0;rb.sleepMode=RigidbodySleepMode2D.NeverSleep;rb.collisionDetectionMode=CollisionDetectionMode2D.Continuous;
        var limb=new Limb {key="weapon-probe",rb=rb,length=owner.sword.length};
        foreach(var source in owner.sword.Frame.GetComponents<Collider2D>()) {
            if(!source.enabled)continue;
            Collider2D copy;
            if(source is BoxCollider2D box){var c=go.AddComponent<BoxCollider2D>();c.size=box.size;c.offset=box.offset;c.edgeRadius=box.edgeRadius;copy=c;}
            else if(source is PolygonCollider2D polygon){var c=go.AddComponent<PolygonCollider2D>();c.points=polygon.points;c.offset=polygon.offset;copy=c;}
            else continue;
            copy.sharedMaterial=bladeMaterial;if(source==owner.sword.shape)limb.shape=copy;
        }
        var contact=go.AddComponent<DuelContact>();contact.owner=owner;contact.kind="sword";contact.limb=limb;limb.SampleVelocity();return limb;
    }
    IEnumerator CheckWeapons() {
        var cases=new List<J>();float leastDamage=999,mostDamage=0,leastInertia=999,mostInertia=0;
        for(int i=0;i<DuelWeapon.All.Length;i++) {
            playerWeaponIndex=enemyWeaponIndex=i;ResetRound();player.testOverride=enemy.testOverride=true;
            // Inspect each authored rig without the opposing long weapon
            // loading its grip. Real weapon/body contacts follow below, and
            // normal inter-fighter collisions remain in the sparring suite.
            foreach(var a in player.colliders)foreach(var b in enemy.colliders)Physics2D.IgnoreCollision(a,b);
            controls.Begin(601,new Vector2(1235,655));controls.Move(601,PadPoint(controls.right,Vector2.up));
            yield return new WaitForSeconds(1.6f);
            float gap=Mathf.Max(player.MaxJointGap(),enemy.MaxJointGap()),limit=Mathf.Max(player.MaxLimitError(),enemy.MaxLimitError());
            float inertia=player.sword.rb.inertia;leastInertia=Mathf.Min(leastInertia,inertia);mostInertia=Mathf.Max(mostInertia,inertia);
            bool shapes=player.sword.shape!=null&&player.sword.rb!=player.limbs["handNear"].rb&&Mathf.Abs(player.sword.rb.mass-player.weapon.mass)<.001f&&player.primaryGrip.enabled&&player.PrimaryGripError<.04f&&player.sword.Frame.GetComponents<Collider2D>().Count(c=>c.enabled)>=2;
            bones=true;ScreenCapture.CaptureScreenshot(Path.Combine(smokePath,"weapon-"+player.weapon.id+"-colliders.png"));yield return new WaitForSeconds(.08f);bones=false;
            controls.ReleaseAll();player.InterruptAttack();yield return new WaitForSeconds(.45f/player.weapon.handling);
            player.stamina=100;bool manual=player.TryManualAttack();float manualCost=100-player.stamina;
            player.InterruptAttack();yield return new WaitForSeconds(.45f/player.weapon.handling);
            player.stamina=100;int oldId=player.attackId;player.Swing(1);float buttonCost=100-player.stamina;
            bool costs=manual&&player.attackId==oldId+1&&Mathf.Abs(manualCost-player.weapon.cost)<.001f&&manualCost==buttonCost;
            fixtureMode=true;player.root.SetActive(false);enemy.root.SetActive(false);yield return PrepareProbeAttack();enemy.health=120;
            DuelWeapon weapon=player.weapon;float headLocal=weapon.Local(new Vector2(0,(weapon.hitStart+weapon.reach)/2),1).y;
            Vector2 start=new Vector2(-1.2f,7-headLocal);
            Probe("刃部身体探针","body",enemy,new Vector2(0,7),new Vector2(.18f,.18f),false);
            Limb blade=WeaponProbe(player,start);LaunchProbe(blade,start,8);yield return new WaitForSeconds(.25f);
            float damage=120-enemy.health;bool damaging=damage>0;leastDamage=Mathf.Min(leastDamage,damage);mostDamage=Mathf.Max(mostDamage,damage);ClearFixtures();
            yield return PrepareProbeAttack();enemy.health=120;
            start=new Vector2(-1.2f,7-weapon.Local(Vector2.zero,1).y);
            Probe("握柄身体探针","body",enemy,new Vector2(0,7),new Vector2(.18f,.06f),false);
            blade=WeaponProbe(player,start);int previousHandles=rejectedHandleContacts;LaunchProbe(blade,start,8);yield return new WaitForSeconds(.25f);
            bool harmless=enemy.health==120&&rejectedHandleContacts>previousHandles;ClearFixtures();fixtureMode=false;
            bool ok=shapes&&costs&&damaging&&harmless&&gap<.10f&&limit<6;
            cases.Add(J.O("id",weapon.id,"name",weapon.name,"passed",ok,"independent_weapon_and_grip",shapes,"manual_and_button_cost_match",costs,"attack_cost",manualCost,"head_damages",damaging,"handle_no_damage",harmless,"physical_probe_damage",damage,"rigidbody_inertia",inertia,"maximum_joint_gap",gap,"maximum_limit_error",limit));
            File.WriteAllText(Path.Combine(smokePath,"weapons-progress.json"),new J(cases).ToString());
        }
        playerWeaponIndex=enemyWeaponIndex=DuelWeapon.SwordIndex;ResetRound();yield return new WaitForSeconds(.5f);
        float health=player.health;OpenArmory();pendingPlayerWeapon=5;pendingEnemyWeapon=4;CloseArmory();
        bool cancel=playerWeaponIndex==DuelWeapon.SwordIndex&&enemyWeaponIndex==DuelWeapon.SwordIndex&&player.health==health;
        bool buttonExcluded=!controls.Begin(-1,new Vector2(120,190));
        OpenArmory();pendingPlayerWeapon=5;pendingEnemyWeapon=4;
        ScreenCapture.CaptureScreenshot(Path.Combine(smokePath,"11-armory.png"));yield return new WaitForSecondsRealtime(.15f);
        ApplyArmory();bool equipped=player.weapon==DuelWeapon.All[5]&&enemy.weapon==DuelWeapon.All[4]&&player.health==120&&enemy.health==120&&!armory&&!controls.right.captured;
        yield return new WaitForSeconds(1.3f);ScreenCapture.CaptureScreenshot(Path.Combine(smokePath,"12-spear-vs-mace.png"));yield return new WaitForSeconds(.08f);
        bool distinct=mostDamage>leastDamage*1.3f&&mostInertia>leastInertia*1.5f;
        weaponResults=J.O("passed",cases.All(c=>c["passed"].B)&&cancel&&equipped&&buttonExcluded&&distinct,"weapon_count",cases.Count,"cases",new J(cases),"armory_cancel_preserves_round",cancel,"armory_applies_both_and_resets_health",equipped,"armory_button_excluded_from_combat_input",buttonExcluded,"different_damage_and_inertia",distinct);
        File.WriteAllText(Path.Combine(smokePath,"weapons.json"),weaponResults.ToString());
        playerWeaponIndex=enemyWeaponIndex=DuelWeapon.SwordIndex;ResetRound();yield return new WaitForSeconds(1.5f);
    }
}
