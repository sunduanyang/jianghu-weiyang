from copy import deepcopy
import json
import tempfile
from pathlib import Path
import unittest

from jianghu import combat_attributes as abilities, martial, physical_combat as combat, items
from jianghu.engine import World
from jianghu.store import Store
from tests.physical_helpers import frame


class CombatAttributesTests(unittest.TestCase):
    def world(self):
        w=World(42,30,durable=False)
        self.addCleanup(w.store.close)
        for n in w.npcs.values():n['age_months']=300;n['health']=100
        return w

    def test_exact_ten_independent_abilities_and_no_legacy_influence(self):
        w=self.world();a=w.player
        expected=abilities.ratings(a)
        a['skill']=100;a['martial']['arts']={'old':{'realm':99,'practice':100}}
        self.assertEqual(expected,abilities.ratings(a))
        martial.initialize(a)
        self.assertNotIn('skill',a);self.assertEqual(a['martial']['arts'],{})
        self.assertNotIn('loadout',a['martial']);self.assertEqual(len(expected),10)

    def test_each_ability_has_distinct_mechanical_effect(self):
        effects={'constitution':('max_health',1),'strength':('force_scale',1),'power':('damage_scale',1),
                 'dexterity':('handling_scale',1),'footwork':('movement_scale',1),
                 'endurance':('stamina_max',1),'breathing':('regen_scale',1),
                 'rooting':('balance_max',1),'protection':('damage_reduction',1),
                 'resilience':('stun_scale',-1)}
        for key,(parameter,direction) in effects.items():
            with self.subTest(key=key):
                low=dict.fromkeys(abilities.KEYS,50.);high=low.copy();low[key]=0;high[key]=100
                a,b=abilities.parameters(low),abilities.parameters(high)
                self.assertGreater((b[parameter]-a[parameter])*direction,0)
        p=abilities.parameters(dict.fromkeys(abilities.KEYS,50.))
        self.assertEqual((p['stamina_max'],p['balance_max'],p['force_scale']),(100,100,1))

    def test_weapon_profile_is_unaffected_by_character_abilities(self):
        w=self.world();gear=combat.equipment(w,'player');a=combat.fighter(w.player,'player',gear)
        for k in abilities.KEYS:w.player['combat_attributes']['base'][k]=100
        b=combat.fighter(w.player,'player',gear)
        self.assertEqual(a['weapon'],b['weapon']);self.assertEqual(a['physique'],b['physique'])
        self.assertGreater(b['stamina_max'],a['stamina_max'])

    def test_variable_stamina_and_balance_caps_validate_and_restore(self):
        w=self.world();w.player['combat_attributes']['base']['endurance']=100
        w.player['combat_attributes']['base']['rooting']=100
        w.start_battle(1);b=w.battle;packet=frame(b,1)
        packet['fighters'][0].update(stamina=140,balance=130,regen_left=.6,motion_exhausted=0,swing_seconds=.3)
        w.battle_action(b['id'],b['revision'],'checkpoint',packet)
        state=deepcopy(w.export());restored=self.world();restored.restore(state)
        self.assertEqual(restored.battle,w.battle)
        for key,value in [('stamina',140.1),('balance',130.1),('regen_left',float('nan'))]:
            before=deepcopy(w.export());bad=deepcopy(packet);bad['fighters'][0][key]=value
            with self.assertRaises(ValueError):w.battle_action(w.battle['id'],w.battle['revision'],'checkpoint',bad)
            self.assertEqual(w.export(),before)

    def test_legacy_people_are_cleared_without_time_money_or_rng_cost(self):
        w=self.world();state=deepcopy(w.export());count=w.store.count()
        for a in [state['player'],*state['npcs'].values()]:
            a.pop('combat_attributes');a['skill']=99
            a['martial'].update(arts={'breath':{'realm':3,'practice':100}},loadout={'inner':'breath'})
        w.restore(state)
        for a in [w.player,*w.npcs.values()]:
            self.assertNotIn('skill',a);self.assertEqual(a['martial']['arts'],{})
            self.assertEqual(abilities.base_ratings(a),dict.fromkeys(abilities.KEYS,abilities.DEFAULT_ATTRIBUTE))
        self.assertEqual(w.rng.getstate(),state['random_state']);self.assertEqual(w.month,state['month'])
        self.assertEqual(w.player['wealth'],state['player']['wealth']);self.assertEqual(w.store.count(),count)

    def test_old_active_bout_restarts_once_without_settlement(self):
        w=self.world();w.start_battle(1)
        for f in w.battle['fighters']:f.pop('attribute_version')
        before=deepcopy(w.export());old=deepcopy(w.battle)
        self.assertTrue(w.restart_legacy_battle());self.assertFalse(w.restart_legacy_battle())
        self.assertEqual(w.store.history(limit=1)[0]['facts']['legacy_combat'],old)
        self.assertEqual(w.player,before['player']);self.assertEqual(w.npcs,before['npcs'])
        self.assertEqual(w.rng.getstate(),before['random_state'])

    def test_legacy_manual_items_and_reading_records_are_removed_on_load(self):
        w=self.world()
        book=items.create(w,'player','basic_manual','legacy fixture')
        w.player['read_books']['basic_manual']=3
        before=deepcopy(w.export());events=w.store.count()
        w.restore(before)
        self.assertNotIn(book['id'],w.items['objects'])
        self.assertNotIn(book['id'],w.player['inventory'])
        self.assertEqual(w.player['read_books'],{})
        self.assertEqual(w.player['wealth'],before['player']['wealth'])
        self.assertEqual(w.store.count(),events)

    def test_retired_training_routes_are_atomic_and_never_regenerate_arts(self):
        w=self.world()
        for op in ('learn','equip','practice','breakthrough'):
            before=deepcopy(w.export())
            with self.assertRaises(ValueError):w.martial_action(op,'breath')
            self.assertEqual(w.export(),before)
        for n in w.npcs.values():
            self.assertFalse({'train','mentor','teach','faction_study','faction_guide','read_item'} & {a['kind'] for a in w.legal_actions(n)})
        for _ in range(3):w.month_step()
        self.assertTrue(all('skill' not in a and not a['martial']['arts'] for a in [w.player,*w.npcs.values()]))
        self.assertTrue(all(v.get('art') for v in items.CATALOG.values() if v['category']=='manual'))
        self.assertFalse(items.RETIRED_MANUALS & items.CATALOG.keys())

    def test_independent_abilities_persist_across_database_restart(self):
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'world.sqlite';w=World(42,30,Store(path),durable=False)
            w.player['combat_attributes']['base']['strength']=83;w.checkpoint();w.store.close()
            restored=World(store=Store(path),durable=False)
            try:self.assertEqual(abilities.ratings(restored.player)['strength'],83)
            finally:restored.store.close()
