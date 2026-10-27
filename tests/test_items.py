from copy import deepcopy
from http.server import ThreadingHTTPServer
import json
import threading
from types import SimpleNamespace
import unittest
import urllib.error
import urllib.request

from jianghu import items, martial
from jianghu.ai import context_for
from jianghu.engine import World
from jianghu.server import handler_for
from jianghu.store import Store


class ItemTests(unittest.TestCase):
    def world(self, population=30):
        w = World(seed=42, population=population, durable=False)
        self.addCleanup(w.store.close)
        return w

    def find(self, w, template_id, owner='player'):
        return next(i for i in items.objects(w, owner) if i['template'] == template_id)

    def assert_ownership(self, w):
        owners = ['player', 'market', 'government']+[str(i) for i in w.npcs]
        ids = []
        for owner in owners:
            n = items.actor(w, owner)
            ids.extend(n['inventory'])
            for i in items.objects(w, owner):
                self.assertEqual(i['owner'], owner)
            for slot, i in n['equipment'].items():
                self.assertIn(i, n['inventory'])
                self.assertEqual(items.spec(items.get(w, owner, i))['category'], slot)
        self.assertEqual(len(ids), len(set(ids)))
        self.assertEqual(set(ids), set(w.items['objects']))

    def test_buy_sell_transfer_same_instance_and_conserve_silver(self):
        w = self.world()
        item = self.find(w, 'iron_sword', 'market')
        total = w.player['wealth']+w.items['market']['wealth']
        before = w.player['wealth']
        e = items.perform(w, 'player', 'buy', item['id'])
        self.assertAlmostEqual(before-w.player['wealth'], 48)
        self.assertEqual(item['owner'], 'player')
        self.assertEqual(item['last_event'], e['id'])
        items.perform(w, 'player', 'sell', item['id'])
        self.assertEqual(item['owner'], 'market')
        self.assertAlmostEqual(w.player['wealth']+w.items['market']['wealth'], total)
        self.assert_ownership(w)

    def test_five_requested_families_have_all_nine_grades_and_shared_palette(self):
        w = self.world()
        market = items.market_view(w)
        self.assertEqual([g['grade'] for g in market['grades']], list(range(9, 0, -1)))
        self.assertEqual(len({g['grade_color'] for g in market['grades']}), 9)
        self.assertEqual(market['grades'], items.view(w, 'player')['grades'])
        for category in ('manual', 'food', 'wine', 'tea', 'weapon'):
            rows = [i for i in market['items'] if i['category'] == category]
            self.assertEqual({i['grade'] for i in rows}, set(range(1, 10)), category)
            for row in rows:
                self.assertEqual(row['grade_color'], items.GRADE_COLORS[row['grade']])
        self.assertEqual(items.CATALOG['wine']['category'], 'wine')

    def test_drinking_at_full_health_improves_mood_and_consumes_one_instance(self):
        w = self.world()
        w.player['health'], w.player['mood']['joy'] = 100, 15
        for template_id in ('wine', 'coarse_tea'):
            drink = items.create(w, 'player', template_id, '测试饮品')
            spare = items.create(w, 'player', template_id, '另一份饮品')
            before = w.player['mood']['joy']
            event = items.perform(w, 'player', 'use', drink['id'])
            self.assertEqual(w.player['health'], 100)
            self.assertGreater(w.player['mood']['joy'], before)
            self.assertEqual(event['facts']['joy_gain'], items.CATALOG[template_id]['joy'])
            self.assertNotIn(drink['id'], w.items['objects'])
            self.assertIn(spare['id'], w.items['objects'])
        w.player['mood']['joy'] = 30
        before = deepcopy(w.export())
        with self.assertRaises(ValueError):
            items.perform(w, 'player', 'use', spare['id'])
        self.assertEqual(w.export(), before)

    def test_catalog_upgrade_preserves_old_possessions_and_never_restocks_on_reload(self):
        w = self.world()
        for item in list(w.items['objects'].values()):
            if item['template'] not in items.ORIGINAL_TEMPLATES:
                items.remove(w, item)
        depleted = self.find(w, 'wine', 'market')
        for item in list(items.objects(w, 'market')):
            if item['template'] == depleted['template']:
                items.remove(w, item)
        w.items['version'] = 1
        previous = deepcopy(w.items['objects'])
        inventory, silver, rng = w.player['inventory'][:], w.player['wealth'], w.rng.getstate()
        w.checkpoint()
        upgraded = World(store=w.store, durable=False)
        self.assertEqual(upgraded.player['inventory'], inventory)
        self.assertEqual(upgraded.player['wealth'], silver)
        self.assertEqual(upgraded.rng.getstate(), rng)
        self.assertTrue(all(upgraded.items['objects'][key] == value for key, value in previous.items()))
        self.assertFalse(any(i['template'] == 'wine' for i in items.objects(upgraded, 'market')))
        self.assertEqual(upgraded.items['version'], 2)
        registration = next(e for e in upgraded.store.history() if e['kind'] == 'item_catalog')
        self.assertTrue(registration['facts']['initial_condition'])
        tea = self.find(upgraded, 'coarse_tea', 'market')
        items.perform(upgraded, 'player', 'buy', tea['id'])
        upgraded.checkpoint()
        snapshot, events = deepcopy(upgraded.items), upgraded.store.count()
        reloaded = World(store=w.store, durable=False)
        self.assertEqual(reloaded.items, snapshot)
        self.assertEqual(reloaded.store.count(), events)
        self.assertEqual(items.get(reloaded, 'player', tea['id'])['owner'], 'player')
        self.assert_ownership(reloaded)

    def test_invalid_purchase_and_foreign_item_leave_world_unchanged(self):
        w = self.world()
        item = self.find(w, 'cloud_sword', 'market')
        before = deepcopy(w.export())
        for op, i in [('buy', item['id']), ('sell', item['id']), ('use', 'missing')]:
            with self.assertRaises(ValueError):
                items.perform(w, 'player', op, i)
            self.assertEqual(w.export(), before)
        w.items['market']['wealth'] = 0
        spare = self.find(w, 'herb')
        before = deepcopy(w.export())
        with self.assertRaises(ValueError):
            items.perform(w, 'player', 'sell', spare['id'])
        self.assertEqual(w.export(), before)

    def test_slot_replacement_breakage_and_material_repair(self):
        w = self.world()
        old = self.find(w, 'wood_sword')
        sword = self.find(w, 'iron_sword', 'market')
        items.perform(w, 'player', 'buy', sword['id'])
        items.perform(w, 'player', 'equip', sword['id'])
        self.assertEqual(w.player['equipment']['weapon'], sword['id'])
        self.assertIn(old['id'], w.player['inventory'])
        sword['durability'] = 1
        items.wear(w, 'player', ['weapon'], 3)
        self.assertEqual(items.bonuses(w, 'player')['attack'], 0)
        material = self.find(w, 'iron')['id']
        items.perform(w, 'player', 'repair', sword['id'])
        self.assertNotIn(material, w.items['objects'])
        self.assertEqual(sword['durability'], 100)
        self.assertEqual(items.bonuses(w, 'player')['attack'], 6)

    def test_consumption_cannot_repeat_and_archives_snapshot(self):
        w = self.world()
        w.player['health'] = 91
        salve = self.find(w, 'salve')
        e = items.perform(w, 'player', 'use', salve['id'])
        self.assertEqual(w.player['health'], 100)
        self.assertEqual(e['facts']['health_gain'], 9)
        self.assertEqual(e['facts']['item_before']['template'], 'salve')
        self.assertIsNone(e['facts']['item_after'])
        with self.assertRaises(ValueError):
            items.perform(w, 'player', 'use', salve['id'])

    def test_same_book_cannot_reset_learning_by_buying_another_copy(self):
        w = self.world()
        book = self.find(w, 'basic_manual')
        for _ in range(3):
            items.perform(w, 'player', 'read', book['id'])
        self.assertAlmostEqual(w.player['skill'], 15.6)
        other = self.find(w, 'basic_manual', 'market')
        items.perform(w, 'player', 'buy', other['id'])
        with self.assertRaises(ValueError):
            items.perform(w, 'player', 'read', other['id'])
        advanced = self.find(w, 'cloud_manual', 'market')
        w.player['wealth'] = 1000
        items.perform(w, 'player', 'buy', advanced['id'])
        with self.assertRaises(ValueError):
            items.perform(w, 'player', 'read', advanced['id'])

    def test_gift_preserves_origin_and_cannot_duplicate(self):
        w = self.world()
        item = self.find(w, 'salve')
        origin = deepcopy(item)
        e = items.perform(w, 'player', 'gift', item['id'], target=1)
        self.assertEqual(item['owner'], '1')
        self.assertEqual(item['origin'], origin['origin'])
        self.assertEqual(item['last_event'], e['id'])
        self.assertGreater(w.player['ties']['1']['affinity'], 0)
        with self.assertRaises(ValueError):
            items.perform(w, 'player', 'gift', item['id'], target=2)
        self.assert_ownership(w)

    def test_capacity_checked_before_purchase_or_gift(self):
        w = self.world()
        for _ in range(38):
            items.create(w, 'player', 'iron', '负重测试')
        item = self.find(w, 'salve', 'market')
        before = deepcopy(w.export())
        with self.assertRaises(ValueError):
            items.perform(w, 'player', 'buy', item['id'])
        self.assertEqual(w.export(), before)
        w.npcs[1]['skill'] = 0
        for _ in range(25):
            items.create(w, 1, 'iron', '负重测试')
        gift = self.find(w, 'salve')
        before = deepcopy(w.export())
        with self.assertRaises(ValueError):
            items.perform(w, 'player', 'gift', gift['id'], target=1)
        self.assertEqual(w.export(), before)

    def test_crafting_consumes_exact_materials_and_rejects_missing_cost(self):
        w = self.world()
        before = set(w.items['objects'])
        herbs = [i['id'] for i in items.objects(w, 'player') if i['template'] == 'herb']
        total = w.player['wealth']+w.items['market']['wealth']
        e = items.perform(w, 'player', 'craft', recipe='salve')
        self.assertTrue(all(i not in w.items['objects'] for i in herbs))
        new = w.items['objects'][e['facts']['item_id']]
        self.assertNotIn(new['id'], before)
        self.assertEqual(new['origin_event'], e['id'])
        self.assertAlmostEqual(w.player['wealth']+w.items['market']['wealth'], total)
        snapshot = deepcopy(w.export())
        with self.assertRaises(ValueError):
            items.perform(w, 'player', 'craft', recipe='salve')
        self.assertEqual(w.export(), snapshot)

    def test_death_transfers_all_items_and_unclaimed_estate_is_preserved(self):
        w = self.world()
        a, b = w.npcs[1], w.npcs[2]
        a['spouse'], b['spouse'] = 2, 1
        ids = a['inventory'][:]
        death = w.die(a, 'old_age')
        self.assertEqual(a['inventory'], [])
        self.assertEqual(a['equipment'], {})
        for i in ids:
            self.assertEqual(w.items['objects'][i]['owner'], '2')
            self.assertIn(death['id'], w.store.event(w.items['objects'][i]['last_event'])['causes'])
        ids = b['inventory'][:]
        w.die(b, 'old_age')
        self.assertTrue(set(ids).issubset(set(w.items['government']['inventory'])))
        self.assert_ownership(w)

    def test_old_save_migration_is_once_only_and_preserves_rng(self):
        w = self.world()
        w.die(w.npcs[2], 'old_age')
        legacy = deepcopy(w.export())
        legacy.pop('items')
        for n in list(legacy['npcs'].values())+[legacy['player']]:
            for key in ('inventory', 'equipment', 'read_books'):
                n.pop(key, None)
        fresh_store = Store()
        self.addCleanup(fresh_store.close)
        fresh_store.save(legacy)
        b = World(store=fresh_store, durable=False)
        self.assertEqual(b.rng.getstate(), w.rng.getstate())
        self.assertEqual(b.month, w.month)
        self.assertEqual(b.npcs[2]['inventory'], [])
        count = b.store.count()
        snapshot = deepcopy(b.items)
        c = World(store=fresh_store, durable=False)
        self.assertEqual(c.items, snapshot)
        self.assertEqual(c.store.count(), count)
        registrations = [e for e in c.store.history(npc_id=1) if e['kind'] == 'item_registration']
        self.assertEqual(len(registrations), 1)
        self.assert_ownership(c)

    def test_npc_legal_item_action_and_private_context(self):
        w = self.world()
        n = w.npcs[1]
        n['age_months'], n['health'] = 300, 25
        salve = self.find(w, 'salve', 1)
        ctx = context_for(w, n)
        self.assertIn(salve['id'], [i['id'] for i in ctx['possessions']])
        self.assertNotIn('market', ctx)
        a = next(a for a in w.legal_actions(n) if a['id'] == f"use_item:{salve['id']}")
        e = w.execute(n, a)
        self.assertEqual(n['health'], 43)
        self.assertEqual(e['kind'], 'item_use')
        self.assertIsNone(w.execute(n, a))
        self.assert_ownership(w)

    def test_npc_does_not_offer_sale_to_an_empty_market(self):
        w = self.world()
        n = w.npcs[1]
        n['wealth'], n['age_months'] = 1, 300
        items.create(w, 1, 'basic_manual', '测试闲书')
        w.items['market']['wealth'] = 0
        self.assertFalse(any(a['kind'] == 'sell_item' for a in w.legal_actions(n)))

    def test_player_equipment_affects_sparring_and_monthly_recovery(self):
        w = self.world()
        n = w.npcs[1]
        n['age_months'], n['health'] = 300, 100.
        state = w.intervene(1, 'challenge')
        b = state['battle']
        self.assertEqual(b['fighters'][0]['attack'], martial.stats(w.player)['attack']+2)
        self.assertAlmostEqual(b['fighters'][0]['outer_guard'], martial.stats(w.player)['outer_guard']+1)
        w.battle_action(b['id'], b['revision'], 'surrender')
        self.assertLess(w.player['health'], 100)
        self.assertEqual(self.find(w, 'wood_sword')['durability'], 98)
        w.month_step()
        self.assertEqual(w.player['health'], 100)

    def test_equipment_changes_real_work_and_training_and_wears_out(self):
        a, b = self.world(), self.world()
        n, control = a.npcs[1], b.npcs[1]
        for p in (n, control):
            p['age_months'], p['wealth'], p['health'] = 300, 100., 100.
        control['equipment'].pop('tool', None)
        tool = items.create(a, 1, 'hammer', '测试器具')
        items.perform(a, 1, 'equip', tool['id'])
        action = {'id': 'work:self'}
        a.execute(n, action)
        b.execute(control, action)
        self.assertGreater(n['wealth'], control['wealth'])
        self.assertEqual(tool['durability'], 99)
        sword = self.find(a, 'wood_sword', 1)
        a.execute(n, {'id': 'train:self'})
        self.assertEqual(sword['durability'], 99)

    def test_long_world_has_one_owner_per_item_and_reload_continues(self):
        w = self.world(population=100)
        for _ in range(120):
            w.month_step()
        self.assert_ownership(w)
        self.assertGreater(w.metrics['actions'].get('use_item', 0), 0)
        store = Store()
        self.addCleanup(store.close)
        w.checkpoint()
        w.store.db.backup(store.db)
        b = World(store=store, durable=False)
        for _ in range(3):
            w.month_step()
            b.month_step()
        self.assertEqual(w.items, b.items)
        self.assertEqual(w.npcs, b.npcs)
        self.assert_ownership(b)


