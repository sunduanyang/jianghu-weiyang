from copy import deepcopy
import json
from pathlib import Path
import unittest

from jianghu import physical_combat as combat
from tests import test_physical_combat as helpers


class PhysicalImpactTests(unittest.TestCase):
    def test_threshold_mass_speed_and_no_small_cap(self):
        self.assertEqual(combat.impact_damage(.7,1.49),0)
        self.assertGreater(combat.impact_damage(.7,1.5),0)
        self.assertLess(combat.impact_damage(.28,2,.72),6)
        hammer=combat.impact_damage(2.35,8,1.42)
        glance=combat.impact_damage(.28,3,.72)
        self.assertGreater(hammer,25)
        self.assertGreater(hammer/glance,10)
        for mass in (.12,.28,.7,1.45,2.35):
            self.assertGreater(combat.impact_damage(mass,8),combat.impact_damage(mass,4))
        self.assertGreater(combat.impact_damage(2.35,6),combat.impact_damage(.28,6))

    def test_zones_and_armour_share_one_law(self):
        body=combat.impact_damage(.7,5)
        self.assertAlmostEqual(combat.impact_damage(.7,5,zone='head'),body*1.35)
        self.assertAlmostEqual(combat.impact_damage(.7,5,zone='leg'),body*.8)
        self.assertAlmostEqual(combat.impact_damage(.7,5,reduction=.4),body*.6)

    def test_offscreen_hit_log_replays_the_live_damage_function(self):
        helper=helpers.PhysicalCombatTests();world=helper.world()
        try:
            a,b=world.npcs[1],world.npcs[2]
            battle=combat.start(a,b,42,equipment=(combat.equipment(world,1),combat.equipment(world,2)))
            result=combat.autoplay(deepcopy(battle))
            self.assertTrue(result['log'])
            self.assertEqual(result,combat.autoplay(deepcopy(battle)))
            for hit in result['log']:
                attacker=battle['fighters'][hit['side']];defender=battle['fighters'][1-hit['side']]
                expected=combat.impact_damage(hit['weapon_mass'],hit['speed'],attacker['weapon']['damage'],attacker['damage_scale'],defender['damage_reduction'],hit['zone'])
                self.assertAlmostEqual(expected,hit['raw_damage'])
                self.assertLessEqual(hit['damage'],expected+.0001)
            zones=set()
            for seed in range(12):
                sample=deepcopy(battle);sample['seed']=seed
                zones.update(h['zone'] for h in combat.autoplay(sample)['log'])
            self.assertIn('head',zones)
        finally:world.store.close()

    def test_shipping_rules_and_python_rules_are_identical(self):
        root=Path(__file__).resolve().parents[1]
        shipped=root/'unity/JianghuUnity/Assets/StreamingAssets/Backend/jianghu/physical_impact.json'
        self.assertEqual(combat.IMPACT,json.loads(shipped.read_text(encoding='utf8')))


if __name__=='__main__':unittest.main()
