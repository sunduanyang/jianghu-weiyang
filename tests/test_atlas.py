"""Eight regions: one city, one 府衙 and one sect each; no dynasties, borders or conquest."""
from copy import deepcopy
from http.server import ThreadingHTTPServer
import json
from pathlib import Path
import re
import tempfile
import threading
from types import SimpleNamespace
import unittest
import urllib.error
import urllib.request

from tests.combat_helpers import update_actor
from jianghu import government, politics, territory
from jianghu.atlas import COUNTRIES, FACTIONS, MAP_VERSION, NEIGHBORS, PLACES, ROUTES, project, unproject
from jianghu.engine import LegacyWorldError, World
from jianghu.server import Game, handler_for
from jianghu.store import Store

ROOT = Path(__file__).resolve().parents[1]
CITIES = ('洛阳', '长安', '襄阳', '成都', '苏州', '大理', '泉州', '凉州')
REGIONS = ('中原', '关中', '荆楚', '巴蜀', '江南', '滇黔', '闽浙', '西北')


def contains(path, point):
    x, y = point
    for section in re.split('[Mm]', path):
        numbers = [float(n) for n in re.findall(r'-?\d+(?:\.\d+)?', section)]
        ring = list(zip(numbers[::2], numbers[1::2]))
        hit = False
        for a, b in zip(ring, ring[1:]+ring[:1]):
            if (a[1] > y) != (b[1] > y) and x < (b[0]-a[0])*(y-a[1])/(b[1]-a[1])+a[0]:
                hit = not hit
        if hit:
            return True
    return False


