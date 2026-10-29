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

    def test_new_world_population_is_spread_over_every_actual_simulation_location(self):
        w = self.world(population=800, compact=True)
        self.assertEqual(len(w.active_places), len(settlements.HUBS))
        self.assertEqual(len(w.npcs), 800)
        self.assertGreaterEqual(min(len(w.residents[h]) for h in settlements.HUBS), 800//len(settlements.HUBS))
        self.assertEqual({n['place'] for n in w.npcs.values()}, set(settlements.HUBS))
        self.assertEqual(sum(len(territory.controlled(w,c['id'])) for c in w.countries), len(settlements.HUBS))
        self.assertEqual(len(w.summary()['locations']),len(settlements.HUBS))
        self.assertEqual(sum(p['population'] for p in w.summary()['places']),800)
        self.assertEqual(sum(p['active'] for p in w.summary()['places']),len(settlements.HUBS))
        geo=json.loads(Path('art/world-map/geography-compact.json').read_text(encoding='utf-8'))
        self.assertEqual(sorted(c['place'] for c in geo['cells']),sorted(settlements.HUBS))
        self.assertEqual({(r['a'],r['b']) for r in geo['roads']},{(r['a'],r['b']) for r in settlements.COMPACT_ROUTES})
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
        self.assertEqual(len(w.routes),len(settlements.COMPACT_ROUTES))
        for r in w.routes:
            self.assertTrue({r['a'],r['b']} <= seen)
        for n in w.npcs.values():
            n.update(wealth=1000,age_months=360,office=None)
            self.assertTrue(all(a['target'] in seen for a in w.legal_actions(n) if a['kind']=='travel'))
        h=protagonist.perform(w,dict(action='create',name='归来客',place=0,month=0,revision=0))
        self.assertTrue(all(r['place'] in seen for r in h['connections']))
        distant=next(p['id'] for p in PLACES if p['id']!=0 and p['id'] not in w.neighbors[0])
        with self.assertRaises(ValueError):
            protagonist.perform(w,dict(action='travel',place=distant,month=0,revision=h['revision']))

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

    def test_every_region_is_its_own_hub(self):
        self.assertEqual(settlements.HUBS, tuple(range(len(PLACES))))
        self.assertEqual(settlements.HUB_FOR, {p['id']: p['id'] for p in PLACES})
        w=self.world(population=24,compact=True)
        for p in PLACES:
            self.assertEqual(city_maps.view(w,p['id'])['place'],p['id'])

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


if __name__ == '__main__':
    unittest.main()
