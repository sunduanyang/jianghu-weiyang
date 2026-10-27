"""Social consent, shared schedules, institutional accounts and unified-save upgrades."""
from copy import deepcopy
import json
from http.server import ThreadingHTTPServer
import threading
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import urllib.error
import urllib.request

from jianghu import government, organizations, politics, relationships, social
from jianghu.ai import context_for
from jianghu.engine import World
from jianghu.store import Store
from jianghu.server import handler_for


class SocietyTests(unittest.TestCase):
    def world(self):
        w = World(seed=42, population=0, durable=False)
        self.addCleanup(w.store.close)
        return w

    def adult(self, w, place=None, **values):
        n = w.make_npc(place=w.factions[0]["place"] if place is None else place)
        n.update(age_months=360, wealth=100., skill=30., health=100.)
        n.update(values)
        w.set_goal(n, 'belonging', initial=True)
        w.reindex()
        return n

    def action(self, w, n, kind, target):
        return w.execute(n, {'id': f'{kind}:{target}'})

    def test_reconciliation_is_voluntary_and_reserves_both_schedules(self):
        w = self.world()
        a, b = self.adult(w), self.adult(w)
        w.link(a, b, -40, -30)
        b['mood']['anger'] = 80
        w.phase = 1
        total = a['wealth'] + b['wealth']
        e = self.action(w, a, 'reconcile', b['id'])
        self.assertFalse(e['facts']['accepted'])
        self.assertEqual(a['wealth'] + b['wealth'], total)
        self.assertEqual(w.relation(b, a['id'])['affinity'], -40)
        self.assertEqual((a['time_left'], b['time_left']), (3, 3))
        self.assertFalse(social.available(w, b))
        self.assertEqual(b['monthly_actions'][-1]['event_id'], e['id'])
        self.assertIsNone(self.action(w, b, 'reconcile', a['id']))
        w.month += 6
        w.phase = 0
        for n in (a, b):
            social.reset_month(w, n)
        b['mood']['anger'] = 0
        accepted = self.action(w, a, 'reconcile', b['id'])
        self.assertTrue(accepted['facts']['accepted'])
        self.assertEqual(w.relation(b, a['id'])['affinity'], -28)
        self.assertEqual(w.relation(b, a['id'])['label'], '仇怨')

    def test_sworn_bond_requires_reciprocal_consent_and_preserves_resentment(self):
        w = self.world()
        a, b = self.adult(w), self.adult(w)
        w.link(a, b, 60, 40)
        w.relation(b, a['id'])['trust'] = 0
        # The candidate exposes only the actor's feelings, never the hidden refusal.
        self.assertIn(f"swear:{b['id']}", {a['id'] for a in w.legal_actions(a)})
        e = self.action(w, a, 'swear', b['id'])
        self.assertFalse(e['facts']['accepted'])
        self.assertFalse(w.relation(a, b['id'])['sworn'])
        w.month += 12
        for n in (a, b):
            social.reset_month(w, n)
        w.relation(b, a['id'])['trust'] = 40
        e = self.action(w, a, 'swear', b['id'])
        self.assertTrue(e['facts']['accepted'])
        self.assertEqual(w.summary()['sworn_bonds'], 1)
        w.link(a, b, -100, -80)
        self.assertEqual(w.relation(a, b['id'])['label'], '义亲·反目')
        self.assertTrue(w.relation(a, b['id'])['sworn'])

    def test_relationship_statistics_count_living_pairs_with_directional_views(self):
        w = self.world()
        a, b, c = self.adult(w), self.adult(w), self.adult(w)
        w.link(a, b, 40, 20)
        w.link(a, c, 40, 20)
        w.relation(b, a['id'])['affinity'] = -35
        c['alive'] = False
        counts = relationships.counts(w)
        self.assertEqual(counts, {'friendships': 0, 'feuds': 1, 'sworn_bonds': 0})

    def test_absence_cools_positive_feelings_without_forgiving_wrongdoing(self):
        w = self.world()
        a, b = self.adult(w), self.adult(w)
        w.link(a, b, 30, 20, 10)
        w.relation(b, a['id'])['affinity'] = -30
        w.month = 7
        relationships.monthly(w)
        self.assertEqual(w.relation(a, b['id'])['affinity'], 29.65)
        self.assertEqual(w.relation(b, a['id'])['affinity'], -30)
        self.assertEqual(w.relation(a, b['id'])['trust'], 20)
        w.link(a, b, 1)
        self.assertEqual(w.relation(a, b['id'])['last_contact'], 7)

    def test_stale_social_proposal_cannot_overbook_partner(self):
        w = self.world()
        a, b = self.adult(w), self.adult(w)
        w.link(a, b, 60, 40)
        proposed = next(x for x in w.legal_actions(a) if x['kind'] == 'swear')
        b['time_left'] = 0
        self.assertIsNone(w.execute(a, proposed))
        self.assertEqual(a['time_left'], 4)
        self.assertEqual(w.relation(a, b['id'])['sworn'], False)

    def test_faction_task_reward_is_real_and_cannot_be_forged_or_repeated(self):
        w = self.world()
        n = self.adult(w, faction=0)
        f = w.factions[0]
        total = f['fund'] + n['wealth']
        e = self.action(w, n, 'faction_task', 0)
        self.assertEqual(n['contribution'], 2)
        self.assertEqual(n['time_left'], 2)
        self.assertEqual(total, f['fund'] + n['wealth'])
        self.assertAlmostEqual(f['fund'], f['opening_fund'] + f['revenue'] - f['expenses'])
        self.assertEqual(e['facts']['reward'], 3)
        self.assertIsNone(self.action(w, n, 'faction_task', 0))
        w.month += 1
        social.reset_month(w, n)
        n['place'] = 0
        w.reindex()
        self.assertIsNone(self.action(w, n, 'faction_task', 0))
        n['place'] = f['place']
        w.reindex()
        f['fund'] = 0
        self.assertIsNone(self.action(w, n, 'faction_task', 0))

    def test_teaching_pays_tutor_and_treasury_and_uses_a_week_for_both(self):
        w = self.world()
        a = self.adult(w, faction=0, contribution=8.)
        b = self.adult(w, faction=0, skill=60.)
        f = w.factions[0]
        total = a['wealth'] + b['wealth'] + f['fund']
        w.phase = 1
        e = self.action(w, a, 'faction_study', 0)
        self.assertEqual(a['wealth'], 98)
        self.assertEqual(b['wealth'], 101)
        self.assertEqual((a['time_left'], b['time_left']), (3, 3))
        self.assertEqual(total, a['wealth'] + b['wealth'] + f['fund'])
        self.assertEqual(e['facts']['tutor'], b['id'])
        self.assertFalse(social.available(w, b))
        self.assertAlmostEqual(f['fund'], f['opening_fund'] + f['revenue'] - f['expenses'])

    def test_departure_scopes_merit_and_elects_a_living_successor(self):
        w = self.world()
        a = self.adult(w, faction=0, contribution=40., faction_since=0)
        b = self.adult(w, faction=0, contribution=15.)
        f = w.factions[0]
        f['leader'] = a['id']
        e = self.action(w, a, 'leave_faction', 0)
        self.assertIsNone(a['faction'])
        self.assertEqual(a['contribution'], 0)
        self.assertEqual(a['membership_history'][0]['contribution'], 40)
        self.assertEqual(f['leader'], b['id'])
        self.assertEqual(w.store.event(f['last_event'])['facts']['previous'], a['id'])
        self.assertIsNone(self.action(w, a, 'join', 0))
        w.month = 12
        social.reset_month(w, a)
        self.assertIsNotNone(self.action(w, a, 'join', 0))
        self.assertEqual(a['contribution'], 0)
        self.assertEqual(a['faction_since'], 12)
        self.assertEqual(e['facts']['rejoin_after'], 12)

    def test_progression_requires_both_merit_and_skill_and_has_a_cause(self):
        w = self.world()
        n = self.adult(w, faction=0, contribution=10., skill=25.)
        other = self.adult(w, faction=0, contribution=50.)
        w.factions[0]['leader'] = other['id']
        e = self.action(w, n, 'faction_task', 0)
        self.assertEqual(organizations.rank(w, n)['title'], '执事')
        rank_event = next(x for x in w.store.history(n['id']) if x['kind'] == 'faction_rank')
        self.assertIn(e['id'], rank_event['causes'])
        n['contribution'] = 50
        self.assertEqual(organizations.rank(w, n)['title'], '执事')
        n['skill'] = 45
        self.assertEqual(organizations.rank(w, n)['title'], '长老')

    def test_known_hostility_toward_leader_can_motivate_departure(self):
        w = self.world()
        n, leader = self.adult(w, faction=0), self.adult(w, faction=0)
        w.factions[0]['leader'] = leader['id']
        w.link(n, leader, -65, -50)
        n['traits']['loyalty'] = 20
        actions = w.legal_actions(n)
        leave = next(a for a in actions if a['kind'] == 'leave_faction')
        work = next(a for a in actions if a['kind'] == 'work')
        self.assertGreater(w.score(n, leave), w.score(n, work))
        # A leader's private hostility cannot influence this person's choice.
        w.relation(n, leader['id'])['affinity'] = 0
        self.assertLess(w.score(n, leave), w.score(n, work))

    def test_leader_death_is_a_cause_of_succession(self):
        w = self.world()
        leader, member = self.adult(w, faction=0), self.adult(w, faction=0)
        f = w.factions[0]
        f['leader'] = leader['id']
        e = w.die(leader, 'old_age')
        organizations.monthly(w)
        successor = w.store.event(f['last_event'])
        self.assertEqual(f['leader'], member['id'])
        self.assertEqual(successor['facts']['previous'], leader['id'])
        self.assertIn(e['id'], successor['causes'])

    def test_policy_responds_to_local_conditions_and_accounts_remain_separate(self):
        w = self.world()
        chief = self.adult(w, 0, wealth=5.)
        g = w.governments[0]
        g['offices']['县令'] = chief['id']
        chief.update(office='县令', office_country=0)
        others = deepcopy(w.governments[1:])
        with patch.object(w.rng, 'random', return_value=1):
            government.annual(w, g)
        self.assertEqual(g['policy'], 'relief')
        self.assertEqual(g['tax_rate'], .06)
        self.assertEqual(w.governments[1:], others)
        self.assertAlmostEqual(g['fund'], g['opening_fund'] + g['revenue'] - g['expenses'])
        chief.update(wealth=100., health=40.)
        with patch.object(w.rng, 'random', return_value=1):
            government.annual(w, g)
        self.assertEqual(g['policy'], 'health')
        self.assertGreater(g['budget_health'], g['budget_security'])
        chief['health'] = 100
        g['security'] = .2
        with patch.object(w.rng, 'random', return_value=1):
            government.annual(w, g)
        self.assertEqual(g['policy'], 'security')
        self.assertEqual(g['tax_rate'], .11)

    def test_real_border_activity_supports_trade_and_is_symmetric(self):
        w = self.world()
        a = next(c['id'] for c in w.countries if c['relations'])
        b = int(next(iter(w.countries[a]['relations'])))
        w.countries[a]['relations'][str(b)] = w.countries[b]['relations'][str(a)] = '中立'
        for _ in range(8):
            politics.record_crossing(w, {'origin_country': a, 'destination_country': b, 'toll': 5})
        self.assertEqual(w.countries[b]['border_activity'][str(a)]['tariffs'], 40)
        with patch.object(w.rng, 'random', return_value=0), patch.object(w.rng, 'choice', return_value=(a, b)):
            politics.annual(w)
        self.assertEqual(w.countries[a]['relations'][str(b)], '通商')
        self.assertEqual(w.countries[b]['relations'][str(a)], '通商')
        self.assertEqual(w.countries[a]['border_activity'], {})
        self.assertEqual(w.countries[a]['last_year_borders'][str(b)]['crossings'], 8)

    def test_player_visits_have_a_saved_monthly_limit_and_bounded_ties(self):
        w = self.world()
        n = self.adult(w)
        w.intervene(n['id'], 'visit')
        self.assertEqual(w.npc_detail(n['id'])['player_relation']['trust'], 2)
        with self.assertRaisesRegex(ValueError, '本月已'):
            w.intervene(n['id'], 'visit')
        tie = w.player['ties'][str(n['id'])]
        tie['affinity'] = tie['trust'] = 99
        w.intervene(n['id'], 'gift')
        self.assertEqual((tie['affinity'], tie['trust']), (100, 100))
        w.battle = {'status': 'active'}
        with self.assertRaisesRegex(ValueError, '切磋'):
            w.intervene(n['id'], 'visit')

    def test_old_unified_save_preserves_funds_events_and_unknown_join_dates(self):
        w = self.world()
        n = self.adult(w, faction=0, contribution=14.)
        other = self.adult(w)
        w.link(n, other, 30, 20)
        w.factions[0]['fund'] = 347.5
        state = json.loads(json.dumps(w.export()))
        for f in state['factions']:
            for key in organizations.STATE_FIELDS:
                if key not in ('leader', 'fund'):
                    f.pop(key, None)
        for n in state['npcs'].values():
            for key in ('faction_since', 'faction_rejoin_after', 'membership_history'):
                n.pop(key, None)
            for r in n['relationships'].values():
                for key in ('last_contact', 'contact_since', 'sworn'):
                    r.pop(key, None)
        for g in state['governments']:
            for key in ('policy', 'policy_reason', 'policy_since', 'records', 'budget_health', 'budget_security'):
                g.pop(key, None)
        count = w.store.count()
        w.restore(state)
        self.assertEqual(w.store.count(), count)
        self.assertEqual(w.factions[0]['opening_fund'], 347.5)
        self.assertEqual(w.npcs[1]['contribution'], 14)
        self.assertIsNone(w.npcs[1]['faction_since'])
        self.assertIsNone(w.npcs[1]['relationships']['2']['last_contact'])
        self.assertEqual(w.npcs[1]['relationships']['2']['contact_since'], w.month)
        self.assertEqual(w.factions[0]['records'], [])

    def test_new_systems_resume_deterministically_with_full_evidence(self):
        w = self.world()
        a, b = self.adult(w, faction=0, contribution=8.), self.adult(w, faction=0, skill=55.)
        w.link(a, b, 60, 40)
        self.action(w, a, 'swear', b['id'])
        self.action(w, a, 'faction_task', 0)
        w.intervene(a['id'], 'visit')
        w.checkpoint()
        copied = Store()
        self.addCleanup(copied.close)
        w.store.db.backup(copied.db)
        resumed = World(store=copied, durable=False)
        self.assertEqual(w.export(), resumed.export())
        w.month_step()
        resumed.month_step()
        self.assertEqual(w.npcs, resumed.npcs)
        self.assertEqual(w.factions, resumed.factions)
        self.assertEqual(w.countries, resumed.countries)
        self.assertEqual(w.governments, resumed.governments)
        self.assertIn('my_contribution', context_for(w, w.npcs[a['id']]))
        self.assertNotIn('reciprocal', json.dumps(context_for(w, w.npcs[a['id']]), ensure_ascii=False))


