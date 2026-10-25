from copy import deepcopy
import unittest
from unittest.mock import patch

from jianghu.engine import World
from jianghu import demography, government


class GenerationsTests(unittest.TestCase):
    def world(self):
        return World(seed=42, population=30, durable=False)

    def couple(self, w):
        a, b = w.npcs[1], w.npcs[2]
        for n, sex, spouse in [(a, '女', 2), (b, '男', 1)]:
            n.update({'sex': sex, 'spouse': spouse, 'age_months': 300,
                      'place': 0, 'health': 100, 'wealth': 80, 'orientation': '异性'})
        w.link(a, b, 50, 50, 60, '夫妻')
        w.reindex()
        return a, b

    def test_pregnancy_takes_nine_months_and_builds_real_genealogy(self):
        w=self.world();mother,father=self.couple(w)
        with patch.object(w.rng, 'random', return_value=0):demography.pregnancies(w)
        p=mother['pregnancy'];self.assertIsNotNone(p)
        count=len(w.npcs);w.month=p['due']-1
        demography.pregnancies(w);self.assertEqual(len(w.npcs),count)
        w.month+=1
        with patch.object(w.rng,'random',return_value=.5):demography.pregnancies(w)
        child=w.npcs[count+1]
        self.assertEqual(child['age_months'],0);self.assertEqual(child['generation'],1)
        self.assertEqual(child['parents'],[1,2]);self.assertIn(child['id'],father['children'])
        birth=next(e for e in w.store.history(child['id']) if e['kind']=='birth')
        self.assertIn(p['event_id'],birth['causes'])

    def test_historical_population_does_not_block_new_generations(self):
        w=self.world();mother,father=self.couple(w)
        for n in list(w.npcs.values())[2:]:w.die(n,'old_age')
        w.population_limit=3
        self.assertGreater(len(w.npcs),w.population_limit)
        self.assertTrue(demography.fertile_pair(w,mother))

    def test_close_kin_cannot_marry_and_children_inherit_generation(self):
        w=self.world();mother,father=self.couple(w)
        child=w.make_npc([1,2],0);sibling=w.make_npc([1,2],0)
        self.assertTrue(w.close_kin(child,sibling));self.assertTrue(w.close_kin(mother,child))
        child['generation']=2;sibling['generation']=3
        next_child=w.make_npc([child['id'],sibling['id']],0)
        self.assertEqual(next_child['generation'],4)

    def test_orphan_gets_surviving_parent_and_moves_with_guardian(self):
        w=self.world();mother,father=self.couple(w)
        child=w.make_npc([1,2],0);child['guardian']=1;w.reindex()
        w.die(mother,'illness');demography.households(w)
        self.assertEqual(child['guardian'],2)
        old=father['place'];w.residents[old].remove(2);father['place']=1;w.residents[1].append(2)
        ids=demography.follow_guardian(w,father,old)
        self.assertIn(child['id'],ids);self.assertEqual(child['place'],1)

    def test_dead_person_does_not_acquire_future_subjective_memories(self):
        w=self.world();mother,father=self.couple(w)
        event=w.emit('pregnancy',[1,2],'怀孕。',.85)
        mother['pregnancy']={'father':2,'due':9,'event_id':event['id']}
        w.die(father,'accident');before=deepcopy(father['memories'])
        w.month=9
        with patch.object(w.rng,'random',return_value=.5):demography.deliver(w,mother)
        self.assertEqual(father['memories'],before)
        child=w.npcs[len(w.npcs)]
        self.assertIn(2,child['parents']);self.assertIn(child['id'],father['children'])

    def test_disease_starvation_and_accident_have_specific_causes(self):
        for cause in ['illness','starvation','accident','injury']:
            w=self.world();n=w.npcs[1];n['age_months']=360
            origin=w.emit('illness' if cause=='illness' else 'accident',[1],'已有实况。',.75)
            n['monthly_settlement']={'food_shortfall':0}
            if cause=='illness':
                n['illness']={'severity':90,'remaining':4,'event_id':origin['id']}
                with patch.object(w.rng,'random',return_value=0):demography.health_step(w,n)
            elif cause=='starvation':
                n['health']=0;n['starvation_months']=1;n['monthly_settlement']['food_shortfall']=2.3
                demography.health_step(w,n)
            elif cause=='injury':
                n['health']=5;n['wound']={'remaining':4,'event_id':origin['id']}
                with patch.object(w.rng,'random',side_effect=[1,0]):demography.health_step(w,n)
            else:
                action=w.emit('work',[1],'劳作。',.15)
                with patch.object(w.rng,'random',return_value=0):demography.accident(w,n,action)
            self.assertFalse(n['alive'],cause);self.assertEqual(n['death']['cause'],cause)
            e=w.store.event(n['death']['event_id'])
            self.assertTrue(e['causes']);self.assertEqual(e['facts']['cause'],cause)

    def test_death_is_idempotent_and_no_heir_estate_enters_treasury(self):
        w=self.world();n=w.npcs[1];wealth=n['wealth'];fund=w.government['fund']
        w.die(n,'old_age');count=w.metrics['deaths'];w.die(n,'old_age')
        self.assertEqual(w.metrics['deaths'],count)
        self.assertAlmostEqual(w.government['fund'],fund+wealth)
        self.assertEqual(n['wealth'],0)


