from copy import deepcopy
import unittest

from jianghu import physical_reference, physical_source_rig as source
from jianghu.physical_combat import number, fields


def state(role='human'):
    template, spec = next((k, v) for k, v in source.TEMPLATES.items() if v['role'] == role)
    return dict(version=1, template=template,
                bodies=[dict(id=i, x=0, y=1, angle=0, vx=0, vy=0, spin=0,
                             simulated=True, active=True, body_type=0, mass=3,
                             drag=0, angular_drag=0, gravity=1, qx=0, qy=0, qz=0, qw=1) for i in spec['bodies']],
                joints=[dict(id=i, enabled=True, anchor_x=0, anchor_y=0,
                             connected_x=0, connected_y=0) for i in spec['joints']],
                points=[dict(id=i, x=0, y=0, z=0, angle=0, sx=1, sy=1) for i in spec['points']])


class SourceRigStateTests(unittest.TestCase):
    def validate(self, data, old=None, **kwargs):
        return source.validate(data, old, number, fields, **kwargs)

    def test_all_three_exact_graphs_round_trip_and_bounds_match_owner(self):
        self.assertEqual(source.BODY_FIELDS, physical_reference.BODY_FIELDS)
        for role, counts in [('human', (34, 32, 14)), ('enemy', (31, 32, 13)), ('reserved', (34, 32, 14))]:
            packet = state(role)
            with self.subTest(role=role):
                self.assertEqual(tuple(len(packet[k]) for k in ('bodies', 'joints', 'points')), counts)
                self.assertEqual(self.validate(packet), packet)

    def test_old_checkpoints_upgrade_but_cannot_drop_tag_or_switch_template(self):
        self.assertIsNone(self.validate(None))
        packet = state();self.assertEqual(self.validate(packet), packet)
        with self.assertRaises(ValueError):self.validate(None, packet)
        with self.assertRaises(ValueError):self.validate(state('enemy'), packet)

    def test_roles_reject_first_checkpoint_template_morphs(self):
        self.validate(state(), human=True);self.validate(state('enemy'), human=False)
        for role, human in [('enemy', True), ('human', False), ('reserved', True), ('reserved', False)]:
            with self.subTest(role=role, human=human), self.assertRaises(ValueError):
                self.validate(state(role), human=human)

    def test_body_joint_point_sets_are_exact_unique_integer_ids(self):
        for collection in ('bodies', 'joints', 'points'):
            for mutation in ('missing', 'duplicate', 'foreign', 'bool', 'float'):
                packet = state();rows = packet[collection]
                if mutation == 'missing':rows.pop()
                elif mutation == 'duplicate':rows[1] = deepcopy(rows[0])
                else:rows[0]['id'] = {'foreign': 999999, 'bool': True, 'float': float(rows[0]['id'])}[mutation]
                with self.subTest(collection=collection, mutation=mutation), self.assertRaises(ValueError):
                    self.validate(packet)

    def test_nonfinite_out_of_bounds_and_boolean_numbers_rejected(self):
        for collection, key, values in [('bodies', 'x', [20001, float('nan'), float('inf'), True]),
                                        ('bodies', 'vx', [100.01]), ('bodies', 'spin', [6001]),
                                        ('points', 'sx', [0, .00099, 20.01]),
                                        ('points', 'sy', [-.00099]), ('points', 'angle', [1000001]),
                                        ('points', 'z', [-20001]),
                                        ('bodies', 'mass', [0, -1, float('nan'), True]),
                                        ('bodies', 'gravity', [101, -101]),
                                        ('bodies', 'drag', [-1]),
                                        ('bodies', 'angular_drag', [float('inf')]),
                                        ('joints', 'connected_x', [float('nan'), 20001])]:
            for value in values:
                packet = state();packet[collection][0][key] = value
                with self.subTest(collection=collection, key=key, value=value), self.assertRaises(ValueError):
                    self.validate(packet)

    def test_explicit_active_self_and_simulation_booleans_are_required(self):
        packet = state();packet['bodies'][0].update(active=False, simulated=False)
        self.assertEqual(self.validate(packet)['bodies'][0]['active'], False)
        for collection, key in [('bodies', 'active'), ('bodies', 'simulated'), ('joints', 'enabled')]:
            packet = state();packet[collection][0][key] = 1
            with self.subTest(key=key), self.assertRaises(ValueError):self.validate(packet)

    def test_unknown_tags_never_become_arbitrary_component_writes(self):
        for collection in (None, 'bodies', 'joints', 'points'):
            packet = state();target = packet if collection is None else packet[collection][0]
            target['SourceMorph'] = '__class__'
            with self.subTest(collection=collection), self.assertRaises(ValueError):self.validate(packet)
        for key, value in [('version', True), ('template', 'f'*32)]:
            packet = state();packet[key] = value
            with self.subTest(key=key), self.assertRaises(ValueError):self.validate(packet)

    def test_reflections_and_inclusive_extreme_coordinates_are_preserved(self):
        packet = state();packet['points'][0].update(x=20000, y=-20000, z=20000, sx=-1, sy=.001, angle=-1000000)
        packet['bodies'][0].update(x=20000, y=-20000, angle=1000000, vx=-100, vy=100, spin=-6000)
        self.assertEqual(self.validate(packet), packet)

    def test_injury_and_recovery_body_properties_and_rebaked_anchors_survive(self):
        packet = state();packet['bodies'][0].update(mass=9, drag=0, angular_drag=5, gravity=1, body_type=0)
        packet['joints'][0].update(anchor_x=.0198, anchor_y=-.0089, connected_x=-.12, connected_y=.37)
        self.assertEqual(self.validate(packet), packet)
        recovered = deepcopy(packet);recovered['bodies'][0].update(mass=3, drag=5, angular_drag=5)
        self.assertEqual(self.validate(recovered, packet), recovered)
        for value in (True, 0.0, -1, 3):
            invalid = deepcopy(packet);invalid['bodies'][0]['body_type'] = value
            with self.subTest(value=value), self.assertRaises(ValueError):self.validate(invalid)

    def test_raw_three_dimensional_rotations_preserved_without_euler_projection(self):
        packet = state();packet['bodies'][0].update(qx=-.7071068, qy=-.7071068, qz=0, qw=0)
        self.assertEqual(self.validate(packet), packet)
        for quaternion in ((0, 0, 0, 0), (0, 0, 0, .99), (0, 1.0002, 0, 0),
                           (0, float('nan'), 0, 1), (0, 0, False, 1)):
            invalid = deepcopy(packet);invalid['bodies'][0].update(zip(('qx', 'qy', 'qz', 'qw'), quaternion))
            with self.subTest(quaternion=quaternion), self.assertRaises(ValueError):self.validate(invalid)


if __name__ == '__main__':unittest.main()
