"""江湖传闻: newsworthy events travel a town a month; locals may retell them without changing who did what."""
from copy import deepcopy
import unittest
from unittest.mock import patch

from jianghu import ai, hearsay, protagonist
from jianghu.engine import World


class HearsayTests(unittest.TestCase):
    def setUp(self):
        self.w = w = World(91, 0, compact=True, durable=False)
        self.addCleanup(w.store.close)
        self.home = w.active_places[0]['id']
        protagonist.perform(w, dict(action='create', revision=0, month=0, name='听闻验收', sex='女', age=24,
                                   place=self.home))
        self.far = self.place_at(2)

    def place_at(self, distance):
        return next(p['id'] for p in self.w.active_places if hearsay.hops(self.w, self.home, p['id']) == distance)

    def npc(self, place, **values):
        n = self.w.make_npc(place=place)
        n.update(dict(dict(age_months=360, reputation=40.), **values))
        self.w.reindex()
        return n

    def wedding(self, place):
        a, b = self.npc(place), self.npc(place)
        return self.w.emit('marriage', [a['id'], b['id']], f"{a['name']}与{b['name']}结为夫妻。", .95), a, b

    def test_news_is_registered_deterministically_without_touching_the_random_stream(self):
        e, a, b = self.wedding(self.far)
        child = self.npc(self.far, age_months=60)
        self.w.emit('death', [child['id']], '孩童夭折。', .9, facts=dict(deceased=child['id']))
        self.w.emit('work', [a['id']], '谋生。', .2)
        state = self.w.rng.getstate()
        hearsay.monthly(self.w)
        self.assertEqual(self.w.rng.getstate(), state)
        rows = hearsay.rows(self.w)
        self.assertEqual([r['event_id'] for r in rows], [e['id']])
        self.assertEqual(rows[0]['origin'], self.far)
        hearsay.monthly(self.w)  # already scanned: nothing new
        self.assertEqual(len(hearsay.rows(self.w)), 1)

    def test_rumours_travel_one_town_a_month_and_fade_after_a_year(self):
        self.wedding(self.far)
        hearsay.monthly(self.w)
        self.assertEqual(hearsay.view(self.w), [])
        before = deepcopy(self.w.player)
        self.w.month += 1
        self.assertEqual(hearsay.view(self.w), [])
        self.w.month += 1
        [row] = hearsay.view(self.w)
        self.assertEqual(row['hops'], 2)
        self.assertTrue(row['text'].startswith('听说'))
        self.assertFalse(row['told'])
        self.assertEqual(self.w.player, before)  # reading is pure
        self.assertEqual(len(hearsay.view(self.w, self.far)), 1)
        self.w.month += hearsay.LIFETIME
        self.assertEqual(hearsay.view(self.w), [])

    def test_a_local_retells_it_keeping_the_names_and_inventing_no_amounts(self):
        e, a, b = self.wedding(self.home)
        for _ in range(3):
            self.npc(self.home, reputation=0.)
        hearsay.monthly(self.w)
        row, ctx = hearsay.next_to_tell(self.w)
        self.assertNotIn(ctx['npc_id'], (a['id'], b['id']))
        self.assertEqual(ctx['rumor']['names'], [a['name'], b['name']])
        self.assertFalse(hearsay.valid(ctx, dict(line='听说城里有人成亲，热闹得很。')))       # names lost
        self.assertFalse(hearsay.valid(ctx, dict(line=f"{a['name']}成亲，摆了30桌酒。")))     # new amount
        self.assertFalse(hearsay.valid(ctx, dict(line=f"{a['name']}的事，系统说过了。")))
        line = f"你可听说了？{a['name']}和{b['name']}成了亲，街坊都去讨喜酒。"
        self.assertTrue(hearsay.apply(self.w, ctx, dict(line=line))[0])
        shown = hearsay.view(self.w)[0]
        self.assertTrue(shown['told'])
        self.assertIn(line, shown['text'])
        self.assertIsNone(hearsay.next_to_tell(self.w))
        self.assertEqual(self.w.metrics['hearsay_told'], 1)

    def test_a_retelling_that_keeps_failing_is_given_up(self):
        self.wedding(self.home)
        self.npc(self.home, reputation=0.)
        hearsay.monthly(self.w)
        for _ in range(2):
            _, ctx = hearsay.next_to_tell(self.w)
            self.assertFalse(hearsay.apply(self.w, ctx, dict(line='听说有人成亲了。'))[0])
        self.assertIsNone(hearsay.next_to_tell(self.w))
        self.assertTrue(hearsay.view(self.w)[0]['text'].startswith('本地传开'))

    def test_the_manager_keeps_one_retelling_in_flight(self):
        e, a, b = self.wedding(self.home)
        self.npc(self.home, reputation=0.)
        self.wedding(self.home)
        hearsay.monthly(self.w)
        manager = ai.AIManager(dict(enabled=True)); self.addCleanup(manager.close)
        told = lambda model, ctx: (dict(line=f"听说{ctx['rumor']['names'][0]}成了亲。"), 1, {}, 'unit-test mock')
        with patch.object(hearsay, 'model_request', told):
            self.assertEqual(manager.narrate(self.w), 1)
            self.assertEqual(manager.narrate(self.w), 0)
            manager.jobs.join(); manager.collect(self.w)
        self.assertEqual(self.w.metrics['hearsay_told'], 1)
        self.assertEqual(manager.view()['rumor_pending'], 0)
        self.assertEqual(sum(r['told'] for r in hearsay.view(self.w)), 1)

    def test_participation_counts_what_the_model_decided_and_voiced(self):
        n = self.npc(self.home)
        before = ai.participation(self.w)
        self.assertEqual(before['model_undertakings'], 0)
        n['plan'] = dict(source='AI', intent='courtship', target=None)
        self.w.metrics['player_reactions'] = 3
        after = ai.participation(self.w)
        self.assertEqual((after['model_undertakings'], after['reactions_completed']), (1, 3))


if __name__ == '__main__':
    unittest.main()
