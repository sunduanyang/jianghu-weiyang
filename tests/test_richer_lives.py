from copy import deepcopy
import io
import json
import unittest
from unittest.mock import patch

from jianghu.engine import World
from jianghu import social, government
from jianghu.ai import AIManager, LocalModel, DEFAULT_CONFIG, context_for, apply_decision


def decision(ctx):
    a=ctx['allowed_actions'][0]['id']
    return {'action_id':a,'plan':[a], 'goal':ctx['self']['goal']['kind'],'reason':'依据自身利益与经历取舍。',
            'reflection':{'event_ids':[],'interpretation':'暂不改变看法。','trait':'none','direction':0,
                          'belief':'none','belief_direction':0}}


class RicherLivesTests(unittest.TestCase):
    def world(self, count=30):return World(42,count,durable=False)

    def adults(self,w):
        for n in w.npcs.values():n['age_months']=360;n['place']=0;n['wealth']=60;n['health']=100
        w.reindex();return w.npcs[1],w.npcs[2]

    def test_time_is_finite_and_half_month_work_does_not_multiply_monthly_salary(self):
        w=self.world();n,q=self.adults(w)
        initial=n['wealth'];fund=w.government['fund']
        for _ in range(2):self.assertIsNotNone(w.execute(n,{'id':'work:self'}))
        self.assertEqual(n['time_left'],0)
        before=n['wealth'];self.assertIsNone(w.execute(n,{'id':'work:self'}));self.assertEqual(n['wealth'],before)
        self.assertLess(n['wealth']-initial+w.government['fund']-fund,14)
        self.assertEqual(sum(a['time_cost'] for a in n['monthly_actions']),4)

    def test_trade_uses_both_peoples_time_and_one_opportunity(self):
        w=self.world();n,q=self.adults(w);w.link(n,q,20,20)
        self.assertIsNotNone(w.execute(n,{'id':'trade:2'}))
        self.assertEqual(q['time_left'],3)
        self.assertNotIn('trade:2',[a['id'] for a in w.legal_actions(n)])
        self.assertNotIn('trade:1',[a['id'] for a in w.legal_actions(q)])

    def test_same_opportunity_has_different_value_for_honest_and_greedy_people(self):
        w=self.world();n,q=self.adults(w);w.set_goal(n,'wealth',initial=True)
        steal={'kind':'steal','target':2};work={'kind':'work','target':None}
        n['traits'].update({'honesty':95,'greed':10,'compassion':85,'caution':65})
        self.assertLess(w.score(n,steal),w.score(n,work))
        n['traits'].update({'honesty':10,'greed':90,'compassion':10,'caution':20})
        self.assertGreater(w.score(n,steal),w.score(n,work))

    def test_secret_theft_preserves_money_and_victim_does_not_learn_thief(self):
        w=self.world();n,q=self.adults(w);w.link(n,q,0,0);before=n['wealth']+q['wealth']
        with patch.object(w.rng,'random',return_value=1):e=w.execute(n,{'id':'steal:2'})
        self.assertEqual(n['wealth']+q['wealth'],before)
        self.assertEqual(e['people'],[1]);self.assertTrue(e['facts']['secret'])
        ctx=context_for(w,q)
        self.assertNotIn(e['id'],[m['event_id'] for m in ctx['memories']])
        self.assertFalse(any('暗中窃取' in m['fact'] for m in q['memories']))

    def test_rumor_is_attributed_and_not_silently_converted_to_verified_fact(self):
        w=self.world();n,q=self.adults(w);n['traits']['honesty']=10;n['traits']['ambition']=90
        w.link(n,q,-20,0);w.know(n,w.npcs[3])
        with patch.object(w.rng,'random',return_value=1):e=w.execute(n,{'id':'smear:2'})
        self.assertEqual(e['kind'],'slander')
        listener=w.npcs[e['facts']['listener']]
        memory=listener['memories'][-1]
        self.assertIn('尚未核实',memory['fact']);self.assertNotIn('故意',memory['fact'])
        self.assertNotIn(e['id'],[m['event_id'] for m in listener['memories']])

    def test_embezzlement_and_public_restitution_balance_treasury(self):
        w=self.world();n=w.npcs[w.government['offices']['县令']];initial=n['wealth'];fund=w.government['fund']
        e=w.execute(n,{'id':'embezzle:self'});self.assertEqual(n['wealth'],initial+8)
        self.assertEqual(w.government['fund'],fund-8)
        with patch.object(w.rng,'random',return_value=0):social.audit(w)
        exposed=next(e for e in w.store.history(important=True) if e['kind']=='embezzlement_exposed')
        auditor=w.npcs[exposed['facts']['victim']];government.report(w,auditor,exposed['id'])
        w.month+=2
        with patch.object(w.rng,'random',return_value=0):government.monthly(w)
        case=w.government['cases'][0];self.assertTrue(case['public_money']);self.assertEqual(case['restitution'],8)
        g=w.government;self.assertAlmostEqual(g['fund'],g['opening_fund']+g['revenue']-g['expenses'])
        self.assertGreater(n['probation_until'],w.month)
        self.assertNotIn('embezzle:self',[a['id'] for a in w.legal_actions(n,planning=True)])

    def test_repeated_crime_can_erode_honesty_instead_of_always_improving_virtue(self):
        w=self.world();n=w.npcs[1];n['traits']['honesty']=20;before=n['traits']['honesty']
        for _ in range(8):
            w.month+=1;n['drift_month']={}
            w.emit('secret_theft',[1],'未被发现的窃取。',.65,facts={'offender':1,'secret':True})
        self.assertLess(n['traits']['honesty'],before)
        self.assertLess(before-n['traits']['honesty'],1)

    def test_ai_recommendation_is_not_duplicated_and_really_executes(self):
        w=self.world();n,q=self.adults(w);ctx=context_for(w,n);result=decision(ctx)
        result['action_id']='work:self';result['plan']=['work:self']
        self.assertTrue(apply_decision(w,ctx,result)[0]);self.assertEqual(n['plan']['steps'],['work:self'])
        w.month_step()
        self.assertEqual(n['monthly_actions'][0]['source'],'AI')
        self.assertEqual(n['monthly_actions'][0]['id'],'work:self')
        self.assertEqual(n['last_ai_execution'],w.month)

    def test_monthly_activity_has_multiple_kinds_but_never_overdraws_time(self):
        w=self.world(100);w.month_step()
        self.assertGreater(w.summary()['activity']['mean_actions'],2)
        for n in w.npcs.values():
            budget=4 if n['age_months']>=120 else 2
            self.assertLessEqual(sum(a['time_cost'] for a in n['monthly_actions']),budget)
            self.assertGreaterEqual(n['time_left'],0)
            occupied=[]
            for a in n['monthly_actions']:
                occupied.extend(range(a['week'],a['week']+a['time_cost']))
            self.assertEqual(len(occupied),len(set(occupied)))
            self.assertTrue(all(1<=week<=4 for week in occupied))

    def test_model_request_does_not_include_rule_recommendation_or_other_personas(self):
        w=self.world();ctx=context_for(w,w.npcs[1]);captured=[]
        def fake(request,**kwargs):
            captured.append(json.loads(request.data))
            return io.BytesIO(json.dumps({'choices':[{'message':{'content':'{}'}}],'usage':{}}).encode())
        with patch('urllib.request.urlopen',side_effect=fake):LocalModel(DEFAULT_CONFIG).request(ctx)
        wire=json.loads(captured[0]['messages'][1]['content'])
        self.assertNotIn('baseline_action',wire);self.assertNotIn('npcs',wire);self.assertNotIn('agents',wire)
        self.assertEqual(wire['npc_id'],1)
        schema=captured[0]['response_format']['json_schema']['schema']
        event_schema=schema['properties']['reflection']['properties']['event_ids']
        self.assertEqual(event_schema['items']['enum'],[m['event_id'] for m in ctx['memories']])

    def test_review_budget_rotates_to_different_people(self):
        w=self.world(100)
        def mocked(self,ctx):
            return decision(ctx),1,{},'unit-test mock'
        with patch.object(LocalModel,'request',mocked):
            manager=AIManager({'enabled':True,'reviews_per_month':24,'queue_limit':48})
            try:
                self.assertEqual(manager.schedule(w),24);manager.jobs.join();manager.collect(w)
                first={n['id'] for n in w.npcs.values() if n['last_ai_review'] is not None}
                w.month+=1
                self.assertEqual(manager.schedule(w),24);manager.jobs.join();manager.collect(w)
                second={n['id'] for n in w.npcs.values() if n['last_ai_review']==w.month}
                self.assertEqual(len(first&second),0);self.assertEqual(manager.stats['coverage'],48)
            finally:
                manager.closed=True;manager.thread.join(timeout=2)


if __name__=='__main__':unittest.main()
