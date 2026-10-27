"""Family capacity, local accounts, shared care and informed migration."""
from copy import deepcopy
import json
import unittest

from jianghu import demography, politics, settlements, social, society
from jianghu.engine import World


class SocialLivelihoodTests(unittest.TestCase):
    def world(self, population=0, compact=False):
        w = World(seed=42, population=population, compact=compact, durable=False)
        self.addCleanup(w.store.close)
        return w

    def person(self, w, place=0, **values):
        n = w.make_npc(place=place)
        n.update(age_months=360, wealth=100., health=100.)
        n.update(values)
        n['traits'].update(compassion=80., loyalty=80.)
        w.set_goal(n, 'belonging', initial=True)
        w.reindex()
        return n

    def child(self, w, parent, **values):
        child = self.person(w, place=parent['place'], parents=[parent['id']], age_months=60,
                            wealth=0., guardian=parent['id'], **values)
        parent['children'].append(child['id'])
        return child

    def bank(self, w, place=0):
        return w.society['communities'][str(place)]

    def total(self, w):
        return sum(n['wealth'] for n in w.npcs.values()) + sum(c['fund'] for c in w.society['communities'].values())

    def test_family_support_conserves_silver_and_settles_once(self):
        w = self.world()
        parent = self.person(w)
        child = self.child(w, parent)
        total, rng = self.total(w), w.rng.getstate()
        society.monthly(w)
        self.assertAlmostEqual(child['wealth'], 2.4)
        self.assertAlmostEqual(self.total(w), total)
        self.assertEqual(w.rng.getstate(), rng)
        event = next(e for e in w.store.history(child['id']) if e['kind'] == 'household_support')
        self.assertEqual(event['facts']['giver'], parent['id'])
        self.assertEqual(event['facts']['transfer'], 2.4)
        before = deepcopy(w.society), parent['wealth'], child['wealth'], w.store.count()
        society.monthly(w)
        self.assertEqual((w.society, parent['wealth'], child['wealth'], w.store.count()), before)

    def test_donor_reserves_dependants_food_and_limits_total_support(self):
        w = self.world()
        parent = self.person(w, wealth=100.)
        children = [self.child(w, parent) for _ in range(5)]
        society.monthly(w)
        self.assertLessEqual(sum(c['wealth'] for c in children), 8.)
        parent['wealth'] = 12 + 5 * 2.4
        for c in children:
            c['wealth'] = 0
        w.month += 1
        society.monthly(w)
        self.assertEqual(sum(c['wealth'] for c in children), 0.)
        self.assertAlmostEqual(parent['wealth'], 24.)

    def test_support_respects_locality_hostility_and_personal_values(self):
        for condition in ('away', 'hostile', 'unwilling', 'dead'):
            with self.subTest(condition=condition):
                w = self.world()
                parent = self.person(w)
                child = self.child(w, parent)
                if condition == 'away':
                    parent['place'] = 1
                elif condition == 'hostile':
                    w.link(parent, child, -60, -60)
                elif condition == 'unwilling':
                    parent['traits'].update(compassion=10, loyalty=10)
                else:
                    parent['alive'] = False
                w.reindex()
                society.monthly(w)
                self.assertEqual(child['wealth'], 0)

    def test_sworn_support_requires_a_mutual_bond(self):
        w = self.world()
        a, b = self.person(w), self.person(w, wealth=0)
        w.link(a, b, 60, 50)
        w.relation(a, b['id'])['sworn'] = True
        society.monthly(w)
        self.assertEqual(b['wealth'], 0)
        w.relation(b, a['id'])['sworn'] = True
        w.month += 1
        society.monthly(w)
        self.assertEqual(b['wealth'], 4)

    def test_donation_and_relief_use_actual_local_money(self):
        w = self.world()
        donor = self.person(w)
        young = self.person(w, age_months=80, wealth=0)
        adult = self.person(w, wealth=0)
        remote = self.person(w, place=1, wealth=0)
        total = self.total(w)
        event = w.execute(donor, {'id': 'donate_relief:self'})
        self.assertEqual(event['facts']['silver'], 4)
        self.assertEqual(donor['time_left'], 3)
        self.assertIsNone(w.execute(donor, {'id': 'donate_relief:self'}))
        w.month = 1
        society.monthly(w)
        self.assertAlmostEqual(young['wealth'], 1.2)
        self.assertAlmostEqual(adult['wealth'], 2.3)
        self.assertEqual(remote['wealth'], 0)
        self.assertAlmostEqual(self.total(w), total)
        bank = self.bank(w)
        self.assertAlmostEqual(bank['fund'], bank['opening_fund'] + bank['revenue'] - bank['expenses'])
        relief = [e for e in w.store.history() if e['kind'] == 'community_relief']
        self.assertEqual(len(relief), 2)
        self.assertTrue(any(event['id'] in e['causes'] for e in relief))

    def test_empty_community_does_not_create_relief_money(self):
        w = self.world()
        n = self.person(w, wealth=0)
        funds = [g['fund'] for g in w.governments]
        society.monthly(w)
        self.assertEqual(n['wealth'], 0)
        self.assertEqual([g['fund'] for g in w.governments], funds)
        self.assertEqual(self.bank(w)['expenses'], 0)

    def test_stale_donation_is_rejected_after_person_moves(self):
        w = self.world()
        donor = self.person(w)
        self.person(w, wealth=0)
        self.assertIn('donate_relief:self', [a['id'] for a in w.legal_actions(donor)])
        donor['place'] = 1
        w.reindex()
        self.assertIsNone(w.execute(donor, {'id': 'donate_relief:self'}))
        self.assertEqual(donor['wealth'], 100)
        self.assertEqual(self.bank(w)['fund'], 0)

    def test_care_reserves_both_people_and_has_receiver_monthly_limit(self):
        w = self.world()
        a, b = self.person(w), self.person(w, health=40)
        c = self.person(w)
        w.link(a, b, 20, 20)
        w.link(c, b, 20, 20)
        w.phase = 1
        event = w.execute(a, {'id': f"neighbor_care:{b['id']}"})
        self.assertEqual((a['time_left'], b['time_left']), (3, 3))
        self.assertFalse(social.available(w, b))
        self.assertEqual(b['health'], 42)
        self.assertEqual(event['facts']['partner_time_cost'], 1)
        self.assertEqual(b['social_life']['last_care_event'], event['id'])
        w.phase = 2
        self.assertIsNone(w.execute(c, {'id': f"neighbor_care:{b['id']}"}))
        self.assertEqual(self.bank(w)['care_visits'], 1)

    def test_care_rechecks_recipient_location_and_free_time(self):
        w = self.world()
        a, b = self.person(w), self.person(w, health=40)
        w.link(a, b, 20, 20)
        for change in ('busy', 'away'):
            with self.subTest(change=change):
                b['time_left'] = 0 if change == 'busy' else 4
                b['place'] = 1 if change == 'away' else 0
                w.reindex()
                self.assertIsNone(w.execute(a, {'id': f"neighbor_care:{b['id']}"}))
                self.assertEqual(a['time_left'], 4)

    def test_care_is_comfort_rather_than_an_instant_disease_cure(self):
        w = self.world()
        a, b = self.person(w), self.person(w, health=40)
        b['illness'] = {'remaining': 5, 'severity': 30, 'event_id': None}
        w.link(a, b, 20, 20)
        before = deepcopy(b['illness'])
        w.execute(a, {'id': f"neighbor_care:{b['id']}"})
        self.assertEqual(b['illness'], before)

    def test_recipient_can_refuse_care_from_a_hated_person(self):
        w = self.world()
        a, b = self.person(w), self.person(w, health=40)
        w.know(a, b)
        w.know(b, a)
        w.relation(b, a['id'])['affinity'] = -60
        self.assertIsNone(w.execute(a, {'id': f"neighbor_care:{b['id']}"}))
        self.assertEqual(b['time_left'], 4)

    def test_hardship_updates_after_food_settlement_and_can_recover(self):
        w = self.world()
        n = self.person(w, wealth=0)
        n['monthly_settlement'] = {'food_shortfall': 1}
        society.settle_hardship(w, n)
        society.settle_hardship(w, n)
        self.assertEqual(n['social_life']['hardship_months'], 2)
        n['monthly_settlement']['food_shortfall'] = 0
        n['wealth'] = 20
        society.settle_hardship(w, n)
        self.assertEqual(n['social_life']['hardship_months'], 0)

    def migrant(self, w):
        n = self.person(w)
        target = sorted(w.neighbors[n['place']])[0]
        host = self.person(w, place=target)
        n['parents'] = [host['id']]
        host['children'] = [n['id']]
        w.link(n, host, 30, 20)
        n['social_life']['hardship_months'] = 2
        return n, host, target

    def test_migration_uses_last_known_place_and_has_no_hidden_host_data(self):
        w = self.world()
        n, host, target = self.migrant(w)
        host['place'], host['wealth'] = 5, 0
        goal = society.migration(w, n)
        self.assertEqual(goal['destination'], target)
        self.assertEqual(goal['host'], host['id'])
        n['knowledge'].clear()
        self.assertIsNone(society.migration(w, n))
        w.know(n, host)
        self.assertIsNone(society.migration(w, n))

    def test_migration_keeps_route_cost_time_and_dependants(self):
        w = self.world()
        n, host, target = self.migrant(w)
        trip = politics.journey(w, n['place'], target)
        n['wealth'] = trip['cost'] + 3
        child = self.child(w, n)
        action = next(a for a in w.legal_actions(n) if a['id'] == f'travel:{target}')
        self.assertTrue(action['migration'])
        w.phase = 1
        event = w.execute(n, action)
        self.assertEqual((n['place'], child['place']), (target, target))
        self.assertAlmostEqual(n['wealth'], 3)
        self.assertEqual(n['time_left'], 2)
        self.assertEqual(event['facts']['migration']['host'], host['id'])
        self.assertEqual(event['facts']['cost'], trip['cost'])
        self.assertIsNone(w.execute(n, action))

    def test_observing_social_views_is_pure(self):
        w = self.world(population=12)
        before, rng, events = deepcopy(w.export()), w.rng.getstate(), w.store.count()
        for _ in range(2):
            w.summary()
            for i in w.npcs:
                w.npc_detail(i)
        self.assertEqual(w.export(), before)
        self.assertEqual(w.rng.getstate(), rng)
        self.assertEqual(w.store.count(), events)

    def test_old_save_upgrade_preserves_random_state_assets_and_history(self):
        w = self.world(population=12)
        state = deepcopy(w.export())
        del state['society']
        for n in state['npcs'].values():
            del n['social_life']
        before = [(n['wealth'], deepcopy(n['relationships'])) for n in w.npcs.values()]
        count, rng = w.store.count(), w.rng.getstate()
        w.restore(state)
        self.assertEqual(w.rng.getstate(), rng)
        self.assertEqual(w.store.count(), count)
        self.assertEqual([(n['wealth'], n['relationships']) for n in w.npcs.values()], before)
        self.assertEqual(sum(c['fund'] for c in w.society['communities'].values()), 0)
        w.checkpoint()
        saved = deepcopy(w.society)
        w.restore(json.loads(json.dumps(w.export())))
        self.assertEqual(w.society, saved)

    def test_accounts_survive_large_map_consolidation(self):
        w = self.world()
        for i, amount in ((0, 7), (15, 4), (80, 11)):
            self.bank(w, i).update(fund=amount, revenue=amount)
        total = sum(c['fund'] for c in w.society['communities'].values())
        w.settlements = settlements.profile()
        society.initialize(w)
        self.assertEqual(len(w.society['communities']), len(w.active_places))
        self.assertEqual(sum(c['fund'] for c in w.society['communities'].values()), total)
        for c in w.society['communities'].values():
            self.assertAlmostEqual(c['fund'], c['opening_fund'] + c['revenue'] - c['expenses'])

    def test_adult_sibling_can_continue_guardianship(self):
        w = self.world()
        parent = self.person(w)
        young = self.child(w, parent)
        sibling = self.child(w, parent)
        sibling.update(age_months=300, wealth=40)
        parent['alive'] = False
        demography.households(w)
        self.assertEqual(young['guardian'], sibling['id'])
        event = next(e for e in w.store.history(young['id']) if e['kind'] == 'guardianship')
        self.assertEqual(event['facts']['previous'], parent['id'])

    def test_parent_sworn_friend_must_have_willingness_and_capacity(self):
        w = self.world()
        parent = self.person(w)
        young = self.child(w, parent)
        friend = self.person(w)
        w.link(parent, friend, 60, 50)
        for a, b in ((parent, friend), (friend, parent)):
            w.relation(a, b['id'])['sworn'] = True
        parent['alive'] = False
        friend['traits']['compassion'] = 20
        demography.households(w)
        self.assertIsNone(young['guardian'])
        friend['traits']['compassion'] = 80
        demography.households(w)
        self.assertEqual(young['guardian'], friend['id'])


if __name__ == '__main__':
    unittest.main()
