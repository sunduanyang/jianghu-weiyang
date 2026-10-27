from copy import deepcopy
import math
import unittest

from jianghu import combat_attributes, items, physical_combat as combat, weapons
from jianghu.engine import World
from tests.physical_helpers import frame


class WeaponSystemTests(unittest.TestCase):
    def world(self):
        w = World(seed=42, population=4, durable=False)
        self.addCleanup(w.store.close)
        for n in w.npcs.values():
            n['age_months'], n['health'], n['alive'] = 300, 100., True
        return w

    def equip(self, w, template_id):
        item = items.create(w, 'player', template_id, '兵器验收')
        w.player['equipment']['weapon'] = item['id']
        return item

    def test_all_weapon_items_expose_exactly_six_attributes(self):
        w = self.world()
        expected = {key for key, value in items.CATALOG.items() if value['category'] == 'weapon'}
        self.assertEqual(set(weapons.CATALOG), expected)
        for data in (items.view(w, 'player'), items.market_view(w)):
            self.assertNotIn('weight', data)
            self.assertNotIn('capacity', data)
            self.assertEqual([a['id'] for a in data['weapon_attribute_catalog']], list(weapons.ATTRIBUTE_KEYS))
            for item in data['items']:
                self.assertNotIn('attack', item)
                self.assertNotIn('weight', item)
                if item['category'] == 'weapon':
                    attributes = item['weapon_attributes']
                    self.assertEqual(set(attributes), set(weapons.ATTRIBUTE_KEYS))
                    self.assertNotIn('training', item)
                    for value in attributes.values():
                        self.assertTrue(math.isfinite(value) and value > 0)
        self.assertTrue(all('weight' not in spec for spec in items.CATALOG.values()))

    def test_equipment_passes_each_item_stat_to_both_combat_sides(self):
        w = self.world()
        original_scale = combat.fighter(w.player, 'player', combat.equipment(w, 'player'))['damage_scale']
        for template_id in weapons.CATALOG:
            with self.subTest(template=template_id):
                item = self.equip(w, template_id)
                fighter = combat.fighter(w.player, 'player', combat.equipment(w, 'player'))
                for key, value in weapons.attributes(item).items():
                    self.assertEqual(fighter['weapon'][key], value)
                self.assertEqual(fighter['damage_scale'], original_scale)
                items.transfer(w, item, 1)
                w.npcs[1]['equipment']['weapon'] = item['id']
                npc = combat.fighter(w.npcs[1], 1, combat.equipment(w, 1))
                self.assertEqual(npc['weapon'], fighter['weapon'])

    def test_legacy_attack_cannot_increase_character_damage(self):
        w = self.world()
        scores = combat_attributes.ratings(w.player)
        self.assertEqual(combat_attributes.parameters(scores, {'attack': 100000}),
                         combat_attributes.parameters(scores))
        self.equip(w, 'tianheng_sword')
        self.assertNotIn('attack', items.bonuses(w, 'player'))
        self.assertNotIn('attack', combat.equipment(w, 'player'))

    def test_weapon_damage_coefficient_is_applied_once(self):
        w = self.world()
        sword = self.equip(w, 'tianheng_sword')
        fighter = combat.fighter(w.player, 'player', combat.equipment(w, 'player'))
        weapon = fighter['weapon']
        base = combat.impact_damage(weapon['mass'], 5., 1., fighter['damage_scale'])
        actual = combat.impact_damage(weapon['mass'], 5., weapon['damage'], fighter['damage_scale'])
        self.assertAlmostEqual(actual / base, weapons.attributes(sword)['damage'])

    def test_same_type_items_keep_geometry_but_have_distinct_combat_stats(self):
        w = self.world()
        a = weapons.profile(self.equip(w, 'wood_sword'))
        b = weapons.profile(self.equip(w, 'tianheng_sword'))
        for key in ('id', 'reach', 'tail', 'support_grip', 'distance'):
            self.assertEqual(a[key], b[key])
        for key in ('mass', 'handling', 'cost', 'damage', 'pressure'):
            self.assertNotEqual(a[key], b[key])

    def test_breakage_and_repair_preserve_identity_and_restore_combat_stats(self):
        w = self.world()
        item = self.equip(w, 'iron_sword')
        before = weapons.attributes(item)
        items.wear(w, 'player', ['weapon'], 100)
        self.assertEqual(combat.equipment(w, 'player')['weapon_profile'], 'unarmed')
        self.assertEqual(items.describe(item)['weapon_attributes']['durability'], 0)
        items.perform(w, 'player', 'repair', item['id'])
        self.assertEqual(weapons.attributes(item), before)
        self.assertEqual(combat.equipment(w, 'player')['weapon_item'], item['id'])

    def test_crafting_with_large_inventory_still_consumes_real_costs(self):
        w = self.world()
        for _ in range(100):
            items.create(w, 'player', 'iron', '大量物品验收')
        herbs = [i['id'] for i in items.objects(w, 'player') if i['template'] == 'herb']
        wealth = w.player['wealth']
        event = items.perform(w, 'player', 'craft', recipe='salve')
        self.assertEqual(w.player['wealth'], wealth - 2)
        self.assertTrue(all(key not in w.items['objects'] for key in herbs))
        self.assertEqual(w.items['objects'][event['facts']['item_id']]['template'], 'salve')

    def test_reload_upgrades_old_active_bout_without_healing_or_restarting(self):
        w = self.world()
        item = self.equip(w, 'cloud_sword')
        w.start_battle(1)
        packet = frame(w.battle, 3)
        packet['fighters'][0]['stamina'] -= 10
        packet['fighters'][0]['health'] -= 5
        packet['fighters'][1]['landed_hits'] = packet['hits'] = 1
        w.battle_action(w.battle['id'], w.battle['revision'], 'checkpoint', packet)
        state = deepcopy(w.export())
        for fighter in state['battle']['fighters']:
            fighter.pop('weapon_version')
            fighter['damage_scale'] += .3
        state['items']['objects'][item['id']].update(attack=999, weight=999)
        state['player'].update(weight=999, capacity=30)
        before = deepcopy(w.battle)
        events = w.store.count()
        w.restore(state)
        self.assertEqual(w.battle['id'], before['id'])
        self.assertEqual(w.battle['revision'], before['revision'])
        self.assertEqual(w.battle['frame'], before['frame'])
        self.assertEqual(w.battle['elapsed'], before['elapsed'])
        self.assertEqual(w.battle['fighters'][0]['vigor'], before['fighters'][0]['vigor'])
        self.assertEqual(w.battle['fighters'][0]['weapon']['mass'], weapons.CATALOG['cloud_sword']['mass'])
        self.assertEqual(w.battle['fighters'][0]['damage_scale'], before['fighters'][0]['damage_scale'])
        self.assertEqual(w.player['wealth'], state['player']['wealth'])
        self.assertEqual(w.rng.getstate(), state['random_state'])
        self.assertEqual(w.store.count(), events)
        self.assertNotIn('weight', w.player)
        self.assertNotIn('capacity', w.player)
        self.assertNotIn('attack', w.items['objects'][item['id']])
        self.assertNotIn('weight', w.items['objects'][item['id']])
        self.assertFalse(combat.upgrade_tuning(w))


if __name__ == '__main__':
    unittest.main()
