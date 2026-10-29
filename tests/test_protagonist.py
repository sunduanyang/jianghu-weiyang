from copy import deepcopy
from http.server import ThreadingHTTPServer
import json
from pathlib import Path
import tempfile
import threading
import unittest
import urllib.error
import urllib.request

from jianghu import city_maps, protagonist
from jianghu.atlas import PLACES, ROUTES
from jianghu.engine import World
from jianghu.server import Game, handler_for
from jianghu.store import Store


class ProtagonistTests(unittest.TestCase):
    def world(self, population=0):
        w = World(population=population, durable=False)
        self.addCleanup(w.store.close)
        return w

    def act(self, w, action, **kwargs):
        return protagonist.perform(w, dict(action=action, month=w.month,
                                           revision=w.player['protagonist']['revision'], **kwargs))

    def create(self, w, **kwargs):
        return self.act(w, 'create', name='沈行舟', **kwargs)

    def rejected(self, w, action, **kwargs):
        before, count = deepcopy(w.export()), w.store.count()
        with self.assertRaises(ValueError):
            self.act(w, action, **kwargs)
        self.assertEqual(w.export(), before)
        self.assertEqual(w.store.count(), count)

    def test_create_uses_existing_player_assets_and_only_happens_once(self):
        w = self.world()
        assets = deepcopy({k: w.player[k] for k in ('wealth', 'inventory', 'martial')})
        h = self.create(w, age=20, sex='女')
        self.assertTrue(h['created'])
        self.assertEqual((h['age_months'], h['sex'], h['points'], h['node']), (240, '女', 30, 'gate'))
        self.assertEqual({k: w.player[k] for k in ('wealth','inventory')}, {k: assets[k] for k in ('wealth','inventory')})
        self.assertEqual({k: w.player['martial'][k] for k in ('stamina','arts')}, {k: assets['martial'][k] for k in ('stamina','arts')})
        self.rejected(w, 'create', name='换一个人')

    def test_invalid_creation_and_uncreated_movement_do_not_mutate(self):
        w = self.world()
        for fields in (dict(name=''), dict(name='a'*13), dict(name='甲', age=15),
                       dict(name='甲', age=18.5), dict(name='甲', place=-1),
                       dict(name='甲', sex='其他'), dict(name='甲', place=True)):
            self.rejected(w, 'create', **fields)
        for action in ('walk', 'travel', 'end_turn'):
            self.rejected(w, action)

    def test_adjacent_travel_costs_and_arrival_gate_without_time_advance(self):
        w = self.world()
        h = self.create(w)
        r = h['connections'][0]
        silver = w.player['wealth']
        result = self.act(w, 'travel', place=r['place'])
        self.assertEqual((w.month, result['place'], result['points'], result['node']),
                         (0, r['place'], 30-r['points'], 'gate'))
        self.assertAlmostEqual(w.player['wealth'], silver-r['silver'])
        self.assertEqual(result['journal'][0]['facts']['origin'], h['place'])

    def test_travel_rejects_non_neighbors_and_insufficient_points_but_needs_no_money(self):
        w = self.world()
        h = self.create(w)
        adjacent = {r['place'] for r in h['connections']}
        self.rejected(w, 'travel', place=next(p['id'] for p in PLACES if p['id'] not in adjacent))
        w.player['protagonist']['points'] = 0
        self.rejected(w, 'travel', place=h['connections'][0]['place'])
        w.player['protagonist']['points'] = 30
        w.player['wealth'] = 0
        self.act(w, 'travel', place=h['connections'][0]['place'])
        self.assertEqual(w.player['wealth'], 0)

    def test_walk_follows_terrain_path_and_travel_can_leave_from_market(self):
        w = self.world()
        h = self.create(w)
        city = city_maps.layout(w.seed, h['place'])
        target = next(n for n in city['nodes'] if n['id'] == 'market')
        path, cost = city_maps.path_between(city['rows'], (h['row'],h['col']), (target['row'],target['col']))
        result = self.act(w, 'walk', place=h['place'], row=target['row'], col=target['col'])
        self.assertEqual((w.month, result['node'], result['points']), (0, target['settlement_id'], 30-cost))
        self.assertEqual(result['journal'][0]['facts']['path'], [list(p) for p in path])
        self.assertTrue(all(city['rows'][r][c] not in '~^#' for r,c in path))
        self.act(w, 'end_turn')
        self.assertEqual((protagonist.view(w)['row'], protagonist.view(w)['col']), (target['row'], target['col']))
        route = protagonist.view(w)['connections'][0]
        self.assertTrue(route['enabled'])
        silver = w.player['wealth']
        result = self.act(w, 'travel', place=route['place'])
        self.assertEqual((w.month, result['place'], result['points'], result['node']),
                         (1, route['place'], 30-route['points'], 'gate'))
        self.assertAlmostEqual(w.player['wealth'], silver-route['silver'])

    def test_walk_rejects_barriers_remote_maps_bounds_and_fractional_coordinates(self):
        w = self.world()
        h = self.create(w)
        city = city_maps.layout(w.seed, h['place'])
        blocked = [(r,c) for r,row in enumerate(city['rows']) for c,t in enumerate(row) if t in '~^#']
        for row,col in blocked:
            self.rejected(w, 'walk', place=h['place'], row=row, col=col)
        for fields in (dict(place=h['place'], row=-1,col=0), dict(place=h['place'],row=0,col=999),
                       dict(place=h['place'],row=1.5,col=2),dict(place=h['connections'][0]['place'],row=1,col=1),
                       dict(place=h['place'],row=h['row'],col=h['col'])):
            self.rejected(w, 'walk', **fields)
        w.player['protagonist']['points'] = 0
        target = next(n for n in city['nodes'] if n['id']=='market')
        self.rejected(w, 'walk', place=h['place'],row=target['row'],col=target['col'])

    def test_turn_advances_world_and_age_once_resets_points_preserves_position(self):
        w = self.world(5)
        h = self.create(w)
        self.act(w, 'travel', place=h['connections'][0]['place'])
        before = deepcopy(w.player['protagonist'])
        payload = dict(action='end_turn', revision=before['revision'], month=w.month)
        result = protagonist.perform(w, payload)
        self.assertEqual((w.month,w.metrics['steps'],result['age_months'],result['points'],result['turn']),
                         (1,1,217,30,2))
        self.assertEqual((result['place'],result['row'],result['col']),
                         (before['place'],before['row'],before['col']))
        state = deepcopy(w.export())
        with self.assertRaisesRegex(ValueError, '已变化'):
            protagonist.perform(w, payload)
        self.assertEqual(w.export(), state)

    def test_existing_world_advance_and_training_also_refresh_month(self):
        w = self.world()
        self.create(w)
        w.player['protagonist']['points'] = 0
        w.martial_action('rest')
        self.assertEqual((protagonist.view(w)['points'],protagonist.view(w)['age_months']), (30,217))
        w.month_step()
        self.assertEqual(protagonist.view(w)['turn'], 3)

    def test_map_browsing_does_not_move_or_spend_and_stale_requests_fail(self):
        w = self.world()
        h = self.create(w)
        before = deepcopy(w.export())
        for p in PLACES:
            city_maps.view(w,p['id'])
        self.assertEqual(w.export(), before)
        self.act(w, 'travel', place=h['connections'][0]['place'])
        state = deepcopy(w.export())
        with self.assertRaises(ValueError):
            protagonist.perform(w, dict(action='travel',place=h['connections'][0]['place'],revision=h['revision'],month=0))
        self.assertEqual(w.export(), state)

    def test_legacy_upgrade_keeps_assets_rng_history_and_does_not_age_during_prehistory(self):
        w = self.world(3)
        w.month_step()
        self.assertEqual(w.player['protagonist']['age_months'],216)
        state = deepcopy(w.export())
        del state['player']['protagonist']
        before, count = deepcopy(state), w.store.count()
        w.restore(state)
        self.assertFalse(protagonist.view(w)['created'])
        exported = deepcopy(w.export())
        del exported['player']['protagonist']
        self.assertEqual(exported, before)
        self.assertEqual(w.store.count(),count)

    def test_durable_save_reload_keeps_location_points_journal_and_revision(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp)/'hero.sqlite'
            w = World(population=0, store=Store(path))
            h = self.create(w)
            self.act(w, 'travel', place=h['connections'][0]['place'])
            before = protagonist.view(w)
            w.store.close()
            restored = World(store=Store(path))
            try:
                self.assertEqual(protagonist.view(restored),before)
            finally:
                restored.store.close()


