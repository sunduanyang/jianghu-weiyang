"""品性 v2: five axes, outlook, stance, archetype generation, inheritance and migration."""
from copy import deepcopy
import json
import statistics
import unittest

from jianghu import player_social, temperament as T
from jianghu.engine import World


class TemperamentTests(unittest.TestCase):
    def world(self, population=0, **extra):
        w = World(7, population, compact=True, durable=False, **extra)
        self.addCleanup(w.store.close)
        return w

    def adult(self, w, **traits):
        n = w.make_npc(place=w.factions[0]['place'])
        n.update(age_months=360, health=100., wealth=60.)
        n['traits'].update(traits)
        w.reindex()
        return n

    def test_every_person_has_five_axes_outlook_and_no_legacy_keys(self):
        w = self.world(400, population_profile=True)
        for n in w.npcs.values():
            self.assertEqual(set(n['traits']), set(T.KEYS))
            self.assertEqual(set(n['beliefs']), set(T.OUTLOOK_KEYS))
            self.assertTrue(all(0 <= v <= 100 for v in list(n['traits'].values()) + list(n['beliefs'].values())))
            self.assertEqual(n['temperament_version'], T.VERSION)
        stances = {T.stance(n) for n in w.npcs.values()}
        self.assertEqual(stances, set(T.STANCES))
        adults = [n for n in w.npcs.values() if n['age_months'] >= 192]
        for k in T.KEYS:
            self.assertGreater(statistics.pstdev(n['traits'][k] for n in adults), 15, k)
        # Most people have something distinctive to say about them.
        self.assertGreater(sum(bool(T.descriptors(n)) for n in adults) / len(adults), .8)

    def test_generation_never_touches_the_world_random_stream(self):
        w = self.world()
        state = w.rng.getstate()
        first = T.draw(w.seed, 12, '书生', '道门')
        self.assertEqual(first, T.draw(w.seed, 12, '书生', '道门'))
        self.assertEqual(w.rng.getstate(), state)
        n = self.adult(w); state = w.rng.getstate()
        T.assign(w, n, '行商')
        self.assertEqual(w.rng.getstate(), state)

    def test_livelihood_and_tradition_shape_archetypes(self):
        scholars = [T.draw(1, i, '书生')[1] for i in range(1, 800)]
        merchants = [T.draw(1, i, '行商')[1] for i in range(1, 800)]
        guards = [T.draw(1, i, '镖师', '武人集团')[1] for i in range(1, 800)]
        mean = lambda rows, k: statistics.mean(r[k] for r in rows)
        self.assertGreater(mean(scholars, 'integrity'), mean(merchants, 'integrity') + 8)
        self.assertGreater(mean(merchants, 'sociability'), mean(scholars, 'sociability') + 10)
        self.assertGreater(mean(guards, 'boldness'), mean(scholars, 'boldness') + 15)

    def test_assignment_keeps_each_persons_desire_noise(self):
        w = self.world(); n = self.adult(w)
        before = deepcopy(n['desires']); old = dict(n['traits'])
        T.assign(w, n, '书生')
        for key, leans in T.DESIRE_LEANS.items():
            expected = before[key] + sum(wt*(n['traits'][a]-old[a]) for a, wt in leans.items())
            self.assertAlmostEqual(n['desires'][key], max(0, min(100, expected)), places=3)
        self.assertEqual(n['initial_traits'], n['traits'])

    def test_children_take_after_their_parents(self):
        w = self.world()
        mother, father = self.adult(w), self.adult(w)
        mother['sex'], father['sex'] = '女', '男'
        for p in (mother, father):
            p['traits'].update(kindness=90., integrity=88., boldness=15., ambition=20., sociability=85.)
            p['beliefs'].update(tradition=90.)
        kids = [w.make_npc([mother['id'], father['id']], mother['place']) for _ in range(12)]
        for k, high in (('kindness', True), ('integrity', True), ('boldness', False), ('sociability', True)):
            avg = statistics.mean(c['traits'][k] for c in kids)
            self.assertTrue(avg > 70 if high else avg < 35, (k, avg))
        self.assertGreater(statistics.mean(c['beliefs']['tradition'] for c in kids), 70)

    def test_stance_follows_the_strongest_leaning(self):
        w = self.world()
        cases = {'upright': dict(integrity=95, boldness=80, kindness=50, ambition=50, sociability=50),
                 'kind': dict(kindness=95, sociability=75, integrity=60, boldness=40, ambition=40),
                 'self': dict(ambition=95, kindness=10, integrity=40, boldness=50, sociability=50),
                 'balanced': dict(kindness=52, integrity=55, boldness=50, ambition=50, sociability=50)}
        for stance, traits in cases.items():
            n = self.adult(w, **traits); n['beliefs'].update(trust=50., justice=50., tradition=55.)
            self.assertEqual(T.stance(n), stance)
        rebel = self.adult(w, kindness=50, integrity=45, boldness=70, ambition=50, sociability=50)
        rebel['beliefs'].update(tradition=5., justice=50.)
        self.assertEqual(T.stance(rebel), 'defiant')

    def test_words_name_only_distinctive_axes(self):
        w = self.world()
        n = self.adult(w, kindness=90., integrity=25., boldness=52., ambition=48., sociability=60.)
        self.assertEqual(T.descriptors(n), ['极仁厚', '颇多诡诈'])
        self.assertEqual(T.word('boldness', 10), '极谨慎')
        flat = self.adult(w, **dict.fromkeys(T.KEYS, 50.)); flat['beliefs'].update(dict.fromkeys(T.OUTLOOK_KEYS, 50.))
        self.assertEqual(T.descriptors(flat), [])
        self.assertTrue(T.summary(flat).startswith('中庸'))

    def test_cold_deceitful_people_are_the_ones_tempted_by_crime(self):
        w = self.world()
        rogue = self.adult(w, kindness=15., integrity=12., boldness=60., ambition=70., sociability=55.)
        honest = self.adult(w, kindness=70., integrity=80., boldness=30., ambition=35., sociability=45.)
        victim = self.adult(w)
        for n in (rogue, honest):
            n['pressure'] = 20.
            w.set_goal(n, 'wealth', initial=True)
        steal = {'kind': 'steal', 'target': victim['id']}
        self.assertGreater(w.score(rogue, steal) - w.score(honest, steal), 40)

    def test_views_are_pure_and_readable(self):
        w = self.world(60, population_profile=True)
        n = next(n for n in w.npcs.values() if n['age_months'] >= 216)
        before = deepcopy(w.export())
        detail = w.faction_detail(0) and w.npc_detail(n['id'])
        view = detail['temperament_view']
        self.assertEqual([a['key'] for a in view['axes']], list(T.KEYS))
        self.assertIn(view['stance_label'], [s[0] for s in T.STANCES.values()])
        prompt = json.dumps(T.prompt_view(n), ensure_ascii=False)
        for legacy in T.LEGACY_NAMES:
            self.assertNotIn(legacy, prompt)
        self.assertEqual(player_social.view(w, n['id'])['stance']['key'], T.stance(n))
        self.assertEqual(w.export(), before)

    def legacy_state(self, w, drop_extras=False):
        state = json.loads(json.dumps(w.export()))
        for row in state['npcs'].values():
            old = dict(compassion=80., ambition=40., caution=70., sociability=30., loyalty=60.,
                       honesty=90., greed=20., aggression=25.)
            if drop_extras:
                for k in ('honesty', 'greed', 'aggression'):
                    old.pop(k)
            row['traits'] = dict(old); row['initial_traits'] = dict(old)
            row['beliefs'] = dict(trust=.4, justice=.7, tradition=.6)
            row.pop('temperament_version', None); row.pop('archetype', None)
            row['personality_changes'] = [dict(month=0, axis='caution', delta=.1, event_id=None, source='旧档')]
        return state

    def test_legacy_saves_convert_with_a_fixed_formula_once(self):
        w = self.world(30, population_profile=True)
        state = self.legacy_state(w); rng = w.rng.getstate(); events = w.store.count()
        w.restore(state)
        n = next(iter(w.npcs.values()))
        expected = T.convert(dict(compassion=80., ambition=40., caution=70., sociability=30., loyalty=60.,
                                  honesty=90., greed=20., aggression=25.))
        self.assertEqual(n['traits'], expected)
        self.assertEqual(n['initial_traits'], expected)
        self.assertEqual(n['beliefs'], dict(trust=40., justice=70., tradition=60.))
        self.assertEqual(n['legacy_traits']['honesty'], 90.)
        self.assertEqual((w.rng.getstate(), w.store.count()), (rng, events))
        self.assertTrue(T.view(n)['changes'][0]['legacy'])
        again = deepcopy(w.export()); w.restore(json.loads(json.dumps(again)))
        self.assertEqual(json.loads(json.dumps(w.export())), json.loads(json.dumps(again)))

    def test_very_old_saves_without_moral_traits_use_the_former_reproducible_draw(self):
        w = self.world(10, population_profile=True)
        w.restore(self.legacy_state(w, drop_extras=True))
        for n in w.npcs.values():
            extras = T.legacy_extras(w.seed, n)
            base = dict(compassion=80., ambition=40., caution=70., sociability=30., loyalty=60.)
            self.assertEqual(n['traits'], T.convert(base, extras))

    def test_converted_axes_keep_the_former_spread(self):
        import random
        rng = random.Random(3)
        rows = [T.convert({k: rng.uniform(15, 85) for k in T.LEGACY_NAMES}) for _ in range(3000)]
        for k in ('kindness', 'integrity', 'boldness'):
            self.assertGreater(statistics.pstdev(r[k] for r in rows), 17)


if __name__ == '__main__':
    unittest.main()
