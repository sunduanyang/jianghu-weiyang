"""期初家户与乡里旧识: a new world starts with families and acquaintances, not strangers."""
from copy import deepcopy
import unittest

from jianghu import demography, households
from jianghu.engine import World


class HouseholdTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.w = World(7, 900, compact=True, durable=False, population_profile=True)

    @classmethod
    def tearDownClass(cls):
        cls.w.store.close()

    def test_couples_are_real_households(self):
        w = self.w; adults = [n for n in w.npcs.values() if n['age_months'] >= 192]
        share = sum(n['spouse'] is not None for n in adults) / len(adults)
        self.assertGreater(share, .35); self.assertLess(share, .65)
        for n in adults:
            if n['spouse'] is None:
                continue
            q = w.npcs[n['spouse']]
            self.assertEqual(q['spouse'], n['id']); self.assertEqual(q['place'], n['place'])
            self.assertTrue(demography.attracted(n, q) and demography.attracted(q, n))
            self.assertGreaterEqual(n['age_months'], 18*12)
            self.assertEqual(n['relationships'][str(q['id'])]['label'], '夫妻')
            if n['faction'] is not None:
                self.assertNotIn(w.factions[n['faction']]['tradition'], households.CELIBATE)

    def test_children_live_with_plausible_parents_and_carry_the_family_name(self):
        w = self.w; kids = [n for n in w.npcs.values() if n['age_months'] < 192]
        parented = [n for n in kids if n['parents']]
        self.assertGreater(len(parented) / len(kids), .6)
        for child in parented:
            mother, father = (w.npcs[i] for i in child['parents'])
            self.assertEqual(mother['spouse'], father['id'])
            self.assertTrue(17 <= (mother['age_months'] - child['age_months']) / 12 <= 42)
            self.assertEqual(child['guardian'], mother['id'])
            self.assertEqual(child['place'], mother['place'])
            self.assertEqual(child['name'][0], (father if father['sex'] == '男' else mother)['name'][0])
            self.assertEqual(child['generation'], max(mother['generation'], father['generation']) + 1)
            for sibling in mother['children']:
                if sibling != child['id']:
                    self.assertGreaterEqual(abs(w.npcs[sibling]['age_months'] - child['age_months']), 15)
        # Children without a registered family still have an initial guardian.
        self.assertTrue(all(n['guardian'] is not None for n in kids if not n['parents']))
        self.assertEqual(len({n['name'] for n in w.npcs.values()}), len(w.npcs))

    def test_everyone_has_a_few_acquaintances_and_no_one_starts_with_a_feud(self):
        w = self.w
        for n in w.npcs.values():
            if n['age_months'] < 192:
                continue
            registered = [r for r in n['relationships'].values() if r.get('registered')]
            self.assertGreaterEqual(len(registered), 2)
            self.assertTrue(all(r['affinity'] > -25 for r in n['relationships'].values()))
            self.assertNotIn('revenge', w.goal_options(n))
        summary = w.population_generation['households']
        self.assertTrue(summary['initial_condition'])
        self.assertGreater(summary['couples'], 0); self.assertGreater(summary['acquaintances'], 0)

    def test_registration_never_draws_on_the_world_random_stream_or_writes_events(self):
        w = World(11, 0, compact=True, durable=False); self.addCleanup(w.store.close)
        people = []
        for age, sex in ((30, '女'), (33, '男'), (8, '女'), (25, '男'), (27, '女')):
            n = w.make_npc(place=w.active_places[0]['id'])
            n.update(age_months=age*12, sex=sex, orientation='异性'); people.append(n)
        w.reindex(); state = w.rng.getstate(); count = w.store.count()
        first = households.register(w, {people[0]['place']: people})
        self.assertEqual(w.rng.getstate(), state); self.assertEqual(w.store.count(), count)
        self.assertGreaterEqual(first['couples'], 1)

    def test_same_seed_registers_the_same_families(self):
        other = World(7, 900, compact=True, durable=False, population_profile=True)
        self.addCleanup(other.store.close)
        pick = lambda w: {i: (n['name'], n['spouse'], tuple(n['parents'])) for i, n in w.npcs.items()}
        self.assertEqual(pick(other), pick(self.w))


if __name__ == '__main__':
    unittest.main()