class GovernmentTests(unittest.TestCase):
    def world(self):return World(seed=42,population=30,durable=False)

    def test_tax_and_relief_transfer_real_money_with_no_overdraft(self):
        w=self.world();n=w.npcs[1];n['wealth']+=20
        total=n['wealth']+w.government['fund']
        tax=government.tax(w,n,20)
        self.assertGreater(tax,0);self.assertAlmostEqual(n['wealth']+w.government['fund'],total)
        government.grant(w,n,1000,'救济')
        self.assertGreaterEqual(w.government['fund'],0)
        self.assertAlmostEqual(n['wealth']+w.government['fund'],total)
        g=w.government;self.assertAlmostEqual(g['fund'],g['opening_fund']+g['revenue']-g['expenses'])

    def test_official_dies_and_successor_references_the_vacancy(self):
        w=self.world();old=w.npcs[w.government['offices']['县令']]
        death=w.die(old,'old_age');w.month+=1;government.monthly(w)
        new=w.government['offices']['县令']
        self.assertNotEqual(new,old['id']);self.assertTrue(w.npcs[new]['alive'])
        appointment=w.store.event(w.government['last_appointments']['县令'])
        self.assertIn(death['id'],appointment['causes'])
        self.assertEqual(appointment['facts']['previous'],old['id'])
        self.assertGreater(w.government['succession_count'],0)

    def test_report_and_restitution_use_evidence_and_actual_assets(self):
        w=self.world();victim,offender=w.npcs[1],w.npcs[2]
        crime=w.emit('betrayal',[1,2],'侵吞败露。',.95,
                     facts={'offender':2,'victim':1,'transfer':12})
        before=victim['wealth'];government.report(w,victim,crime['id'])
        w.month+=2
        with patch.object(w.rng,'random',return_value=0):government.monthly(w)
        case=w.government['cases'][0]
        self.assertEqual(case['status'],'已判决');self.assertAlmostEqual(victim['wealth']-before,12+(1 if victim['office'] else 0))
        e=w.store.event(case['judgment']);self.assertIn(crime['id'],e['causes'])
        secret=w.emit('secret_theft',[2],'无人发现的行为。',.5,facts={'secret':True})
        with self.assertRaises(ValueError):government.report(w,victim,secret['id'])

    def test_budget_cannot_improve_health_or_security_for_free(self):
        w=self.world();w.government['fund']=0
        security,sanitation=w.government['security'],w.government['sanitation']
        with patch.object(w.rng,'random',return_value=1):government.annual(w)
        self.assertEqual(w.government['security'],security);self.assertEqual(w.government['sanitation'],sanitation)


if __name__=='__main__':unittest.main()