class WalkingPersistenceTests(unittest.TestCase):
    def test_walk_survives_restart_without_rewriting_world_checkpoint(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp)/'walking.sqlite'
            w = World(population=0, store=Store(path))
            try:
                h = protagonist.perform(w, dict(action='create', name='行路试客', month=w.month,
                                                revision=w.player['protagonist']['revision']))
                city = city_maps._world_layout(w, h['place'])
                target = next((r,c) for r,c in ((h['row']-1,h['col']), (h['row']+1,h['col']),
                                               (h['row'],h['col']-1), (h['row'],h['col']+1))
                              if 0<=r<city['size'] and 0<=c<city['size'] and city['rows'][r][c] not in '~^#')
                checkpoint = w.store.db.execute('SELECT data FROM checkpoint WHERE id=1').fetchone()[0]
                before = protagonist.perform(w, dict(action='walk', month=w.month, revision=h['revision'],
                                                     place=h['place'], row=target[0], col=target[1]))
                self.assertEqual(w.store.db.execute('SELECT data FROM checkpoint WHERE id=1').fetchone()[0], checkpoint)
                restored = World(store=Store(path))
                try:
                    self.assertEqual(protagonist.view(restored), before)
                    self.assertEqual(restored.store.event(before['journal'][0]['id']), before['journal'][0])
                finally:
                    restored.store.close()
            finally:
                w.store.close()

    def test_full_checkpoint_supersedes_player_save_and_ignores_stale_versions(self):
        store = Store()
        try:
            state = dict(month=2, player=dict(protagonist=dict(revision=5, row=3)))
            store.save(state)
            player = dict(protagonist=dict(revision=6, row=4))
            store.save_player(player, 2)
            self.assertEqual(store.load()['player'], player)
            store.save(dict(month=3, player=dict(protagonist=dict(revision=7, row=5))))
            self.assertIsNone(store.db.execute('SELECT data FROM player_checkpoint').fetchone())
            store.save_player(player, 2)
            self.assertEqual(store.load()['player']['protagonist']['row'], 5)
            store.save_player(player, 3)
            self.assertEqual(store.load()['player']['protagonist']['row'], 5)
        finally:
            store.close()


