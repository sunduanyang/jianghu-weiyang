from copy import deepcopy
import json
from pathlib import Path
import tempfile
import unittest

from jianghu import city_maps, protagonist, settlements, territory
from jianghu.atlas import PLACES
from jianghu.engine import World
from jianghu.store import Store


class SettlementTests(unittest.TestCase):
    def world(self, **kwargs):
        w = World(durable=False, **kwargs)
        self.addCleanup(w.store.close)
        return w

    def test_new_world_population_is_spread_over_24_actual_simulation_locations(self):
        w = self.world(population=800, compact=True)
        self.assertEqual(len(w.active_places), 24)
        self.assertEqual(len(w.npcs), 800)
        self.assertGreaterEqual(min(len(w.residents[h]) for h in settlements.HUBS), 33)
        self.assertEqual({n['place'] for n in w.npcs.values()}, set(settlements.HUBS))
        self.assertEqual(sum(len(territory.controlled(w,c['id'])) for c in w.countries), 24)
        self.assertEqual(len(w.summary()['locations']),24)
        self.assertEqual(sum(p['population'] for p in w.summary()['places']),800)
        self.assertEqual(sum(p['active'] for p in w.summary()['places']),24)
        self.assertEqual(len(json.loads(Path('static/geography-compact.json').read_text(encoding='utf-8'))['cells']),24)
        w.month_step()
        self.assertTrue(all(n['place'] in settlements.HUBS for n in w.npcs.values() if n['alive']))

    def test_routes_connected_and_npcs_and_player_use_only_consolidated_routes(self):
        w = self.world(population=48, compact=True)
        seen, todo = {settlements.HUBS[0]}, [settlements.HUBS[0]]
        while todo:
            for other in w.neighbors[todo.pop()]:
                if other not in seen:
                    seen.add(other);todo.append(other)
        self.assertEqual(seen,set(settlements.HUBS))
        self.assertEqual(len(w.routes),39)
        for r in w.routes:
            self.assertTrue({r['a'],r['b']} <= seen)
        for n in w.npcs.values():
            n.update(wealth=1000,age_months=360,office=None)
            self.assertTrue(all(a['target'] in seen for a in w.legal_actions(n) if a['kind']=='travel'))
        h=protagonist.perform(w,dict(action='create',name='归来客',place=45,month=0,revision=0))
        self.assertTrue(all(r['place'] in seen for r in h['connections']))
        inactive=next(p['id'] for p in PLACES if p['id'] not in seen)
        with self.assertRaises(ValueError):
            protagonist.perform(w,dict(action='travel',place=inactive,month=0,revision=h['revision']))
        self.assertEqual(territory.attack_options(w,inactive),[])

    def test_all_factions_have_reachable_facilities_and_citizens_fill_multiple_venues(self):
        w = self.world(population=800, compact=True)
        for h in settlements.HUBS:
            city=city_maps.view(w,h)
            self.assertEqual(sum(len(n['residents']) for n in city['nodes']),len(w.residents[h]))
            self.assertGreaterEqual(sum(bool(n['residents']) for n in city['nodes']),4)
            for node in city['nodes']:
                self.assertTrue(node['path'])
        for f in w.factions:
            self.assertIn(f['place'],settlements.HUBS)
            self.assertTrue(any(n.get('faction')==f['id'] for n in city_maps.view(w,f['place'])['nodes']))

    def test_migration_preserves_old_assets_histories_and_player_time_and_only_registers_once(self):
        w=self.world(population=12)
        protagonist.perform(w,dict(action='create',name='行舟',place=45,month=0,revision=0))
        w.player['protagonist']['points']=17
        before=deepcopy(w.export())
        events=w.store.db.execute('SELECT * FROM events ORDER BY id').fetchall()
        self.assertTrue(settlements.activate(w))
        self.assertEqual(w.player,before['player'])
        self.assertEqual(w.month,before['month'])
        self.assertEqual(w.store.db.execute('SELECT * FROM events WHERE id<=? ORDER BY id',(events[-1][0],)).fetchall(),events)
        for i,n in before['npcs'].items():
            for key in ('name','wealth','skill','age_months','traits','relationships','memories','parents','children','inventory','equipment'):
                self.assertEqual(w.npcs[i][key],n[key])
        self.assertTrue(all(n['place'] in settlements.HUBS for n in w.npcs.values() if n['alive']))
        for h in settlements.HUBS:
            self.assertGreaterEqual(len(w.residents[h]),48 if h==45 else 24)
        state=deepcopy(w.export())
        self.assertFalse(settlements.activate(w))
        self.assertEqual(w.export(),state)

    def test_archived_place_links_resolve_to_the_merged_city_without_extra_maps(self):
        w=self.world(population=24,compact=True)
        for p in PLACES:
            city=city_maps.view(w,p['id'])
            self.assertEqual(city['place'],settlements.HUB_FOR[p['id']])

    def test_capture_updates_the_whole_merged_region_without_ghost_settlements(self):
        w=self.world(population=48,compact=True)
        target=next(h for h in settlements.HUBS if PLACES[h]['country']==1)
        event=w.emit('test_capture',[], '验证合并辖区易主。',facts={'place':target})
        territory.capture(w,target,0,event)
        for p in PLACES:
            if settlements.HUB_FOR[p['id']]==target:
                self.assertEqual(territory.owner(w,p['id']),0)
        self.assertIn(target,territory.controlled(w,0))
        self.assertEqual(sum(len(territory.controlled(w,c['id'])) for c in w.countries),24)

    def test_saved_compact_profile_survives_restore_without_population_refill_or_rng_drift(self):
        w=self.world(population=48,compact=True)
        before=deepcopy(w.export())
        w.restore(deepcopy(before))
        self.assertEqual(w.export(),before)
        self.assertEqual(len(w.npcs),48)
        with tempfile.TemporaryDirectory() as tmp:
            db=Path(tmp)/'compact.sqlite'
            store=Store(db)
            store.save(before);store.close()
            restored=World(store=Store(db),compact=True)
            try:
                self.assertEqual(restored.export(),before)
            finally:
                restored.store.close()