class AtlasTests(unittest.TestCase):
    def world(self, **kwargs):
        w = World(population=0, durable=False, **kwargs)
        self.addCleanup(w.store.close)
        return w

    def adult(self, w, place):
        n = w.make_npc(place=place)
        update_actor(n, age_months=360, wealth=1000., health=100., office=None)
        w.set_goal(n, w.best_goal(n), None, initial=True)
        w.reindex()
        return n

    def test_eight_regions_each_hold_one_city_and_one_sect(self):
        self.assertEqual(tuple(p['name'] for p in PLACES), CITIES)
        self.assertEqual(tuple(c['name'] for c in COUNTRIES), REGIONS)
        self.assertEqual([p['country'] for p in PLACES], list(range(8)))
        self.assertEqual(sorted(f['place'] for f in FACTIONS), list(range(8)))
        for c in COUNTRIES:
            with self.subTest(region=c['name']):
                self.assertEqual(PLACES[c['id']]['name'], c['capital'])
                self.assertTrue(c['government_name'].startswith(c['capital']))

    def test_routes_are_connected_overland_and_projection_round_trips(self):
        self.assertTrue(ROUTES and not any(r['sea'] for r in ROUTES))
        reached, pending = {0}, [0]
        while pending:
            for neighbor in NEIGHBORS[pending.pop()]:
                if neighbor not in reached:
                    reached.add(neighbor)
                    pending.append(neighbor)
        self.assertEqual(reached, set(range(len(PLACES))))
        for p in PLACES:
            self.assertTrue(36 < p['x'] < 1244 and 26 < p['y'] < 734, p['name'])
            lon, lat = unproject(p['x'], p['y'])
            self.assertAlmostEqual(lon, p['lon'], places=1)
            self.assertAlmostEqual(lat, p['lat'], places=1)
        self.assertEqual(project(p['lon'], p['lat']), (p['x'], p['y']))

    def test_bundled_geography_has_one_washed_cell_per_city(self):
        from PIL import Image
        for relative in ('art/world-map/geography.json', 'art/world-map/geography-compact.json',
                         'unity/JianghuUnity/Assets/Resources/geography.json'):
            data = json.loads((ROOT/relative).read_text(encoding='utf-8'))
            with self.subTest(file=relative):
                self.assertEqual(data['map_version'], MAP_VERSION)
                self.assertNotIn('frontier', data)
                self.assertEqual(sorted(c['place'] for c in data['cells']), list(range(8)))
                for p in PLACES:
                    owners = {c['place'] for c in data['cells'] if contains(c['path'], (p['x'], p['y']))}
                    self.assertEqual(owners, {p['id']}, p['name'])
        compact = json.loads((ROOT/'art/world-map/geography-compact.json').read_text(encoding='utf-8'))
        self.assertEqual({(r['a'], r['b']) for r in compact['roads']}, {(r['a'], r['b']) for r in ROUTES})
        masks = ROOT/'unity/JianghuUnity/Assets/Resources/Map'
        self.assertEqual(sorted(f.name for f in masks.glob('cell-*.png')), sorted(f'cell-{i}.png' for i in range(8)))
        for p in PLACES:
            with Image.open(masks/f"cell-{p['id']}.png") as cell:
                self.assertGreater(cell.getchannel('A').getpixel((round(p['x']), round(p['y']))), 0, p['name'])

    def test_travel_has_no_border_toll_and_no_campaigns(self):
        w = self.world()
        r = ROUTES[0]
        n = self.adult(w, r['a'])
        action = next(a for a in w.legal_actions(n) if a['id'] == f"travel:{r['b']}")
        funds = [g['fund'] for g in w.governments]
        before = n['wealth']
        event = w.execute(n, action)
        self.assertEqual(n['place'], r['b'])
        self.assertEqual(event['facts']['toll'], 0.)
        self.assertAlmostEqual(before-n['wealth'], politics.journey(w, r['a'], r['b'])['fare'])
        self.assertEqual([g['fund'] for g in w.governments], funds)
        self.assertNotIn('关税', event['summary'])
        self.assertEqual(territory.attack_options(w, r['b']), [])
        with self.assertRaises(ValueError):
            territory.campaign(w, 0, r['b'], declare=True)
        self.assertNotIn('campaigns', w.export())
        self.assertNotIn('campaigns', w.summary())

    def test_each_region_keeps_its_own_government_through_the_years(self):
        w = self.world()
        for _ in range(13):
            w.month_step()
        self.assertEqual([t['owner'] for t in w.territory], list(range(8)))
        self.assertTrue(all(c['relations'] == {} for c in w.countries))
        self.assertEqual([g['name'] for g in w.governments], [c['government_name'] for c in COUNTRIES])

    def test_tax_and_relief_balance_inside_one_regional_treasury(self):
        w = self.world()
        n = self.adult(w, 6)
        g = w.local_government(n)
        self.assertEqual(g['country'], 6)
        others = [deepcopy(x) for x in w.governments if x is not g]
        total = n['wealth'] + g['fund']
        government.tax(w, n, 100)
        government.grant(w, n, 200, '救济')
        self.assertAlmostEqual(n['wealth']+g['fund'], total)
        self.assertAlmostEqual(g['fund'], g['opening_fund']+g['revenue']-g['expenses'])
        self.assertEqual(others, [x for x in w.governments if x is not g])

    def test_crime_reports_cannot_be_repeated_at_another_yamen(self):
        w = self.world()
        a, b = self.adult(w, 2), self.adult(w, 2)
        e = w.emit('betrayal', [a['id'], b['id']], '已知侵吞。', .95,
                   facts={'victim': a['id'], 'offender': b['id'], 'transfer': 10})
        government.report(w, a, e['id'])
        a['place'] = 3
        w.reindex()
        with self.assertRaises(ValueError):
            government.report(w, a, e['id'])

    def test_joining_requires_the_sects_own_city(self):
        w = self.world()
        n = self.adult(w, 7)
        elsewhere = next(f for f in w.factions if f['place'] != 7)
        home = next(f for f in w.factions if f['place'] == 7)
        self.assertIsNone(w.execute(n, {'id': f"join:{elsewhere['id']}"}))
        self.assertIsNotNone(w.execute(n, {'id': f"join:{home['id']}"}))
        self.assertEqual(n['faction'], home['id'])

    def test_legacy_world_save_is_refused_and_left_untouched(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp)/'world.sqlite'
            store = Store(path)
            store.save({'seed': 1, 'month': 3, 'map_version': 4, 'npcs': {}, 'player': {}})
            store.close()
            before = path.read_bytes()
            with self.assertRaises(LegacyWorldError) as caught:
                World(store=Store(path), compact=True)
            self.assertEqual(caught.exception.version, 4)
            self.assertIn('另起新世界', str(caught.exception))
            self.assertEqual(path.read_bytes(), before)

    def test_server_starts_a_new_world_beside_a_legacy_save(self):
        from jianghu import server
        with tempfile.TemporaryDirectory() as tmp:
            old_root = server.ROOT
            server.ROOT = Path(tmp)
            try:
                path = Path(tmp)/'data'/'world.sqlite'
                store = Store(path)
                store.save({'seed': 1, 'month': 3, 'map_version': 4, 'npcs': {}, 'player': {}})
                store.close()
                game = Game(path, population=40)
                try:
                    self.assertEqual(game.legacy_save['path'], str(path))
                    self.assertNotEqual(game.path, path)
                    self.assertEqual((Path(tmp)/'data'/'active_world.txt').read_text(encoding='utf-8'), str(game.path))
                    self.assertEqual(len(game.world.active_places), 8)
                    self.assertEqual(game.state()['legacy_save']['map_version'], 4)
                finally:
                    game.closed = True
                    game.thread.join(timeout=3)
                    game.ai.close()
                    game.world.store.close()
                self.assertEqual(Store(path).load()['map_version'], 4)
            finally:
                server.ROOT = old_root


class CampaignHTTPTests(unittest.TestCase):
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

    def test_campaign_endpoint_is_gone(self):
        before = deepcopy(self.w.export())
        for body in (None, {'attacker': 0, 'place': 1}):
            req = urllib.request.Request(self.base+'/api/campaign?place=1',
                                         json.dumps(body).encode() if body else None,
                                         {'Content-Type': 'application/json'})
            with self.assertRaises(urllib.error.HTTPError) as error:
                urllib.request.urlopen(req, timeout=5)
            self.assertEqual(error.exception.code, 404)
        self.assertEqual(self.w.export(), before)


if __name__ == '__main__':
    unittest.main()
