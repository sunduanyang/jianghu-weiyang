from copy import deepcopy
import unittest

from jianghu import physical_combat as combat
from jianghu.engine import World
from jianghu.store import Store
from tests.physical_helpers import frame


class SourceCombatStateTests(unittest.TestCase):
    def setUp(self):
        self.world=World(42,30,Store(),durable=False)
        self.addCleanup(self.world.store.close)
        self.world.start_battle(1)
        self.battle=self.world.battle

    def packet(self):
        packet=frame(self.battle)
        for fighter in packet['fighters']:
            r=fighter['reference']
            r['source_motion']=dict(version=1,clip='',time=0,off=False,aggression=0)
            r['death_state']=dict(version=1,stage=0,wait=0,
                body=dict(x=0,y=1,angle=0,vx=0,vy=0,spin=0))
        return packet

    def test_motion_and_mid_death_survive_world_export(self):
        packet=self.packet();r=packet['fighters'][0]['reference']
        two=bool(self.battle['fighters'][0]['weapon']['support_grip'])
        r['source_motion'].update(clip=('2Hands' if two else '1Hand')+'_SwingDown',time=.27,aggression=3)
        r['death_state'].update(stage=3,wait=.02)
        combat.submit(self.battle,'checkpoint',packet)
        exported=deepcopy(self.world.export());self.world.restore(exported)
        self.assertEqual(self.world.battle['frame'],packet)

    def test_legacy_upgrades_but_new_tags_cannot_disappear(self):
        combat.submit(self.battle,'checkpoint',frame(self.battle))
        packet=self.packet();combat.submit(self.battle,'checkpoint',packet)
        for tag in ('source_motion','death_state'):
            bad=deepcopy(packet);del bad['fighters'][0]['reference'][tag]
            with self.subTest(tag=tag),self.assertRaises(ValueError):combat.validate_frame(self.battle,bad)

    def test_death_order_and_timer_are_validated(self):
        packet=self.packet();packet['fighters'][0]['reference']['death_state'].update(stage=4,wait=.02)
        combat.submit(self.battle,'checkpoint',packet)
        for patch in (dict(stage=3),dict(stage=True),dict(stage=8),dict(wait=.03),dict(wait=float('nan')),dict(stage=7,wait=.01)):
            bad=deepcopy(packet);bad['fighters'][0]['reference']['death_state'].update(patch)
            with self.subTest(patch=patch),self.assertRaises(ValueError):combat.validate_frame(self.battle,bad)
        good=deepcopy(packet);good['fighters'][0]['reference']['death_state'].update(stage=7,wait=0)
        self.assertEqual(combat.validate_frame(self.battle,good),good)

    def test_motion_rejects_unknown_clip_mismatched_grip_and_bad_time(self):
        for patch in (dict(clip='Feet_Kick',time=.2,aggression=3),dict(clip='unknown'),dict(clip=[]),dict(clip={}),
                      dict(time=.1),dict(aggression=True),dict(version=True),dict(off=True)):
            packet=self.packet();packet['fighters'][0]['reference']['source_motion'].update(patch)
            with self.subTest(patch=patch),self.assertRaises(ValueError):combat.validate_frame(self.battle,packet)
        for clip in ('1Hand_Stab','2Hands_Stab'):
            packet=self.packet();motion=packet['fighters'][0]['reference']['source_motion']
            motion.update(clip=clip,time=1.01,aggression=3)
            with self.subTest(clip=clip),self.assertRaises(ValueError):combat.validate_frame(self.battle,packet)

    def test_rejected_new_state_does_not_mutate_battle(self):
        packet=self.packet();packet['fighters'][0]['reference']['death_state']['body']['vx']=101
        before=deepcopy(self.battle)
        with self.assertRaises(ValueError):combat.submit(self.battle,'checkpoint',packet)
        self.assertEqual(self.battle,before)


if __name__=='__main__':unittest.main()
