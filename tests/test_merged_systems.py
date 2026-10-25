"""Regressions at the boundary between settlement control and social systems."""
from copy import deepcopy
import json
import unittest
from unittest.mock import patch

from jianghu import city_maps, government, organizations, politics, social, territory
from jianghu.atlas import PLACES
from jianghu.engine import World
from jianghu.store import Store

SITES = {p['name']: p['id'] for p in PLACES}


class MergedSystemsTests(unittest.TestCase):
    def setUp(self):
        self.w = World(population=0,durable=False)
        self.addCleanup(self.w.store.close)
        self.target = SITES['对马府中']

    def adult(self, place=None, **values):
        w = self.w
        n = w.make_npc(place=self.target if place is None else place)
        n.update(age_months=360,wealth=100.,health=100.,skill=30.,office=None,**values)
        w.set_goal(n,w.best_goal(n),None,initial=True)
        w.reindex()
        return n

    def capture(self):
        self.w.territory[self.target]['guard'] = 1
        with patch.object(self.w.rng,'uniform',return_value=1):
            return territory.campaign(self.w,10,self.target,declare=True)

    def test_country_policy_and_directory_use_current_control(self):
        w = self.w
        n = self.adult()
        event = self.capture()
        n['health'] = 40
        n.update(office='县令',office_country=10)
        w.governments[10]['offices']['县令'] = n['id']
        before_japan = deepcopy(w.governments[11])
        with patch.object(w.rng,'random',return_value=1):
            government.annual(w,w.governments[10])
        self.assertEqual(w.governments[10]['policy'],'health')
        self.assertEqual(w.governments[11],before_japan)
        korea,japan = w.country_detail(10),w.country_detail(11)
        self.assertEqual((korea['population'],korea['patients'],japan['population']),(1,1,0))
        self.assertIn(self.target,{p['id'] for p in korea['places']})
        self.assertNotIn(self.target,{p['id'] for p in japan['places']})
        self.assertIn(event['id'],[e['id'] for e in korea['events']])
        self.assertIn(event['id'],[e['id'] for e in japan['events']])
        self.assertTrue(any(f['name']=='对马船众' for f in korea['factions']))

    def test_faction_duties_improve_captor_and_record_interior_venue(self):
        w = self.w
        n = self.adult()
        f = next(f for f in w.factions if f['place']==self.target)
        n['faction'] = f['id']
        self.capture()
        before = [g['security'] for g in w.governments]
        event = w.execute(n,{'id':f"faction_task:{f['id']}"})
        self.assertEqual(event['facts']['country'],10)
        self.assertGreater(w.governments[10]['security'],before[10])
        self.assertEqual(w.governments[11]['security'],before[11])
        self.assertEqual(w.city_positions[str(n['id'])]['node'],f"faction_{f['id']}")
        self.assertAlmostEqual(f['fund'],f['opening_fund']+f['revenue']-f['expenses'])

    def test_shared_study_records_both_participants_in_the_local_school(self):
        w = self.w
        a,b = self.adult(),self.adult()
        b['skill'] = 70
        f = next(f for f in w.factions if f['place']==self.target)
        for n in (a,b):n.update(faction=f['id'],contribution=8.)
        w.phase = 1
        event = w.execute(a,{'id':f"faction_study:{f['id']}"})
        self.assertIsNotNone(event)
        self.assertEqual((a['time_left'],b['time_left']),(3,3))
        for n in (a,b):
            pos = w.city_positions[str(n['id'])]
            self.assertEqual((pos['node'],pos['event_id']),(f"faction_{f['id']}",event['id']))

    def test_crossing_after_conquest_counts_new_country_and_skips_domestic_tolls(self):
        w = self.w
        self.capture()
        old = deepcopy(w.countries)
        domestic = politics.journey(w,SITES['金州'],self.target)
        self.assertEqual(domestic['border'],'境内')
        politics.record_crossing(w,domestic)
        self.assertEqual(w.countries,old)
        foreign = politics.journey(w,SITES['博多津'],self.target)
        self.assertEqual((foreign['origin_country'],foreign['destination_country']),(11,10))
        politics.record_crossing(w,foreign)
        self.assertEqual(w.countries[10]['border_activity']['11']['tariffs'],foreign['toll'])

    def test_capture_and_social_accounts_reload_without_losing_each_other(self):
        w = self.w
        a,b = self.adult(),self.adult()
        w.link(a,b,60,40)
        self.capture()
        f = next(f for f in w.factions if f['place']==self.target)
        a['faction'] = f['id']
        w.execute(a,{'id':f"faction_task:{f['id']}"})
        w.intervene(b['id'],'visit')
        w.checkpoint()
        store = Store();self.addCleanup(store.close)
        w.store.db.backup(store.db)
        restored = World(store=store,durable=False)
        for field in ('territory','city_positions','factions','governments','countries'):
            self.assertEqual(getattr(w,field),getattr(restored,field))
        for _ in range(3):
            w.month_step();restored.month_step()
        self.assertEqual(w.npcs,restored.npcs)
        self.assertEqual(w.territory,restored.territory)
        self.assertEqual(w.city_positions,restored.city_positions)
        self.assertEqual(w.factions,restored.factions)
        self.assertEqual(w.rng.getstate(),restored.rng.getstate())
