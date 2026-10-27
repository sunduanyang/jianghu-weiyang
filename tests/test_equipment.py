from copy import deepcopy
from http.server import ThreadingHTTPServer
import json
import threading
from types import SimpleNamespace
import unittest
import urllib.error
import urllib.request

from jianghu import equipment, items, physical_combat as combat
from jianghu.engine import World
from jianghu.store import Store


class EquipmentTests(unittest.TestCase):
    def world(self):
        w = World(42, 4, durable=False)
        self.addCleanup(w.store.close)
        for npc in w.npcs.values():
            npc.update(age_months=300, health=100., alive=True)
        return w

    def equip(self, w, template, owner='player'):
        item = items.create(w, owner, template, '装备系统验收')
        items.perform(w, owner, 'equip', item['id'])
        return item

    def test_exact_three_slots_and_no_ordinary_item_equipment(self):
        w = self.world()
        self.assertEqual(items.SLOTS, {'weapon': '主手', 'shield': '副手', 'armor': '护甲'})
        view = equipment.view(w)
        self.assertEqual([s['slot'] for s in view['slots']], list(items.SLOTS))
        self.assertEqual(view['equipped_count'], 2)
        self.assertEqual({i['slot'] for i in view['items']}, {'weapon', 'armor'})
        jade = next(i for i in items.objects(w, 'player') if i['template'] == 'jade')
        self.assertNotIn('equip', items.operations(w, 'player', jade))
        self.assertNotIn('weight', view)
        self.assertNotIn('capacity', view)
        self.assertNotIn('attack', view['effects']['bonuses'])

    def test_replacement_keeps_both_instances_and_has_no_time_or_money_cost(self):
        w = self.world()
        old = w.player['equipment']['weapon']
        sword = items.create(w, 'player', 'iron_sword', '替换验收')
        before = deepcopy(w.export())
        event = items.perform(w, 'player', 'equip', sword['id'])
        self.assertEqual(event['facts']['replaced'], old)
        self.assertEqual(w.player['equipment']['weapon'], sword['id'])
        self.assertIn(old, w.player['inventory'])
        self.assertEqual(set(w.items['objects']), set(before['items']['objects']))
        self.assertEqual(w.player['wealth'], before['player']['wealth'])
        self.assertEqual(w.month, before['month'])
        self.assertEqual(w.rng.getstate(), before['random_state'])
        items.perform(w, 'player', 'unequip', sword['id'])
        self.assertEqual(combat.equipment(w, 'player')['weapon_profile'], 'unarmed')

    def test_two_handed_offhand_suppression_is_shared_by_effects_combat_and_wear(self):
        w = self.world()
        shield = self.equip(w, 'wood_shield')
        defended = items.bonuses(w, 'player')['defense']
        spear = self.equip(w, 'long_spear')
        state = equipment.snapshot(w, 'player')
        offhand = next(s for s in state['slots'] if s['slot'] == 'shield')
        self.assertEqual(offhand['status'], 'suppressed')
        self.assertEqual(w.player['equipment']['shield'], shield['id'])
        self.assertEqual(items.bonuses(w, 'player')['defense'], defended - 2)
        self.assertFalse(combat.equipment(w, 'player')['shield'])
        items.wear(w, 'player', ['weapon', 'shield', 'armor'], 2)
        self.assertEqual(shield['durability'], 100)
        self.assertEqual(spear['durability'], 98)
        self.equip(w, 'iron_sword')
        self.assertTrue(combat.equipment(w, 'player')['shield'])
        self.assertEqual(items.bonuses(w, 'player')['defense'], defended)
        items.wear(w, 'player', ['shield'], 2)
        self.assertEqual(shield['durability'], 98)

    def test_broken_main_hand_releases_offhand_and_repair_restores_conflict(self):
        w = self.world()
        shield = self.equip(w, 'wood_shield')
        spear = self.equip(w, 'long_spear')
        items.wear(w, 'player', ['weapon'], 100)
        self.assertTrue(combat.equipment(w, 'player')['shield'])
        self.assertEqual(combat.equipment(w, 'player')['weapon_profile'], 'unarmed')
        self.assertEqual(equipment.snapshot(w, 'player')['slots'][0]['status'], 'broken')
        items.perform(w, 'player', 'repair', spear['id'])
        self.assertFalse(combat.equipment(w, 'player')['shield'])
        self.assertEqual(w.player['equipment']['shield'], shield['id'])

    def test_armor_breakage_removes_real_damage_reduction_and_repair_restores_it(self):
        w = self.world()
        armor = self.equip(w, 'leather')
        healthy = equipment.effects(w, 'player')['combat_parameters']['damage_reduction']
        items.wear(w, 'player', ['armor'], 100)
        broken = equipment.effects(w, 'player')['combat_parameters']['damage_reduction']
        self.assertLess(broken, healthy)
        self.assertEqual(equipment.snapshot(w, 'player')['slots'][2]['status'], 'broken')
        items.perform(w, 'player', 'repair', armor['id'])
        self.assertEqual(equipment.effects(w, 'player')['combat_parameters']['damage_reduction'], healthy)

    def test_replacement_preview_is_pure_and_matches_real_equip(self):
        w = self.world()
        self.equip(w, 'wood_shield')
        spear = items.create(w, 'player', 'long_spear', '对比验收')
        before = deepcopy(w.export())
        count = w.store.count()
        preview = equipment.preview(w, 'player', spear['id'])
        self.assertEqual(w.export(), before)
        self.assertEqual(w.store.count(), count)
        self.assertTrue(preview['warnings'])
        self.assertTrue(any(c['id'] == 'defense' and c['delta'] == -2 for c in preview['changes']))
        items.perform(w, 'player', 'equip', spear['id'])
        after = equipment.effects(w, 'player')['values']
        for change in preview['changes']:
            self.assertEqual(change['after'], after[change['id']])

    def test_offhand_preview_reports_restoration_when_switching_to_single_hand(self):
        w = self.world()
        self.equip(w, 'wood_shield')
        self.equip(w, 'long_spear')
        sword = items.create(w, 'player', 'iron_sword', '恢复副手验收')
        preview = equipment.preview(w, 'player', sword['id'])
        self.assertIn('副手恢复生效', preview['warnings'])
        self.assertTrue(any(c['id'] == 'defense' and c['delta'] == 2 for c in preview['changes']))

    def test_migration_clears_retired_and_invalid_bindings_without_losing_items(self):
        w = self.world()
        jade = next(i for i in items.objects(w, 'player') if i['template'] == 'jade')
        hammer = items.create(w, 'player', 'hammer', '旧装备验收')
        foreign = w.npcs[1]['equipment']['armor']
        state = deepcopy(w.export())
        state['player']['equipment'].update(accessory=jade['id'], tool=hammer['id'], shield=foreign)
        count = w.store.count()
        w.restore(state)
        self.assertEqual(set(w.player['equipment']), {'weapon', 'armor'})
        self.assertEqual(w.player['inventory'], state['player']['inventory'])
        self.assertEqual(w.items['objects'], state['items']['objects'])
        self.assertEqual(w.player['wealth'], state['player']['wealth'])
        self.assertEqual(w.rng.getstate(), state['random_state'])
        self.assertEqual(w.store.count(), count)
        w.checkpoint()
        reloaded = World(store=w.store, durable=False)
        self.assertEqual(reloaded.player['equipment'], w.player['equipment'])

    def test_npc_and_player_use_the_same_three_slot_rules(self):
        w = self.world()
        self.equip(w, 'wood_shield', 1)
        self.equip(w, 'long_spear', 1)
        possessions = items.view(w, 1)
        self.assertEqual(len(possessions['equipment_state']['slots']), 3)
        self.assertEqual(possessions['equipment_state']['slots'][1]['status'], 'suppressed')
        gear = combat.equipment(w, 1)
        self.assertEqual(gear['defense'], possessions['bonuses']['defense'])
        self.assertFalse(gear['shield'])


