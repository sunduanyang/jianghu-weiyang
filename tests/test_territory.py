"""Control changes must affect gameplay, evidence, reloads and isolation together."""
from collections import Counter
from copy import deepcopy
from http.server import ThreadingHTTPServer
import json
from pathlib import Path
import threading
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import urllib.error
import urllib.request

from jianghu import government, politics, territory
from jianghu.atlas import COUNTRIES, FACTIONS, PLACES, ROUTES, MAP_VERSION
from jianghu.engine import World
from jianghu.server import handler_for
from jianghu.store import Store

SITES = {p['name']: p['id'] for p in PLACES}


class TerritoryTests(unittest.TestCase):
    def setUp(self):
        self.w = World(population=0, durable=False)
        self.addCleanup(self.w.store.close)
        self.target = SITES['对马府中']

    def win(self, attacker=10, target=None):
        target = self.target if target is None else target
        self.w.territory[target]['guard'] = 1.
        with patch.object(self.w.rng, 'uniform', return_value=1.):
            return territory.campaign(self.w, attacker, target, declare=True)

    def test_balanced_polities_stable_legacy_ids_and_real_sea_routes(self):
        counts = Counter(p['country'] for p in PLACES)
        self.assertTrue(all(counts[c['id']] >= 4 for c in COUNTRIES))
        self.assertEqual((PLACES[0]['name'], PLACES[57]['name']), ('青溪镇', '虎思斡耳朵'))
        self.assertEqual(FACTIONS[24]['name'], '琼海船帮')
        self.assertEqual(counts[10], 12)
        self.assertEqual(counts[11], 23)
        for a, b in [('明州', '博多津'), ('金州', '对马府中'), ('金州', '耽罗'), ('雷州', '琼州')]:
            r = territory.ROUTE_INDEX[tuple(sorted((SITES[a], SITES[b])))]
            self.assertTrue(r['sea'])
        self.assertTrue(all(r['sea'] for r in ROUTES if
                            (PLACES[r['a']]['country'] == 11) != (PLACES[r['b']]['country'] == 11)))

    def test_every_site_has_a_bundled_control_cell(self):
        root = Path(__file__).resolve().parents[1]
        data = json.loads((root/'static/geography.json').read_text(encoding='utf-8'))
        self.assertEqual(data['map_version'], MAP_VERSION)
        self.assertEqual({c['place'] for c in data['cells']}, set(range(len(PLACES))))
        for cell in data['cells']:
            self.assertEqual(cell['native_country'], PLACES[cell['place']]['country'])
            self.assertNotIn('nan', cell['path'].lower())
            self.assertNotIn('inf', cell['path'].lower())
        self.assertGreater(data['viewBox'][2], max(p['x'] for p in PLACES if p['country'] == 11))

    def test_capture_changes_tax_toll_organization_and_official_identity(self):
        w = self.w
        n = w.make_npc(place=self.target)
        n.update(age_months=360, office='县令', office_country=11, wealth=100.)
        w.set_goal(n, w.best_goal(n), None, initial=True)
        w.governments[11]['offices']['县令'] = n['id']
        w.reindex()
        n['faction'] = next(f['id'] for f in w.factions if f['place'] == self.target)
        before_fund = w.governments[10]['fund']
        event = self.win()
        self.assertTrue(event['facts']['sea'])
        self.assertTrue(event['facts']['captured'])
        self.assertEqual(territory.owner(w, self.target), 10)
        self.assertIsNone(n['office'])
        self.assertIsNone(w.governments[11]['offices']['县令'])
        self.assertEqual(w.factions[n['faction']]['country'], 10)
        self.assertEqual(w.factions[n['faction']]['name'], '对马船众')
        trip = politics.journey(w, SITES['金州'], self.target)
        self.assertEqual((trip['border'], trip['toll']), ('境内', 0))
        self.assertAlmostEqual(before_fund-w.governments[10]['fund'], event['facts']['cost'])
        original = deepcopy(w.governments[11])
        government.tax(w, n, 100)
        self.assertEqual(w.governments[11], original)
        self.assertIs(government.for_npc(w, n), w.governments[10])
        for g in w.governments:
            self.assertAlmostEqual(g['fund'], g['opening_fund']+g['revenue']-g['expenses'])
        summary = w.summary()
        self.assertEqual(summary['places'][self.target]['color'], w.countries[10]['color'])
        self.assertEqual(summary['countries'][10]['settlements'], 13)
        self.assertEqual(summary['countries'][11]['settlements'], 22)

    def test_defeat_costs_silver_and_preserves_owner(self):
        self.w.territory[self.target]['guard'] = 1000.
        fund = self.w.governments[10]['fund']
        event = territory.campaign(self.w, 10, self.target, declare=True)
        self.assertEqual(event['kind'], 'siege_repulsed')
        self.assertEqual(territory.owner(self.w, self.target), 11)
        self.assertAlmostEqual(fund-self.w.governments[10]['fund'], event['facts']['cost'])
        self.assertLess(self.w.territory[self.target]['stability'], 100)

    def test_invalid_campaigns_are_atomic_including_rng_and_diplomacy(self):
        for attacker, target in [(0, SITES['镰仓']), (11, self.target), (-1, self.target),
                                 (True, self.target), (10, -1), ('10', self.target)]:
            before = deepcopy(self.w.export())
            count = self.w.store.count()
            with self.assertRaises(ValueError):
                territory.campaign(self.w, attacker, target, declare=True)
            self.assertEqual(self.w.export(), before)
            self.assertEqual(self.w.store.count(), count)
        self.w.governments[10]['fund'] = 0
        before = deepcopy(self.w.export())
        with self.assertRaises(ValueError):
            territory.campaign(self.w, 10, self.target, declare=True)
        self.assertEqual(self.w.export(), before)

    def test_cooldown_and_recapture_restore_native_map_color(self):
        first = self.win()
        before = deepcopy(self.w.export())
        with self.assertRaises(ValueError):
            territory.campaign(self.w, 11, self.target, declare=True)
        self.assertEqual(self.w.export(), before)
        self.w.month += 6
        event = self.win(11)
        self.assertTrue(event['facts']['captured'])
        view = self.w.summary_place(self.target)
        self.assertFalse(view['occupied'])
        self.assertEqual(view['country'], view['native_country'])
        self.assertEqual(view['color'], COUNTRIES[11]['color'])
        self.assertIn(first['id'], event['causes'])

    def test_capture_is_world_local_and_survives_reload_and_continuation(self):
        self.win()
        untouched = World(population=0, durable=False)
        self.addCleanup(untouched.store.close)
        self.assertEqual(territory.owner(untouched, self.target), 11)
        self.assertEqual(PLACES[self.target]['country'], 11)
        self.w.checkpoint()
        store = Store()
        self.addCleanup(store.close)
        self.w.store.db.backup(store.db)
        restored = World(store=store, durable=False)
        self.assertEqual(restored.territory, self.w.territory)
        self.assertEqual(restored.campaigns, self.w.campaigns)
        for _ in range(6):
            self.w.month_step()
            restored.month_step()
        self.assertEqual(restored.territory, self.w.territory)
        self.assertEqual(restored.governments, self.w.governments)
        self.assertEqual(restored.rng.getstate(), self.w.rng.getstate())

    def test_eliminated_country_annual_has_no_empty_location_crash(self):
        for t in self.w.territory:
            if t['owner'] == 8:
                t['owner'] = 0
        government.annual(self.w, self.w.governments[8])
        self.w.annual()
        self.assertEqual(self.w.summary()['countries'][8]['settlements'], 0)

    def test_v3_migration_preserves_old_records_and_initializes_new_countries_once(self):
        w = self.w
        n = w.make_npc(place=2)
        w.reindex()
        state = deepcopy(w.export())
        state.update(map_version=3, countries=state['countries'][:10], governments=state['governments'][:10],
                     factions=state['factions'][:25])
        for key in ('territory', 'campaigns'):
            state.pop(key)
        before_npc, before_g = deepcopy(n), deepcopy(state['governments'])
        w.store.save(state)
        migrated = World(store=w.store, durable=False)
        self.assertEqual(migrated.npcs[n['id']], before_npc)
        self.assertEqual(migrated.governments[:10], before_g)
        self.assertEqual(len(migrated.atlas_history), 1)
        self.assertEqual(sum(g['fund'] for g in migrated.governments)-sum(g['fund'] for g in before_g), 160.)
        event = migrated.store.history(limit=1)[0]
        self.assertEqual(event['facts']['new_government_opening_funds'], 160)
        self.assertEqual(event['facts']['new_faction_opening_funds'], 900)
        count = w.store.count()
        again = World(store=w.store, durable=False)
        self.assertEqual(w.store.count(), count)
        self.assertEqual(again.atlas_history, migrated.atlas_history)

    def test_quarterly_hostile_campaigns_are_bounded_and_recorded(self):
        self.w.month = 3
        for t in self.w.territory:
            t['guard'] = 30.
        with patch.object(self.w.rng, 'random', return_value=0):
            territory.monthly(self.w)
        history = self.w.campaigns['history']
        self.assertTrue(0 < len(history) <= 2)
        self.assertTrue(all({e['attacker'],e['defender']} == {0,1} for e in history))


