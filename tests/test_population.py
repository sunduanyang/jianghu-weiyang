"""Opening cast invariants and isolation from existing worlds."""
from copy import deepcopy
import json
from pathlib import Path
import tempfile
import unittest

from jianghu import careers, city_maps, items, population, sect_ranks, social_ranks, territory
from jianghu.engine import World
from jianghu.store import Store
from scripts.regenerate_population import retain_player


class PopulationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.w = World(seed=20261027, population=1800, compact=True, durable=False,
                      population_profile=True, start_month=242)

    @classmethod
    def tearDownClass(cls):
        cls.w.store.close()

    def test_exact_cast_nine_social_estates_in_each_country(self):
        w = self.w
        self.assertEqual(len(w.npcs), 1800)
        self.assertEqual(len({n['name'] for n in w.npcs.values()}), 1800)
        self.assertEqual(sum(n['faction'] is not None for n in w.npcs.values()), 504)
        for c in w.countries:
            view = social_ranks.country_view(w, c['id'])
            self.assertTrue(all(row['population'] > 0 for row in view['grades']), c['name'])
            self.assertEqual(view['grades'][0]['population'], 1)
            self.assertEqual(sum(row['population'] for row in view['grades']), view['population'])

    def test_every_sect_has_one_leader_and_complete_pyramid(self):
        w = self.w
        for f in w.factions:
            members = [n for n in w.npcs.values() if n['faction'] == f['id']]
            grades = [sum(sect_ranks.standing(f, n)['grade'] == grade for n in members) for grade in range(1, 10)]
            self.assertEqual(grades[0], 1)
            self.assertTrue(all(grades))
            self.assertEqual(grades, sorted(grades), f['name'])
            self.assertTrue(all(n['place'] == f['place'] and n['office'] is None for n in members))
            for n in members:
                self.assertEqual(w.city_positions[str(n['id'])]['node'], f"faction_{f['id']}")
                self.assertEqual(n['identity']['system'], 'sect')
        sizes = {f['name']: sum(n['faction'] == f['id'] for n in w.npcs.values()) for f in w.factions}
        self.assertGreater(sizes['丐帮'], sizes['黑水萨满坛'])

    def test_geography_livelihood_gates_and_initial_guardians(self):
        w = self.w
        for p in w.active_places:
            self.assertGreater(len(w.residents[p['id']]), 0)
        self.assertGreater(len(w.residents[45]), len(w.residents[118]))
        self.assertGreater(population.job_weights(61)['渔人'], population.job_weights(101)['渔人'])
        self.assertGreater(population.job_weights(101)['猎户'], population.job_weights(61)['猎户'])
        for n in w.npcs.values():
            self.assertEqual(n['birth_month'], w.month - n['age_months'])
            if n['age_months'] < 192:
                g = w.npcs[n['guardian']]
                self.assertGreaterEqual(g['age_months'], 360)
                self.assertEqual(g['place'], n['place'])
                self.assertFalse(n.get('social_status'))
            else:
                profession = n['career']['profession']
                self.assertGreaterEqual(n['skill'], careers.PROFESSIONS[profession].get('martial', 0))
                self.assertGreaterEqual(careers.experience(n), careers.PROFESSIONS[profession]['entry'])
                self.assertGreater(careers.income(n), 0)
            if n['faction'] is None and n.get('social_status') and n['identity']['grade'] <= 2:
                self.assertEqual(w.place_name(n['place']), w.countries[territory.owner(w, n['place'])]['capital'])

    def test_registered_status_is_independent_of_wealth_and_trade(self):
        w = self.w
        noble = deepcopy(next(n for n in w.npcs.values() if n.get('social_status', {}).get('title') == '世家家主'))
        noble.update(job='农人', wealth=0.)
        self.assertEqual(careers.identity(w, noble)['grade'], 3)
        noble.pop('social_status')
        self.assertEqual(careers.identity(w, noble)['grade'], 8)
        self.assertEqual(social_ranks.grade_for('短工'), 9)

    def test_save_restore_does_not_refill_reroll_or_add_past_events(self):
        w = self.w
        with tempfile.TemporaryDirectory() as tmp:
            db = Store(Path(tmp) / 'world.sqlite')
            w.store.db.backup(db.db)
            before = json.dumps(w.export(), ensure_ascii=False, sort_keys=True)
            events = db.db.execute('SELECT count(*) FROM events').fetchone()[0]
            restored = World(seed=7, population=2400, store=db, compact=True,
                             durable=False, population_profile=True)
            try:
                self.assertEqual(json.dumps(restored.export(), ensure_ascii=False, sort_keys=True), before)
                self.assertEqual(restored.store.count(), events)
            finally:
                db.close()

    def test_legacy_save_is_not_regenerated(self):
        w = World(seed=7, population=30, compact=True, durable=False)
        try:
            before = deepcopy(w.export())
            reloaded = World(store=w.store, compact=True, population_profile=True, durable=False)
            self.assertEqual(reloaded.export(), before)
            self.assertIsNone(reloaded.population_generation)
        finally:
            w.store.close()

    def test_tiny_casts_and_standard_new_game_size_keep_exact_counts(self):
        for count in (0, 2, 100, 800):
            w = World(population=count, compact=True, durable=False, population_profile=True)
            try:
                self.assertEqual(len(w.npcs), count)
                if count == 800:
                    for c in w.countries:
                        self.assertTrue(all(row['population'] for row in social_ranks.country_view(w, c['id'])['grades']))
            finally:
                w.store.close()

    def test_retained_player_keeps_properties_and_items_without_cast_id_aliases(self):
        old = World(population=3, compact=True, durable=False)
        new = World(population=0, compact=True, durable=False, population_profile=True)
        try:
            old.player.update(name='行舟', wealth=630., health=71.2568, ties={'1': {'affinity': 16}}, spar_rewards={'1': 0})
            previous = deepcopy(old.export())
            mapping = retain_player(new, previous, 'archived.sqlite')
            for key in ('name', 'wealth', 'skill', 'health', 'reputation', 'martial', 'read_books'):
                self.assertEqual(new.player[key], previous['player'][key])
            self.assertEqual(new.player['ties'], {})
            self.assertEqual(new.player['spar_rewards'], {})
            for old_id, new_id in mapping.items():
                a, b = previous['items']['objects'][old_id], new.items['objects'][new_id]
                self.assertEqual((a['template'], a['durability']), (b['template'], b['durability']))
                self.assertIsNone(b['origin_event'])
            for slot, key in old.player['equipment'].items():
                self.assertEqual(new.player['equipment'][slot], mapping[str(key)])
            self.assertTrue(all(new.store.event(i) for i in new.player['protagonist']['records']))
        finally:
            old.store.close()
            new.store.close()
