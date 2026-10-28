"""长期筹划: goals become multi-month undertakings of real, ordinary actions."""
from copy import deepcopy
import unittest

from jianghu import goals, npc_plans, undertakings
from jianghu.engine import World


class UndertakingTests(unittest.TestCase):
    def setUp(self):
        self.w = World(7, 0, compact=True, durable=False)
        self.addCleanup(self.w.store.close)
        self.place = self.w.factions[0]['place']

    def adult(self, sex='女', **values):
        n = self.w.make_npc(place=self.place)
        n.update(age_months=300, health=100., wealth=60., spouse=None, sex=sex, orientation='异性',
                 pregnancy=None, faction=None, office=None)
        n['mood'].update(anger=0., joy=0., fear=0.)
        n.update(values)
        self.w.reindex()
        return n

    def goal(self, n, kind, cause=None):
        self.w.set_goal(n, kind, cause)
        return n['goal']

    def act(self, kind, target, **extra):
        return dict(id=f'{kind}:{target}', kind=kind, target=target, label=kind, time_cost=1, **extra)

    def begin(self, n):
        n['plan'] = None
        n.pop('undertaking_review', None)
        npc_plans.begin(self.w, n, self.act('rest', 'self'))
        return n['plan']

    def test_courtship_moves_from_friendship_to_courting_to_a_proposal(self):
        n, q = self.adult('女'), self.adult('男')
        self.goal(n, 'belonging')
        p = self.begin(n)
        self.assertEqual((p['intent'], p['target']), ('courtship', q['id']))
        options = [self.act('befriend', q['id']), self.act('court', q['id']), self.act('propose', q['id'])]
        r = self.w.relation(n, q['id'])
        self.assertEqual(undertakings.step(self.w, n, p, options)[1]['kind'], 'befriend')
        r.update(affinity=20)
        self.assertEqual(undertakings.step(self.w, n, p, options)[1]['kind'], 'court')
        r.update(affinity=35, romance=50)
        self.assertEqual(undertakings.step(self.w, n, p, options)[1]['kind'], 'propose')
        # Someone else marrying the person ends the courtship honestly.
        q['spouse'] = 999
        self.assertEqual(undertakings.step(self.w, n, p, options), ('close', f"{q['name']}已另有婚配"))

    def test_a_marriage_completes_the_courtship_even_if_the_other_proposed(self):
        n, q = self.adult('女'), self.adult('男')
        self.goal(n, 'belonging'); self.begin(n)
        n['spouse'], q['spouse'] = q['id'], n['id']
        self.assertIsNone(npc_plans.resolve(self.w, n, []))
        self.assertIsNone(n['plan'])
        self.assertEqual(n['planning_history'][-1]['status'], '完成')
        self.assertEqual(self.w.metrics['undertakings']['courtship']['completed'], 1)

    def test_only_one_step_a_month_and_waiting_does_not_close_the_plan(self):
        n, q = self.adult('女'), self.adult('男')
        self.goal(n, 'belonging'); p = self.begin(n)
        befriend = self.act('befriend', q['id'])
        chosen = npc_plans.resolve(self.w, n, [befriend])
        self.assertEqual(chosen['id'], befriend['id'])
        n['choice_source'] = p['source']
        e = self.w.emit('befriend', [n['id'], q['id']], '往来。', .2)
        npc_plans.record(self.w, n, befriend, e)
        self.assertEqual(p['last_step'], self.w.month)
        self.assertIsNone(npc_plans.resolve(self.w, n, [befriend]))
        self.assertIs(n['plan'], p)
        self.w.month += 1
        self.assertIsNone(npc_plans.resolve(self.w, n, []))  # target busy: wait, keep the plan
        self.assertEqual(p['status'], '等待时机')
        self.assertIs(n['plan'], p)
        self.w.month += undertakings.PATIENCE
        npc_plans.resolve(self.w, n, [])
        self.assertIsNone(n['plan'])
        self.assertEqual(n['planning_history'][-1]['status'], '中断')

    def test_a_grudge_goes_to_court_first_and_cautious_people_do_not_duel(self):
        thief, victim = self.adult('男'), self.adult('女')
        e = self.w.emit('theft_exposed', [thief['id'], victim['id']], '窃取。', .88,
                        facts={'offender': thief['id'], 'victim': victim['id']})
        victim['grievance_cause'] = e['id']; self.w.link(victim, thief, -60, -60)
        self.goal(victim, 'revenge', e['id'])
        p = self.begin(victim)
        self.assertEqual((p['intent'], p['target']), ('grudge', thief['id']))
        report, duel = self.act('report', e['id']), self.act('duel', thief['id'])
        self.assertEqual(undertakings.step(self.w, victim, p, [report, duel])[1]['kind'], 'report')
        victim['traits']['boldness'] = 20
        self.assertEqual(undertakings.step(self.w, victim, p, [duel])[0], 'wait')
        victim['traits']['boldness'] = 80
        self.assertEqual(undertakings.step(self.w, victim, p, [duel])[1]['kind'], 'duel')
        # However the grievance is settled, the undertaking ends complete.
        goals.resolve(self.w, victim, 'judgment', e['id'])
        self.assertIsNone(victim['plan'])
        self.assertEqual(victim['planning_history'][-1]['status'], '完成')

    def test_a_journey_survives_moving_and_ends_after_new_places(self):
        n = self.adult(wealth=120.)
        self.goal(n, 'freedom'); p = self.begin(n)
        self.assertEqual(p['intent'], 'journey')
        unseen = [d for d in self.w.neighbors[n['place']] if d not in n['visited_places']]
        trips = [self.act('travel', d, cost=3., migration=None) for d in unseen]
        chosen = undertakings.step(self.w, n, p, trips)[1]
        self.assertNotIn(chosen['target'], n['visited_places'])
        # Too poor for the road: wait and keep working instead of spending the last savings.
        n['wealth'] = 20.
        self.assertEqual(undertakings.step(self.w, n, p, trips)[0], 'wait')
        n['wealth'] = 120.
        n['choice_source'] = p['source']; p.update(next_id=chosen['id'], status='执行中')
        n['place'] = chosen['target']; n['visited_places'].append(chosen['target'])
        e = self.w.emit('travel', [n['id']], '远行。', .2)
        npc_plans.record(self.w, n, chosen, e)
        self.assertIs(n['plan'], p)
        self.assertEqual(p['place'], n['place'])
        self.w.month += 1
        npc_plans.resolve(self.w, n, [])
        self.assertIs(n['plan'], p)  # moving is the point of a journey
        n['visited_places'].append('elsewhere')
        npc_plans.resolve(self.w, n, [])
        self.assertIsNone(n['plan'])
        self.assertEqual(n['planning_history'][-1]['reason'], f'已游历{undertakings.JOURNEY_PLACES}处新地方')

    def test_a_spouse_at_home_or_a_small_child_keeps_people_from_long_journeys(self):
        n, q = self.adult('女', wealth=120.), self.adult('男')
        self.goal(n, 'freedom')
        n['spouse'], q['spouse'] = q['id'], n['id']
        self.assertIsNone(undertakings.propose(self.w, n))
        n['spouse'] = q['spouse'] = None
        child = self.adult('男', age_months=30); n['children'].append(child['id'])
        self.assertIsNone(undertakings.propose(self.w, n))
        child['age_months'] = 200
        self.assertEqual(undertakings.propose(self.w, n)[0], 'journey')

    def test_standing_counts_real_good_deeds(self):
        n, q = self.adult(), self.adult('男', wealth=5.)
        self.goal(n, 'renown'); p = self.begin(n)
        self.assertEqual(p['intent'], 'standing')
        aid = self.act('aid', q['id'])
        for month in range(undertakings.STANDING_DEEDS):
            self.w.month += 1
            chosen = npc_plans.resolve(self.w, n, [self.act('work', 'self'), aid])
            self.assertEqual(chosen['kind'], 'aid')
            n['choice_source'] = p['source']
            npc_plans.record(self.w, n, chosen, self.w.emit('aid', [n['id'], q['id']], '接济。', .5))
        self.assertIsNone(n['plan'])
        self.assertEqual(n['planning_history'][-1]['status'], '完成')

    def test_the_counterpart_stays_in_view_and_choosing_is_pure(self):
        n = self.adult('女')
        others = [self.adult('男') for _ in range(12)]
        target = others[-1]
        self.goal(n, 'belonging')
        state, before, count = self.w.rng.getstate(), deepcopy(n), self.w.store.count()
        found = undertakings.propose(self.w, n)
        self.assertEqual(self.w.rng.getstate(), state); self.assertEqual(n, before)
        self.assertEqual(self.w.store.count(), count)
        self.assertIsNotNone(found)
        npc_plans.start(self.w, n, 'courtship', '追求', [], target=target['id'], target_name=target['name'], rebuffs=0)
        ids = {a['id'] for a in self.w.legal_actions(n, planning=True)}
        self.assertIn(f"befriend:{target['id']}", ids)
        view = npc_plans.view(self.w, n)['current']
        self.assertEqual(view['undertaking'], '求偶成家')
        self.assertTrue(view['next_description'])


if __name__ == '__main__':
    unittest.main()
