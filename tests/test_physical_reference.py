from copy import deepcopy
import unittest

from jianghu import physical_combat as combat
from jianghu import physical_reference as reference
from jianghu.engine import World
from jianghu.store import Store
from tests.physical_helpers import frame


class ReferenceStateTests(unittest.TestCase):
    def setUp(self):
        self.world = World(42, 30, Store(), durable=False)
        self.addCleanup(self.world.store.close)
        self.world.start_battle(1)
        self.battle = self.world.battle

    def test_new_sessions_require_all_driver_bodies(self):
        self.assertEqual(self.battle['fighters'][0]['physics_profile'], reference.PROFILE)
        for mutation in ('missing_state', 'missing_body', 'duplicate_body', 'nan_target'):
            p = frame(self.battle)
            r = p['fighters'][0]['reference']
            if mutation == 'missing_state':
                del p['fighters'][0]['reference']
            elif mutation == 'missing_body':
                r['bodies'].pop()
            elif mutation == 'duplicate_body':
                r['bodies'][0] = deepcopy(r['bodies'][1])
            else:
                r['hands']['Near']['target_x'] = float('nan')
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                combat.validate_frame(self.battle, p)

    def test_exhaustion_and_detached_hand_persist_without_regrowth(self):
        p = frame(self.battle)
        r = p['fighters'][0]['reference']
        r.update(exhausted_left=2.5, wake_pending=True)
        r['parts']['handNear'] = dict(health=0, severed=True)
        combat.submit(self.battle, 'checkpoint', p)
        self.assertEqual(self.battle['frame']['fighters'][0]['reference'], r)
        invalid = deepcopy(self.battle['frame'])
        invalid['fighters'][0]['reference']['parts']['handNear']['severed'] = False
        with self.assertRaises(ValueError):
            combat.validate_frame(self.battle, invalid)
        invalid = deepcopy(self.battle['frame'])
        invalid['fighters'][0]['reference']['parts']['handNear']['health'] = 1
        with self.assertRaises(ValueError):
            combat.validate_frame(self.battle, invalid)

    def test_existing_sessions_remain_on_their_original_rig(self):
        for f in self.battle['fighters']:
            f.pop('physics_profile')
        p = frame(self.battle)
        self.assertNotIn('reference', p['fighters'][0])
        self.assertEqual(combat.validate_frame(self.battle, p), p)

    def test_kick_state_persists_and_rejects_downgrade_or_invalid_timers(self):
        p = frame(self.battle)
        p['fighters'][0]['reference']['kick_state'] = dict(version=1, paid=True,
            kicked_left=.15, immunity_left=.3)
        combat.submit(self.battle, 'checkpoint', p)
        self.assertEqual(self.battle['frame'], p)
        for mutation in ('missing', 'version', 'paid', 'nan', 'negative', 'too_long'):
            invalid = deepcopy(self.battle['frame'])
            r = invalid['fighters'][0]['reference']
            if mutation == 'missing':
                del r['kick_state']
            else:
                key, value = {'version': ('version', True), 'paid': ('paid', 1),
                    'nan': ('kicked_left', float('nan')), 'negative': ('immunity_left', -.1),
                    'too_long': ('kicked_left', .6)}[mutation]
                r['kick_state'][key] = value
            before = deepcopy(self.battle)
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                combat.submit(self.battle, 'checkpoint', invalid)
            self.assertEqual(self.battle, before)

    def test_legacy_reference_frame_can_gain_kick_state(self):
        p = frame(self.battle)
        combat.submit(self.battle, 'checkpoint', p)
        upgraded = deepcopy(p)
        for f in upgraded['fighters']:
            f['reference']['kick_state'] = dict(version=1, paid=False,
                kicked_left=0, immunity_left=0)
        self.assertEqual(combat.validate_frame(self.battle, upgraded), upgraded)

    def test_auxiliary_state_is_bounded_and_rejects_non_bools(self):
        for field, value in [('exhausted_left', 4), ('recovering_left', -1), ('wake_pending', 1)]:
            p = frame(self.battle)
            p['fighters'][0]['reference'][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                combat.validate_frame(self.battle, p)

    def test_counterweight_state_cannot_disappear_after_checkpoint(self):
        self.battle['fighters'][0]['shield'] = True
        p = frame(self.battle)
        combat.submit(self.battle, 'checkpoint', p)
        for change in ('missing_weight', 'downgrade', 'nan_velocity'):
            invalid = deepcopy(self.battle['frame'])
            r = invalid['fighters'][0]['reference']
            if change == 'missing_weight':
                r['weights'].pop()
            elif change == 'downgrade':
                del r['weapon_rig']
            else:
                r['weights'][0]['vx'] = float('nan')
            with self.subTest(change=change), self.assertRaises(ValueError):
                combat.validate_frame(self.battle, invalid)

    def test_earlier_reference_snapshot_can_gain_counterweights(self):
        p = frame(self.battle)
        for f in p['fighters']:
            del f['reference']['weapon_rig']
            del f['reference']['weights']
        combat.submit(self.battle, 'checkpoint', p)
        upgraded = frame(self.battle)
        self.assertEqual(combat.validate_frame(self.battle, upgraded), upgraded)

    def test_offstage_rigid_bodies_survive_checkpoint_and_settlement(self):
        p = frame(self.battle, 12)
        sword = next(b for b in p['fighters'][1]['bodies'] if b['key'] == 'sword')
        sword.update(x=46.5, y=73.25, vx=42, vy=65)
        combat.submit(self.battle, 'checkpoint', p)
        self.assertEqual(self.battle['frame'], p)
        restored = deepcopy(self.battle)
        finish = deepcopy(restored['frame'])
        finish['fighters'][1]['health'] = 0
        finish['fighters'][0]['landed_hits'] = finish['hits'] = 1
        combat.submit(restored, 'complete', finish)
        self.assertEqual(restored['status'], 'finished')
        self.assertEqual(restored['winner'], 0)
        self.assertEqual(restored['frame']['fighters'][1]['bodies'], p['fighters'][1]['bodies'])

    def test_reference_world_coordinates_use_the_full_bout_travel_envelope(self):
        self.battle['fighters'][0]['weapon'] = dict(combat.PROFILES['sword'], id='sword')
        p = frame(self.battle)
        f = p['fighters'][0]
        f['reference']['bodies'][0].update(x=-120, y=-1500)
        f['reference']['weights'][0].update(x=reference.MAX_POSITION, y=-reference.MAX_POSITION)
        f['reference']['hands']['Near'].update(target_x=45, target_y=50)
        f['feet']['Near'].update(anchor_x=-25, from_y=-30, target_x=50)
        self.assertEqual(combat.validate_frame(self.battle, p), p)

    def test_coordinate_envelope_still_rejects_corruption_atomically(self):
        self.battle['fighters'][0]['weapon'] = dict(combat.PROFILES['sword'], id='sword')
        for group in ('bodies', 'drivers', 'weights', 'hands', 'feet'):
            for value in (float('nan'), float('inf'), -float('inf'), reference.MAX_POSITION + 1):
                p = frame(self.battle)
                f = p['fighters'][0]
                if group == 'bodies':
                    target, key = next(b for b in f['bodies'] if b['key'] == 'sword'), 'x'
                elif group == 'drivers':
                    target, key = f['reference']['bodies'][0], 'y'
                elif group == 'weights':
                    target, key = f['reference']['weights'][0], 'x'
                elif group == 'hands':
                    target, key = f['reference']['hands']['Near'], 'target_x'
                else:
                    target, key = f['feet']['Near'], 'anchor_x'
                target[key] = value
                before = deepcopy(self.battle)
                with self.subTest(group=group, value=value), self.assertRaises(ValueError):
                    combat.submit(self.battle, 'checkpoint', p)
                self.assertEqual(self.battle, before)

    def test_legacy_coordinate_limits_and_detailed_errors_remain(self):
        for f in self.battle['fighters']:
            f.pop('physics_profile')
        p = frame(self.battle)
        next(b for b in p['fighters'][0]['bodies'] if b['key'] == 'sword')['x'] = 20.01
        with self.assertRaisesRegex(ValueError, r'fighters\[0\]\.bodies\.sword\.x'):
            combat.validate_frame(self.battle, p)
