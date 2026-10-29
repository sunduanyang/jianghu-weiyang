from copy import deepcopy
import unittest

from jianghu import physical_combat as combat
from jianghu.engine import World
from jianghu.store import Store
from tests.physical_helpers import frame


class PhysicsBackendStateTests(unittest.TestCase):
    def setUp(self):
        self.world = World(42, 30, Store(), durable=False)
        self.addCleanup(self.world.store.close)
        self.world.start_battle(1)
        self.battle = self.world.battle

    def test_old_frames_keep_their_original_shape(self):
        packet = frame(self.battle)
        self.assertEqual(combat.validate_frame(self.battle, packet), packet)

    def test_core_backend_survives_checkpoint_and_world_reload(self):
        packet = frame(self.battle)
        packet['physics_backend'] = 'physics-core-2d'
        self.world.battle_action(self.battle['id'], 1, 'checkpoint', packet)
        exported = deepcopy(self.world.export())
        self.world.restore(exported)
        self.assertEqual(self.world.battle['frame'], packet)

    def test_active_backend_cannot_change_or_silently_disappear(self):
        packet = frame(self.battle)
        packet['physics_backend'] = 'physics-core-2d'
        self.world.battle_action(self.battle['id'], 1, 'checkpoint', packet)
        for backend in ('legacy-physics2d', None, 'unknown'):
            changed = deepcopy(packet)
            if backend is None:
                del changed['physics_backend']
            else:
                changed['physics_backend'] = backend
            before = deepcopy(self.world.export())
            with self.subTest(backend=backend), self.assertRaises(ValueError):
                self.world.battle_action(self.battle['id'], 2, 'checkpoint', changed)
            self.assertEqual(self.world.export(), before)
