"""Approved roster, additive map seats, save identity and player transactions."""
from copy import deepcopy
import json
import unittest

from jianghu import city_maps, organizations, protagonist
from jianghu.atlas import COUNTRIES, FACTIONS
from jianghu.engine import World
from jianghu.settlements import HUBS


class SectTests(unittest.TestCase):
    def world(self):
        w = World(seed=42, population=0, compact=True, durable=False)
        self.addCleanup(w.store.close)
        return w

    def test_approved_roster_covers_every_country_with_distinct_traditions(self):
        self.assertEqual([f['name'] for f in FACTIONS], ['武当派', '丐帮', '少林寺', '全真教', '一品堂',
            '天山派', '大轮寺', '天龙寺', '柏孜克里克寺', '克烈武帐', '都房', '鞍马寺', '海东祖灵社', '黑水萨满坛'])
        self.assertEqual({f['country'] for f in FACTIONS}, {c['id'] for c in COUNTRIES})
        self.assertEqual(len({f['key'] for f in FACTIONS}), 14)
        self.assertEqual(len({f['art_id'] for f in FACTIONS}), 14)
        self.assertEqual(FACTIONS[12]['ranks'][0], '主祭')
        self.assertEqual(FACTIONS[13]['ranks'][0], '大萨满')

    def test_empty_regions_keep_two_ordinary_settlements_and_independent_local_seats(self):
        w = self.world(); rng = w.rng.getstate(); events = w.store.count()
        for hub in HUBS:
            city = city_maps.view(w, hub)
            local = [f for f in w.factions if f['place'] == hub]
            ordinary = [s for s in city['settlements'] if s['kind'] != 'sect']
            self.assertEqual(sorted(s['footprint']['size'] for s in ordinary), [1, 3])
            self.assertEqual(len(city['settlements']), 2+len(local))
            occupied = set()
            for site in city['settlements']:
                lot = site['footprint']
                cells = {(lot['row']+r, lot['col']+c) for r in range(lot['size']) for c in range(lot['size'])}
                self.assertFalse(cells & occupied); occupied.update(cells)
                self.assertTrue(site['path'])
                if site['kind'] == 'sect':
                    self.assertEqual(len(cells), 9)
                    self.assertIn(site['faction'], [f['id'] for f in local])
                    node = next(n for n in city['nodes'] if n.get('faction') == site['faction'])
                    self.assertEqual(node['footprint'], lot)
                    self.assertEqual(node['settlement_id'], site['id'])
        self.assertEqual(w.rng.getstate(), rng)
        self.assertEqual(w.store.count(), events)

    def test_legacy_ids_are_remapped_by_name_removed_members_exit_and_money_survives(self):
        w = self.world()
        a = w.make_npc(place=6); b = w.make_npc(place=2)
        for n, fid in ((a, 3), (b, 0)):
            n.update(faction=fid, contribution=17., faction_since=0)
        state = json.loads(json.dumps(w.export()))
        state['factions'] = [dict(id=0, name='栖云门', place=1, country=0, leader=b['id'], fund=83.),
                             dict(id=3, name='武当派', place=7, country=0, leader=a['id'], fund=217.)]
        state.pop('sect_version')
        state['city_positions'] = {str(a['id']): dict(place=6, node='faction_3', event_id=None, month=0),
                                  str(b['id']): dict(place=2, node='faction_0', event_id=None, month=0)}
        before = sum(g['fund'] for g in state['governments'])+300
        count = w.store.count(); rng = w.rng.getstate()
        w.restore(state)
        self.assertEqual(w.npcs[a['id']]['faction'], 0)
        self.assertEqual(w.factions[0]['fund'], 217.)
        self.assertEqual(w.factions[0]['leader'], a['id'])
        self.assertIsNone(w.npcs[b['id']]['faction'])
        self.assertEqual(w.npcs[b['id']]['contribution'], 0)
        self.assertEqual(w.npcs[b['id']]['membership_history'][-1]['name'], '栖云门')
        self.assertEqual(w.city_positions[str(a['id'])]['node'], 'faction_0')
        self.assertEqual(w.city_positions[str(b['id'])]['node'], 'residence')
        self.assertAlmostEqual(sum(f['fund'] for f in w.factions)+sum(g['fund'] for g in w.governments), before)
        for f in w.factions:
            self.assertAlmostEqual(f['fund'], f['opening_fund']+f['revenue']-f['expenses'])
        self.assertEqual(w.rng.getstate(), rng); self.assertEqual(w.store.count(), count)
        w.checkpoint(); again = World(store=w.store, compact=True, durable=False)
        self.assertIsNone(again._sect_migration)
        self.assertEqual(again.factions, w.factions)
        self.assertEqual(len(again.sect_history), 1)

    def hero_action(self, w, action, faction=0):
        h = w.player['protagonist']
        return protagonist.perform(w, dict(action=action, faction=faction, revision=h['revision'], month=w.month))

    def test_player_must_enter_seat_then_can_join_serve_learn_and_leave(self):
        w = self.world(); h = w.player['protagonist']; f = w.factions[0]
        h.update(created=True, started_month=0)
        before = deepcopy(w.export())
        with self.assertRaisesRegex(ValueError, '走入'):
            self.hero_action(w, 'sect_join')
        self.assertEqual(w.export(), before)
        city = city_maps.world_layout(w, f['place']); seat = next(s for s in city['settlements'] if s['kind'] == 'sect')
        h.update(place=f['place'], row=seat['row'], col=seat['col'])
        self.hero_action(w, 'sect_join')
        self.assertEqual(h['faction'], 0)
        for _ in range(3):
            w.month += 1; h['points'] = 30
            total = w.player['wealth']+f['fund']
            self.hero_action(w, 'sect_task')
            self.assertEqual(w.player['wealth']+f['fund'], total)
        before = deepcopy(w.export())
        with self.assertRaisesRegex(ValueError, '本月已'):
            self.hero_action(w, 'sect_task')
        self.assertEqual(w.export(), before)
        total = w.player['wealth']+f['fund']
        self.hero_action(w, 'sect_study')
        self.assertIn(f['art_id'], w.player['martial']['arts'])
        self.assertEqual(w.player['wealth']+f['fund'], total)
        self.assertAlmostEqual(f['fund'], f['opening_fund']+f['revenue']-f['expenses'])
        self.hero_action(w, 'sect_leave')
        self.assertIsNone(h['faction'])
        self.assertEqual(h['membership_history'][-1]['name'], '武当派')
        with self.assertRaisesRegex(ValueError, '一年后'):
            self.hero_action(w, 'sect_join')
        w.checkpoint(); again = World(store=w.store, compact=True, durable=False)
        self.assertEqual(again.player, w.player)

    def test_religious_duties_improve_health_and_keep_local_titles(self):
        w = self.world()
        for fid in (12, 13):
            f = w.factions[fid]; n = w.make_npc(place=f['place'])
            n.update(age_months=360, health=100., skill=30., faction=fid)
            w.set_goal(n, 'belonging', initial=True); w.reindex()
            before = w.governments[f['country']]['sanitation']
            e = w.execute(n, dict(id=f'faction_task:{fid}'))
            self.assertEqual(e['facts']['public_axis'], 'sanitation')
            self.assertGreater(w.governments[f['country']]['sanitation'], before)
            f['leader'] = n['id']
            self.assertEqual(organizations.rank(w, n)['title'], f['ranks'][0])

    def test_school_transmission_cannot_be_bought_through_public_catalog(self):
        w = self.world(); before = deepcopy(w.export())
        self.assertFalse(any(a.get('school') for a in w.martial_view()['catalog']))
        with self.assertRaisesRegex(ValueError, '驻地'):
            w.martial_action('learn', w.factions[0]['art_id'])
        self.assertEqual(w.export(), before)


if __name__ == '__main__':
    unittest.main()
