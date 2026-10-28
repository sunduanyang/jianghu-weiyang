from copy import deepcopy
import unittest
from unittest.mock import patch
from pathlib import Path
import tempfile

from jianghu import city_maps, protagonist
from jianghu.engine import World
from jianghu.store import Store


class WorldTravelTests(unittest.TestCase):
    def setUp(self):
        self.w = World(population=0, durable=False)
        self.addCleanup(self.w.store.close)
        self.act('create', name='行旅测试')
        self.origin = self.w.player['protagonist']['place']

    def act(self, action, **extra):
        return protagonist.perform(self.w, dict(action=action, month=self.w.month,
                                               revision=self.w.player['protagonist']['revision'], **extra))

    def unchanged(self, callback, error=None):
        state, events = deepcopy(self.w.export()), self.w.store.count()
        if error:
            with self.assertRaisesRegex(ValueError, error):
                callback()
        else:
            callback()
        self.assertEqual(self.w.export(), state)
        self.assertEqual(self.w.store.count(), events)

    def graph(self):
        a, b, c = [p['id'] for p in self.w.active_places if p['id'] != self.origin][:3]
        # A costly direct sea crossing competes with two shorter land legs.
        self.routes = [dict(a=self.origin, b=a, km=2400, sea=True),
                       dict(a=self.origin, b=b, km=120, sea=False),
                       dict(a=b, b=a, km=120, sea=False)]
        override = patch.object(World, 'routes', property(lambda w: self.routes))
        override.start()
        self.addCleanup(override.stop)
        return a, b, c

    def test_quote_is_read_only_and_adjacent_cost_matches_existing_rules(self):
        connection = protagonist.connections(self.w)[0]
        self.unchanged(lambda: protagonist.travel_plan(self.w, connection['place']))
        quote = protagonist.travel_plan(self.w, connection['place'])
        self.assertEqual((quote['points'], quote['silver']), (connection['points'], connection['silver']))
        self.assertTrue(quote['enabled'])

    def test_lowest_energy_route_moves_each_leg_without_charging_silver(self):
        target, via, _ = self.graph()
        quote = protagonist.travel_plan(self.w, target)
        self.assertEqual(quote['path'], [self.origin, via, target])
        self.assertEqual((quote['points'], quote['silver']), (18, 0))
        self.assertTrue(quote['can_finish'])
        self.assertEqual((quote['travel_points'], quote['travel_place']), (18, target))
        wealth = self.w.player['wealth']
        hero = self.act('travel_route', place=target)
        gate = next(n for n in city_maps._world_layout(self.w, target)['nodes'] if n['id'] == 'gate')
        self.assertEqual((hero['place'], hero['row'], hero['col'], hero['points']),
                         (target, gate['row'], gate['col'], 12))
        self.assertAlmostEqual(self.w.player['wealth'], wealth-quote['silver'])
        self.assertEqual(self.w.month, 0)
        self.assertEqual([event['facts']['path'] for event in hero['journal'][:2]], [[via, target], [self.origin, via]])
        stale = dict(action='travel_route', place=target, month=quote['month'], revision=quote['revision'])
        self.unchanged(lambda: protagonist.perform(self.w, stale), '已变化')

    def test_partial_route_stops_before_unaffordable_edge_and_waits_for_manual_month(self):
        target, via, _ = self.graph()
        self.w.player['protagonist']['points'] = 17
        self.w.player['wealth'] = 0
        quote = protagonist.travel_plan(self.w, target)
        self.assertTrue(quote['enabled'])
        self.assertFalse(quote['can_finish'])
        self.assertEqual((quote['travel_place'], quote['travel_points']), (via, 9))
        before_events = self.w.store.count()
        hero = self.act('travel_route', place=target)
        self.assertEqual((hero['place'], hero['points'], self.w.month, self.w.player['wealth']), (via, 8, 0, 0))
        self.assertEqual(self.w.store.count(), before_events+1)
        plan = protagonist.travel_plan(self.w, target)
        self.assertFalse(plan['enabled'])
        self.assertTrue(plan['needs_month'])
        self.assertEqual(plan['reason'], '精力不足，点击过月')
        self.unchanged(lambda: self.act('travel_route', place=target), '精力不足，点击过月')
        self.act('end_turn')
        wealth = self.w.player['wealth']
        hero = self.act('travel_route', place=target)
        self.assertEqual((hero['place'], hero['points'], self.w.month), (target, 21, 1))
        self.assertEqual(self.w.player['wealth'], wealth)

    def test_energy_boundaries_never_move_half_an_edge_or_spend_unaffordable_points(self):
        target, via, _ = self.graph()
        for points, destination, remaining in [(0, self.origin, 0), (8, self.origin, 8),
                                                (9, via, 0), (17, via, 8), (18, target, 0)]:
            with self.subTest(points=points):
                self.w.player['protagonist'].update(place=self.origin, points=points)
                plan = protagonist.travel_plan(self.w, target)
                if points < 9:
                    self.assertFalse(plan['enabled'])
                    self.unchanged(lambda: self.act('travel_route', place=target), '精力不足')
                else:
                    self.assertTrue(plan['enabled'])
                    hero = self.act('travel_route', place=target)
                    self.assertEqual((hero['place'], hero['points']), (destination, remaining))
                self.assertEqual(self.w.month, 0)

    def test_zero_silver_allows_land_sea_and_multi_leg_travel(self):
        target, _, _ = self.graph()
        for action in ('travel', 'travel_route'):
            for sea in (False, True):
                with self.subTest(action=action, sea=sea):
                    self.routes = [dict(a=self.origin, b=target, km=120, sea=sea)]
                    self.w.player['protagonist'].update(place=self.origin, points=30)
                    self.w.player['wealth'] = 0
                    countries = deepcopy(self.w.countries)
                    self.assertTrue(protagonist.travel_plan(self.w, target)['enabled'])
                    self.assertTrue(protagonist.connections(self.w)[0]['enabled'])
                    self.act(action, place=target)
                    self.assertEqual(self.w.player['wealth'], 0)
                    self.assertEqual(self.w.countries, countries)

    def test_partial_progress_and_energy_survive_restart(self):
        target, via, _ = self.graph()
        self.w.player['protagonist']['points'] = 9
        with tempfile.TemporaryDirectory() as folder:
            store = Store(Path(folder)/'partial.sqlite')
            saved = World(store=store, load=False, population=0)
            try:
                saved.restore(self.w.export())
                hero = protagonist.perform(saved, dict(action='travel_route', place=target, month=saved.month,
                                                      revision=saved.player['protagonist']['revision']))
                restored = World(store=Store(store.path))
                try:
                    self.assertEqual((restored.player['protagonist']['place'], restored.player['protagonist']['points']), (via, 0))
                    self.assertEqual(protagonist.view(restored)['journal'], hero['journal'])
                finally:
                    restored.store.close()
            finally:
                store.close()

    def test_invalid_current_and_disconnected_destinations_are_safe(self):
        target, _, disconnected = self.graph()
        for place, reason in [(self.origin, '已在'), (disconnected, '没有通往')]:
            self.assertFalse(protagonist.travel_plan(self.w, place)['enabled'])
            self.unchanged(lambda: self.act('travel_route', place=place), reason)
        for place in (-1, True, 1.5, 10000):
            self.unchanged(lambda: protagonist.travel_plan(self.w, place), '有效区域|整数')

    def test_travel_from_roads_and_facilities_uses_only_route_costs(self):
        target, via, _ = self.graph()
        city = city_maps._world_layout(self.w, self.origin)
        exits = {(n['row'], n['col']) for n in city['nodes'] if n['kind'] in ('gate', 'dock')}
        nodes = {(n['row'], n['col']) for n in city['nodes']}
        road = next((r, c) for r, line in enumerate(city['rows']) for c, tile in enumerate(line)
                    if tile not in '~^#' and (r, c) not in nodes)
        facility = next(n for n in city['nodes'] if (n['row'], n['col']) not in exits)
        positions = [road, (facility['row'], facility['col'])]
        for position in positions:
            self.assertNotIn(position, exits)
            for action in ('travel', 'travel_route'):
                for sea in (False, True):
                    with self.subTest(position=position, action=action, sea=sea):
                        # Exercise both a direct connection and a multi-leg quote from inside town.
                        self.routes = ([dict(a=self.origin, b=target, km=120, sea=sea)] if action == 'travel'
                                       else [dict(a=self.origin, b=via, km=120, sea=sea),
                                             dict(a=via, b=target, km=120, sea=False)])
                        h = self.w.player['protagonist']
                        h.update(place=self.origin, row=position[0], col=position[1], points=30)
                        self.w.player['wealth'] = 100
                        self.unchanged(lambda: protagonist.travel_plan(self.w, target))
                        quote = protagonist.travel_plan(self.w, target)
                        self.assertTrue(quote['enabled'], quote['reason'])
                        self.assertEqual(quote['reason'], '')
                        self.assertTrue(protagonist.connections(self.w)[0]['enabled'])
                        hero = self.act(action, place=target)
                        gate = next(n for n in city_maps._world_layout(self.w, target)['nodes'] if n['id'] == 'gate')
                        self.assertEqual((hero['place'], hero['row'], hero['col'], hero['points']),
                                         (target, gate['row'], gate['col'], 30-quote['points']))
                        self.assertAlmostEqual(self.w.player['wealth'], 100-quote['silver'])
                        self.assertEqual(self.w.month, 0)

    def test_sea_surcharge_is_included_in_preview_and_deduction(self):
        target, _, _ = self.graph()
        self.routes = [dict(a=self.origin, b=target, km=120, sea=True)]
        quote = protagonist.travel_plan(self.w, target)
        self.assertEqual((quote['points'], quote['silver']), (13, 0))
        self.assertTrue(quote['legs'][0]['sea'])
        self.assertEqual(self.act('travel_route', place=target)['points'], 17)


if __name__ == '__main__':
    unittest.main()