class ItemHTTPTests(unittest.TestCase):
    def setUp(self):
        self.world = World(population=10, durable=False)
        game = SimpleNamespace(world=self.world, lock=threading.RLock())
        self.server = ThreadingHTTPServer(('127.0.0.1', 0), handler_for(game))
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.base = f'http://127.0.0.1:{self.server.server_port}'

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=3)
        self.world.store.close()

    def request(self, path, body=None, origin=None):
        headers = {'Content-Type': 'application/json'}
        if origin:
            headers['Origin'] = origin
        request = urllib.request.Request(self.base+path,
                                         json.dumps(body).encode() if body is not None else None, headers)
        with urllib.request.urlopen(request, timeout=5) as r:
            return json.load(r)

    def test_inventory_market_and_purchase_are_persisted_through_http(self):
        inventory = self.request('/api/inventory')
        market = self.request('/api/market')
        sword = next(i for i in market['items'] if i['template'] == 'iron_sword')
        self.request('/api/items', {'operation': 'buy', 'item_id': sword['id']})
        after = self.request('/api/inventory')
        self.assertEqual(len(after['items']), len(inventory['items'])+1)
        self.assertEqual(after['wealth'], inventory['wealth']-sword['buy_price'])
        saved = self.world.store.load()
        self.assertEqual(saved['items']['objects'][sword['id']]['owner'], 'player')

    def test_http_rejects_foreign_inventory_and_cross_origin_mutation(self):
        foreign = self.world.npcs[1]['inventory'][0]
        before = deepcopy(self.world.export())
        with self.assertRaises(urllib.error.HTTPError) as raised:
            self.request('/api/items', {'operation': 'sell', 'owner': 1, 'item_id': foreign})
        self.assertEqual(raised.exception.code, 400)
        with self.assertRaises(urllib.error.HTTPError) as raised:
            self.request('/api/items', {'operation': 'craft', 'recipe': 'salve'}, origin='http://example.invalid')
        self.assertEqual(raised.exception.code, 403)
        self.assertEqual(self.world.export(), before)

    def test_items_cannot_change_during_interactive_battle(self):
        n = self.world.npcs[1]
        n['age_months'], n['health'] = 360, 100
        self.world.start_battle(1)
        sword = self.world.player['equipment']['weapon']
        before = deepcopy(self.world.export())
        with self.assertRaises(urllib.error.HTTPError) as raised:
            self.request('/api/items', {'operation': 'unequip', 'item_id': sword})
        self.assertEqual(raised.exception.code, 400)
        self.assertEqual(self.world.export(), before)


if __name__ == '__main__':
    unittest.main()
