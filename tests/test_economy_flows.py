"""Silver that institutions collect finds its way back to people, and every flow balances."""
from copy import deepcopy
import unittest

from jianghu import careers, economics as E, organizations, population
from jianghu.engine import World


class EconomyFlowTests(unittest.TestCase):
    def world(self, population_size=0, **extra):
        w = World(7, population_size, compact=True, durable=False, market_economy=True, **extra)
        self.addCleanup(w.store.close)
        return w

    def market(self, w):
        place = w.active_places[0]['id']
        return place, E.market(w, place)

    def test_cash_rich_markets_narrow_their_spread(self):
        w = self.world(); place, m = self.market(w)
        m['cash'] = 0.
        self.assertAlmostEqual(E.bid(m, 'grain'), m['prices']['grain'] * .85)
        self.assertAlmostEqual(E.ask(m, 'grain'), m['prices']['grain'])
        m['cash'] = E.float_needed(m) * 3
        self.assertGreater(E.bid(m, 'grain'), m['prices']['grain'] * .9)
        self.assertLess(E.ask(m, 'grain'), m['prices']['grain'])
        self.assertLessEqual(E.bid(m, 'grain'), m['prices']['grain'] * E.MAX_BID + 1e-9)
        for ratio in (.5, 1., 1.5, 3., 10., 100.):
            m['cash'] = E.float_needed(m) * ratio
            self.assertLess(E.bid(m, 'grain'), E.ask(m, 'grain'))
        m['cash'] = E.float_needed(m) * 50
        self.assertAlmostEqual(E.ask(m, 'grain'), m['prices']['grain'] * (1 - E.MAX_DISCOUNT))

    def test_buying_and_selling_conserve_silver_at_the_quoted_price(self):
        w = self.world(30); n = next(n for n in w.npcs.values() if n['age_months'] >= 192)
        place, m = self.market(w); n['place'] = place; w.reindex()
        m['cash'] = E.float_needed(m) * 2.5; m['stock']['cloth'] = 0.; E.prepare(n)['stock']['cloth'] = 3.
        total = E.total_cash(w); unit = E.bid(m, 'cloth')
        amount, value = E.sell(w, n['id'], 'cloth', 1.)
        self.assertAlmostEqual(value, round(amount * unit, 4))
        unit = E.ask(m, 'cloth'); amount, cost = E.buy(w, n['id'], 'cloth', .5)
        self.assertAlmostEqual(cost, round(amount * unit, 4))
        self.assertAlmostEqual(E.total_cash(w), total)

    def test_paid_services_use_up_the_capacity_the_market_bought(self):
        w = self.world(); place, m = self.market(w)
        m['stock']['transport'] = 5.; consumed = E.state(w)['consumed']['transport']; cash = m['cash']
        unit = E.ask(m, 'transport')
        E.fee(w, place, unit * 2, 'transport')
        self.assertAlmostEqual(m['stock']['transport'], 3., places=3)
        self.assertAlmostEqual(E.state(w)['consumed']['transport'] - consumed, 2., places=3)
        self.assertAlmostEqual(m['cash'], cash + unit * 2)
        m['stock']['leisure'] = 0.
        E.fee(w, place, 5., 'leisure')
        self.assertEqual(m['stock']['leisure'], 0.)

    def test_gaming_takings_above_the_float_are_spent_in_towns(self):
        w = self.world(40)
        w.economy['gaming_house'] = 900.; total = E.total_cash(w)
        before = sum(m['cash'] for m in E.state(w)['markets'].values())
        paid = E.spend_gaming_surplus(w)
        self.assertAlmostEqual(paid, 300.)
        self.assertAlmostEqual(w.economy['gaming_house'], 600.)
        self.assertAlmostEqual(sum(m['cash'] for m in E.state(w)['markets'].values()) - before, 300.)
        self.assertAlmostEqual(E.total_cash(w), total)
        w.economy['gaming_house'] = 250.
        self.assertEqual(E.spend_gaming_surplus(w), 0.)

    def test_sects_pay_stipends_and_relief_only_above_their_float(self):
        w = self.world(); f = w.factions[0]
        n = w.make_npc(place=f['place']); n.update(faction=0, age_months=360, health=100., wealth=5., contribution=0.)
        w.set_goal(n, 'renown', initial=True)
        w.reindex(); reserve = organizations.float_needed(w, f)
        f['fund'] = reserve + 20; total = f['fund'] + n['wealth']
        organizations.relieve(w, f, 6., 15.)
        self.assertEqual(n['wealth'], 11.)
        organizations.relieve(w, f, 6., 15.)
        self.assertEqual(n['wealth'], 17.)
        self.assertAlmostEqual(f['fund'] + n['wealth'], total)
        f['fund'] = reserve + 3; n['wealth'] = 5.
        organizations.relieve(w, f, 6., 15.)
        self.assertEqual(n['wealth'], 5.)
        n['time_left'] = 4
        action = next(a for a in w.legal_actions(n) if a['kind'] == 'sect_service')
        f['fund'] = reserve
        e = w.execute(n, action)
        self.assertEqual(e['facts']['stipend'], 0.)
        f['fund'] = reserve + 10; n['cooldowns'] = {}; n['action_counts'] = {}; n['time_left'] = 4
        w.month += 1
        action = next(a for a in w.legal_actions(n) if a['kind'] == 'sect_service')
        e = w.execute(n, action)
        self.assertEqual(e['facts']['stipend'], 1.)
        self.assertEqual(f['fund'], reserve + 9)

    def test_sect_livelihoods_follow_tradition_without_touching_the_world_rng(self):
        w = self.world(); state = w.rng.getstate()
        temple = next(f for f in w.factions if f['tradition'] == '佛门')
        trades = [population.sect_trade(w, temple, dict(id=i)) for i in range(400)]
        self.assertEqual(w.rng.getstate(), state)
        self.assertGreater(trades.count('农人'), trades.count('镖师') * 2)
        self.assertEqual(trades, [population.sect_trade(w, temple, dict(id=i)) for i in range(400)])

    def test_trades_are_judged_by_neighbours_actual_earnings(self):
        w = self.world(); place, m = self.market(w)
        def person(job, ema):
            n = w.make_npc(place=place); n.update(age_months=360, health=100., wealth=60.)
            n['career'].update(profession=job, income_ema=ema); return n
        me = person('农人', 2.)
        w.set_goal(me, 'freedom', initial=True)
        for ema in (9., 10., 11.):
            person('匠人', ema)
        w.reindex()
        self.assertAlmostEqual(careers.switch_gain(w, me, '匠人'), 10. - max(E.expected_income(w, me), .6*2 + .4*E.expected_income(w, me)), places=3)

    def test_a_rich_court_hires_the_poor_for_public_works(self):
        from jianghu import government, territory
        w = self.world(); place = w.active_places[0]['id']; g = w.governments[territory.owner(w, place)]
        poor = []
        for wealth in (2., 5., 40.):
            n = w.make_npc(place=place); n.update(age_months=360, health=80., wealth=wealth, time_left=4); poor.append(n)
        w.reindex()
        reserve, _ = government.treasury_float(w, g)
        g['fund'] = reserve; self.assertIsNone(government.public_works(w, g))
        g['fund'] = reserve + 100; total = g['fund'] + sum(n['wealth'] for n in poor)
        e = government.public_works(w, g)
        self.assertEqual(e['facts']['workers'], [poor[0]['id'], poor[1]['id']])
        self.assertEqual((poor[0]['wealth'], poor[1]['wealth'], poor[2]['wealth']), (5., 8., 40.))
        self.assertEqual(poor[0]['time_left'], 3)
        self.assertAlmostEqual(g['fund'] + sum(n['wealth'] for n in poor), total)

    def test_a_simulated_year_keeps_the_books_balanced(self):
        w = self.world(120, population_profile=True)
        for _ in range(6):
            w.month_step()
        audit = E.audit(w)
        self.assertAlmostEqual(audit['cash_residual'], 0., places=3)
        self.assertFalse(audit['negative_stock']); self.assertFalse(audit['negative_market_cash'])
        for f in w.factions:
            self.assertAlmostEqual(f['fund'], f['opening_fund'] + f['revenue'] - f['expenses'], places=3)
            self.assertGreaterEqual(f['fund'], 0.)


if __name__ == '__main__':
    unittest.main()
