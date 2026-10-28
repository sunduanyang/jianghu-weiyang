"""Sect life: commissions, precepts, lineage, trials, seniors and NPC duty."""
from copy import deepcopy
from pathlib import Path
import tempfile
import unittest

from jianghu import city_maps, organizations, player_interactions, protagonist, rank_progression, sect_life
from jianghu.engine import World
from jianghu.store import Store
from tests.combat_helpers import set_rating
from tests.physical_helpers import frame


class SectLifeTests(unittest.TestCase):
    def setUp(self):
        self.w = w = World(91, 0, compact=True, durable=False, rank_assessments=True)
        self.addCleanup(w.store.close)
        self.f = f = w.factions[0]
        protagonist.perform(w, dict(action='create', revision=0, month=0, name='门中验收', sex='女', age=24,
                                   place=f['place']))
        self.elder = self.npc(faction=0, contribution=35., rating=45.)
        self.elder['traits'].update(loyalty=80., compassion=80.)
        self.join()

    # ---------------------------------------------------------- fixtures
    def npc(self, place=None, faction=None, contribution=0., rating=40., **values):
        w = self.w
        n = w.make_npc(place=self.f['place'] if place is None else place)
        n.update(faction=faction, contribution=contribution, faction_since=0 if faction is not None else None,
                 age_months=360, health=100., wealth=60., spouse=None)
        n['mood'].update(anger=0., joy=0.)
        n.update(values)
        set_rating(n, rating)
        w.set_goal(n, 'mastery', initial=True)
        rank_progression.prepare(w, n['id'])
        w.reindex()
        return n

    @property
    def h(self):
        return self.w.player['protagonist']

    def join(self):
        self.interact(self.elder, 'join_sect')

    def interact(self, n, action, **extra):
        return player_interactions.perform(self.w, dict(npc_id=n['id'], action=action, revision=self.h['revision'],
                                                        month=self.w.month, **extra))

    def hero(self, action, choice=None):
        body = dict(action=action, faction=self.f['id'], revision=self.h['revision'], month=self.w.month)
        if choice is not None:
            body['choice'] = choice
        return protagonist.perform(self.w, body)

    def enter_seat(self):
        city = city_maps.world_layout(self.w, self.f['place'])
        seat = next(s for s in city['settlements'] if s['kind'] == 'sect' and s['faction'] == self.f['id'])
        self.h.update(place=self.f['place'], row=seat['row'], col=seat['col'])

    def leave_seat(self):
        gate = next(n for n in city_maps.world_layout(self.w, self.f['place'])['nodes'] if n['id'] == 'gate')
        self.h.update(row=gate['row'], col=gate['col'])

    def offer(self, kind):
        return next(c for c in sect_life.board(self.w, self.f) if c['kind'] == kind)

    def accept(self, kind):
        c = self.offer(kind)
        self.hero('sect_accept', c['id'])
        return c

    def set_grade(self, grade):
        rank_progression.prepare(self.w, 'player')['sect']['grade'] = grade

    def reject(self, call):
        before = deepcopy(self.w.export()); count = self.w.store.count()
        with self.assertRaises(ValueError):
            call()
        self.assertEqual(self.w.export(), before)
        self.assertEqual(self.w.store.count(), count)

    def finish(self, winner=0):
        w = self.w; b = w.battle; packet = frame(b, 180)
        packet['fighters'][1-winner]['health'] = 0
        packet['fighters'][winner]['health'] *= .7
        packet['hits'] = 6
        for fighter, saved in zip(packet['fighters'], b['fighters']):
            fighter['landed_hits'] = 3; fighter['attack_count'] = 3
            fighter['balance'] = min(fighter['balance'], saved['balance_max'])
        return w.battle_action(b['id'], b['revision'], 'complete', packet)

    def next_month(self):
        self.w.month += 1; self.h['points'] = 30; self.h['revision'] += 1
        organizations.monthly(self.w); rank_progression.monthly(self.w)

    def reload(self):
        folder = tempfile.TemporaryDirectory(); self.addCleanup(folder.cleanup)
        path = Path(folder.name)/'world.sqlite'
        self.w.checkpoint(); store = Store(path); self.w.store.db.backup(store.db); store.close()
        self.w = World(store=Store(path), durable=False, compact=True, rank_assessments=True)
        self.addCleanup(self.w.store.close)
        self.f = self.w.factions[self.f['id']]

    # ------------------------------------------------------------- board
    def test_board_and_sect_page_are_pure_and_deterministic(self):
        w = self.w; before = deepcopy(w.export()); rng = w.rng.getstate(); count = w.store.count()
        first = sect_life.board(w, self.f)
        self.assertEqual(first, sect_life.board(w, self.f))
        detail = w.faction_detail(self.f['id'])
        self.assertTrue(detail['sect_life']['member'])
        self.assertEqual(detail['sect_life']['player']['board'][0]['kind'], 'patrol')
        self.assertEqual(len(detail['sect_life']['relations']), len(w.factions)-1)
        player_interactions.view(w, self.elder['id'])
        self.assertEqual(w.export(), before); self.assertEqual(w.rng.getstate(), rng); self.assertEqual(w.store.count(), count)

    def test_outsiders_see_recruiters_and_precepts(self):
        other = self.w.factions[1]
        self.npc(faction=1, contribution=35., rating=45.)
        view = sect_life.view(self.w, other)
        self.assertFalse(view['member'])
        self.assertTrue(view['recruiters'])
        self.assertEqual(view['precepts'][0]['label'], '同门不相残')

    def test_accept_needs_the_seat_is_atomic_and_limited(self):
        c = self.offer('patrol')
        self.leave_seat()
        self.reject(lambda: self.hero('sect_accept', c['id']))
        self.reject(lambda: self.hero('sect_accept', 'forged:id'))
        self.enter_seat()
        self.hero('sect_accept', c['id'])
        self.assertEqual([x['id'] for x in sect_life.state(self.w)['commissions']], [c['id']])
        self.reject(lambda: self.hero('sect_accept', c['id']))
        self.accept('escort')
        third = self.offer('relief') if any(x['kind'] == 'relief' for x in sect_life.board(self.w, self.f)) else None
        if third:
            self.reject(lambda: self.hero('sect_accept', third['id']))

    def test_patrol_pays_contribution_and_counts_as_monthly_service(self):
        self.enter_seat(); c = self.accept('patrol'); points = self.h['points']
        self.hero('sect_complete', c['id'])
        self.assertEqual(self.h['contribution'], 2.)
        self.assertEqual(self.h['points'], points-4)
        self.assertIn(self.w.month, self.h['rank_progress']['activity']['service'])
        self.assertEqual(sect_life.state(self.w)['completed'], 1)
        self.reject(lambda: self.hero('sect_complete', c['id']))

    def test_escort_completes_only_at_destination_and_pays_from_treasury(self):
        self.enter_seat(); c = self.accept('escort'); f = self.f
        self.reject(lambda: self.hero('sect_complete', c['id']))
        self.h['place'] = c['place']
        wealth, fund = self.w.player['wealth'], f['fund']
        self.hero('sect_complete', c['id'])
        self.assertEqual(self.w.player['wealth'], wealth+3); self.assertEqual(f['fund'], fund-3)
        self.assertAlmostEqual(f['fund'], f['opening_fund']+f['revenue']-f['expenses'])
        self.assertEqual(self.h['contribution'], 4.)

    def test_relief_hands_real_treasury_silver_to_the_named_person(self):
        poor = self.npc(wealth=2.)
        self.enter_seat(); c = self.accept('relief')
        self.assertEqual(c['target'], poor['id'])
        stranger = self.npc()
        self.assertNotIn('sect_errand', [a['id'] for a in player_interactions.view(self.w, stranger['id'])['actions']])
        fund = self.f['fund']; reputation = self.w.player['reputation']
        self.interact(poor, 'sect_errand')
        self.assertEqual(poor['wealth'], 6.); self.assertEqual(self.f['fund'], fund-4)
        self.assertEqual(self.h['contribution'], 3.); self.assertEqual(self.w.player['reputation'], reputation+1)
        self.assertFalse(sect_life.state(self.w)['commissions'])
        self.assertAlmostEqual(self.f['fund'], self.f['opening_fund']+self.f['revenue']-self.f['expenses'])

    def test_letter_to_another_sect_improves_relations(self):
        envoy = self.npc(faction=1, contribution=12., rating=25.)
        before = sect_life.relation(self.w, 0, 1)
        self.enter_seat()
        self.reject(lambda: self.hero('sect_accept', self.offer('visit')['id']))
        self.set_grade(8); c = self.accept('visit')
        self.assertEqual((c['target'], c['other']), (envoy['id'], 1))
        self.interact(envoy, 'sect_errand')
        self.assertEqual(sect_life.relation(self.w, 0, 1), before+6)
        self.assertEqual(sect_life.relation(self.w, 1, 0), before+6)

    def test_hunt_is_a_real_battle_that_only_pays_on_victory_and_survives_reload(self):
        outlaw = self.npc(reputation=-40.)
        self.enter_seat()
        self.assertFalse(any(c['kind'] == 'hunt' and not sect_life.accept_reason(self.w, self.f, c)
                             for c in sect_life.board(self.w, self.f)))
        self.set_grade(7)
        c = self.accept('hunt')
        reputation = self.w.player['reputation']
        result = self.interact(outlaw, 'sect_errand')
        self.assertIn('martial', result)
        self.assertFalse(self.w.battle['spar']); self.assertEqual(self.w.battle['encounter'], 'sect_hunt')
        self.assertEqual(self.w.player['reputation'], reputation)
        self.finish(1)
        self.assertEqual([x['id'] for x in sect_life.state(self.w)['commissions']], [c['id']])
        self.h['points'] = 30; self.w.player['health'] = 100; outlaw['health'] = 100; outlaw['wound'] = None
        self.interact(outlaw, 'sect_errand')
        self.reload(); outlaw = self.w.npcs[outlaw['id']]
        wealth = self.w.player['wealth']
        self.finish(0)
        self.assertFalse(sect_life.state(self.w)['commissions'])
        self.assertEqual(self.w.player['wealth'], wealth+5)
        self.assertEqual(self.w.player['reputation'], reputation+3)

    def test_expired_and_abandoned_commissions_cost_contribution(self):
        self.h['contribution'] = 5.
        self.enter_seat(); self.accept('patrol'); self.accept('escort')
        self.hero('sect_abandon', f"{self.f['id']}:{self.w.month}:patrol")
        self.assertEqual(self.h['contribution'], 4.)
        for _ in range(3):
            self.next_month()
        s = sect_life.state(self.w)
        self.assertEqual((s['commissions'], s['failed']), ([], 2))
        self.assertEqual(self.h['contribution'], 3.)

    def test_precepts_record_discipline_penance_and_expulsion(self):
        w = self.w; peer = self.npc(faction=0, contribution=1., rating=20.)
        self.interact(peer, 'insult')
        s = sect_life.state(w)
        self.assertEqual(s['discipline'][-1]['precepts'], ['同门不相残'])
        sect = self.h['rank_progress']['sect']
        self.assertEqual(sect['penalty'], 2.); self.assertEqual(sect['censure_until'], w.month+6)
        self.enter_seat(); self.hero('sect_penance', str(s['discipline'][-1]['event_id']))
        self.assertEqual(sect['penalty'], 1.); self.assertFalse(sect_life.unatoned(w))
        victims = [self.npc(wealth=40.), self.npc(wealth=40.)]
        for victim in victims:
            self.h['points'] = 30; w.battle = None
            self.interact(victim, 'rob')
        self.assertIsNone(self.h['faction'])
        self.assertEqual(self.h['membership_history'][-1]['reason'], '违反门规被逐出')
        self.assertEqual(self.h['faction_rejoin_after'], w.month+24)

    def test_outsider_harm_is_not_a_breach_for_every_tradition(self):
        w = self.w; bystander = self.npc(wealth=40.)
        self.interact(bystander, 'insult')
        self.assertFalse(sect_life.state(w)['discipline'])

    def test_master_lineage_counsel_manual_and_recommendation(self):
        w = self.w; r = w.player['ties'].setdefault(str(self.elder['id']), dict(affinity=0., trust=0.))
        self.assertIn('sect_apprentice', [a['id'] for a in player_interactions.view(w, self.elder['id'])['actions']])
        self.reject(lambda: self.interact(self.elder, 'sect_apprentice'))
        r.update(affinity=20., trust=10.)
        self.interact(self.elder, 'sect_apprentice')
        self.assertEqual(sect_life.master(w)['id'], self.elder['id'])
        self.interact(self.elder, 'sect_counsel')
        self.assertEqual(self.h['contribution'], 1.)
        self.reject(lambda: self.interact(self.elder, 'sect_counsel'))
        self.elder['traits'].update(loyalty=0., compassion=0.); r['affinity'] = 0.
        art = next(o['art'] for o in player_interactions.view(w, self.elder['id'])['manual_offers'] if not o['reason'])
        result = self.interact(self.elder, 'teach', art=art)
        self.assertTrue(result['event']['facts']['accepted']); self.assertTrue(result['event']['facts']['lineage'])
        r['affinity'] = 40.
        conditions = rank_progression.sect_conditions(w, 'player')
        self.assertEqual(conditions['sponsor']['id'], self.elder['id'])
        self.assertEqual(conditions['required_months'], rank_progression.PROMOTION_MONTHS[conditions['target']]-1)
        self.elder['alive'] = False
        self.next_month()
        self.assertIsNone(sect_life.state(w)['master'])

    def qualify_for_trial(self):
        w = self.w; self.h['contribution'] = 1.
        self.reject(lambda: self.interact(self.elder, 'sect_trial'))
        self.enter_seat(); c = self.accept('patrol'); self.hero('sect_complete', c['id'])
        conditions = rank_progression.sect_conditions(w, 'player')
        self.assertTrue(conditions['qualified']); self.assertEqual(conditions['target'], 8)
        self.reject(lambda: self.interact(self.elder, 'sect_trial'))
        self.next_month()
        c = self.accept('patrol'); self.hero('sect_complete', c['id'])
        conditions = rank_progression.sect_conditions(w, 'player')
        self.assertEqual((conditions['completed_months'], conditions['required_months']), (1, 2))

    def test_trial_loss_keeps_grade_and_waits_a_month(self):
        w = self.w; self.qualify_for_trial()
        result = self.interact(self.elder, 'sect_trial')
        self.assertIn('martial', result); self.assertTrue(w.battle['spar'])
        self.assertEqual(w.battle['encounter'], 'sect_trial')
        self.finish(1)
        self.assertEqual(organizations.player_membership(w)['grade'], 9)
        self.reject(lambda: self.interact(self.elder, 'sect_trial'))

    def test_trial_win_promotes_before_the_required_months(self):
        w = self.w; self.qualify_for_trial()
        self.interact(self.elder, 'sect_trial')
        self.finish(0)
        self.assertEqual(organizations.player_membership(w)['grade'], 8)
        change = w.store.event(self.h['rank_progress']['history'][-1]['event_id'])
        self.assertEqual(change['facts']['direction'], '升阶')
        self.assertIn('晋升比试', change['facts']['reason'])

    def test_seniors_guide_juniors_and_recruit_outsiders(self):
        w = self.w; junior = self.npc(faction=0, contribution=0., rating=20.)
        self.assertNotIn('sect_guide', [a['id'] for a in player_interactions.view(w, junior['id'])['actions']])
        self.set_grade(4)
        self.interact(junior, 'sect_guide')
        self.assertEqual(junior['contribution'], .5)
        self.assertIn(w.month, self.h['rank_progress']['activity']['service'])
        self.reject(lambda: self.interact(junior, 'sect_guide'))
        outsider = self.npc(rating=30.)
        outsider['beliefs']['tradition'] = .9
        self.assertNotIn('sect_recruit', [a['id'] for a in player_interactions.view(w, outsider['id'])['actions']])
        self.set_grade(3)
        self.reject(lambda: self.interact(outsider, 'sect_recruit'))
        w.player['ties'][str(outsider['id'])] = dict(affinity=40., trust=20.)
        self.interact(outsider, 'sect_recruit')
        self.assertEqual(outsider['faction'], 0)
        self.assertEqual(organizations.rank(w, outsider)['grade'], 9)

    def test_sect_life_survives_save_and_reload(self):
        self.enter_seat(); self.accept('escort')
        player = deepcopy(self.w.player)
        self.reload()
        self.assertEqual(self.w.player, player)
        self.assertEqual(len(sect_life.state(self.w)['commissions']), 1)

    def test_npc_service_is_a_real_duty_that_counts_for_assessment(self):
        w = self.w; n = self.npc(faction=0, contribution=12., rating=30.)
        n['time_left'] = 4
        action = next(a for a in w.legal_actions(n) if a['kind'] == 'sect_service')
        e = w.execute(n, action)
        self.assertEqual(n['contribution'], 13.)
        self.assertIn(w.month, n['rank_progress']['activity']['service'])
        self.assertEqual(e['kind'], 'sect_service')


if __name__ == '__main__':
    unittest.main()
