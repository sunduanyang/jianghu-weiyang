"""人生信条: the model writes who a person is; every later weekly choice is weighed by it."""
from copy import deepcopy
import unittest
from unittest.mock import patch

from jianghu import ai, creed, goals
from jianghu.engine import World


def a_creed(**changes):
    value = dict(motto='宁折不弯', values=['公道', '亲情'], bottom_line='不欺弱小', when_wronged='讨回公道',
                 temptation='不为所动', dream='renown', leanings=[dict(kind='gamble', weight=-12)])
    value.update(changes)
    return value


class CreedTests(unittest.TestCase):
    def setUp(self):
        self.w = World(7, 0, compact=True, durable=False)
        self.addCleanup(self.w.store.close)
        self.place = self.w.active_places[0]['id']

    def adult(self, **values):
        n = self.w.make_npc(place=self.place)
        n.update(dict(dict(age_months=360, health=100., wealth=60., spouse=None, faction=None, office=None), **values))
        self.w.set_goal(n, 'wealth', initial=True)
        self.w.reindex()
        return n

    def act(self, kind, target=None):
        return dict(id=f'{kind}:{target}', kind=kind, target=target, label=kind, time_cost=1)

    def test_only_well_formed_creeds_are_accepted(self):
        self.assertTrue(creed.valid(a_creed()))
        for bad in (a_creed(values=['公道']), a_creed(values=['公道', '公道']), a_creed(bottom_line='杀人放火'),
                    a_creed(motto='我是模型'), a_creed(motto='八8'), a_creed(dream='immortality'),
                    a_creed(leanings=[dict(kind='mint_silver', weight=6)]), a_creed(leanings=[dict(kind='work', weight=30)]),
                    a_creed(leanings=[dict(kind='work', weight=6)] * 2), dict(a_creed(), extra=1)):
            self.assertFalse(creed.valid(bad), bad)

    def test_people_without_a_creed_act_exactly_as_before(self):
        n, q = self.adult(), self.adult(wealth=5.)
        for kind in ('steal', 'work', 'duel', 'gamble', 'report'):
            self.assertEqual(creed.adjustment(self.w, n, self.act(kind, q['id'])), 0.)
        self.assertEqual(creed.grudge_fade(n), 1.); self.assertEqual(creed.dream_bonus(n, 'renown'), 0.)
        self.assertTrue(creed.due(self.w, n))
        self.assertFalse(creed.view(self.w, n)['written'])

    def test_bottom_lines_and_temptation_shape_wrongdoing(self):
        n, poor, rich = self.adult(), self.adult(wealth=5.), self.adult(wealth=500.)
        creed.apply(self.w, n, a_creed())
        steal_poor = creed.adjustment(self.w, n, self.act('steal', poor['id']))
        steal_rich = creed.adjustment(self.w, n, self.act('steal', rich['id']))
        self.assertLessEqual(steal_poor, -60); self.assertLess(steal_poor, steal_rich)
        creed.apply(self.w, n, a_creed(bottom_line='无所顾忌', temptation='见利忘义', values=['财富', '自在']))
        self.assertGreater(creed.adjustment(self.w, n, self.act('betray', rich['id'])), 0)
        spouse = self.adult(wealth=500.); n['spouse'] = spouse['id']
        creed.apply(self.w, n, a_creed(bottom_line='不负亲人', temptation='量力而取'))
        self.assertLessEqual(creed.adjustment(self.w, n, self.act('steal', spouse['id'])), -60)

    def test_values_and_leanings_pull_everyday_choices(self):
        n = self.adult()
        creed.apply(self.w, n, a_creed(values=['自在', '财富'], leanings=[dict(kind='gamble', weight=-12)]))
        self.assertGreater(creed.adjustment(self.w, n, self.act('travel')), 0)
        self.assertLess(creed.adjustment(self.w, n, self.act('gamble')), 0)
        # The engine's weekly score includes the creed.
        trip = dict(self.act('travel', self.w.neighbors[self.place][0]), cost=1., border='', migration=None)
        with_creed = self.w.score(n, trip)
        written = n.pop('creed')
        self.assertAlmostEqual(with_creed - self.w.score(n, trip), creed.adjustment(self.w, dict(n, creed=written), trip))
        self.assertGreater(with_creed, self.w.score(n, trip))

    def test_how_a_wrong_is_answered_changes_duels_reports_and_how_grudges_fade(self):
        hot, calm = self.adult(), self.adult()
        creed.apply(self.w, hot, a_creed(when_wronged='以牙还牙'))
        creed.apply(self.w, calm, a_creed(when_wronged='一笑置之'))
        self.assertGreater(creed.adjustment(self.w, hot, self.act('duel', calm['id'])),
                           creed.adjustment(self.w, calm, self.act('duel', hot['id'])))
        for n in (hot, calm):
            n['desires']['revenge'] = 60.; n['traits']['kindness'] = 50.
            goals.monthly(self.w, n)
        self.assertGreater(hot['desires']['revenge'], calm['desires']['revenge'])

    def test_the_dream_weighs_in_when_a_new_wish_is_chosen(self):
        n = self.adult()
        for k in n['desires']:
            n['desires'][k] = 50.
        creed.apply(self.w, n, a_creed(dream='freedom'))
        state = self.w.rng.getstate()
        with patch.object(self.w.rng, 'uniform', return_value=0.):
            self.assertEqual(self.w.best_goal(n), 'freedom')
        self.w.rng.setstate(state)

    def test_the_model_writes_the_creed_with_a_decision_and_reviews_it_after_years(self):
        n = self.adult()
        ctx = ai.context_for(self.w, n)
        self.assertTrue(ctx['creed_due'])
        self.assertIn('creed', ai.lean_schema(ctx)['required'])
        self.assertIn('creed', ai.response_schema(ctx)['required'])
        answer = dict(action_id=ctx['allowed_actions'][0]['id'], goal=ctx['allowed_goals'][0], note='做人要有骨气',
                      undertaking='none', creed=a_creed(motto='做人要有骨气'))
        bad = deepcopy(answer); bad['creed']['bottom_line'] = '杀人放火'
        before = deepcopy(self.w.export())
        self.assertFalse(ai.apply_decision(self.w, ctx, ai.expand(ctx, bad))[0])
        self.assertEqual(self.w.export(), before)
        self.assertTrue(ai.apply_decision(self.w, ctx, ai.expand(ctx, answer))[0])
        self.assertEqual(n['creed']['motto'], '做人要有骨气'); self.assertEqual(n['creed']['source'], 'LLM')
        later = ai.context_for(self.w, n)
        self.assertFalse(later['creed_due']); self.assertNotIn('creed', ai.lean_schema(later)['required'])
        self.assertEqual(ai.lean_wire(later)['creed']['motto'], '做人要有骨气')
        self.assertIn('信条「做人要有骨气」', creed.view(self.w, n)['text'])
        self.assertEqual(ai.participation(self.w)['creeds'], 1)
        self.w.month += creed.REVIEW_MONTHS
        self.assertTrue(creed.due(self.w, n))
        creed.apply(self.w, n, a_creed(motto='退一步海阔天空'))
        self.assertEqual(n['creed_history'][-1]['motto'], '做人要有骨气')


if __name__ == '__main__':
    unittest.main()
