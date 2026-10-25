from copy import deepcopy
import json
from pathlib import Path
import unittest
from unittest.mock import patch

from jianghu import social, government, politics
from jianghu.atlas import COUNTRIES, FACTIONS, NEIGHBORS, PLACES, ROUTES, distance
from jianghu.engine import World
from jianghu.historical_map import TERRITORIES, ERA


class AtlasTests(unittest.TestCase):
    def world(self):
        w = World(population=0, durable=False)
        self.addCleanup(w.store.close)
        return w

    def adult(self, w, place):
        n = w.make_npc(place=place)
        n.update(age_months=360, wealth=1000., health=100., skill=40., office=None)
        w.set_goal(n, w.best_goal(n), None, initial=True)
        w.reindex()
        return n

    def test_historical_territories_and_remote_sites_are_reachable(self):
        data = json.loads((Path(__file__).resolve().parents[1]/'static/geography.json').read_text(encoding='utf-8'))
        self.assertNotIn('provinces', data)
        self.assertEqual({p['country'] for p in data['territories']}, {c['id'] for c in COUNTRIES})
        self.assertEqual(data['era']['year'], 1200)
        self.assertEqual([p['id'] for p in PLACES], list(range(len(PLACES))))
        self.assertTrue({'疏勒','逻些','黑水营地','海东聚落','琼州','官富场','香山海澳'} <= {p['name'] for p in PLACES})
        reached, pending = {0}, [0]
        while pending:
            for neighbor in NEIGHBORS[pending.pop()]:
                if neighbor not in reached:
                    reached.add(neighbor)
                    pending.append(neighbor)
        self.assertEqual(reached, set(range(len(PLACES))))
        self.assertGreater(distance(31, 22), 3500)

    def test_travel_requires_connected_affordable_route_and_current_border_cost(self):
        w = self.world()
        r = next(r for r in ROUTES if PLACES[r['a']]['country'] != PLACES[r['b']]['country'])
        n = self.adult(w, r['a'])
        old = next(a for a in w.legal_actions(n) if a['id'] == f"travel:{r['b']}")
        ca, cb = PLACES[r['a']]['country'], PLACES[r['b']]['country']
        w.countries[ca]['relations'][str(cb)] = w.countries[cb]['relations'][str(ca)] = '交恶'
        current = politics.journey(w, r['a'], r['b'])
        before, fund = n['wealth'], w.governments[cb]['fund']
        event = w.execute(n, old)
        self.assertEqual(n['place'], r['b'])
        self.assertAlmostEqual(before-n['wealth'], current['cost'])
        self.assertAlmostEqual(w.governments[cb]['fund']-fund, 10)
        self.assertEqual(event['facts']['border'], '交恶')
        self.assertEqual(event['facts']['origin'], r['a'])
        self.assertEqual(event['facts']['destination'], r['b'])
        self.assertIn(r['b'], n['visited_places'])
        distant = next(i for i in range(len(PLACES)) if i != n['place'] and i not in NEIGHBORS[n['place']])
        self.assertIsNone(w.execute(n, {'id': f'travel:{distant}'}))
        n['wealth'] = 0
        self.assertFalse(any(a['kind']=='travel' for a in w.legal_actions(n)))

    def test_country_tax_relief_and_border_accounts_balance_independently(self):
        w = self.world()
        n = self.adult(w, 31)
        g = w.local_government(n)
        others = [deepcopy(x) for x in w.governments if x is not g]
        total = n['wealth'] + g['fund']
        government.tax(w, n, 100)
        government.grant(w, n, 200, '救济')
        self.assertAlmostEqual(n['wealth']+g['fund'], total)
        self.assertAlmostEqual(g['fund'], g['opening_fund']+g['revenue']-g['expenses'])
        self.assertEqual(others, [x for x in w.governments if x is not g])

    def test_joining_requires_local_presence_and_training_uses_home_school(self):
        w = self.world()
        n = self.adult(w, 7)
        self.assertIsNone(w.execute(n, {'id': 'join:4'}))  # Shaolin is elsewhere.
        self.assertIsNotNone(w.execute(n, {'id': 'join:3'}))
        event = w.execute(n, {'id': 'train:self'})
        self.assertEqual(event['facts']['training_multiplier'], 1.25)
        w.residents[7].remove(n['id']); n['place'] = 6; w.reindex()
        event = w.execute(n, {'id': 'train:self'})
        self.assertEqual(event['facts']['training_multiplier'], 1)

    def test_trade_guild_bonus_only_applies_at_its_home(self):
        w = self.world()
        n = self.adult(w, 2)
        w.execute(n, {'id': 'join:1'})
        event = w.execute(n, {'id': 'work:self'})
        self.assertEqual(event['facts']['guild_multiplier'], 1.12)
        w.month += 1; social.reset_month(w, n)
        n['place'] = 31; w.reindex()
        event = w.execute(n, {'id': 'work:self'})
        self.assertEqual(event['facts']['guild_multiplier'], 1)
        self.assertEqual(event['facts']['tax_country'], 4)

    def test_period_ownership_names_and_settlements_match_the_drawn_ranges(self):
        places = {p['name']: p for p in PLACES}
        names = {p['name'] for p in PLACES}
        self.assertFalse({'天津卫','盛京','哈尔滨','归绥','迪化','台湾府','贵阳'} & names)
        for name in ('临安府','成都府','广州','琼州'):
            self.assertEqual(COUNTRIES[places[name]['country']]['name'], '南宋')
        for name in ('中都','京兆府','华山','沈州','丰州'):
            self.assertEqual(COUNTRIES[places[name]['country']]['name'], '金')
        self.assertEqual(places['临安府']['region'], '两浙西路')
        self.assertEqual(places['成都府']['region'], '成都府路')
        self.assertEqual(COUNTRIES[places['海东聚落']['country']]['kind'], '诸部')
        self.assertEqual(COUNTRIES[5]['name'], '吐蕃诸部')

        def inside(point, ring):
            x, y = point
            hit = False
            for a, b in zip(ring, ring[1:]+ring[:1]):
                if (a[1]>y) != (b[1]>y) and x < (b[0]-a[0])*(y-a[1])/(b[1]-a[1])+a[0]:
                    hit = not hit
            return hit
        for p in PLACES:
            shape = next(t for t in TERRITORIES if t['country'] == p['country'])
            self.assertTrue(any(inside((p['lon'],p['lat']), ring) for ring in shape['rings']), p['name'])

    def test_v2_replacement_conserves_all_public_funds_and_reloads_once(self):
        w = self.world()
        a, b = self.adult(w, 2), self.adult(w, 45)
        state = json.loads(json.dumps(w.export()))
        state['map_version'] = 2
        state['countries'] = [{'id':i,'name':f'旧势力{i}','relations':{}} for i in range(11)]
        old = []
        for i in range(11):
            g = government.new(0)
            g.update(country=i, fund=80.+2*i, revenue=5.*i, expenses=3.*i)
            old.append(g)
        for n, country in ((a,0),(b,9)):
            old[country]['offices']['县令'] = n['id']
            state['npcs'][str(n['id'])].update(office='县令',office_country=country)
        state['governments'] = old
        state['government'] = old[0]
        prior = deepcopy(state['npcs'])
        w.store.save(state)
        migrated = World(store=w.store, durable=False)
        self.assertAlmostEqual(sum(g['fund'] for g in old), sum(g['fund'] for g in migrated.governments))
        for field in ('opening_fund','revenue','expenses','relief'):
            self.assertAlmostEqual(sum(g[field] for g in old), sum(g[field] for g in migrated.governments))
        for g in migrated.governments:
            self.assertAlmostEqual(g['fund'], g['opening_fund']+g['revenue']-g['expenses'])
        for i, before in prior.items():
            after = migrated.npcs[int(i)]
            for field in ('wealth','place','memories','relationships','faction','parents','children'):
                self.assertEqual(before[field], after[field], field)
        self.assertEqual(migrated.atlas_history[0]['governments'], old)
        self.assertEqual(migrated.store.history(limit=1)[0]['facts']['new_money'], 0)
        count = migrated.store.count()
        again = World(store=w.store, durable=False)
        self.assertEqual(again.store.count(), count)
        self.assertEqual(again.governments, migrated.governments)

    def test_diplomacy_is_symmetric_and_persists(self):
        w = self.world()
        with patch.object(w.rng, 'random', return_value=0):
            politics.annual(w)
        event = w.store.history(limit=1)[0]
        self.assertEqual(event['kind'], 'diplomacy')
        a, b = event['facts']['countries']
        self.assertEqual(w.countries[a]['relations'][str(b)], w.countries[b]['relations'][str(a)])
        restored = self.world()
        restored.restore(json.loads(json.dumps(w.export())))
        self.assertEqual(w.countries, restored.countries)
        self.assertEqual(w.governments, restored.governments)

    def test_legacy_save_migration_preserves_records_ids_members_and_funds(self):
        w = self.world()
        n = self.adult(w, 2)
        n['faction'] = 1
        w.emit('census', [n['id']], '旧乡籍。', .25)
        state = json.loads(json.dumps(w.export()))
        for k in ('map_version','countries','governments'):
            state.pop(k)
        state['factions'] = [{k: f[k] for k in ('id','name','place','ethos','leader','fund')} for f in state['factions'][:3]]
        state['factions'][1]['fund'] = 347.5
        for record in state['npcs'].values():
            record.pop('visited_places'); record.pop('office_country')
        w.store.save(state)
        count, before = w.store.count(), deepcopy(n)
        migrated = World(store=w.store, durable=False)
        after = migrated.npcs[n['id']]
        for k in ('name','place','wealth','skill','faction','memories','goal'):
            self.assertEqual(before[k], after[k], k)
        self.assertEqual(migrated.factions[1]['fund'], 347.5)
        self.assertEqual([f['name'] for f in migrated.factions[:3]], [f['name'] for f in FACTIONS[:3]])
        self.assertEqual(len(migrated.factions), 25)
        self.assertGreater(migrated.store.count(), count)
        migrated.checkpoint()
        migrated_again = World(store=w.store, durable=False)
        self.assertEqual(migrated_again.store.count(), migrated.store.count())

    def test_cross_country_duplicate_crime_reports_are_rejected(self):
        w = self.world()
        a, b = self.adult(w, 2), self.adult(w, 2)
        e = w.emit('betrayal', [a['id'],b['id']], '已知侵吞。', .95,
                   facts={'victim':a['id'], 'offender':b['id'], 'transfer':10})
        government.report(w, a, e['id'])
        a['place'] = 31; w.reindex()
        with self.assertRaises(ValueError):
            government.report(w, a, e['id'])


if __name__ == '__main__':
    unittest.main()
