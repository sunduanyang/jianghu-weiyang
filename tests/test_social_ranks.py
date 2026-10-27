from copy import deepcopy
import json
import unittest

from jianghu import careers, organizations, social_ranks, territory
from jianghu.engine import World


class SocialRankTests(unittest.TestCase):
    def setUp(self):
        self.w = World(seed=42, population=0, durable=False)
        self.addCleanup(self.w.store.close)

    def person(self, job='农人', **values):
        n = self.w.make_npc(place=0)
        n.update(age_months=360, job=job, wealth=100., health=100., reputation=0., faction=None)
        n['career'].update(profession=job if job in careers.PROFESSIONS else '农人',
                           experience={job: 0}, last_change=-24)
        n.update(values)
        n['identity'] = careers.identity(self.w, n)
        self.w.set_goal(n, 'wealth', initial=True)
        self.w.reindex()
        return n

    def test_occupations_follow_social_estate_instead_of_proficiency(self):
        for job, grade in [('乞丐', 9), ('强盗', 9), ('农人', 8), ('农民', 8),
                           ('渔人', 8), ('猎户', 8), ('匠人', 7), ('工匠', 7),
                           ('艺人', 7), ('行商', 6), ('镖师', 6), ('书生', 5), ('药师', 5)]:
            with self.subTest(job=job):
                n = self.person(job)
                self.assertEqual(careers.identity(self.w, n)['grade'], grade)
                self.assertEqual(careers.identity(self.w, n)['system'], 'nation')

    def test_complete_nine_estates_recognize_real_identity_titles(self):
        for job, expected in [('国主', 1), ('宰相', 2), ('世家子弟', 3), ('乡绅', 4),
                              ('书生', 5), ('行商', 6), ('匠人', 7), ('农人', 8), ('乞丐', 9)]:
            self.assertEqual(careers.identity(self.w, self.person(job))['grade'], expected)

    def test_ability_wealth_reputation_and_teaching_do_not_promote_a_farmer(self):
        n = self.person()
        student = self.person()
        self.w.link(n, student, 50, 50)
        student['mentor'] = n['id']
        n.update(wealth=100000., reputation=100., skill=100.)
        n['career']['experience']['农人'] = 100
        self.assertEqual(careers.identity(self.w, n)['grade'], 8)
        n['reputation'] = -100
        self.assertEqual(careers.identity(self.w, n)['grade'], 8)

    def test_explicit_low_estate_is_not_replaced_by_legacy_fallback_trade(self):
        n = self.person('强盗')
        n['career'].update(profession='药师', experience={'药师': 100.})
        identity = careers.identity(self.w, n)
        self.assertEqual((identity['grade'], identity['title']), (9, '强盗'))
        n['job'] = '无名营生'
        self.assertEqual(careers.identity(self.w, n)['grade'], 9)

    def test_children_do_not_inherit_rank_and_apprentices_follow_their_trade(self):
        parent = self.person('国主')
        child = self.w.make_npc([parent['id']], 0)
        self.assertEqual(careers.identity(self.w, child)['grade'], 9)
        child.update(age_months=180, job='学徒')
        child['career'].update(profession='匠人', experience={'匠人': 0})
        self.assertEqual(careers.identity(self.w, child)['grade'], 7)

    def test_sect_rank_is_separate_from_social_estate(self):
        n = self.person('农人', faction=0)
        self.w.factions[0]['leader'] = n['id']
        identity = careers.identity(self.w, n)
        self.assertEqual(identity['grade'], organizations.rank(self.w, n)['grade'])
        self.assertEqual(identity['system'], 'sect')
        self.assertIsNone(identity['class_label'])

    def test_only_actual_office_changes_official_social_standing(self):
        n = self.person()
        n.update(office='县令', office_country=0)
        self.assertEqual(careers.identity(self.w, n)['grade'], 4)
        n.update(office=None, office_country=None)
        self.assertEqual(careers.identity(self.w, n)['grade'], 8)
        self.assertEqual(set(self.w.government['offices']), {'县令', '捕头', '主簿'})

    def test_census_excludes_sects_and_deceased_and_follows_current_territory(self):
        farmer = self.person()
        self.person('匠人')
        self.person('乞丐', faction=0)
        dead = self.person('药师')
        self.w.die(dead, 'old_age')
        view = social_ranks.country_view(self.w, 0, include_roster=True)
        self.assertEqual((view['population'], view['sect_population']), (2, 1))
        self.assertEqual(sum(g['population'] for g in view['grades']), 2)
        self.assertEqual(len(view['grades']), 9)
        self.assertIn(farmer['id'], {n['id'] for n in view['roster']})
        self.w.territory[0]['owner'] = 1
        self.assertEqual(social_ranks.country_view(self.w, 0)['population'], 0)
        self.assertEqual(social_ranks.country_view(self.w, 1)['population'], 2)
        self.assertEqual(careers.identity(self.w, farmer)['country'], territory.owner(self.w, 0))

    def test_census_and_public_views_do_not_mutate_world_or_rng(self):
        self.person()
        before, events = deepcopy(self.w.export()), self.w.store.count()
        self.w.country_detail(0)
        self.w.summary()
        self.assertEqual(self.w.export(), before)
        self.assertEqual(self.w.store.count(), events)

    def test_protagonist_uses_social_estate_until_joining_a_sect(self):
        self.assertIsNone(social_ranks.player_identity(self.w))
        h = self.w.player['protagonist']
        h['created'] = True
        self.assertEqual(social_ranks.player_identity(self.w)['grade'], 9)
        h['faction'] = 0
        identity = social_ranks.player_identity(self.w)
        self.assertEqual(identity['system'], 'sect')
        self.assertEqual(identity['grade'], organizations.rank(self.w, {'id': -1, 'faction': 0,
            'contribution': h['contribution'], 'skill': self.w.player['skill']})['grade'])

    def test_real_job_change_updates_estate_and_has_an_event_cause(self):
        n = self.person()
        event = self.w.execute(n, {'id': 'change_job:行商'})
        self.assertIsNotNone(event)
        self.assertEqual(n['identity']['grade'], 6)
        change = self.w.store.event(n['identity_event'])
        self.assertIn(event['id'], change['causes'])
        self.assertEqual(change['facts']['before']['grade'], 8)

    def test_old_save_refreshes_living_ranks_without_rewriting_history_or_funds(self):
        farmer = self.person()
        dead = self.person('匠人')
        self.w.die(dead, 'old_age')
        final_identity = deepcopy(dead['final_identity'])
        state = json.loads(json.dumps(self.w.export()))
        for n in state['npcs'].values():
            n['identity']['grade'] = 1
        before_rng, before_count = self.w.rng.getstate(), self.w.store.count()
        funds = [(g['fund'], g['revenue'], g['expenses'], deepcopy(g['offices'])) for g in self.w.governments]
        self.w.restore(state)
        self.assertEqual(self.w.npcs[farmer['id']]['identity']['grade'], 8)
        self.assertEqual(self.w.npcs[dead['id']]['final_identity'], final_identity)
        self.assertEqual(self.w.rng.getstate(), before_rng)
        self.assertEqual(self.w.store.count(), before_count)
        self.assertEqual([(g['fund'], g['revenue'], g['expenses'], g['offices']) for g in self.w.governments], funds)


if __name__ == '__main__':
    unittest.main()
