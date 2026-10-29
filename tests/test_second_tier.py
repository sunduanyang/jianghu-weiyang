"""NPC manual pacing, town-by-town fame, and the month loop's single legality check."""
from copy import deepcopy
import json
import unittest

from jianghu import fame, items, martial, martial_catalog, martial_manuals as manuals, protagonist, rank_progression
from jianghu.engine import World


class ManualPacingTests(unittest.TestCase):
    def setUp(self):
        self.w = World(42, 0, durable=False)
        self.addCleanup(self.w.store.close)
        protagonist.perform(self.w, dict(action='create', revision=0, month=0, name='习武人', sex='女', age=24, place=0))

    def npc(self, **values):
        n = self.w.make_npc(place=0)
        n.update(age_months=360, health=100., time_left=4, **values)
        n['traits'].update(integrity=80., kindness=80.)
        self.w.reindex()
        return n

    def pair(self):
        teacher, pupil = self.npc(), self.npc()
        mine = set(martial.resolve(self.w, pupil)['known'])
        arts = [k for k in martial_catalog.SCHOOL_ARTS['wudang'] if k not in mine][:2]
        teacher['martial']['learned'] = arts
        self.w.link(pupil, teacher, 30, 30)
        return teacher, pupil, arts

    def test_an_unread_manual_must_be_learned_before_asking_for_another(self):
        teacher, pupil, (first, second) = self.pair()
        self.assertEqual(manuals.reason(self.w, pupil['id'], teacher, second), '')
        items.create(self.w, pupil['id'], items.MANUAL_TEMPLATES[first], '旧藏')
        self.assertIn('未曾研习', manuals.reason(self.w, pupil['id'], teacher, second))
        self.assertEqual(manuals.candidates(self.w, pupil, teacher), [])

    def test_npcs_seek_a_new_art_at_most_once_in_a_few_months(self):
        teacher, pupil, (first, second) = self.pair()
        manuals.request(self.w, pupil['id'], teacher['id'], first)
        items.perform(self.w, pupil['id'], 'use', next(i for i in items.objects(self.w, pupil['id']))['id'])
        self.w.month += 1
        self.assertIn('已求过师', manuals.reason(self.w, pupil['id'], teacher, second))
        self.w.month += manuals.REQUEST_MONTHS - 1
        pupil['time_left'] = 4
        self.assertEqual(manuals.reason(self.w, pupil['id'], teacher, second), '')

    def test_the_player_is_paced_by_action_points_not_by_the_npc_window(self):
        teacher, _, (first, second) = self.pair()
        self.w.player['protagonist']['place'] = teacher['place']
        self.w.player['protagonist']['points'] = 10
        teacher['martial']['learned'] = [first, second]
        self.w.player['ties'][str(teacher['id'])] = dict(affinity=30., trust=30.)
        manuals.request(self.w, 'player', teacher['id'], first)
        self.assertEqual(manuals.reason(self.w, 'player', teacher, second), '')


class FameTests(unittest.TestCase):
    def setUp(self):
        self.w = World(7, 0, compact=True, durable=False)
        self.addCleanup(self.w.store.close)
        self.home = self.w.active_places[0]['id']

    def person(self, reputation):
        n = self.w.make_npc(place=self.home)
        n.update(age_months=360, reputation=reputation)
        self.w.reindex()
        return n

    def test_a_name_is_known_at_home_heard_nearby_and_forgotten_after_leaving(self):
        hero, nobody = self.person(80.), self.person(2.)
        state = self.w.rng.getstate()
        for _ in range(12):
            fame.monthly(self.w)
        self.assertEqual(self.w.rng.getstate(), state)
        self.assertGreater(fame.known(hero, self.home), .95)
        near = self.w.neighbors[self.home][0]
        self.assertGreater(fame.known(hero, near), .3)
        self.assertEqual(fame.known(nobody, near), 0.)  # small reputations do not travel
        self.assertLess(fame.known(hero, near), fame.known(hero, self.home))
        # Moving away: the new town starts from hearsay and the old one slowly forgets.
        far = next(p for p in self.w.neighbors[near] if p != self.home and fame.known(hero, p) < .2)
        hero['place'] = far
        self.assertLess(fame.local(hero), 20)
        before = fame.known(hero, self.home)
        for _ in range(6):
            fame.monthly(self.w)
        self.assertLess(fame.known(hero, self.home), before)
        self.assertGreater(fame.local(hero), 60)

    def test_local_recognition_reads_the_locally_known_reputation(self):
        n = self.person(50.)
        n['fame'] = {str(self.home): .4}
        self.assertEqual(fame.local_of(self.w, n['id']), 20.)
        self.assertEqual(fame.view(self.w, n)['towns'][0]['local'], 20.)

    def test_town_keys_survive_a_json_round_trip(self):
        n = self.person(60.)
        fame.monthly(self.w)
        copy = json.loads(json.dumps(n['fame']))
        self.assertEqual(copy, n['fame'])


class LegalityTests(unittest.TestCase):
    def test_execute_uses_the_supplied_legal_set_and_still_rejects_anything_else(self):
        w = World(7, 0, compact=True, durable=False); self.addCleanup(w.store.close)
        n = w.make_npc(place=w.active_places[0]['id']); n.update(age_months=360, time_left=4); w.set_goal(n, 'wealth', initial=True); w.reindex()
        legal = w.legal_actions(n)
        work = next(a for a in legal if a['kind'] == 'work')
        state, count = deepcopy(w.export()), w.store.count()
        self.assertIsNone(w.execute(n, work, legal=[a for a in legal if a['id'] != work['id']]))
        self.assertEqual(w.store.count(), count)
        state['metrics']['invalid_actions'] += 1
        self.assertEqual(w.export(), state)
        self.assertIsNotNone(w.execute(n, work, legal=legal))


if __name__ == '__main__':
    unittest.main()
