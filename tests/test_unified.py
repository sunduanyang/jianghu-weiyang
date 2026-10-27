from jianghu import combat_attributes as abilities
"""Interactions between the formerly independent feature worktrees."""
from copy import deepcopy
import unittest
from unittest.mock import patch

from jianghu import careers, items, martial
from jianghu.ai import context_for
from jianghu.atlas import PLACES
from jianghu.engine import World
from jianghu.store import Store


class UnifiedTests(unittest.TestCase):
    def world(self):
        w = World(seed=42, population=30, durable=False)
        self.addCleanup(w.store.close)
        return w

    def test_all_national_sites_support_career_catalogs_and_income(self):
        w = self.world()
        n = w.npcs[1]
        for p in PLACES:
            n['place'] = p['id']
            for job in careers.PROFESSIONS:
                self.assertGreater(careers.income(n, profession=job), 0)
                self.assertIsInstance(careers.entry_blockers(w, n, job), list)
        self.assertGreaterEqual(len(PLACES), 150)

    def test_item_actions_consume_week_and_enter_the_same_itinerary(self):
        w = self.world()
        n = w.npcs[1]
        n['age_months'], n['health'] = 300, 40
        salve = next(i for i in items.objects(w, 1) if i['template'] == 'salve')
        before = n['time_left']
        e = w.execute(n, {'id': f"use_item:{salve['id']}"})
        self.assertEqual(n['time_left'], before-1)
        self.assertEqual(n['monthly_actions'][-1]['event_id'], e['id'])
        self.assertEqual(e['facts']['time_cost'], 1)
        self.assertEqual(e['facts']['actor'], 1)
        self.assertNotIn(salve['id'], w.items['objects'])

    def test_regional_official_corruption_uses_own_treasury(self):
        w = self.world()
        n = w.npcs[1]
        n.update(age_months=360, place=14, office='主簿', office_country=1, wealth=60.)
        w.reindex()
        local, song = w.governments[1], w.governments[0]
        local['offices']['主簿'] = 1
        before, unrelated = local['fund'], deepcopy(song)
        with patch.object(w.rng, 'random', return_value=1):
            e = w.execute(n, {'id': 'embezzle:self'})
        self.assertIsNotNone(e)
        self.assertEqual(local['fund'], before-8)
        self.assertEqual(n['wealth'], 68)
        self.assertEqual(song, unrelated)
        self.assertEqual(local['corruption_records'][-1]['event_id'], e['id'])

    def test_battle_uses_gear_and_breakage_changes_the_next_bout(self):
        w = self.world()
        n = w.npcs[1]
        n['age_months'], n['health'] = 360, 100
        sword = next(i for i in items.objects(w, 'player') if i['template'] == 'wood_sword')
        b = w.start_battle(1)['battle']
        base = abilities.parameters(abilities.ratings(w.player))['damage_scale']
        self.assertAlmostEqual(b['fighters'][0]['damage_scale'], base+.024)
        w.battle_action(b['id'], b['revision'], 'surrender')
        self.assertEqual(sword['durability'], 98)
        sword['durability'] = 0
        b = w.start_battle(1)['battle']
        self.assertAlmostEqual(b['fighters'][0]['damage_scale'], abilities.parameters(abilities.ratings(w.player))['damage_scale'])

    def test_combined_save_reloads_and_continues_without_random_drift(self):
        w = self.world()
        for _ in range(2):
            w.month_step()
        store = Store()
        self.addCleanup(store.close)
        w.checkpoint()
        w.store.db.backup(store.db)
        restored = World(store=store, durable=False)
        w.month_step()
        restored.month_step()
        self.assertEqual(w.npcs, restored.npcs)
        self.assertEqual(w.items, restored.items)
        self.assertEqual(w.governments, restored.governments)
        self.assertEqual(w.rng.getstate(), restored.rng.getstate())
        ctx = context_for(restored, restored.npcs[1])
        for key in ('possessions', 'equipment_bonuses', 'social_identity', 'location'):
            self.assertIn(key, ctx)
        self.assertIn('career', ctx['self'])
        self.assertIn('martial', ctx['self'])


if __name__ == '__main__':
    unittest.main()
