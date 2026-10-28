using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Jianghu {
public partial class JianghuApp {
    public void WeaponDetailsForSmoke(string mode,string item){
        if(mode=="货栈")OpenMarket(mode);else{ViewPerson(-1,PersonTab.Bag);inventoryMode=mode;}
        selectedItem=item;itemCategory="武器";itemQuery="";
    }
}
public sealed partial class PhysicsBattleSmoke {
    IEnumerator AuditWeapons(){
        J pack=new J(),shop=new J();
        yield return Call("/api/inventory",null,j=>app.Inventory=pack=j);
        yield return Call("/api/market",null,j=>app.Market=shop=j);
        var keys=new[]{"mass","handling","cost","damage","pressure","durability"};
        Check("inventory has no carrying mass or capacity",!pack["weight"].Has&&!pack["capacity"].Has);
        Check("exactly six weapon attributes",pack["weapon_attribute_catalog"].A.Select(a=>a["id"].S).SequenceEqual(keys));
        Check("large inventory fixture exceeds former limit",pack["items"].A.Count>100);
        Check("inventory and market have no item attack or weight",pack["items"].A.Concat(shop["items"].A).All(i=>!i["attack"].Has&&!i["weight"].Has));
        Check("all market weapons have six attributes",shop["items"].A.Where(i=>i["category"].S=="weapon").All(i=>i["weapon_attributes"].D.Count==6));
        var item=shop["items"].A.First(i=>i["template"].S=="iron_sword");
        yield return Call("/api/items",J.O("operation","buy","item_id",item["id"]));
        yield return Call("/api/items",J.O("operation","equip","item_id",item["id"]));
        yield return Call("/api/inventory",null,j=>app.Inventory=pack=j);
        Check("large inventory can buy and equip",pack["equipment"]["weapon"].S==item["id"].S);
        app.WeaponDetailsForSmoke("行囊",item["id"].S);yield return Picture("01-weapon-inventory");
        var premium=shop["items"].A.First(i=>i["template"].S=="tianheng_sword");
        app.WeaponDetailsForSmoke("货栈",premium["id"].S);yield return Picture("02-weapon-market");

        var fixture=J.Parse(File.ReadAllText(GameClient.Arg("-weaponFixture")));
        var scene=SceneManager.CreateScene("Weapon attributes acceptance",new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        var root=new GameObject("Weapon attributes acceptance");root.SetActive(false);SceneManager.MoveGameObjectToScene(root,scene);
        var arena=root.AddComponent<PhysicsDuel>();
        var neutral=fixture["cases"][0]["fighter"];
        arena.campaign=new PhysicsDuel.CampaignOptions{playerSkin="m-youth-farmer",enemySkin="f-youth-farmer",battle=J.O("fighters",new J(new List<J>{neutral,neutral}))};
        app.enabled=false;root.SetActive(true);
        foreach(var entry in fixture["cases"].A){
            var fighter=entry["fighter"];var data=fighter["weapon"];
            arena.campaign.battle=J.O("fighters",new J(new List<J>{fighter,neutral}));arena.ResetRound();
            arena.player.testOverride=arena.enemy.testOverride=true;arena.RunCampaign(true);
            var player=arena.player;string name=entry["template"].S;
            Check(name+" item mass reaches real rigidbody",Math.Abs(player.sword.rb.mass-data["mass"].N)<.001);
            Check(name+" handling and effort reach fighter",Math.Abs(player.ArmSpeed-data["handling"].N*fighter["handling_scale"].N)<.001&&Math.Abs(player.AttackCost-data["cost"].N*fighter["combat_parameters"]["effort_scale"].N)<.001);
            Check(name+" killing and shield pressure reach fighter",Math.Abs(player.weapon.damage-data["damage"].N)<.001&&Math.Abs(player.weapon.pressure-data["pressure"].N)<.001);
            Check(name+" has no second equipment damage bonus",Math.Abs(player.damageScale-fixture["character_damage_scale"].N)<.001);
            float baseline=DuelImpact.Damage(player.weapon.mass,5,1,player.damageScale);
            float damage=DuelImpact.Damage(player.weapon.mass,5,player.weapon.damage,player.damageScale);
            Check(name+" killing coefficient applied once",Math.Abs(damage/baseline-player.weapon.damage)<.001);
        }
        arena.PauseCampaign();yield return SceneManager.UnloadSceneAsync(scene);app.enabled=true;Time.timeScale=1;
        yield return Call("/api/intervene",J.O("action","challenge","npc_id",1),j=>app.Martial=j);
        app.ShowBattleForSmoke();yield return WaitArena();if(app.PhysicsArena==null)yield break;
        arena=app.PhysicsArena;var equipped=pack["items"].A.First(i=>i["id"].S==item["id"].S)["weapon_attributes"];
        Check("actual equipped weapon reaches campaign",Math.Abs(arena.player.sword.rb.mass-equipped["mass"].N)<.001&&Math.Abs(arena.player.weapon.damage-equipped["damage"].N)<.001);
        arena.enemy.testOverride=true;arena.RunCampaign(true);arena.CampaignTestPad(true,Vector2.up);
        yield return new WaitForSecondsRealtime(1);arena.CampaignTestPad(true,Vector2.right);
        yield return new WaitForSecondsRealtime(1);arena.PauseCampaign();arena.CampaignReleasePads();
        Check("weapon movement spends real stamina",arena.player.motionStaminaSpent>0);
        yield return Picture("03-equipped-combat");
        yield return app.SavePhysicsForSmoke("checkpoint");
        Check("physical pose and weapon version saved",app.Martial["battle"]["frame"].Has&&app.Martial["battle"]["fighters"][0]["weapon_version"].I==1);
        yield return app.SavePhysicsForSmoke("surrender");
        yield return Call("/api/inventory",null,j=>pack=j);
        Check("battle wear updates the same item durability",pack["items"].A.First(i=>i["id"].S==item["id"].S)["weapon_attributes"]["durability"].N<100);
    }
}
}
