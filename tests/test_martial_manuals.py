from copy import deepcopy
from pathlib import Path
import tempfile
import unittest

from jianghu import items, martial, martial_catalog, martial_manuals as manuals, protagonist
from jianghu.engine import World
from jianghu.store import Store


class MartialManualTests(unittest.TestCase):
    def world(self):
        w = World(42, 0, durable=False)
        self.addCleanup(w.store.close)
        protagonist.perform(w, dict(action='create', revision=0, month=0, name='习武人', sex='女', age=24, place=45))
        return w

    def teacher(self, w, school='shaolin'):
        n = w.make_npc(place=45)
        n.update(age_months=360, health=100., time_left=4)
        n['traits'].update(loyalty=80., compassion=80.)
        key = next(k for k in martial_catalog.SCHOOL_ARTS[school] if not items.knows(w, 'player', k))
        n['martial']['learned'] = [key]
        w.set_goal(n, 'mastery', initial=True)
        martial.sync_actor(w, n)
        w.reindex()
        return n, key

    def book(self, w, key, owner='player'):
        return items.create(w, owner, items.MANUAL_TEMPLATES[key], '测试秘籍')

    def test_all_126_current_arts_have_matching_grade_and_description(self):
        self.assertEqual(set(items.MANUAL_TEMPLATES), set(martial_catalog.ARTS))
        for key, template_id in items.MANUAL_TEMPLATES.items():
            d = items.CATALOG[template_id]
            self.assertEqual((d['art'], d['grade']), (key, martial_catalog.ARTS[key]['grade']))
            self.assertIn(martial_catalog.ARTS[key]['name'], d['name'])

    def test_player_use_consumes_one_copy_and_applies_actual_ability_bonuses(self):
        w = self.world(); _, key = self.teacher(w)
        first = self.book(w, key); spare = self.book(w, key)
        before = martial_catalog.bonuses(w.player)
        event = items.perform(w, 'player', 'use', first['id'])
        self.assertNotIn(first['id'], w.items['objects'])
        self.assertIn(spare['id'], w.items['objects'])
        self.assertIn(key, w.player['martial']['learned'])
        self.assertEqual(event['facts']['art'], key)
        after = martial_catalog.bonuses(w.player)
        for axis, value in martial_catalog.ARTS[key]['bonuses'].items():
            self.assertEqual(after[axis]-before[axis], value)
        self.assertNotIn('use', items.operations(w, 'player', spare))
        state = deepcopy(w.export()); events = w.store.count()
        with self.assertRaises(ValueError): items.perform(w, 'player', 'use', spare['id'])
        self.assertEqual(w.export(), state); self.assertEqual(w.store.count(), events)

    def test_learned_arts_survive_rank_changes_sect_changes_and_database_restart(self):
        w = self.world(); _, key = self.teacher(w)
        items.perform(w, 'player', 'use', self.book(w, key)['id'])
        h = w.player['protagonist']; h.update(faction=0, contribution=50)
        martial.sync_actor(w, w.player, True)
        h.update(faction=None, contribution=0)
        martial.sync_actor(w, w.player, True)
        self.assertTrue(items.knows(w, 'player', key))
        w.checkpoint()
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder)/'manuals.sqlite'; store = Store(path)
            w.store.db.backup(store.db); store.close()
            loaded = World(store=Store(path), durable=False)
            try:
                self.assertIn(key, loaded.player['martial']['learned'])
                self.assertTrue(items.knows(loaded, 'player', key))
                self.assertEqual(martial_catalog.bonuses(loaded.player), martial_catalog.bonuses(w.player))
            finally: loaded.store.close()

    def test_player_seeking_transfers_existing_instance_without_learning(self):
        w = self.world(); teacher, key = self.teacher(w)
        book = self.book(w, key, teacher['id']); total = len(w.items['objects'])
        points = w.player['protagonist']['points']; before = teacher['time_left']
        h = w.player['protagonist']
        from jianghu import player_interactions
        player_interactions.perform(w, dict(action='teach', revision=h['revision'], month=w.month, npc_id=teacher['id'], art=key))
        self.assertEqual(book['owner'], 'player')
        self.assertEqual(len(w.items['objects']), total)
        self.assertFalse(items.knows(w, 'player', key))
        self.assertEqual(w.player['protagonist']['points'], points-2)
        self.assertEqual(teacher['time_left'], before)
        event = w.store.event(book['last_event'])
        self.assertTrue(event['facts']['accepted'])
        self.assertEqual(event['facts']['item_id'], book['id'])
        self.assertIn(book['id'], w.player['inventory'])
        self.assertNotIn(book['id'], teacher['inventory'])

    def test_repeated_copies_keep_provenance_and_each_costs_player_ap(self):
        w = self.world(); teacher, key = self.teacher(w)
        event = manuals.request(w, 'player', teacher['id'], key)
        book = items.get(w, 'player', event['facts']['item_id'])
        self.assertIn(teacher['name'], book['origin'])
        self.assertEqual(book['origin_event'], event['id'])
        points=w.player['protagonist']['points'];time=teacher['time_left']
        again=manuals.request(w, 'player', teacher['id'], key)
        self.assertNotEqual(again['facts']['item_id'],book['id'])
        self.assertEqual(w.player['protagonist']['points'],points-2);self.assertEqual(teacher['time_left'],time)

    def test_refusal_costs_player_ap_without_npc_time_or_monthly_cooldown(self):
        w = self.world(); teacher, key = self.teacher(w)
        teacher['traits'].update(loyalty=0., compassion=0.)
        total = len(w.items['objects'])
        e = manuals.request(w, 'player', teacher['id'], key)
        self.assertFalse(e['facts']['accepted'])
        self.assertEqual(len(w.items['objects']), total)
        self.assertFalse(items.knows(w, 'player', key))
        saved = deepcopy(w.export()); w.restore(saved)
        self.assertEqual('',manuals.reason(w, 'player', w.npcs[teacher['id']], key))
        points=w.player['protagonist']['points'];time=w.npcs[teacher['id']]['time_left']
        again=manuals.request(w,'player',teacher['id'],key)
        self.assertFalse(again['facts']['accepted']);self.assertEqual(w.player['protagonist']['points'],points-2)
        self.assertEqual(w.npcs[teacher['id']]['time_left'],time)

    def test_invalid_requests_are_atomic(self):
        w = self.world(); teacher, key = self.teacher(w)
        cases = [dict(art='missing'), dict(art=[]), dict(teacher=9999), dict(place=0), dict(alive=False),
                 dict(points=1)]
        for case in cases:
            baseline = deepcopy(w.export())
            if 'place' in case: teacher['place'] = case['place']
            if 'alive' in case: teacher['alive'] = case['alive']
            if 'time_left' in case: teacher['time_left'] = case['time_left']
            if 'points' in case: w.player['protagonist']['points'] = case['points']
            before = deepcopy(w.export()); count = w.store.count()
            with self.subTest(case=case), self.assertRaises(ValueError):
                manuals.request(w, 'player', case.get('teacher', teacher['id']), case.get('art', key))
            self.assertEqual(w.export(), before); self.assertEqual(w.store.count(), count)
            w.restore(baseline); teacher = w.npcs[teacher['id']]

    def test_npc_seeks_then_autonomously_uses_the_received_manual(self):
        w = self.world(); teacher, key = self.teacher(w)
        pupil = w.make_npc(place=45); pupil.update(age_months=300, health=100., wealth=80.)
        w.set_goal(pupil, 'mastery', initial=True)
        w.link(pupil, teacher, 30, 30); w.reindex()
        choices = manuals.candidates(w, pupil, teacher)
        self.assertEqual(len(choices), 1)
        art = choices[0]['art']
        e = w.execute(pupil, choices[0]); self.assertIsNotNone(e)
        self.assertTrue(e['facts']['accepted'])
        book = items.get(w, pupil['id'], e['facts']['item_id'])
        self.assertFalse(items.knows(w, pupil['id'], art))
        use = next(a for a in w.legal_actions(pupil) if a['id'] == f"use_item:{book['id']}")
        w.execute(pupil, use)
        self.assertTrue(items.knows(w, pupil['id'], art))
        self.assertNotIn(book['id'], w.items['objects'])
        self.assertEqual(pupil['mentor'], teacher['id'])
        self.assertEqual(teacher['time_left'], 3)

    def test_migration_adds_stock_once_and_never_deletes_new_manuals(self):
        w = self.world(); _, key = self.teacher(w); book = self.book(w, key)
        for item in list(w.items['objects'].values()):
            if item['owner'] == 'market' and items.spec(item).get('art'): items.remove(w, item)
        w.items.pop('manual_catalog')
        rng = w.rng.getstate(); month = w.month; before = len(w.items['objects'])
        self.assertTrue(items.initialize(w, legacy=True))
        self.assertEqual(len(w.items['objects']), before+126)
        self.assertEqual(w.rng.getstate(), rng); self.assertEqual(w.month, month)
        self.assertIn(book['id'], w.items['objects'])
        self.assertFalse(items.initialize(w, legacy=True))
        self.assertIn(book['id'], w.items['objects'])


if __name__ == '__main__':
    unittest.main()
