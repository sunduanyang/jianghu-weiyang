from copy import deepcopy
import unittest

from jianghu import items, physical_combat as combat, physical_reference
from jianghu.engine import World
from jianghu.store import Store
from tests.physical_helpers import frame
from tests.test_physical_source_ai import checkpoint as ai_checkpoint
from tests.test_physical_source_rig import state as rig_checkpoint
from tests.test_physical_impalement import idle as idle_impalement, active as active_impalement
from tests.test_physical_source_shape import state as shape_checkpoint


class SourceProtocolIntegrationTests(unittest.TestCase):
    def setUp(self):
        self.world=World(42,30,Store(),durable=False)
        self.addCleanup(self.world.store.close)
        self.world.start_battle(1)
        self.battle=self.world.battle

    def packet(self):
        packet=frame(self.battle)
        for index, fighter in enumerate(packet['fighters']):
            fighter['reference'].update(source_ai=ai_checkpoint(),
                source_rig=rig_checkpoint('human' if index == 0 else 'enemy'),
                source_shape=shape_checkpoint(),
                source_impalement=idle_impalement())
        return packet

    def test_full_parallel_ai_rig_and_impalement_checkpoint_survives_world_reload(self):
        packet=self.packet()
        packet['fighters'][0]['reference']['source_ai']['left'].update(
            clip='1Hand_SwingUp', playing=True, time=.31)
        packet['fighters'][0]['reference']['source_ai']['right'].update(
            clip='2Hands_Stab', playing=True, time=.18)
        packet['fighters'][1]['reference']['source_impalement']=active_impalement(2)
        packet['fighters'][1]['health']=0
        packet['fighters'][0].update(landed_hits=1, attack_count=1)
        packet['hits']=1
        combat.submit(self.battle,'checkpoint',packet)
        exported=deepcopy(self.world.export())
        self.world.restore(exported)
        self.assertEqual(self.world.battle['frame'],packet)
        self.assertEqual(self.world.battle['frame']['fighters'][0]['reference']['source_ai']['jobs'][0]['child'],1)

    def test_legacy_frame_upgrades_once_and_each_extension_cannot_disappear(self):
        legacy=frame(self.battle)
        self.assertEqual(combat.validate_frame(self.battle,legacy),legacy)
        combat.submit(self.battle,'checkpoint',legacy)
        packet=self.packet();combat.submit(self.battle,'checkpoint',packet)
        for index in (0,1):
            for tag in ('source_ai','source_rig','source_shape','source_impalement'):
                bad=deepcopy(packet);del bad['fighters'][index]['reference'][tag]
                before=deepcopy(self.battle)
                with self.subTest(index=index,tag=tag),self.assertRaises(ValueError):
                    combat.submit(self.battle,'checkpoint',bad)
                self.assertEqual(self.battle,before)
        bad=deepcopy(packet)
        bad['fighters'][0]['reference']['source_ai']=dict(version=1,initialized=False)
        with self.assertRaises(ValueError):combat.validate_frame(self.battle,bad)

    def test_saved_body_configuration_cannot_morph_or_disappear_mid_bout(self):
        packet=self.packet();combat.submit(self.battle,'checkpoint',packet)
        for field,value in (('Gender',0),('Height',.1),('ShouldersWidth',.14)):
            changed=deepcopy(packet)
            changed['fighters'][0]['reference']['source_shape']['config'][field]=value
            before=deepcopy(self.battle)
            with self.subTest(field=field),self.assertRaises(ValueError):
                combat.submit(self.battle,'checkpoint',changed)
            self.assertEqual(self.battle,before)

    def test_first_frame_cannot_assign_enemy_or_reserved_graph_to_player(self):
        for role,index in (('enemy',0),('reserved',0),('human',1),('reserved',1)):
            packet=self.packet();packet['fighters'][index]['reference']['source_rig']=rig_checkpoint(role)
            with self.subTest(role=role,index=index),self.assertRaises(ValueError):
                combat.validate_frame(self.battle,packet)

    def test_cross_fighter_impalement_is_checked_after_local_validation(self):
        for failure in ('alive','self','severed'):
            packet=self.packet();victim=packet['fighters'][1]
            victim['reference']['source_impalement']=active_impalement()
            victim['health']=0;packet['fighters'][0]['landed_hits']=1;packet['hits']=1
            if failure == 'alive':victim['health']=1
            elif failure == 'self':victim['reference']['source_impalement']['attacker']=1
            else:victim['reference']['parts']['head']=dict(health=0,severed=True)
            before=deepcopy(self.battle)
            with self.subTest(failure=failure),self.assertRaises(ValueError):
                combat.submit(self.battle,'checkpoint',packet)
            self.assertEqual(self.battle,before)

    def test_unknown_ai_routine_and_foreign_rig_id_fail_atomically(self):
        for bad_tag in ('ai','rig'):
            packet=self.packet();reference=packet['fighters'][0]['reference']
            if bad_tag == 'ai':reference['source_ai']['jobs'][0]['stack'][0]['routine']='System.Reflection.Assembly.Load'
            else:reference['source_rig']['bodies'][0]['id']=9999999
            before=deepcopy(self.world.export())
            with self.subTest(tag=bad_tag),self.assertRaises(ValueError):
                self.world.battle_action(self.battle['id'],self.battle['revision'],'checkpoint',packet)
            self.assertEqual(self.world.export(),before)

    def nothing_packet(self):
        packet=self.packet()
        for index, fighter in enumerate(packet['fighters']):
            reference=fighter['reference']
            reference['source_equipment']=dict(version=1,nothing_template='sharedassets0.assets:273502')
            reference['weapon_rig']=1
            reference['weights']=[dict(key=key,x=0,y=1,angle=0,vx=0,vy=0,spin=0)
                for key in sorted(physical_reference.weight_keys(self.battle['fighters'][index],True))]
        return packet

    def test_source_nothing_counterweights_survive_checkpoint_and_reload(self):
        packet=self.nothing_packet();combat.submit(self.battle,'checkpoint',packet)
        self.world.restore(deepcopy(self.world.export()))
        self.assertEqual(self.world.battle['frame'],packet)
        for index, row in enumerate(packet['fighters']):
            expected=physical_reference.weight_keys(self.world.battle['fighters'][index],True)
            self.assertEqual({w['key'] for w in row['reference']['weights']},expected)

    def test_nothing_version_graph_and_complete_counterweights_are_required(self):
        packet=self.nothing_packet()
        for mutation in ('version','bool_version','template','missing_graph','missing_rig','missing_weight'):
            bad=deepcopy(packet);ref=bad['fighters'][0]['reference']
            if mutation=='version':ref['source_equipment']['version']=2
            elif mutation=='bool_version':ref['source_equipment']['version']=True
            elif mutation=='template':ref['source_equipment']['nothing_template']='foreign:1'
            elif mutation=='missing_graph':del ref['source_rig']
            elif mutation=='missing_rig':del ref['weapon_rig']
            else:ref['weights'].pop()
            before=deepcopy(self.battle)
            with self.subTest(mutation=mutation),self.assertRaises(ValueError):combat.submit(self.battle,'checkpoint',bad)
            self.assertEqual(self.battle,before)

    def test_saved_nothing_equipment_cannot_downgrade_to_legacy_fist(self):
        packet=self.nothing_packet();combat.submit(self.battle,'checkpoint',packet)
        bad=deepcopy(packet);del bad['fighters'][0]['reference']['source_equipment']
        before=deepcopy(self.battle)
        with self.assertRaises(ValueError):combat.submit(self.battle,'checkpoint',bad)
        self.assertEqual(self.battle,before)

    def test_counterweight_layout_distinguishes_source_nothing_from_old_fists(self):
        for weapon,grip in (('unarmed',0),('dagger',0),('greatsword',.22)):
            fighter=dict(weapon=dict(id=weapon,support_grip=grip),shield=False)
            self.assertEqual(physical_reference.weight_keys(fighter,True),{'Near'} if grip else {'Near','Far'})
            self.assertEqual(physical_reference.weight_keys(fighter),set() if weapon=='unarmed' else {'Near'})


