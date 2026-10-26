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

from jianghu import city_maps, territory
from jianghu.atlas import PLACES, FACTIONS
from jianghu.engine import World
from jianghu.server import handler_for
from jianghu.store import Store

SITES = {p['name']: p['id'] for p in PLACES}


class CityMapTests(unittest.TestCase):
    def world(self, population=0):
        w = World(population=population, durable=False)
        self.addCleanup(w.store.close)
        return w

    def adult(self,w,place):
        n = w.make_npc(place=place)
        n.update(age_months=360,wealth=1000.,health=100.,office=None)
        w.set_goal(n,w.best_goal(n),None,initial=True)
        w.reindex()
        return n

    def test_all_168_interiors_have_unique_reachable_facilities_and_safe_paths(self):
        for seed in (0,42,99):
            for p in PLACES:
                city = city_maps.layout(seed,p['id'])
                self.assertTrue(all(len(row)==city['size'] for row in city['rows']))
                self.assertGreaterEqual(len(city['nodes']),10)
                self.assertEqual(len({n['id'] for n in city['nodes']}),len(city['nodes']))
                self.assertEqual(len({(n['row'],n['col']) for n in city['nodes']}),len(city['nodes']))
                start = city['nodes'][0]
                occupied = set()
                for node in city['nodes']:
                    lot = node['footprint']
                    self.assertIn(lot['size'], (1, 2))
                    cells = {(lot['row']+r, lot['col']+c)
                             for r in range(lot['size']) for c in range(lot['size'])}
                    self.assertIn((node['row'], node['col']), cells)
                    self.assertFalse(cells & occupied, (seed, p['name'], node['name']))
                    self.assertTrue(all(0 <= r < city['size'] and 0 <= c < city['size'] for r,c in cells))
                    if lot['size'] == 2:
                        self.assertTrue(all(city['rows'][r][c] not in '~^#=' for r,c in cells))
                    occupied.update(cells)
                    path,_ = city_maps.path_between(city['rows'],(start['row'],start['col']),
                                                   (node['row'],node['col']))
                    self.assertTrue(path,(seed,p['name'],node['name']))
                    self.assertTrue(all(city['rows'][r][c] not in '~^#' for r,c in path))
                    self.assertTrue(all(abs(a[0]-b[0])+abs(a[1]-b[1])==1 for a,b in zip(path,path[1:])))

    def test_authored_anchors_are_fixed_and_all_organizations_have_a_home(self):
        self.assertEqual(len(city_maps.PLANS),25)
        for name,profile in city_maps.PLANS.items():
            city = city_maps.layout(42,SITES[name])
            other = city_maps.layout(999,SITES[name])
            self.assertEqual(city,other)
            for kind,title,r,c in profile['anchors']:
                node = next(n for n in city['nodes'] if n['id']==kind)
                self.assertEqual((node['name'],node['row'],node['col']),(title,r,c))
        for f in FACTIONS:
            city = city_maps.layout(42,f['place'])
            self.assertTrue(any(n.get('faction')==f['id'] for n in city['nodes']))

    def test_generated_maps_are_seed_stable_and_cached_results_cannot_be_mutated(self):
        place = SITES['义州']
        a = city_maps.layout(42,place)
        self.assertFalse(a['authored'])
        self.assertEqual(a,city_maps.layout(42,place))
        self.assertNotEqual(a['layout_id'],city_maps.layout(43,place)['layout_id'])
        a['nodes'][0]['name'] = '污染'
        a['rows'][0] = 'bad'
        self.assertNotEqual(a,city_maps.layout(42,place))

    def test_browsing_all_maps_does_not_change_world_rng_assets_or_event_ledger(self):
        w = self.world(30)
        before = deepcopy(w.export())
        count = w.store.count()
        for p in PLACES:
            view = city_maps.view(w,p['id'])
            self.assertEqual(sum(len(n['residents']) for n in view['nodes']),len(w.residents[p['id']]))
        self.assertEqual(w.export(),before)
        self.assertEqual(w.store.count(),count)

    def test_real_work_and_rest_record_venues_and_existing_itinerary_costs(self):
        w = self.world()
        n = self.adult(w,SITES['临安府'])
        n['job'] = '匠人'
        w.execute(n,{'id':'work:self'})
        pos = w.city_positions[str(n['id'])]
        self.assertEqual(pos['node'],'workshop')
        self.assertEqual(pos['event_id'],n['last_action']['event_id'])
        self.assertEqual(n['time_left'],2)
        w.execute(n,{'id':'rest:self'})
        self.assertEqual(w.city_positions[str(n['id'])]['node'],'residence')
        self.assertEqual(n['time_left'],1)
        city = city_maps.view(w,n['place'])
        resident = next(n for node in city['nodes'] for n in node['residents'])
        self.assertEqual(resident['source'],'行动落点')

    def test_travel_places_actor_at_new_gate_and_stale_positions_do_not_leak(self):
        w = self.world()
        n = self.adult(w,0)
        action = next(a for a in w.legal_actions(n) if a['kind']=='travel')
        w.execute(n,action)
        self.assertEqual(w.city_positions[str(n['id'])]['place'],n['place'])
        self.assertEqual(w.city_positions[str(n['id'])]['node'],'gate')
        self.assertFalse(any(p['id']==n['id'] for node in city_maps.view(w,0)['nodes'] for p in node['residents']))
        n['place'] = 0
        w.reindex()
        view = city_maps.view(w,0)
        home = next(node for node in view['nodes'] if node['id']=='residence')
        self.assertEqual(home['residents'][0]['source'],'乡籍居所')
        self.assertIsNone(home['residents'][0]['event_id'])

    def test_conquest_changes_government_but_preserves_streets_and_culture(self):
        w = self.world()
        target = SITES['对马府中']
        before = city_maps.view(w,target)
        w.territory[target]['guard'] = 1
        with patch.object(w.rng,'uniform',return_value=1):
            territory.campaign(w,10,target,declare=True)
        after = city_maps.view(w,target)
        self.assertEqual(before['layout_id'],after['layout_id'])
        self.assertEqual(before['rows'],after['rows'])
        self.assertEqual(after['country_name'],'高丽')
        self.assertTrue(after['occupied'])
        self.assertEqual(next(n for n in after['nodes'] if n['kind']=='government')['name'],'高丽驻署')
        self.assertTrue(any(n['name']=='对马船众' for n in after['nodes']))

    def test_old_save_adds_no_invented_activity_and_continuation_is_reproducible(self):
        w = self.world(30)
        before = deepcopy(w.export())
        before.pop('city_map_version');before.pop('city_positions')
        w.store.save(before)
        restored = World(store=w.store,durable=False)
        self.assertEqual(restored.npcs,before['npcs'])
        self.assertEqual(restored.city_positions,{})
        self.assertEqual(restored.rng.getstate(),before['random_state'])
        restored.month_step();restored.checkpoint()
        store = Store();self.addCleanup(store.close)
        restored.store.db.backup(store.db)
        again = World(store=store,durable=False)
        self.assertEqual(again.city_positions,restored.city_positions)
        again.month_step();restored.month_step()
        self.assertEqual(again.city_positions,restored.city_positions)
        self.assertEqual(again.npcs,restored.npcs)
        self.assertEqual(again.rng.getstate(),restored.rng.getstate())

    def test_invalid_place_or_version_does_not_alias_another_map(self):
        for place in (-1,168,'0',True):
            with self.assertRaises(ValueError):city_maps.layout(42,place)
        with self.assertRaises(ValueError):city_maps.layout(42,0,999)


