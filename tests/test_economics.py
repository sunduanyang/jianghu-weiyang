from copy import deepcopy
import math
import unittest

from jianghu import economics as e, protagonist, items
from jianghu.engine import World
from jianghu.store import Store


class EconomyTests(unittest.TestCase):
    def setUp(self):
        self.w = World(seed=7, population=0, durable=False)
        self.addCleanup(self.w.store.close)
        self.n = self.w.make_npc(place=self.w.active_places[0]['id'])
        self.n.update(age_months=360, wealth=100., job='农人')
        self.n['career'].update(profession='农人', experience={'农人':20.})
        self.w.set_goal(self.n, 'wealth', initial=True)
        self.w.reindex()
        self.pre_registration=deepcopy(self.w.export())
        e.initialize(self.w)
        self.m = e.market(self.w,self.n['place'])

    def baseline(self):
        system=e.state(self.w)
        system.update(opening_cash=e.total_cash(self.w),opening_goods=e.total_goods(self.w),destroyed_silver=0.)
        for field in ('produced','consumed','spoiled'):
            system[field]={g:0. for g in e.GOODS}

    def balanced(self):
        report=e.audit(self.w)
        self.assertLess(abs(report['cash_residual']),.001)
        self.assertTrue(all(abs(x)<.0001 for x in report['goods_residuals'].values()),report)
        self.assertFalse(report['negative_market_cash']);self.assertFalse(report['negative_stock'])

    def test_registration_preserves_wallets_calendar_rng_and_is_idempotent(self):
        self.assertEqual(self.pre_registration['month'],self.w.month)
        self.assertEqual(self.pre_registration['random_state'],self.w.rng.getstate())
        self.assertEqual(self.pre_registration['npcs'][str(self.n['id'])]['wealth'] if str(self.n['id']) in self.pre_registration['npcs'] else self.pre_registration['npcs'][self.n['id']]['wealth'],self.n['wealth'])
        self.balanced()
        before=deepcopy(self.w.export());events=self.w.store.next_id
        self.assertFalse(e.initialize(self.w))
        self.assertEqual(before,self.w.export());self.assertEqual(events,self.w.store.next_id)
        event=self.w.store.event(e.state(self.w)['registration_event'])
        self.assertTrue(event['facts']['initial_condition'])
        self.assertEqual(event['month'],self.w.month)

    def test_buy_sell_use_real_stock_and_counterparty_money(self):
        own=e.prepare(self.n)['stock']['grain'];stock=self.m['stock']['grain']
        bought,cost=e.buy(self.w,self.n['id'],'grain',2)
        self.assertEqual(e.prepare(self.n)['stock']['grain'],own+bought)
        self.assertEqual(self.m['stock']['grain'],stock-bought)
        sold,value=e.sell(self.w,self.n['id'],'grain',bought)
        self.assertLess(value,cost);self.assertEqual(sold,bought);self.balanced()

    def test_empty_market_cannot_create_food_for_a_rich_household(self):
        for g in e.FOOD:self.m['stock'][g]=0.;e.prepare(self.n)['stock'][g]=0.
        self.baseline()
        paid,missing=e.consume(self.w,self.n['id'],'food',1.,self.n['id'])
        self.assertEqual(paid,0.);self.assertEqual(missing,1.);self.assertEqual(self.n['wealth'],100.)
        self.balanced()

    def test_food_substitution_consumes_fish_when_grain_missing(self):
        for g in e.FOOD:self.m['stock'][g]=0.;e.prepare(self.n)['stock'][g]=0.
        self.m['stock']['fish']=2.;self.baseline()
        _,missing=e.consume(self.w,self.n['id'],'food',1.,self.n['id'])
        self.assertEqual(missing,0.);self.assertEqual(e.state(self.w)['consumed']['fish'],1.)
        self.balanced()

    def test_sale_stops_when_market_has_no_silver(self):
        self.m['cash']=0.;self.baseline();before=self.n['wealth']
        amount,value=e.sell(self.w,self.n['id'],'grain',.5)
        self.assertEqual((amount,value),(0.,0.));self.assertEqual(self.n['wealth'],before);self.balanced()

    def test_existing_food_is_used_before_purchasing_cheaper_food(self):
        e.prepare(self.n)['stock'].update(grain=0.,fish=1.)
        self.baseline();paid,missing=e.consume(self.w,self.n['id'],'food',1.,self.n['id'])
        self.assertEqual((paid,missing),(0.,0.));self.assertEqual(e.prepare(self.n)['stock']['fish'],0.)
        self.balanced()

    def test_discretionary_consumption_preserves_emergency_cash_but_uses_own_services(self):
        self.n['wealth']=5.;e.prepare(self.n)['stock']['study']=.07;self.baseline()
        paid,missing=e.consume(self.w,self.n['id'],'study',.07,self.n['id'],reserve=6.)
        self.assertEqual((paid,missing),(0.,0.));self.assertEqual(self.n['wealth'],5.)
        self.balanced()

    def test_production_has_finite_sales_and_preserves_unsold_goods(self):
        self.m['cash']=0.;self.baseline();before=self.n['wealth']
        result=e.work(self.w,self.n)
        self.assertGreater(result['produced']['grain'],0.)
        self.assertEqual(self.n['wealth'],before)
        self.assertGreater(e.prepare(self.n)['stock']['grain'],.5);self.balanced()

    def test_artisan_requires_inputs_and_spends_them_once(self):
        self.n['career']['profession']='匠人'
        for g in ('fiber','timber','iron'):self.m['stock'][g]=0.
        self.baseline();result=e.work(self.w,self.n)
        self.assertEqual(result['produced'],{});self.balanced()
        self.m['stock']['fiber']=10.;self.m['stock']['cloth']=0.;self.m['cash']=100.
        self.baseline();result=e.work(self.w,self.n)
        self.assertGreater(result['produced']['cloth'],0.)
        self.assertGreater(e.state(self.w)['consumed']['fiber'],0.);self.balanced()

    def test_prices_respond_to_local_scarcity_with_bounds(self):
        self.m['stock']['grain']=0.;self.m['stock']['iron']=1000.
        e.begin_month(self.w)
        self.assertGreater(self.m['prices']['grain'],2.)
        self.assertLess(self.m['prices']['iron'],4.)
        for _ in range(40):e.begin_month(self.w)
        self.assertLessEqual(self.m['prices']['grain'],2.*2.4)
        self.assertGreaterEqual(self.m['prices']['iron'],4.*.65)

    def test_child_is_fed_from_guardians_goods_before_money(self):
        child=self.w.make_npc(parents=[self.n['id']],place=self.n['place'])
        child.update(age_months=60,guardian=self.n['id']);self.w.reindex()
        e.prepare(self.n)['stock']['grain']=4.;self.baseline()
        e.close_month(self.w)
        self.assertEqual(child['monthly_settlement']['payer'],self.n['id'])
        self.assertEqual(child['monthly_settlement']['food_shortfall'],0.)
        self.assertEqual(e.prepare(child)['needs']['food']['paid'],0.);self.balanced()

    def test_settlement_spoilage_and_unused_services_recorded_once(self):
        self.m['stock']['fish']=10.;self.m['stock']['study']=10.;self.baseline()
        e.close_month(self.w);self.balanced();before=deepcopy(self.w.export())
        self.assertGreater(e.state(self.w)['spoiled']['fish'],0.)
        self.assertEqual(self.m['stock']['study'],0.)
        e.close_month(self.w);self.assertEqual(before,self.w.export())

    def test_adjacent_trade_moves_goods_and_pays_freight_tax(self):
        neighbor=self.w.neighbors[self.n['place']][0];dest=e.market(self.w,neighbor)
        self.m['stock']['grain']=50.;self.m['prices']['grain']=1.3
        dest['stock']['grain']=0.;dest['target']['grain']=20.;dest['prices']['grain']=4.8;dest['cash']=100.
        self.baseline();result=e.haul(self.w,self.n)
        self.assertIn('shipment',result);self.assertGreater(result['gross'],0.)
        self.assertIn(result['shipment']['destination'],self.w.neighbors[result['shipment']['origin']])
        self.balanced()

    def test_heirs_receive_physical_stores_without_creating_goods(self):
        heir=self.w.make_npc(parents=[self.n['id']],place=self.n['place'])
        self.baseline();e.inherit(self.w,self.n,[heir]);self.balanced()
        self.assertEqual(e.prepare(self.n)['stock']['grain'],0.)
        self.assertEqual(e.prepare(heir)['stock']['grain'],.5)

    def test_views_and_ai_scoring_do_not_change_goods_money_events_or_rng(self):
        before=deepcopy(self.w.export());event=self.w.store.next_id
        e.view(self.w,self.n['place'],self.n['id']);e.expected_income(self.w,self.n);e.work_adjustment(self.w,self.n,'work')
        self.assertEqual(before,self.w.export());self.assertEqual(event,self.w.store.next_id)

    def test_player_orders_reject_stale_remote_or_invalid_requests(self):
        h=self.w.player['protagonist'];h.update(created=True,place=self.n['place'],points=30)
        body=dict(operation='buy',good='grain',amount=1,month=self.w.month,revision=h['revision'])
        before=deepcopy(self.w.export())
        for value in (0,-1,math.nan,math.inf,True,21):
            with self.assertRaises(ValueError):e.perform(self.w,{**body,'amount':value})
        with self.assertRaises(ValueError):e.perform(self.w,{**body,'revision':-1})
        with self.assertRaises(ValueError):e.perform(self.w,{**body,'place':self.w.neighbors[self.n['place']][0]})
        self.assertEqual(before,self.w.export())
        e.perform(self.w,body)
        self.assertEqual(h['points'],29);self.assertEqual(e.prepare(self.w.player)['stock']['grain'],1.)
        with self.assertRaises(ValueError):e.perform(self.w,body)
        self.balanced()

    def test_save_roundtrip_retains_all_economic_balances(self):
        e.work(self.w,self.n);e.close_month(self.w)
        before=deepcopy(self.w.export());store=Store();store.save(before)
        loaded=World(store=store,durable=False,market_economy=True);self.addCleanup(store.close)
        self.assertEqual(before,loaded.export());self.balanced()

    def test_packed_inventory_goods_consume_real_materials_and_processing_fee(self):
        self.w.month=(-self.n['id'])%6
        event=self.w.emit('work',[self.n['id']],'备料',.2)
        before=len(self.n['inventory']);self.baseline()
        items.produce(self.w,self.n,event)
        self.assertEqual(len(self.n['inventory']),before+1)
        self.assertEqual(e.state(self.w)['consumed']['grain'],.5)
        self.balanced()

    def test_packed_goods_cannot_be_created_without_materials(self):
        self.w.month=(-self.n['id'])%6
        self.m['stock']['grain']=0.;e.prepare(self.n)['stock']['grain']=0.
        event=self.w.emit('work',[self.n['id']],'停工',.2)
        before=len(self.n['inventory']);self.baseline()
        items.produce(self.w,self.n,event)
        self.assertEqual(len(self.n['inventory']),before);self.balanced()


if __name__=='__main__':unittest.main()
