from jianghu import combat_attributes as abilities
from tests.combat_helpers import set_rating, update_actor
"""Nine-grade membership across all traditions and both actor types."""
from copy import deepcopy
import json
import unittest

from jianghu import careers, organizations, protagonist, sect_ranks, social_ranks
from jianghu.atlas import FACTIONS
from jianghu.engine import World


class SectRankTests(unittest.TestCase):
    def setUp(self):
        self.w = World(seed=42, population=0, durable=False, compact=True)
        self.addCleanup(self.w.store.close)

    def member(self, fid=0, contribution=0., skill=12.):
        w = self.w; n = w.make_npc(place=w.factions[fid]['place'])
        update_actor(n, age_months=360, health=100., faction=fid, contribution=contribution, skill=skill)
        w.set_goal(n, 'mastery', initial=True); w.reindex()
        return n

    def test_fourteen_profiles_each_have_nine_distinct_titles(self):
        self.assertEqual(len(FACTIONS), 14)
        for f in FACTIONS:
            self.assertEqual(len(f['ranks']), 9, f['name'])
            self.assertEqual(len(set(f['ranks'])), 9, f['name'])
        self.assertEqual([FACTIONS[i]['ranks'][0] for i in (0, 1, 2, 12, 13)],
                         ['掌门', '帮主', '方丈', '主祭', '大萨满'])

    def test_every_grade_is_reachable_for_each_school_and_leader_is_first(self):
        w = self.w
        for f in w.factions:
            n = self.member(f['id'])
            for grade in range(2, 10):
                contribution, skill = sect_ranks.REQUIREMENTS[grade]
                update_actor(n, contribution=contribution, skill=skill)
                rank = organizations.rank(w, n)
                self.assertEqual((rank['grade'], rank['title']), (grade, f['ranks'][grade-1]))
                self.assertEqual(careers.identity(w, n)['grade'], grade)
            f['leader'] = n['id']
            update_actor(n, contribution=0., skill=0.)
            self.assertEqual(organizations.rank(w, n)['grade'], 1)
            self.assertEqual(careers.identity(w, n)['grade_label'], '一品')
            f['leader'] = None
            self.assertEqual(organizations.rank(w, n)['grade'], 9)

    def test_higher_contribution_alone_cannot_bypass_martial_gate_or_leadership(self):
        w = self.w; n = self.member(contribution=1000., skill=12.)
        self.assertEqual(organizations.rank(w, n)['grade'], 8)
        update_actor(n, contribution=0., skill=100.)
        self.assertEqual(organizations.rank(w, n)['grade'], 9)
        n.update(contribution=1000.)
        self.assertEqual(organizations.rank(w, n)['grade'], 2)

    def test_player_and_npc_use_identical_nonleader_gates(self):
        w = self.w; h = w.player['protagonist']; h.update(created=True, faction=0)
        n = self.member()
        for grade in range(2, 10):
            h['contribution'], _combat_fixture_rating = sect_ranks.REQUIREMENTS[grade]; set_rating(w.player, _combat_fixture_rating)
            update_actor(n, contribution=h['contribution'], skill=abilities.rating(w.player))
            membership = organizations.player_membership(w)
            self.assertEqual({k: membership[k] for k in ('grade', 'grade_label', 'title')}, organizations.rank(w, n))
            self.assertEqual(social_ranks.player_identity(w)['grade'], grade)

    def test_census_lists_empty_grades_and_counts_each_member_and_player_once(self):
        w = self.w; f = w.factions[12]
        leader = self.member(12); self.member(12); dead = self.member(12)
        f['leader'] = leader['id']; w.die(dead, 'old_age')
        h = w.player['protagonist']; h.update(created=True, faction=12, contribution=0.)
        before = deepcopy(w.export()); events = w.store.count()
        view = w.faction_detail(12); rows = view['rank_system']['grades']
        self.assertEqual([row['grade'] for row in rows], list(range(1, 10)))
        self.assertEqual((rows[0]['population'], rows[8]['population']), (1, 2))
        self.assertEqual(sum(row['population'] for row in rows), view['members'])
        self.assertEqual(view['members'], 3)
        self.assertEqual(view['roster'][0]['id'], leader['id'])
        self.assertEqual(w.export(), before); self.assertEqual(w.store.count(), events)

    def test_duty_promotion_records_actual_before_after_grades(self):
        w = self.w; n = self.member(contribution=10., skill=25.)
        before = organizations.rank(w, n)
        e = w.execute(n, dict(id='faction_task:0'))
        self.assertEqual(before['grade'], 6)
        self.assertEqual(organizations.rank(w, n)['grade'], 5)
        change = next(e for e in w.store.history(n['id']) if e['kind']=='faction_rank')
        self.assertEqual((change['facts']['before']['grade'], change['facts']['after']['grade']), (6, 5))
        self.assertIn(e['id'], change['causes'])

    def test_join_starts_at_ninth_and_player_rank_updates_after_task(self):
        w = self.w; h = w.player['protagonist']; f = w.factions[0]
        from jianghu import city_maps
        seat = next(s for s in city_maps.world_layout(w, f['place'])['settlements'] if s.get('faction')==0)
        h.update(created=True, started_month=0, place=f['place'], row=seat['row'], col=seat['col'])
        def perform(action):
            return protagonist.perform(w, dict(action=action, faction=0, revision=h['revision'], month=w.month))
        perform('sect_join')
        self.assertEqual(organizations.player_membership(w)['grade'], 9)
        self.assertIn('九品记名弟子', w.store.event(h['records'][-1])['summary'])
        perform('sect_task')
        self.assertEqual(organizations.player_membership(w)['grade'], 8)

    def test_legacy_four_rank_metadata_is_replaced_without_spending_or_new_history(self):
        w = self.w; n = self.member(contribution=35., skill=45.)
        n['identity'] = careers.identity(w, n); n['identity']['grade'] = 6
        dead = self.member(); w.die(dead, 'old_age')
        final = deepcopy(dead['final_identity'])
        state = json.loads(json.dumps(w.export()))
        for f in state['factions']:
            f['ranks'] = ['旧掌门', '旧长老', '旧执事', '旧弟子']
        rng = w.rng.getstate(); count = w.store.count()
        accounts = [(f['fund'], f['revenue'], f['expenses']) for f in w.factions]
        w.restore(state)
        self.assertEqual(w.npcs[n['id']]['identity']['grade'], 3)
        self.assertEqual(len(w.factions[0]['ranks']), 9)
        self.assertEqual(w.npcs[dead['id']]['final_identity'], final)
        self.assertEqual(w.rng.getstate(), rng); self.assertEqual(w.store.count(), count)
        self.assertEqual([(f['fund'], f['revenue'], f['expenses']) for f in w.factions], accounts)
        w.checkpoint(); again = World(store=w.store, compact=True, durable=False)
        self.assertEqual(again.factions, w.factions)
        self.assertEqual(careers.identity(again, again.npcs[n['id']])['grade'], 3)


if __name__ == '__main__':
    unittest.main()
