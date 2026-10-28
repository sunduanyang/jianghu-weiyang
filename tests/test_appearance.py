from copy import deepcopy
from pathlib import Path
import tempfile
import unittest

from jianghu import appearance, protagonist
from jianghu.engine import World
from jianghu.store import Store


class AppearanceTests(unittest.TestCase):
    def world(self, **kw):
        w = World(population=20, durable=False, **kw)
        self.addCleanup(w.store.close)
        return w

    def test_registration_is_stable_independent_and_preserves_simulation_rng(self):
        w = self.world(seed=71)
        n = w.npcs[1]
        original = deepcopy(n['appearance'])
        state = w.rng.getstate()
        n.pop('appearance')
        self.assertEqual(appearance.prepare(w, n), original)
        self.assertEqual(w.rng.getstate(), state)
        n.update(job='书生', age_months=75*12, place=12, faction=3)
        self.assertEqual(appearance.prepare(w, n), original)
        self.assertEqual(appearance.view(n)['grey'], 4)
        other = self.world(seed=72)
        self.assertNotEqual(other.npcs[1]['appearance'], original)

    def test_old_checkpoint_gets_looks_without_new_events_or_rng(self):
        w = self.world(seed=74)
        expected = deepcopy(w.npcs[1]['appearance'])
        state = deepcopy(w.export())
        for n in state['npcs'].values():
            n.pop('appearance')
        rng = w.rng.getstate()
        count = w.store.count()
        w.restore(state)
        self.assertEqual(w.npcs[1]['appearance'], expected)
        self.assertEqual(w.rng.getstate(), rng)
        self.assertEqual(w.store.count(), count)
        self.assertTrue(all(n['appearance']['source'] == '期初外貌登记' for n in w.npcs.values()))

    def test_sqlite_roundtrip_and_every_person_view_agree(self):
        with tempfile.TemporaryDirectory() as d:
            path = Path(d)/'isolated.sqlite'
            first = World(seed=22, population=20, store=Store(path), durable=False)
            h = first.player['protagonist']
            protagonist.perform(first, dict(action='create', revision=h['revision'], month=first.month,
                                           name='林青', sex='女', age=28, place=h['place']))
            first.checkpoint()
            looks = deepcopy([n['appearance'] for n in first.npcs.values()])
            hero = deepcopy(first.player['protagonist']['appearance'])
            first.store.close()
            second = World(store=Store(path), durable=False)
            try:
                self.assertEqual([n['appearance'] for n in second.npcs.values()], looks)
                self.assertEqual(second.summary()['hero']['appearance'], hero)
                self.assertIn(hero['head'], appearance.FEMALE_HEADS)
                for n in second.summary()['npcs']:
                    self.assertEqual(n['appearance'], second.npc_detail(n['id'])['appearance'])
                    self.assertEqual(n['appearance_view'], second.npc_detail(n['id'])['appearance_view'])
            finally:
                second.store.close()

    def test_births_inherit_only_actual_parents_and_keep_independent_rng(self):
        w = self.world(seed=5)
        parent = w.npcs[1]
        child = w.make_npc(parents=[1], place=parent['place'])
        self.assertEqual(child['appearance']['source'], '出生生成')
        self.assertEqual(child['appearance']['skin'], parent['appearance']['skin'])
        self.assertEqual(child['appearance']['hair'], parent['appearance']['hair'])
        self.assertNotEqual(child['appearance']['seed'], parent['appearance']['seed'])

    def test_v1_migration_preserves_identity_and_does_not_advance_world(self):
        w = self.world(seed=19)
        actor = w.npcs[1]
        old = {k: v for k, v in actor['appearance'].items() if k not in appearance.MODULES}
        old['version'] = 1
        actor['appearance'] = deepcopy(old)
        rng, month, events = w.rng.getstate(), w.month, w.store.count()
        migrated = appearance.prepare(w, actor)
        self.assertEqual(migrated['version'], 2)
        for key, value in old.items():
            if key != 'version':
                self.assertEqual(migrated[key], value)
        expected = deepcopy(migrated)
        self.assertEqual(appearance.prepare(w, actor), expected)
        actor['appearance'] = deepcopy(old)
        self.assertEqual(appearance.prepare(w, actor), expected)
        self.assertEqual((w.rng.getstate(), w.month, w.store.count()), (rng, month, events))

    def test_modular_features_are_saved_and_have_independent_combinations(self):
        w = self.world(seed=17)
        profiles = [appearance.prepare(w, dict(id=i, sex='女')) for i in range(1, 801)]
        faces = {(p['eyes'], p['brows'], p['lips']) for p in profiles}
        self.assertEqual(len(faces), 27)
        self.assertEqual({p['outfit'] for p in profiles}, {0, 1})
        for p in profiles:
            self.assertTrue(all(0 <= p[k] < 3 for k in ('eyes', 'brows', 'lips')))

    def test_800_same_role_people_have_meaningful_combination_diversity(self):
        w = self.world(seed=71)
        looks = [appearance.prepare(w, dict(id=i, sex='男', age_months=336, job='镖师')) for i in range(1, 801)]
        signatures = {tuple(p[k] for k in ('head', 'skin', 'hair', 'palette', 'face_width')) for p in looks}
        self.assertGreater(len(signatures), 700)
        self.assertEqual(len({p['head'] for p in looks}), 8)
        self.assertEqual(len({p['palette'] for p in looks}), 8)
        self.assertEqual(len({p['skin'] for p in looks}), 4)
