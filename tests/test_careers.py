from copy import deepcopy
import json
import unittest

from jianghu import careers, government, items
from jianghu.ai import context_for
from jianghu.engine import World


class CareerTests(unittest.TestCase):
    def setUp(self):
        self.w = World(seed=42, population=30, durable=False)
        self.addCleanup(self.w.store.close)

    def person(self, job='农人', xp=0):
        n = next(n for n in self.w.npcs.values() if not n['office'])
        n.update({'age_months': 300, 'job': job, 'place': 0, 'wealth': 80., 'health': 100., 'skill': 30.})
        n['career'] = {'profession': job, 'experience': {job: xp}, 'since': 0,
                       'last_change': -24, 'last_event': None, 'source': '测试条件'}
        n['identity'] = careers.identity(self.w, n)
        self.w.reindex()
        return n

    def tutor(self, student, job='药师', xp=50):
        tutor = next(n for n in self.w.npcs.values() if n['id'] != student['id'] and not n['office'])
        tutor.update({'age_months': 400, 'job': job, 'place': student['place'], 'health': 100.})
        tutor['career'].update({'profession': job, 'experience': {job: xp}})
        tutor['identity'] = careers.identity(self.w, tutor)
        self.w.reindex()
        return tutor

    def test_work_uses_trade_experience_and_local_demand_not_unrelated_martial_skill(self):
        n = self.person('渔人')
        first = careers.income(n)
        n['skill'] = 100
        self.assertEqual(first, careers.income(n))
        self.assertGreater(careers.income(n, place=3), careers.income(n, place=1))
        n['career']['experience']['渔人'] = 80
        self.assertGreater(careers.income(n), first)

    def test_work_books_tax_and_crossing_a_grade_has_a_real_cause(self):
        n = self.person(xp=14.7)
        money, treasury, estimate = n['wealth'], self.w.government['fund'], (careers.income(n)+items.bonuses(self.w,n['id'])['income'])*.5
        e = self.w.execute(n, {'id': 'work:self'})
        self.assertAlmostEqual(n['wealth']-money, estimate-e['facts']['tax'])
        self.assertAlmostEqual(self.w.government['fund']-treasury, e['facts']['tax'])
        self.assertEqual(careers.experience(n), 15.1)
        self.assertEqual(careers.identity(self.w, n)['grade'], 8)
        change = next(e for e in self.w.store.history(n['id']) if e['kind'] == 'identity_change')
        self.assertIn(e['id'], change['causes'])
        self.assertEqual(change['facts']['before']['grade'], 9)

    def test_learning_pays_actual_tutor_and_does_not_rewrite_current_profession(self):
        n = self.person()
        tutor = self.tutor(n)
        total = n['wealth']+tutor['wealth']
        action = next(a for a in self.w.legal_actions(n) if a['id'] == f'learn_trade:{tutor["id"]}')
        e = self.w.execute(n, action)
        self.assertAlmostEqual(n['wealth']+tutor['wealth'], total)
        self.assertEqual(e['facts']['payment'], 2)
        self.assertEqual(careers.experience(n, '药师'), 3.5)
        self.assertEqual(n['job'], '农人')
        self.assertEqual(careers.experience(n), 0)
        tutor['health'] = 10
        before = deepcopy(n)
        self.assertIsNone(self.w.execute(n, action))
        self.assertEqual(n, before)

    def test_medicine_requires_learning_before_switch_and_stale_action_is_rejected(self):
        n = self.person()
        self.assertNotIn('change_job:药师', [a['id'] for a in self.w.legal_actions(n)])
        n['career']['experience']['药师'] = 12
        self.assertIn('change_job:药师', [a['id'] for a in self.w.legal_actions(n)])
        n['wealth'] = 11
        before = deepcopy(self.w.npcs)
        self.assertIsNone(self.w.execute(n, {'id': 'change_job:药师'}))
        self.assertEqual(self.w.npcs, before)

    def test_switch_preserves_old_skills_charges_setup_and_enforces_two_year_commitment(self):
        n = self.person(xp=40)
        old_money = n['wealth']
        self.w.execute(n, {'id': 'change_job:行商', 'target': '药师'})
        self.assertEqual(n['job'], '行商')  # Payload cannot replace the canonical action target.
        self.assertEqual(n['wealth'], old_money-12)
        self.assertEqual(careers.experience(n, '农人'), 40)
        self.assertFalse(any(a['kind'] == 'change_job' for a in self.w.legal_actions(n)))
        self.w.month += 24
        self.assertIn('change_job:农人', [a['id'] for a in self.w.legal_actions(n)])

    def test_job_entry_checks_location_age_office_and_martial_ability(self):
        n = self.person()
        for update, job in [({'place': 1}, '行商'), ({'age_months': 180}, '行商'),
                            ({'office': '主簿'}, '行商'), ({'skill': 19}, '镖师')]:
            before = deepcopy(n)
            n.update(update)
            self.assertTrue(careers.entry_blockers(self.w, n, job))
            n.clear(); n.update(before)

    def test_child_grows_into_family_apprenticeship_and_keeps_learned_experience(self):
        parent = self.person('匠人', 90)
        child = self.w.make_npc([parent['id']], parent['place'])
        child['age_months'] = 120
        careers.grow(self.w, child)
        self.assertEqual(child['job'], '学徒')
        self.assertEqual(child['career']['profession'], '匠人')
        self.assertEqual(careers.experience(child), 0)
        careers.gain(child, '匠人', 22)
        child['age_months'] = 192
        careers.grow(self.w, child)
        self.assertEqual(child['job'], '匠人')
        self.assertEqual(careers.experience(child), 22)
        self.assertEqual(careers.identity(self.w, child)['grade'], 8)
        self.assertIsNone(child['office'])

    def test_apprentice_earns_fraction_of_adult_income_and_young_children_cannot_work(self):
        n = self.person()
        adult_income = careers.income(n)
        n['age_months'] = 180
        self.assertAlmostEqual(careers.income(n), round(adult_income*.45, 4), places=4)
        n['age_months'] = 119
        self.assertEqual({a['kind'] for a in self.w.legal_actions(n)}, {'rest', 'train', 'play'})

    def test_professional_doctor_skill_changes_real_healing_and_transfers_fee(self):
        n = self.person()
        tutor = self.tutor(n, xp=80)
        n['health'] = 40
        self.w.link(n, tutor, 10, 10)
        total = n['wealth']+tutor['wealth']
        e = self.w.execute(n, {'id': f'treatment:{tutor["id"]}'})
        self.assertEqual(n['health'], 66)
        self.assertEqual(e['facts']['healing'], 26)
        self.assertAlmostEqual(n['wealth']+tutor['wealth'], total)
        self.assertEqual(careers.experience(tutor), 80.6)

    def test_identity_combines_real_roles_and_does_not_inherit_parent_rank(self):
        n = self.person('匠人', 90)
        n.update({'office': '县令', 'faction': 0, 'spouse': 2})
        self.w.factions[0]['leader'] = n['id']
        identity = careers.identity(self.w, n)
        self.assertEqual(identity['grade'], 4)
        self.assertEqual({r['kind'] for r in identity['roles']}, {'office', 'faction', 'family', 'livelihood'})
        child = self.w.make_npc([n['id']], 0)
        self.assertEqual(careers.identity(self.w, child)['grade'], 9)
        self.assertEqual(child['career']['experience'], {})

    def test_death_preserves_last_identity_and_does_not_gain_future_experiences(self):
        official = self.w.npcs[self.w.government['offices']['县令']]
        before = careers.identity(self.w, official)
        self.w.die(official, 'old_age')
        memories = deepcopy(official['memories'])
        self.assertIsNone(official['office'])
        self.w.month += 1
        government.monthly(self.w)
        self.assertEqual(careers.identity(self.w, official), before)
        self.assertEqual(official['memories'], memories)

    def test_death_updates_surviving_spouses_identity_immediately(self):
        n = self.person()
        spouse = self.tutor(n)
        n['spouse'], spouse['spouse'] = spouse['id'], n['id']
        self.w.link(n, spouse, 20, 20)
        self.w.emit('marriage', [n['id'], spouse['id']], '测试婚姻。', .9)
        death = self.w.die(n, 'old_age')
        self.assertNotIn('已婚', [r['title'] for r in spouse['identity']['roles']])
        change = self.w.store.event(spouse['identity_event'])
        self.assertIn(death['id'], change['causes'])

    def test_expired_office_with_no_successor_releases_identity(self):
        official = self.w.npcs[self.w.government['offices']['县令']]
        for n in self.w.npcs.values():
            n['age_months'] = 900
        self.w.month = 72
        government.appoint(self.w)
        self.assertIsNone(self.w.government['offices']['县令'])
        self.assertIsNone(official['office'])
        self.assertFalse(any(r['kind'] == 'office' for r in official['identity']['roles']))

    def test_public_observation_and_ai_context_preserve_information_boundaries(self):
        n = self.person()
        tutor = self.tutor(n)
        self.w.know(n, tutor)
        ctx = context_for(self.w, n)
        observed = n['knowledge'][str(tutor['id'])]
        self.assertEqual(observed['job'], '药师')
        self.assertIn('identity_grade', observed)
        self.assertNotIn('traits', observed)
        self.assertNotIn('experience', observed)
        self.assertEqual(ctx['self']['career'], n['career'])
        self.assertEqual(ctx['social_identity'], careers.identity(self.w, n))
        self.assertNotIn('npcs', ctx)

    def test_legacy_migration_preserves_journal_and_rng_without_inventing_experience(self):
        state = json.loads(json.dumps(self.w.export()))
        for n in state['npcs'].values():
            for field in ('career', 'identity', 'identity_event', 'final_identity'):
                n.pop(field, None)
        before_count, random_state = self.w.store.count(), self.w.rng.getstate()
        self.w.restore(state)
        self.assertEqual(self.w.store.count(), before_count)
        self.assertEqual(self.w.rng.getstate(), random_state)
        self.assertTrue(all(careers.experience(n) == 0 for n in self.w.npcs.values()))
        self.assertTrue(all(n['career']['source'] == '旧档登记' for n in self.w.npcs.values()))
        self.w.month_step()
        self.assertTrue(self.w.npc_detail(1)['career_options'])

    def test_professional_standing_changes_with_public_reputation_but_skill_remains(self):
        n = self.person(xp=90)
        self.assertEqual(careers.identity(self.w, n)['grade'], 5)
        n['reputation'] = -20
        self.assertEqual(careers.identity(self.w, n)['grade'], 8)
        self.assertEqual(careers.experience(n), 90)


if __name__ == '__main__':
    unittest.main()
