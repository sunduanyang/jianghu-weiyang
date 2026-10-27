from copy import deepcopy
import unittest

from jianghu import city_maps
from jianghu.atlas import PLACES
from jianghu.engine import World


class NoBridgeTests(unittest.TestCase):
    def test_every_region_uses_walkable_fords_without_bridges(self):
        count=0
        for compact in (False,True):
            for place in PLACES:
                city=deepcopy(city_maps._layout(42,place['id'],city_maps.VERSION,compact))
                self.assertFalse(any('=' in row for row in city['rows']))
                gate=city['nodes'][0]
                for r,row in enumerate(city['rows']):
                    for c,t in enumerate(row):
                        if t=='s':
                            count+=1
                            self.assertEqual(city['elevations'][r][c],0)
                            path,cost=city_maps.path_between(city['rows'],(gate['row'],gate['col']),(r,c))
                            self.assertTrue(path)
                            self.assertGreaterEqual(cost,0)
                for town in city['settlements']:
                    path,_=city_maps.path_between(city['rows'],(gate['row'],gate['col']),(town['row'],town['col']))
                    self.assertTrue(path)
        self.assertGreater(count,0)

    def test_version_seven_ford_conversion_preserves_existing_settlements(self):
        for place in PLACES:
            old=city_maps.layout(42,place['id'],6);current=city_maps.layout(42,place['id'],7)
            self.assertEqual(current['rows'],[row.replace('=','s') for row in old['rows']])
            self.assertEqual(current['settlements'],old['settlements'])

    def test_saved_bridge_position_becomes_a_ford_without_spending_a_turn(self):
        place=next(p['id'] for p in PLACES if p['name']=='克鲁伦河营地')
        w=World(population=0,durable=False);self.addCleanup(w.store.close)
        state=deepcopy(w.export());old=city_maps.layout(w.seed,place,6)
        r,c=next((r,c) for r,row in enumerate(old['rows']) for c,t in enumerate(row) if t=='=')
        state['city_map_version']=6
        state['player']['protagonist'].update(created=True,place=place,row=r,col=c)
        w.store.save(state);events=w.store.count()
        restored=World(store=w.store,durable=False);hero=restored.player['protagonist']
        current=city_maps.world_layout(restored,place)
        self.assertEqual((hero['row'],hero['col']),(r,c))
        self.assertEqual(current['rows'][r][c],'s')
        self.assertEqual(hero['points'],state['player']['protagonist']['points'])
        self.assertEqual(restored.month,state['month'])
        self.assertEqual(w.store.count(),events)
        self.assertEqual(restored.rng.getstate(),state['random_state'])
        self.assertNotIn('=',city_maps.view(restored,place)['terrain_legend'])
