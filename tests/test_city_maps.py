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

from jianghu import city_maps, city_biomes, city_gardens, territory
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
                self.assertEqual(len(city['settlements']),2+sum(f['place']==p['id'] for f in FACTIONS))
                self.assertEqual(sorted(n['footprint']['size'] for n in city['settlements']),[2,3]+[3]*sum(f['place']==p['id'] for f in FACTIONS))
                start = city['nodes'][0]
                occupied = set()
                for node in city['settlements']:
                    lot = node['footprint']
                    self.assertIn(lot['size'], (1, 2, 3))
                    cells = {(lot['row']+r, lot['col']+c)
                             for r in range(lot['size']) for c in range(lot['size'])}
                    self.assertIn((node['row'], node['col']), cells)
                    self.assertFalse(cells & occupied, (seed, p['name'], node['name']))
                    self.assertTrue(all(0 <= r < city['size'] and 0 <= c < city['size'] for r,c in cells))
                    if lot['size'] > 1:
                        self.assertTrue(all(city['rows'][r][c] not in '~^#=' for r,c in cells))
                    occupied.update(cells)
                    path,_ = city_maps.path_between(city['rows'],(start['row'],start['col']),
                                                   (node['row'],node['col']))
                    self.assertTrue(path,(seed,p['name'],node['name']))
                    self.assertTrue(all(city['rows'][r][c] not in '~^#' for r,c in path))
                    self.assertTrue(all(abs(a[0]-b[0])+abs(a[1]-b[1])==1 for a,b in zip(path,path[1:])))

    def test_named_landmarks_survive_seeded_landscapes_and_organizations_have_homes(self):
        self.assertEqual(len(city_maps.PLANS),25)
        for name,profile in city_maps.PLANS.items():
            city = city_maps.layout(42,SITES[name])
            other = city_maps.layout(999,SITES[name])
            self.assertNotEqual(city['layout_id'],other['layout_id'])
            for kind,title,r,c in profile['anchors']:
                node = next(n for n in city['nodes'] if n['id']==kind)
                self.assertEqual(node['name'],title)
                self.assertEqual(next(n['name'] for n in other['nodes'] if n['id']==kind),title)
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

    def test_new_terrain_covers_every_cell_once_and_respects_asset_footprints(self):
        sizes = set()
        for p in PLACES:
            city = city_maps.layout(42,p['id'])
            sizes.add(city['size'])
            self.assertEqual(city['size'],city_biomes.size_for(p)[0])
            self.assertEqual(city['art_pack'],city_gardens.PACK)
            used = set()
            lots = {(n['footprint']['row']+r,n['footprint']['col']+c)
                    for n in city['nodes'] for r in range(n['footprint']['size'])
                    for c in range(n['footprint']['size'])}
            for tile in city['terrain_tiles']:
                self.assertIn(tile['asset'],city_gardens.SELECTED)
                self.assertEqual(tile['size'],city_gardens.FOOTPRINTS[tile['asset']])
                cells = {(tile['row']+r,tile['col']+c) for r in range(tile['size']) for c in range(tile['size'])}
                self.assertFalse(used & cells)
                self.assertTrue(all(0<=r<city['size'] and 0<=c<city['size'] for r,c in cells))
                if tile['size']==2:
                    self.assertFalse(cells & lots)
                    self.assertEqual(len({city['rows'][r][c] for r,c in cells}),1)
                    self.assertNotIn(city['rows'][tile['row']][tile['col']],'+=#')
                used.update(cells)
            self.assertEqual(len(used),city['size']**2)
        self.assertEqual(sizes,{20,30})

    def test_legacy_save_moves_hero_to_same_facility_without_spending_time(self):
        w = self.world()
        state = deepcopy(w.export())
        old = city_maps.layout(w.seed,SITES['临安府'],1)
        market = next(n for n in old['nodes'] if n['id']=='market')
        state['city_map_version'] = 1
        state['player']['protagonist'].update(created=True,place=old['place'],row=market['row'],col=market['col'])
        before = deepcopy(state['player']['protagonist'])
        w.store.save(state)
        events = w.store.count()
        restored = World(store=w.store,durable=False)
        self.assertEqual(restored.city_map_version,city_maps.VERSION)
        new = city_maps.world_layout(restored,old['place'])
        market = next(n for n in new['nodes'] if n['id']=='market')
        hero = restored.player['protagonist']
        self.assertEqual((hero['row'],hero['col']),(market['row'],market['col']))
        self.assertEqual(hero['points'],before['points'])
        self.assertEqual(hero['records'],before['records'])
        self.assertEqual(w.store.count(),events)
        self.assertEqual(restored.rng.getstate(),state['random_state'])

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
        self.assertEqual([(n['id'],n['name']) for n in before['nodes'] if n['kind']=='faction'], [(n['id'],n['name']) for n in after['nodes'] if n['kind']=='faction'])

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

    def test_cached_gate_tree_matches_individual_paths_and_views_are_independent(self):
        for compact in (False,True):
            w=World(population=0,durable=False,compact=compact)
            self.addCleanup(w.store.close)
            for name in ('临安府','海东聚落','克鲁伦河营地'):
                city=city_maps.view(w,SITES[name])
                gate=next(n for n in city['nodes'] if n['id']==city['entrance'])
                start=(gate['row'],gate['col'])
                for node in city['nodes']:
                    path,cost=city_maps.path_between(city['rows'],start,(node['row'],node['col']))
                    self.assertEqual((node['path'],node['walk_cost']),(path,cost))
                expected=deepcopy(city)
                city['nodes'][-1]['path'].clear()
                city['nodes'][-1]['walk_cost']=-1
                self.assertEqual(city_maps.view(w,SITES[name]),expected)


class CityHTTPTests(unittest.TestCase):
    def setUp(self):
        self.w = World(population=0,durable=False)
        self.server = ThreadingHTTPServer(('127.0.0.1',0),handler_for(SimpleNamespace(world=self.w,lock=threading.RLock())))
        self.thread = threading.Thread(target=self.server.serve_forever,daemon=True);self.thread.start()
        self.base = f'http://127.0.0.1:{self.server.server_port}'

    def tearDown(self):
        self.server.shutdown();self.server.server_close();self.thread.join(timeout=3);self.w.store.close()

    def test_city_api_is_read_only_and_web_game_is_unavailable(self):
        before = deepcopy(self.w.export())
        with urllib.request.urlopen(self.base+'/api/city?place=45') as r:city=json.load(r)
        self.assertEqual(city['name'],'临安府')
        self.assertTrue(city['authored'])
        for path in ('/','/index.html','/city.js','/city.css','/assets/terrain-biomes-v3/01-open-plains.png'):
            with self.assertRaises(urllib.error.HTTPError) as error:
                urllib.request.urlopen(self.base+path)
            self.assertEqual(error.exception.code,404)
        self.assertEqual(self.w.export(),before)

    def test_city_api_rejects_missing_unknown_or_bad_place(self):
        for query in ('','?place=-1','?place=168','?place=abc'):
            with self.assertRaises(urllib.error.HTTPError) as e:urllib.request.urlopen(self.base+'/api/city'+query)
            self.assertEqual(e.exception.code,400)
