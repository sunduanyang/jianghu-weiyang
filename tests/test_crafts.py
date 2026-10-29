from copy import deepcopy
import json
import threading
from http.server import ThreadingHTTPServer
from types import SimpleNamespace
from urllib.error import HTTPError
from urllib.request import Request, urlopen
import unittest

from jianghu import (careers, crafts, economics, items, physical_combat as combat, physical_reference as reference,
                     player_interactions as encounters, protagonist, weapons)
from jianghu.engine import World
from tests.physical_helpers import frame


class CraftTests(unittest.TestCase):
    def setUp(self):
        self.w = w = World(42, 0, durable=False)
        self.addCleanup(w.store.close)
        protagonist.perform(w, dict(action='create', revision=0, month=0, name='来客', sex='女', age=24, place=45))
        self.n = n = w.make_npc(place=45)
        n.update(age_months=360, health=100., wealth=100., time_left=4, spouse=None, job='匠人')
        n['career'].update(profession='匠人', experience={'匠人': 0.})
        n['traits'].update(integrity=90., sociability=90., kindness=90., boldness=50.)
        n['mood'].update(anger=0., joy=0.)
        w.reindex(); w.checkpoint()

    def skill(self, craft, amount, actor=None):
        actor = actor or self.w.player
        if 'career' in actor:
            actor['career']['experience'][crafts.PROFESSION[craft]] = amount
        else:
            crafts.prepare(actor)['skill'][craft] = amount

    def body(self, action, **extra):
        return dict(npc_id=self.n['id'], action=action, revision=self.w.player['protagonist']['revision'],
                    month=self.w.month, **extra)

    def act(self, action, **extra):
        return encounters.perform(self.w, self.body(action, **extra))

    def iron(self, count):
        for _ in range(count):
            items.create(self.w, 'player', 'iron', '测试铁料')

    # ------------------------------------------------------------ proficiency

    def test_npc_craft_is_the_trade_experience_itself(self):
        n = self.n
        n['career']['experience']['匠人'] = 42.
        self.assertEqual(crafts.value(n, 'forging'), 42.)
        crafts.gain(n, 'forging', 3)
        self.assertEqual(careers.experience(n, '匠人'), 45.)
        self.assertEqual(crafts.level(n, 'forging')[1], careers.level(n)[1])
        self.assertNotIn('crafts', n)

    def test_player_keeps_own_record_and_practice_slows_with_mastery(self):
        self.assertEqual(crafts.value(self.w.player, 'letters'), 0.)
        low = crafts.practice(self.w.player, 'letters', 1.2)['gain']
        self.skill('letters', 90)
        high = crafts.practice(self.w.player, 'letters', 1.2)['gain']
        self.assertGreater(low, high)
        self.assertGreater(high, 0)

    # ---------------------------------------------------------------- forging

    def test_items_without_craft_keep_identical_combat_packets(self):
        sword = items.create(self.w, 'player', 'iron_sword', '测试')
        self.w.player['equipment']['weapon'] = sword['id']
        packet = combat.equipment(self.w, 'player')['weapon']
        template = weapons.CATALOG['iron_sword']
        for key in weapons.STAT_KEYS:
            self.assertEqual(packet[key], template[key])
        self.assertEqual(items.buy_price(sword), 48)

    def test_forging_is_reproducible_bounded_and_reaches_the_arena(self):
        self.skill('forging', 40); self.iron(4)
        rng_state = self.w.rng.getstate(); points = self.w.player['protagonist']['points']
        twin = deepcopy(self.w.export())
        result = crafts.perform(self.w, dict(operation='forge', template='steel_blade', temper='heavy'))
        item = items.get(self.w, 'player', result['event']['facts']['item_id'])
        self.assertEqual(self.w.rng.getstate(), rng_state)
        self.assertEqual(self.w.player['protagonist']['points'], points-crafts.FORGE_POINTS)
        lo, hi = crafts.MOD_LIMITS
        self.assertTrue(all(lo <= v <= hi for v in item['craft']['mods'].values()))
        base = weapons.CATALOG['steel_blade']
        self.assertGreater(weapons.attributes(item)['mass'], base['mass'])
        self.assertGreater(weapons.attributes(item)['pressure'], base['pressure'])
        self.w.player['equipment']['weapon'] = item['id']
        self.assertEqual(combat.equipment(self.w, 'player')['weapon']['mass'], weapons.attributes(item)['mass'])
        self.assertIn('所铸', items.describe(item)['craft_label'])
        # The same world forging the same piece gets the same fire.
        self.skill('forging', 40)
        self.assertEqual(crafts.forge_quality(self.w, self.w.player, 'steel_blade', item['id']), item['craft']['quality'])
        self.assertEqual(twin['month'], self.w.month)

    def test_light_temper_quickens_and_balanced_wears_slower(self):
        light, _ = crafts.modifiers('light', .8)
        balanced, wear = crafts.modifiers('balanced', .8)
        self.assertLess(light['mass'], 1); self.assertGreater(light['handling'], 1)
        self.assertLess(wear, 1)
        rough, _ = crafts.modifiers('balanced', .1)
        self.assertLess(rough['handling'], 1)
        self.assertLessEqual(max(crafts.modifiers(t, 1.)[0]['damage'] for t in crafts.TEMPER_NAMES), 1.03)

    def test_forging_requires_skill_materials_and_points(self):
        self.assertIn('锻造造诣', crafts.forge_reason(self.w, self.w.player, 'tianheng_sword'))
        for i in [i for i in items.objects(self.w, 'player') if i['template'] == 'iron']:
            items.remove(self.w, i)
        self.assertIn('不足', crafts.forge_reason(self.w, self.w.player, 'iron_dagger'))
        self.iron(1)
        self.w.player['protagonist']['points'] = 0
        before = deepcopy(self.w.export())
        with self.assertRaises(ValueError):
            crafts.perform(self.w, dict(operation='forge', template='iron_dagger', temper='light'))
        self.assertEqual(self.w.export(), before)
        with self.assertRaises(ValueError):
            crafts.perform(self.w, dict(operation='forge', template='iron_dagger', temper='light', price=0))

    def test_bulk_market_iron_covers_missing_pieces(self):
        w = World(seed=7, population=30, durable=False, market_economy=True)
        self.addCleanup(w.store.close)
        protagonist.perform(w, dict(action='create', revision=0, month=0, name='来客', sex='女', age=24, place=0))
        for i in [i for i in items.objects(w, 'player') if i['template'] == 'iron']:
            items.remove(w, i)
        m = economics.market(w, economics.place_of(w, 'player'))
        m['stock']['iron'] = 5.
        self.assertEqual(crafts.forge_reason(w, w.player, 'iron_dagger'), '')
        total = economics.state(w)['consumed']['iron']
        crafts.perform(w, dict(operation='forge', template='iron_dagger', temper='balanced'))
        self.assertAlmostEqual(economics.state(w)['consumed']['iron']-total, .5)

    def test_unskilled_repair_stops_at_seventy_and_smiths_mend_fully(self):
        sword = items.create(self.w, 'player', 'iron_sword', '测试'); sword['durability'] = 10
        self.w.player['equipment']['weapon'] = sword['id']
        self.iron(1)
        items.perform(self.w, 'player', 'repair', sword['id'])
        self.assertEqual(sword['durability'], 70)
        self.assertIn('请人修补', [a['label'] for a in encounters.view(self.w, self.n['id'])['actions']])
        self.assertIn('锻造造诣', encounters.reason(self.w, self.n, 'mend'))
        self.skill('forging', 30, self.n)
        price = crafts.mend_price(sword); money = self.w.player['wealth']+self.n['wealth']
        self.act('mend')
        self.assertEqual(sword['durability'], 100)
        self.assertAlmostEqual(self.w.player['wealth']+self.n['wealth'], money)
        self.assertGreater(crafts.value(self.n, 'forging'), 30)
        self.assertGreaterEqual(price, 2)

    def test_practised_smith_forges_for_the_trading_house(self):
        n = self.n; self.skill('forging', 60, n); n['wealth'] = 80.
        before = len(items.objects(self.w, 'market')); silver = self.w.items['market']['wealth']+n['wealth']
        self.iron_for(n, 4)
        e = crafts.market_forge(self.w, n, dict(id=n['last_event']))
        self.assertIsNotNone(e)
        self.assertTrue(e['facts']['sold'])
        self.assertEqual(len(items.objects(self.w, 'market')), before+1)
        stocked = items.get(self.w, 'market', e['facts']['item_id'])
        self.assertEqual(stocked['craft']['maker_name'], n['name'])
        # Silver moves between smith and trading house; only the forge fee leaves them.
        self.assertAlmostEqual(self.w.items['market']['wealth']+n['wealth'], silver)

    def iron_for(self, n, count):
        for _ in range(count):
            items.create(self.w, n['id'], 'iron', '测试铁料')

    # --------------------------------------------------------------- medicine

    def assault_with_parts(self, parts):
        self.act('assault')
        b = self.w.battle
        packet = frame(b, 180)
        packet['fighters'][1]['health'] = 0
        packet['fighters'][0]['health'] *= .7
        packet['hits'] = 6
        for fighter in packet['fighters']:
            fighter['landed_hits'] = 3; fighter['attack_count'] = 3
        for key, (hp, severed) in parts.items():
            packet['fighters'][1]['reference']['parts'][key] = dict(health=hp, severed=severed)
        self.w.battle_action(b['id'], b['revision'], 'complete', packet)

    def test_hostile_bout_records_part_injuries_from_the_physical_frame(self):
        self.assault_with_parts({'forearmNear': (5., False), 'thighFar': (10., False), 'shinNear': (25., False),
                                 'handFar': (0., True)})
        parts = self.n['wound']['parts']
        self.assertEqual(parts['forearmNear']['kind'], 'fracture')
        self.assertEqual(parts['thighFar']['kind'], 'fracture')
        self.assertEqual(parts['shinNear']['kind'], 'cut')
        self.assertEqual(parts['handFar']['kind'], 'maimed')
        self.assertGreaterEqual(self.n['wound']['remaining'], 8)
        self.assertNotIn('parts', self.w.player['wound'] or {})
        self.assertLess(crafts.work_capacity(self.n), 1)
        self.assertGreater(crafts.travel_strain(self.n), 1)

    def test_spar_leaves_no_wound(self):
        self.act('spar')
        b = self.w.battle; packet = frame(b, 180)
        packet['fighters'][1]['health'] = 0; packet['hits'] = 3
        packet['fighters'][0]['landed_hits'] = 3; packet['fighters'][0]['attack_count'] = 3
        packet['fighters'][1]['reference']['parts']['forearmNear'] = dict(health=1., severed=False)
        self.w.battle_action(b['id'], b['revision'], 'complete', packet)
        self.assertFalse(self.n['wound'])

    def test_frame_without_parts_matches_former_wound_rule(self):
        battle = dict(cause='e1', fighters=[dict(vigor=50., vigor_max=100.), dict(vigor=95., vigor_max=100.)])
        a, b = {'wound': None}, {'wound': None}
        crafts.settle_battle(battle, (a, b))
        self.assertEqual(a['wound'], dict(remaining=2+round(.5*3), event_id='e1'))
        self.assertIsNone(b['wound'])

    def test_bones_need_a_practised_hand(self):
        wound = dict(remaining=7, event_id='e', parts={'shinNear': {'severity': .8, 'kind': 'fracture'}})
        novice = crafts.treat_parts(deepcopy(wound), 10)
        self.assertEqual(novice['outcome'], 'relieved')
        self.assertEqual(novice['after']['kind'], 'fracture')
        healed = deepcopy(wound)
        master = crafts.treat_parts(healed, 60)
        self.assertEqual(master['outcome'], 'set')
        self.assertLess(healed['remaining'], 7)
        weak = deepcopy(wound); strong = deepcopy(wound)
        crafts.treat_parts(weak, 25); crafts.treat_parts(strong, 70)
        self.assertGreater(weak['parts']['shinNear']['severity'], strong.get('parts', {}).get('shinNear', {'severity': 0})['severity'])

    def test_treat_interaction_heals_raises_affinity_and_medicine(self):
        self.n['wound'] = dict(remaining=6, event_id='e', parts={'torso': {'severity': .5, 'kind': 'cut'}})
        self.n['health'] = 50.
        self.skill('medicine', 30)
        self.act('treat')
        self.assertLess(self.n['wound'].get('parts', {}).get('torso', {'severity': 0})['severity'], .5)
        self.assertGreater(self.n['health'], 50)
        self.assertGreater(crafts.value(self.w.player, 'medicine'), 30)
        self.assertGreater(self.w.player['ties'][str(self.n['id'])]['affinity'], 0)
        self.n['wound'] = None; self.n['illness'] = None; self.n['health'] = 100.
        self.assertIn('没有', encounters.reason(self.w, self.n, 'treat'))

    def test_brewing_gates_pills_and_skilled_salve_heals_more(self):
        for _ in range(3):
            items.create(self.w, 'player', 'herb', '测试')
        with self.assertRaises(ValueError):
            items.perform(self.w, 'player', 'craft', recipe='pill')
        self.skill('medicine', 50)
        e = items.perform(self.w, 'player', 'craft', recipe='salve')
        salve = items.get(self.w, 'player', e['facts']['item_id'])
        self.assertGreater(items.heal_amount(salve), items.CATALOG['salve']['heal'])

    def test_player_wound_runs_its_course(self):
        self.w.player['wound'] = dict(remaining=2, event_id='e')
        crafts.player_month(self.w); self.assertEqual(self.w.player['wound']['remaining'], 1)
        crafts.player_month(self.w); self.assertIsNone(self.w.player['wound'])

    def test_self_treatment_uses_a_salve(self):
        self.w.player['wound'] = dict(remaining=5, event_id='e', parts={'upperFar': {'severity': .4, 'kind': 'cut'}})
        salves = sum(i['template'] == 'salve' for i in items.objects(self.w, 'player'))
        crafts.perform(self.w, dict(operation='treat_self'))
        self.assertEqual(sum(i['template'] == 'salve' for i in items.objects(self.w, 'player')), salves-1)

    # ---------------------------------------------------------------- letters

    def test_written_pieces_are_never_stocked(self):
        w = World(seed=3, population=10, durable=False)
        self.addCleanup(w.store.close)
        self.assertFalse([i for i in items.objects(w, 'market') if i['template'] in items.CRAFTED_ONLY])

    def test_learned_readers_value_a_gift_in_ones_own_hand(self):
        self.skill('letters', 40)
        gains = []
        for job, learning, tradition in (('农人', 0, 30), ('书生', 60, 80)):
            n = self.w.make_npc(place=45)
            n.update(age_months=360, health=100., wealth=50., job=job)
            n['career'].update(profession=job, experience={job: 0., '书生': learning})
            n['beliefs']['tradition'] = tradition
            self.w.reindex()
            self.w.player['protagonist']['points'] = 30
            piece = crafts.perform(self.w, dict(operation='compose', title='松风'))['event']['facts']['item_id']
            item = items.get(self.w, 'player', piece)
            self.assertEqual(items.name(item)[:4], '《松风》')
            e = items.perform(self.w, 'player', 'gift', piece, n['id'])
            gains.append(e['facts']['affinity_gain'])
            self.assertTrue(e['facts']['own_hand'])
        self.assertGreater(gains[1], gains[0])

    def test_titles_are_bounded(self):
        for title in ('', 'x'*13, 3):
            with self.assertRaises(ValueError):
                crafts.clean_title(title)
        self.assertEqual(crafts.clean_title(' 《秋水》 '), '秋水')

    def test_learn_craft_from_a_better_teacher(self):
        self.assertIn('没有', encounters.reason(self.w, self.n, 'learn_craft'))
        self.skill('forging', 50, self.n)
        money = self.w.player['wealth']
        self.act('learn_craft')
        self.assertAlmostEqual(crafts.value(self.w.player, 'forging'), 2+50*.03)
        self.assertEqual(self.w.player['wealth'], money-2)

    # ------------------------------------------------------------------- http

    def test_http_view_and_forge(self):
        from jianghu.server import handler_for
        self.skill('forging', 20); self.iron(2)
        game = SimpleNamespace(world=self.w, lock=threading.RLock(), autoplay=False, job=dict(remaining=0))
        server = ThreadingHTTPServer(('127.0.0.1', 0), handler_for(game))
        thread = threading.Thread(target=server.serve_forever, daemon=True); thread.start()
        root = f'http://127.0.0.1:{server.server_port}'
        try:
            with urlopen(root+'/api/crafts', timeout=10) as response:
                view = json.load(response)
            self.assertEqual([c['id'] for c in view['crafts']], list(crafts.KEYS))
            row = next(r for r in view['forge']['templates'] if r['template'] == 'iron_dagger')
            self.assertEqual(row['reason'], '')
            body = json.dumps(dict(operation='forge', template='iron_dagger', temper='light')).encode()
            with urlopen(Request(root+'/api/crafts', data=body, headers={'Content-Type': 'application/json'}), timeout=10) as r:
                self.assertEqual(json.load(r)['event']['kind'], 'craft_forge')
            game.autoplay = True
            with self.assertRaises(HTTPError):
                urlopen(Request(root+'/api/crafts', data=body, headers={'Content-Type': 'application/json'}), timeout=10)
        finally:
            server.shutdown(); server.server_close()


if __name__ == '__main__':
    unittest.main()