class CityHTTPTests(unittest.TestCase):
    def setUp(self):
        self.w = World(population=0,durable=False)
        self.server = ThreadingHTTPServer(('127.0.0.1',0),handler_for(SimpleNamespace(world=self.w,lock=threading.RLock())))
        self.thread = threading.Thread(target=self.server.serve_forever,daemon=True);self.thread.start()
        self.base = f'http://127.0.0.1:{self.server.server_port}'

    def tearDown(self):
        self.server.shutdown();self.server.server_close();self.thread.join(timeout=3);self.w.store.close()

    def test_city_api_is_read_only_and_assets_are_bundled(self):
        before = deepcopy(self.w.export())
        with urllib.request.urlopen(self.base+'/api/city?place=45') as r:city=json.load(r)
        self.assertEqual(city['name'],'临安府')
        self.assertTrue(city['authored'])
        for asset in ('city.js','city.css'):
            with urllib.request.urlopen(self.base+'/'+asset) as r:self.assertEqual(r.status,200)
        self.assertEqual(self.w.export(),before)

    def test_city_api_rejects_missing_unknown_or_bad_place(self):
        for query in ('','?place=-1','?place=168','?place=abc'):
            with self.assertRaises(urllib.error.HTTPError) as e:urllib.request.urlopen(self.base+'/api/city'+query)
            self.assertEqual(e.exception.code,400)
