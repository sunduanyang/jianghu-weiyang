from copy import deepcopy
import random
import unittest

from jianghu import combat, martial
from jianghu.engine import World
from jianghu.store import Store
from tests.test_martial import actor


class CombatFlowTests(unittest.TestCase):
    def battle(self, seed=10):
        return combat.start(actor('我', 1), actor('客', 2), seed)

    def ready(self, b, key='wave_palm', side=0):
        f = b['fighters'][side]
        f['arts'].setdefault(key, dict(practice=40, realm=1))
        if key not in f['attacks']:
            f['attacks'].append(key)
        f['tokens'] = martial.ARTS[key].get('pattern', []).copy()
        f['forms'] = len(f['tokens'])
        return f

    def test_basic_forms_are_typed_and_free_and_cap_at_four(self):
        b = self.battle()
        f = b['fighters'][0]
        qi = f['qi']
        b['fighters'][1]['vigor'] = 10000
        b['fighters'][1]['agility'] = -999
        for key in ['basic_thrust', 'basic_slash', 'basic_crush', 'basic_thrust', 'basic_slash']:
            combat.perform(b, key, 0)
        self.assertEqual(f['tokens'], ['slash', 'crush', 'thrust', 'slash'])
        self.assertEqual(f['forms'], 4)
        self.assertEqual(f['qi'], qi)

    def test_required_forms_and_wildcards_spend_atomically(self):
        b = self.battle()
        b['distance'] = 2
        f = self.ready(b)
        f.update(tokens=['slash', 'slash'], forms=2)
        before = deepcopy(b)
        with self.assertRaisesRegex(ValueError, '缺少'):
            combat.step(b, 'wave_palm')
        self.assertEqual(b, before)
        f.update(tokens=['crush', 'focus', 'thrust'], forms=3)
        combat.perform(b, 'wave_palm', 0)
        self.assertEqual(f['tokens'], ['thrust'])
        self.assertEqual(f['forms'], 1)

    def test_cast_can_miss_after_enemy_moves_and_is_not_refunded(self):
        b = self.battle()
        b['distance'] = 3
        f = self.ready(b)
        b['intent'] = 'retreat'
        qi = f['qi']
        combat.step(b, 'wave_palm')
        self.assertEqual(b['distance'], 4)
        self.assertEqual(f['qi'], qi-12)
        self.assertEqual(f['casting']['remaining'], 1)
        combat.step(b, 'continue')
        release = next(r for r in b['log'] if r['action'] == 'wave_palm' and r.get('outcome') == 'out_of_range')
        self.assertFalse(release['hit'])
        self.assertEqual(f['qi'], qi-12)
        self.assertIsNone(f['casting'])

    def test_cancelled_cast_keeps_costs_and_enemy_intention_is_committed(self):
        b = self.battle()
        b['distance'] = 2
        f = self.ready(b)
        combat.perform(b, 'wave_palm', 0)
        qi = f['qi']
        b['intent'] = 'bridge_fist'
        combat.step(b, 'retreat')
        self.assertEqual(b['distance'], 3)
        self.assertIsNone(f['casting'])
        self.assertEqual(f['qi'], qi)
        self.assertTrue(any(r.get('outcome') == 'out_of_range' and r['action'] == 'bridge_fist' for r in b['log']))
        self.assertFalse(any(r.get('damage', 0)>0 and r['side']==1 for r in b['log']))

    def test_parry_stops_basic_but_not_named_technique(self):
        plain, technique = self.battle(), self.battle()
        for b in (plain, technique):
            b['distance'] = 2
            b['fighters'][1]['parrying'] = True
        combat.perform(plain, 'basic_thrust', 0)
        self.assertEqual(plain['log'][-1]['outcome'], 'parried')
        self.assertEqual(plain['fighters'][1]['vigor'], plain['fighters'][1]['vigor_max'])
        self.assertLess(plain['fighters'][0]['posture'], 60)
        combat.perform(technique, 'bridge_fist', 0)
        self.assertTrue(technique['log'][-1]['hit'])
        self.assertGreater(technique['log'][-1]['damage'], 0)

    def test_parry_guard_break_is_credited_to_defender(self):
        b = self.battle()
        b['fighters'][0]['posture'] = 1
        b['fighters'][1]['parrying'] = True
        combat.perform(b, 'basic_thrust', 0)
        result = combat.view(b)['summary']
        self.assertEqual(result[0]['breaks'], 0)
        self.assertEqual(result[1]['breaks'], 1)

    def test_guard_break_interrupts_opponent_before_release(self):
        b = self.battle()
        b['distance'] = 2
        target = self.ready(b, side=1)
        combat.perform(b, 'wave_palm', 1)
        target['posture'] = 1
        b['intent'] = 'continue'
        combat.step(b, 'bridge_fist')
        self.assertIsNone(target['casting'])
        self.assertTrue(any(r.get('guard_break') for r in b['log']))
        self.assertTrue(any(r['action'] == 'continue' and r.get('outcome') == 'interrupted' for r in b['log']))

    def test_auto_continues_cast_and_views_are_pure(self):
        b = self.battle()
        b['distance'] = 2
        self.ready(b)
        combat.perform(b, 'wave_palm', 0)
        before = deepcopy(b)
        self.assertEqual(combat.view(b), combat.view(b))
        self.assertEqual(b, before)
        combat.step(b, 'auto')
        self.assertIsNone(b['fighters'][0]['casting'])
        self.assertTrue(any(r['action'] == 'wave_palm' and 'damage' in r for r in b['log']))

    def test_no_mobility_means_invalid_move_has_no_side_effects(self):
        b = self.battle()
        b['fighters'][0]['mobility'] = 0
        before = deepcopy(b)
        with self.assertRaisesRegex(ValueError, '脚力'):
            combat.step(b, 'advance')
        self.assertEqual(b, before)
        for _ in range(3):
            combat.step(b, 'guard')
        self.assertTrue(next(a for a in combat.actions(b) if a['id']=='advance')['enabled'])

    def test_telegraphed_cast_can_be_evaded_by_both_sides(self):
        for side in (0, 1):
            with self.subTest(side=side):
                b = self.battle()
                b['distance'] = 3
                self.ready(b, side=side)
                combat.perform(b, 'wave_palm', side)
                if side == 0:
                    b['intent'] = combat.ai_choice(b, 1)
                    self.assertEqual(b['intent'], 'retreat')
                    combat.step(b, 'continue')
                else:
                    b['intent'] = 'continue'
                    combat.step(b, 'retreat')
                self.assertTrue(any(r['side'] == side and r.get('outcome') == 'out_of_range' for r in b['log']))

    def test_committed_attack_swings_and_pays_after_opponent_retreats(self):
        b = self.battle()
        b['distance'] = 2
        b['intent'] = 'retreat'
        qi = b['fighters'][0]['qi']
        combat.step(b, 'bridge_fist')
        self.assertEqual(b['fighters'][0]['qi'], qi-2)
        self.assertTrue(any(r['side'] == 0 and r.get('outcome') == 'out_of_range' for r in b['log']))

    def test_old_active_battle_keeps_instant_techniques(self):
        b = self.battle()
        b.pop('ruleset')
        b['distance'] = 2
        b['fighters'][0]['forms'] = 3
        combat.perform(b, 'wave_palm', 0)
        self.assertTrue(b['log'][-1]['hit'])
        self.assertGreater(b['fighters'][1]['inner_wounds'], 0)
        self.assertIsNone(b['fighters'][0]['casting'])

    def test_many_deterministic_bouts_terminate_with_bounded_resources(self):
        for seed in range(50):
            b = self.battle(seed)
            f = self.ready(b, 'stone_force')
            f['tokens'] = ['focus']*4
            f['forms'] = 4
            before = deepcopy(b)
            result = combat.autoplay(b)
            self.assertEqual(result, combat.autoplay(before))
            self.assertEqual(result['status'], 'finished')
            self.assertLessEqual(result['round'], 60)
            for fighter in result['fighters']:
                self.assertTrue(0 <= fighter['forms'] <= 4)
                self.assertTrue(0 <= fighter['mobility'] <= 100)
                self.assertTrue(0 <= fighter['posture'] <= fighter['posture_max'])
                self.assertTrue(0 <= fighter['qi'] <= fighter['qi_max'])
                self.assertTrue(0 <= fighter['vigor'] <= fighter['vigor_max'])

    def test_legal_random_actions_and_mid_cast_restore_match(self):
        for seed in range(20):
            b = self.battle(seed)
            self.ready(b)
            rng = random.Random(seed)
            while b['status'] == 'active':
                legal = [a['id'] for a in combat.actions(b) if a['enabled'] and a['id'] != 'surrender']
                choice = rng.choice(legal)
                restored = deepcopy(b)
                combat.step(b, choice)
                combat.step(restored, choice)
                self.assertEqual(b, restored)

    def test_mid_cast_database_reload_preserves_intent_and_exact_outcome(self):
        w = World(42, 30, durable=False)
        self.addCleanup(w.store.close)
        opponent = next(n for n in w.npcs.values() if n['age_months'] >= 216)
        w.start_battle(opponent['id'])
        w.battle_action(w.battle['id'], 1, 'guard')
        w.battle_action(w.battle['id'], 2, 'guard')
        w.battle_action(w.battle['id'], 3, 'reed_cut')
        self.assertIsNotNone(w.battle['fighters'][0]['casting'])
        db = Store()
        self.addCleanup(db.close)
        w.store.db.backup(db.db)
        restored = World(store=db, durable=False)
        self.assertEqual(w.export(), restored.export())
        for current in (w, restored):
            current.battle_action(current.battle['id'], current.battle['round'], 'auto')
            current.battle_action(current.battle['id'], current.battle['round'], 'surrender')
        self.assertEqual(w.export(), restored.export())
        self.assertEqual(w.store.history(limit=1), restored.store.history(limit=1))


if __name__ == '__main__':
    unittest.main()
