from copy import deepcopy
import json
import unittest

from jianghu import combat, combat_realtime as rt
from jianghu.engine import World
from jianghu.store import Store
from tests.test_martial import actor


class RealtimeCombatTests(unittest.TestCase):
    def battle(self, seed=10):
        b = combat.start(actor('我', 1), actor('客', 2), seed)
        for f in b['fighters']:
            f['auto_attack'] = False
        b['ai_at'] = 999
        return b

    def ticks(self, b, count):
        for _ in range(count):
            if b['status'] == 'active':
                combat.step(b, 'tick')

    def ready(self, b, side=0, key='reed_cut'):
        f = b['fighters'][side]
        if key not in f['attacks']:
            f['attacks'].append(key)
        f['tokens'] = ['thrust', 'slash', 'crush', 'crush']
        f['forms'] = 4
        f['posture'] = f['breath'] = 100.
        return f

    def test_new_bouts_default_to_continuous_rules(self):
        b = self.battle()
        self.assertEqual(b['ruleset'], 3)
        self.assertEqual(b['distance'], 6)
        self.assertEqual(b['elapsed'], 0)

    def test_commands_and_views_never_advance_enemy_or_time(self):
        b = self.battle()
        enemy = deepcopy(b['fighters'][1])
        combat.step(b, 'advance')
        combat.step(b, 'target_head')
        combat.step(b, 'ratio_up')
        self.assertEqual(b['elapsed'], 0)
        self.assertEqual(b['distance'], 6)
        self.assertEqual(b['fighters'][1], enemy)
        before = deepcopy(b)
        self.assertEqual(combat.view(b), combat.view(b))
        self.assertEqual(b, before)

    def test_fine_movement_is_continuous_and_stops(self):
        b = self.battle()
        combat.step(b, 'advance')
        self.ticks(b, 1)
        self.assertTrue(5 < b['distance'] < 6)
        self.assertEqual(b['elapsed'], .2)
        combat.step(b, 'stop')
        distance = b['distance']
        self.ticks(b, 3)
        self.assertEqual(b['distance'], distance)

    def test_movement_uses_both_speeds_and_world_limits(self):
        b = self.battle()
        b['fighters'][0]['move'] = -1
        b['fighters'][1]['move'] = 1
        self.ticks(b, 1)
        self.assertEqual(b['distance'], 6)
        b['fighters'][1]['move_speed'] *= 2
        self.ticks(b, 1)
        self.assertGreater(b['distance'], 6)
        b['distance'] = 9.99
        self.ticks(b, 1)
        self.assertEqual(b['distance'], 10)

    def test_empty_mobility_invalid_command_is_atomic_and_recovers(self):
        b = self.battle()
        b['fighters'][0]['mobility'] = 0
        before = deepcopy(b)
        with self.assertRaises(ValueError):
            combat.step(b, 'advance')
        self.assertEqual(before, b)
        self.ticks(b, 1)
        self.assertGreater(b['fighters'][0]['mobility'], 0)

    def test_basic_spends_attack_charge_and_rotates_nine_forms(self):
        b = self.battle()
        f = b['fighters'][0]
        for i in range(12):
            f['attack_charge'] = 3
            f['recovery'] = 0
            b['distance'] = 1  # Out of range still earns forms/readiness.
            combat.step(b, 'basic_thrust')
        self.assertEqual(f['forms'], 9)
        self.assertEqual(f['tokens'], ['crush', 'thrust', 'slash', 'thrust', 'crush', 'thrust', 'slash', 'thrust', 'crush'])
        self.assertEqual(f['attack_charge'], 2)
        self.assertEqual(b['fighters'][1]['vigor'], b['fighters'][1]['vigor_max'])

    def test_auto_attacks_are_independent_of_player_commands(self):
        b = self.battle()
        b['fighters'][1]['auto_attack'] = True
        self.ticks(b, 1)
        self.assertTrue(any(r['side'] == 1 and r.get('kind') == 'basic' for r in b['log']))
        self.assertFalse(any(r['side'] == 0 for r in b['log']))

    def test_no_attack_charge_cannot_be_spammed(self):
        b = self.battle()
        b['fighters'][0]['attack_charge'] = .1
        before = deepcopy(b)
        with self.assertRaisesRegex(ValueError, '攻势'):
            combat.step(b, 'basic_thrust')
        self.assertEqual(before, b)

    def test_cast_costs_paid_once_and_moving_does_not_cancel(self):
        b = self.battle()
        f = self.ready(b)
        qi = f['qi']
        combat.step(b, 'reed_cut')
        self.assertAlmostEqual(f['posture'], 20)
        self.assertAlmostEqual(f['breath'], 80)
        self.assertEqual(f['qi'], qi-8)
        self.assertEqual(f['forms'], 2)
        combat.step(b, 'advance')
        self.assertIsNotNone(f['casting'])
        combat.step(b, 'stop')
        self.ticks(b, 15)
        self.assertIsNone(f['casting'])
        self.assertEqual(sum(r['action'] == 'reed_cut' and 'damage' in r for r in b['log']), 1)

    def test_retreat_during_windup_avoids_release_without_refund(self):
        b = self.battle()
        f = self.ready(b)
        b['fighters'][1]['move'] = 1
        b['fighters'][1]['move_speed'] = 4
        combat.step(b, 'reed_cut')
        forms = f['forms']
        self.ticks(b, 12)
        self.assertTrue(any(r.get('outcome') == 'out_of_range' and r['action'] == 'reed_cut' for r in b['log']))
        self.assertEqual(f['forms'], forms)

    def test_invalid_patterns_and_readiness_have_no_side_effects(self):
        for field, value in [('tokens', ['crush', 'crush']), ('posture', 0), ('breath', 0), ('qi', 0)]:
            b = self.battle()
            f = self.ready(b)
            f[field] = value
            before = deepcopy(b)
            with self.assertRaises(ValueError):
                combat.step(b, 'reed_cut')
            self.assertEqual(before, b)

    def test_cancel_keeps_costs_and_switch_is_locked(self):
        b = self.battle()
        f = self.ready(b)
        combat.step(b, 'reed_cut')
        qi, forms = f['qi'], f['forms']
        combat.step(b, 'cancel')
        self.assertEqual((f['qi'], f['forms']), (qi, forms))
        self.ticks(b, 3)
        combat.step(b, 'mode_stone')
        before = deepcopy(b)
        with self.assertRaisesRegex(ValueError, '切换'):
            combat.step(b, 'mode_fist')
        self.assertEqual(before, b)
        self.ticks(b, 16)
        combat.step(b, 'mode_fist')
        self.assertEqual(f['mode'], 'fist')

    def test_inner_outer_split_controls_both_costs_and_injury(self):
        b = self.battle()
        f = self.ready(b)
        f['inner_ratio'] = 80
        combat.step(b, 'reed_cut')
        self.assertEqual((f['posture'], f['breath']), (80, 20))
        self.ticks(b, 12)
        row = next(r for r in b['log'] if r.get('hit'))
        self.assertAlmostEqual(row['internal_damage']/row['damage'], .8, delta=.01)
        self.assertGreater(b['fighters'][1]['inner_wounds'], b['fighters'][1]['outer_wounds'])

    def test_hit_checks_record_three_comparisons_and_body_part(self):
        b = self.battle()
        combat.step(b, 'target_leg')
        combat.step(b, 'basic_thrust')
        row = b['log'][-1]
        self.assertEqual(set(row['checks']), {'force', 'precision', 'swiftness'})
        self.assertEqual(row['body'], 'leg')

    def test_cast_freezes_paid_ratio_and_target_until_release(self):
        b = self.battle()
        self.ready(b)
        combat.step(b, 'reed_cut')
        combat.step(b, 'ratio_up')
        combat.step(b, 'target_head')
        self.ticks(b, 12)
        row = next(r for r in b['log'] if r.get('hit'))
        self.assertEqual(row['body'], 'chest')
        self.assertAlmostEqual(row['internal_damage']/row['damage'], .2, delta=.01)

    def test_limb_hits_reduce_the_corresponding_resource(self):
        for part, resource in [('arm', 'attack_charge'), ('leg', 'mobility')]:
            b = self.battle()
            f, target = b['fighters']
            f.update(target=part, force=10000, precision=10000, swiftness=10000)
            before = target[resource]
            combat.step(b, 'basic_thrust')
            self.assertTrue(b['log'][-1]['hit'])
            self.assertLess(target[resource], before)

    def test_simultaneous_releases_can_tie(self):
        b = self.battle()
        for side in (0, 1):
            f = self.ready(b, side)
            f.update(force=10000, precision=10000, swiftness=10000, attack=10000)
            combat.perform(b, 'reed_cut', side)
            f['casting']['remaining'] = .1
        self.ticks(b, 1)
        self.assertEqual(b['status'], 'finished')
        self.assertIsNone(b['winner'])
        self.assertEqual(sum(r.get('hit', False) for r in b['log']), 2)

    def test_parry_blocks_basic_but_not_cast(self):
        b = self.battle()
        combat.perform(b, 'parry', 1)
        combat.step(b, 'basic_thrust')
        self.assertEqual(b['log'][-1]['outcome'], 'parried')
        self.assertEqual(b['fighters'][1]['vigor'], b['fighters'][1]['vigor_max'])

    def test_temporary_openings_expire_and_restore_capacity(self):
        b = self.battle()
        f = b['fighters'][0]
        f['openings'] = [dict(slots=2, remaining=.3, body='head')]
        f['seals'] = [dict(slots=1, remaining=.6, body='arm')]
        self.assertEqual(rt.occupancy(f), 3)
        self.ticks(b, 4)
        self.assertEqual(rt.occupancy(f), 0)

    def test_capacity_defeat_does_not_require_zero_vigor(self):
        b = self.battle()
        b['fighters'][0]['openings'] = [dict(slots=12, remaining=10., body='head')]
        self.ticks(b, 1)
        self.assertEqual(b['winner'], 1)
        self.assertEqual(b['status'], 'finished')

    def test_healing_removes_actual_wounds_and_may_be_interrupted(self):
        b = self.battle()
        f = b['fighters'][0]
        f['wounds'] = [dict(kind='external', damage=10., slots=1., body='chest')]
        f['vigor'] -= 10
        f['outer_wounds'] = 10
        combat.step(b, 'heal')
        self.ticks(b, 11)
        self.assertEqual(f['outer_wounds'], 0)
        self.assertEqual(f['wounds'], [])
        self.assertEqual(f['vigor'], f['vigor_max'])

    def test_damage_interrupts_healing_without_refund(self):
        b = self.battle()
        f, enemy = b['fighters']
        f['wounds'] = [dict(kind='external', damage=10., slots=1., body='chest')]
        combat.step(b, 'heal')
        qi = f['qi']
        enemy.update(force=10000, precision=10000, swiftness=10000)
        combat.perform(b, 'basic_thrust', 1)
        self.assertIsNone(f['casting'])
        self.assertEqual(f['qi'], qi)
        self.assertTrue(b['log'][-1]['guard_break'])

    def test_saved_mid_cast_round_trip_matches_all_later_frames(self):
        b = self.battle()
        self.ready(b)
        combat.step(b, 'reed_cut')
        self.ticks(b, 3)
        restored = json.loads(json.dumps(b))
        for _ in range(30):
            combat.step(b, 'tick')
            combat.step(restored, 'tick')
            self.assertEqual(b, restored)

    def test_many_seeded_bouts_terminate_with_bounded_resources(self):
        for seed in range(20):
            b = combat.start(actor('我', 1), actor('客', 2), seed)
            copy = deepcopy(b)
            combat.autoplay(b)
            combat.autoplay(copy)
            self.assertEqual(b, copy)
            self.assertEqual(b['status'], 'finished')
            self.assertLessEqual(b['elapsed'], rt.LIMIT)
            for f in b['fighters']:
                for field in ('posture', 'breath', 'mobility'):
                    self.assertTrue(0 <= f[field] <= 100, (seed, field, f[field]))
                self.assertTrue(0 <= f['forms'] <= 9)
                self.assertTrue(0 <= f['qi'] <= f['qi_max'])
                self.assertTrue(0 <= f['attack_charge'] <= 3)

    def test_database_restore_and_stale_revision_are_atomic(self):
        w = World(42, 30, durable=False)
        self.addCleanup(w.store.close)
        opponent = next(n for n in w.npcs.values() if n['age_months'] >= 216)
        w.start_battle(opponent['id'])
        w.battle_action(w.battle['id'], 1, 'tick')
        db = Store()
        self.addCleanup(db.close)
        w.store.db.backup(db.db)
        restored = World(store=db, durable=False)
        before = deepcopy(restored.export())
        with self.assertRaises(ValueError):
            restored.battle_action(restored.battle['id'], 1, 'tick')
        self.assertEqual(before, restored.export())
        for current in (w, restored):
            for _ in range(5):
                current.battle_action(current.battle['id'], current.battle['round'], 'tick')
            current.battle_action(current.battle['id'], current.battle['round'], 'surrender')
        self.assertEqual(w.export(), restored.export())
        self.assertEqual(w.store.history(limit=1), restored.store.history(limit=1))

    def test_free_commands_cannot_farm_sparring_rewards(self):
        w = World(42, 30, durable=False)
        self.addCleanup(w.store.close)
        opponent = next(n for n in w.npcs.values() if n['age_months'] >= 216)
        w.start_battle(opponent['id'])
        skill = w.player['skill']
        f = w.battle['fighters'][0]
        f.update(force=10000, precision=10000, swiftness=10000)
        for choice in ('target_arm', 'target_chest', 'basic_thrust', 'surrender'):
            w.battle_action(w.battle['id'], w.battle['round'], choice)
        self.assertEqual(w.player['skill'], skill)


if __name__ == '__main__':
    unittest.main()
