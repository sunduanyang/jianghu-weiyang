from copy import deepcopy
import math
import unittest

from jianghu.physical_impalement import validate, validate_links


def number(value, lo, hi, label):
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or not lo <= value <= hi:
        raise ValueError(label)
    return value


def fields(data, specs, prefix=''):
    return {key: number(data.get(key), lo, hi, prefix + key) for key, lo, hi in specs}


def idle():
    return dict(version=1, accumulated_damage=0, stage=0, wait=0, duration=0, organ='', attacker=-1, slot='')


def active(stage=1):
    return dict(version=1, accumulated_damage=2, stage=stage,
                wait={1: .025, 2: 1.2, 3: .25}[stage], duration=1.2,
                organ='Brain', attacker=0, slot='Near',
                joint=dict(anchor_x=.02, anchor_y=.13, connected_x=-.01, connected_y=.08))


class ImpalementStateTests(unittest.TestCase):
    def test_legacy_upgrade_then_no_silent_downgrade(self):
        self.assertIsNone(validate(None, None, number, fields))
        self.assertEqual(validate(idle(), None, number, fields), idle())
        with self.assertRaises(ValueError):
            validate(None, idle(), number, fields)

    def test_all_wait_phases_and_fixed_joint_anchors_survive_checkpoint(self):
        for stage in (1, 2, 3):
            state = active(stage)
            with self.subTest(stage=stage):
                self.assertEqual(validate(state, idle(), number, fields), state)

    def test_corrupt_or_nonphysical_connections_are_rejected(self):
        mutations = [('stage', True), ('version', True), ('attacker', True),
                     ('attacker', -1), ('organ', 'Foot'), ('slot', 'Other'),
                     ('duration', 1.19), ('wait', .026), ('accumulated_damage', float('nan')),
                     ('accumulated_damage', 601), ('joint', None)]
        for key, value in mutations:
            state = active();state[key] = value
            with self.subTest(key=key, value=value), self.assertRaises(ValueError):
                validate(state, None, number, fields)
        state = active(2);state['wait'] = state['duration'] + .01
        with self.assertRaises(ValueError):
            validate(state, None, number, fields)
        state = active();state['joint']['anchor_x'] = float('inf')
        with self.assertRaises(ValueError):
            validate(state, None, number, fields)

    def test_idle_cannot_smuggle_an_orphan_joint_or_pending_timer(self):
        for key, value in [('organ', 'Brain'), ('slot', 'Near'), ('attacker', 0),
                           ('duration', 1.2), ('wait', .01), ('joint', {})]:
            state = idle();state[key] = value
            with self.subTest(key=key), self.assertRaises(ValueError):
                validate(state, None, number, fields)

    def test_links_require_opponent_dead_victim_and_attached_organ(self):
        fighters = [dict(health=100, reference={}), dict(health=0, reference={
            'source_impalement': active(), 'parts': {'head': {'severed': False}}})]
        validate_links(fighters)
        for mutation in ('self', 'alive', 'severed'):
            modified = deepcopy(fighters)
            if mutation == 'self':
                modified[1]['reference']['source_impalement']['attacker'] = 1
            elif mutation == 'alive':
                modified[1]['health'] = 1
            else:
                modified[1]['reference']['parts']['head']['severed'] = True
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                validate_links(modified)


if __name__ == '__main__':
    unittest.main()
