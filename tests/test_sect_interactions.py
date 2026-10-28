"""Recruitment belongs to eligible people; removed routes cannot change saves."""
from copy import deepcopy
import unittest

from jianghu import martial, organizations, player_interactions, protagonist, rank_progression
from jianghu.engine import World
from jianghu.store import Store
from tests.combat_helpers import set_rating


class SectInteractionTests(unittest.TestCase):
    def setUp(self):
        self.w = w = World(91, 0, compact=True, durable=False, rank_assessments=True)
        self.addCleanup(w.store.close)
        protagonist.perform(w, dict(action='create', revision=0, month=0,
                                   name='入门验收', sex='女', age=24, place=45))
        self.n = n = w.make_npc(place=45)
        n.update(faction=0, contribution=35., faction_since=0, age_months=360, health=100.)
        set_rating(n, 45.)
        w.set_goal(n, 'mastery', initial=True)
        rank_progression.prepare(w, n['id'])
        w.reindex()

    def body(self, **extra):
        h = self.w.player['protagonist']
        return dict(action='join_sect', npc_id=self.n['id'], revision=h['revision'], month=self.w.month, **extra)

    def reject(self, body=None):
        before = deepcopy(self.w.export()); count = self.w.store.count()
        with self.assertRaises(ValueError):
            player_interactions.perform(self.w, body or self.body())
        self.assertEqual(self.w.export(), before)
        self.assertEqual(self.w.store.count(), count)

    def test_recruitment_is_listed_only_for_living_top_three_sect_members(self):
        for grade in range(1, 10):
            with self.subTest(grade=grade):
                self.w.factions[0]['leader'] = self.n['id'] if grade == 1 else None
                self.n['rank_progress']['sect']['grade'] = grade
                before = deepcopy(self.w.export())
                actions = player_interactions.view(self.w, self.n['id'])['actions']
                entry = next((a for a in actions if a['id'] == 'join_sect'), None)
                self.assertEqual(entry is not None, grade <= 3)
                self.assertEqual(self.w.export(), before)
                if entry:
                    self.assertEqual((entry['category'], entry['points']), (5, 4))
                else:
                    self.reject()
        self.n['faction'] = None
        self.assertNotIn('join_sect', [a['id'] for a in player_interactions.view(self.w, self.n['id'])['actions']])
        self.reject()

    def test_join_uses_current_recruiter_faction_away_from_seat_and_persists(self):
        w = self.w; h = w.player['protagonist']
        self.n['faction'] = 1
        rank_progression.prepare(w, self.n['id'])
        h['rank_progress']['sect'] = dict(faction=0, grade=2)
        before = (h['points'], self.n['time_left'], w.player['wealth'])
        request = self.body()
        result = player_interactions.perform(w, request)
        self.assertEqual((h['faction'], h['contribution'], h['faction_since']), (1, 0., w.month))
        self.assertEqual(organizations.player_membership(w)['grade'], 9)
        self.assertEqual((h['points'], self.n['time_left'], w.player['wealth']), (before[0]-4, before[1], before[2]))
        self.assertEqual(result['event']['facts']['recruiter'], self.n['id'])
        self.assertIn(result['event']['id'], w.factions[1]['records'])
        self.assertIn(result['event']['id'], w.player['ties'][str(self.n['id'])]['encounter_records'])
        self.assertEqual(martial.resolve(w, w.player, True)['school'], w.factions[1]['key'])
        self.reject(request)
        w.checkpoint(); store = Store(); w.store.db.backup(store.db)
        loaded = World(store=store, durable=False, compact=True)
        self.addCleanup(loaded.store.close)
        self.assertEqual(loaded.player, w.player)
        self.assertEqual(organizations.player_membership(loaded)['grade'], 9)

    def test_changed_authority_and_player_requirements_are_rechecked_atomically(self):
        w = self.w; h = w.player['protagonist']
        for actor, field, value in ((self.n, 'alive', False), (self.n, 'place', 46),
                                    (h, 'created', False), (h, 'faction', 1),
                                    (h, 'faction_rejoin_after', 12), (h, 'points', 3)):
            old = actor[field]; actor[field] = value
            self.reject()
            actor[field] = old
        set_rating(w.player, 11.); self.reject(); set_rating(w.player, 75.)
        request = self.body()
        self.n['rank_progress']['sect']['grade'] = 4
        self.reject(request)
        self.n['rank_progress']['sect']['grade'] = 3
        request['faction'] = 2
        self.reject(request)

    def test_removed_hero_routes_and_all_paid_duties_are_unavailable(self):
        w = self.w; h = w.player['protagonist']
        for action in ('sect_join', 'sect_task', 'sect_study', 'seek_teacher'):
            before = deepcopy(w.export())
            with self.assertRaises(ValueError):
                protagonist.perform(w, dict(action=action, faction=0, teacher=self.n['id'],
                                            revision=h['revision'], month=w.month))
            self.assertEqual(w.export(), before)
        self.assertNotIn('faction_task', [a['kind'] for a in w.legal_actions(self.n)])
        before = deepcopy(w.export())
        self.assertIsNone(w.execute(self.n, dict(id='faction_task:0')))
        self.assertEqual(w.export(), before)
        player_interactions.perform(w, self.body())
        self.assertEqual({o['action'] for o in organizations.player_options(w, 0)}, {'sect_donate', 'sect_leave'})


if __name__ == '__main__':
    unittest.main()