class SourceEquipmentPacketTests(unittest.TestCase):
    def setUp(self):
        self.world=World(42,30,Store(),durable=False)
        self.addCleanup(self.world.store.close)

    def equipped(self, template, slot):
        item=items.create(self.world,'player',template,'source equipment packet test')
        self.world.player['equipment'][slot]=item['id']
        return item

    def packet(self):
        return combat.fighter(self.world.player,'player',combat.equipment(self.world,'player'))

    def test_all_active_weapon_packets_carry_exact_template_without_changing_stats(self):
        for template,spec in items.CATALOG.items():
            if spec['category'] != 'weapon':continue
            item=self.equipped(template,'weapon');packet=self.packet()
            self.assertEqual(packet['weapon']['template_id'],template)
            self.assertEqual(packet['weapon_item'],item['id'])
            for key,value in items.weapons.attributes(item).items():
                self.assertEqual(packet['weapon'][key],value)
        item['durability']=0;packet=self.packet()
        self.assertEqual(packet['weapon']['id'],'unarmed')
        self.assertEqual(packet['weapon']['template_id'],'')

    def test_armor_packet_preserves_identity_defense_and_broken_state_without_shield_defense(self):
        self.equipped('wood_shield','shield')
        for template in ('cloth','leather','silk_robe'):
            armor=self.equipped(template,'armor');packet=self.packet()
            expected=dict(template_id=template,defense=items.spec(armor)['defense'],durability=100)
            self.assertEqual(packet['armor'],expected)
            self.assertTrue(packet['shield'])
            self.assertGreater(combat.equipment(self.world,'player')['defense'],packet['armor']['defense'])
            armor['durability']=0;packet=self.packet()
            self.assertEqual(packet['armor'],{**expected,'durability':0})
            self.assertEqual(combat.equipment(self.world,'player')['defense'],2)

    def test_no_armor_is_explicit_empty_packet_and_fighter_does_not_alias_inventory_data(self):
        self.world.player['equipment'].pop('armor',None)
        self.assertEqual(self.packet()['armor'],{})
        self.equipped('leather','armor')
        gear=combat.equipment(self.world,'player');packet=combat.fighter(self.world.player,'player',gear)
        packet['armor']['defense']=99
        self.assertEqual(gear['armor']['defense'],4)

    def test_older_active_save_receives_metadata_without_retuning_or_replaying_its_frame(self):
        self.equipped('iron_practice_sword','weapon');self.equipped('leather','armor')
        self.world.start_battle(1);battle=self.world.battle
        packet=frame(battle);packet['fighters'][0].update(stamina=12,motion_spent=3,attack_count=1)
        combat.submit(battle,'checkpoint',packet)
        for fighter in battle['fighters']:
            fighter.pop('armor');fighter['weapon'].pop('template_id')
        prior_frame=deepcopy(battle['frame']);prior_resources=[(f['vigor'],f['stamina']) for f in battle['fighters']]
        prior_random=self.world.rng.getstate();prior_revision=battle['revision']
        self.assertTrue(combat.upgrade_tuning(self.world))
        self.assertEqual(battle['fighters'][0]['weapon']['template_id'],'iron_practice_sword')
        self.assertEqual(battle['fighters'][0]['armor'],dict(template_id='leather',defense=4,durability=100))
        self.assertEqual(battle['frame'],prior_frame)
        self.assertEqual([(f['vigor'],f['stamina']) for f in battle['fighters']],prior_resources)
        self.assertEqual(self.world.rng.getstate(),prior_random)
        self.assertEqual(battle['revision'],prior_revision)
        self.assertFalse(combat.upgrade_tuning(self.world))


if __name__=='__main__':
    unittest.main()
