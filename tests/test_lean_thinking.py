"""Lean background thinking and turning points: short requests, and thought spent where lives turn."""
from copy import deepcopy
import json
import unittest
from unittest.mock import patch

from jianghu import ai, npc_attention, turning
from jianghu.engine import World


def lean_answer(ctx, stance=None):
    value = dict(action_id=ctx['allowed_actions'][0]['id'], goal=ctx['allowed_goals'][0], note='日子还得过下去')
    if ctx.get('undertakings'):
        value['undertaking'] = 'none'
    if stance:
        value['stance'] = stance
    return value


class LeanThinkingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.base = World(7, 300, compact=True, durable=False, population_profile=True, market_economy=True)
        for _ in range(2):
            cls.base.month_step()

    @classmethod
    def tearDownClass(cls):
        cls.base.store.close()

    def setUp(self):
        self.w = World(7, 0, compact=True, durable=False)
        self.addCleanup(self.w.store.close)
        self.place = self.w.active_places[0]['id']

    def adult(self, **values):
        n = self.w.make_npc(place=self.place)
        n.update(dict(dict(age_months=360, health=100., wealth=40., spouse=None, faction=None, office=None), **values))
        n['mood'].update(anger=0., joy=0.)
        self.w.set_goal(n, 'wealth', initial=True)
        self.w.reindex()
        return n

    def test_background_people_get_a_short_request(self):
        w = self.base
        n = next(n for n in w.npcs.values() if n['alive'] and n['age_months'] >= 240)
        ctx = ai.context_for(w, n)
        self.assertTrue(ai.lean(ctx))
        full = len(json.dumps({k: v for k, v in ctx.items() if not k.startswith('_')}, ensure_ascii=False))
        wire = ai.lean_wire(ctx)
        short = len(json.dumps(wire, ensure_ascii=False, separators=(',', ':')))
        self.assertLess(short, 5000); self.assertLess(short * 2, full)
        self.assertLessEqual(len(wire['allowed_actions']), ai.LEAN_ACTIONS)
        self.assertIn('own_life', wire); self.assertNotIn('baseline_action', wire)
        # The first thought about someone also writes their creed (tests/test_creed.py).
        self.assertEqual(set(ai.lean_schema(ctx)['required']), {'action_id', 'goal', 'note', 'undertaking', 'creed'})
        captured = []
        def complete(model, messages, schema, budget, stage):
            captured.append((budget, stage, schema))
            return lean_answer(ctx), '{}', dict(prompt_tokens=900, completion_tokens=30), 1.
        with patch.object(ai.LocalModel, 'complete', complete):
            result, _, usage, _ = ai.LocalModel(ai.continuous_config()).request(ctx)
        self.assertEqual((captured[0][0], captured[0][1]), (230, '简短决定'))
        self.assertEqual(usage['request_count'], 1)
        self.assertEqual(result['plan'], [result['action_id']])
        # People the player deals with keep the full request.
        deep = ai.context_for(w, n, dict(level='deep', label='深入思考', reasons=[], memory_limit=6, contact_limit=6, action_limit=24))
        self.assertFalse(ai.lean(deep))

    def test_a_short_answer_is_applied_like_a_full_one(self):
        n = self.adult()
        ctx = ai.context_for(self.w, n)
        ok, reason = ai.apply_decision(self.w, ctx, ai.expand(ctx, lean_answer(ctx)))
        self.assertTrue(ok, reason)
        self.assertEqual(n['plan']['source'], 'AI'); self.assertEqual(n['plan']['reason'], '日子还得过下去')
        self.assertEqual(self.w.metrics['ai_lean_applied'], 1)
        bad = ai.expand(ctx, {})
        self.assertFalse(ai.apply_decision(self.w, ai.context_for(self.w, n), bad)[0])

    def test_turning_points_are_noted_for_the_people_whose_lives_turn(self):
        mother, son, child = self.adult(), self.adult(), self.adult(age_months=60)
        thief, victim = self.adult(), self.adult()
        e = self.w.emit('death', [mother['id'], son['id'], child['id']], '母亲离世。', 1.,
                        facts=dict(deceased=mother['id'], heirs=[son['id'], child['id']]))
        self.assertEqual([t['role'] for t in son['turning']], ['bereaved'])
        self.assertFalse(child.get('turning'))  # children are not asked
        self.w.emit('betrayal', [thief['id'], victim['id']], '背信。', .88, facts=dict(offender=thief['id'], victim=victim['id']))
        self.assertEqual(victim['turning'][0]['role'], 'victim'); self.assertEqual(thief['turning'][0]['role'], 'offender')
        for i in range(3):
            self.w.emit('goal_complete', [son['id']], f'心愿{i}。', .85)
        self.assertEqual(len(son['turning']), 2)
        self.assertEqual(son['turning'][0]['event_id'], e['id'])  # the most significant is kept
        self.w.month += turning.LIFETIME
        self.assertEqual(turning.pending(self.w, son), [])

    def test_turning_points_take_slots_before_rotation_but_not_all_of_them(self):
        people = [self.adult() for _ in range(6)]
        for n in people[:3]:
            self.w.emit('marriage', [n['id']], '成亲。', .95)
        manager = ai.AIManager(dict(enabled=True, queue_limit=4)); self.addCleanup(manager.close)
        seen = []
        def request(model, ctx):
            seen.append((ctx['npc_id'], bool(ctx.get('turning_point'))))
            return ai.expand(ctx, lean_answer(ctx, ctx['turning_point']['stances'][0]['id'] if ctx.get('turning_point') else None)), 1, {}, ''
        with patch.object(ai.LocalModel, 'request', request):
            self.assertEqual(manager.schedule(self.w), 4)
            manager.jobs.join(); manager.collect(self.w)
        turned = [i for i, t in seen if t]
        self.assertEqual(len(turned), 2)
        self.assertTrue(set(turned) <= {n['id'] for n in people[:3]})
        self.assertEqual(self.w.metrics['turning_points_decided'], 2)
        decided = next(n for n in people if n['id'] == turned[0])
        self.assertEqual(decided['turning_history'][-1]['note'], '日子还得过下去')
        self.assertEqual(ai.participation(self.w)['turning_decided'], 2)

    def test_a_stance_has_a_small_effect_and_an_invalid_one_changes_nothing(self):
        thief, victim = self.adult(), self.adult()
        self.w.link(victim, thief, -40, -40)
        e = self.w.emit('theft_exposed', [thief['id'], victim['id']], f"{thief['name']}窃取{victim['name']}五银。", .88,
                        facts=dict(offender=thief['id'], victim=victim['id']))
        [t] = turning.pending(self.w, victim)
        ctx = ai.turning_context(self.w, victim, t)
        self.assertIn('revenge', ctx['allowed_goals'])
        self.assertEqual([s['id'] for s in ctx['turning_point']['stances']], list(turning.FITS['victim']))
        self.assertIn('stance', ai.lean_schema(ctx)['required'])
        before = deepcopy(self.w.export())
        self.assertFalse(ai.apply_decision(self.w, ctx, ai.expand(ctx, lean_answer(ctx, 'rejoice')))[0])
        self.assertEqual(self.w.export(), before)
        victim['desires']['revenge'] = 50.
        affinity = self.w.relation(victim, thief['id'])['affinity']
        self.assertTrue(ai.apply_decision(self.w, ctx, ai.expand(ctx, lean_answer(ctx, 'forgive')))[0])
        self.assertEqual(victim['desires']['revenge'], 35.)
        self.assertEqual(self.w.relation(victim, thief['id'])['affinity'], affinity + 5)
        self.assertEqual(turning.pending(self.w, victim), [])
        memory = next(m for m in victim['memories'] if m['event_id'] == e['id'])
        self.assertEqual(memory['interpretation'], '日子还得过下去')
        # An answer without a stance (older shape) leaves the point for a later thought.
        self.w.emit('betrayal', [thief['id'], victim['id']], '又背信。', .88, facts=dict(offender=thief['id'], victim=victim['id']))
        [t] = turning.pending(self.w, victim)
        ctx = ai.turning_context(self.w, victim, t)
        self.assertTrue(ai.apply_decision(self.w, ctx, ai.expand(ctx, lean_answer(ctx)))[0])
        self.assertEqual(len(turning.pending(self.w, victim)), 1)


if __name__ == '__main__':
    unittest.main()
