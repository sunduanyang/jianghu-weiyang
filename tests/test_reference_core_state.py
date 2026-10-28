from copy import deepcopy
import unittest

from jianghu import physical_combat as combat, physical_reference as reference
from jianghu.engine import World
from jianghu.store import Store
from tests.physical_helpers import frame


class ReferenceCoreStateTests(unittest.TestCase):
    def setUp(self):
        self.world=World(42,30,Store(),durable=False)
        self.addCleanup(self.world.store.close)
        self.world.start_battle(1)
        self.battle=self.world.battle

    def packet(self):
        packet=frame(self.battle)
        for fighter in packet['fighters']:
            fighter['reference']['core_state']=dict(version=1,
                part_modes=dict.fromkeys(reference.PARTS,0),
                part_cooldowns=dict.fromkeys(reference.PARTS,0.),
                bleeding=False,bleed_left=0.,impact_count=0,impact_left=0.)
        return packet

    def test_legacy_snapshot_upgrades_without_losing_existing_state(self):
        legacy=frame(self.battle)
        combat.submit(self.battle,'checkpoint',legacy)
        upgraded=self.packet()
        self.assertEqual(combat.validate_frame(self.battle,upgraded),upgraded)

    def test_bleeding_injury_and_contact_budget_round_trip(self):
        packet=self.packet();state=packet['fighters'][0]['reference']
        state['parts']['forearmNear']=dict(health=0,severed=False)
        core=state['core_state'];core['part_modes']['forearmNear']=2
        core.update(bleeding=True,bleed_left=.64,impact_count=3,impact_left=.08)
        core['part_cooldowns']['forearmNear']=.12
        combat.submit(self.battle,'checkpoint',packet)
        saved=deepcopy(self.world.export());self.world.restore(saved)
        self.assertEqual(self.world.battle['frame'],packet)

    def test_new_state_cannot_be_downgraded_or_heal_immutable_modes(self):
        packet=self.packet();r=packet['fighters'][0]['reference']
        r['parts']['handFar']=dict(health=0,severed=False)
        r['core_state']['part_modes']['handFar']=1
        r['core_state'].update(bleeding=True,bleed_left=.2)
        combat.submit(self.battle,'checkpoint',packet)
        for mutation in ('missing','heal','replace','stop_bleeding'):
            invalid=deepcopy(packet);state=invalid['fighters'][0]['reference']
            if mutation=='missing':del state['core_state']
            elif mutation=='stop_bleeding':state['core_state']['bleeding']=False
            else:state['core_state']['part_modes']['handFar']=0 if mutation=='heal' else 3
            before=deepcopy(self.battle)
            with self.subTest(mutation=mutation),self.assertRaises(ValueError):combat.submit(self.battle,'checkpoint',invalid)
            self.assertEqual(self.battle,before)

    def test_invalid_modes_timers_counts_and_part_consistency_rejected(self):
        for mutation in ('bool_version','bool_mode','mode_range','missing_part','nan','long_bleed','long_cooldown','count','healthy_injury','cut_without_detachment','trunk','bad_parts'):
            packet=self.packet();state=packet['fighters'][0]['reference'];core=state['core_state']
            if mutation=='bool_version':core['version']=True
            elif mutation=='bool_mode':core['part_modes']['head']=True
            elif mutation=='mode_range':core['part_modes']['head']=4
            elif mutation=='missing_part':del core['part_modes']['head']
            elif mutation=='nan':core['impact_left']=float('nan')
            elif mutation=='long_bleed':core.update(bleeding=True,bleed_left=.7)
            elif mutation=='long_cooldown':core['part_cooldowns']['head']=.2
            elif mutation=='count':core['impact_count']=4
            elif mutation=='healthy_injury':core['part_modes']['handNear']=1
            elif mutation=='cut_without_detachment':
                state['parts']['handNear']['health']=0;core['part_modes']['handNear']=3
            elif mutation=='trunk':
                state['parts']['torso']['health']=0;core['part_modes']['torso']=1
            else:state['parts']=[]
            with self.subTest(mutation=mutation),self.assertRaises(ValueError):combat.validate_frame(self.battle,packet)


if __name__=='__main__':unittest.main()
