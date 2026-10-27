from jianghu import combat_attributes as abilities
from tests.combat_helpers import set_rating, update_actor
from copy import deepcopy
import math
from pathlib import Path
import unittest
from jianghu import physical_combat as combat, items
from jianghu.engine import World
from jianghu.store import Store
from tests.physical_helpers import frame

class PhysicalCombatTests(unittest.TestCase):
    def world(self):
        w=World(42,30,Store(),durable=False)
        self.addCleanup(w.store.close)
        for n in w.npcs.values():n['age_months']=300;n['health']=100
        return w

    def test_loadouts_cover_all_profiles_and_broken_weapon_becomes_fists(self):
        w=self.world();profiles=set()
        for key,spec in items.CATALOG.items():
            if spec['category']!='weapon':continue
            item=items.create(w,'player',key,'acceptance fixture')
            w.player['equipment']['weapon']=item['id']
            f=combat.fighter(w.player,'player',combat.equipment(w,'player'))
            profiles.add(f['weapon']['id'])
            self.assertEqual(f['weapon']['mass'],items.describe(item)['weapon_attributes']['mass'])
        self.assertEqual(profiles,set(combat.PROFILES)-{'unarmed'})
        item['durability']=0
        self.assertEqual(combat.equipment(w,'player')['weapon_profile'],'unarmed')

    def test_two_handed_weapon_disables_equipped_shield_without_deleting_it(self):
        w=self.world()
        shield=items.create(w,'player','wood_shield','fixture');w.player['equipment']['shield']=shield['id']
        self.assertTrue(combat.equipment(w,'player')['shield'])
        item=items.create(w,'player','long_spear','fixture');w.player['equipment']['weapon']=item['id']
        gear=combat.equipment(w,'player')
        self.assertFalse(gear['shield']);self.assertEqual(w.player['equipment']['shield'],shield['id'])
        self.assertEqual(gear['defense'],items.bonuses(w,'player')['defense'])

    def test_rejected_frame_and_retired_commands_are_atomic(self):
        w=self.world();w.start_battle(1);b=w.battle
        for action in ('guard','tick','auto','advance','bridge_fist'):
            before=deepcopy(w.export())
            with self.assertRaises(ValueError):w.battle_action(b['id'],b['revision'],action)
            self.assertEqual(w.export(),before)
        for field,value in (('health',math.nan),('stamina',b['fighters'][0]['stamina_max']+1),('health',b['fighters'][0]['vigor']+1)):
            packet=frame(b);packet['fighters'][0][field]=value;before=deepcopy(w.export())
            with self.assertRaises(ValueError):w.battle_action(b['id'],b['revision'],'checkpoint',packet)
            self.assertEqual(w.export(),before)
        packet=frame(b);packet['fighters'][0]['bodies'].pop();before=deepcopy(w.export())
        with self.assertRaises(ValueError):w.battle_action(b['id'],b['revision'],'checkpoint',packet)
        self.assertEqual(w.export(),before)
        with self.assertRaises(ValueError):w.battle_action(b['id'],b['revision'],'complete',frame(b,2))
        self.assertEqual(w.export(),before)

    def test_checkpoint_reload_and_single_settlement(self):
        w=self.world();w.start_battle(1);b=w.battle
        packet=frame(b,4);packet['fighters'][0]['attack_count']=2;packet['fighters'][0]['motion_spent']=2.5
        w.battle_action(b['id'],1,'checkpoint',packet)
        b=w.battle
        state=deepcopy(w.export());restored=self.world();restored.restore(state)
        self.assertEqual(restored.battle,w.battle)
        self.assertEqual(restored.battle['frame'],packet)
        revision=b['revision'];finish=frame(b,8);finish['fighters'][1]['health']=0
        finish['fighters'][0]['landed_hits']=6;finish['fighters'][0]['attack_count']=6;finish['hits']=6
        skill=abilities.rating(w.player);result=w.battle_action(b['id'],revision,'complete',finish)
        self.assertTrue(result['battle']['settled']);self.assertEqual(result['battle']['winner'],0)
        self.assertEqual(abilities.rating(w.player),skill)
        after=deepcopy(w.export());count=w.store.count()
        w.battle_action(b['id'],revision,'complete',finish)
        self.assertEqual(w.export(),after);self.assertEqual(w.store.count(),count)
        self.assertEqual(w.battle['summary'][0]['hits'],6)

    def test_old_active_rules_migrate_without_settlement_or_random_drift(self):
        for version in (1,2,3):
            w=self.world();w.start_battle(1);old=deepcopy(w.battle);old['ruleset']=version
            old['round']=7;old.pop('revision');w.battle=old
            before=deepcopy(w.export());count=w.store.count()
            self.assertTrue(w.restart_legacy_battle());self.assertEqual(w.battle['ruleset'],4)
            self.assertEqual(w.store.history(limit=1)[0]['facts']['legacy_combat'],old)
            self.assertEqual(w.store.count(),count+1)
            after=w.export()
            for key in before:
                if key!='battle':self.assertEqual(after[key],before[key],key)
            self.assertFalse(w.restart_legacy_battle())

    def test_offscreen_is_seeded_and_explicitly_identified(self):
        w=self.world();a,b=w.npcs[1],w.npcs[2]
        first=combat.start(a,b,42,equipment=(combat.equipment(w,1),combat.equipment(w,2)))
        result=combat.autoplay(deepcopy(first))
        self.assertEqual(result,combat.autoplay(deepcopy(first)))
        self.assertEqual(result['status'],'finished');self.assertTrue(result['log'])
        self.assertEqual(result['simulation'],'offscreen-weapon-dynamics')
        self.assertLessEqual(result['elapsed'],60)

    def test_created_hero_has_name_and_settlement_completes(self):
        from jianghu import protagonist
        w=self.world();hero=protagonist.view(w)
        protagonist.perform(w,dict(action='create',revision=hero['revision'],month=w.month,name='物理验收',sex='男',age=24,place=hero['place']))
        w.start_battle(1);self.assertEqual(w.battle['fighters'][0]['name'],'物理验收')
        w.battle_action(w.battle['id'],w.battle['revision'],'surrender',frame(w.battle,4))
        self.assertTrue(w.battle['settled']);self.assertIsNotNone(w.battle['event_id'])

    def test_retired_engine_modules_are_absent(self):
        folder=Path(combat.__file__).parent
        for name in ('combat.py','combat_flow.py','combat_realtime.py'):self.assertFalse((folder/name).exists())

if __name__=='__main__':unittest.main()
