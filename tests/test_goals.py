"""Reachable pursuits, grudges that can end, deterrence and desynchronized trades."""
from copy import deepcopy
import unittest
from unittest.mock import patch

from jianghu import careers, goals, social
from jianghu.engine import World


class GoalTests(unittest.TestCase):
    def setUp(self):
        self.w = World(7, 0, compact=True, durable=False)
        self.addCleanup(self.w.store.close)
        self.place = self.w.factions[0]['place']

    def adult(self, **values):
        n = self.w.make_npc(place=self.place)
        n.update(age_months=360, health=100., wealth=60., spouse=None)
        n['mood'].update(anger=0., joy=0., fear=0.)
        n.update(values)
        self.w.reindex()
        return n

    def crime(self, offender, victim):
        e = self.w.emit('theft_exposed', [offender['id'], victim['id']], '当场败露的窃取。', .88,
                        facts={'offender': offender['id'], 'victim': victim['id'], 'transfer': 5.})
        victim['grievance_cause'] = e['id']
        self.w.link(victim, offender, -60, -60)
        return e

    def grudge(self):
        thief, victim = self.adult(), self.adult()
        e = self.crime(thief, victim)
        self.w.set_goal(victim, 'revenge', e['id'])
        return thief, victim

    def tick(self, n, months=1):
        for _ in range(months):
            self.w.month += 1
            self.w.update_goal(n)

    def test_targets_are_relative_to_the_person_and_town(self):
        neighbours = [self.adult(wealth=w, reputation=r) for w, r in ((20, 0), (40, 5), (80, 10), (300, 40), (500, 60))]
        n = self.adult(wealth=30., reputation=2.)
        self.w.set_goal(n, 'wealth', initial=True)
        self.assertEqual(n['goal']['start'], 30.)
        self.assertEqual(n['goal']['target'], 300.)
        self.w.set_goal(n, 'renown', initial=True)
        self.assertEqual(n['goal']['target'], 60.)
        self.w.set_goal(n, 'mastery', initial=True)
        self.assertEqual(n['goal']['target'], n['goal']['start'] + 2)
        n['wealth'] = 165.
        self.assertAlmostEqual(goals.progress(self.w, n, dict(n['goal'], kind='wealth', start=30., target=300.)), .5)
        self.assertTrue(neighbours)

    def test_fulfilled_pursuit_completes_after_a_year_and_a_new_one_begins(self):
        n = self.adult(wealth=30.)
        self.w.set_goal(n, 'wealth', initial=True)
        n['wealth'] = n['goal']['target'] + 1
        self.tick(n, 11)
        self.assertEqual(n['goal']['kind'], 'wealth'); self.assertEqual(n['goal']['progress'], 1.)
        self.tick(n)
        self.assertEqual(self.w.metrics['goals_completed'], 1)
        self.assertEqual(n['past_goals'][-1]['status'], '已达成')

    def test_grudge_names_the_offender_and_ends_with_a_judgment(self):
        thief, victim = self.grudge()
        self.assertEqual(victim['goal']['against'], thief['id'])
        self.assertIn(thief['name'], victim['goal']['title'])
        before = victim['desires']['revenge'] = 80.
        self.w.emit('judgment', [victim['id'], thief['id']], '官府判决。', .9,
                    facts={'offender': thief['id'], 'victim': victim['id'], 'restitution': 5.})
        self.assertEqual(victim['goal']['resolved']['how'], 'judgment')
        self.assertLess(victim['desires']['revenge'], before)
        self.tick(victim)
        done = self.w.store.event(victim['goal']['cause'])
        self.assertEqual(done['kind'], 'goal_complete')
        self.assertEqual(done['facts']['resolution'], 'judgment')
        self.assertNotEqual(victim['goal']['kind'], 'revenge')

    def test_apology_duel_and_death_also_settle_a_grudge(self):
        thief, victim = self.grudge()
        self.w.emit('reconcile', [thief['id'], victim['id']], '赔礼。', .5,
                    facts={'accepted': True, 'giver': thief['id'], 'receiver': victim['id']})
        self.assertEqual(victim['goal']['resolved']['how'], 'reconcile')
        thief, victim = self.grudge()
        self.w.emit('duel', [victim['id'], thief['id']], '比武。', .8, facts={'winner': victim['id'], 'loser': thief['id']})
        self.assertEqual(victim['goal']['resolved']['how'], 'duel')
        thief, victim = self.grudge()
        thief['alive'] = False
        self.tick(victim)
        self.assertEqual(self.w.store.event(victim['goal']['cause'])['facts']['resolution'], 'death')

    def test_unrelated_outcomes_do_not_settle_a_grudge(self):
        thief, victim = self.grudge(); stranger = self.adult()
        self.w.emit('judgment', [victim['id'], stranger['id']], '另案判决。', .9,
                    facts={'offender': stranger['id'], 'victim': victim['id']})
        self.w.emit('duel', [victim['id'], stranger['id']], '另一场比武。', .8,
                    facts={'winner': victim['id'], 'loser': stranger['id']})
        self.assertNotIn('resolved', victim['goal'])

    def test_kind_people_let_go_sooner_and_record_it(self):
        months = {}
        for kindness in (90., 10.):
            thief, victim = self.grudge()
            victim['traits']['kindness'] = kindness
            victim['desires'].update(revenge=70., wealth=40., freedom=40., mastery=40., belonging=40., renown=40.)
            for m in range(1, 60):
                self.tick(victim)
                if victim['goal']['kind'] != 'revenge':
                    months[kindness] = m
                    break
            self.assertEqual(self.w.store.event(victim['goal']['cause'])['kind'], 'grudge_released')
        self.assertLess(months[90.], months[10.])

    def test_legacy_goal_is_rebased_on_todays_situation(self):
        n = self.adult(wealth=30.)
        n['goal'] = dict(kind='mastery', title='武学有成', since=-30, horizon_years=10, target=110., cause=None,
                         progress=0., status='追求中', minimum_months=48)
        before = deepcopy(n['goal'])
        self.assertTrue(goals.upgrade(self.w, n))
        self.assertEqual((n['goal']['kind'], n['goal']['since']), (before['kind'], before['since']))
        self.assertEqual(n['goal']['target'], n['goal']['start'] + 2)
        self.assertFalse(goals.upgrade(self.w, n))

    def test_victims_people_and_ties_deter_and_remember(self):
        rogue = self.adult(); rogue['traits'].update(kindness=15., integrity=15., boldness=40.)
        loner, family = self.adult(), self.adult()
        spouse = self.adult(); family['spouse'] = spouse['id']; spouse['spouse'] = family['id']
        family['faction'] = 0
        self.assertGreater(social.deterrence(self.w, rogue, family), social.deterrence(self.w, rogue, loner) + 8)
        friend = self.adult(); self.w.link(family, friend, 40, 30)
        social.expose(self.w, 'theft_exposed', rogue, family, 5., '败露。')
        self.assertLess(friend['relationships'][str(rogue['id'])]['affinity'], 0)
        self.assertLess(spouse['relationships'][str(rogue['id'])]['trust'], 0)

    def test_many_possible_victims_do_not_multiply_temptation(self):
        n = self.adult(); self.w.set_goal(n, 'wealth', initial=True)
        actions = [{'id': f'steal:{i}', 'kind': 'steal', 'target': i, 'label': ''} for i in range(2, 12)]
        actions += [{'id': 'work:self', 'kind': 'work', 'target': None, 'label': ''}]
        draws = []
        original = self.w.rng.uniform
        with patch.object(self.w.rng, 'uniform', side_effect=lambda a, b: draws.append((a, b)) or original(a, b)), \
             patch.object(self.w, 'score', return_value=0.):
            from jianghu import npc_plans
            npc_plans.choose(self.w, n, actions)
        self.assertEqual(draws.count((-9, 9)), 2)

    def test_trades_are_judged_on_memory_crowding_and_own_rhythm(self):
        self.w = World(7, 0, compact=True, durable=False, market_economy=True)
        self.addCleanup(self.w.store.close)
        n = self.adult(); self.w.set_goal(n, 'freedom', initial=True)
        n['career'].update(profession='农人', income_ema=None)
        base = careers.switch_gain(self.w, n, '艺人')
        careers.note_income(n, 50.)
        self.assertLess(careers.switch_gain(self.w, n, '艺人'), base)
        for _ in range(5):
            q = self.adult(); q['career'].update(profession='书生', last_change=self.w.month, source='自主转业')
        self.assertEqual(careers.entrants(self.w, n, '书生'), 5)
        on = next(m for m in range(3) if (n['id'] + m) % 3 == 0)
        self.w.month = on; a = {'kind': 'change_job', 'target': '艺人'}
        here = careers.score(self.w, n, a)
        self.w.month = on + 1
        off = careers.score(self.w, n, a)
        self.assertEqual(off, -40 - careers.PROFESSIONS['艺人']['cost']*.35)
        self.assertNotEqual(here, off)


if __name__ == '__main__':
    unittest.main()
