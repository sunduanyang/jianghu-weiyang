from jianghu import combat_attributes as abilities
from tests.physical_helpers import frame
from copy import deepcopy
from http.server import ThreadingHTTPServer
import json
import random
from pathlib import Path
import tempfile
import threading
import unittest
import urllib.error
import urllib.request

from jianghu import physical_combat as combat, martial
from jianghu.engine import World
from jianghu.server import Game, handler_for
from jianghu.store import Store


def actor(name='甲', key=1, skill=30):
    n = {'id': key, 'name': name, 'skill': skill, 'health': 100.}
    martial.initialize(n)
    return n


class MartialTests(unittest.TestCase):
    def world(self):
        w = World(42, 30, durable=False)
        self.addCleanup(w.store.close)
        return w

    def adult(self, w):
        return next(n for n in w.npcs.values() if n['age_months'] >= 216)

    def test_legacy_cultivation_is_cleared_without_ability_conversion(self):
        a = actor()
        a['skill']=99; a['martial']['arts']={'breath':{'realm':3,'practice':100}}
        martial.initialize(a)
        self.assertNotIn('skill',a)
        self.assertEqual(a['martial']['arts'],{})
        self.assertEqual(abilities.base_ratings(a),dict.fromkeys(abilities.KEYS,abilities.DEFAULT_ATTRIBUTE))

    def test_failed_learning_and_practice_do_not_spend_money_time_or_rng(self):
        w = self.world()
        for action, art in [('learn', 'cold_spring'), ('practice', 'no_such_art'), ('breakthrough', 'breath')]:
            before = deepcopy(w.export())
            with self.assertRaises(ValueError):
                w.martial_action(action, art)
            self.assertEqual(w.export(), before)
        w.player['wealth'] = 0
        before = deepcopy(w.export())
        with self.assertRaises(ValueError):
            w.martial_action('practice', 'breath')
        self.assertEqual(w.export(), before)

    def test_rest_still_advances_the_real_world(self):
        w=self.world(); before=w.month
        w.martial_action('rest')
        self.assertEqual(w.month,before+1)
        self.assertEqual(w.store.history(limit=1)[0]['kind'],'player_rest')

    def test_retired_loadout_cannot_be_recreated(self):
        a=actor(); before=deepcopy(a)
        with self.assertRaises(ValueError):martial.equip(a,'bridge_fist')
        self.assertEqual(a,before)

    def test_retired_transmission_cannot_mutate_people(self):
        w=self.world(); teacher,pupil=w.npcs[1],w.npcs[2]
        before=deepcopy(w.export())
        self.assertIsNone(w.execute(teacher,{'id':f'teach:{pupil["id"]}'}))
        self.assertEqual(w.export(),before)

    def test_old_save_clears_martial_data_without_rng_or_history_changes(self):
        w=self.world(); old=deepcopy(w.export()); count=w.store.count()
        for a in [old['player'],*old['npcs'].values()]:
            a['skill']=99;a['martial']={'stamina':80,'arts':{'breath':{'realm':3,'practice':100}}}
        w.restore(old)
        self.assertTrue(all('skill' not in a and not a['martial']['arts'] for a in [w.player,*w.npcs.values()]))
        self.assertEqual(w.rng.getstate(),old['random_state'])
        self.assertEqual(w.store.count(),count)

    def test_active_battle_reload_is_identical_and_stale_inputs_are_atomic(self):
        a = self.world()
        target = self.adult(a)
        a.start_battle(target['id'])
        a.battle_action(a.battle['id'], 1, 'checkpoint', frame(a.battle, .2))
        a.checkpoint()
        copied = Store()
        a.store.db.backup(copied.db)
        self.addCleanup(copied.close)
        b = World(store=copied, durable=False)
        before = deepcopy(b.export())
        with self.assertRaises(ValueError):
            b.battle_action(b.battle['id'], 0, 'checkpoint', frame(b.battle, .2))
        with self.assertRaises(ValueError):
            b.month_step()
        with self.assertRaises(ValueError):
            b.martial_action('rest')
        self.assertEqual(b.export(), before)
        a.battle_action(a.battle['id'], 2, 'surrender')
        b.battle_action(b.battle['id'], 2, 'surrender')
        self.assertEqual(a.export(), b.export())
        self.assertEqual(a.store.history(limit=1), b.store.history(limit=1))
        self.assertTrue(b.battle['settled'])
        self.assertIn('combat', b.store.history(limit=1)[0]['facts'])

    def test_surrender_does_not_farm_skill_and_result_settles_once(self):
        w = self.world()
        target = self.adult(w)
        w.start_battle(target['id'])
        skill = abilities.ratings(w.player)
        w.battle_action(w.battle['id'], 1, 'surrender')
        self.assertEqual(abilities.ratings(w.player), skill)
        before = deepcopy(w.export())
        with self.assertRaises(ValueError):
            w.battle_action(w.battle['id'], 0, 'surrender')
        self.assertEqual(w.export(), before)
        self.assertGreater(w.player['health'], 0)
        self.assertTrue(target['alive'])

    def test_server_restart_resumes_first_month_battle_without_prehistory(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder)/'resume.sqlite'
            w = World(42, 30, Store(path))
            target = self.adult(w)
            w.start_battle(target['id'])
            expected = deepcopy(w.battle)
            w.store.close()
            game = Game(path, prehistory=18,model_config={'enabled':False})
            try:
                self.assertEqual(game.job['remaining'], 0)
                self.assertEqual(game.world.battle, expected)
                game.world.battle_action(expected['id'], 1, 'surrender')
                self.assertTrue(game.world.battle['settled'])
            finally:
                game.closed = game.ai.closed = True
                game.thread.join(timeout=2)
                game.world.store.close()

    def test_npc_grievance_uses_same_battle_engine_and_records_wounds(self):
        w = self.world()
        a, b = w.npcs[1], w.npcs[2]
        for n in (a, b):
            n['age_months'] = 300
            n['health'] = 100
        b['place'] = a['place']
        w.link(a, b, -60, -60)
        w.reindex()
        e = w.execute(a, {'id': 'duel:2'})
        self.assertIsNotNone(e)
        bout = e['facts']['combat']
        self.assertEqual(bout['status'], 'finished')
        self.assertGreater(len(bout['log']), 1)
        self.assertTrue(a['wound'] or b['wound'])
        self.assertTrue(any('external' in n['wound'] for n in (a,b) if n['wound']))


class MartialHTTPTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.game = Game(':memory:', population=30, prehistory=0,model_config={'enabled':False})
        cls.server = ThreadingHTTPServer(('127.0.0.1', 0), handler_for(cls.game))
        cls.thread = threading.Thread(target=cls.server.serve_forever, daemon=True)
        cls.thread.start()
        cls.base = f'http://127.0.0.1:{cls.server.server_port}'

    @classmethod
    def tearDownClass(cls):
        cls.server.shutdown()
        cls.server.server_close()
        cls.thread.join(timeout=2)
        cls.game.closed = cls.game.ai.closed = True
        cls.game.thread.join(timeout=2)
        cls.game.world.store.close()

    def request(self, path, body=None):
        req = urllib.request.Request(self.base+path,
              json.dumps(body).encode() if body is not None else None,
              {'Content-Type': 'application/json'})
        with urllib.request.urlopen(req) as response:
            return json.load(response)

    def test_http_practice_battle_and_resume_routes(self):
        with self.assertRaises(urllib.error.HTTPError):
            self.request('/api/martial', {'action':'practice', 'art':'breath'})
        self.request('/api/martial', {'action':'rest'})
        self.assertEqual(self.request('/api/state')['month'], 1)
        target = next(n for n in self.game.world.npcs.values() if n['age_months'] >= 216)
        state = self.request('/api/intervene', {'action':'challenge', 'npc_id':target['id']})
        b = state['battle']
        self.assertTrue(self.request('/api/state')['battle_active'])
        for path, payload in [('/api/control', {'months':1}), ('/api/martial', {'action':'rest'}),
                              ('/api/new', {'population':100, 'prehistory':0})]:
            with self.assertRaises(urllib.error.HTTPError) as err:
                self.request(path, payload)
            self.assertEqual(err.exception.code, 400)
        result = self.request('/api/battle', {'battle_id':b['id'], 'revision':b['revision'], 'action':'surrender'})
        self.assertEqual(result['battle']['status'], 'finished')
        self.assertFalse(self.request('/api/state')['battle_active'])
        self.assertEqual(self.request('/api/martial')['battle']['id'], b['id'])

    def test_web_combat_scripts_and_styles_are_unavailable(self):
        for path in ('/combat.js','/combat.css'):
            with self.subTest(path=path), self.assertRaises(urllib.error.HTTPError) as error:
                urllib.request.urlopen(self.base+path)
            self.assertEqual(error.exception.code,404)
