from copy import deepcopy
import tempfile
from pathlib import Path
import unittest

from jianghu import martial, martial_catalog as catalog, combat_attributes as attributes
from jianghu import careers, sect_ranks, protagonist, physical_combat
from jianghu.atlas import COUNTRIES, PLACES, FACTIONS
from jianghu.engine import World
from jianghu.store import Store


class MartialCurriculaTests(unittest.TestCase):
    def world(self):
        w=World(42,0,durable=False)
        self.addCleanup(w.store.close)
        return w

    def person(self,w,place=0):
        n=w.make_npc(place=place)
        n.update(age_months=360,job='短工',office=None,social_status=None,health=100.)
        n['career']['profession']='短工'
        martial.sync_actor(w,n)
        return n

    def member(self,w,n,fid,grade):
        f=w.factions[fid];f['leader']=n['id'] if grade==1 else None
        contribution,ability=sect_ranks.REQUIREMENTS[grade] or (110,78)
        n.update(faction=fid,contribution=contribution)
        n['combat_attributes']['base']=dict.fromkeys(attributes.KEYS,ability)
        martial.sync_actor(w,n)

    def test_catalog_has_126_unique_selective_grade_scaled_arts(self):
        self.assertEqual(len(catalog.ARTS),126)
        self.assertEqual(len({a['name'] for a in catalog.ARTS.values()}),126)
        for school in catalog.catalog():
            self.assertEqual([a['grade'] for a in school['arts']],list(range(1,10)))
            for art in school['arts']:
                self.assertIn(len(art['bonuses']),(2,3))
                self.assertLessEqual(set(art['bonuses']),set(attributes.KEYS))
                self.assertEqual(sum(art['bonuses'].values()),sum(catalog.BUDGETS[art['grade']]))

    def test_all_fourteen_schools_and_all_nine_membership_grades(self):
        w=self.world();n=self.person(w)
        for f in w.factions:
            for grade in range(1,10):
                with self.subTest(school=f['key'],grade=grade):
                    self.member(w,n,f['id'],grade)
                    known=n['martial']['curriculum']['known']
                    self.assertEqual(len(known),10-grade)
                    self.assertEqual({catalog.ARTS[k]['grade'] for k in known},set(range(grade,10)))
                    self.assertEqual({catalog.ARTS[k]['school'] for k in known},{f['key']})
            f['leader']=None

    def test_all_twelve_countries_give_one_exact_grade_art(self):
        w=self.world()
        titles=['国主','宰相','世家家主','乡绅','书生','行商','匠人','农人','短工']
        for country in COUNTRIES:
            place=next(p['id'] for p in PLACES if p['country']==country['id'])
            n=self.person(w,place)
            source=n['martial']['curriculum']['civil_school']
            self.assertTrue(source)
            for grade,title in enumerate(titles,1):
                n['social_status']={'title':title,'country':country['id']}
                martial.sync_actor(w,n)
                c=n['martial']['curriculum'];self.assertEqual(c['civil_school'],source)
                self.assertEqual(c['known'],[f'{source}_g{grade}'])
                self.assertEqual(c['origin_country'],country['id'])
                f=next(f for f in FACTIONS if f['key']==source)
                self.assertEqual(PLACES[f['place']]['country'],country['id'])

    def test_lineage_stays_fixed_after_travel_conquest_and_rank_changes(self):
        w=self.world();n=self.person(w,FACTIONS[0]['place'])
        original=deepcopy(n['martial']['curriculum'])
        n['place']=FACTIONS[6]['place'];n['visited_places'].append(n['place'])
        n['social_status']={'title':'乡绅','country':5}
        w.territory[original['origin_place']]['owner']=5
        martial.sync_actor(w,n);c=n['martial']['curriculum']
        self.assertEqual(c['origin_country'],original['origin_country'])
        self.assertEqual(c['school'],original['school'])
        self.assertEqual(c['known'],[f"{c['school']}_g4"])

    def test_migration_is_deterministic_idempotent_and_does_not_charge(self):
        w=self.world();n=self.person(w)
        n['martial'].pop('curriculum')
        state=deepcopy(w.export());rng=w.rng.getstate();events=w.store.count()
        w.restore(deepcopy(state));expected=deepcopy(w.npcs[n['id']]['martial']['curriculum'])
        self.assertEqual(w.rng.getstate(),rng);self.assertEqual(w.store.count(),events)
        self.assertEqual(w.month,state['month']);self.assertEqual(w.npcs[n['id']]['wealth'],state['npcs'][n['id']]['wealth'])
        w.restore(deepcopy(state));self.assertEqual(w.npcs[n['id']]['martial']['curriculum'],expected)
        self.assertFalse(martial.synchronize(w))

    def test_bonuses_stack_once_are_capped_and_do_not_change_base_or_rank_gate(self):
        w=self.world();n=self.person(w);self.member(w,n,0,1)
        base=deepcopy(n['combat_attributes']);rating=attributes.rating(n)
        known=n['martial']['curriculum']['known'];expected=dict.fromkeys(attributes.KEYS,0)
        for key in known:
            for axis,value in catalog.ARTS[key]['bonuses'].items():expected[axis]+=value
        self.assertEqual(catalog.bonuses(n),expected)
        n['martial']['curriculum']['known']+=known[:]
        self.assertEqual(catalog.bonuses(n),expected)
        for axis,value in attributes.ratings(n).items():
            self.assertEqual(value,min(100,base['base'][axis]+expected[axis]))
        self.assertEqual(n['combat_attributes'],base);self.assertEqual(attributes.rating(n),rating)

    def test_promotion_demotion_leaving_and_changing_sects_recompute_entitlements(self):
        w=self.world();n=self.person(w);civil=n['martial']['curriculum']['civil_school']
        self.member(w,n,0,8);self.assertEqual(len(n['martial']['curriculum']['known']),2)
        self.member(w,n,0,3);self.assertEqual(len(n['martial']['curriculum']['known']),7)
        self.member(w,n,0,9);self.assertEqual(n['martial']['curriculum']['known'],['wudang_g9'])
        self.member(w,n,2,5);self.assertTrue(all(k.startswith('shaolin_') for k in n['martial']['curriculum']['known']))
        n['faction']=None;martial.sync_actor(w,n)
        self.assertEqual(n['martial']['curriculum']['known'],[f'{civil}_g9'])

    def test_newborns_record_birthplace_and_later_travel_does_not_change_it(self):
        w=self.world();parent=self.person(w,FACTIONS[2]['place'])
        child=w.make_npc([parent['id']],parent['place'])
        before=deepcopy(child['martial']['curriculum'])
        child['place']=FACTIONS[9]['place'];martial.sync_actor(w,child)
        self.assertEqual(child['martial']['curriculum'],before)

    def test_views_show_current_entitlements_without_mutating_the_world(self):
        w=self.world();n=self.person(w);n.update(faction=0,contribution=12)
        before=deepcopy(w.export());view=w.npc_detail(n['id'])['martial_view']
        self.assertEqual({a['grade'] for a in view['arts']},{5,6,7,8,9})
        self.assertEqual(w.export(),before)
        self.assertEqual(len(w.faction_detail(0)['martial_curriculum']['arts']),9)

    def test_player_birthplace_is_creation_choice_and_stays_fixed(self):
        w=self.world();place=FACTIONS[6]['place'];h=w.player['protagonist']
        protagonist.perform(w,dict(action='create',revision=h['revision'],month=w.month,name='验收',sex='女',age=24,place=place))
        c=deepcopy(w.player['martial']['curriculum']);self.assertEqual(c['origin_country'],5)
        h['place']=FACTIONS[0]['place'];martial.sync_actor(w,w.player,True)
        self.assertEqual(w.player['martial']['curriculum'],c)

    def test_real_fighter_uses_effective_abilities_and_weapon_is_unchanged(self):
        w=self.world();n=self.person(w);gear=physical_combat.equipment(w,n['id'])
        n['combat_attributes']['base']=dict.fromkeys(attributes.KEYS,50)
        naked=deepcopy(n);naked['martial'].pop('curriculum')
        before=physical_combat.fighter(naked,n['id'],gear)
        self.member(w,n,0,1);n['combat_attributes']['base']=dict.fromkeys(attributes.KEYS,50)
        after=physical_combat.fighter(n,n['id'],gear)
        self.assertEqual(before['weapon'],after['weapon'])
        self.assertEqual(after['combat_ratings'],attributes.ratings(n))
        self.assertGreater(after['damage_scale'],before['damage_scale'])
        self.assertGreater(after['combat_parameters']['stability_scale'],before['combat_parameters']['stability_scale'])

    def test_database_restart_preserves_known_arts_exactly(self):
        with tempfile.TemporaryDirectory() as folder:
            w=World(42,3,Store(Path(folder)/'world.sqlite'),durable=False)
            expected=deepcopy(w.npcs[1]['martial']['curriculum']);w.checkpoint();w.store.close()
            restored=World(store=Store(Path(folder)/'world.sqlite'),durable=False)
            try:self.assertEqual(restored.npcs[1]['martial']['curriculum'],expected)
            finally:restored.store.close()
