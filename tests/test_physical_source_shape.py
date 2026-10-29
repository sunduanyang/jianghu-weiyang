from copy import deepcopy
import math
import struct
import unittest

from jianghu import physical_source_shape as source


def state():
    return dict(version=1, config=dict(source.NEUTRAL))


class SourceShapeTests(unittest.TestCase):
    def test_old_checkpoint_can_upgrade_but_new_cannot_downgrade(self):
        self.assertIsNone(source.validate(None))
        packet = state()
        self.assertEqual(source.validate(packet), packet)
        self.assertEqual(source.validate(packet, deepcopy(packet)), packet)
        with self.assertRaises(ValueError):
            source.validate(None, packet)

    def test_gender_and_native_float_endpoints_round_trip(self):
        for gender in (0, 1):
            for edge in (0, 1):
                packet = state(); packet['config']['Gender'] = gender
                for key, bounds in source.BOUNDS.items():
                    packet['config'][key] = bounds[edge]
                self.assertEqual(source.validate(packet), packet)

    def test_every_shape_field_is_immutable_after_first_save(self):
        for key in source.NEUTRAL:
            packet = state()
            packet['config'][key] = 0 if key == 'Gender' else source.BOUNDS[key][0]
            with self.subTest(key=key), self.assertRaises(ValueError):
                source.validate(packet, state())

    def test_nonfinite_bool_strings_and_just_outside_native_bounds_rejected(self):
        for key, (low, high) in source.BOUNDS.items():
            for invalid in (True, False, None, '0', math.nan, math.inf, -math.inf,
                            low - 1e-7, high + 1e-7):
                packet = state(); packet['config'][key] = invalid
                with self.subTest(key=key, invalid=invalid), self.assertRaises(ValueError):
                    source.validate(packet)

    def test_unity_short_single_spelling_is_normalized_not_clamped(self):
        packet = state()
        packet['config'].update(Height=.45, Weight=.55, ShouldersWidth=.155,
                                WaistWidth=.75, HipsWidth=.035)
        clean = source.validate(packet)
        self.assertEqual(clean['config']['Height'], source.BOUNDS['Height'][1])
        self.assertEqual(clean['config']['ShouldersWidth'], source.BOUNDS['ShouldersWidth'][1])
        self.assertEqual(source.validate(packet, clean), clean)
        for key, (low, high) in source.BOUNDS.items():
            for edge in (low, high):
                bits = struct.unpack('<I', struct.pack('<f', edge))[0]
                # The positive lower shoulder bound steps toward zero;
                # all other endpoints step away from zero.
                delta = -1 if edge == low and edge > 0 else 1
                outside = struct.unpack('<f', struct.pack('<I', bits + delta))[0]
                bad = state(); bad['config'][key] = outside
                with self.subTest(key=key, outside=outside), self.assertRaises(ValueError):
                    source.validate(bad)

    def test_complete_closed_schema_and_integer_version_gender_required(self):
        for key in source.NEUTRAL:
            packet = state(); del packet['config'][key]
            with self.assertRaises(ValueError): source.validate(packet)
        for key in ('version', 'config'):
            packet = state(); del packet[key]
            with self.assertRaises(ValueError): source.validate(packet)
        invalid_packets = [None, {}, {'version': True, 'config': source.NEUTRAL},
                           {'version': 1.0, 'config': source.NEUTRAL},
                           {'version': 2, 'config': source.NEUTRAL}]
        for invalid in invalid_packets:
            with self.assertRaises(ValueError): source.validate(invalid, state())
        for invalid in (True, 1.0, '1', 2, -1, None):
            packet = state(); packet['config']['Gender'] = invalid
            with self.assertRaises(ValueError): source.validate(packet)
        for location in ('outer', 'config'):
            packet = state(); (packet if location == 'outer' else packet['config'])['extra'] = 0
            with self.assertRaises(ValueError): source.validate(packet)

    def test_clean_result_does_not_alias_mutable_input(self):
        packet = state(); clean = source.validate(packet)
        packet['config']['Height'] = .1
        self.assertEqual(clean, state())


if __name__ == '__main__':
    unittest.main()