class TerritoryHTTPTests(unittest.TestCase):
    def setUp(self):
        self.w = World(population=0, durable=False)
        self.game = SimpleNamespace(world=self.w, lock=threading.RLock(), autoplay=False, job={'remaining': 0})
        self.server = ThreadingHTTPServer(('127.0.0.1', 0), handler_for(self.game))
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.base = f'http://127.0.0.1:{self.server.server_port}'

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=3)
        self.w.store.close()

    def request(self, path, body=None, origin=None):
        headers = {'Content-Type':'application/json'}
        if origin:
            headers['Origin'] = origin
        req = urllib.request.Request(self.base+path, json.dumps(body).encode() if body is not None else None, headers)
        with urllib.request.urlopen(req, timeout=5) as r:
            return json.load(r)

    def test_campaign_api_returns_eligible_routes_and_persists_result(self):
        target = SITES['对马府中']
        options = self.request('/api/campaign?place='+str(target))['options']
        self.assertTrue(any(o['attacker']==10 and o['sea'] and o['ready'] for o in options))
        self.w.territory[target]['guard'] = 1.
        event = self.request('/api/campaign', {'attacker':10, 'place':target})
        self.assertTrue(event['facts']['captured'])
        self.assertEqual(self.w.store.load()['territory'][target]['owner'], 10)

    def test_running_battle_and_cross_origin_requests_cannot_change_control(self):
        for mode in ('running', 'battle', 'origin'):
            self.game.autoplay = mode == 'running'
            self.w.battle = {'status':'active'} if mode == 'battle' else None
            before = deepcopy(self.w.export())
            with self.assertRaises(urllib.error.HTTPError) as error:
                self.request('/api/campaign', {'attacker':10,'place':SITES['对马府中']},
                             'http://example.invalid' if mode == 'origin' else None)
            self.assertEqual(error.exception.code, 403 if mode == 'origin' else 400)
            self.assertEqual(self.w.export(), before)
