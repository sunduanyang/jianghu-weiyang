"""人物回应: settled outcomes stay fixed; the person concerned answers in character with a small, bounded effect."""
from copy import deepcopy
import unittest
from unittest.mock import patch

from jianghu import ai, player_interactions, protagonist, reactions, sect_life
from jianghu.engine import World


class ReactionTests(unittest.TestCase):
    def setUp(self):
        self.w = w = World(91, 0, compact=True, durable=False, rank_assessments=True)
        self.addCleanup(w.store.close)
        self.f = w.factions[0]
        protagonist.perform(w, dict(action='create', revision=0, month=0, name='回应验收', sex='女', age=24,
                                   place=self.f['place']))

    @property
    def h(self):
        return self.w.player['protagonist']

    def npc(self, **values):
        n = self.w.make_npc(place=self.f['place'])
        n.update(age_months=360, health=100., wealth=60., spouse=None, faction=None)
        n['mood'].update(anger=0., joy=0.)
        n.update(values)
        self.w.set_goal(n, 'wealth', initial=True)
        self.w.reindex()
        return n

    def act(self, n, action, **extra):
        return player_interactions.perform(self.w, dict(npc_id=n['id'], action=action, revision=self.h['revision'],
                                                        month=self.w.month, **extra))

    def answer(self, ctx, attitude=None, reply='多谢你的好意，我记下了。', thought='此人倒还懂些礼数。'):
        return dict(attitude=attitude or ctx['attitudes'][0]['id'], reply=reply, thought=thought)

    def test_the_person_acted_upon_answers_and_only_fitting_attitudes_are_offered(self):
        n = self.npc()
        since = self.w.store.next_id
        self.act(n, 'praise')
        [ctx] = reactions.queue(self.w, since)
        self.assertEqual((ctx['npc_id'], ctx['role']), (n['id'], '被称赞的人'))
        self.assertEqual([a['id'] for a in ctx['attitudes']], list(reactions.FAVOURABLE))
        since = self.w.store.next_id
        self.act(n, 'insult')
        [ctx] = reactions.queue(self.w, since)
        self.assertIn('vengeful', [a['id'] for a in ctx['attitudes']])
        self.assertNotIn('grateful', [a['id'] for a in ctx['attitudes']])
        self.assertEqual(len(reactions.state(self.w)['rows']), 2)

    def test_an_unnoticed_theft_gets_no_answer(self):
        n = self.npc()
        since = self.w.store.next_id
        with patch.object(self.w.rng, 'random', return_value=.999):
            n['traits']['boldness'] = 100.
            with patch.object(player_interactions, 'theft_chance', return_value=1.):
                self.act(n, 'steal')
        self.assertEqual(reactions.queue(self.w, since), [])

    def test_a_valid_answer_is_recorded_with_a_small_effect_and_kept_in_memory(self):
        n = self.npc()
        since = self.w.store.next_id
        praise = self.act(n, 'praise')['event']
        [ctx] = reactions.queue(self.w, since)
        tie = deepcopy(self.w.player['ties'][str(n['id'])])
        ok, _ = reactions.apply(self.w, ctx, self.answer(ctx, 'wary', '你忽然夸我，可是有事相求？', '无事献殷勤，得留个心眼。'))
        self.assertTrue(ok)
        after = self.w.player['ties'][str(n['id'])]
        self.assertEqual(after['affinity'], tie['affinity'])
        self.assertEqual(after['trust'], round(tie['trust'] - 2, 4))
        e = self.w.store.event(after['last_event'])
        self.assertEqual(e['kind'], 'player_reaction')
        self.assertIn(praise['id'], e['causes'])
        self.assertEqual(e['facts']['attitude_label'], '心存戒备')
        memory = next(m for m in n['memories'] if m['event_id'] == praise['id'])
        self.assertEqual(memory['interpretation'], '无事献殷勤，得留个心眼。')
        self.assertTrue(memory['ai'])
        view = reactions.view(self.w, n['id'])[-1]
        self.assertEqual((view['status'], view['attitude_label']), ('completed', '心存戒备'))
        self.assertEqual(player_interactions.view(self.w, n['id'])['reactions'][-1]['reply'], '你忽然夸我，可是有事相求？')
        # The same answer cannot be applied twice.
        self.assertFalse(reactions.apply(self.w, ctx, self.answer(ctx))[0])

    def test_invalid_or_late_answers_change_nothing_but_the_row(self):
        n = self.npc()
        since = self.w.store.next_id
        self.act(n, 'insult')
        [ctx] = reactions.queue(self.w, since)
        before, count = deepcopy(self.w.player['ties']), self.w.store.count()
        for bad in (dict(attitude='grateful', reply='多谢。', thought='好人。'),          # not offered for an insult
                    dict(attitude='resentful', reply='好感度下降了', thought='哼。'),     # numbers talk
                    dict(attitude='resentful', reply='OK', thought='fine'),                 # no Chinese
                    dict(attitude='resentful', reply='哼。', thought='记下了。', extra=1)):
            self.assertFalse(reactions.valid(ctx, bad))
        self.assertFalse(reactions.apply(self.w, ctx, dict(attitude='grateful', reply='多谢。', thought='好人。'))[0])
        self.assertEqual(reactions.row(self.w, ctx['_row'])['status'], 'failed')
        self.assertEqual(self.w.player['ties'], before)
        self.assertEqual(self.w.store.count(), count)
        since = self.w.store.next_id
        self.act(n, 'praise')
        [late] = reactions.queue(self.w, since)
        self.w.month += 2
        self.assertFalse(reactions.apply(self.w, late, self.answer(late))[0])
        self.assertEqual(reactions.row(self.w, late['_row'])['status'], 'expired')
        self.assertEqual(self.w.store.count(), count + 1)  # only the second, real praise event

    def test_vengeance_and_forgiveness_touch_the_persons_own_feelings(self):
        n = self.npc()
        since = self.w.store.next_id
        self.act(n, 'insult')
        [ctx] = reactions.queue(self.w, since)
        anger, revenge = n['mood']['anger'], n['desires']['revenge']
        self.assertTrue(reactions.apply(self.w, ctx, self.answer(ctx, 'vengeful', '今日之辱，我记下了。', '此仇必报。'))[0])
        self.assertGreater(n['mood']['anger'], anger)
        self.assertGreater(n['desires']['revenge'], revenge)

    def test_sect_events_are_answered_by_the_issuer_or_master(self):
        elder = self.npc(faction=0, contribution=35.)
        from tests.combat_helpers import set_rating
        set_rating(elder, 45.)
        from jianghu import rank_progression
        rank_progression.prepare(self.w, elder['id'])
        self.act(elder, 'join_sect')
        e = protagonist.record(self.w, 'hero_sect_task', '办妥差事。', dict(issuer=elder['id'], faction=0))
        found = reactions.target(self.w, e)
        self.assertEqual(found, (elder['id'], '派下差事的人'))
        sect_life.state(self.w)['master'] = elder['id']
        e = protagonist.record(self.w, 'hero_sect_discipline', '记过。', dict(faction=0))
        self.assertEqual(reactions.target(self.w, e), (elder['id'], '你的师父'))

    def test_the_manager_queues_answers_and_applies_them_when_they_arrive(self):
        n = self.npc()
        manager = ai.AIManager(dict(enabled=True)); self.addCleanup(manager.close)
        since = self.w.store.next_id
        self.act(n, 'praise')
        reply = lambda model, ctx: (self.answer(ctx, 'grateful'), 1, {}, 'unit-test mock')
        with patch.object(reactions, 'model_request', reply):
            self.assertEqual(manager.react(self.w, since), 1)
            self.assertEqual(manager.view()['reaction_pending'], 1)
            manager.jobs.join(); manager.collect(self.w)
        self.assertEqual(reactions.view(self.w, n['id'])[-1]['attitude'], 'grateful')
        self.assertEqual(self.w.metrics['player_reactions'], 1)
        self.assertEqual(manager.view()['reaction_pending'], 0)
        off = ai.AIManager(dict(enabled=False)); self.addCleanup(off.close)
        since = self.w.store.next_id
        self.act(n, 'praise')
        self.assertEqual(off.react(self.w, since), 0)

    def test_the_http_route_asks_for_answers_only_after_the_settlement_succeeds(self):
        import json, threading
        from http.server import ThreadingHTTPServer
        from types import SimpleNamespace
        from urllib.error import HTTPError
        from urllib.request import Request, urlopen
        from jianghu.server import handler_for
        n = self.npc(); calls = []
        game = SimpleNamespace(world=self.w, lock=threading.RLock(), autoplay=False, job=dict(remaining=0),
                               ai=SimpleNamespace(react=lambda w, since: calls.append(since)))
        server = ThreadingHTTPServer(('127.0.0.1', 0), handler_for(game))
        threading.Thread(target=server.serve_forever, daemon=True).start()
        self.addCleanup(server.server_close); self.addCleanup(server.shutdown)
        def post(body):
            request = Request(f'http://127.0.0.1:{server.server_port}/api/interaction', json.dumps(body).encode(),
                              {'Content-Type': 'application/json'})
            with urlopen(request, timeout=5) as r:
                return json.load(r)
        since = self.w.store.next_id
        post(dict(npc_id=n['id'], action='praise', revision=self.h['revision'], month=self.w.month))
        self.assertEqual(calls, [since])
        with self.assertRaises(HTTPError):
            post(dict(npc_id=n['id'], action='praise', revision=-1, month=self.w.month))
        self.assertEqual(len(calls), 1)

    def test_recovery_marks_interrupted_answers(self):
        n = self.npc()
        since = self.w.store.next_id
        self.act(n, 'praise')
        reactions.queue(self.w, since)
        self.assertTrue(reactions.recover(self.w))
        self.assertEqual(reactions.state(self.w)['rows'][-1]['status'], 'failed')


if __name__ == '__main__':
    unittest.main()
