import unittest
from copy import deepcopy

from jianghu import physical_source_ai as source_ai


def relay():
    return dict(parent=dict(x=.323, y=.564), rest=dict(x=.473,y=.564), defending=False, enabled=True,
                playing=False, clip='', time=0, offset=0, started_at=0, tweens=[])


def checkpoint():
    return dict(version=1, initialized=True, clock=3.25,
                fields=dict(aiAggression=3, aiAggressionUnit=.5,
                            sourceNubLeft=dict(x=.12, y=.2), aiAttacking=True, sourceRealtimeValue=3.25),
                state=1, persona=1, reaction=1, procedural=False,
                random_seed=-123, random_draws=52, walk_taps=8, kick_schedules=1,
                left=relay(), right=relay(), double=relay(),
                jobs=[dict(done=False, due=.06, realtime=False, child=1,
                           stack=[dict(routine='attack', fields={'<>1__state': 2})]),
                      dict(done=False, due=.03, realtime=True, child=-1,
                           stack=[dict(routine='walk', fields={
                               '<>1__state': 1, 'direction': -1,
                               '<delay>5__1': .08})])])


class PhysicalSourceAITests(unittest.TestCase):
    def test_parallel_queue_and_tween_roundtrip(self):
        data=checkpoint()
        data['left'].update(playing=True, clip='1Hand_SwingUp', time=.23)
        data['right'].update(playing=True, clip='2Hands_Stab', time=.11)
        data['right']['tweens']=[dict(start=dict(x=.2,y=.4), target=dict(x=.4,y=2),
            age=.2, duration=.8, axes=2, circ=True, world_y=True)]
        clean=source_ai.validate(data)
        self.assertEqual(clean, data)
        clean['jobs'][0]['child']=-1
        self.assertEqual(data['jobs'][0]['child'], 1)

    def test_initial_upgrade_and_no_downgrade(self):
        empty=dict(version=1, initialized=False)
        self.assertEqual(source_ai.validate(empty), empty)
        source_ai.validate(checkpoint(), empty)
        with self.assertRaises(ValueError):
            source_ai.validate(empty, checkpoint())

    def test_unknown_or_type_named_routines_are_rejected(self):
        for name in ('System.Diagnostics.Process.Start', 'original.attack', '', None):
            data=checkpoint();data['jobs'][0]['stack'][0]['routine']=name
            with self.subTest(name=name),self.assertRaises(ValueError):
                source_ai.validate(data)

    def test_nonfinite_values_are_rejected_at_every_numeric_layer(self):
        for value in (float('nan'), float('inf'), -float('inf')):
            cases=[]
            data=checkpoint();data['clock']=value;cases.append(data)
            data=checkpoint();data['fields']['aiThreatY']=value;cases.append(data)
            data=checkpoint();data['jobs'][0]['due']=value;cases.append(data)
            data=checkpoint();data['jobs'][1]['stack'][0]['fields']['direction']=value;cases.append(data)
            data=checkpoint();data['left']['parent']['y']=value;cases.append(data)
            for data in cases:
                with self.assertRaises(ValueError):source_ai.validate(data)

    def test_cyclic_job_dependencies_are_rejected(self):
        data=checkpoint();data['jobs'][1]['child']=0
        with self.assertRaises(ValueError):source_ai.validate(data)
        data=checkpoint();data['jobs'][0]['child']=0
        with self.assertRaises(ValueError):source_ai.validate(data)

    def test_oversized_jobs_stack_and_random_replay_are_rejected(self):
        data=checkpoint();data['jobs']*=65
        with self.assertRaises(ValueError):source_ai.validate(data)
        data=checkpoint();data['jobs'][0]['stack']*=9
        with self.assertRaises(ValueError):source_ai.validate(data)
        data=checkpoint();data['random_draws']=1_000_001
        with self.assertRaises(ValueError):source_ai.validate(data)

    def test_numeric_booleans_and_invalid_flags_are_rejected(self):
        for key in ('version','state','persona','random_draws'):
            data=checkpoint();data[key]=True
            with self.subTest(key=key),self.assertRaises(ValueError):source_ai.validate(data)
        data=checkpoint();data['procedural']=1
        with self.assertRaises(ValueError):source_ai.validate(data)

    def test_unknown_fields_cannot_name_types_or_embed_objects(self):
        for fields in ({'type':'Assembly-CSharp'}, {'aiType': {'assembly': 'Injected'}},
                       {'aiObject': [1,2,3]}, {'aiDeep': {'x': {'x': 1}, 'y': 2}}):
            data=checkpoint();data['fields']=fields
            with self.subTest(fields=fields),self.assertRaises(ValueError):source_ai.validate(data)

    def test_clip_and_stopped_time_rules(self):
        data=checkpoint();data['left'].update(clip='2Hands_Stab', time=1.4)
        source_ai.validate(data)
        data['left']['clip']='Feet_Kick'
        with self.assertRaises(ValueError):source_ai.validate(data)
        data=checkpoint();data['left']['playing']=True
        with self.assertRaises(ValueError):source_ai.validate(data)

    def test_routine_state_and_schema_must_be_bounded(self):
        for state in (-2, 65, False, 1.5):
            data=checkpoint();data['jobs'][0]['stack'][0]['fields']['<>1__state']=state
            with self.subTest(state=state),self.assertRaises(ValueError):source_ai.validate(data)
        data=checkpoint();data['jobs'][0]['child']=2
        with self.assertRaises(ValueError):source_ai.validate(data)
        data=checkpoint();data['state']=7
        with self.assertRaises(ValueError):source_ai.validate(data)
        for logical_time in (-.001, True, float('nan'), 10_000_001):
            data=checkpoint();data['fields']['sourceRealtimeValue']=logical_time
            with self.subTest(logical_time=logical_time),self.assertRaises(ValueError):source_ai.validate(data)


if __name__=='__main__':
    unittest.main()
