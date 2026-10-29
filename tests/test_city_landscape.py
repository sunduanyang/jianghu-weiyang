from copy import deepcopy
import unittest

from jianghu import city_maps
from jianghu.atlas import PLACES, FACTIONS
from jianghu.engine import World

HOME=next(p['id'] for p in PLACES if p['name']=='洛阳')


class CityLandscapeTests(unittest.TestCase):
    def test_elevation_has_terraces_whole_lots_and_safe_walking_steps(self):
        footprints=set()
        for p in PLACES:
            city=city_maps.layout(42,p['id']); heights=city['elevations']; size=city['size']
            self.assertEqual(len(heights),size)
            self.assertTrue(all(len(row)==size for row in heights))
            self.assertGreater(max(map(max,heights)),min(map(min,heights)))
            self.assertEqual(city['settlement_pack'],'settlement-tiles-1-4-9-20261026')
            for r in range(size):
                for c in range(size):
                    if city['rows'][r][c]=='~':self.assertEqual(heights[r][c],0)
                    if city['rows'][r][c] in '~^#':continue
                    for a,b in ((r+1,c),(r,c+1)):
                        if a<size and b<size and city['rows'][a][b] not in '~^#':
                            self.assertLessEqual(abs(heights[r][c]-heights[a][b]),1)
            for node in city['settlements']:
                lot=node['footprint']; footprints.add(lot['size'])
                self.assertEqual(node['settlement_asset'],{1:'01-village-1cell',2:'02-town-4cell',3:'03-city-9cell'}[lot['size']])
            for lot in [n['footprint'] for n in city['settlements']]+city['terrain_tiles']:
                self.assertEqual(len({heights[lot['row']+r][lot['col']+c]
                                     for r in range(lot['size']) for c in range(lot['size'])}),1)
        self.assertEqual(footprints,{2,3})

    def test_whole_areas_share_every_intrinsic_cell_property(self):
        for seed in (0,42,99):
            for p in PLACES:
                city=city_maps.layout(seed,p['id']);covered=set()
                for i,area in enumerate(city['areas']):
                    cells={(area['row']+r,area['col']+c) for r in range(area['size']) for c in range(area['size'])}
                    self.assertFalse(covered&cells)
                    covered.update(cells)
                    for r,c in cells:
                        self.assertEqual(city['cell_areas'][r][c],i)
                        self.assertIs(city_maps.city_regions.area_at(city,r,c),area)
                        terrain=city['rows'][r][c]
                        self.assertEqual(terrain,area['terrain'])
                        self.assertEqual(city['elevations'][r][c],area['elevation'])
                        self.assertEqual(terrain not in '~^#',area['passable'])
                        self.assertEqual(1 if terrain in '+=' else 3 if terrain=='t' else 2,area['move_cost'])
                self.assertEqual(len(covered),city['size']**2)
                self.assertEqual(sorted(t['footprint']['size'] for t in city['settlements']),[2,3]+[3]*sum(f['place']==p['id'] for f in FACTIONS))

    def test_version_three_save_keeps_venue_and_migrates_without_new_events(self):
        w=World(population=0,durable=False);self.addCleanup(w.store.close)
        state=deepcopy(w.export());old=city_maps.layout(w.seed,HOME,3)
        service=next(n for n in old['nodes'] if n['id']=='inn')
        state['city_map_version']=3
        state['player']['protagonist'].update(created=True,place=HOME,row=service['row'],col=service['col'])
        w.store.save(state);events=w.store.count()
        restored=World(store=w.store,durable=False)
        current=city_maps.world_layout(restored,HOME);target=next(n for n in current['nodes'] if n['id']=='inn')
        hero=restored.player['protagonist']
        self.assertEqual((hero['row'],hero['col']),(target['row'],target['col']))
        self.assertEqual(hero['points'],state['player']['protagonist']['points'])
        self.assertEqual(w.store.count(),events)
        self.assertEqual(restored.rng.getstate(),state['random_state'])

    def test_residents_and_services_are_merged_into_registered_visible_settlements(self):
        w=World(population=45,durable=False,compact=True);self.addCleanup(w.store.close)
        for place in w.residents:
            city=city_maps.view(w,place)
            self.assertEqual(len(city['settlements']),2+sum(f['place']==city['place'] for f in w.factions))
            self.assertEqual(sum(len(t['residents']) for t in city['settlements']),len(w.residents[city['place']]))
            for n in city['nodes']:
                town=next(t for t in city['settlements'] if t['id']==n['settlement_id'])
                if n['id']!='gate':self.assertEqual((n['row'],n['col']),(town['row'],town['col']))

    def test_legacy_interior_cell_migrates_with_its_whole_settlement(self):
        w=World(population=0,durable=False);self.addCleanup(w.store.close)
        state=deepcopy(w.export());old=city_maps.layout(w.seed,HOME,3)
        service=next(n for n in old['nodes'] if n['footprint']['size']==3)
        lot=service['footprint']
        cell=next((lot['row']+r,lot['col']+c) for r in range(3) for c in range(3)
                  if (lot['row']+r,lot['col']+c)!=(service['row'],service['col']))
        state['city_map_version']=3
        state['player']['protagonist'].update(created=True,place=HOME,row=cell[0],col=cell[1])
        w.store.save(state);events=w.store.count()
        restored=World(store=w.store,durable=False);current=city_maps.world_layout(restored,HOME)
        target=next(n for n in current['nodes'] if n['id']==service['id']);hero=restored.player['protagonist']
        self.assertEqual((hero['row'],hero['col']),(target['row'],target['col']))
        self.assertEqual(hero['points'],state['player']['protagonist']['points'])
        self.assertEqual(w.store.count(),events)

    def test_seed_changes_landscape_but_preserves_named_services(self):
        cities=[city_maps.layout(seed,HOME) for seed in (0,42,99)]
        self.assertEqual(len({tuple(c['rows']) for c in cities}),3)
        self.assertEqual(cities[1],city_maps.layout(42,HOME))
        for city in cities:
            through_rows=sum(all(t in '+=' for t in row) for row in city['rows'])
            through_cols=sum(all(city['rows'][r][c] in '+=' for r in range(city['size'])) for c in range(city['size']))
            self.assertLess(through_rows+through_cols,3)

    def test_version_two_save_shrinks_and_retains_the_current_service(self):
        w=World(population=0,durable=False);self.addCleanup(w.store.close)
        state=deepcopy(w.export());old=city_maps.layout(w.seed,HOME,2)
        node=next(n for n in old['nodes'] if n['id']=='market')
        state['city_map_version']=2
        state['player']['protagonist'].update(created=True,place=HOME,row=node['row'],col=node['col'])
        w.store.save(state);events=w.store.count()
        restored=World(store=w.store,durable=False)
        current=city_maps.world_layout(restored,HOME)
        target=next(n for n in current['nodes'] if n['id']=='market')
        hero=restored.player['protagonist']
        self.assertEqual((old['size'],current['size']),(40,30))
        self.assertEqual((hero['row'],hero['col']),(target['row'],target['col']))
        self.assertEqual(hero['points'],state['player']['protagonist']['points'])
        self.assertEqual(w.store.count(),events)
        self.assertEqual(restored.rng.getstate(),state['random_state'])
