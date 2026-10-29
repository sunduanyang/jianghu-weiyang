from copy import deepcopy
import unittest

from jianghu.physical_kick_receiver import validate
from jianghu.physical_combat import number
from jianghu import physical_combat as combat
from jianghu.engine import World
from jianghu.store import Store
from tests.physical_helpers import frame


class KickReceiverStateTests(unittest.TestCase):
    def test_legacy_and_two_waits_roundtrip_without_downgrade(self):
        self.assertIsNone(validate(None, None, number))
        for stage, wait in [(0, 0), (1, .35), (2, .15)]:
            data = dict(version=1, stage=stage, wait=wait)
            self.assertEqual(validate(data, None, number), data)
            with self.assertRaises(ValueError):validate(None, data, number)

    def test_invalid_stage_boolean_nonfinite_and_wait_durations_rejected(self):
        for stage, wait in [(True, 0), (3, 0), (0, .001), (1, .3501), (2, .1501),
                            (1, float('nan')), (1, float('inf')), (1, True), (1, -.01)]:
            with self.subTest(stage=stage, wait=wait), self.assertRaises(ValueError):
                validate(dict(version=1, stage=stage, wait=wait), None, number)
        with self.assertRaises(ValueError):validate(dict(version=True, stage=0, wait=0), None, number)
        with self.assertRaises(ValueError):validate(dict(version=1, stage=0, wait=0, damping=5), None, number)

    def test_full_checkpoint_retains_both_wait_stages_and_rejects_lost_tag(self):
        world = World(42, 30, Store(), durable=False);self.addCleanup(world.store.close)
        world.start_battle(1);battle = world.battle;packet = frame(battle)
        packet['fighters'][0]['reference']['kick_state'] = dict(version=1, paid=False,
            kicked_left=.35, immunity_left=.35, source_receiver=dict(version=1, stage=1, wait=.2))
        combat.submit(battle, 'checkpoint', packet)
        self.assertEqual(battle['frame'], packet)
        packet = deepcopy(packet);packet['fighters'][0]['reference']['kick_state'].update(
            kicked_left=.1, immunity_left=.1, source_receiver=dict(version=1, stage=2, wait=.1))
        combat.submit(battle, 'checkpoint', packet)
        self.assertEqual(battle['frame'], packet)
        broken = deepcopy(packet);del broken['fighters'][0]['reference']['kick_state']['source_receiver']
        with self.assertRaises(ValueError):combat.validate_frame(battle, broken)


if __name__ == '__main__':unittest.main()
