from copy import deepcopy
import json
from pathlib import Path
import unittest
from jianghu import combat_attributes as abilities
from jianghu.engine import World
from jianghu import physical_combat as combat
from tests.physical_helpers import frame

class CombatPaceTests(unittest.TestCase):
    def test_new_baseline_improves_abilities_speed_and_energy(self):
        actor={};abilities.initialize(actor)
        self.assertEqual(abilities.base_ratings(actor),dict.fromkeys(abilities.KEYS,75.))
        p=abilities.parameters(abilities.ratings(actor))
        self.assertGreaterEqual(p['max_health'],110)
        self.assertEqual(p['stamina_max'],120)
        self.assertAlmostEqual(p['movement_scale'],1.595)
        self.assertLessEqual(p['effort_scale'],.53)
        self.assertGreaterEqual(p['regen_scale'],2.35)
        self.assertLess(p['recovery_delay_scale'],.5)
        self.assertGreater(p['force_scale'],1)

    def test_baseline_upgrade_is_once_and_preserves_custom_values(self):
        uniform={'combat_attributes':{'version':1,'base':dict.fromkeys(abilities.KEYS,50.)}}
        abilities.initialize(uniform)
        self.assertEqual(abilities.base_ratings(uniform),dict.fromkeys(abilities.KEYS,75.))
        uniform['combat_attributes']['base']=dict.fromkeys(abilities.KEYS,50.)
        abilities.initialize(uniform)
        self.assertEqual(abilities.base_ratings(uniform),dict.fromkeys(abilities.KEYS,50.))
        custom={'combat_attributes':{'version':1,'base':dict.fromkeys(abilities.KEYS,50.)}}
        custom['combat_attributes']['base']['strength']=83
        before=deepcopy(custom['combat_attributes']['base'])
        abilities.initialize(custom)
        self.assertEqual(abilities.base_ratings(custom),before)

    def test_save_upgrade_does_not_charge_or_advance_the_world(self):
        w=World(42,12,durable=False)
        try:
            for a in [w.player,*w.npcs.values()]:a['combat_attributes']={'version':1,'base':dict.fromkeys(abilities.KEYS,50.)}
            state=deepcopy(w.export());events=w.store.count();w.restore(state)
            self.assertEqual(w.month,state['month']);self.assertEqual(w.rng.getstate(),state['random_state'])
            self.assertEqual(w.player['wealth'],state['player']['wealth']);self.assertEqual(w.store.count(),events)
            self.assertTrue(all(abilities.base_ratings(a)==dict.fromkeys(abilities.KEYS,75.) for a in [w.player,*w.npcs.values()]))
        finally:w.store.close()

    def test_tuning_is_shared_with_the_client(self):
        root=Path(__file__).resolve().parents[1]
        staged=root/'unity/JianghuUnity/Assets/StreamingAssets/Backend/jianghu/combat_tuning.json'
        self.assertEqual(abilities.TUNING,json.loads(staged.read_text(encoding='utf8')))

    def test_existing_bout_keeps_pose_and_damage_ratios_when_retuned(self):
        w=World(42,12,durable=False)
        try:
            w.start_battle(1)
            for a in [w.player,w.npcs[1]]:a['combat_attributes']={'version':1,'base':dict.fromkeys(abilities.KEYS,50.)}
            for f in w.battle['fighters']:
                f.pop('tuning_version');f.update(vigor=40.,vigor_max=80.,stamina=50.,stamina_max=100.,balance_max=100.)
            w.battle['frame']=frame(w.battle,8);w.battle['elapsed']=8
            state=deepcopy(w.export());bodies=deepcopy(w.battle['frame']['fighters'][0]['bodies']);events=w.store.count()
            w.restore(state);b=w.battle
            self.assertEqual(b['id'],state['battle']['id']);self.assertEqual(b['revision'],state['battle']['revision'])
            self.assertEqual(b['elapsed'],8);self.assertEqual(b['frame']['fighters'][0]['bodies'],bodies)
            for f,p in zip(b['fighters'],b['frame']['fighters']):
                self.assertAlmostEqual(f['vigor']/f['vigor_max'],.5)
                self.assertAlmostEqual(p['health']/f['vigor_max'],.5)
                self.assertAlmostEqual(p['stamina']/f['stamina_max'],.5)
            self.assertEqual(w.store.count(),events);self.assertEqual(w.rng.getstate(),state['random_state'])
            before=deepcopy(b);self.assertFalse(combat.upgrade_tuning(w));self.assertEqual(before,b)
            combat.validate_frame(b,b['frame'])
        finally:w.store.close()

if __name__=='__main__':unittest.main()