class EquipmentHTTPTests(unittest.TestCase):
    def setUp(self):
        from jianghu.server import handler_for
        self.world = World(42, 4, durable=False)
        game = SimpleNamespace(world=self.world, lock=threading.RLock())
        self.server = ThreadingHTTPServer(('127.0.0.1', 0), handler_for(game))
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.url = f'http://127.0.0.1:{self.server.server_port}/api/equipment'

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(3)
        self.world.store.close()

    def request(self, body=None):
        request = urllib.request.Request(self.url, json.dumps(body).encode() if body is not None else None,
                                         {'Content-Type': 'application/json'})
        with urllib.request.urlopen(request, timeout=5) as response:
            return json.load(response)

    def test_equipment_endpoint_renders_and_persists_real_replacement(self):
        item = items.create(self.world, 'player', 'iron_sword', 'HTTP验收')
        self.assertEqual(len(self.request()['slots']), 3)
        response = self.request(dict(operation='equip', item_id=item['id']))
        self.assertEqual(response['slots'][0]['item']['id'], item['id'])
        self.assertIn('last_event', response)
        self.assertEqual(self.world.store.load()['player']['equipment']['weapon'], item['id'])

    def test_foreign_item_and_unsupported_requests_are_atomic(self):
        foreign = self.world.npcs[1]['equipment']['weapon']
        for body in (dict(operation='equip', item_id=foreign),
                     dict(operation='equip', item_id=foreign, owner=1),
                     dict(operation='sell', item_id=self.world.player['equipment']['weapon'])):
            before = deepcopy(self.world.export())
            with self.assertRaises(urllib.error.HTTPError):
                self.request(body)
            self.assertEqual(self.world.export(), before)

    def test_active_battle_locks_equipment_without_preventing_inspection(self):
        self.world.npcs[1].update(age_months=300, health=100.)
        self.world.start_battle(1)
        self.assertTrue(self.request()['locked'])
        before = deepcopy(self.world.export())
        with self.assertRaises(urllib.error.HTTPError):
            self.request(dict(operation='unequip', item_id=self.world.player['equipment']['weapon']))
        self.assertEqual(self.world.export(), before)


if __name__ == '__main__':
    unittest.main()
