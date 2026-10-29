"""The local model chooses long undertakings (with a named counterpart); rules carry them out."""
from copy import deepcopy
import unittest

from jianghu import ai, npc_plans, undertakings
from jianghu.engine import World


def proposal(ctx, undertaking, goal=None, kind='work'):
    action = next((a for a in ctx['allowed_actions'] if a['kind'] == kind), ctx['allowed_actions'][0])
    return dict(action_id=action['id'], plan=[action['id']], goal=goal or ctx['allowed_goals'][0],
                reason='先成家，再顾生计', undertaking=undertaking,
                life_policy=dict(title='成家为先', priorities=[dict(kind=action['kind'], weight=6)]),
                reflection=dict(event_ids=[], interpretation='按眼前处境安排', trait='none', direction=0,
                                belief='none', belief_direction=0))


class LLMUndertakingTests(unittest.TestCase):
    def setUp(self):
        self.w = World(7, 0, compact=True, durable=False)
        self.addCleanup(self.w.store.close)
        self.place = self.w.factions[0]['place']

    def adult(self, sex='女', **values):
        n = self.w.make_npc(place=self.place)
        n.update(age_months=300, health=100., wealth=60., spouse=None, sex=sex, orientation='异性',
                 pregnancy=None, faction=None, office=None)
        n.update(values)
        self.w.reindex()
        return n

    def courting(self):
        n = self.adult('女')
        men = [self.adult('男') for _ in range(3)]
        self.w.set_goal(n, 'belonging')
        return n, men

    def test_context_offers_named_pursuits_and_the_schema_requires_a_choice(self):
        n, men = self.courting()
        ctx = ai.context_for(self.w, n)
        ids = [u['id'] for u in ctx['undertakings']]
        self.assertEqual(ids[-1], 'none')
        self.assertEqual({f"courtship:{q['id']}" for q in men}, set(ids[:-1]))
        self.assertTrue(all(u['goal'] == 'belonging' for u in ctx['undertakings'][:-1]))
        schema = ai.response_schema(ctx)
        self.assertEqual(schema['properties']['undertaking']['enum'], ids)
        self.assertIn('undertaking', schema['required'])

    def test_the_model_can_pick_someone_other_than_the_rules_favourite(self):
        n, men = self.courting()
        favourite = undertakings.suitor(self.w, n)
        other = next(q for q in men if q['id'] != favourite['id'])
        ctx = ai.context_for(self.w, n)
        ok, reason = ai.apply_decision(self.w, ctx, proposal(ctx, f"courtship:{other['id']}", 'belonging'))
        self.assertTrue(ok, reason)
        p = n['plan']
        self.assertEqual((p['intent'], p['target'], p['source']), ('courtship', other['id'], 'AI'))
        self.assertEqual(p['ai_reason'], '先成家，再顾生计')
        self.assertTrue(p['interim'])
        self.assertEqual(npc_plans.view(self.w, n)['current']['chosen_by'], '本地模型选定')
        self.assertEqual(self.w.metrics['ai_undertakings_chosen'], 1)
        # The engine keeps the chosen person in view, then executes steps as an AI decision.
        ids = {a['id'] for a in self.w.legal_actions(n, planning=True)}
        self.assertIn(f"befriend:{other['id']}", ids)

    def test_invalid_or_stale_choices_change_nothing(self):
        n, men = self.courting()
        ctx = ai.context_for(self.w, n)
        before, count = deepcopy(self.w.export()), self.w.store.count()
        for bad in (proposal(ctx, 'courtship:99999', 'belonging'),
                    proposal(ctx, f"courtship:{men[0]['id']}", 'wealth') if 'wealth' in ctx['allowed_goals'] else None):
            if bad is None:
                continue
            self.assertFalse(ai.apply_decision(self.w, ctx, bad)[0])
            self.assertEqual(self.w.export(), before)
        # The chosen person marries someone else before the proposal arrives.
        men[1]['spouse'] = men[2]['id']; men[2]['spouse'] = men[1]['id']
        before = deepcopy(self.w.export())
        ok, reason = ai.apply_decision(self.w, ctx, proposal(ctx, f"courtship:{men[1]['id']}", 'belonging'))
        self.assertFalse(ok)
        self.assertEqual(reason, '所选长期筹划的条件已经改变')
        self.assertEqual(self.w.export(), before)
        self.assertEqual(self.w.store.count(), count)

    def test_keep_continues_the_pursuit_and_the_short_plan_fills_other_weeks(self):
        n, men = self.courting()
        n.pop('undertaking_review', None)
        npc_plans.begin(self.w, n, dict(id='rest:self', kind='rest', target=None, label='休养'))
        p = n['plan']
        self.assertEqual(p['intent'], 'courtship')
        ctx = ai.context_for(self.w, n)
        self.assertEqual(ctx['undertakings'][0]['id'], 'keep')
        work = next(a for a in ctx['allowed_actions'] if a['kind'] == 'work')
        self.assertTrue(ai.apply_decision(self.w, ctx, proposal(ctx, 'keep', 'belonging'))[0])
        self.assertIs(n['plan'], p)
        self.assertEqual(p['source'], 'AI')
        # This month's pursuit step is already taken: the model's errand runs instead.
        p['last_step'] = self.w.month
        chosen = npc_plans.resolve(self.w, n, [work])
        self.assertEqual(chosen['id'], work['id'])
        n['choice_source'] = 'AI'
        npc_plans.record(self.w, n, chosen, self.w.emit('work', [n['id']], '谋生。', .2))
        self.assertNotIn(work['id'], p['interim'])
        self.assertTrue(p['completed'][-1]['interim'])
        self.assertEqual(p['last_progress'], p['created'])  # an errand is not progress on the pursuit

    def test_none_sets_pursuits_aside_for_a_season(self):
        n, men = self.courting()
        ctx = ai.context_for(self.w, n)
        self.assertTrue(ai.apply_decision(self.w, ctx, proposal(ctx, 'none', 'belonging'))[0])
        self.assertIsNone(n['plan'].get('intent'))
        n['plan'] = None; n.pop('undertaking_review', None)
        npc_plans.begin(self.w, n, dict(id='rest:self', kind='rest', target=None, label='休养'))
        self.assertEqual(n['plan']['intent'], 'sequence')
        self.w.month += 3; n['plan'] = None
        npc_plans.begin(self.w, n, dict(id='rest:self', kind='rest', target=None, label='休养'))
        self.assertEqual(n['plan']['intent'], 'courtship')

    def test_a_legacy_proposal_without_a_choice_keeps_a_running_pursuit(self):
        n, men = self.courting()
        n.pop('undertaking_review', None)
        npc_plans.begin(self.w, n, dict(id='rest:self', kind='rest', target=None, label='休养'))
        p = n['plan']
        ctx = ai.context_for(self.w, n)
        result = proposal(ctx, None, 'belonging'); result.pop('undertaking')
        self.assertTrue(ai.apply_decision(self.w, ctx, result)[0])
        self.assertIs(n['plan'], p)


if __name__ == '__main__':
    unittest.main()
