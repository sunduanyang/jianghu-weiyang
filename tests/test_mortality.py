"""Age-based background mortality: harsh first years, a long adult plateau, steep old age."""
import unittest
from unittest.mock import patch

from jianghu import demography
from jianghu.engine import World


class MortalityTests(unittest.TestCase):
    def setUp(self):
        self.w = World(7, 0, compact=True, durable=False)
        self.addCleanup(self.w.store.close)

    def person(self, years, health=100.):
        n = self.w.make_npc(place=self.w.active_places[0]['id'])
        n.update(age_months=years*12, health=health, illness=None, wound=None)
        n['monthly_settlement'] = dict(month=self.w.month, payer=n['id'], paid=0., food_shortfall=0.)
        self.w.reindex()
        return n

    def test_schedule_is_harsh_early_flat_in_adulthood_and_steep_late(self):
        rates = [demography.annual_mortality(a) for a in (0, 3, 10, 25, 45, 55, 65, 75, 90)]
        self.assertGreater(rates[0], rates[1]); self.assertGreater(rates[1], rates[2])
        self.assertLess(rates[3], .01)
        self.assertEqual(rates[3:], sorted(rates[3:]))
        self.assertGreater(rates[-1], .15)

    def test_frailty_hunger_and_poor_sanitation_raise_the_hazard(self):
        healthy, frail = self.person(40), self.person(40, health=20.)
        base = demography.monthly_hazard(self.w, healthy)
        self.assertGreater(demography.monthly_hazard(self.w, frail), base * 2)
        healthy['starvation_months'] = 3
        self.assertGreater(demography.monthly_hazard(self.w, healthy), base * 2)
        healthy['starvation_months'] = 0
        g = self.w.local_government(healthy); g['sanitation'] = .9; clean = demography.monthly_hazard(self.w, healthy)
        g['sanitation'] = .1
        self.assertGreater(demography.monthly_hazard(self.w, healthy), clean)
        # A healthy adult of forty faces well under one percent a year.
        self.assertLess(1 - (1 - base) ** 12, .02)

    def test_background_deaths_carry_an_age_appropriate_cause(self):
        for years, cause in ((2, 'infancy'), (35, 'sudden_illness'), (70, 'old_age')):
            n = self.person(years)
            with patch.object(self.w.rng, 'random', return_value=0.):
                demography.health_step(self.w, n)
            self.assertFalse(n['alive'])
            self.assertEqual(n['death']['cause'], cause)
            self.assertIn(n['death']['cause_label'], ('幼年夭折', '急病', '衰老'))

    def test_a_safe_draw_leaves_people_alive(self):
        n = self.person(30)
        with patch.object(self.w.rng, 'random', return_value=.999):
            demography.health_step(self.w, n)
        self.assertTrue(n['alive'])


if __name__ == '__main__':
    unittest.main()