class SocietyHTTPTests(unittest.TestCase):
    def setUp(self):
        self.world = World(population=5, durable=False)
        game = SimpleNamespace(world=self.world, lock=threading.RLock(), autoplay=False, job={'remaining': 0},
                               ai=SimpleNamespace(schedule=lambda *_: False))
        self.server = ThreadingHTTPServer(('127.0.0.1', 0), handler_for(game))
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.base = f'http://127.0.0.1:{self.server.server_port}'

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=3)
        self.world.store.close()

    def request(self, path, body=None):
        req = urllib.request.Request(self.base + path, json.dumps(body).encode() if body is not None else None,
                                     {'Content-Type': 'application/json'})
        with urllib.request.urlopen(req, timeout=5) as response:
            return json.load(response)

    def test_institution_views_are_read_only_and_reject_invalid_ids(self):
        before = deepcopy(self.world.export())
        self.assertIn('policy_catalog', self.request('/api/country/0'))
        self.assertIn('roster', self.request('/api/faction/0'))
        self.assertEqual(before, self.world.export())
        for path in ('/api/country/-1', f'/api/country/{len(self.world.countries)}',
                     '/api/faction/-1', f'/api/faction/{len(self.world.factions)}'):
            with self.assertRaises(urllib.error.HTTPError) as caught:
                self.request(path)
            self.assertEqual(caught.exception.code, 400)

    def test_visit_is_persisted_and_repetition_is_rejected_through_http(self):
        n = self.world.npcs[1]
        n['health'], n['mood']['anger'] = 100, 0
        self.request('/api/intervene', {'npc_id': 1, 'action': 'visit'})
        detail = self.request('/api/npc/1')
        self.assertEqual(detail['player_relation']['affinity'], 4)
        self.assertEqual(self.world.store.load()['player']['ties']['1']['visit_month'], self.world.month)
        before = deepcopy(self.world.export())
        with self.assertRaises(urllib.error.HTTPError) as caught:
            self.request('/api/intervene', {'npc_id': 1, 'action': 'visit'})
        self.assertEqual(caught.exception.code, 400)
        self.assertEqual(self.world.export(), before)


if __name__ == '__main__':
    unittest.main()
