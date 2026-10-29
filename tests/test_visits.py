"""人物来访: people with a tie to the player come by for real reasons; the rules settle the answer."""
from copy import deepcopy
import unittest
from unittest.mock import patch

from jianghu import ai, protagonist, reactions, sect_life, visits
from jianghu.engine import World


class VisitTests(unittest.TestCase):
    def setUp(self):
        self.w = w = World(91, 0, compact=True, durable=False)
        self.addCleanup(w.store.close)
        self.place = w.active_places[0]['id']
        protagonist.perform(w, dict(action='create', revision=0, month=0, name='来访验收', sex='女', age=24,
                                   place=self.place))

    @property
    def h(self):
        return self.w.player['protagonist']

    def npc(self, affinity=0., trust=0., **values):
        n = self.w.make_npc(place=self.place)
        n.update(dict(dict(age_months=360, health=100., wealth=60., spouse=None, faction=None), **values))
        n['mood'].update(anger=0., joy=0.)
        self.w.set_goal(n, 'wealth', initial=True)
        self.w.player['ties'][str(n['id'])] = dict(affinity=affinity, trust=trust)
        self.w.reindex()
        return n

    def answer(self, visit, choice):
        return visits.answer(self.w, dict(visit_id=visit['id'], choice=choice, revision=self.h['revision'], month=self.w.month))

    def test_real_reasons_bring_people_and_strangers_do_not_come(self):
        poor_friend = self.npc(30., 10., wealth=5.)
        enemy = self.npc(-50., -30.); enemy['traits']['boldness'] = 80.; enemy['mood']['anger'] = 30.
        stranger = self.npc(); self.w.player['ties'].pop(str(stranger['id']))
        state = self.w.rng.getstate()
        made = visits.monthly(self.w)
        self.assertEqual(self.w.rng.getstate(), state)
        kinds = {v['npc_id']: v['kind'] for v in made}
        self.assertEqual(kinds, {enemy['id']: 'confront', poor_friend['id']: 'ask_help'})
        self.assertEqual(visits.monthly(self.w), [])  # once a month
        shown = visits.view(self.w)
        self.assertEqual({v['npc_id'] for v in shown}, set(kinds))
        self.assertTrue(all(v['opening_source'] == '规则' for v in shown))
        self.assertEqual(protagonist.view(self.w)['visits'], shown)

    def test_at_most_two_a_month_highest_reasons_first_and_they_expire(self):
        for _ in range(4):
            self.npc(45., 20.)
        master = self.npc(10., 5.)
        sect_life.state(self.w)['master'] = master['id']
        self.w.month = 3
        with patch.object(sect_life, 'master', return_value=master):
            made = visits.monthly(self.w)
        self.assertEqual(len(made), 2)
        self.assertEqual(made[0]['kind'], 'summon')
        self.w.month = 4
        again = visits.monthly(self.w)
        self.assertTrue(again and not {v['npc_id'] for v in again} & {v['npc_id'] for v in made})  # others come next
        self.w.month = 5
        visits.monthly(self.w)
        self.assertTrue(all(r['status'] != 'pending' or r['month'] >= 4 for r in visits.rows(self.w)))

    def test_answering_settles_the_choice_and_invalid_answers_change_nothing(self):
        friend = self.npc(30., 10., wealth=5.)
        [visit] = visits.monthly(self.w)
        before, count = deepcopy(self.w.export()), self.w.store.count()
        for bad in (dict(visit_id=visit['id'], choice='steal', revision=self.h['revision'], month=self.w.month),
                    dict(visit_id=visit['id'], choice='give', revision=-1, month=self.w.month),
                    dict(visit_id=999, choice='give', revision=self.h['revision'], month=self.w.month)):
            with self.assertRaises(ValueError):
                visits.answer(self.w, bad)
            self.assertEqual(self.w.export(), before); self.assertEqual(self.w.store.count(), count)
        money, theirs = self.w.player['wealth'], friend['wealth']
        result = self.answer(visit, 'give')
        self.assertEqual(self.w.player['wealth'], money - 4); self.assertEqual(friend['wealth'], theirs + 4)
        tie = self.w.player['ties'][str(friend['id'])]
        self.assertEqual((tie['affinity'], tie['trust']), (34., 13.))
        self.assertEqual(result['event']['kind'], 'visit_answer')
        self.assertIn(visit['event_id'], result['event']['causes'])
        with self.assertRaises(ValueError):
            self.answer(visit, 'give')
        # The visitor then answers in character, as after any other dealing.
        found = reactions.target(self.w, result['event'])
        self.assertEqual(found, (friend['id'], '来找你的人'))

    def test_the_visitor_must_still_be_here_and_money_must_suffice(self):
        friend = self.npc(30., 10., wealth=5.)
        [visit] = visits.monthly(self.w)
        self.w.player['wealth'] = 1.
        self.assertFalse(next(c for c in visits.view(self.w)[0]['choices'] if c['id'] == 'give')['enabled'])
        with self.assertRaises(ValueError):
            self.answer(visit, 'give')
        friend['place'] = next(p['id'] for p in self.w.active_places if p['id'] != self.place)
        self.assertFalse(visits.view(self.w)[0]['here'])
        with self.assertRaises(ValueError):
            self.answer(visit, 'decline')

    def test_the_model_writes_the_opening_in_the_visitors_voice(self):
        friend, other = self.npc(30., 10., wealth=5.), self.npc()
        [visit] = visits.monthly(self.w)
        found = visits.next_to_write(self.w)
        self.assertIsNotNone(found)
        _, ctx = found
        self.assertIn('voice', ctx); self.assertIn('own_life', ctx)
        self.assertFalse(visits.valid(ctx, dict(opening=f"{other['name']}让我来找你借银子。")))
        self.assertFalse(visits.valid(ctx, dict(opening='能借我10两银子吗？')))
        manager = ai.AIManager(dict(enabled=True)); self.addCleanup(manager.close)
        line = '实在不好意思开口，这个月家里揭不开锅，能借我四两银子周转吗？'
        with patch.object(visits, 'model_request', lambda model, c: (dict(opening=line), 1, {}, '')):
            self.assertEqual(manager.write_visit(self.w), 1)
            self.assertEqual(manager.write_visit(self.w), 0)
            manager.jobs.join(); manager.collect(self.w)
        shown = visits.view(self.w)[0]
        self.assertEqual((shown['opening'], shown['opening_source']), (line, 'LLM'))
        self.assertEqual(ai.participation(self.w)['visit_openings'], 1)

    def test_visits_are_registered_by_the_month_step(self):
        self.npc(45., 20.)
        self.w.month_step()
        self.assertEqual([v['kind'] for v in visits.view(self.w)], ['invite'])


if __name__ == '__main__':
    unittest.main()
