from jianghu import combat_attributes as abilities
from tests.combat_helpers import set_rating, update_actor
"""Regressions at the boundary between regional governments, sects and social systems."""
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
        self.target = SITES['洛阳']

    def adult(self, place=None, **values):
        w = self.w
        n = w.make_npc(place=self.target if place is None else place)
        update_actor(n, age_months=360,wealth=100.,health=100.,skill=30.,office=None,**values)
        w.set_goal(n,w.best_goal(n),None,initial=True)
        w.reindex()
        return n

    def test_country_policy_and_directory_use_the_local_yamen(self):
        w = self.w
        n = self.adult()
        n['health'] = 40
        n.update(office='县令',office_country=0)
        w.governments[0]['offices']['县令'] = n['id']
        before_other = deepcopy(w.governments[1])
        with patch.object(w.rng,'random',return_value=1):
            government.annual(w,w.governments[0])
        self.assertEqual(w.governments[0]['policy'],'health')
        self.assertEqual(w.governments[1],before_other)
        here,there = w.country_detail(0),w.country_detail(1)
        self.assertEqual((here['population'],here['patients'],there['population']),(1,1,0))
        self.assertEqual([p['id'] for p in here['places']],[self.target])
        self.assertEqual([f['name'] for f in here['factions']],['少林寺'])

    def test_removed_faction_duties_do_not_change_local_security(self):
        w = self.w
        n = self.adult()
        f = next(f for f in w.factions if f['place']==self.target)
        n['faction'] = f['id']
        before = [g['security'] for g in w.governments]
        event = w.execute(n,{'id':f"faction_task:{f['id']}"})
        self.assertIsNone(event)
        self.assertEqual([g['security'] for g in w.governments],before)
        self.assertAlmostEqual(f['fund'],f['opening_fund']+f['revenue']-f['expenses'])

    def test_retired_shared_study_does_not_reserve_either_participant(self):
        w=self.w; a,b=self.adult(),self.adult()
        f=next(f for f in w.factions if f['place']==self.target)
        for n in (a,b):n.update(faction=f['id'],contribution=8.)
        before=deepcopy(w.export())
        self.assertIsNone(w.execute(a,{'id':f'faction_study:{f["id"]}'}))
        self.assertEqual(w.export(),before)

    def test_sect_and_social_accounts_reload_without_losing_each_other(self):
        w = self.w
        a,b = self.adult(),self.adult()
        w.link(a,b,60,40)
        f = next(f for f in w.factions if f['place']==self.target)
        a['faction'] = f['id']
        w.execute(a,{'id':f"duty:{f['id']}"})
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


if __name__ == '__main__':
    unittest.main()
