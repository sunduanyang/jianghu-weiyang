from copy import deepcopy
import hashlib
import json
from pathlib import Path
import unittest

from jianghu import city_gardens, city_maps
from jianghu.atlas import PLACES, FACTIONS
from jianghu.engine import World

ROOT=Path(__file__).resolve().parents[1]


class CityGardenTests(unittest.TestCase):
    def test_supplied_sources_are_archived_exactly_and_duplicates_are_documented(self):
        archive=ROOT/'art/landscape-library-20261027'
        manifest=json.loads((archive/'source-manifest.json').read_text(encoding='utf-8'))
        self.assertEqual((manifest['count'],manifest['unique_count']),(31,30))
        for item in manifest['files']:
            data=(archive/'source'/item['file']).read_bytes()
            self.assertEqual(hashlib.sha256(data).hexdigest(),item['sha256'])
        self.assertEqual(sum('duplicate_of' in f for f in manifest['files']),1)

    def test_version_seven_regional_cities_four_settlements_and_breathing_room(self):
        seen=set();sizes=set()
        for p in PLACES:
            city=city_maps.layout(42,p['id'],7);sizes.add(city['size'])
            self.assertEqual(city['size'],30 if p['kind']=='capital' or p['weight']>=8 else 20)
            self.assertEqual(sorted(t['footprint']['size'] for t in city['settlements']),[1,1,2,3]+[3]*sum(f['place']==p['id'] for f in FACTIONS))
            complex_cells=sum(t['size']**2 for t in city['terrain_tiles'] if t['size']==2)
            self.assertLessEqual(complex_cells/(city['size']**2),.35)
            for tile in city['terrain_tiles']:
                seen.add(tile['asset'])
                self.assertIn(tile['asset'],city_gardens.SELECTED)
                self.assertEqual(tile['size'],city_gardens.FOOTPRINTS[tile['asset']])
        self.assertEqual(sizes,{30})  # every regional city is a large district
        self.assertTrue({'33-winding-meadow','34-ancient-grove','35-jagged-peaks','37-river-terraces','42-blue-water'}<=seen)

    def test_version_four_map_upgrades_and_retains_the_same_settlement(self):
        p=next(p for p in PLACES if p['name']=='凉州')
        w=World(population=0,durable=False);self.addCleanup(w.store.close)
        state=deepcopy(w.export());old=city_maps.layout(w.seed,p['id'],4)
        service=next(n for n in old['nodes'] if n['id']=='inn')
        state['city_map_version']=4
        state['player']['protagonist'].update(created=True,place=p['id'],row=service['row'],col=service['col'])
        w.store.save(state);events=w.store.count()
        restored=World(store=w.store,durable=False);current=city_maps.world_layout(restored,p['id'])
        target=next(n for n in current['nodes'] if n['id']=='inn');hero=restored.player['protagonist']
        self.assertEqual((old['size'],current['size']),(30,30))
        self.assertEqual((hero['row'],hero['col']),(target['row'],target['col']))
        self.assertEqual(hero['points'],state['player']['protagonist']['points'])
        self.assertEqual(w.store.count(),events)
        self.assertEqual(restored.rng.getstate(),state['random_state'])
