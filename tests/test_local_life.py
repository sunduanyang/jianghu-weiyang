from collections import Counter
from copy import deepcopy
import unittest

from jianghu import city_maps, local_life, economics, society
from jianghu.atlas import PLACES
from jianghu.engine import World

HOME = next(p['id'] for p in PLACES if p['name'] == '洛阳')


class LocalLifeTests(unittest.TestCase):
    def world(self, count=12, **kwargs):
        w = World(population=count, compact=True, durable=False, **kwargs)
        self.addCleanup(w.store.close)
        for n in w.npcs.values():
            n.update(place=HOME, age_months=300, spouse=None, guardian=None, parents=[], children=[], faction=None, office=None)
        w.reindex(); w.local_life = None; local_life.initialize(w)
        return w

    def test_upgrade_preserves_existing_people_assets_journal_rng_and_locations(self):
        w = World(population=12, compact=True, durable=False); self.addCleanup(w.store.close)
        state = deepcopy(w.export()); state.pop('local_life')
        rng = w.rng.getstate(); count = w.store.count(); w.store.save(state)
        loaded = World(store=w.store, compact=True, durable=False)
        for key in ('npcs', 'economy', 'items', 'player', 'city_positions', 'factions', 'month'):
            self.assertEqual(loaded.export()[key], state[key], key)
        self.assertEqual(loaded.rng.getstate(), rng); self.assertEqual(loaded.store.count(), count)
        self.assertEqual(len(loaded.local_life['people']), 12)
        snapshot = deepcopy(loaded.export()); loaded.checkpoint()
        again = World(store=w.store, compact=True, durable=False)
        self.assertEqual(again.export(), snapshot)

    def test_couples_and_dependent_children_live_together_without_merging_adult_dynasties(self):
        w = self.world(4); a,b,c,d = w.npcs.values()
        a['spouse']=b['id']; b['spouse']=a['id']; c.update(age_months=60, guardian=a['id'], parents=[a['id'],b['id']])
        a['children']=[c['id'],d['id']]; d['parents']=[a['id'],b['id']]
        w.local_life=None; local_life.initialize(w)
        homes=[local_life.entry(w,n)['home']['settlement_id'] for n in (a,b,c)]
        self.assertEqual(len(set(homes)),1)
        other=next(s for s in local_life.places(w,HOME) if s!=homes[0] and not s.startswith('sect_'))
        local_life.entry(w,d)['home']['settlement_id']=other
        self.assertEqual(society.household(w,a),[a['id'],b['id'],c['id']])

    def test_months_career_and_membership_do_not_randomly_move_home(self):
        w=self.world(1); n=w.npcs[1]; before=deepcopy(local_life.entry(w,n)['home'])
        w.month+=1; n['job']='匠人'; n['career']['profession']='匠人'; local_life.refresh(w,n)
        self.assertEqual(local_life.entry(w,n)['home'],before)
        self.assertEqual(local_life.entry(w,n)['work']['node'],'workshop')
        w.month+=1; local_life.reconcile(w); self.assertEqual(local_life.entry(w,n)['home'],before)

    def test_marriage_unites_homes_once_then_keeps_them_stable(self):
        w=self.world(2); a,b=w.npcs.values(); homes=[s for s in local_life.places(w,HOME) if not s.startswith('sect_')]
        local_life.entry(w,a)['home']['settlement_id']=homes[0]; local_life.entry(w,b)['home']['settlement_id']=homes[1]
        a['spouse']=b['id']; b['spouse']=a['id']; local_life.refresh(w,b,123)
        self.assertEqual(local_life.entry(w,a)['home'],local_life.entry(w,b)['home'])
        self.assertEqual(local_life.entry(w,b)['home']['event_id'],123)
        before=deepcopy(w.local_life); local_life.reconcile(w); self.assertEqual(w.local_life,before)

    def test_newborn_follows_guardian_and_departure_rehomes_in_destination(self):
        w=self.world(2); a,b=w.npcs.values(); b.update(age_months=0, guardian=a['id'])
        del w.local_life['people'][str(b['id'])]; local_life.refresh(w,b)
        self.assertEqual(local_life.entry(w,b)['home']['settlement_id'],local_life.entry(w,a)['home']['settlement_id'])
        destination=next(p['id'] for p in w.active_places if p['id']!=HOME)
        a['place']=b['place']=destination; w.reindex(); local_life.reconcile(w)
        self.assertEqual(local_life.entry(w,b)['home']['place'],destination)
        self.assertEqual(local_life.entry(w,b)['home']['settlement_id'],local_life.entry(w,a)['home']['settlement_id'])

    def test_rest_and_work_use_actual_saved_cells_inside_correct_settlements(self):
        w=self.world(3); n=w.npcs[1]; event=dict(id=99,people=[1],facts={})
        for kind,field in (('rest','home'),('work','work')):
            city_maps.record_action(w,n,dict(kind=kind,target=None),event)
            pos=local_life.current(w,n); site=local_life.places(w,HOME)[local_life.entry(w,n)[field]['settlement_id']]
            lot=site['footprint']
            self.assertEqual(pos['settlement_id'],site['id'])
            self.assertIn(pos['row'],range(lot['row'],lot['row']+lot['size']))
            self.assertIn(pos['col'],range(lot['col'],lot['col']+lot['size']))

    def test_real_encounter_colocates_only_the_two_participants_and_records_walk(self):
        w=self.world(3); a,b,c=w.npcs.values(); before=deepcopy(w.city_positions.get(str(c['id'])))
        action=dict(id='befriend:2',kind='befriend',target=2)
        visit=local_life.meeting(w,a,action); event=dict(id=100,people=[1,2],facts=dict(local_visit=visit))
        city_maps.record_action(w,a,action,event)
        self.assertEqual(local_life.current(w,a),local_life.current(w,b))
        self.assertEqual(w.city_positions.get(str(c['id'])),before)
        self.assertGreaterEqual(visit['walk_cost'],0); self.assertTrue(visit['travel_included'])

    def test_busy_partner_is_not_teleported_and_real_meeting_uses_both_time_budgets(self):
        w=self.world(2); a,b=w.npcs.values(); w.link(a,b,20,20)
        b['time_left']=0; before=deepcopy(w.city_positions)
        self.assertIsNone(w.execute(a,dict(id='befriend:2')))
        self.assertEqual(w.city_positions,before)
        b['time_left']=4; event=w.execute(a,dict(id='befriend:2'))
        self.assertIsNotNone(event); self.assertEqual((a['time_left'],b['time_left']),(3,3))
        self.assertIn('local_visit',event['facts']);self.assertEqual(local_life.current(w,a),local_life.current(w,b))

    def test_neighbours_have_priority_while_one_regional_contact_remains(self):
        w=self.world(4); a,b,c,d=w.npcs.values(); sites=list(local_life.places(w,HOME))
        for n in (a,b):local_life.entry(w,n)['home']['settlement_id']=sites[0]
        for n in (c,d):local_life.entry(w,n)['home']['settlement_id']=sites[1]
        options=local_life.strangers(w,a,[3,4,2])
        self.assertEqual(options[0],2); self.assertIn(options[1],[3,4])

    def test_views_separate_census_from_presence_count_each_person_once_and_never_write(self):
        w=self.world(); before=deepcopy(w.export()); rng=w.rng.getstate(); events=w.store.count()
        city=city_maps.view(w,HOME); detail=w.npc_detail(1)
        self.assertTrue(detail['local_life_view']['enabled'])
        self.assertEqual(Counter(p['id'] for t in city['settlements'] for p in t['home_residents']),Counter(w.npcs.keys()))
        self.assertEqual(Counter(p['id'] for t in city['settlements'] for p in t['residents']),Counter(w.npcs.keys()))
        self.assertEqual(w.export(),before); self.assertEqual(w.rng.getstate(),rng); self.assertEqual(w.store.count(),events)

    def test_funded_local_delivery_and_production_are_a_single_goods_and_cash_ledger(self):
        w=self.world(1,market_economy=True); n=w.npcs[1]
        n.update(job='农人',wealth=100); n['career']['profession']='农人'; local_life.refresh(w,n)
        m=economics.market(w,HOME)
        for g in economics.GOODS:m['stock'][g]=0.
        m['cash']=100.; before=n['wealth']+m['cash']; stock_before=economics.prepare(n)['stock']['grain']
        result=economics.work(w,n); freight=result['local_delivery']['paid']
        self.assertGreater(freight,0); self.assertTrue(result['local_delivery']['goods'])
        self.assertAlmostEqual(n['wealth']+m['cash']+result['tax'],before,places=4)
        made=result['produced']['grain']; owned=economics.prepare(n)['stock']['grain']+m['stock']['grain']
        self.assertAlmostEqual(owned,stock_before+made,places=4)
        rows=list(w.local_life['production'].values());self.assertEqual(len(rows),1)
        self.assertEqual(rows[0]['produced'],result['produced']);self.assertEqual(rows[0]['freight'],freight)
        self.assertEqual(result['workplace']['node'],'granary')


if __name__=='__main__':unittest.main()
