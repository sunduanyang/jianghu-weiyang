from collections import Counter
from copy import deepcopy
from types import SimpleNamespace
import unittest

from jianghu import city_maps,city_regions,settlements
from jianghu.atlas import PLACES
from jianghu.engine import World

HOME=next(p['id'] for p in PLACES if p['name']=='洛阳')
SECT=next(p['id'] for p in PLACES if p['name']=='襄阳')


class SettlementLayoutTests(unittest.TestCase):
    def test_exact_user_patterns_and_population_boundaries(self):
        for population,tier in ((0,'sparse'),(24,'sparse'),(25,'general'),(49,'general'),(50,'prosperous')):
            self.assertEqual(city_regions.population_tier(population),tier)
        for tier,cells in (('prosperous',[1,4,9]),('general',[4,9]),('sparse',[1,9])):
            city=city_maps.layout(42,HOME,settlement_tier=tier)
            self.assertEqual(sorted(t['footprint']['size']**2 for t in city['settlements'] if t['kind']!='sect'),cells)

    def test_existing_sect_positions_geography_and_facility_ids_are_preserved(self):
        old=deepcopy(city_maps._layout(42,SECT,7,True))
        for tier in city_regions.LAYOUTS:
            city=city_maps._layout(42,SECT,8,True,tier)
            self.assertEqual([t for t in old['settlements'] if t['kind']=='sect'],[t for t in city['settlements'] if t['kind']=='sect'])
            for key in ('rows','elevations','terrain_tiles','size','theme'):
                self.assertEqual(old[key],city[key],(tier,key))
            self.assertEqual({n['id'] for n in old['nodes']},{n['id'] for n in city['nodes']})
            self.assertEqual(next(n for n in old['nodes'] if n['id']=='gate'),next(n for n in city['nodes'] if n['id']=='gate'))
        self.assertEqual(old,city_maps._layout(42,SECT,7,True))

    def test_removed_settlements_leave_no_orphan_service_overlap_or_unreachable_facility(self):
        for tier in city_regions.LAYOUTS:
            city=city_maps.layout(42,HOME,settlement_tier=tier);occupied=set()
            by_id={t['id']:t for t in city['settlements']}
            gate=next(n for n in city['nodes'] if n['kind']=='gate')
            for town in city['settlements']:
                lot=town['footprint'];cells={(lot['row']+r,lot['col']+c) for r in range(lot['size']) for c in range(lot['size'])}
                self.assertFalse(occupied&cells);occupied.update(cells)
                self.assertTrue(town['services'])
            for node in city['nodes']:
                town=by_id[node['settlement_id']]
                self.assertIn(node['kind'],town['services'])
                if node['kind']!='gate':self.assertEqual(node['footprint'],town['footprint'])
                path,_=city_maps.path_between(city['rows'],(gate['row'],gate['col']),(node['row'],node['col']))
                self.assertTrue(path)
            self.assertNotIn('settlement_3',by_id)
            self.assertEqual(len(city['cell_areas']),city['size'])
            self.assertTrue(all(index>=0 for row in city['cell_areas'] for index in row))

    def test_profiles_use_census_once_and_do_not_flap_with_population_changes(self):
        w=SimpleNamespace(month=20,city_settlement_profiles={},active_places=PLACES[:3],npcs={})
        for place,count in enumerate((50,25,24)):
            for _ in range(count):w.npcs[len(w.npcs)+1]=dict(place=place,alive=True)
        city_regions.initialize_profiles(w)
        self.assertEqual([w.city_settlement_profiles[str(i)]['tier'] for i in range(3)],['prosperous','general','sparse'])
        before=deepcopy(w.city_settlement_profiles);w.npcs.clear();w.month+=1;city_regions.initialize_profiles(w)
        self.assertEqual(w.city_settlement_profiles,before)

    def test_migration_from_removed_town_preserves_facility_progress_rng_assets_and_journal(self):
        w=World(population=0,durable=False,compact=True);self.addCleanup(w.store.close)
        state=deepcopy(w.export());state['city_map_version']=7;state.pop('city_settlement_profiles')
        old=city_maps._layout(w.seed,HOME,7,True);workshop=next(n for n in old['nodes'] if n['id']=='workshop')
        h=state['player']['protagonist'];h.update(created=True,place=HOME,row=workshop['row'],col=workshop['col'],points=17)
        w.store.save(state);events=w.store.count()
        loaded=World(store=w.store,durable=False,compact=True)
        city=city_maps.world_layout(loaded,HOME);target=next(n for n in city['nodes'] if n['id']=='workshop')
        hero=loaded.player['protagonist']
        self.assertEqual((hero['row'],hero['col']),(target['row'],target['col']))
        self.assertEqual(target['settlement_id'],'settlement_0')
        self.assertEqual(hero['points'],17);self.assertEqual(hero['revision'],h['revision'])
        self.assertEqual(loaded.npcs,state['npcs']);self.assertEqual(loaded.items,state['items'])
        self.assertEqual(loaded.economy,state['economy']);self.assertEqual(loaded.rng.getstate(),state['random_state'])
        self.assertEqual(events,w.store.count())
        saved=deepcopy(loaded.export());again=World(store=w.store,durable=False,compact=True)
        self.assertEqual(again.export(),saved)

    def test_hero_on_retained_sect_or_city_cell_keeps_exact_coordinates(self):
        for kind in ('city','sect'):
            w=World(population=0,durable=False,compact=True)
            try:
                state=deepcopy(w.export());state['city_map_version']=7;state.pop('city_settlement_profiles')
                city=city_maps._layout(w.seed,SECT,7,True);site=next(t for t in city['settlements'] if t['kind']==kind)
                at=site['footprint']['row'],site['footprint']['col']
                state['player']['protagonist'].update(created=True,place=SECT,row=at[0],col=at[1])
                w.restore(state)
                self.assertEqual((w.player['protagonist']['row'],w.player['protagonist']['col']),at)
            finally:w.store.close()

    def test_view_counts_each_person_once_after_merging_and_is_read_only(self):
        w=World(population=12,durable=False,compact=True);self.addCleanup(w.store.close)
        for n in w.npcs.values():
            n['place']=HOME;w.city_positions[str(n['id'])]=dict(place=HOME,node=('residence','garden','workshop')[n['id']%3],event_id=None,month=0)
        w.reindex();before=deepcopy(w.export());events=w.store.count()
        city=city_maps.view(w,HOME)
        ids=[p['id'] for t in city['settlements'] for p in t['residents']]
        self.assertEqual(Counter(ids),Counter(w.npcs.keys()))
        self.assertEqual(w.export(),before);self.assertEqual(events,w.store.count())

    def test_path_cache_distinguishes_layout_profiles_and_view_copies_are_isolated(self):
        w=World(population=0,durable=False,compact=True);self.addCleanup(w.store.close)
        for tier in city_regions.LAYOUTS:
            w.city_settlement_profiles[str(HOME)]['tier']=tier
            city=city_maps.view(w,HOME);gate=next(n for n in city['nodes'] if n['kind']=='gate')
            for node in city['nodes']:
                expected=city_maps.path_between(city['rows'],(gate['row'],gate['col']),(node['row'],node['col']))
                self.assertEqual((node['path'],node['walk_cost']),expected)
            city['settlements'].clear()
            self.assertTrue(city_maps.view(w,HOME)['settlements'])


if __name__=='__main__':unittest.main()