class ProtagonistHTTPTests(unittest.TestCase):
    def test_unity_api_create_walk_turn_and_busy_guards_without_web_pages(self):
        game = Game(':memory:',population=0,prehistory=0,model_config={'enabled':False})
        game.closed=True
        game.thread.join(timeout=2)
        server = ThreadingHTTPServer(('127.0.0.1',0),handler_for(game))
        thread = threading.Thread(target=server.serve_forever,daemon=True)
        thread.start()
        base = f'http://127.0.0.1:{server.server_port}'

        def request(path, body=None):
            req=urllib.request.Request(base+path, None if body is None else json.dumps(body).encode(),
                                       {'Content-Type':'application/json'})
            with urllib.request.urlopen(req) as response:
                return json.load(response)

        def act(action, **fields):
            h=request('/api/hero')
            return request('/api/hero',dict(action=action,revision=h['revision'],month=game.world.month,**fields))

        try:
            for path in ('/','/hero.js','/hero.css','/city.js'):
                with self.assertRaises(urllib.error.HTTPError) as error:
                    urllib.request.urlopen(base+path)
                self.assertEqual(error.exception.code,404)
            self.assertFalse(request('/api/state')['hero']['created'])
            h=act('create',name='测试行者')
            before = deepcopy(game.world.export())
            quote=request('/api/hero/travel?place='+str(h['connections'][0]['place']))
            self.assertTrue(quote['enabled'])
            self.assertEqual(game.world.export(), before)
            with self.assertRaises(urllib.error.HTTPError):
                request('/api/hero/travel?place=-1')
            h=act('travel',place=h['connections'][0]['place'])
            self.assertLess(h['points'],30)
            h=act('end_turn')
            self.assertEqual((request('/api/state')['month'],h['points']),(1,30))
            payload=dict(action='end_turn',revision=h['revision'],month=1)
            game.autoplay=True
            with self.assertRaises(urllib.error.HTTPError) as error:
                request('/api/hero',payload)
            self.assertEqual(error.exception.code,400)
            game.autoplay=False
            game.world.battle={'status':'active'}
            with self.assertRaises(urllib.error.HTTPError):
                request('/api/hero',payload)
            game.world.battle=None
        finally:
            server.shutdown();server.server_close();thread.join(timeout=2)
            game.closed=game.ai.closed=True
            game.thread.join(timeout=2)
            game.world.store.close()
